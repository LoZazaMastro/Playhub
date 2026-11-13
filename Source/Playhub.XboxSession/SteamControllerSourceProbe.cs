// Playhub Xbox Session. Distributed under GPL-3.0-only.
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using WSGM.PackagedLaunch;

internal static class SteamControllerSourceProbe
{
    private const string Feature = "PlayhubSteamControllerProbe";
    private const int MaxInventoryBytes = 16384;
    private const ushort Valve = 0x28DE;
    private const ushort SteamVirtual = 0x11FF;

    internal readonly record struct Slot(int Index, ushort Product, ulong Handle);

    internal static IDisposable? TryStart(TrustedSteamFiles steam, ulong gameId,
        IRetainedSteamGameTarget target, Process parent)
    {
        if (!typeof(SteamControllerSourceProbe).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Any(item => item.Key == Feature && item.Value == "true")) return null;

        try
        {
            var renderer = steam.ComponentPaths.Last();
            string root = Path.GetDirectoryName(renderer)!;
            string inventory = Path.Combine(root, "config", "virtualgamepadinfo.txt");
            string? published = Environment.GetEnvironmentVariable("SteamVirtualGamepadInfo");
            bool inventoryMatches = !string.IsNullOrWhiteSpace(published) && Path.IsPathFullyQualified(published)
                && string.Equals(Path.GetFullPath(published), inventory, StringComparison.OrdinalIgnoreCase);
            PackagedLaunchLog.Info($"Steam controller probe: env-inventory-matches={inventoryMatches}.");
            if (!steam.UnchangedRoot() || !target.IsCurrent() || parent.HasExited || !MatchesGameId(gameId)) return null;
            if (ReadInventory(inventory) is not [_])
            {
                var evidence = SteamInputEvidence.Read(inventory);
                PackagedLaunchLog.Info($"Steam controller probe unavailable: physical-source={evidence.Source is not null}; source-identity-conflict={evidence.Shadow}.");
                return null;
            }

            // The host already verified the wrapper and exact AAM target. The
            // canonical Steam path also lets this read-only diagnostic distinguish
            // missing inherited metadata from a missing injected input source.
            return new ProbeLease(token => Probe(renderer, inventory, gameId, target, parent, token));
        }
        catch (Exception error)
        {
            PackagedLaunchLog.Info($"Steam controller probe unavailable: {error.GetType().Name}.");
            return null;
        }
    }

    private static bool MatchesGameId(ulong gameId) =>
        ulong.TryParse(Environment.GetEnvironmentVariable("SteamGameId"), NumberStyles.None,
            CultureInfo.InvariantCulture, out ulong observed) && observed == gameId;

    internal static void RunLayoutTrial(TrustedSteamFiles steam, ulong gameId,
        IRetainedSteamGameTarget target, Process parent, Func<bool> ownsInput, CancellationToken cancellation)
    {
        string renderer = steam.ComponentPaths.Last();
        string inventory = Path.Combine(Path.GetDirectoryName(renderer)!, "config", "virtualgamepadinfo.txt");
        string? published = Environment.GetEnvironmentVariable("SteamVirtualGamepadInfo");
        if (string.IsNullOrEmpty(published) || !Path.IsPathFullyQualified(published)
            || !string.Equals(Path.GetFullPath(published), inventory, StringComparison.OrdinalIgnoreCase)) return;
        Probe(renderer, inventory, gameId, target, parent, cancellation, ownsInput);
    }

    private static void Probe(string renderer, string inventory, ulong gameId,
        IRetainedSteamGameTarget target, Process parent, CancellationToken cancellation, Func<bool>? ownsInput = null)
    {
        bool Active() => !cancellation.IsCancellationRequested && !parent.HasExited
            && target.IsCurrent() && MatchesGameId(gameId) && (ownsInput?.Invoke() ?? true);

        nint module = 0;
        try
        {
            using var self = Process.GetCurrentProcess();
            var deadline = Stopwatch.StartNew();
            bool rendererLoaded = false;
            GetCapabilitiesEx? readCaps = null;
            string? lastCandidate = null;
            while (Active() && deadline.Elapsed < TimeSpan.FromSeconds(6))
            {
                self.Refresh();
                rendererLoaded = self.Modules.Cast<ProcessModule>()
                    .Any(item => string.Equals(item.FileName, renderer, StringComparison.OrdinalIgnoreCase));
                if (!rendererLoaded)
                {
                    cancellation.WaitHandle.WaitOne(100);
                    continue;
                }

                var slots = ReadInventory(inventory);
                if (slots.Length > 1)
                {
                    PackagedLaunchLog.Info("Steam controller probe: ambiguous Triton slots.");
                    return;
                }
                if (slots.Length == 0)
                {
                    cancellation.WaitHandle.WaitOne(100);
                    continue;
                }

                var slot = slots[0];
                bool? rawProof = SteamControllerRawInputEvidence.HasExactDevice(slot, self.Id);
                if (readCaps is null)
                {
                    module = NativeLibrary.Load("xinput1_4.dll", typeof(SteamControllerSourceProbe).Assembly,
                        DllImportSearchPath.System32);
                    nint pointer = GetProcAddress(module, 108);
                    if (pointer == 0)
                    {
                        PackagedLaunchLog.Info("Steam controller probe: XInput capability ordinal unavailable.");
                        return;
                    }
                    readCaps = Marshal.GetDelegateForFunctionPointer<GetCapabilitiesEx>(pointer);
                }

                uint capStatus = readCaps(1, (uint)slot.Index, 0, out var caps);
                uint stateStatus = XInputGetState((uint)slot.Index, out var initial);
                bool verified = capStatus == 0 && stateStatus == 0
                    && MatchesVirtual(slot, caps.Vendor, caps.Product, caps.SteamSlot);
                lastCandidate = $"source=28DE:{slot.Product:X4}; slot={slot.Index}; "
                    + $"caps=0x{capStatus:X8}:{caps.Vendor:X4}:{caps.Product:X4}:{caps.SteamSlot}; "
                    + $"state=0x{stateStatus:X8}; verified={verified}; "
                    + $"layout-trial={ownsInput is not null}; raw-proof={rawProof?.ToString() ?? "unavailable"}.";
                if (!verified)
                {
                    cancellation.WaitHandle.WaitOne(100);
                    continue;
                }
                PackagedLaunchLog.Info($"Steam controller probe: {lastCandidate}");

                uint previous = initial.Packet;
                int changes = 0;
                var sampling = Stopwatch.StartNew();
                while (Active() && sampling.Elapsed < TimeSpan.FromSeconds(5))
                {
                    cancellation.WaitHandle.WaitOne(40);
                    if (!Active()) break;
                    if (ReadInventory(inventory) is not [var current] || current != slot
                        || readCaps(1, (uint)slot.Index, 0, out caps) != 0
                        || !MatchesVirtual(slot, caps.Vendor, caps.Product, caps.SteamSlot)
                        || XInputGetState((uint)slot.Index, out var state) != 0)
                    {
                        PackagedLaunchLog.Info("Steam controller probe: virtual source changed during sample.");
                        return;
                    }
                    if (state.Packet != previous)
                    {
                        previous = state.Packet;
                        changes++;
                    }
                }
                PackagedLaunchLog.Info($"Steam controller probe: source packet changes={changes}; "
                    + $"sample-ms={sampling.ElapsedMilliseconds}; no output was created.");
                return;
            }
            PackagedLaunchLog.Info($"Steam controller probe: no verified virtual source; renderer={rendererLoaded}. {lastCandidate}");
        }
        catch (Exception error)
        {
            PackagedLaunchLog.Info($"Steam controller probe stopped: {error.GetType().Name}.");
        }
        finally { if (module != 0) NativeLibrary.Free(module); }
    }

    internal static bool MatchesVirtual(Slot slot, ushort vendor, ushort product, uint steamSlot) =>
        slot.Index is >= 0 and <= 3 && slot.Product is 0x1302 or 0x1304 && slot.Handle != 0
        && vendor == Valve && product == SteamVirtual && steamSlot == slot.Index;

    internal static bool MatchesConnectedVirtual(TritonSource source, uint capabilitiesStatus,
        ushort vendor, ushort product, uint steamSlot, uint stateStatus) => source.Valid
        && capabilitiesStatus == 0 && stateStatus == 0
        && MatchesVirtual(new Slot(source.Slot, source.Product, source.Handle), vendor, product, steamSlot);

    internal enum ConnectionStatus { Unknown, Connected, Disconnected }
    internal readonly record struct LiveProof(ConnectionStatus Status, nint RawDevice = 0);

    internal static LiveProof ConnectionProof(ConnectionStatus capabilities, nint? rawDevice)
    {
        if (capabilities == ConnectionStatus.Connected) return new(ConnectionStatus.Connected);
        if (rawDevice is { } raw && raw is not (0 or -1)) return new(ConnectionStatus.Connected, raw);
        return new(capabilities == ConnectionStatus.Disconnected && rawDevice == 0
            ? ConnectionStatus.Disconnected : ConnectionStatus.Unknown);
    }

    internal static ConnectionStatus Connection(TritonSource source, uint capabilitiesStatus,
        ushort vendor, ushort product, uint steamSlot, uint stateStatus)
    {
        if (!source.Valid) return ConnectionStatus.Unknown;
        if (MatchesConnectedVirtual(source, capabilitiesStatus, vendor, product, steamSlot, stateStatus))
            return ConnectionStatus.Connected;
        // A generic or missing replacement slot is not evidence that this
        // physical device disconnected. Its exact Steam virtual capabilities
        // must remain available alongside the state-not-connected result.
        const uint disconnected = 1167;
        if (stateStatus == disconnected && capabilitiesStatus == 0
            && MatchesVirtual(new Slot(source.Slot, source.Product, source.Handle), vendor, product, steamSlot))
            return ConnectionStatus.Disconnected;
        return ConnectionStatus.Unknown;
    }

    internal sealed class InitialConnection(Func<bool> read)
    {
        private bool _transferred;
        internal bool Current() => _transferred || read();
        internal void LayoutStarted(bool started) { if (started) _transferred = true; }
    }

    // Only a new layout lease needs this initial connection proof. Once Steam
    // enumerates the independent relay output, its XInput view may change; the
    // pinned reader's exact RawInput path then owns all source validation.
    internal sealed class LiveSource : IDisposable
    {
        private readonly nint _module;
        private readonly GetCapabilitiesEx _read;
        private readonly string _renderer;
        private LiveSource(nint module, GetCapabilitiesEx read, string renderer)
        { _module = module; _read = read; _renderer = renderer; }

        internal static LiveSource? TryOpen(string renderer)
        {
            nint module = 0;
            try
            {
                module = NativeLibrary.Load("xinput1_4.dll", typeof(SteamControllerSourceProbe).Assembly,
                    DllImportSearchPath.System32);
                nint pointer = GetProcAddress(module, 108);
                if (pointer != 0)
                    return new LiveSource(module, Marshal.GetDelegateForFunctionPointer<GetCapabilitiesEx>(pointer), renderer);
            }
            catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { }
            if (module != 0) NativeLibrary.Free(module);
            return null;
        }

        internal bool Current(TritonSource source, nint expectedRawDevice = 0)
        {
            if (expectedRawDevice == 0) return Capabilities(source) == ConnectionStatus.Connected;
            return RendererCurrent() && SteamControllerRawInputEvidence.SameDevice(expectedRawDevice,
                SteamControllerRawInputEvidence.ExactDevice(new Slot(source.Slot, source.Product, source.Handle), Environment.ProcessId));
        }

        internal ConnectionStatus Read(TritonSource source) => Sample(source).Status;

        internal LiveProof Sample(TritonSource source)
        {
            var caps = Capabilities(source);
            if (caps == ConnectionStatus.Connected) return new(caps);
            if (!source.Valid || !RendererCurrent()) return default;
            return ConnectionProof(caps, SteamControllerRawInputEvidence.ExactDevice(
                new Slot(source.Slot, source.Product, source.Handle), Environment.ProcessId));
        }

        private bool RendererCurrent()
        {
            nint renderer = GetModuleHandleW("GameOverlayRenderer64.dll");
            if (renderer == 0) return false;
            var path = new StringBuilder(1024);
            uint length = GetModuleFileNameW(renderer, path, path.Capacity);
            if (length == 0 || length >= path.Capacity || !string.Equals(path.ToString(), _renderer, StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }

        private ConnectionStatus Capabilities(TritonSource source)
        {
            if (!source.Valid || !RendererCurrent()) return ConnectionStatus.Unknown;
            uint capsStatus = _read(1, (uint)source.Slot, 0, out var caps);
            uint stateStatus = XInputGetState((uint)source.Slot, out _);
            return Connection(source, capsStatus, caps.Vendor, caps.Product, caps.SteamSlot, stateStatus);
        }

        public void Dispose() => NativeLibrary.Free(_module);
    }

    internal static Slot[] ReadInventory(string path)
    {
        var inventory=SteamInputEvidence.Read(path);
        return !inventory.Shadow&&inventory.Source is {Valid:true} source
            ? [new Slot(source.Slot,source.Product,source.Handle)] : [];
    }

    internal static Slot[] ParseInventory(string text)
    {
        var inventory = SteamInputEvidence.Parse(text);
        return !inventory.Shadow && inventory.Source is { Valid: true } source
            ? [new Slot(source.Slot, source.Product, source.Handle)] : [];
    }

    private sealed class ProbeLease : IDisposable
    {
        private readonly CancellationTokenSource _cancel = new();
        private readonly Task _worker;
        internal ProbeLease(Action<CancellationToken> run) => _worker = Task.Run(() => run(_cancel.Token));
        public void Dispose()
        {
            _cancel.Cancel();
            if (_worker.Wait(TimeSpan.FromMilliseconds(250))) _cancel.Dispose();
            else _ = _worker.ContinueWith(_ => _cancel.Dispose(), TaskScheduler.Default);
        }
    }

    // ABI from SDL's Windows XInput definitions. The ordinal lets this
    // diagnostic distinguish Steam's virtual device from a system Xbox pad.
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct XInputCaps
    {
        [FieldOffset(20)] internal ushort Vendor;
        [FieldOffset(22)] internal ushort Product;
        [FieldOffset(28)] internal uint SteamSlot;
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct XInputState
    {
        [FieldOffset(0)] internal uint Packet;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint GetCapabilitiesEx(uint reserved, uint index, uint flags, out XInputCaps caps);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern nint GetProcAddress(nint module, nint ordinal);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint GetModuleHandleW(string moduleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint GetModuleFileNameW(nint module, StringBuilder path, int capacity);

    [DllImport("xinput1_4.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint XInputGetState(uint index, out XInputState state);
}

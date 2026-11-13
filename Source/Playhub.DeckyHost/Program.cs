using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Playhub.DeckyHost;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length is not (2 or 4) || args[0] != "--loader") return 2;
        int? attach = null;
        if (args.Length == 4)
        {
            if (args[2] != "--attach" || !int.TryParse(args[3], out int pid) || pid <= 0) return 2;
            attach = pid;
        }
        try
        {
            string loader = Path.GetFullPath(args[1]);
            if (!DeckyProcessTree.IsLoaderPath(loader) || !File.Exists(loader)) return 2;
            string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(loader.ToUpperInvariant())));
            using var single = new Mutex(true, "Local\\Playhub.DeckyHost." + key, out bool owns);
            if (!owns) return 0;
            try { using var lifetime = new DeckyLifetime(loader, attach); return lifetime.Run(); }
            finally { single.ReleaseMutex(); }
        }
        catch { return 1; }
    }
}

// One hidden native window and a blocking message pump. Steam generation checks
// run at most once a second. Health probes run only after an owned runtime
// consumes most of one core and loses its listener; idle uses no HTTP.
internal sealed class DeckyLifetime : IDisposable
{
    private const uint RootExited = 0x8001;
    private const uint MaintenanceFinished = 0x8002;
    private const nuint SteamTimer = 1;
    private readonly string _loader;
    private readonly int? _attach;
    private readonly WindowProcedure _procedure;
    private readonly string _className = "Playhub.DeckyLifetime." + Guid.NewGuid().ToString("N");
    private nint _window;
    private Process? _root;
    private DeckyProcessTree.Identity? _identity;
    private string? _runtime;
    private bool _sessionEnded;
    private readonly object _state = new();
    private EventHandler? _rootExitHandler;
    private ISteamGenerationLease? _steam;
    private SteamGenerationIdentity? _attemptedGeneration;
    private SteamGenerationIdentity? _healthAttemptedGeneration;
    private IDeckyHealthMonitor? _health;
    private readonly Func<DeckyProcessTree.Identity, IDeckyHealthMonitor> _healthFactory;
    private readonly CancellationTokenSource _maintenanceStop = new();
    private long _rootEpoch;
    private int _closing, _maintaining;
    private bool _resetPending;
    private Task? _maintenance;
    private readonly Func<string, uint, ISteamGenerationLease?> _readSteam;
    private readonly Func<Process, DeckyProcessTree.Identity?> _readStarted;
    private readonly string _startupMutex;

    internal DeckyLifetime(string loader, int? attach = null,
        Func<string, uint, ISteamGenerationLease?>? readSteam = null, string startupMutex = @"Global\Playhub.Decky.Start",
        Func<Process, DeckyProcessTree.Identity?>? readStarted = null,
        Func<DeckyProcessTree.Identity, IDeckyHealthMonitor>? healthFactory = null)
    {
        _loader = loader; _attach = attach; _procedure = WindowProc;
        _readSteam = readSteam ?? ((sid, session) => SteamGenerationLease.Read(sid, session));
        _readStarted = readStarted ?? (process => DeckyProcessTree.Read(process.SafeHandle, process.Id));
        _healthFactory = healthFactory ?? (identity => new DeckyHealthMonitor(identity));
        _startupMutex = startupMutex;
    }

    internal int Run()
    {
        // Windows notifies this host before default-priority (0x280) onefile
        // parents. Confirmed session end releases worker DLLs before extraction
        // cleanup. Neither QUERYENDSESSION nor a cancelled ENDSESSION stops Decky.
        if (!SetProcessShutdownParameters(0x3f0, 0)) throw new IOException("Cannot set Decky shutdown order.");
        var cls = new WindowClass { Size = (uint)Marshal.SizeOf<WindowClass>(), Procedure = _procedure, Instance = GetModuleHandle(null), Name = _className };
        if (RegisterClassEx(ref cls) == 0) throw new IOException("Cannot register Decky lifecycle window.");
        _window = CreateWindowEx(0, _className, "Playhub Decky lifecycle", 0, 0, 0, 0, 0, 0, 0, cls.Instance, 0);
        if (_window == 0) throw new IOException("Cannot create Decky lifecycle window.");
        if (_attach is int attachedPid)
        {
            // Upgrading Playhub must protect a loader already running from an
            // older startup entry, without restarting Steam or its plugins.
            _identity = DeckyProcessTree.FindRoot(attachedPid, _loader)
                ?? throw new IOException("Cannot verify the existing Decky process.");
            _root = Process.GetProcessById(_identity.Pid);
            if (DeckyProcessTree.Read(_root.SafeHandle, _root.Id) != _identity)
                throw new IOException("The existing Decky process has changed.");
        }
        else
        {
            DeckyRuntimeDirectory.CleanupAbandoned(_loader);
            _runtime = DeckyRuntimeDirectory.Create(_loader);
            try { (_root, _identity) = StartOwnedLoader(LoaderStart(_runtime)); }
            catch { DeckyRuntimeDirectory.TryCleanup(_runtime, _loader); throw; }
        }
        if (_identity is null || !_identity.Path.Equals(_loader, StringComparison.OrdinalIgnoreCase)) throw new IOException("Cannot verify Decky ownership.");
        WatchRoot();
        _health = _healthFactory(_identity);
        _steam = _readSteam(_identity.Sid, _identity.Session);
        if (SetTimer(_window, SteamTimer, 1000, 0) == 0) Log("Steam lifecycle timer could not be started.");
        int result;
        while ((result = GetMessage(out var message, 0, 0, 0)) > 0) { TranslateMessage(ref message); DispatchMessage(ref message); }
        return result < 0 ? 1 : 0;
    }

    private void WatchRoot()
    {
        long epoch = ++_rootEpoch;
        _rootExitHandler = (_, _) => PostMessage(_window, RootExited, (nuint)epoch, 0);
        _root!.Exited += _rootExitHandler;
        _root.EnableRaisingEvents = true;
    }

    private void ObserveSteam()
    {
        if (Volatile.Read(ref _closing) != 0 || Interlocked.CompareExchange(ref _maintaining, 1, 0) != 0) return;
        _maintenance = Task.Run(MaintainSteamGeneration);
    }

    private async Task MaintainSteamGeneration()
    {
        ISteamGenerationLease? next = null;
        Process? created = null;
        string? createdRuntime = null;
        try
        {
            DeckyProcessTree.Identity root;
            long epoch;
            lock (_state)
            {
                if (_identity is null || _root is null || Volatile.Read(ref _closing) != 0) return;
                root = _identity; epoch = _rootEpoch;
            }
            // A retained live Steam handle already pins the immutable process
            // identity. Normal idle ticks need no token/SID or image queries.
            bool healthReset = false;
            DeckyProcessTree.Identity? failedRuntime = null;
            bool sameSteam = _steam is SteamGenerationLease retained && retained.MatchesRegistration(root.Sid, root.Session);
            if (!sameSteam)
            {
                next = _readSteam(root.Sid, root.Session);
                sameSteam = next is not null && _steam?.Identity == next.Identity && _steam.Alive && next.Alive;
            }
            if (sameSteam)
            {
                if (_healthAttemptedGeneration == _steam!.Identity || _health is null) return;
                failedRuntime = await _health.ReadFailure(_maintenanceStop.Token);
                if (failedRuntime is null) return;
                healthReset = true;
            }
            else _health?.Pause();
            next ??= _readSteam(root.Sid, root.Session);
            bool alive;
            lock (_state) alive = _root is not null && DeckyProcessTree.Read(_root.SafeHandle, root.Pid) == root;
            var action = SteamGenerationPolicy.Observe(_steam?.Identity, next?.Identity, _steam?.Exited == true,
                alive, Volatile.Read(ref _closing) != 0);
            if (healthReset && alive && next?.Identity == _steam?.Identity && next?.Alive == true
                && Volatile.Read(ref _closing) == 0) action = SteamGenerationAction.ResetLoader;
            if (action == SteamGenerationAction.Ignore) return;
            if (action == SteamGenerationAction.Bind)
            {
                lock (_state)
                {
                    if (_rootEpoch != epoch || Volatile.Read(ref _closing) != 0) return;
                    _steam = next; next = null;
                }
                return;
            }
            if (healthReset ? next!.Identity == _healthAttemptedGeneration : next!.Identity == _attemptedGeneration) return;
            using var startup = new Mutex(false, _startupMutex);
            bool owned;
            try { owned = startup.WaitOne(0); }
            catch (AbandonedMutexException) { owned = true; }
            if (!owned) return; // An installer or explicit stop always takes priority.
            try
            {
                using var current = _readSteam(root.Sid, root.Session);
                lock (_state)
                {
                    if (_rootEpoch != epoch || Volatile.Read(ref _closing) != 0 || !next.Alive
                        || current?.Identity != next.Identity
                        || (healthReset ? !DeckyHealthPolicy.CanReset(_steam?.Identity, next.Identity,
                            _healthAttemptedGeneration, _steam?.Alive == true, alive,
                            failedRuntime is not null && _health?.Confirm(failedRuntime) == true,
                            Volatile.Read(ref _closing) != 0) : _steam?.Exited != true)
                        || _root is null || DeckyProcessTree.Read(_root.SafeHandle, root.Pid) != root) return;
                    _resetPending = true;
                    if (healthReset) _healthAttemptedGeneration = next.Identity;
                    else _attemptedGeneration = next.Identity;
                }
                Log(healthReset
                    ? $"Decky runtime {failedRuntime!.Pid}/{failedRuntime.Birth} is spinning without a listener after three failed health probes; resetting owned root {root.Pid}/{root.Birth} once for Steam {next.Identity.Pid}/{next.Identity.Birth}."
                    : $"Steam generation changed to {next.Identity.Pid}/{next.Identity.Birth}; resetting owned Decky root {root.Pid}/{root.Birth}.");
                if (DeckyProcessTree.StopRoot(root) == 0) return;
                if (_runtime is not null) DeckyRuntimeDirectory.TryCleanup(_runtime, _loader);
                if (Volatile.Read(ref _closing) != 0) return;
                createdRuntime = DeckyRuntimeDirectory.Create(_loader);
                var start = LoaderStart(createdRuntime);
                var started = StartOwnedLoader(start, root);
                created = started.Process;
                var identity = started.Identity;
                lock (_state)
                {
                    if (_rootEpoch != epoch || Volatile.Read(ref _closing) != 0) return;
                    if (_rootExitHandler is not null) _root!.Exited -= _rootExitHandler;
                    _root!.Dispose();
                    _root = created; created = null; _identity = identity;
                    _runtime = createdRuntime; createdRuntime = null;
                    _steam?.Dispose(); _steam = next; next = null;
                    _health?.Dispose(); _health = _healthFactory(identity);
                    WatchRoot();
                }
                Log($"Decky lifecycle reset completed with root {identity.Pid}/{identity.Birth}.");
                if (Volatile.Read(ref _closing) != 0) DeckyProcessTree.StopDescendants(identity);
            }
            finally { startup.ReleaseMutex(); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or System.ComponentModel.Win32Exception or WaitHandleCannotBeOpenedException)
        { Log("Steam lifecycle reset stopped: " + error.Message); }
        catch (OperationCanceledException) when (_maintenanceStop.IsCancellationRequested) { }
        finally
        {
            next?.Dispose();
            if (created is not null)
            {
                try { StopUnadopted(created); }
                catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
                { Log("Unadopted Decky cleanup stopped: " + error.Message); }
                finally { created.Dispose(); }
            }
            if (createdRuntime is not null) { try { DeckyRuntimeDirectory.TryCleanup(createdRuntime, _loader); } catch { } }
            lock (_state) _resetPending = false;
            Interlocked.Exchange(ref _maintaining, 0);
            PostMessage(_window, MaintenanceFinished, 0, 0);
        }
    }

    private ProcessStartInfo LoaderStart(string runtime)
    {
        var start = new ProcessStartInfo(_loader) { WorkingDirectory = Path.GetDirectoryName(_loader)!, UseShellExecute = false };
        foreach (string name in start.Environment.Keys.ToArray())
            if (name.StartsWith("_PYI", StringComparison.OrdinalIgnoreCase) || name.Equals("_MEIPASS2", StringComparison.OrdinalIgnoreCase)) start.Environment.Remove(name);
        start.Environment["PYINSTALLER_RESET_ENVIRONMENT"] = "1";
        start.Environment["TEMP"] = start.Environment["TMP"] = runtime;
        return start;
    }

    private (Process Process, DeckyProcessTree.Identity Identity) StartOwnedLoader(ProcessStartInfo start,
        DeckyProcessTree.Identity? previous = null)
    {
        Process process;
        if (previous is not null)
        {
            // The state gate covers only CreateProcess. Attestation and cleanup
            // must not block the native window's Windows shutdown handler.
            lock (_state)
            {
                if (Volatile.Read(ref _closing) != 0) throw new IOException("The Decky session is ending.");
                process = Process.Start(start) ?? throw new IOException("Cannot start Decky.");
            }
        }
        else process = Process.Start(start) ?? throw new IOException("Cannot start Decky.");
        try
        {
            var identity = _readStarted(process);
            if (identity is null || !identity.Path.Equals(_loader, StringComparison.OrdinalIgnoreCase)
                || (previous is not null && (identity.Sid != previous.Sid || identity.Session != previous.Session)))
                throw new IOException("Cannot verify started Decky ownership.");
            return (process, identity);
        }
        catch
        {
            try { StopUnadopted(process); }
            finally { process.Dispose(); }
            throw;
        }
    }

    private void StopUnadopted(Process process)
    {
        // This handle belongs to the exact process returned by Process.Start,
        // never a subsequently opened PID. Descendants require an independent
        // full identity read; incomplete proof never authorizes a tree walk.
        var handle = process.SafeHandle;
        try
        {
            var identity = DeckyProcessTree.Read(handle, process.Id);
            if (identity is not null) DeckyProcessTree.StopDescendants(identity);
        }
        finally
        {
            if (WaitForSingleObject(handle, 1500) != 0)
            {
                if (!TerminateProcess(handle, 0) && WaitForSingleObject(handle, 0) != 0)
                    throw new IOException("The unverified Decky child is still closing.");
                if (WaitForSingleObject(handle, 10000) != 0)
                    throw new IOException("The unverified Decky child did not close.");
            }
        }
    }

    private void Log(string message)
    {
        try { File.AppendAllText(Path.Combine(Path.GetDirectoryName(_loader)!, "Playhub-Decky-lifecycle.log"), DateTimeOffset.UtcNow.ToString("O") + " " + message + Environment.NewLine); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private nint WindowProc(nint window, uint message, nuint wparam, nint lparam)
    {
        if (message == 0x11) return 1; // WM_QUERYENDSESSION: consent immediately
        if (message == 0x16) // WM_ENDSESSION
        {
            if (wparam != 0 && !_sessionEnded)
            {
                _sessionEnded = true;
                Interlocked.Exchange(ref _closing, 1);
                _maintenanceStop.Cancel();
                KillTimer(window, SteamTimer);
                DeckyProcessTree.Identity? identity;
                lock (_state) identity = _identity;
                try { if (identity is not null) DeckyProcessTree.StopDescendants(identity); } catch { }
            }
            return 0;
        }
        if (message == 0x113 && wparam == SteamTimer) { ObserveSteam(); return 0; }
        if (message == RootExited || message == MaintenanceFinished)
        {
            lock (_state)
                if (_resetPending || _root is null || !_root.HasExited || (message == RootExited && wparam != (nuint)_rootEpoch)) return 0;
            // PyInstaller normally removes _MEI itself. Abandoned extraction is
            // retried only under this host's marked private runtime container.
            try { if (_runtime is not null) DeckyRuntimeDirectory.TryCleanup(_runtime, _loader); } catch { }
            PostQuitMessage(0);
            return 0;
        }
        return DefWindowProc(window, message, wparam, lparam);
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _closing, 1);
        _maintenanceStop.Cancel();
        if (_window != 0) KillTimer(_window, SteamTimer);
        try { _maintenance?.Wait(TimeSpan.FromSeconds(3)); } catch (AggregateException) { }
        if (_root is not null) { if (_rootExitHandler is not null) _root.Exited -= _rootExitHandler; _root.Dispose(); }
        _steam?.Dispose();
        _health?.Dispose();
        _maintenanceStop.Dispose();
        if (_window != 0) { DestroyWindow(_window); _window = 0; }
        UnregisterClass(_className, GetModuleHandle(null));
    }

    private delegate nint WindowProcedure(nint hwnd, uint message, nuint wparam, nint lparam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass { public uint Size, Style; public WindowProcedure Procedure; public int ClassExtra, WindowExtra; public nint Instance, Icon, Cursor, Background; public string? Menu, Name; public nint SmallIcon; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Message { public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetProcessShutdownParameters(uint level, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? module);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterClassExW")] private static extern ushort RegisterClassEx(ref WindowClass cls);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")] private static extern nint CreateWindowEx(uint ex, string cls, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string cls, nint instance);
    [DllImport("user32.dll", EntryPoint = "GetMessageW")] private static extern int GetMessage(out Message message, nint window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")] private static extern nint DispatchMessage(ref Message message);
    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")] private static extern nint DefWindowProc(nint window, uint message, nuint wparam, nint lparam);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] private static extern bool PostMessage(nint window, uint message, nuint wparam, nint lparam);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] private static extern nuint SetTimer(nint window, nuint id, uint milliseconds, nint callback);
    [DllImport("user32.dll")] private static extern bool KillTimer(nint window, nuint id);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(Microsoft.Win32.SafeHandles.SafeProcessHandle process, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateProcess(Microsoft.Win32.SafeHandles.SafeProcessHandle process, uint code);
}

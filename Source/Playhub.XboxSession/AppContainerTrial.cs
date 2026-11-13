// Playhub Xbox Session. Distributed under GPL-3.0-only.
// The overlay preparation order follows WSGM AppContainerOverlayRoute at 5268460.
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using Playhub.GameSession;
using WSGM.PackagedLaunch;

internal readonly record struct TrialHandoff(uint Pid, long Creation, long Activation)
{
    internal bool IsFresh(long actualCreation, long now) => Pid is > 0 and <= int.MaxValue
        && Creation == actualCreation && Activation > 0 && Activation <= Creation && Creation <= now
        && now - Activation <= TimeSpan.FromSeconds(15).Ticks;
}

internal static class AppContainerTrial
{
    private const string Family = "StudioMDHR.20872A364DAA1_tm1s6a95559gt";
    private const string Executable = "Cuphead.exe";
    private static readonly XboxCommand Command = new(Family + "!App", Executable, "");
    private static readonly string[] SteamFlags = ["SteamClientLaunch", "SteamEnv", "SteamTenfoot", "SteamVirtualGamepad"];

    internal static int Run()
    {
        try
        {
            var handoff = ReceiveHandoff();
            if (handoff is null) return 0;

            using var current = Process.GetCurrentProcess();
            using var parent = TrialSteamTracking.OpenParent(current);
            var gameId = ReadSteamGameId(Environment.GetEnvironmentVariable("SteamGameId"));
            using var steam = TrustedSteamFiles.Open();
            var plan = ResolveCuphead();
            using var target = InspectTarget(handoff.Value, plan, (uint)current.SessionId);
            if (target is null)
                throw new InvalidOperationException("The handoff did not identify a newly activated registered Cuphead process.");
            if (!WaitForSteamTracking(steam, parent, gameId))
                throw new InvalidOperationException("Steam did not register the verified GameSession parent for this shortcut.");
            var sid = target.ReadContainerSid();
            if (!sid.Equals(TrialBridgeFile.PackageSid(Family)))
                throw new InvalidOperationException("The process token does not carry the registered Cuphead package SID.");
            using var bridgeFile = TrialBridgeFile.Open(sid);
            return RunOverlay(target, parent, steam, gameId, bridgeFile.Path, plan.PackageFullName,
                new LeaseProcess((uint)current.Id, current.StartTime.ToFileTimeUtc()));
        }
        catch (Exception error)
        {
            PackagedLaunchLog.Error("AppContainer trial skipped: " + error.Message);
            return 1;
        }
    }

    internal static AppContainerLaunchPlan ResolveCuphead() =>
        AppContainerProof.Resolve(Command, new RegisteredPackageCatalog(physicalPaths: true), verifyImage: false);

    internal static TrialHandoff? ReceiveHandoff()
    {
        if (!Console.IsInputRedirected) throw new InvalidOperationException("A private GameSession handoff is required.");
        using var stdin = Console.OpenStandardInput();
        return ReadHandoff(stdin).WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
    }

    internal static RetainedAppContainerTarget? InspectTarget(TrialHandoff handoff, AppContainerLaunchPlan plan, uint session)
    {
        if (!handoff.IsFresh(handoff.Creation, DateTime.UtcNow.ToFileTimeUtc())) return null;
        // AAM can return immediately before the package identity becomes readable.
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var target = RetainedAppContainerTarget.Inspect(handoff.Pid, Command, plan, session);
            if (target is not null)
            {
                if (handoff.IsFresh(target.Creation, DateTime.UtcNow.ToFileTimeUtc())) return target;
                target.Dispose();
                return null;
            }
            if (attempt < 7) Thread.Sleep(75);
        }
        return null;
    }

    internal static bool WaitForSteamTracking(TrustedSteamFiles steam, Process parent, ulong gameId)
    {
        var clock = Stopwatch.StartNew();
        do
        {
            if (parent.HasExited || !steam.UnchangedRoot()) return false;
            if (TrialSteamTracking.IsTracked(steam.TrackingLog, parent, gameId)) return true;
            Thread.Sleep(50);
        } while (clock.ElapsedMilliseconds < 1000);
        return false;
    }

    private static int RunOverlay(RetainedAppContainerTarget target, Process parent, TrustedSteamFiles steam,
        ulong gameId, string bridgePath, string package, LeaseProcess owner)
    {
        int pid = checked((int)target.Pid);
        string[] paths = [.. steam.ComponentPaths];
        if (parent.HasExited || !steam.UnchangedRoot() || !target.IsCurrent()
            || paths.Any(path => GameInjector.HasModule(pid, Path.GetFileName(path)))
            || GameInjector.HasModule(pid, AppContainerProof.BridgeFileName))
            throw new InvalidOperationException("The game exited or a Steam component or bridge was already present.");
        using var transfer = target.OpenDuplicateHandle();
        nint Duplicate(nint source, uint rights)
        {
            if (!target.Owns(pid, transfer.DangerousGetHandle())) return 0;
            if (!DuplicateHandle(GetCurrentProcess(), source, transfer, out nint duplicate, rights, false, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return duplicate;
        }

        var setupTime = Stopwatch.StartNew();
        var injector = new GameInjector(new PrivilegeJournal(), (processId, handle) =>
            setupTime.ElapsedMilliseconds < 90_000 && !parent.HasExited && steam.UnchangedRoot() && target.Owns(processId, handle));
        var leaseDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Playhub", "XboxPackageLifetime");
        using var lease = PackageLifetimeLease.Acquire(leaseDirectory, package, WindowsPackageLifetime.BootMarker(),
            owner, new LeaseProcess(target.Pid, target.Creation), new WindowsPackageLifetime(), WindowsPackageLifetime.Observe,
            PackagedLaunchLog.Info);
        AppContainerBridgeSession? broker = null;
        try
        {
            broker = AppContainerBridgeSession.Start(pid, gameId, bridgePath, injector, Duplicate);
            if (broker.Stage != AppContainerBridgeStage.Ready || broker.Uncertain)
            {
                PackagedLaunchLog.Error($"AppContainer bridge stopped at {broker.Stage}; uncertain={broker.Uncertain}; result={broker.InitializationResult?.ToString() ?? "none"}; {broker.Failure?.Message}");
                return 1;
            }
            var result = PrepareRenderer(SessionVariables(gameId), paths, variables => injector.SetEnvironment(pid, variables),
                path => injector.Load(pid, path) && GameInjector.HasModule(pid, Path.GetFileName(path), path),
                () => injector.Call(pid, bridgePath, "InitializeInputBridge"));
            PackagedLaunchLog.Info($"AppContainer preparation pid={pid} renderer-load={result.RendererLoaded}; input-init={result.InputResult?.ToString() ?? "none"}; uncertain={injector.Latched(pid)}");
            return result.RendererLoaded ? 0 : 1;
        }
        finally
        {
            // The bridge can still use its private transport after partial setup or parent exit.
            try { target.WaitForExit(); }
            finally { broker?.Dispose(); }
        }
    }

    internal static (bool RendererLoaded, uint? InputResult) PrepareRenderer(IReadOnlyList<string> variables,
        IReadOnlyList<string> components, Func<IReadOnlyList<string>, bool> publish, Func<string, bool> load, Func<uint?> input)
    {
        if (components.Count != 4 || !publish(variables)) return (false, null);
        foreach (var component in components)
            if (!load(component)) return (false, null);
        return (true, input());
    }

    internal static ulong ReadSteamGameId(string? value)
    {
        if (!ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id)
            || (uint)id != 0x02000000 || (id >> 32) < 0x80000000)
            throw new InvalidOperationException("The Steam shortcut session identity is unavailable.");
        return id;
    }

    private static IReadOnlyList<string> SessionVariables(ulong id)
    {
        var exact = id.ToString(CultureInfo.InvariantCulture);
        List<string> values = [$"SteamGameId={exact}", $"SteamOverlayGameId={exact}"];
        string? app = Environment.GetEnvironmentVariable("SteamAppId");
        if (uint.TryParse(app, NumberStyles.None, CultureInfo.InvariantCulture, out var appId)
            && (appId == 0 || appId == id >> 32)) values.Add($"SteamAppId={appId}");
        foreach (var name in SteamFlags)
        {
            string? value = Environment.GetEnvironmentVariable(name);
            if (value is "0" or "1") values.Add($"{name}={value}");
        }
        return values;
    }

    internal static async Task<TrialHandoff?> ReadHandoff(Stream input)
    {
        byte[] buffer = new byte[256];
        int length = 0;
        while (length < buffer.Length)
        {
            int count = await input.ReadAsync(buffer.AsMemory(length)).ConfigureAwait(false);
            if (count == 0 && length == 0) return null;
            if (count == 0) break;
            int end = Array.IndexOf(buffer, (byte)'\n', length, count);
            if (end >= 0)
            {
                if (end != length + count - 1) throw new InvalidDataException("Unexpected data after the AppContainer handoff.");
                using var data = JsonDocument.Parse(buffer.AsMemory(0, end));
                var value = data.RootElement;
                var handoff = new TrialHandoff(value.GetProperty("pid").GetUInt32(),
                    value.GetProperty("creationFileTime").GetInt64(), value.GetProperty("activationFileTime").GetInt64());
                if (await input.ReadAsync(new byte[1]).ConfigureAwait(false) != 0)
                    throw new InvalidDataException("Unexpected data after the AppContainer handoff.");
                return handoff;
            }
            length += count;
        }
        throw new InvalidDataException("The AppContainer handoff is incomplete or too large.");
    }

    [DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DuplicateHandle(nint sourceProcess, nint source, SafeProcessHandle targetProcess,
        out nint target, uint desired, bool inherit, uint options);
}

// Playhub Xbox Session. Distributed under GPL-3.0-only.
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using WSGM.PackagedLaunch;

internal static class SteamCompanionTrial
{
    private const string NativeFile = "PlayhubSteamCompanion.dll";
    private static int _overlayIsOpen;
    private static int _gameHasForeground;
    private static int _overlayScope;
    private static long _keyboardAt;
    internal static bool OverlayIsOpen => Volatile.Read(ref _overlayIsOpen) != 0;
    internal static bool GameHasForeground => Volatile.Read(ref _gameHasForeground) != 0;

    internal static int Run()
    {
        try
        {
            using var self = Process.GetCurrentProcess();
            using var parent = TrialSteamTracking.OpenParent(self);
            ulong gameId = AppContainerTrial.ReadSteamGameId(Environment.GetEnvironmentVariable("SteamGameId"));
            using var steam = TrustedSteamFiles.Open();
            var plan = AppContainerTrial.ResolveCuphead();
            using var verifiedLibrary = OpenVerifiedLibrary();
            using var preference = PrepareWindowedPreference(steam, parent, gameId, plan);
            var handoff = AppContainerTrial.ReceiveHandoff();
            if (handoff is null) return 0;
            using var target = AppContainerTrial.InspectTarget(handoff.Value, plan, (uint)self.SessionId);
            if (target is null || !AppContainerTrial.WaitForSteamTracking(steam, parent, gameId))
                throw new InvalidOperationException("The companion requires the verified fresh Cuphead process and Steam shortcut.");
            using var controllerProbe = SteamControllerSourceProbe.TryStart(steam, gameId, target, parent);
            return RunSurface(target.ProcessHandle, parent, steam, gameId, target.Pid, target.Creation, CupheadWindowedPreference.Aumid,
                () => SteamControllerLayoutTrial.TryStart(steam, gameId, target, parent, () => GameHasForeground, () => OverlayIsOpen));
        }
        catch (Exception error)
        {
            PackagedLaunchLog.Error("Steam companion skipped: " + error.Message);
            return 1;
        }
    }

    // Both launch routes retain the original exact process. AppContainer
    // identity checks stay in Run(); the direct Win32 route has its own guard.
    internal static int RunSurface(SafeProcessHandle target, Process parent, TrustedSteamFiles steam,
        ulong gameId, uint pid, long birth, string? expectedFrameAumid = null, Func<IDisposable?>? onConfiguredLayout = null)
    {
        string libraryPath = Path.Combine(AppContext.BaseDirectory, NativeFile);
        using var libraryFile = OpenVerifiedLibrary();
        string rendererPath = steam.ComponentPaths.Last();
        string steamRoot = Path.GetDirectoryName(rendererPath)!;
        using var steamClient = TryPinValveImage(Path.Combine(steamRoot, "steam.exe"));
        using var steamBrowser = TryPinValveImage(Path.Combine(steamRoot, "bin", "cef", "cef.win64", "steamwebhelper.exe"));
        if (parent.HasExited || !steam.UnchangedRoot())
            throw new InvalidOperationException("The verified Steam session ended before companion initialization.");
        nint library = NativeLibrary.Load(libraryPath, typeof(SteamCompanionTrial).Assembly, DllImportSearchPath.System32);
        try
        {
            using var nativeInputStatus = new SteamNativeOverlayInputStatus(target, parent, gameId, pid, birth,
                Path.GetDirectoryName(steam.ComponentPaths.Last())!);
            using var selfMirror = Process.GetCurrentProcess();
            long mirrorBirth = selfMirror.StartTime.ToFileTimeUtc();
            bool mirrorConfigured = SteamMirrorNativeConfiguration.TryConfigure(library, (uint)parent.Id,
                parent.StartTime.ToFileTimeUtc(), gameId, pid, birth, mirrorBirth);
            using var mirrorBridge = mirrorConfigured ? SteamMirrorInputPresentationBridge.Attach(library, pid, birth, gameId, mirrorBirth) : null;
            if (mirrorConfigured && mirrorBridge is null) throw new InvalidDataException("The configured mirror getter is unavailable.");
            using var mirrorStatus = mirrorConfigured ? new SteamMirrorStatus((uint)parent.Id, parent.StartTime.ToFileTimeUtc(),
                gameId, pid, birth, mirrorBirth, SteamMirrorNativeConfiguration.LeaseDuration) : null;
            using var status = new SteamCompanionStatus((uint)parent.Id, parent.StartTime.ToFileTimeUtc(), gameId,
                pid, birth, () => (Volatile.Read(ref _overlayScope), OverlayIsOpen, Volatile.Read(ref _keyboardAt)));
            var run = Marshal.GetDelegateForFunctionPointer<RunNative>(NativeLibrary.GetExport(library, "RunSteamCompanion"));
            IDisposable? StartLayout()
            {
                var worker = onConfiguredLayout?.Invoke();
                bool enabled = typeof(SteamControllerLayoutTrial).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                    .Any(item => item.Key == "PlayhubSteamControllerLayoutTrial" && item.Value == "true");
                if (worker is null && !enabled) nativeInputStatus.MarkNoLayoutWorker();
                return worker;
            }
            int outcome = SteamMirrorRunOrder.Run(StartLayout, () => run(target, parent.SafeHandle, pid, birth, rendererPath, HandleEvent,
                status.TakeOpenRequest, steamClient?.Name, steamBrowser?.Name, expectedFrameAumid));
            PackagedLaunchLog.Info($"Steam companion ended status=0x{outcome:X8}.");
            return outcome;
        }
        finally
        {
            Volatile.Write(ref _overlayIsOpen, 0);
            Volatile.Write(ref _gameHasForeground, 0);
            Volatile.Write(ref _overlayScope, 0);
            NativeLibrary.Free(library);
        }
    }

    internal static FileStream OpenVerifiedLibrary()
    {
        string? expected = typeof(SteamCompanionTrial).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "PlayhubSteamCompanionSha256")?.Value;
        var library = File.OpenRead(Path.Combine(AppContext.BaseDirectory, NativeFile));
        try { VerifyDigest(library, expected); return library; }
        catch { library.Dispose(); throw; }
    }

    private static FileStream? TryPinValveImage(string path)
    {
        FileStream? file = null;
        try
        {
            file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (TrustedSteamFiles.ValveSignature(path)) return file;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        file?.Dispose();
        return null;
    }

    private static CupheadWindowedPreference? PrepareWindowedPreference(TrustedSteamFiles steam, Process parent,
        ulong gameId, AppContainerLaunchPlan plan)
    {
        bool enabled = typeof(SteamCompanionTrial).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Any(attribute => attribute.Key == "PlayhubCupheadWindowedPreference" && attribute.Value == "true");
        if (!enabled) return null;
        CupheadWindowedPreference? preference = null;
        try
        {
            if (AppContainerTrial.WaitForSteamTracking(steam, parent, gameId))
                preference = CupheadWindowedPreference.TryAcquire(CupheadWindowedPreference.Aumid,
                    plan.Executable, plan.PackageFullName, CupheadIsAbsent, PackagedLaunchLog.Info);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or InvalidDataException or System.Text.Json.JsonException)
        {
            PackagedLaunchLog.Info($"Cuphead windowed preference unavailable: {error.GetType().Name}.");
        }

        try
        {
            Console.WriteLine("playhub-cuphead-ready");
            return preference;
        }
        catch
        {
            preference?.Dispose();
            throw;
        }
    }

    private static bool CupheadIsAbsent()
    {
        var candidates = Process.GetProcessesByName("Cuphead");
        foreach (var candidate in candidates) candidate.Dispose();
        return candidates.Length == 0;
    }

    internal static void VerifyDigest(Stream image, string? expected)
    {
        if (expected?.Length != 64 || !expected.All(Uri.IsHexDigit) ||
            !Convert.ToHexString(SHA256.HashData(image)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The local companion does not match this build's pinned SHA-256 digest.");
    }

    private static void HandleEvent(uint kind, int detail)
    {
        if (kind == 11) { Volatile.Write(ref _overlayScope, detail); return; }
        if (kind == 12) { Volatile.Write(ref _keyboardAt, Environment.TickCount64); return; }
        if (kind == 9)
        {
            Volatile.Write(ref _gameHasForeground, detail == 1 ? 1 : 0);
            return;
        }
        if (kind == 2) Volatile.Write(ref _overlayIsOpen, 1);
        if (kind is 3 or 4) Volatile.Write(ref _overlayIsOpen, 0);
        string name = kind switch
        {
            1 => "renderer and transparent surface ready",
            2 => "Valve overlay open",
            3 => "Valve overlay closed",
            4 => "companion hidden after focus left the game",
            5 => "overlay hotkey refused or timed out",
            6 => detail == 0 ? "Guide warmup returned focus to the game" : "Guide warmup could not confirm game focus",
            7 => "transparent surface preparation failed",
            8 => detail switch
            {
                0 => "direct focus transfer confirmed",
                1 => "attached focus transfer confirmed",
                2 => "Guide window activation confirmed",
                _ => "focus transfer refused"
            },
            10 => detail switch
            {
                0 => "mouse activation button shown",
                1 => "mouse activation transferred focus to companion",
                _ => "mouse activation focus transfer refused"
            },
            13 => "Steam Guide requested the overlay",
            16 => detail == 0 ? "Steam Guide recovered overlay foreground" : "Steam Guide foreground recovery refused",
            17 => detail == 0 ? "SDR display frame retained for the fullscreen menu" : "fullscreen menu frame capture refused",
            18 => detail == 1 ? "exact minimized game retained behind the menu" : "game left the minimized menu state",
            19 => detail == 0 ? "fullscreen game focus restored before hiding the menu" : "fullscreen game focus restoration pending",
            20 => "surface Present result changed",
            21 => detail == 1 ? "shown surface frame submitted with S_OK" : "surface frame submission not confirmed",
            22 => detail switch
            {
                0 => "display frame copied and source revalidated",
                1 => "black display frame copied and source revalidated",
                2 => "display frame capture surface unavailable",
                3 => "display frame source identity or foreground changed",
                4 => "display frame BitBlt failed",
                5 => "display frame GDI batch flush failed",
                _ => "display frame pixel buffer invalid"
            },
            23 => "display frame BitBlt last-error value",
            24 => "public Steam context verification flags",
            25 => "public Steam context application ID",
            26 => "public Steam context refused with Win32 error",
            27 => "public Steam context resource release flags",
            28 => detail switch
            {
                0 => "public Steam context read not started",
                1 => "public Steam context repeated read refused",
                2 => "public Steam session changed before pipe creation",
                3 => "public Steam context pipe unavailable",
                4 => "public Steam session changed before connecting the global user",
                5 => "public Steam global user connection returned no handle",
                6 => "public Steam session changed before Friends acquisition",
                7 => "public Steam session changed before Utils acquisition",
                8 => "public Steam context Utils unavailable",
                9 => "public Steam session changed before application ID read",
                10 => "public Steam session changed after application ID read",
                11 => "public Steam context read completed",
                12 => "public Steam context skipped after a creation Present without S_OK",
                _ => "public Steam context read outcome unknown"
            },
            29 => $"post-attach focus facts: current={(detail & 1) != 0}, invoked={(detail & 2) != 0}, returned={(detail & 4) != 0}, ownBefore={(detail & 8) != 0}, sourceBefore={(detail & 16) != 0}, ownAfter={(detail & 32) != 0}, sourceAfter={(detail & 64) != 0}, before={((detail >> 8) & 3) switch { 0 => "none", 1 => "source", 2 => "owned", _ => "other" }}, after={((detail >> 10) & 3) switch { 0 => "none", 1 => "source", 2 => "owned", _ => "other" }}",
            _ => "native diagnostic"
        };
        PackagedLaunchLog.Info($"Steam companion: {name}; detail=0x{detail:X8}.");
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void CompanionEvent(uint kind, int detail);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CompanionGuideRequest();

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int RunNative(SafeProcessHandle game, SafeProcessHandle owner, uint pid, long birth,
        [MarshalAs(UnmanagedType.LPWStr)] string renderer, CompanionEvent report, CompanionGuideRequest takeGuideRequest,
        [MarshalAs(UnmanagedType.LPWStr)] string? steamClient, [MarshalAs(UnmanagedType.LPWStr)] string? steamBrowser,
        [MarshalAs(UnmanagedType.LPWStr)] string? expectedFrameAumid);
}

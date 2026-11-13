using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Playhub.DeckyHost;
using Playhub.Shared;

namespace GamingMode.Services;

internal sealed class BigPictureGameReturn(FileLogger logger) : IDisposable
{
    internal readonly record struct ForegroundProof(uint AppId, bool UsedCompanion, uint GamePid = 0, long GameBirth = 0);
    private sealed record ObservedCompanion(QamWindowIdentity Window);
    private sealed record ConfirmedReturn(QamWindowIdentity Window, long ExpiresAt, ForegroundProof Proof, bool PackagedGame,
        CompanionLaunchIdentity? Win32Launch);
    private sealed record Session(QamWindowIdentity Source, OverlayWindowTools.ProcessLease Game,
        QamWindowIdentity Steam, string SteamRoot, string? HostedAumid, UwpSessionIdentity? Uwp,
        CancellationTokenSource Stop, BigPictureReturnPolicy Policy)
    {
        internal uint VerifiedAppId { get; set; }
        internal volatile ObservedCompanion? Companion;
        internal volatile CompanionLaunchIdentity? Win32Launch;
    }

    private readonly object _sync = new();
    private Session? _session;
    private ConfirmedReturn? _confirmedReturn;
    private int _version;
    private bool _disposed;
    internal int Version => Volatile.Read(ref _version);

    internal bool Arm(nint window, uint pid, uint appId, Func<bool> verify, string? configuredSteamPath,
        string? hostedAumid = null, UwpSessionIdentity? uwp = null)
    {
        lock (_sync)
        {
            if (_disposed || window == 0 || pid == 0 || OverlayWindowTools.GetForegroundWindowHandle() != window
                || !MatchesSource(window, pid, hostedAumid)) return false;
            if (_session is { } active && active.Source.GamePid == pid && active.Source.Handle == window
                && active.HostedAumid == hostedAumid && (active.Uwp == uwp || (appId == 0 && uwp is null))
                && active.Game.Alive && WindowsQamFocusAdapter.IsWindowOwnerCurrent(active.Source))
            {
                if (appId != 0 && appId != active.VerifiedAppId && !verify()) return false;
                if (appId != 0) active.VerifiedAppId = appId;
                if (active.Uwp is null && active.Win32Launch is null && active.VerifiedAppId >= 0x80000000)
                    active.Win32Launch = ReadWin32Launch(active.Source, active.VerifiedAppId, active.SteamRoot);
                active.Policy.Observe(ReturnForeground.Game, true);
                return true;
            }
            if (SteamInstallationPath.FindRoot(configuredSteamPath) is not { } root
                || WindowsQamFocusAdapter.CaptureSteam(root) is not { } steam
                || WindowsQamFocusAdapter.Capture(window, pid) is not { } source || !verify()) return false;
            var game = OverlayWindowTools.ProcessLease.TryOpen((int)pid);
            if (game is null) return false;
            if (!game.Alive || game.CreatedAt != source.GameBirth || OverlayWindowTools.GetForegroundWindowHandle() != window
                || !WindowsQamFocusAdapter.IsWindowOwnerCurrent(source) || !MatchesSource(window, pid, hostedAumid))
            {
                game.Dispose();
                return false;
            }
            uint verifiedAppId = appId;
            if (appId == 0 && hostedAumid is null && uwp is null
                && _session is { HostedAumid: null, Uwp: null, VerifiedAppId: > 0 } prior
                && !prior.Stop.IsCancellationRequested && prior.Game.Alive && prior.Game.CreatedAt == game.CreatedAt
                && prior.Steam.OwnerPid == steam.OwnerPid && prior.Steam.OwnerBirth == steam.OwnerBirth
                && string.Equals(prior.SteamRoot, root, StringComparison.OrdinalIgnoreCase))
            {
                // A game can replace its foreground HWND during initialization.
                // The passive scan has no new AppID, but the process may already be verified.
                verifiedAppId = ReplacementAppId(prior.VerifiedAppId, prior.Source, source);
            }
            _session?.Stop.Cancel();
            _confirmedReturn = null;
            var session = new Session(source, game, steam, root, hostedAumid, uwp,
                new CancellationTokenSource(), new BigPictureReturnPolicy()) { VerifiedAppId = verifiedAppId };
            if (uwp is null && verifiedAppId >= 0x80000000)
                session.Win32Launch = ReadWin32Launch(source, verifiedAppId, root);
            _session = session;
            logger.Info($"Post-game return armed: gamePid={pid}, appId={verifiedAppId}, hosted={hostedAumid is not null}, companionIdentity={uwp is not null}.");
            _ = ObserveExitAsync(session);
            return true;
        }
    }

    internal static uint ReplacementAppId(uint verifiedAppId, QamWindowIdentity previous, QamWindowIdentity current) =>
        previous.Handle != 0 && current.Handle != 0 && previous.Handle != current.Handle
        && previous.GamePid != 0 && previous.GamePid == current.GamePid
        && previous.OwnerPid == previous.GamePid && current.OwnerPid == current.GamePid
        && previous.GameBirth > 0 && previous.GameBirth == current.GameBirth
        && previous.OwnerBirth == previous.GameBirth && current.OwnerBirth == current.GameBirth ? verifiedAppId : 0;

    private async Task ObserveExitAsync(Session session)
    {
        var token = session.Stop.Token;
        try
        {
            using var observer = new WindowLifecycleObserver(() => { }, hwnd =>
            {
                try { session.Policy.Observe(Classify(session, hwnd), session.Game.Alive); }
                catch (Exception error) { logger.Error("Could not check post-game foreground ownership.", error); }
            }, watchLifecycle: false);
            await session.Game.WaitForExitAsync(token);
            // Let Windows and Steam settle their own focus transition before trying once.
            await Task.Delay(200, token);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                bool steamAlreadyForeground = IsReturnedToSteam(session, token);
                if (!steamAlreadyForeground && !CanReturn(session, token))
                {
                    logger.Info($"Post-game return withheld: cancelled={token.IsCancellationRequested}, gameExited={session.Game.Exited}, steamOwnerCurrent={WindowsQamFocusAdapter.IsWindowOwnerCurrent(session.Steam)}, foreground={Classify(session, OverlayWindowTools.GetForegroundWindowHandle())}.");
                    return;
                }
                if (steamAlreadyForeground ||
                    WindowsQamFocusAdapter.ActivateWindow(session.Steam.Handle, () => CanReturn(session, token)))
                {
                    lock (_sync)
                    {
                        if (!ReferenceEquals(_session, session) || !IsReturnedToSteam(session, token)) return;
                        // Launcher helpers can keep Steam's running-app flag set
                        // for several seconds after the verified game has exited.
                        _confirmedReturn = new(session.Steam, Environment.TickCount64 + 15000,
                            new(session.VerifiedAppId, session.VerifiedAppId != 0 && session.Policy.UsedCompanion,
                                session.Source.GamePid, session.Source.GameBirth), session.Uwp is not null,
                            session.Win32Launch);
                        Interlocked.Increment(ref _version);
                    }
                    logger.Info("Post-game Big Picture foreground confirmed.");
                    return;
                }
                if (attempt < 2) await Task.Delay(200, token);
            }
            logger.Info("Windows did not grant post-game Big Picture foreground.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { logger.Error("Could not restore post-game Big Picture foreground.", error); }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_session, session)) _session = null;
                session.Stop.Dispose();
            }
            session.Game.Dispose();
        }
    }

    private static bool MatchesSource(nint window, uint pid, string? aumid) =>
        aumid is null ? OverlayWindowTools.WindowOwnerProcessId(window) == pid : UwpFrameWindow.Matches(window, aumid);

    // Windows can restore the captured Steam window after passing through another
    // window. Acknowledge that return independently of permission to move focus.
    private static bool IsReturnedToSteam(Session session, CancellationToken token) => !token.IsCancellationRequested
        && session.Game.Exited && OverlayWindowTools.GetForegroundWindowHandle() == session.Steam.Handle
        && WindowsQamFocusAdapter.IsWindowOwnerCurrent(session.Steam);

    private static bool CanReturn(Session session, CancellationToken token) => !token.IsCancellationRequested
        && WindowsQamFocusAdapter.IsWindowOwnerCurrent(session.Steam)
        && session.Policy.AllowsReturn(Classify(session, OverlayWindowTools.GetForegroundWindowHandle()), session.Game.Exited);

    private static ReturnForeground Classify(Session session, nint hwnd)
    {
        if (!OverlayWindowTools.HasVisibleWindow(hwnd)) return ReturnForeground.Transition;
        if (hwnd == session.Steam.Handle || WindowsQamFocusAdapter.BelongsToSteam(hwnd, session.SteamRoot)) return ReturnForeground.Steam;
        bool isSource = hwnd == session.Source.Handle && WindowsQamFocusAdapter.IsWindowOwnerCurrent(session.Source);
        if (session.HostedAumid is { } aumid)
        {
            if (isSource && UwpFrameWindow.Matches(hwnd, aumid))
                return session.Game.Alive ? ReturnForeground.Game : ReturnForeground.Transition;
        }
        else
        {
            if (session.Game.Alive && OverlayWindowTools.WindowOwnerProcessId(hwnd) == session.Source.GamePid) return ReturnForeground.Game;
            if (isSource && session.Game.Exited) return ReturnForeground.Transition;
        }
        var name = new StringBuilder(128);
        GetClassName(hwnd, name, name.Capacity);
        CompanionLaunchIdentity? launch = session.Uwp is { } uwp
            ? new(uwp.WrapperPid, uwp.WrapperBirth, uwp.GamePid, uwp.GameBirth)
            : session.Win32Launch;
        if (launch is { } identity && identity.MatchesGame(session.Source.GamePid, session.Source.GameBirth)
            && name.ToString() == "PlayhubSteamCompanion")
        {
            // Keep the observed window's owner and birth identity. The launcher
            // can exit before its companion has finished dismissing the overlay.
            if (session.Companion is { } retained && retained.Window.Handle == hwnd
                && WindowsQamFocusAdapter.IsWindowOwnerCurrent(retained.Window)) return ReturnForeground.Companion;
            if (ReadCompanion(hwnd, identity) is { } observed)
            {
                session.Companion = new(observed);
                return ReturnForeground.Companion;
            }
        }
        int shellOwner = OverlayWindowTools.WindowOwnerProcessId(GetShellWindow());
        return BigPictureReturnPolicy.IsShellTransition(name.ToString(), OverlayWindowTools.WindowOwnerProcessId(hwnd), shellOwner)
            ? ReturnForeground.Transition : ReturnForeground.Foreign;
    }

    private static QamWindowIdentity? ReadCompanion(nint window, CompanionLaunchIdentity launch)
    {
        int pid = OverlayWindowTools.WindowOwnerProcessId(window);
        if (pid == 0 || DeckyProcessTree.Read(pid) is not { } owner || owner.ParentPid != launch.WrapperPid
            || DeckyProcessTree.Read(owner.ParentPid) is not { } wrapper
            || wrapper.Birth != launch.WrapperBirth || owner.Birth < wrapper.Birth || owner.Session != wrapper.Session
            || !string.Equals(owner.Sid, wrapper.Sid, StringComparison.Ordinal)) return null;
        if (UwpSteamSessionAssociation.ReadInstalledImages() is not { } images
            || !owner.Path.Equals(images.Companion, StringComparison.OrdinalIgnoreCase)
            || !wrapper.Path.Equals(images.Wrapper, StringComparison.OrdinalIgnoreCase)) return null;
        return WindowsQamFocusAdapter.Capture(window, 0) is { } candidate && candidate.OwnerPid == pid
            && candidate.OwnerBirth == owner.Birth ? candidate : null;
    }

    // This synchronous path runs only while arming an authenticated exact game
    // foreground. The existing native process facts and Steam tracking suffice
    // to bind the parent; no pipe waits occur in the WinEvent callback.
    private static CompanionLaunchIdentity? ReadWin32Launch(QamWindowIdentity source, uint appId, string root)
    {
        try
        {
            if (appId < 0x80000000 || source.GamePid == 0 || source.GameBirth <= 0
                || source.OwnerPid != source.GamePid || source.OwnerBirth != source.GameBirth
                || DeckyProcessTree.Read(checked((int)source.GamePid)) is not { } game
                || game.Birth != source.GameBirth || DeckyProcessTree.Read(game.ParentPid) is not { } wrapper
                || wrapper.Birth > game.Birth || wrapper.Session != game.Session
                || !string.Equals(wrapper.Sid, game.Sid, StringComparison.Ordinal)
                || UwpSteamSessionAssociation.ReadInstalledImages() is not { } images
                || !wrapper.Path.Equals(images.Wrapper, StringComparison.OrdinalIgnoreCase)) return null;
            string text = ReadTrackingLog(root);
            var matches = Win32SteamSessionAssociation.Parse(text, appId).Where(tracked => tracked.Pid == wrapper.Pid
                && Math.Abs((DateTime.FromFileTimeUtc(wrapper.Birth).ToLocalTime() - tracked.Started).TotalSeconds) <= 2).ToArray();
            if (matches.Length != 1 || !WindowsQamFocusAdapter.IsWindowOwnerCurrent(source)) return null;
            var confirmedGame = DeckyProcessTree.Read(game.Pid);
            var confirmedWrapper = DeckyProcessTree.Read(wrapper.Pid);
            return confirmedGame == game && confirmedWrapper == wrapper
                ? new((uint)wrapper.Pid, wrapper.Birth, source.GamePid, source.GameBirth) : null;
        }
        catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException
            or UnauthorizedAccessException or System.ComponentModel.Win32Exception or OverflowException) { return null; }
    }

    private static string ReadTrackingLog(string root)
    {
        using var input = new FileStream(Path.Combine(root, "logs", "gameprocess_log.txt"), FileMode.Open,
            FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        input.Position = Math.Max(0, input.Length - 256 * 1024);
        using var reader = new StreamReader(input);
        return reader.ReadToEnd();
    }

    private static bool HasOtherWin32Launch(uint appId, CompanionLaunchIdentity previous)
    {
        if (SteamInstallationPath.FindRoot() is not { } root
            || UwpSteamSessionAssociation.ReadInstalledImages() is not { } images) return true;
        foreach (var tracked in Win32SteamSessionAssociation.Parse(ReadTrackingLog(root), appId))
        {
            if (DeckyProcessTree.Read(checked((int)tracked.Pid)) is not { } wrapper
                || !wrapper.Path.Equals(images.Wrapper, StringComparison.OrdinalIgnoreCase)
                || Math.Abs((DateTime.FromFileTimeUtc(wrapper.Birth).ToLocalTime() - tracked.Started).TotalSeconds) > 2) continue;
            if (tracked.Pid != previous.WrapperPid || wrapper.Birth != previous.WrapperBirth) return true;
        }
        return false;
    }

    internal void Cancel()
    {
        lock (_sync) { _session?.Stop.Cancel(); _session = null; _confirmedReturn = null; }
    }

    internal ForegroundProof? ConfirmedForeground(int version)
    {
        ConfirmedReturn confirmed;
        lock (_sync)
        {
            if (_confirmedReturn is not { } proof || !IsCurrentConfirmation(proof, version)) return null;
            if ((!proof.PackagedGame && proof.Win32Launch is null) || proof.Proof.AppId == 0) return proof.Proof;
            confirmed = proof;
        }
        // A shortcut can already be launching again while its Steam window
        // remains foreground. Check the exact live association outside the lock.
        bool relaunched;
        try { relaunched = confirmed.PackagedGame ? UwpSteamSessionAssociation.FindForApp(confirmed.Proof.AppId).Count != 0
            : confirmed.Win32Launch is { } previous && HasOtherWin32Launch(confirmed.Proof.AppId, previous); }
        catch (Exception error) when (error is IOException or InvalidOperationException
            or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { return null; }
        lock (_sync)
        {
            if (!ReferenceEquals(_confirmedReturn, confirmed)) return null;
            if (relaunched) { _confirmedReturn = null; return null; }
            return IsCurrentConfirmation(confirmed, version) ? confirmed.Proof : null;
        }
    }

    private bool IsCurrentConfirmation(ConfirmedReturn confirmed, int version) => BigPictureReturnPolicy.IsConfirmed(
        version, _version, Environment.TickCount64, confirmed.ExpiresAt,
        OverlayWindowTools.GetForegroundWindowHandle() == confirmed.Window.Handle,
        WindowsQamFocusAdapter.IsWindowOwnerCurrent(confirmed.Window));

    public void Dispose()
    {
        lock (_sync) { _disposed = true; Cancel(); }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern nint GetShellWindow();
}

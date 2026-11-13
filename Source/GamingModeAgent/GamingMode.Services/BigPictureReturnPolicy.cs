using System.Threading;

namespace GamingMode.Services;

internal enum ReturnForeground { Transition, Game, Companion, Steam, Foreign }

internal sealed record CompanionLaunchIdentity(uint WrapperPid, long WrapperBirth, uint GamePid, long GameBirth)
{
    internal bool MatchesGame(uint pid, long birth) => WrapperPid != 0 && WrapperBirth > 0
        && GamePid != 0 && GamePid != WrapperPid && GameBirth >= WrapperBirth
        && GamePid == pid && GameBirth == birth;
}

internal sealed class BigPictureReturnPolicy
{
    // UWP closure may foreground Explorer's application manager instead of the
    // desktop. It is a shell transition, not evidence the user chose another app.
    internal static bool IsShellTransition(string className, int ownerPid, int shellOwnerPid) =>
        shellOwnerPid > 0 && ownerPid == shellOwnerPid && className is
            "Progman" or "WorkerW" or "Shell_TrayWnd" or "ApplicationManager_ImmersiveShellWindow";

    internal static bool IsConfirmed(int requestedVersion, int currentVersion, long now, long expiresAt,
        bool foregroundMatches, bool ownerCurrent) => requestedVersion > 0 && requestedVersion == currentVersion
        && now < expiresAt && foregroundMatches && ownerCurrent;

    private int _leftGame;
    private int _usedCompanion;
    internal bool UsedCompanion => Volatile.Read(ref _usedCompanion) != 0;

    internal void Observe(ReturnForeground foreground, bool gameAlive)
    {
        if (gameAlive && foreground == ReturnForeground.Companion) Interlocked.Exchange(ref _usedCompanion, 1);
        if (foreground == ReturnForeground.Foreign) Interlocked.Exchange(ref _leftGame, 1);
        else if (gameAlive && foreground is ReturnForeground.Game or ReturnForeground.Companion) Interlocked.Exchange(ref _leftGame, 0);
    }

    internal bool AllowsReturn(ReturnForeground foreground, bool gameExited) => gameExited
        && Volatile.Read(ref _leftGame) == 0 && foreground is ReturnForeground.Transition or ReturnForeground.Companion or ReturnForeground.Steam;
}

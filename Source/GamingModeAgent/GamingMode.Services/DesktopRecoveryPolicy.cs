using System;

namespace GamingMode.Services;

// Two observations avoid treating Steam's normal bootstrap/restart gap as exit.
internal sealed class DesktopRecoveryPolicy
{
    private int missing;
    internal void Reset() => missing = 0;

    internal bool ShouldRestore(Func<bool> shellReady, Func<bool> isGaming,
        Func<bool> steamRunning, Func<bool> shuttingDown)
    {
        if (shellReady())
        {
            Reset();
            return false;
        }

        bool gaming = isGaming();
        bool steamPresent = gaming && steamRunning();
        // The shell or Windows shutdown may have started during the slower probes.
        return ShouldRestore(gaming, steamPresent, shellReady(), shuttingDown());
    }

    internal bool ShouldRestore(bool gaming, bool steamRunning, bool shellReady, bool shuttingDown)
    {
        if (shuttingDown || shellReady || (gaming && steamRunning))
        {
            Reset();
            return false;
        }
        if (++missing < 2) return false;
        Reset();
        return true;
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;

namespace GamingMode.Services;

// Two observations avoid treating Steam's normal bootstrap/restart gap as exit.
internal sealed class DesktopRecoveryPolicy
{
    private int missing;
    internal void Reset() => missing = 0;

    // Never queue a recovery decision behind a mode transition. Eligibility
    // and the synchronous start of the existing Desktop apply share its gate.
    internal Task? TryRestoreCurrent(object applyGate, Func<bool> shellReady,
        Func<bool> isGaming, Func<bool> steamRunning, Func<bool> shuttingDown,
        Func<Task> restore)
    {
        if (!Monitor.TryEnter(applyGate)) { Reset(); return null; }
        try
        {
            if (!isGaming()) { Reset(); return null; }
            if (!ShouldRestore(shellReady, isGaming, steamRunning, shuttingDown)) return null;
            // Close changes during the slower shell/process observations. No
            // stale proposal can override an already committed mode or Steam.
            if (!isGaming() || shellReady() || steamRunning() || shuttingDown())
            { Reset(); return null; }
            return restore();
        }
        finally { Monitor.Exit(applyGate); }
    }

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

namespace GamingMode.Services;

// Two observations avoid treating Steam's normal bootstrap/restart gap as exit.
internal sealed class DesktopRecoveryPolicy
{
    private int missing;
    internal void Reset() => missing = 0;
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

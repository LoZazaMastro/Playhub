using System;

namespace GamingMode.Services;

internal enum ExplorerShellReadyResult { Ready, Cancelled, TimedOut }

internal static class ExplorerShellReadiness
{
    internal static ExplorerShellReadyResult Ensure(Func<bool> isReady, Action start,
        Func<bool> canContinue, Action<int> wait, Func<long> clock, int timeoutMs = 12000)
    {
        long began = clock();
        bool launched = false;
        for (int polls = 0; polls <= 120; polls++)
        {
            if (!canContinue()) return ExplorerShellReadyResult.Cancelled;
            long elapsed = clock() - began;
            if (elapsed < 0) return ExplorerShellReadyResult.TimedOut;
            if (isReady()) return canContinue() ? ExplorerShellReadyResult.Ready : ExplorerShellReadyResult.Cancelled;
            if (elapsed >= timeoutMs) return ExplorerShellReadyResult.TimedOut;
            if (!launched) { start(); launched = true; }
            wait((int)Math.Min(100, timeoutMs - elapsed));
        }
        return ExplorerShellReadyResult.TimedOut;
    }
}

namespace GamingMode.Services;

// A delivered desktop exit can briefly leave no foreground window, or expose
// the Windows desktop, before the following explicit Big Picture request.
internal sealed class SteamThemeForegroundTransition(bool settlingDeliveredExit, uint? requestInputTick)
{
    private bool _seenMain, _cancelled;
    internal string? CancellationReason { get; private set; }
    internal bool Allows(bool originalForeground, bool verifiedSteamForeground, bool exactMainForeground,
        bool neutralDesktopForeground, uint? currentInputTick)
    {
        if (_cancelled) return false;
        if (exactMainForeground) _seenMain = true;
        bool neutralTransition = settlingDeliveredExit && neutralDesktopForeground;
        bool allowed = exactMainForeground || (!_seenMain && (neutralTransition
            ? requestInputTick.HasValue && currentInputTick == requestInputTick
            : originalForeground || verifiedSteamForeground));
        if (!allowed)
        {
            _cancelled = true;
            CancellationReason = _seenMain ? "left-confirmed-main"
                : !neutralDesktopForeground ? "foreign-foreground"
                : !settlingDeliveredExit ? "neutral-without-pending-exit"
                : !requestInputTick.HasValue || !currentInputTick.HasValue ? "input-proof-unavailable"
                : "new-desktop-input";
        }
        return allowed;
    }
}

namespace GamingMode.Services;

// Cold login or a delivered desktop exit can briefly expose the real Windows
// desktop before Big Picture. Only unchanged input admits that startup transition.
internal sealed class SteamThemeForegroundTransition(bool settlingDeliveredExit, uint? requestInputTick, bool coldBoot = false)
{
    private bool _seenMain, _cancelled;
    internal string? CancellationReason { get; private set; }
    internal bool Allows(bool originalForeground, bool verifiedSteamForeground, bool exactMainForeground,
        bool neutralDesktopForeground, uint? currentInputTick)
    {
        if (_cancelled) return false;
        if (exactMainForeground) _seenMain = true;
        bool neutralTransition = (settlingDeliveredExit || coldBoot) && neutralDesktopForeground;
        bool allowed = exactMainForeground || (!_seenMain && (neutralTransition
            ? requestInputTick.HasValue && currentInputTick == requestInputTick
            : originalForeground || verifiedSteamForeground));
        if (!allowed)
        {
            _cancelled = true;
            CancellationReason = _seenMain ? "left-confirmed-main"
                : !neutralDesktopForeground ? "foreign-foreground"
                : !settlingDeliveredExit && !coldBoot ? "neutral-without-pending-exit"
                : !requestInputTick.HasValue || !currentInputTick.HasValue ? "input-proof-unavailable"
                : "new-desktop-input";
        }
        return allowed;
    }
}

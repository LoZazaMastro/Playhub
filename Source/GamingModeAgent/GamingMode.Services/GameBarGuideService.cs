namespace GamingMode.Services;

internal sealed record GameBarGuideResult(bool Ok, bool Sent, string Reason);
internal sealed class GameBarGuideService(Func<uint, GameBarGuideResult> toggleVerified, Func<long> clock)
{
    private readonly object _gate = new();
    private string _held = "", _last = "";
    private long _heldAt, _lastSent = long.MinValue;
    internal GameBarGuideResult Handle(string id, uint appId, bool pressed)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 160) return new(false, false, "invalidRequest");
        lock (_gate)
        {
            long now = clock();
            if (!pressed)
            {
                if (_held == id) _held = "";
                return new(true, false, "released");
            }
            if (_held.Length > 0 && now - _heldAt >= 2000) _held = "";
            if (_held.Length > 0 || id == _last) return new(true, false, "duplicate");
            if (_lastSent != long.MinValue && now - _lastSent < 220) return new(true, false, "debounced");
            if (appId == 0) return new(false, false, "unverifiedForeground");
            var result = toggleVerified(appId);
            if (!result.Ok) return result;
            _held = _last = id; _heldAt = now;
            if (result.Sent) _lastSent = now;
            return result;
        }
    }
}

// An interactive Game Bar can be GPU-composed without a visible GameBar HWND.
// Pinned widgets alone must not suppress opening the actual bar.
internal static class GameBarInteractionGuard
{
    internal static GameBarGuideResult Run(bool verifiedSession, Func<bool> foregroundMatches,
        Func<bool> inputRedirected, Func<GameBarGuideResult> send)
    {
        if (!verifiedSession) return new(false, false, "unverifiedSession");
        bool? ReadState() { try { return inputRedirected(); } catch { return null; } }
        bool? state = ReadState();
        if (state is null) return new(false, false, "gameBarStateUnavailable");
        if (state.Value) return new(true, false, "alreadyInteractive");
        if (!foregroundMatches()) return new(false, false, "unverifiedForeground");
        state = ReadState(); // Cover OS auto-opening between the initial query and our chord.
        if (state is null) return new(false, false, "gameBarStateUnavailable");
        if (state.Value) return new(true, false, "alreadyInteractive");
        return send();
    }
}

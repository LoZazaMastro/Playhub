using System;
using System.Collections.Generic;

namespace GamingMode.Services;

// This policy only moves our HUD. The foreground proof includes the retained
// game/host process identity; a z-order sample alone never authorizes a move.
internal sealed class LivePerformanceHudStacking(ILivePerformanceHudStacking native)
{
    internal const int CheckIntervalMs = 250, MaximumPredecessors = 256;
    internal const uint PlacementFlags = 0x1 | 0x2 | 0x10 | 0x200; // NOSIZE | NOMOVE | NOACTIVATE | NOOWNERZORDER
    private long _sequence = -1, _checkedAt;
    private bool _checked;

    internal bool TryRepair(nint hud, long sequence, bool fresh, long now, Func<bool> exactForeground)
    {
        if (!fresh || sequence <= 0 || sequence == _sequence) return false;
        _sequence = sequence;
        if (_checked && now >= _checkedAt && now - _checkedAt < CheckIntervalMs) return false;
        _checked = true; _checkedAt = now;
        if (hud == 0 || !native.Visible(hud) || !exactForeground()) return false;
        nint game = native.Foreground();
        if (game == 0 || game == hud || !native.Visible(game)) return false;

        // A game may promote its existing topmost window after our initial
        // Show. Repair only when that exact foreground window is above us.
        // The bound/cycle guard also handles windows disappearing mid-walk.
        var visited = new HashSet<nint> { hud };
        nint previous = hud;
        bool gameAbove = false;
        for (int i = 0; i < MaximumPredecessors; i++) {
            previous = native.Previous(previous);
            if (previous == 0 || !visited.Add(previous)) break;
            if (previous == game) { gameAbove = true; break; }
        }
        if (!gameAbove) return false;
        nint anchor = native.Previous(game);
        if (anchor == game || anchor == hud) return false;

        // Insert immediately above the game, BELOW every existing popup above
        // it. Reasserting HWND_TOPMOST unconditionally would cover Steam's QAM.
        // Validate again after any process proof work, adjacent to the write.
        if (!exactForeground() || native.Foreground() != game || !native.Visible(hud)
            || !native.Visible(game) || native.Previous(game) != anchor) return false;
        return native.PlaceAfter(hud, anchor == 0 ? new nint(-1) : anchor, PlacementFlags);
    }

    internal void Reset() { _sequence = -1; _checkedAt = 0; _checked = false; }
}

internal interface ILivePerformanceHudStacking
{
    nint Foreground();
    nint Previous(nint window);
    bool Visible(nint window);
    bool PlaceAfter(nint hud, nint anchor, uint flags);
}

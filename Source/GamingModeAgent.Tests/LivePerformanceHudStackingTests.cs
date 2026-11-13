using GamingMode.Services;

internal static class LivePerformanceHudStackingTests
{
    internal static void Verify()
    {
        int passed = 0;
        void Check(bool condition, string label) { if (!condition) throw new Exception(label); passed++; }
        var native = new Windows(10, 20, 30, 40) { Game = 20 };
        var repair = new LivePerformanceHudStacking(native);
        Check(repair.TryRepair(30, 1, true, 1000, () => true), "A promoted foreground game lifts the HUD once");
        Check(native.Order.SequenceEqual(new nint[] { 10, 30, 20, 40 }), "Existing popup 10 remains above HUD and game");
        Check(native.Calls is [{ Hud: 30, Anchor: 10, Flags: 0x213 }], "Only our HUD is moved without size, move, activation or owner reordering");
        for (int i = 2; i < 100; i++) repair.TryRepair(30, i, true, 1000 + i * 20, () => true);
        Check(native.Calls.Count == 1, "Steady fresh 20ms samples never repeatedly reassert topmost");

        native.Order = [10, 20, 30, 40];
        native.Calls.Clear();
        repair.Reset();
        Check(repair.TryRepair(30, 1, true, 5000, () => true), "A later promotion of the same HWND is repaired");
        native.Order = [10, 20, 30, 40];
        Check(!repair.TryRepair(30, 2, true, 5249, () => true) && native.Calls.Count == 1, "Repeated game promotion is bounded to four checks/second");
        Check(repair.TryRepair(30, 3, true, 5250, () => true) && native.Calls.Count == 2, "A genuinely newer sample repairs after the budget interval");
        native.Order = [10, 20, 30, 40];
        Check(!repair.TryRepair(30, 3, true, 5600, () => true) && native.Calls.Count == 2, "Rerendering the same telemetry never repairs stacking");

        foreach (bool fresh in new[] { false, true }) {
            var blocked = new Windows(20, 30) { Game = 20 };
            var policy = new LivePerformanceHudStacking(blocked);
            Check(!policy.TryRepair(30, fresh ? 0 : 1, fresh, 0, () => true) && blocked.Reads == 0,
                "NoData, stale frames and missing sequences do not inspect/reorder windows");
        }
        var top = new Windows(20, 30) { Game = 20 };
        Check(new LivePerformanceHudStacking(top).TryRepair(30, 1, true, 0, () => true)
            && top.Calls is [{ Anchor: -1 }] && top.Order.SequenceEqual(new nint[] { 30, 20 }),
            "HWND_TOPMOST is used only when there is no preceding popup to preserve");
        var foreign = new Windows(20, 30) { Game = 40 };
        Check(!new LivePerformanceHudStacking(foreign).TryRepair(30, 1, true, 0, () => false)
            && foreign.Calls.Count == 0, "Alt-Tab or an unrelated foreground cannot promote the HUD");
        var hidden = new Windows(20, 30) { Game = 20, Hidden = 30 };
        Check(!new LivePerformanceHudStacking(hidden).TryRepair(30, 1, true, 0, () => true)
            && hidden.Calls.Count == 0, "Suspended/hidden HUD cannot reappear through stacking repair");
        var hiddenGame = new Windows(20, 30) { Game = 20, Hidden = 20 };
        Check(!new LivePerformanceHudStacking(hiddenGame).TryRepair(30, 1, true, 0, () => true)
            && hiddenGame.Calls.Count == 0, "A hidden game cannot authorize stacking");

        var lost = new Windows(10, 20, 30) { Game = 20 };
        int proofs = 0;
        bool ChangedDuringProof() { if (++proofs == 2) lost.Game = 40; return true; }
        Check(!new LivePerformanceHudStacking(lost).TryRepair(30, 1, true, 0, ChangedDuringProof)
            && lost.Calls.Count == 0, "Foreground is re-read after slow process proof immediately before placing");
        var changed = new Windows(10, 20, 30) { Game = 20 };
        proofs = 0;
        bool PopupDuringProof() { if (++proofs == 2) changed.Order.Insert(1, 50); return true; }
        Check(!new LivePerformanceHudStacking(changed).TryRepair(30, 1, true, 0, PopupDuringProof)
            && changed.Calls.Count == 0, "A newly inserted popup invalidates the captured insertion anchor");
        var denied = new Windows(10, 20, 30) { Game = 20 };
        proofs = 0;
        Check(!new LivePerformanceHudStacking(denied).TryRepair(30, 1, true, 0, () => ++proofs == 1)
            && denied.Calls.Count == 0, "Revoked identity or QAM suspension prevents placement");

        var cyclic = new Windows(20, 30) { Game = 20, PreviousOverride = _ => 40 };
        Check(!new LivePerformanceHudStacking(cyclic).TryRepair(30, 1, true, 0, () => true)
            && cyclic.Reads <= 2 && cyclic.Calls.Count == 0, "A changing/cyclic native window chain terminates without writing");
        var longChain = new Windows(20, 30) { Game = 20, PreviousOverride = value => value + 1 };
        Check(!new LivePerformanceHudStacking(longChain).TryRepair(30, 1, true, 0, () => true)
            && longChain.Reads == LivePerformanceHudStacking.MaximumPredecessors,
            "Native traversal has a strict finite upper bound");
        var failure = new Windows(20, 30) { Game = 20, PlacementSucceeds = false };
        Check(!new LivePerformanceHudStacking(failure).TryRepair(30, 1, true, 0, () => true)
            && failure.Calls.Count == 1, "A refused native placement is not reported as repaired");
        Console.WriteLine($"HUD stacking: {passed} checks passed");
    }

    private sealed class Windows(params nint[] windows) : ILivePerformanceHudStacking
    {
        internal List<nint> Order = [.. windows];
        internal nint Game, Hidden;
        internal int Reads;
        internal bool PlacementSucceeds = true;
        internal Func<nint, nint>? PreviousOverride;
        internal readonly List<(nint Hud, nint Anchor, uint Flags)> Calls = [];
        public nint Foreground() => Game;
        public bool Visible(nint window) => window != 0 && window != Hidden;
        public nint Previous(nint window) {
            Reads++;
            if (PreviousOverride is not null) return PreviousOverride(window);
            int index = Order.IndexOf(window);
            return index <= 0 ? 0 : Order[index - 1];
        }
        public bool PlaceAfter(nint hud, nint anchor, uint flags) {
            Calls.Add((hud, anchor, flags));
            if (!PlacementSucceeds) return false;
            Order.Remove(hud);
            Order.Insert(anchor == -1 ? 0 : Order.IndexOf(anchor) + 1, hud);
            return true;
        }
    }
}

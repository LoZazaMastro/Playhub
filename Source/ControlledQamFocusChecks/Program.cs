using GamingMode.Services;

int passed = 0;
void Check(string name, Action<FakeWindows, ControlledQamFocusService> test)
{
    var windows = new FakeWindows();
    using var service = new ControlledQamFocusService(windows, true);
    test(windows, service);
    passed++;
    Console.WriteLine("PASS " + name);
}
static void Expect(bool value) { if (!value) throw new Exception("Assertion failed"); }
Check("one chosen Steam layer and exact game restore", (w,s) => {
    Expect(s.Acquire("a").Ok && w.Foreground == w.Steam.Handle);
    Expect(w.Layers.SequenceEqual(new[] {(w.Steam.Handle, true)}));
    Expect(s.Close("a").Ok && w.Foreground == w.Game.Handle);
    Expect(w.Activations.SequenceEqual(new[] {w.Steam.Handle, w.Game.Handle}));
    Expect(w.Layers.Last() == (w.Steam.Handle, false));
});
Check("already topmost Steam retains original layer", (w,s) => {
    w.Topmost = true; Expect(s.Acquire("a").Ok); s.Close("a");
    Expect(w.Layers.Last() == (w.Steam.Handle, true));
});
Check("activation request without actual foreground fails", (w,s) => {
    w.RefuseActivation = true; Expect(!s.Acquire("a").Ok);
    Expect(w.Foreground == w.Game.Handle && w.Layers.Last() == (w.Steam.Handle, false));
});
Check("duplicate acquire and duplicate close do not steal focus", (w,s) => {
    Expect(s.Acquire("a").Ok && s.Acquire("a").Ok && !s.Acquire("b").Ok);
    Expect(w.Activations.Count == 1); s.Close("a"); s.Close("a"); Expect(w.Activations.Count == 2);
});
Check("close before delayed acquire cancels it", (w,s) => { s.Close("a"); Expect(!s.Acquire("a").Ok && w.Activations.Count == 0); });
Check("release before delayed acquire cancels it", (w,s) => { s.Release("a"); Expect(!s.Acquire("a").Ok && w.Activations.Count == 0); });
Check("Desktop invalidates generation before mode state changes", (w,s) => {
    Expect(s.Acquire("a").Ok); s.ModeChanging(); Expect(!s.Acquire("b").Ok); s.ModeCompleted(true);
    Expect(!s.Close("a").Ok && w.Activations.Count == 1 && !s.Acquire("a").Ok);
});
Check("application selection releases without restoring old game", (w,s) => {
    Expect(s.Acquire("a").Ok); w.Foreground = 777; s.WindowChanged(); s.Close("a");
    Expect(w.Foreground == 777 && w.Activations.Count == 1 && !w.Topmost);
});
Check("same HWND with recycled owner birth does not receive writes", (w,s) => {
    Expect(s.Acquire("a").Ok); w.Steam = w.Steam with {OwnerBirth = 999}; s.Close("a");
    Expect(w.Layers.Count == 1 && w.Activations.Count == 1);
});
Check("UWP frame surviving hosted game exit is not restored", (w,s) => {
    Expect(s.Acquire("a").Ok); w.Game = w.Game with {GameBirth = 999}; s.Close("a");
    Expect(w.Activations.Count == 1 && !w.Topmost);
});
Check("returning directly to game releases Steam layer without activation", (w,s) => {
    Expect(s.Acquire("a").Ok); w.Foreground = w.Game.Handle; s.WindowChanged(); s.Close("a");
    Expect(w.Foreground == w.Game.Handle && w.Activations.Count == 1 && !w.Topmost);
});
Check("foreign Steam layer write is preserved on release", (w,s) => {
    w.Topmost = true; Expect(s.Acquire("a").Ok); w.Topmost = false; s.Release("a");
    Expect(w.Layers.Count == 1 && !w.Topmost);
});
Check("missing source never changes Steam layer", (w,s) => {
    w.NoGame = true; Expect(!s.Acquire("a").Ok && w.Layers.Count == 0 && w.Activations.Count == 0);
});
Check("idle events perform no window queries", (w,s) => {
    for (int i=0; i<1000; i++) s.WindowChanged(); Expect(w.Validations == 0 && w.Activations.Count == 0 && w.Layers.Count == 0);
});
Check("normal UWP delegates to Game Bar without Steam focus or layer writes", (w,s) => {
    w.NormalUwp = true; w.SteamTree = false;
    Expect(!s.Acquire("bar", 42).Ok && w.Layers.Count == 0 && w.Activations.Count == 0);
    Expect(w.Foreground == w.Game.Handle);
});
Check("verified normal UWP wins even if present in Steam tree", (w,s) => {
    w.NormalUwp = true;
    Expect(!s.Acquire("bar", 42).Ok && w.Layers.Count == 0 && w.Activations.Count == 0);
});
Check("ordinary native-tree fallback remains available", (w,s) => {
    Expect(s.Acquire("native", 42).Ok && w.Foreground == w.Steam.Handle);
    Expect(s.Close("native").Ok && w.Foreground == w.Game.Handle);
});
Check("unverified non-tree window remains rejected", (w,s) => {
    w.SteamTree = false;
    Expect(!s.Acquire("foreign", 42).Ok && w.Layers.Count == 0 && w.Activations.Count == 0);
});
Console.WriteLine($"Controlled QAM focus: {passed} checks passed.");

sealed class FakeWindows : IQamFocusWindows
{
    public QamWindowIdentity Game = new(10, 11, 100, 12, 101), Steam = new(20, 21, 200, 0, 0);
    public nint Foreground = 10;
    public bool Topmost, RefuseActivation, NoGame, NormalUwp;
    public bool SteamTree = true;
    public int Validations;
    public List<(nint,bool)> Layers = new();
    public List<nint> Activations = new();
    public QamWindowIdentity? CaptureGame(uint appId) => NoGame || !ControlledQamFocusService.AllowsAutomaticGame(SteamTree, NormalUwp) ? null : Game;
    public QamWindowIdentity? CaptureSteam() => Steam;
    public bool IsValid(QamWindowIdentity identity) { Validations++; return identity == Game || identity == Steam; }
    public bool IsSteamForeground(QamWindowIdentity identity) => identity == Steam && Foreground == Steam.Handle;
    public bool IsSourceForeground(QamWindowIdentity identity) => identity == Game && Foreground == Game.Handle;
    public bool IsTopmost(QamWindowIdentity identity) => Topmost;
    public bool SetTopmost(QamWindowIdentity identity, bool topmost) { Layers.Add((identity.Handle, topmost)); Topmost = topmost; return true; }
    public bool Activate(QamWindowIdentity identity) { Activations.Add(identity.Handle); if (!RefuseActivation) Foreground = identity.Handle; return true; }
}

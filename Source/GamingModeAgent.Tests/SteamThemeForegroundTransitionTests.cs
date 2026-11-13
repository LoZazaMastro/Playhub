using GamingMode.Services;

internal static class SteamThemeForegroundTransitionTests
{
    internal static void Run()
    {
        var commands = new SteamModeCommandState();
        commands.RequestDesktop(); commands.Read();
        var open = commands.BeginOpen();
        var transition = new SteamThemeForegroundTransition(open.Command?.Kind == "barrier", 100);
        Check(transition.Allows(true, true, false, false, 100), "The original Steam window must remain allowed before its pending exit.");
        Check(transition.Allows(false, false, false, true, 100), "An owned delivered exit may destroy its foreground HWND without cancelling the explicit open.");
        Check(!open.Completed.IsCompleted, "A neutral foreground must not fabricate a desktop acknowledgement.");
        var barrier = commands.Read()!;
        Check(commands.Acknowledge(barrier.Revision, barrier.Token, true), "Only the exact barrier settles the original exit.");
        Check(transition.Allows(false, false, false, true, 100), "A quiet desktop handover remains valid until the requested Steam window exists.");
        Check(transition.Allows(false, true, false, false, 100), "The new verified Steam desktop may precede Big Picture.");
        Check(transition.Allows(false, true, true, false, 100), "The exact verified Big Picture window commits the foreground scope.");
        Check(!transition.Allows(false, false, false, true, 100), "The neutral desktop exception must end after the exact main window appears.");
        Check(!transition.Allows(false, true, true, false, 100), "A cancelled foreground must not reopen the curtain later.");
        var plain = new SteamThemeForegroundTransition(false, 100);
        Check(!plain.Allows(false, false, false, true, 100), "Ordinary startup cannot authorize a neutral window without an owned pending exit.");
        var user = new SteamThemeForegroundTransition(true, 100);
        Check(!user.Allows(false, false, false, true, 101), "New desktop input is a real user cancellation, including a desktop hotkey.");
        Check(!user.Allows(true, true, false, false, 101), "Returning to the original window cannot erase user cancellation.");
        Check(!new SteamThemeForegroundTransition(true, 100).Allows(true, false, false, true, 101),
            "New desktop input also cancels when the shell itself was the original foreground.");
        var foreign = new SteamThemeForegroundTransition(true, 100);
        Check(!foreign.Allows(false, false, false, false, 100), "A foreign application cancels even without observed input.");
        foreach (uint? tick in new uint?[] { null, 0, uint.MaxValue })
            Check(!new SteamThemeForegroundTransition(true, 100).Allows(false, false, false, true, tick),
                "Absent, wrapped or changed input proof cannot authorize a neutral handover.");
        Check(!new SteamThemeForegroundTransition(true, null).Allows(false, false, false, true, null),
            "Unavailable input tracking must fail closed.");
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}

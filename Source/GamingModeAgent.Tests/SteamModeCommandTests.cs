using GamingMode.Services;

internal static class SteamModeCommandTests
{
    internal static void Run()
    {
        var state = new SteamModeCommandState();
        var desktop = state.RequestDesktop();
        var consumed = state.Read()!;
        Check(consumed.Kind == "exit" && state.Read() == consumed, "Polling must retain the same outstanding command.");
        Check(!desktop.Completed.IsCompleted, "A consumed desktop response is not a native exit acknowledgement.");
        var open = state.BeginOpen();
        Check(open.Command?.Kind == "barrier" && !open.Completed.IsCompleted, "An open must wait for an already delivered exit.");
        Check(!state.Acknowledge(consumed.Revision, consumed.Token, true), "A delayed old exit acknowledgement cannot release a newer open.");
        Check(!open.Completed.IsCompleted, "An old response incorrectly completed the open barrier.");
        var barrier = state.Read()!;
        Check(!state.Acknowledge(barrier.Revision, new string('0', 32), true), "A foreign acknowledgement token was accepted.");
        Check(state.Acknowledge(barrier.Revision, barrier.Token, true) && open.Completed.Result, "The exact settled barrier should release the open.");
        Check(state.Read() is null && state.BeginOpen().Completed.Result, "A settled transition should not block a later explicit open.");

        var unissued = new SteamModeCommandState();
        unissued.RequestDesktop();
        Check(unissued.BeginOpen().Completed.Result && unissued.Read() is null, "An exit which was never delivered should be revoked without waiting for a frontend.");

        var failed = new SteamModeCommandState();
        failed.RequestDesktop(); var first = failed.Read()!;
        failed.Acknowledge(first.Revision, first.Token, false);
        var waiting = failed.BeginOpen();
        Check(waiting.Command is not null && !waiting.Completed.IsCompleted, "A failed readback must retain the unsettled exit.");
        var pending = failed.Read()!;
        failed.Acknowledge(pending.Revision, pending.Token, false);
        Check(!waiting.Completed.IsCompleted && failed.Read() == pending,
            "A negative barrier observation retains the same queued open without requiring a second request.");
        Check(failed.Acknowledge(pending.Revision, pending.Token, true) && waiting.Completed.Result,
            "A later native Desktop observation completes the original open waiter.");

        var retry = new SteamModeCommandState();
        var requested = retry.RequestDesktop(); var issued = retry.Read()!;
        Check(retry.Acknowledge(issued.Revision, issued.Token, false) && !requested.Completed.IsCompleted
            && retry.Read() == issued, "Transient native Exit readback never cancels or redispatches the desktop command.");
        var queuedOpen = retry.BeginOpen(); var queuedBarrier = retry.Read()!;
        Check(!retry.Acknowledge(issued.Revision, issued.Token, true) && !queuedOpen.Completed.IsCompleted,
            "An old root finishing Exit cannot acknowledge a newer open barrier.");
        for (int i = 0; i < 3; i++)
            Check(retry.Acknowledge(queuedBarrier.Revision, queuedBarrier.Token, false)
                && retry.Read() == queuedBarrier && !queuedOpen.Completed.IsCompleted,
                "Every in-progress native read keeps the exact open request queued.");
        Check(retry.Acknowledge(queuedBarrier.Revision, queuedBarrier.Token, true) && queuedOpen.Completed.Result,
            "The original queued EnterBP is released once, on real native settlement.");

        var replaced = new SteamModeCommandState();
        var old = replaced.RequestDesktop(); var stale = replaced.Read()!;
        var newer = replaced.RequestDesktop(); var current = replaced.Read()!;
        Check(current.Revision > stale.Revision && current.Token != stale.Token, "New commands require a fresh monotonically ordered identity.");
        Check(old.Completed.IsCompleted && !old.Completed.Result && !newer.Completed.IsCompleted, "Replacement must retire only the old waiter.");
        Check(!replaced.Acknowledge(stale.Revision, stale.Token, true), "A replaced desktop command cannot acknowledge its successor.");

        long now = 100;
        var expired = new SteamModeCommandState(() => now);
        var unused = expired.RequestDesktop(); now += 4001;
        Check(expired.Read() is null && !unused.Completed.Result, "An undelivered desktop request must retain the existing four-second expiry.");
        expired.RequestDesktop(); var alreadyDelivered = expired.Read()!; now += 5000;
        Check(expired.Read() == alreadyDelivered && expired.BeginOpen().Command is not null,
            "An already delivered request must remain ordered even after its dispatch deadline.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

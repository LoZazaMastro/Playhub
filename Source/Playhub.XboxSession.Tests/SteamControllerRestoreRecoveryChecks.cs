// Playhub Xbox Session. Distributed under GPL-3.0-only.
internal static class SteamControllerRestoreRecoveryChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var good = new SteamRawInputRelayTrial.ReaderOutcome(true, false, true, RelayExit.FocusLost);
        foreach (var status in new[] { SteamControllerLayoutTrial.AutomaticRestoreStatus.OtherCommand,
            SteamControllerLayoutTrial.AutomaticRestoreStatus.Failed, SteamControllerLayoutTrial.AutomaticRestoreStatus.Unknown })
        {
            int sends = 0, drains = 0;
            var recovery = new SteamControllerLayoutTrial.AutomaticRestoreRecovery();
            check(!recovery.TryRecover(good, true, () => true, () => status,
                () => { drains++; return SteamControllerLayoutTrial.AutomaticRestoreStatus.Acknowledged; },
                () => { sends++; return new(true, true); }) && sends == 0 && drains == 0,
                $"{status} automatic restore cannot overwrite an unknown or different command");
        }
        foreach (var outcome in new[] { default, good with { Attested = false }, good with { CleanupConfirmed = false }, good with { SourceRemoved = true } })
        {
            int reads = 0;
            check(!new SteamControllerLayoutTrial.AutomaticRestoreRecovery().TryRecover(outcome, true, () => true,
                () => { reads++; return SteamControllerLayoutTrial.AutomaticRestoreStatus.Acknowledged; },
                () => throw new Exception(), () => throw new Exception()) && reads == 0,
                "Unauthenticated EOF, failed detach or a removed source cannot renew the old bridge");
        }
        check(!new SteamControllerLayoutTrial.AutomaticRestoreRecovery().TryRecover(good, false, () => true,
            () => throw new Exception(), () => throw new Exception(), () => throw new Exception()),
            "A reader ending without observed focus loss cannot restart through automatic-restore recovery");
        foreach (RelayExit? reason in new RelayExit?[] { null, RelayExit.SourceUnavailable, RelayExit.SessionEnded,
            RelayExit.OutputObservationUnavailable, RelayExit.SteamShadow, RelayExit.SourceChanged, RelayExit.OutputUnavailable,
            RelayExit.Cancelled, RelayExit.Expired, RelayExit.Faulted })
        {
            check(!new SteamControllerLayoutTrial.AutomaticRestoreRecovery().TryRecover(good with { EndReason = reason },
                true, () => true, () => throw new Exception(), () => throw new Exception(), () => throw new Exception()),
                "An attested EOF/unknown ending remains stopped even when focus loss is simultaneous");
        }
        foreach (var reason in new[] { RelayExit.FocusLost, RelayExit.OverlayOpened })
        {
            check(new SteamControllerLayoutTrial.AutomaticRestoreRecovery().TryRecover(good with { EndReason = reason },
                true, () => true, () => SteamControllerLayoutTrial.AutomaticRestoreStatus.Acknowledged,
                () => throw new Exception(), () => throw new Exception()),
                $"Only explicit clean {reason} may renew the source epoch after automatic acknowledgement");
        }
        int pendingSends = 0, pendingDrains = 0;
        var pending = new SteamControllerLayoutTrial.AutomaticRestoreRecovery();
        check(pending.TryRecover(good, true, () => true, () => SteamControllerLayoutTrial.AutomaticRestoreStatus.Pending,
            () => { pendingDrains++; return SteamControllerLayoutTrial.AutomaticRestoreStatus.Acknowledged; },
            () => { pendingSends++; return new(true, true); }) && pendingDrains == 1 && pendingSends == 0,
            "A slow owned0 is acknowledged once without dispatching a duplicate cleanup");
        check(!pending.TryRecover(good, true, () => true, () => throw new Exception(), () => throw new Exception(), () => throw new Exception()),
            "One failed lease can reset its epoch at most once");
        foreach (bool stillCurrent in new[] { false, true })
        {
            int sends = 0; bool dispatched = false;
            bool current = true;
            bool recovered = new SteamControllerLayoutTrial.AutomaticRestoreRecovery().TryRecover(good, true, () => current,
                () => dispatched ? SteamControllerLayoutTrial.AutomaticRestoreStatus.Acknowledged : SteamControllerLayoutTrial.AutomaticRestoreStatus.Missing,
                () => throw new Exception(), () => { sends++; dispatched = true; current = stillCurrent; return new(true, true); });
            check(recovered == stillCurrent && sends == 1,
                "An unstarted0 gets one ordered submission and must revalidate the retained session afterward");
        }
        foreach (var status in new[] { SteamControllerLayoutTrial.AutomaticRestoreStatus.Unknown, SteamControllerLayoutTrial.AutomaticRestoreStatus.Pending,
            SteamControllerLayoutTrial.AutomaticRestoreStatus.Failed, SteamControllerLayoutTrial.AutomaticRestoreStatus.OtherCommand })
        {
            check(!new SteamControllerLayoutTrial.AutomaticRestoreRecovery().TryRecover(good, true, () => true,
                () => SteamControllerLayoutTrial.AutomaticRestoreStatus.Pending, () => status, () => throw new Exception()),
                "An unacknowledged drain keeps the worker stopped instead of allowing a later0 across a new game layout");
        }
        for (int invalidAt = 1; invalidAt <= 3; invalidAt++)
        {
            int reads = 0, currentReads = 0;
            int captured = invalidAt;
            check(!new SteamControllerLayoutTrial.AutomaticRestoreRecovery().TryRecover(good, true, () => ++currentReads != captured,
                () => { reads++; return SteamControllerLayoutTrial.AutomaticRestoreStatus.Acknowledged; },
                () => throw new Exception(), () => throw new Exception()),
                "Game/source/Steam replacement at any slow observation boundary invalidates recovery");
        }
        check(!new SteamControllerLayoutTrial.AutomaticRestoreRecovery().TryRecover(good, true, () => true,
            () => SteamControllerLayoutTrial.AutomaticRestoreStatus.Missing, () => throw new Exception(), () => default),
            "A vetoed or unstarted cleanup is never treated as restored");
        check(!new SteamControllerLayoutTrial.AutomaticRestoreRecovery().TryRecover(good, true, () => throw new Exception(),
            () => throw new Exception(), () => throw new Exception(), () => throw new Exception()),
            "An unreadable retained proof stops recovery without raising a worker fault");

        long now = 0; int polls = 0, waits = 0;
        var drained = SteamControllerLayoutTrial.DrainAutomaticRestore(
            () => ++polls < 3 ? SteamControllerLayoutTrial.AutomaticRestoreStatus.Pending : SteamControllerLayoutTrial.AutomaticRestoreStatus.Acknowledged,
            () => true, () => now, ms => { waits++; now += ms; });
        check(drained == SteamControllerLayoutTrial.AutomaticRestoreStatus.Acknowledged && waits == 2 && now == 100,
            "The production drain waits only for the exact retained pending launcher and returns its real acknowledgement");
        now = 0; polls = 0;
        drained = SteamControllerLayoutTrial.DrainAutomaticRestore(() => { polls++; return SteamControllerLayoutTrial.AutomaticRestoreStatus.Pending; },
            () => true, () => now, ms => now += ms);
        check(drained == SteamControllerLayoutTrial.AutomaticRestoreStatus.Unknown && now == 1500 && polls <= 32,
            "A never-exiting automatic launcher reaches the1500ms deadline without a resend");
        polls = 0;
        drained = SteamControllerLayoutTrial.DrainAutomaticRestore(() => { polls++; return SteamControllerLayoutTrial.AutomaticRestoreStatus.Pending; },
            () => true, () => 0, _ => { });
        check(drained == SteamControllerLayoutTrial.AutomaticRestoreStatus.Unknown && polls == 32,
            "A stalled provider clock is also bounded by a fixed sample limit");
        now = 0;
        drained = SteamControllerLayoutTrial.DrainAutomaticRestore(() => { now = 1501; return SteamControllerLayoutTrial.AutomaticRestoreStatus.Acknowledged; },
            () => true, () => now, _ => throw new Exception());
        check(drained == SteamControllerLayoutTrial.AutomaticRestoreStatus.Unknown,
            "A slow status getter cannot return a late acknowledgement after the deadline");
        bool alive = true;
        drained = SteamControllerLayoutTrial.DrainAutomaticRestore(() => { alive = false; return SteamControllerLayoutTrial.AutomaticRestoreStatus.Acknowledged; },
            () => alive, () => 0, _ => throw new Exception());
        check(drained == SteamControllerLayoutTrial.AutomaticRestoreStatus.Unknown,
            "Cancellation/game exit after status read vetoes an otherwise valid acknowledgement");
        drained = SteamControllerLayoutTrial.DrainAutomaticRestore(() => SteamControllerLayoutTrial.AutomaticRestoreStatus.Pending,
            () => true, () => 0, _ => throw new Exception());
        check(drained == SteamControllerLayoutTrial.AutomaticRestoreStatus.Unknown,
            "Wait/read failures preserve uncertainty rather than guessing cleanup success");

        var source = new TritonSource(1, 0x1302, 52);
        var epochs = new SteamControllerLayoutTrial.RelayFocusEpoch();
        check(!epochs.ShouldStart(23, source, false, 0x1234) && epochs.ShouldStart(23, source, false, 0x1234),
            "The old bridge starts only after two exact native-source samples");
        long oldEpoch = epochs.Generation;
        var oldLease = new SteamControllerSourceLease(source);
        var ack = new SteamControllerLayoutTrial.AutomaticRestoreRecovery();
        bool authorized = ack.TryRecover(good, true, () => oldLease.Current(new(source, false)),
            () => SteamControllerLayoutTrial.AutomaticRestoreStatus.Acknowledged, () => throw new Exception(), () => throw new Exception());
        if (authorized) { oldLease.Revoke(); epochs.Reset(); }
        var newLease = new SteamControllerSourceLease(source);
        check(authorized && oldLease.Revoked && epochs.Generation == oldEpoch + 1 && newLease.Current(new(source, false))
            && !epochs.ShouldStart(23, source, false, 0x1234) && epochs.ShouldStart(23, source, false, 0x1234)
            && !epochs.ShouldStart(23, source, false, 0x1234),
            "A confirmed0 retires the old lease once and requires two fresh samples for the new epoch, without reviving old reports");

        var attestation = new SteamRawInputRelayTrial.SessionAttestation();
        var binding = new SteamRawInputRelayTrial.SessionBinding(0xA000000102000000, 25, 100, 27, 110,
            source, new object(), 23, 7, 27);
        var originalAuthorization = attestation.Authorize(binding, oldEpoch, true, true);
        var renewedAuthorization = attestation.Authorize(binding, epochs.Generation, false, true);
        check(originalAuthorization == SteamRawInputRelayTrial.ReaderAuthorization.SteamLog
            && renewedAuthorization == SteamRawInputRelayTrial.ReaderAuthorization.RetainedSession
            && attestation.Authorize(binding, oldEpoch, false, true) == SteamRawInputRelayTrial.ReaderAuthorization.None,
            "A renewed epoch reattests the retained same-game/source route and cannot accept the retired reader epoch");
        long tick = 0;
        var output = new Output();
        var buttons = new XboxReport(0x1000, 0, 0, 0, 0, 0, 0);
        var replay = SteamInputRelay.Run(() => new RelayObservation(tick < 1300, true, false, true, false, source,
                tick < 1000 ? default(XboxReport) : buttons), () => output,
            () => tick, delay => tick += (long)delay.TotalMilliseconds, CancellationToken.None);
        check(replay.CleanupConfirmed && output.Attaches == 1 && output.Closes == 1 && output.Reports.Contains(buttons)
            && !oldLease.Current(new(source, false)) && newLease.Current(new(source, false)),
            "After confirmed automatic cleanup the renewed same-source relay forwards fresh game-button reports and neutralizes its output");
    }
    private sealed class Output : IXboxRelayOutput
    {
        internal int Attaches, Closes;
        internal readonly List<XboxReport> Reports = [];
        public bool IsOwned { get; private set; }
        public bool TryConnectNeutral(Func<bool> canAttach) { if (!canAttach()) return false; Attaches++; IsOwned = true; return true; }
        public void Submit(XboxReport report) => Reports.Add(report);
        public bool Close() { Closes++; IsOwned = false; Reports.Add(default); return true; }
    }
}

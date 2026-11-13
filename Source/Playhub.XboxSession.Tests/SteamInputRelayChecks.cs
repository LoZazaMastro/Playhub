internal static class SteamInputRelayChecks
{
    internal static void Run(Action<bool, string> check)
    {
        SteamControllerGateChecks.Run(check);
        var source = new TritonSource(1, 0x1302, 52);
        var changedSource = source with { Handle = 53 };
        var action = new XboxReport(0x1000, 0, 0, 0, 0, 0, 0);
        var secondAction = action with { Buttons = 0x2000 };
        var epochs = new SteamControllerLayoutTrial.RelayFocusEpoch();
        check(!epochs.ShouldStart(23, source) && epochs.ShouldStart(23, source) && !epochs.ShouldStart(23, source)
            && !epochs.ShouldStart(0, null) && !epochs.ShouldStart(23, source) && epochs.ShouldStart(23, source)
            && !epochs.ShouldStart(23, changedSource) && epochs.ShouldStart(23, changedSource),
            "Relay reacquires only a stable exact foreground and controller after overlay or replug");
        check(!epochs.ShouldStart(23, null, sourceUnavailable: true)
            && !epochs.ShouldStart(23, changedSource) && !epochs.ShouldStart(23, changedSource)
            && !epochs.ShouldStart(23, changedSource, sourceUnavailable: true)
            && !epochs.ShouldStart(23, changedSource) && !epochs.ShouldStart(23, changedSource)
            && !epochs.ShouldStart(23, source) && !epochs.ShouldStart(23, null, sourceUnavailable: true)
            && !epochs.ShouldStart(23, source) && epochs.ShouldStart(23, source),
            "A blank or shadowed inventory cannot re-force layout for the same foreground and controller");

        var attestation = new SteamRawInputRelayTrial.SessionAttestation();
        const ulong gameId = 0xA000000102000000;
        var binding = new SteamRawInputRelayTrial.SessionBinding(gameId, 23, 120, 27, 130, source, new object(), 23, 7, 27);
        var noProof = SteamRawInputRelayTrial.ReaderAuthorization.None;
        check(attestation.Authorize(binding, 1, false, true) == noProof
            && attestation.Authorize(binding, 1, true, false) == noProof
            && attestation.Authorize(binding, 1, true, true) == SteamRawInputRelayTrial.ReaderAuthorization.SteamLog,
            "First reader requires both positive Steam PID tracking and fresh exact native input");
        check(attestation.Authorize(binding, 1, false, true) == noProof
            && attestation.Authorize(binding with { GameId = gameId + (1UL << 32) }, 2, false, true) == noProof
            && attestation.Authorize(binding with { CompanionPid = 29 }, 2, false, true) == noProof
            && attestation.Authorize(binding with { CompanionBirth = 121 }, 2, false, true) == noProof
            && attestation.Authorize(binding with { GamePid = 28 }, 2, false, true) == noProof
            && attestation.Authorize(binding with { GameBirth = 131 }, 2, false, true) == noProof
            && attestation.Authorize(binding with { Source = changedSource }, 2, false, true) == noProof
            && attestation.Authorize(binding, 2, false, false) == noProof
            && attestation.Authorize(binding, 2, false, true) == SteamRawInputRelayTrial.ReaderAuthorization.RetainedSession
            && attestation.Authorize(binding, 2, false, true) == noProof,
            "A later reader can renew the same retained session once with fresh native proof and exact process births and controller");

        const string nativeProof = "{\"ready\":true,\"exactSteamSource\":true,\"readerPid\":45,\"sourceSlot\":1,\"sourceProduct\":4866}";
        bool NativeSource(string payload, int pid, TritonSource controller)
        {
            using var json = System.Text.Json.JsonDocument.Parse(payload);
            return SteamRawInputRelayTrial.NativeSourceMatches(json.RootElement, pid, controller);
        }
        check(NativeSource(nativeProof, 45, source) && !NativeSource(nativeProof, 46, source)
            && !NativeSource(nativeProof, 45, source with { Slot = 0 }) && !NativeSource(nativeProof, 45, source with { Product = 0x1304 })
            && !NativeSource(nativeProof.Replace("\"exactSteamSource\":true", "\"exactSteamSource\":false"), 45, source)
            && !NativeSource("{\"ready\":true}", 45, source),
            "Native layout attestation requires an exact child PID, Valve device, slot and source verification");

        var inventory = new SteamRawInputRelayTrial.InventoryWatch(source);
        check(inventory.Observe(new(source, false), 10) == SteamRawInputRelayTrial.InventoryStatus.Current
            && inventory.Observe(default, 20) == SteamRawInputRelayTrial.InventoryStatus.Suspended
            && inventory.Observe(default, 199) == SteamRawInputRelayTrial.InventoryStatus.Suspended
            && inventory.Observe(new(source, false), 200) == SteamRawInputRelayTrial.InventoryStatus.Current
            && inventory.Observe(default, 210) == SteamRawInputRelayTrial.InventoryStatus.Suspended
            && inventory.Observe(default, 390) == SteamRawInputRelayTrial.InventoryStatus.Revoked
            && inventory.Observe(new(changedSource, false), 395) == SteamRawInputRelayTrial.InventoryStatus.Revoked
            && inventory.Observe(new(source, true), 400) == SteamRawInputRelayTrial.InventoryStatus.Revoked,
            "Brief missing inventory suspends forwarding while persistent absence, a different source or shadow revokes the pad");

        check(SteamControllerLayoutTrial.RestoreContext("PlayhubSteamCompanion", true, false, false) == 769
            && SteamControllerLayoutTrial.RestoreContext("PlayhubSteamCompanion", false, true, false) == 0
            && SteamControllerLayoutTrial.RestoreContext("SDL_app", false, true, false) == 769
            && SteamControllerLayoutTrial.RestoreContext("Shell_TrayWnd", false, false, true) == 413080
            && SteamControllerLayoutTrial.RestoreContext("ApplicationManager_ImmersiveShellWindow", false, false, true) == 0,
            "Controller restoration distinguishes the exact companion, Steam, desktop and transient immersive shell");

        const uint game = 0xA0000001;
        uint context = 0;
        bool companionHasFocus = false;
        var transitions = new List<uint>();
        var lease = SteamControllerLayoutTrial.RunLease(game, () => context == 0, (next, guard) =>
        {
            if (guard?.Invoke() == false) return default;
            transitions.Add(next);
            return new(true, true);
        }, () => context, queueExitRestore => { queueExitRestore(); companionHasFocus = true; context = 769; }, () =>
        {
            check(companionHasFocus && transitions.SequenceEqual(new[] { game }),
                "No external Steam command starts while the exact companion opens the overlay");
            companionHasFocus = false;
            context = 0;
        }, companionOwnsInput: () => companionHasFocus);
        check(lease.Restored && transitions.SequenceEqual(new[] { game, 0u }),
            "Controller layout lease restores automatic selection only after companion focus ends");

        transitions.Clear();
        bool racedIntoCompanion = false;
        var reopened = SteamControllerLayoutTrial.RunLease(game, () => true, (next, guard) =>
        {
            if (next == 0 && !racedIntoCompanion) { racedIntoCompanion = true; companionHasFocus = true; }
            if (guard?.Invoke() == false) return default;
            transitions.Add(next);
            return new(true, true);
        }, () => 0, _ => { }, () => companionHasFocus = false, companionOwnsInput: () => companionHasFocus);
        check(reopened.Restored && racedIntoCompanion && transitions.SequenceEqual(new[] { game, 0u }),
            "A companion opening immediately before reset delays the external command until focus is released again");

        bool exited = false, neutral = false, reaped = false;
        transitions.Clear();
        var final = SteamControllerLayoutTrial.RunLease(game, () => !exited, (next, guard) =>
        {
            if (guard?.Invoke() == false) return default;
            transitions.Add(next);
            return new(true, true);
        }, () => throw new InvalidOperationException("An exited game must not probe foreground context."),
            queueExitRestore =>
            {
                neutral = true;
                exited = true;
                queueExitRestore();
                reaped = true;
                queueExitRestore();
            }, gameHasExited: () => exited, queueExitRestore: () =>
            {
                if (!neutral || reaped) throw new InvalidOperationException("Reset must follow neutral cleanup and precede reader reap.");
                transitions.Add(0);
                return new(true, false);
            });
        check(final.Delivered && !final.Restored && final.RestoreQueued && transitions.SequenceEqual(new[] { game, 0u }),
            "An exact game exit queues automatic reset once between neutral relay cleanup and reader reap");

        exited = false;
        transitions.Clear();
        var raced = SteamControllerLayoutTrial.RunLease(game, () => !exited, (next, guard) =>
        {
            if (guard?.Invoke() == false) return default;
            transitions.Add(next);
            return new(true, true);
        }, () => { exited = true; return 769; }, queueExitRestore => queueExitRestore(), gameHasExited: () => exited, queueExitRestore: () =>
        {
            transitions.Add(0);
            return new(true, false);
        });
        check(raced.RestoreQueued && transitions.SequenceEqual(new[] { game, 0u }),
            "Exit during foreground classification vetoes the intermediate menu command");

        long now = 0;
        var output = new Output();
        RelayObservation Observe() => new(true, true, false, true, false, now < 7050 ? source : changedSource,
            now switch
            {
                < 1500 => default(XboxReport),
                < 5000 => action,
                < 6000 => null,
                < 6300 => secondAction,
                < 6500 => default(XboxReport),
                _ => secondAction
            });
        var result = SteamInputRelay.Run(Observe, () => output, () => now,
            interval => now += (long)interval.TotalMilliseconds, CancellationToken.None);
        check(result == new RelayResult(RelayExit.SourceChanged, true) && output.Attaches == 1 && output.Closes == 1
            && output.Reports.SequenceEqual(new[] { action, default, secondAction, default }),
            "One pad outlives the old timer, neutralizes stale input, rearms only on release and closes on source change");

        foreach (var loss in new[] { RelayExit.FocusLost, RelayExit.OverlayOpened, RelayExit.SessionEnded, RelayExit.SteamShadow })
        {
            now = 0;
            output = new Output();
            RelayObservation LoseScope() => new(now < 1800 || loss != RelayExit.SessionEnded,
                now < 1800 || loss != RelayExit.FocusLost, now >= 1800 && loss == RelayExit.OverlayOpened, true,
                now >= 1800 && loss == RelayExit.SteamShadow, source, now < 1500 ? default : action);
            result = SteamInputRelay.Run(LoseScope, () => output, () => now,
                interval => now += (long)interval.TotalMilliseconds, CancellationToken.None);
            check(result == new RelayResult(loss, true) && output.Attaches == 1 && output.Closes == 1
                && output.Reports.SequenceEqual(new[] { action, default }),
                $"Relay stops forwarding and closes neutral on {loss}");
        }
    }

    private sealed class Output : IXboxRelayOutput
    {
        internal readonly List<XboxReport> Reports = [];
        internal int Attaches, Closes;
        public bool IsOwned => Attaches == 1 && Closes == 0;
        public bool TryConnectNeutral(Func<bool> canAttach)
        {
            if (!canAttach()) return false;
            Attaches++;
            return true;
        }
        public void Submit(XboxReport report) => Reports.Add(report);
        public bool Close()
        {
            Closes++;
            Reports.Add(default);
            return true;
        }
    }
}

// Playhub Xbox Session. Distributed under GPL-3.0-only.
internal static class SteamControllerGateChecks
{
    internal static void Run(Action<bool, string> check)
    {
        SteamInputInventoryCompatibilityChecks.Run(check);
        SteamControllerReconnectChecks.Run(check);
        SteamControllerRestoreRecoveryChecks.Run(check);
        const string steam = "[slot 1]\nVID=0x28DE\nPID=0x1302\nhandle=0x34\ntype=steam\n";
        var source = new TritonSource(1, 0x1302, 0x34);
        var connected = SteamInputEvidence.Parse(steam);
        check(connected == new SteamVirtualInventory(source, false)
            && SteamControllerSourceProbe.ParseInventory(steam) is [var slot]
            && slot == new SteamControllerSourceProbe.Slot(1, 0x1302, 0x34),
            "Both input paths require the same Steam-classified physical controller and exact slot/handle");
        check(SteamInputEvidence.Parse(steam.Replace("1302", "1304")).Source == source with { Product = 0x1304 },
            "The verified Steam Controller 2 puck identity remains supported");
        bool Ready(uint caps = 0, ushort vendor = 0x28DE, ushort product = 0x11FF, uint slotIndex = 1, uint state = 0) =>
            SteamControllerSourceProbe.MatchesConnectedVirtual(source, caps, vendor, product, slotIndex, state);
        check(Ready() && !Ready(caps: 1167) && !Ready(state: 1167) && !Ready(vendor: 0x045E)
            && !Ready(product: 0x028E) && !Ready(slotIndex: 2),
            "A stale typed record, disconnected pad, generic XInput and different Steam slot cannot start a layout lease");
        var readyEpoch = new SteamControllerLayoutTrial.RelayFocusEpoch();
        check(!readyEpoch.ShouldStart(23, source, sourceUnavailable: !Ready(state: 1167))
            && !readyEpoch.ShouldStart(23, source, sourceUnavailable: !Ready(state: 1167))
            && !readyEpoch.ShouldStart(23, source, sourceUnavailable: !Ready())
            && readyEpoch.ShouldStart(23, source, sourceUnavailable: !Ready())
            && !readyEpoch.ShouldStart(23, source, sourceUnavailable: !Ready()),
            "Absent initial connection proof does not consume the epoch; two ready samples acquire it once");
        bool initialReady = false;
        int connectionReads = 0;
        var initialConnection = new SteamControllerSourceProbe.InitialConnection(() => { connectionReads++; return initialReady; });
        check(!initialConnection.Current(), "A disconnected typed device is refused immediately before layout dispatch");
        initialConnection.LayoutStarted(false);
        initialReady = true;
        check(initialConnection.Current(), "A refused dispatch never bypasses subsequent live connection proof");
        initialConnection.LayoutStarted(true);
        initialReady = false;
        int priorReads = connectionReads;
        check(initialConnection.Current() && connectionReads == priorReads,
            "After dispatch source ownership transfers to exact RawInput and does not depend on the synthetic output's changed XInput view");
        const string syntheticRecord = "[slot 3]\nVID=0x045E\nPID=0x7EB0\nhandle=0x99\ntype=xbox360\n";
        var enumeratedOutput = SteamInputEvidence.Parse(steam + syntheticRecord);
        check(enumeratedOutput == connected && SteamInputEvidence.Parse(syntheticRecord + steam) == connected
            && SteamControllerSourceProbe.ParseInventory(steam + syntheticRecord) is [var same] && same.Handle == source.Handle,
            "Enumerating a separate owned output or retaining its stale Steam record does not hide the physical source");
        check(SteamInputEvidence.Parse(syntheticRecord).Source is null && SteamControllerSourceProbe.ParseInventory(syntheticRecord).Length == 0,
            "A synthetic output alone can never enable a source or layout compatibility");
        foreach (string alias in new[] { syntheticRecord.Replace("0x99", "0x34"), syntheticRecord.Replace("slot 3", "slot 1"),
            syntheticRecord.Replace("type=xbox360", "type=steam"), syntheticRecord.Replace("handle=0x99\n", ""),
            syntheticRecord.Replace("handle=0x99", "handle=0") })
        {
            var denied = SteamInputEvidence.Parse(steam + alias);
            check(denied.Shadow || denied.Source is null,
                "Conflicting handle/slot, Steam-type masquerade and incomplete output identity fail closed");
        }
        var outputLease = new SteamControllerSourceLease(source);
        check(outputLease.Current(connected) && outputLease.Current(enumeratedOutput) && outputLease.Current(connected)
            && outputLease.Current(enumeratedOutput) && !outputLease.Revoked,
            "Output attach, detach and stale output inventory preserve the same exact physical source lease");
        check(!outputLease.Current(SteamInputEvidence.Parse(syntheticRecord)) && !outputLease.Revoked
            && outputLease.Current(connected),
            "An output-only metadata snapshot pauses forwarding without proving physical removal; the exact source may recover");
        outputLease.Revoke();
        check(outputLease.Revoked && !outputLease.Current(connected) && !outputLease.Current(enumeratedOutput),
            "An explicit physical-removal revocation cannot be revived by stale output records or the old source identity");
        foreach (string ordinary in new[]
        {
            "", "[slot 0]\nVID=0x045E\nPID=0x028E\nhandle=0x1\ntype=xbox360\nname=SteamController2\n",
            "[slot 0]\nVID=0x054C\nPID=0x0CE6\nhandle=0x1\ntype=ps5\n",
            steam.Replace("type=steam", "type=xbox360"), steam.Replace("type=steam", "type=generic"),
            steam.Replace("type=steam\n", ""), steam.Replace("1302", "11FF"), steam.Replace("1302", "1205"),
            steam.Replace("handle=0x34", "handle=0"), steam + "type=xbox360\n",
            steam + steam.Replace("slot 1", "slot 2").Replace("handle=0x34", "handle=0x35")
        })
            check(SteamInputEvidence.Parse(ordinary).Source is null
                && SteamControllerSourceProbe.ParseInventory(ordinary).Length == 0,
                "Absent, generic, Xbox/PS, malformed or ambiguous devices cannot enable Steam Controller compatibility");

        var lease = new SteamControllerSourceLease(source);
        check(lease.Current(connected) && !lease.Revoked, "A controller lease starts only with its exact connected Steam identity");
        check(!lease.Current(default) && !lease.Revoked && lease.Current(connected),
            "Unknown inventory pauses the lease and requires its same exact source before forwarding resumes");
        lease.Revoke();
        check(lease.Revoked && !lease.Current(connected),
            "A positively revoked lease cannot resume even when Steam reuses the same slot/handle");
        var changed = source with { Handle = source.Handle + 1 };
        var replacement = new SteamControllerSourceLease(changed);
        check(replacement.Current(new(changed, false)) && !replacement.Current(connected),
            "Reconnection binds a new lease and refuses old controller evidence");
        lease = new SteamControllerSourceLease(source);
        check(!lease.Current(new(source, true)), "A conflicting synthetic source identity revokes the physical source lease");

        var win32 = new SteamControllerLayoutTrial.Win32LayoutTransition();
        check(!win32.ObserveController(null, true, true, 0) && !win32.NeedsExitRestore,
            "A Win32 overlay with no Steam Controller does not acquire any compatibility layout obligation");
        check(!win32.ObserveController(source, true, false, 10) && win32.NeedsExitRestore
            && !win32.ObserveController(null, true, false, 20)
            && win32.ObserveController(null, true, true, 30),
            "Win32 disconnect releases only a previously observed Steam Controller overlay lease after game focus returns");
        win32.Restored(true, 30);
        check(!win32.ObserveController(null, true, true, 40) && !win32.NeedsExitRestore,
            "An ordinary controller cannot reacquire the released Win32 compatibility lease");

        const ulong gameId = 0xA000000102000000;
        var binding = new SteamRawInputRelayTrial.SessionBinding(gameId, 23, 120, 27, 130, source, new object(), 23, 7, 27);
        var attestation = new SteamRawInputRelayTrial.SessionAttestation();
        check(attestation.Authorize(binding, 1, true, true) == SteamRawInputRelayTrial.ReaderAuthorization.SteamLog,
            "The first connected controller reader has fresh Steam tracking");
        attestation.Revoke();
        check(!attestation.CanCarry(binding, 2)
            && attestation.Authorize(binding, 2, false, true) == SteamRawInputRelayTrial.ReaderAuthorization.None
            && attestation.Authorize(binding, 2, true, true) == SteamRawInputRelayTrial.ReaderAuthorization.SteamLog,
            "A reconnected controller cannot reuse a prior reader's session attestation");

        var epochs = new SteamControllerLayoutTrial.RelayFocusEpoch();
        check(!epochs.ShouldStart(23, null) && !epochs.ShouldStart(23, null),
            "No-controller focus samples cannot begin a layout epoch, including the no-relay diagnostic path");
        int sends = 0, probes = 0;
        SteamControllerLayoutTrial.RunLease(0xA0000001, () => false, (_, _) => { sends++; return new(true, true); },
            () => throw new Exception("No controller must not query a restore layout"), _ => probes++);
        check(sends == 0 && probes == 0, "An absent Steam Controller never starts a layout command or reader");
        bool present = true;
        var commands = new List<uint>();
        SteamControllerLayoutTrial.RunLease(0xA0000001, () => present, (layout, guard) =>
        {
            if (guard?.Invoke() == false) return default;
            commands.Add(layout);
            if (layout != 0) present = false;
            return new(true, true);
        }, () => throw new Exception("A disconnected controller must not force a menu/desktop layout"),
            _ => throw new Exception("The disconnected controller must not start a relay"), controllerPresent: () => present);
        check(commands.SequenceEqual(new uint[] { 0xA0000001, 0 }),
            "Disconnect immediately after layout acquisition releases only that override to automatic Steam selection");

        long now = 0;
        var output = new Output();
        var action = new XboxReport(0x1000, 0, 0, 0, 0, 0, 0);
        lease = new SteamControllerSourceLease(source);
        RelayObservation Observe()
        {
            bool current = lease.Current(now < 1800 ? connected : default);
            return new(true, true, false, true, false, current ? source : null, now < 1500 ? default(XboxReport) : action);
        }
        var result = SteamInputRelay.Run(Observe, () => output, () => now,
            interval => now += (long)interval.TotalMilliseconds, CancellationToken.None);
        check(result == new RelayResult(RelayExit.SourceUnavailable, true) && output.Attaches == 1 && output.Closes == 1
            && output.Reports.SequenceEqual(new[] { action, default(XboxReport) }),
            "Disconnect while holding a button neutralizes and detaches exactly the owned output");
        now = 0;
        output = new Output();
        lease = new SteamControllerSourceLease(source);
        RelayObservation WithEnumeratedOutput()
        {
            string snapshot = now >= 2100 ? syntheticRecord : now >= 300 ? steam + syntheticRecord : steam;
            var evidence = SteamInputEvidence.Parse(snapshot);
            bool current = lease.Current(evidence);
            return new(true, true, false, true, evidence.Shadow, current ? evidence.Source : null,
                now < 1500 ? default(XboxReport) : action);
        }
        // Steam may add this independent output row after its initial neutral
        // enumeration, long before Guide or any physical controller removal.
        result = SteamInputRelay.Run(WithEnumeratedOutput, () => output, () => now,
            interval => now += (long)interval.TotalMilliseconds, CancellationToken.None);
        check(result == new RelayResult(RelayExit.SourceUnavailable, true) && now >= 2100
            && output.Attaches == 1 && output.Closes == 1 && output.Reports.Contains(action),
            "A production relay survives Steam enumerating its independent output and stops only when its physical source leaves");
        output = new Output();
        result = SteamInputRelay.Run(() => new(true, true, false, true, false,
            SteamInputEvidence.Parse("[slot 0]\nVID=0x045E\nPID=0x028E\ntype=xbox360\n").Source, default(XboxReport)),
            () => output, () => 0, _ => { }, CancellationToken.None);
        check(result.Reason == RelayExit.SourceUnavailable && output.Attaches == 0 && output.Closes == 0,
            "An ordinary Xbox pad creates no emulated controller and owns nothing to detach");
    }

    private sealed class Output : IXboxRelayOutput
    {
        internal int Attaches, Closes;
        internal readonly List<XboxReport> Reports = [];
        public bool IsOwned => Attaches == 1 && Closes == 0;
        public bool TryConnectNeutral(Func<bool> canAttach) { if (!canAttach()) return false; Attaches++; return true; }
        public void Submit(XboxReport report) => Reports.Add(report);
        public bool Close() { Reports.Add(default); Closes++; return true; }
    }
}

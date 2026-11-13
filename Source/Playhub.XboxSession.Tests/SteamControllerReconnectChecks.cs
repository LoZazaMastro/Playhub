// Playhub Xbox Session. Distributed under GPL-3.0-only.
internal static class SteamControllerReconnectChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var source = new TritonSource(1, 0x1302, 52);
        var connected = SteamControllerSourceProbe.Connection(source, 0, 0x28DE, 0x11FF, 1, 0);
        var disconnected = SteamControllerSourceProbe.Connection(source, 0, 0x28DE, 0x11FF, 1, 1167);
        var unknown = SteamControllerSourceProbe.ConnectionStatus.Unknown;
        check(SteamControllerRawInputEvidence.ValveDeviceInfo(2, 0x28DE, 0x11FF)
            && !SteamControllerRawInputEvidence.ValveDeviceInfo(0, 0x28DE, 0x11FF)
            && !SteamControllerRawInputEvidence.ValveDeviceInfo(2, 0x045E, 0x7EB0)
            && !SteamControllerRawInputEvidence.ValveDeviceInfo(2, 0x28DE, 0x1302),
            "Only HID capabilities from Valve's virtual input accompany the exact physical-controller pipe");
        var rawProof = SteamControllerSourceProbe.ConnectionProof(unknown, 0x1234);
        check(SteamControllerRawInputEvidence.SameDevice(0x1234, 0x1234)
            && !SteamControllerRawInputEvidence.SameDevice(0x1234, 0x1235)
            && !SteamControllerRawInputEvidence.SameDevice(0x1234, null)
            && !SteamControllerRawInputEvidence.SameDevice(0x1234, 0)
            && !SteamControllerRawInputEvidence.SameDevice(-1, -1),
            "The final pre-dispatch RawInput proof refuses disappeared, unknown or replaced native device instances");
        check(rawProof.Status == SteamControllerSourceProbe.ConnectionStatus.Connected && rawProof.RawDevice == 0x1234
            && SteamControllerSourceProbe.ConnectionProof(unknown, null).Status == unknown
            && SteamControllerSourceProbe.ConnectionProof(unknown, 0).Status == unknown
            && SteamControllerSourceProbe.ConnectionProof(unknown, -1).Status == unknown,
            "Generic XInput may use an independently attested present RawInput source; stale files and unknown/missing devices cannot authorize it");
        var rawEpoch = new SteamControllerLayoutTrial.RelayFocusEpoch();
        check(!rawEpoch.ShouldStart(23, source, false, 0x1234)
            && !rawEpoch.ShouldStart(23, source, false, 0x1235)
            && rawEpoch.ShouldStart(23, source, false, 0x1235),
            "A RawInput bootstrap requires the same native device in both samples, not only a reused Steam slot or handle");
        check(connected == SteamControllerSourceProbe.ConnectionStatus.Connected
            && disconnected == SteamControllerSourceProbe.ConnectionStatus.Disconnected,
            "Connection readback distinguishes the exact Steam virtual source from its confirmed state-not-connected result");
        check(SteamControllerSourceProbe.Connection(source, 1167, 0, 0, 0, 1167) == unknown
            && SteamControllerSourceProbe.Connection(source, 0, 0x045E, 0x028E, 1, 1167) == unknown
            && SteamControllerSourceProbe.Connection(source, 0, 0x28DE, 0x11FF, 2, 1167) == unknown
            && SteamControllerSourceProbe.Connection(source, 0, 0x045E, 0x028E, 1, 0) == unknown,
            "Missing or remapped XInput slots and a generic output are unknown, not physical disconnect evidence");
        var removed = new SteamRawInputRelayTrial.ReaderOutcome(true, true, true);
        var observed = new SteamRawInputRelayTrial.ReaderOutcome(true, false, true);
        check(SteamControllerLayoutTrial.CanReconnectSource(removed, unknown)
            && SteamControllerLayoutTrial.CanReconnectSource(observed, disconnected),
            "An attested removed RawInput device or exact disconnected source may renew only after owned output cleanup");
        check(!SteamControllerLayoutTrial.CanReconnectSource(default, disconnected)
            && !SteamControllerLayoutTrial.CanReconnectSource(removed with { Attested = false }, disconnected)
            && !SteamControllerLayoutTrial.CanReconnectSource(removed with { CleanupConfirmed = false }, disconnected)
            && !SteamControllerLayoutTrial.CanReconnectSource(observed, unknown),
            "Unknown EOF, unauthenticated readers, failed cleanup and output topology churn never authorize a reconnect");

        var epochs = new SteamControllerLayoutTrial.RelayFocusEpoch();
        check(!epochs.ShouldStart(23, source) && epochs.ShouldStart(23, source), "The first controller lease requires two ready samples");
        long originalEpoch = epochs.Generation;
        var oldLease = new SteamControllerSourceLease(source);
        check(oldLease.Current(new(source, false)), "The original source lease starts with its exact identity");
        if (SteamControllerLayoutTrial.CanReconnectSource(removed, unknown)) { oldLease.Revoke(); epochs.Reset(); }
        check(epochs.Generation > originalEpoch && !oldLease.Current(new(source, false))
            && !epochs.ShouldStart(23, source, sourceUnavailable: true)
            && !epochs.ShouldStart(23, source) && epochs.ShouldStart(23, source)
            && !epochs.ShouldStart(23, source),
            "Confirmed removal revokes the old lease and allows one fresh two-sample lease even when Steam reuses slot and handle");
        var nextLease = new SteamControllerSourceLease(source);
        check(nextLease.Current(new(source, false)) && !oldLease.Current(new(source, false)),
            "A reconnected physical source cannot revive its old lease or reports");
        if (SteamControllerLayoutTrial.CanReconnectSource(observed, unknown)) epochs.Reset();
        check(!epochs.ShouldStart(23, source) && !epochs.ShouldStart(23, source),
            "Generic capabilities while the output is enumerated cannot reset an attempted epoch into a restart loop");
        if (SteamControllerLayoutTrial.CanReconnectSource(default, unknown)) epochs.Reset();
        check(!epochs.ShouldStart(23, source), "An unexplained reader EOF does not restart its controller output");

        const ulong gameId = 0xA000000102000000;
        var steamFiles = new object();
        var binding = new SteamRawInputRelayTrial.SessionBinding(gameId, 23, 120, 27, 130, source, steamFiles, 23, 7, 27);
        var attestation = new SteamRawInputRelayTrial.SessionAttestation();
        var none = SteamRawInputRelayTrial.ReaderAuthorization.None;
        check(attestation.Authorize(binding, 1, true, true) == SteamRawInputRelayTrial.ReaderAuthorization.SteamLog,
            "The original parent session is established by fresh native input and positive Steam process tracking");
        attestation.Revoke();
        check(!attestation.CanCarry(binding, 2) && attestation.Authorize(binding, 2, false, true) == none,
            "An ordinary revocation cannot reuse original Steam tracking as permission to reconnect");
        attestation.ConfirmSourceDisconnected();
        foreach (var changed in new[]
        {
            binding with { GameId = gameId + (1UL << 32) }, binding with { CompanionPid = 24 },
            binding with { CompanionBirth = 121 }, binding with { GamePid = 28 }, binding with { GameBirth = 131 },
            binding with { SteamSession = new object() }, binding with { Window = 24 },
            binding with { WindowThread = 8 }, binding with { WindowOwner = 28 },
        })
            check(!attestation.CanCarry(changed, 2) && attestation.Authorize(changed, 2, false, true) == none,
                "Reconnect refuses replaced Steam trust, parent, game, process birth and foreground-window identity");
        check(attestation.Authorize(binding, 2, false, true, sessionCurrent: false) == none
            && attestation.Authorize(binding, 2, false, false) == none,
            "An exited parent/session or missing fresh packet cannot renew connection proof");
        var reconnected = binding with { Source = source with { Handle = 53 } };
        check(attestation.Authorize(reconnected, 2, false, true) == SteamRawInputRelayTrial.ReaderAuthorization.RetainedSession
            && attestation.Authorize(reconnected, 2, false, true) == none && !attestation.CanCarry(binding, 3),
            "One fresh reader may reuse only the retained parent Steam proof; old source reports and the reconnect permit cannot replay");
    }
}

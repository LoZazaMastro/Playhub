// Playhub Xbox Session. Distributed under GPL-3.0-only.
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;
using WSGM.PackagedLaunch;

internal sealed class SteamRawInputRelayTrial : IDisposable
{
    private const long InputMaxAgeMs = 350;
    private const long InventoryRefreshMs = 45;
    private readonly string _library, _bus;
    private readonly SessionAttestation _attestation = new();
    private SteamRawInputRelayTrial(string library, string bus) { _library = library; _bus = bus; }
    private SteamInputSessionOutput? _sessionOutput;
    internal bool NeutralHeld => _sessionOutput?.NeutralHeld==true;
    internal bool NoSessionOutput => _sessionOutput is null;
    // A completed, detached reader is never revived. A new admission must
    // authorize its new child from Steam tracking or the retained wrapper chain.
    internal bool RetireForFreshAdmission(ReaderOutcome previous)
    {
        if (!previous.FreshAdmissionRequired || _sessionOutput is not null) return false;
        _attestation.Revoke();
        return true;
    }
    internal void RevokeController() { _attestation.Revoke(); EndOutput(); }
    internal void ConfirmSourceDisconnected() { _attestation.ConfirmSourceDisconnected(); EndOutput(); }
    private bool EndOutput()
    {
        var held=_sessionOutput;if(held is null)return true;
        held.Dispose();if(!held.CleanupConfirmed)return false;
        _sessionOutput=null;return true;
    }
    internal bool ConfirmDisconnectedHeld(ReaderOutcome reader,SteamControllerSourceProbe.ConnectionStatus proof)
    {
        if(_sessionOutput is not {} held||!StopDisconnectedHeld(held,reader,proof))return false;
        _sessionOutput=null;_attestation.ConfirmSourceDisconnected();return true;
    }
    internal static bool StopDisconnectedHeld(SteamInputSessionOutput held,ReaderOutcome reader,
        SteamControllerSourceProbe.ConnectionStatus proof)
    {
        if(!reader.CanResume||proof!=SteamControllerSourceProbe.ConnectionStatus.Disconnected||!held.NeutralHeld)return false;
        held.Dispose();return held.CleanupConfirmed;
    }
    public void Dispose() => EndOutput();

    internal static SteamRawInputRelayTrial? Prepare()
    {
        string? expected = typeof(SteamRawInputRelayTrial).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(item => item.Key == "PlayhubSteamInputRelayDriverSha256")?.Value;
        if (expected?.Length != 64 || !expected.All(Uri.IsHexDigit)) return null;

        try
        {
            using var service = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\ViGEmBus");
            if (service?.GetValue("ImagePath") is not string image ||
                !image.Equals(@"\SystemRoot\System32\drivers\ViGEmBus.sys", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("ViGEm service image differs from the approved private fixture.");
            string driver = Path.Combine(Environment.SystemDirectory, "drivers", "ViGEmBus.sys");
            using var input = new FileStream(driver, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!Convert.ToHexString(SHA256.HashData(input)).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The installed driver differs from the approved private fixture.");
            string library = Path.Combine(AppContext.BaseDirectory, "ViGEmClient.x64.dll");
            if (!File.Exists(library) || WindowsXboxPdo.SinglePresentBus() is not { } bus)
                throw new InvalidOperationException("The pinned SDK or one unambiguous ViGEm bus is unavailable.");
            return new SteamRawInputRelayTrial(library, bus);
        }
        catch (Exception error)
        {
            PackagedLaunchLog.Info($"Steam RawInput relay preparation refused: {error.GetType().Name}.");
            return null;
        }
    }

    // CleanupConfirmed means actual terminal detach; NeutralHeld means the SAME
    // session PDO is connected but this reader can no longer forward reports.
    internal readonly record struct ReaderOutcome(bool Attested, bool SourceRemoved, bool CleanupConfirmed,
        RelayExit? EndReason = null, bool NeutralHeld = false, bool ReaderReleased = false,
        bool ExplicitScopeEnded = false, int ReaderPid = 0, long ReaderBirth = 0, long Epoch = 0, bool ReaderStopped = false, bool ParentScopeRevoked = false,
        bool TerminalScopeLost = false)
    {
        internal bool CanResume => Attested && !CleanupConfirmed && NeutralHeld && ReaderReleased && ReaderStopped && (ExplicitScopeEnded || ParentScopeRevoked)
            && !SourceRemoved && ReaderPid > 0 && ReaderBirth > 0 && Epoch > 0
            && EndReason is RelayExit.FocusLost or RelayExit.OverlayOpened;
        internal bool FreshAdmissionRequired => TerminalScopeLost && Attested && CleanupConfirmed && ReaderReleased && ReaderStopped
            && !NeutralHeld && !SourceRemoved && ReaderPid > 0 && ReaderBirth > 0 && Epoch > 0 && EndReason == RelayExit.FocusLost;
    }
    internal readonly record struct ReaderTerminal(bool Seen, bool ScopeIntact, bool SourceRemoved, bool Shadow)
    {
        internal bool KnownFocusPause(bool attested, bool sameSession, bool gameFocusLost) =>
            Seen && !ScopeIntact && !SourceRemoved && !Shadow && attested && sameSession && gameFocusLost;
        internal static ReaderTerminal Read(JsonElement packet) =>
            packet.TryGetProperty("scopeIntact", out var intact) && intact.ValueKind is JsonValueKind.True or JsonValueKind.False
            && packet.TryGetProperty("sourceRemoved", out var removed) && removed.ValueKind is JsonValueKind.True or JsonValueKind.False
            && packet.TryGetProperty("steamShadow", out var shadow) && shadow.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? new(true, intact.GetBoolean(), removed.GetBoolean(), shadow.GetBoolean()) : default;
    }

    internal ReaderOutcome Run(Process child, TrustedSteamFiles steam, string inventory, SessionBinding binding,
        long focusEpoch, Func<bool> ownsInput, CancellationToken cancellation, Func<bool>? sessionCurrent = null, Func<RelayExit?>? readPause = null,
        SteamTrackedWrapperChain.ReaderProof? wrapperReader = null,Func<RelayExit?>? readCapturedPause = null)
    {
        bool tracked = false, layout = false, layoutSeen = false, complete = false, shadow = false, combined = false, sourceRemoved = false;
        var authorization = ReaderAuthorization.None;
        var terminalProof = default(ReaderTerminal);
        var releasedFrames = new FreshReleasedReaderFrames();
        RelayExit? parentPause = null;
        bool parentPauseCaptured=false,inventorySuspended=false;
        long inventoryReattestedAt=long.MinValue;
        uint sequence = 0;
        TimedReport? latest = null;
        var source = binding.Source;
        var inventoryWatch = new InventoryWatch(source);
        Task<string?> pending = child.StandardOutput.ReadLineAsync();
        DateTime childStarted = child.StartTime;
        string childImage = child.StartInfo.FileName;
        int childId = child.Id;
        int forwarded = 0;
        long nextTrackingCheck = 0;
        long inventoryReadAt = long.MinValue;
        SteamVirtualInventory inventoryState = default;
        bool SessionCurrent() => !cancellation.IsCancellationRequested && (sessionCurrent?.Invoke() ?? ownsInput())
            && (wrapperReader is null || wrapperReader.Current(binding));
        bool Current() => SessionCurrent() && ownsInput() && !child.HasExited;
        bool CanForward() => Current() && !complete && authorization != ReaderAuthorization.None
            && layout && releasedFrames.Ready && latest is { } report
            && Environment.TickCount64 - report.Tick is >= 0 and <= InputMaxAgeMs && Current();

        SteamVirtualInventory ReadInventory(long now)
        {
            if (inventoryReadAt != long.MinValue && now - inventoryReadAt < InventoryRefreshMs) return inventoryState;
            inventoryReadAt = now;
            return inventoryState = SteamInputEvidence.Read(inventory);
        }

        void PollPackets()
        {
            for (int count = 0; count < 32 && pending.IsCompleted && !complete; count++)
            {
                string? line = pending.GetAwaiter().GetResult();
                if (line is null || line.Length > 2048)
                {
                    complete = true;
                    break;
                }
                using var json = JsonDocument.Parse(line);
                var packet = json.RootElement;
                switch (packet.GetProperty("stage").GetString())
                {
                    case "control-layout":
                        if (layoutSeen || sequence != 0) { complete = true; break; }
                        layoutSeen = true;
                        layout = NativeSourceMatches(packet, childId, source);
                        combined = packet.GetProperty("combinedTriggers").GetBoolean();
                        PackagedLaunchLog.Info($"Steam RawInput descriptor: {packet.GetRawText()}");
                        break;
                    case "frame":
                        if (!layout || ReadFrame(packet, sequence) is not { } frame) { complete = true; break; }
                        latest = frame;
                        sequence = frame.Sequence;
                        if(!inventorySuspended)releasedFrames.Observe(frame.Sequence, frame.Report,
                            FreshAfterInventory(frame.Tick,Environment.TickCount64,inventoryReattestedAt));
                        break;
                    case "native-source-sample":
                        terminalProof = ReaderTerminal.Read(packet);
                        shadow = packet.GetProperty("steamShadow").GetBoolean();
                        sourceRemoved = packet.TryGetProperty("sourceRemoved", out var removed)
                            && removed.ValueKind == JsonValueKind.True;
                        PackagedLaunchLog.Info($"Steam RawInput native result: {packet.GetRawText()}");
                        complete = true;
                        break;
                    case "native-source-absent":
                    case "raw-descriptor-unavailable":
                    case "native-reader-refused":
                        PackagedLaunchLog.Info($"Steam RawInput native result: {packet.GetRawText()}");
                        complete = true;
                        break;
                    default:
                        complete = true;
                        break;
                }
                if (!complete) pending = child.StandardOutput.ReadLineAsync();
            }
        }

        void Track(long now)
        {
            if (tracked || authorization != ReaderAuthorization.None || now < nextTrackingCheck) return;
            nextTrackingCheck = now + 150;
            try { tracked = TrialSteamTracking.IsTracked(steam.TrackingLog, child, binding.GameId); }
            catch (Exception error) when (error is InvalidOperationException or IOException
                or System.ComponentModel.Win32Exception) { }
        }

        SteamNativeOverlayInputPresentationBridge.Command? bootstrapPrepare=null;
        SteamNativeOverlayInputPresentationBridge.Command? ReadBootstrapPrepare()
        {
            var c=SteamNativeOverlayInputPresentationBridge.Read().Command;
            return c is not null&&PendingNativePrepare(c,binding,Current)?c:null;
        }
        bool BootstrapCurrent()
        {
            PollPackets();
            var inventoryNow=ReadInventory(Environment.TickCount64);
            // A known shadow/replacement revokes even a neutral-only claim.
            // A transient missing file remains neutral; it never means Connected
            // and the issuer must still prove its physical source before769.
            if(inventoryNow.Shadow||inventoryNow.Source is {} other&&other!=source)return false;
            return bootstrapPrepare is {} expected&&!complete&&!child.HasExited&&authorization!=ReaderAuthorization.None
                &&layout&&releasedFrames.Ready&&!inventorySuspended&&!shadow&&!sourceRemoved
                &&latest is {} frame&&Environment.TickCount64-frame.Tick is >=0 and <=InputMaxAgeMs
                &&PendingNativePrepare(expected,binding,Current);
        }
        RelayExit? ReadSubmitPause()
        {
            bool CaptureCurrent()
            {
                PollPackets();
                return !complete && !child.HasExited && authorization != ReaderAuthorization.None
                    && layout && !shadow && !sourceRemoved && !inventorySuspended && SessionCurrent();
            }
            // Read only the exact witness captured by the denied ownsInput
            // getter. A later menu/EOF/device fault cannot invent that witness.
            if (!CaptureCurrent() || readCapturedPause?.Invoke() != RelayExit.OverlayOpened
                || !CaptureCurrent()) return null;
            parentPause = RelayExit.OverlayOpened;
            parentPauseCaptured = true;
            return parentPause;
        }
        RelayObservation Observe()=>ObserveCore(false);
        RelayObservation ObserveCore(bool allowNeutralBootstrap)
        {
            if(allowNeutralBootstrap)bootstrapPrepare=null;
            // Keep retained session identity distinct from foreground. A known
            // focus pause is not EOF; a missing/malformed terminal remains STOP.
            PollPackets();
            if (!SessionCurrent()) return default;
            // Reconcile only a parent veto observed while this reader is alive.
            // The separate post-denial callback returns only its captured opening
            // receipt. Actual pause reads remain unchanged for terminal diagnosis.
            var scopeEnd=ObserveParentScope(ownsInput,readPause,
                ()=>!complete&&!child.HasExited&&SessionCurrent(),ref parentPause,ref parentPauseCaptured,readCapturedPause:readCapturedPause);
            if(scopeEnd==RelayExit.OverlayOpened&&(!allowNeutralBootstrap||(bootstrapPrepare=ReadBootstrapPrepare())is null))
                return new(true,true,true,false,false,source,null);
            if(scopeEnd==RelayExit.FocusLost)return new(true,false,false,false,false,source,null);
            if (child.HasExited) return new(true, true, false, false, false, source, null);
            long now = Environment.TickCount64;
            var state = ReadInventory(now);
            var status = inventoryWatch.Observe(state, now, authorization!=ReaderAuthorization.None&&layout&&!complete&&SessionCurrent());
            if(status!=InventoryStatus.Current)
            {
                latest=null;
                if(!inventorySuspended){inventorySuspended=true;releasedFrames=new FreshReleasedReaderFrames();}
            }
            else if(inventorySuspended)
            {
                inventorySuspended=false;inventoryReattestedAt=now;latest=null;releasedFrames=new FreshReleasedReaderFrames();
            }
            XboxReport? report = null;
            bool fresh = false;
            if (!complete && latest is { } observed)
            {
                long age = now - observed.Tick;
                fresh = age is >= 0 and <= InputMaxAgeMs;
                // An old neutral report is safe. A held control needs fresh
                // reports so a lost release never leaves it asserted.
                report=ReleasedReport(observed,now,releasedFrames.Ready);
            }
            Track(now);
            if (authorization == ReaderAuthorization.None && layout && fresh && !complete && ownsInput())
            {
                authorization = _attestation.Authorize(binding, focusEpoch, tracked, nativeFresh: true, sessionCurrent: Current(), wrapperReader);
                if (authorization == ReaderAuthorization.SteamTrackedWrapperChain)
                    PackagedLaunchLog.Info($"Steam input reader authorization: proof={authorization}; reader={childId}; birth={childStarted.ToFileTimeUtc()}; epoch={focusEpoch}; tracked-in-log={tracked}.");
            }
            var finalScope=ObserveParentScope(ownsInput,readPause,
                ()=>!complete&&!child.HasExited&&SessionCurrent(),ref parentPause,ref parentPauseCaptured,readFirst:false,readCapturedPause:readCapturedPause);
            return new RelayObservation(true, finalScope!=RelayExit.FocusLost, finalScope==RelayExit.OverlayOpened,
                authorization != ReaderAuthorization.None && layout && !complete,
                shadow || state.Shadow, status == InventoryStatus.Revoked ? null : source, report);
        }

        var start = Stopwatch.StartNew();
        while (AwaitReader(complete, start.ElapsedMilliseconds, layout, tracked,
            _attestation.CanCarry(binding, focusEpoch, wrapperReader), authorization))
        {
            // An early native refusal can be written before the tracking poll
            // runs, or before this worker observes that the process exited.
            PollPackets();
            if (complete || !Current()) break;
            var ready = ObserveCore(true);
            if (ready.SyntheticVisibleToSteam || ready.Source != source) break;
            if (ready.CanObserveOutput && releasedFrames.Ready && ready.Source == source && ready.Report is not null)
            {
                using var lease = new Mutex(false, @"Local\Playhub.SteamRawInputRelay");
                if (!lease.WaitOne(0)) return default;
                try
                {
                    _sessionOutput ??= new SteamInputSessionOutput(() => new VigemRelayOutput(_library, _bus));
                    var sessionOutput = _sessionOutput;
                    var readerBinding = new SteamInputSessionOutput.ReaderBinding(binding, childId,
                        childStarted.ToFileTimeUtc(), focusEpoch);
                    var readerLease = sessionOutput.Borrow(readerBinding, CanForward, ReadSubmitPause);
                    if (readerLease is null) return default;
                    bool neutralBootstrap=ready.OverlayOwnsInput&&ready.ExactSession&&ready.ExactGameForeground&&bootstrapPrepare is not null;
                    var result = neutralBootstrap ? HoldNeutralForPause(readerLease,BootstrapCurrent)
                        : SteamInputRelay.Run(Observe, () => new CountedOutput(readerLease, () => forwarded++),
                            () => Environment.TickCount64, delay => cancellation.WaitHandle.WaitOne(delay),cancellation);
                    // Relay may refuse before constructing its output adapter.
                    // Retire the borrowed reader even on that early veto.
                    if(!readerLease.ReleaseConfirmed)
                    {
                        bool released=readerLease.Close();
                        result=result with{CleanupConfirmed=result.CleanupConfirmed&&released};
                    }
                    PackagedLaunchLog.Info($"Steam RawInput relay: frames-forwarded={forwarded}; combined-triggers={combined}; "
                        + $"reader-proof={authorization}; ended={result.Reason}; reader-release-confirmed={result.CleanupConfirmed}; "
                        + $"output-neutral-held={sessionOutput.NeutralHeld}; output-terminal-cleanup={sessionOutput.CleanupConfirmed}; submit-rejection={result.SubmitRejection}.");
                    // Close retired ONLY this reader and neutralized the same PDO.
                    // Drain an authenticated terminal even if the child exited
                    // before Observe could report why its source disappeared.
                    var terminal = Stopwatch.StartNew();
                    for (int drain = 0; drain < 16 && !complete && terminal.ElapsedMilliseconds < 200; drain++)
                    {
                        if (!pending.IsCompleted && !pending.Wait(20)) continue;
                        PollPackets();
                    }
                    bool attested = authorization != ReaderAuthorization.None && layout;
                    var actualPause = readPause?.Invoke();
                    bool sameRetained = SessionCurrent();
                    bool nativePause = terminalProof.KnownFocusPause(attested, sameRetained,
                        actualPause is RelayExit.FocusLost or RelayExit.OverlayOpened || parentPauseCaptured);
                    bool parentRevoked = ParentPauseAllowed(parentPause, result.Reason, complete,
                        attested, sameRetained, actualPause,parentPauseCaptured);
                    bool paused = result.CleanupConfirmed && sessionOutput.NeutralHeld && readerLease.ReleaseConfirmed
                        && !sourceRemoved && !shadow && !terminalProof.SourceRemoved && !terminalProof.Shadow
                        && (!neutralBootstrap||BootstrapCurrent())
                        && (nativePause || parentRevoked) && result.Reason is RelayExit.FocusLost or RelayExit.OverlayOpened;
                    if(neutralBootstrap)PackagedLaunchLog.Info($"Steam RawInput neutral bootstrap: reader={childId}; epoch={focusEpoch}; attested={attested}; neutral-held={sessionOutput.NeutralHeld}; reader-release-confirmed={readerLease.ReleaseConfirmed}; pause-current={paused}; gameplay-forwarded=0.");
                    // Unknown EOF, malformed terminal, shadow/disconnect/fault are
                    // terminal. No new reader may borrow a live unsafe output.
                    if (!paused) EndOutput();
                    return new(attested, sourceRemoved, sessionOutput.CleanupConfirmed, result.Reason,
                        paused && sessionOutput.NeutralHeld, readerLease.ReleaseConfirmed,
                        paused && nativePause, childId, childStarted.ToFileTimeUtc(), focusEpoch,
                        ReaderStopped: false, ParentScopeRevoked: paused && parentRevoked,
                        TerminalScopeLost: result.Reason == RelayExit.FocusLost && terminalProof.Seen && !terminalProof.ScopeIntact
                            && !terminalProof.SourceRemoved && !terminalProof.Shadow && !sourceRemoved && !shadow && sameRetained);
                }
                finally { lease.ReleaseMutex(); }
            }
            cancellation.WaitHandle.WaitOne(15);
        }
        if (!complete)
        {
            // Preserve an already-produced terminal diagnostic after the scope
            // closes, without keeping the relay eligible for output.
            if (!pending.IsCompleted) pending.Wait(200);
            PollPackets();
        }
        if (complete && !child.HasExited) child.WaitForExit(150);
        bool trackedInLog = tracked || WasTracked(steam.TrackingLog, childId, childImage, childStarted, binding.GameId);
        string exit = child.HasExited ? child.ExitCode.ToString() : "running";
        PackagedLaunchLog.Info($"Steam RawInput relay skipped: tracked={tracked}; control-layout={layout}; "
            + $"controls-received={sequence}; steam-shadow={shadow}; layout-seen={layoutSeen}; "
            + $"tracked-log={trackedInLog}; reader-proof={authorization}; native-exit={exit}; scope-current={ownsInput()}.");
        return default;
    }

    internal static bool PendingNativePrepare(SteamNativeOverlayInputPresentationBridge.Command expected,
        SessionBinding binding,Func<bool> current)
    {
        if(expected.Action!="prepare"||expected.FullGameId!=binding.GameId
            ||expected.GamePid!=binding.GamePid||expected.GameBirth!=binding.GameBirth
            ||expected.ExpiresAt<=Environment.TickCount64||!current()
            ||!SteamNativeOverlayInputPresentationBridge.AuthorityCurrent(expected)||!current())return false;
        // No slow provider follows this last mailbox/expiry comparison. A cancel,
        // replacement or expiry during either current getter cannot attach output.
        return SteamNativeOverlayInputPresentationBridge.Read().Command==expected
            &&expected.ExpiresAt>Environment.TickCount64;
    }

    internal static RelayResult HoldNeutralForPause(SteamInputSessionOutput.ReaderLease reader,Func<bool> current)
    {
        RelayExit reason=RelayExit.Faulted;bool clean=false;
        try{reason=reader.TryHoldNeutral(current)&&current()?RelayExit.OverlayOpened:RelayExit.OutputUnavailable;}
        catch{reason=RelayExit.Faulted;}
        finally{try{clean=reader.Close();}catch{}}
        return new(reason,clean);
    }

    internal static RelayExit? ObserveParentScope(Func<bool> ownsInput,Func<RelayExit?>? readPause,
        Func<bool> canCapture,ref RelayExit? requested,ref bool captured,bool readFirst=true,Func<RelayExit?>? readCapturedPause=null)
    {
        // Read callbacks may observe a new Preparing generation synchronously.
        // Do not lose that exact veto between the pause and ownership reads.
        RelayExit? Capture(bool ownershipVeto)
        {
            if(!canCapture())return null;
            var pause=(ownershipVeto?readCapturedPause:readPause)?.Invoke();
            return (pause is RelayExit.FocusLost or RelayExit.OverlayOpened)&&canCapture()?pause:null;
        }
        if(readFirst&&Capture(false) is {} first){requested=first;captured=true;}
        if(requested is {} held)return held;
        if(ownsInput())return null;
        if(Capture(true) is {} after){requested=after;captured=true;return after;}
        return RelayExit.FocusLost;
    }

    internal static bool ParentPauseAllowed(RelayExit? requested, RelayExit result, bool streamComplete,
        bool attested, bool retainedCurrent, RelayExit? actualPause,bool capturedBeforeCompletion=false) => attested && retainedCurrent
        && requested is RelayExit.FocusLost or RelayExit.OverlayOpened && result == requested
        &&(capturedBeforeCompletion||!streamComplete&&actualPause==requested);

    private static bool WasTracked(string log, int pid, string image, DateTime started, ulong gameId)
    {
        try
        {
            using var file = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            file.Position = Math.Max(0, file.Length - 256 * 1024);
            using var rows = new StreamReader(file);
            var now = DateTime.Now;
            while (rows.ReadLine() is { } line)
                if (TrialSteamTracking.MatchesLog(line, pid, image, gameId, started, now)) return true;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return false;
    }

    internal readonly record struct TimedReport(uint Sequence, long Tick, XboxReport Report);

    internal readonly record struct SessionBinding(ulong GameId, int CompanionPid, long CompanionBirth,
        uint GamePid, long GameBirth, TritonSource Source, object SteamSession, nint Window, uint WindowThread, uint WindowOwner)
    {
        internal bool Valid => (GameId >> 32) >= 0x80000000 && (uint)GameId == 0x02000000
            && CompanionPid > 0 && CompanionBirth > 0 && GamePid > 0 && GameBirth > 0 && Source.Valid
            && SteamSession is not null && Window != 0 && WindowThread != 0 && WindowOwner != 0;
        // SteamSession is the same live TrustedSteamFiles instance whose four
        // signed component streams stay pinned, not a caller-supplied path.
        // The enclosing ownsInput predicate also retains the original Steam
        // process and exact game process, and is rechecked before every action.
        internal bool SameRetainedSession(SessionBinding other) => ReferenceEquals(SteamSession, other.SteamSession)
            && this with { Source = other.Source } == other;
    }

    internal enum ReaderAuthorization { None, SteamLog, RetainedSession, SteamTrackedWrapperChain }

    // Once independently authorized, waiting for the two released reports is
    // neutral, including when the user keeps a control held beyond bootstrap.
    internal static bool AwaitReader(bool complete, long elapsed, bool layout, bool tracked,
        bool canCarry, ReaderAuthorization authorization) => !complete
        && (elapsed < 3500 || layout && (tracked || canCarry || authorization != ReaderAuthorization.None));

    internal sealed class SessionAttestation
    {
        private (SessionBinding Binding, long Epoch, bool RequiresWrapperChain)? _proof;
        private (SessionBinding Binding, long Epoch)? _steamProof;
        private bool _reconnecting;
        internal void Revoke() => _proof = null;
        // Keep only immutable parent/game/Steam tracking. Every native reader
        // and controller report must be newly attested after the connection
        // proof and epoch have been renewed by the layout owner.
        internal void ConfirmSourceDisconnected() { _proof = null; _reconnecting = _steamProof is not null; }

        internal bool CanCarry(SessionBinding binding, long epoch, SteamTrackedWrapperChain.ReaderProof? wrapperReader = null) =>
            _proof is { } proof && proof.Binding == binding && epoch > proof.Epoch
                && (!proof.RequiresWrapperChain || wrapperReader?.Current(binding) == true)
            || _reconnecting && _steamProof is { } original && original.Binding.SameRetainedSession(binding) && epoch > original.Epoch;

        internal ReaderAuthorization Authorize(SessionBinding binding, long epoch, bool steamTracked, bool nativeFresh,
            bool sessionCurrent = true, SteamTrackedWrapperChain.ReaderProof? wrapperReader = null)
        {
            if (!sessionCurrent || !nativeFresh || !binding.Valid || epoch <= 0) return ReaderAuthorization.None;
            bool chainCurrent = wrapperReader?.Current(binding) == true;
            if (wrapperReader is not null && !chainCurrent) return ReaderAuthorization.None;
            if (!steamTracked && !chainCurrent && !CanCarry(binding, epoch, wrapperReader)) return ReaderAuthorization.None;
            if (steamTracked) _steamProof = (binding, epoch);
            _proof = (binding, epoch, !steamTracked && chainCurrent);
            _reconnecting = false;
            return steamTracked ? ReaderAuthorization.SteamLog : chainCurrent
                ? ReaderAuthorization.SteamTrackedWrapperChain : ReaderAuthorization.RetainedSession;
        }
    }

    internal static bool NativeSourceMatches(JsonElement packet, int childId, TritonSource source) =>
        packet.TryGetProperty("ready", out var ready) && ready.ValueKind == JsonValueKind.True &&
        packet.TryGetProperty("exactSteamSource", out var exact) && exact.ValueKind == JsonValueKind.True &&
        packet.TryGetProperty("readerPid", out var reader) && reader.ValueKind == JsonValueKind.Number &&
        reader.TryGetInt32(out int pid) && pid == childId &&
        packet.TryGetProperty("sourceSlot", out var slot) && slot.ValueKind == JsonValueKind.Number &&
        slot.TryGetInt32(out int index) && index == source.Slot &&
        packet.TryGetProperty("sourceProduct", out var product) && product.ValueKind == JsonValueKind.Number &&
        product.TryGetUInt16(out ushort productId) && productId == source.Product;

    internal enum InventoryStatus { Current, Suspended, Revoked }

    internal sealed class InventoryWatch(TritonSource source)
    {
        private const long MissingGraceMs = 180;
        private long? _missingSince;

        internal InventoryStatus Observe(SteamVirtualInventory inventory, long now,bool retainAttestedNeutral=false)
        {
            if (inventory.Shadow || inventory.Source is { } other && other != source) return InventoryStatus.Revoked;
            if (inventory.Source == source)
            {
                _missingSince = null;
                return InventoryStatus.Current;
            }
            _missingSince ??= now;
            return retainAttestedNeutral || now - _missingSince.Value < MissingGraceMs ? InventoryStatus.Suspended : InventoryStatus.Revoked;
        }
    }

    internal static bool FreshAfterInventory(long tick,long now,long reattestedAt) => tick>reattestedAt
        &&now-tick is >=0 and <=InputMaxAgeMs;
    internal static XboxReport? ReleasedReport(TimedReport observed,long now,bool releasedReady)
    {
        if(!releasedReady)return null;
        long age=now-observed.Tick;
        return age is >=0 and <=InputMaxAgeMs||age>=0&&observed.Report==default?observed.Report:null;
    }

    internal static TimedReport? ReadFrame(JsonElement packet, uint previous)
    {
        if (!packet.GetProperty("seq").TryGetUInt32(out uint sequence) || sequence <= previous ||
            !packet.GetProperty("tick").TryGetInt64(out long tick) || tick < 0 ||
            !packet.GetProperty("buttons").TryGetUInt16(out ushort buttons) || (buttons & ~0xF3FF) != 0 ||
            !packet.GetProperty("lt").TryGetByte(out byte leftTrigger) ||
            !packet.GetProperty("rt").TryGetByte(out byte rightTrigger) ||
            !packet.GetProperty("lx").TryGetInt16(out short leftX) ||
            !packet.GetProperty("ly").TryGetInt16(out short leftY) ||
            !packet.GetProperty("rx").TryGetInt16(out short rightX) ||
            !packet.GetProperty("ry").TryGetInt16(out short rightY)) return null;
        return new TimedReport(sequence, tick,
            new XboxReport(buttons, leftTrigger, rightTrigger, leftX, leftY, rightX, rightY));
    }

    private sealed class CountedOutput(IXboxRelayOutput output, Action count) : IXboxRelayOutput, IExistingXboxRelayConnection
    {
        public bool ExistingConnection => output is IExistingXboxRelayConnection { ExistingConnection: true };
        public bool TryConnectNeutral(Func<bool> canAttach) => output.TryConnectNeutral(canAttach);
        public bool IsOwned => output.IsOwned;
        public void Submit(XboxReport report) { output.Submit(report); count(); }
        public bool Close() => output.Close();
    }
}

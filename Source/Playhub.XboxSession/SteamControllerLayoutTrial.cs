// Playhub Xbox Session. Distributed under GPL-3.0-only.
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using WSGM.PackagedLaunch;

internal static class SteamControllerLayoutTrial
{
    // STOP provenance from the exact per-reader opening veto, never UI authority.
    internal sealed class OpeningPause(ulong gameId,uint gamePid,long gameBirth)
    {
        private SteamMirrorInputPresentation? captured;
        private bool revoked;
        private bool Game(SteamMirrorInputPresentation value)=>value.GameId==gameId&&value.GamePid==gamePid&&value.GameBirth==gameBirth;
        internal bool Capture(SteamMirrorOpeningStop opening,SteamMirrorInputPresentation witness,Func<bool> current)
        {
            if(revoked||captured is not null||!Game(witness))return false;
            if(!current()){revoked=true;return false;}
            if(revoked||captured is not null||opening.Admission is not {} actual||!Game(actual)||!witness.SamePresentation(actual))return false;
            captured=actual;return true;
        }
        internal RelayExit? Read(SteamMirrorOpeningStop opening,Func<bool> current)
        {
            if(revoked||captured is not {} held)return null;
            if(!current()){revoked=true;return null;}
            if(revoked||captured!=held||opening.Admission is not {} actual||!Game(actual)||!held.SamePresentation(actual))
            {revoked=true;return null;}
            return RelayExit.OverlayOpened;
        }
    }

    private const string Feature = "PlayhubSteamControllerLayoutTrial";
    private const string CompanionClass = "PlayhubSteamCompanion";
    private const uint SteamMenus = 769, Desktop = 413080;
    private const string LayoutMutexName = @"Local\Playhub.SteamControllerLayoutTrial";

    internal static IDisposable? TryStart(TrustedSteamFiles steam, ulong gameId,
        IRetainedSteamGameTarget target, Process parent, Func<bool> foreground, Func<bool> overlay)
    {
        if (!typeof(SteamControllerLayoutTrial).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Any(item => item.Key == Feature && item.Value == "true")) return null;

        return new Worker(token => Run(steam, gameId, target, parent, foreground, overlay, token));
    }

    // The shared relay is admitted only by the same live physical Steam
    // Controller proof as packaged games. Other controllers create no output
    // or layout command. Win32 retains its original verified process identity.
    internal static IDisposable TryStartWin32(TrustedSteamFiles steam, ulong gameId, SafeProcessHandle game, uint pid, long birth,
        Process parent, Func<bool> foreground, Func<bool> overlay, SteamTrackedWrapperChain? readerBootstrap = null)
    {
        bool configured = typeof(SteamControllerLayoutTrial).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Any(item => item.Key == Feature && item.Value == "true") && SteamInputReaderTrial.Configured;
        if (!configured) return TryStartWin32Ordinary(steam,game,pid,birth,parent,foreground,overlay);
        return new Worker(cancellation =>
        {
            try
            {
                if (cancellation.IsCancellationRequested) return;
                using var target = new RetainedWin32InputTarget(game,pid,birth);
                if (target.IsCurrent()) Run(steam,gameId,target,parent,foreground,overlay,cancellation,requireOwnedRelay:true,readerBootstrap:readerBootstrap);
            }
            catch (Exception error) { PackagedLaunchLog.Info($"Win32 Steam Controller relay ended: {error.GetType().Name}."); }
        });
    }

    // Build configurations without the exact reader retain the ordinary path.
    private static IDisposable TryStartWin32Ordinary(TrustedSteamFiles steam, SafeProcessHandle game, uint pid, long birth,
        Process parent, Func<bool> foreground, Func<bool> overlay) => new Worker(cancellation =>
    {
        try
        {
            // Reader-OFF builds use the same command owner as the configured
            // worker. They cannot run an independent global769 issuer.
            using var ownershipMutex=new Mutex(false,LayoutMutexName);
            bool owned;
            try {owned=ownershipMutex.WaitOne(0);}
            catch(AbandonedMutexException)
            {
                ownershipMutex.ReleaseMutex();
                PackagedLaunchLog.Info("Ordinary layout worker refused: previous layout owner ended unexpectedly.");
                return;
            }
            if(!owned)return;
            try
            {
            string root = Path.GetDirectoryName(steam.ComponentPaths.Last())!;
            string inventory = Path.Combine(root, "config", "virtualgamepadinfo.txt");
            using var endpoint = SteamEndpoint.Open(root, steam.UnchangedRoot);
            bool CurrentGame() => !game.IsInvalid && !game.IsClosed && GetProcessId(game) == pid
                && WaitForSingleObject(game, 0) == 258 && GetProcessTimes(game, out long actual, out _, out _, out _)
                && actual == birth;
            bool Current() => !cancellation.IsCancellationRequested && !parent.HasExited && endpoint.Current && CurrentGame();
            bool ExactForeground()
            {
                nint window = GetForegroundWindow();
                uint thread = GetWindowThreadProcessId(window, out uint owner);
                return thread != 0 && owner == pid && Current() && foreground() && !overlay()
                    && GetForegroundWindow() == window && GetWindowThreadProcessId(window, out uint confirmed) == thread
                    && confirmed == pid;
            }
            var policy = new Win32LayoutTransition();
            try
            {
                while (Current())
                {
                    if(SteamNativeOverlayInputPresentationBridge.Read().Command is {} nativeExpected)
                    {
                        var nativeOrdinary=SteamNativeOverlayInputCycle.RunOrdinary(nativeExpected.FullGameId,pid,birth,
                            ()=>endpoint.NoOwnedOverride,Current,()=>Environment.TickCount64,ms=>cancellation.WaitHandle.WaitOne(ms),cancellation,new(endpoint,()=>endpoint.CurrentReceipt));
                        if(nativeOrdinary is {} handled){if(!handled)return;continue;}
                    }
                    if (SteamMirrorInputPresentationBridge.Available)
                    {
                        RunWin32Mirror(steam, endpoint, inventory, game, pid, birth, parent, foreground, cancellation);
                        return;
                    }
                    var controller = SteamInputEvidence.Read(inventory);
                    // A missing Steam Controller may only release an already
                    // observed lease; ordinary Xbox/PS/keyboard sessions do not
                    // acquire this compatibility path in the first place.
                    if (policy.ObserveController(controller.Shadow ? null : controller.Source,
                        overlay(), ExactForeground(), Environment.TickCount64))
                    {
                        var result = endpoint.Send(0, ExactForeground);
                        policy.Restored(result.Started, Environment.TickCount64);
                        PackagedLaunchLog.Info($"Win32 overlay layout reset: started={result.Started}; delivered={result.Delivered}.");
                    }
                    if (cancellation.WaitHandle.WaitOne(50)) break;
                }
            }
            finally
            {
                // On exact process exit release an observed menu override once.
                // A cancellation while the game is alive does not change an
                // external foreground application's layout.
                if (policy.NeedsExitRestore && !CurrentGame() && !parent.HasExited && endpoint.Current
                    && !endpoint.OwnsCompanionForeground())
                {
                    var reset = endpoint.QueueAutomaticRestore();
                    PackagedLaunchLog.Info($"Win32 exited-game layout reset queued={reset.Started}.");
                }
            }
            }
            finally {ownershipMutex.ReleaseMutex();}
        }
        catch (Exception error) { PackagedLaunchLog.Info($"Win32 overlay layout lease ended: {error.GetType().Name}."); }
    });

    private static void RunWin32Mirror(TrustedSteamFiles steam, SteamEndpoint endpoint, string inventory,
        SafeProcessHandle game,uint pid,long birth,Process parent,Func<bool> foreground,CancellationToken cancellation)
    {
        if(!ulong.TryParse(Environment.GetEnvironmentVariable("SteamGameId"),NumberStyles.None,CultureInfo.InvariantCulture,out ulong gameId)
            || (uint)gameId!=0x02000000 || gameId>>32<0x80000000)return;
        using var live=SteamControllerSourceProbe.LiveSource.TryOpen(steam.ComponentPaths.Last());
        using var self=Process.GetCurrentProcess();
        if(live is null)
        {
            ServeOrdinaryMenus(gameId,pid,birth,endpoint,
                ()=>!cancellation.IsCancellationRequested&&!parent.HasExited&&endpoint.Current&&!game.IsInvalid&&!game.IsClosed
                    &&GetProcessId(game)==pid&&WaitForSingleObject(game,0)==258&&GetProcessTimes(game,out long actual,out _,out _,out _)&&actual==birth,
                foreground,cancellation);
            return;
        }
        SteamInputOrderedIssuer? issuer=null;
        bool Alive()=>!cancellation.IsCancellationRequested&&!parent.HasExited&&endpoint.Current&&!game.IsInvalid&&!game.IsClosed
            &&GetProcessId(game)==pid&&WaitForSingleObject(game,0)==258&&GetProcessTimes(game,out long actual,out _,out _,out _)&&actual==birth;
        var epochs=new RelayFocusEpoch();
        var ordinaryMirror=new SteamOrdinaryMirrorMenuCycle(gameId,pid,birth);
        try
        {
            while(Alive()&&SteamMirrorInputPresentationBridge.Available)
            {
                var nativeOrdinary=SteamNativeOverlayInputCycle.RunOrdinary(gameId,pid,birth,
                    ()=>endpoint.NoOwnedOverride,Alive,()=>Environment.TickCount64,ms=>cancellation.WaitHandle.WaitOne(ms),cancellation,new(endpoint,()=>endpoint.CurrentReceipt));
                if(nativeOrdinary is {} handled){if(!handled)return;epochs.Reset();continue;}
                var mirrorOrdinary=RunOrdinaryMirrorMenu(ordinaryMirror,gameId,pid,birth,endpoint,Alive,foreground,cancellation);
                if(mirrorOrdinary is {} mirrorHandled){if(!mirrorHandled)return;epochs.Reset();continue;}
                nint window=GetForegroundWindow();uint thread=GetWindowThreadProcessId(window,out uint owner);
                bool GameForeground()=>thread!=0&&owner==pid&&foreground()&&Alive()&&GetForegroundWindow()==window
                    &&GetWindowThreadProcessId(window,out uint fresh)==thread&&fresh==pid&&GetForegroundWindow()==window;
                var inventoryState=SteamInputEvidence.Read(inventory);
                var proof=inventoryState.Source is {Valid:true} source&&GameForeground()?live.Sample(source):default;
                bool unavailable=inventoryState.Shadow||proof.Status!=SteamControllerSourceProbe.ConnectionStatus.Connected;
                if(epochs.ShouldStart(GameForeground()?window:0,inventoryState.Source,unavailable,proof.RawDevice)
                    &&inventoryState.Source is {Valid:true} physical)
                {
                    var lease=new SteamControllerSourceLease(physical);
                    bool PhysicalCurrent()=>Alive()&&lease.Current(SteamInputEvidence.Read(inventory))&&live.Current(physical,proof.RawDevice);
                    SteamMirrorInputPresentation? ReadMirror()=>SteamMirrorInputCycle.ReadCurrent(gameId,pid,birth,GameForeground);
                    if(ReadMirror() is not { } presentation||!presentation.Shown(Environment.TickCount64)){epochs.Reset();}
                    else
                    {
                        var binding=new SteamRawInputRelayTrial.SessionBinding(gameId,self.Id,self.StartTime.ToFileTimeUtc(),pid,birth,
                            physical,steam,window,thread,owner);
                        issuer=new SteamInputOrderedIssuer(binding,SteamInputOrderedIssuer.InputRoute.SteamOrdinary,
                            ()=>endpoint.Current,Alive,PhysicalCurrent,()=>Environment.TickCount64,
                            expected=>ReadMirror() is {} m&&m.Shown(Environment.TickCount64)&&m.SessionId==expected.SessionId&&m.Generation==expected.Generation,
                            ()=>false,endpoint);
                        if(endpoint.CurrentReceipt is {} previous&&!issuer.AdoptPrior(previous))return;
                        var result=SteamMirrorInputCycle.Run((uint)(gameId>>32),epochs.Generation,binding,issuer,Alive,GameForeground,
                            ReadMirror,()=>default,_=>{},()=>Environment.TickCount64,ms=>Thread.Sleep(ms),cancellation,ordinary:true);
                        PackagedLaunchLog.Info($"Win32 mirror input: automatic-cleanup={result.CleanupConfirmed}; no-owned-PDO=True.");
                        if(!result.CleanupConfirmed)return;
                        issuer=null;epochs.Reset();
                    }
                }
                if(cancellation.WaitHandle.WaitOne(50))break;
            }
        }
        finally
        {
            if(issuer is not null)SteamMirrorInputCycle.Release(issuer,()=>Environment.TickCount64,ms=>Thread.Sleep(ms));
        }
    }

    private static bool? RunOrdinaryMirrorMenu(SteamOrdinaryMirrorMenuCycle cycle,ulong gameId,uint gamePid,long gameBirth,
        SteamEndpoint endpoint,Func<bool> current,Func<bool> foreground,CancellationToken cancellation)
    {
        bool ExactForeground()
        {
            nint window=GetForegroundWindow();uint thread=GetWindowThreadProcessId(window,out uint owner);
            return thread!=0&&owner==gamePid&&current()&&foreground()&&current()&&GetForegroundWindow()==window
                &&GetWindowThreadProcessId(window,out uint last)==thread&&last==gamePid&&GetForegroundWindow()==window;
        }
        var result=cycle.Run(()=>SteamMirrorInputCycle.ReadCurrent(gameId,gamePid,gameBirth,ExactForeground),
            SteamMirrorInputPresentationBridge.Read,()=>endpoint.NoOwnedOverride,current,
            ()=>Environment.TickCount64,ms=>cancellation.WaitHandle.WaitOne(ms),cancellation,new(endpoint,()=>endpoint.CurrentReceipt),
            stage=>PackagedLaunchLog.Info($"Steam mirror ordinary-menu: stage={stage}; gamePid={gamePid}; physical-SC-claimed=False; owned-PDO=False; layout-consumption-confirmed=False."));
        if(result is not {} outcome)return null;
        PackagedLaunchLog.Info($"Steam mirror ordinary-menu: gamePid={gamePid}; menu-command={outcome.MenuCommandStarted}; menu-launcher-receipt={outcome.MenuLauncherAcknowledged}; automatic-cleanup={outcome.AutomaticCleanup}; reason={outcome.EndReason}; physical-SC-claimed=False; owned-PDO=False; layout-consumption-confirmed=False.");
        return outcome.AutomaticCleanup;
    }

    private static void ServeOrdinaryMenus(ulong gameId,uint gamePid,long gameBirth,SteamEndpoint endpoint,
        Func<bool> current,Func<bool> foreground,CancellationToken cancellation)
    {
        var mirror=new SteamOrdinaryMirrorMenuCycle(gameId,gamePid,gameBirth);
        while(current()&&!cancellation.IsCancellationRequested)
        {
            var native=SteamNativeOverlayInputCycle.RunOrdinary(gameId,gamePid,gameBirth,()=>endpoint.NoOwnedOverride,
                current,()=>Environment.TickCount64,ms=>cancellation.WaitHandle.WaitOne(ms),cancellation,new(endpoint,()=>endpoint.CurrentReceipt));
            if(native is {} n){if(!n)return;continue;}
            var ordinary=RunOrdinaryMirrorMenu(mirror,gameId,gamePid,gameBirth,endpoint,current,foreground,cancellation);
            if(ordinary is {} m){if(!m)return;continue;}
            if(cancellation.WaitHandle.WaitOne(100))return;
        }
    }

    internal sealed class Win32LayoutTransition
    {
        private bool _open, _pending, _owned;
        private int _attempts;
        private long _retryAt;
        internal bool NeedsExitRestore => _owned;
        internal bool ObserveController(TritonSource? controller, bool overlayOpen, bool exactGameForeground, long now)
        {
            bool present = controller is { Valid: true };
            return (present || _owned) && Observe(present && overlayOpen, exactGameForeground, now);
        }
        internal bool Observe(bool overlayOpen, bool exactGameForeground, long now = 0)
        {
            if (overlayOpen) { _open = _owned = true; _pending = false; return false; }
            if (_open) { _open = false; _pending = true; _attempts = 0; _retryAt = 0; }
            if (!_pending || !exactGameForeground || now < _retryAt || _attempts >= 3) return false;
            _pending = false;
            _attempts++;
            return true;
        }
        internal void Restored(bool started, long now = 0)
        {
            if (started) { _owned = false; return; }
            // A closing surface can win a foreground race after Observe.
            // Keep the lease until a bounded retry can reset the real game.
            _pending = true;
            _retryAt = now + 500;
        }
    }

    private static void Run(TrustedSteamFiles steam, ulong gameId, IRetainedSteamGameTarget target,
        Process parent, Func<bool> foreground, Func<bool> overlay, CancellationToken cancellation, bool requireOwnedRelay = false,
        SteamTrackedWrapperChain? readerBootstrap = null)
    {
        try
        {
            uint app = (uint)(gameId >> 32);
            if (app < 0x80000000 || (uint)gameId != 0x02000000
                || !ulong.TryParse(Environment.GetEnvironmentVariable("SteamGameId"), NumberStyles.None,
                    CultureInfo.InvariantCulture, out ulong actual) || actual != gameId) return;

            string steamRoot = Path.GetDirectoryName(steam.ComponentPaths.Last())!;
            string inventory = Path.Combine(steamRoot, "config", "virtualgamepadinfo.txt");
            string? published = Environment.GetEnvironmentVariable("SteamVirtualGamepadInfo");
            if (string.IsNullOrEmpty(published) || !Path.IsPathFullyQualified(published)
                || !string.Equals(Path.GetFullPath(published), inventory, StringComparison.OrdinalIgnoreCase))
            {
                PackagedLaunchLog.Info("Steam layout trial refused: exact Steam inventory was not inherited.");
                return;
            }

            using var mutex = new Mutex(false, LayoutMutexName);
            bool acquired;
            try { acquired = mutex.WaitOne(0); }
            catch (AbandonedMutexException)
            {
                mutex.ReleaseMutex();
                PackagedLaunchLog.Info("Steam layout trial refused: a previous layout lease ended unexpectedly.");
                return;
            }
            if (!acquired) return;
            SteamRawInputRelayTrial? relay = null;
            try
            {
                using var endpoint = SteamEndpoint.Open(steamRoot, steam.UnchangedRoot);
                using var prototypeFence = ExperimentalInputFence.TryStart(steam,gameId,target,parent,cancellation,()=>endpoint.Current);
                using var liveSource = SteamControllerSourceProbe.LiveSource.TryOpen(steam.ComponentPaths.Last());
                if (liveSource is null)
                {
                    ServeOrdinaryMenus(gameId,target.Pid,target.Creation,endpoint,
                        ()=>!cancellation.IsCancellationRequested&&!parent.HasExited&&target.IsCurrent()&&endpoint.Current,
                        foreground,cancellation);
                    return;
                }
                bool relayPrepared = false;
                string? retainedFailure = null;
                bool FailRetained(string reason) { retainedFailure ??= reason; return false; }
                bool Alive()
                {
                    if (cancellation.IsCancellationRequested) return FailRetained("layout-cancelled");
                    if (parent.HasExited) return FailRetained("wrapper-exited");
                    if (!target.IsCurrent()) return FailRetained("retained-target-not-current");
                    if (!endpoint.Current) return FailRetained("retained-steam-endpoint-not-current");
                    if (readerBootstrap is not null && !readerBootstrap.Current) return FailRetained("tracked-wrapper-reader-chain-not-current");
                    return true;
                }
                var openingStop = new SteamMirrorOpeningStop(gameId,target.Pid,target.Creation);
                bool OpeningBlocks() { openingStop.Observe(SteamMirrorInputPresentationBridge.Read(),Environment.TickCount64);return openingStop.Blocking; }
                bool ExactScope() => Alive() && foreground() && (SteamMirrorInputPresentationBridge.Available
                    ? !OpeningBlocks() : !overlay());
                using var companion = Process.GetCurrentProcess();
                SteamInputOrderedIssuer? mirrorIssuer = null;
                SteamRawInputRelayTrial.SessionBinding? mirrorBinding = null;
                SteamRawInputRelayTrial.ReaderOutcome pausedReader = default;
                SteamRawInputRelayTrial.ReaderOutcome freshAdmission = default;
                Func<SteamMirrorInputPresentation?>? pausedMirrorRead = null;
                var epochs = new RelayFocusEpoch();
                var ordinaryMirror=new SteamOrdinaryMirrorMenuCycle(gameId,target.Pid,target.Creation);
                TritonSource? connectedSource = null;
                long resumeSampleEpoch=0;
                try
                {
                while (Alive())
                {
                    if (freshAdmission.FreshAdmissionRequired)
                    {
                        // This is a new admission after terminal detach, not a
                        // pause/resume permission and never an inputReady report.
                        var expectedSource = connectedSource;
                        bool FreshSource()
                        {
                            var evidence = SteamInputEvidence.Read(inventory);
                            return expectedSource is {Valid:true} exact && !evidence.Shadow && evidence.Source == exact
                                && liveSource.Read(exact) == SteamControllerSourceProbe.ConnectionStatus.Connected;
                        }
                        if (CanReadmit(freshAdmission, ExactScope, FreshSource,
                            () => endpoint.NoOwnedOverride, () => relay?.NoSessionOutput == true))
                        {
                            if (relay?.RetireForFreshAdmission(freshAdmission) != true) return;
                            PackagedLaunchLog.Info($"Steam input fresh admission scheduled: previous-reader={freshAdmission.ReaderPid}; previous-epoch={freshAdmission.Epoch}; reason=terminal-scope-loss; old-reader-resumed=False.");
                            freshAdmission=default;mirrorIssuer=null;mirrorBinding=null;pausedReader=default;pausedMirrorRead=null;epochs.Reset();
                        }
                        else { if(cancellation.WaitHandle.WaitOne(100))return;continue; }
                    }
                    nint window = ExactScope() ? GetForegroundWindow() : 0;
                    SteamVirtualInventory inventoryState = SteamInputEvidence.Read(inventory);
                    bool sourceUnavailable = inventoryState.Shadow || inventoryState.Source is not { Valid: true };
                    if(sourceUnavailable&&(relay is null||relay.NoSessionOutput))
                    {
                        var ordinaryNative=SteamNativeOverlayInputCycle.RunOrdinary(gameId,target.Pid,target.Creation,
                            ()=>endpoint.NoOwnedOverride,Alive,()=>Environment.TickCount64,ms=>cancellation.WaitHandle.WaitOne(ms),cancellation,new(endpoint,()=>endpoint.CurrentReceipt));
                        if(ordinaryNative is {} handled){if(!handled)return;epochs.Reset();continue;}
                    }
                    if(relay is null||relay.NoSessionOutput)
                    {
                        var mirrorOrdinary=RunOrdinaryMirrorMenu(ordinaryMirror,gameId,target.Pid,target.Creation,endpoint,Alive,foreground,cancellation);
                        if(mirrorOrdinary is {} mirrorHandled){if(!mirrorHandled)return;epochs.Reset();continue;}
                    }
                    if (!sourceUnavailable && !relayPrepared)
                    {
                        relayPrepared = true;
                        if (SteamInputReaderTrial.Configured) relay = SteamRawInputRelayTrial.Prepare();
                    }
                    // A Win32 relay route must never force a game profile if
                    // its exact reader/output could not be prepared.
                    if (!sourceUnavailable && requireOwnedRelay && relay is null)
                    {
                        PackagedLaunchLog.Info("Win32 Steam Controller relay refused: exact owned output unavailable; no layout command sent.");
                        return;
                    }
                    if (connectedSource is {} heldInventory && inventoryState.Source is null && !inventoryState.Shadow
                        && liveSource.Read(heldInventory)!=SteamControllerSourceProbe.ConnectionStatus.Disconnected)
                    {
                        // Unknown metadata never switches/revokes the physical
                        // source. No new layout or reader is admitted here.
                        epochs.Reset();
                        if(cancellation.WaitHandle.WaitOne(100))return;
                        continue;
                    }
                    if (connectedSource != inventoryState.Source || sourceUnavailable && connectedSource is not null)
                    {
                        relay?.RevokeController();
                        if(mirrorIssuer is not null)
                        {
                            if(!SteamMirrorInputCycle.Release(mirrorIssuer,()=>Environment.TickCount64,ms=>Thread.Sleep(ms)))return;
                            mirrorIssuer=null;mirrorBinding=null;pausedReader=default;pausedMirrorRead=null;
                        }
                        epochs.Reset();
                        connectedSource = sourceUnavailable ? null : inventoryState.Source;
                    }
                    if(mirrorIssuer is not null&&pausedReader.CanResume&&inventoryState.Source is {Valid:true} heldSource)
                    {
                        var connection=liveSource.Read(heldSource);
                        if(connection==SteamControllerSourceProbe.ConnectionStatus.Disconnected)
                        {
                            if(relay?.ConfirmDisconnectedHeld(pausedReader,connection)!=true
                                ||!SteamMirrorInputCycle.Release(mirrorIssuer,()=>Environment.TickCount64,ms=>Thread.Sleep(ms)))return;
                            mirrorIssuer=null;mirrorBinding=null;pausedReader=default;pausedMirrorRead=null;epochs.Reset();
                            if(cancellation.WaitHandle.WaitOne(100))return;
                            continue;
                        }
                    }
                    if(mirrorIssuer is not null&&mirrorBinding is {} heldBinding&&relay?.NeutralHeld==true&&pausedReader.CanResume
                        &&(OpeningBlocks()||pausedMirrorRead?.Invoke()?.Prepared(Environment.TickCount64)==true))
                    {
                        if(!SteamMirrorInputCycle.ResumePresentation(pausedReader.Epoch,heldBinding,pausedReader,mirrorIssuer,Alive,
                            pausedMirrorRead!,()=>Environment.TickCount64,ms=>Thread.Sleep(ms),cancellation,opening:openingStop))return;
                        if(cancellation.WaitHandle.WaitOne(30))return;
                        continue;
                    }
                    var initialProof = window != 0 && inventoryState.Source is { Valid: true } initial
                        ? liveSource.Sample(initial) : default;
                    bool sourceReady = initialProof.Status == SteamControllerSourceProbe.ConnectionStatus.Connected;
                    if(pausedReader.CanResume&&resumeSampleEpoch!=pausedReader.Epoch)
                    {
                        try { PackagedLaunchLog.Info($"Steam input resume sample: reader={pausedReader.ReaderPid}; previous-epoch={pausedReader.Epoch}; reason={pausedReader.EndReason}; status={initialProof.Status}; raw-device=0x{initialProof.RawDevice:X}; inventory-current={!sourceUnavailable}; previous-neutral-held={pausedReader.NeutralHeld}."); } catch { }
                        resumeSampleEpoch=pausedReader.Epoch;
                    }
                    if (epochs.ShouldStart(window, inventoryState.Source, sourceUnavailable || !sourceReady, initialProof.RawDevice))
                    {
                        PackagedLaunchLog.Info($"Steam controller layout connection verified: proof={(initialProof.RawDevice == 0 ? "Steam XInput" : "exact RawInput")}; stable-samples=2.");
                        var sourceLease = new SteamControllerSourceLease(inventoryState.Source!.Value);
                        uint thread = GetWindowThreadProcessId(window, out uint owner);
                        bool lost = false;
                        bool sourceDisconnected = false;
                        SteamRawInputRelayTrial.ReaderOutcome readerOutcome = default;
                        SteamNativeOverlayInputPresentationBridge.Command? nativeAdmission=null;
                        var initialConnection = new SteamControllerSourceProbe.InitialConnection(
                            () => liveSource.Current(inventoryState.Source.Value, initialProof.RawDevice));
                        var openingPause = new OpeningPause(gameId,target.Pid,target.Creation);
                        bool PauseSourceCurrent()=>Alive()&&!sourceLease.Revoked&&initialConnection.Current()&&Alive()&&!sourceLease.Revoked;
                        bool GameWindowCurrent()=>GetForegroundWindow()==window
                            &&(owner==target.Pid||SteamControllerUwpFrameWindow.Matches(window,target.Aumid))
                            &&GetWindowThreadProcessId(window,out uint currentOwner)==thread&&currentOwner==owner
                            &&GetForegroundWindow()==window;
                        bool OwnsExactWindow()
                        {
                            // Only the actual authenticated opening veto may be
                            // remembered as a pause; a device/window failure may not.
                            if(lost)return false;
                            SteamMirrorInputPresentation? openingVeto=null;
                            bool ReaderScope()
                            {
                                if(!Alive()||!foreground())return false;
                                if(!SteamMirrorInputPresentationBridge.Available)return !overlay();
                                if(!OpeningBlocks())return true;
                                openingVeto=openingStop.Admission;return false;
                            }
                            bool exact=!sourceLease.Revoked&&initialConnection.Current()
                                &&thread!=0&&owner!=0&&GetForegroundWindow()==window&&ReaderScope()
                                &&(owner==target.Pid||SteamControllerUwpFrameWindow.Matches(window,target.Aumid))
                                &&GetForegroundWindow()==window
                                &&GetWindowThreadProcessId(window,out uint currentOwner)==thread&&currentOwner==owner;
                            if(!exact&&openingVeto is {} witness)
                                openingPause.Capture(openingStop,witness,()=>PauseSourceCurrent()&&GameWindowCurrent());
                            lost=!exact;return exact;
                        }

                        bool MirrorGameForeground() => Alive() && foreground() && GetForegroundWindow()==window
                            && GetWindowThreadProcessId(window,out uint actualOwner)==thread && actualOwner==owner
                            && (owner==target.Pid || SteamControllerUwpFrameWindow.Matches(window,target.Aumid))
                            && GetForegroundWindow()==window && Alive();
                        SteamMirrorInputPresentation? ReadMirror() => SteamMirrorInputCycle.ReadCurrent(gameId,target.Pid,
                            target.Creation,MirrorGameForeground);
                        RelayExit? ReadPause()
                        {
                            // This is actual scope/menu state, not the latched
                            // OwnsExactWindow failure and never a fabricated scope2.
                            if (!Alive() || sourceLease.Revoked || !initialConnection.Current()) return null;
                            bool menu = SteamMirrorInputPresentationBridge.Available
                                ? OpeningBlocks() : overlay();
                            bool gameForeground = foreground();
                            if (sourceLease.Revoked || !Alive()) return null;
                            if(SteamNativeOverlayInputCycle.Pending(gameId,target.Pid,target.Creation,out var native))
                            {nativeAdmission??=native;return RelayExit.OverlayOpened;}
                            return menu ? RelayExit.OverlayOpened : !gameForeground ? RelayExit.FocusLost : null;
                        }

                        CommandResult Send(uint context, Func<bool>? guard)
                        {
                            var sent = endpoint.Send(context, guard);
                            if (context == app) initialConnection.LayoutStarted(sent.Started);
                            return sent;
                        }
                        bool ReaderSessionCurrent()
                        {
                            if (!Alive()) return false;
                            if (sourceLease.Revoked)
                                return FailRetained("layout-source-lease: " + sourceLease.FailureText);
                            return Alive();
                        }
                        if (SteamMirrorInputPresentationBridge.Available
                            || SteamNativeOverlayInputCycle.Pending(gameId,target.Pid,target.Creation,out _))
                        {
                            var binding = new SteamRawInputRelayTrial.SessionBinding(gameId,companion.Id,companion.StartTime.ToFileTimeUtc(),
                                target.Pid,target.Creation,inventoryState.Source.Value,steam,window,thread,owner);
                            if (mirrorBinding is {} old && old!=binding)
                            {
                                if(mirrorIssuer is not null&&!SteamMirrorInputCycle.Release(mirrorIssuer,()=>Environment.TickCount64,ms=>Thread.Sleep(ms)))return;
                                mirrorIssuer=null;
                            }
                            mirrorBinding=binding;
                            bool PhysicalCurrent()=>Alive()&&sourceLease.Current(SteamInputEvidence.Read(inventory))&&initialConnection.Current();
                            bool newIssuer=mirrorIssuer is null;
                            mirrorIssuer ??=new SteamInputOrderedIssuer(binding,SteamInputOrderedIssuer.InputRoute.OwnedSessionPad,
                                ()=>endpoint.Current,Alive,PhysicalCurrent,()=>Environment.TickCount64,
                                lease=>ReadMirror() is {} m&&m.Shown(Environment.TickCount64)&&m.SessionId==lease.SessionId&&m.Generation==lease.Generation,
                                ()=>relay?.NeutralHeld==true,endpoint,
                                native=>SteamNativeOverlayInputPresentationBridge.Read().Command is {} command
                                    &&native.Owner==Guid.ParseExact(command.OwnerToken,"N")
                                    &&native.SessionId==command.SessionId&&native.Generation==command.Generation
                                    &&native.HostPid==command.HostPid&&native.HostBirth==command.HostBirth
                                    &&native.BrowserId==command.BrowserId&&native.NativeAppId==command.NativeAppId
                                    &&native.ObservedGameId==command.ObservedGameId&&native.GameId==command.FullGameId
                                    &&command.Serial>=native.AdmissionSerial&&endpoint.SameSteam(command.SteamPid,command.SteamBirth)
                                    &&SteamNativeOverlayInputPresentationBridge.AuthorityCurrent(command)&&MirrorGameForeground());
                            if(newIssuer&&endpoint.CurrentReceipt is {} previous&&!mirrorIssuer.AdoptPrior(previous))return;
                            var cycle=SteamMirrorInputCycle.Run(app,epochs.Generation,binding,mirrorIssuer,Alive,OwnsExactWindow,ReadMirror,()=>
                            {
                                pausedReader=default;pausedMirrorRead=null;retainedFailure=null;
                                SteamInputReaderTrial.TryRun(steam,gameId,window,thread,owner,OwnsExactWindow,cancellation,relay,
                                    inventoryState.Source,target,epochs.Generation,()=>{},out readerOutcome,
                                    ReaderSessionCurrent,ReadPause,()=>retainedFailure,readerBootstrap,
                                    ()=>openingPause.Read(openingStop,PauseSourceCurrent));
                                return readerOutcome;
                            },initialConnection.LayoutStarted,()=>Environment.TickCount64,
                                ms=>cancellation.WaitHandle.WaitOne(ms),cancellation,opening:openingStop,
                                nativePause:reader=>nativeAdmission is {} native
                                    ?SteamNativeOverlayInputCycle.Run(native,epochs.Generation,binding,reader,mirrorIssuer,Alive,
                                        ()=>relay?.NeutralHeld==true,()=>Environment.TickCount64,ms=>cancellation.WaitHandle.WaitOne(ms),cancellation,
                                        ()=>liveSource.Read(inventoryState.Source.Value)==SteamControllerSourceProbe.ConnectionStatus.Disconnected)
                                    :null);
                            PackagedLaunchLog.Info($"Steam mirror input cycle: game-command={cycle.GameCommandAcknowledged}; automatic-cleanup={cycle.CleanupConfirmed}; reader={readerOutcome.ReaderPid}; epoch={epochs.Generation}.");
                            sourceDisconnected=Alive()&&CanReconnectSource(readerOutcome,liveSource.Read(inventoryState.Source.Value));
                            if(sourceDisconnected){sourceLease.Revoke();relay?.ConfirmSourceDisconnected();mirrorIssuer=null;mirrorBinding=null;}
                            if(!cycle.CleanupConfirmed)return;
                            if(!readerOutcome.CanResume&&!sourceDisconnected)
                            {
                                if(!readerOutcome.FreshAdmissionRequired||relay?.NoSessionOutput!=true)return;
                                freshAdmission=readerOutcome;
                                PackagedLaunchLog.Info($"Steam input fresh admission waiting: previous-reader={readerOutcome.ReaderPid}; previous-epoch={readerOutcome.Epoch}; cleanup-complete=True; old-reader-resumed=False.");
                            }
                            if(readerOutcome.CanResume){pausedReader=readerOutcome;pausedMirrorRead=ReadMirror;}
                            epochs.Reset();
                            continue;
                        }
                        var result = RunLease(app, OwnsExactWindow, Send, endpoint.KnownForegroundContext, queueExitRestore =>
                        {
                            PackagedLaunchLog.Info("Steam layout trial: scoped game layout command delivered.");
                            retainedFailure = null;
                            if (!SteamInputReaderTrial.TryRun(steam, gameId, window, thread, owner, OwnsExactWindow,
                                cancellation, relay, inventoryState.Source, target, epochs.Generation, queueExitRestore, out readerOutcome,
                                ReaderSessionCurrent, ReadPause, () => retainedFailure, readerBootstrap,
                                ()=>openingPause.Read(openingStop,PauseSourceCurrent)))
                                SteamControllerSourceProbe.RunLayoutTrial(steam, gameId, target, parent, OwnsExactWindow, cancellation);
                            sourceDisconnected = Alive() && CanReconnectSource(readerOutcome,
                                liveSource.Read(inventoryState.Source.Value));
                            if (sourceDisconnected)
                            {
                                // This happens after neutral/detach and before
                                // RunLease's automatic-layout restoration.
                                sourceLease.Revoke();
                                relay?.ConfirmSourceDisconnected();
                            }
                        }, () =>
                        {
                            while (Alive() && endpoint.OwnsCompanionForeground())
                                if (cancellation.WaitHandle.WaitOne(30)) break;
                        }, () => target.HasExited, endpoint.QueueAutomaticRestore, endpoint.OwnsCompanionForeground,
                            () => sourceLease.Current(SteamInputEvidence.Read(inventory)));
                        PackagedLaunchLog.Info($"Steam layout trial: delivered={result.Delivered}; automatic-restored={result.Restored}; "
                            + $"automatic-restore-queued={result.RestoreQueued}.");
                        if (sourceLease.Revoked) relay?.RevokeController();
                        if (relay is null) return;
                        if (!result.Restored)
                        {
                            // A launcher may outlive Send's first acknowledgement
                            // budget. Drain only our exact pending automatic0;
                            // never let that old0 arrive after a new game layout.
                            bool RecoveryCurrent() => Alive() && !sourceLease.Revoked
                                && sourceLease.Current(SteamInputEvidence.Read(inventory))
                                && liveSource.Current(inventoryState.Source.Value, initialProof.RawDevice) && Alive();
                            var recovery = new AutomaticRestoreRecovery();
                            // Recovery uses an explicit neutral-held disposition,
                            // never calls that connected PDO terminally detached.
                            bool recovered = recovery.TryRecover(readerOutcome, lost, RecoveryCurrent,
                                endpoint.ReadAutomaticRestore,
                                () => endpoint.DrainAutomaticRestore(RecoveryCurrent),
                                () => endpoint.Send(0, () => RecoveryCurrent() && !endpoint.OwnsCompanionForeground()));
                            PackagedLaunchLog.Info($"Steam layout automatic restore recovery: acknowledged={recovered}.");
                            if (!recovered) return;
                            // Retire this local source lease without revoking the
                            // relay's retained parent proof. The next iteration
                            // creates a fresh lease and two-sample connection.
                            sourceLease.Revoke();
                            epochs.Reset();
                        }
                        else if (lost || sourceDisconnected) epochs.Reset();
                        // A focus pause can reuse the same PDO only with an
                        // authenticated explicit terminal and a retired reader.
                        if (readerOutcome.NeutralHeld && !readerOutcome.CanResume) return;
                        if (!readerOutcome.CanResume && !sourceDisconnected)
                        {
                            if(!readerOutcome.FreshAdmissionRequired||relay.NoSessionOutput!=true)return;
                            freshAdmission=readerOutcome;
                            PackagedLaunchLog.Info($"Steam input fresh admission waiting: previous-reader={readerOutcome.ReaderPid}; previous-epoch={readerOutcome.Epoch}; cleanup-complete=True; old-reader-resumed=False.");
                            epochs.Reset();
                        }
                    }
                    if (cancellation.WaitHandle.WaitOne(100)) return;
                }
                }
                finally
                {
                    CleanupMirror(() => relay?.Dispose(), () =>
                    {
                        try
                        {
                            if (mirrorIssuer is not null) SteamMirrorInputCycle.Release(mirrorIssuer,()=>Environment.TickCount64,
                                ms=>Thread.Sleep(ms));
                        }
                        finally { endpoint.RestoreExitedGameLayout(() => target.HasExited); }
                    });
                }
            }
            finally { try { relay?.Dispose(); } finally { mutex.ReleaseMutex(); } }
        }
        catch (Exception error)
        {
            PackagedLaunchLog.Info($"Steam layout trial stopped: {error.GetType().Name}.");
        }
    }

    // Output cleanup may fail; never skip our separately owned command cleanup.
    internal static void CleanupMirror(Action releaseOutput,Action releaseCommand)
    {
        try { releaseOutput(); } finally { releaseCommand(); }
    }

    internal readonly record struct CommandResult(bool Started, bool Delivered);
    internal readonly record struct LeaseResult(bool Delivered, bool Restored, bool RestoreQueued = false);

    // Scheduling only. The ordinary epoch still reattests source, window,
    // layout, actual child tracking/descriptor and two fresh released reports.
    internal static bool CanReadmit(SteamRawInputRelayTrial.ReaderOutcome previous,Func<bool> scopeCurrent,
        Func<bool> sourceConnected,Func<bool> automaticSettled,Func<bool> noOutput)
    {
        if(!previous.FreshAdmissionRequired||!noOutput()||!scopeCurrent())return false;
        if(!sourceConnected()||!automaticSettled()||!scopeCurrent())return false;
        return sourceConnected()&&automaticSettled()&&scopeCurrent()&&noOutput();
    }

    internal static bool CanReconnectSource(SteamRawInputRelayTrial.ReaderOutcome outcome,
        SteamControllerSourceProbe.ConnectionStatus connection) => outcome.Attested && outcome.CleanupConfirmed
        && (outcome.SourceRemoved || connection == SteamControllerSourceProbe.ConnectionStatus.Disconnected);

    internal enum AutomaticRestoreStatus { Missing, Pending, Acknowledged, OtherCommand, Failed, Unknown }

    internal sealed class AutomaticRestoreRecovery
    {
        private bool _attempted;
        internal bool TryRecover(SteamRawInputRelayTrial.ReaderOutcome reader, bool focusLost,
            Func<bool> current, Func<AutomaticRestoreStatus> read, Func<AutomaticRestoreStatus> drain,
            Func<CommandResult> sendAutomatic)
        {
            if (_attempted) return false;
            _attempted = true;
            try
            {
                // An unexplained EOF or failed neutral/detach cannot renew a
                // bridge. A real focus loss and current retained source are required.
                if (!reader.Attested || !(reader.CleanupConfirmed || reader.CanResume) || reader.SourceRemoved || !focusLost
                    || reader.EndReason is not (RelayExit.FocusLost or RelayExit.OverlayOpened) || !current()) return false;
                var status = read();
                if (!current()) return false;
                if (status == AutomaticRestoreStatus.Missing)
                {
                    // No automatic command was started. One ordered cleanup is
                    // permitted; the dispatcher rechecks ownership next to spawn.
                    var sent = sendAutomatic();
                    if (!sent.Started || !current()) return false;
                    status = read();
                }
                if (status == AutomaticRestoreStatus.Pending) status = drain();
                return status == AutomaticRestoreStatus.Acknowledged && current();
            }
            catch { return false; }
        }
    }

    internal static AutomaticRestoreStatus DrainAutomaticRestore(Func<AutomaticRestoreStatus> read,
        Func<bool> current, Func<long> clock, Action<int> wait)
    {
        try
        {
            long deadline = checked(clock() + 1500);
            // Both elapsed time and a fixed batch limit bound an uncertain
            // clock/provider. No polling or retries survive this operation.
            for (int sample = 0; sample < 32; sample++)
            {
                if (!current()) return AutomaticRestoreStatus.Unknown;
                var status = read();
                if (!current() || clock() > deadline) return AutomaticRestoreStatus.Unknown;
                if (status != AutomaticRestoreStatus.Pending) return status;
                long remaining = deadline - clock();
                if (remaining <= 0) return AutomaticRestoreStatus.Unknown;
                wait((int)Math.Min(50, remaining));
            }
        }
        catch { }
        return AutomaticRestoreStatus.Unknown;
    }

    internal sealed class RelayFocusEpoch
    {
        private nint _window;
        private TritonSource? _candidate, _lastAttempted;
        private nint _candidateRawDevice;
        private bool _sawCandidate, _attempted;
        internal long Generation { get; private set; }

        internal bool ShouldStart(nint window, TritonSource? source, bool sourceUnavailable = false, nint rawDevice = 0)
        {
            if (window != _window)
            {
                Reset();
                _window = window;
            }
            if (window == 0 || sourceUnavailable || source is not { Valid: true })
            {
                _sawCandidate = false;
                return false;
            }
            if (!_sawCandidate || _candidate != source || _candidateRawDevice != rawDevice)
            {
                _candidate = source;
                _candidateRawDevice = rawDevice;
                _sawCandidate = true;
                return false;
            }
            if (_attempted && _lastAttempted == source) return false;
            _lastAttempted = source;
            return _attempted = true;
        }

        internal void Reset() { _window = 0; _sawCandidate = _attempted = false; Generation++; }
    }

    internal static LeaseResult RunLease(uint app, Func<bool> ownsInput, Func<uint, Func<bool>?, CommandResult> send,
        Func<uint> knownRestore, Action<Action> probe, Action? waitForCompanionFocusToEnd = null, Func<bool>? gameHasExited = null,
        Func<CommandResult>? queueExitRestore = null, Func<bool>? companionOwnsInput = null, Func<bool>? controllerPresent = null)
    {
        if (app < 0x80000000 || !ownsInput()) return new(false, true);
        // send performs the same ownership check adjacent to Process.Start. A
        // dispatched command may apply even if its launcher doesn't return zero.
        CommandResult force = send(app, ownsInput);
        if (!force.Started) return new(false, true);
        bool GameHasExited() => gameHasExited?.Invoke() == true;
        bool CanSendLayout() => companionOwnsInput?.Invoke() != true;
        CommandResult restore = default;
        bool exitRestoreAttempted = false;
        void TryQueueExitRestore()
        {
            if (exitRestoreAttempted || queueExitRestore is null || !GameHasExited()) return;
            exitRestoreAttempted = true;
            restore = queueExitRestore();
        }
        try
        {
            if (force.Delivered && ownsInput()) probe(TryQueueExitRestore);
        }
        finally
        {
            try
            {
                // Neutralize the relay immediately, then wait before launching
                // any external Steam command. Steam can raise its main window
                // as the companion is still opening or showing the overlay.
                if (!GameHasExited()) waitForCompanionFocusToEnd?.Invoke();
                uint context = GameHasExited() || controllerPresent?.Invoke() == false ? 0 : knownRestore();
                if ((context is SteamMenus or Desktop) && CanSendLayout())
                    send(context, () => !GameHasExited() && CanSendLayout() && knownRestore() == context);
            }
            finally
            {
                // The tracked shortcut can finish while Steam processes the
                // one final reset. An active game still needs acknowledgement
                // before the next focus epoch can acquire a new layout.
                TryQueueExitRestore();
                if (!exitRestoreAttempted)
                {
                    restore = send(0, CanSendLayout);
                    if (!restore.Started && !CanSendLayout())
                    {
                        if (!GameHasExited()) waitForCompanionFocusToEnd?.Invoke();
                        TryQueueExitRestore();
                        if (!exitRestoreAttempted) restore = send(0, CanSendLayout);
                    }
                }
            }
        }
        return new(force.Delivered, restore.Delivered, exitRestoreAttempted && restore.Started && !restore.Delivered);
    }

    internal static uint RestoreContext(string windowClass, bool ownCompanion, bool steam, bool explorer) =>
        windowClass switch
        {
            CompanionClass when ownCompanion => SteamMenus,
            "SDL_app" when steam => SteamMenus,
            "Progman" or "WorkerW" or "Shell_TrayWnd" when explorer => Desktop,
            // The immersive application manager is a transient shell focus
            // during UWP exit. Restore automatic selection for its next owner.
            _ => 0
        };

    private sealed class SteamEndpoint : IDisposable, SteamInputOrderedIssuer.IChannel
    {
        private readonly string _root;
        private readonly Process _owner;
        private readonly FileStream _exe;
        private readonly Func<bool> _rootUnchanged;
        private Process? _pending;
        private uint? _pendingLayout;
        private bool _commandAttempted;

        private SteamEndpoint(string root, Process owner, FileStream exe, Func<bool> rootUnchanged)
        {
            _root = root; _owner = owner; _exe = exe; _rootUnchanged = rootUnchanged;
        }

        internal static SteamEndpoint Open(string root, Func<bool> rootUnchanged)
        {
            string executable = Path.Combine(root, "steam.exe");
            if (!rootUnchanged()) throw new InvalidOperationException("Steam installation changed.");
            var file = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read);
            Process? owner = null, retained = null;
            Process[] candidates = [];
            try
            {
                using var self = Process.GetCurrentProcess();
                if (!TrustedSteamFiles.ValveSignature(executable)) throw new InvalidOperationException("Steam executable signature unavailable.");
                candidates = Process.GetProcessesByName("steam");
                foreach (var process in candidates)
                {
                    if (process.HasExited || process.SessionId != self.SessionId
                        || !string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase)) continue;
                    if (owner is not null) throw new InvalidOperationException("Steam owner ambiguous.");
                    // Bind a process handle before the command can race a PID reuse.
                    _ = process.SafeHandle;
                    owner = process;
                }
                if (owner is null || owner.HasExited) throw new InvalidOperationException("Steam owner unavailable.");
                var result = new SteamEndpoint(root, owner, file, rootUnchanged);
                retained = owner;
                return result;
            }
            catch { file.Dispose(); throw; }
            finally
            {
                foreach (var process in candidates)
                    if (!ReferenceEquals(process, retained)) process.Dispose();
            }
        }

        internal bool Current => !_owner.HasExited && _rootUnchanged();
        internal bool SameSteam(uint pid,long birth)=>Current&&_owner.Id==pid
            &&_owner.StartTime.ToFileTimeUtc()==birth&&Current;
        internal bool NoOwnedOverride
        {
            get
            {
                if(!Current)return false;
                if(!_commandAttempted)return _pending is null&&Current&&!_commandAttempted;
                var held=_pending;
                return _pendingLayout==0&&ReadAutomaticRestore()==AutomaticRestoreStatus.Acknowledged
                    &&ReferenceEquals(_pending,held)&&_pendingLayout==0&&Current;
            }
        }

        internal CommandResult Send(uint layout, Func<bool>? lastGuard) => Send(layout, lastGuard, waitForDelivery: true);

        internal CommandResult QueueAutomaticRestore() => Send(0, null, waitForDelivery: false);

        // Terminal cleanup only: force the layout for the actual native Steam
        // or Explorer foreground, then release our own exact command with 0.
        // A running game, foreign/zero foreground or missing receipt grants no
        // prelude. This does not change the active menu/prepare reset budgets.
        internal void RestoreExitedGameLayout(Func<bool> gameExited)
        {
            var prior = _pending; var priorLayout = _pendingLayout;
            bool PriorCurrent() => Current && _commandAttempted && prior is not null
                && ReferenceEquals(_pending, prior) && _pendingLayout == priorLayout;
            // Do not discard a nonautomatic/uncertain receipt just to prime
            // selection. The existing cleanup must first complete its own0.
            if (!gameExited() || priorLayout != 0 || !PriorCurrent()
                || ReadAutomaticRestore() != AutomaticRestoreStatus.Acknowledged || !PriorCurrent()) return;
            uint fitting = KnownForegroundContext(includeCompanion: false);
            if (fitting is not (SteamMenus or Desktop) || !gameExited() || !PriorCurrent()) return;
            Process? primer = null;
            CommandResult prefix = default, automatic = default;
            AutomaticRestoreStatus receipt = AutomaticRestoreStatus.Unknown;
            try
            {
                prefix = Send(fitting, () => Current && gameExited()
                    && KnownForegroundContext(includeCompanion: false) == fitting
                    && _pending is null && _pendingLayout is null, waitForDelivery: true,
                    started: process => primer = process);
            }
            finally
            {
                // Foreground/exit proof is not renewed authority here. Once
                // our prelude was dispatched, withdraw it even after AltTab or
                // a failed launcher result; never overwrite a changed receipt.
                if (primer is not null && Current && ReferenceEquals(_pending, primer) && _pendingLayout == fitting)
                {
                    Process? zero = null;
                    automatic = Send(0, () => Current && _pending is null && _pendingLayout is null,
                        waitForDelivery: false, started: process => zero = process);
                    if (automatic.Started && zero is not null)
                        receipt = DrainAutomaticRestore(() => Current && ReferenceEquals(_pending, zero) && _pendingLayout == 0);
                }
                PackagedLaunchLog.Info($"Steam exited-game layout release: context={fitting}; context-started={prefix.Started}; "
                    + $"automatic-queued={automatic.Started}; launcher-receipt={receipt}; layout-consumption-confirmed=False.");
            }
        }

        internal SteamInputOrderedIssuer.Receipt? CurrentReceipt => Current && _pending is { } held && _pendingLayout is { } layout
            ? new(held, layout) : null;

        SteamInputOrderedIssuer.Receipt? SteamInputOrderedIssuer.IChannel.Start(uint layout, SteamInputOrderedIssuer.Receipt? expectedPrior, Func<bool> finalGuard)
        {
            if (expectedPrior is { } expected
                ? !ReferenceEquals(_pending, expected.Identity) || _pendingLayout != expected.Layout
                : _pending is not null) return null;
            // Send retires the previous launcher before the last guard. Verify
            // prior identity BEFORE that transition, then require an empty slot
            // AFTER all slow current/source/menu getters and next to spawn.
            var result = Send(layout, () => finalGuard() && _pending is null && _pendingLayout is null, waitForDelivery: false);
            return result.Started ? CurrentReceipt : null;
        }

        SteamInputOrderedIssuer.Acknowledgement SteamInputOrderedIssuer.IChannel.Read(SteamInputOrderedIssuer.Receipt receipt)
        {
            try
            {
                var held = _pending;
                if (held is null || !ReferenceEquals(held, receipt.Identity) || _pendingLayout != receipt.Layout || !Current)
                    return SteamInputOrderedIssuer.Acknowledgement.Unknown;
                if (!held.HasExited) return SteamInputOrderedIssuer.Acknowledgement.Pending;
                int exit = held.ExitCode;
                if (!ReferenceEquals(_pending, held) || _pendingLayout != receipt.Layout || !Current)
                    return SteamInputOrderedIssuer.Acknowledgement.Unknown;
                return exit == 0 ? SteamInputOrderedIssuer.Acknowledgement.Acknowledged : SteamInputOrderedIssuer.Acknowledgement.Failed;
            }
            catch { return SteamInputOrderedIssuer.Acknowledgement.Unknown; }
        }

        internal AutomaticRestoreStatus ReadAutomaticRestore()
        {
            try
            {
                if (!Current) return AutomaticRestoreStatus.Unknown;
                if (_pending is null) return AutomaticRestoreStatus.Missing;
                if (_pendingLayout != 0) return AutomaticRestoreStatus.OtherCommand;
                if (!_pending.HasExited) return AutomaticRestoreStatus.Pending;
                return _pending.ExitCode == 0 && Current ? AutomaticRestoreStatus.Acknowledged : AutomaticRestoreStatus.Failed;
            }
            catch { return AutomaticRestoreStatus.Unknown; }
        }

        internal AutomaticRestoreStatus DrainAutomaticRestore(Func<bool> current)
        {
            Process? pending = _pending;
            if (pending is null) return AutomaticRestoreStatus.Missing;
            bool SameCommand() => ReferenceEquals(_pending, pending) && _pendingLayout == 0 && Current && current();
            return SteamControllerLayoutTrial.DrainAutomaticRestore(ReadAutomaticRestore, SameCommand,
                () => Environment.TickCount64, milliseconds => pending.WaitForExit(milliseconds));
        }

        private CommandResult Send(uint layout, Func<bool>? lastGuard, bool waitForDelivery, Action<Process>? started = null)
        {
            var startAt = Stopwatch.GetTimestamp();
            if (_pending is { } prior)
            {
                // Preserve command order, even if a handoff was slow. Never
                // terminate Steam or start an out-of-order cleanup command.
                if (!prior.WaitForExit(1300)) return default;
                prior.Dispose();
                _pending = null;
                _pendingLayout = null;
            }
            if (!Current || lastGuard is not null && !lastGuard()) return default;
            var start = new ProcessStartInfo(_exe.Name) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add($"steam://forceinputappid/{layout.ToString(CultureInfo.InvariantCulture)}");
            if (!Current || lastGuard is not null && !lastGuard()) return default;
            try
            {
                long spawnAt = Stopwatch.GetTimestamp();
                _commandAttempted=true;
                _pending = Process.Start(start);
                _pendingLayout = _pending is null ? null : layout;
                if (_pending is null) return default;
                started?.Invoke(_pending);
                if (!waitForDelivery)
                    PackagedLaunchLog.Info($"Steam automatic layout reset dispatched: prepare-ms={Stopwatch.GetElapsedTime(startAt, spawnAt).TotalMilliseconds:F0}; "
                        + $"spawn-ms={Stopwatch.GetElapsedTime(spawnAt).TotalMilliseconds:F0}.");
                return new(true, waitForDelivery && _pending.WaitForExit(900) && _pending.ExitCode == 0 && Current);
            }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                return new(_pending is not null, false);
            }
        }

        internal uint KnownForegroundContext() => KnownForegroundContext(includeCompanion: true);
        private uint KnownForegroundContext(bool includeCompanion)
        {
            if (!Current) return 0;
            nint window = GetForegroundWindow();
            uint thread = GetWindowThreadProcessId(window, out uint pid);
            if (window == 0 || thread == 0
                || pid == 0 || GetAncestor(window, 2) != window || GetWindow(window, 4) != 0) return 0;
            var name = new StringBuilder(64);
            if (GetClassNameW(window, name, name.Capacity) == 0) return 0;
            string type = name.ToString();
            bool SameWindow() => GetForegroundWindow() == window &&
                GetWindowThreadProcessId(window, out uint current) == thread && current == pid;
            if (type == CompanionClass && pid == Environment.ProcessId)
                return includeCompanion && SameWindow() ? RestoreContext(type, true, false, false) : 0;
            try
            {
                using var process = Process.GetProcessById(checked((int)pid));
                string? image = process.MainModule?.FileName;
                if (process.HasExited || process.SessionId != _owner.SessionId || !SameWindow()) return 0;
                string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                string steamCef = Path.Combine(_root, "bin", "cef", "cef.win64", "steamwebhelper.exe");
                return RestoreContext(type, false, pid == _owner.Id ||
                    string.Equals(image, steamCef, StringComparison.OrdinalIgnoreCase),
                    string.Equals(image, explorer, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
            return 0;
        }

        internal bool OwnsCompanionForeground()
        {
            nint window = GetForegroundWindow();
            return window != 0 && GetWindowThreadProcessId(window, out uint pid) != 0
                && pid == Environment.ProcessId && KnownForegroundContext() == SteamMenus
                && GetForegroundWindow() == window;
        }

        public void Dispose()
        {
            _pending?.Dispose();
            _owner.Dispose();
            _exe.Dispose();
        }
    }

    private sealed class Worker : IDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _task;
        internal Worker(Action<CancellationToken> run) => _task = Task.Run(() => run(_stop.Token));
        public void Dispose()
        {
            _stop.Cancel();
            if (_task.Wait(4500)) _stop.Dispose();
            else _ = _task.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
        }
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("kernel32.dll")] private static extern uint GetProcessId(SafeProcessHandle process);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);
    [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(SafeProcessHandle process, out long birth, out long exit, out long kernel, out long user);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetClassNameW(nint window, StringBuilder name, int capacity);
}

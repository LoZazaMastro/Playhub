// SPDX-License-Identifier: GPL-3.0-only
using Playhub.FirstContent;
using System.IO;
namespace GamingMode.Services;
internal interface ILaunchCurtainSourceDiagnostics{string? CurrentFailure{get;}}
internal sealed class LaunchCurtainCoverController:IAsyncDisposable
{
 readonly object gate=new();readonly SemaphoreSlim transition=new(1,1);
 readonly Func<CurtainCoverRequest,CancellationToken,Task<ILaunchCurtainSource?>> resolve;
 readonly Func<ICurtainNativeDisplay?> nativeFactory;readonly Func<ContentTarget,Func<bool>,OwnedWindowContentClient> observe;
 readonly CancellationToken stopping;readonly bool kernelPublisherRequired;ICurtainNativeDisplay? display;Session? active;long revision;bool disposed;
 ulong latestEpoch;string? latestRequest;bool requestRetired;
 Session? closedRelease;long closedReleaseRevision;bool releaseAcknowledged;
 const int MaximumSurfaceReoffers=2;
 // Scheduling priority only, including bounded same-launch geometry recovery.
 // This never proves readiness, capture contents, input or foreground focus.
 internal bool HasVisualPriority {
  get {lock(gate){long now=Environment.TickCount64;
   if(disposed||active is not {} session||now>=session.Deadline||now>=session.SourceDeadline)return false;
   bool live=!session.Retired&&!session.Stop.IsCancellationRequested&&session.CurrentFailure is null;
   bool geometryRecovery=!session.FinishRequested&&session.SurfaceReoffers<=MaximumSurfaceReoffers
    &&(session.CurrentFailure=="game-window"||session.Retired&&session.RetireFailure=="game-window"
      &&session.Reason is CoverRetireReason.Canceled or CoverRetireReason.SourceLost);
   return live||geometryRecovery;}}
 }
 sealed record SurfaceRecovery(Session Previous,long Deadline,long SourceDeadline);
 bool CanRecoverSurface(Session held,long now){
  if(disposed||!ReferenceEquals(active,held)||!held.Retired||held.FinishRequested||held.SurfaceReoffers>=MaximumSurfaceReoffers
    ||held.RetireFailure!="game-window"||held.Reason is not (CoverRetireReason.Canceled or CoverRetireReason.SourceLost)
    ||held.Deadline<=now||held.SourceDeadline<=now||!SurfaceRecoveryClosed(held))return false;
  long after=Environment.TickCount64;
  return !disposed&&ReferenceEquals(active,held)&&held.Retired&&!held.FinishRequested&&held.RetireFailure=="game-window"
    &&held.Deadline>after&&held.SourceDeadline>after;
 }
 bool RecoveryStillCurrent(SurfaceRecovery held,long expectedRevision,long now){
  bool Pure(long at)=>!disposed&&revision==expectedRevision&&ReferenceEquals(active,held.Previous)
    &&held.Previous.Retired&&!held.Previous.FinishRequested&&held.Previous.RetireFailure=="game-window"
    &&held.Deadline>at&&held.SourceDeadline>at;
  if(!Pure(now)||!SurfaceRecoveryClosed(held.Previous))return false;
  return Pure(Environment.TickCount64); // no provider may run after this final barrier
 }
 static bool NativeIdle(CurtainNativeDisplay.WireSnapshot? snapshot)=>snapshot is {Size:56,Version:1,Phase:0,CleanupComplete:1,Destination:0} idle
  &&(idle.Flags&~34u)==0; // only hidden + static backend capability, no source/thumbnail/presented lease
 bool SurfaceRecoveryClosed(Session session){
  if(session.NativeGeneration>0)return NativeClosed(session);
  if(!session.Retired||session.PublishAttempted||session.InitialDisplayGeneration!=0||!NativeIdle(session.InitialNative))return false;
  var native=session.Display.Snapshot();
  return NativeIdle(native)&&native!.Value.Generation==session.InitialNative.Generation&&session.Display.ActiveGeneration==0
   &&session.NativeGeneration==0&&!session.PublishAttempted;
 }
 static bool ChangedSameGame(CurtainVisualAuthority old,CurtainVisualAuthority next)=>old.GamePid==next.GamePid&&old.GameBirth==next.GameBirth
  &&old.GameImage.Equals(next.GameImage,StringComparison.OrdinalIgnoreCase)&&old.ExpectedFrameAumid==next.ExpectedFrameAumid
  &&(old.GameWindow!=next.GameWindow||!old.GameRect.SequenceEqual(next.GameRect));
 internal LaunchCurtainCoverController(GamingWindowFocusService focus,CancellationToken appStopping)
 {var resolver=new LivePerformanceSourceResolver(focus);resolve=async(r,c)=>await LaunchCurtainRetainedSource.ResolveAsync(r,resolver,c).ConfigureAwait(false);
  nativeFactory=CurtainNativeDisplay.FromAssemblyPins;observe=OwnedWindowContentClient.Start;stopping=appStopping;kernelPublisherRequired=true;}
 // Inert seam for whole lifecycle tests. Neither endpoint accepts providers.
 internal LaunchCurtainCoverController(Func<CurtainCoverRequest,CancellationToken,Task<ILaunchCurtainSource?>> resolver,
   Func<ICurtainNativeDisplay?> native,Func<ContentTarget,Func<bool>,OwnedWindowContentClient> observer,CancellationToken appStopping,bool requireKernelPublisher=false)
 {resolve=resolver;nativeFactory=native;observe=observer;stopping=appStopping;kernelPublisherRequired=requireKernelPublisher;}
 internal async Task<object> ApplyAsync(CurtainCoverRequest request,CancellationToken cancellation,CurtainPublisher? publisher=null)
 {
  if(kernelPublisherRequired&&publisher is null)return new{ok=false,available=false,reason="publisher-channel-unverified"};
  if(kernelPublisherRequired&&request.Action is not ("close" or "released")&&!CurtainCoverProtocol.SourceFresh(request.SourceObservedAt,Environment.TickCount64))
   return new{ok=false,available=display is not null,reason="rendered-source-stale"};
  if(kernelPublisherRequired&&request.Action=="begin"){
   ulong now=checked((ulong)DateTime.UtcNow.ToFileTimeUtc());
   if(request.RequestEpoch>now||now-request.RequestEpoch>36000000000)return new{ok=false,available=display is not null,reason="launch-request-stale"};
  }
  if(publisher is not null){
   if(request.Action=="begin"){
    if(request.PublisherPid!=publisher.Pid||request.PublisherBirth!=publisher.Birth)return new{ok=false,available=false,reason="publisher-channel-mismatch"};
    request=request with{PublisherImage=publisher.Image};
   }else lock(gate)if((request.Action=="released"?closedRelease:active) is not {} owned||owned.Request.PublisherPid!=publisher.Pid||owned.Request.PublisherBirth!=publisher.Birth||owned.Request.PublisherImage!=publisher.Image)
    return new{ok=false,available=false,reason="publisher-channel-mismatch"};
  }
  if(request.Action=="released"){
   lock(gate){
    if(disposed||closedRelease is not {} held||active is not null||closedReleaseRevision!=revision
      ||held.Token!=request.Lease||held.Request.RequestId!=request.RequestId||held.Request.RequestEpoch!=request.RequestEpoch
      ||!NativeClosed(held))return new{ok=false,available=display is not null,reason="release-not-current"};
    releaseAcknowledged=true;return new{ok=true,available=true,releaseAcknowledged=true,gameReadyClaimed=false,inputAuthorityClaimed=false};
   }
  }
  if(request.Action!="begin"){
   Session? stop=null;object result;
   lock(gate){
    if(disposed||active is not {} held||held.Token!=request.Lease||held.Request.RequestId!=request.RequestId||held.Request.RequestEpoch!=request.RequestEpoch)
     return new{ok=false,available=display is not null,reason="receipt-not-current"};
    if(request.Action=="close"){++revision;closedRelease=held.Retired&&held.NativeGeneration>0?held:null;closedReleaseRevision=revision;releaseAcknowledged=false;active=null;requestRetired=true;held.Retired=true;held.Deadline=0;display?.Close();stop=held;result=new{ok=true,available=true,state="closed",gameReadyClaimed=false};}
    else{
     if(kernelPublisherRequired&&request.SourceObservedAt<held.SourceObservedAt)return new{ok=false,available=true,reason="rendered-source-regressed"};
     if(request.Action=="finish"&&!held.Retired)held.FinishRequested=true;
     if(!held.Retired){held.Deadline=Environment.TickCount64+1000;if(kernelPublisherRequired){held.SourceObservedAt=request.SourceObservedAt;held.SourceDeadline=checked((long)request.SourceObservedAt+700);}}
     result=Reply(held);
    }
   }
   stop?.Stop.Cancel();return result;
  }
  await transition.WaitAsync(cancellation).ConfigureAwait(false);
  ILaunchCurtainSource? source=null;Session? old=null;SurfaceRecovery? recovery=null;long stamp=0;
  try{
   lock(gate){
    if(disposed)return new{ok=false,available=false,reason="stopping"};
    bool sameEpoch=request.RequestEpoch==latestEpoch;
    if(request.RequestEpoch<latestEpoch||(sameEpoch&&request.RequestId!=latestRequest))
     return new{ok=false,available=display is not null,reason="rendered-source-retired"};
    if(sameEpoch&&requestRetired){
     if(active is not {} previous||!CanRecoverSurface(previous,Environment.TickCount64))
      return new{ok=false,available=display is not null,reason="rendered-source-retired"};
     if(previous.Request with{SourceObservedAt=0}!=request with{SourceObservedAt=0})
      return new{ok=false,available=true,reason="rendered-source-replaced"};
     if(request.SourceObservedAt<=Math.Max(previous.SourceObservedAt,previous.ReofferObservedAt))
      return new{ok=false,available=true,reason="rendered-source-stale"};
     recovery=new(previous,previous.Deadline,previous.SourceDeadline);
     ++previous.SurfaceReoffers;previous.ReofferObservedAt=request.SourceObservedAt;
    }
    if(recovery is null&&active is {} same&&same.Request.RequestEpoch==request.RequestEpoch){
     if(same.Request with{SourceObservedAt=0}!=request with{SourceObservedAt=0})return new{ok=false,available=true,reason="rendered-source-replaced"};
     return Reply(same);
    }
    closedRelease=null;releaseAcknowledged=false;old=active;
    if(recovery is null){active=null;if(old is not null){old.Retired=true;old.Deadline=0;}}
    // A failed recovery keeps the retired slot and its original deadlines.
    // New requests/close/invalidate can still revoke it via the same revision.
    latestEpoch=request.RequestEpoch;latestRequest=request.RequestId;requestRetired=recovery is not null;stamp=++revision;display?.Close();
   }
   old?.Stop.Cancel();
   // Pin/trust/association I/O stays outside the coordination lock.
   display??=nativeFactory();
   if(display is null)return new{ok=false,available=false,reason="native-pins-unavailable"};
   if(display.Snapshot() is not {Phase:0,CleanupComplete:1} initialNative)return new{ok=false,available=true,reason="owned-cleanup-pending"};
   ulong initialDisplayGeneration=display.ActiveGeneration;
   source=await resolve(request,cancellation).ConfigureAwait(false);
   if(source is null||!source.Current||source.Visual.AppId!=request.AppId||source.Visual.RequestId!=request.RequestId||source.Visual.RequestEpoch!=request.RequestEpoch
     ||source.Visual.SourceReceipt!=request.SourceReceipt
     ||source.Visual.SteamPid!=request.SteamPid||source.Visual.SteamBirth!=(ulong)request.SteamBirth||source.Visual.SteamWindow!=request.SteamWindow
     ||source.Visual.SteamThread!=request.SteamThread||source.Visual.SteamClass!=request.SteamClass
     ||source.CaptureTarget.Window!=source.Visual.GameWindow||source.CaptureTarget.Pid!=source.Visual.FramePid
     ||source.CaptureTarget.Birth!=(long)source.Visual.FrameBirth||source.CaptureTarget.Thread!=source.Visual.FrameThread)
    return new{ok=false,available=true,reason="retained-source-unavailable"};
   // Association/trust I/O cannot consume the immutable rendered receipt and
   // then permanently retire this request before its actual fresh retry.
   if(kernelPublisherRequired&&!CurtainCoverProtocol.SourceFresh(request.SourceObservedAt,Environment.TickCount64))
    return new{ok=false,available=true,reason="rendered-source-stale"};
   if(recovery is {} pending){
    if(!ChangedSameGame(pending.Previous.InitialVisual,source.Visual))
     return new{ok=false,available=true,reason="retained-source-unavailable"};
    lock(gate)if(revision!=stamp||!RecoveryStillCurrent(pending,stamp,Environment.TickCount64))
     return new{ok=false,available=true,reason="request-replaced"};
   }
   cancellation.ThrowIfCancellationRequested();var session=new Session(this,request,source,display,initialNative,initialDisplayGeneration);source=null;bool accepted;
   if(recovery is {} recovered){session.SurfaceReoffers=recovered.Previous.SurfaceReoffers;
    session.Deadline=Math.Min(session.Deadline,recovered.Deadline);session.SourceDeadline=Math.Min(session.SourceDeadline,recovered.SourceDeadline);
   }
   lock(gate){
    accepted=!disposed&&revision==stamp&&!stopping.IsCancellationRequested
     &&(recovery is null||RecoveryStillCurrent(recovery,stamp,Environment.TickCount64));
    if(accepted){active=session;latestEpoch=request.RequestEpoch;latestRequest=request.RequestId;requestRetired=false;session.Task=Task.Run(()=>Run(session));}
   }
   if(!accepted){session.Source.Dispose();return new{ok=false,available=true,reason="request-replaced"};}
   lock(gate)return Reply(session);
  }catch(Exception error)when(error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception){
   return new{ok=false,available=display is not null,reason="component-or-source-unavailable"};
  }finally{source?.Dispose();transition.Release();}
 }
 bool NativeClosed(Session session){
  var native=session.Display.Snapshot();
  return session.Retired&&session.NativeGeneration>0&&native is {Phase:0,CleanupComplete:1,Destination:0}
    &&native.Value.Generation==session.NativeGeneration&&(native.Value.Flags&12)==0;
 }
 // Cleanup-only receipt. The trusted broker callback must be brief and may
 // touch ONLY its independently retained Explorer process. No await/activation.
 internal bool WithBootstrapReleased(uint appId,uint gamePid,long gameBirth,Func<bool> action){
  lock(gate){
   var held=closedRelease;
   // Cleanup acknowledges removal even after failure; it does not prove a
   // completed launch and must never authorize stopping the bootstrap shell.
   if(disposed||held is null||!releaseAcknowledged||closedReleaseRevision!=revision||active is not null
     ||!held.ContentObserved||!held.FinishRequested||held.Reason!=CoverRetireReason.Canceled
     ||held.RetireFacts is not {ObservedOpaqueContent:true} facts
     ||held.RetireRetirement is not {Check:"fresh"} retirement||!ReferenceEquals(retirement.Facts,facts)
     ||held.Request.AppId!=appId||held.ReleaseGamePid!=gamePid||held.ReleaseGameBirth!=(ulong)gameBirth
     ||gameBirth<=0||!NativeClosed(held))return false;
   return action();
  }
 }
 // EXIT ONLY: failed content/fade may still have real cleanup. This API never
 // authorizes stopping a live host and cannot turn that failure into success.
 internal XboxShellBootstrap.ExitCleanup WithBootstrapCleanupAfterGameExit(uint appId,uint gamePid,long gameBirth,
  Func<bool> exactGameExited,Func<bool> action){
  lock(gate){
   bool Exited(){try{return exactGameExited();}catch{return false;}}
   bool Matches(Session value)=>value.Request.AppId==appId&&value.ReleaseGamePid==gamePid
     &&gameBirth>0&&value.ReleaseGameBirth==(ulong)gameBirth;
   if(disposed||appId==0||gamePid==0||gameBirth<=0||!Exited())return XboxShellBootstrap.ExitCleanup.Refused;
   if(active is {} pending){
    long heldRevision=revision;
    bool same=Matches(pending)&&pending.NativeGeneration>0;
    return same&&Exited()&&!disposed&&heldRevision==revision&&ReferenceEquals(active,pending)
      ?XboxShellBootstrap.ExitCleanup.Pending:XboxShellBootstrap.ExitCleanup.Refused;
   }
   var held=closedRelease;long expectedRevision=revision;
   bool Same()=>!disposed&&held is not null&&ReferenceEquals(closedRelease,held)&&active is null
     &&revision==expectedRevision&&closedReleaseRevision==expectedRevision&&held.Retired&&Matches(held)&&held.NativeGeneration>0;
   if(!Same())return XboxShellBootstrap.ExitCleanup.Refused;
   var native=held!.Display.Snapshot();
   if(!Same()||!Exited()||!Same()||native is not {} snapshot||snapshot.Generation!=held.NativeGeneration)
    return XboxShellBootstrap.ExitCleanup.Refused;
   if(snapshot.Size!=56||snapshot.Version!=1||snapshot.Phase>3||snapshot.CleanupComplete>1)
    return XboxShellBootstrap.ExitCleanup.Refused;
   bool closed=snapshot.Phase==0&&snapshot.CleanupComplete==1&&snapshot.Destination==0&&(snapshot.Flags&12)==0;
   if(snapshot.Phase==0&&snapshot.CleanupComplete==1&&!closed)return XboxShellBootstrap.ExitCleanup.Refused;
   if(!closed||!releaseAcknowledged){
    // A matching cleanup not acknowledged yet may settle; inconsistent closed
    // state or lost identity is refusal, never a reason to issue StopExact.
    bool cleanupPending=!releaseAcknowledged&&(snapshot.Phase<=3&&snapshot.CleanupComplete<=1);
    return cleanupPending&&Same()&&Exited()&&Same()?XboxShellBootstrap.ExitCleanup.Pending:XboxShellBootstrap.ExitCleanup.Refused;
   }
   if(!Same()||!Exited()||!Same())return XboxShellBootstrap.ExitCleanup.Refused;
   return action()?XboxShellBootstrap.ExitCleanup.Ready:XboxShellBootstrap.ExitCleanup.Refused;
  }
 }
 object Reply(Session session){
  var captured=session.Retired?session.RetireFacts:session.Observer?.LastFacts;
  var retirement=session.Retired?session.RetireRetirement:session.Observer?.LastRetirement;
  if(captured is null||!ReferenceEquals(retirement?.Facts,captured))retirement=null;
  var captureDiagnostic=session.Retired?session.RetireCaptureDiagnostic:session.Observer?.LastDiagnostic;var native=display?.Snapshot();bool presented=native is {Phase:2} p&&p.Generation==display?.ActiveGeneration
   &&(p.Flags&24)==24&&p.ObservedAt<=(ulong)Environment.TickCount64&&(ulong)Environment.TickCount64-p.ObservedAt<=250;
  long now=Environment.TickCount64;long? proofAge=session.ProofAt>0&&now>=session.ProofAt?now-session.ProofAt:null;
  return new{ok=true,available=true,lease=session.Token,appId=session.Request.AppId,gamePid=session.Source.Visual.GamePid,gameBirth=session.Source.Visual.GameBirth.ToString(System.Globalization.CultureInfo.InvariantCulture),
   gameWindow=session.Source.Visual.GameWindow.ToString(System.Globalization.CultureInfo.InvariantCulture),
   nativePresented=presented&&native?.Generation==session.NativeGeneration&&session.NativeGeneration>0
     &&!disposed&&ReferenceEquals(active,session)&&!session.Retired&&session.CurrentFailure is null
     &&now<session.Deadline&&now<session.SourceDeadline,sourceProofAgeMs=proofAge,
   requestId=session.Request.RequestId,requestEpoch=session.Request.RequestEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture),
   state=session.ContentObserved?"first-content-observed":session.Retired?"retired":presented?"presented":"preparing",
   cleanupComplete=session.Retired&&native?.CleanupComplete==1,nativePhase=native?.Phase,nativeStage=native?.Stage,nativeError=native?.Error,nativeResult=native?.Result,nativeFlags=native?.Flags,nativeFinishComplete=HasNormalFinish(session),
   retireReason=session.Retired?session.Reason.ToString():null,currentFailure=session.Retired?session.RetireFailure:session.CurrentFailure,captureState=session.Retired?session.RetireCaptureState:session.Observer?.LastState.ToString(),finishRequested=session.FinishRequested,
   captureSamples=captured?.Samples,captureOpaqueSamples=captured?.Opaque,
   retirementCheck=retirement?.Check,retirementFrameAgeMs=retirement?.FrameAgeMs,retirementPostReadElapsedMs=retirement?.PostReadElapsedMs,
   captureObserverState=captureDiagnostic?.State,captureObserverStage=captureDiagnostic?.Stage,captureRejectReason=captureDiagnostic?.Rejection,
   captureHresult=captureDiagnostic?.HResult,captureWinError=captureDiagnostic?.WinError,captureReceiverFrameAgeMs=captureDiagnostic?.ReceiverFrameAgeMs,
   leaseRemainingMs=session.Deadline-now,renderReceiptRemainingMs=kernelPublisherRequired?session.SourceDeadline-now:(long?)null,
   sourceReceiptAgeMs=session.SourceObservedAt>0&&(ulong)now>=session.SourceObservedAt?(long)((ulong)now-session.SourceObservedAt):(long?)null,
   observedAtTick=session.RetiredAt,gameReadyClaimed=false,inputAuthorityClaimed=false};}
 static bool Failed(Session session,string reason){Interlocked.CompareExchange(ref session.CurrentFailure,reason,null);return false;}
 bool Current(Session session){
  lock(gate){
   if(disposed)return Failed(session,"controller-disposed");
   if(!ReferenceEquals(active,session))return Failed(session,"session-replaced");
   if(session.Retired)return Failed(session,"session-retired");
   if(session.Deadline<=Environment.TickCount64)return Failed(session,"lease-expired");
   if(session.SourceDeadline<=Environment.TickCount64)return Failed(session,"render-receipt-expired");
   if(session.Stop.IsCancellationRequested)return Failed(session,"session-canceled");
  }
  if(!session.Source.Current)return Failed(session,(session.Source as ILaunchCurtainSourceDiagnostics)?.CurrentFailure??"retained-source");
  lock(gate){
   if(disposed)return Failed(session,"controller-disposed-after-source");
   if(!ReferenceEquals(active,session))return Failed(session,"session-replaced-after-source");
   if(session.Retired)return Failed(session,"session-retired-after-source");
   if(session.Deadline<=Environment.TickCount64)return Failed(session,"lease-expired-after-source");
   if(session.SourceDeadline<=Environment.TickCount64)return Failed(session,"render-receipt-expired-after-source");
   return !session.Stop.IsCancellationRequested||Failed(session,"session-canceled-after-source");
  }
 }
 bool Heartbeat(Session session){
  if(!Current(session))return false;
  // Native queue/read are bounded in-process copies, not source/trust I/O.
  lock(gate){bool okay=!disposed&&ReferenceEquals(active,session)&&!session.Retired&&session.Deadline>Environment.TickCount64&&session.SourceDeadline>Environment.TickCount64&&!session.Stop.IsCancellationRequested
      &&PublishSession(session,Math.Min(session.SourceDeadline,Math.Min(session.Deadline,Environment.TickCount64+1000)));
   if(okay){session.NativeGeneration=session.Display.ActiveGeneration;session.ProofAt=Environment.TickCount64;return true;}
   // A normal native fade can finish between the preceding Snapshot and this
   // final pulse. The typed completion receipt grants no new presentation.
   if(LatchNormalFinish(session,session.Display.Snapshot()))return true;
   return Failed(session,"heartbeat-or-native-publish");}
 }
 static bool HasNormalFinish(Session session)=>session.FinishAccepted&&session.FinishReceipt is {} completed
  &&completed.NormalFadeComplete&&completed.Generation==session.NativeGeneration;
 bool LatchNormalFinish(Session session,CurtainNativeDisplay.WireSnapshot? native){
  if(native is not {} done||!done.NormalFadeComplete)return false;
  lock(gate){
   if(!session.FinishAccepted||!session.ContentObserved||!session.FinishRequested||session.NativeGeneration==0
      ||done.Generation!=session.NativeGeneration||disposed||!ReferenceEquals(active,session)||session.Retired
      ||session.Stop.IsCancellationRequested||session.CurrentFailure is not null
      ||session.Deadline<=Environment.TickCount64||session.SourceDeadline<=Environment.TickCount64)return false;
   // Immutable historical completion, not a new live/ready/input receipt.
   session.FinishReceipt??=done;return true;
  }
 }
 bool PublishSession(Session session,long until){
  // Mark even an unknown/refused native attempt before crossing its boundary.
  // NativeGen0 alone must never imply that no Begin was queued.
  session.PublishAttempted=true;return session.Display.Publish(session.Source.Visual,until);
 }
 void Retire(Session session,CoverRetireReason reason){lock(gate){
  if(!ReferenceEquals(active,session)||session.Retired)return;
  if(reason==CoverRetireReason.FirstObservedContent){session.ContentObserved=true;return;}
  requestRetired=true;session.RetireFailure=session.CurrentFailure;session.RetireCaptureState=session.Observer?.LastState.ToString();session.RetireCaptureDiagnostic=session.Observer?.LastDiagnostic;session.RetireFacts=session.Observer?.LastFacts;session.RetireRetirement=session.Observer?.LastRetirement;session.Retired=true;session.Reason=reason;session.RetiredAt=Environment.TickCount64;session.Display.Close();}}
 async Task Run(Session session){
  using var stop=CancellationTokenSource.CreateLinkedTokenSource(stopping,session.Stop.Token);Task? refresh=null;
  try{
   refresh=Refresh(session,stop.Token);
   var observer=observe(session.Source.CaptureTarget,()=>Current(session));session.Observer=observer;
   var reason=await FirstContentProducer.RunAsync(session,observer,stop.Token).ConfigureAwait(false);
   if(reason==CoverRetireReason.FirstObservedContent){
    // First content is an observation, not permission to cut a soundbite short.
    // The authenticated LC publisher owns the same request's duration gate.
    while(Current(session)&&!session.FinishRequested){if(!Heartbeat(session))break;await Task.Delay(25,stop.Token).ConfigureAwait(false);}
    if(Current(session)&&session.FinishRequested){
     if(!session.Display.Finish())Failed(session,"native-finish-rejected");
     else{
      session.FinishAccepted=true;CurtainNativeDisplay.WireSnapshot? terminal=null;
      long until=Environment.TickCount64+600;
      while(Current(session)&&Environment.TickCount64<until
          &&(terminal=session.Display.Snapshot()) is not {Phase:0,CleanupComplete:1}){
       if(!Heartbeat(session))break;await Task.Delay(10,stop.Token).ConfigureAwait(false);
      }
      LatchNormalFinish(session,terminal);
      if(!HasNormalFinish(session))Failed(session,"native-finish-incomplete");
     }
    }
    Retire(session,CoverRetireReason.Canceled);
   }
  }catch(OperationCanceledException){Retire(session,CoverRetireReason.Canceled);}
   catch(Exception error)when(error is IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception){Retire(session,CoverRetireReason.CaptureUnavailable);}
  finally{stop.Cancel();if(refresh is not null)try{await refresh.ConfigureAwait(false);}catch(OperationCanceledException){}session.Source.Dispose();}
 }
 async Task Refresh(Session session,CancellationToken token){
  while(!token.IsCancellationRequested){await Task.Delay(400,token).ConfigureAwait(false);
   if(!await session.Source.RefreshAssociationAsync(token).ConfigureAwait(false)){Failed(session,"association-refresh");Retire(session,CoverRetireReason.SourceLost);session.Stop.Cancel();return;}}
 }
 internal void Invalidate(){Session? held;lock(gate){closedRelease=null;releaseAcknowledged=false;++revision;if(latestEpoch>0)requestRetired=true;held=active;active=null;if(held is not null){held.Retired=true;held.Deadline=0;}display?.Close();}held?.Stop.Cancel();}
 public async ValueTask DisposeAsync(){Session? held;lock(gate){if(disposed)return;disposed=true;held=active;}Invalidate();
  if(held is not null)try{await held.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);}catch(TimeoutException){}catch(OperationCanceledException){}
  display?.Dispose();}
 sealed class Session:IFirstContentCoverPublisher {
  readonly LaunchCurtainCoverController owner;internal readonly CurtainCoverRequest Request;internal readonly ILaunchCurtainSource Source;internal readonly ICurtainNativeDisplay Display;
  internal readonly string Token=Guid.NewGuid().ToString("N");internal readonly CancellationTokenSource Stop=new();internal long Deadline=Environment.TickCount64+1000;
  internal ulong SourceObservedAt,NativeGeneration,ReofferObservedAt;internal long SourceDeadline;internal readonly uint ReleaseGamePid;internal readonly ulong ReleaseGameBirth;
  internal readonly CurtainVisualAuthority InitialVisual;internal int SurfaceReoffers;
  internal readonly CurtainNativeDisplay.WireSnapshot InitialNative;internal readonly ulong InitialDisplayGeneration;internal bool PublishAttempted;
  internal ContentFacts? RetireFacts;internal ContentRetirementDiagnostic? RetireRetirement;
  internal OwnedWindowContentClient? Observer;internal ContentObservationDiagnostic? RetireCaptureDiagnostic;internal string? CurrentFailure,RetireFailure,RetireCaptureState;internal bool Retired;internal volatile bool ContentObserved,FinishRequested;internal bool FinishAccepted;internal CurtainNativeDisplay.WireSnapshot? FinishReceipt;internal CoverRetireReason Reason=CoverRetireReason.SourceLost;internal long RetiredAt,ProofAt;internal Task Task=System.Threading.Tasks.Task.CompletedTask;
  internal Session(LaunchCurtainCoverController controller,CurtainCoverRequest request,ILaunchCurtainSource source,ICurtainNativeDisplay display,CurtainNativeDisplay.WireSnapshot initialNative,ulong initialDisplayGeneration)
  {
   (owner,Request,Source,Display)=(controller,request,source,display);InitialNative=initialNative;InitialDisplayGeneration=initialDisplayGeneration;SourceObservedAt=request.SourceObservedAt;var visual=source.Visual;
   InitialVisual=visual with{GameRect=(int[])visual.GameRect.Clone(),SteamRect=(int[])visual.SteamRect.Clone()};ReleaseGamePid=visual.GamePid;ReleaseGameBirth=visual.GameBirth;
   SourceDeadline=owner.kernelPublisherRequired?checked((long)request.SourceObservedAt+700):long.MaxValue;
  }
  public bool Current=>owner.Current(this);public bool PublishHeartbeat()=>owner.Heartbeat(this);public void RetireOwned(CoverRetireReason reason)=>owner.Retire(this,reason);
 }
}

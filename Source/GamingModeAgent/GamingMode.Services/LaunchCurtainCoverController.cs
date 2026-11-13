// SPDX-License-Identifier: GPL-3.0-only
using Playhub.FirstContent;
using System.IO;
namespace GamingMode.Services;
internal sealed class LaunchCurtainCoverController:IAsyncDisposable
{
 readonly object gate=new();readonly SemaphoreSlim transition=new(1,1);
 readonly Func<CurtainCoverRequest,CancellationToken,Task<ILaunchCurtainSource?>> resolve;
 readonly Func<ICurtainNativeDisplay?> nativeFactory;readonly Func<ContentTarget,Func<bool>,OwnedWindowContentClient> observe;
 readonly CancellationToken stopping;readonly bool kernelPublisherRequired;ICurtainNativeDisplay? display;Session? active;long revision;bool disposed;
 ulong latestEpoch;string? latestRequest;bool requestRetired;
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
  if(kernelPublisherRequired&&request.Action!="close"&&!CurtainCoverProtocol.SourceFresh(request.SourceObservedAt,Environment.TickCount64))
   return new{ok=false,available=display is not null,reason="rendered-source-stale"};
  if(kernelPublisherRequired&&request.Action=="begin"){
   ulong now=checked((ulong)DateTime.UtcNow.ToFileTimeUtc());
   if(request.RequestEpoch>now||now-request.RequestEpoch>36000000000)return new{ok=false,available=display is not null,reason="launch-request-stale"};
  }
  if(publisher is not null){
   if(request.Action=="begin"){
    if(request.PublisherPid!=publisher.Pid||request.PublisherBirth!=publisher.Birth)return new{ok=false,available=false,reason="publisher-channel-mismatch"};
    request=request with{PublisherImage=publisher.Image};
   }else lock(gate)if(active is not {} owned||owned.Request.PublisherPid!=publisher.Pid||owned.Request.PublisherBirth!=publisher.Birth||owned.Request.PublisherImage!=publisher.Image)
    return new{ok=false,available=false,reason="publisher-channel-mismatch"};
  }
  if(request.Action!="begin"){
   Session? stop=null;object result;
   lock(gate){
    if(disposed||active is not {} held||held.Token!=request.Lease||held.Request.RequestId!=request.RequestId||held.Request.RequestEpoch!=request.RequestEpoch)
     return new{ok=false,available=display is not null,reason="receipt-not-current"};
    if(request.Action=="close"){++revision;active=null;requestRetired=true;held.Retired=true;held.Deadline=0;display?.Close();stop=held;result=new{ok=true,available=true,state="closed",gameReadyClaimed=false};}
    else{
     if(kernelPublisherRequired&&request.SourceObservedAt<held.SourceObservedAt)return new{ok=false,available=true,reason="rendered-source-regressed"};
     if(!held.Retired){held.Deadline=Environment.TickCount64+1000;if(kernelPublisherRequired){held.SourceObservedAt=request.SourceObservedAt;held.SourceDeadline=checked((long)request.SourceObservedAt+700);}}
     result=Reply(held);
    }
   }
   stop?.Stop.Cancel();return result;
  }
  await transition.WaitAsync(cancellation).ConfigureAwait(false);
  ILaunchCurtainSource? source=null;Session? old=null;long stamp=0;
  try{
   lock(gate){
    if(disposed)return new{ok=false,available=false,reason="stopping"};
    if(request.RequestEpoch<latestEpoch||(request.RequestEpoch==latestEpoch&&(request.RequestId!=latestRequest||requestRetired)))
     return new{ok=false,available=display is not null,reason="rendered-source-retired"};
    if(active is {} same&&same.Request.RequestEpoch==request.RequestEpoch){
     if(same.Request with{SourceObservedAt=0}!=request with{SourceObservedAt=0})return new{ok=false,available=true,reason="rendered-source-replaced"};
     return Reply(same);
    }
    old=active;active=null;if(old is not null){old.Retired=true;old.Deadline=0;}
    latestEpoch=request.RequestEpoch;latestRequest=request.RequestId;requestRetired=false;stamp=++revision;display?.Close();
   }
   old?.Stop.Cancel();
   // Pin/trust/association I/O stays outside the coordination lock.
   display??=nativeFactory();
   if(display is null)return new{ok=false,available=false,reason="native-pins-unavailable"};
   if(display.Snapshot() is not {Phase:0,CleanupComplete:1})return new{ok=false,available=true,reason="owned-cleanup-pending"};
   source=await resolve(request,cancellation).ConfigureAwait(false);
   if(source is null||!source.Current||source.Visual.AppId!=request.AppId||source.Visual.RequestId!=request.RequestId||source.Visual.RequestEpoch!=request.RequestEpoch
     ||source.Visual.SourceReceipt!=request.SourceReceipt
     ||source.Visual.SteamPid!=request.SteamPid||source.Visual.SteamBirth!=(ulong)request.SteamBirth||source.Visual.SteamWindow!=request.SteamWindow
     ||source.Visual.SteamThread!=request.SteamThread||source.Visual.SteamClass!=request.SteamClass
     ||source.CaptureTarget.Window!=source.Visual.GameWindow||source.CaptureTarget.Pid!=source.Visual.FramePid
     ||source.CaptureTarget.Birth!=(long)source.Visual.FrameBirth||source.CaptureTarget.Thread!=source.Visual.FrameThread)
    return new{ok=false,available=true,reason="retained-source-unavailable"};
   cancellation.ThrowIfCancellationRequested();var session=new Session(this,request,source,display);source=null;bool accepted;
   lock(gate){
    accepted=!disposed&&revision==stamp&&!stopping.IsCancellationRequested;
    if(accepted){active=session;latestEpoch=request.RequestEpoch;latestRequest=request.RequestId;requestRetired=false;session.Task=Task.Run(()=>Run(session));}
   }
   if(!accepted){session.Source.Dispose();return new{ok=false,available=true,reason="request-replaced"};}
   lock(gate)return Reply(session);
  }catch(Exception error)when(error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception){
   return new{ok=false,available=display is not null,reason="component-or-source-unavailable"};
  }finally{source?.Dispose();transition.Release();}
 }
 object Reply(Session session){var native=display?.Snapshot();bool presented=native is {Phase:2} p&&p.Generation==display?.ActiveGeneration
   &&(p.Flags&24)==24&&p.ObservedAt<=(ulong)Environment.TickCount64&&(ulong)Environment.TickCount64-p.ObservedAt<=250;
  long now=Environment.TickCount64;long? proofAge=session.ProofAt>0&&now>=session.ProofAt?now-session.ProofAt:null;
  return new{ok=true,available=true,lease=session.Token,appId=session.Request.AppId,gamePid=session.Source.Visual.GamePid,gameBirth=session.Source.Visual.GameBirth.ToString(System.Globalization.CultureInfo.InvariantCulture),sourceProofAgeMs=proofAge,
   requestId=session.Request.RequestId,requestEpoch=session.Request.RequestEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture),
   state=session.Retired?session.Reason==CoverRetireReason.FirstObservedContent?"first-content-observed":"retired":presented?"presented":"preparing",
   cleanupComplete=native?.CleanupComplete==1,nativePhase=native?.Phase,nativeStage=native?.Stage,nativeError=native?.Error,nativeResult=native?.Result,
   sourceReceiptAgeMs=session.SourceObservedAt>0&&(ulong)now>=session.SourceObservedAt?(long)((ulong)now-session.SourceObservedAt):(long?)null,
   observedAtTick=session.RetiredAt,gameReadyClaimed=false,inputAuthorityClaimed=false};}
 bool Current(Session session){
  lock(gate)if(disposed||!ReferenceEquals(active,session)||session.Retired||session.Deadline<=Environment.TickCount64||session.SourceDeadline<=Environment.TickCount64||session.Stop.IsCancellationRequested)return false;
  if(!session.Source.Current)return false;
  lock(gate)return !disposed&&ReferenceEquals(active,session)&&!session.Retired&&session.Deadline>Environment.TickCount64&&session.SourceDeadline>Environment.TickCount64&&!session.Stop.IsCancellationRequested;
 }
 bool Heartbeat(Session session){
  if(!Current(session))return false;
  // Native queue/read are bounded in-process copies, not source/trust I/O.
  lock(gate){bool okay=!disposed&&ReferenceEquals(active,session)&&!session.Retired&&session.Deadline>Environment.TickCount64&&session.SourceDeadline>Environment.TickCount64&&!session.Stop.IsCancellationRequested
      &&session.Display.Publish(session.Source.Visual,Math.Min(session.SourceDeadline,Math.Min(session.Deadline,Environment.TickCount64+1000)));
   if(okay)session.ProofAt=Environment.TickCount64;return okay;}
 }
 void Retire(Session session,CoverRetireReason reason){lock(gate){
  if(!ReferenceEquals(active,session)||session.Retired)return;requestRetired=true;session.Retired=true;session.Reason=reason;session.RetiredAt=Environment.TickCount64;session.Display.Close();}}
 async Task Run(Session session){
  using var stop=CancellationTokenSource.CreateLinkedTokenSource(stopping,session.Stop.Token);Task? refresh=null;
  try{
   refresh=Refresh(session,stop.Token);
   var observer=observe(session.Source.CaptureTarget,()=>Current(session));
   await FirstContentProducer.RunAsync(session,observer,stop.Token).ConfigureAwait(false);
  }catch(OperationCanceledException){Retire(session,CoverRetireReason.Canceled);}
   catch(Exception error)when(error is IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception){Retire(session,CoverRetireReason.CaptureUnavailable);}
  finally{stop.Cancel();if(refresh is not null)try{await refresh.ConfigureAwait(false);}catch(OperationCanceledException){}session.Source.Dispose();}
 }
 async Task Refresh(Session session,CancellationToken token){
  while(!token.IsCancellationRequested){await Task.Delay(400,token).ConfigureAwait(false);
   if(!await session.Source.RefreshAssociationAsync(token).ConfigureAwait(false)){Retire(session,CoverRetireReason.SourceLost);session.Stop.Cancel();return;}}
 }
 internal void Invalidate(){Session? held;lock(gate){++revision;if(latestEpoch>0)requestRetired=true;held=active;active=null;if(held is not null){held.Retired=true;held.Deadline=0;}display?.Close();}held?.Stop.Cancel();}
 public async ValueTask DisposeAsync(){Session? held;lock(gate){if(disposed)return;disposed=true;held=active;}Invalidate();
  if(held is not null)try{await held.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);}catch(TimeoutException){}catch(OperationCanceledException){}
  display?.Dispose();}
 sealed class Session:IFirstContentCoverPublisher {
  readonly LaunchCurtainCoverController owner;internal readonly CurtainCoverRequest Request;internal readonly ILaunchCurtainSource Source;internal readonly ICurtainNativeDisplay Display;
  internal readonly string Token=Guid.NewGuid().ToString("N");internal readonly CancellationTokenSource Stop=new();internal long Deadline=Environment.TickCount64+1000;
  internal ulong SourceObservedAt;internal long SourceDeadline;
  internal bool Retired;internal CoverRetireReason Reason=CoverRetireReason.SourceLost;internal long RetiredAt,ProofAt;internal Task Task=System.Threading.Tasks.Task.CompletedTask;
  internal Session(LaunchCurtainCoverController controller,CurtainCoverRequest request,ILaunchCurtainSource source,ICurtainNativeDisplay display)
  {
   (owner,Request,Source,Display)=(controller,request,source,display);SourceObservedAt=request.SourceObservedAt;
   SourceDeadline=owner.kernelPublisherRequired?checked((long)request.SourceObservedAt+700):long.MaxValue;
  }
  public bool Current=>owner.Current(this);public bool PublishHeartbeat()=>owner.Heartbeat(this);public void RetireOwned(CoverRetireReason reason)=>owner.Retire(this,reason);
 }
}

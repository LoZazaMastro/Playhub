using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using GamingMode.Services;
using Playhub.FirstContent;
internal static class ControllerChecks
{
 internal static async Task<int> Run(JsonElement good,long frame)
 {
  int count=0;void Check(bool b){if(!b)throw new Exception("controller "+(count+1));++count;}
  var request=new CurtainCoverRequest("begin",42,new string('a',32),134390248600000000,null,10,1000,2,102,200,22,"SDL_app",new string('b',32),1,SourceObservedAt:1000);
  byte[] Body()=>JsonSerializer.SerializeToUtf8Bytes(new{protocol=1,action="begin",appId=request.AppId,requestId=request.RequestId,requestEpoch=request.RequestEpoch.ToString(),publisherPid=10,publisherBirth="1000",steamPid=2,steamBirth="102",steamWindow="200",steamThread=22,steamClass="SDL_app",sourceReceipt=new string('b',32),surfaceRevision="1",sourceObservedAt="1000"});
  Check(CurtainCoverProtocol.Parse(Body())==request);
  foreach(var mutate in new Action<JsonObject>[] {o=>o["protocol"]=true,o=>o["protocol"]=2,o=>o["appId"]=0,o=>o["appId"]=true,o=>o["publisherBirth"]=1000,
   o=>o["publisherBirth"]="-1",o=>o["steamWindow"]="900719925474099300000",o=>o["steamClass"]="",o=>o["steamClass"]="bad\nclass",o=>o["requestId"]="foreign",
   o=>o["requestEpoch"]="1.1",o=>o["gamePid"]=123,o=>o["gameImage"]="C:\\game.exe",o=>o["sourceCurrent"]=true,o=>o.Remove("publisherPid"),
   o=>o["sourceReceipt"]="foreign",o=>o["surfaceRevision"]=true,o=>o["surfaceRevision"]="0",o=>o.Remove("sourceReceipt")})
  {var node=JsonNode.Parse(Body())!.AsObject();mutate(node);Check(CurtainCoverProtocol.Parse(Encoding.UTF8.GetBytes(node.ToJsonString()))is null);}
  Check(CurtainCoverProtocol.Parse(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Body()).Replace("\"protocol\":1","\"protocol\":1,\"protocol\":1")))is null);
  Check(System.Runtime.InteropServices.Marshal.SizeOf<CurtainNativeDisplay.WirePulse>()==18736);
  Check(System.Runtime.InteropServices.Marshal.SizeOf<CurtainNativeDisplay.WireSnapshot>()==56);
  Check(System.Runtime.InteropServices.Marshal.OffsetOf<CurtainNativeDisplay.WirePulse>("SteamClass").ToInt32()==300);
  Check(System.Runtime.InteropServices.Marshal.OffsetOf<CurtainNativeDisplay.WirePulse>("ExpectedFrameAumid").ToInt32()==17708);
  foreach(string mode in new[]{"normal","missing-receipt","foreign-generation","cleanup-debt","finish-refused"}){
   var n=new Native(){raceClose=true,emitNormalReceipt=mode!="missing-receipt",foreignFinishGeneration=mode=="foreign-generation",rejectFinish=mode=="finish-refused"};
   if(mode=="cleanup-debt")n.badCleanup=true;
   var s=new Source(request);Pipe? p=null;
   await using var c=new LaunchCurtainCoverController((_,_)=>Task.FromResult<ILaunchCurtainSource?>(s),()=>n,
    (t,k)=>new OwnedWindowContentClient(p=new Pipe(good),t,k,()=>frame+300000),default);
   // Cleanup debt applies only after the initial native Begin has been admitted.
   n.badCleanup=false;var begin=JsonSerializer.SerializeToElement(await c.ApplyAsync(request,default));string lease=begin.GetProperty("lease").GetString()!;
   await Until(()=>n.pulses>0);p!.allow.TrySetResult();await Until(()=>p.disposed);
   if(mode=="cleanup-debt")n.badCleanup=true;
   await c.ApplyAsync(new("finish",0,request.RequestId,request.RequestEpoch,lease),default);await Until(()=>s.disposed);
   var done=JsonSerializer.SerializeToElement(await c.ApplyAsync(new("renew",0,request.RequestId,request.RequestEpoch,lease),default));
   bool noFailure=done.GetProperty("currentFailure").ValueKind==JsonValueKind.Null;
   if(mode=="normal"&&!noFailure)throw new Exception("causal finish race: native completed the fade between Snapshot and late Heartbeat; must retain bona-fide completed outcome");
   Check(mode=="normal"?noFailure:!noFailure);
   Check(done.GetProperty("finishRequested").GetBoolean()&&n.finishes==1);
   if(mode=="normal")Check(done.GetProperty("cleanupComplete").GetBoolean()&&n.rejectedAfterFade==1);
  }
  {
   var visual=new Source(request).Visual;var type=typeof(CurtainNativeDisplay);
   var actual=(CurtainNativeDisplay)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
   void Set(string name,object value)=>type.GetField(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(actual,value);
   void Delegate(string field,string method){var target=type.GetField(field,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
    target.SetValue(actual,System.Delegate.CreateDelegate(target.FieldType,typeof(ControllerChecks).GetMethod(method,System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!));}
   Delegate("queue",nameof(RefuseLateRenew));Delegate("read",nameof(ClosedNormalFade));
   long now=Environment.TickCount64;var pulse=CurtainNativeDisplay.Encode(visual,1,1,1,101,1,new string('c',64),now,now+500);
   Set("active",pulse);Set("authority",visual);Set("finishStarted",true);Set("generation",1ul);Set("sequence",1ul);
   Set("pid",1u);Set("birth",101ul);Set("session",1u);Set("ownerHash",new string('c',64));lateRenews=lateCloses=0;
   Check(!actual.Publish(visual,now+500)&&lateRenews==1);
   if(actual.ActiveGeneration!=1||lateCloses!=0)throw new Exception("late renew must not erase native completion before controller inspection");
   Check(actual.Snapshot() is {Phase:0,CleanupComplete:1,Generation:1,Destination:0} receipt&&(receipt.Flags&64u)!=0);
   actual.Close();Check(actual.ActiveGeneration==0&&lateCloses==1);
  }
  var native=new Native();var source=new Source(request);int resolves=0;Pipe? pipe=null;
  await using(var controller=new LaunchCurtainCoverController((_,_)=>{resolves++;return Task.FromResult<ILaunchCurtainSource?>(source);},()=>native,
     (t,c)=>new OwnedWindowContentClient(pipe=new Pipe(good),t,c,()=>frame+300000),default))
  {
   var reply=JsonSerializer.SerializeToElement(await controller.ApplyAsync(request,default));string lease=reply.GetProperty("lease").GetString()!;
   Check(CurtainCoverProtocol.Hex(lease));await Until(()=>native.pulses>0);Check(resolves==1);
   var duplicate=JsonSerializer.SerializeToElement(await controller.ApplyAsync(request,default));Check(duplicate.GetProperty("lease").GetString()==lease&&resolves==1);
   var wrong=JsonSerializer.SerializeToElement(await controller.ApplyAsync(new("renew",0,request.RequestId,request.RequestEpoch,new string('f',32)),default));Check(!wrong.GetProperty("ok").GetBoolean());
   var renewed=JsonSerializer.SerializeToElement(await controller.ApplyAsync(new("renew",0,request.RequestId,request.RequestEpoch,lease),default));Check(renewed.GetProperty("ok").GetBoolean());
   pipe!.allow.TrySetResult();await Until(()=>pipe.disposed);
   var held=JsonSerializer.SerializeToElement(await controller.ApplyAsync(new("renew",0,request.RequestId,request.RequestEpoch,lease),default));
   Check(held.GetProperty("state").GetString()=="first-content-observed"&&!source.disposed&&!held.GetProperty("cleanupComplete").GetBoolean());
   Check(held.GetProperty("gameWindow").GetString()==source.Visual.GameWindow.ToString(System.Globalization.CultureInfo.InvariantCulture));
   Check(held.GetProperty("nativePresented").GetBoolean());
   native.driftGeneration=true;
   var foreignNative=JsonSerializer.SerializeToElement(await controller.ApplyAsync(new("renew",0,request.RequestId,request.RequestEpoch,lease),default));
   Check(!foreignNative.GetProperty("nativePresented").GetBoolean());native.driftGeneration=false;
   int beats=native.pulses;await Task.Delay(60);Check(native.pulses>beats&&!source.disposed&&native.finishes==0);
   var wrongFinish=JsonSerializer.SerializeToElement(await controller.ApplyAsync(new("finish",0,request.RequestId,request.RequestEpoch,new string('f',32)),default));
   Check(!wrongFinish.GetProperty("ok").GetBoolean()&&native.finishes==0);
   var finish=JsonSerializer.SerializeToElement(await controller.ApplyAsync(new("finish",0,request.RequestId,request.RequestEpoch,lease),default));Check(finish.GetProperty("ok").GetBoolean());
   await Until(()=>source.disposed);Check(native.finishes==1);
   var done=JsonSerializer.SerializeToElement(await controller.ApplyAsync(new("renew",0,request.RequestId,request.RequestEpoch,lease),default));
   Check(done.GetProperty("state").GetString()=="first-content-observed");Check(pipe.disposed);
   Check(!done.GetProperty("nativePresented").GetBoolean()&&done.GetProperty("nativeFinishComplete").GetBoolean());

   var closed=JsonSerializer.SerializeToElement(await controller.ApplyAsync(new("close",0,request.RequestId,request.RequestEpoch,lease),default));Check(closed.GetProperty("ok").GetBoolean());
   var released=JsonSerializer.SerializeToElement(await controller.ApplyAsync(new("released",0,request.RequestId,request.RequestEpoch,lease),default));Check(released.GetProperty("releaseAcknowledged").GetBoolean());
   int stops=0;Check(controller.WithBootstrapReleased(42,40576,134390248656109619,()=>{++stops;return true;})&&stops==1);
   var replay=JsonSerializer.SerializeToElement(await controller.ApplyAsync(request with{SourceObservedAt=2000},default));Check(!replay.GetProperty("ok").GetBoolean()&&resolves==1);
  }
  {var mismatched=new Source(request);mismatched.visual=mismatched.visual with{AppId=43};var n=new Native();
   await using var controller=new LaunchCurtainCoverController((_,_)=>Task.FromResult<ILaunchCurtainSource?>(mismatched),()=>n,(t,c)=>throw new Exception("must not observe"),default);
   var reply=JsonSerializer.SerializeToElement(await controller.ApplyAsync(request,default));Check(!reply.GetProperty("ok").GetBoolean()&&mismatched.disposed&&n.pulses==0);}
  {var pending=new TaskCompletionSource<ILaunchCurtainSource?>(TaskCreationOptions.RunContinuationsAsynchronously);var s=new Source(request);var n=new Native();
   await using var controller=new LaunchCurtainCoverController((_,_)=>pending.Task,()=>n,(t,c)=>throw new Exception("must not observe"),default);
   var running=controller.ApplyAsync(request,default);controller.Invalidate();pending.SetResult(s);var result=JsonSerializer.SerializeToElement(await running);
   Check(!result.GetProperty("ok").GetBoolean()&&s.disposed&&n.pulses==0);
   var replay=JsonSerializer.SerializeToElement(await controller.ApplyAsync(request,default));Check(replay.GetProperty("reason").GetString()=="rendered-source-retired");}
  {var n=new Native();var s=new Source(request);Pipe? p=null;
   await using var controller=new LaunchCurtainCoverController((_,_)=>Task.FromResult<ILaunchCurtainSource?>(s),()=>n,(t,c)=>new OwnedWindowContentClient(p=new Pipe(good),t,c,()=>frame+300000),default);
   var reply=JsonSerializer.SerializeToElement(await controller.ApplyAsync(request,default));await Until(()=>n.pulses>0);controller.Invalidate();await Until(()=>s.disposed);
   Check(p is {disposed:true});var renew=JsonSerializer.SerializeToElement(await controller.ApplyAsync(new("renew",0,request.RequestId,request.RequestEpoch,reply.GetProperty("lease").GetString()),default));Check(!renew.GetProperty("ok").GetBoolean());}
  {var n=new Native();int called=0;await using var controller=new LaunchCurtainCoverController((_,_)=>{called++;return Task.FromResult<ILaunchCurtainSource?>(null);},()=>n,(t,c)=>throw new Exception("must not observe"),default);
   var context=new DefaultHttpContext();var spy=new Spy();context.Request.Body=spy;
   context.RequestServices=new ServiceCollection().AddLogging().BuildServiceProvider();var result=await LaunchCurtainCoverEndpoints.HandleAsync(context,()=>false,controller);await result.ExecuteAsync(context);
   Check(context.Response.StatusCode==401&&spy.reads==0&&called==0);
   context=new DefaultHttpContext();context.RequestServices=new ServiceCollection().AddLogging().BuildServiceProvider();context.Request.Body=new MemoryStream(new byte[4097]);
   result=await LaunchCurtainCoverEndpoints.HandleAsync(context,()=>true,controller);await result.ExecuteAsync(context);Check(context.Response.StatusCode==400&&called==0);}
  {var n=new Native();int called=0;await using var controller=new LaunchCurtainCoverController((_,_)=>{called++;return Task.FromResult<ILaunchCurtainSource?>(null);},()=>n,(t,c)=>throw new Exception("must not observe"),default);
   var wrong=JsonSerializer.SerializeToElement(await controller.ApplyAsync(request,default,new(11,1000,"loader","sid",1)));
   Check(!wrong.GetProperty("ok").GetBoolean()&&called==0);
   var context=new DefaultHttpContext();var spy=new Spy();context.Request.Body=spy;context.RequestServices=new ServiceCollection().AddLogging().BuildServiceProvider();
   var result=await LaunchCurtainCoverEndpoints.HandleAsync(context,()=>true,controller,_=>null);await result.ExecuteAsync(context);Check(context.Response.StatusCode==403&&spy.reads==0&&called==0);}
  foreach((ulong observed,long now,bool expected) in new[]{(1000ul,1000L,true),(1000ul,1700L,true),(1000ul,1701L,false),(1000ul,999L,false),(0ul,1000L,false)})
   Check(CurtainCoverProtocol.SourceFresh(observed,now)==expected);
  {var n=new Native();int called=0;await using var controller=new LaunchCurtainCoverController((_,_)=>{called++;return Task.FromResult<ILaunchCurtainSource?>(null);},()=>n,(t,c)=>throw new Exception("must not observe"),default,true);
   var origin=new CurtainPublisher(10,1000,"loader","sid",1);
   var stale=JsonSerializer.SerializeToElement(await controller.ApplyAsync(request with{SourceObservedAt=(ulong)Environment.TickCount64-701},default,origin));
   Check(!stale.GetProperty("ok").GetBoolean()&&called==0&&n.pulses==0);}
  // A finish request is not first-content authority: it cannot invoke native
  // fading until the existing observer produces actual current frame facts.
  {var n=new Native();var s=new Source(request);Pipe? p=null;
   await using var controller=new LaunchCurtainCoverController((_,_)=>Task.FromResult<ILaunchCurtainSource?>(s),()=>n,(t,c)=>new OwnedWindowContentClient(p=new Pipe(good),t,c,()=>frame+300000),default);
   var reply=JsonSerializer.SerializeToElement(await controller.ApplyAsync(request,default));await Until(()=>n.pulses>0);
   var accepted=JsonSerializer.SerializeToElement(await controller.ApplyAsync(new("finish",0,request.RequestId,request.RequestEpoch,reply.GetProperty("lease").GetString()),default));
   await Task.Delay(40);Check(accepted.GetProperty("ok").GetBoolean()&&n.finishes==0&&!s.disposed);
   p!.allow.TrySetResult();await Until(()=>s.disposed);Check(n.finishes==1);
  }
  {var n=new Native();var s=new Source(request);Pipe? p=null;
   await using var controller=new LaunchCurtainCoverController((_,_)=>Task.FromResult<ILaunchCurtainSource?>(s),()=>n,(t,c)=>new OwnedWindowContentClient(p=new Pipe(good),t,c,()=>frame+300000),default);
   var reply=JsonSerializer.SerializeToElement(await controller.ApplyAsync(request,default));await Until(()=>n.pulses>0);p!.allow.TrySetResult();await Until(()=>p.disposed);
   controller.Invalidate();await Until(()=>s.disposed);Check(n.finishes==0&&n.Snapshot() is {Phase:0,CleanupComplete:1});
   var late=JsonSerializer.SerializeToElement(await controller.ApplyAsync(new("finish",0,request.RequestId,request.RequestEpoch,reply.GetProperty("lease").GetString()),default));
   Check(!late.GetProperty("ok").GetBoolean()&&n.finishes==0);
  }
  {var n=new Native();var s=new Source(request);Pipe? p=null;
   await using var controller=new LaunchCurtainCoverController((_,_)=>Task.FromResult<ILaunchCurtainSource?>(s),()=>n,(t,c)=>new OwnedWindowContentClient(p=new Pipe(good),t,c,()=>frame+300000),default);
   var reply=JsonSerializer.SerializeToElement(await controller.ApplyAsync(request,default));await Until(()=>n.pulses>0);n.forceClosedSnapshot=true;p!.allow.TrySetResult();await Until(()=>p.disposed);
   var held=JsonSerializer.SerializeToElement(await controller.ApplyAsync(new("renew",0,request.RequestId,request.RequestEpoch,reply.GetProperty("lease").GetString()),default));
   Check(held.GetProperty("state").GetString()=="first-content-observed"&&!held.GetProperty("cleanupComplete").GetBoolean()&&!s.disposed&&n.finishes==0);
  }
  // Causal diagnostics exercise the actual lifecycle; only transport/source/native are inert.
  foreach(var failure in new[]{"retained-source","heartbeat-or-native-publish","lease-expired","render-receipt-expired","association-refresh","capture-invalid"}){
   var n=new Native();var src=new Source(request);Pipe? transport=null;
   await using var controller=new LaunchCurtainCoverController((_,_)=>Task.FromResult<ILaunchCurtainSource?>(src),()=>n,(t,c)=>new OwnedWindowContentClient(transport=new Pipe(good),t,c,()=>frame+300000),default);
   var first=JsonSerializer.SerializeToElement(await controller.ApplyAsync(request,default));string token=first.GetProperty("lease").GetString()!;
   await Until(()=>n.pulses>0);
   if(failure=="retained-source")src.available=false;
   else if(failure=="heartbeat-or-native-publish")n.rejectPublish=true;
   else if(failure=="association-refresh")src.refreshAvailable=false;
   else if(failure=="capture-invalid"){transport!.invalid=true;transport.allow.TrySetResult();}
   else{
    var active=typeof(LaunchCurtainCoverController).GetField("active",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(controller)!;
    active.GetType().GetField(failure=="lease-expired"?"Deadline":"SourceDeadline",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(active,0L);
   }
   await Until(()=>src.disposed);
   var retired=JsonSerializer.SerializeToElement(await controller.ApplyAsync(new("renew",0,request.RequestId,request.RequestEpoch,token),default));
   Check(retired.GetProperty("retireReason").GetString()==(failure=="capture-invalid"?"CaptureUnavailable":"SourceLost"));
   Check(retired.GetProperty("currentFailure").GetString()==(failure=="capture-invalid"?null:failure));
   Check(retired.GetProperty("finishRequested").GetBoolean()==false);
   Check(retired.GetProperty("gameReadyClaimed").GetBoolean()==false&&retired.GetProperty("inputAuthorityClaimed").GetBoolean()==false);
   if(failure=="capture-invalid")Check(retired.GetProperty("captureState").GetString()=="Invalid");
   var session=typeof(LaunchCurtainCoverController).GetField("active",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(controller)!;
   if(failure=="capture-invalid"){
    Check(retired.GetProperty("captureRejectReason").GetString()=="envelope");
    var observer=session.GetType().GetField("Observer",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(session)!;
    var property=observer.GetType().GetProperty("LastDiagnostic",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
    property.SetValue(observer,Activator.CreateInstance(property.PropertyType,["unknown","client-crop","native-state",(int?)-1,(uint?)5,(long?)999]));
   }
   typeof(LaunchCurtainCoverController).GetMethod("Current",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(controller,[session]);
   var afterCleanup=JsonSerializer.SerializeToElement(await controller.ApplyAsync(new("renew",0,request.RequestId,request.RequestEpoch,token),default));
   Check(afterCleanup.GetProperty("currentFailure").GetString()==retired.GetProperty("currentFailure").GetString()&&afterCleanup.GetProperty("captureState").GetString()==retired.GetProperty("captureState").GetString());
   if(failure=="capture-invalid")Check(afterCleanup.GetProperty("captureRejectReason").GetString()=="envelope"&&afterCleanup.GetProperty("captureObserverStage").GetString()=="none");
  }
  // An actually delayed resolver cannot turn an expired begin into an owned
  // session/tombstone before the publisher can forward its real newer receipt.
  {var n=new Native();var lateRequest=request with{RequestEpoch=(ulong)DateTime.UtcNow.ToFileTimeUtc(),SourceObservedAt=(ulong)Environment.TickCount64};
   var s=new Source(lateRequest);int observations=0,resolutionCount=0;
   await using var controller=new LaunchCurtainCoverController(async(r,_)=>{if(++resolutionCount==1){await Task.Delay(760);return s;}return new Source(r);},()=>n,
      (t,c)=>{++observations;return new OwnedWindowContentClient(new Pipe(good),t,c,()=>frame+300000);},default,true);
   var origin=new CurtainPublisher(10,1000,"loader","sid",1);
   var reply=JsonSerializer.SerializeToElement(await controller.ApplyAsync(lateRequest,default,origin));
   Check(!reply.GetProperty("ok").GetBoolean()&&reply.GetProperty("reason").GetString()=="rendered-source-stale"&&n.pulses==0&&observations==0&&s.disposed);
   // Model the next actual Main render receipt forwarded by the publisher,
   // keeping every other begin field and the same request epoch unchanged.
   var fresh=lateRequest with{SourceObservedAt=(ulong)Environment.TickCount64};
   var retry=JsonSerializer.SerializeToElement(await controller.ApplyAsync(fresh,default,origin));
   Check(retry.GetProperty("ok").GetBoolean()&&retry.GetProperty("requestEpoch").GetString()==lateRequest.RequestEpoch.ToString()&&resolutionCount==2);
   Check(!retry.GetProperty("gameReadyClaimed").GetBoolean()&&!retry.GetProperty("inputAuthorityClaimed").GetBoolean());
   await Until(()=>n.pulses>0);Check(observations==1&&n.finishes==0);
   string token=retry.GetProperty("lease").GetString()!;
   var close=JsonSerializer.SerializeToElement(await controller.ApplyAsync(new("close",0,fresh.RequestId,fresh.RequestEpoch,token),default,origin));
   Check(close.GetProperty("ok").GetBoolean());
  }
  foreach(bool expireRender in new[]{false,true}){
   var n=new Native();var s=new Source(request);
   await using var controller=new LaunchCurtainCoverController((_,_)=>Task.FromResult<ILaunchCurtainSource?>(s),()=>n,
    (t,c)=>new OwnedWindowContentClient(new Pipe(good),t,c,()=>frame+300000),default);
   Check(!controller.HasVisualPriority);
   var begin=JsonSerializer.SerializeToElement(await controller.ApplyAsync(request,default));
   Check(begin.GetProperty("ok").GetBoolean()&&controller.HasVisualPriority);
   var held=typeof(LaunchCurtainCoverController).GetField("active",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(controller)!;
   void Field(string name,object? value)=>held.GetType().GetField(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(held,value);
   Field("CurrentFailure","game-window");Check(controller.HasVisualPriority);
   Field("CurrentFailure","publisher-window");Check(!controller.HasVisualPriority);
   Field("CurrentFailure",null);Field("Retired",true);Field("RetireFailure","game-window");Field("Reason",CoverRetireReason.SourceLost);
   Check(controller.HasVisualPriority);
   Field("FinishRequested",true);Check(!controller.HasVisualPriority);Field("FinishRequested",false);
   held.GetType().GetField(expireRender?"SourceDeadline":"Deadline",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(held,0L);
   Check(!controller.HasVisualPriority);
   controller.Invalidate();Check(!controller.HasVisualPriority);
  }
  count+=await NeverPublishedRecoveryChecks(good,frame,request);
  count+=await GeometryRecoveryChecks(good,frame,request);
  count+=await XboxExitCleanupChecks.Run(good,frame);
  return count;
 }

 static async Task<int> NeverPublishedRecoveryChecks(JsonElement good,long frame,CurtainCoverRequest template){
  int count=0;void Check(bool value,string message){++count;if(!value)throw new Exception("never-published geometry: "+message);}
  JsonElement E(object value)=>JsonSerializer.SerializeToElement(value);
  foreach(string mode in new[]{"zero","prior-idle-generation","unknown","cleanup-pending","initial-resources","current-resources","generation-changed","destination-live","active-lease","publish-attempted"}){
   var request=template with{SourceObservedAt=(ulong)Environment.TickCount64};var old=new Source(request);var fresh=new Source(request);fresh.visual=fresh.visual with{GameRect=[0,0,1706,899]};
   var n=new Native();n.idleFlags=mode=="initial-resources"?4u:2u;if(mode=="prior-idle-generation")n.SeedIdle(77);if(mode=="active-lease")n.SeedActive(77);
   int resolves=0,observers=0;Pipe? second=null;
   await using var c=new LaunchCurtainCoverController((_,_)=>Task.FromResult<ILaunchCurtainSource?>(++resolves==1?old:fresh),()=>n,(target,current)=>{
    var pipe=new Pipe(good);if(++observers==1){old.failure="game-window";old.available=false;}else second=pipe;
    return new OwnedWindowContentClient(pipe,target,current,()=>frame+300000);
   },default);
   var begun=E(await c.ApplyAsync(request,default));string token=begun.GetProperty("lease").GetString()!;await Until(()=>old.disposed);
   var retired=E(await c.ApplyAsync(new("renew",0,request.RequestId,request.RequestEpoch,token),default));
   Check(n.pulses==0&&retired.GetProperty("nativePhase").GetInt32()==0&&retired.GetProperty("captureSamples").ValueKind==JsonValueKind.Null,"geometry changed before actual first Publish/opaque frame");
   Check(retired.GetProperty("currentFailure").GetString()=="game-window"&&!retired.GetProperty("finishRequested").GetBoolean(),"exact no-finish geometry retirement");
   n.idleFlags=2;
   if(mode=="unknown")n.unknownSnapshot=true;
   if(mode=="cleanup-pending")n.badCleanup=true;
   if(mode=="current-resources")n.idleFlags=16;
   if(mode=="generation-changed")n.driftGeneration=true;
   if(mode=="destination-live")n.staleDestination=true;
   if(mode=="publish-attempted"){
    var held=typeof(LaunchCurtainCoverController).GetField("active",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(c)!;
    held.GetType().GetField("PublishAttempted",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(held,true);
   }
   await Task.Delay(20);var offered=E(await c.ApplyAsync(request with{SourceObservedAt=(ulong)Environment.TickCount64},default));
   if(mode is "zero" or "prior-idle-generation"){
    Check(offered.GetProperty("ok").GetBoolean()&&offered.TryGetProperty("lease",out _),"never-published idle geometry must receive new lease");
    Check(offered.GetProperty("lease").GetString()!=token&&offered.GetProperty("captureSamples").ValueKind==JsonValueKind.Null,"new lease has no old facts");
    await Until(()=>n.pulses>0&&second is not null);Check(n.finishes==0,"no native finish before new real content");
   }else Check(!offered.GetProperty("ok").GetBoolean()&&n.pulses==0,"unknown/dirty/changed/attempted native state cannot recover: "+mode);
   c.Invalidate();Check(!c.WithBootstrapReleased(42,40576,134390248656109619,()=>throw new Exception("fake first-content release")),"never-published recovery is never shell-release authority");
  }
  return count;
 }
 static async Task<int> GeometryRecoveryChecks(JsonElement good,long frame,CurtainCoverRequest template){
  int count=0;void Check(bool okay,string reason){++count;if(!okay)throw new Exception("geometry recovery: "+reason);}
  JsonElement Element(object value)=>JsonSerializer.SerializeToElement(value);
  object Active(LaunchCurtainCoverController c)=>typeof(LaunchCurtainCoverController).GetField("active",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(c)!;
  long Deadline(LaunchCurtainCoverController c,string name)=>(long)Active(c).GetType().GetField(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(Active(c))!;
  void SetDeadline(LaunchCurtainCoverController c,string name,long value)=>Active(c).GetType().GetField(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(Active(c),value);
  var request=template with{SourceObservedAt=(ulong)Environment.TickCount64};
  // Actual normal first-content then geometry withdrawal: old facts must not
  // finish a new token. New facts are a genuinely later captured frame.
  {
   var first=new Source(request);var second=new Source(request);second.visual=second.visual with{GameRect=[0,30,2560,1440]};
   var n=new Native();int calls=0;var pipes=new List<Pipe>();
   await using var c=new LaunchCurtainCoverController((_,_)=>Task.FromResult<ILaunchCurtainSource?>(++calls==1?first:second),()=>n,
    (target,current)=>{int index=pipes.Count;var json=JsonNode.Parse(good.GetRawText())!.AsObject();long shift=index*1000000L;json["frameQpc100ns"]=(frame+shift).ToString();json["observedQpc100ns"]=(frame+shift+216182).ToString();json["sourceHwnd"]=target.Window.ToString();
     var pipe=new Pipe(Element(json));pipes.Add(pipe);return new OwnedWindowContentClient(pipe,target,current,()=>frame+shift+300000);},default);
   var begun=Element(await c.ApplyAsync(request,default));string oldToken=begun.GetProperty("lease").GetString()!;await Until(()=>n.pulses>0&&pipes.Count==1);ulong oldGen=n.ActiveGeneration;
   SetDeadline(c,"SourceDeadline",Environment.TickCount64+650);
   pipes[0].allow.TrySetResult();await Until(()=>pipes[0].disposed);first.failure="game-window";first.available=false;await Until(()=>first.disposed);
   var lost=Element(await c.ApplyAsync(new("renew",0,request.RequestId,request.RequestEpoch,oldToken),default));
   Check(lost.GetProperty("state").GetString()=="first-content-observed"&&lost.GetProperty("cleanupComplete").GetBoolean()&&!lost.GetProperty("finishRequested").GetBoolean(),"the real false-finish tuple is not a finish request");
   long oldIntentDeadline=Deadline(c,"Deadline"),oldRenderDeadline=Deadline(c,"SourceDeadline");
   var next=request with{SourceObservedAt=(ulong)Environment.TickCount64};var offered=Element(await c.ApplyAsync(next,default));
   Check(offered.GetProperty("ok").GetBoolean()&&offered.TryGetProperty("lease",out _),"fresh geometry must receive a new lease");string newToken=offered.GetProperty("lease").GetString()!;
   Check(newToken!=oldToken&&offered.GetProperty("state").GetString()!="first-content-observed"&&offered.GetProperty("captureSamples").ValueKind==JsonValueKind.Null,"old facts cannot cross the new lease");
   Check(Deadline(c,"Deadline")==oldIntentDeadline&&Deadline(c,"SourceDeadline")==oldRenderDeadline,"new geometry preserves both original deadlines before any new heartbeat");
   await Until(()=>n.ActiveGeneration>oldGen&&pipes.Count==2);Check(!second.disposed&&n.finishes==0,"new native generation has no finish from old facts");
   var finish=Element(await c.ApplyAsync(new("finish",0,request.RequestId,request.RequestEpoch,newToken),default));Check(finish.GetProperty("ok").GetBoolean(),"a requested finish can wait for fresh new facts");await Task.Delay(30);Check(n.finishes==0,"finish cannot use old facts while new capture is pending");
   pipes[1].allow.TrySetResult();await Until(()=>second.disposed);Check(n.finishes==1,"fresh new frame permits exactly one normal finish");
   var done=Element(await c.ApplyAsync(new("renew",0,request.RequestId,request.RequestEpoch,newToken),default));Check(done.GetProperty("finishRequested").GetBoolean()&&done.GetProperty("cleanupComplete").GetBoolean()&&done.GetProperty("captureOpaqueSamples").GetInt32()>0,"normal new lease finishes with opaque facts");
   await c.ApplyAsync(new("close",0,request.RequestId,request.RequestEpoch,newToken),default);await c.ApplyAsync(new("released",0,request.RequestId,request.RequestEpoch,newToken),default);
   Check(c.WithBootstrapReleased(42,40576,134390248656109619,()=>true),"fresh recovered content plus finish/cleanup still authorizes only the exact bootstrap release");
   var replay=Element(await c.ApplyAsync(next with{SourceObservedAt=(ulong)Environment.TickCount64},default));Check(!replay.GetProperty("ok").GetBoolean(),"completed request cannot reappear");
  }
  // A failed prepare/withdrawal is valid cleanup but never completed content.
  {
   var n=new Native();var s=new Source(request);Pipe? pipe=null;await using var c=new LaunchCurtainCoverController((_,_)=>Task.FromResult<ILaunchCurtainSource?>(s),()=>n,
    (t,current)=>new OwnedWindowContentClient(pipe=new Pipe(good),t,current,()=>frame+300000),default);
   var begun=Element(await c.ApplyAsync(request,default));string token=begun.GetProperty("lease").GetString()!;await Until(()=>n.pulses>0);n.rejectPublish=true;await Until(()=>s.disposed);
   await c.ApplyAsync(new("close",0,request.RequestId,request.RequestEpoch,token),default);var ack=Element(await c.ApplyAsync(new("released",0,request.RequestId,request.RequestEpoch,token),default));
   Check(ack.GetProperty("releaseAcknowledged").GetBoolean(),"cleanup acknowledgement remains valid without content");Check(!c.WithBootstrapReleased(42,40576,134390248656109619,()=>throw new Exception("unsafe shell stop")),"no-first-content bootstrap cannot stop shell");
  }
  // Only a real changed surface of the same retained game may be rebound.
  foreach(string mode in new[]{"unchanged","style-only","late-invalidate","late-close","slow-resolver","other-game","other-birth","other-image","other-package","finished","stale-render","other-receipt","other-publisher","bad-cleanup","old-generation","destination-live","expired-intent","expired-render","canceled-during-resolve","limit"}){
   var s=new Source(request);var n=new Native();int calls=0;var newSources=new List<Source>();var pending=new TaskCompletionSource<ILaunchCurtainSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
   Task<ILaunchCurtainSource?> Resolve(CurtainCoverRequest r,CancellationToken stop){++calls;if(calls==1)return Task.FromResult<ILaunchCurtainSource?>(s);
    var candidate=new Source(r);candidate.visual=candidate.visual with{GameRect=[0,30,2560,1440]};
    if(mode is "unchanged" or "style-only" or "limit")candidate.visual=candidate.visual with{GameRect=(int[])s.visual.GameRect.Clone()};
    if(mode=="other-game")candidate.visual=candidate.visual with{GamePid=40577};if(mode=="other-birth")candidate.visual=candidate.visual with{GameBirth=candidate.visual.GameBirth+1};
    if(mode=="other-image")candidate.visual=candidate.visual with{GameImage=@"C:\XboxGames\foreign.exe"};if(mode=="other-package")candidate.visual=candidate.visual with{ExpectedFrameAumid="Other!App"};newSources.Add(candidate);
    return mode is "canceled-during-resolve" or "slow-resolver"?pending.Task:Task.FromResult<ILaunchCurtainSource?>(candidate);}
   await using var c=new LaunchCurtainCoverController(Resolve,()=>n,(t,current)=>new OwnedWindowContentClient(new Pipe(good),t,current,()=>frame+300000),default);
   var begun=Element(await c.ApplyAsync(request,default));string token=begun.GetProperty("lease").GetString()!;await Until(()=>n.pulses>0);
   if(mode=="finished")await c.ApplyAsync(new("finish",0,request.RequestId,request.RequestEpoch,token),default);
   s.failure="game-window";s.available=false;await Until(()=>s.disposed);ulong gen=n.ActiveGeneration;
   if(mode=="bad-cleanup")n.badCleanup=true;if(mode=="old-generation")n.driftGeneration=true;if(mode=="destination-live")n.staleDestination=true;
   if(mode=="expired-intent")await Task.Delay(1050);
   if(mode=="expired-render")SetDeadline(c,"SourceDeadline",Environment.TickCount64-1);
   var next=request with{SourceObservedAt=(ulong)Environment.TickCount64};if(mode=="stale-render")next=request;if(mode=="other-receipt")next=next with{SourceReceipt=new string('f',32)};if(mode=="other-publisher")next=next with{PublisherPid=11};
   if(mode is "late-invalidate" or "late-close"){
    n.callbackAt=n.snapshots+4;n.onSnapshot=()=>{if(mode=="late-invalidate")c.Invalidate();else c.ApplyAsync(new("close",0,request.RequestId,request.RequestEpoch,token),default).GetAwaiter().GetResult();};
   }
   var attempt=c.ApplyAsync(next,default);
   if(mode=="slow-resolver"){await Until(()=>calls==2);await Task.Delay(1050);pending.SetResult(newSources[0]);}
   if(mode=="canceled-during-resolve"){await Until(()=>calls==2);c.Invalidate();pending.SetResult(newSources[0]);}
   var reply=Element(await attempt);Check(!reply.GetProperty("ok").GetBoolean(),mode+" must refuse recovery");
   Check(n.ActiveGeneration==gen&&n.finishes==0,mode+" cannot mint a new native generation or finish");
   if(mode=="limit"){
    await Task.Delay(2);await c.ApplyAsync(next with{SourceObservedAt=(ulong)Environment.TickCount64},default);int before=calls;await Task.Delay(2);var last=Element(await c.ApplyAsync(next with{SourceObservedAt=(ulong)Environment.TickCount64},default));
    Check(!last.GetProperty("ok").GetBoolean()&&calls==before&&calls==3,"two recovery attempts are the fixed limit");
   }
   if(mode is "finished" or "stale-render" or "other-receipt" or "other-publisher" or "bad-cleanup" or "old-generation" or "destination-live" or "expired-intent" or "expired-render")Check(calls==1,mode+" rejects before resolver I/O");
  }
  return count;
 }
 static int lateRenews,lateCloses;
 static int RefuseLateRenew(uint action,in CurtainNativeDisplay.WirePulse pulse,uint bytes){if(action==2){++lateRenews;return 0;}if(action==3){++lateCloses;return 1;}throw new Exception("unexpected native action");}
 static int ClosedNormalFade(out CurtainNativeDisplay.WireSnapshot value,uint bytes){value=new(){Size=56,Version=1,Phase=0,CleanupComplete=1,Generation=1,Flags=64};return 1;}
 static async Task Until(Func<bool> condition){var end=Environment.TickCount64+1500;while(!condition()){if(Environment.TickCount64>=end)throw new TimeoutException("fake session lifecycle");await Task.Delay(5);}}
 sealed class Source:ILaunchCurtainSource,ILaunchCurtainSourceDiagnostics{
  internal bool disposed,available=true,refreshAvailable=true;internal string? failure;public string? CurrentFailure=>failure;
  internal CurtainVisualAuthority visual;public CurtainVisualAuthority Visual=>visual;
  public ContentTarget CaptureTarget=>new(visual.GameWindow,visual.FramePid,(long)visual.FrameBirth,visual.FrameThread,visual.GameImage,visual.GameClass);
  internal Source(CurtainCoverRequest r){visual=new(r.AppId,r.RequestEpoch,r.RequestId,new string('b',32),40576,134390248656109619,40576,134390248656109619,69808,7341982,
    r.SteamPid,(ulong)r.SteamBirth,r.SteamThread,r.SteamWindow,[0,0,3840,2160],[0,0,3840,2160],r.SteamClass,"SDL_app",@"C:\Steam\steamwebhelper.exe",@"C:\XboxGames\DOOM64\Content\DOOM64_x64.exe","");}
  public bool Current=>!disposed&&available;public Task<bool> RefreshAssociationAsync(CancellationToken stop)=>Task.FromResult(Current&&refreshAvailable);public void Dispose(){disposed=true;}
 }
 sealed class Native:ICurtainNativeDisplay{
  internal uint idleFlags;internal ulong? idleGeneration;internal bool unknownSnapshot;internal void SeedIdle(ulong value){idleGeneration=value;}internal void SeedActive(ulong value){ActiveGeneration=value;}
  internal bool raceClose,finishQueued,emitNormalReceipt=true,foreignFinishGeneration,rejectFinish;internal int rejectedAfterFade;internal int pulses,finishes,snapshots,callbackAt;internal Action? onSnapshot;internal bool forceClosedSnapshot,rejectPublish,badCleanup,driftGeneration,staleDestination;bool closed=true;public ulong ActiveGeneration{get;private set;}
  public CurtainNativeDisplay.WireSnapshot? Snapshot(){if(unknownSnapshot)return null;++snapshots;if(snapshots==callbackAt){var run=onSnapshot;onSnapshot=null;run?.Invoke();}var value=new CurtainNativeDisplay.WireSnapshot(){Size=56,Version=1,Phase=(closed||forceClosedSnapshot)?0u:2u,CleanupComplete=badCleanup?0u:(closed||forceClosedSnapshot)?1u:0u,Generation=(closed&&idleGeneration is {} idle?idle:ActiveGeneration)+(driftGeneration||foreignFinishGeneration&&finishQueued&&closed?1u:0u),Destination=staleDestination?123u:0u,Flags=closed?(idleFlags|(emitNormalReceipt&&finishes>0&&!rejectFinish?64u:0u)):24u,ObservedAt=(ulong)Environment.TickCount64};if(raceClose&&finishQueued&&!closed)closed=true;return value;}
  public bool Publish(CurtainVisualAuthority source,long expires){_ =source;_ =expires;if(rejectPublish)return false;if(raceClose&&finishQueued&&closed){++rejectedAfterFade;return false;}if(closed){++ActiveGeneration;closed=false;}++pulses;return true;}
  public bool Finish(){++finishes;if(rejectFinish)return false;if(raceClose)finishQueued=true;else closed=true;return true;}
  public void Close(){closed=true;emitNormalReceipt=false;}public void Dispose(){Close();}
 }
 sealed class Pipe(JsonElement good):IContentTransport{
  internal readonly TaskCompletionSource allow=new(TaskCreationOptions.RunContinuationsAsynchronously);internal bool disposed,invalid;int reads;string? request;
  public Task WriteLineAsync(string line,CancellationToken cancellation){request=line;return Task.CompletedTask;}
  public async Task<string?> ReadLineAsync(CancellationToken cancellation){if(++reads==1)return "{\"ready\":true,\"protocol\":1}";await allow.Task.WaitAsync(cancellation);
   if(invalid)return "{\"state\":\"unsupported\"}";var node=JsonNode.Parse(good.GetRawText())!.AsObject();node["id"]=request!.Split('\t')[0];return node.ToJsonString();}
  public ValueTask DisposeAsync(){disposed=true;return ValueTask.CompletedTask;}
 }
 sealed class Spy:MemoryStream{internal int reads;public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellationToken=default){++reads;throw new Exception("unauthorized read");}}
}

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
  var native=new Native();var source=new Source(request);int resolves=0;Pipe? pipe=null;
  await using(var controller=new LaunchCurtainCoverController((_,_)=>{resolves++;return Task.FromResult<ILaunchCurtainSource?>(source);},()=>native,
     (t,c)=>new OwnedWindowContentClient(pipe=new Pipe(good),t,c,()=>frame+300000),default))
  {
   var reply=JsonSerializer.SerializeToElement(await controller.ApplyAsync(request,default));string lease=reply.GetProperty("lease").GetString()!;
   Check(CurtainCoverProtocol.Hex(lease));await Until(()=>native.pulses>0);Check(resolves==1);
   var duplicate=JsonSerializer.SerializeToElement(await controller.ApplyAsync(request,default));Check(duplicate.GetProperty("lease").GetString()==lease&&resolves==1);
   var wrong=JsonSerializer.SerializeToElement(await controller.ApplyAsync(new("renew",0,request.RequestId,request.RequestEpoch,new string('f',32)),default));Check(!wrong.GetProperty("ok").GetBoolean());
   var renewed=JsonSerializer.SerializeToElement(await controller.ApplyAsync(new("renew",0,request.RequestId,request.RequestEpoch,lease),default));Check(renewed.GetProperty("ok").GetBoolean());
   pipe!.allow.TrySetResult();await Until(()=>source.disposed);var done=JsonSerializer.SerializeToElement(await controller.ApplyAsync(new("renew",0,request.RequestId,request.RequestEpoch,lease),default));
   Check(done.GetProperty("state").GetString()=="first-content-observed");Check(pipe.disposed);
   var closed=JsonSerializer.SerializeToElement(await controller.ApplyAsync(new("close",0,request.RequestId,request.RequestEpoch,lease),default));Check(closed.GetProperty("ok").GetBoolean());
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
  return count;
 }
 static async Task Until(Func<bool> condition){var end=Environment.TickCount64+1500;while(!condition()){if(Environment.TickCount64>=end)throw new TimeoutException("fake session lifecycle");await Task.Delay(5);}}
 sealed class Source:ILaunchCurtainSource{
  internal bool disposed;internal CurtainVisualAuthority visual;public CurtainVisualAuthority Visual=>visual;
  public ContentTarget CaptureTarget=>new(7341982,40576,134390248656109619,69808,@"C:\XboxGames\DOOM64\Content\DOOM64_x64.exe","SDL_app");
  internal Source(CurtainCoverRequest r){visual=new(r.AppId,r.RequestEpoch,r.RequestId,new string('b',32),40576,134390248656109619,40576,134390248656109619,69808,7341982,
    r.SteamPid,(ulong)r.SteamBirth,r.SteamThread,r.SteamWindow,[0,0,3840,2160],[0,0,3840,2160],r.SteamClass,"SDL_app",@"C:\Steam\steamwebhelper.exe",CaptureTarget.Image,"");}
  public bool Current=>!disposed;public Task<bool> RefreshAssociationAsync(CancellationToken stop)=>Task.FromResult(Current);public void Dispose(){disposed=true;}
 }
 sealed class Native:ICurtainNativeDisplay{
  internal int pulses;bool closed=true;public ulong ActiveGeneration{get;private set;}
  public CurtainNativeDisplay.WireSnapshot? Snapshot()=>new(){Size=56,Version=1,Phase=closed?0u:2u,CleanupComplete=closed?1u:0u,Generation=ActiveGeneration,Flags=closed?0u:24u,ObservedAt=(ulong)Environment.TickCount64};
  public bool Publish(CurtainVisualAuthority source,long expires){_ =source;_ =expires;if(closed){++ActiveGeneration;closed=false;}++pulses;return true;}
  public void Close(){closed=true;}public void Dispose(){Close();}
 }
 sealed class Pipe(JsonElement good):IContentTransport{
  internal readonly TaskCompletionSource allow=new(TaskCreationOptions.RunContinuationsAsynchronously);internal bool disposed;int reads;string? request;
  public Task WriteLineAsync(string line,CancellationToken cancellation){request=line;return Task.CompletedTask;}
  public async Task<string?> ReadLineAsync(CancellationToken cancellation){if(++reads==1)return "{\"ready\":true,\"protocol\":1}";await allow.Task.WaitAsync(cancellation);
   var node=JsonNode.Parse(good.GetRawText())!.AsObject();node["id"]=request!.Split('\t')[0];return node.ToJsonString();}
  public ValueTask DisposeAsync(){disposed=true;return ValueTask.CompletedTask;}
 }
 sealed class Spy:MemoryStream{internal int reads;public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellationToken=default){++reads;throw new Exception("unauthorized read");}}
}

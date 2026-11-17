using GamingMode.Services;
using Playhub.FirstContent;
using System.Text.Json;

internal static class XboxExitCleanupChecks
{
 const uint App=0x8000002a,Pid=40576;
 const long Birth=134390248656109619;
 static int count;
 static void Check(bool value,string label){if(!value)throw new Exception("exit-cleanup "+label);++count;}
 internal static async Task<int> Run(JsonElement good,long frame)
 {
  count=0;
  // Parent: strict successful-content gate abandons this owned shell forever.
  // Candidate: the same actual failed Controller grants teardown ONLY after exit + ACK.
  await using(var c=await Failed.Create(good,frame)){
   using var e=new Engine(c);await e.Host();e.Step();Check(e.Shell.Stops==0,"alive never stops");
   e.Target.Exited=true;e.Step();Check(e.Shell.Stops==0,"pending cleanup cannot stop");
   await c.Ack();e.Step();Check(e.Shell.Stops==1,"failed LC plus retained exit and actual cleanup must stop exact owned shell");
   Check(e.Logs.Contains("bootstrap-retired-after-exit"),"real stop recorded");
   Check(!c.Controller.WithBootstrapReleased(App,Pid,Birth,()=>throw new Exception("strict pre-exit grant changed")),"strict normal-content gate remains false");
  }
  await using(var c=await Failed.Create(good,frame)){
   int calls=0,snapshots=c.Native.Snapshots;
   Check(Query(c,()=>false,()=>{calls++;return true;})=="Refused"&&calls==0&&c.Native.Snapshots==snapshots,"alive refuses before snapshot");
   Check(Query(c,()=>throw new InvalidOperationException("unknown"),()=>{calls++;return true;})=="Refused"&&calls==0,"unknown exit refuses");
   Check(Query(c,()=>true,()=>{calls++;return true;})=="Pending"&&calls==0,"valid original close waits DOM ACK");
   await c.Ack();Check(Query(c,()=>true,()=>{calls++;return true;})=="Ready"&&calls==1,"cleanup-only actual failed lease ready");
   Check(Query(c,()=>true,()=>throw new Exception("foreign"),Pid+1)=="Refused","foreign game");
   Check(Query(c,()=>true,()=>throw new Exception("foreign"),Pid,Birth+1)=="Refused","foreign birth");
   Check(Query(c,()=>true,()=>throw new Exception("foreign app"),app:App+1)=="Refused","foreign app");
   c.Native.Drift=true;Check(Query(c,()=>true,()=>throw new Exception("foreign generation"))=="Refused","foreign native generation");c.Native.Drift=false;
   c.Native.Hook=c.Controller.Invalidate;Check(Query(c,()=>true,()=>throw new Exception("late invalidate"))=="Refused","snapshot reentry barrier");
  }
  await using(var c=await Failed.Create(good,frame)){
   using var e=new Engine(c);await e.Host();e.Target.Exited=true;e.Step();long first=e.Now;
   for(int n=0;n<12;n++)e.Step();
   Check(e.Shell.Stops==0&&e.Logs.Contains("bootstrap-exit-cleanup-abandoned"),"missing ACK ends without stop");
   Check(e.Now==first+2000,"pending retries preserve original two-second exit deadline");
   await c.Ack();e.Step();Check(e.Shell.Stops==0,"late ACK cannot resurrect abandoned lease");
  }
  foreach(string mutation in new[]{"mode","user-window","owner","new-launch","late-mode","late-time"}){
   await using var c=await Failed.Create(good,frame);using var e=new Engine(c);await e.Host();e.Target.Exited=true;e.Step();await c.Ack();
   if(mutation=="mode")e.Gaming=false;
   if(mutation=="user-window")e.Shell.UserSafe=false;
   if(mutation=="owner")e.Shell.Alive=false;
   if(mutation=="new-launch")e.Core.Begin(e.Caller with{WrapperPid=11,WrapperBirth=12});
   if(mutation=="late-mode")e.Shell.Read=()=>e.Gaming=false;
   if(mutation=="late-time")e.Shell.Read=()=>e.Now+=2001;
   e.Step();Check(e.Shell.Stops==0,mutation+" cannot stop");
  }
  await using(var c=await Failed.Create(good,frame)){
   using var e=new Engine(c);await e.Host();e.Target.Exited=true;e.Step();await c.Ack();
   await c.Controller.ApplyAsync(c.Request with{RequestId=new string('e',32),RequestEpoch=c.Request.RequestEpoch+1},default);
   e.Step();Check(e.Shell.Stops==0,"new curtain request invalidates original cleanup");
  }
  return count;
 }
 static string Query(Failed c,Func<bool> exited,Func<bool> action,uint pid=Pid,long birth=Birth,uint app=App){
#if BEFORE
  return c.Controller.WithBootstrapReleased(app,pid,birth,()=>exited()&&action())?"Ready":"Refused";
#else
  return c.Controller.WithBootstrapCleanupAfterGameExit(app,pid,birth,exited,action).ToString();
#endif
 }
 sealed class Engine:IDisposable
 {
  internal long Now=1000;internal bool Gaming=true;
  internal readonly Shell Shell=new();internal readonly Target Target=new();internal readonly List<string> Logs=[];
  internal readonly XboxShellBootstrap CallerCore;
  internal XboxShellBootstrap Core=>CallerCore;
  internal readonly XboxShellBootstrap.Caller Caller=new(App,10,10,"Exact!App","Game.exe");
  readonly List<Timer> timers=[];
  internal Engine(Failed c){Target.Binding=Caller;
   CallerCore=new(_=>true,()=>true,()=>Shell,work=>Gaming&&work(),(_,_,_)=>Task.FromResult<XboxShellBootstrap.ITarget?>(Target),
    (_,work)=>c.Controller.WithBootstrapReleased(App,Pid,Birth,work),Logs.Add,()=>Now,Schedule
#if !BEFORE
    ,cleanupAfterExit:(target,work)=>c.Controller.WithBootstrapCleanupAfterGameExit(App,Pid,Birth,()=>target.GameExited,work)
#endif
    );
  }
  IDisposable Schedule(int ms,Action action){var t=new Timer(Now+ms,action);timers.Add(t);return t;}
  internal async Task Host(){var begin=Core.Begin(Caller);Check(begin is not null,"begin");Check(await Core.CompleteAsync(begin!.Ticket,App,Pid,default)==XboxShellBootstrap.Completion.Hosted,"host");}
  internal void Step(){var next=timers.Where(t=>!t.Disposed).OrderBy(t=>t.At).FirstOrDefault();if(next is null)return;Now=next.At;next.Disposed=true;next.Work();}
  public void Dispose()=>Core.Dispose();
 }
 sealed class Timer(long at,Action work):IDisposable{internal long At=at;internal Action Work=work;internal bool Disposed;public void Dispose()=>Disposed=true;}
 sealed class Shell:XboxShellBootstrap.IOwnedShell
 {
  internal bool Alive=true,UserSafe=true;internal int Stops;internal Action? Read;
  public XboxShellBootstrap.ShellIdentity Identity=>new(99,20,1,"explorer.exe");
  public bool Current=>Alive;public bool OwnsDesktopShell=>Alive;
  public bool NoUserExplorer{get{Read?.Invoke();return UserSafe;}}
  public bool StopExact(Func<bool> final){if(!Alive||!UserSafe||!final())return false;Stops++;Alive=false;return true;}
  public void Dispose(){}
 }
 sealed class Target:XboxShellBootstrap.ITarget
 {
  internal bool Exited;internal XboxShellBootstrap.Caller Binding=null!;
  public XboxShellBootstrap.Caller Caller=>Binding;public uint GamePid=>Pid;public long GameBirth=>Birth;
  public bool Current=>!Exited;public bool CheapCurrent=>!Exited;public bool GameExited=>Exited;
  public void Dispose(){}
 }
 sealed class Failed:IAsyncDisposable
 {
  internal LaunchCurtainCoverController Controller=null!;internal Native Native=new();internal CurtainCoverRequest Request=null!;internal string Token="";
  internal static async Task<Failed> Create(JsonElement good,long frame){
   var c=new Failed();c.Request=new("begin",App,new string('a',32),134390248600000000,null,10,1000,2,102,200,22,"SDL_app",new string('b',32),1,SourceObservedAt:1000);
   var source=new Source(c.Request);
   c.Controller=new((_,_)=>Task.FromResult<ILaunchCurtainSource?>(source),()=>c.Native,(t,current)=>new OwnedWindowContentClient(new Pipe(),t,current,()=>frame+300000),default);
   var reply=JsonSerializer.SerializeToElement(await c.Controller.ApplyAsync(c.Request,default));c.Token=reply.GetProperty("lease").GetString()!;
   await Until(()=>c.Native.Pulses>0,"publish");c.Native.Reject=true;await Until(()=>source.Disposed,"retired");
   await c.Controller.ApplyAsync(new("close",0,c.Request.RequestId,c.Request.RequestEpoch,c.Token),default);
   return c;
  }
  internal async Task Ack(){var reply=JsonSerializer.SerializeToElement(await Controller.ApplyAsync(new("released",0,Request.RequestId,Request.RequestEpoch,Token),default));Check(reply.GetProperty("releaseAcknowledged").GetBoolean(),"actual cleanup ACK");}
  public ValueTask DisposeAsync()=>Controller.DisposeAsync();
 }
 static async Task Until(Func<bool> test,string label){long end=Environment.TickCount64+1500;while(!test()){if(Environment.TickCount64>=end)throw new Exception("inert controller timeout "+label);await Task.Delay(5);}}
 sealed class Source:ILaunchCurtainSource
 {
  internal bool Disposed;readonly CurtainVisualAuthority visual;
  internal Source(CurtainCoverRequest r){visual=new(r.AppId,r.RequestEpoch,r.RequestId,new string('b',32),Pid,(ulong)Birth,Pid,(ulong)Birth,69808,7341982,r.SteamPid,(ulong)r.SteamBirth,r.SteamThread,r.SteamWindow,[0,0,3840,2160],[0,0,3840,2160],r.SteamClass,"SDL_app",@"C:\Steam\steamwebhelper.exe",@"C:\XboxGames\DOOM64\Content\DOOM64_x64.exe","");}
  public CurtainVisualAuthority Visual=>visual;public ContentTarget CaptureTarget=>new(visual.GameWindow,visual.FramePid,(long)visual.FrameBirth,visual.FrameThread,visual.GameImage,visual.GameClass);
  public bool Current=>!Disposed;public Task<bool> RefreshAssociationAsync(CancellationToken token)=>Task.FromResult(Current);public void Dispose()=>Disposed=true;
 }
 sealed class Native:ICurtainNativeDisplay
 {
  internal bool Reject,Drift;internal Action? Hook;internal int Pulses,Snapshots;bool closed=true;
  public ulong ActiveGeneration{get;private set;}
  public bool Publish(CurtainVisualAuthority source,long expires){if(Reject)return false;if(closed){++ActiveGeneration;closed=false;}++Pulses;return true;}
  public bool Finish()=>false;public void Close()=>closed=true;public void Dispose()=>Close();
  public CurtainNativeDisplay.WireSnapshot? Snapshot(){Snapshots++;var hook=Hook;Hook=null;hook?.Invoke();return new(){Size=56,Version=1,Generation=ActiveGeneration+(Drift?1u:0u),Phase=closed?0u:2u,CleanupComplete=closed?1u:0u,Flags=closed?0u:24u,ObservedAt=(ulong)Environment.TickCount64};}
 }
 sealed class Pipe:IContentTransport
 {
  int reads;
  public Task WriteLineAsync(string line,CancellationToken token)=>Task.CompletedTask;
  public async Task<string?> ReadLineAsync(CancellationToken token){if(++reads==1)return "{\"ready\":true,\"protocol\":1}";await Task.Delay(Timeout.Infinite,token);return null;}
  public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
 }
}

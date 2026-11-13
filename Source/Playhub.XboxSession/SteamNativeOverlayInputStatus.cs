using System.IO;
// Playhub changes. GPL-3.0-only. Agent-authenticated managed-only mailbox.
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using W=SteamNativeOverlayInputWire;
using B=SteamNativeOverlayInputPresentationBridge;
internal sealed class SteamNativeOverlayInputStatus:IDisposable
{
 readonly CancellationTokenSource stop=new();readonly Task serve;readonly IDisposable scope;readonly object gate=new();
 readonly uint wrapperPid,gamePid,companionPid;readonly long wrapperBirth,gameBirth,companionBirth;readonly ulong gameId;readonly string root;
 readonly SafeProcessHandle game;readonly Process wrapper;
 readonly Func<W.Target,IAuthority?> createAuthority;readonly Func<long> clock;
 internal interface IAuthority:IDisposable{bool Matches(B.Command command);bool Matches(W.Target target);bool Current();}
 IAuthority? authority;ulong generation,serial,lastCallback;bool? lastActive;bool disposed;volatile bool knownNoWorker;
 internal SteamNativeOverlayInputStatus(SafeProcessHandle game,Process wrapper,ulong gameId,uint gamePid,long gameBirth,string root)
 {
  this.game=game;this.wrapper=wrapper;this.gameId=gameId;this.gamePid=gamePid;this.gameBirth=gameBirth;this.root=root;
  wrapperPid=(uint)wrapper.Id;wrapperBirth=wrapper.StartTime.ToFileTimeUtc();using var self=Process.GetCurrentProcess();companionBirth=self.StartTime.ToFileTimeUtc();companionPid=(uint)self.Id;
  clock=()=>Environment.TickCount64;createAuthority=t=>Authority.Open(t,root,game,wrapper);
  scope=B.Configure(wrapperPid,wrapperBirth,gameId,gamePid,gameBirth,authorityCurrent:Validate);
  string name=FormattableString.Invariant($"Playhub.XboxCompanion.{wrapperPid}.{wrapperBirth}.NativeInput");serve=Serve(name);
 }
 // Inert fixture seam exercises this same Apply/Reply/lifetime code. No pipe,
 // native object or fake readiness is created by the production constructor.
 internal SteamNativeOverlayInputStatus(W.Target target,Func<W.Target,IAuthority?> createAuthority,Func<long> clock)
 {
  wrapperPid=target.WrapperPid;wrapperBirth=target.WrapperBirth;gamePid=target.GamePid;gameBirth=target.GameBirth;gameId=target.FullGameId;
  companionPid=target.CompanionPid;companionBirth=target.CompanionBirth;root="";game=null!;wrapper=null!;this.createAuthority=createAuthority;this.clock=clock;
  scope=B.Configure(wrapperPid,wrapperBirth,gameId,gamePid,gameBirth,clock:clock,authorityCurrent:Validate);serve=Task.CompletedTask;
 }
 bool TargetMatches(W.Target t)=>t.FullGameId==gameId&&t.GamePid==gamePid&&t.GameBirth==gameBirth&&t.WrapperPid==wrapperPid&&t.WrapperBirth==wrapperBirth
  &&t.CompanionPid==companionPid&&t.CompanionBirth==companionBirth&&t.ObservedGameId>0&&t.SessionId is {Length:64}&&t.SessionId.All(Uri.IsHexDigit);
 bool Validate(B.Command c){lock(gate)return !disposed&&authority is {} a&&a.Matches(c)&&a.Current();}
 // Called only after the actual layout factory returned null AND its feature
 // metadata proved disabled. Missing worker/timeout alone never calls this.
 internal void MarkNoLayoutWorker()=>knownNoWorker=true;
 sealed class Authority:IAuthority
 {
  readonly W.Target target;readonly SafeProcessHandle steam,host,game;readonly Process wrapper;readonly uint session;readonly uint thread;
  Authority(W.Target t,SafeProcessHandle s,SafeProcessHandle h,SafeProcessHandle g,Process w,uint session,uint thread)=>(target,steam,host,game,wrapper,this.session,this.thread)=(t,s,h,g,w,session,thread);
  internal static Authority? Open(W.Target t,string root,SafeProcessHandle game,Process wrapper)
  {
   SafeProcessHandle? steam=null,host=null;
   try{
    steam=OpenProcess(0x101000,false,t.SteamPid);host=OpenProcess(0x101000,false,t.HostPid);
    if(!Alive(steam,t.SteamPid,t.SteamBirth)||!Alive(host,t.HostPid,t.HostBirth)||!Alive(game,t.GamePid,t.GameBirth)
     ||!Image(steam,Path.Combine(root,"steam.exe"))||!Image(host,Path.Combine(root,"gameoverlayui64.exe"))
     ||!TrustedSteamFiles.ValveSignature(Path.Combine(root,"steam.exe"))||!TrustedSteamFiles.ValveSignature(Path.Combine(root,"gameoverlayui64.exe"))
     ||!ProcessIdToSessionId((uint)Environment.ProcessId,out uint session)||!ProcessIdToSessionId(t.SteamPid,out uint ss)||ss!=session
     ||!ProcessIdToSessionId(t.HostPid,out uint hs)||hs!=session||!ProcessIdToSessionId(t.GamePid,out uint gs)||gs!=session)return null;
    uint thread=WindowPid((nint)t.GameWindow,out uint pid);if(thread==0||pid!=t.GamePid)return null;
    var result=new Authority(t,steam,host,game,wrapper,session,thread);if(!result.Current())return null;steam=host=null;return result;
   }finally{host?.Dispose();steam?.Dispose();}
  }
  public bool Matches(B.Command c)=>c.FullGameId==target.FullGameId&&c.ObservedGameId==target.ObservedGameId&&c.SessionId==target.SessionId
   &&c.GamePid==target.GamePid&&c.GameBirth==target.GameBirth&&c.WrapperPid==target.WrapperPid&&c.WrapperBirth==target.WrapperBirth
   &&c.SteamPid==target.SteamPid&&c.SteamBirth==target.SteamBirth&&c.HostPid==target.HostPid&&c.HostBirth==target.HostBirth;
  public bool Matches(W.Target t)=>t==target;
  public bool Current(){
   nint window=(nint)target.GameWindow;
   return Alive(game,target.GamePid,target.GameBirth)&&Alive(steam,target.SteamPid,target.SteamBirth)&&Alive(host,target.HostPid,target.HostBirth)
    &&!wrapper.HasExited&&wrapper.StartTime.ToFileTimeUtc()==target.WrapperBirth&&wrapper.SessionId==session
    &&Foreground()==window&&WindowPid(window,out uint pid)==thread&&pid==target.GamePid&&Root(window,2)==window
    &&IsWindowVisible(window)&&IsWindowEnabled(window)&&!IsIconic(window)&&Foreground()==window;
  }
  public void Dispose(){host.Dispose();steam.Dispose();}
 }
 async Task Serve(string name){
  try{while(!stop.IsCancellationRequested){using var pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
   await pipe.WaitForConnectionAsync(stop.Token).ConfigureAwait(false);if(!ClientPid(pipe.SafePipeHandle.DangerousGetHandle(),out uint client)||!IsAgent(client))continue;
   using var bound=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);bound.CancelAfter(3000);
   try{
    byte[] data=new byte[8193];int used=0;bool ended=false;while(used<data.Length){int n=await pipe.ReadAsync(data.AsMemory(used),bound.Token).ConfigureAwait(false);if(n==0)break;int lf=Array.IndexOf(data,(byte)10,used,n);used+=n;if(lf>=0){used=lf;ended=true;break;}}
    var request=ended&&used>0&&used<=8192?JsonSerializer.Deserialize<W.Request>(data.AsSpan(0,used),W.Json):null;
    W.Reply answer=request is null?new(false,Error:"invalid-command"):await Apply(request,bound.Token).ConfigureAwait(false);
    await pipe.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(answer,W.Json),bound.Token).ConfigureAwait(false);
   }catch(Exception e)when(e is IOException or OperationCanceledException or JsonException or ArgumentException or OverflowException){ }
  }}catch(OperationCanceledException)when(stop.IsCancellationRequested){}catch(Exception e)when(e is IOException or UnauthorizedAccessException){WSGM.PackagedLaunch.PackagedLaunchLog.Info("Original game overlay input pipe unavailable.");}
 }
 internal async Task<W.Reply> Apply(W.Request r,CancellationToken cancel)
 {
  if(r.Protocol!=1||!TargetMatches(r.Target)||r.BrowserId==0||r.NativeAppId==0||r.Action is not("prepare" or "active" or "renew" or "closed" or "cancel" or "abort"))return new(false,Error:"invalid-native-input-command");
  B.Command command;
  if(r.Action=="prepare"){
   var next=createAuthority(r.Target);if(next is null)return new(false,Error:"native-input-source-not-current");
   lock(gate){if(disposed){next.Dispose();return new(false,Error:"native-input-disposed");}
    string token=Guid.NewGuid().ToString("N");command=Command(r,token,checked(++generation),checked(++serial),clock()+5000);
    var previous=authority;authority=next;if(!B.Publish(command)){authority=previous;next.Dispose();return new(false,Error:"native-input-busy");}previous?.Dispose();lastCallback=0;lastActive=null;
   }
  }else{
   var prior=B.Read();if(prior.Command is not {} old||old.OwnerToken!=r.OwnerToken||old.Generation!=r.Generation
    ||old.BrowserId!=r.BrowserId||old.NativeAppId!=r.NativeAppId||!SameTarget(old,r.Target)||authority?.Matches(r.Target)!=true)return new(false,Error:"native-input-owner-stale");
   if(r.Action is "active" or "renew" or "closed"){
    bool active=r.Action!="closed";if(r.CallbackActive!=active||r.CallbackSerial==0||r.CallbackSerial<lastCallback
     ||(r.CallbackSerial==lastCallback&&lastActive!=active))return new(false,Error:"native-input-callback-stale");
    if(r.Action=="renew"&&(r.CallbackSerial!=lastCallback||lastActive!=true))return new(false,Error:"native-input-renew-needs-existing-active");
    lastCallback=r.CallbackSerial;lastActive=active;
   }
   if(r.Action is "active" or "renew"&&!Validate(old)){
    B.Publish(old with{Action="cancel",Serial=checked(++serial)});
    if(r.Action=="active")B.Publish(Command(r,old.OwnerToken,old.Generation,checked(++serial),old.ExpiresAt));
    return Reply(B.Read(),false,"native-input-source-left");
   }
   command=Command(r,old.OwnerToken,old.Generation,checked(++serial),r.Action is "active" or "renew"?clock()+1500:old.ExpiresAt);
   if(!B.Publish(command))return new(false,Error:"native-input-command-refused");
  }
  long until=clock()+2400;
  do{
   var state=B.Read();if(state.Command is not {} actual||actual.OwnerToken!=command.OwnerToken||actual.Generation!=command.Generation)return new(false,Error:"native-input-owner-changed");
   if(knownNoWorker){B.ReportOrdinary(actual.OwnerToken,actual.Generation,r.Action is "closed" or "cancel",r.Action=="abort");state=B.Read();}
   bool prepare=r.Action=="prepare";if(prepare&&(state.InputReady||state.OrdinaryReady&&(state.NoOwnedOverride||state.MenuLayoutReady))){if(!B.AuthorityCurrent(command))return Reply(state,false,"native-input-source-left");return Reply(B.Read(),true);}
   if(!prepare&&(r.Action is "active" or "renew"||state.CleanupComplete||r.Action=="cancel"))return Reply(state,true);
   await Task.Delay(10,cancel).ConfigureAwait(false);
  }while(clock()<until);
  return Reply(B.Read(),false,"native-input-worker-not-ready");
 }
 static bool SameTarget(B.Command c,W.Target t)=>c.FullGameId==t.FullGameId&&c.ObservedGameId==t.ObservedGameId&&c.SessionId==t.SessionId&&c.GamePid==t.GamePid&&c.GameBirth==t.GameBirth&&c.WrapperPid==t.WrapperPid&&c.WrapperBirth==t.WrapperBirth&&c.SteamPid==t.SteamPid&&c.SteamBirth==t.SteamBirth&&c.HostPid==t.HostPid&&c.HostBirth==t.HostBirth;
 static B.Command Command(W.Request r,string token,ulong gen,ulong serial,long expiry)=>new(r.Action,token,gen,r.Target.SessionId,r.Target.FullGameId,r.Target.ObservedGameId,r.Target.WrapperPid,r.Target.WrapperBirth,r.Target.GamePid,r.Target.GameBirth,r.Target.SteamPid,r.Target.SteamBirth,r.Target.HostPid,r.Target.HostBirth,r.BrowserId,r.NativeAppId,serial,expiry);
 W.Reply Reply(B.Snapshot s,bool ok,string? error=null){bool current=ok&&s.Command is {} c&&B.AuthorityCurrent(c);return new(ok,current&&s.InputReady,s.Command?.Action=="closed"&&s.CleanupComplete,s.CleanupComplete,s.ResumeAllowed,s.Command?.OwnerToken,s.Command?.Generation.ToString(CultureInfo.InvariantCulture),s.Phase,error,s.InputMode,current&&s.OrdinaryReady,s.NoOwnedOverride,s.Aborted,current&&s.MenuLayoutReady);}
 bool IsAgent(uint pid){using var p=OpenProcess(0x101000,false,pid);return !p.IsInvalid&&ProcessIdToSessionId(pid,out uint sid)&&sid==wrapper.SessionId&&Image(p,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GamingMode","GamingMode.exe"));}
 static bool Alive(SafeProcessHandle p,uint pid,long birth)=>!p.IsClosed&&!p.IsInvalid&&Wait(p,0)==258&&Pid(p)==pid&&Times(p,out long b,out long exit,out _,out _)&&b==birth&&exit==0;
 static bool Image(SafeProcessHandle p,string path){var b=new StringBuilder(32768);uint size=32768;return QueryImage(p,0,b,ref size)&&b.ToString().Equals(path,StringComparison.OrdinalIgnoreCase);}
 public void Dispose(){lock(gate){if(disposed)return;disposed=true;}stop.Cancel();try{serve.GetAwaiter().GetResult();}finally{scope.Dispose();lock(gate){authority?.Dispose();authority=null;}stop.Dispose();}}
 [DllImport("kernel32.dll")]static extern SafeProcessHandle OpenProcess(uint access,bool inherit,uint pid);
 [DllImport("kernel32.dll",EntryPoint="GetNamedPipeClientProcessId")]static extern bool ClientPid(nint pipe,out uint pid);
 [DllImport("kernel32.dll")]static extern bool ProcessIdToSessionId(uint pid,out uint session);
 [DllImport("kernel32.dll",EntryPoint="WaitForSingleObject")]static extern uint Wait(SafeProcessHandle p,uint ms);
 [DllImport("kernel32.dll",EntryPoint="GetProcessId")]static extern uint Pid(SafeProcessHandle p);
 [DllImport("kernel32.dll",EntryPoint="GetProcessTimes")]static extern bool Times(SafeProcessHandle p,out long birth,out long exit,out long kernel,out long user);
 [DllImport("kernel32.dll",EntryPoint="QueryFullProcessImageNameW",CharSet=CharSet.Unicode)]static extern bool QueryImage(SafeProcessHandle p,uint flags,StringBuilder path,ref uint size);
 [DllImport("user32.dll",EntryPoint="GetForegroundWindow")]static extern nint Foreground();
 [DllImport("user32.dll",EntryPoint="GetWindowThreadProcessId")]static extern uint WindowPid(nint hwnd,out uint pid);
 [DllImport("user32.dll",EntryPoint="GetAncestor")]static extern nint Root(nint hwnd,uint flags);
 [DllImport("user32.dll")]static extern bool IsWindowVisible(nint hwnd);
 [DllImport("user32.dll")]static extern bool IsWindowEnabled(nint hwnd);
 [DllImport("user32.dll")]static extern bool IsIconic(nint hwnd);
}

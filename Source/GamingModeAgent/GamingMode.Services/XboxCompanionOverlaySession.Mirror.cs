// Playhub Xbox Session. Distributed under GPL-3.0-only.
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using Playhub.DeckyHost;
using Playhub.Shared;
namespace GamingMode.Services;
internal static partial class XboxCompanionOverlaySession
{
 internal sealed record MirrorDecision(bool Ok,string? GameId=null,uint CompanionPid=0,uint OverlayPid=0,int ForegroundScope=0,bool OverlayOpen=false,string? SessionId=null,SteamMirrorDto? Mirror=null,bool Queued=false,string? Error=null,string? MirrorSessionId=null,SteamMirrorContinuationDto? Continuation=null);
 private sealed class MirrorOwner:IDisposable
 {
  internal required Process Wrapper,Game,Companion,Overlay;internal required ulong GameId;internal required uint GamePid;internal required long GameBirth,WrapperBirth,CompanionBirth,OverlayBirth;internal required string Name;internal required Func<bool> AssociationCurrent;internal string? Aumid;internal Win32OwnedAssociationCatalog.Lease? OwnedAssociation;
  internal bool Current()=>!Wrapper.HasExited&&!Game.HasExited&&!Companion.HasExited&&!Overlay.HasExited&&Wrapper.StartTime.ToFileTimeUtc()==WrapperBirth&&Game.StartTime.ToFileTimeUtc()==GameBirth&&Companion.StartTime.ToFileTimeUtc()==CompanionBirth&&Overlay.StartTime.ToFileTimeUtc()==OverlayBirth&&AssociationCurrent();
  internal bool GameForeground(){nint window=OverlayWindowTools.GetForegroundWindowHandle();if(!OverlayWindowTools.IsPresentGameSurface(window)||GetWindowThreadProcessId(window,out uint pid)==0)return false;bool exact=pid==GamePid||(Aumid is not null&&UwpFrameWindow.Matches(window,Aumid));return exact&&OverlayWindowTools.GetForegroundWindowHandle()==window;}
  internal bool PresentationForeground(SteamMirrorDto mirror)
  {
   if(mirror.ActualScope==1)return GameForeground();
   if(mirror.ActualScope!=3||!SteamMirrorWireProtocol.Shown(mirror,Environment.TickCount64)
       ||!SteamMirrorWireProtocol.Number(mirror.SourceHwnd,out ulong raw)||raw>long.MaxValue
       ||!SteamMirrorWireProtocol.Number(mirror.SourceCookie,out ulong cookie))return false;
   nint window=(nint)raw;uint thread=GetWindowThreadProcessId(window,out uint pid);
   if(window==0||thread==0||pid!=(uint)Companion.Id||pid!=mirror.OwnerPid
       ||OverlayWindowTools.GetForegroundWindowHandle()!=window||MirrorGetAncestor(window,2)!=window
       ||MirrorGetWindow(window,4)!=0||!MirrorIsWindowEnabled(window)||!MirrorIsWindowVisible(window)
       ||unchecked((ulong)MirrorGetWindowLongPtr(window,-21))!=cookie||!Current())return false;
   if(!Current()||!SteamMirrorWireProtocol.Shown(mirror,Environment.TickCount64))return false;
   return OverlayWindowTools.GetForegroundWindowHandle()==window
       &&GetWindowThreadProcessId(window,out uint afterPid)==thread&&afterPid==pid
       &&MirrorGetAncestor(window,2)==window&&MirrorGetWindow(window,4)==0
       &&MirrorIsWindowEnabled(window)&&MirrorIsWindowVisible(window)
       &&unchecked((ulong)MirrorGetWindowLongPtr(window,-21))==cookie
       &&OverlayWindowTools.GetForegroundWindowHandle()==window;
  }
  internal string Session=>SessionIdentity(GameId,(uint)Wrapper.Id,WrapperBirth,GamePid,GameBirth,(uint)Companion.Id,CompanionBirth,(uint)Overlay.Id,OverlayBirth);
  internal string MirrorSession=>SteamMirrorWireProtocol.Session(GameId,(uint)Wrapper.Id,WrapperBirth,GamePid,GameBirth,(uint)Companion.Id,CompanionBirth);
  public void Dispose(){Overlay.Dispose();Companion.Dispose();Game.Dispose();Wrapper.Dispose();OwnedAssociation?.Dispose();}
 }
 [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="GetAncestor")] private static extern nint MirrorGetAncestor(nint window,uint flags);
 [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="GetWindow")] private static extern nint MirrorGetWindow(nint window,uint command);
 [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] private static extern nint MirrorGetWindowLongPtr(nint window,int index);
 [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="IsWindowEnabled")] [return:System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] private static extern bool MirrorIsWindowEnabled(nint window);
 [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="IsWindowVisible")] [return:System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] private static extern bool MirrorIsWindowVisible(nint window);
 private static async Task<byte[]?> MirrorBytes(NamedPipeClientStream pipe,int maximum,CancellationToken cancellation){byte[] buffer=new byte[maximum];int length=0;while(length<buffer.Length){int n=await pipe.ReadAsync(buffer.AsMemory(length),cancellation);if(n==0)break;length+=n;}return length>0&&length<maximum?buffer[..length]:null;}
 private static async Task<MirrorOwner?> MirrorOwnerAsync(uint appId,uint overlayPid,CancellationToken cancellation)
 {
  if(appId<0x80000000||UwpSteamSessionAssociation.ReadInstalledImages() is not { } images||SteamInstallationPath.FindRoot() is not { } root)return null;
  uint wrapperPid,gamePid;long wrapperBirth,gameBirth;ulong gameId;string? aumid=null;Func<bool> association;Win32OwnedAssociationCatalog.Lease? ownedAssociation=null;
  var uwp=UwpSteamSessionAssociation.FindForApp(appId,images);
  if(uwp is [var registered]&&UwpSteamSessionAssociation.IsOnlyRunningInstance(registered)){wrapperPid=registered.WrapperPid;wrapperBirth=registered.WrapperBirth;gamePid=registered.GamePid;gameBirth=registered.GameBirth;gameId=((ulong)appId<<32)|0x02000000UL;aumid=registered.Aumid;association=()=>UwpSteamSessionAssociation.Find(gamePid,appId)==registered&&UwpSteamSessionAssociation.IsOnlyRunningInstance(registered);}
  else if(uwp.Count==0&&await Win32SteamSessionAssociation.FindLeaseAsync(appId,cancellation) is { } directLease){ownedAssociation=directLease;var direct=directLease.Identity;wrapperPid=direct.WrapperPid;wrapperBirth=direct.WrapperBirth;gamePid=direct.GamePid;gameBirth=direct.GameBirth;gameId=direct.GameId;association=()=>directLease.Current;}
  else return null;
  Process? wrapper=null,game=null,companion=null,overlay=null;bool transferred=false;
  try{
   wrapper=Process.GetProcessById((int)wrapperPid);game=Process.GetProcessById((int)gamePid);int session=wrapper.SessionId;
   if(wrapper.HasExited||game.HasExited||wrapper.StartTime.ToFileTimeUtc()!=wrapperBirth||game.StartTime.ToFileTimeUtc()!=gameBirth||game.SessionId!=session||!IsProcess(wrapper,images.Wrapper,session)||!association())return null;
   string name=FormattableString.Invariant($"Playhub.XboxCompanion.{wrapperPid}.{wrapperBirth}");using var bounded=CancellationTokenSource.CreateLinkedTokenSource(cancellation);bounded.CancelAfter(600);
   using(var status=new NamedPipeClientStream(".",name,PipeDirection.In,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly)){
    await status.ConnectAsync(bounded.Token);if(!GetNamedPipeServerProcessId(status.SafePipeHandle.DangerousGetHandle(),out uint owner))return null;companion=Process.GetProcessById((int)owner);
    if(!IsProcess(companion,images.Companion,session)||companion.StartTime<wrapper.StartTime||DeckyProcessTree.Read(companion.Id)?.ParentPid!=wrapper.Id)return null;
    var bytes=await MirrorBytes(status,512,bounded.Token);var state=bytes is null?null:JsonSerializer.Deserialize<State>(bytes);if(state is null||state.GameId!=gameId||state.GamePid!=gamePid||state.GameBirth!=gameBirth)return null;
   }
   long companionBirth=companion.StartTime.ToFileTimeUtc();var host=NativeSteamOverlayHost.Find(root,(uint)companion.Id,companionBirth,gameId,session);if(host is null||(overlayPid!=0&&host.Pid!=overlayPid))return null;overlay=Process.GetProcessById((int)host.Pid);
   if(!IsProcess(overlay,Path.Combine(root,"gameoverlayui64.exe"),session)||overlay.StartTime.ToFileTimeUtc()!=host.Birth||!association())return null;
   var result=new MirrorOwner{Wrapper=wrapper,Game=game,Companion=companion,Overlay=overlay,WrapperBirth=wrapperBirth,GameBirth=gameBirth,GamePid=gamePid,GameId=gameId,CompanionBirth=companionBirth,OverlayBirth=host.Birth,Name=name,Aumid=aumid,AssociationCurrent=association,OwnedAssociation=ownedAssociation};if(!result.Current())return null;transferred=true;return result;
  }finally{if(!transferred){overlay?.Dispose();companion?.Dispose();game?.Dispose();wrapper?.Dispose();ownedAssociation?.Dispose();}}
 }
 private static bool MirrorMatchesOwner(SteamMirrorDto mirror,MirrorOwner owner)=>SteamMirrorWireProtocol.Identity(mirror)&&mirror.SessionId==owner.MirrorSession&&mirror.GameId==owner.GameId.ToString(CultureInfo.InvariantCulture)&&mirror.GamePid==owner.GamePid&&mirror.GameBirth==owner.GameBirth.ToString(CultureInfo.InvariantCulture)&&mirror.OwnerPid==(uint)owner.Companion.Id&&mirror.OwnerBirth==owner.CompanionBirth.ToString(CultureInfo.InvariantCulture);
 private static SteamMirrorContinuationDto? ContinuationForOwner(SteamMirrorContinuationDto? continuation,MirrorOwner owner)
 {
  return owner.Current()&&SteamMirrorWireProtocol.ContinuationMatchesOwner(continuation,owner.MirrorSession,(uint)owner.Companion.Id,owner.CompanionBirth,owner.GamePid,owner.GameBirth,owner.GameId,Environment.TickCount64)
    &&owner.Current()?continuation:null;
 }
 private static async Task<SteamMirrorStatusEnvelope?> ReadMirror(MirrorOwner owner,CancellationToken cancellation){using var bounded=CancellationTokenSource.CreateLinkedTokenSource(cancellation);bounded.CancelAfter(350);using var pipe=new NamedPipeClientStream(".",owner.Name+".Mirror",PipeDirection.In,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);await pipe.ConnectAsync(bounded.Token);if(!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(),out uint pid)||pid!=owner.Companion.Id||!owner.Current())return null;var bytes=await MirrorBytes(pipe,4096,bounded.Token);var result=bytes is null?null:JsonSerializer.Deserialize<SteamMirrorStatusEnvelope>(bytes,SteamMirrorWireProtocol.Json);return result is {Protocol:1}&&owner.Current()?result:null;}
 internal static async Task<MirrorDecision> ReadMirrorPresentationAsync(uint appId,uint overlayPid,CancellationToken cancellation,Action<string> diagnostic)
 {
  try{using var owner=await MirrorOwnerAsync(appId,overlayPid,cancellation);if(owner is null)return new(false,Error:"owned-session-unavailable");var envelope=await ReadMirror(owner,cancellation);if(!owner.Current())return new(false,Error:"mirror-owner-changed");var continuation=ContinuationForOwner(envelope?.Continuation,owner);if(envelope is not {Available:true,Mirror:{ } mirror}||!MirrorMatchesOwner(mirror,owner))return new(false,owner.GameId.ToString(CultureInfo.InvariantCulture),(uint)owner.Companion.Id,(uint)owner.Overlay.Id,SessionId:owner.Session,Error:"mirror-not-prepared",MirrorSessionId:owner.MirrorSession,Continuation:continuation);bool foreground=owner.PresentationForeground(mirror);if(!foreground)mirror=SteamMirrorWireProtocol.RevokeForegroundFlags(mirror);continuation=ContinuationForOwner(continuation,owner);return new(true,owner.GameId.ToString(CultureInfo.InvariantCulture),(uint)owner.Companion.Id,(uint)owner.Overlay.Id,foreground?(int)mirror.ActualScope:0,false,owner.Session,mirror,MirrorSessionId:owner.MirrorSession,Continuation:continuation);}
  catch(Exception e)when(e is IOException or ArgumentException or InvalidOperationException or OverflowException or UnauthorizedAccessException or OperationCanceledException or JsonException or System.ComponentModel.Win32Exception){diagnostic("Mirror read refused: "+e.GetType().Name);return new(false,Error:"mirror-unavailable");}
 }
 internal static async Task<MirrorDecision> SetMirrorPresentationAsync(uint appId,uint overlayPid,string action,JsonElement expectedMirror,CancellationToken cancellation,Action<string> diagnostic)
 {
  if(action is not ("show" or "close")||!SteamMirrorWireProtocol.Expected(expectedMirror,out var expected)||expected is null)return new(false,Error:"invalid-mirror-command");
  try{using var owner=await MirrorOwnerAsync(appId,overlayPid,cancellation);if(owner is null)return new(false,Error:"owned-session-unavailable");var envelope=await ReadMirror(owner,cancellation);var mirror=envelope?.Mirror;if(envelope is not {Available:true}||mirror is null||!MirrorMatchesOwner(mirror,owner)||!SteamMirrorWireProtocol.Same(mirror,expected)||!owner.Current()||(action=="show"&&(!owner.GameForeground()||!SteamMirrorWireProtocol.Prepared(mirror,Environment.TickCount64))))return new(false,Error:"mirror-command-stale");
   using var bounded=CancellationTokenSource.CreateLinkedTokenSource(cancellation);bounded.CancelAfter(400);using var pipe=new NamedPipeClientStream(".",owner.Name+".MirrorControl",PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);await pipe.ConnectAsync(bounded.Token);if(!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(),out uint pid)||pid!=owner.Companion.Id||!owner.Current()||(action=="show"&&!owner.GameForeground()))return new(false,Error:"mirror-owner-changed");var command=JsonSerializer.SerializeToUtf8Bytes(new SteamMirrorCommand(1,action,mirror),SteamMirrorWireProtocol.Json);await pipe.WriteAsync(command,bounded.Token);await pipe.WriteAsync(new byte[]{10},bounded.Token);var bytes=await MirrorBytes(pipe,512,bounded.Token);var answer=bytes is null?null:JsonSerializer.Deserialize<SteamMirrorCommandResult>(bytes,SteamMirrorWireProtocol.Json);return answer is {Ok:true,Queued:true}&&owner.Current()?new(true,owner.GameId.ToString(CultureInfo.InvariantCulture),(uint)owner.Companion.Id,(uint)owner.Overlay.Id,owner.GameForeground()?1:0,false,owner.Session,mirror,true):new(false,Error:"native-mirror-refused");
  }catch(Exception e)when(e is IOException or ArgumentException or InvalidOperationException or OverflowException or UnauthorizedAccessException or OperationCanceledException or JsonException or System.ComponentModel.Win32Exception){diagnostic("Mirror control refused: "+e.GetType().Name);return new(false,Error:"mirror-command-unavailable");}
 }
 internal static async Task<Decision> RequestMirrorPreparationAsync(uint appId,CancellationToken cancellation,Action<string> diagnostic)
 {
  // This entrypoint never enters legacy keyboard/replay fallback, including
  // unavailable/unknown capability and failures before the one delivery.
  var attempt=await TryRequestMirrorPreparationAsync(appId,cancellation,diagnostic);
  return attempt.Handled?attempt.Result:new(false);
 }
 internal static async Task<(bool Handled,Decision Result)> TryRequestMirrorPreparationAsync(uint appId,CancellationToken cancellation,Action<string> diagnostic,OwnedWin32Route? expectedRoute=null)
 {
  bool sent=false;
  try{using var owner=await MirrorOwnerAsync(appId,0,cancellation);bool Expected()=>expectedRoute is null||(owner?.OwnedAssociation?.Route is { } actual&&OwnedWin32RoutePolicy.Same(expectedRoute,actual));if(owner is null||!Expected()||!owner.GameForeground())return(false,new(false));var envelope=await ReadMirror(owner,cancellation);if(envelope is not {Available:true})return(false,new(false));using var bounded=CancellationTokenSource.CreateLinkedTokenSource(cancellation);bounded.CancelAfter(300);using var pipe=new NamedPipeClientStream(".",owner.Name+".Open",PipeDirection.Out,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);await pipe.ConnectAsync(bounded.Token);if(!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(),out uint pid)||pid!=owner.Companion.Id||!Expected()||!owner.Current()||!owner.GameForeground())return(true,new(false));ulong baseline=0;if(envelope.Mirror is { } previous)SteamMirrorWireProtocol.Number(previous.Generation,out baseline);
   sent=true;await pipe.WriteAsync(new byte[]{1},bounded.Token);
   // Exactly one delivery. Admission is a real native generation and owned
   // hidden HWND, published before slow callback setup; reads never re-open.
   using var admission=CancellationTokenSource.CreateLinkedTokenSource(cancellation);admission.CancelAfter(1500);
   while(!admission.IsCancellationRequested&&owner.Current()){
    var observed=await ReadMirror(owner,admission.Token);if(observed is {Available:true,Mirror:{ } receipt}&&MirrorMatchesOwner(receipt,owner)&&SteamMirrorWireProtocol.AdmissionAfter(receipt,baseline))
     return(true,new(true,owner.GameId.ToString(CultureInfo.InvariantCulture),(uint)owner.Companion.Id,0,(uint)owner.Overlay.Id,owner.GameForeground()?1:0,false,owner.Session,receipt));
    await Task.Delay(10,admission.Token);
   }
   return(true,new(false));}
  catch(Exception e)when(e is IOException or ArgumentException or InvalidOperationException or OverflowException or UnauthorizedAccessException or OperationCanceledException or JsonException or System.ComponentModel.Win32Exception){diagnostic("Mirror prepare refused: "+e.GetType().Name);return(sent,new(false));}
 }
}

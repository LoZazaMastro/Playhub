using System.Diagnostics;
using System.IO;
// Playhub changes. GPL-3.0-only. Authenticated UI intent; native proof stays routing-only.
using System.Globalization;
using System.IO.Pipes;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using W=SteamNativeOverlayInputWire;
namespace GamingMode.Services;
internal static class NativeGameOverlayEndpoints
{
 internal sealed record Browser(uint OverlayPid,uint BrowserId,uint AppId);
 internal sealed record Callback(uint OverlayPid,uint BrowserId,uint AppId,bool Active,string Serial);
 internal sealed record Request(uint AppId,OwnedWin32Route? ExpectedRoute=null);
 internal sealed record InputRequest(uint AppId,string Action,NativeGameOverlayProof ExpectedNative,Browser Browser,
  string? OwnerToken=null,string? Generation=null,Callback? Callback=null);
 internal sealed record InputDecision(bool Ok,bool InputReady=false,bool Closed=false,bool CleanupComplete=false,bool ResumeAllowed=false,
  string? OwnerToken=null,string? Generation=null,NativeGameOverlayProof? NativeGame=null,string? Phase=null,string? Error=null,string? InputMode=null,bool OrdinaryReady=false,bool NoOwnedOverride=false,long? ProofProducedAfterMs=null,bool Aborted=false,bool MenuLayoutReady=false);
 internal static async Task<NativeGameOverlayDecision> Read(uint appId,OwnedWin32Route? expected,CancellationToken cancel)
 {
  cancel.ThrowIfCancellationRequested();
  try{
   using var owner=await NativeGameOverlayOwner.Open(appId,cancel).ConfigureAwait(false);
   if(owner is not null)return NativeGameOverlayRoute.Read(owner,expected);
  }catch(OperationCanceledException)when(!cancel.IsCancellationRequested){
   // The Win32 transport has its own deadline. A local timeout leaves the
   // independently authenticated packaged route available; caller cancellation
   // still ends this request.
  }
  cancel.ThrowIfCancellationRequested();
  // Missing Win32 proof never means packaged. Preserve that existing path
  // only after its separate authenticated association and foreground proof.
  if(UwpSteamSessionAssociation.ReadInstalledImages() is {} images){
   var matches=UwpSteamSessionAssociation.FindForApp(appId,images);
   if(matches is [var packaged]&&UwpSteamSessionAssociation.IsOnlyRunningInstance(packaged)){
    nint window=OverlayWindowTools.GetForegroundWindowHandle();
    if(window!=0&&OverlayWindowTools.IsPresentGameSurface(window)
     &&(NativeGameOverlayOwner.OwnedGameWindow(window,packaged.GamePid)||UwpFrameWindow.Matches(window,packaged.Aumid))
     &&UwpSteamSessionAssociation.Find(packaged.GamePid,appId)==packaged&&UwpSteamSessionAssociation.IsOnlyRunningInstance(packaged)
     &&OverlayWindowTools.GetForegroundWindowHandle()==window){
      cancel.ThrowIfCancellationRequested();
      return new(true,"packaged");
     }
   }
  }
  cancel.ThrowIfCancellationRequested();
  return new(false,Error:"native-game-owner-unavailable");
 }
 internal static bool Target(NativeGameOverlayProof p,out W.Target? target)
 {
  target=null;bool Number(string value,out ulong n)=>ulong.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out n)&&n>0;
  bool Birth(string value,out long n)=>long.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out n)&&n>0;
  if(p.Protocol!=1||!p.RoutingOnly||p.CanonicalAppId<0x80000000||!Number(p.FullGameId,out ulong full)||full!=((ulong)p.CanonicalAppId<<32|0x02000000UL)
   ||!Number(p.ObservedGameId,out ulong observed)||p.GamePid==0||p.WrapperPid==0||p.SteamPid==0||p.OverlayPid==0||p.CompanionPid==0
   ||!Birth(p.GameBirth,out long gb)||!Birth(p.WrapperBirth,out long wb)||!Birth(p.SteamBirth,out long sb)||!Birth(p.OverlayBirth,out long hb)
   ||!Birth(p.CompanionBirth,out long cb)||!Birth(p.GameWindow,out long window)||p.SessionId is not{Length:64}||!p.SessionId.All(Uri.IsHexDigit))return false;
  target=new(full,observed,p.SessionId,p.WrapperPid,wb,p.GamePid,gb,p.SteamPid,sb,p.OverlayPid,hb,p.CompanionPid,cb,window);return true;
 }
 private static bool ValidInput(InputRequest r,out W.Request? wire)
 {
  wire=null;if(r.ExpectedNative is not {} p||r.AppId!=p.CanonicalAppId||r.Action is not("prepare" or "active" or "renew" or "closed" or "cancel" or "abort")
   ||r.Browser is not {} b||b.OverlayPid!=p.OverlayPid||b.BrowserId==0||b.AppId==0||!Target(p,out var target)||target is null)return false;
  ulong gen=0,serial=0;
  if(r.Action!="prepare"&&(!Guid.TryParseExact(r.OwnerToken,"N",out var token)||token==Guid.Empty
   ||!ulong.TryParse(r.Generation,NumberStyles.None,CultureInfo.InvariantCulture,out gen)||gen==0))return false;
  if(r.Action is "active" or "renew" or "closed"){
   if(r.Callback is not {} c||c.OverlayPid!=b.OverlayPid||c.BrowserId!=b.BrowserId||c.AppId!=b.AppId||c.Active!=(r.Action!="closed")
    ||!ulong.TryParse(c.Serial,NumberStyles.None,CultureInfo.InvariantCulture,out serial)||serial==0)return false;
  }
  wire=new(1,r.Action,target,b.BrowserId,b.AppId,r.OwnerToken,gen,r.Callback?.Active,serial);return true;
 }
 internal static async Task<InputDecision> Control(InputRequest request,CancellationToken cancel)
 {
  if(!ValidInput(request,out var wire)||wire is null)return new(false,Error:"invalid-native-input-request");
  long controlStarted=Stopwatch.GetTimestamp();
  long? proofProducedAfterMs=null;
  NativeGameOverlayProof proof=request.ExpectedNative;
  bool grants=request.Action is "prepare" or "active" or "renew";
  if(grants){var fresh=await Read(request.AppId,null,cancel).ConfigureAwait(false);
   if(fresh is not{Ok:true,Kind:"native-game",NativeGame:{} actual}||!NativeGameOverlayRoute.Same(proof,actual))return new(false,Error:"native-game-input-owner-changed");proof=actual;}
  // Cleanup is exact token/gen on the original configured sidecar, not a new
  // grant. It remains reachable after AltTab/host death/proof expiry.
  var result=await Send(proof,wire,cancel).ConfigureAwait(false);
  if(grants&&result.Ok){
   // Subtract only producer work BEFORE this final fresh read. Including the
   // read or response transit would understate the client proof age.
   proofProducedAfterMs=Math.Max(0L,(long)Stopwatch.GetElapsedTime(controlStarted).TotalMilliseconds);
   var fresh=await Read(request.AppId,null,cancel).ConfigureAwait(false);
   if(fresh is not{Ok:true,Kind:"native-game",NativeGame:{} actual}||!NativeGameOverlayRoute.Same(proof,actual)){
    if(result.OwnerToken is {} token&&ulong.TryParse(result.Generation,out ulong gen)){
     using var clean=new CancellationTokenSource(700);try{_ = await Send(proof,wire with{Action="cancel",OwnerToken=token,Generation=gen},clean.Token).ConfigureAwait(false);}catch(Exception e)when(Expected(e)){}
    }
    return new(false,OwnerToken:result.OwnerToken,Generation:result.Generation,NativeGame:proof,Error:"native-game-input-final-owner-changed");
   }proof=actual;
  }
  return new(result.Ok,result.InputReady,result.Closed,result.CleanupComplete,result.ResumeAllowed,result.OwnerToken,result.Generation,proof,result.Phase,result.Error,result.InputMode,result.OrdinaryReady,result.NoOwnedOverride,proofProducedAfterMs,result.Aborted,result.MenuLayoutReady);
 }
 private static async Task<W.Reply> Send(NativeGameOverlayProof proof,W.Request request,CancellationToken cancel)
 {
  if(UwpSteamSessionAssociation.ReadInstalledImages() is not {} images)return new(false,Error:"native-input-installed-unavailable");
  var system=new NativeOwnedAssociationSystem();using var companion=RetainedOwnedWin32Process.Open(system.Processes,proof.CompanionPid);
  if(companion?.Current!=true||companion.Identity.Birth.ToString(CultureInfo.InvariantCulture)!=proof.CompanionBirth
   ||companion.Identity.Sid!=system.Self.Sid||companion.Identity.Session!=system.Self.Session
   ||!companion.Identity.Image.Equals(images.Companion,StringComparison.OrdinalIgnoreCase))return new(false,Error:"native-input-server-unavailable");
  string name=FormattableString.Invariant($"Playhub.XboxCompanion.{proof.WrapperPid}.{proof.WrapperBirth}.NativeInput");
  using var bound=CancellationTokenSource.CreateLinkedTokenSource(cancel);bound.CancelAfter(2900);
  using var pipe=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
  await pipe.ConnectAsync(bound.Token).ConfigureAwait(false);
  if(!NativeGameOverlayOwner.ServerPid(pipe.SafePipeHandle.DangerousGetHandle(),out uint pid)||pid!=proof.CompanionPid||!companion.Current)return new(false,Error:"native-input-server-changed");
  byte[] payload=JsonSerializer.SerializeToUtf8Bytes(request,W.Json);await pipe.WriteAsync(payload,bound.Token).ConfigureAwait(false);await pipe.WriteAsync(new byte[]{10},bound.Token).ConfigureAwait(false);
  byte[] bytes=new byte[4097];int used=0;while(used<bytes.Length){int n=await pipe.ReadAsync(bytes.AsMemory(used),bound.Token).ConfigureAwait(false);if(n==0)break;used+=n;}
  if(used==0||used>4096||!companion.Current)return new(false,Error:"native-input-reply-unavailable");
  return JsonSerializer.Deserialize<W.Reply>(bytes.AsSpan(0,used),W.Json)??new(false,Error:"native-input-reply-invalid");
 }
 internal static bool Expected(Exception e)=>e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException
  or OperationCanceledException or JsonException or OverflowException or System.ComponentModel.Win32Exception or System.Security.SecurityException;
 internal static void Map(WebApplication app,XboxShellBroker shell,Action<string> diagnostic)
 {
  bool Authorized(HttpContext c)=>shell.IsAuthorized(c.Connection.RemoteIpAddress,c.Request.Headers["X-Playhub-Shell-Token"].ToString());
  app.MapPost("/session/steam/native-game-overlay",async(HttpContext c)=>{
   if(!Authorized(c))return Results.StatusCode(401);using var stop=CancellationTokenSource.CreateLinkedTokenSource(c.RequestAborted,app.Lifetime.ApplicationStopping);
   try{var body=await Body<Request>(c,stop.Token).ConfigureAwait(false);return body is not{AppId:>=0x80000000}?Results.StatusCode(400):Results.Json(await Read(body.AppId,body.ExpectedRoute,stop.Token).ConfigureAwait(false));}
   catch(Exception e)when(Expected(e)){diagnostic("Original game overlay proof refused: "+e.GetType().Name);return Results.Json(new NativeGameOverlayDecision(false,Error:"native-game-unavailable"));}
  });
  app.MapPost("/session/steam/native-game-overlay-input",async(HttpContext c)=>{
   if(!Authorized(c))return Results.StatusCode(401);using var stop=CancellationTokenSource.CreateLinkedTokenSource(c.RequestAborted,app.Lifetime.ApplicationStopping);
   try{var body=await Body<InputRequest>(c,stop.Token).ConfigureAwait(false);return body is null?Results.StatusCode(400):Results.Json(await Control(body,stop.Token).ConfigureAwait(false));}
   catch(Exception e)when(Expected(e)){diagnostic("Original game overlay input refused: "+e.GetType().Name);return Results.Json(new InputDecision(false,Error:"native-game-input-unavailable"));}
  });
 }
 private static async Task<T?> Body<T>(HttpContext context,CancellationToken cancel){byte[] data=new byte[8193];int used=0;while(used<data.Length){int n=await context.Request.Body.ReadAsync(data.AsMemory(used),cancel).ConfigureAwait(false);if(n==0)break;used+=n;}return used is >0 and <=8192?JsonSerializer.Deserialize<T>(data.AsSpan(0,used),W.Json):default;}
}

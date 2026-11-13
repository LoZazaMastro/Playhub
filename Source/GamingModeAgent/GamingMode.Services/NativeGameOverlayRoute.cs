// Playhub changes. GPL-3.0-only.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
namespace GamingMode.Services;

internal sealed record NativeGameOverlayProof(int Protocol,uint CanonicalAppId,string FullGameId,string ObservedGameId,
 string SessionId,uint GamePid,string GameBirth,uint WrapperPid,string WrapperBirth,uint SteamPid,string SteamBirth,
 uint OverlayPid,string OverlayBirth,uint CompanionPid,string CompanionBirth,string GameWindow,string ObservedAt,string ExpiresAt,bool RoutingOnly=true);
internal sealed record NativeGameOverlayDecision(bool Ok,string Kind="unknown",NativeGameOverlayProof? NativeGame=null,string? Error=null);
internal interface INativeGameOverlayOwner:IDisposable
{
 OwnedWin32Route Route { get; }
 OwnedProcessFacts Companion { get; }
 bool Current { get; }
 nint GameForeground();
 NativeGameHostSearch FindHost();
 long Tick { get; }
}
internal sealed record NativeGameHostSearch(string Kind,IOwnedWin32Process? Host=null);
internal static class NativeGameOverlayRoute
{
 internal static bool Same(NativeGameOverlayProof a,NativeGameOverlayProof b)=>a.Protocol==1&&b.Protocol==1&&a.RoutingOnly&&b.RoutingOnly
  &&a.CanonicalAppId==b.CanonicalAppId&&a.FullGameId==b.FullGameId&&a.ObservedGameId==b.ObservedGameId&&a.SessionId==b.SessionId
  &&a.GamePid==b.GamePid&&a.GameBirth==b.GameBirth&&a.WrapperPid==b.WrapperPid&&a.WrapperBirth==b.WrapperBirth
  &&a.SteamPid==b.SteamPid&&a.SteamBirth==b.SteamBirth&&a.OverlayPid==b.OverlayPid&&a.OverlayBirth==b.OverlayBirth
  &&a.CompanionPid==b.CompanionPid&&a.CompanionBirth==b.CompanionBirth&&a.GameWindow==b.GameWindow;
 internal static NativeGameOverlayDecision Read(INativeGameOverlayOwner owner,OwnedWin32Route? expected=null)
 {
  var route=owner.Route;long start=owner.Tick;
  bool Current()=>owner.Current&&OwnedWin32RoutePolicy.Same(route,owner.Route)
   &&(expected is null||OwnedWin32RoutePolicy.Same(expected,route))&&owner.Tick>=start&&owner.Tick-start<1500;
  if(!Current())return new(false,Error:"native-game-owner-unavailable");
  nint window=owner.GameForeground();if(window==0)return new(false,Error:"native-game-owner-unavailable");
  var search=owner.FindHost();using var host=search.Host;
  if(!Current()||owner.GameForeground()!=window||!Current())return new(false,Error:"native-game-owner-changed");
  if(search.Kind=="absent"&&host is null)return new(true,"absent");
  if(search.Kind!="present"||host is null||!host.Current)return new(false,Error:"native-game-host-unavailable");
  var h=host.Identity;
  if(h.Pid==0||h.Birth<=0||route.ObservedSteamGameId is not { } observed
   ||!ulong.TryParse(observed,NumberStyles.None,CultureInfo.InvariantCulture,out ulong id)||id==0
   ||h.Pid==route.GamePid||h.Pid==route.WrapperPid||h.Pid==route.SteamPid)return new(false,Error:"native-game-host-invalid");
  var companion=owner.Companion;
  string identity=string.Join("|",route.OriginalGameId,observed,route.GamePid,route.GameBirth,route.WrapperPid,route.WrapperBirth,
   route.SteamPid,route.SteamBirth,h.Pid,h.Birth,companion.Pid,companion.Birth,window.ToInt64());
  string session=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
  long at=owner.Tick;var proof=new NativeGameOverlayProof(1,route.CanonicalShortcutAppId,route.OriginalGameId,observed,session,
   route.GamePid,route.GameBirth,route.WrapperPid,route.WrapperBirth,route.SteamPid,route.SteamBirth,h.Pid,
   h.Birth.ToString(CultureInfo.InvariantCulture),companion.Pid,companion.Birth.ToString(CultureInfo.InvariantCulture),window.ToInt64().ToString(CultureInfo.InvariantCulture),at.ToString(CultureInfo.InvariantCulture),
   checked(start+1500).ToString(CultureInfo.InvariantCulture));
  return Current()&&host.Current&&host.Identity==h&&owner.GameForeground()==window&&Current()&&host.Current
   ?new(true,"native-game",proof):new(false,Error:"native-game-final-owner-changed");
 }
}

// Playhub changes. GPL-3.0-only. Private managed pipe; never a C++/Mirror ABI.
using System.Text.Json;
internal static class SteamNativeOverlayInputWire
{
 internal sealed record Target(ulong FullGameId,ulong ObservedGameId,string SessionId,uint WrapperPid,long WrapperBirth,
  uint GamePid,long GameBirth,uint SteamPid,long SteamBirth,uint HostPid,long HostBirth,uint CompanionPid,long CompanionBirth,long GameWindow);
 internal sealed record Request(int Protocol,string Action,Target Target,uint BrowserId,uint NativeAppId,
  string? OwnerToken=null,ulong Generation=0,bool? CallbackActive=null,ulong CallbackSerial=0);
 internal sealed record Reply(bool Ok,bool InputReady=false,bool Closed=false,bool CleanupComplete=false,bool ResumeAllowed=false,
  string? OwnerToken=null,string? Generation=null,string? Phase=null,string? Error=null,string? InputMode=null,bool OrdinaryReady=false,bool NoOwnedOverride=false,bool Aborted=false,bool MenuLayoutReady=false);
 internal static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
}

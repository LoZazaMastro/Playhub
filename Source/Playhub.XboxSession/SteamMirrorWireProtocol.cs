// Playhub Xbox Session. Distributed under GPL-3.0-only.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Playhub.Shared;
internal sealed record SteamMirrorDto
{
 public int Protocol {get;init;}=1; public string State {get;init;}="closed";
 public string SessionId {get;init;}="";public string Generation {get;init;}="";public string GameId {get;init;}="";
 public string ExpiresAt {get;init;}="";public string ObservedAt {get;init;}="";public string ReadAt {get;init;}="";
 public string SourceHwnd {get;init;}="";public string SourceCookie {get;init;}="";public string DestinationHwnd {get;init;}="";public string DestinationCookie {get;init;}="";
 public uint OwnerPid {get;init;}public string OwnerBirth {get;init;}="";public uint GamePid {get;init;}public string GameBirth {get;init;}="";
 public bool PreparedCurrent {get;init;}public bool ShownCurrent {get;init;}public bool LeaseCurrent {get;init;}public bool DestinationShown {get;init;}public bool DwmAttached {get;init;}
 public bool CleanupComplete {get;init;}public uint ActualScope {get;init;}public uint OwnedForegroundPhase {get;init;}public bool PixelsVerified {get;init;}public bool InputAuthority {get;init;}
}
// Passive lineage only; never participates in Prepared/Shown or native-input admission.
internal sealed record SteamMirrorContinuationDto
{
 public int Protocol {get;init;}=1;public uint Phase {get;init;}
 public string SessionId {get;init;}="";public string GuideEpoch {get;init;}="";public string ParentGeneration {get;init;}="";public string ResumedGeneration {get;init;}="";
 public string GapDeadline {get;init;}="";public string ObservedAt {get;init;}="";public string ReadAt {get;init;}="";
 public uint OwnerPid {get;init;}public uint GamePid {get;init;}public string OwnerBirth {get;init;}="";public string GameBirth {get;init;}="";public string GameId {get;init;}="";
 public string SourceHwnd {get;init;}="";public string SourceCookie {get;init;}="";public string TicketLow {get;init;}="";public string TicketHigh {get;init;}="";
 public bool InputAuthority {get;init;}public bool VisualAuthority {get;init;}public bool CleanupComplete {get;init;}
 public string OriginalLeaseExpiry {get;init;}="";public string PreparationExpiresAt {get;init;}="";
}
internal sealed record SteamMirrorStatusEnvelope(int Protocol,bool Available,SteamMirrorDto? Mirror,SteamMirrorContinuationDto? Continuation=null);
internal sealed record SteamMirrorCommand(int Protocol,string Action,SteamMirrorDto ExpectedMirror);
internal sealed record SteamMirrorCommandResult(bool Ok,bool Queued,string? Error=null);
internal static class SteamMirrorWireProtocol
{
 internal static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){MaxDepth=16};
 internal static bool Number(string? text,out ulong value)=>ulong.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out value)&&value!=0&&text==value.ToString(CultureInfo.InvariantCulture);
 internal static bool ZeroNumber(string? text,out ulong value)=>ulong.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out value)&&text==value.ToString(CultureInfo.InvariantCulture);
 internal static bool Continuation(SteamMirrorContinuationDto? c,long now)
 {
  if(c is not {Protocol:1,Phase:>=1 and <=4,OwnerPid:>0,GamePid:>0}||c.InputAuthority||c.VisualAuthority||now<0
    ||c.SessionId is not {Length:64}||!c.SessionId.All(x=>x is >= '0' and <= '9' or >= 'a' and <= 'f')
    ||!Number(c.GuideEpoch,out _)||!Number(c.ParentGeneration,out ulong parent)||!ZeroNumber(c.ResumedGeneration,out ulong resumed)
    ||!Number(c.GapDeadline,out ulong gap)||gap>long.MaxValue||!Number(c.OriginalLeaseExpiry,out ulong original)||original>long.MaxValue||gap>original
    ||!Number(c.ObservedAt,out ulong observed)||!Number(c.ReadAt,out ulong read)||read>(ulong)now||observed>read||(ulong)now-read>250||(ulong)now-observed>250
    ||!Number(c.OwnerBirth,out ulong ownerBirth)||ownerBirth>long.MaxValue||!Number(c.GameBirth,out ulong gameBirth)||gameBirth>long.MaxValue
    ||!Number(c.GameId,out ulong game)||(uint)game!=0x02000000||game>>32<0x80000000
    ||!Number(c.SourceHwnd,out ulong hwnd)||hwnd>long.MaxValue||!Number(c.SourceCookie,out _)
    ||!ZeroNumber(c.TicketLow,out ulong low)||!ZeroNumber(c.TicketHigh,out ulong high)||(low|high)==0
    ||!ZeroNumber(c.PreparationExpiresAt,out ulong prep)||prep>long.MaxValue)return false;
  if(c.Phase==1)return resumed==0&&prep==0&&gap>(ulong)now&&gap>=observed&&gap-observed<=500;
  if(c.Phase is 2 or 4)return resumed>parent&&prep>(ulong)now&&prep>=read&&prep-read<=5000;
  return resumed==0||resumed>parent;
 }
 internal static bool ContinuationMatchesOwner(SteamMirrorContinuationDto? c,string session,uint ownerPid,long ownerBirth,uint gamePid,long gameBirth,ulong gameId,long now)
  =>Continuation(c,now)&&c is not null&&ownerPid>0&&gamePid>0&&ownerBirth>0&&gameBirth>0
    &&c.SessionId==session&&c.OwnerPid==ownerPid&&c.GamePid==gamePid
    &&c.OwnerBirth==ownerBirth.ToString(CultureInfo.InvariantCulture)&&c.GameBirth==gameBirth.ToString(CultureInfo.InvariantCulture)&&c.GameId==gameId.ToString(CultureInfo.InvariantCulture);
 internal static bool Identity(SteamMirrorDto? m)
 {
  if(m is not {Protocol:1,OwnerPid:>0,GamePid:>0}||m.OwnedForegroundPhase>2
      ||m.OwnedForegroundPhase==1&&m.State is not ("shown" or "closed" or "cleanup-pending")
      ||m.OwnedForegroundPhase==2&&(m.State is not ("closed" or "cleanup-pending")||m.ActualScope!=0)||m.SessionId is not {Length:64}||!m.SessionId.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f')||!Number(m.Generation,out _)||!Number(m.GameId,out ulong id)||(uint)id!=0x02000000||id>>32<0x80000000||!Number(m.SourceHwnd,out _)||!Number(m.SourceCookie,out _)||!Number(m.OwnerBirth,out _)||!Number(m.GameBirth,out _))return false;
  if(Number(m.DestinationHwnd,out _)&&Number(m.DestinationCookie,out _))return true;
  return m.DestinationHwnd=="0"&&(m.DestinationCookie=="0"||Number(m.DestinationCookie,out _))&&m.State is "closed" or "cleanup-pending"&&!m.PreparedCurrent&&!m.ShownCurrent&&!m.LeaseCurrent&&!m.DestinationShown&&!m.DwmAttached&&m.ActualScope==0&&(m.State!="closed"||m.CleanupComplete);
 }
 internal static bool Same(SteamMirrorDto a,SteamMirrorDto b)=>Identity(a)&&Identity(b)&&a.SessionId==b.SessionId&&a.Generation==b.Generation&&a.GameId==b.GameId&&a.SourceHwnd==b.SourceHwnd&&a.SourceCookie==b.SourceCookie&&a.DestinationHwnd==b.DestinationHwnd&&a.DestinationCookie==b.DestinationCookie&&a.OwnerPid==b.OwnerPid&&a.OwnerBirth==b.OwnerBirth&&a.GamePid==b.GamePid&&a.GameBirth==b.GameBirth;
 internal static bool Prepared(SteamMirrorDto m,long now)=>Identity(m)&&m.State is "prepared" or "shown"&&m.PreparedCurrent&&m.LeaseCurrent&&m.DwmAttached&&(m.ActualScope==1||m.ActualScope==3&&m.OwnedForegroundPhase==1&&m.State=="shown"&&m.ShownCurrent&&m.DestinationShown)&&!m.PixelsVerified&&!m.InputAuthority&&Number(m.ExpiresAt,out ulong end)&&end>(ulong)now&&Number(m.ObservedAt,out ulong seen)&&seen<=(ulong)now;
 internal static bool AdmissionAfter(SteamMirrorDto m,ulong baseline)=>Identity(m)&&Number(m.Generation,out ulong generation)&&generation>baseline&&m.State is "preparing" or "prepared" or "shown" or "cleanup-pending" or "closed";
 internal static SteamMirrorDto RevokeForegroundFlags(SteamMirrorDto m)=>m with{PreparedCurrent=false,ShownCurrent=false,LeaseCurrent=false,ActualScope=0};
 internal static bool Shown(SteamMirrorDto m,long now)=>Prepared(m,now)&&m.State=="shown"&&m.ShownCurrent&&m.DestinationShown;
 internal static string Session(ulong gameId,uint wrapperPid,long wrapperBirth,uint gamePid,long gameBirth,uint companionPid,long companionBirth)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant($"mirror-v1:{gameId}:{wrapperPid}:{wrapperBirth}:{gamePid}:{gameBirth}:{companionPid}:{companionBirth}")))).ToLowerInvariant();
 internal static bool Expected(JsonElement json,out SteamMirrorDto? expected){expected=null;try{if(json.ValueKind!=JsonValueKind.Object)return false;var names=new HashSet<string>(StringComparer.Ordinal);foreach(var property in json.EnumerateObject())if(!names.Add(property.Name))return false;expected=json.Deserialize<SteamMirrorDto>(Json);return Identity(expected);}catch(JsonException){return false;}}
}

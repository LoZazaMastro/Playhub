// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using System.Text.Json;
namespace Playhub.FirstContent;
internal sealed record CurtainCoverRequest(string Action,uint AppId,string RequestId,ulong RequestEpoch,string? Lease,
    uint PublisherPid=0,long PublisherBirth=0,uint SteamPid=0,long SteamBirth=0,ulong SteamWindow=0,uint SteamThread=0,string SteamClass="",string SourceReceipt="",ulong SurfaceRevision=0,string PublisherImage="",ulong SourceObservedAt=0);
internal static class CurtainCoverProtocol
{
 static readonly HashSet<string> BeginFields=new(StringComparer.Ordinal){"protocol","action","appId","requestId","requestEpoch","publisherPid","publisherBirth","steamPid","steamBirth","steamWindow","steamThread","steamClass","sourceReceipt","surfaceRevision","sourceObservedAt"};
 static readonly HashSet<string> OtherFields=new(StringComparer.Ordinal){"protocol","action","requestId","requestEpoch","lease"};
 static readonly HashSet<string> RenewFields=new(OtherFields,StringComparer.Ordinal){"sourceObservedAt"};
 internal static bool SourceFresh(ulong observed,long now)=>observed>0&&now>=0&&observed<=(ulong)now&&(ulong)now-observed<=700;
 internal static bool Hex(string? s)=>s is {Length:32}&&s.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
 static bool Text(JsonElement r,string key,out string value){value="";if(!r.TryGetProperty(key,out var p)||p.ValueKind!=JsonValueKind.String)return false;value=p.GetString()!;return true;}
 static bool UInt(JsonElement r,string key,out uint value){value=0;return r.TryGetProperty(key,out var p)&&p.ValueKind==JsonValueKind.Number&&p.TryGetUInt32(out value)&&value>0;}
 static bool Decimal(JsonElement r,string key,out ulong value){value=0;return Text(r,key,out var s)&&s.Length is >0 and <=19&&s.All(c=>c is >= '0' and <= '9')&&ulong.TryParse(s,NumberStyles.None,CultureInfo.InvariantCulture,out value)&&value is >0 and <=long.MaxValue;}
 internal static CurtainCoverRequest? Parse(ReadOnlyMemory<byte> body)
 {
  if(body.Length is <2 or >4096)return null;
  try{
   using var doc=JsonDocument.Parse(body,new(){MaxDepth=3});var root=doc.RootElement;
   if(root.ValueKind!=JsonValueKind.Object||!Text(root,"action",out var action)||action is not("begin" or "renew" or "close"))return null;
   var allowed=action=="begin"?BeginFields:action=="renew"?RenewFields:OtherFields;var seen=new HashSet<string>(StringComparer.Ordinal);
   foreach(var p in root.EnumerateObject())if(!allowed.Contains(p.Name)||!seen.Add(p.Name))return null;
   if(!seen.SetEquals(allowed)||!UInt(root,"protocol",out var protocol)||protocol!=1||!Text(root,"requestId",out var request)||!Hex(request)||!Decimal(root,"requestEpoch",out var epoch))return null;
   if(action!="begin"){
    if(!Text(root,"lease",out var lease)||!Hex(lease))return null;
    if(action=="close")return new(action,0,request,epoch,lease);
    return Decimal(root,"sourceObservedAt",out var renewedAt)?new(action,0,request,epoch,lease,SourceObservedAt:renewedAt):null;
   }
   if(!UInt(root,"appId",out var app)||!UInt(root,"publisherPid",out var publisherPid)||!Decimal(root,"publisherBirth",out var publisherBirth)
      ||!UInt(root,"steamPid",out var steamPid)||!Decimal(root,"steamBirth",out var steamBirth)||!Decimal(root,"steamWindow",out var window)||!UInt(root,"steamThread",out var thread)
      ||!Text(root,"sourceReceipt",out var receipt)||!Hex(receipt)||!Decimal(root,"surfaceRevision",out var surfaceRevision)||!Decimal(root,"sourceObservedAt",out var observedAt)
      ||publisherPid==steamPid||!Text(root,"steamClass",out var cls)||cls.Length is <1 or >=256||cls.Any(char.IsControl))return null;
   return new(action,app,request,epoch,null,publisherPid,(long)publisherBirth,steamPid,(long)steamBirth,window,thread,cls,receipt,surfaceRevision,SourceObservedAt:observedAt);
  }catch(JsonException){return null;}
 }
}

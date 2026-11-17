// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using System.Text.Json;
namespace Playhub.FirstContent;
// Created only by a retained native source adapter, never from a UI/HTTP PID.
internal sealed record ContentTarget(ulong Window,uint Pid,long Birth,uint Thread,string Image,string WindowClass);
internal sealed record ContentFacts(long FrameQpc,long ObservedQpc,int Samples,int Opaque,int NonBlack,
    int Width,int Height,int ClientX,int ClientY,int ClientWidth,int ClientHeight)
{
 // Native luma>8 and alpha>=240 define these aggregate samples. This proves
 // observed nonblack content only; a logo/loading image is also content.
 internal bool ObservedNonBlackContent=>Samples>0&&Opaque>0&&NonBlack>0;
 // A fresh opaque frame is rendered output, including a legitimate black
 // intro. Exact target/foreground/freshness checks remain in the parser/client.
 internal bool ObservedOpaqueContent=>Samples>0&&Opaque>0;
}
internal sealed record ContentReadDiagnostic(string Rejection,long? ReceiverFrameAgeMs,bool RetryableExpiredFrame=false);
internal static class ContentFactsParser
{
 internal static bool Envelope(JsonElement r,string id)=>r.ValueKind==JsonValueKind.Object&&id.Length==32&&id.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f')
    &&r.EnumerateObject().Count()<=48&&r.EnumerateObject().Select(p=>p.Name).Distinct(StringComparer.Ordinal).Count()==r.EnumerateObject().Count()
    &&Text(r,"id",id)&&Flag(r,"gameReadyClaimed",false)&&Flag(r,"inputAuthorityClaimed",false)&&Flag(r,"imagesPersisted",false);
 internal static bool ValidTarget(ContentTarget t)=>t.Window is >0 and <=long.MaxValue&&t.Pid>0&&t.Birth>0&&t.Thread>0
    &&t.Image.Length is >3 and <=4096&&t.Image[1]==':'&&t.Image[2]=='\\'&&char.IsAsciiLetter(t.Image[0])
    &&t.WindowClass.Length is >0 and <=256&&!t.Image.Any(char.IsControl)&&!t.WindowClass.Any(char.IsControl);
 static bool Flag(JsonElement r,string k,bool expected)=>r.TryGetProperty(k,out var v)&&v.ValueKind==(expected?JsonValueKind.True:JsonValueKind.False);
 static bool Text(JsonElement r,string k,string expected)=>r.TryGetProperty(k,out var v)&&v.ValueKind==JsonValueKind.String&&v.GetString()==expected;
 static bool Int(JsonElement r,string k,out int value){value=0;return r.TryGetProperty(k,out var v)&&v.ValueKind==JsonValueKind.Number&&v.TryGetInt32(out value);}
 static bool LongText(JsonElement r,string k,out long value){value=0;return r.TryGetProperty(k,out var v)&&v.ValueKind==JsonValueKind.String
    &&v.GetString() is {} s&&s.Length>0&&s.All(c=>c is >= '0' and <= '9')&&long.TryParse(s,NumberStyles.None,CultureInfo.InvariantCulture,out value)&&value>0;}
 static bool Number(JsonElement r,string k,double min,double max)=>r.TryGetProperty(k,out var v)&&v.ValueKind==JsonValueKind.Number
    &&v.TryGetDouble(out var n)&&double.IsFinite(n)&&n>=min&&n<=max;
 internal static ContentFacts? Read(JsonElement r,string expectedId,ContentTarget t,Func<bool> current,Func<long> qpc100ns)
 =>Read(r,expectedId,t,current,qpc100ns,out _);
 internal static ContentFacts? Read(JsonElement r,string expectedId,ContentTarget t,Func<bool> current,Func<long> qpc100ns,out ContentReadDiagnostic diagnostic)
 {
  diagnostic=new("none",null);
  if(!ValidTarget(t)){diagnostic=new("target-invalid",null);return null;}
  if(!current()){diagnostic=new("source-before-frame",null);return null;}
  if(!Envelope(r,expectedId)){diagnostic=new("envelope",null);return null;}
  if(!Flag(r,"gameReadyClaimed",false)||!Flag(r,"inputAuthorityClaimed",false)||!Flag(r,"imagesPersisted",false)){diagnostic=new("authority-flags",null);return null;}
  if(!Text(r,"state","frame-facts")){diagnostic=new("frame-state",null);return null;}
  if(!Flag(r,"sourceCurrent",true)){diagnostic=new("source-current-flag",null);return null;}
  if(!Flag(r,"actualForeground",true)){diagnostic=new("foreground-flag",null);return null;}
  if(!Flag(r,"hdrMeaningClaimed",false)||!Text(r,"format","BGRA8")){diagnostic=new("format",null);return null;}
  if(!r.TryGetProperty("sourcePid",out var pid)||pid.ValueKind!=JsonValueKind.Number||!pid.TryGetUInt32(out var p)||p!=t.Pid
     ||!Text(r,"sourceBirth",t.Birth.ToString(CultureInfo.InvariantCulture))||!Text(r,"sourceHwnd",t.Window.ToString(CultureInfo.InvariantCulture))
     ||!r.TryGetProperty("sourceThread",out var thread)||thread.ValueKind!=JsonValueKind.Number||!thread.TryGetUInt32(out var th)||th!=t.Thread){diagnostic=new("target-identity",null);return null;}
  if(!LongText(r,"frameQpc100ns",out long frame)||!LongText(r,"observedQpc100ns",out long observed)
     ||!Number(r,"ageMs",0,200)||!Number(r,"queryMs",0,200)){diagnostic=new("frame-time-fields",null);return null;}
  if(!Int(r,"contentWidth",out int width)||!Int(r,"contentHeight",out int height)||!Int(r,"clientX",out int x)||!Int(r,"clientY",out int y)
     ||!Int(r,"clientWidth",out int cw)||!Int(r,"clientHeight",out int ch)||width is <1 or >8192||height is <1 or >8192
     ||(long)width*height>16777216||x<0||y<0||x>=width||y>=height||cw<=0||ch<=0||cw>width-x||ch>height-y
     ||!Int(r,"samples",out int count)||count!=Math.Min(32,cw)*Math.Min(18,ch)
     ||!Int(r,"opaqueSamples",out int opaque)||!Int(r,"nonBlackSamples",out int nonBlack)||nonBlack<0||nonBlack>opaque||opaque>count
     ||!Int(r,"minLuma",out int min)||!Int(r,"maxLuma",out int max)||min<0||max>255||min>max
     ||!Number(r,"meanLuma",min,max)||!Number(r,"varianceLuma",0,16256.251)){diagnostic=new("geometry-or-statistics",null);return null;}
  // Current may be slow. Sample receiver QPC only after that final getter.
  if(!current()){diagnostic=new("source-after-frame",null);return null;}long now=qpc100ns();
  long? receiverAge=now>=0?(now-frame)/10000:null;
  if(frame>observed||observed>now||now-frame>2000000||now<0){
   // A valid sample can expire in transit or retained-source validation.
   // It remains unusable for handoff; only that chronological expiration
   // permits querying a NEW frame under the existing source/cover lease.
   bool expired=now>=0&&frame<=observed&&observed<=now&&now-frame>2000000;
   diagnostic=new("receiver-frame-time",receiverAge,expired);return null;
  }
  diagnostic=new("none",receiverAge);
  return new(frame,observed,count,opaque,nonBlack,width,height,x,y,cw,ch);
 }
}

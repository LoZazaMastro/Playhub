using System.Buffers.Binary;
using Playhub.Shared;

internal static class SteamMirrorOwnedForegroundChecks
{
 static readonly ulong GameId=((ulong)2979426017<<32)|0x02000000UL;
 const long Now=1000;
 static SteamMirrorInputPresentation View()=>new(new string('a',64),1,GameId,2000,900,Now,
   101,102,77,88,100,200,(uint)Environment.ProcessId,900,3,true,true,true,true,false,SteamMirrorForegroundScope.OwnedOverlay,SteamMirrorOwnedForegroundPhase.AwaitingNativeClose);
 sealed class Windows:SteamMirrorInputCycle.IMirrorWindows
 {
  internal nint Fg=101,SourceCookie=77,DestinationCookie=88;internal bool EnabledValue=true,VisibleValue=true,Root=true;
  internal uint Pid=(uint)Environment.ProcessId;internal Action? OnCookie;
  public nint Foreground()=>Fg;
  public uint ThreadProcess(nint window,out uint pid){pid=window is 101 or 102?Pid:900;return window is 101 or 102?22u:44u;}
  public nint Ancestor(nint window,uint flags)=>Root?window:999;
  public nint Owner(nint window)=>0;
  public nint Cookie(nint window){OnCookie?.Invoke();return window==101?SourceCookie:DestinationCookie;}
  public bool Enabled(nint window)=>EnabledValue;
  public bool Visible(nint window)=>VisibleValue;
 }
 static SteamMirrorInputPresentation? Read(Windows windows,SteamMirrorInputPresentation? view=null,
   Func<SteamMirrorInputPresentation?>? provider=null,Func<bool>? gameForeground=null)
   =>SteamMirrorInputCycle.ReadCurrent(GameId,900,200,gameForeground??(()=>false),windows,provider??(()=>view??View()),()=>Now);
 static byte[] Wire(uint scope=3,uint state=3)
 {
  byte[] b=new byte[224];void U32(int p,uint v)=>BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(p),v);
  void U64(int p,ulong v)=>BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(p),v);
  U32(0,1);U32(4,224);U32(8,state);System.Text.Encoding.ASCII.GetBytes(new string('a',64)).CopyTo(b,16);
  U64(88,1);U64(96,GameId);U64(104,2000);U64(112,900);U64(120,101);U64(128,102);
  U64(136,77);U64(144,88);U64(152,100);U64(160,200);U32(168,(uint)Environment.ProcessId);U32(172,900);U32(176,scope);U32(180,scope==3?1u:0u);U64(184,1000);
  foreach(int p in new[]{192,196,200,204,208})U32(p,1);
  return b;
 }
 static SteamMirrorInputPresentation? Parse(byte[] b)=>SteamMirrorInputPresentation.Parse(b,(uint)Environment.ProcessId,100,900,200,GameId);
 sealed class Channel:SteamInputOrderedIssuer.IChannel
 {
  internal List<uint> Sent=new();SteamInputOrderedIssuer.Receipt? last;
  public SteamInputOrderedIssuer.Receipt? Start(uint layout,SteamInputOrderedIssuer.Receipt? expected,Func<bool> guard)
  {if(expected is {} e?last is not {} l||!ReferenceEquals(l.Identity,e.Identity):last is not null)return null;if(!guard())return null;last=new(new object(),layout);Sent.Add(layout);return last;}
  public SteamInputOrderedIssuer.Acknowledgement Read(SteamInputOrderedIssuer.Receipt value)=>last is {} l&&ReferenceEquals(l.Identity,value.Identity)?SteamInputOrderedIssuer.Acknowledgement.Acknowledged:SteamInputOrderedIssuer.Acknowledgement.Unknown;
 }
 static byte[] RecordWire(SteamMirrorInputPresentation m)
 {
  var b=Wire();void U32(int p,uint v)=>BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(p),v);void U64(int p,ulong v)=>BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(p),v);
  U32(8,m.State);U64(88,m.Generation);U64(104,(ulong)m.ExpiresAt);U64(112,(ulong)m.ObservedAt);U64(128,(ulong)m.DestinationWindow);U64(144,m.DestinationCookie);
  U32(176,(uint)m.ForegroundScope);U32(180,(uint)m.OwnedForegroundPhase);U64(184,(ulong)m.ReadAt);U32(192,m.PreparedCurrent?1u:0u);U32(196,m.ShownCurrent?1u:0u);
  U32(200,m.PreparedCurrent?1u:0u);U32(204,m.DestinationShown?1u:0u);U32(208,m.DwmAttached?1u:0u);U32(212,m.CleanupComplete?1u:0u);return b;
 }
 internal static void Run(Action<bool,string> check)
 {
  void Check(bool value,string why)=>check(value,why);
  Check(Read(new()) is not null,"owned source succeeds while truthful game foreground callback is false");
  Check(Read(new(){Fg=0}) is null,"FG0 is never admitted");
  Check(Read(new(){Fg=999}) is null,"foreign foreground is never admitted");
  Check(Read(new(){Fg=300}) is null,"scope3 cannot borrow a different game window");
  Check(Read(new(){SourceCookie=99}) is null,"source cookie mismatch");
  Check(Read(new(){DestinationCookie=99}) is null,"destination cookie mismatch");
  Check(Read(new(){Pid=999}) is null,"foreign process source and destination");
  Check(Read(new(){Root=false}) is null,"nonroot owned window");
  Check(Read(new(){EnabledValue=false}) is null,"disabled source");
  Check(Read(new(){VisibleValue=false}) is null,"hidden source");
  var drift=new Windows();drift.OnCookie=()=>drift.Fg=0;
  Check(Read(drift) is null,"postgetter foreground loss");
  var m=View();int calls=0;
  Check(Read(new(),provider:()=>++calls==1?m:m with{Generation=2}) is null,"lastcopy generation replacement");
  calls=0;Check(Read(new(),provider:()=>++calls==1?m:m with{ForegroundScope=SteamMirrorForegroundScope.Game}) is null,"lastcopy scope transition retries");
  Check(Read(new(),m with{ExpiresAt=Now}) is null,"expired owned lease");
  Check(Read(new(),m with{ObservedAt=Now+1}) is null,"future native observation");
  Check(Read(new(),m with{State=2,ShownCurrent=false,DestinationShown=false}) is null,"owned scope cannot start while only Prepared");
  Check(Read(new(){Fg=300},m with{ForegroundScope=SteamMirrorForegroundScope.Game},gameForeground:()=>true) is not null,"legacy true game foreground remains admitted");
  Check(Read(new(){Fg=300},m with{ForegroundScope=SteamMirrorForegroundScope.Game}) is null,"legacy game foreground callback remains necessary");
  var parsed=Parse(Wire());Check(parsed?.ForegroundScope==SteamMirrorForegroundScope.OwnedOverlay,"actual224 parses typed3");
  var dto=SteamMirrorStatus.ToDto(parsed!);Check(dto.ActualScope==3&&SteamMirrorWireProtocol.Shown(dto,Now),"actual DTO keeps3 instead of fabricating1");
  foreach(uint bad in new uint[]{0,2,4,0xffffffff})Check(Parse(Wire(bad)) is null,"unsupported live scope "+bad);
  foreach(uint bad in new uint[]{0,1,2,4})Check(Parse(Wire(3,bad)) is null,"scope3 nonShown state "+bad);
  foreach(int flag in new[]{192,196,200,204,208}){var wire=Wire();BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(flag),0);Check(Parse(wire) is null,"scope3 missing native flag "+flag);}
  Check(!SteamMirrorWireProtocol.Prepared(dto with{State="prepared"},Now),"DTO scope3 cannot be only prepared");
  Check(!SteamMirrorWireProtocol.Prepared(dto with{ActualScope=2},Now),"DTO legacy scope2 never accepted");
  Check(SteamMirrorWireProtocol.Same(dto,dto with{ActualScope=1}),"same immutable owner survives scoped phase transition without new generation");
  Check(SteamMirrorInputCycle.MatchAfterGetters(m,m with{ExpiresAt=2100},Now) is not null,"samegeneration current renewal joins");
  foreach(uint bad in new uint[]{3,0xffffffff}){var wire=Wire();BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(180),bad);Check(Parse(wire) is null,"invalid native foreground phase "+bad);}
  var noHandoff=Wire();BinaryPrimitives.WriteUInt32LittleEndian(noHandoff.AsSpan(180),0);Check(Parse(noHandoff) is null,"scope3 requires explicit handoff debt phase");
  var terminal=m with{State=0,PreparedCurrent=false,ShownCurrent=false,DestinationShown=false,DwmAttached=false,CleanupComplete=true,DestinationWindow=0,DestinationCookie=0,ForegroundScope=SteamMirrorForegroundScope.None};
  Check(Parse(RecordWire(terminal)) is {OwnedForegroundPhase:SteamMirrorOwnedForegroundPhase.AwaitingNativeClose},"closed visual cleanup preserves phase1");
  var terminalDto=SteamMirrorStatus.ToDto(terminal);Check(terminalDto.ActualScope==0&&terminalDto.OwnedForegroundPhase==1&&SteamMirrorWireProtocol.RevokeForegroundFlags(terminalDto).OwnedForegroundPhase==1,"DTO and foreground revocation cannot erase debt");
  var stop=new SteamMirrorOpeningStop(GameId,900,200);stop.Observe(m,Now);stop.Observe(terminal,Now);
  Check(stop.Blocking&&stop.NativeClosePending&&!stop.GiveUpWaiting(100000),"known closure debt stays neutral beyond expiry timeout");
  stop.Observe(terminal with{OwnedForegroundPhase=SteamMirrorOwnedForegroundPhase.None},Now);Check(stop.Blocking,"phase0 downgrade never discharges admitted debt");
  stop.Observe(terminal with{OwnedForegroundPhase=SteamMirrorOwnedForegroundPhase.NativeClosed,Generation=2},Now);Check(stop.Blocking,"other generation close cannot release debt");
  stop.Observe(terminal with{OwnedForegroundPhase=SteamMirrorOwnedForegroundPhase.NativeClosed,State=4,CleanupComplete=false},Now);Check(stop.Blocking,"native false without visual cleanup remains stopped");
  stop.Observe(terminal with{OwnedForegroundPhase=SteamMirrorOwnedForegroundPhase.NativeClosed},Now);Check(!stop.Blocking,"same owner native false and visual cleanup release STOP");
  var coldStop=new SteamMirrorOpeningStop(GameId,900,200);coldStop.Observe(terminal,Now);Check(coldStop.NativeClosePending,"fresh observer also retains native phase1 debt");
  var expiredShown=m with{PreparedCurrent=false,ShownCurrent=false,ForegroundScope=SteamMirrorForegroundScope.None,ExpiresAt=Now-1};
  var expiredStop=new SteamMirrorOpeningStop(GameId,900,200);expiredStop.Observe(expiredShown,Now);
  Check(expiredStop.Blocking&&expiredStop.NativeClosePending&&!expiredStop.GiveUpWaiting(100000),"revoked Shown flags at expiry retain phase1 without worker timeout");
  var liveStop=new SteamMirrorOpeningStop(GameId,900,200);liveStop.Observe(m,Now);Check(!liveStop.NativeClosePending,"fresh fully Shown handoff continues its active presentation");
  liveStop.Observe(expiredShown,Now);Check(liveStop.NativeClosePending,"active Shown transitions to cleanup-only debt when native lease flags revoke");

  long clock=1000;var native=terminal;var channel=new Channel();var session=new SteamRawInputRelayTrial.SessionBinding(GameId,Environment.ProcessId,100,900,200,new(1,0x1302,123),new object(),300,44,900);
  var outcome=new SteamRawInputRelayTrial.ReaderOutcome(true,false,false,RelayExit.OverlayOpened,true,true,false,1234,3001,1,true,true);
  var issuer=new SteamInputOrderedIssuer(session,SteamInputOrderedIssuer.InputRoute.OwnedSessionPad,()=>true,()=>true,()=>true,()=>clock,_=>false,()=>true,channel);
  Check(issuer.SendGame((uint)(GameId>>32),1,()=>true),"debt fixture starts after real owned game command");
  using(var bridge=SteamMirrorInputPresentationBridge.Register(900,200,GameId,100,()=>RecordWire(native with{ReadAt=clock,ObservedAt=clock}),()=>{},_=>false))
  {
   void Wait(int ms){clock+=ms;if(clock>=4000)native=terminal with{OwnedForegroundPhase=SteamMirrorOwnedForegroundPhase.NativeClosed};}
   Check(SteamMirrorInputCycle.ResumePresentation(1,session,outcome,issuer,()=>clock<7000,()=>null,()=>clock,Wait,CancellationToken.None,opening:coldStop),"actual cycle survives expired debt until late genuine native false");
  }
  Check(clock>=4000&&channel.Sent.SequenceEqual(new uint[]{(uint)(GameId>>32),0})&&issuer.AutomaticSettled&&!coldStop.Blocking,"debt wait sends one owned0, no769 or premature resume");

 }
}

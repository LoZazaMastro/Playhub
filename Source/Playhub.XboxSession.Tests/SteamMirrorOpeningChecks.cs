using System.Buffers.Binary;
internal static class SteamMirrorOpeningChecks
{
 static int checks;static Action<bool,string> checkSink=null!;static void Check(bool value,string text){checks++;checkSink(value,"mirror opening: "+text);}
 static ulong GameId=0x9234567802000000;
 static SteamMirrorInputPresentation M(long now,uint state=1)=>new(new string('a',64),1,GameId,5000,now,now,30,state==0?0:40,300,400,111,2003,(uint)Environment.ProcessId,20,state,state is 2 or 3,state==3,state==3,state is 2 or 3,state==0);
 static byte[] Wire(SteamMirrorInputPresentation m){var w=new byte[224];void U(int i,ulong x)=>BinaryPrimitives.WriteUInt64LittleEndian(w.AsSpan(i),x);void D(int i,uint x)=>BinaryPrimitives.WriteUInt32LittleEndian(w.AsSpan(i),x);D(0,1);D(4,224);D(8,m.State);System.Text.Encoding.ASCII.GetBytes(m.SessionId).CopyTo(w,16);U(88,m.Generation);U(96,m.GameId);U(104,(ulong)m.ExpiresAt);U(112,(ulong)m.ObservedAt);U(120,(ulong)m.SourceWindow);U(128,(ulong)m.DestinationWindow);U(136,m.SourceCookie);U(144,m.DestinationCookie);U(152,(ulong)m.OwnerBirth);U(160,(ulong)m.GameBirth);D(168,m.OwnerPid);D(172,m.GamePid);D(176,m.PreparedCurrent?1U:0);U(184,(ulong)m.ReadAt);D(192,m.PreparedCurrent?1U:0);D(196,m.ShownCurrent?1U:0);D(200,m.PreparedCurrent?1U:0);D(204,m.DestinationShown?1U:0);D(208,m.DwmAttached?1U:0);D(212,m.CleanupComplete?1U:0);return w;}
 sealed class Channel(Func<long> clock):SteamInputOrderedIssuer.IChannel
 {
  internal SteamInputOrderedIssuer.Receipt? last;internal List<(uint Layout,long At)> Sent=new();
  public SteamInputOrderedIssuer.Receipt? Start(uint layout,SteamInputOrderedIssuer.Receipt? expected,Func<bool> guard){if(expected is {} e ? last is not {} l||!ReferenceEquals(l.Identity,e.Identity):last is not null)return null;last=null;if(!guard())return null;var r=new SteamInputOrderedIssuer.Receipt(new object(),layout);last=r;Sent.Add((layout,clock()));return r;}
  public SteamInputOrderedIssuer.Acknowledgement Read(SteamInputOrderedIssuer.Receipt r)=>last is {} l&&ReferenceEquals(l.Identity,r.Identity)?SteamInputOrderedIssuer.Acknowledgement.Acknowledged:SteamInputOrderedIssuer.Acknowledgement.Unknown;
 }
 internal static void Run(Action<bool,string> assertion)
 {
  checkSink=assertion; checks=0;
  ScopePauseChecks();
  var stop=new SteamMirrorOpeningStop(GameId,20,2003);var first=M(1000);stop.Observe(first,1000);Check(stop.Blocking,"Preparing stops before Prepared");Check(!first.Prepared(1000),"Preparing never grants Prepared/769");stop.Observe(null,1500);Check(stop.Blocking,"unknown read cannot reopen game");stop.Observe(M(1600,0) with{Generation=2},1600);Check(stop.Blocking,"foreign generation cannot release");stop.Observe(M(1600,0) with{GameBirth=2004},1600);Check(stop.Blocking,"PID birth reuse cannot release");stop.Observe(M(1700,4),1700);Check(stop.Blocking,"cleanup pending remains stopped");stop.Observe(M(1750,0) with{DestinationCookie=401},1750);Check(stop.Blocking,"foreign retained cookie cannot release");stop.Observe(M(1800,0),1800);Check(!stop.Blocking,"same native Closed cleanup releases stop");
  stop.Observe(M(2000),2000);Check(stop.Expired(5000),"initial deadline observed");int queues=0;stop.RequestExpiredClose(5000,()=>Wire(M(5000)),_=>{queues++;return true;});stop.RequestExpiredClose(5010,()=>Wire(M(5010)),_=>{queues++;return true;});Check(queues==1,"one exact expired close request");Check(stop.GiveUpWaiting(6501)&&stop.CancellationUnconfirmed&&stop.Blocking,"unknown close bounded and fail closed");
  long now=1000;var native=M(now);var active=new SteamMirrorOpeningStop(GameId,20,2003);active.Observe(native,now);var channel=new Channel(()=>now);var session=new SteamRawInputRelayTrial.SessionBinding(GameId,Environment.ProcessId,111,20,2003,new(1,0x1302,123),new object(),100,12,20);var outcome=new SteamRawInputRelayTrial.ReaderOutcome(true,false,false,RelayExit.OverlayOpened,true,true,false,1234,3001,1,true,true);
  var issuer=new SteamInputOrderedIssuer(session,SteamInputOrderedIssuer.InputRoute.OwnedSessionPad,()=>true,()=>true,()=>true,()=>now,_=>now>=2400&&now<2600,()=>true,channel);
  Check(issuer.SendGame((uint)(GameId>>32),1,()=>true),"production prior game command exists before retired reader");
  using(var bridge=SteamMirrorInputPresentationBridge.Register(20,2003,GameId,111,()=>Wire(native),()=>{},_=>false))
  {
   SteamMirrorInputPresentation? Prepared()=>native.Prepared(now)?native:null;
   void Wait(int ms){now+=ms;native=M(now,now<2300?1U:now<2400?2U:now<2600?3U:0U);}
   Check(SteamMirrorInputCycle.ResumePresentation(1,session,outcome,issuer,()=>true,Prepared,()=>now,Wait,CancellationToken.None,opening:active),"real cycle waits through Preparing and restores ordered0");
  }
  Check(channel.Sent.Count==3&&channel.Sent[1].Layout==769&&channel.Sent[1].At>=2400,"zero769 from Preparing or merely Prepared");Check(channel.Sent[2].Layout==0&&channel.Sent[2].At>=2600,"own0 only after same Closed");Check(issuer.AutomaticSettled&&!active.Blocking,"own0 ACK before fresh game reader");
  now=1000;native=M(now);active=new(GameId,20,2003);active.Observe(native,now);channel=new(()=>now);
  issuer=new(session,SteamInputOrderedIssuer.InputRoute.OwnedSessionPad,()=>true,()=>true,()=>true,()=>now,_=>false,()=>true,channel);
  Check(issuer.SendGame((uint)(GameId>>32),1,()=>true),"failed opening begins after owned game command");
  using(var bridge=SteamMirrorInputPresentationBridge.Register(20,2003,GameId,111,()=>Wire(native),()=>{},_=>false))
  {void Wait(int ms){now+=ms;native=M(now,now<1800?1U:0U);}Check(SteamMirrorInputCycle.ResumePresentation(1,session,outcome,issuer,()=>true,()=>null,()=>now,Wait,CancellationToken.None,opening:active),"preparation failure closes without dead pending loop");}
  Check(channel.Sent.Count==2&&channel.Sent[1].Layout==0&&channel.Sent[1].At>=1800&&!channel.Sent.Any(x=>x.Layout==769),"failed opening never requests769; own0 after Closed");
  var releases=new FreshReleasedReaderFrames();Check(!releases.Observe(1,new(0x1000,0,0,0,0,0,0),true),"A held after Guide release cannot resume");Check(!releases.Observe(2,default,true),"new reader first released frame insufficient");Check(releases.Observe(3,default,true),"new reader second fresh released frame opens report gate");
  bool outputThrew=false;int commandCleanup=0;
  try{SteamControllerLayoutTrial.CleanupMirror(()=>throw new IOException("fake output cleanup"),()=>commandCleanup++);}catch(IOException){outputThrew=true;}
  Check(outputThrew&&commandCleanup==1,"owned command cleanup runs once even if output dispose throws");
  int outputCleanup=0;commandCleanup=0;SteamControllerLayoutTrial.CleanupMirror(()=>outputCleanup++,()=>commandCleanup++);
  Check(outputCleanup==1&&commandCleanup==1,"normal output then command cleanup runs once");
 }
 static void ScopePauseChecks()
 {
  var stop=new SteamMirrorOpeningStop(GameId,20,2003);
  var pause=new SteamControllerLayoutTrial.OpeningPause(GameId,20,2003);
  RelayExit? requested=null;bool captured=false,lost=false;int reads=0;
  RelayExit? Read()=>++reads==1?null:lost?pause.Read(stop,()=>true):null;
  bool Owns(){stop.Observe(M(1000),1000);lost=true;pause.Capture(stop,M(1000),()=>true);return false;}
  var reason=SteamRawInputRelayTrial.ObserveParentScope(Owns,Read,()=>true,ref requested,ref captured,readCapturedPause:Read);
  Check(reason==RelayExit.OverlayOpened&&requested==reason&&captured&&reads==2,"null then exact Preparing veto is one captured parent pause");
  bool allowed=SteamRawInputRelayTrial.ParentPauseAllowed(requested,reason!.Value,true,true,true,null,captured);
  var outcome=new SteamRawInputRelayTrial.ReaderOutcome(true,false,false,reason,true,true,false,123,3001,1,true,allowed);
  Check(allowed&&outcome.CanResume,"captured opening preserves SAME neutral PDO eligibility after later EOF");
  requested=null;captured=false;int ownsCalls=0;
  reason=SteamRawInputRelayTrial.ObserveParentScope(()=>{ownsCalls++;return true;},()=>RelayExit.OverlayOpened,()=>true,ref requested,ref captured);
  Check(reason==RelayExit.OverlayOpened&&captured&&ownsCalls==0,"positive first parent pause retains existing early stop order");
  requested=null;captured=false;int finalReads=0;
  reason=SteamRawInputRelayTrial.ObserveParentScope(()=>true,()=>{finalReads++;return null;},()=>true,ref requested,ref captured,readFirst:false);
  Check(reason is null&&finalReads==0,"normal final ownership needs no extra pause read");
  requested=null;captured=false;
  reason=SteamRawInputRelayTrial.ObserveParentScope(()=>false,()=>pause.Read(stop,()=>true),()=>true,ref requested,ref captured,readFirst:false,readCapturedPause:()=>pause.Read(stop,()=>true));
  Check(reason==RelayExit.OverlayOpened&&captured,"late observation ownership veto uses same exact captured receipt");
  foreach(var which in new[]{"device","retained-owner","foreign-window"})
  {
   var later=new SteamMirrorOpeningStop(GameId,20,2003);var noVeto=new SteamControllerLayoutTrial.OpeningPause(GameId,20,2003);
   bool failed=false;RelayExit? q=null;bool c=false;
   reason=SteamRawInputRelayTrial.ObserveParentScope(()=>{failed=true;return false;},()=>
   {if(failed)later.Observe(M(1000),1000);return failed?noVeto.Read(later,()=>true):null;},()=>true,ref q,ref c,readCapturedPause:()=>noVeto.Read(later,()=>true));
   Check(reason==RelayExit.FocusLost&&!c&&!SteamRawInputRelayTrial.ParentPauseAllowed(q,reason.Value,false,true,true,RelayExit.OverlayOpened,c),which+" failure cannot adopt a Preparing that appeared later");
  }
  foreach(var wrong in new[]{M(1000) with{Generation=2},M(1000) with{GameId=GameId+0x100000000},M(1000) with{GamePid=21},M(1000) with{GameBirth=2004},M(1000) with{OwnerBirth=112},M(1000) with{SourceCookie=301},M(1000) with{SessionId=new string('b',64)}})
  {
   var exact=new SteamMirrorOpeningStop(GameId,20,2003);exact.Observe(M(1000),1000);var denied=new SteamControllerLayoutTrial.OpeningPause(GameId,20,2003);
   Check(!denied.Capture(exact,wrong,()=>true)&&denied.Read(exact,()=>true) is null,"wrong generation/game/owner/source/session cannot create pause provenance");
  }
  var physical=new SteamControllerLayoutTrial.OpeningPause(GameId,20,2003);
  Check(!physical.Capture(stop,M(1000),()=>false)&&!physical.Capture(stop,M(1000),()=>true)&&physical.Read(stop,()=>true) is null,"physical/retained failure during capture is sticky terminal");
  var lateStop=new SteamMirrorOpeningStop(GameId,20,2003);lateStop.Observe(M(1000),1000);var late=new SteamControllerLayoutTrial.OpeningPause(GameId,20,2003);
  Check(!late.Capture(lateStop,M(1000),()=>{lateStop.Observe(M(1001,0),1001);return true;}),"last capture getter cannot retain already closed admission");
  var readStop=new SteamMirrorOpeningStop(GameId,20,2003);readStop.Observe(M(1000),1000);var readLate=new SteamControllerLayoutTrial.OpeningPause(GameId,20,2003);
  Check(readLate.Capture(readStop,M(1000),()=>true)&&readLate.Read(readStop,()=>{readStop.Observe(M(1001,0),1001);return true;}) is null,"last read getter cannot retain closed/replaced admission");
  var lostPhysical=new SteamControllerLayoutTrial.OpeningPause(GameId,20,2003);lostPhysical.Capture(stop,M(1000),()=>true);
  Check(lostPhysical.Read(stop,()=>false) is null&&lostPhysical.Read(stop,()=>true) is null,"source/owner loss after captured veto cannot recover by a later positive query");
  var reentrant=new SteamControllerLayoutTrial.OpeningPause(GameId,20,2003);reentrant.Capture(stop,M(1000),()=>true);
  Check(reentrant.Read(stop,()=>{reentrant.Read(stop,()=>false);return true;}) is null,"reentrant current getter cannot undo source revocation");
  var nested=new SteamControllerLayoutTrial.OpeningPause(GameId,20,2003);
  Check(!nested.Capture(stop,M(1000),()=>{nested.Read(stop,()=>false);return nested.Capture(stop,M(1000),()=>true);})&&nested.Read(stop,()=>true)==RelayExit.OverlayOpened,"outer capture cannot replace receipt admitted by nested getter");
  foreach(var terminal in new[]{"EOF","child-exit","session-loss"})
  {
   RelayExit? q=null;bool c=false,live=true;int calls=0;
   reason=SteamRawInputRelayTrial.ObserveParentScope(()=>{live=false;return false;},()=>{calls++;return calls==1?null:RelayExit.OverlayOpened;},()=>terminal=="EOF"?false:live,ref q,ref c);
   Check(reason==RelayExit.FocusLost&&!c&&!SteamRawInputRelayTrial.ParentPauseAllowed(q,reason.Value,true,true,terminal!="session-loss",RelayExit.OverlayOpened,c),terminal+" before capture stays terminal");
  }
  int actualReads=0;requested=null;captured=false;
  RelayExit? ActualPause()=>++actualReads==1?null:RelayExit.FocusLost;
  reason=SteamRawInputRelayTrial.ObserveParentScope(()=>false,ActualPause,()=>true,ref requested,ref captured,readCapturedPause:()=>null);
  Check(reason==RelayExit.FocusLost&&!captured&&actualReads==1,"veto reconciliation never rereads generic actual pause after ownership loss");
  var terminalProof=new SteamRawInputRelayTrial.ReaderTerminal(true,false,false,false);
  bool nativeFocusPause=terminalProof.KnownFocusPause(true,true,ActualPause()==RelayExit.FocusLost);
  var focusOutcome=new SteamRawInputRelayTrial.ReaderOutcome(true,false,false,reason,true,true,nativeFocusPause,123,3001,1,true,false);
  Check(nativeFocusPause&&focusOutcome.CanResume,"existing authenticated native focus terminal still pauses after Alt-Tab");
  Check(!default(SteamRawInputRelayTrial.ReaderTerminal).KnownFocusPause(true,true,true),"actual later FocusLost cannot turn unknown EOF into a native terminal");
  Check(!new SteamRawInputRelayTrial.ReaderTerminal(true,false,true,false).KnownFocusPause(true,true,true),"actual later FocusLost cannot hide a native device removal");
  bool current=true;requested=null;captured=false;
  reason=SteamRawInputRelayTrial.ObserveParentScope(()=>false,()=>{current=false;return RelayExit.OverlayOpened;},()=>current,ref requested,ref captured);
  Check(reason==RelayExit.FocusLost&&!captured,"EOF/session loss inside pause getter cannot be captured");
  requested=RelayExit.OverlayOpened;captured=true;
  reason=SteamRawInputRelayTrial.ObserveParentScope(()=>false,()=>null,()=>false,ref requested,ref captured);
  Check(reason==RelayExit.OverlayOpened&&SteamRawInputRelayTrial.ParentPauseAllowed(requested,reason.Value,true,true,true,null,captured),"previous live capture remains known after terminal drain");
 }

}

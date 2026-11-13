using System.Buffers.Binary;
internal static class InputFenceReleaseChecks
{
 sealed class Sink:IFenceSink {internal byte[] Last=[];public void Write(byte[] value){Last=value;}}
 internal static void Run(Action<bool,string> assert)
 {
  int check=0;void Check(bool b)=>assert(b,"release producer "+ ++check);
  var id=new FenceProducer.Identity(100,10,200,20,1,900,new string('a',64),0x8000000002000000);
  long now=1000;bool alive=true,source=true;int sourceCalls=0;int denySourceAt=-1;
  SteamMirrorInputPresentation M()=>new(new string('b',64),1,id.GameId,now+2000,900,now,(nint)50,(nint)60,7,8,20,10,200,100,3,true,true,true,true,false);
  SteamMirrorInputPresentation m=M();var sink=new Sink();
  bool Source(SteamMirrorInputPresentation _) {++sourceCalls;return source&&sourceCalls!=denySourceAt;}
  FenceProducer New()=>new(id,sink,()=>alive,()=>now,()=>m,sourceCurrent:Source);
  void Close()=>m=m with{State=0,PreparedCurrent=false,ShownCurrent=false,DestinationShown=false,DwmAttached=false,CleanupComplete=true,ForegroundScope=SteamMirrorForegroundScope.None,ReadAt=now};
  uint Phase()=>BinaryPrimitives.ReadUInt32LittleEndian(sink.Last.AsSpan(108));
  uint Kind()=>BinaryPrimitives.ReadUInt16LittleEndian(sink.Last.AsSpan(6));
  var p=New();Check(p.Update()==1);Close();Check(p.Update()==2&&Kind()==1);
  now=1370;m=m with{ReadAt=now};Check(p.Update()==5&&Kind()==1);
  Check(BinaryPrimitives.ReadUInt64LittleEndian(sink.Last.AsSpan(80))==(ulong)now+250);
  now=3000;m=m with{ReadAt=now};Check(p.Update()==5);now=6000;m=m with{ReadAt=now};Check(p.Update()==4&&Kind()==0);
  now=6010;m=m with{ReadAt=now};Check(p.Update()==4);m=M() with{Generation=2};Check(p.Update()==1);
  p.Dispose();Check(Phase()==0&&Kind()==0);
  foreach(string failure in new[]{"source","last-source","current","stale","foreign-generation","foreign-cookie","foreign-session","unknown"})
  {
   now=1000;alive=source=true;sourceCalls=0;denySourceAt=-1;m=M();var f=New();Check(f.Update()==1);Close();Check(f.Update()==2);
   now=1370;m=m with{ReadAt=now};var saved=m;
   switch(failure){case "source":source=false;break;case "last-source":denySourceAt=sourceCalls+2;break;case "current":alive=false;break;
    case "stale":m=m with{ReadAt=now-251};break;case "foreign-generation":m=m with{Generation=2};break;
    case "foreign-cookie":m=m with{SourceCookie=9};break;case "foreign-session":m=m with{SessionId=new string('c',64)};break;case "unknown":m=m with{State=4,CleanupComplete=false};break;}
   Check(f.Update()!=5);Check(Kind()==0);alive=source=true;denySourceAt=-1;m=saved with{ReadAt=now};
   Check(f.Update()!=5); // a negative receipt cannot revive the retired cleanup tail
  }
  now=1000;alive=source=true;denySourceAt=-1;m=M();var slowSink=new Sink();int reads=0;
  var slow=new FenceProducer(id,slowSink,()=>true,()=>now,()=>{reads++;return m;},sourceCurrent:_=>{now+=251;return true;});
  Check(slow.Update()==1);Close();Check(slow.Update()==0); // source getter consumes the complete freshness budget
  now=1000;alive=true;m=M();int sourceReads=0;
  var drift=new FenceProducer(id,sink,()=>alive,()=>now,()=>m,sourceCurrent:_=>{if(++sourceReads==2)alive=false;return true;});
  Check(drift.Update()==1);Close();Check(drift.Update()==0&&Kind()==0);alive=true;
  now=1000;alive=true;bool nativeAuthority=true;
  using(var binding=SteamNativeOverlayInputPresentationBridge.Configure(300,30,id.GameId,100,10,()=>now,_=>nativeAuthority))
  {
   string token="11111111222233334444555555555555";
   var command=new SteamNativeOverlayInputPresentationBridge.Command("prepare",token,1,new string('c',64),id.GameId,id.GameId,300,30,100,10,400,40,500,50,6,123,1,now+1500);
   Check(SteamNativeOverlayInputPresentationBridge.Publish(command));command=command with{Action="active",Serial=2};Check(SteamNativeOverlayInputPresentationBridge.Publish(command));
   var native=new FenceProducer(id,sink,()=>alive,()=>now,()=>null,closedNativeCurrent:SteamNativeOverlayInputPresentationBridge.FenceClosedSourceCurrent);
   Check(native.Update()==1&&Kind()==2);var actualActive=SteamNativeOverlayInputPresentationBridge.Read();
   command=command with{Action="closed",Serial=3};Check(SteamNativeOverlayInputPresentationBridge.Publish(command));
   Check(native.Update()==2);now=1370;Check(native.Update()==5);now=3000;Check(native.Update()==5); // active lease expiry cannot grant UI, only finite cleanup
   Check(!SteamNativeOverlayInputPresentationBridge.AuthorityCurrent(command));
   nativeAuthority=false;Check(native.Update()==0);nativeAuthority=true;Check(native.Update()!=5&&Kind()==0);
   var actualClosed=SteamNativeOverlayInputPresentationBridge.Read();var sample=actualActive;long freshAt=1370;int nativeReads=0;bool revokeAtLast=false,clearSeenAtLast=false;
   SteamNativeOverlayInputPresentationBridge.Snapshot NativeRead(){++nativeReads;if(revokeAtLast&&nativeReads==3)alive=false;
    return clearSeenAtLast&&nativeReads==3?sample with{NativeActiveSeen=false}:sample;}
   var late=new FenceProducer(id,sink,()=>alive,()=>freshAt,()=>null,NativeRead,_=>true,closedNativeCurrent:_=>true);
   Check(late.Update()==1);sample=actualClosed;nativeReads=0;revokeAtLast=true;Check(late.Update()==0&&Kind()==0&&nativeReads==3);
   alive=true;revokeAtLast=false;sample=actualActive;
   var lastSeen=new FenceProducer(id,sink,()=>alive,()=>freshAt,()=>null,NativeRead,_=>true,closedNativeCurrent:_=>true);
   Check(lastSeen.Update()==1);sample=actualClosed;nativeReads=0;clearSeenAtLast=true;Check(lastSeen.Update()==0&&Kind()==0&&nativeReads==3);
   clearSeenAtLast=false;sample=actualActive;nativeReads=0;bool disposeAtLast=false;
   SteamNativeOverlayInputPresentationBridge.Snapshot DisposeRead(){++nativeReads;var copy=sample;if(disposeAtLast&&nativeReads==3)binding.Dispose();return copy;}
   var scoped=new FenceProducer(id,sink,()=>true,()=>freshAt,()=>null,DisposeRead,_=>true,closedNativeCurrent:SteamNativeOverlayInputPresentationBridge.FenceClosedSourceCurrent);
   Check(scoped.Update()==1);sample=actualClosed;nativeReads=0;disposeAtLast=true;Check(scoped.Update()==0&&Kind()==0&&nativeReads==3);
  }
 }
}

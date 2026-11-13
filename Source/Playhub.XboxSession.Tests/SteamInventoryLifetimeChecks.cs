using System.Text;
internal static class SteamInventoryLifetimeChecks
{
 internal static void Run(Action<bool,string> check)
 {
  var source=new TritonSource(0,0x1302,0x123);
  string text="[slot 0]\nVID=28de\nPID=1302\nhandle=123\ntype=steam\n";
  byte[] valid=Encoding.UTF8.GetBytes(text);int opens=0,waits=0;
  var snapshot=SteamInputEvidence.Read(()=>{opens++;return new MemoryStream(valid);},_=>waits++);
  check(snapshot.Source==source&&opens==2&&waits==0,"two complete identical snapshots prove a fresh source");
  opens=waits=0;
  snapshot=SteamInputEvidence.Read(()=>{if(opens++==0)throw new IOException("sharing");return new MemoryStream(valid);},_=>waits++);
  check(snapshot.Source==source&&opens==3&&waits==1,"sharing failure retries bounded then uses fresh bytes");
  opens=waits=0;
  snapshot=SteamInputEvidence.Read(()=>{opens++;throw new IOException("unavailable");},_=>waits++);
  check(snapshot.Source is null&&opens==3&&waits==2,"persistent IO never reuses an earlier positive source");
  opens=0;
  snapshot=SteamInputEvidence.Read(()=>new MemoryStream(opens++%2==0?valid:Encoding.UTF8.GetBytes(text.Replace("handle=123","handle=124"))),_=>{});
  check(snapshot.Source is null&&opens==6,"changing byte snapshots are unavailable after bounded retries");
  snapshot=SteamInputEvidence.Read(()=>new MemoryStream([]),_=>{});check(snapshot.Source is null,"empty writer snapshot is not an absent connection grant");
  var lease=new SteamControllerSourceLease(source);
  check(!lease.Current(default)&&!lease.Revoked,"unknown inventory suspends without permanently revoking retained identity");
  check(lease.Current(new(source,false)),"fresh exact source can reattest after unknown");
  check(!lease.Current(new(source with{Handle=999},false))&&lease.Revoked,"positive different handle revokes");
  var shadow=new SteamControllerSourceLease(source);check(!shadow.Current(new(source,true))&&shadow.Revoked,"positive shadow still revokes");
  var bad=new SteamControllerSourceLease(default);check(!bad.Current(default)&&bad.Revoked,"invalid expected identity cannot gain retention from unknown");
  var watch=new SteamRawInputRelayTrial.InventoryWatch(source);
  check(watch.Observe(new(source,false),0,true)==SteamRawInputRelayTrial.InventoryStatus.Current,"watch exact current");
  check(watch.Observe(default,10,true)==SteamRawInputRelayTrial.InventoryStatus.Suspended,"unknown is suspended, not connected");
  check(watch.Observe(default,10000,true)==SteamRawInputRelayTrial.InventoryStatus.Suspended,"attested reader retains only neutral beyond missing metadata grace");
  check(watch.Observe(new(source with{Handle=999},false),10001,true)==SteamRawInputRelayTrial.InventoryStatus.Revoked,"different source cannot use suspension grace");
  check(watch.Observe(new(source,true),10002,true)==SteamRawInputRelayTrial.InventoryStatus.Revoked,"shadow cannot use suspension grace");
  var initial=new SteamRawInputRelayTrial.InventoryWatch(source);_ = initial.Observe(default,0);
  check(initial.Observe(default,180)==SteamRawInputRelayTrial.InventoryStatus.Revoked,"unattested initial source gets no indefinite retention");
  var released=new FreshReleasedReaderFrames();var held=new XboxReport(0x1000,0,0,0,0,0,0);
  check(!released.Observe(1,default,true),"first release alone is not ready");
  check(SteamRawInputRelayTrial.ReleasedReport(new(1,1,default),1,released.Ready) is null,"first release cannot arm downstream relay");
  check(!released.Observe(2,held,true)&&SteamRawInputRelayTrial.ReleasedReport(new(2,2,held),2,released.Ready) is null,"held before second release stays neutral without faulting");
  check(!released.Observe(3,default,true),"release after held begins a new pair");
  check(released.Observe(4,default,true)&&SteamRawInputRelayTrial.ReleasedReport(new(4,4,default),4,released.Ready)==default(XboxReport),"two fresh releases restore forwarding eligibility");
  check(SteamRawInputRelayTrial.ReleasedReport(new(5,5,held),5,released.Ready)==held,"fresh held report admitted only after the new released pair");
  var afterGap=new FreshReleasedReaderFrames();
  check(!afterGap.Observe(10,default,SteamRawInputRelayTrial.FreshAfterInventory(390,500,400))
    &&!afterGap.Observe(11,default,SteamRawInputRelayTrial.FreshAfterInventory(395,500,400)),"queued neutral frames from unknown period cannot rearm after exact source returns");
  check(!afterGap.Observe(12,held,SteamRawInputRelayTrial.FreshAfterInventory(500,500,400))
    &&SteamRawInputRelayTrial.ReleasedReport(new(12,500,held),500,afterGap.Ready) is null,"held after queued stale releases still cannot forward");
  check(!afterGap.Observe(13,default,SteamRawInputRelayTrial.FreshAfterInventory(501,501,400))
    &&afterGap.Observe(14,default,SteamRawInputRelayTrial.FreshAfterInventory(502,502,400)),"two releases strictly after reattestation permit recovery");
  check(SteamRawInputRelayTrial.ParentPauseAllowed(RelayExit.FocusLost,RelayExit.FocusLost,true,true,true,null,true),"captured live focus pause survives focus return and later EOF during drain");
  check(!SteamRawInputRelayTrial.ParentPauseAllowed(null,RelayExit.FocusLost,true,true,true,null,true),"EOF before any captured scope loss stays STOP");
  check(!SteamRawInputRelayTrial.ParentPauseAllowed(RelayExit.FocusLost,RelayExit.Faulted,true,true,true,null,true),"a fault cannot be relabeled as a focus pause");
  check(!SteamRawInputRelayTrial.ParentPauseAllowed(RelayExit.FocusLost,RelayExit.FocusLost,true,true,false,null,true),"retained owner loss prevents pause recovery");
  check(!SteamRawInputRelayTrial.ParentPauseAllowed(RelayExit.FocusLost,RelayExit.FocusLost,true,true,true,null),"unattested after-the-fact scope inference remains refused");
 }
}

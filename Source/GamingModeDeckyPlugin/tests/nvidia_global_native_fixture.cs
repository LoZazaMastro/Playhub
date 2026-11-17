using System.Runtime.InteropServices;
static class Program {
 static int checks;static void Check(bool value,string label){++checks;if(!value)throw new Exception(label);}
 static NvidiaSnapshot Initial(){return new NvidiaSnapshot {DriverVersion=62000,Branch="r620",ProfileName="Base Profile",GpuSupport=1,ProfilePredefined=1,ProfileSettings=3,
  Settings=new[]{new NvidiaSetting{Id="frame_limit",Raw=0,DefaultRaw=0,Location=3,CurrentPredefined=true,DefaultValid=true,Supported=true},
   new NvidiaSetting{Id="vsync",Raw=0x60925292,DefaultRaw=0x60925292,Location=3,CurrentPredefined=true,DefaultValid=true,Supported=true},
   new NvidiaSetting{Id="power",Raw=5,DefaultRaw=5,Location=3,CurrentPredefined=true,DefaultValid=true,Supported=true}}};}
 static NvidiaSetting Copy(NvidiaSetting s){return new NvidiaSetting{Id=s.Id,Raw=s.Raw,DefaultRaw=s.DefaultRaw,Location=s.Location,CurrentPredefined=s.CurrentPredefined,DefaultValid=s.DefaultValid,Supported=s.Supported};}
 static NvidiaSnapshot Copy(NvidiaSnapshot s,string only=null){return new NvidiaSnapshot{DriverVersion=s.DriverVersion,Branch=s.Branch,ProfileName=s.ProfileName,GpuSupport=s.GpuSupport,ProfilePredefined=s.ProfilePredefined,ProfileSettings=s.ProfileSettings,Settings=s.Settings.Where(x=>only==null||only==x.Id).Select(Copy).ToArray()};}
 sealed class Fake : INvidiaGlobalDrs {
  internal NvidiaSnapshot state=Initial();internal List<string> reads=new(),writes=new();internal Action<Fake,string> beforeRead=null,beforeWrite,afterWrite;
  internal int throwSave;internal bool throwReads;internal int failReadNumber;
  public NvidiaSnapshot Read(string feature){reads.Add(feature??"all");beforeRead?.Invoke(this,feature);if(throwReads||reads.Count==failReadNumber)throw new IOException("fake");return Copy(state,feature);}
  public bool WriteOne(NvidiaSnapshot expected,string feature,uint? wanted,NvidiaWriteReceipt receipt){beforeWrite?.Invoke(this,feature);if(!expected.SameIdentity(state)||!expected.Find(feature).Same(state.Find(feature)))return false;
   writes.Add(feature+":"+(wanted.HasValue?wanted.Value.ToString():"delete"));var s=state.Find(feature);
   if(wanted.HasValue){s.Raw=wanted.Value;s.Location=0;s.CurrentPredefined=false;}else{s.Raw=s.DefaultRaw;s.Location=3;s.CurrentPredefined=true;}
   receipt.SaveAttempted=true;afterWrite?.Invoke(this,feature);if(throwSave!=0){var code=throwSave;throwSave=0;throw new NvidiaApiError(code);}return true;
  }
 }
 static void Main(){
  var initial=Initial();Check(initial.Token().Length==64&&initial.Token()==Copy(initial).Token(),"canonical token");
  var changed=Copy(initial);changed.ProfileName="Another global";Check(initial.Token()!=changed.Token()&&!initial.SameIdentity(changed),"profile bound");
  changed=Copy(initial);changed.DriverVersion++;Check(initial.Token()!=changed.Token()&&!initial.SameIdentity(changed),"driver bound");
  changed=Copy(initial);changed.Branch="r621";Check(!initial.SameIdentity(changed),"branch bound");
  changed=Copy(initial);changed.Find("power").Location=2;Check(initial.Token()!=changed.Token(),"location bound");
  changed=Copy(initial);changed.Find("power").CurrentPredefined=false;Check(initial.Token()!=changed.Token(),"override bound");
  changed=Copy(initial);changed.Find("power").DefaultRaw=2;Check(initial.Token()!=changed.Token(),"default bound");
  foreach(uint x in new uint[]{0,20,60,120,1000})Check(NvidiaGlobalTransaction.Allowed("frame_limit",x),"fps valid");
  foreach(uint x in new uint[]{1,19,1001,1023,uint.MaxValue})Check(!NvidiaGlobalTransaction.Allowed("frame_limit",x),"fps bounds");
  foreach(uint x in new uint[]{0x60925292,0x08416747,0x47814940})Check(NvidiaGlobalTransaction.Allowed("vsync",x),"vsync public values");
  foreach(uint x in new uint[]{0,1,2,0x18888888})Check(!NvidiaGlobalTransaction.Allowed("vsync",x),"vsync not boolean/private");
  Check(NvidiaGlobalTransaction.Allowed("power",1)&&!NvidiaGlobalTransaction.Allowed("power",5)&&!NvidiaGlobalTransaction.Allowed("clock",1),"power minimal");
  var f=new Fake();var read=NvidiaGlobalTransaction.Run(f,null,null,null,false);Check(read.status=="ready"&&!read.ok&&read.available&&f.writes.Count==0,"readonly");
  Check(read.settings[2].@default&&!read.settings[2].custom&&read.settings[2].editable&&(uint)read.settings[2].value==5,"driver default not forced constant");
  var invalid=NvidiaGlobalTransaction.Run(f,f.state.Token(),"frame_limit",19,true);Check(invalid.status=="invalid"&&f.writes.Count==0,"invalid no write");
  var stale=NvidiaGlobalTransaction.Run(f,new string('0',64),"frame_limit",60,true);Check(stale.status=="changed"&&f.writes.Count==0,"stale no write");
  var off=NvidiaGlobalTransaction.Run(f,f.state.Token(),"frame_limit",0,true);Check(off.ok&&off.status=="persisted"&&f.writes.SequenceEqual(new[]{"frame_limit:0"}),"explicit off creates only own override");
  Check(f.state.Find("power").Same(initial.Find("power"))&&f.state.Find("vsync").Same(initial.Find("vsync")),"other settings untouched");
  var beforeOff=f.writes.Count;var same=NvidiaGlobalTransaction.Run(f,f.state.Token(),"frame_limit",0,true);Check(same.ok&&same.status=="unchanged"&&f.writes.Count==beforeOff,"idempotent own off");
  var reset=NvidiaGlobalTransaction.Run(f,f.state.Token(),"frame_limit",null,true);Check(reset.ok&&reset.status=="persisted"&&f.writes.Last()=="frame_limit:delete"&&!f.state.Find("frame_limit").OwnOverride,"single delete default");
  var beforeReset=f.writes.Count;var noReset=NvidiaGlobalTransaction.Run(f,f.state.Token(),"frame_limit",null,true);Check(noReset.status=="unchanged"&&f.writes.Count==beforeReset,"inherited default no writes");
  f=new Fake();f.state.Find("power").Raw=99;f.state.Find("power").Location=0;f.state.Find("power").CurrentPredefined=false;
  var custom=NvidiaGlobalTransaction.Run(f,null,null,null,false);Check(custom.settings[2].custom&&!custom.settings[2].editable&&(uint)custom.settings[2].value==99,"custom readonly preserved");
  var noCustom=NvidiaGlobalTransaction.Run(f,f.state.Token(),"power",1,true);Check(noCustom.status=="unsupported"&&f.writes.Count==0,"cannot force unknown current policy");
  var customReset=NvidiaGlobalTransaction.Run(f,f.state.Token(),"power",null,true);Check(customReset.ok&&f.writes.Single()=="power:delete","custom reset only");
  f=new Fake();f.state.Find("power").Raw=3;f.state.Find("power").Location=0;f.state.Find("power").CurrentPredefined=false;
  var knownPower=NvidiaGlobalTransaction.Run(f,null,null,null,false);var preferMax=NvidiaGlobalTransaction.Run(f,f.state.Token(),"power",1,true);Check(!knownPower.settings[2].custom&&knownPower.settings[2].editable&&preferMax.ok&&f.writes.Single()=="power:1","official existing power modes stay recognized without expanding write allowlist");
  f=new Fake();f.state.Find("vsync").Supported=false;var unsupported=NvidiaGlobalTransaction.Run(f,f.state.Token(),"vsync",0x47814940,true);Check(unsupported.status=="unsupported"&&f.writes.Count==0,"unsupported no write");
  f=new Fake();f.beforeWrite=(driver,id)=>driver.state.DriverVersion++;var driverRace=NvidiaGlobalTransaction.Run(f,f.state.Token(),"power",1,true);Check(driverRace.status=="changed"&&f.writes.Count==0,"driver changes before CAS");
  f=new Fake();f.beforeWrite=(driver,id)=>driver.state.ProfileName="Other";var profileRace=NvidiaGlobalTransaction.Run(f,f.state.Token(),"power",1,true);Check(profileRace.status=="changed"&&f.writes.Count==0,"profile changes before CAS");
  f=new Fake();f.beforeWrite=(driver,id)=>driver.state.Find(id).Raw=77;var targetRace=NvidiaGlobalTransaction.Run(f,f.state.Token(),"frame_limit",60,true);Check(targetRace.status=="changed"&&f.writes.Count==0,"target changes before CAS");
  f=new Fake();f.beforeWrite=(driver,id)=>driver.state.Find("vsync").Raw=0x08416747;var otherRace=NvidiaGlobalTransaction.Run(f,f.state.Token(),"frame_limit",60,true);Check(otherRace.ok&&f.writes.Single()=="frame_limit:60"&&f.state.Find("vsync").Raw==0x08416747,"fresh one-target mutation preserves concurrent unrelated control");
  f=new Fake{throwSave=-137};var restored=NvidiaGlobalTransaction.Run(f,f.state.Token(),"power",1,true);Check(!restored.ok&&restored.status=="restored"&&restored.rollbackStatus=="restored"&&f.writes.SequenceEqual(new[]{"power:1","power:delete"}),"save ambiguous conditional own restore");
  Check(f.reads.Contains("power")&&f.reads.Count(x=>x=="power")==2,"rollback rereads only target before/after");
  f=new Fake{throwSave=-137};f.afterWrite=(driver,id)=>{driver.state.Find(id).Raw=77;driver.afterWrite=null;};var foreign=NvidiaGlobalTransaction.Run(f,f.state.Token(),"power",1,true);Check(foreign.status=="changed"&&foreign.rollbackStatus=="skipped-changed"&&f.writes.Count==1&&f.state.Find("power").Raw==77,"never overwrite third party changed target");
  f=new Fake{throwSave=-157};f.afterWrite=(driver,id)=>{driver.state.ProfileName="New global";driver.afterWrite=null;};var foreignProfile=NvidiaGlobalTransaction.Run(f,f.state.Token(),"power",1,true);Check(foreignProfile.status=="changed"&&f.writes.Count==1,"never rollback another global profile");
  f=new Fake{throwSave=-137};f.afterWrite=(driver,id)=>driver.throwReads=true;var lost=NvidiaGlobalTransaction.Run(f,f.state.Token(),"power",1,true);Check(lost.status=="lost-state"&&!lost.available&&!lost.ok&&f.writes.Count==1,"unknown readback no blind restore");
  f=new Fake();f.failReadNumber=2;var readFailure=NvidiaGlobalTransaction.Run(f,f.state.Token(),"frame_limit",120,true);Check(readFailure.status=="restored"&&!readFailure.ok&&f.writes.SequenceEqual(new[]{"frame_limit:120","frame_limit:delete"}),"failed verification restores only proven own value");
  f=new Fake{throwSave=-137};f.state.Find("power").Raw=1;f.state.Find("power").Location=0;f.state.Find("power").CurrentPredefined=false;
  var uncertainDelete=NvidiaGlobalTransaction.Run(f,f.state.Token(),"power",null,true);Check(uncertainDelete.status=="changed"&&uncertainDelete.rollbackStatus=="skipped-unproven"&&f.writes.Single()=="power:delete","ambiguous inherited default never guessed for rollback");
  f=new Fake{throwSave=-137};f.afterWrite=(driver,id)=>{driver.state.Find(id).DefaultRaw=2;driver.afterWrite=null;};var inheritedRace=NvidiaGlobalTransaction.Run(f,f.state.Token(),"power",1,true);Check(inheritedRace.status=="changed"&&f.writes.Count==1,"changed default metadata vetoes restore");
  foreach(var code in new[]{-137,-157,-124}){f=new Fake();f.beforeWrite=(driver,id)=>{throw new NvidiaApiError(code);};var refused=NvidiaGlobalTransaction.Run(f,f.state.Token(),"power",1,true);Check(refused.status==NvidiaGlobalTransaction.Failure(new NvidiaApiError(code))&&!refused.ok&&f.writes.Count==0,"uncommitted API failure reason preserved");}
  f=new Fake();f.failReadNumber=2;f.beforeWrite=(driver,id)=>{driver.state.Find(id).Raw=60;driver.state.Find(id).Location=0;driver.state.Find(id).CurrentPredefined=false;};var failedCasRead=NvidiaGlobalTransaction.Run(f,f.state.Token(),"frame_limit",60,true);Check(!failedCasRead.ok&&f.writes.Count==0&&f.state.Find("frame_limit").Raw==60,"rejected CAS plus read error never rolls back external matching desired value");
  f=new Fake();f.beforeWrite=(driver,id)=>{driver.state.Find(id).Raw=60;driver.state.Find(id).Location=0;driver.state.Find(id).CurrentPredefined=false;throw new NvidiaApiError(-137);};var beforeSaveError=NvidiaGlobalTransaction.Run(f,f.state.Token(),"frame_limit",60,true);Check(beforeSaveError.status=="permission"&&f.writes.Count==0&&f.state.Find("frame_limit").Raw==60,"pre-save API error never claims third-party desired value");
  foreach(var code in new[]{-137,-157,-124,-104,-160,-3,-1})Check(NvidiaGlobalTransaction.Failure(new NvidiaApiError(code))==new Dictionary<int,string>{{-137,"permission"},{-157,"driver-reload-required"},{-124,"reboot-required"},{-104,"unsupported"},{-160,"unsupported"},{-3,"unsupported"},{-1,"failed"}}[code],"status differentiated");
  Check(!read.restartGameRecommended.Equals(false)&&read.effectiveInRunningGame=="unknown"&&read.global,"no hot effect claim");
  foreach(uint count in new uint[]{1,64}){NvidiaGlobalDrs.ValidateGpuCount(count);Check(true,"valid actual API count gate");}
  foreach(uint count in new uint[]{0,65,uint.MaxValue}){try{NvidiaGlobalDrs.ValidateGpuCount(count);Check(false,"invalid GPU count accepted");}catch(NvidiaApiError error){Check(error.Code==-6&&NvidiaGlobalTransaction.Failure(error)=="unsupported","no GPU is unsupported, never NVIDIA by DLL alone");}}
  Console.WriteLine(checks+" actual NVIDIA transaction checks PASS; fake DRS only, no DLL loading, driver writes or hardware claim");
 }
}

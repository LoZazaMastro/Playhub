using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32.SafeHandles;
using Playhub.Shared;
[assembly:AssemblyMetadata("PlayhubSteamInputReaderSha256","0000000000000000000000000000000000000000000000000000000000000000")]
internal static class ReaderZeroChecks
{
 static int count;static void Need(bool ok,string text){++count;if(!ok)throw new Exception(text);}
 sealed class Target:IRetainedSteamGameTarget
 {public uint Pid=>20;public long Creation=>200;public string Aumid=>"";public SafeProcessHandle ProcessHandle=>throw new Exception("No OS process handle allowed");public bool HasExited=>false;public bool IsCurrent()=>true;}
 sealed class Output:IGuardedXboxRelayOutput
 {internal bool Owned;internal int Closes,Held;public bool IsOwned=>Owned;public bool TryConnectNeutral(Func<bool> guard){if(!guard())return false;Owned=true;return guard();}public bool TrySubmitCurrent(XboxReport r,Func<bool> guard){if(!Owned||!guard())return false;if(r!=default)Held++;return true;}public void Submit(XboxReport r)=>throw new Exception("unguarded");public bool Close(){Owned=false;++Closes;return true;}}
 sealed class Channel:SteamInputOrderedIssuer.IChannel
 {internal long Now=1000;internal readonly List<uint> Commands=[];internal SteamInputOrderedIssuer.Receipt? Last;
  public SteamInputOrderedIssuer.Receipt? Start(uint layout,SteamInputOrderedIssuer.Receipt? expected,Func<bool> guard)
  {if(expected is {} p?Last is not {} l||!ReferenceEquals(p.Identity,l.Identity):Last is not null)return null;if(!guard())return null;Commands.Add(layout);if(layout!=769)Now+=1036;return Last=new(new object(),layout);}
  public SteamInputOrderedIssuer.Acknowledgement Read(SteamInputOrderedIssuer.Receipt r)=>Last is {} l&&ReferenceEquals(l.Identity,r.Identity)?SteamInputOrderedIssuer.Acknowledgement.Acknowledged:SteamInputOrderedIssuer.Acknowledgement.Unknown;
 }
 static bool? Recover(SteamRawInputRelayTrial.ReaderOutcome attempted,SteamRawInputRelayTrial.ReaderOutcome previous,
  SteamRawInputRelayTrial.SessionBinding? old,SteamRawInputRelayTrial.SessionBinding binding,SteamInputOrderedIssuer issuer,
  Func<bool> current,Func<bool> neutral,Func<SteamMirrorNativeVisibleDto?> read,Func<long> clock,Action<int> wait,Func<bool> disconnected)
 {var method=typeof(SteamControllerLayoutTrial).GetMethod("ResumeDeferredReader",BindingFlags.Static|BindingFlags.NonPublic);
  Need(method is not null,"actual post-cleanup branch retains typed pre-start deferral");
  return (bool?)method!.Invoke(null,[attempted,previous,old,binding,issuer,current,neutral,read,clock,wait,CancellationToken.None,disconnected]);}
 static bool Deferred(SteamRawInputRelayTrial.ReaderOutcome r)=>(bool?)r.GetType().GetProperty("DeferredBeforeStart",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(r)==true;
 static int Main()=>Run();
 internal static int Run(){try{
  string dir=Path.Combine(Path.GetTempPath(),"Playhub-Reader-Zero-Fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
  try{
   string renderer=Path.Combine(dir,"GameOverlayRenderer64.dll");File.WriteAllBytes(renderer,[1]);
   using var steam=(TrustedSteamFiles)Activator.CreateInstance(typeof(TrustedSteamFiles),BindingFlags.Instance|BindingFlags.NonPublic,null,[dir],null)!;
   ((List<FileStream>)typeof(TrustedSteamFiles).GetField("_files",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(steam)!).Add(File.OpenRead(renderer));
   foreach(string mode in new[]{"recover","no-witness","foreign-frame","retained-ended","no-prior","prior-binding","prior-not-stopped","prior-not-neutral","no-native","stale-native","changed-game","disconnected","default-not-deferred"})
   {
    const ulong game=0x9234567802000000;var src=new TritonSource(0,0x1302,123);var binding=new SteamRawInputRelayTrial.SessionBinding(game,11,100,20,200,src,steam,100,8,20);
    var output=new Output();using var pdo=new SteamInputSessionOutput(()=>output);var rb=new SteamInputSessionOutput.ReaderBinding(binding,4940,300,7);
    var oldLease=pdo.Borrow(rb,()=>false)!;Need(oldLease.TryHoldNeutral(()=>true)&&oldLease.Close()&&pdo.NeutralHeld,"real old reader releases to SAME neutral PDO");
    var relay=(SteamRawInputRelayTrial)Activator.CreateInstance(typeof(SteamRawInputRelayTrial),BindingFlags.Instance|BindingFlags.NonPublic,null,["inert","inert"],null)!;
    typeof(SteamRawInputRelayTrial).GetField("_sessionOutput",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(relay,pdo);
    var channel=new Channel();bool hidden=false,ready=false;
    SteamMirrorNativeVisibleDto? Read(){long n=channel.Now;return mode=="no-native"?null:new(){Phase=hidden?2u:1u,SessionId=new('a',64),Generation="4",GameId=game.ToString(),GuideEpoch="4",OwnerPid=11,OwnerBirth="100",GamePid=mode=="changed-game"?21u:20u,GameBirth="200",SourceHwnd="500",SourceCookie="99",ValveOpen=!hidden,OwnCleanupComplete=true,ReceiptFrame="1",SourceFrame="2",BaselineSamples=32,BaselineMatched=32,OutputSamples=32,OutputMatched=32,ObservedAt=n.ToString(),ReadAt=n.ToString(),ExpiresAt=(n+1000).ToString(),PhysicalObservedAt=hidden?"0":n.ToString(),PhysicalExpiresAt=hidden?"0":(mode=="stale-native"?n-1:n+250).ToString()};}
    var issuer=new SteamInputOrderedIssuer(binding,SteamInputOrderedIssuer.InputRoute.OwnedSessionPad,()=>true,()=>true,()=>true,()=>channel.Now,_=>false,()=>relay.NeutralHeld,channel,
     nativeVisibleCurrent:n=>SteamNativeVisibleInputCycle.MatchesLease(Read(),n,channel.Now));
    SteamRawInputRelayTrial.ReaderOutcome attempted=default;
    var cycle=SteamMirrorInputCycle.Run((uint)(game>>32),9,binding,issuer,()=>true,()=>true,()=>null,()=>{
     Need(SteamInputReaderTrial.TryRun(steam,game,100,8,20,()=>false,CancellationToken.None,relay,src,new Target(),9,()=>throw new Exception("No reader reap"),out attempted,
      ()=>mode!="retained-ended",readCapturedPause:()=>mode=="no-witness"?null:RelayExit.OverlayOpened,
      readNativeVisible:()=>null,nativeBootstrapScope:()=>mode!="foreign-frame"),"actual TryRun returns without child start");return attempted;
    },_=>{},()=>channel.Now,ms=>channel.Now+=ms,CancellationToken.None);
    Need(cycle.GameCommandAcknowledged&&cycle.CleanupConfirmed&&issuer.AutomaticSettled&&attempted.ReaderPid==0,"actual slow game command + zero ACK leaves no new reader");
    Need(!attempted.CanResume&&!attempted.FreshAdmissionRequired,"deferred outcome never fabricates reader or readiness");
    if(mode is "no-witness" or "foreign-frame" or "retained-ended") {Need(!Deferred(attempted),"unknown veto is not an authenticated deferred opening "+mode);continue;}
    Need(Deferred(attempted),"actual opening pre-start deferral must survive instead of terminal reader0");
    var previous=new SteamRawInputRelayTrial.ReaderOutcome(true,false,false,RelayExit.OverlayOpened,true,true,false,4940,300,7,true,true);
    if(mode=="no-prior")previous=default;if(mode=="prior-not-stopped")previous=previous with{ReaderStopped=false};if(mode=="prior-not-neutral")previous=previous with{NeutralHeld=false};
    SteamRawInputRelayTrial.SessionBinding? prior=mode=="prior-binding"?binding with{Window=101}:binding;
    void Wait(int ms){channel.Now+=ms;var status=SteamNativeVisibleInputCycle.ReadStatus();if(status?.InputReady==true){ready=true;hidden=true;}if(channel.Now>12000)throw new Exception("unbounded");}
    bool? result=Recover(mode=="default-not-deferred"?default:attempted,previous,prior,binding,issuer,()=>true,()=>relay.NeutralHeld,Read,()=>channel.Now,Wait,()=>mode=="disconnected");
    if(mode=="recover"){
     Need(result==true&&ready,"actual new Native240 + new769 ACK restores UI on SAME stopped reader/PDO");
     Need(channel.Commands.SequenceEqual(new[]{(uint)(game>>32),0u,769u,0u}),"no second game command or raw bootstrap in deferred handoff");
     Need(output.Closes==0&&output.Held==0&&pdo.NeutralHeld,"same target retained and neutral, never forwarded under menu");
    }else {Need(result==(mode=="default-not-deferred"?(bool?)null:false),"unsafe deferred recovery rejected "+mode);Need(!ready&&!channel.Commands.Contains(769),"negative never acquires menu "+mode);}
   }
  }finally{Directory.Delete(dir,true);}
  Console.WriteLine(count+" zero-start/actual typed handoff checks PASS; inert files/output/channel, no Steam/native/game actions");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}

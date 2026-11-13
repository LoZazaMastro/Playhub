using Playhub.Shared;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
int count=0;void Check(bool value,string name){if(!value)throw new Exception(name);count++;}
string registeredLauncher = @"C:\Program Files\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe";
Check(EpicNativeLaunchSystem.ParseRegisteredLauncherCommand('"'+registeredLauncher+"\" %1")==registeredLauncher,"current Epic unquoted URI placeholder");
Check(EpicNativeLaunchSystem.ParseRegisteredLauncherCommand('"'+registeredLauncher+"\" \"%1\"")==registeredLauncher,"quoted URI placeholder remains supported");
Check(EpicNativeLaunchSystem.ParseRegisteredLauncherCommand(" \""+registeredLauncher+"\" %1 \t")==registeredLauncher,"registration boundary whitespace");
foreach(string command in new[]{registeredLauncher+" %1",'"'+registeredLauncher+"\" %10",'"'+registeredLauncher+"\" %1 -extra",'"'+registeredLauncher+"\" -extra %1",'"'+registeredLauncher+"\" \"%1\" \"extra\"", "\"relative\\EpicGamesLauncher.exe\" %1", "\"C:\\Other.exe\" %1"})
    Check(EpicNativeLaunchSystem.ParseRegisteredLauncherCommand(command)is null,"reject ambiguous registration: "+command);
Check(EpicNativeLaunchSystem.ParseRegisteredLauncherCommand(null)is null,"missing registration is unavailable");
FakeSystem New()=>new();
EpicLaunchProvenance Seal(FakeSystem s,EpicCaptureScope scope)=>scope.CreateProvenance(30,140,777,110,0)??throw new Exception("seal");
using(var s=New())using(var scope=EpicLaunchWindows.Capture(s.Plan,10,100,s)!) {
 Check(scope.Current&&!scope.FastCurrent,"capture is not yet game ownership");
 var p=Seal(s,scope);Check(scope.FastCurrent&&p.GameExecutable==s.PayloadValue.Executable&&p.RecordedLaunchExecutable==s.Plan.GameExecutable,"actual configured payload separate from recorded bootstrapper");
 Check(JsonSerializer.Deserialize<EpicLaunchProvenance>(JsonSerializer.Serialize(p))==p,"scalar witness stable across IPC JSON roundtrips");
 Check(EpicLaunchWindows.ReadWitness(p,10,100)?.Length==5,"game EAC launcher bootstrapper signedEpic exact five generations");
 Check(s.ImageDisposals==1,"signature file pin released at seal for updater");
 s.Metadata=false;s.Registered="changed";s.Trusted=false;
 Check(scope.Current,"postseal catalog handler signer update does not revoke authorized game");
 foreach(uint pid in new uint[]{31,32,33,20})s.Models[pid].State=EpicProcessState.Exited;
 Check(scope.FastCurrent,"normal intermediate and Epic UI exit preserve game input lifetime");
 int reads=s.ResolveCalls+s.PayloadCalls+s.RegisterCalls+s.WindowCalls;Check(scope.FastCurrent&&s.ResolveCalls+s.PayloadCalls+s.RegisterCalls+s.WindowCalls==reads,"fast path has zero metadata registry or window IO");
 int opens=s.Opened.Count;
 using var consumer=EpicLaunchWindows.RetainFromAuthenticatedWrapper(p,10,100,s);
 Check(consumer?.Current==true,"authenticated consumer accepts historical witness after positive ancestor exits");
 Check(s.Opened.Skip(opens).SequenceEqual(new uint[]{10,30}),"consumer opens only wrapper and actual game, never reconstructs dead parent PIDs");
 s.Models[31].State=EpicProcessState.Unknown;
 Check(!scope.Current,"producer unknown ancestor stops fresh private publications");
 s.Models[31].State=EpicProcessState.Exited;s.Models[30].State=EpicProcessState.Exited;
 Check(!scope.FastCurrent&&consumer?.Current==false,"actual game exit revokes producer and consumer");
}
void Refuse(Action<FakeSystem> change,string name){using var s=New();change(s);using var scope=EpicLaunchWindows.Capture(s.Plan,10,100,s);Check(scope is null||scope.CreateProvenance(30,140,777,110,0)is null,name);}
Refuse(s=>s.Models[31].State=EpicProcessState.Exited,"ancestor must be alive when witnessed");
Refuse(s=>s.Models[31].State=EpicProcessState.Unknown,"unknown ancestor before seal unavailable");
Refuse(s=>s.Models[31].Facts=s.Models[31].Facts with{Birth=160},"parent born after child rejects reuse");
Refuse(s=>s.Models[32].Facts=s.Models[32].Facts with{ParentPid=20},"missing recorded metadata launch target rejected");
Refuse(s=>s.Models[31].Facts=s.Models[31].Facts with{ParentPid=31},"ancestry loop rejected");
Refuse(s=>s.Models[31].Facts=s.Models[31].Facts with{Sid="foreign"},"cross-user intermediate rejected");
Refuse(s=>s.Models[31].Facts=s.Models[31].Facts with{Image=@"C:\OtherInstall\Eac.exe"},"external intermediate outside metadata install rejected");
Refuse(s=>s.Models[31].Facts=s.Models[31].Facts with{Birth=109},"old intermediary cannot be adopted by fresh request");
Refuse(s=>s.Models[30].Facts=s.Models[30].Facts with{Image=s.Plan.GameExecutable},"EAC splash/bootstrapper cannot replace configured payload");
Refuse(s=>s.Window=false,"unpresented or wrong game window cannot seal");
Refuse(s=>s.Trusted=false,"untrusted initial Epic executable rejected");
Refuse(s=>s.PayloadValue=null!,"unknown payload configuration cannot fall back to bootstrapper");
using(var s=New())using(var scope=EpicLaunchWindows.Capture(s.Plan,10,100,s)!) {
 var p=Seal(s,scope);var rows=JsonSerializer.Deserialize<EpicProcessIdentity[]>(p.ParentChain!)!;
 Check(EpicLaunchWindows.ReadWitness(p with{ParentChain="[]"},10,100)is null,"incomplete historical witness rejected");
 Check(EpicLaunchWindows.ReadWitness(p with{ParentChain="[null,null]"},10,100)is null,"null witness nodes fail closed");
 Check(EpicLaunchWindows.ReadWitness(p with{Namespace=null!},10,100)is null,"missing private scalar fails closed");
 Check(EpicLaunchWindows.ReadWitness(p with{GameBirth=141},10,100)is null,"witness game birth mismatch rejected");
 Check(EpicLaunchWindows.ReadWitness(p with{WrapperBirth=99},10,100)is null,"old wrapper witness replay rejected");
 Check(EpicLaunchWindows.ReadWitness(p with{GameExecutable=s.Plan.GameExecutable},10,100)is null,"witness cannot relabel bootstrapper as game");
 Check(EpicLaunchWindows.ReadWitness(p with{PayloadConfigurationPath=null},10,100)is null,"different configured payload requires recorded config pin");
 Check(EpicLaunchWindows.ReadWitness(p with{ManifestIdentitySha256=new string('F',64)},10,100)is null,"metadata identity tampering rejected");
 rows[2]=rows[2] with{Birth=190};Check(EpicLaunchWindows.ReadWitness(p with{ParentChain=JsonSerializer.Serialize(rows)},10,100)is null,"historical chronology tampering rejected");
 s.Models[30].Facts=s.Models[30].Facts with{Birth=141};using var replay=EpicLaunchWindows.RetainFromAuthenticatedWrapper(p,10,100,s);Check(replay is null,"old witness cannot attach reused game PID");
}
using(var s=New())using(var scope=EpicLaunchWindows.Capture(s.Plan,10,100,s)!) {
 Seal(s,scope);s.Models[32].State=EpicProcessState.Exited;s.Models[32].Exit=120;Check(!scope.FastCurrent,"impossible historical exit before child birth refused");
}
using(var s=New())using(var scope=EpicLaunchWindows.Capture(s.Plan,10,100,s)!) {
 s.OnResolve=()=>scope.Dispose();Check(scope.CreateProvenance(30,140,777,110,0)is null&&!scope.Current,"dispose during slow read cannot reopen witness");
}
using(var s=New())using(var scope=EpicLaunchWindows.Capture(s.Plan,10,100,s)!) {
 s.OnWindow=()=>scope.Dispose();Check(scope.CreateProvenance(30,140,777,110,0)is null&&!scope.Current,"dispose during window query prevents seal");
}
using(var s=New())using(var scope=EpicLaunchWindows.Capture(s.Plan,10,100,s)!) {
 var p=Seal(s,scope);s.Models[20].Facts=s.Models[20].Facts with{Birth=91};Check(!scope.FastCurrent,"retained ancestor generation replacement refused after seal");
}
Console.WriteLine($"PASS {count} actual historical-chain checks; fake native only; no launch, UI or native API executed.");

sealed class Model(EpicProcessIdentity facts){internal EpicProcessIdentity Facts=facts;internal EpicProcessState State=EpicProcessState.Alive;internal long Exit=180;}
sealed class FakeProcess(Model m):IEpicRetainedProcess {
 bool disposed;public EpicProcessIdentity Identity=>m.Facts;public bool Current=>State==EpicProcessState.Alive;
 public EpicProcessState State=>disposed?EpicProcessState.Unknown:m.State;public bool IdentityRetained=>State!=EpicProcessState.Unknown;
 public long ExitAt=>State==EpicProcessState.Exited?m.Exit:0;public void Dispose()=>disposed=true;
}
sealed class FakeSignedImage(FakeSystem s):IEpicTrustedImage {
 bool disposed;public string Image=>s.Launcher;public string Sha256=>new('A',64);public bool Current=>!disposed&&s.Trusted;
 public void Dispose(){if(!disposed)s.ImageDisposals++;disposed=true;}
}
sealed class FakeSystem:IEpicLaunchSystem,IDisposable {
 internal readonly Dictionary<uint,Model> Models=new();internal readonly List<uint> Opened=new();
 internal readonly string Launcher=@"C:\Epic\EpicGamesLauncher.exe";internal string Registered;internal bool Trusted=true,Metadata=true,Window=true;
 internal EpicInstalledLaunch Plan;internal EpicLaunchPayload PayloadValue;internal Action? OnResolve,OnWindow;
 internal int ImageDisposals,ResolveCalls,PayloadCalls,RegisterCalls,WindowCalls;
 public FakeSystem(){
  Registered=Launcher;string root=@"C:\Installed\Example",recorded=root+@"\Bin\Bootstrapper.exe",manifest=@"C:\EpicData\installation.item";
  string id=string.Join("\n","namespace","catalog","artifact",manifest.ToUpperInvariant(),root.ToUpperInvariant(),recorded.ToUpperInvariant());
  Plan=new("namespace","catalog","artifact",manifest,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))),root,recorded,"unused-test-uri");
  PayloadValue=new(root+@"\Bin\ActualGame.exe",root+@"\Bin\EasyAntiCheat\Settings.json",new string('B',64));
  Models[10]=new(new(10,100,@"C:\Playhub\Playhub.GameSession.exe",1,"same",1));Models[20]=new(new(20,90,Launcher,1,"same",1));
  Models[30]=new(new(30,140,PayloadValue.Executable,31,"same",1));Models[31]=new(new(31,130,root+@"\Bin\Eac.exe",32,"same",1));
  Models[32]=new(new(32,120,root+@"\Bin\Launcher.exe",33,"same",1));Models[33]=new(new(33,115,recorded,20,"same",1));
 }
 public long Now=>150;
 public EpicInstalledLaunch? Resolve(EpicInstalledLaunch expected){ResolveCalls++;var c=OnResolve;OnResolve=null;c?.Invoke();return Metadata?Plan:null;}
 public EpicLaunchPayload? Payload(EpicInstalledLaunch expected){PayloadCalls++;return PayloadValue;}
 public string? RegisteredLauncher(){RegisterCalls++;return Registered;}
 public IEpicTrustedImage? Trust(string image)=>Trusted?new FakeSignedImage(this):null;
 public IEpicRetainedProcess? Open(uint pid){Opened.Add(pid);return Models.TryGetValue(pid,out var model)?new FakeProcess(model):null;}
 public IReadOnlyList<uint> LauncherCandidates(string image)=>new uint[]{20};
 public bool GameWindow(long window,uint pid){WindowCalls++;var c=OnWindow;OnWindow=null;c?.Invoke();return Window&&window==777&&pid==30;}
 public void Dispose(){}
}

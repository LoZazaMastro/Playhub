"""Inert regression checks for the actual Explorer startup and boot-mode methods."""
import pathlib,subprocess,tempfile,os
ROOT=pathlib.Path(__file__).resolve().parents[1]
SERVICES=ROOT/"GamingModeAgent/GamingMode.Services"
BEFORE=pathlib.Path(os.environ.get("PLAYHUB_EXPLORER_BOOT_BEFORE",pathlib.Path(__file__).parent/"Fixtures/ExplorerBootBefore.cs.fixture"))
def baseline(name): return BEFORE/name if BEFORE.is_dir() else BEFORE
def method(path,signature):
 text=path.read_text(encoding="utf-8-sig");start=text.index(signature);opened=text.index("{",start);depth=1;end=opened+1
 while depth:
  if text[end]=="{":depth+=1
  elif text[end]=="}":depth-=1
  end+=1
 return text[start:end]
boot=method(SERVICES/"ModeManager.cs","public async Task ApplyBootModeAsync(")
before_boot=method(baseline("ModeManager.cs"),"public async Task ApplyBootModeAsync(")
explorer=method(SERVICES/"ProcessTools.cs","public bool StartExplorer(")
before_explorer=method(baseline("ProcessTools.cs"),"public bool StartExplorer(")
template=r'''
using System; using System.IO; using System.Threading.Tasks;
using Process=FakeProcess;using ProcessStartInfo=FakeStart;using ProcessWindowStyle=FakeStyle;using Thread=FakeThread;
internal class Program {
 static int count;static void Check(bool value,string label){if(!value)throw new Exception(label);count++;Console.WriteLine("PASS "+label);}
 static async Task Main(){
  Fixture.Reset(5700);Check(!new BeforeExplorer().StartExplorer(true)&&Fixture.Clock==4000,"BEFORE: late shell at5.7s fails4s readiness");
  Fixture.Reset(5700);Check(new AfterExplorer().StartExplorer(true)&&Fixture.Clock==5700&&Fixture.Starts==1,"AFTER: same late shell completes original launch once");
  Fixture.Reset(0);Check(new AfterExplorer().StartExplorer(true)&&Fixture.Starts==0,"Existing exact shell starts no additional Explorer");
  long now=0;int starts=0,waits=0;
  Check(ExplorerShellReadiness.Ensure(()=>false,()=>starts++,()=>true,ms=>{now+=ms;waits++;},()=>now)==ExplorerShellReadyResult.TimedOut&&now==12000&&starts==1&&waits==120,"Unavailable shell has a twelve-second bound and one launch");
  now=0;starts=0;
  Check(ExplorerShellReadiness.Ensure(()=>false,()=>starts++,()=>false,ms=>now+=ms,()=>now)==ExplorerShellReadyResult.Cancelled&&starts==0&&now==0,"Cancellation before launch changes no process");
  now=0;starts=0;
  Check(ExplorerShellReadiness.Ensure(()=>false,()=>starts++,()=>now<500,ms=>now+=ms,()=>now)==ExplorerShellReadyResult.Cancelled&&starts==1&&now==500,"Foreground cancellation interrupts the wait before Steam");
  now=0;int guards=0;
  Check(ExplorerShellReadiness.Ensure(()=>true,()=>throw new Exception("start"),()=>++guards<2,ms=>now+=ms,()=>now)==ExplorerShellReadyResult.Cancelled,"Late cancellation vetoes a ready shell");
  Fixture.Apply=()=>Task.FromResult(new ApiResult{Ok=false,Message="shell unavailable"});
  var old=new BeforeBoot();await old.ApplyBootModeAsync(true);Check(old.ShellWrites==1,"BEFORE: failed boot silently writes future shell as success");
  var failed=new AfterBoot();bool rejected=false;try{await failed.ApplyBootModeAsync(true);}catch(InvalidOperationException e){rejected=e.Message.Contains("shell unavailable");}
  Check(rejected&&failed.ShellWrites==0,"AFTER: actual failed boot result propagates before success-only shell update");
  var entered=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
  var gate=new TaskCompletionSource<ApiResult>(TaskCreationOptions.RunContinuationsAsynchronously);
  int caller=Environment.CurrentManagedThreadId;
  Fixture.Apply=()=>{entered.TrySetResult(Environment.CurrentManagedThreadId);return gate.Task;};
  var live=new AfterBoot();var operation=live.ApplyBootModeAsync(true);
  Check(await Task.WhenAny(entered.Task,Task.Delay(5000))==entered.Task&&!operation.IsCompleted,"Readiness work yields the caller while splash dispatcher remains available");
  Check(await entered.Task!=caller,"Actual boot application executes on a worker thread");
  gate.SetResult(new ApiResult{Ok=true});await operation;Check(live.ShellWrites==1,"Successful worker boot completes original configuration once");
  Console.WriteLine(count+" actual production checks PASS; no OS process, registry, Steam or window calls.");
 }
}
internal static class Fixture {internal static long Clock,ReadyAt;internal static int Starts;internal static Func<Task<ApiResult>> Apply=()=>Task.FromResult(new ApiResult{Ok=true});internal static void Reset(long at){Clock=0;ReadyAt=at;Starts=0;}}
internal enum FakeStyle{Hidden,Normal}
internal sealed class FakeStart{public string FileName{get;set;}="";public bool UseShellExecute{get;set;}public FakeStyle WindowStyle{get;set;}}
internal sealed class FakeProcess:IDisposable{public static FakeProcess Start(FakeStart p){Fixture.Starts++;return new();}public void Dispose(){}}
internal static class FakeThread{internal static void Sleep(int ms){Fixture.Clock+=ms;}}
internal sealed class Log{public void Info(string s){}public void Error(string s){}public void Error(string s,Exception e){}}
internal class BeforeExplorer{private Log _logger=new();private static bool IsExplorerShellRunning()=>Fixture.Clock>=Fixture.ReadyAt;BEFORE_EXPLORER}
internal class AfterExplorer{private Log _logger=new();private static bool IsExplorerShellRunning()=>Fixture.Clock>=Fixture.ReadyAt;AFTER_EXPLORER}
internal enum ModeKind{Gaming,Desktop}
internal class ModeConfig{public ModeKind? NextBootMode;public ModeKind DefaultMode=ModeKind.Gaming;}
internal class Store{public ModeConfig LoadConfig()=>new();public void SaveConfig(ModeConfig c){}}
internal class ApiResult{public bool Ok;public string Message="";}
internal class Shell{public int Writes;public void RestoreExplorerShell(){}public void SetShellForMode(ModeKind m){Writes++;}}
internal static class SafeModeGuard{public static bool ShouldForceDesktop(object p)=>false;public static void ApplySafeDefaults(ModeConfig c){}}
internal class BeforeBoot{private object _paths=new();private Log _logger=new();private Store _store=new();private Shell _shellTools=new();public int ShellWrites=>_shellTools.Writes;private Task<ApiResult> ApplyModeAsync(ModeKind m,string s,bool interactive,bool restoreStartupApps)=>Fixture.Apply();BEFORE_BOOT}
internal class AfterBoot{private long _explicitDesktopRevision;private Func<bool>? _configuredBootGuard;internal Func<bool>? StartupForegroundGuard;private object _paths=new();private Log _logger=new();private Store _store=new();private Shell _shellTools=new();public int ShellWrites=>_shellTools.Writes;private Task<ApiResult> ApplyModeAsync(ModeKind m,string s,bool interactive,bool restoreStartupApps)=>Fixture.Apply();AFTER_BOOT}
namespace Playhub.Shared { internal static class DeckyStartupGuard{internal static int UpgradeExistingAutostart()=>0;} }
'''
code=template.replace("BEFORE_EXPLORER",before_explorer).replace("AFTER_EXPLORER",explorer).replace("BEFORE_BOOT",before_boot).replace("AFTER_BOOT",boot)
task=pathlib.Path(tempfile.mkdtemp(prefix="Playhub-Explorer-Boot-Actual-"))
(task/"Program.cs").write_text(code,encoding="utf-8")
(task/"ExplorerShellReadiness.cs").write_bytes((SERVICES/"ExplorerShellReadiness.cs").read_bytes())
(task/"test.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><NoWarn>CS0649</NoWarn></PropertyGroup></Project>',encoding="utf-8")
# The production helper remains internal to its namespace.
p=task/"Program.cs";p.write_text("using GamingMode.Services;\n"+p.read_text(encoding="utf-8"),encoding="utf-8")
result=subprocess.run(["dotnet","run","--project",str(task/"test.csproj"),"-c","Release"],capture_output=True,text=True)
(task/"result.log").write_text(result.stdout+result.stderr,encoding="utf-8")
print(result.stdout+result.stderr);print("Evidence: "+str(task/"result.log"));raise SystemExit(result.returncode)


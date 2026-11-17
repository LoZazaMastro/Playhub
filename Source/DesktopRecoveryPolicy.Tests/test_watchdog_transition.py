"""Compile the actual watchdog decision/apply branches with inert OS boundaries."""
from pathlib import Path
import os,subprocess,tempfile

source=Path(os.environ.get('PLAYHUB_WATCHDOG_SOURCE',str(Path(__file__).resolve().parents[1])))
manager=(source/'GamingModeAgent/GamingMode.Services/ModeManager.cs').read_text(encoding='utf-8-sig')
policy=(source/'GamingModeAgent/GamingMode.Services/DesktopRecoveryPolicy.cs').read_text(encoding='utf-8-sig')
if 'Task? restore = recovery.TryRestoreCurrent' in manager:
    start=manager.index('Task? restore = recovery.TryRestoreCurrent')
    end=manager.index('if (restore is not null) await restore;',start)+len('if (restore is not null) await restore;')
else:
    start=manager.index('if (recovery.ShouldRestore(ProcessTools.IsExplorerShellRunning')
    end=manager.index(';',start)+1
tick=manager[start:end]
start=manager.index('public Task<ApiResult> ApplyModeAsync(')
brace=manager.index('{',start);depth=1;end=brace+1
while depth:
    depth += (manager[end]=='{')-(manager[end]=='}');end+=1
apply=manager[start:end]
work=Path(tempfile.mkdtemp(prefix='Playhub-Watchdog-Actual-'))
(work/'Checks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>')
(work/'ActualPolicy.cs').write_text(policy)
harness=r'''
using GamingMode.Services;
using System.Threading;
class ApiResult {}
enum ModeKind { Desktop,Gaming }
sealed class State { public ModeKind CurrentMode; }
sealed class Store { public readonly State Value=new(); public State LoadState()=>Value; }
static class ProcessTools { public static bool IsExplorerShellRunning()=>Harness.Active.ReadShell(); }
sealed class Harness {
 public static Harness Active=null!;
 internal readonly object _applyMode=new();internal readonly Store _store=new();
 internal readonly DesktopRecoveryPolicy recovery=new();private long _explicitDesktopRevision;
 public bool Steam,Shell,Shutdown;public int Applied,ShellReads;public bool AppliedWithLock;
 public bool LastInteractive=true,LastStartup=true,LastUpdate=true;public Action<int>? OnShell;
 public long Proposals=>Interlocked.Read(ref _explicitDesktopRevision);
 public Harness(bool gaming=true){_store.Value.CurrentMode=gaming?ModeKind.Gaming:ModeKind.Desktop;Active=this;}
 public bool ReadShell(){int n=++ShellReads;OnShell?.Invoke(n);return Shell;}
 private bool IsSteamProcessRunning()=>Steam;
 private bool IsSystemShuttingDown()=>Shutdown;
 private Task<ApiResult> ApplyMode(ModeKind mode,string action,bool interactive,bool restoreStartupApps,bool updateShell){
  Applied++;AppliedWithLock=Monitor.IsEntered(_applyMode);LastInteractive=interactive;LastStartup=restoreStartupApps;LastUpdate=updateShell;
  _store.Value.CurrentMode=mode;return Task.FromResult(new ApiResult());
 }
 __APPLY__
 public async Task Tick(){Func<bool> isGaming=()=>_store.LoadState().CurrentMode==ModeKind.Gaming;
 __TICK__
 }
}
static class Checks {
 static int count;static void Need(bool condition,string name){if(!condition)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
 static async Task Main(){
  // Real race: committed state remains Desktop while ApplyGaming owns the gate.
  // The old predicate proposes Desktop and waits behind that very Gaming apply.
  var h=new Harness(false){Steam=true};using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
  var transition=new Thread(()=>{lock(h._applyMode){entered.Set();release.Wait();h._store.Value.CurrentMode=ModeKind.Gaming;}});transition.Start();entered.Wait();
  await Task.Run(h.Tick).WaitAsync(TimeSpan.FromSeconds(2));
  var second=Task.Run(h.Tick);
  Need(SpinWait.SpinUntil(()=>second.IsCompleted||h.Proposals>0,2000),"second tick reached its real decision or completed");
  release.Set();transition.Join();await second.WaitAsync(TimeSpan.FromSeconds(2));
  Need(h.Applied==0&&h._store.Value.CurrentMode==ModeKind.Gaming,"stale watchdog proposal must not undo successful Gaming transition");
  Need(h.Proposals==0,"busy watchdog never increments explicit Desktop revision or queues recovery");
  h=new Harness();await h.Tick();Need(h.Applied==0,"one missing Steam observation waits");await h.Tick();
  Need(h.Applied==1&&h._store.Value.CurrentMode==ModeKind.Desktop,"genuine committed Gaming Steam crash still restores Desktop after two observations");
  Need(h.AppliedWithLock&&!h.LastInteractive&&!h.LastStartup&&!h.LastUpdate,"existing bounded Desktop recovery options and transition lock preserved");
  h=new Harness(false);for(int i=0;i<4;i++)await h.Tick();Need(h.Applied==0,"already Desktop receives no stale automatic mode apply");
  h=new Harness(){Steam=true};for(int i=0;i<4;i++)await h.Tick();Need(h.Applied==0,"live Steam without Explorer remains Gaming");
  h=new Harness(){Shutdown=true};for(int i=0;i<4;i++)await h.Tick();Need(h.Applied==0,"Windows shutdown never restores Explorer");
  h=new Harness(){Shell=true};for(int i=0;i<4;i++)await h.Tick();Need(h.Applied==0,"existing desktop shell prevents recovery");
  h=new Harness();await h.Tick();h.Steam=true;await h.Tick();h.Steam=false;await h.Tick();Need(h.Applied==0,"Steam bootstrap return resets prior absence");await h.Tick();Need(h.Applied==1,"new persistent absence still recovers");
  h=new Harness();await h.Tick();h.OnShell=n=>{if(n==4)h.Steam=true;};await h.Tick();
  Need(h.Applied==0,"Steam returns during slow observations: final commit barrier rejects old absence");
  h=new Harness();await h.Tick();h.OnShell=n=>{if(n==4)h._store.Value.CurrentMode=ModeKind.Desktop;};await h.Tick();
  Need(h.Applied==0,"mode changes during slow observations: stale proposal is ignored");
  h=new Harness();await h.Tick();using var busy=new ManualResetEventSlim();using var free=new ManualResetEventSlim();
  var owner=new Thread(()=>{lock(h._applyMode){busy.Set();free.Wait();}});owner.Start();busy.Wait();
  await Task.Run(h.Tick).WaitAsync(TimeSpan.FromSeconds(2));free.Set();owner.Join();
  await h.Tick();Need(h.Applied==0,"busy transition resets accumulated absence");await h.Tick();Need(h.Applied==1,"post-transition recovery requires two new current observations");
  Console.WriteLine($"{count} actual watchdog transaction checks PASS; no process/shell/registry/input/HTTP calls");
 }
}
'''.replace('__APPLY__',apply).replace('__TICK__',tick)
(work/'Program.cs').write_text(harness)
run=subprocess.run(['dotnet','run','--project',str(work/'Checks.csproj'),'-c','Release','--nologo'],capture_output=True,text=True,encoding='utf-8',errors='replace')
(work/'results.log').write_text(run.stdout+run.stderr,encoding='utf-8')
print(run.stdout+run.stderr);print('Actual extracted source harness:',work)
raise SystemExit(run.returncode)

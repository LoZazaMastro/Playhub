# SPDX-License-Identifier: MIT
"""Rehost actual deferred-row producer and callbacks; fake controls/network only."""
import pathlib,re,tempfile,subprocess,sys,json
source=pathlib.Path(__file__).resolve().parents[1]
page=source/'Playhub/StoreLibrary/StoreLibraryPage.xaml.cs'
if '--page' in sys.argv:page=pathlib.Path(sys.argv[sys.argv.index('--page')+1])
s=page.read_text(encoding='utf-8-sig')
def method(name):
 m=re.search(r'    private (?:async )?(?:void|Task) '+name+r'\([^\n]*\)\s*\{',s)
 if not m:return ''
 start=m.start();i=s.index('{',m.start());depth=1;i+=1
 while depth:
  if s[i]=='{':depth+=1
  elif s[i]=='}':depth-=1
  i+=1
 return s[start:i]
def klass():
 start=s.index('    private sealed class InstalledCardActions');i=s.index('{',start);depth=1;j=i+1
 while depth:
  if s[j]=='{':depth+=1
  elif s[j]=='}':depth-=1
  j+=1
 return s[start:j]
start=s.index('            var row = new InstalledCardActions(game, play, uninstall);') if '            var row = new InstalledCardActions(game, play, uninstall);' in s else s.index('            installedRows[game.Key] = new(game, play, uninstall);')
end=s.index('            content.Children.Add(play);',start)
producer=s[start:end]
methods='\n'.join(method(n) for n in ['InstalledCardLoaded','UpdateInstalledButtons','ReadInstalledCardAsync','ShowError'])
code=r'''using System;using System.Linq;using System.Collections.Generic;using System.Threading.Tasks;
record StoreLibraryGame(string Key);record StoreInstalledGameStatus(bool CanLaunch,bool CanUninstall);
class Button{public bool IsEnabled;}
class FakeContent{public event EventHandler? Loaded;public void Load()=>Loaded?.Invoke(this,EventArgs.Empty);}
class Actions{public bool Busy;public int Reads;public Task<StoreInstalledGameStatus?>? Next;public Task<StoreInstalledGameStatus?> ReadStatusAsync(StoreLibraryGame g){Reads++;return Next??Task.FromResult<StoreInstalledGameStatus?>(new(true,true));}}
enum StoreKind{Epic,Gog,Xbox,Amazon}
enum StoreJobState{Starting,Downloading,Pausing,Installing,Verifying,Canceling}
record Job(StoreJobState State);
class Snapshot{public Job[] Jobs=Array.Empty<Job>();}
class Controller{public bool Busy;public Snapshot Snapshot=new();public bool IsRefreshing(StoreKind store)=>false;}
class CardState{public object? Installed;}
enum InfoBarSeverity{Warning}
class InfoBar{public bool IsOpen;public InfoBarSeverity Severity;public string Message="";public int Revealed;public void StartBringIntoView()=>Revealed++;}
class Queue{readonly List<Action> pending=new();public bool TryEnqueue(Action a){pending.Add(a);return true;}public void Drain(){var old=pending.ToArray();pending.Clear();foreach(var a in old)a();}}
class Page{
 public bool disposed,active=true,dialogOpen,installedViewPending;public readonly Actions installedActions=new();readonly Controller controller=new();readonly Dictionary<string,InstalledCardActions> installedRows=new();readonly Queue DispatcherQueue=new();public readonly InfoBar Status=new();string? localErrorKey;readonly Func<string,string> text=x=>x;
 bool CurrentInstalledGame(StoreLibraryGame g)=>active&&!disposed;
 public void Render(){installedRows.Clear();UpdateInstalledButtons();}
 public void Notify()=>UpdateInstalledButtons();
 public FakeContent Realize(string key){var game=new StoreLibraryGame(key);var play=new Button();var uninstall=new Button();var content=new FakeContent();var cardState=new CardState();
''' +producer+r'''
 return content;}
 public bool Enabled(string key)=>installedRows[key].Play.IsEnabled&&installedRows[key].Uninstall.IsEnabled;
 public void Error(string key)=>ShowError(key);public void Drain()=>DispatcherQueue.Drain();
''' +klass()+'\n'+methods+r'''
}
static class Test{
 static int n;static void Check(bool v,string m){n++;if(!v)throw new Exception(m);}
 static async Task Main(){
 var p=new Page();p.Render();var card=p.Realize("game");Check(p.installedActions.Reads==0,"virtual producer does not run during prior Render");card.Load();Check(p.installedActions.Reads==1&&p.Enabled("game"),"first deferred realization must fetch and enable verified actions without a tab bounce");card.Load();Check(p.installedActions.Reads==1,"repeated Loaded never duplicates an attempted request");
 p=new();p.Render();var old=p.Realize("game");var replacement=p.Realize("game");old.Load();Check(p.installedActions.Reads==0,"replaced row Loaded cannot fetch for successor");replacement.Load();Check(p.installedActions.Reads==1&&p.Enabled("game"),"current replacement can load once");
 p=new();p.Render();card=p.Realize("game");p.active=false;card.Load();Check(p.installedActions.Reads==0,"inactive view cannot fetch from a late load");p.active=true;p.Notify();Check(p.installedActions.Reads==1,"explicit view activation can fetch unattempted current row");
 p=new();p.Render();card=p.Realize("game");p.disposed=true;card.Load();Check(p.installedActions.Reads==0,"disposed row never fetches");
 p=new();p.Render();var gate=new TaskCompletionSource<StoreInstalledGameStatus?>(TaskCreationOptions.RunContinuationsAsynchronously);p.installedActions.Next=gate.Task;old=p.Realize("game");old.Load();Check(p.installedActions.Reads==1&&!p.Enabled("game"),"pending response leaves actions disabled");p.Render();p.installedActions.Next=null;replacement=p.Realize("game");replacement.Load();Check(p.Enabled("game"),"new row accepts its own positive response");gate.SetResult(new(false,false));await Task.Delay(20);Check(p.Enabled("game"),"old response cannot overwrite replacement row state");
 p=new();p.Error("old");p.Error("new");p.Drain();Check(p.Status.Revealed==1&&p.Status.Message=="new","only latest current error is revealed after layout");p.Error("inactive");p.active=false;p.Drain();Check(p.Status.Revealed==1,"late error reveal cannot pull an inactive page");p.active=true;p.Error("closed");p.disposed=true;p.Drain();Check(p.Status.Revealed==1,"closed page cannot reveal a queued error");
 Console.WriteLine($"PASS {n} actual deferred-card/load/error callback checks; no WinUI, process or network executed");
 }
}
'''
w=pathlib.Path(tempfile.mkdtemp(prefix='Playhub-Deferred-Card-Checks-'));(w/'Program.cs').write_text(code,encoding='utf-8');(w/'Checks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><NoWarn>CS0649</NoWarn></PropertyGroup></Project>',encoding='utf-8')
r=subprocess.run(['dotnet','run','--project',str(w/'Checks.csproj'),'-c','Release'],capture_output=True,text=True,encoding='utf-8',errors='replace');(w/'run.log').write_text(r.stdout+r.stderr,encoding='utf-8');print(r.stdout+r.stderr);print(json.dumps({'work':str(w),'productPage':str(page),'actualMethodsRehosted':['producer registration','InstalledCardLoaded','UpdateInstalledButtons','ReadInstalledCardAsync','ShowError']}));raise SystemExit(r.returncode)

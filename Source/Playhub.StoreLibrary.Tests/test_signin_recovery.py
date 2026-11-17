"""Compile the actual pure recovery type and NavigationCompleted body; never start WinUI."""
from pathlib import Path
import argparse
import re
import subprocess
import tempfile

SOURCE=Path(__file__).resolve().parents[1]/'Playhub/StoreLibrary/StoreSignInWindow.cs'
parser=argparse.ArgumentParser()
parser.add_argument('--before-handler',type=Path)
args=parser.parse_args()
source=SOURCE.read_text(encoding='utf-8')
state=source[source.index('internal enum StoreSignInOutcome'):]
handler_source=args.before_handler.read_text(encoding='utf-8') if args.before_handler else source
handler=re.search(r'core\.NavigationCompleted \+= .*?=>\s*(\{[^\n]*\}|[^;]+);',handler_source).group(1)
handler=handler[1:-1] if handler.startswith('{') else handler+';'
program=r'''
using System;
using Playhub.StoreLibrary;
int checks=0;
void Check(bool condition,string label){if(!condition)throw new Exception(label);checks++;}
DateTimeOffset now=DateTimeOffset.Parse("2026-11-14T12:00:00Z");
StoreSignInRecovery New(int seconds=300)=>new(now.AddSeconds(seconds),()=>now);
var host=new Host(New());host.recovery.StartNavigation();host.Complete(false,0);
Check(host.recovery.State==StoreSignInPageState.Failed&&host.errors==1,"actual failed-navigation handler exposes recovery");
Check(host.recovery.Result is null,"failure does not become cancel or authorization");
host.recovery.NavigationCompleted(true,200);Check(host.recovery.State==StoreSignInPageState.Failed,"late success cannot clear visible error");
Check(!host.recovery.Authorize("inert-code"),"late auth cannot recover failed view implicitly");
Check(host.recovery.Retry()&&host.recovery.Result?.Outcome==StoreSignInOutcome.RetryRequested,"only explicit retry requests fresh session");
Check(host.recovery.Result?.Code is null,"retry carries no credential");
Check(!host.recovery.Retry()&&!host.recovery.Cancel(),"retry is one-shot");
var http=new Host(New());http.recovery.StartNavigation();http.Complete(true,503);
Check(http.recovery.State==StoreSignInPageState.Failed&&http.errors==1,"HTTP error page exposes recovery even if transport succeeded");
var ready=new Host(New());ready.recovery.StartNavigation();ready.Complete(true,200);
Check(ready.recovery.State==StoreSignInPageState.Ready&&ready.errors==0,"normal form remains usable");
Check(ready.recovery.Authorize("inert-code")&&ready.recovery.Result?.Outcome==StoreSignInOutcome.Authorized,"real callback returns authorization");
Check(!ready.recovery.Cancel()&&ready.recovery.Result?.Code=="inert-code","late Closed event cannot overwrite callback");
var failed=New();failed.Fail();Check(failed.State==StoreSignInPageState.Failed&&failed.Result is null,"initialize failure stays visible without fake cancel");
var canceled=New();Check(canceled.Cancel()&&canceled.Result?.Outcome==StoreSignInOutcome.Canceled,"voluntary cancel distinct");
Check(!canceled.Retry()&&!canceled.Authorize("late"),"closed view cannot retry or authorize");
var loading=New();loading.StartNavigation();now=now.AddSeconds(30);loading.Tick();
Check(loading.State==StoreSignInPageState.Failed,"page loading is bounded to thirty seconds");
var init=New();now=now.AddSeconds(30);init.Tick();Check(init.State==StoreSignInPageState.Failed,"initialization deadline is bounded");
var redirects=New();redirects.StartNavigation();now=now.AddSeconds(29);redirects.StartNavigation();now=now.AddSeconds(1);redirects.Tick();
Check(redirects.State==StoreSignInPageState.Failed,"redirects cannot extend the original page-loading budget");
var expired=New(5);expired.StartNavigation();now=now.AddSeconds(5);expired.NavigationCompleted(true,200);
Check(expired.State==StoreSignInPageState.Expired&&expired.Result is null,"expiry is visible and never cancel or success");
Check(!expired.StartNavigation()&&!expired.Authorize("late"),"original expired session cannot navigate or authorize");
Check(expired.Retry()&&expired.Result?.Outcome==StoreSignInOutcome.RetryRequested,"expired view requires explicit new-session retry");
var closed=New();closed.Cancel();closed.Fail();closed.NavigationCompleted(false,500);closed.Tick();
Check(closed.State==StoreSignInPageState.Finished&&closed.Result?.Outcome==StoreSignInOutcome.Canceled,"late receipt never resurrects closed view");
Console.WriteLine($"SignInRecovery {checks} assertions PASS");
class Element{public int Visibility;}
class Visibility{public const int Collapsed=0;}
record Event(bool IsSuccess,int HttpStatusCode);
class Host(StoreSignInRecovery state){
 public StoreSignInRecovery recovery=state;public Element loading=new();public int errors;
 void RenderFailure(){if(recovery.State is StoreSignInPageState.Failed or StoreSignInPageState.Expired)errors++;}
 public void Complete(bool success,int code){var e=new Event(success,code); /* HANDLER */}
}
'''.replace('/* HANDLER */',handler)
work=Path(tempfile.mkdtemp(prefix='Playhub-SignIn-Recovery-Checks-'))
(work/'State.cs').write_text('using System;\nnamespace Playhub.StoreLibrary;\n'+state,encoding='utf-8')
(work/'Program.cs').write_text(program,encoding='utf-8')
(work/'Checks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>')
build=subprocess.run(['dotnet','build','Checks.csproj','-c','Release','--nologo'],cwd=work,capture_output=True,text=True)
(work/'build.log').write_text(build.stdout+build.stderr)
if build.returncode:raise SystemExit(build.stdout+build.stderr)
run=subprocess.run(['dotnet',str(work/'bin/Release/net8.0/Checks.dll')],cwd=work,capture_output=True,text=True)
(work/'run.log').write_text(run.stdout+run.stderr)
print(run.stdout+run.stderr,end='');print('Evidence:',work)
raise SystemExit(run.returncode)

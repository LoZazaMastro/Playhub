# SPDX-License-Identifier: MIT
from pathlib import Path
import shutil,subprocess,tempfile,json,sys,os
here=Path(__file__).resolve().parent;source=here.parents[1]
w=Path(tempfile.mkdtemp(prefix='Playhub-Store-UI-Focus-'));ui=w/'ui';ui.mkdir()
shutil.copytree(source/'Playhub/StoreLibrary',ui/'StoreLibrary')
# The independent auth-only process entry point is outside this inert page
# harness; compiling it would pull real host/account service dependencies in.
(ui/'StoreLibrary/StoreSignInCommand.cs').unlink(missing_ok=True)
if '--before' in sys.argv:
 before=Path(sys.argv[sys.argv.index('--before')+1])
 for n in ['StoreLibraryPage.xaml','StoreLibraryPage.xaml.cs']:shutil.copyfile(before/n,ui/'StoreLibrary'/n)
shutil.copyfile(source/'Shared/StoreLibraryModels.cs',ui/'StoreLibraryModels.cs')
for model in ['StoreXboxCatalogModels.cs','StoreXboxAccountModels.cs','StoreInstalledGameModels.cs']:shutil.copyfile(source/'Shared'/model,ui/model)
(ui/'ReviewServices.cs').write_text('namespace Playhub.Services; internal static class AppPaths { internal static string LocalDataRoot => System.IO.Path.Combine(System.IO.Path.GetTempPath(),"Playhub-Store-Review-Never-Authenticated"); }',encoding='utf-8')
for n in ['GameCardViewport.cs','GameCardLayout.cs','GameCardDensityControl.cs']:shutil.copyfile(source/'Playhub/Importing'/n,ui/n)
(ui/'App.xaml').write_text((source/'Playhub/App.xaml').read_text(encoding='utf-8').replace('x:Class="Playhub.App"','x:Class="Playhub.CheckApp"'),encoding='utf-8')
shutil.copyfile(here/'ReviewApp.cs',ui/'App.xaml.cs')
words={}
for lang in 'en it es fr de pt uk zh ja ko hi ru'.split():words[lang]={k:v for k,v in json.loads((source/'Playhub/Assets/Localization'/f'{lang}.json').read_text(encoding='utf-8-sig')).items()if k.startswith('StoreLibrary.')}
(ui/'review-locales.json').write_text(json.dumps(words,ensure_ascii=False),encoding='utf-8')
for n in ['Epic.png','Gog.png','Xbox.png','Amazon.png']:
 p=ui/'Assets/ServiceLogos'/n;p.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(source/'Playhub/Assets/ServiceLogos'/n,p)
(ui/'Ui.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>WinExe</OutputType><TargetFramework>net8.0-windows10.0.19041.0</TargetFramework><TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion><RootNamespace>Playhub</RootNamespace><Platforms>x64</Platforms><Platform>x64</Platform><RuntimeIdentifier>win-x64</RuntimeIdentifier><UseWinUI>true</UseWinUI><WindowsPackageType>None</WindowsPackageType><WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.WindowsAppSDK" Version="1.6.250205002"/><Content Include="review-locales.json;Assets/ServiceLogos/*.png" CopyToOutputDirectory="PreserveNewest"/></ItemGroup></Project>''')
build=subprocess.run([r'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe',str(ui/'Ui.csproj'),'/restore','/t:Build','/p:Configuration=Release','/p:Platform=x64','/v:minimal','/nologo'],capture_output=True,text=True,encoding='utf-8');(w/'build.log').write_text(build.stdout+build.stderr,encoding='utf-8');assert build.returncode==0,build.stdout+build.stderr
if '--private-desktop' not in sys.argv:print(json.dumps({'work':str(w),'compiledActualWinUI':True,'UIExecuted':False}));sys.exit(0)
output=w/'screens';output.mkdir();exe=ui/'bin/x64/Release/net8.0-windows10.0.19041.0/win-x64/Ui.exe'
environment=os.environ.copy()
if '--focus-only'in sys.argv:environment['PLAYHUB_STORE_REVIEW_FOCUS_ONLY']='1'
run=subprocess.run(['powershell.exe','-NoProfile','-NonInteractive','-File',str(here/'StartPrivate.ps1'),'-Executable',str(exe),'-Output',str(output)],capture_output=True,text=True,encoding='utf-8',env=environment,timeout=55);(w/'desktop.log').write_text(run.stdout+run.stderr,encoding='utf-8');assert run.returncode==0,run.stdout+run.stderr
assert not(output/'error.txt').exists(),(output/'error.txt').read_text(encoding='utf-8')
rows=json.loads((output/'review.json').read_text());focus=next(x for x in rows if x.get('kind')=='poll-focus')
okay=focus['accountRetained']and focus['rowRetained']and focus['timerPolled']and focus['automaticRetained']and focus['progressAdvanced']and focus['ownerReplaced']
expected='--expect-before-failure'in sys.argv
assert okay!=expected,focus
artwork=next(x for x in rows if x.get('kind')=='artwork-refresh')
assert (artwork['repeaterRetained']and artwork['focusRetained']and artwork['imagesUpdated']and artwork['scrollRetained'])!=expected,artwork
eligibility=next(x for x in rows if x.get('kind')=='installed-eligibility')
assert (eligibility['blocked']and eligibility['unblocked']and eligibility['retained']and eligibility['statusReread'])!=expected,eligibility
print(json.dumps({'work':str(w),'focus':focus,'expectedBeforeFailure':expected,'privateDesktop':run.stdout.strip(),'noAccountProviderDownloadCalls':True},indent=2))

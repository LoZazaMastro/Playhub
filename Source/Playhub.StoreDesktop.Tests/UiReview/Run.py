# SPDX-License-Identifier: MIT
from pathlib import Path
import json,shutil,subprocess,tempfile,sys
here=Path(__file__).resolve().parent; source=here.parents[1]
work=Path(tempfile.mkdtemp(prefix='Playhub-Desktop-Auth-Migration-'));ui=work/'ui';ui.mkdir()
files=['Playhub/StoreLibrary/StoreAccountsPage.cs','Playhub/StoreLibrary/StoreLibraryController.cs','Playhub/StoreLibrary/StoreAccountControllers.cs','Playhub/StoreLibrary/StoreLibraryPresentation.cs','Playhub/StoreLibrary/StoreDesktopCopy.cs','Playhub/StoreLibrary/StoreMigrationController.cs','Playhub/StoreLibrary/StoreMigrationAnnouncements.cs','Playhub/StoreLibrary/StoreXboxSignInController.cs','Playhub/Services/LocalizationService.cs','Shared/StoreLibraryModels.cs','Shared/StoreSteamSyncModels.cs','Shared/StoreXboxAccountModels.cs','Shared/StoreXboxCatalogModels.cs']
for name in files:shutil.copyfile(source/name,ui/Path(name).name)
for name,target in [('ReviewApp.cs','App.xaml.cs'),('Fixtures.cs','Fixtures.cs')]:shutil.copyfile(here/name,ui/target)
(ui/'App.xaml').write_text((source/'Playhub/App.xaml').read_text(encoding='utf-8-sig').replace('x:Class="Playhub.App"','x:Class="Playhub.CheckApp"'),encoding='utf-8')
for lang in 'en it es fr de pt uk zh ja ko hi ru'.split():
 p=ui/'Assets/Localization'/f'{lang}.json';p.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(source/'Playhub/Assets/Localization'/f'{lang}.json',p)
for logo in ['Epic','Gog','Xbox','Amazon']:
 p=ui/'Assets/ServiceLogos'/f'{logo}.png';p.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(source/'Playhub/Assets/ServiceLogos'/f'{logo}.png',p)
(ui/'Ui.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>WinExe</OutputType><TargetFramework>net8.0-windows10.0.19041.0</TargetFramework><TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion><RootNamespace>Playhub</RootNamespace><Platforms>x64</Platforms><Platform>x64</Platform><RuntimeIdentifier>win-x64</RuntimeIdentifier><UseWinUI>true</UseWinUI><WindowsPackageType>None</WindowsPackageType><WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.WindowsAppSDK" Version="1.6.250205002"/><Content Include="Assets/**/*.json;Assets/ServiceLogos/*.png" CopyToOutputDirectory="PreserveNewest"/></ItemGroup></Project>''',encoding='utf-8')
build=subprocess.run([r'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe',str(ui/'Ui.csproj'),'/restore','/t:Build','/p:Configuration=Release','/p:Platform=x64','/v:minimal','/nologo'],capture_output=True,text=True,encoding='utf-8');(work/'build.log').write_text(build.stdout+build.stderr,encoding='utf-8');assert build.returncode==0,build.stdout+build.stderr
if '--private-desktop' not in sys.argv:print(json.dumps({'work':str(work),'compiledActualWinUI':True,'UIExecuted':False}));sys.exit(0)
screens=work/'screens';screens.mkdir();exe=ui/'bin/x64/Release/net8.0-windows10.0.19041.0/win-x64/Ui.exe'
run=subprocess.run(['powershell.exe','-NoProfile','-NonInteractive','-File',str(source/'Playhub.StoreLibrary.Tests/UiReview/StartPrivate.ps1'),'-Executable',str(exe),'-Output',str(screens)],capture_output=True,text=True,encoding='utf-8',timeout=55);(work/'private-desktop.log').write_text(run.stdout+run.stderr,encoding='utf-8');assert run.returncode==0,run.stdout+run.stderr
assert not(screens/'error.txt').exists(),(screens/'error.txt').read_text(encoding='utf-8')
result=json.loads((screens/'result.json').read_text());assert result['passed'] and result['checks']>=32 and result['gameMutations']==0 and 'inputBefore=Default inputAfter=Default childExit=0 timeout=False' in run.stdout
print(json.dumps({'work':str(work),'result':result,'desktopProof':run.stdout.strip()},indent=2))

"""Actual settings methods/models with inert concurrent config storage only."""
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

SOURCE = Path(__file__).parents[2]
PROGRAM = r'''
using GamingMode.Models;using GamingMode.Services;using System.Text.Json;
int count=0;void Check(bool ok,string name){if(!ok)throw new Exception(name);++count;}
var store=new JsonStore();
Check(DashboardApi.ReadSettings(store).SteamControllerTranslationEnabled,"actual legacy model default");
var disabled=DashboardApi.WriteSettings(store,null,null,steamControllerTranslationEnabled:false);
Check(!disabled.SteamControllerTranslationEnabled,"actual disabled readback");
string first=store.Config.Gaming.SteamControllerTranslationRevision;
Check(Guid.TryParseExact(first,"N",out _),"revocation revision persisted");
Check(store.Config.Gaming.DashboardHotkey=="Ctrl+Alt+P","unrelated settings preserved");
DashboardApi.WriteSettings(store,null,null,steamControllerTranslationEnabled:false);
Check(store.Config.Gaming.SteamControllerTranslationRevision==first,"idempotent write does not restart");
DashboardApi.WriteSettings(store,null,null,navigationHapticsEnabled:true);
Check(!store.Config.Gaming.SteamControllerTranslationEnabled&&store.Config.Gaming.SteamControllerTranslationRevision==first,"haptics preserves translation");
DashboardApi.WriteSettings(store,null,null,steamControllerTranslationEnabled:true);
Check(store.Config.Gaming.SteamControllerTranslationEnabled&&store.Config.Gaming.SteamControllerTranslationRevision!=first,"rapid re-enable revokes old run");
var options=JsonStore.Options;
var restored=JsonSerializer.Deserialize<ModeConfig>(JsonSerializer.Serialize(store.Config,options),options)!;
Check(restored.Gaming.SteamControllerTranslationEnabled&&restored.Gaming.SteamControllerTranslationRevision==store.Config.Gaming.SteamControllerTranslationRevision,"actual Agent model roundtrip");
var app=JsonSerializer.Deserialize<Playhub.Models.GamingModeConfig>(JsonSerializer.Serialize(store.Config,options),options)!;
var preserved=JsonSerializer.Deserialize<ModeConfig>(JsonSerializer.Serialize(app,options),options)!;
Check(preserved.Gaming.SteamControllerTranslationEnabled&&preserved.Gaming.SteamControllerTranslationRevision==store.Config.Gaming.SteamControllerTranslationRevision,"App extension data preserves preference");

var concurrent=new JsonStore{PauseFirst=true};
var translation=Task.Run(()=>DashboardApi.WriteSettings(concurrent,null,null,steamControllerTranslationEnabled:false));
if(!concurrent.Entered.Wait(3000))throw new Exception("first snapshot never entered");
var haptics=Task.Run(()=>DashboardApi.WriteSettings(concurrent,null,null,navigationHapticsEnabled:true));
bool overlapped=concurrent.OtherLoad.Wait(250);
concurrent.Release.Set();
if(!Task.WaitAll([translation,haptics],3000))throw new Exception("writer did not drain");
Console.WriteLine($"parallel snapshot overlap={overlapped}, translation={concurrent.Config.Gaming.SteamControllerTranslationEnabled}, haptics={concurrent.Config.Gaming.NavigationHapticsEnabled}");
Check(!overlapped,"parallel settings snapshot writes are serialized");
Check(!concurrent.Config.Gaming.SteamControllerTranslationEnabled&&concurrent.Config.Gaming.NavigationHapticsEnabled,"both concurrent user preferences survive");
Console.WriteLine("PASS "+count+" actual Dashboard settings checks; inert storage/no process or controller API");
'''
STORE = r'''
using GamingMode.Models;using System.Text.Json;using System.Text.Json.Serialization;
namespace GamingMode.Services;
internal sealed class JsonStore {
 internal static readonly JsonSerializerOptions Options=new(){PropertyNameCaseInsensitive=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase,Converters={new JsonStringEnumConverter()}};
 internal ModeConfig Config=new();internal bool PauseFirst;int reads;readonly object gate=new();
 internal readonly ManualResetEventSlim Entered=new(),Release=new(),OtherLoad=new();
 public ModeConfig LoadConfig(){
  ModeConfig snapshot;lock(gate)snapshot=JsonSerializer.Deserialize<ModeConfig>(JsonSerializer.Serialize(Config,Options),Options)!;
  if(Interlocked.Increment(ref reads)==1&&PauseFirst){Entered.Set();if(!Release.Wait(3000))throw new Exception("test release timed out");}
  else if(PauseFirst)OtherLoad.Set();return snapshot;
 }
 public void SaveConfig(ModeConfig config){lock(gate)Config=JsonSerializer.Deserialize<ModeConfig>(JsonSerializer.Serialize(config,Options),Options)!;}
}
'''


class TranslationSettings(unittest.TestCase):
    def run_actual(self, before=False):
        with tempfile.TemporaryDirectory(prefix='Playhub-Translation-Settings-') as folder:
            root = Path(folder)
            text = (SOURCE/'GamingModeAgent/GamingMode.Services/DashboardApi.cs').read_text(encoding='utf-8-sig')
            start = text.index('public sealed record DashboardSettings(')
            text = text[start:text.index('// ---------- CONTATORI DI SISTEMA',start)]
            if before:
                self.assertEqual(text.count('lock (DashboardSettingsWriteGate)'),1)
                text = text.replace('lock (DashboardSettingsWriteGate)','if (true)')
            (root/'ActualSettings.cs').write_text('using GamingMode.Models; namespace GamingMode.Services; static class DashboardApi {'+text+'}',encoding='utf-8')
            for model in (SOURCE/'GamingModeAgent/GamingMode.Models').glob('*.cs'):
                shutil.copy2(model,root/model.name)
            shutil.copy2(SOURCE/'Playhub/Models/GamingModeConfig.cs',root/'AppConfigMirror.cs')
            (root/'Program.cs').write_text(PROGRAM,encoding='utf-8')
            (root/'JsonStore.cs').write_text(STORE,encoding='utf-8')
            (root/'Checks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>',encoding='utf-8')
            return subprocess.run(['dotnet','run','--project',str(root/'Checks.csproj'),'-c','Release'],capture_output=True,text=True,encoding='utf-8',timeout=90)

    def test_parent_parallel_load_save_loses_a_user_setting(self):
        result = self.run_actual(before=True)
        self.assertNotEqual(result.returncode,0,result.stdout)
        self.assertIn('parallel settings snapshot writes are serialized',result.stderr)
        self.assertIn('translation=False, haptics=False',result.stdout)

    def test_actual_read_write_models_and_concurrent_user_settings(self):
        result = self.run_actual()
        self.assertEqual(result.returncode,0,result.stdout+result.stderr)
        self.assertIn('PASS 11 actual Dashboard settings checks',result.stdout)
        self.assertIn('translation=False, haptics=True',result.stdout)


if __name__ == '__main__': unittest.main()

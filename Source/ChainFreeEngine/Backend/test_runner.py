import json
import pathlib
import subprocess
import sys
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parent
RUNNER = ROOT / "runner.py"
FAKE = '''
import decky, json
from pathlib import Path
print("plugin diagnostic must go to stderr")
class Plugin:
 def __init__(self): self.path=Path(decky.DECKY_PLUGIN_SETTINGS_DIR)/"preferences.json"
 async def _main(self): raise RuntimeError("hardware lifecycle must never run")
 async def _unload(self): raise RuntimeError("hardware lifecycle must never run")
 async def get_panel_preferences(self): return json.loads(self.path.read_text()) if self.path.exists() else {"fixture":True}
 async def get_qam_preferences(self): return {"selected":["Decky"]}
 async def get_home_news_settings(self): return {"enabled":False}
 async def get_daily_history_settings(self): return {"enabled":True}
 async def save_panel_preferences(self, preferences): self.path.write_text(json.dumps(preferences)); return preferences
 async def set_display_mode(self, request): raise RuntimeError("must be excluded")
'''

class RunnerChecks(unittest.TestCase):
 def setUp(self):
  fixtures=ROOT/"fixtures"; fixtures.mkdir(exist_ok=True)
  self.temp=tempfile.TemporaryDirectory(dir=fixtures)
  self.root=pathlib.Path(self.temp.name); self.plugin=self.root/"Plugin"; self.plugin.mkdir()
  (self.plugin/"main.py").write_text(FAKE)
  self.settings=self.root/".local"/"session"/"settings"
  self.processes=[]
 def tearDown(self):
  for p in self.processes:
   if p.poll() is None: p.stdin.close(); p.wait(timeout=5)
   elif not p.stdin.closed: p.stdin.close()
   p.stdout.close(); p.stderr.close()
  self.temp.cleanup()
 def start(self, writes=False, settings=None):
  command=[sys.executable,"-I",str(RUNNER),"--workspace",str(self.root),"--plugin-root",str(self.plugin),"--settings-dir",str(settings or self.settings),"--instance-id","fixture-instance"]
  if writes: command.append("--allow-isolated-writes")
  p=subprocess.Popen(command,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,encoding="utf-8")
  self.processes.append(p); return p
 def rpc(self,p,method,args=None):
  p.stdin.write(json.dumps({"id":1,"method":method,"args":args})+"\n");p.stdin.flush()
  return json.loads(p.stdout.readline())
 def test_status_and_read(self):
  p=self.start(); status=self.rpc(p,"host.status")["result"]
  self.assertTrue(status["backendReady"]);self.assertFalse(status["writerActive"])
  self.assertFalse(status["lifecycleStarted"]);self.assertFalse(status["hardwareEnabled"])
  self.assertEqual(self.rpc(p,"get_panel_preferences")["result"],{"fixture":True})
  self.assertTrue(self.rpc(p,"host.shutdown")["ok"]);self.assertEqual(p.wait(timeout=5),0)
  self.assertIn("plugin diagnostic",p.stderr.read())
 def test_allowlist_and_write_default(self):
  p=self.start()
  for method in ("_main","_unload","set_display_mode","__dict__"):
   self.assertEqual(self.rpc(p,method)["error"]["code"],"method_not_allowed")
  self.assertEqual(self.rpc(p,"save_panel_preferences",[{"v":1}])["error"]["code"],"write_disabled")
  self.assertFalse((self.settings/"preferences.json").exists())
 def test_isolated_write(self):
  p=self.start(True); value={"unknownFutureSetting":"preserved","enabled":True}
  self.assertTrue(self.rpc(p,"save_panel_preferences",[value])["ok"])
  self.assertEqual(self.rpc(p,"get_panel_preferences")["result"],value)
 def test_second_owner_rejected_and_restart_releases(self):
  p=self.start();self.assertTrue(self.rpc(p,"host.status")["ok"])
  second=self.start();self.assertEqual(second.wait(timeout=5),2)
  self.assertIn("settings_owner_exists",second.stderr.read())
  self.rpc(p,"host.shutdown");p.wait(timeout=5)
  third=self.start();self.assertTrue(self.rpc(third,"host.status")["ok"])
 def test_external_settings_rejected(self):
  p=self.start(settings=self.root.parent/"outside")
  self.assertEqual(p.wait(timeout=5),2)
  self.assertIn("path_outside_workspace",p.stderr.read())
 def test_invalid_request_does_not_poison_channel(self):
  p=self.start();p.stdin.write("not-json\n");p.stdin.flush()
  self.assertFalse(json.loads(p.stdout.readline())["ok"])
  self.assertTrue(self.rpc(p,"host.status")["ok"])
 def test_import_does_not_make_pycache(self):
  p=self.start();self.rpc(p,"host.shutdown");p.wait(timeout=5)
  self.assertFalse((self.plugin/"__pycache__").exists())

if __name__=="__main__":unittest.main(verbosity=2)

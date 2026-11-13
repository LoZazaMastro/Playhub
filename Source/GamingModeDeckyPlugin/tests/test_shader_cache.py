import importlib.util
import json
import os
from pathlib import Path
import tempfile
import subprocess
import unittest
from unittest.mock import Mock,patch

spec=importlib.util.spec_from_file_location("shader_cache_test",Path(__file__).resolve().parents[1]/"quick_settings"/"shader_cache.py")
cache=importlib.util.module_from_spec(spec);spec.loader.exec_module(cache)
STATE={"Enabled":True,"Limit":16384,"DefaultLimit":16384,"EnableDefault":True,"LimitDefault":True}
class CacheTests(unittest.TestCase):
 def setUp(self):
  self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup);self.root=Path(self.temp.name)
  self.preflight=Mock();self.preflight.get.return_value={"adapters":[{"vendor":"nvidia"}],"game":{}}
  self.preflight.cache_paths.return_value=()
  self.driver=Mock(return_value={"available":True,"status":"ready","state":STATE})
  self.manager=cache.ShaderCacheManager(self.preflight,Mock(),lambda:"powershell.exe",lambda:"",self.driver,str(self.root))
 def test_inventory_counts_only_real_files_and_no_paths_escape_to_ui(self):
  d=self.root/"D3DSCache";d.mkdir();(d/"first.bin").write_bytes(b"shader")
  (d/"nested").mkdir();(d/"nested"/"second.bin").write_bytes(b"cache")
  r=self.manager.get();row=r["caches"][0]
  self.assertEqual((row["files"],row["bytes"],row["state"]),(2,11,"ready"))
  self.assertNotIn(str(self.root),json.dumps(r));self.assertNotIn("first.bin",json.dumps(r))
  self.driver.assert_called_once()
 def test_missing_and_deadline_are_distinct(self):
  self.assertEqual(cache.measure(self.root/"missing",999999999)["state"],"missing")
  self.assertEqual(cache.measure(self.root,0)["state"],"partial")
 def test_file_budget_is_bounded(self):
  for n in range(10):(self.root/str(n)).touch()
  r=cache.measure(self.root,999999999,limit=3)
  self.assertEqual(r["state"],"partial");self.assertLessEqual(r["files"],3)
 def test_reparse_ancestor_is_refused_before_scan(self):
  with patch.object(cache.Path,"lstat",return_value=type("S",(),{"st_mode":0,"st_file_attributes":0x400})()),patch.object(cache.os,"scandir") as scan:
   self.assertEqual(cache.measure(self.root,999999999)["state"],"unavailable");scan.assert_not_called()
 def test_per_game_path_requires_valid_uint_and_verified_manifest_callback(self):
  for value in [0,-1,True,"123",2**32]:self.manager.get(value)
  self.preflight.cache_paths.assert_not_called()
  self.manager.get(123);self.preflight.cache_paths.assert_called_once_with(123)
 def test_other_gpu_never_loads_nvidia(self):
  self.preflight.get.return_value={"adapters":[{"vendor":"intel"}]}
  r=self.manager.get();self.assertFalse(r["nvidia"]["available"]);self.driver.assert_not_called()
 def test_set_consumes_exact_proof_and_does_not_trust_client_expected_state(self):
  r=self.manager.get();proof=r["nvidia"]["token"];self.driver.reset_mock()
  request={"token":proof,"enabled":True,"limit":4096,"expected":{"Limit":1}}
  self.manager.set_nvidia(request)
  self.assertEqual(self.driver.call_args.args[2],STATE)
  self.assertEqual(self.driver.call_args.args[3:],(True,4096))
  self.driver.reset_mock();self.assertEqual(self.manager.set_nvidia(request)["status"],"changed");self.driver.assert_not_called()
 def test_invalid_and_expired_proofs_never_write(self):
  proof=self.manager.get()["nvidia"]["token"];self.driver.reset_mock()
  for request in [{"token":proof,"enabled":True,"limit":True},{"token":proof,"enabled":"true","limit":4096},{"token":"Ã©"*32,"enabled":True,"limit":4096},{"token":"x"*10000,"enabled":True,"limit":4096}]:
   self.assertIn(self.manager.set_nvidia(request)["status"],["invalid","changed"])
  self.manager._issued-=121
  self.assertEqual(self.manager.set_nvidia({"token":proof,"enabled":False,"limit":"default"})["status"],"changed")
  self.driver.assert_not_called()
 def test_operation_is_single_flight(self):
  self.manager._lock.acquire()
  try:self.assertEqual(self.manager.get()["status"],"busy")
  finally:self.manager._lock.release()
 def test_bad_driver_payload_never_creates_writable_control(self):
  self.driver.return_value={"available":True,"state":{**STATE,"Limit":True}}
  self.assertFalse(self.manager.get()["nvidia"]["available"])
 def test_powershell_arguments_are_fixed_encoded_and_bounded(self):
  run=Mock(return_value=(0,json.dumps({"available":False,"status":"unavailable"}),""))
  result=cache._nvapi.invoke(run,lambda:"safe-powershell",STATE,True,4096)
  self.assertEqual(result["status"],"unavailable")
  args=run.call_args.args[0];self.assertEqual(args[:4],["safe-powershell","-NoProfile","-NonInteractive","-EncodedCommand"])
  self.assertEqual(run.call_args.kwargs["timeout"],12)
  self.assertLess(len(args[-1]),30000)

 @unittest.skipUnless(os.name=="nt","Windows C# fixture")
 def test_native_transaction_code_with_owned_fake_driver(self):
  with tempfile.TemporaryDirectory() as tmp:
   root=Path(tmp)
   (root/"native.cs").write_text(cache._nvapi.NATIVE+"\n"+Path(__file__).with_name("shader_cache_native_fixture.cs").read_text())
   script=root/"run.ps1"
   script.write_text("$ErrorActionPreference='Stop';Add-Type -TypeDefinition (Get-Content (Join-Path $PSScriptRoot 'native.cs') -Raw);[Fixture]::Run()")
   result=subprocess.run([str(Path(os.environ["SystemRoot"])/"System32"/"WindowsPowerShell"/"v1.0"/"powershell.exe"),"-NoProfile","-NonInteractive","-File",str(script)],capture_output=True,text=True,timeout=12)
   self.assertEqual(result.returncode,0,result.stdout+result.stderr)
   self.assertIn("Native fixtures: 10",result.stdout)

 def test_custom_current_limit_can_be_retained_when_toggling_but_not_forged(self):
  custom={**STATE,"Limit":3072,"LimitDefault":False}
  self.driver.return_value={"available":True,"status":"ready","state":custom}
  proof=self.manager.get()["nvidia"]["token"];self.driver.reset_mock()
  self.assertEqual(self.manager.set_nvidia({"token":proof,"enabled":False,"limit":3073})["status"],"invalid")
  self.driver.assert_not_called()
  self.manager.set_nvidia({"token":proof,"enabled":False,"limit":3072})
  self.assertEqual(self.driver.call_args.args[3:],(False,3072))
  run=Mock(return_value=(0,'{}',''))
  cache._nvapi.invoke(run,lambda:"powershell",custom,False,3072);run.assert_called_once()
  run.reset_mock()
  self.assertEqual(cache._nvapi.invoke(run,lambda:"powershell",custom,False,3073)["status"],"invalid");run.assert_not_called()

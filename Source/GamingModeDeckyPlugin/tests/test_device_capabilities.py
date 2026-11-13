import importlib.util
from pathlib import Path
import threading
import unittest
from unittest.mock import Mock

ROOT=Path(__file__).resolve().parents[1]
def load(name):
 spec=importlib.util.spec_from_file_location(name,ROOT/"quick_settings"/(name+".py"));m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m);return m
cap=load("device_capabilities");profiles=load("device_profile");asus=load("asus_performance_commands");msi=load("msi_claw_firmware");lenovo=load("lenovo_power_mode")
def profile(model="pc",detected=None,**extra):
 return {"selection":model,"effective":model,"detected":detected or model,"power":{"available":True,"battery":False},"display":{"width":1920,"height":1080},"powerModeAvailable":True,**extra}
def proof(provider,**values):return {"available":True,"ok":True,"provider":provider,"token":"a"*64,**values}
def rows(report):return {r["id"]:r for r in report["controls"]}
class DeviceCapabilityTests(unittest.TestCase):
 def manager(self,p,identity,providers,timeout=.5):
  obj=cap.DeviceCapabilities(lambda:p,lambda:identity,providers,timeout);self.addCleanup(obj.close);return obj
 def test_all_selector_profiles_have_an_exact_documented_coverage_row(self):
  doc=cap.support_document(profiles._HANDHELDS)
  self.assertEqual((ROOT/"docs"/"HANDHELD-SUPPORT.md").read_text(),doc)
  self.assertEqual(len(profiles._HANDHELDS),53)
  for key,name,_,_ in profiles._HANDHELDS:self.assertIn("| "+name+" |",doc)
  self.assertEqual(sum(bool(cap.expected_controls(k)) for k,_,_,_ in profiles._HANDHELDS),17)
 def test_both_gpd_v1_profiles_use_native_config_proof_and_their_own_firmware(self):
  for model,board,version in (("gpd-win-4","G1618-04",[4,7]),("gpd-win-mini","G1617-01",[5,4])):
   self.assertEqual(cap.expected_controls(model),("vibration",))
   identity={"manufacturer":"GPD","model":board,"board_model":board}
   result=proof("gpd-v1-hid",profile=model,readbackKind="gpd-v1-config128-checksum-double-read",
                state={"rumble":"low"},firmware={"k":version},writeAvailable=True,recoveryRequired=False)
   native=Mock(return_value=result)
   manager=self.manager(profile(model),identity,{"gpd-v1":(cap.gpd_v1_identity,native)})
   row=rows(manager.get())["vibration"]
   self.assertTrue(row["available"]);self.assertEqual(row["status"],"readback")
   self.assertEqual(row["destination"],"device");native.assert_called_once()
   for invalid in ({**result,"firmware":{"k":[9,9]}}, {**result,"readbackKind":"saved-setting"},
                   {**result,"profile":"gpd-win-5"}):
    self.assertFalse(cap._control("vibration","gpd-v1",invalid,identity,model)["available"])
  self.assertFalse(cap.expected_controls("gpd-win-5"))
  self.assertFalse(cap.gpd_v1_identity({"manufacturer":"GPD","model":"G1617-02","board_model":"G1617-02"}))
 def test_manual_selection_never_probes_different_physical_hardware(self):
  native=Mock();m=self.manager(profile("rog-ally","pc"),{"manufacturer":"Fixture","model":"PC"},{"asus":(lambda _:True,native)})
  r=m.get("rog-ally");self.assertTrue(r["mismatch"]);self.assertEqual(rows(r)["presets"]["status"],"hardware-required");native.assert_not_called()
 def test_forged_matching_profile_still_requires_actual_provider_identity(self):
  native=Mock();m=self.manager(profile("rog-ally"),{"manufacturer":"MSI","model":"MS-1T91"},{"asus":(asus.supports_identity,native)})
  self.assertFalse(rows(m.get())["presets"]["available"]);native.assert_not_called()
 def test_rog_commands_and_fan_reads_are_independent_and_never_claim_current_preset(self):
  a=Mock(return_value=proof("asus-ally",commands=[2,0,1],acknowledged=False));f=Mock(return_value=proof("asus-ally",capabilities={"fan_rpm":True},fans={"cpu_rpm":2200,"gpu_rpm":None}))
  m=self.manager(profile("rog-ally"),{"manufacturer":"ASUS","model":"RC71L"},{"asus":(asus.supports_identity,a),"asus-fan":(asus.supports_identity,f)})
  r=rows(m.get());self.assertEqual(r["presets"]["status"],"manual-commands");self.assertEqual(r["fans"]["values"],[2200,None]);self.assertTrue(r["fans"]["available"])
 def test_bad_provider_proof_or_rpm_is_not_available(self):
  for result in [proof("other",commands=[0,1,2]),proof("asus-ally",commands=[0,True,2]),proof("asus-ally",commands=[0,1,2],token="x")]:
   m=self.manager(profile("rog-ally"),{"manufacturer":"ASUS","model":"RC71L"},{"asus":(asus.supports_identity,lambda:result)})
   self.assertFalse(rows(m.get())["presets"]["available"])
  self.assertFalse(cap._control("fans","asus-fan",proof("asus-ally",capabilities={"fan_rpm":True},fans={"cpu_rpm":-1,"gpu_rpm":2300}))["available"])
 def test_msi_controls_require_readback_and_tdp_requires_manual_mode(self):
  result=proof("msi-claw-8-ex",mode=1,chargeLimit=80,tdpAvailable=True,pl1=17,pl2=19)
  native=Mock(return_value=result);m=self.manager(profile("msi-claw-8-ex"),{"manufacturer":"MSI","model":"MS-1T91"},{"msi":(msi.supports_identity,native)})
  r=rows(m.get());self.assertEqual(r["power-limits"]["status"],"manual-required");self.assertEqual(r["charge-limit"]["status"],"readback");native.assert_called_once()
  self.assertFalse(cap._control("power-limits","msi",{**result,"tdpAvailable":False})["available"])
 def test_legion_go_s_does_not_inherit_original_go_firmware(self):
  native=Mock();m=self.manager(profile("legion-go-s"),{"manufacturer":"LENOVO","model":"83L3"},{"lenovo":(lenovo.supports_identity,native)})
  r=m.get();self.assertEqual(r["coverage"],"oem");self.assertFalse(rows(r)["presets"]["available"]);self.assertFalse(rows(r)["power-limits"]["available"]);native.assert_not_called()
  r=cap._control("presets","lenovo",proof("lenovo-legion-go",mode=2,modes=[1,2,3],editable=True));self.assertTrue(r["available"]);self.assertEqual(r["status"],"readback")
 def test_lenovo_other_modes_survive_without_optional_tdp_provider(self):
  report=proof("lenovo-other-tdp",nativeUnit="W",readbackKind="firmware-register",mode=2,
               modeControlAvailable=True,manualRecoveryRequired=False,tdpAvailable=False,limitsWatts=None,boundsWatts=None)
  r=cap._control("presets","lenovo-other",report)
  self.assertTrue(r["available"]);self.assertEqual(r["status"],"readback")
  self.assertFalse(cap._control("power-limits","lenovo-other",report)["available"])
  self.assertEqual(cap._control("presets","lenovo-other",{**report,"manualRecoveryRequired":True})["status"],"read-only")
 def test_lenovo_other_reports_measured_limits_and_native_bounds(self):
  report=proof("lenovo-other-tdp",nativeUnit="W",readbackKind="firmware-register",mode=255,
               modeControlAvailable=True,manualRecoveryRequired=False,tdpAvailable=True,writable=True,
               limitsWatts={"spl":18,"sppt":22,"fppt":27},boundsWatts={key:{"minimum":5,"maximum":35,"step":1} for key in ("spl","sppt","fppt")})
  r=cap._control("power-limits","lenovo-other",report)
  self.assertTrue(r["available"]);self.assertEqual(r["values"],[18,22,27]);self.assertEqual(r["status"],"readback")
  self.assertFalse(cap._control("power-limits","lenovo-other",{**report,"nativeUnit":"mW"})["available"])
  self.assertFalse(cap._control("power-limits","lenovo-other",{**report,"limitsWatts":{"spl":27,"sppt":22,"fppt":18}})["available"])
  self.assertFalse(cap._control("power-limits","lenovo-other",{**report,"boundsWatts":{"spl":{"minimum":5,"maximum":35,"step":0}}})["available"])
 def test_onex_type_two_keeps_tachometer_independent(self):
  report=proof("onex-type2-fan",state={"mode":1,"pwm":129},telemetry={"fanRpm":3291,"cpuTemperatureC":54},manualRecoveryRequired=False,readbackKind="firmware-register")
  r=cap._control("fans","onex-fan",report)
  self.assertTrue(r["available"]);self.assertEqual(r["values"],[3291]);self.assertEqual(r["status"],"readback")
  self.assertEqual(cap._control("temperatures","onex-fan",report)["values"],[54,None])
  self.assertFalse(cap._control("temperatures","onex-fan",{**report,"telemetry":{"fanRpm":3291,"cpuTemperatureC":255}})["available"])
  self.assertNotIn("values",cap._control("fans","onex-fan",{**report,"telemetry":{"fanRpm":65535,"cpuTemperatureC":54}}))
  self.assertEqual(cap._control("fans","onex-fan",{**report,"manualRecoveryRequired":True})["status"],"read-only")
  native=Mock();m=self.manager(profile("onexplayer-x2-mini"),{"manufacturer":"ONE-NETBOOK","model":"ONEXPLAYER X2Mini PRO"},{"onex-fan":(lambda _:False,native)})
  self.assertFalse(rows(m.get())["fans"]["available"]);native.assert_not_called()
 def test_windows_controls_are_individually_reported_not_assumed(self):
  m=self.manager(profile(display=None,powerModeAvailable=False),{},{});r=rows(m.get())
  self.assertTrue(all(not v["available"] and v["status"]=="unavailable" for v in r.values()))
 def test_ayaneo3_vendor_readback_requires_exact_identity_modules_and_state(self):
  identity={"manufacturer":"AYANEO","model":"AYANEO 3"}
  report=proof("ayaneo3-hid",readbackKind="vendor-hid-status",writeAvailable=True,manualRecoveryRequired=False,
               state={"leftMode":"solid","leftRgb":"ff0011","rightMode":"pulse","rightRgb":"112233","vibration":"high"},modules={"left":2,"right":16})
  for control in ("rgb","vibration"):
   row=cap._control(control,"ayaneo3",report,identity,"ayaneo-3")
   self.assertTrue(row["available"]);self.assertEqual(row["destination"],"device")
   self.assertFalse(cap._control(control,"ayaneo3",report,{**identity,"model":"AB07"},"ayaneo-3")["available"])
   self.assertFalse(cap._control(control,"ayaneo3",{**report,"modules":{"left":2,"right":99}},identity,"ayaneo-3")["available"])
   self.assertFalse(cap._control(control,"ayaneo3",{**report,"state":{**report["state"],"vibration":"unknown"}},identity,"ayaneo-3")["available"])
   self.assertEqual(cap._control(control,"ayaneo3",{**report,"manualRecoveryRequired":True},identity,"ayaneo-3")["status"],"read-only")
  native=Mock();manager=self.manager(profile("ayaneo-3"),{"manufacturer":"AYANEO","model":"AB07"},{"ayaneo3":(cap.ayaneo3_identity,native)})
  self.assertFalse(rows(manager.get())["rgb"]["available"]);native.assert_not_called()
 def test_ayaneo3_identity_matches_native_strip_and_case_but_never_model_alias_or_wrong_types(self):
  for identity in ({"manufacturer":"AYANEO","model":"AYANEO 3"}, {"manufacturer":" ayaneo ","model":" ayaneo 3 "},
                   {"manufacturer":"AyAnEo","model":"aYaNeO 3"}):
   self.assertTrue(cap.ayaneo3_identity(identity))
  for identity in (None, [], "AYANEO", {}, {"manufacturer":True,"model":"AYANEO 3"},
                   {"manufacturer":"AYANEO","model":3}, {"manufacturer":"AYANEO","model":"AB07"},
                   {"manufacturer":"AYANEO","model":"AYANEO 3 OLED"}, {"manufacturer":"AYANEO2","model":"AYANEO 3"}):
   self.assertFalse(cap.ayaneo3_identity(identity))
 def test_cache_reuses_physical_probe_without_repeating_calls(self):
  native=Mock(return_value=proof("asus-ally",commands=[0,1,2]));m=self.manager(profile("rog-ally"),{"manufacturer":"ASUS","model":"RC71L"},{"asus":(asus.supports_identity,native)})
  m.get();m.get();native.assert_called_once()
 def test_pending_probe_is_bounded_singleflight_and_can_complete_later(self):
  gate=threading.Event();self.addCleanup(gate.set)
  def native():gate.wait(1);return proof("asus-ally",commands=[0,1,2])
  call=Mock(side_effect=native);m=self.manager(profile("rog-ally"),{"manufacturer":"ASUS","model":"RC71L"},{"asus":(asus.supports_identity,call)},.01)
  self.assertEqual(rows(m.get())["presets"]["status"],"pending");m.get();call.assert_called_once();gate.set()
  self.assertTrue(rows(m.get())["presets"]["available"])
 def test_unknown_or_stale_selection_cannot_invoke_providers(self):
  native=Mock();m=self.manager(profile("pc"),{}, {"asus":(lambda _:True,native)})
  self.assertEqual(m.get("rog-ally")["status"],"changed");native.assert_not_called()
 def test_probe_error_and_close_never_enable_controls(self):
  native=Mock(side_effect=OSError("fixture"));m=self.manager(profile("rog-ally"),{"manufacturer":"ASUS","model":"RC71L"},{"asus":(asus.supports_identity,native)})
  self.assertFalse(rows(m.get())["presets"]["available"]);m.close();self.assertFalse(rows(m.get())["presets"]["available"]);native.assert_called_once()

"""Four exact Ally models; temperature protocol all firmware calls are fake PowerShell functions."""
import base64
import copy
import json
import os
import pathlib
import subprocess
import tempfile
import unittest
import uuid
from unittest.mock import Mock
from quick_settings.asus_device_controls import AsusDeviceControls, _SCRIPT, supports_temperature_identity
from test_asus_performance_commands import _FAKE
from test_device_capabilities import cap, profile, proof, rows

IDENTITY = {"manufacturer": "ASUSTeK COMPUTER INC.", "model": "ROG Xbox Ally X RC73XA_RC73XA"}
REPORT = {"available": True, "ok": True, "code": "", "provider": "asus-ally", "token": "a" * 64,
          "capabilities": {"fan_rpm": True, "charge_limit": False, "temperature_c": True},
          "fans": {"cpu_rpm": 3500, "gpu_rpm": 0}, "temperatures": {"cpu_c": 51, "gpu_c": 39}}


class AsusTemperatureServiceTests(unittest.TestCase):
    def setUp(self):
        self.identity = Mock(return_value=IDENTITY)
        self.response = copy.deepcopy(REPORT)
        self.now = 10
        self.run = Mock(side_effect=lambda *a, **k: (0, json.dumps(self.response), ""))
        self.service = AsusDeviceControls(self.identity, self.run, lambda: "fake-powershell", windows=True,
                                          clock=lambda: self.now, wall_clock=lambda: 1000)

    def test_only_four_exact_physical_ally_models_have_temperature_probe_contract(self):
        for model in ("RC71L", "RC72LA", "RC73YA", "RC73XA", "ROG Ally RC71L_RC71L", "ROG Ally X RC72LA_RC72LA", "ROG Xbox Ally RC73YA_RC73YA", "ROG Xbox Ally X RC73XA_RC73XA"):
            self.assertTrue(supports_temperature_identity({"manufacturer": "ASUS", "model": model}))
        for model in ("RC74XA", "RC73XA-Other", "rog-xbox-ally-x", "RC71L-Other"):
            self.assertFalse(supports_temperature_identity({"manufacturer": "ASUS", "model": model}))
        self.assertFalse(supports_temperature_identity({"manufacturer": "Other", "model": "RC73XA"}))

    def test_temperature_units_partial_null_and_cache_are_real_and_independent(self):
        first = self.service.get()
        self.assertEqual(first["temperatures"], {"cpu_c": 51, "gpu_c": 39})
        first["temperatures"]["cpu_c"] = 99
        self.assertEqual(self.service.get()["temperatures"]["cpu_c"], 51)
        self.run.assert_called_once()
        self.now += 2
        self.response["temperatures"] = {"cpu_c": None, "gpu_c": 125}
        self.assertEqual(self.service.get()["temperatures"], {"cpu_c": None, "gpu_c": 125})

    def test_temperature_only_is_available_without_fabricating_stopped_fans(self):
        self.response["fans"] = {"cpu_rpm": None, "gpu_rpm": None}
        self.response["capabilities"]["fan_rpm"] = False
        result = self.service.get()
        self.assertTrue(result["available"])
        self.assertEqual(result["fans"], {"cpu_rpm": None, "gpu_rpm": None})
        self.assertTrue(result["capabilities"]["temperature_c"])

    def test_legacy_fans_remain_unchanged_without_temperature_keys(self):
        del self.response["temperatures"]
        del self.response["capabilities"]["temperature_c"]
        self.identity.return_value = {"manufacturer": "ASUS", "model": "RC71L"}
        result = self.service.get()
        self.assertTrue(result["available"])
        self.assertEqual(result["capabilities"], {"fan_rpm": True, "charge_limit": False})
        self.assertNotIn("temperatures", result)

    def test_missing_temperatures_remain_null_and_do_not_remove_valid_fans(self):
        self.response["temperatures"] = {"cpu_c": None, "gpu_c": None}
        self.response["capabilities"]["temperature_c"] = False
        result = self.service.get()
        self.assertTrue(result["available"])
        self.assertEqual(result["fans"], {"cpu_rpm": 3500, "gpu_rpm": 0})
        self.assertEqual(result["temperatures"], {"cpu_c": None, "gpu_c": None})
        self.assertFalse(result["capabilities"]["temperature_c"])

    def test_malformed_out_of_range_or_wrong_device_never_publish_temperature(self):
        for temperatures in ({"cpu_c": 0, "gpu_c": 39}, {"cpu_c": 126, "gpu_c": None},
                             {"cpu_c": True, "gpu_c": 39}, {"cpu_c": 51.5, "gpu_c": 39},
                             {"cpu_c": "51", "gpu_c": 39}, {"cpu_c": 51},
                             {"cpu_c": None, "gpu_c": None}):
            self.now += 3
            self.response["temperatures"] = temperatures
            self.assertFalse(self.service.get()["available"], temperatures)
        self.response = copy.deepcopy(REPORT)
        self.identity.return_value = {"manufacturer": "ASUS", "model": "RC73YA-Other"}
        self.assertFalse(self.service.get()["available"])

    def test_failed_read_clears_prior_cache_and_identity_change_discards_late_reply(self):
        self.assertTrue(self.service.get()["available"])
        self.now += 3
        self.run.side_effect = OSError("fixture timeout")
        self.assertFalse(self.service.get()["available"])
        self.run.side_effect = lambda *a, **k: (0, json.dumps(REPORT), "")
        self.identity.side_effect = [IDENTITY, {"manufacturer": "ASUS", "model": "RC71L"}]
        self.assertFalse(self.service.get()["available"])


class AsusTemperatureCapabilityTests(unittest.TestCase):
    def get(self, identity=IDENTITY, effective="rog-xbox-ally-x", detected=None, result=None):
        native = Mock(return_value=result or REPORT)
        obj = cap.DeviceCapabilities(lambda: profile(effective, detected), lambda: identity,
                                     {"asus-fan": (lambda _: True, native)})
        self.addCleanup(obj.close)
        return rows(obj.get()), native

    def test_actual_temperature_capability_is_independent_of_preset_and_fans(self):
        report = copy.deepcopy(REPORT)
        report["fans"] = {"cpu_rpm": None, "gpu_rpm": None}
        report["capabilities"]["fan_rpm"] = False
        result, native = self.get(result=report)
        self.assertTrue(result["temperatures"]["available"])
        self.assertEqual(result["temperatures"]["values"], [51, 39])
        self.assertEqual(result["temperatures"]["status"], "read-only")
        self.assertFalse(result["fans"]["available"])
        self.assertFalse(result["presets"]["available"])
        native.assert_called_once()

    def test_profile_or_forged_matching_profile_never_overrides_physical_identity(self):
        result, native = self.get(effective="rog-xbox-ally-x", detected="pc")
        self.assertFalse(result["temperatures"]["available"])
        native.assert_not_called()
        result, _ = self.get(identity={"manufacturer": "ASUS", "model": "RC71L"})
        self.assertFalse(result["temperatures"]["available"])
        result, _ = self.get(effective="rog-ally")
        self.assertFalse(result["temperatures"]["available"])

    def test_four_actual_ally_profiles_publish_independent_temperature_readback(self):
        for selected, model in (("rog-ally", "RC71L"), ("rog-ally-x", "RC72LA"),
                                ("rog-xbox-ally", "RC73YA"), ("rog-xbox-ally-x", "RC73XA")):
            result, native = self.get(effective=selected, identity={"manufacturer": "ASUS", "model": model})
            self.assertTrue(result["temperatures"]["available"], selected)
            self.assertEqual(result["temperatures"]["values"], [51, 39])
            native.assert_called_once()

    def test_temperature_capability_rejects_bad_proof_numbers_and_empty_values(self):
        for temperatures in ({"cpu_c": True, "gpu_c": None}, {"cpu_c": 0, "gpu_c": None},
                             {"cpu_c": 126, "gpu_c": None}, {"cpu_c": None, "gpu_c": None}):
            result, _ = self.get(result={**REPORT, "temperatures": temperatures})
            self.assertFalse(result["temperatures"]["available"])
        result, _ = self.get(result={**REPORT, "provider": "other"})
        self.assertFalse(result["temperatures"]["available"])


_TEMP_FAKE = _FAKE.split("function Invoke-CimMethod {")[0].replace(
    "$model='ROG Ally RC71L_RC71L'", "$model='ROG Xbox Ally X RC73XA_RC73XA'; if($fixture.model){$model=$fixture.model}") + r'''
$script:sensorReads=[Collections.Generic.List[object]]::new()
function Invoke-CimMethod {
    param($InputObject,$MethodName,$Arguments,$OperationTimeoutSec)
    if($MethodName -ne 'DSTS' -or $Arguments.Count -ne 1 -or $Arguments.Device_ID -isnot [uint32] -or
       @(0x00110013,0x00110014,0x00120094,0x00120097) -notcontains $Arguments.Device_ID) {throw 'Unexpected firmware operation'}
    $script:sensorReads.Add($Arguments.Device_ID)
    $raw=switch($Arguments.Device_ID) {0x00110013 {0x10023} 0x00110014 {0x1001B} 0x00120094 {0x10033} 0x00120097 {0x10027}}
    if($Arguments.Device_ID -eq 0x00120094 -and $null -ne $fixture.cpu_raw){$raw=$fixture.cpu_raw}
    if($Arguments.Device_ID -eq 0x00120097 -and $null -ne $fixture.gpu_raw){$raw=$fixture.gpu_raw}
    if($fixture.fan_absent -and @(0x00110013,0x00110014) -contains $Arguments.Device_ID){$raw=0xFFFFFFFFL}
    return [pscustomobject]@{device_status=$raw}
}
'''


@unittest.skipUnless(os.name == "nt", "Windows PowerShell fake provider only")
class AsusTemperatureScriptTests(unittest.TestCase):
    def run_native(self, **fixture):
        encoded = base64.b64encode(json.dumps(fixture).encode()).decode("ascii")
        script = _TEMP_FAKE.replace("__FIXTURE__", encoded) + _SCRIPT.replace(
            "Local\\Playhub.AsusAllyFirmware", "Local\\Playhub.AsusAllyFirmware.Test." + uuid.uuid4().hex).replace(
            "try {Operate | ConvertTo-Json", "try {$r=Operate; $r['testReads']=@($script:sensorReads); $r | ConvertTo-Json")
        with tempfile.TemporaryDirectory(prefix="Playhub-ASUS-Temperature-Fake-") as directory:
            path = pathlib.Path(directory) / "fixture.ps1"
            path.write_text(script, encoding="utf-8-sig")
            result = subprocess.run([str(pathlib.Path(os.environ["SystemRoot"]) / "System32/WindowsPowerShell/v1.0/powershell.exe"),
                                     "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", str(path)],
                                    text=True, capture_output=True, timeout=9, creationflags=0x08000000)
        self.assertEqual(result.returncode, 0, result.stderr)
        return json.loads(result.stdout.lstrip("\ufeff"))

    def test_actual_script_requests_only_four_dsts_endpoints_and_decodes_celsius(self):
        result = self.run_native()
        self.assertEqual(result["temperatures"], {"cpu_c": 51, "gpu_c": 39})
        self.assertEqual(result["testReads"], [0x00110013, 0x00110014, 0x00120094, 0x00120097])
        self.assertEqual(self.run_native(cpu_raw=0x1007D)["temperatures"]["cpu_c"], 125)

    def test_zero_flags_error_bits_bad_types_and_implausible_values_stay_null(self):
        for raw in (0, 51, 0x10000, 0x30033, 0x90033, 0xFFFFFFFF, 0x1007E, True, 65587.5):
            with self.subTest(raw=raw):
                self.assertIsNone(self.run_native(cpu_raw=raw)["temperatures"]["cpu_c"])

    def test_all_four_models_probe_separate_endpoints_but_unverified_models_do_not(self):
        for model in ("RC71L", "RC72LA", "RC73YA"):
            result = self.run_native(model=model)
            self.assertEqual(result["testReads"], [0x00110013, 0x00110014, 0x00120094, 0x00120097])
            self.assertEqual(result["temperatures"], {"cpu_c": 51, "gpu_c": 39})
            self.assertTrue(result["capabilities"]["temperature_c"])
            partial = self.run_native(model=model, gpu_raw=0xFFFFFFFF)
            self.assertEqual(partial["temperatures"], {"cpu_c": 51, "gpu_c": None})
            self.assertTrue(partial["capabilities"]["temperature_c"])
        self.assertFalse(self.run_native(model="RC73XA-other")["available"])
        unknown = self.run_native(model="RC74XA")
        self.assertFalse(unknown["available"])
        self.assertEqual(unknown["testReads"], [])

    def test_temperature_only_and_partial_unavailable_never_fabricate_zero(self):
        result = self.run_native(fan_absent=True, cpu_raw=0x10000)
        self.assertFalse(result["capabilities"]["fan_rpm"])
        self.assertEqual(result["temperatures"], {"cpu_c": None, "gpu_c": 39})
        result = self.run_native(cpu_raw=0x10000, gpu_raw=0x10000)
        self.assertFalse(result["capabilities"]["temperature_c"])
        self.assertEqual(result["temperatures"], {"cpu_c": None, "gpu_c": None})

    def test_schema_signature_wrong_hardware_and_provider_fail_before_any_sensor(self):
        for fault in ("wrong_model", "output", "provider", "guid", "instances", "signature", "path"):
            result = self.run_native(fault=fault)
            self.assertFalse(result["available"], fault)
            self.assertEqual(result["testReads"], [])

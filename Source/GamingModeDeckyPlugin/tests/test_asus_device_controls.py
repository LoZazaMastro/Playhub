import ast
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

from quick_settings.asus_device_controls import AsusDeviceControls, _SCRIPT, supports_identity
from test_asus_performance_commands import _FAKE


IDENTITY = {"manufacturer": "ASUSTeK COMPUTER INC.", "model": "ROG Ally RC71L_RC71L"}
REPORT = {"available": True, "ok": True, "code": "", "provider": "asus-ally", "token": "a" * 64,
          "capabilities": {"fan_rpm": True, "charge_limit": False}, "fans": {"cpu_rpm": 3500, "gpu_rpm": 2700}}


class AsusDeviceControlTests(unittest.TestCase):
    def setUp(self):
        self.identity = Mock(return_value=IDENTITY)
        self.response = copy.deepcopy(REPORT)
        self.now = 10
        self.run = Mock(side_effect=self.capture)
        self.native = AsusDeviceControls(self.identity, self.run, lambda: "trusted-powershell.exe", windows=True,
                                         clock=lambda: self.now, wall_clock=lambda: 1000 + self.now)

    def capture(self, arguments, timeout):
        self.path = pathlib.Path(arguments[6])
        self.assertTrue(self.path.exists())
        self.assertEqual(arguments[1:6], ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File"])
        self.assertEqual(timeout, 8)
        self.assertEqual(len(arguments), 7)
        return 0, json.dumps(self.response), ""

    def test_only_actual_supported_asus_identity_reaches_firmware(self):
        for model in ("RC71L", "RC72LA", "RC73YA", "RC73XA", "ROG Xbox Ally X RC73XA_RC73XA"):
            self.assertTrue(supports_identity({"manufacturer": "ASUS", "model": model}))
        for identity in ({"manufacturer": "Other", "model": "RC71L"}, {"manufacturer": "ASUS", "model": "RC74XA"},
                         {"manufacturer": "ASUS", "model": "GA403WR", "selected_profile": "rog-ally"}, None, {}):
            self.identity.return_value = identity
            self.assertFalse(self.native.get()["available"])
        self.run.assert_not_called()

    def test_native_readings_are_returned_with_units_and_no_claimed_charge_state(self):
        report = self.native.get()
        self.assertEqual(report, {**REPORT, "sampled_at_ms": 1010000})
        self.assertFalse(self.path.exists())
        self.assertNotIn("charge_limit", report)
        self.assertNotIn("current_mode", report)
        self.assertNotIn("DEVS", _SCRIPT)

    def test_cached_reads_are_short_lived_and_cannot_be_modified_by_another_consumer(self):
        report = self.native.get()
        report["fans"]["cpu_rpm"] = 9999
        report["capabilities"]["charge_limit"] = True
        self.assertEqual(self.native.get()["fans"]["cpu_rpm"], 3500)
        self.assertIs(self.native.get()["capabilities"]["charge_limit"], False)
        self.assertEqual(self.run.call_count, 1)
        self.now += 2
        self.response["fans"]["cpu_rpm"] = 0
        self.assertEqual(self.native.get()["fans"]["cpu_rpm"], 0)
        self.assertEqual(self.run.call_count, 2)

    def test_invalid_numbers_provider_or_capability_never_create_a_sensor_value(self):
        for change in ({"fans": {"cpu_rpm": True, "gpu_rpm": 0}}, {"fans": {"cpu_rpm": -100, "gpu_rpm": None}},
                       {"fans": {"cpu_rpm": 3501, "gpu_rpm": None}}, {"fans": {"cpu_rpm": 12100, "gpu_rpm": None}},
                       {"fans": {"cpu_rpm": None, "gpu_rpm": None}}, {"provider": "other"}, {"token": "wrong"},
                       {"capabilities": {"fan_rpm": 1, "charge_limit": False}},
                       {"capabilities": {"fan_rpm": True, "charge_limit": 0}}):
            self.now += 3
            self.response = copy.deepcopy(REPORT) | change
            self.assertFalse(self.native.get()["available"], change)

    def test_changed_physical_identity_discards_a_late_response_and_old_cache(self):
        self.identity.side_effect = [IDENTITY, {"manufacturer": "Other", "model": "Other"}]
        self.assertFalse(self.native.get()["available"])
        self.identity.side_effect = None
        self.identity.return_value = {"manufacturer": "Other", "model": "Other"}
        self.assertFalse(self.native.get()["available"])
        self.assertEqual(self.run.call_count, 1)

    def test_backend_uses_raw_identity_and_exposes_only_read_rpc(self):
        tree = ast.parse((pathlib.Path(__file__).resolve().parents[1] / "main.py").read_text(encoding="utf-8-sig"))
        ctor = next(node for node in ast.walk(tree) if isinstance(node, ast.Call)
                    and isinstance(node.func, ast.Attribute) and node.func.attr == "AsusDeviceControls")
        self.assertEqual(ast.unparse(ctor.args[0]), "self._device_profiles.identity")
        rpc = next(node for node in ast.walk(tree) if isinstance(node, ast.AsyncFunctionDef) and node.name == "get_asus_device_controls")
        self.assertIn("self._asus_device_controls.get", ast.unparse(rpc))
        self.assertFalse(any(isinstance(node, ast.AsyncFunctionDef) and node.name == "set_asus_charge_limit" for node in ast.walk(tree)))


_FAN_FAKE = _FAKE.split("function Invoke-CimMethod {")[0] + r'''
function Invoke-CimMethod {
    param($InputObject,$MethodName,$Arguments,$OperationTimeoutSec)
    if($MethodName -ne 'DSTS' -or $Arguments.Count -ne 1 -or $Arguments.Device_ID -isnot [uint32] -or
        @(0x00110013,0x00110014) -notcontains $Arguments.Device_ID) {throw 'Unexpected firmware operation'}
    $script:reads++
    $raw=[uint32](0x10000 + $(if($Arguments.Device_ID -eq 0x00110013){35}else{27}))
    if($fixture.fault -eq 'zero') {$raw=[uint32]0x10000}
    if($fixture.fault -eq 'absent' -or ($fixture.fault -eq 'cpu_absent' -and $Arguments.Device_ID -eq 0x00110013)) {$raw=[uint32]0xFFFFFFFF}
    if($fixture.fault -eq 'error_bit') {$raw=[uint32]0x90023}
    if($fixture.fault -eq 'too_fast') {$raw=[uint32](0x10000+121)}
    if($fixture.fault -eq 'bool_value') {return [pscustomobject]@{device_status=$true}}
    if($fixture.fault -eq 'read_failure') {throw 'Firmware timeout'}
    return [pscustomobject]@{device_status=$raw}
}
'''


@unittest.skipUnless(os.name == "nt", "Windows PowerShell ASUS sensor fixtures")
class AsusNativeFanTests(unittest.TestCase):
    def run_native(self, fault=""):
        encode = base64.b64encode(json.dumps({"fault": fault}).encode()).decode("ascii")
        script = _FAN_FAKE.replace("__FIXTURE__", encode) + _SCRIPT.replace(
            "Local\\Playhub.AsusAllyFirmware", "Local\\Playhub.AsusAllyFirmware.Test." + uuid.uuid4().hex)
        executable = pathlib.Path(os.environ["SystemRoot"]) / "System32/WindowsPowerShell/v1.0/powershell.exe"
        with tempfile.TemporaryDirectory(prefix="Playhub-ASUS-Sensor-Test-") as directory:
            path = pathlib.Path(directory) / "fixture.ps1"
            path.write_text(script, encoding="utf-8-sig")
            result = subprocess.run([str(executable), "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", str(path)],
                                    text=True, capture_output=True, timeout=9, creationflags=0x08000000)
        self.assertEqual(result.returncode, 0, result.stderr)
        return json.loads(result.stdout.lstrip("\ufeff"))

    def test_real_script_decodes_units_stopped_fans_and_independent_missing_sensor(self):
        self.assertEqual(self.run_native()["fans"], {"cpu_rpm": 3500, "gpu_rpm": 2700})
        self.assertEqual(self.run_native("zero")["fans"], {"cpu_rpm": 0, "gpu_rpm": 0})
        self.assertEqual(self.run_native("cpu_absent")["fans"], {"cpu_rpm": None, "gpu_rpm": 2700})

    def test_real_script_refuses_wrong_hardware_schema_provider_signature_and_broken_readings(self):
        for fault in ("wrong_model", "output", "provider", "guid", "instances", "signature", "path",
                      "absent", "error_bit", "too_fast", "bool_value", "read_failure"):
            with self.subTest(fault=fault):
                self.assertFalse(self.run_native(fault)["available"])

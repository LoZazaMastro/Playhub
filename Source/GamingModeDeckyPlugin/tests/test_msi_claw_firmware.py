import base64
import ast
import json
import os
import pathlib
import subprocess
import tempfile
import unittest
import uuid
from unittest.mock import Mock

from quick_settings.msi_claw_firmware import MsiClawFirmware, _SCRIPT, supports_identity


IDENTITY = {"manufacturer": "Micro-Star International Co., Ltd.", "model": "MS-1T91"}
REPORT = {"available": True, "ok": True, "code": "", "provider": "msi-claw-8-ex", "token": "a" * 64, "chargeLimit": 80,
          "mode": 6, "tdpAvailable": True, "pl1": 17, "pl2": 19}


class MsiClawFirmwareTests(unittest.TestCase):
    def setUp(self):
        self.identity = Mock(return_value=IDENTITY)
        self.response = REPORT
        self.run = Mock(side_effect=self.capture)
        self.native = MsiClawFirmware(self.identity, self.run, lambda: "trusted-powershell.exe", windows=True)

    def capture(self, arguments, timeout):
        self.path = pathlib.Path(arguments[6])
        self.script = self.path.read_text(encoding="utf-8-sig")
        return 0, json.dumps(self.response), ""

    def test_other_msi_models_and_manual_labels_do_not_unlock_firmware(self):
        for identity in ({"manufacturer": "MSI", "model": "MS-1T52"},
                         {"manufacturer": "MSI", "model": "MS-1T91-2"},
                         {"manufacturer": "Other", "model": "MS-1T91"}, {}, None):
            self.identity.return_value = identity
            self.assertFalse(self.native.get()["available"])
        self.run.assert_not_called()
        self.assertTrue(supports_identity({"manufacturer": "MSI", "model": "Claw 8 EX AI+ CG3EM"}))

    def test_only_typed_charge_limit_can_be_requested(self):
        request = {"token": "a" * 64, "expected": 80, "percent": 100, "method": "Set_EC", "buffer": [0] * 32}
        self.assertEqual(self.native.set_charge_limit(request), REPORT)
        arguments, options = self.run.call_args
        payload = json.loads(base64.b64decode(arguments[0][8]))
        self.assertEqual(payload, {"action": "charge", "token": "a" * 64, "expected": 80, "percent": 100})
        self.assertEqual(options["timeout"], 24)
        self.assertFalse(self.path.exists())
        self.run.reset_mock()
        for expected, percent in ((80, True), (80, "80"), (19, 80), (101, 80), (80, 19), (80, 101)):
            self.assertEqual(self.native.set_charge_limit({"token": "a" * 64, "expected": expected, "percent": percent})["code"], "invalid_request")
        self.run.assert_not_called()

    def test_invalid_firmware_report_is_hidden(self):
        for report in (REPORT | {"provider": "other"}, REPORT | {"chargeLimit": True},
                       REPORT | {"chargeLimit": 101}, REPORT | {"token": "invalid"}, REPORT | {"mode": True}, {}, None):
            self.response = report
            self.assertFalse(self.native.get()["available"])

    def test_power_requires_exact_integer_limits_and_manual_mode(self):
        request = {"token": "a" * 64, "expectedMode": 6, "expectedPl1": 17, "expectedPl2": 19, "pl1": 25, "pl2": 30}
        self.assertEqual(self.native.set_power_limits(request), REPORT)
        arguments, _ = self.run.call_args
        payload = json.loads(base64.b64decode(arguments[0][8]))
        self.assertEqual(payload, {"action": "power", **request})
        self.run.reset_mock()
        for extra in ({"expectedMode": 1}, {"pl1": True}, {"pl1": 7}, {"pl1": 36}, {"pl1": 25, "pl2": 26},
                      {"pl2": 46}, {"expectedPl2": 18}, {"expectedPl1": "17"}):
            self.assertEqual(self.native.set_power_limits(request | extra)["code"], "invalid_request")
        self.run.assert_not_called()

    def test_mode_requests_use_only_the_three_measured_firmware_values(self):
        for mode in (1, 2, 6):
            self.assertEqual(self.native.set_power_mode({"token": "a" * 64, "expectedMode": 6, "mode": mode}), REPORT)
        self.run.reset_mock()
        for mode in (0, 3, 4, True, "6", 255):
            self.assertEqual(self.native.set_power_mode({"token": "a" * 64, "expectedMode": 6, "mode": mode})["code"], "invalid_request")
        self.run.assert_not_called()

    def test_invalid_power_report_removes_only_power_capability(self):
        for extra in ({"pl1": 36}, {"pl2": 18}, {"pl2": True}, {"tdpAvailable": "true"}):
            self.response = REPORT | extra
            report = self.native.get()
            self.assertTrue(report["available"])
            self.assertFalse(report["tdpAvailable"])

    def test_backend_wires_actual_identity_and_fixed_typed_rpc_methods(self):
        tree = ast.parse((pathlib.Path(__file__).resolve().parents[1] / "main.py").read_text(encoding="utf-8-sig"))
        constructor = next(node for node in ast.walk(tree) if isinstance(node, ast.Call)
                           and isinstance(node.func, ast.Attribute) and node.func.attr == "MsiClawFirmware")
        self.assertEqual(ast.unparse(constructor.args[0]), "self._device_profiles.identity")
        expected = {"get_msi_claw_firmware": "get", "set_msi_claw_charge_limit": "set_charge_limit",
                    "set_msi_claw_power_mode": "set_power_mode", "set_msi_claw_power_limits": "set_power_limits"}
        for public, private in expected.items():
            endpoint = next(node for node in ast.walk(tree) if isinstance(node, ast.AsyncFunctionDef) and node.name == public)
            target = endpoint.body[0].value.value
            self.assertEqual(ast.unparse(target.func), "asyncio.to_thread")
            self.assertEqual(ast.unparse(target.args[0]), "self._msi_claw_firmware." + private)
            self.assertEqual(len(target.args), 1 if public.startswith("get_") else 2)


_FAKE = r'''
$fixture = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__FIXTURE__')) | ConvertFrom-Json
$script:ap = [byte[]]::new(32)
$script:ap[0]=1; $script:ap[3]=0xC6; $script:ap[4]=0x80; $script:ap[5]=0xD0
$script:ap[6]=0x42; $script:ap[31]=0x99
$script:limits = [byte[]]::new(32); $script:limits[0]=1; $script:limits[1]=17; $script:limits[2]=19
if ($fixture.fault -eq 'automatic') {$script:ap[3]=0xC1}
$script:writes = [Collections.Generic.List[object]]::new()
$script:methods = [Collections.Generic.List[string]]::new()
$script:reads = 0
$script:limitReads = 0
function Get-ItemProperty {
    param($LiteralPath)
    if ($LiteralPath -like '*\BIOS') {
        $model='MS-1T91'; if ($fixture.fault -eq 'wrong_model') {$model='MS-1T52'}
        return [pscustomobject]@{SystemManufacturer='Micro-Star International Co., Ltd.';SystemProductName=$model;BIOSVersion='E1T91IMS.10A'}
    }
    return [pscustomobject]@{'(default)'=(Join-Path $env:SystemRoot 'System32\wbem\wmiprov.dll')}
}
function Method([int]$id) {
    return [pscustomobject]@{Qualifiers=@{WmiMethodId=@{Value=$id};Implemented=@{Value=$true}};
        Parameters=@{Data=[pscustomobject]@{CimType='Instance';Qualifiers=@{
            EmbeddedInstance=@{Value='Package_32'};In=@{Value=$true};Out=@{Value=$true}}}}}
}
function Get-CimClass {
    param($Namespace,$ClassName,$OperationTimeoutSec)
    if ($ClassName -eq 'Package_32') {
        $type='UInt8Array'; if($fixture.fault -eq 'package') {$type='UInt16Array'}
        return [pscustomobject]@{CimClassQualifiers=@{guid=@{Value='{ABBC0F63-8EA1-11d1-00A0-C90629100000}'}};
            CimClassProperties=@{Bytes=@{CimType=$type}}}
    }
    if($fixture.fault -eq 'slow') {Start-Sleep -Milliseconds 4100}
    $methods=@{Get_AP=(Method 25);Set_AP=(Method 26);Get_SlaveBattery=(Method 11);Set_SlaveBattery=(Method 12)}
    if($fixture.fault -eq 'power_schema') {$methods.Set_SlaveBattery=Method 13}
    if($fixture.fault -eq 'method') {$methods.Set_AP=Method 27}
    if($fixture.fault -eq 'embedded') {$methods.Get_AP.Parameters.Data.Qualifiers.EmbeddedInstance.Value='OtherPackage'}
    $provider='WmiProv'; if($fixture.fault -eq 'provider') {$provider='OtherProvider'}
    return [pscustomobject]@{CimClassQualifiers=@{Provider=@{Value=$provider};Dynamic=@{Value=$true};
        guid=@{Value='{ABBC0F6E-8EA1-11d1-00A0-C90629100000}'}};CimClassMethods=$methods}
}
function Get-CimInstance {
    param($Namespace,$ClassName,$Filter,$OperationTimeoutSec)
    if($ClassName -eq '__Win32Provider') {return [pscustomobject]@{CLSID='{D2D588B5-D081-11d0-99E0-00C04FC2F8EC}';HostingModel='LocalSystemHost'}}
    $instance=[pscustomobject]@{Active=$true;InstanceName='ACPI\MSI_ACPI\test_0'}
    if($fixture.fault -eq 'instances') {return @($instance,$instance)}
    return $instance
}
function Get-AuthenticodeSignature {
    param($LiteralPath)
    $status='Valid';if($fixture.fault -eq 'signature') {$status='NotSigned'}
    return [pscustomobject]@{Status=$status;SignerCertificate=@{Subject='CN=Microsoft Windows, O=Microsoft Corporation, C=US'}}
}
function Get-FileHash {param($LiteralPath,$Algorithm) return [pscustomobject]@{Hash=('A'*64)}}
function New-CimInstance {param($CimClass,$Property,[switch]$ClientOnly) return [pscustomobject]@{Bytes=[byte[]]$Property.Bytes.Clone()}}
function Invoke-CimMethod {
    param($InputObject,$MethodName,$Arguments,$OperationTimeoutSec)
    if($MethodName -eq 'Get_SlaveBattery') {
        if($Arguments.Data.Bytes[0] -ne 1) {throw 'Wrong subfunction'}
        $script:limitReads++
        if($fixture.fault -eq 'stale_limits' -and $script:limitReads -eq 2) {$script:limits[1]=20;$script:limits[2]=25}
        if($fixture.fault -eq 'power_report_race' -and $script:limitReads -eq 4) {$script:limits[1]=20;$script:limits[2]=25}
        return [pscustomobject]@{Data=[pscustomobject]@{Bytes=[byte[]]$script:limits.Clone()}}
    }
    if($MethodName -eq 'Set_SlaveBattery') {
        $payload=[byte[]]$Arguments.Data.Bytes.Clone()
        if($payload[0] -ne 1 -or @($payload[3..31] | Where-Object {$_ -ne 0}).Count -ne 0) {throw 'Wrong power payload'}
        $script:writes.Add([int[]]$payload); $script:methods.Add($MethodName)
        if($fixture.fault -ne 'ignore_power') {$script:limits=[byte[]]$payload.Clone()}
        if($fixture.fault -eq 'external_limits') {$script:limits[1]=20;$script:limits[2]=25}
        if($fixture.fault -eq 'power_fail_after' -and $script:writes.Count -eq 1) {$script:ap[3]=0xC2;throw 'Failure after power write'}
        if($fixture.fault -eq 'power_write_fail_after' -and $script:writes.Count -eq 1) {throw 'Failure after power write'}
        if($fixture.fault -eq 'mode_after_power') {$script:ap[3]=0xC2}
        $reply=[byte[]]::new(32);$reply[0]=1
        return [pscustomobject]@{Data=[pscustomobject]@{Bytes=$reply}}
    }
    if($MethodName -eq 'Get_AP') {
        $script:reads++
        if($fixture.fault -eq 'mode_change' -and $script:reads -eq 2) {$script:ap[3]=0xC2}
        if($fixture.fault -eq 'charge_report_race' -and $script:reads -eq 4) {$script:ap[5]=0xBC}
        if($fixture.fault -eq 'header') {$script:ap[4]=0x81}
        return [pscustomobject]@{Data=[pscustomobject]@{Bytes=[byte[]]$script:ap.Clone()}}
    }
    if($MethodName -ne 'Set_AP') {throw 'Unexpected native method'}
    $payload=[byte[]]$Arguments.Data.Bytes.Clone()
    $script:writes.Add([int[]]$payload);$script:methods.Add($MethodName)
    if($fixture.fault -eq 'denied') {throw 'Access denied'}
    if($fixture.fault -ne 'ignore') {$script:ap=[byte[]]$payload.Clone();$script:ap[0]=1}
    if($fixture.fault -eq 'external_charge') {$script:ap[5]=0xBC}
    if($fixture.fault -eq 'fail_after' -and $script:writes.Count -eq 1) {$script:ap[3]=0xC2;throw 'Failure after write'}
    if($fixture.fault -eq 'mode_fail_after' -and $script:writes.Count -eq 1) {$script:ap[5]=0xBC;throw 'Failure after mode write'}
    if($fixture.fault -eq 'external_mode') {$script:ap[3]=0xC1}
    $reply=[byte[]]::new(32);$reply[0]=1
    return [pscustomobject]@{Data=[pscustomobject]@{Bytes=$reply}}
}
'''


@unittest.skipUnless(os.name == "nt", "Windows PowerShell firmware transaction fixture")
class MsiPowerShellTransactionTests(unittest.TestCase):
    def run_native(self, request, fault=""):
        encode = lambda value: base64.b64encode(json.dumps(value).encode()).decode("ascii")
        prefix = _FAKE.replace("__FIXTURE__", encode({"fault": fault}))
        script = _SCRIPT.replace("param([Parameter(Mandatory=$true)][string]$RequestBase64)", "$RequestBase64='" + encode(request) + "'")
        # Isolate simulated firmware transactions from other concurrently running suites.
        script = script.replace("Local\\Playhub.MsiClawFirmware", "Local\\Playhub.MsiClawFirmware.Test." + uuid.uuid4().hex)
        script = script.replace("$report | ConvertTo-Json -Compress -Depth 4",
            "$report['testWrites']=@($script:writes); $report['testMethods']=@($script:methods); $report | ConvertTo-Json -Compress -Depth 4")
        executable = pathlib.Path(os.environ.get("SystemRoot", r"C:\Windows")) / "System32/WindowsPowerShell/v1.0/powershell.exe"
        with tempfile.TemporaryDirectory(prefix="Playhub-MSI-Transaction-Test-") as directory:
            path = pathlib.Path(directory) / "fixture.ps1"
            path.write_text(prefix + script, encoding="utf-8-sig")
            process = subprocess.run([str(executable), "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", str(path)],
                text=True, capture_output=True, timeout=10, creationflags=0x08000000)
        self.assertEqual(process.returncode, 0, process.stderr)
        return json.loads(process.stdout.lstrip("\ufeff"))

    def request(self, token, **extra):
        return {"action": "charge", "token": token, "percent": 100, "expected": 80, **extra}

    def test_native_read_and_charge_write_preserve_unrelated_bytes(self):
        read = self.run_native({"action": "get"})
        self.assertTrue(read["available"], read)
        self.assertEqual((read["chargeLimit"], read["testWrites"]), (80, []))
        changed = self.run_native(self.request(read["token"]), fault="mode_change")
        self.assertTrue(changed["ok"], changed)
        self.assertEqual(changed["chargeLimit"], 100)
        payload = changed["testWrites"][0]
        self.assertEqual((payload[0], payload[3], payload[4], payload[5], payload[6], payload[31]), (0, 0xC2, 0x80, 0xE4, 0x42, 0x99))

    def test_wrong_model_provider_schema_signature_or_package_never_write(self):
        for fault in ("wrong_model", "provider", "method", "embedded", "instances", "signature", "package", "header"):
            with self.subTest(fault=fault):
                report = self.run_native(self.request("a" * 64), fault=fault)
                self.assertFalse(report["available"], report)
                self.assertEqual(report["testWrites"], [])

    def test_stale_charge_and_slow_preflight_do_not_write(self):
        read = self.run_native({"action": "get"})
        for request, fault, code in ((self.request("b" * 64), "", "profile_changed"),
                                     (self.request(read["token"], expected=60), "", "profile_changed"),
                                     (self.request(read["token"]), "slow", "preflight_timeout")):
            report = self.run_native(request, fault=fault)
            self.assertEqual((report["ok"], report["code"], report["testWrites"]), (False, code, []))

    def test_failed_write_rolls_back_only_the_charge_byte(self):
        read = self.run_native({"action": "get"})
        report = self.run_native(self.request(read["token"]), fault="fail_after")
        self.assertFalse(report["ok"], report)
        self.assertEqual(report["chargeLimit"], 80)
        self.assertEqual(len(report["testWrites"]), 2)
        restored = report["testWrites"][1]
        self.assertEqual((restored[3], restored[5], restored[6], restored[31]), (0xC2, 0xD0, 0x42, 0x99))

    def test_external_charge_selection_is_not_overwritten_during_recovery(self):
        read = self.run_native({"action": "get"})
        report = self.run_native(self.request(read["token"]), fault="external_charge")
        self.assertEqual((report["ok"], report["code"], report["chargeLimit"]), (False, "profile_changed", 60))
        self.assertEqual(len(report["testWrites"]), 1)

    def power_request(self, token, **extra):
        return {"action": "power", "token": token, "expectedMode": 6, "expectedPl1": 17, "expectedPl2": 19,
                "pl1": 25, "pl2": 30, **extra}

    def test_verified_manual_power_limits_write_exact_method_and_payload(self):
        read = self.run_native({"action": "get"})
        self.assertEqual((read["mode"], read["tdpAvailable"], read["pl1"], read["pl2"]), (6, True, 17, 19))
        report = self.run_native(self.power_request(read["token"]))
        self.assertTrue(report["ok"], report)
        self.assertEqual((report["mode"], report["pl1"], report["pl2"], report["chargeLimit"]), (6, 25, 30, 80))
        self.assertEqual(report["testMethods"], ["Set_SlaveBattery"])
        self.assertEqual(report["testWrites"], [[1, 25, 30] + [0] * 29])

    def test_automatic_mode_or_missing_power_schema_never_changes_limits(self):
        for fault in ("automatic", "power_schema"):
            read = self.run_native({"action": "get"}, fault=fault)
            self.assertTrue(read["available"], read)
            report = self.run_native(self.power_request(read["token"]), fault=fault)
            self.assertFalse(report["ok"], report)
            self.assertEqual(report["testWrites"], [])
        self.assertFalse(self.run_native({"action": "get"}, fault="power_schema")["tdpAvailable"])

    def test_stale_or_out_of_range_limits_never_write(self):
        read = self.run_native({"action": "get"})
        for extra, fault in (({"pl1": 36}, ""), ({"pl2": 26}, ""), ({"expectedPl1": 16}, ""), ({}, "stale_limits")):
            report = self.run_native(self.power_request(read["token"], **extra), fault=fault)
            self.assertFalse(report["ok"], report)
            self.assertEqual(report["testWrites"], [])

    def test_power_rollback_preserves_an_external_mode_and_external_limits(self):
        read = self.run_native({"action": "get"})
        for fault in ("power_fail_after", "mode_after_power"):
            report = self.run_native(self.power_request(read["token"]), fault=fault)
            self.assertFalse(report["ok"], report)
            self.assertEqual((report["mode"], report["code"], report["pl1"], report["pl2"]), (2, "profile_changed", 25, 30))
            self.assertEqual(report["testMethods"], ["Set_SlaveBattery"])
        owned = self.run_native(self.power_request(read["token"]), fault="power_write_fail_after")
        self.assertEqual((owned["ok"], owned["mode"], owned["pl1"], owned["pl2"]), (False, 6, 17, 19))
        self.assertEqual(owned["testMethods"], ["Set_SlaveBattery", "Set_SlaveBattery"])
        external = self.run_native(self.power_request(read["token"]), fault="external_limits")
        self.assertEqual((external["ok"], external["code"], external["pl1"], external["pl2"]), (False, "profile_changed", 20, 25))
        self.assertEqual(len(external["testWrites"]), 1)

    def test_mode_write_and_rollback_preserve_charge_and_unrelated_flags(self):
        read = self.run_native({"action": "get"})
        request = {"action": "mode", "token": read["token"], "expectedMode": 6, "mode": 2}
        report = self.run_native(request)
        self.assertTrue(report["ok"], report)
        self.assertEqual((report["mode"], report["chargeLimit"], report["testWrites"][0][3]), (2, 80, 0xC2))
        failed = self.run_native(request, fault="mode_fail_after")
        self.assertEqual((failed["ok"], failed["mode"], failed["chargeLimit"]), (False, 6, 60))
        self.assertEqual((failed["testWrites"][1][3], failed["testWrites"][1][5]), (0xC6, 0xBC))
        external = self.run_native(request, fault="external_mode")
        self.assertEqual((external["ok"], external["code"], external["mode"]), (False, "profile_changed", 1))
        self.assertEqual(len(external["testWrites"]), 1)

    def test_final_report_checks_competing_changes_after_successful_confirmation(self):
        read = self.run_native({"action": "get"})
        charge = self.run_native(self.request(read["token"]), fault="charge_report_race")
        self.assertEqual((charge["ok"], charge["code"], charge["chargeLimit"]), (False, "profile_changed", 60))
        self.assertEqual(len(charge["testWrites"]), 1)
        power = self.run_native(self.power_request(read["token"]), fault="power_report_race")
        self.assertEqual((power["ok"], power["code"], power["pl1"], power["pl2"]), (False, "profile_changed", 20, 25))
        self.assertEqual(len(power["testWrites"]), 1)


if __name__ == "__main__":
    unittest.main()

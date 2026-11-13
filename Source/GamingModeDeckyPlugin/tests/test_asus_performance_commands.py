import ast
import base64
import json
import os
import pathlib
import subprocess
import tempfile
import time
import unittest
import uuid
from unittest.mock import Mock

from quick_settings.asus_performance_commands import AsusPerformanceCommands, _SCRIPT, supports_identity


IDENTITY = {"manufacturer": "ASUSTeK COMPUTER INC.", "model": "ROG Ally RC71L_RC71L"}
REPORT = {"available": True, "ok": True, "code": "", "provider": "asus-ally", "token": "a" * 64,
          "commands": [2, 0, 1], "acknowledged": False}


class AsusPerformanceCommandsTests(unittest.TestCase):
    def setUp(self):
        self.identity = Mock(return_value=IDENTITY)
        self.response = REPORT
        self.run = Mock(side_effect=self.capture)
        self.native = AsusPerformanceCommands(self.identity, self.run, lambda: "trusted-powershell.exe", windows=True)

    def capture(self, arguments, timeout):
        self.path = pathlib.Path(arguments[6])
        return 0, json.dumps(self.response), ""

    def test_only_real_asus_ally_identities_unlock_commands(self):
        for identity in ({"manufacturer": "Other", "model": "RC71L"}, {"manufacturer": "ASUS", "model": "RC71L-Other"},
                         {"manufacturer": "ASUS", "model": "GA403WR"}, {}, None):
            self.identity.return_value = identity
            self.assertFalse(self.native.get()["available"])
        self.run.assert_not_called()
        for model in ("RC71L", "RC72LA", "RC73YA", "RC73XA", "ROG Ally X RC72LA_RC72LA",
                      "ROG Xbox Ally RC73YA_RC73YA", "ROG Xbox Ally X RC73XA_RC73XA"):
            self.assertTrue(supports_identity({"manufacturer": "ASUS", "model": model}), model)

    def test_only_explicit_typed_preset_command_reaches_native_helper(self):
        self.response = REPORT | {"acknowledged": True}
        request = {"token": "a" * 64, "mode": 2, "device": 0x00120057, "action": "charge", "automatic": True}
        self.assertTrue(self.native.apply(request)["acknowledged"])
        arguments, options = self.run.call_args
        self.assertEqual(json.loads(base64.b64decode(arguments[0][8])), {"action": "apply", "token": "a" * 64, "mode": 2})
        self.assertEqual(options["timeout"], 8)
        self.assertFalse(self.path.exists())
        self.run.reset_mock()
        for mode in (True, "1", -1, 3, 4, 255):
            self.assertEqual(self.native.apply({"token": "a" * 64, "mode": mode})["code"], "invalid_request")
        self.run.assert_not_called()

    def test_report_exposes_capability_without_inventing_current_profile(self):
        self.response = REPORT | {"currentMode": 2, "mode": 0, "lastRequested": 1}
        report = self.native.get()
        self.assertEqual(report, REPORT)
        for extra in ({"token": "invalid"}, {"provider": "other"}, {"commands": [2, False, 1]}, {"acknowledged": 1}):
            self.response = REPORT | extra
            self.assertFalse(self.native.get()["available"])

    def test_backend_uses_actual_identity_and_sole_explicit_command_endpoint(self):
        tree = ast.parse((pathlib.Path(__file__).resolve().parents[1] / "main.py").read_text(encoding="utf-8-sig"))
        constructor = next(node for node in ast.walk(tree) if isinstance(node, ast.Call)
                           and isinstance(node.func, ast.Attribute) and node.func.attr == "AsusPerformanceCommands")
        self.assertEqual(ast.unparse(constructor.args[0]), "self._device_profiles.identity")
        endpoint = next(node for node in ast.walk(tree) if isinstance(node, ast.AsyncFunctionDef) and node.name == "apply_asus_performance_command")
        self.assertEqual(ast.unparse(endpoint.body[0].value.value.args[0]), "self._asus_performance_commands.apply")


_FAKE = r'''
$fixture = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__FIXTURE__')) | ConvertFrom-Json
$script:writes = [Collections.Generic.List[object]]::new()
$script:reads = 0
function Get-ItemProperty {
    param($LiteralPath)
    if ($LiteralPath -like '*\BIOS') {
        $model='ROG Ally RC71L_RC71L'; if ($fixture.fault -eq 'wrong_model') {$model='GA403WR'}
        return [pscustomobject]@{SystemManufacturer='ASUSTeK COMPUTER INC.';SystemProductName=$model;BIOSVersion='343'}
    }
    $path=Join-Path $env:SystemRoot 'System32\wbem\wmiprov.dll'
    if($fixture.fault -eq 'path') {$path=Join-Path $env:TEMP 'wmiprov.dll'}
    return [pscustomobject]@{'(default)'=$path}
}
function Parameter([bool]$isInput) {
    return [pscustomobject]@{CimType='UInt32';Qualifiers=@{In=@{Value=$isInput};Out=@{Value=(!$isInput)}}}
}
function Get-CimClass {
    param($Namespace,$ClassName,$OperationTimeoutSec)
    if($ClassName -ne 'AsusAtkWmi_WMNB' -or $Namespace -ne 'root/wmi') {throw 'Unexpected class'}
    if($fixture.fault -eq 'slow') {Start-Sleep -Milliseconds 4100}
    $methods=@{
        DSTS=[pscustomobject]@{Qualifiers=@{Implemented=@{Value=$true}};Parameters=@{Device_ID=(Parameter $true);device_status=(Parameter $false)}}
        DEVS=[pscustomobject]@{Qualifiers=@{Implemented=@{Value=$true}};Parameters=@{Device_ID=(Parameter $true);Control_status=(Parameter $true);result=(Parameter $false)}}
    }
    if($fixture.fault -eq 'method') {$methods.DEVS.Parameters.Control_status.CimType='UInt64'}
    if($fixture.fault -eq 'output') {$methods.DSTS.Parameters.device_status.Qualifiers.Out.Value=$false}
    $provider='WmiProv'; if($fixture.fault -eq 'provider') {$provider='OtherProvider'}
    $guid='{97845ED0-4E6D-11DE-8A39-0800200C9A66}'; if($fixture.fault -eq 'guid') {$guid='{00000000-0000-0000-0000-000000000000}'}
    return [pscustomobject]@{CimClassQualifiers=@{Provider=@{Value=$provider};Dynamic=@{Value=$true};guid=@{Value=$guid}};CimClassMethods=$methods}
}
function Get-CimInstance {
    param($Namespace,$ClassName,$Filter,$OperationTimeoutSec)
    if($ClassName -eq '__Win32Provider') {return [pscustomobject]@{CLSID='{D2D588B5-D081-11D0-99E0-00C04FC2F8EC}';HostingModel='LocalSystemHost'}}
    $instance=[pscustomobject]@{Active=$true;InstanceName='ACPI\ASUS\test_0'}
    if($fixture.fault -eq 'instances') {return @($instance,$instance)}
    return $instance
}
function Get-AuthenticodeSignature {
    param($LiteralPath)
    $status='Valid';if($fixture.fault -eq 'signature') {$status='NotSigned'}
    return [pscustomobject]@{Status=$status;SignerCertificate=@{Subject='CN=Microsoft Windows, O=Microsoft Corporation, C=US'}}
}
function Get-FileHash {param($LiteralPath,$Algorithm) return [pscustomobject]@{Hash=('A'*64)}}
function Invoke-CimMethod {
    param($InputObject,$MethodName,$Arguments,$OperationTimeoutSec)
    if($Arguments.Device_ID -ne 0x00120075 -or $Arguments.Device_ID -isnot [uint32]) {throw 'Unexpected endpoint'}
    if($MethodName -eq 'DSTS') {
        if($Arguments.Count -ne 1) {throw 'Unexpected read parameters'}
        $script:reads++
        $raw=[uint32]0x10003
        if($fixture.fault -eq 'absent' -or ($fixture.fault -eq 'disappeared' -and $script:reads -eq 2)) {$raw=[uint32]0xFFFFFFFE}
        return [pscustomobject]@{device_status=$raw}
    }
    if($MethodName -ne 'DEVS' -or $Arguments.Count -ne 2 -or $Arguments.Control_status -isnot [uint32]) {throw 'Unexpected native method'}
    $script:writes.Add(@{endpoint=[uint32]$Arguments.Device_ID;mode=[uint32]$Arguments.Control_status})
    if($fixture.fault -eq 'throw_after') {throw 'Uncertain firmware response'}
    $ack=1; if($fixture.fault -eq 'refused') {$ack=0}; if($fixture.fault -eq 'bool_ack') {$ack=$true}
    return [pscustomobject]@{result=$ack}
}
'''


@unittest.skipUnless(os.name == "nt", "Windows PowerShell ASUS command fixture")
class AsusPowerShellCommandsTests(unittest.TestCase):
    def run_native(self, request, fault="", isolate=True):
        encode = lambda value: base64.b64encode(json.dumps(value).encode()).decode("ascii")
        prefix = _FAKE.replace("__FIXTURE__", encode({"fault": fault}))
        script = _SCRIPT.replace("param([Parameter(Mandatory=$true)][string]$RequestBase64)", "$RequestBase64='" + encode(request) + "'")
        # Isolate simulated firmware transactions from other concurrently running suites.
        if isolate:
            script = script.replace("Local\\Playhub.AsusAllyFirmware", "Local\\Playhub.AsusAllyFirmware.Test." + uuid.uuid4().hex)
        script = script.replace("$report | ConvertTo-Json -Compress -Depth 4",
            "$report['testWrites']=@($script:writes); $report | ConvertTo-Json -Compress -Depth 4")
        executable = pathlib.Path(os.environ.get("SystemRoot", r"C:\Windows")) / "System32/WindowsPowerShell/v1.0/powershell.exe"
        with tempfile.TemporaryDirectory(prefix="Playhub-ASUS-Command-Test-") as directory:
            path = pathlib.Path(directory) / "fixture.ps1"
            path.write_text(prefix + script, encoding="utf-8-sig")
            process = subprocess.run([str(executable), "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", str(path)],
                text=True, capture_output=True, timeout=9, creationflags=0x08000000)
        self.assertEqual(process.returncode, 0, process.stderr)
        return json.loads(process.stdout.lstrip("\ufeff"))

    def test_probe_is_read_only_and_never_interprets_presence_as_current_mode(self):
        report = self.run_native({"action": "get"})
        self.assertTrue(report["available"], report)
        self.assertFalse(report["acknowledged"])
        self.assertNotIn("mode", report)
        self.assertNotIn("currentMode", report)
        self.assertEqual(report["testWrites"], [])

    def test_explicit_preset_command_writes_only_documented_endpoint_once(self):
        read = self.run_native({"action": "get"})
        for mode in (2, 0, 1):
            report = self.run_native({"action": "apply", "token": read["token"], "mode": mode})
            self.assertEqual((report["ok"], report["acknowledged"]), (True, True))
            self.assertEqual(report["testWrites"], [{"endpoint": 0x00120075, "mode": mode}])
            self.assertNotIn("mode", report)

    def test_wrong_identity_schema_provider_and_absent_endpoint_never_write(self):
        for fault in ("wrong_model", "method", "output", "provider", "guid", "instances", "signature", "path", "absent"):
            with self.subTest(fault=fault):
                report = self.run_native({"action": "apply", "token": "a" * 64, "mode": 0}, fault=fault)
                self.assertFalse(report["available"], report)
                self.assertEqual(report["testWrites"], [])

    def test_changed_token_unsupported_command_and_slow_preflight_never_write(self):
        read = self.run_native({"action": "get"})
        request = {"action": "apply", "token": read["token"], "mode": 0}
        for extra, fault in (({"token": "b" * 64}, ""), ({"mode": True}, ""), ({"mode": 4}, ""),
                             ({"action": "charge"}, ""), ({}, "slow"), ({}, "disappeared")):
            report = self.run_native(request | extra, fault=fault)
            self.assertFalse(report["ok"], report)
            self.assertEqual(report["testWrites"], [])

    def test_failed_or_uncertain_ack_does_not_retry_or_invent_rollback(self):
        read = self.run_native({"action": "get"})
        for fault in ("refused", "throw_after", "bool_ack"):
            report = self.run_native({"action": "apply", "token": read["token"], "mode": 2}, fault=fault)
            self.assertFalse(report["ok"], report)
            self.assertFalse(report["acknowledged"])
            self.assertEqual(len(report["testWrites"]), 1)

    def test_production_fixed_mutex_refuses_a_second_process_before_any_firmware_write(self):
        executable = pathlib.Path(os.environ.get("SystemRoot", r"C:\Windows")) / "System32/WindowsPowerShell/v1.0/powershell.exe"
        holder_script = r'''
param([string]$ReadyPath)
$mutex=[Threading.Mutex]::new($false,'Local\Playhub.AsusAllyFirmware')
$locked=$false
try {
    try {$locked=$mutex.WaitOne(1000)} catch [Threading.AbandonedMutexException] {$locked=$true}
    if(!$locked) {exit 2}
    [IO.File]::WriteAllText($ReadyPath,'ready')
    Start-Sleep -Seconds 5
} finally {if($locked){$mutex.ReleaseMutex()};$mutex.Dispose()}
'''
        with tempfile.TemporaryDirectory(prefix="Playhub-ASUS-Mutex-Test-") as directory:
            path = pathlib.Path(directory) / "holder.ps1"
            ready = pathlib.Path(directory) / "ready"
            path.write_text(holder_script, encoding="utf-8-sig")
            process = subprocess.Popen([str(executable), "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", str(path), "-ReadyPath", str(ready)],
                stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, creationflags=0x08000000)
            try:
                deadline = time.monotonic() + 3
                while not ready.exists() and process.poll() is None and time.monotonic() < deadline:
                    time.sleep(0.02)
                self.assertTrue(ready.exists(), "Could not acquire the production command mutex in the fixture")
                report = self.run_native({"action": "get"}, isolate=False)
                self.assertEqual((report["available"], report["ok"], report["code"], report["testWrites"]), (False, False, "busy", []))
            finally:
                if process.poll() is None:
                    process.terminate()
                process.wait(timeout=3)


if __name__ == "__main__":
    unittest.main()

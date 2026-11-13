import base64
import asyncio
import ast
import json
import os
import pathlib
import subprocess
import tempfile
import time
import types
import unittest
import uuid
from unittest.mock import Mock
from contextlib import contextmanager

from quick_settings.lenovo_power_mode import LenovoPowerMode, _SCRIPT, supports_identity


_IDENTITY = {"manufacturer": "LENOVO", "model": "83E1"}
_REPORT = {"available": True, "ok": True, "code": "", "provider": "lenovo-legion-go", "mode": 2,
           "modes": [1, 2, 3], "token": "a" * 64, "editable": True}


class LenovoPowerModeTests(unittest.TestCase):
    def setUp(self):
        self.identity = Mock(return_value=_IDENTITY)
        self.run = Mock(side_effect=self.capture)
        self.response = _REPORT
        self.native = LenovoPowerMode(self.identity, self.run, lambda: "trusted-powershell.exe", windows=True)

    def capture(self, arguments, timeout):
        self.script_path = pathlib.Path(arguments[6])
        self.script = self.script_path.read_text(encoding="utf-8-sig")
        return 0, json.dumps(self.response), ""

    def test_only_the_actual_supported_device_can_probe_firmware(self):
        for identity in ({"manufacturer": "LENOVO", "model": "83N0"},
                         {"manufacturer": "ASUS", "model": "83E1"},
                         {"manufacturer": "LENOVO", "model": "83E10"}, {}, None):
            with self.subTest(identity=identity):
                self.identity.return_value = identity
                self.assertFalse(self.native.get()["available"])
        self.run.assert_not_called()
        self.assertTrue(supports_identity({"manufacturer": "Lenovo", "model": "Legion Go 8APU1"}))

    def test_get_uses_owned_temporary_script_and_bounded_read(self):
        self.assertEqual(self.native.get(), _REPORT)
        args, options = self.run.call_args
        self.assertEqual(args[0][0:6], ["trusted-powershell.exe", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File"])
        self.assertIn("-MethodName GetSmartFanMode", self.script)
        self.assertEqual(json.loads(base64.b64decode(args[0][8])), {"action": "get"})
        self.assertFalse(self.script_path.exists())
        self.assertFalse(self.script_path.parent.exists())
        self.assertEqual(options["timeout"], 6)

    def test_set_forwards_only_validated_enum_token_and_expected_value(self):
        request = {"token": "a" * 64, "mode": 1, "expected": 2, "method": "DeleteEverything"}
        self.native.set(request)
        args, options = self.run.call_args
        self.assertNotIn("DeleteEverything", self.script)
        self.assertNotIn("method", json.loads(base64.b64decode(args[0][8])))
        self.assertEqual(options["timeout"], 16)
        self.run.reset_mock()
        for request in (None, [], {}, {"token": "a" * 64, "mode": 1, "expected": 255},
                        {"token": "a" * 64, "mode": True, "expected": 2},
                        {"token": "a" * 63 + "'", "mode": 1, "expected": 2},
                        {"token": "a" * 64, "mode": 255, "expected": 2}):
            self.assertEqual(self.native.set(request)["code"], "invalid_request")
        self.run.assert_not_called()

    def test_malformed_ambiguous_or_untrusted_response_is_hidden(self):
        for report in (_REPORT | {"provider": "other"}, _REPORT | {"mode": True},
                       _REPORT | {"token": "unexpected"}, _REPORT | {"modes": [1, 2, 2]},
                       _REPORT | {"editable": False}, _REPORT | {"modes": [1, 2, 99]},
                       {"available": True}, [], None):
            with self.subTest(report=report):
                self.response = report
                self.assertEqual(self.native.get(), {"available": False, "ok": False, "code": "unavailable"})
        self.run.side_effect = None
        self.run.return_value = (-1, "", "timeout")
        self.assertFalse(self.native.get()["available"])

    def test_custom_profile_is_read_only_and_preserved(self):
        report = _REPORT | {"mode": 255, "editable": False}
        self.response = report
        self.assertEqual(self.native.get(), report)


class FirmwareHelperLifecycleTests(unittest.TestCase):
    def setUp(self):
        self.events = []

        @contextmanager
        def independent_dll_directory():
            self.events.append("dll-cleared")
            try:
                yield
            finally:
                self.events.append("dll-restored")

        self.child = Mock(returncode=0)

        def communicate(timeout=None):
            self.assertEqual(self.events[-1], "dll-restored")
            return '{"available":false}', ""

        self.child.communicate.side_effect = communicate
        self.runtime = types.SimpleNamespace(
            _independent_process_dll_directory=independent_dll_directory,
            _independent_process_environment=lambda: {"PYINSTALLER_RESET_ENVIRONMENT": "1"},
            _NO_WINDOW=0x08000000,
            subprocess=types.SimpleNamespace(Popen=Mock(return_value=self.child), PIPE=-1, TimeoutExpired=subprocess.TimeoutExpired),
        )
        path = pathlib.Path(__file__).resolve().parents[1] / "main.py"
        tree = ast.parse(path.read_text(encoding="utf-8"))
        plugin = next(node for node in tree.body if isinstance(node, ast.ClassDef) and node.name == "Plugin")
        plugin.bases = []
        plugin.body = [node for node in plugin.body if getattr(node, "name", "") in
                       ("_run_handheld_firmware_command", "get_handheld_power_mode", "set_handheld_power_mode")]
        scope = {"asyncio": asyncio, "_runtime": self.runtime}
        exec(compile(ast.Module(body=[plugin], type_ignores=[]), str(path), "exec"), scope)
        self.plugin = scope["Plugin"]()

    def test_helper_releases_extraction_directory_before_waiting(self):
        result = self.plugin._run_handheld_firmware_command(["trusted-powershell", "fixed-encoded-script"], 6)
        self.assertEqual(result, (0, '{"available":false}', ""))
        self.assertEqual(self.events, ["dll-cleared", "dll-restored"])
        arguments, options = self.runtime.subprocess.Popen.call_args
        self.assertEqual(arguments, (["trusted-powershell", "fixed-encoded-script"],))
        self.assertEqual(options["env"], {"PYINSTALLER_RESET_ENVIRONMENT": "1"})
        self.assertEqual(options["creationflags"], 0x08000000)
        self.assertNotIn("shell", options)

    def test_timeout_reaps_only_the_owned_helper(self):
        self.child.communicate.side_effect = [subprocess.TimeoutExpired("owned-firmware-helper", 16), ("", "")]
        result = self.plugin._run_handheld_firmware_command(["trusted-powershell"], 16)
        self.assertEqual(result[0], -1)
        self.child.kill.assert_called_once_with()
        self.assertEqual(self.child.communicate.call_count, 2)
        self.assertEqual(self.events, ["dll-cleared", "dll-restored"])

    def test_public_rpc_forwards_to_background_provider(self):
        self.plugin._handheld_power_mode = Mock()
        self.plugin._handheld_power_mode.get.return_value = _REPORT
        self.plugin._handheld_power_mode.set.return_value = _REPORT | {"mode": 1}
        self.assertEqual(asyncio.run(self.plugin.get_handheld_power_mode()), _REPORT)
        request = {"token": "a" * 64, "mode": 1, "expected": 2}
        self.assertEqual(asyncio.run(self.plugin.set_handheld_power_mode(request))["mode"], 1)
        self.plugin._handheld_power_mode.set.assert_called_once_with(request)


# Execute the production PowerShell transaction against fake CIM/registry/signature
# functions. These fixtures never load or invoke an OEM firmware provider.
_POWERSHELL = r'''
$ErrorActionPreference = 'Stop'
$fixture = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__FIXTURE__')) | ConvertFrom-Json
$script:testMode = [int]$fixture.mode
$script:writes = [Collections.Generic.List[int]]::new()
$script:readsAfterWrite = 0
function Get-ItemProperty {
    param($LiteralPath)
    if ($LiteralPath -like '*\BIOS') {return [pscustomobject]@{SystemManufacturer='LENOVO';SystemProductName='83E1';BIOSVersion='test'}}
    return [pscustomobject]@{'(default)'=(Join-Path $env:SystemRoot 'System32\wbem\wmiprov.dll')}
}
function Method([int]$id, [string]$direction) {
    return [pscustomobject]@{Qualifiers=@{WmiMethodId=@{Value=$id};Implemented=@{Value=$true}};
        Parameters=@{Data=[pscustomobject]@{CimType='UInt32';Qualifiers=@{$direction=@{Value=$true}}}}}
}
function Get-CimClass {
    param($Namespace,$ClassName,$OperationTimeoutSec)
    if ($fixture.fault -eq 'slow') {Start-Sleep -Milliseconds 4100}
    $provider = 'WmiProv'; if ($fixture.fault -eq 'provider') {$provider = 'OtherProvider'}
    $methods = @{IsSupportSmartFan=(Method 43 'Out');GetSmartFanMode=(Method 45 'Out');SetSmartFanMode=(Method 44 'In')}
    if ($fixture.fault -eq 'method') {$methods.SetSmartFanMode = Method 99 'In'}
    if ($fixture.fault -eq 'parameter') {$methods.SetSmartFanMode.Parameters.Data.CimType='String'}
    return [pscustomobject]@{CimClassQualifiers=@{Provider=@{Value=$provider};Dynamic=@{Value=$true};
        guid=@{Value='{887B54E3-DDDC-4B2C-8B88-68A26A8835D0}'}};CimClassMethods=$methods}
}
function Get-CimInstance {
    param($Namespace,$ClassName,$Filter,$OperationTimeoutSec)
    if ($ClassName -eq '__Win32Provider') {return [pscustomobject]@{CLSID='{D2D588B5-D081-11d0-99E0-00C04FC2F8EC}';HostingModel='LocalSystemHost'}}
    $instance = [pscustomobject]@{Active=$true;InstanceName='ACPI\PNP0C14\test_0'}
    if ($fixture.fault -eq 'instances') {return @($instance,$instance)}
    return $instance
}
function Get-AuthenticodeSignature {
    param($LiteralPath)
    $status='Valid'; if ($fixture.fault -eq 'signature') {$status='NotSigned'}
    return [pscustomobject]@{Status=$status;SignerCertificate=@{Subject='CN=Microsoft Windows, O=Microsoft Corporation, C=US'}}
}
function Get-FileHash {param($LiteralPath,$Algorithm) return [pscustomobject]@{Hash=('A'*64)}}
function Invoke-CimMethod {
    param($InputObject,$MethodName,$OperationTimeoutSec,$Arguments)
    switch ($MethodName) {
        'IsSupportSmartFan' {
            $version=6; if($fixture.fault -eq 'version') {$version=7}
            return [pscustomobject]@{Data=[uint32]$version}
        }
        'GetSmartFanMode' {
            if ($script:writes.Count -gt 0) {
                $script:readsAfterWrite++
                if ($fixture.fault -eq 'final_external' -and $script:readsAfterWrite -ge 2) {$script:testMode=3}
            }
            return [pscustomobject]@{Data=[uint32]$script:testMode}
        }
        'SetSmartFanMode' {
            $script:writes.Add([int]$Arguments.Data)
            if ($fixture.fault -eq 'denied') {throw 'Access denied'}
            if ($fixture.fault -ne 'ignore') {$script:testMode=[int]$Arguments.Data}
            if ($fixture.fault -eq 'throw_after') {throw 'Failure after the write'}
            return [pscustomobject]@{}
        }
        default {throw 'Unexpected native operation'}
    }
}
'''


@unittest.skipUnless(os.name == "nt", "Windows PowerShell transaction fixture")
class LenovoPowerShellTransactionTests(unittest.TestCase):
    def run_native(self, request, mode=2, fault="", isolate=True):
        encode = lambda value: base64.b64encode(json.dumps(value).encode()).decode("ascii")
        script = _POWERSHELL.replace("__FIXTURE__", encode({"mode": mode, "fault": fault}))
        transaction = _SCRIPT.replace("param([Parameter(Mandatory=$true)][string]$RequestBase64)",
            "$RequestBase64='" + encode(request) + "'")
        if isolate:
            transaction = transaction.replace("Local\\Playhub.LenovoGoFirmware", "Local\\Playhub.LenovoGoFirmware.Test." + uuid.uuid4().hex)
        transaction = transaction.replace("$report | ConvertTo-Json -Compress -Depth 4",
            "$report['testWrites']=@($script:writes); $report | ConvertTo-Json -Compress -Depth 4")
        executable = pathlib.Path(os.environ.get("SystemRoot", r"C:\Windows")) / "System32/WindowsPowerShell/v1.0/powershell.exe"
        completed = subprocess.run([str(executable), "-NoProfile", "-NonInteractive", "-EncodedCommand",
            base64.b64encode((script + transaction).encode("utf-16le")).decode("ascii")],
            capture_output=True, text=True, timeout=8, creationflags=0x08000000)
        self.assertEqual(completed.returncode, 0, completed.stderr)
        return json.loads(completed.stdout.lstrip("\ufeff"))

    def test_read_and_verified_write_use_only_the_firmware_preset(self):
        read = self.run_native({"action": "get"})
        self.assertTrue(read["available"], read)
        self.assertEqual(read["testWrites"], [])
        changed = self.run_native({"action": "set", "token": read["token"], "expected": 2, "mode": 1})
        self.assertTrue(changed["ok"], changed)
        self.assertEqual((changed["mode"], changed["testWrites"]), (1, [1]))

    def test_changed_target_custom_profile_and_foreign_schema_never_write(self):
        for fault in ("provider", "method", "parameter", "instances", "signature", "version"):
            with self.subTest(fault=fault):
                report = self.run_native({"action": "set", "token": "a" * 64, "expected": 2, "mode": 1}, fault=fault)
                self.assertFalse(report["ok"], report)
                self.assertEqual(report["testWrites"], [])
        read = self.run_native({"action": "get"}, mode=255)
        self.assertTrue(read["available"])
        self.assertFalse(read["editable"])
        report = self.run_native({"action": "set", "token": read["token"], "expected": 255, "mode": 1}, mode=255)
        self.assertFalse(report["ok"])
        self.assertEqual((report["mode"], report["testWrites"]), (255, []))

    def test_failed_or_ignored_writes_do_not_report_success(self):
        read = self.run_native({"action": "get"})
        for fault, writes in (("ignore", [1]), ("denied", [1]), ("throw_after", [1, 2])):
            with self.subTest(fault=fault):
                report = self.run_native({"action": "set", "token": read["token"], "expected": 2, "mode": 1}, fault=fault)
                self.assertFalse(report["ok"], report)
                self.assertEqual((report["mode"], report["testWrites"]), (2, writes))

    def test_slow_preflight_leaves_mutation_and_rollback_budget_unused(self):
        read = self.run_native({"action": "get"})
        report = self.run_native({"action": "set", "token": read["token"], "expected": 2, "mode": 1}, fault="slow")
        self.assertEqual((report["ok"], report["code"], report["mode"]), (False, "preflight_timeout", 2))
        self.assertEqual(report["testWrites"], [])

    def test_final_read_preserves_competing_oem_selection_and_reports_change(self):
        read = self.run_native({"action": "get"})
        report = self.run_native({"action": "set", "token": read["token"], "expected": 2, "mode": 1}, fault="final_external")
        self.assertEqual((report["ok"], report["code"], report["mode"]), (False, "profile_changed", 3))
        self.assertEqual(report["testWrites"], [1])

    def test_production_mutex_blocks_other_process_before_firmware_access(self):
        executable = pathlib.Path(os.environ.get("SystemRoot", r"C:\Windows")) / "System32/WindowsPowerShell/v1.0/powershell.exe"
        holder_script = r'''
param([string]$ReadyPath)
$mutex=[Threading.Mutex]::new($false,'Local\Playhub.LenovoGoFirmware')
$locked=$false
try {
    try {$locked=$mutex.WaitOne(1000)} catch [Threading.AbandonedMutexException] {$locked=$true}
    if(!$locked) {exit 2}
    [IO.File]::WriteAllText($ReadyPath,'ready')
    Start-Sleep -Seconds 5
} finally {if($locked){$mutex.ReleaseMutex()};$mutex.Dispose()}
'''
        with tempfile.TemporaryDirectory(prefix="Playhub-Lenovo-Mutex-Test-") as directory:
            path = pathlib.Path(directory) / "holder.ps1"
            ready = pathlib.Path(directory) / "ready"
            path.write_text(holder_script, encoding="utf-8-sig")
            process = subprocess.Popen([str(executable), "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", str(path), "-ReadyPath", str(ready)],
                stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, creationflags=0x08000000)
            try:
                deadline = time.monotonic() + 3
                while not ready.exists() and process.poll() is None and time.monotonic() < deadline:
                    time.sleep(0.02)
                self.assertTrue(ready.exists(), "Could not acquire production firmware mutex in fixture")
                report = self.run_native({"action": "get"}, isolate=False)
                self.assertEqual((report["available"], report["ok"], report["code"], report["testWrites"]), (False, False, "busy", []))
            finally:
                if process.poll() is None:
                    process.terminate()
                process.wait(timeout=3)


if __name__ == "__main__":
    unittest.main()

import base64
import hashlib
import os
import unittest
from unittest.mock import patch

from tests.test_control_surface_faults import load_runtime


TOKEN = base64.b64encode(hashlib.sha256(b"fixture-internal-panel").digest()).decode()
OTHER_TOKEN = base64.b64encode(hashlib.sha256(b"removed-panel").digest()).decode()

WMI_FIXTURE = r'''
$script:level = 45
function Get-CimInstance {
    param($Namespace, $ClassName, $OperationTimeoutSec)
    if ($Namespace -ne 'root/wmi') { throw 'Unexpected WMI namespace' }
    if ($scenario -eq 'external') { return }
    if ($ClassName -eq 'WmiMonitorBrightness') {
        [pscustomobject]@{Active=$true; InstanceName='fixture-internal-panel'; Level=@(100,10,45,75,45); CurrentBrightness=$script:level}
        if ($scenario -eq 'ambiguous') {
            [pscustomobject]@{Active=$true; InstanceName='second-panel'; Level=@(10,45); CurrentBrightness=45}
        }
    }
    elseif ($ClassName -eq 'WmiMonitorBrightnessMethods') {
        if ($scenario -ne 'missing-method') { [pscustomobject]@{Active=$true; InstanceName='fixture-internal-panel'} }
        [pscustomobject]@{Active=$true; InstanceName='unrelated-panel'}
    }
}
function Invoke-CimMethod {
    param($InputObject, $MethodName, $OperationTimeoutSec, $Arguments)
    if ($InputObject.InstanceName -ne 'fixture-internal-panel' -or $MethodName -ne 'WmiSetBrightness' -or
        $Arguments.Brightness -ne 75 -or $scenario -in @('no-write','external','ambiguous','missing-method')) {
        throw 'Unexpected brightness write'
    }
    if ($scenario -eq 'write-denied') { return [pscustomobject]@{ReturnValue=5} }
    if ($scenario -ne 'write-no-change') { $script:level = $Arguments.Brightness }
    [pscustomobject]@{ReturnValue=0}
}
'''


class PanelBrightnessTests(unittest.TestCase):
    def setUp(self):
        self.runtime, directory = load_runtime()
        self.addCleanup(directory.cleanup)

    def test_invalid_values_never_spawn_powershell(self):
        with patch.object(self.runtime, "_run_cmd") as command:
            for token, level in (("'; exit", 75), (TOKEN, True), (TOKEN, -1), (TOKEN, 101), (TOKEN, 75.0)):
                self.assertEqual(self.runtime._panel_brightness_sync(token, level)["code"], "invalid_request")
            command.assert_not_called()

    @unittest.skipUnless(os.name == "nt", "runs the actual Windows PowerShell adapter with fake WMI")
    def test_windows_provider_identity_supported_levels_and_readback(self):
        source = self.runtime._PANEL_BRIGHTNESS_SCRIPT

        def run(scenario, token=None, level=None):
            with patch.object(self.runtime, "_PANEL_BRIGHTNESS_SCRIPT", f"$scenario='{scenario}'\n" + WMI_FIXTURE + source):
                return self.runtime._panel_brightness_sync(token, level)

        state = run("no-write")
        self.assertEqual((state["token"], state["levels"], state["level"]), (TOKEN, [10, 45, 75, 100], 45))
        for unsupported in ("external", "ambiguous", "missing-method"):
            with self.subTest(unsupported=unsupported):
                self.assertFalse(run(unsupported)["available"])
        self.assertEqual(run("no-write", OTHER_TOKEN, 75)["code"], "panel_changed")
        self.assertEqual(run("no-write", TOKEN, 46)["code"], "unsupported_level")
        self.assertTrue(run("no-write", TOKEN, 45)["ok"])
        applied = run("write", TOKEN, 75)
        self.assertTrue(applied["ok"])
        self.assertEqual(applied["level"], 75)
        for scenario, code in (("write-denied", "write_failed"), ("write-no-change", "not_confirmed")):
            with self.subTest(scenario=scenario):
                result = run(scenario, TOKEN, 75)
                self.assertEqual((result["ok"], result["level"], result["code"]), (False, 45, code))


if __name__ == "__main__":
    unittest.main()

"""Compile the shipped C# helper but invoke only its delegate-based transaction."""
import ast
import json
import os
from pathlib import Path
import subprocess
import re
import tempfile
import time
import unittest


ROOT = Path(__file__).resolve().parents[1]


class AudioRoleTransactionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        tree = ast.parse((ROOT / "quick_settings/main.py").read_text(encoding="utf-8"))
        script = next(ast.literal_eval(node.value) for node in tree.body
                      if isinstance(node, ast.Assign) and any(
                          isinstance(target, ast.Name) and target.id == "_AUDIO_HELPER_SCRIPT"
                          for target in node.targets))
        csharp = script.split('Add-Type -Language CSharp -TypeDefinition @"\n', 1)[1].split('\n"@', 1)[0]
        fixture = (ROOT / "tests/audio_role_transaction_fixture.cs").read_text(encoding="utf-8")
        # Compile the real COM declarations too, but never call Audio.SetDefault,
        # Enumerator, a volume method, or the production PowerShell script.
        command = "$ErrorActionPreference='Stop'\nAdd-Type -TypeDefinition @'\n" + csharp + "\n" + fixture + "\n'@\n"
        command += "@{ roles = @(0..16 | ForEach-Object { [AudioRoleTests.Fixture]::Run($_) }); microphone = @(0..4 | ForEach-Object { [AudioRoleTests.Fixture]::RunMicrophone($_) }) } | ConvertTo-Json -Depth 8 -Compress\n"
        executable = Path(os.environ["SystemRoot"]) / "System32/WindowsPowerShell/v1.0/powershell.exe"
        result = subprocess.run([str(executable), "-NoProfile", "-NonInteractive", "-Command",
                                 "& ([scriptblock]::Create([Console]::In.ReadToEnd()))"],
                                input=command, capture_output=True, text=True, timeout=45,
                                creationflags=subprocess.CREATE_NO_WINDOW)
        if result.returncode:
            raise AssertionError(result.stdout + result.stderr)
        decoded = json.loads(result.stdout)
        cls.results = decoded["roles"]
        cls.microphone = decoded["microphone"]

    def test_microphone_external_default_mismatch_never_calls_setter(self):
        result = self.microphone[0]
        self.assertFalse(result["Result"]["Ok"])
        self.assertIsNone(result["WrittenEndpoint"])

    def test_microphone_default_change_after_validation_cannot_redirect_write(self):
        result = self.microphone[1]
        self.assertTrue(result["Result"]["Ok"])
        self.assertEqual(result["DefaultEndpoint"], "mic-b")
        self.assertEqual(result["WrittenEndpoint"], "mic-a")
        self.assertEqual(result["Result"]["EndpointId"], "mic-a")

    def test_optional_microphone_endpoint_preserves_legacy_contract(self):
        self.assertTrue(self.microphone[2]["Result"]["Ok"])

    def test_microphone_error_and_bad_readback_never_claim_success(self):
        for scenario in (3, 4):
            self.assertFalse(self.microphone[scenario]["Result"]["Ok"])

    def test_success_confirms_every_role(self):
        result = self.results[0]
        self.assertTrue(result["Result"]["Ok"])
        self.assertEqual(result["State"], ["new"] * 3)
        self.assertFalse(result["Result"]["RollbackAttempted"])

    def test_each_hresult_failure_before_and_after_mutation_restores_distinct_baselines(self):
        for scenario in range(1, 7):
            with self.subTest(scenario=scenario):
                result = self.results[scenario]
                self.assertFalse(result["Result"]["Ok"])
                self.assertTrue(result["Result"]["RollbackVerified"])
                self.assertEqual(result["State"], ["console", "multimedia", "communications"])

    def test_failed_restore_is_reported_and_other_roles_still_restore(self):
        result = self.results[7]
        self.assertTrue(result["Result"]["RecoveryRequired"])
        self.assertFalse(result["Result"]["RollbackVerified"])
        self.assertEqual(result["State"], ["console", "new", "communications"])

    def test_external_selection_is_preserved(self):
        result = self.results[8]
        self.assertTrue(result["Result"]["RecoveryRequired"])
        self.assertEqual(result["State"], ["external", "multimedia", "communications"])
        self.assertNotIn("0:console", result["Writes"])

    def test_missing_baseline_prevents_all_writes(self):
        result = self.results[9]
        self.assertFalse(result["Result"]["Ok"])
        self.assertEqual(result["Writes"], [])

    def test_hresult_success_without_readback_is_rolled_back(self):
        result = self.results[10]
        self.assertFalse(result["Result"]["Ok"])
        self.assertTrue(result["Result"]["RollbackVerified"])
        self.assertEqual(result["State"], ["console", "multimedia", "communications"])

    def test_already_selected_roles_are_not_written(self):
        self.assertTrue(self.results[11]["Result"]["Ok"])
        self.assertEqual(self.results[11]["Writes"], [])

    def test_exception_after_mutation_is_rolled_back(self):
        result = self.results[12]
        self.assertTrue(result["Result"]["RollbackVerified"])
        self.assertEqual(result["State"], ["console", "multimedia", "communications"])

    def test_unreadable_role_does_not_prevent_other_role_restoration(self):
        result = self.results[13]
        self.assertTrue(result["Result"]["RecoveryRequired"])
        self.assertEqual(result["State"], ["console", "new", "communications"])

    def test_wrong_or_unknown_flow_never_writes_any_role(self):
        for scenario in (14, 15, 16):
            with self.subTest(scenario=scenario):
                result = self.results[scenario]
                self.assertFalse(result["Result"]["Ok"])
                self.assertEqual(result["Writes"], [])
                self.assertEqual(result["State"], ["console", "multimedia", "communications"])


class CpuPackagingTests(unittest.TestCase):
    def test_cpu_package_is_not_copied(self):
        script = (ROOT / "build-plugin.bat").read_text(encoding="utf-8")
        self.assertNotIn('"cpu_power', script)

    def test_no_npm_fallback_checks_actual_helper_not_just_bundle_timestamp(self):
        batch = (ROOT / "build-plugin.bat").read_text(encoding="utf-8")
        fallback = batch.split('\n:no_npm\n', 1)[1]
        block = fallback.split('powershell -NoProfile -ExecutionPolicy Bypass -Command ^', 1)[1]
        parts = []
        for line in block.splitlines():
            match = re.match(r'^\s*"([^"]*)"', line)
            if match:
                parts.append(match.group(1))
            elif parts:
                break
        self.assertTrue(parts)
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "cpu_power"
            source.mkdir()
            payload = root / "payload"
            (payload / "dist").mkdir(parents=True)
            bundle = payload / "dist/index.js"
            bundle.write_text("fixture bundle")
            # The fallback validates all runtime helpers before the CPU package.
            for relative in (
                "LICENSE-Shortcuts", "main.py", "THIRD-PARTY-NOTICES.md", "quick_settings/main.py",
                "quick_settings/history_editorial.json", "quick_settings/history_images.json",
                "quick_settings/history_days.json", "quick_settings/history_imported.json",
                "quick_settings/LICENSE", "quick_settings/NOTICE", "quick_settings/LEGAL.md",
                "quick_settings/THIRD-PARTY-NOTICES.md", "quick_settings/licenses/license.txt",
                "quick_settings/helper/apply_perf.ps1", "quick_settings/bin/QuickSettingsAgent.exe",
                "quick_settings/amd/adlx_helper.exe", "quick_settings/amd/ADLXCSharpBind.dll",
                "quick_settings/amd/LICENSES.txt", "quick_settings/amd/build_amd.bat",
            ):
                for base in (root, payload):
                    item = base / relative
                    item.parent.mkdir(parents=True, exist_ok=True)
                    item.write_bytes(("fixture " + relative).encode("ascii"))
            for name in ("__init__.py", "capability.py", "README.md"):
                (source / name).write_text("fixture " + name)
            future = time.time() + 60
            os.utime(bundle, (future, future))
            command = "".join(parts).replace("%ASSETS%", str(payload))
            powershell = Path(os.environ["SystemRoot"]) / "System32/WindowsPowerShell/v1.0/powershell.exe"
            environment = dict(os.environ)
            environment["PSModulePath"] = str(powershell.parent / "Modules")
            # A child of PowerShell 7 can inherit module-analysis state from another
            # runtime. Load the Windows module explicitly in this isolated fixture.
            utility = powershell.parent / "Modules/Microsoft.PowerShell.Utility/Microsoft.PowerShell.Utility.psd1"
            command = "Import-Module '" + str(utility).replace("'", "''") + "';" + command
            def check():
                self.last_check = subprocess.run([str(powershell), "-NoProfile", "-NonInteractive",
                    "-ExecutionPolicy", "Bypass", "-Command", command],
                    cwd=root, env=environment, capture_output=True, text=True, errors="replace", timeout=15,
                    creationflags=subprocess.CREATE_NO_WINDOW)
                return self.last_check.returncode
            helper = payload / "quick_settings/helper/apply_perf.ps1"
            self.assertEqual(check(), 0, self.last_check.stdout + self.last_check.stderr)
            helper.write_text("outdated helper")
            self.assertEqual(check(), 1, "Mismatched helper must fail regardless of bundle timestamp")
            helper.unlink()
            self.assertEqual(check(), 1, "Missing helper must fail")



if __name__ == "__main__":
    unittest.main()

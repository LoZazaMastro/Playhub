import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import Mock
from quick_settings import support_report


class SupportReportTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        self.root = Path(self.folder.name)
        self.app = self.root / "Playhub.exe"
        self.app.write_bytes(b"test placeholder; never executed")
        (self.root / "Assets").mkdir()
        (self.root / "Assets/support-report-v1.json").write_text('{"protocol":1}')
        self.report = self.root / "report.txt"
        self.report.write_text("PLAYHUB DIAGNOSTIC REPORT\ncomplete log", encoding="utf-8")

    def runner(self, args, **kwargs):
        self.assertEqual(args[:2], [str(self.app), "diagnostics-report"])
        self.assertEqual(len(args), 3)
        self.assertRegex(args[2], r"^[0-9a-f]{32}$")
        self.assertNotIn("shell", kwargs)
        receipt = self.root / "DiagnosticsRequests" / (args[2] + ".json")
        receipt.parent.mkdir(exist_ok=True)
        receipt.write_text(json.dumps({"ok": True, "path": str(self.report)}))
        return Mock(returncode=0)

    def generate(self, runner=None):
        return support_report.generate(app=self.app, local_root=self.root, runner=runner or self.runner)

    def test_shared_app_command_returns_one_report_and_removes_internal_receipt(self):
        result = self.generate()
        self.assertTrue(result["ok"])
        self.assertEqual(result["path"], str(self.report))
        self.assertEqual(list((self.root / "DiagnosticsRequests").iterdir()), [])

    def test_old_app_is_not_launched_with_an_unknown_command(self):
        (self.root / "Assets/support-report-v1.json").unlink()
        runner = Mock()
        self.assertFalse(self.generate(runner)["ok"])
        runner.assert_not_called()

    def test_timeout_and_process_failure_are_not_success(self):
        for runner in (Mock(side_effect=subprocess.TimeoutExpired("test", 180)), Mock(return_value=Mock(returncode=1))):
            self.assertFalse(self.generate(runner)["ok"])

    def test_unexpected_report_content_is_rejected(self):
        self.report.write_text("not the app diagnostic")
        self.assertFalse(self.generate()["ok"])

    def test_parallel_generation_does_not_spawn_duplicate_process(self):
        support_report._gate.acquire()
        try:
            runner = Mock()
            self.assertFalse(self.generate(runner)["ok"])
            runner.assert_not_called()
        finally:
            support_report._gate.release()

import ast
import asyncio
import json
import pathlib
import types
import unittest
from unittest.mock import Mock


class DeviceInfoTests(unittest.TestCase):
    def setUp(self):
        path = pathlib.Path(__file__).resolve().parents[1] / "main.py"
        tree = ast.parse(path.read_text(encoding="utf-8"))
        plugin = next(node for node in tree.body if isinstance(node, ast.ClassDef) and node.name == "Plugin")
        plugin.bases = []
        plugin.body = [node for node in plugin.body if getattr(node, "name", "") in ("_get_windows_device_details", "open_windows_update")]
        self.run = Mock(return_value=(0, "{}", ""))
        self.start = Mock()
        self.os = types.SimpleNamespace(name="nt", startfile=self.start)
        scope = {"json": json, "asyncio": asyncio, "os": self.os,
                 "_runtime": types.SimpleNamespace(_run_cmd=self.run, _powershell_path=lambda: "powershell.exe")}
        exec(compile(ast.Module(body=[plugin], type_ignores=[]), str(path), "exec"), scope)
        self.plugin = scope["Plugin"]()
        self.foreground = Mock(return_value=True)
        self.plugin._windows_update_foreground = self.foreground

    def test_details_preserve_edition_version_gpu_and_capacity(self):
        self.run.return_value = (0, json.dumps({"device_name": " Gaming PC ", "gpu": ["RX 9070 XT", "RX 9070 XT"],
            "windows_edition": "Microsoft Windows 11 Pro", "windows_version": "10.0.26100", "storage_total_bytes": 2000000000000}), "")
        info = self.plugin._get_windows_device_details()
        self.assertEqual(info["device_name"], "Gaming PC")
        self.assertEqual(info["gpu"], ["RX 9070 XT"])
        self.assertEqual(info["windows_edition"], "Microsoft Windows 11 Pro")
        self.assertEqual(info["windows_version"], "10.0.26100")
        self.assertEqual(info["storage_total_bytes"], 2000000000000)
        args = self.run.call_args.args[0]
        self.assertIn("-NonInteractive", args)
        self.assertIn("DriveType=3", args[-1])
        self.assertEqual(self.run.call_args.kwargs["timeout"], 15)

    def test_missing_or_invalid_capacity_never_becomes_zero_or_fake_total(self):
        for value in (None, -1, 0, True, "1000", float("nan"), float("inf")):
            self.run.return_value = (0, json.dumps({"gpu": "RX 9070 XT", "storage_total_bytes": value}), "")
            result = self.plugin._get_windows_device_details()
            self.assertNotIn("storage_total_bytes", result)
            self.assertEqual(result["gpu"], ["RX 9070 XT"])

    def test_query_failure_and_bad_json_are_partial_not_exceptions(self):
        for result in ((1, "", "failed"), (0, "garbage", ""), (0, "[]", "")):
            self.run.return_value = result
            self.assertEqual(self.plugin._get_windows_device_details(), {})

    def test_update_opens_only_fixed_uri_and_no_other_action(self):
        self.assertEqual(asyncio.run(self.plugin.open_windows_update()), {"ok": True})
        self.start.assert_called_once_with("ms-settings:windowsupdate")
        self.foreground.assert_called_once_with()
        self.run.assert_not_called()
        with self.assertRaises(TypeError):
            asyncio.run(self.plugin.open_windows_update("ms-settings:other"))

    def test_update_failure_and_non_windows_are_reported(self):
        self.start.side_effect = OSError("not available")
        self.assertEqual(asyncio.run(self.plugin.open_windows_update())["code"], "open_failed")
        self.os.name = "posix"
        self.start.reset_mock()
        self.assertEqual(asyncio.run(self.plugin.open_windows_update())["code"], "windows_only")
        self.start.assert_not_called()

    def test_uri_launch_is_not_success_without_verified_foreground(self):
        self.foreground.return_value = False
        self.assertEqual(asyncio.run(self.plugin.open_windows_update()), {"ok": False, "code": "foreground_failed"})

    def test_foreground_error_is_not_misreported_as_uri_launch_failure(self):
        self.foreground.side_effect = OSError("focus denied")
        self.assertEqual(asyncio.run(self.plugin.open_windows_update()), {"ok": False, "code": "foreground_failed"})


if __name__ == "__main__":
    unittest.main()

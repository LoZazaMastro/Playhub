"""Offline tests of the exact proposed RPC without importing main.py."""
import asyncio
import hashlib
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from controller_porting.passive_diagnostics import passive_diagnostics, PassiveControllerDiagnosticsMixin, FEATURES


class PassiveTests(unittest.TestCase):
    def test_default_rpc_is_passive_and_has_no_renderer_path_argument(self):
        endpoint = PassiveControllerDiagnosticsMixin()
        with patch("ctypes.CDLL", side_effect=AssertionError("native load")):
            result = asyncio.run(endpoint.get_controller_sdl_diagnostics())
        self.assertEqual(result["status"], "not_configured")
        self.assertEqual(result["capabilities"], dict.fromkeys(FEATURES, False))
        self.assertFalse(result["nativeLoaded"])
        self.assertFalse(result["deviceProbed"])
        with self.assertRaises(TypeError):
            endpoint.get_controller_sdl_diagnostics("attacker path")

    def test_non_dll_file_is_only_file_evidence(self):
        with tempfile.TemporaryDirectory(dir=Path(__file__).parent) as folder:
            file = Path(folder) / "SDL3.dll"
            file.write_bytes(b"deliberately not executable")
            endpoint = PassiveControllerDiagnosticsMixin()
            endpoint.controller_sdl_diagnostic_path = file
            with patch("ctypes.CDLL", side_effect=AssertionError("native load")):
                result = asyncio.run(endpoint.get_controller_sdl_diagnostics())
            self.assertEqual(result["status"], "file_observed")
            self.assertEqual(result["sha256"], hashlib.sha256(file.read_bytes()).hexdigest())
            self.assertFalse(any(result["capabilities"].values()))
            self.assertNotIn("path", result)
            self.assertEqual(file.read_bytes(), b"deliberately not executable")

    def test_errors_do_not_become_unsupported_hardware(self):
        for path in ["relative.dll", 32, "\\\\server\\share\\SDL3.dll"]:
            self.assertEqual(passive_diagnostics(path)["status"], "invalid_path")
        self.assertEqual(passive_diagnostics(Path(__file__).parent)["status"], "invalid_path")
        self.assertEqual(passive_diagnostics(Path(__file__).parent / "absent.dll")["status"], "file_missing")
        with patch("pathlib.Path.stat", side_effect=PermissionError()):
            self.assertEqual(passive_diagnostics(Path(__file__))["status"], "access_denied")

    def test_bound_and_changed_file_fail_closed(self):
        with patch("controller_porting.passive_diagnostics.MAX_BYTES", 1):
            result = passive_diagnostics(Path(__file__))
            self.assertEqual(result["status"], "file_too_large")
            self.assertIsNone(result["sha256"])
        original = Path(__file__).stat()
        from types import SimpleNamespace
        altered = SimpleNamespace(**{name: getattr(original, name) for name in
                                     ["st_dev", "st_ino", "st_size", "st_mtime_ns", "st_ctime_ns"]})
        altered.st_size += 1
        with patch("os.fstat", side_effect=[original, altered]):
            self.assertEqual(passive_diagnostics(Path(__file__))["status"], "file_changed")


if __name__ == "__main__":
    unittest.main()

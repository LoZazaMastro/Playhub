"""Every control must fail loudly and specifically.

Covers the four faults reported from a real Windows 11 / RX 9070 XT / LG CX
build: audio changes always reporting "audio devices unavailable", resolution
and refresh rate silently doing nothing, HDR leaving the panel on no signal,
and the Windows power mode never actually moving.
"""
import importlib.util
import logging
import pathlib
import sys
import tempfile
import types
import unittest
from unittest.mock import Mock, patch


def load_runtime():
    """Load quick_settings/main.py on its own: these tests exercise the raw
    Windows adapters, not the display transaction coordinator."""
    directory = tempfile.TemporaryDirectory()
    decky = types.ModuleType("decky")
    decky.DECKY_PLUGIN_SETTINGS_DIR = str(pathlib.Path(directory.name) / "playhub")
    decky.logger = logging.getLogger("control-surface-test")
    sys.modules["decky"] = decky
    path = pathlib.Path(__file__).resolve().parents[1] / "quick_settings" / "main.py"
    spec = importlib.util.spec_from_file_location("control_surface_runtime", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module, directory


class ControlSurfaceFaultTests(unittest.TestCase):
    def setUp(self):
        self.runtime, directory = load_runtime()
        self.addCleanup(directory.cleanup)
        self.runtime._audio_cache_clear()
        self.addCleanup(self.runtime._audio_cache_clear)

    # ----------------------------- audio ------------------------------ #
    def test_helper_failure_is_an_error_code_not_an_empty_device_list(self):
        for action in ("get", "set"):
            with self.subTest(action=action):
                with patch.object(self.runtime.os, "name", "nt"), \
                        patch.object(self.runtime, "_powershell_path", return_value="powershell.exe"), \
                        patch("subprocess.run", side_effect=OSError("helper is missing")):
                    result = (self.runtime._get_audio_devices_sync() if action == "get"
                              else self.runtime._set_audio_device_sync("output", "speaker-a"))
                self.assertFalse(result["ok"])
                # A broken call must never be presented as "no audio devices".
                self.assertNotEqual(result.get("code", ""), "")
                self.assertIn("helper is missing", result["message"])
                self.runtime._audio_cache_clear()

    def test_working_helper_with_no_endpoints_is_a_success_with_no_error_code(self):
        empty = {"ok": True, "outputs": [], "inputs": [], "default_output_id": "",
                 "default_input_id": "", "input_volume": 0}
        with patch.object(self.runtime, "_run_audio_powershell", return_value=dict(empty)):
            result = self.runtime._get_audio_devices_sync()
        self.assertTrue(result["ok"])
        self.assertEqual(result.get("code", ""), "")
        self.assertEqual(result["outputs"], [])

    def test_endpoint_id_readback_is_case_insensitive(self):
        # The picker builds ids from the registry, the readback comes from
        # IMMDevice::GetId, and the two do not agree on GUID casing. Comparing
        # them verbatim failed every single change.
        requested = "{0.0.0.00000000}.{AB12CD34-0000-0000-0000-000000000001}"
        readback = requested.lower()
        with patch.object(self.runtime, "_run_audio_powershell",
                          return_value={"ok": True, "default_output_id": readback}):
            result = self.runtime._set_audio_device_sync("output", requested)
        self.assertTrue(result["ok"], result)
        self.assertEqual(result["code"], "")

    def test_rejected_endpoint_change_keeps_its_own_reason(self):
        with patch.object(self.runtime, "_run_audio_powershell",
                          return_value={"ok": False, "message": "Audio role write was not confirmed.",
                                        "default_output_id": "speaker-a"}):
            result = self.runtime._set_audio_device_sync("output", "speaker-b")
        self.assertFalse(result["ok"])
        self.assertEqual(result["code"], "audio_change_rejected")
        self.assertIn("not confirmed", result["message"])

    # ------------------------ display mode ---------------------------- #
    def test_resolution_only_request_picks_a_refresh_rate_that_exists(self):
        modes = [{"width": 1920, "height": 1080, "hz": 60},
                 {"width": 3840, "height": 2160, "hz": 60},
                 {"width": 3840, "height": 2160, "hz": 120}]
        with patch.object(self.runtime.os, "name", "nt"):
            with patch.object(self.runtime, "_enum_display_modes", return_value=modes):
                exact, message, code = self.runtime._resolve_display_mode_sync(
                    {"width": 3840, "height": 2160, "hz": 120}, "display-a")
                self.assertEqual(exact, {"width": 3840, "height": 2160, "hz": 120})
                self.assertEqual((message, code), ("", ""))
                # 144 Hz is not offered: take the closest the resolution has.
                nearest, _, _ = self.runtime._resolve_display_mode_sync(
                    {"width": 3840, "height": 2160, "hz": 144}, "display-a")
                self.assertEqual(nearest, {"width": 3840, "height": 2160, "hz": 120})
                missing, message, code = self.runtime._resolve_display_mode_sync(
                    {"width": 5120, "height": 2880, "hz": 60}, "display-a")
                self.assertIsNone(missing)
                self.assertEqual(code, "display_mode_unsupported")
                self.assertIn("5120", message)

    def test_mode_enumeration_targets_the_device_being_driven(self):
        with patch.object(self.runtime.os, "name", "nt"):
            with patch.object(self.runtime, "_enum_display_modes", return_value=[]) as enumerate_modes:
                mode, _, code = self.runtime._resolve_display_mode_sync(
                    {"width": 1920, "height": 1080, "hz": 60}, "display-a")
        enumerate_modes.assert_called_once_with("display-a")
        self.assertIsNone(mode)
        self.assertEqual(code, "display_enumeration_failed")

    # -------------------------- HDR timing ---------------------------- #
    def test_hdr_readback_window_outlasts_an_hdmi_renegotiation(self):
        # An LG OLED can take the better part of ten seconds to bring the link
        # back up. Judging the write before then and toggling HDR back is what
        # left the panel on "no signal".
        self.assertGreaterEqual(self.runtime.HDR_VERIFY_TIMEOUT, 10.0)
        self.assertEqual(
            self.runtime._wait_for_hdr_states.__defaults__[0], self.runtime.HDR_VERIFY_TIMEOUT)

    # ------------------------- power mode ----------------------------- #
    def _overlay(self, write_status, observed):
        calls = {"set": 0}

        def set_overlay(_guid):
            calls["set"] += 1
            return write_status

        powrprof = unittest.mock.Mock()
        powrprof.PowerSetActiveOverlayScheme = unittest.mock.Mock(side_effect=set_overlay)
        with patch.object(self.runtime.os, "name", "nt"):
            with patch.object(self.runtime.ctypes, "windll", create=True) as windll, \
                    patch.object(self.runtime, "_get_power_overlay", return_value=observed), \
                    patch.object(self.runtime.time, "sleep"):
                windll.powrprof = powrprof
                result = self.runtime._set_power_overlay_sync("best")
        return result, calls

    def test_power_mode_accepted_but_ignored_by_windows_is_not_a_success(self):
        # PowerSetActiveOverlayScheme returns ERROR_SUCCESS even when the active
        # power plan makes Windows 11 ignore the overlay entirely.
        result, calls = self._overlay(0, "balanced")
        self.assertEqual(calls["set"], 1)
        self.assertFalse(result["ok"])
        self.assertEqual(result["code"], "power_mode_not_applied")
        self.assertEqual(result["power_mode"], "balanced")
        self.assertIn("power plan", result["message"])

    def test_power_mode_confirmed_by_readback_is_a_success(self):
        result, _ = self._overlay(0, "best")
        self.assertTrue(result["ok"], result)
        self.assertEqual(result["power_mode"], "best")

    def test_unreadable_power_overlay_is_not_reported_as_balanced(self):
        with patch.object(self.runtime.os, "name", "nt"):
            with patch.object(self.runtime.ctypes, "windll", create=True) as windll:
                windll.powrprof.PowerGetActualOverlayScheme.return_value = 1
                self.assertIsNone(self.runtime._get_power_overlay())
                status = self.runtime._get_performance_status_sync()
        self.assertFalse(status["detected"])
        self.assertIn("did not report", status["message"])


if __name__ == "__main__":
    unittest.main()

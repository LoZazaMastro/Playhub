"""Driver/capture state transitions with every native boundary blocked."""
import asyncio
import unittest
from contextlib import nullcontext
from unittest.mock import Mock, patch

import test_display_entrypoints as entrypoints


class GlobalGraphicsAndLosslessTests(unittest.TestCase):
    def setUp(self):
        fixture = entrypoints.DisplayEntrypointTests()
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        self.plugin = fixture.plugin
        self.r = fixture.module._runtime
        fixture.stack.enter_context(patch.object(self.r, "_read_lossless_settings", return_value={
            "settings": dict(self.r.LS_PROFILE_DEFAULTS), "scaling_active": False}))

    def driver_result(self, feature, value, **extra):
        return {"ok": True, "confirmed": True, "normalized_value": value,
                "requested_operations": [{"feature": feature, "value": value}], **extra}

    def test_all_global_radeon_controls_reach_driver_without_running_game(self):
        self.plugin._last_runtime = {"app_id": 0, "title": ""}
        for feature, value in (("afmf", True), ("rsr", True), ("antilag", True),
                               ("chill", False), ("boost", True), ("enhanced_sync", True),
                               ("sharpening", True), ("display_brightness", 14)):
            with self.subTest(feature=feature), patch.object(
                    self.r, "_set_amd_sync", return_value=self.driver_result(feature, value)) as write:
                result = asyncio.run(self.plugin.set_amd({"feature": feature, "value": value}))
                self.assertTrue(result["ok"])
                self.assertEqual(write.call_args.args[:2], (feature, value))

    def test_global_edit_wins_immediately_over_active_game_profile_and_survives_restore(self):
        r = self.r
        baseline = r._normalize_amd_profile({"afmf": False})
        profile = r._normalize_amd_profile({"afmf": False})
        r._AMD_PROFILE_RUNTIME.update(active=True, active_app_id=42, baseline=baseline,
                                      profile=profile, global_overrides={})
        with patch.object(r, "_set_amd_sync", return_value=self.driver_result("afmf", True)) as write:
            self.plugin._set_amd_global_sync("afmf", "on")
        self.assertEqual(write.call_args.args[:2], ("afmf", "on"))
        self.assertTrue(r._AMD_PROFILE_RUNTIME["baseline"]["afmf"])
        with patch.object(r, "_apply_amd_profile_sync", return_value={"ok": True}) as apply:
            self.assertTrue(self.plugin._activate_amd_profile_sync(profile, 42, "Game")["unchanged"])
            apply.assert_not_called()
            self.assertTrue(self.plugin._restore_amd_profile_sync("game_stopped")["ok"])
            self.assertTrue(apply.call_args.args[0]["afmf"])
        self.assertEqual(r._AMD_PROFILE_RUNTIME["global_overrides"], {})

    def test_failed_or_unconfirmed_global_edit_does_not_replace_restore_baseline(self):
        r = self.r
        for result in ({"ok": False}, {"ok": True, "confirmed": False}):
            with self.subTest(result=result):
                r._AMD_PROFILE_RUNTIME.update(active=True, baseline={"afmf": False},
                                              profile={"afmf": False}, global_overrides={})
                with patch.object(r, "_set_amd_sync", return_value=result):
                    self.plugin._set_amd_global_sync("afmf", "on")
                self.assertEqual(r._AMD_PROFILE_RUNTIME["baseline"], {"afmf": False})
                self.assertEqual(r._AMD_PROFILE_RUNTIME["global_overrides"], {})

    def test_confirmed_global_edit_survives_pending_profile_recovery(self):
        r = self.r
        r._AMD_PROFILE_RUNTIME.update(active=False, recovery_required=True,
            baseline=r._normalize_amd_profile({"afmf": False}), profile={}, global_overrides={})
        with patch.object(r, "_set_amd_sync", return_value=self.driver_result("afmf", True)):
            self.plugin._set_amd_global_sync("afmf", "on")
        with patch.object(r, "_apply_amd_profile_sync", return_value={"ok": True}) as restore:
            self.assertTrue(self.plugin._restore_amd_profile_sync("game_stopped")["ok"])
            self.assertTrue(restore.call_args.args[0]["afmf"])
        self.assertEqual(r._AMD_PROFILE_RUNTIME["baseline"], {})

    def test_confirmed_mutual_exclusion_is_preserved_after_game_exit(self):
        r = self.r
        r._AMD_PROFILE_RUNTIME.update(active=True, baseline={"chill": True, "antilag": False},
                                      profile={"chill": True, "antilag": False}, global_overrides={})
        result = self.driver_result("antilag", True, requested_operations=[
            {"feature": "chill", "value": False}, {"feature": "antilag", "value": True}])
        with patch.object(r, "_set_amd_sync", return_value=result):
            self.plugin._set_amd_global_sync("antilag", "on")
        self.assertEqual(r._AMD_PROFILE_RUNTIME["baseline"], {"chill": False, "antilag": True})

    def capture(self):
        r = self.r
        dll = Mock()
        r._LS_CORE.update(initialized=True, dll=dll, last_status={"status": 2}, capture_requested=True)
        r._LS_RUNTIME.update(active=True, active_app_id=42, active_title="Game", watchdog_token="owned")
        return dll

    def test_stop_timeout_keeps_active_state_and_prevents_restart(self):
        r = self.r
        self.capture()
        with patch.object(r, "_lossless_dpi_scope", side_effect=nullcontext), \
             patch.object(r.time, "monotonic", side_effect=[0, 6]), \
             patch.object(r, "_find_lossless", return_value="fixture.exe"), \
             patch.object(r, "_external_lossless_ui_pids", return_value=[]), \
             patch.object(r, "_lossless_core_target") as target:
            result = r._activate_lossless_managed_sync(app_id=42, force_restart=True)
        self.assertFalse(result["ok"])
        self.assertTrue(result["active"])
        self.assertFalse(result["stop_confirmed"])
        self.assertTrue(r._LS_RUNTIME["active"])
        target.assert_not_called()

    def test_stop_waits_for_actual_native_idle_receipt(self):
        r = self.r
        dll = self.capture()
        def stopped(_):
            r._lossless_core_status_callback(0, 0, 0, 0, 0, 0, False, 1.0, 0)
        dll.Activate.side_effect = stopped
        with patch.object(r, "_lossless_dpi_scope", side_effect=nullcontext):
            result = r._deactivate_lossless_capture_sync()
        self.assertTrue(result["ok"])
        self.assertTrue(result["stop_confirmed"])
        self.assertFalse(result["active"])
        self.assertEqual(r._LS_RUNTIME["watchdog_token"], "")

    def test_late_active_callback_after_stop_cannot_rearm_capture_or_overlay_resume(self):
        r = self.r
        r._LS_CORE.update(capture_requested=False)
        r._STEAM_OVERLAY_STATE["active"] = True
        r._LS_RUNTIME["active"] = False
        with patch.object(r.threading, "Thread") as thread:
            r._lossless_core_status_callback(1, 22, 800, 600, 800, 600, False, 1.0, 0)
        self.assertEqual(r._LS_CORE["last_status"]["status"], 1)
        self.assertFalse(r._LS_RUNTIME["active"])
        thread.assert_not_called()

    def test_failed_native_stop_is_not_reported_as_successful_managed_stop(self):
        r = self.r
        self.capture()
        with patch.object(r, "_lossless_dpi_scope", side_effect=nullcontext), \
             patch.object(r.time, "monotonic", side_effect=[0, 6]), \
             patch.object(r, "_stop_owned_lossless_ui", return_value=""), \
             patch.object(r, "_lossless_running", return_value=False), \
             patch.object(r, "_remove_lossless_autoscale_profiles", return_value={"ok": True}):
            result = r._stop_lossless_managed_sync()
        self.assertFalse(result["ok"])
        self.assertTrue(result["active"])
        self.assertEqual(r._LS_RUNTIME["active_app_id"], 42)
        r._LS_SESSION_CONTROL["resume_requested"] = True
        with patch.object(r, "_find_lossless", return_value="fixture.exe"), \
             patch.object(r, "_lossless_running", return_value=False), \
             patch.object(r, "_dpi_awareness_snapshot", return_value={}):
            status = r._get_lossless_status_sync()
        self.assertEqual(status["capture_state"], "failed")
        self.assertFalse(status["capture_active"])

    def test_overlay_close_waits_for_returned_game_focus_in_background(self):
        r = self.r
        r._LS_SESSION_CONTROL.update(resume_requested=True, resume_app_id=42,
            resume_title="Game", resume_settings={}, resume_filters=["game.exe"])
        with patch.object(r, "_schedule_lossless_autostart", return_value={"ok": True, "pending": True}) as gate, \
             patch.object(r, "_activate_lossless_managed_sync") as activate:
            result = r._resume_lossless_after_overlay_sync()
        gate.assert_called_once_with({}, 42, "Game", ["game.exe"])
        activate.assert_not_called()
        self.assertTrue(result["pending"])
        self.assertFalse(result["resumed"])

    def test_disable_cancels_queued_resume_even_after_menu_closes(self):
        r = self.r
        r._LS_SESSION_CONTROL.update(resume_requested=True, resume_app_id=42)
        with patch.object(r, "_stop_lossless_managed_sync", return_value={"ok": True, "active": False}), \
             patch.object(r, "_schedule_lossless_autostart") as gate:
            r._set_lossless_scaling_sync(False, 42, "Game")
            result = r._resume_lossless_after_overlay_sync()
        self.assertFalse(result["resumed"])
        gate.assert_not_called()

    def test_status_separates_confirmed_capture_from_requested_or_suspended_capture(self):
        r = self.r
        self.capture()
        with patch.object(r, "_find_lossless", return_value="fixture.exe"), \
             patch.object(r, "_lossless_running", return_value=False), \
             patch.object(r, "_dpi_awareness_snapshot", return_value={}):
            r._LS_CORE["last_status"] = {"status": 1}
            status = r._get_lossless_status_sync()
            self.assertEqual(status["capture_state"], "suspended")
            self.assertTrue(status["scaling_requested"])
            self.assertFalse(status["capture_active"])
            r._LS_CORE["last_status"] = {"status": 2, "fullscreen_output_verified": True}
            status = r._get_lossless_status_sync()
            self.assertEqual(status["capture_state"], "active")
            self.assertTrue(status["capture_active"])
            r._LS_RUNTIME.update(active=False, last_error="Capture failed")
            r._LS_CORE["last_status"] = {"status": 0}
            status = r._get_lossless_status_sync()
            self.assertEqual(status["capture_state"], "failed")
            self.assertFalse(status["scaling_requested"])


if __name__ == "__main__":
    unittest.main()

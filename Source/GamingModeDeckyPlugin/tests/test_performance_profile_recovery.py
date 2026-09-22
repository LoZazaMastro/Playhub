"""AMD profile lifecycle with native calls blocked by the shared fixture."""
import unittest
from unittest.mock import patch

import test_display_entrypoints as entrypoints


class ProfileRecoveryTests(unittest.TestCase):
    def setUp(self):
        self.fixture = entrypoints.DisplayEntrypointTests()
        self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        self.plugin = self.fixture.plugin
        self.runtime = self.fixture.module._runtime
        self.baseline = {"chill_enabled": False}
        for name, value in (("_normalize_amd_profile", lambda x: dict(x)),
                            ("_get_amd_status_sync", lambda: {"ok": True}),
                            ("_amd_profile_snapshot", lambda _: dict(self.baseline))):
            guard = patch.object(self.runtime, name, side_effect=value)
            guard.start()
            self.addCleanup(guard.stop)
        guard = patch.object(self.plugin, "_record_event")
        guard.start()
        self.addCleanup(guard.stop)

    def failed_apply(self):
        return self.plugin._activate_amd_profile_sync({"chill_enabled": True}, 42, "test")

    def test_failed_rollback_retains_baseline_and_blocks_new_apply(self):
        with patch.object(self.runtime, "_apply_amd_profile_sync",
                          side_effect=[{"ok": False}, {"ok": False}]) as apply:
            result = self.failed_apply()
            self.assertFalse(result["active"])
            self.assertTrue(result["recovery_required"])
            self.assertEqual(self.runtime._AMD_PROFILE_RUNTIME["baseline"], self.baseline)
            blocked = self.plugin._activate_amd_profile_sync({}, 43, "other", force=True)
            self.assertTrue(blocked["recovery_required"])
            self.assertEqual(apply.call_count, 2)

    def test_restore_can_recover_inactive_failed_application(self):
        with patch.object(self.runtime, "_apply_amd_profile_sync",
                          side_effect=[{"ok": False}, {"ok": False}, {"ok": True}]) as apply:
            self.failed_apply()
            result = self.plugin._restore_amd_profile_sync("plugin_unload")
            self.assertTrue(result["ok"])
            self.assertEqual(apply.call_args.args, (self.baseline,))
            self.assertFalse(self.runtime._AMD_PROFILE_RUNTIME["recovery_required"])
            self.assertEqual(self.runtime._AMD_PROFILE_RUNTIME["baseline"], {})

    def test_successful_rollback_does_not_claim_active_profile(self):
        with patch.object(self.runtime, "_apply_amd_profile_sync",
                          side_effect=[{"ok": False}, {"ok": True}]):
            result = self.failed_apply()
            self.assertFalse(result["ok"])
            self.assertFalse(result["active"])
            self.assertFalse(result["recovery_required"])
            self.assertEqual(self.runtime._AMD_PROFILE_RUNTIME["baseline"], {})

    def test_failed_recovery_stays_unconfirmed_and_preserves_baseline(self):
        with patch.object(self.runtime, "_apply_amd_profile_sync", return_value={"ok": False}):
            self.failed_apply()
            result = self.plugin._restore_amd_profile_sync("game_stopped")
            self.assertFalse(result["active"])
            self.assertTrue(result["recovery_required"])
            self.assertEqual(self.runtime._AMD_PROFILE_RUNTIME["baseline"], self.baseline)


if __name__ == "__main__":
    unittest.main()

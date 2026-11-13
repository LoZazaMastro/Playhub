import unittest
from unittest.mock import patch

import test_display_entrypoints as entrypoints


class GameRuntimeProfileReads(unittest.TestCase):
    def setUp(self):
        fixture = entrypoints.DisplayEntrypointTests()
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        self.plugin = fixture.plugin
        self.runtime = fixture.module._runtime

    def report(self):
        return self.plugin._game_runtime_changed_sync(987654, "Test game", 4, "poll")

    def test_running_game_without_auto_profiles_does_not_read_lossless_settings(self):
        self.plugin._settings["lossless_profiles"] = {
            "987654": {"auto_enabled": False, "amd_auto_enabled": False},
            "987655": {"auto_enabled": True},
        }
        with patch.object(self.runtime, "_ls_read_text", side_effect=AssertionError("Unexpected settings read")):
            for _ in range(30):
                self.assertEqual(self.report()["components"], {})

    def test_amd_only_profile_does_not_load_lossless_settings(self):
        self.plugin._settings["lossless_profiles"] = {
            "987654": {"amd_auto_enabled": True, "amd_settings": {"rsr": True}},
        }
        with patch.object(self.runtime, "_ls_read_text", side_effect=AssertionError("Unexpected settings read")), \
             patch.object(self.plugin, "_activate_amd_profile_sync", return_value={"ok": True}) as activate:
            self.assertTrue(self.report()["amd_auto_enabled"])
            self.assertTrue(activate.call_args.args[0]["rsr"])

    def test_legacy_active_profile_still_uses_lossless_defaults(self):
        self.plugin._settings["lossless_profiles"] = {"987654": {"auto_enabled": True}}
        with patch.object(self.runtime, "_read_lossless_settings", return_value={"settings": {"frame_generation": "LSFG3"}}), \
             patch.object(self.runtime, "_schedule_lossless_autostart", return_value={"ok": True}) as schedule:
            self.assertTrue(self.report()["auto_enabled"])
            self.assertEqual(schedule.call_args.args[0]["frame_generation"], "LSFG3")


if __name__ == "__main__":
    unittest.main()

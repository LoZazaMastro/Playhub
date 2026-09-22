import unittest
from unittest.mock import patch
import test_panel_backend as panel


class AudioReadbackTests(unittest.TestCase):
    def setUp(self):
        self.fixture = panel.PanelBackendTests()
        self.fixture.setUp()
        self.addCleanup(self.fixture.tearDown)
        self.addCleanup(self.fixture.doCleanups)
        self.runtime = self.fixture.module._runtime

    def test_accepted_but_unchanged_endpoint_is_failure_and_clears_cache(self):
        for kind, field in (("output", "default_output_id"), ("input", "default_input_id")):
            with self.subTest(kind=kind):
                self.runtime._audio_cache_set({"ok": True, field: "old"})
                with patch.object(self.runtime, "_run_audio_powershell", return_value={"ok": True, field: "old"}):
                    result = self.runtime._set_audio_device_sync(kind, "new")
                self.assertFalse(result["ok"])
                self.assertEqual(result[field], "old")
                self.assertIsNone(self.runtime._audio_cache_get())

    def test_verified_endpoint_is_cached_as_authoritative_readback(self):
        for kind, field in (("output", "default_output_id"), ("input", "default_input_id")):
            with self.subTest(kind=kind):
                with patch.object(self.runtime, "_run_audio_powershell", return_value={"ok": True, field: "new"}):
                    result = self.runtime._set_audio_device_sync(kind, "new")
                self.assertTrue(result["ok"])
                self.assertEqual(self.runtime._audio_cache_get()[field], "new")


if __name__ == "__main__":
    unittest.main()

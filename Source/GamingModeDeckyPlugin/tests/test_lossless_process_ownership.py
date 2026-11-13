import importlib.util
import os
from pathlib import Path
import subprocess
import sys
import types
import unittest
from unittest.mock import Mock, patch


class LosslessProcessOwnershipTests(unittest.TestCase):
    def setUp(self):
        path = Path(__file__).resolve().parents[1] / "quick_settings" / "main.py"
        spec = importlib.util.spec_from_file_location("lossless_test_runtime", path)
        self.runtime = importlib.util.module_from_spec(spec)
        with patch.dict(sys.modules, {"decky": types.ModuleType("decky")}):
            spec.loader.exec_module(self.runtime)
        platform_patch = patch.object(self.runtime, "os", types.SimpleNamespace(name="nt", getpid=os.getpid))
        platform_patch.start()
        self.addCleanup(platform_patch.stop)

    def test_existing_ui_is_observed_and_left_running_on_stop(self):
        with (
            patch.object(self.runtime, "_process_ids_by_name", return_value=[42]),
            patch.object(self.runtime, "_start_lossless_hidden") as launch,
            patch.object(self.runtime, "_remove_lossless_autoscale_profiles") as cleanup,
        ):
            self.assertEqual(self.runtime._launch_lossless_sync()["pid"], 42)
            result = self.runtime._stop_lossless_managed_sync("plugin_unload")

        self.assertTrue(result["ok"])
        self.assertTrue(result["running"])
        self.assertFalse(result["active"])
        self.assertIsNone(self.runtime._LS_OWNED_UI)
        launch.assert_not_called()
        cleanup.assert_not_called()

    def test_launch_and_stop_use_only_the_spawned_child_handle(self):
        pids = []
        owned = Mock(pid=42)
        owned.poll.return_value = None

        def start(_):
            pids.append(42)
            return owned

        def stopped(*, timeout):
            self.assertEqual(timeout, 4)
            pids.remove(42)

        owned.wait.side_effect = stopped
        with (
            patch.object(self.runtime, "_find_lossless", return_value="LosslessScaling.exe"),
            patch.object(self.runtime, "_process_ids_by_name", side_effect=lambda _: list(pids)),
            patch.object(self.runtime, "_lossless_process_creation_time", return_value=100),
            patch.object(self.runtime, "_start_lossless_hidden", side_effect=start) as launch,
            patch.object(self.runtime, "_remove_lossless_autoscale_profiles") as cleanup,
        ):
            self.assertEqual(self.runtime._launch_lossless_sync()["pid"], 42)
            self.assertEqual(self.runtime._launch_lossless_sync()["pid"], 42)
            pids.append(123)
            result = self.runtime._stop_lossless_managed_sync("game_stopped")

        self.assertEqual(pids, [123])
        self.assertTrue(result["ok"])
        self.assertTrue(result["running"])
        self.assertIsNone(self.runtime._LS_OWNED_UI)
        owned.terminate.assert_called_once_with()
        launch.assert_called_once()
        cleanup.assert_not_called()

    def test_reused_pid_or_unavailable_birth_is_treated_as_external(self):
        for reported_birth in (101, None):
            with self.subTest(reported_birth=reported_birth):
                owned = Mock(pid=42)
                owned.poll.return_value = None
                self.runtime._LS_OWNED_UI = owned, 100
                with (
                    patch.object(self.runtime, "_process_ids_by_name", return_value=[42]),
                    patch.object(self.runtime, "_lossless_process_creation_time", return_value=reported_birth),
                    patch.object(self.runtime, "_remove_lossless_autoscale_profiles") as cleanup,
                ):
                    self.assertEqual(self.runtime._external_lossless_ui_pids(), [42])
                    result = self.runtime._stop_lossless_managed_sync("game_stopped")
                self.assertTrue(result["ok"])
                self.assertTrue(result["running"])
                owned.terminate.assert_not_called()
                cleanup.assert_not_called()

    def test_external_ui_blocks_managed_capture_without_touching_the_core_or_profiles(self):
        with (
            patch.object(self.runtime, "_find_lossless", return_value="LosslessScaling.exe"),
            patch.object(self.runtime, "_process_ids_by_name", return_value=[42]),
            patch.object(self.runtime, "_lossless_core_target") as target,
            patch.object(self.runtime, "_load_lossless_core") as load,
            patch.object(self.runtime, "_remove_lossless_autoscale_profiles") as cleanup,
        ):
            result = self.runtime._activate_lossless_managed_sync(app_id=100, title="Fixture")
        self.assertFalse(result["ok"])
        self.assertTrue(result["running"])
        self.assertEqual(result["code"], "lossless_external_instance")
        target.assert_not_called()
        load.assert_not_called()
        cleanup.assert_not_called()

    def test_external_ui_appearing_during_owned_shutdown_blocks_core_activation(self):
        owned = Mock(pid=42)
        owned.poll.return_value = None
        self.runtime._LS_OWNED_UI = owned, 100
        pids = [42]

        def replace(*, timeout):
            pids[:] = [123]

        owned.wait.side_effect = replace
        with (
            patch.object(self.runtime, "_find_lossless", return_value="LosslessScaling.exe"),
            patch.object(self.runtime, "_process_ids_by_name", side_effect=lambda _: list(pids)),
            patch.object(self.runtime, "_lossless_process_creation_time", return_value=100),
            patch.object(self.runtime, "_lossless_core_target", return_value={"pid": 44, "hwnd": 55}),
            patch.object(self.runtime, "_lossless_monitor_snapshot", return_value={"ok": True}),
            patch.object(self.runtime, "_dpi_awareness_snapshot", return_value={}),
            patch.object(self.runtime, "_initialize_lossless_core") as initialize,
            patch.object(self.runtime, "_remove_lossless_autoscale_profiles") as cleanup,
        ):
            result = self.runtime._activate_lossless_managed_sync(settings={"frame_generation": "Off"}, app_id=100)

        owned.terminate.assert_called_once_with()
        self.assertFalse(result["ok"])
        self.assertEqual(result["code"], "lossless_external_instance")
        self.assertEqual(pids, [123])
        initialize.assert_not_called()
        cleanup.assert_not_called()

    @unittest.skipUnless(os.name == "nt", "Windows process creation times")
    def test_native_birth_and_retained_handle_on_independent_fixture(self):
        args = [sys.executable, "-c", "import time; time.sleep(20)"]
        owned = subprocess.Popen(args, creationflags=subprocess.CREATE_NO_WINDOW)
        external = subprocess.Popen(args, creationflags=subprocess.CREATE_NO_WINDOW)
        try:
            birth = self.runtime._lossless_process_creation_time(owned.pid)
            self.assertIsNotNone(birth)
            self.runtime._LS_OWNED_UI = owned, birth
            self.assertEqual(self.runtime._stop_owned_lossless_ui(), "")
            self.assertIsNotNone(owned.poll())
            self.assertIsNone(external.poll())
        finally:
            for process in (owned, external):
                if process.poll() is None:
                    process.terminate()
                    process.wait(timeout=4)


if __name__ == "__main__":
    unittest.main()

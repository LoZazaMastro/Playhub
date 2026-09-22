"""Real backend logic with native calls, subprocesses and network blocked."""
import asyncio
from concurrent.futures import ThreadPoolExecutor
import threading
import unittest
from unittest.mock import AsyncMock, patch

import test_display_entrypoints as entrypoints


class BackendClosureTests(unittest.TestCase):
    def setUp(self):
        self.fixture = entrypoints.DisplayEntrypointTests()
        self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        self.plugin = self.fixture.plugin
        self.runtime = self.fixture.module._runtime

    def test_unload_waits_for_inflight_preview_and_restores_it(self):
        entered, release, parent_finished = threading.Event(), threading.Event(), threading.Event()

        def delayed_primary():
            entered.set()
            if not release.wait(3):
                raise AssertionError("preview not released")
            return "display-a"

        async def parent_unload():
            parent_finished.set()

        async def run():
            with patch.object(self.runtime, "_primary_display_name", side_effect=delayed_primary), \
                 patch.object(self.fixture.module.QuickSettingsPlugin, "_unload", new=AsyncMock(side_effect=parent_unload)):
                preview = asyncio.create_task(self.plugin.begin_display_change({"kind": "display", "hz": 120}))
                await asyncio.to_thread(entered.wait, 2)
                self.assertTrue(entered.is_set())
                stopping = asyncio.create_task(self.plugin._unload())
                try:
                    await asyncio.to_thread(parent_finished.wait, 0.15)
                finally:
                    release.set()
                    await asyncio.gather(preview, stopping)
        asyncio.run(run())
        self.assertIsNone(self.plugin._display_pending)
        self.assertEqual(self.fixture.mode["hz"], 60)
        self.assertIsNone(self.plugin._display_timer)

    def test_inflight_audio_poll_cannot_overwrite_confirmed_selection(self):
        reading, release, changed = threading.Event(), threading.Event(), threading.Event()

        def helper(action, device_id, **kwargs):
            if action == "get":
                reading.set()
                if not release.wait(3):
                    raise AssertionError("poll not released")
                return {"ok": True, "default_output_id": "old"}
            changed.set()
            return {"ok": True, "default_output_id": device_id}

        with patch.object(self.runtime, "_run_audio_powershell", side_effect=helper), \
             ThreadPoolExecutor(max_workers=2) as pool:
            poll = pool.submit(self.runtime._get_audio_devices_sync)
            self.assertTrue(reading.wait(2))
            setter = pool.submit(self.runtime._set_audio_device_sync, "output", "new")
            try:
                changed.wait(0.15)
            finally:
                release.set()
            poll.result(3)
            self.assertTrue(setter.result(3)["ok"])
        self.assertEqual(self.runtime._audio_cache_get()["default_output_id"], "new")

    def test_microphone_missing_or_mismatched_readback_is_failure(self):
        for response in ({"ok": True}, {"ok": True, "input_volume": None},
                         {"ok": True, "input_volume": 10},
                         {"ok": True, "input_volume": float("nan")}):
            with self.subTest(response=response):
                self.runtime._audio_cache_set({"ok": True, "input_volume": 20})
                with patch.object(self.runtime, "_run_audio_powershell", return_value=response):
                    result = self.runtime._set_microphone_volume_sync(75)
                self.assertFalse(result["ok"])
                self.assertIsNone(self.runtime._audio_cache_get())

    def test_verified_microphone_volume_updates_cache_from_readback(self):
        self.runtime._audio_cache_set({"ok": True, "input_volume": 20})
        with patch.object(self.runtime, "_run_audio_powershell", return_value={"ok": True, "input_volume": 74.6}):
            result = self.runtime._set_microphone_volume_sync(75)
        self.assertTrue(result["ok"])
        self.assertEqual(self.runtime._audio_cache_get()["input_volume"], 75)

    def test_hdr_missing_target_cannot_confirm_disabled_state(self):
        # Test the real waiter/decoder, not the transaction fixture's shortcut.
        import ast
        import pathlib
        path = pathlib.Path(self.runtime.__file__)
        tree = ast.parse(path.read_text(encoding="utf-8"))
        names = {"_hdr_target_states", "_wait_for_hdr_states"}
        # The waiter defaults to the module-level HDR read-back window.
        nodes = [n for n in tree.body
                 if (isinstance(n, ast.FunctionDef) and n.name in names)
                 or (isinstance(n, ast.Assign) and any(isinstance(target, ast.Name)
                     and target.id == "HDR_VERIFY_TIMEOUT" for target in n.targets))]
        from unittest.mock import Mock
        namespace = {"time": Mock(), "_read_real_hdr_status": Mock(return_value={"targets": []})}
        # Jump well past any read-back window so the waiter runs to its verdict.
        import itertools
        namespace["time"].monotonic.side_effect = itertools.count(0, 1000)
        exec(compile(ast.Module(body=nodes, type_ignores=[]), str(path), "exec"), namespace)
        verified, _ = namespace["_wait_for_hdr_states"]({"disconnected": False})
        self.assertFalse(verified)

    def test_stale_timer_cannot_revert_a_new_preview(self):
        first = self.plugin._begin_display_change({"kind": "display", "hz": 120})
        callback = self.fixture.timer.call_args.args[1]
        callback_args = self.fixture.timer.call_args.kwargs["args"]
        self.plugin._finish_display_change({"token": first["token"], "keep": True})
        second = self.plugin._begin_display_change({"kind": "display", "hz": 144})
        calls = self.fixture.display_write.call_count
        callback(*callback_args)
        self.assertEqual(self.fixture.display_write.call_count, calls)
        self.assertEqual(self.plugin._display_pending["token"], second["token"])
        self.assertEqual(self.fixture.mode["hz"], 144)

    def test_failed_rollback_backs_off_and_unload_keeps_recovery_journal(self):
        result = self.plugin._begin_display_change({"kind": "display", "hz": 120})
        with patch.object(self.runtime, "_set_display_mode_sync", return_value={"ok": False}):
            for delay in (2, 2, 2, 30):
                failed = self.plugin._revert_display(result["token"])
                self.assertFalse(failed["terminal"])
                self.assertEqual(self.fixture.timer.call_args.args[0], delay)
            calls = self.fixture.timer.call_count
            self.plugin._stop_display_sync()
            self.assertEqual(self.fixture.timer.call_count, calls)
        import json
        from pathlib import Path
        journal = json.loads(Path(self.plugin._display_journal).read_text())
        self.assertEqual(journal["state"], "recovery_required")
        self.assertIsNone(self.plugin._display_timer)
        self.assertEqual(self.plugin._begin_display_change({"kind": "display", "hz": 60})["state"], "stopping")

    def test_partial_hdr_failure_restores_mixed_target_states(self):
        self.fixture.hdr = {"display-a": False, "display-b": False, "display-c": True}
        previous = dict(self.fixture.hdr)
        calls = []
        def partial_write(*, states):
            calls.append(dict(states))
            if len(calls) == 1:
                self.fixture.hdr["display-b"] = states["display-b"]
                return {"all_accepted": False}
            self.fixture.hdr = dict(states)
            return {"all_accepted": True}
        with patch.object(self.plugin, "_hdr_write_allowed", return_value=True), \
             patch.object(self.runtime, "_set_advanced_color_states", side_effect=partial_write):
            result = self.plugin._begin_display_change({"kind": "hdr", "enabled": True})
        self.assertFalse(result["ok"])
        self.assertTrue(result["rollback"]["terminal"])
        self.assertEqual(self.fixture.hdr, previous)
        self.assertEqual(calls[-1], previous)

    def test_missing_cpu_package_does_not_crash_installer_payload_rpc(self):
        import builtins
        original_import = builtins.__import__
        def without_cpu(name, *args, **kwargs):
            if name == "cpu_power" or name.startswith("cpu_power."):
                raise ModuleNotFoundError(name)
            return original_import(name, *args, **kwargs)
        with patch.object(builtins, "__import__", side_effect=without_cpu):
            status = asyncio.run(self.plugin.get_tdp_status())
            apply = asyncio.run(self.plugin.set_cpu_ppt({"watts": 142, "confirmed": True}))
            restore = asyncio.run(self.plugin.restore_cpu_ppt({"confirmed": True}))
        self.assertFalse(status["available"])
        self.assertEqual(status["reason"], "feature_removed")
        self.assertFalse(apply["ok"])
        self.assertFalse(restore["ok"])

    def test_invalid_microphone_request_never_becomes_a_mute_write(self):
        with patch.object(self.runtime, "_set_microphone_volume_sync") as setter:
            for request in ({}, None, {"level": "not-a-number"}, {"level": float("nan")},
                            {"level": float("inf")}, {"level": True}):
                with self.subTest(request=request):
                    result = asyncio.run(self.plugin.set_microphone_volume(request))
                    self.assertFalse(result["ok"])
            setter.assert_not_called()

    def test_audio_request_flow_is_forwarded_to_native_guard(self):
        for kind, field in (("output", "default_output_id"), ("input", "default_input_id")):
            with self.subTest(kind=kind), patch.object(self.runtime, "_run_audio_powershell",
                    return_value={"ok": True, field: "new"}) as helper:
                self.assertTrue(self.runtime._set_audio_device_sync(kind, "new")["ok"])
                helper.assert_called_once_with("set", "new", kind=kind)

    def test_invalid_audio_kind_never_invokes_setter(self):
        with patch.object(self.runtime, "_run_audio_powershell") as helper:
            result = self.runtime._set_audio_device_sync("other", "endpoint")
            self.assertFalse(result["ok"])
            helper.assert_not_called()

    def test_root_microphone_rpc_forwards_expected_endpoint(self):
        with patch.object(self.runtime, "_set_microphone_volume_sync", return_value={"ok": True}) as setter:
            asyncio.run(self.plugin.set_microphone_volume({"level": 75, "expected_endpoint": "mic-a"}))
            setter.assert_called_once_with(75, "mic-a")

    def test_microphone_expected_endpoint_runs_under_audio_lock_and_checks_ack(self):
        def helper(action, device_id, level, expected_endpoint=None):
            self.assertTrue(self.runtime._AUDIO_OPERATION_LOCK.locked())
            self.assertEqual(expected_endpoint, "mic-a")
            return {"ok": True, "input_volume": 75, "endpoint_id": "mic-b"}
        with patch.object(self.runtime, "_run_audio_powershell", side_effect=helper):
            result = self.runtime._set_microphone_volume_sync(75, "mic-a")
        self.assertFalse(result["ok"])
        self.assertIsNone(self.runtime._audio_cache_get())

    def test_invalid_expected_endpoint_does_not_write(self):
        with patch.object(self.runtime, "_set_microphone_volume_sync") as setter:
            for expected in ("", " ", 1, True, []):
                result = asyncio.run(self.plugin.set_microphone_volume({"level": 75, "expected_endpoint": expected}))
                self.assertFalse(result["ok"])
            setter.assert_not_called()


if __name__ == "__main__":
    unittest.main()

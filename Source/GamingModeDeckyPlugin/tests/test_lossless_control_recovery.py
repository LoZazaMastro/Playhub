"""Lossless control CAS/recovery with native, network and process boundaries blocked."""
import asyncio
import threading
import unittest
from unittest.mock import Mock, patch

import test_display_entrypoints as entrypoints


class LosslessControlRecoveryTests(unittest.TestCase):
    def setUp(self):
        fixture = entrypoints.DisplayEntrypointTests()
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        self.r = fixture.module._runtime
        self.plugin = fixture.plugin
        self.plugin._last_runtime = {"app_id": 42, "title": "Game"}
        self.status = fixture.stack.enter_context(patch.object(self.r, "_read_lossless_status_sync",
            return_value={"ok": True, "installed": True, "capture_state": "inactive",
                          "capture_active": False, "scaling_requested": False}, create=True))

    def test_write_consumes_token_before_operation_and_replay_never_mutates(self):
        r = self.r
        old = r._LS_CONTROL_TOKEN
        def operation():
            self.assertNotEqual(r._LS_CONTROL_TOKEN, old)
            return {"ok": True}
        write = Mock(side_effect=operation)
        result = r._run_lossless_control_sync(old, write)
        self.assertTrue(result["ok"])
        self.assertNotEqual(result["control_token"], old)
        self.assertFalse(r._run_lossless_control_sync(old, write)["ok"])
        write.assert_called_once()

    def test_read_only_retry_revokes_delayed_not_started_write(self):
        r = self.r
        old = r._LS_CONTROL_TOKEN
        fresh = r._get_lossless_status_sync(old)
        self.assertTrue(fresh["ok"])
        self.assertFalse(fresh["control_busy"])
        operation = Mock(return_value={"ok": True})
        self.assertFalse(r._run_lossless_control_sync(old, operation)["ok"])
        operation.assert_not_called()
        self.assertNotEqual(fresh["control_token"], old)
        self.assertTrue(r._run_lossless_control_sync(fresh["control_token"], operation)["ok"])
        operation.assert_called_once()

    def test_busy_read_never_waits_or_reads_mutable_capture_and_cannot_enable_writes(self):
        r = self.r
        old = r._LS_CONTROL_TOKEN
        entered, release = threading.Event(), threading.Event()
        def operation():
            entered.set()
            self.assertTrue(release.wait(2))
            return {"ok": True}
        result = []
        thread = threading.Thread(target=lambda: result.append(r._run_lossless_control_sync(old, operation)))
        thread.start()
        try:
            self.assertTrue(entered.wait(2))
            status = r._get_lossless_status_sync(old)
            self.assertFalse(status["ok"])
            self.assertTrue(status["control_busy"])
            self.status.assert_not_called()
            consumed = status["control_token"]
            self.assertNotEqual(consumed, old)
        finally:
            release.set()
            thread.join(2)
        self.assertFalse(thread.is_alive())
        fresh = r._get_lossless_status_sync(old)
        self.assertFalse(fresh["control_busy"])
        self.assertEqual(fresh["control_token"], consumed)
        operation = Mock(return_value={"ok": True})
        self.assertFalse(r._run_lossless_control_sync(old, operation)["ok"])
        operation.assert_not_called()

    def test_exception_also_consumes_token_and_retry_is_read_only(self):
        r = self.r
        old = r._LS_CONTROL_TOKEN
        operation = Mock(side_effect=RuntimeError("lost result"))
        with self.assertRaises(RuntimeError):
            r._run_lossless_control_sync(old, operation)
        self.assertFalse(r._run_lossless_control_sync(old, operation)["ok"])
        self.assertTrue(r._get_lossless_status_sync(old)["ok"])
        operation.assert_called_once()

    def test_missing_malformed_old_backend_tokens_do_not_run_operations(self):
        r = self.r
        operation = Mock(return_value={"ok": True})
        old = r._LS_CONTROL_TOKEN
        for bad in (None, "", 1, True, "A" * 32, "0" * 31, "0" * 33, old + "\n"):
            with self.subTest(token=bad):
                self.assertFalse(r._run_lossless_control_sync(bad, operation)["ok"])
        # A backend restart changes its process-local token; old in-flight RPCs fail closed.
        r._LS_CONTROL_TOKEN = "f" * 32
        self.assertFalse(r._run_lossless_control_sync(old, operation)["ok"])
        operation.assert_not_called()

    def test_public_scale_rpc_requires_token_and_exact_current_game(self):
        r = self.r
        with patch.object(r, "_set_lossless_scaling_sync", return_value={"ok": True}) as write:
            for request in ({"enabled": True},
                {"enabled": True, "expected_control_token": r._LS_CONTROL_TOKEN, "expected_app_id": 43},
                {"enabled": True, "expected_control_token": r._LS_CONTROL_TOKEN, "expected_app_id": True}):
                self.assertFalse(asyncio.run(self.plugin.set_lossless_scaling(request))["ok"])
            write.assert_not_called()
            result = asyncio.run(self.plugin.set_lossless_scaling({"enabled": False,
                "expected_control_token": r._LS_CONTROL_TOKEN, "expected_app_id": 42}))
            self.assertTrue(result["ok"])
            write.assert_called_once_with(False, 42, "Game")

    def test_public_setting_rpc_uses_same_cas_and_game_guard(self):
        r = self.r
        with patch.object(r, "_set_lossless_setting_sync", return_value={"ok": True}) as write:
            request = {"key": "frame_gen", "value": "LSFG3", "expected_control_token": r._LS_CONTROL_TOKEN,
                       "expected_app_id": 43}
            self.assertFalse(asyncio.run(self.plugin.set_lossless_setting(request))["ok"])
            write.assert_not_called()
            request.update(expected_control_token=r._LS_CONTROL_TOKEN, expected_app_id=42)
            self.assertTrue(asyncio.run(self.plugin.set_lossless_setting(request))["ok"])
            write.assert_called_once_with("frame_gen", "LSFG3")
            self.assertFalse(asyncio.run(self.plugin.set_lossless_setting(request))["ok"])
            write.assert_called_once()

    def test_game_changed_while_request_queued_is_rechecked_at_execution(self):
        r = self.r
        async def scenario():
            request = {"enabled": True, "expected_control_token": r._LS_CONTROL_TOKEN, "expected_app_id": 42}
            task = asyncio.create_task(self.plugin.set_lossless_scaling(request))
            # Dispatch is held behind the real RLock until the context changes.
            await asyncio.sleep(0)
            self.plugin._last_runtime = {"app_id": 43, "title": "Other"}
            r._LS_CONTROL_LOCK.release()
            return await task
        with patch.object(r, "_set_lossless_scaling_sync", return_value={"ok": True}) as write:
            r._LS_CONTROL_LOCK.acquire()
            result = asyncio.run(scenario())
            self.assertFalse(result["ok"])
            self.assertEqual(result["code"], "lossless_context_changed")
            write.assert_not_called()

    def test_settings_write_shares_capture_control_lock(self):
        r = self.r
        started, done = threading.Event(), threading.Event()
        with patch.object(r, "_set_lossless_setting_locked", return_value={"ok": True}) as write:
            def setting():
                started.set()
                r._set_lossless_setting_sync("frame_gen", "LSFG3")
                done.set()
            r._LS_CONTROL_LOCK.acquire()
            thread = threading.Thread(target=setting)
            thread.start()
            try:
                self.assertTrue(started.wait(2))
                self.assertFalse(done.wait(0.02))
                write.assert_not_called()
            finally:
                r._LS_CONTROL_LOCK.release()
                thread.join(2)
            self.assertFalse(thread.is_alive())
            write.assert_called_once()

    def test_pending_foreground_worker_uses_latest_settings_without_restarting_deadline(self):
        r = self.r
        workers = []
        class DeferredThread:
            def __init__(self, target, **_): self.target = target
            def start(self): workers.append(self.target)
        current = {**r.LS_PROFILE_DEFAULTS, "frame_generation": "Off"}
        def settings_write(value):
            current.update(value)
            return {"ok": True, "settings": dict(current)}
        with patch.object(r.threading, "Thread", DeferredThread), \
             patch.object(r, "_read_lossless_settings", side_effect=lambda: {"settings": dict(current)}), \
             patch.object(r, "_write_lossless_profile_settings", side_effect=settings_write), \
             patch.object(r, "_wait_for_lossless_autostart_target", return_value={"ok": True}), \
             patch.object(r, "_activate_lossless_managed_sync", return_value={"ok": True, "active": True}) as activate:
            result = r._schedule_lossless_autostart(current, 42, "Game", ["game.exe"])
            self.assertTrue(result["pending"])
            identity = r._LS_AUTOSTART_GATE["token"], r._LS_AUTOSTART_GATE["started_at"]
            r._LS_SESSION_CONTROL.update(resume_requested=True, resume_app_id=42, overlay_suspended=False)
            changed = r._set_lossless_setting_sync("frame_gen", "LSFG3")
            self.assertEqual((r._LS_AUTOSTART_GATE["token"], r._LS_AUTOSTART_GATE["started_at"]), identity)
            self.assertEqual(len(workers), 1)
            workers[0]()
            self.assertEqual(activate.call_args.args[0]["frame_generation"], "LSFG3")
            self.assertFalse(changed["runtime"]["active"])
            self.assertFalse(changed["runtime"]["suspended"])
            self.assertFalse(r._LS_AUTOSTART_GATE["worker_running"])

    def test_public_status_exposes_real_context_and_control_state(self):
        result = asyncio.run(self.plugin.get_lossless_status())
        self.assertEqual(result["current_game"]["app_id"], 42)
        self.assertTrue(result["can_scale"])
        self.assertFalse(result["control_busy"])
        self.assertEqual(result["control_token"], self.r._LS_CONTROL_TOKEN)


if __name__ == "__main__":
    unittest.main()

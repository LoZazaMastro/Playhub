import asyncio
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock, patch

import test_display_entrypoints as entrypoints


class LivePerformanceRpc(unittest.TestCase):
    def setUp(self):
        fixture = entrypoints.DisplayEntrypointTests()
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        self.plugin = fixture.plugin

    def test_rejects_unbounded_targets_bad_shapes_and_bool_numbers(self):
        valid = dict(action="configure", appId=42, enabled=True, detail=2, corner=1, transparent=True, refreshMs=100)
        with patch.object(self.plugin, "_live_performance_request") as native:
            for changes in ({"pid": 9}, {"appId": True}, {"appId": -1}, {"appId": 0x100000000},
                            {"detail": True}, {"corner": 7}, {"refreshMs": 10}, {"suspended": 1}, {"lease": "bad"}):
                self.assertFalse(asyncio.run(self.plugin.set_live_performance({**valid, **changes}))["ok"])
            for value in (None, [], {}, {"action": "stop"}, {"action": "renew", "lease": "a" * 32, "appId": 42}):
                self.assertFalse(asyncio.run(self.plugin.set_live_performance(value))["ok"])
            native.assert_not_called()

    def test_auth_get_and_exact_configure_body_use_loopback_without_proxy(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp, "GamingMode")
            directory.mkdir()
            (directory / "xbox-shell-token").write_text("fixture-only-token", encoding="utf-8")
            state = dict(enabled=True, detail=2, corner=1, transparent=True, refreshMs=100,
                         status="NoData", appId=42, lease="a" * 32)
            client = Mock()
            client.open.side_effect = [io.BytesIO(json.dumps(state).encode()),
                                      io.BytesIO(json.dumps({"ok": True, "state": state}).encode())]
            request = dict(action="configure", appId=42, enabled=True, detail=2, corner=1,
                           transparent=True, refreshMs=100, suspended=True)
            with patch.dict(os.environ, {"APPDATA": temp}), patch("urllib.request.build_opener", return_value=client):
                self.assertEqual(asyncio.run(self.plugin.get_live_performance()), state)
                self.assertTrue(asyncio.run(self.plugin.set_live_performance(request))["ok"])
            get, post = [entry.args[0] for entry in client.open.call_args_list]
            self.assertEqual([entry.kwargs for entry in client.open.call_args_list],
                             [{"timeout": 2.5}, {"timeout": 2.5}])
            self.assertEqual(get.get_method(), "GET")
            self.assertEqual(get.full_url, "http://127.0.0.1:47991/session/live-performance")
            self.assertEqual(post.get_header("X-playhub-shell-token"), "fixture-only-token")
            self.assertEqual(json.loads(post.data), {**request, "refreshMs": 20})

    def test_automatic_minimum_accepts_legacy_or_omitted_cadence_without_mutating_request(self):
        base = dict(action="configure", appId=42, enabled=True, detail=2, corner=1, transparent=True)
        with patch.object(self.plugin, "_live_performance_request", return_value={"ok": True}) as native:
            for cadence in (None, 20, 50, 100):
                request = base if cadence is None else {**base, "refreshMs": cadence}
                original = dict(request)
                self.assertTrue(asyncio.run(self.plugin.set_live_performance(request))["ok"])
                self.assertEqual(native.call_args.args, ({**base, "refreshMs": 20},))
                self.assertEqual(request, original)
            for cadence in (True, False, "20", 0, 1, 10, 19, 21, 1000, None):
                self.assertFalse(asyncio.run(self.plugin.set_live_performance({**base, "refreshMs": cadence}))["ok"])

    def test_renew_and_stop_only_forward_the_owned_lease(self):
        with patch.object(self.plugin, "_live_performance_request", return_value={"ok": True}) as native:
            for request in ({"action": "renew", "lease": "b" * 32, "suspended": True}, {"action": "stop", "lease": "b" * 32}):
                self.assertTrue(asyncio.run(self.plugin.set_live_performance(request))["ok"])
                self.assertEqual(native.call_args.args, (request,))

    def test_palette_only_forwards_four_bright_colors_on_configure(self):
        base = dict(action="configure", appId=42, enabled=True, detail=2, corner=1, transparent=True)
        with patch.object(self.plugin, "_live_performance_request", return_value={"ok": True}) as native:
            for color in ("white", "yellow", "red", "green"):
                self.assertTrue(asyncio.run(self.plugin.set_live_performance({**base, "textColor": color}))["ok"])
                self.assertEqual(native.call_args.args[0]["textColor"], color)
            native.reset_mock()
            for color in ("black", "#fff", "White", None, 1, True, [], {}):
                self.assertFalse(asyncio.run(self.plugin.set_live_performance({**base, "textColor": color}))["ok"])
            for action in ("renew", "stop"):
                self.assertFalse(asyncio.run(self.plugin.set_live_performance(dict(action=action, lease="a"*32, textColor="red")))["ok"])
            native.assert_not_called()


if __name__ == "__main__":
    unittest.main()

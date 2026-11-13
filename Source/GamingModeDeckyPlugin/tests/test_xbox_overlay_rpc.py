import asyncio
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock, patch

import test_display_entrypoints as entrypoints


class XboxOverlayRpc(unittest.TestCase):
    def setUp(self):
        fixture = entrypoints.DisplayEntrypointTests()
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        self.plugin = fixture.plugin

    def test_rejects_invalid_shortcut_ids_before_native_request(self):
        with patch.object(self.plugin, "_native_session_post") as native:
            for app in (0, True, 42, -1, 0x100000000, "2485417537"):
                with self.subTest(app=app):
                    self.assertEqual(asyncio.run(self.plugin.request_xbox_steam_overlay(app)), {"ok": False})
            native.assert_not_called()

    def test_native_guide_dispatch_posts_auth_and_exact_shortcut_json(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp, "GamingMode")
            directory.mkdir()
            (directory / "xbox-shell-token").write_text("fixture-only-token", encoding="utf-8")
            client = Mock()
            accepted = {"ok": True, "gameId": "10674787038353424384", "companionPid": 27156,
                        "overlayPid": 31704, "keyboardAt": 9000, "foregroundScope": 1, "overlayOpen": False}
            client.open.return_value = io.BytesIO(json.dumps(accepted).encode())
            with patch.dict(os.environ, {"APPDATA": temp}), patch("urllib.request.build_opener", return_value=client):
                self.assertEqual(asyncio.run(self.plugin.request_xbox_steam_overlay(2485417537)), accepted)
            request = client.open.call_args.args[0]
            self.assertEqual(request.get_method(), "POST")
            self.assertEqual(request.full_url, "http://127.0.0.1:47991/session/uwp/steam-overlay/open")
            self.assertEqual(request.get_header("Content-type"), "application/json")
            self.assertEqual(request.get_header("X-playhub-shell-token"), "fixture-only-token")
            self.assertEqual(json.loads(request.data), {"appId": 2485417537})

    def test_no_overlay_pid_is_accepted_only_for_a_read_only_scope_check(self):
        with patch.object(self.plugin, "_native_session_post", return_value={"ok": True}) as native:
            self.assertEqual(asyncio.run(self.plugin.verify_xbox_steam_overlay(2485417537, 0, False)), {"ok": True})
            self.assertEqual(asyncio.run(self.plugin.verify_xbox_steam_overlay(2485417537, 0, True)), {"ok": False})
            self.assertEqual(asyncio.run(self.plugin.verify_xbox_steam_overlay(2485417537, 0, False, True)), {"ok": False})
            native.assert_called_once_with("/session/uwp/steam-overlay",
                {"appId": 2485417537, "overlayPid": 0, "keyboard": "false", "menu": "false"})

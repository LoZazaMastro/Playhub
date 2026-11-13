import asyncio
import unittest
from unittest.mock import patch
import test_display_entrypoints as entrypoints

class SnapshotRpcTests(unittest.TestCase):
    def setUp(self):
        fixture = entrypoints.DisplayEntrypointTests()
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        self.plugin = fixture.plugin

    def test_invalid_identity_never_dispatches(self):
        with patch.object(self.plugin, "_native_session_post") as native:
            for app, pid in [(True, 9), (0, 9), (42, 9), (-1, 9), (2**32, 9),
                (2485417537, True), (2485417537, 0), (2485417537, -1),
                (2485417537, 2**32), (2485417537, "31704")]:
                with self.subTest(app=app, pid=pid):
                    self.assertFalse(asyncio.run(self.plugin.read_xbox_steam_overlay(app, pid))["ok"])
            native.assert_not_called()

    def test_snapshot_has_a_separate_readonly_dispatch(self):
        proof = {"kind":"owned-session", "ok":True, "gameId":"10674787038353424384",
            "sessionId":"A" * 64, "companionPid":29872, "overlayPid":31704,
            "foregroundScope":1, "overlayOpen":True}
        with patch.object(self.plugin, "_native_session_post", return_value=proof) as native:
            self.assertEqual(asyncio.run(self.plugin.read_xbox_steam_overlay(2485417537, 31704)), proof)
            native.assert_called_once_with("/session/uwp/steam-overlay/snapshot", {"appId":2485417537, "overlayPid":31704})

    def test_an_action_response_cannot_be_relabelled_a_snapshot(self):
        for response in [{"ok":True}, {"kind":"open", "ok":True}, {}]:
            with self.subTest(response=response), patch.object(self.plugin, "_native_session_post", return_value=response):
                self.assertEqual(asyncio.run(self.plugin.read_xbox_steam_overlay(2485417537,31704)),
                    {"ok":False,"kind":"owned-session"})

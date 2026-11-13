import asyncio
import unittest
from unittest.mock import patch
import test_display_entrypoints as entrypoints


class MirrorRpcTests(unittest.TestCase):
    def setUp(self):
        fixture = entrypoints.DisplayEntrypointTests()
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        self.plugin = fixture.plugin
        self.identity = dict(protocol=1, sessionId="a" * 64, generation="1", gameId="10674787038353424384",
            sourceHwnd="41001", sourceCookie="18446744073709551614", destinationHwnd="41002",
            destinationCookie="18446744073709551615", ownerPid=27156, ownerBirth="134389155136169895",
            gamePid=48584, gameBirth="134389155148953005")

    def test_read_discovery_uses_only_the_authenticated_read_endpoint(self):
        proof = {"ok": True, "mirror": {"protocol": 1, "state": "prepared"}}
        with patch.object(self.plugin, "_native_session_post", return_value=proof) as native:
            self.assertEqual(asyncio.run(self.plugin.get_mirror_presentation(2485417537, 0)), proof)
            native.assert_called_once_with("/session/uwp/steam-overlay/mirror", {"appId": 2485417537, "overlayPid": 0})

    def test_bad_app_pid_and_command_never_dispatch(self):
        with patch.object(self.plugin, "_native_session_post") as native:
            for app, pid, action in [(True, 31704, "show"), (42, 31704, "show"), (2485417537, True, "show"),
                (2485417537, 0, "close"), (2485417537, 31704, "769"), (2485417537, 2**32, "show")]:
                self.assertFalse(asyncio.run(self.plugin.set_mirror_presentation(app, pid, action, self.identity))["ok"])
            native.assert_not_called()

    def test_native_identity_cannot_pass_through_a_number_or_unicode_decimal(self):
        with patch.object(self.plugin, "_native_session_post") as native:
            for key, value in [("ownerBirth", 134389155136169895), ("generation", "18446744073709551616"),
                ("sourceCookie", "١٢٣"), ("protocol", True), ("gamePid", True), ("sessionId", "x" * 64)]:
                expected = {**self.identity, key: value}
                self.assertFalse(asyncio.run(self.plugin.set_mirror_presentation(2485417537, 31704, "show", expected))["ok"])
            native.assert_not_called()

    def test_commands_forward_exact_identity_and_drop_client_authority_claims(self):
        for action in ("show", "close"):
            with patch.object(self.plugin, "_native_session_post", return_value={"ok": True}) as native:
                expected = {**self.identity, "shownCurrent": True, "actualScope": 2, "leaseCurrent": True}
                self.assertTrue(asyncio.run(self.plugin.set_mirror_presentation(2485417537, 31704, action, expected))["ok"])
                native.assert_called_once_with("/session/uwp/steam-overlay/mirror/control", None,
                    {"appId": 2485417537, "overlayPid": 31704, "action": action, "expectedMirror": self.identity})

    def test_action_reply_cannot_impersonate_a_presentation(self):
        with patch.object(self.plugin, "_native_session_post", return_value={"ok": True}):
            self.assertFalse(asyncio.run(self.plugin.get_mirror_presentation(2485417537, 31704))["ok"])

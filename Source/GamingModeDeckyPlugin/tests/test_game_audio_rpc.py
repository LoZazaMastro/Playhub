import asyncio
import unittest
from unittest.mock import patch

import test_display_entrypoints as entrypoints


class GameAudioRpc(unittest.TestCase):
    def setUp(self):
        fixture = entrypoints.DisplayEntrypointTests()
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        self.plugin = fixture.plugin

    def test_rejects_wrong_app_and_untokened_or_malformed_write(self):
        with patch.object(self.plugin, "_native_session_post") as native:
            for args in ((0,), (True,), (42, 10), (42, True, "a" * 64),
                         (42, 101, "a" * 64), (42, 75, "G" * 64), (42, None, "b" * 64)):
                with self.subTest(args=args):
                    self.assertEqual(asyncio.run(self.plugin.game_audio_volume(*args))["status"], "Unavailable")
            native.assert_not_called()

    def test_confirmed_read_token_is_carried_to_exact_game_write(self):
        token = "b" * 64
        with patch.object(self.plugin, "_native_session_post",
                          side_effect=[{"status": "Ready", "level": 100, "token": token},
                                       {"status": "Ready", "level": 24, "token": token}]) as native:
            read = asyncio.run(self.plugin.game_audio_volume(42))
            write = asyncio.run(self.plugin.game_audio_volume(42, 24, read["token"]))
            self.assertEqual(write["level"], 24)
            self.assertEqual([call.args for call in native.call_args_list], [
                ("/session/steam/game-audio", {"appId": 42}),
                ("/session/steam/game-audio", {"appId": 42, "level": 24, "token": token}),
            ])

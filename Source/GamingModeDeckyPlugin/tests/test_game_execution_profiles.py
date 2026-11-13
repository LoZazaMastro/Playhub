import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

from quick_settings.game_execution_profiles import GameExecutionProfiles


class GameExecutionProfileTests(unittest.TestCase):
    def setUp(self):
        folder = tempfile.TemporaryDirectory()
        self.addCleanup(folder.cleanup)
        self.folder = Path(folder.name)
        self.calls = []

        def request(route, query):
            self.calls.append((route.rsplit('/', 1)[-1], query))
            return {"ok": True, "status": "Default" if route.endswith('/stop') else "Applied"}

        self.request = Mock(side_effect=request)
        self.profiles = GameExecutionProfiles(self.folder, self.request)

    def test_idle_and_unselected_games_never_create_a_lease(self):
        self.profiles.observe(0)
        self.profiles.observe(42)
        self.assertEqual(self.profiles.get(42)["status"], "Default")
        self.assertEqual(self.calls, [])
        self.assertFalse((self.folder / 'game-execution-profiles.json').exists())

    def test_preferences_persist_and_only_apply_to_the_matching_game(self):
        self.profiles.observe(42)
        self.profiles.save(43, 'performance', 'efficiency')
        self.assertEqual(self.calls, [])
        reload = GameExecutionProfiles(self.folder, self.request)
        self.assertEqual(reload.get(43), {"ok": True, "ac": "performance", "battery": "efficiency", "status": "Default"})
        reload.observe(43)
        reload.observe(43)
        reload.observe(42)
        self.assertEqual(self.calls, [('game-qos', {'appId': 43, 'ac': 'performance', 'battery': 'efficiency'})] * 2 + [('stop', {})])

    def test_invalid_ids_and_modes_cannot_persist_or_reach_agent(self):
        for app in (True, -1, 0, '42', 2**32):
            self.assertFalse(self.profiles.save(app, 'performance', 'default')['ok'])
        for mode in ('turbo', ['efficiency'], None):
            self.assertFalse(self.profiles.save(42, mode, 'default')['ok'])
        self.assertEqual(self.calls, [])

    def test_failed_persistence_retains_current_profile_and_stops_no_game(self):
        self.profiles.save(42, 'performance', 'default')
        self.profiles.observe(42)
        with patch.object(self.profiles, '_write', side_effect=OSError('read only')):
            self.assertFalse(self.profiles.save(42, 'efficiency', 'default')['ok'])
        self.assertEqual(self.profiles.get(42)['ac'], 'performance')
        self.assertEqual(len(self.calls), 1)

    def test_disable_and_unload_stop_once_without_accepting_late_renewal(self):
        self.profiles.observe(42)
        self.profiles.save(42, 'performance', 'default')
        self.profiles.save(42, 'default', 'default')
        self.profiles.save(42, 'efficiency', 'default')
        self.profiles.close()
        self.profiles.observe(42)
        self.assertFalse(self.profiles.save(42, 'performance', 'default')['ok'])
        self.assertEqual([action for action, _ in self.calls], ['game-qos', 'stop', 'game-qos', 'stop'])

    def test_failed_restore_retries_before_arming_another_game(self):
        self.profiles.save(42, 'efficiency', 'default')
        self.profiles.save(43, 'performance', 'default')
        self.profiles.observe(42)
        self.request.side_effect = [{}, {"ok": True}, {"status": "Applied"}]
        self.profiles.observe(43)
        self.assertEqual(self.profiles.get(43)['status'], 'RestoreUnavailable')
        self.assertTrue(self.request.call_args.args[0].endswith('/stop'))
        self.profiles.observe(43)
        self.assertEqual(self.request.call_args.args[1]['appId'], 43)
        self.assertEqual(self.profiles.get(43)['status'], 'Applied')

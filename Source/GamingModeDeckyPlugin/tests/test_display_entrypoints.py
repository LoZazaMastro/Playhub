"""Public display RPC regression tests. Native libraries/processes are forbidden."""
import asyncio
import ctypes
import importlib.util
import logging
import pathlib
import subprocess
import sys
import tempfile
import threading
import types
import unittest
import urllib.request
from contextlib import ExitStack
from unittest.mock import Mock, patch


class DisplayEntrypointTests(unittest.TestCase):
    def setUp(self):
        self.stack = ExitStack()
        self.addCleanup(self.stack.close)
        directory = self.stack.enter_context(tempfile.TemporaryDirectory())
        decky = types.ModuleType('decky')
        decky.DECKY_PLUGIN_SETTINGS_DIR = directory
        decky.logger = logging.getLogger('display-entrypoint-test')
        self.stack.enter_context(patch.dict(sys.modules, {'decky': decky}))
        for name in ('WinDLL', 'CDLL'):
            self.stack.enter_context(patch.object(ctypes, name,
                side_effect=AssertionError('Native library access forbidden'), create=True))
        self.stack.enter_context(patch.object(ctypes, 'windll', Mock(
            user32=Mock(side_effect=AssertionError('Native display access forbidden'))), create=True))
        self.stack.enter_context(patch.object(subprocess, 'Popen',
            side_effect=AssertionError('Subprocess forbidden')))
        self.stack.enter_context(patch.object(urllib.request, 'urlopen',
            side_effect=AssertionError('Network forbidden')))
        self.timer = self.stack.enter_context(patch.object(threading, 'Timer'))
        path = pathlib.Path(__file__).resolve().parents[1] / 'main.py'
        spec = importlib.util.spec_from_file_location('display_entrypoint_test_runtime', path)
        self.module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.module)
        runtime = self.module._runtime
        self.mode = {'width': 1920, 'height': 1080, 'hz': 60}
        self.registered = dict(self.mode)
        self.hdr = {'display-a': False}
        def display_write(width, height, hz, persist=True, device_name=None):
            self.mode.update({key: value for key, value in
                (('width', width), ('height', height), ('hz', hz)) if value > 0})
            if persist:
                self.registered = dict(self.mode)
            return {'ok': True, 'verified': True}
        def hdr_write(*, states):
            self.hdr = dict(states)
            return {'all_accepted': True}
        self.display_write = self.stack.enter_context(patch.object(runtime,
            '_set_display_mode_sync', side_effect=display_write))
        self.direct_hdr = self.stack.enter_context(patch.object(runtime,
            '_set_hdr_enabled_sync', return_value={'ok': True}))
        self.stack.enter_context(patch.object(runtime, '_primary_display_name', return_value='display-a'))
        self.stack.enter_context(patch.object(runtime, '_get_display_status_sync',
            side_effect=lambda *_: {'current': dict(self.mode)}))
        self.stack.enter_context(patch.object(runtime, '_get_registered_display_mode_sync',
            side_effect=lambda *_: dict(self.registered)))
        self.stack.enter_context(patch.object(runtime, '_resolve_display_mode_sync',
            side_effect=lambda desired, device_name=None: (dict(desired), '', '')))
        self.stack.enter_context(patch.object(runtime, '_resolve_display_mode_sync',
            side_effect=lambda desired, device_name=None: (dict(desired), '', '')))
        self.stack.enter_context(patch.object(runtime, '_get_hdr_status_sync', return_value={'available': True}))
        self.stack.enter_context(patch.object(runtime, '_hdr_target_states', side_effect=lambda *_: dict(self.hdr)))
        self.stack.enter_context(patch.object(runtime, '_set_advanced_color_states', side_effect=hdr_write))
        self.stack.enter_context(patch.object(runtime, '_wait_for_hdr_states',
            side_effect=lambda expected: (expected == self.hdr, {})))
        self.plugin = self.module.Plugin()
        self.stack.enter_context(patch.object(self.plugin, '_record_event'))

    def test_legacy_entrypoints_cannot_mutate_during_preview(self):
        token = asyncio.run(self.plugin.begin_display_change({'kind': 'display', 'hz': 120}))['token']
        for method, request in (
            ('set_display_mode', {'width': 1280, 'height': 720}),
            ('set_refresh_rate', {'hz': 144}),
            ('set_hdr_enabled', {'enabled': True}),
        ):
            with self.subTest(method=method):
                before = self.display_write.call_count, self.direct_hdr.call_count
                try:
                    result = asyncio.run(getattr(self.plugin, method)(request))
                except (ValueError, RuntimeError, PermissionError):
                    result = {'ok': False}
                self.assertEqual((self.display_write.call_count, self.direct_hdr.call_count), before,
                    f'{method} bypassed the pending display transaction {token}; result={result}')

    def test_legacy_display_from_idle_must_reject_or_start_a_guarded_preview(self):
        try:
            result = asyncio.run(self.plugin.set_display_mode({'width': 1280, 'height': 720}))
        except (ValueError, RuntimeError, PermissionError):
            result = {'ok': False}
        if self.display_write.called:
            self.assertIsNotNone(self.plugin._display_pending,
                f'Legacy entrypoint wrote display mode without a pending transaction: {result}')
            self.assertTrue(self.timer.called, 'A successful preview requires backend rollback timer')
        else:
            self.assertFalse(result.get('ok'), result)

    def test_guarded_entrypoint_preserves_registry_until_confirmation(self):
        previous = dict(self.registered)
        result = asyncio.run(self.plugin.begin_display_change({'kind': 'display', 'hz': 120}))
        self.assertTrue(result['ok'])
        self.assertEqual(self.registered, previous)
        self.assertTrue(self.timer.called)
        finished = asyncio.run(self.plugin.finish_display_change({'token': result['token'], 'keep': False}))
        self.assertTrue(finished['terminal'])
        self.assertEqual(self.mode, previous)

    def test_lost_finish_result_is_queryable_by_token_without_another_write(self):
        result = asyncio.run(self.plugin.begin_display_change({'kind': 'display', 'hz': 120}))
        request = {'token': result['token']}
        finished = asyncio.run(self.plugin.finish_display_change({**request, 'keep': True}))
        calls = self.display_write.call_count
        recovered = asyncio.run(self.plugin.get_display_change(request))
        self.assertEqual(recovered['state'], 'completed')
        self.assertEqual(recovered['kept'], finished['kept'])
        self.assertTrue(recovered['terminal'])
        self.assertEqual(self.display_write.call_count, calls)
        restarted = self.module.Plugin()
        self.assertEqual(asyncio.run(restarted.get_display_change(request)), recovered)
        self.assertEqual(self.display_write.call_count, calls)

    def test_state_query_returns_pending_token_and_remaining_time(self):
        result = asyncio.run(self.plugin.begin_display_change({'kind': 'display', 'hz': 120}))
        recovered = asyncio.run(self.plugin.get_display_change())
        self.assertEqual(recovered['token'], result['token'])
        self.assertEqual(recovered['state'], 'previewing')
        self.assertGreater(recovered['secondsRemaining'], 0)
        self.assertLessEqual(recovered['secondsRemaining'], 15)

    def test_unknown_token_does_not_claim_a_terminal_result(self):
        recovered = asyncio.run(self.plugin.get_display_change({'token': 'missing'}))
        self.assertEqual(recovered['state'], 'idle')
        self.assertNotIn('terminal', recovered)
        self.display_write.assert_not_called()

    def test_malformed_query_token_is_rejected_without_writes(self):
        with self.assertRaises(ValueError):
            asyncio.run(self.plugin.get_display_change({'token': []}))
        self.display_write.assert_not_called()


if __name__ == '__main__':
    unittest.main()

import asyncio
import importlib.util
import json
import logging
import pathlib
import sys
import tempfile
import types
import unittest
import subprocess
from contextlib import ExitStack
from unittest.mock import patch, Mock


class PanelBackendTests(unittest.TestCase):
    def _allow_hdr(self, plugin):
        """The fixture exposes verified HDR-capable targets."""
        return plugin

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = pathlib.Path(self.temp.name)
        decky = types.ModuleType("decky")
        decky.DECKY_PLUGIN_SETTINGS_DIR = str(self.root / "gaming-mode")
        decky.logger = logging.getLogger("panel-test")
        sys.modules["decky"] = decky
        path = pathlib.Path(__file__).resolve().parents[1] / "main.py"
        spec = importlib.util.spec_from_file_location("panel_test_runtime", path)
        self.module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.module)
        self.mocks = ExitStack()
        self.addCleanup(self.mocks.close)
        self.original = {'width': 1920, 'height': 1080, 'hz': 60}
        self.current = dict(self.original)
        self.registered = dict(self.original)
        self.hdr_states = {'screen-a': True, 'screen-b': False}
        runtime = self.module._runtime
        self.native_display_set = runtime._set_display_mode_sync
        self.mocks.enter_context(patch.object(runtime, '_primary_display_name', return_value='test-display'))
        self.mocks.enter_context(patch.object(runtime, '_get_display_status_sync', side_effect=lambda *_: {'current': dict(self.current)}))
        self.mocks.enter_context(patch.object(runtime, '_get_registered_display_mode_sync', side_effect=lambda *_: dict(self.registered)))
        # None = the panel accepts anything; a list restricts it to those modes.
        self.supported_modes = None
        self.mocks.enter_context(patch.object(runtime, '_resolve_display_mode_sync', side_effect=self.resolve_mode))
        self.apply = self.mocks.enter_context(patch.object(runtime, '_set_display_mode_sync', side_effect=self.apply_mode))
        self.mocks.enter_context(patch.object(runtime, '_get_hdr_status_sync', return_value={'available': True, 'real_state': True, 'targets': [{'supported': True, 'force_disabled': False}]}))
        self.mocks.enter_context(patch.object(runtime, '_hdr_target_states', side_effect=lambda *_: dict(self.hdr_states)))
        self.hdr_write = self.mocks.enter_context(patch.object(runtime, '_set_advanced_color_states', side_effect=self.apply_hdr))
        self.mocks.enter_context(patch.object(runtime, '_wait_for_hdr_states', side_effect=lambda expected: (self.hdr_states == expected, {})))
        self.timer = self.mocks.enter_context(patch.object(self.module.threading, 'Timer'))

    def test_optional_news_import_failure_does_not_stop_core_backend(self):
        with patch.object(self.module.importlib.util, 'spec_from_file_location', side_effect=ImportError('frozen runtime missing module')):
            plugin = self.module.Plugin()
        self.assertIsNone(plugin._home_news)
        self.assertIsInstance(asyncio.run(plugin.get_panel_preferences()), dict)
        with self.assertRaisesRegex(RuntimeError, 'News unavailable'):
            asyncio.run(plugin.get_home_news_settings())

    def resolve_mode(self, desired, device_name=None):
        self.assertEqual(device_name, 'test-display')
        if self.supported_modes is None:
            return dict(desired), '', ''
        if any(mode == desired for mode in self.supported_modes):
            return dict(desired), '', ''
        same_size = [mode for mode in self.supported_modes
                     if mode['width'] == desired['width'] and mode['height'] == desired['height']]
        if same_size:
            best = min(same_size, key=lambda mode: (abs(mode['hz'] - desired['hz']), -mode['hz']))
            return dict(best), '', ''
        return None, 'The display does not support the requested mode.', 'display_mode_unsupported'

    def apply_mode(self, width, height, hz, persist=True, device_name=None):
        self.assertEqual(device_name, 'test-display')
        self.current = dict(width=width, height=height, hz=hz)
        if persist:
            self.registered = dict(self.current)
        return {'ok': True, 'current': dict(self.current)}

    def apply_hdr(self, *, states):
        self.hdr_states = dict(states)
        return {'all_accepted': True}

    def begin(self, plugin):
        result = plugin._begin_display_change({'kind': 'display', 'width': 1280, 'height': 720})
        self.assertTrue(result['ok'], result)
        return result['token']

    def finish(self, plugin, token, keep):
        return asyncio.run(plugin.finish_display_change({'token': token, 'keep': keep}))

    def tearDown(self):
        self.temp.cleanup()

    def test_resolution_change_adopts_a_refresh_rate_the_panel_supports(self):
        # The old code carried the current 60 Hz over to a resolution that only
        # offers 120 Hz; Windows rejected the pair and the change rolled back at
        # once, which read as "the resolution dropdown does nothing".
        self.supported_modes = [{'width': 1920, 'height': 1080, 'hz': 60},
                                {'width': 3840, 'height': 2160, 'hz': 120}]
        plugin = self.module.Plugin()
        result = plugin._begin_display_change({'kind': 'display', 'width': 3840, 'height': 2160})
        self.assertTrue(result['ok'], result)
        self.assertEqual(self.current, {'width': 3840, 'height': 2160, 'hz': 120})
        self.assertEqual(plugin._display_pending['desired'], {'width': 3840, 'height': 2160, 'hz': 120})

    def test_unsupported_mode_reports_a_specific_error_and_touches_no_hardware(self):
        self.supported_modes = [{'width': 1920, 'height': 1080, 'hz': 60}]
        plugin = self.module.Plugin()
        result = plugin._begin_display_change({'kind': 'display', 'width': 5120, 'height': 2880})
        self.assertFalse(result['ok'])
        self.assertEqual(result['code'], 'display_mode_unsupported')
        self.assertIn('does not support', result['message'])
        self.apply.assert_not_called()
        self.assertIsNone(plugin._display_pending)
        self.assertEqual(self.current, self.original)

    def test_hdr_write_accepted_by_every_target_previews_while_the_link_resyncs(self):
        # An HDMI 2.1 sink can take longer to come back than the read-back
        # window. Toggling HDR straight back on top of that renegotiation is
        # what strands the panel on "no signal": stay in preview instead and
        # let the confirmation timer own the rollback.
        plugin = self._allow_hdr(self.module.Plugin())
        self.mocks.enter_context(patch.object(self.module._runtime, '_wait_for_hdr_states',
                                              side_effect=lambda expected: (False, {})))
        result = plugin._begin_display_change({'kind': 'hdr', 'enabled': False})
        self.assertTrue(result['ok'], result)
        self.assertEqual(plugin._display_pending['state'], 'previewing')
        self.assertEqual(self.hdr_write.call_count, 1)
        self.assertEqual(self.hdr_states, {'screen-a': False, 'screen-b': False})

    def test_hdr_write_rejected_by_the_driver_rolls_back_immediately(self):
        plugin = self._allow_hdr(self.module.Plugin())
        # The driver refuses the write, so nothing moved and the rollback of an
        # untouched state succeeds: the transaction must end, not preview.
        self.mocks.enter_context(patch.object(self.module._runtime, '_set_advanced_color_states',
                                              return_value={'all_accepted': False, 'message': 'Windows rejected the HDR request.'}))
        result = plugin._begin_display_change({'kind': 'hdr', 'enabled': False})
        self.assertFalse(result['ok'])
        self.assertEqual(result['code'], 'display_change_rejected')
        self.assertIn('rejected', result['message'])
        self.assertIsNone(plugin._display_pending)
        self.assertEqual(self.hdr_states, {'screen-a': True, 'screen-b': False})

    def test_failed_rollback_stops_re_arming_instead_of_flapping_forever(self):
        plugin = self.module.Plugin()
        token = self.begin(plugin)
        self.apply.side_effect = lambda *args, **kwargs: {'ok': False}
        for _ in range(self.module._DISPLAY_RECOVERY_ATTEMPTS + 1):
            result = plugin._revert_display(token)
            self.assertFalse(result['ok'])
        self.assertTrue(result['exhausted'])
        # One arm for the preview window plus one per recovery attempt, then stop.
        self.assertEqual(self.timer.call_count, self.module._DISPLAY_RECOVERY_ATTEMPTS + 1)

    def test_saved_order_visibility_and_sections_survive_new_instance(self):
        plugin = self.module.Plugin()
        saved = asyncio.run(plugin.save_panel_preferences(dict(
            order=["audio", "home"], hidden=["store"], active="audio", collapsed=["radeon"])))
        restored = asyncio.run(self.module.Plugin().get_panel_preferences())
        self.assertEqual(saved, restored)
        self.assertEqual(restored["order"][0], "audio")
        self.assertEqual(restored["hidden"], ["store"])
        self.assertFalse(pathlib.Path(plugin._panel_path + ".tmp").exists())

    def test_clock_position_persists_with_date_hidden(self):
        plugin = self.module.Plugin()
        asyncio.run(plugin.save_panel_preferences(dict(topbarClockLeft=True, topbarDateEnabled=False)))
        restored = asyncio.run(self.module.Plugin().get_panel_preferences())
        self.assertTrue(restored["topbarClockLeft"])
        self.assertFalse(restored["topbarDateEnabled"])
        asyncio.run(plugin.save_panel_preferences(dict(restored, topbarClockLeft=False)))
        self.assertFalse(asyncio.run(self.module.Plugin().get_panel_preferences())["topbarClockLeft"])

    def test_visible_decky_is_valid_fallback_when_other_tabs_are_hidden(self):
        tabs = ["home", "audio", "performance", "graphics", "controller", "store"]
        result = asyncio.run(self.module.Plugin().save_panel_preferences(dict(hidden=tabs, active="audio")))
        self.assertTrue(result["deckyHostEnabled"])
        self.assertEqual(set(result["hidden"]), set(tabs))
        self.assertEqual(result["active"], "decky")
        self.assertNotIn(result["active"], result["hidden"])
        self.assertEqual(asyncio.run(self.module.Plugin().get_panel_preferences()), result)

    def test_all_tabs_hidden_including_decky_preserves_one_visible_fallback(self):
        tabs = ["home", "audio", "performance", "graphics", "controller", "store", "decky"]
        result = asyncio.run(self.module.Plugin().save_panel_preferences(dict(hidden=tabs, active="audio")))
        self.assertEqual(set(result["order"]) - set(result["hidden"]), {"home"})
        self.assertEqual(set(result["hidden"]), set(tabs) - {"home"})
        self.assertNotIn(result["active"], result["hidden"])
        self.assertEqual(result["active"], "home")
        self.assertEqual(asyncio.run(self.module.Plugin().get_panel_preferences()), result)

    def test_existing_quick_settings_profile_is_imported_only_once(self):
        old = self.root / "quick-settings"
        old.mkdir()
        source = old / "quick-settings-2.3.json"
        source.write_text(json.dumps({"lossless_profiles": {"42": {"title": "Original"}}}), encoding="utf-8")
        plugin = self.module.Plugin()
        path = pathlib.Path(plugin._settings_path)
        self.assertEqual(json.loads(path.read_text())["lossless_profiles"]["42"]["title"], "Original")
        source.write_text("{}")
        self.module.Plugin()
        self.assertEqual(json.loads(path.read_text())["lossless_profiles"]["42"]["title"], "Original")

    def test_decky_can_resolve_inherited_rpc_methods(self):
        plugin = self.module.Plugin()
        for name in ("get_capabilities", "set_volume", "get_lossless_profile", "set_tdp", "get_panel_preferences"):
            if name == "set_volume":
                continue  # Volume uses the existing local HTTP agent.
            self.assertTrue(callable(getattr(plugin, name)))

    def test_display_timeout_restores_original_without_frontend(self):
        plugin = self.module.Plugin()
        token = self.begin(plugin)
        self.assertEqual(self.timer.call_args.args[0], 15)
        self.timer.call_args.args[1](*self.timer.call_args.kwargs['args'])
        self.apply.assert_called_with(1920, 1080, 60, False, 'test-display')
        self.assertIsNone(plugin._display_pending)
        self.assertFalse(self.finish(plugin, token, True)['kept'])

    def test_display_keep_cancels_timer_and_rejects_overlap(self):
        plugin = self.module.Plugin()
        token = self.begin(plugin)
        self.assertEqual(self.registered, self.original)
        self.assertTrue(plugin._begin_display_change({'kind': 'display', 'hz': 144})['busy'])
        kept = self.finish(plugin, token, True)
        self.assertTrue(kept['kept'])
        self.assertEqual(self.registered, self.current)
        self.assertEqual(self.finish(plugin, token, True), kept)
        self.assertEqual(self.finish(plugin, token, False), kept)
        self.assertEqual(self.apply.call_count, 2)
        self.timer.return_value.cancel.assert_called_once()

    def test_hdr_rollback_preserves_each_display_state(self):
        plugin = self._allow_hdr(self.module.Plugin())
        states = dict(self.hdr_states)
        result = plugin._begin_display_change({'kind': 'hdr', 'enabled': True})
        self.assertEqual(self.hdr_states, {'screen-a': True, 'screen-b': True})
        plugin._revert_display(result['token'])
        self.hdr_write.assert_called_with(states=states)
        self.assertEqual(self.hdr_states, states)

    def test_expiry_uses_monotonic_not_wall_clock(self):
        plugin = self.module.Plugin()
        token = self.begin(plugin)
        with patch.object(self.module.time, 'monotonic', return_value=plugin._display_pending['deadline']):
            result = self.finish(plugin, token, True)
        self.assertTrue(result['terminal'])
        self.assertTrue(result['expired'])
        self.assertFalse(result['kept'])
        self.assertEqual(self.current, self.original)

    def test_wall_clock_jump_does_not_expire_preview(self):
        plugin = self.module.Plugin()
        token = self.begin(plugin)
        with patch.object(self.module.time, 'time', return_value=10**12):
            self.assertTrue(self.finish(plugin, token, True)['kept'])

    def test_failed_rollback_keeps_journal_and_retries(self):
        plugin = self.module.Plugin()
        token = self.begin(plugin)
        self.apply.side_effect = None
        self.apply.return_value = {'ok': False}
        failed = self.finish(plugin, token, False)
        self.assertFalse(failed['terminal'])
        self.assertEqual(plugin._display_pending['state'], 'recovery_required')
        self.assertTrue(pathlib.Path(plugin._display_journal).exists())
        self.assertEqual(self.timer.call_args.args[0], 2)
        self.apply.side_effect = self.apply_mode
        self.assertTrue(self.finish(plugin, token, False)['terminal'])
        self.assertEqual(self.current, self.original)

    def test_readback_mismatch_is_not_success(self):
        plugin = self.module.Plugin()
        token = self.begin(plugin)
        self.apply.side_effect = None
        self.apply.return_value = {'ok': True}
        self.assertFalse(self.finish(plugin, token, False)['ok'])
        self.assertIsNotNone(plugin._display_pending)

    def test_external_change_prevents_keep(self):
        plugin = self.module.Plugin()
        token = self.begin(plugin)
        self.current['hz'] = 144
        result = self.finish(plugin, token, True)
        self.assertFalse(result['kept'])
        self.assertEqual(self.registered, self.original)

    def test_hdr_keep_requires_target_readback(self):
        plugin = self._allow_hdr(self.module.Plugin())
        result = plugin._begin_display_change({'kind': 'hdr', 'enabled': True})
        self.hdr_states['screen-b'] = False
        self.assertFalse(self.finish(plugin, result['token'], True)['kept'])

    def test_startup_recovers_preview_before_accepting_new_request(self):
        first = self.module.Plugin()
        token = self.begin(first)
        second = self.module.Plugin()
        second._recover_display_sync()
        self.assertEqual(self.current, self.original)
        self.assertTrue(self.finish(second, token, False)['terminal'])
        self.assertIsNone(second._display_pending)

    def test_completed_keep_survives_restart_and_lost_response(self):
        first = self.module.Plugin()
        token = self.begin(first)
        expected = self.finish(first, token, True)
        calls = self.apply.call_count
        second = self.module.Plugin()
        self.assertEqual(self.finish(second, token, True), expected)
        self.assertEqual(calls, self.apply.call_count)

    def test_startup_confirming_restores_registry_and_live_baselines(self):
        self.registered = {'width': 1600, 'height': 900, 'hz': 60}
        registry_before = dict(self.registered)
        first = self.module.Plugin()
        self.begin(first)
        first._display_pending.update(state='confirming', persistence_attempted=True)
        first._save_display_recovery()
        self.registered = dict(self.current)
        second = self.module.Plugin()
        second._recover_display_sync()
        self.assertEqual(self.current, self.original)
        self.assertEqual(self.registered, registry_before)

    def test_failed_persistence_restores_registry_and_live_baseline(self):
        plugin = self.module.Plugin()
        token = self.begin(plugin)
        def fail_once(width, height, hz, persist=True, device_name=None):
            result = self.apply_mode(width, height, hz, persist, device_name)
            if persist and width == 1280:
                result['ok'] = False
            return result
        self.apply.side_effect = fail_once
        result = self.finish(plugin, token, True)
        self.assertTrue(result['terminal'])
        self.assertFalse(result['kept'])
        self.assertEqual(self.current, self.original)
        self.assertEqual(self.registered, self.original)

    def test_journal_failure_before_apply_never_touches_hardware(self):
        plugin = self.module.Plugin()
        with patch.object(plugin, '_save_display_recovery', side_effect=OSError('disk full')):
            result = plugin._begin_display_change({'kind': 'display', 'hz': 120})
        self.assertFalse(result['ok'])
        self.apply.assert_not_called()
        self.assertIsNone(plugin._display_pending)

    def test_terminal_journal_failure_does_not_acknowledge_keep(self):
        plugin = self.module.Plugin()
        token = self.begin(plugin)
        save = plugin._save_display_recovery
        def fail_terminal(record=None):
            if record is not None:
                raise OSError('disk full')
            save()
        with patch.object(plugin, '_save_display_recovery', side_effect=fail_terminal):
            result = self.finish(plugin, token, True)
        self.assertFalse(result['ok'])
        self.assertEqual(self.current, self.original)
        self.assertIsNotNone(plugin._display_pending)
        self.assertTrue(self.finish(plugin, token, False)['terminal'])

    def test_corrupt_recovery_is_set_aside_instead_of_wedging_the_panel(self):
        # Uno spegnimento forzato lascia il journal a meta'. Bloccare per sempre ogni
        # cambio schermo toglie all'utente proprio il modo di rimettere a posto lo
        # schermo: il record inutilizzabile si mette da parte e si riparte.
        plugin = self.module.Plugin()
        journal = pathlib.Path(plugin._display_journal)
        journal.parent.mkdir(parents=True, exist_ok=True)
        journal.write_text('{broken', encoding='utf-8')
        result = plugin._begin_display_change({'kind': 'display', 'hz': 120})
        self.assertNotEqual(result.get('state'), 'recovery_required')
        self.assertIsNotNone(plugin._display_recovery_note)
        self.assertTrue(pathlib.Path(str(journal) + '.corrupt').exists(),
                        'il record inutilizzabile viene messo da parte, non riprovato all\'infinito')
        self.assertNotIn('{broken', journal.read_text(encoding='utf-8') if journal.exists() else '')

    def test_invalid_request_does_not_write(self):
        plugin = self.module.Plugin()
        for request in ({'kind': 'display', 'width': True}, {'kind': 'display', 'hz': -1}, {'kind': 'hdr', 'enabled': 'false'}):
            with self.assertRaises(ValueError):
                plugin._begin_display_change(request)
        self.apply.assert_not_called()
        self.hdr_write.assert_not_called()

    def test_unknown_token_cannot_acknowledge_a_rollback(self):
        plugin = self.module.Plugin()
        result = self.finish(plugin, 'unknown', False)
        self.assertFalse(result['ok'])
        self.assertFalse(result['terminal'])
        self.apply.assert_not_called()

    def test_display_rpc_never_blocks_event_loop(self):
        import threading
        plugin = self.module.Plugin()
        token = self.begin(plugin)
        entered, release = threading.Event(), threading.Event()
        def slow_finish(request):
            entered.set()
            release.wait(3)
            return {'ok': True}
        async def exercise():
            with patch.object(plugin, '_finish_display_change', side_effect=slow_finish):
                task = asyncio.create_task(plugin.finish_display_change({'token': token, 'keep': True}))
                try:
                    for _ in range(100):
                        if entered.is_set():
                            break
                        await asyncio.sleep(0.001)
                    self.assertTrue(entered.is_set())
                    self.assertFalse(task.done())
                finally:
                    release.set()
                    await task
        asyncio.run(exercise())

    def test_modal_waits_for_confirmed_response_and_allows_retry(self):
        script = r'''
const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const ts = require('typescript');
const source = fs.readFileSync('src/quickSettings/index.tsx', 'utf8');
const component = source.slice(source.indexOf('function HdrConfirmModal('), source.indexOf('const sharedDisplayModal'));
const code = ts.transpileModule(component, {compilerOptions: {jsx: ts.JsxEmit.React, target: ts.ScriptTarget.ES2022}}).outputText;
function harness() {
  let slots = [], cursor = 0, effect, interval, now = 0, initialized = false;
  const context = {
    React: {createElement: (type, props, ...children) => ({type, props: props || {}, children})},
    useRef: value => { const i = cursor++; return slots[i] ??= {current: value}; },
    useState: value => { const i = cursor++; if (!(i in slots)) slots[i] = typeof value === 'function' ? value() : value;
      return [slots[i], next => { slots[i] = typeof next === 'function' ? next(slots[i]) : next; }]; },
    useEffect: callback => { if (!initialized) effect = callback; },
    performance: {now: () => now}, Date,
    window: {setInterval: callback => { interval = callback; return 1; }, clearInterval: () => {}},
    ModalRoot: 'ModalRoot', DialogButton: 'DialogButton', Focusable: 'Focusable',
  };
  vm.createContext(context); vm.runInContext(code, context);
  return {
    render(props) {cursor = 0; const tree = context.HdrConfirmModal(props); if (!initialized) {initialized = true; effect();} return tree;},
    expire() {now = 16000; interval();},
  };
}
function buttons(tree) {return [tree, ...tree.children.flatMap(child => child && typeof child === 'object' ? buttons(child) : [])].filter(x => x.type === 'DialogButton');}
const flush = async () => {for (let i = 0; i < 8; i++) await Promise.resolve();};
(async () => {
  const h = harness(); let resolve, closed = 0, calls = 0, reverts = 0;
  const props = {label: {}, expiresAt: Date.now() + 15000, secondsRemaining: 15,
    closeModal: () => closed++, onKeep: () => {calls++; return new Promise(r => resolve = r);},
    onRevert: async () => {reverts++; return true;}};
  buttons(h.render(props))[1].props.onClick();
  assert.equal(closed, 0); assert.equal(calls, 1);
  let tree = h.render(props); assert.equal(buttons(tree)[0].props.disabled, true);
  tree.props.closeModal(); assert.equal(reverts, 0);
  resolve(false); await flush(); assert.equal(closed, 0);
  tree = h.render(props); assert.equal(buttons(tree)[1].props.disabled, false);
  buttons(tree)[1].props.onClick(); resolve(true); await flush(); assert.equal(closed, 1);
  const timeout = harness(); let timeoutClosed = 0, timeoutResolve;
  const timeoutProps = {...props, closeModal: () => timeoutClosed++, onRevert: () => new Promise(r => timeoutResolve = r)};
  timeout.render(timeoutProps); timeout.expire(); assert.equal(timeoutClosed, 0);
  timeoutResolve(true); await flush(); assert.equal(timeoutClosed, 1);
})().catch(error => {console.error(error); process.exitCode = 1;});
'''
        result = subprocess.run(['node', '-e', script], cwd=pathlib.Path(__file__).resolve().parents[1],
                                capture_output=True, text=True, timeout=30)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_win32_preview_does_not_persist_and_checks_readback(self):
        runtime = self.module._runtime
        def mode(width, height, hz):
            value = runtime._DEVMODE()
            value.dmPelsWidth, value.dmPelsHeight, value.dmDisplayFrequency = width, height, hz
            return value
        user32 = Mock()
        user32.ChangeDisplaySettingsExW.return_value = 0
        with patch.object(runtime.ctypes, 'windll', types.SimpleNamespace(user32=user32), create=True), \
             patch.object(runtime, '_read_current_devmode', side_effect=[mode(1920, 1080, 60), mode(1280, 720, 60)]):
            result = self.native_display_set(1280, 720, 60, False, 'test-display')
        self.assertTrue(result['verified'])
        calls = user32.ChangeDisplaySettingsExW.call_args_list
        self.assertEqual(calls[0].args[3], runtime._CDS_TEST)
        self.assertEqual(calls[1].args[3], 0)
        self.assertTrue(all(call.args[0] == 'test-display' for call in calls))

    def test_win32_success_code_without_matching_mode_is_failure(self):
        runtime = self.module._runtime
        mode = runtime._DEVMODE()
        mode.dmPelsWidth, mode.dmPelsHeight, mode.dmDisplayFrequency = 1920, 1080, 60
        user32 = Mock()
        user32.ChangeDisplaySettingsExW.return_value = 0
        with patch.object(runtime.ctypes, 'windll', types.SimpleNamespace(user32=user32), create=True), \
             patch.object(runtime, '_read_current_devmode', return_value=mode), \
             patch.object(runtime.time, 'monotonic', side_effect=[0, 0.1, 4]), \
             patch.object(runtime.time, 'sleep'):
            result = self.native_display_set(1280, 720, 60, False, 'test-display')
        self.assertFalse(result['ok'])
        self.assertFalse(result['verified'])

    def test_win32_persistence_requires_registry_readback(self):
        runtime = self.module._runtime
        mode = runtime._DEVMODE()
        mode.dmPelsWidth, mode.dmPelsHeight, mode.dmDisplayFrequency = 1280, 720, 60
        user32 = Mock()
        user32.ChangeDisplaySettingsExW.return_value = 0
        with patch.object(runtime.ctypes, 'windll', types.SimpleNamespace(user32=user32), create=True), \
             patch.object(runtime, '_read_current_devmode', return_value=mode):
            result = self.native_display_set(1280, 720, 60, True, 'test-display')
        self.assertFalse(result['ok'])
        self.assertEqual(user32.ChangeDisplaySettingsExW.call_args.args[3], runtime._CDS_UPDATEREGISTRY)

    def test_malformed_terminal_record_is_discarded_without_locking_the_user_out(self):
        # Un record TERMINALE malformato descrive un cambio gia' concluso: non c'e'
        # niente da ripristinare. Rifiutare ogni cambio successivo non protegge nulla
        # e toglie all'utente l'unico modo di rimettere a posto lo schermo, che e'
        # esattamente il vicolo cieco in cui si e' trovato dopo uno spegnimento forzato.
        plugin = self.module.Plugin()
        journal = pathlib.Path(plugin._display_journal)
        journal.parent.mkdir(parents=True, exist_ok=True)
        journal.write_text(json.dumps({'token': 'test', 'state': 'completed', 'result': []}))
        result = plugin._begin_display_change({'kind': 'display', 'hz': 120})
        self.assertNotEqual(result.get('state'), 'recovery_required')
        self.assertIsNotNone(plugin._display_recovery_note)
        self.assertTrue(pathlib.Path(str(journal) + '.corrupt').exists())


if __name__ == "__main__":
    unittest.main()

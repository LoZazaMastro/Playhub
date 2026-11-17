"""Issue #19: run the actual backend in a clean, reduced-runtime process.

Only Decky configuration and hardware readouts are fixtures. No HID is opened.
"""
import subprocess
import sys
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]
CHILD = r'''
import asyncio, builtins, importlib.util, logging, pathlib, sys, tempfile, types
from unittest.mock import patch
root = pathlib.Path(sys.argv[1])
scenario = sys.argv[2]
with tempfile.TemporaryDirectory() as temp:
    decky = types.ModuleType('decky')
    decky.DECKY_PLUGIN_SETTINGS_DIR = temp
    decky.logger = logging.getLogger('issue19')
    sys.modules['decky'] = decky
    original_import = builtins.__import__
    blocked = []
    def limited_import(name, *args, **kwargs):
        if name == 'colorsys':
            blocked.append(name)
            raise ModuleNotFoundError("No module named 'colorsys'", name='colorsys')
        return original_import(name, *args, **kwargs)
    guard = patch('builtins.__import__', side_effect=limited_import)
    with guard:
        spec = importlib.util.spec_from_file_location('issue19_backend', root / 'main.py')
        backend = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(backend)
        plugin = backend.Plugin()
        audio = {'outputs': [{'id':'fixture-speakers','name':'Speakers'}], 'inputs': []}
        with patch.object(backend._runtime, '_get_audio_devices_sync', return_value=audio), \
             patch.object(backend._runtime, '_get_capabilities_sync', return_value={'audio':True}):
            assert asyncio.run(plugin.get_audio_devices()) == audio
            assert asyncio.run(plugin.get_initial_state())['audio'] == audio
            assert isinstance(asyncio.run(plugin.get_panel_preferences()), dict)
        assert 'playhub_ayaneo3_controller' not in sys.modules
        assert blocked == [], 'non-AYANEO startup attempted the optional dependency'
        if scenario == 'missing-colorsys':
            # Exercise the genuine controller with a read-only original HID fixture.
            spec = importlib.util.spec_from_file_location('issue19_aya', root / 'quick_settings/ayaneo3_controller.py')
            aya = importlib.util.module_from_spec(spec)
            sys.modules[spec.name] = aya
            spec.loader.exec_module(aya)
            class Timer:
                daemon = False
                def __init__(self, *args): pass
                def start(self): pass
                def cancel(self): pass
            class Session:
                binding = 'original-fixture'
                writes = []
                closed = False
                def current(self): return not self.closed
                def close(self): self.closed = True
                def exchange(self, command):
                    assert command == aya.CHECK, 'no write permitted without RGB conversion'
                    report = bytearray(64)
                    report[3] = 8; report[18] = 2; report[21:23] = b'\x33\x22'
                    report[7] = report[11] = 1
                    report[8:11] = report[12:15] = b'\x20\x40\x60'
                    report[23] = 0x30; report[32:34] = b'\x02\x10'
                    return bytes(report)
            session = Session()
            transport = types.SimpleNamespace(open=lambda: session)
            controller = aya.Ayaneo3Controller(transport_factory=lambda:transport, timer_factory=Timer)
            sys.modules['playhub_ayaneo3_controller'] = aya
            with patch.object(aya, 'Ayaneo3Controller', return_value=controller):
                result = asyncio.run(plugin.get_ayaneo3_controller())
            assert result['ok'] is False and result['available'] is False, result
            assert result['reason'] == 'dependency_unavailable', result
            assert session.closed and session.writes == []
            assert blocked == ['colorsys']
        elif scenario == 'missing-ayaneo-module':
            original_spec = backend.importlib.util.spec_from_file_location
            def absent_ayaneo(name, path, *args, **kwargs):
                if name == 'playhub_ayaneo3_controller':
                    raise ModuleNotFoundError('optional hardware dependency unavailable')
                return original_spec(name, path, *args, **kwargs)
            with patch.object(backend.importlib.util, 'spec_from_file_location', side_effect=absent_ayaneo):
                for method, args in ((plugin.get_ayaneo3_controller,()),
                                     (plugin.set_ayaneo3_controller,({},)),
                                     (plugin.restore_ayaneo3_controller,({},))):
                    result = asyncio.run(method(*args))
                    assert result['reason'] == 'dependency_unavailable', result
                    assert result['available'] is False and result['writeAvailable'] is False
            assert isinstance(asyncio.run(plugin.get_panel_preferences()), dict)
            assert 'playhub_ayaneo3_controller' not in sys.modules
        else:
            assert scenario == 'startup'
        plugin._ayaneo3_controller.dispose()
'''


class OptionalAyaneoImportTests(unittest.TestCase):
    def run_child(self, scenario):
        completed = subprocess.run([sys.executable, '-I', '-c', CHILD, str(ROOT), scenario],
                                   capture_output=True, text=True, timeout=30)
        self.assertEqual(0, completed.returncode, completed.stdout + completed.stderr)

    def test_non_ayaneo_startup_and_audio_do_not_import_colorsys(self):
        self.run_child('startup')

    def test_missing_colorsys_is_typed_and_never_writes_hardware(self):
        self.run_child('missing-colorsys')

    def test_optional_ayaneo_import_failure_is_isolated_from_core_controls(self):
        self.run_child('missing-ayaneo-module')

    def test_hue_matches_standard_colorsys_and_wraps(self):
        import colorsys
        import importlib.util
        import random
        spec = importlib.util.spec_from_file_location('issue19_rgb', ROOT / 'quick_settings/ayaneo3_controller.py')
        module = importlib.util.module_from_spec(spec)
        sys.modules[spec.name] = module
        try:
            spec.loader.exec_module(module)
            rng = random.Random(19)
            colors = [bytes((v, v, v)) for v in range(256)]
            colors += [bytes(rng.randrange(256) for _ in range(3)) for _ in range(256)]
            colors += [bytes((255,0,0)), bytes((0,255,0)), bytes((0,0,255))]
            for color in colors:
                for degrees in (-730, -360, -10, 0, 10, 360, 730):
                    h, s, v = colorsys.rgb_to_hsv(*(x / 255 for x in color))
                    expected = bytes(round(x*255) for x in colorsys.hsv_to_rgb((h+degrees/360)%1,s,v))
                    self.assertEqual(expected, module._hue(color, degrees))
        finally:
            sys.modules.pop(spec.name, None)


if __name__ == '__main__':
    unittest.main()

"""Actual power reader/profile methods with a fake Windows API; no native calls."""
from pathlib import Path
import ast
import copy
import ctypes
import types
import unittest


ROOT = Path(__file__).resolve().parents[1]
TREE = ast.parse((ROOT/'main.py').read_text(encoding='utf-8'))
PLUGIN = next(node for node in TREE.body if isinstance(node, ast.ClassDef) and node.name == 'Plugin')


def actual_reader(ac=0, flags=1, percent=50, remaining=3661, ok=True, platform='nt'):
    calls = []
    def read(pointer):
        calls.append(1)
        status = pointer._obj
        assert ctypes.sizeof(status) == 12
        status.ac, status.flags, status.percent = ac, flags, percent
        status.remaining, status.full = remaining, 0xffffffff
        return int(ok)
    native = types.SimpleNamespace(Structure=ctypes.Structure, c_ubyte=ctypes.c_ubyte, byref=ctypes.byref,
        windll=types.SimpleNamespace(kernel32=types.SimpleNamespace(GetSystemPowerStatus=read)))
    names = ('_get_power_status_sync', '_get_device_profile_sync')
    methods = [copy.deepcopy(node) for node in PLUGIN.body if isinstance(node, ast.FunctionDef) and node.name in names]
    unit = ast.Module(body=[ast.ClassDef(name='Actual', bases=[], keywords=[], body=methods, decorator_list=[])], type_ignores=[])
    ast.fix_missing_locations(unit)
    env = dict(ctypes=native, wintypes=types.SimpleNamespace(DWORD=ctypes.c_uint32), os=types.SimpleNamespace(name=platform),
        _runtime=types.SimpleNamespace(_read_current_devmode=lambda:None, _get_windows_power_profile_sync=lambda read:{'available':False}))
    exec(compile(unit, 'actual-power-reader', 'exec'), env)
    actual = env['Actual']()
    actual._device_profiles = types.SimpleNamespace(get=lambda:{'selection':'auto','detected':'pc','effective':'pc'})
    return actual, calls


class BatteryRemaining(unittest.TestCase):
    def test_actual_seconds_remain_seconds_on_known_discharging_battery(self):
        for flags in (0, 1, 2, 4, 6):
            with self.subTest(flags=flags):
                reader, calls = actual_reader(flags=flags)
                value = reader._get_power_status_sync()
                self.assertIs(value['discharging'], True)
                self.assertEqual(value['remainingSeconds'], 3661)
                self.assertEqual(value['percent'], 50)
                self.assertEqual(value['source'], 'battery')
                self.assertEqual(calls, [1])

    def test_unknown_duration_never_becomes_zero(self):
        reader, _ = actual_reader(remaining=0xffffffff)
        self.assertIsNone(reader._get_power_status_sync()['remainingSeconds'])

    def test_real_zero_is_not_unknown_or_a_fallback(self):
        reader, _ = actual_reader(remaining=0)
        self.assertEqual(reader._get_power_status_sync()['remainingSeconds'], 0)
        reader, _ = actual_reader(ac=1, remaining=0)
        self.assertIsNone(reader._get_power_status_sync()['remainingSeconds'])

    def test_ac_unknown_source_charging_absent_and_reserved_flags_have_no_estimate(self):
        for ac, flags in ((1, 1), (255, 1), (2, 1), (0, 8), (0, 9), (0, 128), (0, 255), (0, 16), (0, 64)):
            with self.subTest(ac=ac, flags=flags):
                reader, _ = actual_reader(ac=ac, flags=flags)
                value = reader._get_power_status_sync()
                self.assertIs(value['discharging'], False)
                self.assertIsNone(value['remainingSeconds'])

    def test_failed_api_and_non_windows_have_no_invented_fields(self):
        reader, calls = actual_reader(ok=False)
        self.assertEqual(reader._get_power_status_sync(), {'available':False})
        self.assertEqual(calls, [1])
        reader, calls = actual_reader(platform='other')
        self.assertEqual(reader._get_power_status_sync(), {'available':False})
        self.assertEqual(calls, [])

    def test_unknown_percentage_does_not_invent_percentage_or_discard_actual_duration(self):
        reader, _ = actual_reader(percent=255)
        value = reader._get_power_status_sync()
        self.assertIsNone(value['percent'])
        self.assertEqual(value['remainingSeconds'], 3661)

    def test_actual_profile_join_preserves_the_new_native_readback(self):
        reader, calls = actual_reader(remaining=125)
        value = reader._get_device_profile_sync()
        self.assertEqual(value['power']['remainingSeconds'], 125)
        self.assertEqual(value['selection'], 'auto')
        self.assertEqual(calls, [1])


if __name__ == '__main__':
    unittest.main()

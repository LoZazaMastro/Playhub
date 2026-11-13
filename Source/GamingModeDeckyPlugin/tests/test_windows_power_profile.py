import types
import unittest
from unittest.mock import Mock, patch

from tests.test_control_surface_faults import load_runtime


class WindowsPowerProfileTests(unittest.TestCase):
    def setUp(self):
        self.runtime, directory = load_runtime()
        self.addCleanup(directory.cleanup)
        self.read_power = Mock(return_value={"source": "ac"})
        self.current = {"AC": "balanced", "DC": "efficiency"}
        self.writes = []
        self.reject = False
        self.ignore = False
        exports = types.SimpleNamespace()
        for source in ("AC", "DC"):
            setattr(exports, f"PowerGetUserConfigured{source}PowerMode", Mock(side_effect=lambda ptr, scope=source: self.read(scope, ptr)))
            setattr(exports, f"PowerSetUserConfigured{source}PowerMode", Mock(side_effect=lambda ptr, scope=source: self.write(scope, ptr)))
        self.exports = exports
        for replacement in (
            patch.object(self.runtime.os, "name", "nt"),
            patch.object(self.runtime.ctypes, "windll", types.SimpleNamespace(powrprof=exports), create=True),
        ):
            replacement.start()
            self.addCleanup(replacement.stop)

    def read(self, source, pointer):
        text = self.runtime._WINDOWS_POWER_MODES[self.current[source]]
        guid = self.runtime._guid_from_string(text)
        self.runtime.ctypes.memmove(pointer, self.runtime.ctypes.byref(guid), self.runtime.ctypes.sizeof(guid))
        return 0

    def write(self, source, pointer):
        guid = self.runtime._guid_to_string(pointer._obj)
        mode = next(name for name, value in self.runtime._WINDOWS_POWER_MODES.items() if value == guid)
        self.writes.append((source, mode))
        if self.reject:
            return 5
        if not self.ignore:
            self.current[source] = mode
        return 0

    def get(self):
        return self.runtime._get_windows_power_profile_sync(self.read_power)

    def set(self, source, mode):
        return self.runtime._set_windows_power_profile_sync(self.read_power, {"source": source, "mode": mode})

    def test_native_ac_and_battery_preferences_are_independent(self):
        self.assertEqual(self.get(), {"available": True, "source": "ac", "mode": "balanced"})
        result = self.set("ac", "performance")
        self.assertEqual(result, {"available": True, "ok": True, "source": "ac", "mode": "performance"})
        self.assertEqual(self.current, {"AC": "performance", "DC": "efficiency"})
        self.read_power.return_value = {"source": "battery"}
        self.assertEqual(self.get()["mode"], "efficiency")
        self.assertTrue(self.set("battery", "balanced")["ok"])
        self.assertEqual(self.writes, [("AC", "performance"), ("DC", "balanced")])

    def test_unplug_after_snapshot_refuses_the_write(self):
        self.read_power.side_effect = [{"source": "ac"}, {"source": "battery"}, {"source": "battery"}]
        result = self.set("ac", "performance")
        self.assertEqual(result["code"], "source_changed")
        self.assertEqual(result["source"], "battery")
        self.assertEqual(result["mode"], "efficiency")
        self.assertEqual(self.writes, [])

    def test_power_source_can_change_after_a_confirmed_scoped_write(self):
        self.read_power.side_effect = [{"source": "ac"}, {"source": "ac"}, {"source": "battery"}]
        result = self.set("ac", "performance")
        self.assertTrue(result["ok"])
        self.assertEqual(result["source"], "battery")
        self.assertEqual(result["mode"], "efficiency")
        self.assertEqual(self.writes, [("AC", "performance")])

    def test_windows_error_or_unconfirmed_write_reports_previous_value(self):
        self.reject = True
        rejected = self.set("ac", "performance")
        self.assertEqual((rejected["ok"], rejected["code"], rejected["mode"]), (False, "write_failed", "balanced"))
        self.reject = False
        self.ignore = True
        ignored = self.set("ac", "performance")
        self.assertEqual((ignored["ok"], ignored["code"], ignored["mode"]), (False, "not_confirmed", "balanced"))

    def test_unavailable_or_invalid_input_never_calls_a_native_setter(self):
        self.assertTrue(self.set("ac", "balanced")["ok"])
        for source, mode in (("auto", "balanced"), ("ac", "turbo"), ("ac", ["balanced"]), (0, "performance")):
            with self.subTest(source=source, mode=mode):
                self.assertEqual(self.set(source, mode)["code"], "invalid_profile")
        del self.exports.PowerSetUserConfiguredACPowerMode
        self.assertFalse(self.get()["available"])
        self.assertEqual(self.set("ac", "performance")["code"], "unavailable")
        self.read_power.return_value = {"source": None}
        self.assertFalse(self.get()["available"])
        self.assertEqual(self.writes, [])

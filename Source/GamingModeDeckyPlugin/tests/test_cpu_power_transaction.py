import ast
from contextlib import contextmanager
from dataclasses import replace
from pathlib import Path
import os
import tempfile
import threading
from types import SimpleNamespace
import unittest
from unittest.mock import patch

from cpu_power.transaction import PptController, Snapshot, watts_to_mw


class FakeProcessor:
    """Synthetic bounds, NOT a verified 7900X hardware profile."""
    def __init__(self):
        self.state = Snapshot("fixture-cpu", "fixture-fw", 230000, 88000, 230000,
                              1000, "fixture-limits", "fixture-independent-readback", 10)
        self.writes = []
        self.reads = 0
        self.locked = False
        self.on_read = None
        self.on_write = None

    @contextmanager
    def exclusive(self):
        if self.locked:
            raise AssertionError("nested cross-process lock")
        self.locked = True
        try:
            yield
        finally:
            self.locked = False

    def snapshot(self):
        assert self.locked
        self.reads += 1
        if self.on_read:
            self.on_read(self)
        return self.state

    def set_ppt_mw(self, value):
        assert self.locked
        self.writes.append(value)
        if self.on_write:
            return self.on_write(self, value)
        self.state = replace(self.state, active_ppt_mw=value)
        return True


class TransactionTests(unittest.TestCase):
    def setUp(self):
        self.cpu = FakeProcessor()
        self.controller = PptController(self.cpu, clock=lambda: 10)

    def apply(self, value=142):
        return self.controller.apply(value, authorized=True)

    def test_constructor_does_nothing(self):
        self.assertEqual(self.cpu.reads, 0)
        self.assertEqual(self.cpu.writes, [])

    def test_capability_requires_fresh_independent_readback(self):
        status = self.controller.status()
        self.assertTrue(status["supported_read"])
        self.assertTrue(status["supported_write"])
        self.assertEqual(status["observed_ppt_w"], 230)
        self.assertEqual(status["range"], {"min": 88, "max": 230, "step": 1, "unit": "W"})
        self.assertEqual(self.cpu.writes, [])
        self.cpu.state = replace(self.cpu.state, observed_at=0)
        status = self.controller.status()
        self.assertFalse(status["supported_read"])
        self.assertFalse(status["supported_write"])
        self.assertIsNone(status["observed_ppt_w"])

    def test_uncertainty_is_not_cleared_by_polling(self):
        def fail(*_):
            raise OSError("write lost")
        self.cpu.on_write = fail
        self.apply()
        status = self.controller.status()
        self.assertTrue(status["supported_read"])
        self.assertFalse(status["supported_write"])
        self.assertTrue(status["manual_recovery_required"])

    def test_explicit_authorization_is_literal_boolean(self):
        for value in (False, None, 1, "true"):
            self.assertEqual(self.controller.apply(142, authorized=value)["reason"],
                             "explicit_apply_required")
        self.assertEqual(self.cpu.reads, 0)

    def test_default_provider_is_not_fake_support(self):
        self.assertEqual(PptController().apply(142, authorized=True)["reason"],
                         "verified_ppt_provider_missing")

    def test_watts_are_not_clamped_or_truncated(self):
        for value in (None, True, [], "nan", "inf", -1, 0, "1.0001", "1e99999"):
            with self.subTest(value=value), self.assertRaises(ValueError):
                watts_to_mw(value)
        self.assertEqual(watts_to_mw("142.125"), 142125)
        self.assertEqual(watts_to_mw(230), 230000)

    def test_model_bounds_reject_mobile_and_overclock_values(self):
        for value in (4, 40, 60, 87, 231):
            self.assertEqual(self.apply(value)["reason"], "outside_documented_processor_limits")
        self.assertEqual(self.cpu.writes, [])

    def test_step_rejected(self):
        self.assertEqual(self.apply(142.5)["reason"], "invalid_processor_step")

    def test_no_increase(self):
        self.cpu.state = replace(self.cpu.state, active_ppt_mw=142000)
        self.assertEqual(self.apply(170)["reason"], "power_increase_not_allowed")

    def test_apply_and_restore_are_independently_verified(self):
        result = self.apply()
        self.assertTrue(result["ok"])
        self.assertEqual(result["observed_ppt_w"], 142)
        self.assertTrue(result["restore_required"])
        self.assertEqual(self.cpu.reads, 3)
        self.assertTrue(self.controller.restore(authorized=True)["ok"])
        self.assertEqual(self.cpu.writes, [142000, 230000])
        self.assertEqual(self.cpu.reads, 5)

    def test_noop(self):
        self.assertFalse(self.apply(230)["changed"])
        self.assertEqual(self.cpu.writes, [])

    def test_double_apply_requires_restore(self):
        self.apply()
        self.assertEqual(self.apply(120)["reason"], "restore_previous_transaction_first")
        self.assertEqual(len(self.cpu.writes), 1)

    def test_missing_limits_or_stale_snapshot_never_write(self):
        for change in ({"limits_source": ""}, {"observed_at": 7},
                       {"observed_at": 11}, {"maximum_ppt_mw": 0},
                       {"active_ppt_mw": float("nan")}, {"minimum_ppt_mw": True}):
            with self.subTest(change=change):
                cpu = FakeProcessor()
                cpu.state = replace(cpu.state, **change)
                result = PptController(cpu, clock=lambda: 10).apply(142, authorized=True)
                self.assertFalse(result["ok"])
                self.assertEqual(cpu.writes, [])

    def test_concurrent_change_before_write(self):
        def change(cpu):
            if cpu.reads == 2:
                cpu.state = replace(cpu.state, active_ppt_mw=170000)
        self.cpu.on_read = change
        self.assertEqual(self.apply()["reason"], "concurrent_change")
        self.assertEqual(self.cpu.writes, [])

    def test_success_code_without_readback_is_not_success(self):
        self.cpu.on_write = lambda *_: True
        self.assertEqual(self.apply()["reason"], "apply_not_confirmed")
        self.assertEqual(self.cpu.writes, [142000])

    def test_rejection_after_change_rolls_back_owned_target(self):
        def reject_first(cpu, value):
            cpu.state = replace(cpu.state, active_ppt_mw=value)
            return len(cpu.writes) > 1
        self.cpu.on_write = reject_first
        result = self.apply()
        self.assertFalse(result["ok"])
        self.assertTrue(result["rollback"]["ok"])
        self.assertEqual(self.cpu.writes, [142000, 230000])

    def test_unknown_readback_never_blindly_rolls_back(self):
        def other(cpu, value):
            cpu.state = replace(cpu.state, active_ppt_mw=150000)
            return True
        self.cpu.on_write = other
        result = self.apply()
        self.assertTrue(result["manual_recovery_required"])
        self.assertFalse(self.controller.restore(authorized=True)["ok"])
        self.assertEqual(self.cpu.writes, [142000])

    def test_read_failure_after_write_is_quarantined(self):
        def fail(cpu):
            if cpu.reads > 2:
                raise OSError("lost readback")
        self.cpu.on_read = fail
        self.assertTrue(self.apply()["manual_recovery_required"])
        self.assertEqual(self.apply()["reason"], "manual_recovery_required")
        self.assertEqual(self.cpu.writes, [142000])

    def test_setter_exception_is_not_retried(self):
        def fail(*_):
            raise OSError("driver response lost")
        self.cpu.on_write = fail
        self.assertEqual(self.apply()["reason"], "apply_state_unknown")
        self.assertEqual(self.cpu.writes, [142000])

    def test_external_change_is_preserved_on_restore(self):
        self.apply()
        self.cpu.state = replace(self.cpu.state, active_ppt_mw=170000)
        self.assertEqual(self.controller.restore(authorized=True)["reason"],
                         "external_change_restore_skipped")
        self.assertEqual(self.cpu.writes, [142000])

    def test_firmware_change_prevents_restore(self):
        self.apply()
        self.cpu.state = replace(self.cpu.state, firmware_id="different")
        self.assertFalse(self.controller.restore(authorized=True)["ok"])
        self.assertEqual(self.cpu.writes, [142000])

    def test_failed_restore_remains_uncertain(self):
        self.apply()
        self.cpu.on_write = lambda *_: False
        result = self.controller.restore(authorized=True)
        self.assertEqual(result["reason"], "restore_not_confirmed")
        self.assertTrue(result["manual_recovery_required"])


class CpuRpcTests(unittest.TestCase):
    def test_retired_rpcs_never_import_or_access_hardware_even_when_authorized(self):
        path = Path(__file__).resolve().parents[1] / "quick_settings/main.py"
        tree = ast.parse(path.read_text(encoding="utf-8"))
        names = {"_tdp_probe", "_get_tdp_status_sync", "_set_tdp_sync",
                 "_set_cpu_ppt_sync", "_restore_cpu_ppt_sync"}
        selected = [n for n in tree.body if isinstance(n, ast.FunctionDef) and n.name in names]
        self.assertEqual(len(selected), len(names))
        scope = {}
        exec(compile(ast.Module(body=selected, type_ignores=[]), str(path), "exec"), scope)
        for name, args in (("_get_tdp_status_sync", ()), ("_set_tdp_sync", (40,40,40)),
                           ("_set_cpu_ppt_sync", (142,True)), ("_restore_cpu_ppt_sync", (True,))):
            with self.subTest(name=name):
                result=scope[name](*args)
                self.assertEqual(result["reason"], "feature_removed")
                self.assertFalse(result["ok"])
                self.assertFalse(result["hardware_accessed"])
                self.assertFalse(result["supported_write"])


if __name__ == "__main__":
    unittest.main()

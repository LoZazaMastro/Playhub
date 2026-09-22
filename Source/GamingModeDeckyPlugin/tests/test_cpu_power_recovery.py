"""Readback obbligatorio, clamp di famiglia e journal di ripristino."""

from contextlib import contextmanager
from dataclasses import replace
import json
import os
import tempfile
import unittest

from cpu_power.recovery import PptRecoveryJournal, RecoveryJournalError, validate
from cpu_power.transaction import PptController, Snapshot


RAPHAEL = {"codename": "raphael_or_dragon_range", "verified": True,
           "minimum_mw": 35000, "maximum_mw": 170000, "step_mw": 1000}


class FakeProcessor:
    """Limiti sintetici, NON un profilo hardware verificato."""

    def __init__(self, device="fixture-cpu"):
        self.state = Snapshot(device, "fixture-fw", 160000, 88000, 170000,
                              1000, "fixture-limits", "fixture-independent-readback", 10)
        self.writes = []
        self.on_write = None

    @contextmanager
    def exclusive(self):
        yield

    def snapshot(self):
        return self.state

    def set_ppt_mw(self, value):
        self.writes.append(value)
        if self.on_write:
            return self.on_write(self, value)
        self.state = replace(self.state, active_ppt_mw=value)
        return True


class JournalFileTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.path = os.path.join(self.directory.name, "nested", "cpu-ppt-recovery.json")
        self.journal = PptRecoveryJournal(self.path)

    def record(self, **overrides):
        return {"schema_version": 1, "state": "applied", "token": "abc",
                "device_id": "fixture-cpu", "firmware_id": "fixture-fw",
                "baseline_ppt_mw": 160000, "target_ppt_mw": 120000, **overrides}

    def test_save_is_atomic_and_leaves_no_temporary(self):
        self.journal.save(self.record())
        self.assertTrue(os.path.isfile(self.path))
        self.assertFalse(os.path.exists(self.path + ".tmp"))
        with open(self.path, encoding="utf-8") as handle:
            self.assertEqual(json.load(handle)["baseline_ppt_mw"], 160000)

    def test_missing_file_is_not_an_error(self):
        self.assertIsNone(self.journal.load())

    def test_broken_record_is_reported_not_ignored(self):
        os.makedirs(os.path.dirname(self.path), exist_ok=True)
        with open(self.path, "w", encoding="utf-8") as handle:
            handle.write("{ not json")
        with self.assertRaises(RecoveryJournalError):
            self.journal.load()

    def test_validation_refuses_nonsense(self):
        for broken in (self.record(schema_version=99),
                       self.record(state="whatever"),
                       self.record(baseline_ppt_mw=0),
                       self.record(target_ppt_mw=-1),
                       self.record(device_id=" "),
                       self.record(target_ppt_mw=200000)):
            with self.subTest(broken["state"]), self.assertRaises(RecoveryJournalError):
                validate(broken)

    def test_clear_is_idempotent(self):
        self.journal.save(self.record())
        self.journal.clear()
        self.journal.clear()
        self.assertFalse(os.path.exists(self.path))


class ReadbackTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.journal = PptRecoveryJournal(os.path.join(self.directory.name, "cpu-ppt-recovery.json"))
        self.cpu = FakeProcessor()
        self.controller = PptController(self.cpu, clock=lambda: 10,
                                        journal=self.journal, limits=RAPHAEL)

    def test_confirmed_apply_writes_the_baseline_first(self):
        result = self.controller.apply(120, authorized=True)
        self.assertTrue(result["ok"])
        self.assertEqual(self.cpu.writes, [120000])
        record = self.journal.load()
        self.assertEqual(record["state"], "applied")
        self.assertEqual(record["baseline_ppt_mw"], 160000)
        self.assertEqual(record["target_ppt_mw"], 120000)

    def test_write_accepted_but_readback_unchanged_is_a_failure(self):
        # Il setter dice di si', il getter indipendente dice di no: FALLITO.
        self.cpu.on_write = lambda cpu, value: True
        result = self.controller.apply(120, authorized=True)
        self.assertFalse(result["ok"])
        self.assertEqual(result["reason"], "apply_not_confirmed")
        self.assertFalse(result["restore_required"])
        self.assertIsNone(self.journal.load())

    def test_readback_of_a_third_value_requires_manual_recovery(self):
        def wrong(cpu, value):
            cpu.state = replace(cpu.state, active_ppt_mw=99000)
            return True
        self.cpu.on_write = wrong
        result = self.controller.apply(120, authorized=True)
        self.assertFalse(result["ok"])
        self.assertEqual(result["reason"], "unexpected_readback_manual_recovery")
        self.assertTrue(result["manual_recovery_required"])

    def test_unwritable_journal_stops_the_write(self):
        blocked = PptRecoveryJournal(os.path.join(self.directory.name, "cpu-ppt-recovery.json", "x.json"))
        controller = PptController(self.cpu, clock=lambda: 10, journal=blocked, limits=RAPHAEL)
        self.journal.save({"schema_version": 1, "state": "completed", "token": "t",
                           "device_id": "fixture-cpu", "firmware_id": "fixture-fw"})
        result = controller.apply(120, authorized=True)
        self.assertFalse(result["ok"])
        self.assertEqual(result["reason"], "recovery_journal_unwritable")
        self.assertEqual(self.cpu.writes, [])

    def test_restore_closes_the_journal_only_after_readback(self):
        self.controller.apply(120, authorized=True)
        result = self.controller.restore(authorized=True)
        self.assertTrue(result["ok"])
        self.assertEqual(result["observed_ppt_w"], 160)
        self.assertIsNone(self.journal.load())


class FamilyClampTests(unittest.TestCase):
    def setUp(self):
        self.cpu = FakeProcessor()

    def controller(self, limits):
        return PptController(self.cpu, clock=lambda: 10, limits=limits)

    def test_unverified_family_never_writes(self):
        result = self.controller({"verified": False, "reason": "cpu_family_backend_unverified"}
                                 ).apply(120, authorized=True)
        self.assertFalse(result["ok"])
        self.assertEqual(result["reason"], "cpu_family_backend_unverified")
        self.assertEqual(self.cpu.writes, [])

    def test_out_of_table_cpu_never_writes(self):
        result = self.controller({"verified": False, "reason": "cpu_not_allowlisted"}
                                 ).apply(120, authorized=True)
        self.assertEqual(result["reason"], "cpu_not_allowlisted")
        self.assertEqual(self.cpu.writes, [])

    def test_below_the_family_floor_is_refused(self):
        result = self.controller(RAPHAEL).apply(20, authorized=True)
        self.assertEqual(result["reason"], "outside_family_table_limits")
        self.assertEqual(self.cpu.writes, [])

    def test_inside_the_family_interval_is_allowed(self):
        self.assertTrue(self.controller(RAPHAEL).apply(120, authorized=True)["ok"])


class RecoverTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.journal = PptRecoveryJournal(os.path.join(self.directory.name, "cpu-ppt-recovery.json"))
        self.cpu = FakeProcessor()
        self.cpu.state = replace(self.cpu.state, active_ppt_mw=120000)
        self.journal.save({"schema_version": 1, "state": "applied", "token": "abc",
                           "device_id": "fixture-cpu", "firmware_id": "fixture-fw",
                           "baseline_ppt_mw": 160000, "target_ppt_mw": 120000})

    def controller(self, provider="default"):
        return PptController(self.cpu if provider == "default" else provider,
                             clock=lambda: 10, journal=self.journal, limits=RAPHAEL)

    def test_explicit_authorization_is_required(self):
        self.assertEqual(self.controller().recover()["reason"], "explicit_recovery_required")
        self.assertIsNotNone(self.journal.load())

    def test_baseline_is_reapplied_and_read_back(self):
        result = self.controller().recover(authorized=True)
        self.assertTrue(result["ok"])
        self.assertEqual(result["reason"], "recovered")
        self.assertEqual(self.cpu.writes, [160000])
        self.assertEqual(self.cpu.state.active_ppt_mw, 160000)
        self.assertIsNone(self.journal.load())

    def test_recovery_without_readback_is_a_failure_and_keeps_the_record(self):
        self.cpu.on_write = lambda cpu, value: True
        result = self.controller().recover(authorized=True)
        self.assertFalse(result["ok"])
        self.assertEqual(result["reason"], "recovery_not_confirmed")
        self.assertTrue(result["recovery_pending"])
        self.assertIsNotNone(self.journal.load())

    def test_without_a_verified_backend_nothing_is_touched(self):
        result = self.controller(None).recover(authorized=True)
        self.assertFalse(result["ok"])
        self.assertEqual(result["reason"], "verified_ppt_provider_missing")
        self.assertTrue(result["recovery_pending"])
        self.assertIsNotNone(self.journal.load())

    def test_another_processor_drops_the_record(self):
        other = FakeProcessor(device="other-cpu")
        other.state = replace(other.state, active_ppt_mw=120000)
        result = self.controller(other).recover(authorized=True)
        self.assertEqual(result["reason"], "recovery_hardware_changed")
        self.assertEqual(other.writes, [])
        self.assertIsNone(self.journal.load())

    def test_already_at_baseline_changes_nothing(self):
        self.cpu.state = replace(self.cpu.state, active_ppt_mw=160000)
        result = self.controller().recover(authorized=True)
        self.assertTrue(result["ok"])
        self.assertFalse(result["changed"])
        self.assertEqual(self.cpu.writes, [])
        self.assertIsNone(self.journal.load())

    def test_no_record_is_not_a_recovery(self):
        self.journal.clear()
        self.assertEqual(self.controller().recover(authorized=True)["reason"], "no_recovery_pending")

    def test_broken_record_is_reported(self):
        with open(self.journal.path, "w", encoding="utf-8") as handle:
            handle.write("{}")
        result = self.controller().recover(authorized=True)
        self.assertFalse(result["ok"])
        self.assertEqual(result["reason"], "recovery_schema_unknown")


if __name__ == "__main__":
    unittest.main()

import asyncio
import ast
import pathlib
import types
import unittest
from unittest.mock import Mock, patch

from quick_settings.processor_boost import ProcessorBoost


PLAN = "a4286308-c568-402b-8025-6ec4f5ee882a"
OTHER = "381b4222-f694-41f0-9685-ff5bb260df2e"


class Native:
    def __init__(self):
        self.plan = PLAN
        self.values = {(plan, source): 2 for plan in (PLAN, OTHER) for source in ("ac", "battery")}
        self.values[(PLAN, "battery")] = 3
        self.writes, self.applies = [], []
        self.supported = list(range(7))
        self.reject = self.ignore = self.fail_apply = self.switch_after_write = False

    def active(self):
        return self.plan

    def options(self):
        return self.supported

    def read(self, plan, source):
        return self.values[(plan, source)]

    def write(self, plan, source, mode):
        self.writes.append((plan, source, mode))
        if self.reject:
            return False
        if not self.ignore:
            self.values[(plan, source)] = mode
        if self.switch_after_write:
            self.plan = OTHER
        return True

    def apply(self, plan):
        self.applies.append(plan)
        return not self.fail_apply


class ProcessorBoostTests(unittest.TestCase):
    def setUp(self):
        self.native = Native()
        self.power = Mock(return_value={"source": "ac"})
        self.boost = ProcessorBoost(self.power, self.native)

    def change(self, mode, source="ac", plan=PLAN):
        return self.boost.set({"mode": mode, "source": source, "plan": plan})

    def test_read_is_capability_based_and_never_writes(self):
        self.assertEqual(self.boost.get(), {"available": True, "plan": PLAN, "source": "ac", "mode": 2, "modes": list(range(7))})
        self.assertEqual((self.native.writes, self.native.applies), ([], []))
        self.native.supported = [0]
        self.assertEqual(self.boost.get(), {"available": False})
        self.assertEqual(self.change(0)["code"], "unavailable")

    def test_plugin_startup_does_not_load_native_power_libraries(self):
        with patch("quick_settings.processor_boost.WindowsProcessorBoost", return_value=self.native) as load:
            boost = ProcessorBoost(self.power)
            load.assert_not_called()
            self.assertEqual(boost.get()["mode"], 2)
            load.assert_called_once()
            boost.get()
            load.assert_called_once()

    def test_ac_battery_independence_and_verified_apply(self):
        self.assertTrue(self.change(1)["ok"])
        self.assertEqual(self.native.values[(PLAN, "ac")], 1)
        self.assertEqual(self.native.values[(PLAN, "battery")], 3)
        self.power.return_value = {"source": "battery"}
        self.assertEqual(self.boost.get()["mode"], 3)
        self.assertTrue(self.change(0, "battery")["ok"])
        self.assertEqual(self.native.writes, [(PLAN, "ac", 1), (PLAN, "battery", 0)])
        self.assertEqual(self.native.applies, [PLAN, PLAN])

    def test_stale_plan_or_source_refuses_all_writes(self):
        self.assertEqual(self.change(0, plan=OTHER)["code"], "profile_changed")
        self.assertEqual(self.change(0, source="battery")["code"], "profile_changed")
        self.power.side_effect = [{"source": "ac"}, {"source": "battery"}, {"source": "battery"}]
        changed = self.change(0)
        self.assertEqual((changed["code"], changed["source"], changed["mode"]), ("profile_changed", "battery", 3))
        self.assertEqual((self.native.writes, self.native.applies), ([], []))

    def test_plan_changed_after_write_restores_old_plan_without_activating_it(self):
        self.native.switch_after_write = True
        changed = self.change(0)
        self.assertEqual((changed["ok"], changed["code"], changed["plan"]), (False, "profile_changed", OTHER))
        self.assertEqual(self.native.values[(PLAN, "ac")], 2)
        self.assertEqual(self.native.values[(OTHER, "ac")], 2)
        self.assertEqual(self.native.writes, [(PLAN, "ac", 0), (PLAN, "ac", 2)])
        self.assertEqual(self.native.applies, [])

    def test_plan_changed_between_readback_and_apply_is_not_reactivated(self):
        calls = 0

        def active():
            nonlocal calls
            calls += 1
            if calls == 5:
                self.native.plan = OTHER
            return self.native.plan

        self.native.active = active
        changed = self.change(0)
        self.assertEqual((changed["ok"], changed["code"], changed["plan"]), (False, "profile_changed", OTHER))
        self.assertEqual(self.native.values[(PLAN, "ac")], 2)
        self.assertEqual(self.native.applies, [])

    def test_external_selection_after_write_is_preserved_during_recovery(self):
        original_write = self.native.write

        def external_change(plan, source, mode):
            result = original_write(plan, source, mode)
            self.native.values[(plan, source)] = 3
            return result

        self.native.write = external_change
        changed = self.change(0)
        self.assertEqual((changed["ok"], changed["code"], changed["mode"]), (False, "profile_changed", 3))
        self.assertEqual(self.native.writes, [(PLAN, "ac", 0)])
        self.assertEqual(self.native.applies, [])

    def test_source_change_after_scoped_write_returns_new_source(self):
        self.power.side_effect = [{"source": "ac"}, {"source": "ac"}, {"source": "battery"}]
        changed = self.change(0)
        self.assertEqual((changed["ok"], changed["source"], changed["mode"]), (True, "battery", 3))
        self.assertEqual(self.native.writes, [(PLAN, "ac", 0)])

    def test_ignored_write_is_not_success_and_original_is_retained(self):
        self.native.ignore = True
        changed = self.change(0)
        self.assertEqual((changed["ok"], changed["code"], changed["mode"]), (False, "not_confirmed", 2))
        self.assertEqual(self.native.writes, [(PLAN, "ac", 0)])

    def test_rejected_write_does_not_apply_or_change_policy(self):
        self.native.reject = True
        changed = self.change(0)
        self.assertEqual((changed["ok"], changed["code"], changed["mode"]), (False, "write_failed", 2))
        self.assertEqual(self.native.applies, [])

    def test_failed_apply_rolls_back_and_reports_failed_restore(self):
        self.native.fail_apply = True
        changed = self.change(0)
        self.assertEqual((changed["ok"], changed["code"], changed["mode"]), (False, "restore_failed", 2))
        self.assertEqual(self.native.writes, [(PLAN, "ac", 0), (PLAN, "ac", 2)])
        self.assertEqual(self.native.applies, [PLAN, PLAN])

    def test_apply_exception_rolls_back_the_value(self):
        apply = Mock(side_effect=[OSError("apply failed"), True])
        self.native.apply = apply
        changed = self.change(0)
        self.assertFalse(changed["ok"])
        self.assertEqual(changed["mode"], 2)
        self.assertEqual(self.native.writes, [(PLAN, "ac", 0), (PLAN, "ac", 2)])

    def test_unchanged_or_invalid_request_never_writes(self):
        self.assertTrue(self.change(2)["ok"])
        for request in (None, [], {}, {"plan": PLAN, "source": [], "mode": 0},
                        {"plan": PLAN, "source": "ac", "mode": False},
                        {"plan": PLAN, "source": "ac", "mode": 7},
                        {"plan": PLAN, "source": "ac", "mode": "0"}):
            with self.subTest(request=request):
                self.assertEqual(self.boost.set(request)["code"], "invalid_request")
        self.native.supported = [0, 2]
        self.assertEqual(self.change(1)["code"], "invalid_request")
        self.assertEqual((self.native.writes, self.native.applies), ([], []))

    def test_unknown_source_or_missing_native_policy_is_hidden(self):
        self.power.return_value = {"source": None}
        self.assertEqual(self.boost.get(), {"available": False})
        self.power.return_value = {"source": "ac"}
        self.native.read = Mock(side_effect=OSError("missing policy"))
        self.assertEqual(self.boost.get(), {"available": False})
        self.assertEqual(self.change(0)["code"], "unavailable")

    def test_public_rpc_uses_background_thread_and_exact_request(self):
        path = pathlib.Path(__file__).resolve().parents[1] / "main.py"
        tree = ast.parse(path.read_text(encoding="utf-8"))
        plugin = next(node for node in tree.body if isinstance(node, ast.ClassDef) and node.name == "Plugin")
        plugin.bases = []
        plugin.body = [node for node in plugin.body if getattr(node, "name", "") in ("get_processor_boost", "set_processor_boost")]
        scope = {"asyncio": asyncio}
        exec(compile(ast.Module(body=[plugin], type_ignores=[]), str(path), "exec"), scope)
        public = scope["Plugin"]()
        public._processor_boost = self.boost
        self.assertEqual(asyncio.run(public.get_processor_boost())["mode"], 2)
        changed = asyncio.run(public.set_processor_boost({"plan": PLAN, "source": "ac", "mode": 0}))
        self.assertTrue(changed["ok"])
        self.assertEqual(changed["mode"], 0)


if __name__ == "__main__":
    unittest.main()

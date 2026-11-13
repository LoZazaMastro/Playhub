import copy
import importlib.util
import json
from pathlib import Path
import unittest
from unittest.mock import Mock


ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("onex_type2_fan_tests", ROOT / "quick_settings/onex_type2_fan.py")
fan = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fan)
BOARD = {"manufacturer": "ONE-NETBOOK", "product": "ONEXPLAYER X2Mini"}


class OneXFanTests(unittest.TestCase):
    def setUp(self):
        self.state = {"mode": 0, "pwm": 70}
        self.requests = []
        self.reply = None

        def transport(arguments, timeout):
            import base64
            request = json.loads(base64.b64decode(arguments[-1]))
            self.requests.append(request)
            if self.reply:
                return 0, json.dumps(self.reply), ""
            if request["action"] == "manual":
                self.state = {"mode": 1, "pwm": (request["percent"] * 184 + 50) // 100}
            elif request["action"] == "auto":
                self.state["mode"] = 0
            elif request["action"] == "restore":
                self.state = copy.deepcopy(request["baseline"])
            value = {"ok": True, "available": True, "code": "", "token": "a" * 64,
                     "state": self.state, "changed": request["action"] != "snapshot", "recoveryRequired": False,
                     "telemetry": {"fanRpm": 3291, "cpuTemperatureC": 54}}
            return 0, json.dumps(value), ""

        self.transport = Mock(side_effect=transport)
        self.control = fan.OneXTypeTwoFan(lambda: BOARD, self.transport, lambda: "fake-powershell", windows=True)
        self.addCleanup(fan.OneXTypeTwoFan._owners.clear)

    def test_constructing_and_unsupported_board_never_call_hardware(self):
        self.transport.assert_not_called()
        unsupported = fan.OneXTypeTwoFan(lambda: {**BOARD, "product": "ONEXPLAYER X2Mini PRO"},
                                         self.transport, lambda: "fake-powershell", windows=True)
        self.assertFalse(unsupported.snapshot()["available"])
        self.transport.assert_not_called()

    def test_manual_changes_restore_the_original_automatic_baseline(self):
        first = self.control.snapshot()
        self.assertTrue(self.control.set_manual(50, first["token"], True)["ok"])
        second = self.control.snapshot()
        self.assertTrue(self.control.set_manual(70, second["token"], True)["ok"])
        current = self.control.snapshot()
        self.assertTrue(current["restoreAvailable"])
        self.assertTrue(self.control.restore(current["token"], True)["ok"])
        self.assertEqual(self.state, {"mode": 0, "pwm": 70})
        self.assertFalse(self.control.snapshot()["restoreAvailable"])

    def test_unknown_auto_write_keeps_ownership_and_blocks_a_second_writer(self):
        first = self.control.snapshot()
        self.reply = {"ok": False, "available": False, "code": "operation_unknown",
                      "changed": False, "recoveryRequired": True}
        self.assertFalse(self.control.set_auto(first["token"], True)["ok"])
        self.reply = None
        other = fan.OneXTypeTwoFan(lambda: BOARD, self.transport, lambda: "fake-powershell", windows=True)
        other.snapshot()
        count = len(self.requests)
        self.assertEqual(other.set_manual(70, first["token"], True)["code"], "owned_by_another_session")
        self.assertEqual(len(self.requests), count)

    def test_foreign_manual_change_relinquishes_ownership_without_overwriting_it(self):
        self.control.snapshot()
        self.control.set_manual(50, "a" * 64, True)
        self.state = {"mode": 1, "pwm": 140}
        current = self.control.snapshot()
        self.assertFalse(current["restoreAvailable"])
        count = len(self.requests)
        self.assertEqual(self.control.restore("a" * 64, True)["code"], "no_owned_change")
        self.assertEqual(len(self.requests), count)
        self.assertEqual(self.state["pwm"], 140)
        self.assertTrue(self.control.set_manual(60, "a" * 64, True)["ok"])
        self.control.snapshot()
        self.assertTrue(self.control.restore("a" * 64, True)["ok"])
        self.assertEqual(self.state, {"mode": 1, "pwm": 140})

    def test_telemetry_is_independent_of_the_percent_setting(self):
        self.control.snapshot()
        self.control.set_manual(70, "a" * 64, True)
        current = self.control.snapshot()
        self.assertEqual(current["telemetry"], {"fanRpm": 3291, "cpuTemperatureC": 54})
        self.assertEqual(current["state"]["pwm"], 129)

    def test_only_the_documented_response_format_is_decoded(self):
        self.assertEqual(fan.parse_firmware_response("0x00,0x81,0x00,0x00,0x00,0x00,0x00,0x00"), 129)
        for value in ("0,129,0,0,0,0,0,0", "0x00,0x81", "0x01,0x81,0x00,0x00,0x00,0x00,0x00,0x00",
                      "0x00,0x81,0x01,0x00,0x00,0x00,0x00,0x00"):
            with self.assertRaises(ValueError):
                fan.parse_firmware_response(value)

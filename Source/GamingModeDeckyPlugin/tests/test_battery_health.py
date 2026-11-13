import json
import unittest
from unittest.mock import Mock, patch

from quick_settings.battery_health import BatteryHealth, battery_health_percent


def readings(design=50_000, full=45_000):
    return {
        "design": [{"InstanceName": "Battery_A", "Active": True, "Tag": 21, "DesignedCapacity": design}],
        "full": [{"InstanceName": "battery_a", "Active": True, "Tag": 21, "FullChargedCapacity": full}],
    }


class BatteryHealthTests(unittest.TestCase):
    def test_valid_pack_reports_percentage_and_caps_above_design(self):
        self.assertEqual(battery_health_percent(readings()), 90)
        self.assertEqual(battery_health_percent(readings(full=51_000)), 100)

    def test_missing_ambiguous_and_replaced_pack_is_hidden(self):
        self.assertIsNone(battery_health_percent(None))
        report = readings()
        report["design"].append({"InstanceName": "Battery_B", "Active": True, "Tag": 22, "DesignedCapacity": 70_000})
        self.assertIsNone(battery_health_percent(report))
        report["design"][1]["Active"] = False
        self.assertEqual(battery_health_percent(report), 90)
        report["full"].append({"InstanceName": "Battery_B", "Active": True, "Tag": 22, "FullChargedCapacity": 60_000})
        self.assertIsNone(battery_health_percent(report))
        report["full"].pop()
        report["full"][0]["InstanceName"] = "Battery_B"
        self.assertIsNone(battery_health_percent(report))
        report["full"][0]["InstanceName"] = "Battery_A"
        report["full"][0]["Tag"] = 22
        self.assertIsNone(battery_health_percent(report))
        report["full"].clear()
        self.assertIsNone(battery_health_percent(report))

    def test_unsound_values_are_hidden(self):
        for value in (None, -1, 0, 0xffffffff, 0x100000000, True, 50_000.0, "50000"):
            with self.subTest(value=value):
                self.assertIsNone(battery_health_percent(readings(design=value)))
                self.assertIsNone(battery_health_percent(readings(full=value)))
        self.assertIsNone(battery_health_percent(readings(full=65_000)))

    def test_no_battery_skips_wmi_and_clears_cached_estimate(self):
        read_power = Mock(return_value={"battery": False})
        run = Mock(return_value=(0, json.dumps(readings()), ""))
        service = BatteryHealth(read_power, run, lambda: "powershell.exe")
        self.assertEqual(service.get(), {"percent": None})
        run.assert_not_called()
        read_power.return_value = {"battery": True}
        self.assertEqual(service.get(), {"percent": 90})
        read_power.return_value = {"battery": False}
        self.assertEqual(service.get(), {"percent": None})
        read_power.return_value = {"battery": True}
        run.return_value = (0, json.dumps(readings(full=40_000)), "")
        self.assertEqual(service.get(), {"percent": 80})
        self.assertEqual(run.call_count, 2)

    def test_removal_while_reading_discards_result(self):
        read_power = Mock(side_effect=[{"battery": True}, {"battery": False}])
        run = Mock(return_value=(0, json.dumps(readings()), ""))
        self.assertEqual(BatteryHealth(read_power, run, lambda: "powershell.exe").get(), {"percent": None})

    def test_one_bounded_read_is_cached_and_failure_is_unavailable(self):
        run = Mock(return_value=(0, "\ufeff" + json.dumps(readings()), ""))
        service = BatteryHealth(lambda: {"battery": True}, run, lambda: "powershell.exe")
        with patch("quick_settings.battery_health.time.monotonic", return_value=100) as clock:
            self.assertEqual(service.get(), {"percent": 90})
            clock.return_value = 200
            self.assertEqual(service.get(), {"percent": 90})
            run.assert_called_once()
            args, = run.call_args.args
            self.assertEqual(args[0:4], ["powershell.exe", "-NoProfile", "-NonInteractive", "-Command"])
            self.assertEqual(run.call_args.kwargs["timeout"], 4)
            self.assertIn("BatteryStaticData", args[4])
            self.assertIn("BatteryFullChargedCapacity", args[4])
            clock.return_value = 400
            run.return_value = (1, "", "unavailable")
            self.assertEqual(service.get(), {"percent": None})

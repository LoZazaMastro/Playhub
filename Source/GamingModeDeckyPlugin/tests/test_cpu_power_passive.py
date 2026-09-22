import unittest

from cpu_power.capability import PassiveAdapter


def inventory(registered=True, identifier="AMD64 Family 25 Model 97 Stepping 2"):
    return {"platform": "windows", "cpu": {
        "vendor": "AuthenticAMD", "identifier": identifier},
        "driver": {"registered": registered, "service_type": 1}}


class PassiveTests(unittest.TestCase):
    def test_raphael_is_candidate_not_write_permission(self):
        status = PassiveAdapter(inventory).get_status()
        self.assertEqual(status["cpu_candidate"], "raphael_or_dragon_range")
        self.assertFalse(status["supported_write"])
        self.assertFalse(status["supported_read"])
        self.assertFalse(status["hardware_accessed"])
        self.assertIsNone(status["observed_ppt_w"])

    def test_missing_driver(self):
        self.assertEqual(PassiveAdapter(lambda: inventory(False)).get_status()["reason"],
                         "pawnio_service_missing")

    def test_unknown_cpu(self):
        status = PassiveAdapter(lambda: inventory(identifier="AMD64 Family 25 Model 80 Stepping 0")).get_status()
        self.assertEqual(status["reason"], "cpu_not_allowlisted")

    def test_malformed_identity(self):
        self.assertEqual(PassiveAdapter(lambda: inventory(identifier="unknown")).get_status()["reason"],
                         "inventory_failed")

    def test_closed(self):
        adapter = PassiveAdapter(inventory)
        adapter.close()
        with self.assertRaises(RuntimeError):
            adapter.get_status()

    def test_7900x_nominal_tdp_is_not_a_ppt_measurement(self):
        data = inventory()
        data["cpu"]["name"] = "AMD Ryzen 9 7900X 12-Core Processor"
        status = PassiveAdapter(lambda: data).get_status()
        self.assertEqual(status["rated_tdp_w"], 170)
        self.assertIsNone(status["observed_ppt_w"])
        self.assertFalse(status["supported_write"])

    def test_7900x3d_does_not_inherit_7900x_rating(self):
        data = inventory()
        data["cpu"]["name"] = "AMD Ryzen 9 7900X3D 12-Core Processor"
        self.assertNotIn("rated_tdp_w", PassiveAdapter(lambda: data).get_status())


if __name__ == "__main__":
    unittest.main()

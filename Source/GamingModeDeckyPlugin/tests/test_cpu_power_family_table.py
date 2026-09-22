"""Tabella famiglie: dentro, fuori, e non verificata. Niente si accende da solo."""

import unittest

from cpu_power.capability import (FAMILY_TABLE, PassiveAdapter, current_family_limits,
                                  family_limits_mw, lookup_family)


def inventory(identifier="AMD64 Family 25 Model 97 Stepping 2",
              vendor="AuthenticAMD", registered=True):
    return {"platform": "windows",
            "cpu": {"vendor": vendor, "identifier": identifier},
            "driver": {"registered": registered, "service_type": 1}}


class FamilyTableTests(unittest.TestCase):
    def test_every_row_declares_a_usable_interval(self):
        for entry in FAMILY_TABLE:
            with self.subTest(entry.codename):
                self.assertLess(0, entry.minimum_w)
                self.assertLess(entry.minimum_w, entry.maximum_w)
                self.assertLess(0, entry.step_w)
                self.assertLessEqual(entry.models[0], entry.models[1])
                self.assertTrue(entry.note.strip())

    def test_only_the_verified_family_is_marked_verified(self):
        verified = [entry.codename for entry in FAMILY_TABLE if entry.verified]
        self.assertEqual(verified, ["raphael_or_dragon_range"])

    def test_lookup_matches_the_whole_model_range(self):
        for model in (0x60, 0x61, 0x6F):
            self.assertEqual(lookup_family("AuthenticAMD", 0x19, model).codename,
                             "raphael_or_dragon_range")

    def test_lookup_outside_the_table_is_none(self):
        # Zen 3 APU mobile (Cezanne): deliberatamente non in tabella.
        self.assertIsNone(lookup_family("AuthenticAMD", 0x19, 0x50))
        self.assertIsNone(lookup_family("SomethingElse", 0x19, 0x61))

    def test_limits_are_milliwatts(self):
        limits = family_limits_mw("AuthenticAMD", 0x19, 0x61)
        self.assertEqual(limits["minimum_mw"], 35000)
        self.assertEqual(limits["maximum_mw"], 170000)
        self.assertTrue(limits["verified"])

    def test_status_reports_the_family_row(self):
        status = PassiveAdapter(inventory).get_status()
        self.assertEqual(status["cpu_candidate"], "raphael_or_dragon_range")
        self.assertEqual(status["family_entry"]["backend"], "amd_smu_mailbox")
        self.assertEqual(status["range"], {"min": 35, "max": 170, "step": 1, "unit": "W"})
        # In tabella e verificata, ma il driver non e' ancora provato: niente scrittura.
        self.assertFalse(status["supported_write"])

    def test_unverified_family_is_refused_with_its_own_reason(self):
        # Zen 5 desktop: in tabella, backend mai verificato.
        status = PassiveAdapter(lambda: inventory("AMD64 Family 26 Model 68 Stepping 0")).get_status()
        self.assertEqual(status["cpu_candidate"], "granite_ridge")
        self.assertEqual(status["reason"], "cpu_family_backend_unverified")
        self.assertFalse(status["supported_write"])

    def test_outside_the_table_says_which_cpu_it_saw(self):
        status = PassiveAdapter(lambda: inventory("AMD64 Family 25 Model 80 Stepping 0")).get_status()
        self.assertEqual(status["reason"], "cpu_not_allowlisted")
        self.assertEqual(status["cpu_family"], {"vendor": "AuthenticAMD",
                                                "family": 0x19, "model": 0x50})

    def test_intel_is_in_the_table_but_stays_off(self):
        status = PassiveAdapter(
            lambda: inventory("Intel64 Family 6 Model 183 Stepping 1", "GenuineIntel")).get_status()
        self.assertEqual(status["cpu_candidate"], "intel_rapl")
        self.assertEqual(status["reason"], "cpu_family_backend_unverified")

    def test_current_family_limits_never_raises(self):
        self.assertEqual(current_family_limits(lambda: {"platform": "linux"}),
                         {"verified": False, "reason": "platform_unsupported"})
        self.assertEqual(current_family_limits(lambda: inventory("nonsense"))["reason"],
                         "inventory_failed")
        self.assertEqual(
            current_family_limits(lambda: inventory("AMD64 Family 25 Model 80 Stepping 0"))["reason"],
            "cpu_not_allowlisted")
        self.assertTrue(current_family_limits(inventory)["verified"])


if __name__ == "__main__":
    unittest.main()

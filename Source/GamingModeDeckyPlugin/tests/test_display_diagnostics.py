"""La diagnostica schermo deve essere di SOLA LETTURA.

Serve a sostituire le ipotesi con i fatti dopo la perdita di segnale, quindi non
puo' toccare l'hardware, e un singolo errore non deve farla fallire tutta.
"""
import os
import unittest

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
with open(os.path.join(ROOT, "main.py"), encoding="utf-8") as handle:
    SOURCE = handle.read()
BODY = SOURCE[SOURCE.index("def _dump_display_diagnostics_sync"):SOURCE.index("async def _main")]


class DisplayDiagnostics(unittest.TestCase):
    def test_it_never_writes_to_the_hardware(self):
        for forbidden in ("_write_hdr_target", "_write_color_state", "_set_display_mode_sync",
                          "_begin_display_change", "_revert_display", "ChangeDisplaySettings"):
            self.assertNotIn(forbidden, BODY, forbidden)
        self.assertIn('"hardware_written": False', BODY)

    def test_every_probe_survives_its_own_failure(self):
        # Una sonda che esplode non deve portarsi via il resto del referto.
        self.assertGreaterEqual(BODY.count("except Exception as error:"), 3)
        self.assertIn("type(error).__name__", BODY)

    def test_it_records_what_a_write_would_have_touched(self):
        # Se i bersagli sono piu' del pannello acceso, e' li' che si perde il segnale.
        self.assertIn("_hdr_target_states", BODY)
        self.assertIn('report["hdr_write_enabled"]', BODY)
        self.assertIn('report["recovery_note"]', BODY)
        self.assertIn('report["recovery_error"]', BODY)

    def test_the_report_lands_in_a_named_file(self):
        self.assertIn('"display-diagnostics.json"', BODY)
        self.assertIn("except OSError as error:", BODY)
        self.assertIn('return {"ok": True, "path": path', BODY)


if __name__ == "__main__":
    unittest.main()

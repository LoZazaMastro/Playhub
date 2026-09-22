"""Dopo uno spegnimento forzato il journal resta a meta': non deve bloccare tutto.

Sintomo reale: dal cambio HDR in poi risoluzione, frequenza e HDR non cambiavano
piu' e il dialog con il timer non compariva mai, perche' ogni richiesta usciva
subito con "recovery_required" o "busy".
"""
import os
import re
import unittest

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
with open(os.path.join(ROOT, "main.py"), encoding="utf-8") as handle:
    SOURCE = handle.read()


class DisplayRecoveryWedge(unittest.TestCase):
    def test_a_corrupt_journal_is_set_aside_instead_of_blocking_forever(self):
        recover = SOURCE[SOURCE.index("def _recover_display_sync"):SOURCE.index("def _quarantine_display_journal")]
        # Il contenuto incoerente non alza piu' la barriera permanente...
        self.assertIn("except (ValueError, TypeError) as error:", recover)
        self.assertIn("self._display_recovery_note = str(error)", recover)
        self.assertIn("self._quarantine_display_journal()", recover)
        blocking = recover[recover.index("except (ValueError, TypeError)"):recover.index("except OSError")]
        self.assertNotIn("_display_recovery_error", blocking,
                         "un journal illeggibile non deve piu' bloccare ogni cambio schermo")
        # ...mentre un errore di I/O vero resta segnalato.
        self.assertIn("except OSError as error:", recover)
        self.assertIn("self._display_recovery_error = str(error)", recover)

    def test_the_journal_is_removed_when_it_cannot_be_renamed(self):
        body = SOURCE[SOURCE.index("def _quarantine_display_journal"):SOURCE.index("def _revert_display")]
        self.assertIn("os.replace", body)
        self.assertIn("os.remove", body)
        self.assertIn("except OSError", body)

    def test_an_exhausted_recovery_releases_the_panel(self):
        begin = SOURCE[SOURCE.index("def _begin_display_change"):SOURCE.index("token = uuid.uuid4().hex")]
        self.assertIn('pending.get("retries", 0) > _DISPLAY_RECOVERY_ATTEMPTS', begin)
        self.assertIn("self._display_pending = None", begin)
        # Il "busy" resta solo mentre un ripristino sta davvero lavorando.
        self.assertIn('return {"ok": False, "busy": True}', begin)
        self.assertLess(begin.index("_DISPLAY_RECOVERY_ATTEMPTS"), begin.index('"busy": True'))

    def test_the_reason_survives_for_diagnostics(self):
        self.assertIn("self._display_recovery_note = None", SOURCE)
        self.assertIn('"display_recovery_exhausted"', SOURCE)


if __name__ == "__main__":
    unittest.main()

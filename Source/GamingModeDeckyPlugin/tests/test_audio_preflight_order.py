"""Windows sposta piu' ruoli audio con una sola SetDefaultEndpoint.

Controllare l'invariante di preflight DENTRO il ciclo di scrittura la faceva
scattare sull'effetto delle nostre stesse scritture: e' il messaggio
"Audio role changed during preflight" che compariva cambiando ingresso o uscita.
"""
import os
import unittest

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
with open(os.path.join(ROOT, "quick_settings", "main.py"), encoding="utf-8") as handle:
    SOURCE = handle.read()
APPLY = SOURCE[SOURCE.index("public static DefaultChangeResult Apply("):SOURCE.index("Audio roles changed before confirmation.")]


class AudioPreflightOrder(unittest.TestCase):
    def test_preflight_closes_before_the_first_write(self):
        preflight = APPLY.index("Audio role changed during preflight.")
        first_write = APPLY.index("Marshal.ThrowExceptionForHR(write(")
        self.assertLess(preflight, first_write, "il preflight deve chiudersi prima di scrivere")
        # e non deve esserci nessun controllo di preflight dopo la prima scrittura
        self.assertNotIn("Audio role changed during preflight.", APPLY[first_write:])

    def test_a_role_already_moved_by_our_own_write_is_a_success(self):
        # Il salto si decide su una rilettura, non sullo stato catturato all'inizio.
        self.assertIn("if (Same(read((ERole)i), id)) continue;", APPLY)
        self.assertNotIn("if (Same(result.Previous[i], id)) continue;", APPLY)

    def test_the_write_is_still_confirmed_by_readback(self):
        self.assertIn('throw new InvalidOperationException("Audio role write was not confirmed.");', APPLY)


if __name__ == "__main__":
    unittest.main()

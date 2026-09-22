"""Una chiave doppia in LocalizationService fa crashare Playhub all'avvio.

Le stringhe stanno in un unico `Dictionary<string, string[]>` inizializzato con la
sintassi `["chiave"] = ...`. Due voci con la stessa chiave non sono un warning del
compilatore: sono un ArgumentException al primo accesso alla classe, cioe' una
finestra che non si apre. E' successo aggiungendo "Lingua", che esisteva gia'.

Questo test controlla anche che ogni voce abbia una traduzione per ognuna delle
undici lingue oltre all'italiano.
"""
import os
import re
import unittest
from collections import Counter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SERVICE = os.path.join(os.path.dirname(ROOT), 'Playhub', 'Services', 'LocalizationService.cs')
LANGUAGES = 11  # en, es, fr, de, pt, uk, zh, ja, ko, hi, ru

ENTRY = re.compile(r'^\s*\["((?:[^"\\]|\\.)*)"\]\s*=\s*(new\[\]\s*\{)?', re.M)


def source():
    with open(SERVICE, encoding='utf-8-sig') as handle:
        return handle.read()


class LocalizationKeyTests(unittest.TestCase):
    def setUp(self):
        self.assertTrue(os.path.isfile(SERVICE), SERVICE)
        self.text = source()

    def test_no_key_is_declared_twice(self):
        keys = [match.group(1) for match in ENTRY.finditer(self.text)]
        self.assertGreater(len(keys), 100, 'the parser found almost nothing — check the format')
        duplicates = sorted(key for key, count in Counter(keys).items() if count > 1)
        self.assertEqual(duplicates, [], 'duplicate keys throw at static init and the window never opens')

    def test_every_single_line_entry_carries_all_eleven_translations(self):
        """Le voci su una riga sola sono la quasi totalita'; quelle su piu' righe
        usano V(...) e non si contano in modo affidabile con un'espressione regolare."""
        short = []
        for line in self.text.split('\n'):
            match = re.match(r'\s*\["((?:[^"\\]|\\.)*)"\]\s*=\s*new\[\]\s*\{(.*)\},\s*$', line)
            if not match:
                continue
            key, body = match.group(1), match.group(2)
            count = len(re.findall(r'"(?:[^"\\]|\\.)*"', body))
            if count != LANGUAGES:
                short.append(f'{key[:48]} -> {count}')
        self.assertEqual(short, [], f'each entry needs exactly {LANGUAGES} translations')


if __name__ == '__main__':
    unittest.main()

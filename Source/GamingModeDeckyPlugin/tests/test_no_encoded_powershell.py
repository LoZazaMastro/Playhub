"""PowerShell in base64 non deve tornare nel codice che spediamo.

`powershell -EncodedCommand <base64>` e' uno degli inneschi piu' forti per gli
euristici antivirus: su una macchina utente Defender lo segnala come
Trojan:Win32/Commando.A!ml, e la segnalazione riappare a ogni riavvio. La app lo
aveva gia' tolto in due punti (DeckyPluginService.cs, UwpXboxService.cs) ma era
rientrato in cpu_power/pawnio.py. Questo test tiene chiusa la porta.

Il modo giusto di eseguire uno script resta: file .ps1 temporaneo + -File.
"""
import os
import re
import unittest

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SOURCE = os.path.dirname(ROOT)
# Alberi che finiscono sulla macchina dell'utente.
SHIPPED = ('Playhub', 'GamingModeDeckyPlugin', 'GamingModeAgent', 'PlayhubSetup')
CODE = ('.cs', '.py', '.ts', '.tsx', '.ps1', '.bat', '.cmd', '.iss')
SKIP = ('node_modules', '.git', 'bin', 'obj', 'dist', '__pycache__', 'tests', 'OLD')
# Una riga di commento che *nomina* la pratica per spiegare perche' e' vietata.
COMMENT = re.compile(r'^\s*(//|#|<!--|\*)')


def shipped_files():
    for tree in SHIPPED:
        base = os.path.join(SOURCE, tree)
        if not os.path.isdir(base):
            continue
        for folder, directories, names in os.walk(base):
            directories[:] = [d for d in directories if d not in SKIP]
            for name in names:
                if name.endswith(CODE):
                    yield os.path.join(folder, name)


class NoEncodedPowerShellTests(unittest.TestCase):
    def test_no_shipped_file_launches_powershell_with_encodedcommand(self):
        offenders = []
        for path in shipped_files():
            try:
                with open(path, encoding='utf-8-sig', errors='replace') as handle:
                    for number, line in enumerate(handle, 1):
                        if 'encodedcommand' in line.lower() and not COMMENT.match(line):
                            offenders.append(f'{os.path.relpath(path, SOURCE)}:{number}')
            except OSError:
                continue
        self.assertEqual(offenders, [], 'use a temporary .ps1 file with -File instead')

    def test_pawnio_runs_its_script_from_a_file(self):
        with open(os.path.join(ROOT, 'cpu_power', 'pawnio.py'), encoding='utf-8') as handle:
            source = handle.read()
        self.assertIn('"-File", script_path', source)
        self.assertIn('tempfile.mkstemp', source)
        # Lo script temporaneo va rimosso anche quando PowerShell fallisce.
        self.assertIn('finally:', source.split('tempfile.mkstemp')[1][:900])


if __name__ == '__main__':
    unittest.main()

"""Inert auth-window launcher checks. No browser, StoreHost or app is executed."""
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SOURCE = Path(__file__).resolve().parents[1] / 'store_sign_in.py'
spec = importlib.util.spec_from_file_location('store_sign_in_checks', SOURCE)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class SignInLauncherTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='Playhub-SignIn-Inert-')
        self.root = Path(self.temp.name)
        (self.root / 'StoreLibrary').mkdir()
        self.exe = self.root / 'Playhub.exe'
        self.exe.write_bytes(b'INERT NOT AN EXECUTABLE')
        self.manifest = {'protocol': 1, 'finalized': True, 'allowedClients': [
            {'kind': 'app', 'path': 'Playhub.exe', 'sha256': hashlib.sha256(self.exe.read_bytes()).hexdigest()}]}
        self.write_manifest()
        self.calls = []
        self.processes = []
        def start(args, **kwargs):
            self.calls.append((args, kwargs))
            process = type('FakeProcess', (), {'code': None, 'poll': lambda obj: obj.code})()
            self.processes.append(process)
            return process
        self.launcher = module.StoreSignInLauncher(lambda: self.root, start)

    def tearDown(self):
        self.temp.cleanup()

    def write_manifest(self):
        (self.root / 'StoreLibrary/store-runtime.json').write_text(json.dumps(self.manifest), encoding='utf8')

    def test_all_four_stores_open_only_auth_command_without_shell_or_credentials(self):
        for store in sorted(module.STORES):
            self.assertTrue(self.launcher.open(store)['opened'])
            args, options = self.calls[-1]
            self.assertEqual(args, [str(self.exe), '--store-sign-in', store])
            self.assertFalse(options['shell'])
            self.assertEqual(self.launcher.status(store)['state'], 'pending')
        self.assertEqual(len(self.calls), 4)

    def test_repeated_activation_while_open_does_not_spawn_another_sign_in(self):
        self.launcher.open('amazon')
        self.launcher.open('amazon')
        self.assertEqual(len(self.calls), 1)
        self.processes[0].code = 1
        self.assertEqual(self.launcher.status('amazon')['state'], 'canceled')
        self.launcher.open('amazon')
        self.assertEqual(len(self.calls), 2)

    def test_process_creation_is_pending_not_connected_and_exit_receipt_is_distinct(self):
        self.assertEqual(self.launcher.status('gog')['state'], 'idle')
        self.assertEqual(self.launcher.open('gog')['state'], 'pending')
        for code, expected in [(0, 'connected'), (1, 'canceled'), (2, 'failed'), (99, 'failed')]:
            self.processes[0].code = code
            self.assertEqual(self.launcher.status('gog')['state'], expected)

    def test_foreign_arguments_and_numeric_store_values_are_refused(self):
        for store in ('0', 'epic --extra', 'https://example.org', '', None, [], 'GOG'):
            with self.subTest(store=store), self.assertRaises(ValueError):
                self.launcher.open(store)
        self.assertEqual(self.calls, [])

    def test_changed_executable_unfinalized_or_duplicate_claim_does_not_run(self):
        for change in ('exe', 'unfinalized', 'duplicate'):
            with self.subTest(change=change):
                self.exe.write_bytes(b'INERT NOT AN EXECUTABLE')
                self.manifest['finalized'] = True
                self.manifest['allowedClients'] = self.manifest['allowedClients'][:1]
                if change == 'exe': self.exe.write_bytes(b'CHANGED INERT FILE')
                if change == 'unfinalized': self.manifest['finalized'] = False
                if change == 'duplicate': self.manifest['allowedClients'] *= 2
                self.write_manifest()
                self.assertFalse(self.launcher.open('epic')['opened'])
        self.assertEqual(self.calls, [])

    def test_incomplete_or_corrupt_runtime_manifest_returns_bounded_failure(self):
        path = self.root / 'StoreLibrary/store-runtime.json'
        for value in ([], None, 'unfinished', {'allowedClients': None},
                      {'protocol': 1, 'finalized': True, 'allowedClients': [None, 'broken']}):
            with self.subTest(value=value):
                path.write_text(json.dumps(value), encoding='utf8')
                self.assertEqual(self.launcher.open('amazon')['errorCode'], 'store_sign_in_unavailable')
        path.write_text('{unfinished', encoding='utf8')
        self.assertFalse(self.launcher.open('amazon')['opened'])
        self.assertEqual(self.calls, [])


if __name__ == '__main__':
    unittest.main()

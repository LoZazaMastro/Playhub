"""Open only Playhub's store-owned sign-in window. No credentials cross this API."""
# SPDX-License-Identifier: MIT
import hashlib
import json
from pathlib import Path
import subprocess
import threading

STORES = frozenset(('epic', 'gog', 'xbox', 'amazon'))


class StoreSignInLauncher:
    def __init__(self, installed_root, process_factory=subprocess.Popen):
        self.root = installed_root
        self.process_factory = process_factory
        self.lock = threading.Lock()
        self.processes = {}

    @staticmethod
    def _store(store):
        if not isinstance(store, str) or store not in STORES:
            raise ValueError('store_invalid')
        return store

    def _executable(self):
        root = Path(self.root())
        manifest_path = root / 'StoreLibrary' / 'store-runtime.json'
        if not root.is_absolute() or not manifest_path.is_file() or manifest_path.stat().st_size > 1024 * 1024:
            raise ValueError('store_runtime_unavailable')
        manifest = json.loads(manifest_path.read_text('utf-8-sig'))
        if not isinstance(manifest, dict) or not isinstance(manifest.get('allowedClients'), list):
            raise ValueError('store_runtime_unavailable')
        clients = [entry for entry in manifest['allowedClients']
                   if isinstance(entry, dict) and entry.get('kind') == 'app' and entry.get('path') == 'Playhub.exe']
        executable = root / 'Playhub.exe'
        if (manifest.get('protocol') != 1 or manifest.get('finalized') is not True or len(clients) != 1
                or not executable.is_file() or executable.stat().st_size > 64 * 1024 * 1024
                or hashlib.sha256(executable.read_bytes()).hexdigest().lower() != str(clients[0].get('sha256')).lower()):
            raise ValueError('store_runtime_unavailable')
        return executable

    def status(self, store):
        store = self._store(store)
        with self.lock:
            process = self.processes.get(store)
            if process is None:
                return {'store': store, 'state': 'idle'}
            code = process.poll()
            return {'store': store, 'state': 'pending' if code is None else
                    'connected' if code == 0 else 'canceled' if code == 1 else 'failed'}

    def open(self, store):
        store = self._store(store)
        with self.lock:
            existing = self.processes.get(store)
            if existing is not None and existing.poll() is None:
                return {'opened': True, 'store': store, 'state': 'pending'}
            try:
                executable = self._executable()
                process = self.process_factory([str(executable), '--store-sign-in', store],
                    stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                    creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0), shell=False)
                self.processes[store] = process
                return {'opened': True, 'store': store, 'state': 'pending'}
            except (OSError, ValueError, KeyError, TypeError):
                return {'opened': False, 'store': store, 'state': 'failed', 'errorCode': 'store_sign_in_unavailable'}

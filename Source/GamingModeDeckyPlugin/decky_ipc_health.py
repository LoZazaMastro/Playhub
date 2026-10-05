"""Bounded, fail-closed recovery evidence for Decky's pre-plugin IPC bind failure."""
import ctypes
from ctypes import wintypes
from datetime import datetime
import json
import os
from pathlib import Path
import re
import threading


def decky_session():
    """Identify the actual loader parent, including its creation time (PID reuse safe)."""
    if os.name != "nt":
        return None
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel.OpenProcess.restype = wintypes.HANDLE
    kernel.GetProcessTimes.argtypes = [wintypes.HANDLE] + [ctypes.POINTER(wintypes.FILETIME)] * 4
    kernel.GetProcessTimes.restype = wintypes.BOOL
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    pid = os.getppid()
    handle = kernel.OpenProcess(0x1000, False, pid)
    if not handle:
        return None
    try:
        created, exited, system, user = (wintypes.FILETIME() for _ in range(4))
        if not kernel.GetProcessTimes(handle, ctypes.byref(created), ctypes.byref(exited), ctypes.byref(system), ctypes.byref(user)):
            return None
        ticks = (created.dwHighDateTime << 32) | created.dwLowDateTime
        return {"id": f"{pid}:{ticks}", "started": (ticks - 116444736000000000) / 10000000}
    finally:
        kernel.CloseHandle(handle)


def is_pre_main_bind_failure(text):
    # Require Decky's socket task and its exact loopback bind denial together.
    # A plugin exception, a normal disconnect, or a startup already reached is
    # never a reason to reload a plugin.
    if re.search(r"main\.py|backend[. ]start|\b_main\b", text, re.I):
        return False
    return ("PortSocket.setup_server" in text and "localsocket.py" in text
            and re.search(r"PermissionError(?:: \[Errno 13\]|\(13,)", text) is not None
            and re.search(r"error while attempting to bind on address \('127\.0\.0\.1', (?:[4-5]\d{4})\)", text) is not None)


class DeckyIpcHealth:
    def __init__(self, plugins, logs, runtime, session=None):
        self.plugins, self.logs = Path(plugins), Path(logs)
        self.journal = Path(runtime) / "decky-ipc-recovery.json"
        self.session = session if session is not None else decky_session()
        self.lock = threading.Lock()

    def failures(self):
        if not self.session:
            return []
        result = []
        try:
            folders = list(self.plugins.iterdir())
        except OSError:
            return []
        for folder in folders:
            try:
                if not folder.is_dir() or folder.is_symlink() or not (folder / "main.py").is_file():
                    continue
                manifest = json.loads((folder / "plugin.json").read_text(encoding="utf-8-sig"))
                name = manifest.get("name")
                if not isinstance(name, str) or not name:
                    continue
                log_dir = self.logs / folder.name
                if log_dir.is_symlink():
                    continue
                logs = []
                for path in log_dir.glob("*.log"):
                    try:
                        stamp = datetime.strptime(path.stem, "%Y-%m-%d %H.%M.%S").timestamp()
                    except ValueError:
                        continue
                    logs.append((stamp, path))
                if not logs:
                    continue
                stamp, latest = max(logs, key=lambda item: item[0])
                # Filenames are second-resolution local timestamps. Never infer
                # session membership from mtime, which can be changed by copying.
                if stamp < int(self.session["started"]) or latest.is_symlink() or latest.stat().st_size > 131072:
                    continue
                content = latest.read_text(encoding="utf-8", errors="replace")
                if is_pre_main_bind_failure(content):
                    result.append({"folder": folder.name, "name": name, "session": self.session["id"], "log": latest.name})
            except (OSError, ValueError, TypeError):
                continue
        return result

    def claim(self, folder, session, log):
        """Persist before the reload request, so frontend remounts cannot loop."""
        with self.lock:
            candidate = next((item for item in self.failures() if item["folder"] == folder
                              and item["session"] == session and item["log"] == log), None)
            if candidate is None:
                return {"ok": False}
            try:
                record = {"session": session, "plugins": []}
                if self.journal.exists():
                    previous = json.loads(self.journal.read_text(encoding="utf-8"))
                    if not isinstance(previous, dict) or not isinstance(previous.get("plugins"), list):
                        return {"ok": False}
                    if previous.get("session") == session:
                        record = previous
                if folder in record["plugins"]:
                    return {"ok": False}
                record["plugins"].append(folder)
                self.journal.parent.mkdir(parents=True, exist_ok=True)
                temporary = self.journal.with_suffix(".tmp")
                with temporary.open("w", encoding="utf-8") as handle:
                    json.dump(record, handle)
                    handle.flush()
                    os.fsync(handle.fileno())
                os.replace(temporary, self.journal)
                return {"ok": True, "name": candidate["name"]}
            except (OSError, ValueError, TypeError, KeyError):
                # Never retry a recovery when its session guard cannot be saved.
                return {"ok": False}

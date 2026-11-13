"""User-selected process scheduling profiles. The native owner grants and revokes the lease."""
import json
import os
import tempfile
import threading
from pathlib import Path


MODES = frozenset(("default", "efficiency", "performance"))
DEFAULT = ("default", "default")
MAX_PROFILES = 512


def valid_app(app_id):
    return type(app_id) is int and 0 < app_id <= 0xffffffff


class GameExecutionProfiles:
    def __init__(self, directory, request):
        self._path = Path(directory) / "game-execution-profiles.json"
        self._request = request
        self._lock = threading.Lock()
        self._profiles = self._read()
        self._running = 0
        self._requested = 0
        self._status = "Default"
        self._closed = False

    def _read(self):
        try:
            with self._path.open(encoding="utf-8") as handle:
                record = json.loads(handle.read(65537))
            if type(record) is not dict or record.get("version") != 1 or type(record.get("profiles")) is not dict:
                return {}
            valid = {}
            for key, modes in list(record["profiles"].items())[:MAX_PROFILES]:
                if not key.isascii() or not key.isdecimal() or len(key) > 10:
                    continue
                app_id = int(key)
                if (valid_app(app_id) and key == str(app_id) and type(modes) is list and len(modes) == 2
                        and all(type(mode) is str and mode in MODES for mode in modes)):
                    valid[app_id] = tuple(modes)
            return valid
        except (OSError, ValueError):
            return {}

    def _write(self, profiles):
        self._path.parent.mkdir(parents=True, exist_ok=True)
        temporary = None
        try:
            with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=self._path.parent,
                                             prefix=".game-execution-", delete=False) as handle:
                temporary = Path(handle.name)
                json.dump({"version": 1, "profiles": profiles}, handle, separators=(",", ":"))
            os.replace(temporary, self._path)
        finally:
            if temporary is not None:
                temporary.unlink(missing_ok=True)

    def _state(self, app_id, ok=True):
        ac, battery = self._profiles.get(app_id, DEFAULT)
        return {"ok": ok, "ac": ac, "battery": battery,
                "status": self._status if app_id == self._running else "Default"}

    def get(self, app_id):
        if not valid_app(app_id):
            return {"ok": False}
        with self._lock:
            return self._state(app_id)

    def save(self, app_id, ac, battery):
        if not valid_app(app_id) or any(type(mode) is not str or mode not in MODES for mode in (ac, battery)):
            return {"ok": False}
        with self._lock:
            if self._closed:
                return self._state(app_id, False)
            value = (ac, battery)
            if value != self._profiles.get(app_id, DEFAULT):
                profiles = dict(self._profiles)
                if value == DEFAULT:
                    profiles.pop(app_id, None)
                else:
                    profiles[app_id] = value
                if len(profiles) > MAX_PROFILES:
                    return self._state(app_id, False)
                try:
                    self._write(profiles)
                except OSError:
                    return self._state(app_id, False)
                self._profiles = profiles
            if app_id == self._running:
                self._renew()
            return self._state(app_id)

    def _stop(self):
        if self._requested:
            result = self._request("/session/steam/game-qos/stop", {})
            if result.get("ok") is not True:
                return "RestoreUnavailable"
            self._requested = 0
        return "Default"

    def _renew(self):
        modes = self._profiles.get(self._running, DEFAULT)
        if modes == DEFAULT:
            self._status = self._stop()
            return
        if self._requested and self._requested != self._running:
            if self._stop() != "Default":
                self._status = "RestoreUnavailable"
                return
        self._requested = self._running
        response = self._request("/session/steam/game-qos",
                                 {"appId": self._running, "ac": modes[0], "battery": modes[1]})
        self._status = response.get("status", "Unavailable")

    def observe(self, app_id):
        with self._lock:
            if self._closed:
                return
            self._running = app_id if valid_app(app_id) else 0
            self._renew()

    def close(self):
        with self._lock:
            self._closed = True
            self._stop()

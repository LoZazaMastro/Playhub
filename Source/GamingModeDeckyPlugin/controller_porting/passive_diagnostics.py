"""File-only RPC implementation. Deliberately does not import sdl_backend."""

import hashlib
import os
from pathlib import Path
import stat
import time

MAX_BYTES = 32 * 1024 * 1024
FEATURES = ("liveInput", "physicalRumble", "liveMapping", "virtualOutput",
            "gyro", "trackpads", "exclusiveMode", "hardwareValidated")


def steam_sdl_path():
    """Read Steam's current-user install path; no scan, launch or DLL loading."""
    if os.name != "nt":
        return None
    import winreg
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, r"Software\Valve\Steam") as key:
            value, kind = winreg.QueryValueEx(key, "SteamPath")
        if kind != winreg.REG_SZ or not isinstance(value, str) or not value:
            return None
        return str(Path(value) / "SDL3.dll")
    except OSError:
        return None


def passive_diagnostics(configured_path=None):
    """Observe a host-configured regular file, never a client-supplied path.

    Presence/hash do not prove PE validity, ABI, architecture, license approval,
    SDL version, compiled-in drivers, connected devices or hardware capability.
    """
    result = {
        "schemaVersion": 1, "scope": "sdl_file_only",
        "observedAtMs": time.time_ns() // 1_000_000,
        "status": "not_configured", "sha256": None, "sizeBytes": None,
        "nativeLoaded": False, "deviceProbed": False,
        "capabilities": {name: False for name in FEATURES},
    }
    if configured_path is None:
        return result
    if not isinstance(configured_path, (str, os.PathLike)):
        result["status"] = "invalid_path"
        return result
    try:
        path = Path(configured_path)
        # Reject network/device namespace paths; this endpoint is local file-only.
        if not path.is_absolute() or str(path).startswith(("\\\\", "//")):
            result["status"] = "invalid_path"
            return result
        info = path.stat()
        if not stat.S_ISREG(info.st_mode):
            result["status"] = "invalid_path"
            return result
        if info.st_size > MAX_BYTES:
            result["status"] = "file_too_large"
            return result
        digest = hashlib.sha256()
        with path.open("rb") as stream:
            before = os.fstat(stream.fileno())
            if not stat.S_ISREG(before.st_mode):
                result["status"] = "invalid_path"
                return result
            total = 0
            while block := stream.read(64 * 1024):
                total += len(block)
                if total > MAX_BYTES:
                    result["status"] = "file_too_large"
                    return result
                digest.update(block)
            after = os.fstat(stream.fileno())
        identity = lambda s: (s.st_dev, s.st_ino, s.st_size, s.st_mtime_ns, s.st_ctime_ns)
        if identity(before) != identity(after) or identity(after) != identity(path.stat()):
            result["status"] = "file_changed"
            return result
        result.update(status="file_observed", sha256=digest.hexdigest(), sizeBytes=total)
    except FileNotFoundError:
        result["status"] = "file_missing"
    except PermissionError:
        result["status"] = "access_denied"
    except (OSError, ValueError):
        result["status"] = "read_failed"
    return result


class PassiveControllerDiagnosticsMixin:
    # Parent owns configuration. No renderer argument, path discovery or default DLL.
    controller_sdl_diagnostic_path = None

    async def get_controller_sdl_diagnostics(self):
        return passive_diagnostics(self.controller_sdl_diagnostic_path)

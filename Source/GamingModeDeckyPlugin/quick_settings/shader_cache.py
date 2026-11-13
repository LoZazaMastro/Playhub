"""Lazy shader-cache inventory and explicit, verified NVIDIA cache controls."""
import importlib.util
import ctypes
import os
from pathlib import Path
import secrets
import re
import stat
import threading
import time

_spec = importlib.util.spec_from_file_location("playhub_shader_nvapi", Path(__file__).with_name("shader_nvapi.py"))
_nvapi = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_nvapi)


def _plain(path):
    """Cache inventory must never follow links, junctions or a network share."""
    path = Path(path)
    if not path.is_absolute() or str(path).startswith(("\\\\", "//")):
        return False
    if os.name == "nt" and ctypes.windll.kernel32.GetDriveTypeW(str(path.anchor)) not in (2, 3, 6):
        return False
    try:
        for parent in (path, *path.parents):
            try:
                info = parent.lstat()
            except FileNotFoundError:
                continue
            if stat.S_ISLNK(info.st_mode) or getattr(info, "st_file_attributes", 0) & 0x400:
                return False
        return True
    except OSError:
        return False


def measure(path, deadline, clock=time.monotonic, limit=20000):
    result = {"state": "missing", "bytes": 0, "files": 0, "modified": None}
    if clock() >= deadline:
        return {**result, "state": "partial"}
    if not _plain(path):
        return {**result, "state": "unavailable"}
    try:
        if not path.is_dir():
            return result
    except OSError:
        return {**result, "state": "unavailable"}
    result["state"] = "ready"
    pending = [path]
    seen = 0
    while pending:
        if clock() >= deadline or seen >= limit:
            result["state"] = "partial"
            break
        directory = pending.pop()
        # Repeat the ancestor proof immediately before enumerating each child.
        if not _plain(directory):
            result["state"] = "partial"
            continue
        try:
            with os.scandir(directory) as entries:
                for entry in entries:
                    seen += 1
                    if clock() >= deadline or seen >= limit:
                        result["state"] = "partial"
                        pending.clear()
                        break
                    try:
                        info = entry.stat(follow_symlinks=False)
                        if stat.S_ISLNK(info.st_mode) or getattr(info, "st_file_attributes", 0) & 0x400:
                            result["state"] = "partial"
                        elif stat.S_ISDIR(info.st_mode):
                            pending.append(Path(entry.path))
                        elif stat.S_ISREG(info.st_mode):
                            result["bytes"] += info.st_size
                            result["files"] += 1
                            result["modified"] = max(result["modified"] or 0, int(info.st_mtime))
                    except OSError:
                        result["state"] = "partial"
        except OSError:
            result["state"] = "partial"
    return result


class ShaderCacheManager:
    def __init__(self, preflight, run_cmd, powershell, steam_path, vendor_api=None, local_data=None):
        self._preflight = preflight
        self._run_cmd = run_cmd
        self._powershell = powershell
        self._steam_path = steam_path
        self._vendor_api = vendor_api or _nvapi.invoke
        self._local_data = local_data
        self._lock = threading.Lock()
        self._token = None
        self._state = None
        self._issued = 0

    def _driver(self, *args):
        try:
            result = self._vendor_api(self._run_cmd, self._powershell, *args)
            state = result.get("state") if isinstance(result, dict) else None
            valid = (isinstance(state, dict) and type(state.get("Enabled")) is bool
                     and type(state.get("Limit")) is int and 0 <= state["Limit"] <= 0xffffffff
                     and type(state.get("DefaultLimit")) is int and 0 <= state["DefaultLimit"] <= 0xffffffff
                     and all(type(state.get(k)) is bool for k in ("EnableDefault", "LimitDefault")))
            if result.get("available") is not True or not valid:
                self._token = self._state = None
                return {"available": False, "status": result.get("status", "unavailable")}
            self._state = dict(state)
            self._issued = time.monotonic()
            self._token = secrets.token_urlsafe(24)
            return {"available": True, "status": result.get("status", "ready"), "token": self._token,
                    "enabled": state["Enabled"], "limit": state["Limit"], "driverDefault": state["DefaultLimit"],
                    "defaultLimit": state["LimitDefault"]}
        except Exception:
            self._token = self._state = None
            return {"available": False, "status": "unavailable"}

    def get(self, app_id=0):
        if not self._lock.acquire(blocking=False):
            return {"status": "busy", "caches": [], "nvidia": {"available": False, "status": "busy"}}
        try:
            preflight = self._preflight.get(app_id)
            vendors = {row.get("vendor") for row in preflight.get("adapters", [])}
            local = Path(self._local_data if self._local_data is not None else os.environ.get("LOCALAPPDATA", ""))
            candidates = [("directx", local / "D3DSCache")]
            if "nvidia" in vendors:
                candidates += [("nvidia-dx", local / "NVIDIA" / "DXCache"), ("nvidia-gl", local / "NVIDIA" / "GLCache"),
                               ("nvidia-low-dx", local.parent / "LocalLow" / "NVIDIA" / "PerDriverVersion" / "DXCache")]
            if "amd" in vendors:
                candidates += [("amd-dx", local / "AMD" / "DxCache"), ("amd-dxc", local / "AMD" / "DxcCache"),
                               ("amd-vulkan", local / "AMD" / "VkCache"), ("amd-gl", local / "AMD" / "GLCache")]
            if "intel" in vendors:
                candidates += [("intel", local.parent / "LocalLow" / "Intel" / "ShaderCache")]
            # Reuse the manifest-verified Steam library parser. No client path
            # or executable is accepted by this operation.
            if type(app_id) is int and 0 < app_id <= 0xffffffff:
                for index, path in enumerate(self._preflight.cache_paths(app_id)):
                    candidates.append(("steam" if index == 0 else "steam-" + str(index), path))
            deadline = time.monotonic() + 1.5
            caches = [{"id": key, **measure(path, deadline)} for key, path in candidates]
            nvidia = self._driver() if "nvidia" in vendors else {"available": False, "status": "unsupported"}
            return {"status": "ready", "caches": caches, "nvidia": nvidia, "preflight": preflight}
        except Exception:
            return {"status": "unavailable", "caches": [], "nvidia": {"available": False, "status": "unavailable"}}
        finally:
            self._lock.release()

    def set_nvidia(self, request):
        if not isinstance(request, dict) or type(request.get("enabled")) is not bool:
            return {"available": False, "status": "invalid"}
        limit = request.get("limit")
        if limit != "default" and (type(limit) is not int or not 0 <= limit <= 0xffffffff):
            return {"available": False, "status": "invalid"}
        if not self._lock.acquire(blocking=False):
            return {"available": False, "status": "busy"}
        try:
            if (not isinstance(request.get("token"), str) or not re.fullmatch(r"[A-Za-z0-9_-]{32}", request["token"]) or not self._token
                    or not secrets.compare_digest(request["token"], self._token)
                    or time.monotonic() - self._issued > 120):
                return {"available": False, "status": "changed"}
            # Consume each proof once. A failed/slow write is never retried.
            expected = self._state
            if limit != "default" and limit not in _nvapi.LIMITS and limit != expected["Limit"]:
                return {"available": False, "status": "invalid"}
            self._token = self._state = None
            return self._driver(expected, request["enabled"], limit)
        finally:
            self._lock.release()

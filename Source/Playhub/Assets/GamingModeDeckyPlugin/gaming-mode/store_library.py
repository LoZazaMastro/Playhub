"""Playhub's Windows store bridge. SPDX-License-Identifier: MIT.

Account authorization stays in the Playhub app. The Decky surface can read its
library, request explicit installations, and control downloads owned by Playhub.
"""
import asyncio
import ctypes
import hashlib
import json
import os
import subprocess
import time
import uuid
from ctypes import wintypes

_ALLOWED = frozenset(("snapshot", "downloads", "refresh", "disconnect", "prepare", "prepare-parent", "directories", "enqueue", "control", "clear-completed", "clear-canceled", "remove-history", "cancel-cleanup", "windows-install-dismiss", "steam-native-identities", "steam-adopt-import", "steam-forget-uninstalled", "steam-repair-missing", "steam-migration-pending", "steam-migration-report", "xbox-catalog", "xbox-refresh", "xbox-gamepass-install",
    "installed-game", "prepare-uninstall", "uninstall", "xbox-account-owned", "xbox-account-open-owned", "xbox-install-volumes", "ui-install-take",
    "steam-register", "steam-activity", "steam-sync-request", "steam-sync-status", "steam-sync-pending", "steam-sync-created", "steam-sync-ack", "steam-owned"))
_ALLOWED = _ALLOWED | frozenset(("xbox-achievements", "store-achievements"))
_MAX_RESPONSE = 8 * 1024 * 1024


def installed_root():
    import winreg
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER,
            r"Software\Microsoft\Windows\CurrentVersion\Uninstall\Playhub") as key:
        root = os.path.abspath(winreg.QueryValueEx(key, "InstallLocation")[0])
    if not os.path.isfile(os.path.join(root, "Playhub.exe")):
        raise RuntimeError("store_runtime_missing")
    return root


def _sid():
    advapi, kernel = ctypes.WinDLL("advapi32", use_last_error=True), ctypes.WinDLL("kernel32", use_last_error=True)
    token, length, text = wintypes.HANDLE(), wintypes.DWORD(), wintypes.LPWSTR()
    kernel.GetCurrentProcess.restype = wintypes.HANDLE
    advapi.OpenProcessToken.argtypes = (wintypes.HANDLE, wintypes.DWORD, ctypes.POINTER(wintypes.HANDLE))
    advapi.GetTokenInformation.argtypes = (wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(wintypes.DWORD))
    advapi.ConvertSidToStringSidW.argtypes = (ctypes.c_void_p, ctypes.POINTER(wintypes.LPWSTR))
    kernel.CloseHandle.argtypes = (wintypes.HANDLE,)
    kernel.LocalFree.argtypes = (ctypes.c_void_p,)
    if not advapi.OpenProcessToken(kernel.GetCurrentProcess(), 8, ctypes.byref(token)):
        raise RuntimeError("store_user_unavailable")
    try:
        advapi.GetTokenInformation(token, 1, None, 0, ctypes.byref(length))
        if not 8 <= length.value <= 65536:
            raise RuntimeError("store_user_unavailable")
        data = ctypes.create_string_buffer(length.value)
        if not advapi.GetTokenInformation(token, 1, data, length, ctypes.byref(length)):
            raise RuntimeError("store_user_unavailable")
        sid = ctypes.cast(data, ctypes.POINTER(ctypes.c_void_p))[0]
        if not advapi.ConvertSidToStringSidW(sid, ctypes.byref(text)):
            raise RuntimeError("store_user_unavailable")
        try:
            return text.value
        finally:
            kernel.LocalFree(ctypes.cast(text, ctypes.c_void_p))
    finally:
        kernel.CloseHandle(token)


def _ensure_host(root):
    folder = os.path.join(root, "StoreLibrary")
    host = os.path.join(folder, "Playhub.StoreHost.exe")
    with open(os.path.join(folder, "store-runtime.json"), "r", encoding="utf-8-sig") as stream:
        manifest = json.load(stream)
    with open(host, "rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    if manifest.get("protocol") != 1 or digest.lower() != str(manifest.get("hostSha256", "")).lower():
        raise RuntimeError("store_runtime_invalid")
    subprocess.Popen([host, "--serve"], cwd=folder, creationflags=subprocess.CREATE_NO_WINDOW,
        stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


class _Overlapped(ctypes.Structure):
    _fields_ = [("internal", ctypes.c_size_t), ("internal_high", ctypes.c_size_t),
        ("offset", wintypes.DWORD), ("offset_high", wintypes.DWORD), ("event", wintypes.HANDLE)]


class _Pipe:
    def __init__(self, path):
        self.kernel = k = ctypes.WinDLL("kernel32", use_last_error=True)
        k.CreateFileW.argtypes = (wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p,
            wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE)
        k.CreateFileW.restype = wintypes.HANDLE
        k.CreateEventW.argtypes = (ctypes.c_void_p, wintypes.BOOL, wintypes.BOOL, wintypes.LPCWSTR)
        k.CreateEventW.restype = wintypes.HANDLE
        for name in ("ReadFile", "WriteFile"):
            getattr(k, name).argtypes = (wintypes.HANDLE, ctypes.c_void_p, wintypes.DWORD,
                ctypes.POINTER(wintypes.DWORD), ctypes.POINTER(_Overlapped))
        k.WaitForSingleObject.argtypes = (wintypes.HANDLE, wintypes.DWORD)
        k.GetOverlappedResult.argtypes = (wintypes.HANDLE, ctypes.POINTER(_Overlapped), ctypes.POINTER(wintypes.DWORD), wintypes.BOOL)
        k.CancelIoEx.argtypes = (wintypes.HANDLE, ctypes.POINTER(_Overlapped))
        k.CloseHandle.argtypes = (wintypes.HANDLE,)
        self.handle = k.CreateFileW(path, 0xC0000000, 0, None, 3, 0x40000000, None)
        if self.handle == ctypes.c_void_p(-1).value:
            raise FileNotFoundError("store_host_unavailable")

    def transfer(self, writing, data, deadline):
        k, count = self.kernel, wintypes.DWORD()
        event = k.CreateEventW(None, True, False, None)
        if not event:
            raise RuntimeError("store_pipe_failed")
        operation = _Overlapped(event=event)
        buffer = ctypes.create_string_buffer(data) if writing else ctypes.create_string_buffer(16384)
        size = len(data) if writing else 16384
        try:
            ok = (k.WriteFile if writing else k.ReadFile)(self.handle, buffer, size, None, ctypes.byref(operation))
            if not ok:
                if ctypes.get_last_error() != 997:
                    raise RuntimeError("store_pipe_failed")
                wait = k.WaitForSingleObject(event, max(0, min(60000, int((deadline - time.monotonic()) * 1000))))
                if wait != 0:
                    k.CancelIoEx(self.handle, ctypes.byref(operation))
                    # Drain cancellation before releasing the OVERLAPPED/event memory.
                    k.GetOverlappedResult(self.handle, ctypes.byref(operation), ctypes.byref(count), True)
                    raise TimeoutError("store_host_timeout")
            if not k.GetOverlappedResult(self.handle, ctypes.byref(operation), ctypes.byref(count), False):
                raise RuntimeError("store_pipe_failed")
            return count.value if writing else buffer.raw[:count.value]
        finally:
            k.CloseHandle(event)

    def close(self):
        self.kernel.CloseHandle(self.handle)


def _rpc(method, args):
    if method not in _ALLOWED or not isinstance(args, dict):
        raise ValueError("store_method_invalid")
    maintenance = os.path.join(os.environ.get("LOCALAPPDATA", ""), "Playhub", "StoreLibrary", "maintenance-v1.json")
    try:
        with open(maintenance, "r", encoding="utf-8-sig") as stream:
            marker = json.load(stream)
        from datetime import datetime, timezone
        expires = datetime.fromisoformat(str(marker.get("expiresAt", "")).replace("Z", "+00:00"))
        remaining = (expires - datetime.now(timezone.utc)).total_seconds()
        if marker.get("protocol") == 1 and 0 < remaining <= 330:
            raise RuntimeError("store_update_in_progress")
    except (OSError, ValueError, TypeError):
        pass
    request_id = str(uuid.uuid4())
    request = json.dumps({"protocol": 1, "id": request_id, "method": method, "args": args},
        separators=(",", ":"), allow_nan=False).encode("utf-8") + b"\n"
    if len(request) > 65536:
        raise ValueError("store_request_invalid")
    name = r"\\.\pipe\Playhub.StoreLibrary.v1." + hashlib.sha256(_sid().encode()).hexdigest()[:24].upper()
    deadline, channel, started = time.monotonic() + (120 if method in ("refresh", "uninstall") else 60), None, False
    while channel is None:
        try:
            channel = _Pipe(name)
        except FileNotFoundError:
            if not started:
                _ensure_host(installed_root())
                started = True
            if time.monotonic() >= deadline:
                raise TimeoutError("store_host_timeout")
            time.sleep(.1)
    try:
        sent = 0
        while sent < len(request):
            count = channel.transfer(True, request[sent:], deadline)
            if not count:
                raise RuntimeError("store_pipe_failed")
            sent += count
        response = bytearray()
        while b"\n" not in response:
            block = channel.transfer(False, None, deadline)
            if not block or len(response) + len(block) > _MAX_RESPONSE:
                raise RuntimeError("store_response_invalid")
            response.extend(block)
        message = json.loads(response.split(b"\n", 1)[0])
        if message.get("protocol") != 1 or message.get("id") != request_id:
            raise RuntimeError("store_response_invalid")
        if message.get("ok") is not True:
            code = message.get("errorCode", "store_command_failed")
            raise RuntimeError(code if isinstance(code, str) and len(code) <= 80 and code.replace("_", "").isalnum() else "store_command_failed")
        return message.get("data")
    finally:
        channel.close()


async def request(method, args):
    # No raw exception/arguments are returned or logged: launch/auth material stays private.
    try:
        return {"ok": True, "data": await asyncio.to_thread(_rpc, method, args)}
    except (OSError, ValueError, RuntimeError, TimeoutError) as error:
        code = str(error)
        return {"ok": False, "errorCode": code if len(code) <= 80 and code.replace("_", "").isalnum() else "store_unavailable"}

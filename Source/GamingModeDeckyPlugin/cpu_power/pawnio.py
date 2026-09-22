"""Original diagnostic-only client of the published PawnIO device IOCTL ABI.

No DLL from HC or PawnIOLib is loaded. No raw register or SMU-command RPC exists.
Calling open() is an explicit hardware diagnostic, never an availability probe.
"""

from contextlib import contextmanager
from dataclasses import dataclass
import ctypes
import hashlib
import json
import os
from pathlib import Path
import re
import struct
import subprocess
import tempfile
import threading


LOAD_BINARY = (41394 << 16) | (0x821 << 2)
EXECUTE_FN = (41394 << 16) | (0x841 << 2)
VERSION = (41394 << 16) | (0x861 << 2)
READ_FUNCTIONS = {
    "ioctl_get_code_name": 1,
    "ioctl_get_smu_version": 1,
    "ioctl_resolve_pm_table": 2,
}


@dataclass(frozen=True)
class AuditedArtifacts:
    """Integrator-owned pins, not paths/hashes accepted from a Decky request.

    The module hash must identify a reviewed, officially signed RyzenSMU module.
    The driver hash must identify the reviewed Official Edition (not Unrestricted).
    Empty/unreviewed pins fail closed; this source distribution ships no pins.
    """
    driver_path: str
    driver_sha256: str
    module_path: str
    module_sha256: str
    driver_version: int


def _pinned_bytes(path, digest, maximum_size):
    if not Path(path).is_absolute() or not re.fullmatch(r"[0-9a-fA-F]{64}", digest):
        raise ValueError("artifact_pin_required")
    with open(path, "rb") as source:
        data = source.read(maximum_size + 1)
    if not data or len(data) > maximum_size:
        raise ValueError("invalid_artifact_size")
    if hashlib.sha256(data).hexdigest() != digest.lower():
        raise ValueError("artifact_hash_mismatch")
    return data


def _verify_installed_driver(artifacts):
    _pinned_bytes(artifacts.driver_path, artifacts.driver_sha256, 16 * 1024 * 1024)
    path = artifacts.driver_path.replace("'", "''")
    script = (
        "$ErrorActionPreference='Stop';"
        f"$s=Get-AuthenticodeSignature -LiteralPath '{path}';"
        "$d=Get-CimInstance Win32_SystemDriver -Filter \"Name='PawnIO'\";"
        "@{signature=[string]$s.Status;state=[string]$d.State;"
        "path=[string]$d.PathName}|ConvertTo-Json -Compress"
    )
    powershell = os.path.join(os.environ["SystemRoot"], "System32", "WindowsPowerShell", "v1.0", "powershell.exe")
    # Lo script gira da un file .ps1 temporaneo con -File, non con -EncodedCommand:
    # PowerShell in base64 e' un innesco molto forte per gli euristici antivirus
    # (Trojan:Win32/Commando.A!ml) e produce falsi positivi sulle macchine degli utenti.
    # Stessa scelta gia' adottata nell'app: DeckyPluginService.cs e UwpXboxService.cs.
    handle, script_path = tempfile.mkstemp(prefix="playhub-pawnio-", suffix=".ps1")
    try:
        with os.fdopen(handle, "w", encoding="utf-8-sig", newline="\r\n") as file:
            file.write(script)
        result = subprocess.run(
            [powershell, "-NoLogo", "-NoProfile", "-NonInteractive",
             "-ExecutionPolicy", "Bypass", "-File", script_path],
            capture_output=True, text=True, timeout=15, check=True,
            env={key: value for key, value in os.environ.items() if key.lower() != "psmodulepath"},
            creationflags=subprocess.CREATE_NO_WINDOW,
        )
    finally:
        try:
            os.unlink(script_path)
        except OSError:
            pass
    state = json.loads(result.stdout)
    if state.get("signature") != "Valid" or state.get("state") != "Running":
        raise PermissionError("signed_running_pawnio_required")
    image = state.get("path", "").strip('"')
    if image.startswith("\\??\\"):
        image = image[4:]
    if image.lower().startswith("\\systemroot\\"):
        image = os.path.join(os.environ["SystemRoot"], image[12:])
    if Path(image).resolve() != Path(artifacts.driver_path).resolve():
        raise PermissionError("driver_service_image_mismatch")


class NativeDevice:
    def __init__(self):
        self._handle = None
        self._kernel = None

    def open(self, artifacts):
        if os.name != "nt" or ctypes.sizeof(ctypes.c_void_p) != 8:
            raise OSError("windows_x64_required")
        _verify_installed_driver(artifacts)
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel.CreateFileW.argtypes = [ctypes.c_wchar_p, ctypes.c_uint32, ctypes.c_uint32,
                                      ctypes.c_void_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p]
        kernel.CreateFileW.restype = ctypes.c_void_p
        kernel.DeviceIoControl.argtypes = [ctypes.c_void_p, ctypes.c_uint32, ctypes.c_void_p,
                                          ctypes.c_uint32, ctypes.c_void_p, ctypes.c_uint32,
                                          ctypes.POINTER(ctypes.c_uint32), ctypes.c_void_p]
        kernel.DeviceIoControl.restype = ctypes.c_int
        kernel.CloseHandle.argtypes = [ctypes.c_void_p]
        kernel.CloseHandle.restype = ctypes.c_int
        kernel.CreateMutexW.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_wchar_p]
        kernel.CreateMutexW.restype = ctypes.c_void_p
        kernel.WaitForSingleObject.argtypes = [ctypes.c_void_p, ctypes.c_uint32]
        kernel.WaitForSingleObject.restype = ctypes.c_uint32
        kernel.ReleaseMutex.argtypes = [ctypes.c_void_p]
        kernel.ReleaseMutex.restype = ctypes.c_int
        handle = kernel.CreateFileW(r"\\?\GLOBALROOT\Device\PawnIO", 0xC0000000,
                                    7, None, 3, 0, None)
        if handle in (None, ctypes.c_void_p(-1).value):
            raise ctypes.WinError(ctypes.get_last_error())
        self._kernel, self._handle = kernel, handle

    def ioctl(self, code, payload, output_size):
        if self._handle is None:
            raise RuntimeError("device_closed")
        if code not in (VERSION, LOAD_BINARY, EXECUTE_FN):
            raise ValueError("ioctl_not_allowlisted")
        incoming = ctypes.create_string_buffer(payload) if payload else None
        outgoing = ctypes.create_string_buffer(output_size) if output_size else None
        written = ctypes.c_uint32()
        # Synchronous driver calls cannot promise a userspace cancellation bound.
        # This diagnostic must not be scheduled as a frequent UI polling job.
        if not self._kernel.DeviceIoControl(self._handle, code, incoming, len(payload),
                                            outgoing, output_size, ctypes.byref(written), None):
            raise ctypes.WinError(ctypes.get_last_error())
        if written.value != output_size:
            raise OSError("unexpected_ioctl_output_length")
        return outgoing.raw if outgoing else b""

    @contextmanager
    def exclusive(self):
        mutex = self._kernel.CreateMutexW(None, False, r"Global\Access_PCI")
        if not mutex:
            raise ctypes.WinError(ctypes.get_last_error())
        acquired = False
        try:
            result = self._kernel.WaitForSingleObject(mutex, 2000)
            acquired = result in (0, 0x80)
            if result == 0x80:
                raise OSError("abandoned_pci_mutex")
            if result != 0:
                raise TimeoutError("pci_mutex_unavailable")
            yield
        finally:
            if acquired:
                self._kernel.ReleaseMutex(mutex)
            self._kernel.CloseHandle(mutex)

    def close(self):
        if self._handle is not None:
            handle, self._handle = self._handle, None
            if not self._kernel.CloseHandle(handle):
                raise ctypes.WinError(ctypes.get_last_error())


class RaphaelDiagnostic:
    def __init__(self, artifacts, device_factory=NativeDevice):
        self._artifacts = artifacts
        self._factory = device_factory
        self._lock = threading.RLock()
        self._device = None

    def open(self, *, authorized=False):
        with self._lock:
            if authorized is not True:
                raise PermissionError("explicit_diagnostic_authorization_required")
            if self._device is not None:
                raise RuntimeError("diagnostic_already_open")
            module = _pinned_bytes(self._artifacts.module_path,
                                   self._artifacts.module_sha256, 1024 * 1024)
            device = self._factory()
            try:
                device.open(self._artifacts)
                version = struct.unpack("<I", device.ioctl(VERSION, b"", 4))[0]
                if version != self._artifacts.driver_version:
                    raise OSError("driver_abi_not_reviewed")
                # Official Edition verifies the module signature during load.
                # The audited RyzenSMU main performs identification, not tuning.
                with device.exclusive():
                    device.ioctl(LOAD_BINARY, module, 0)
                    codename = self._read(device, "ioctl_get_code_name")[0]
                    if codename != 16:
                        raise OSError("processor_is_not_raphael")
                self._device = device
            except Exception:
                device.close()
                raise

    @staticmethod
    def _read(device, name):
        count = READ_FUNCTIONS[name]
        payload = name.encode("ascii").ljust(32, b"\0")
        result = device.ioctl(EXECUTE_FN, payload, count * 8)
        return struct.unpack("<" + "Q" * count, result)

    def snapshot(self):
        with self._lock:
            if self._device is None:
                raise RuntimeError("diagnostic_closed")
            with self._device.exclusive():
                codename = self._read(self._device, "ioctl_get_code_name")[0]
                if codename != 16:
                    raise OSError("processor_identity_changed")
                smu = self._read(self._device, "ioctl_get_smu_version")[0]
                table_version, table_base = self._read(self._device, "ioctl_resolve_pm_table")
            if not smu or not table_version or not table_base:
                raise OSError("invalid_smu_metadata")
            return {"schema_version": 1, "probe": "pawnio_raphael_diagnostic",
                    "hardware_accessed": True, "diagnostic_read_supported": True,
                    "cpu_codename": "raphael", "smu_version": smu,
                    "pm_table_version": table_version, "pm_table_resolved": True,
                    "supported_read": False, "supported_write": False,
                    "observed_ppt_w": None, "range": None,
                    "reason": "active_ppt_decoder_and_processor_limits_unverified"}

    def close(self):
        with self._lock:
            if self._device is not None:
                device, self._device = self._device, None
                device.close()

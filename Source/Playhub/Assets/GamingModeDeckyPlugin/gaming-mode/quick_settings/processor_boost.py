"""Windows processor boost policy, scoped to the active plan and power source."""
import ctypes
import os
import threading
import uuid
from ctypes import wintypes


class _Guid(ctypes.Structure):
    _fields_ = [("data", ctypes.c_ubyte * 16)]


def _guid(value):
    return _Guid.from_buffer_copy(uuid.UUID(value).bytes_le)


_PROCESSOR = _guid("54533251-82be-4824-96c1-47b60b740d00")
_BOOST = _guid("be337238-0d82-4146-a960-4f3749d470c7")
_SOURCES = {"ac": "AC", "battery": "DC"}
_MODES = frozenset(range(7))


class WindowsProcessorBoost:
    def __init__(self):
        if os.name != "nt":
            raise OSError("Windows power settings unavailable")
        self._power = ctypes.WinDLL("powrprof", use_last_error=True)
        self._kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        pointer = ctypes.POINTER(_Guid)
        self._power.PowerGetActiveScheme.argtypes = [wintypes.HKEY, ctypes.POINTER(ctypes.c_void_p)]
        self._power.PowerGetActiveScheme.restype = wintypes.DWORD
        self._kernel.LocalFree.argtypes = [ctypes.c_void_p]
        self._kernel.LocalFree.restype = ctypes.c_void_p
        self._power.PowerSetActiveScheme.argtypes = [wintypes.HKEY, pointer]
        self._power.PowerSetActiveScheme.restype = wintypes.DWORD
        self._power.PowerReadPossibleValue.argtypes = [wintypes.HKEY, pointer, pointer,
            ctypes.POINTER(wintypes.ULONG), wintypes.ULONG, ctypes.POINTER(ctypes.c_ubyte), ctypes.POINTER(wintypes.DWORD)]
        self._power.PowerReadPossibleValue.restype = wintypes.DWORD
        for source in _SOURCES.values():
            read = getattr(self._power, f"PowerRead{source}ValueIndex")
            read.argtypes = [wintypes.HKEY, pointer, pointer, pointer, ctypes.POINTER(wintypes.DWORD)]
            read.restype = wintypes.DWORD
            write = getattr(self._power, f"PowerWrite{source}ValueIndex")
            write.argtypes = [wintypes.HKEY, pointer, pointer, pointer, wintypes.DWORD]
            write.restype = wintypes.DWORD

    def active(self):
        allocated = ctypes.c_void_p()
        if self._power.PowerGetActiveScheme(None, ctypes.byref(allocated)) != 0 or not allocated.value:
            raise OSError("Active power plan unavailable")
        try:
            return str(uuid.UUID(bytes_le=ctypes.string_at(allocated, 16)))
        finally:
            self._kernel.LocalFree(allocated)

    def options(self):
        values = []
        for index in range(16):
            kind, size = wintypes.ULONG(), wintypes.DWORD(4)
            value = (ctypes.c_ubyte * 4)()
            code = self._power.PowerReadPossibleValue(None, ctypes.byref(_PROCESSOR), ctypes.byref(_BOOST),
                ctypes.byref(kind), index, value, ctypes.byref(size))
            if code in (2, 259):  # Missing next value or ERROR_NO_MORE_ITEMS.
                break
            if code != 0 or kind.value != 4 or size.value != 4:
                raise OSError("Processor boost values unavailable")
            mode = int.from_bytes(bytes(value), "little")
            if mode in _MODES:
                values.append(mode)
        return sorted(set(values))

    def read(self, plan, source):
        value = wintypes.DWORD()
        code = getattr(self._power, f"PowerRead{_SOURCES[source]}ValueIndex")(None, ctypes.byref(_guid(plan)),
            ctypes.byref(_PROCESSOR), ctypes.byref(_BOOST), ctypes.byref(value))
        if code != 0:
            raise OSError("Processor boost policy unavailable")
        return value.value

    def write(self, plan, source, mode):
        return getattr(self._power, f"PowerWrite{_SOURCES[source]}ValueIndex")(None, ctypes.byref(_guid(plan)),
            ctypes.byref(_PROCESSOR), ctypes.byref(_BOOST), mode) == 0

    def apply(self, plan):
        return self._power.PowerSetActiveScheme(None, ctypes.byref(_guid(plan))) == 0


class ProcessorBoost:
    def __init__(self, read_power, native=None):
        self._read_power = read_power
        self._lock = threading.Lock()
        self._native = native
        self._initialized = native is not None

    def _snapshot(self):
        # The power adapter is needed only while its Performance control is visible.
        if not self._initialized:
            self._initialized = True
            try:
                self._native = WindowsProcessorBoost()
            except (AttributeError, OSError):
                pass
        source = self._read_power().get("source")
        if self._native is None or not isinstance(source, str) or source not in _SOURCES:
            return {"available": False}
        try:
            plan = self._native.active()
            modes = self._native.options()
            mode = self._native.read(plan, source)
            if mode not in modes or len(modes) < 2 or self._native.active() != plan:
                return {"available": False}
            return {"available": True, "plan": plan, "source": source, "mode": mode, "modes": modes}
        except (AttributeError, OSError, ValueError):
            return {"available": False}

    def get(self):
        with self._lock:
            return self._snapshot()

    def set(self, request):
        if not isinstance(request, dict):
            return {"ok": False, "code": "invalid_request"}
        plan, source, mode = (request.get(key) for key in ("plan", "source", "mode"))
        if (not isinstance(plan, str) or source not in ("ac", "battery")
                or type(mode) is not int or mode not in _MODES):
            return {"ok": False, "code": "invalid_request"}
        with self._lock:
            current = self._snapshot()
            if not current["available"]:
                return {**current, "ok": False, "code": "unavailable"}
            if current["plan"] != plan or current["source"] != source:
                return {**current, "ok": False, "code": "profile_changed"}
            if mode not in current["modes"]:
                return {**current, "ok": False, "code": "invalid_request"}
            if current["mode"] == mode:
                return {**current, "ok": True}
            wrote = False
            code = "write_failed"
            try:
                if self._read_power().get("source") != source or self._native.active() != plan:
                    return {**self._snapshot(), "ok": False, "code": "profile_changed"}
                wrote = self._native.write(plan, source, mode)
                if wrote:
                    if self._native.active() != plan:
                        code = "profile_changed"
                    elif self._native.read(plan, source) != mode:
                        code = "not_confirmed"
                    elif self._native.active() != plan:
                        code = "profile_changed"
                    elif self._native.apply(plan) and self._native.read(plan, source) == mode:
                        # Re-read the current source for the UI if the cable changed during the write.
                        return {**self._snapshot(), "ok": True}
            except (AttributeError, OSError, ValueError):
                pass
            restored = True
            if wrote:
                try:
                    observed = self._native.read(plan, source)
                    if observed != current["mode"]:
                        if observed != mode:
                            # A newer Windows/OEM selection owns this value now.
                            code = "profile_changed"
                        else:
                            restored = self._native.write(plan, source, current["mode"])
                            if restored and self._native.active() == plan:
                                restored = self._native.apply(plan)
                            restored = restored and self._native.read(plan, source) == current["mode"]
                except (AttributeError, OSError, ValueError):
                    restored = False
            return {**self._snapshot(), "ok": False, "code": code if restored else "restore_failed"}

"""Windows inbox HID transport. No third-party DLLs and no report-based probing."""

import ctypes as c
from ctypes import wintypes as w
import os
import math
import threading
import uuid

from .protocols import Device, classify


class GUID(c.Structure):
    _fields_ = [("a", w.DWORD), ("b", w.WORD), ("d", w.WORD), ("e", c.c_ubyte * 8)]


class InterfaceData(c.Structure):
    _fields_ = [("size", w.DWORD), ("guid", GUID), ("flags", w.DWORD),
                ("reserved", c.c_size_t)]


class Attributes(c.Structure):
    _fields_ = [("size", w.ULONG), ("vid", w.USHORT), ("pid", w.USHORT),
                ("release", w.USHORT)]


class Caps(c.Structure):
    _fields_ = [("usage", w.USHORT), ("usage_page", w.USHORT),
                ("input_length", w.USHORT), ("output_length", w.USHORT),
                ("feature_length", w.USHORT), ("reserved", w.USHORT * 17),
                ("counts", w.USHORT * 10)]


class Overlapped(c.Structure):
    _fields_ = [("internal", c.c_size_t), ("internal_high", c.c_size_t),
                ("offset", w.DWORD), ("offset_high", w.DWORD), ("event", w.HANDLE)]


class ReportCaps(c.Union):
    # HIDP_BUTTON_CAPS and HIDP_VALUE_CAPS both occupy 72 bytes, aligned to 4;
    # the shared ReportID field is byte 2. No other field is interpreted here.
    _fields_ = [("raw", c.c_ubyte * 72), ("alignment", c.c_uint32 * 18)]


class Win32HidTransport:
    def __init__(self):
        if os.name != "nt":
            raise RuntimeError("windows_required")
        # LOAD_LIBRARY_SEARCH_SYSTEM32 prevents local DLL substitution.
        self.k = c.WinDLL("kernel32.dll", use_last_error=True, winmode=0x800)
        self.s = c.WinDLL("setupapi.dll", use_last_error=True, winmode=0x800)
        self.h = c.WinDLL("hid.dll", use_last_error=True, winmode=0x800)
        self._bind()

    def _bind(self):
        def bind(dll, name, result, *args):
            fn = getattr(dll, name)
            fn.restype, fn.argtypes = result, args
        ptr = c.c_void_p
        bind(self.h, "HidD_GetHidGuid", None, ptr)
        bind(self.h, "HidD_GetAttributes", c.c_ubyte, w.HANDLE, ptr)
        bind(self.h, "HidD_GetPreparsedData", c.c_ubyte, w.HANDLE, ptr)
        bind(self.h, "HidD_FreePreparsedData", c.c_ubyte, ptr)
        bind(self.h, "HidP_GetCaps", c.c_long, ptr, ptr)
        bind(self.h, "HidP_GetButtonCaps", c.c_long, c.c_int, ptr, ptr, ptr)
        bind(self.h, "HidP_GetValueCaps", c.c_long, c.c_int, ptr, ptr, ptr)
        bind(self.s, "SetupDiGetClassDevsW", w.HANDLE, ptr, w.LPCWSTR, w.HWND, w.DWORD)
        bind(self.s, "SetupDiEnumDeviceInterfaces", w.BOOL, w.HANDLE, ptr, ptr, w.DWORD, ptr)
        bind(self.s, "SetupDiGetDeviceInterfaceDetailW", w.BOOL,
             w.HANDLE, ptr, ptr, w.DWORD, ptr, ptr)
        bind(self.s, "SetupDiDestroyDeviceInfoList", w.BOOL, w.HANDLE)
        bind(self.k, "CreateFileW", w.HANDLE, w.LPCWSTR, w.DWORD, w.DWORD,
             ptr, w.DWORD, w.DWORD, w.HANDLE)
        bind(self.k, "CloseHandle", w.BOOL, w.HANDLE)
        bind(self.k, "CreateEventW", w.HANDLE, ptr, w.BOOL, w.BOOL, w.LPCWSTR)
        bind(self.k, "WriteFile", w.BOOL, w.HANDLE, ptr, w.DWORD, ptr, ptr)
        bind(self.k, "ReadFile", w.BOOL, w.HANDLE, ptr, w.DWORD, ptr, ptr)
        bind(self.k, "WaitForSingleObject", w.DWORD, w.HANDLE, w.DWORD)
        bind(self.k, "CancelIoEx", w.BOOL, w.HANDLE, ptr)
        bind(self.k, "GetOverlappedResult", w.BOOL, w.HANDLE, ptr, ptr, w.BOOL)

    def _open(self, path, write=False, read=False):
        access = (0x40000000 if write else 0) | (0x80000000 if read else 0)
        handle = self.k.CreateFileW(path, access,
                                    3, None, 3, 0x40000000 if access else 0, None)
        if handle == c.c_void_p(-1).value or handle is None:
            raise c.WinError(c.get_last_error())
        return handle

    def _describe(self, handle, path):
        attributes = Attributes()
        attributes.size = c.sizeof(attributes)
        if not self.h.HidD_GetAttributes(handle, c.byref(attributes)):
            raise c.WinError(c.get_last_error())
        preparsed = c.c_void_p()
        if not self.h.HidD_GetPreparsedData(handle, c.byref(preparsed)):
            raise c.WinError(c.get_last_error())
        try:
            caps = Caps()
            if self.h.HidP_GetCaps(preparsed, c.byref(caps)) != 0x00110000:
                raise OSError("hid_caps_unavailable")
            return Device(path, attributes.vid, attributes.pid,
                          caps.usage_page, caps.usage, attributes.release,
                          caps.output_length, caps.input_length,
                          self._report_ids(preparsed, caps, 0),
                          self._report_ids(preparsed, caps, 1))
        finally:
            self.h.HidD_FreePreparsedData(preparsed)

    def _report_ids(self, preparsed, caps, report_type):
        ids = set()
        base = 1 if report_type == 0 else 4
        for delta, name in ((0, "HidP_GetButtonCaps"), (1, "HidP_GetValueCaps")):
            count = caps.counts[base + delta]
            if not count:
                continue
            if count > 1024:
                raise OSError("excessive_report_caps")
            items = (ReportCaps * count)()
            actual = w.USHORT(count)
            if getattr(self.h, name)(report_type, items, c.byref(actual), preparsed) != 0x00110000:
                raise OSError("report_ids_unavailable")
            if actual.value > count:
                raise OSError("invalid_report_caps_count")
            ids.update(items[i].raw[2] for i in range(actual.value))
        return tuple(sorted(ids))

    def enumerate(self):
        guid = GUID()
        self.h.HidD_GetHidGuid(c.byref(guid))
        info = self.s.SetupDiGetClassDevsW(c.byref(guid), None, None, 0x12)
        if info == c.c_void_p(-1).value or info is None:
            raise c.WinError(c.get_last_error())
        devices = []
        try:
            index = 0
            while True:
                entry = InterfaceData()
                entry.size = c.sizeof(entry)
                if not self.s.SetupDiEnumDeviceInterfaces(info, None, c.byref(guid),
                                                          index, c.byref(entry)):
                    error = c.get_last_error()
                    if error == 259:
                        break
                    raise c.WinError(error)
                index += 1
                required = w.DWORD()
                self.s.SetupDiGetDeviceInterfaceDetailW(info, c.byref(entry), None,
                                                        0, c.byref(required), None)
                if c.get_last_error() != 122 or not 8 <= required.value <= 65536:
                    raise OSError("invalid_interface_detail_size")
                buffer = c.create_string_buffer(required.value)
                c.cast(buffer, c.POINTER(w.DWORD))[0] = 8 if c.sizeof(c.c_void_p) == 8 else 6
                if not self.s.SetupDiGetDeviceInterfaceDetailW(
                        info, c.byref(entry), buffer, required.value, None, None):
                    raise c.WinError(c.get_last_error())
                # DevicePath starts at offset 4 even when cbSize is 8 on x64.
                path = c.wstring_at(c.addressof(buffer) + 4,
                                    (required.value - 4) // 2).split("\0", 1)[0]
                try:
                    handle = self._open(path)
                    try:
                        devices.append(self._describe(handle, path))
                    finally:
                        self.k.CloseHandle(handle)
                except OSError:
                    # Busy, restricted and disconnected collections are not writable candidates.
                    continue
            return devices
        finally:
            self.s.SetupDiDestroyDeviceInfoList(info)

    def write(self, device, report):
        family, reason = classify(device)
        if family != "msi_claw" or reason or len(report) != 64 or report[0] != 0x0F:
            raise OSError("write_policy_rejected")
        handle = self._open(device.path, write=True)
        try:
            if self._describe(handle, device.path) != device:
                raise OSError("device_identity_changed")
            return self._transfer(handle, report)
        finally:
            self.k.CloseHandle(handle)

    def _transfer(self, handle, report=None, timeout_ms=1000):
        reading = report is None
        action = "read" if reading else "write"
        length = 64 if reading else len(report)
        event = None
        try:
            event = self.k.CreateEventW(None, True, False, None)
            if not event:
                raise c.WinError(c.get_last_error())
            overlapped = Overlapped()
            overlapped.event = event
            buffer = c.create_string_buffer(length) if reading else c.create_string_buffer(report, length)
            transferred = w.DWORD()
            operation = self.k.ReadFile if reading else self.k.WriteFile
            started = operation(handle, buffer, length, None, c.byref(overlapped))
            if not started and c.get_last_error() != 997:
                raise c.WinError(c.get_last_error())
            if not started:
                status = self.k.WaitForSingleObject(event, timeout_ms)
                if status != 0:
                    self.k.CancelIoEx(handle, c.byref(overlapped))
                    # Buffers must outlive cancelled I/O until the OS acknowledges completion.
                    self.k.GetOverlappedResult(handle, c.byref(overlapped),
                                              c.byref(transferred), True)
                    if status == 258:
                        raise TimeoutError(action + "_timeout")
                    raise OSError(action + "_wait_failed")
            if not self.k.GetOverlappedResult(handle, c.byref(overlapped),
                                             c.byref(transferred), False):
                raise c.WinError(c.get_last_error())
            if transferred.value > length:
                raise OSError("invalid_transfer_length")
            return buffer.raw[:transferred.value] if reading else transferred.value
        finally:
            if event:
                self.k.CloseHandle(event)

    def open_channel(self, device, *, user_requested=False):
        family, reason = classify(device)
        if (user_requested is not True or family != "msi_claw" or reason
                or device.input_length != 64 or device.output_length != 64
                or device.input_report_ids != (0x10,) or device.output_report_ids != (0x0F,)):
            raise OSError("duplex_policy_rejected")
        handle = self._open(device.path, write=True, read=True)
        try:
            if self._describe(handle, device.path) != device:
                raise OSError("device_identity_changed")
            return _Win32Channel(self, handle, device)
        except BaseException:
            self.k.CloseHandle(handle)
            raise

    def close(self):
        pass


class _Win32Channel:
    def __init__(self, transport, handle, device):
        self._transport = transport
        self._handle = handle
        self._device = device
        self._lock = threading.Lock()
        self._identity = device.id + ":" + str(device.release) + ":" + uuid.uuid4().hex

    @property
    def identity(self):
        return self._identity if self._handle is not None else "closed"

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        self.close()

    def send(self, report):
        from .msi_ram import frame
        if not isinstance(report, bytes) or len(report) != 64:
            raise ValueError("invalid_report")
        offset, length = int.from_bytes(report[6:8], "big"), report[8]
        payload = length if report[4] == 4 else report[9:9 + length]
        if report != frame(report[4], offset, payload):
            raise ValueError("noncanonical_ram_report")
        with self._lock:
            if self._handle is None:
                raise OSError("channel_closed")
            family, reason = classify(self._device)
            if family != "msi_claw" or reason:
                raise OSError("duplex_policy_rejected")
            return self._transport._transfer(self._handle, report)

    def read(self, timeout):
        if not isinstance(timeout, (float, int)) or not math.isfinite(timeout) or timeout <= 0:
            raise ValueError("invalid_timeout")
        with self._lock:
            if self._handle is None:
                raise OSError("channel_closed")
            return self._transport._transfer(self._handle, timeout_ms=min(3000, max(1, math.ceil(timeout * 1000))))

    def close(self):
        with self._lock:
            if self._handle is not None:
                self._transport.k.CloseHandle(self._handle)
                self._handle = None

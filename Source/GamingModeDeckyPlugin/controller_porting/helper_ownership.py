"""Host ownership and read-only Windows device ancestry; no device writes."""
import ctypes as C
import hashlib
import os
import re


class OwnershipError(RuntimeError):
    pass


class WindowsLease:
    def __init__(self, identity):
        self.name = "Local\\Playhub.Controller." + hashlib.sha256(identity.encode()).hexdigest()
        self.handle = None

    def acquire(self):
        if os.name != "nt" or self.handle:
            raise OwnershipError("windows_lease_unavailable")
        self.api = C.WinDLL("kernel32", use_last_error=True)
        self.api.CreateMutexW.argtypes = [C.c_void_p, C.c_bool, C.c_wchar_p]
        self.api.CreateMutexW.restype = C.c_void_p
        self.api.WaitForSingleObject.argtypes = [C.c_void_p, C.c_uint32]
        self.api.WaitForSingleObject.restype = C.c_uint32
        self.api.ReleaseMutex.argtypes = [C.c_void_p]
        self.api.ReleaseMutex.restype = C.c_bool
        self.api.CloseHandle.argtypes = [C.c_void_p]
        self.api.CloseHandle.restype = C.c_bool
        handle = self.api.CreateMutexW(None, False, self.name)
        if not handle:
            raise OwnershipError("lease_creation_failed")
        wait = self.api.WaitForSingleObject(handle, 0)
        if wait not in (0, 0x80):
            self.api.CloseHandle(handle)
            raise OwnershipError("source_owned_elsewhere")
        # Abandoned ownership means the old pipeline may have left hardware state.
        if wait == 0x80:
            self.api.ReleaseMutex(handle)
            self.api.CloseHandle(handle)
            raise OwnershipError("abandoned_source_requires_recovery")
        self.handle = handle

    def valid(self):
        return self.handle is not None

    def close(self):
        if self.handle:
            handle, self.handle = self.handle, None
            try:
                if not self.api.ReleaseMutex(handle):
                    raise OwnershipError("lease_release_failed")
            finally:
                self.api.CloseHandle(handle)


def physical_identity(ancestors):
    if not isinstance(ancestors, (list, tuple)) or not 1 <= len(ancestors) <= 32:
        raise OwnershipError("ancestry_missing")
    normalized = []
    for item in ancestors:
        if not isinstance(item, str) or not item or len(item) > 4096:
            raise OwnershipError("ancestry_invalid")
        normalized.append(item.upper())
    if any(any(word in item for word in ("VIGEM", "USBIP", "VHCI", "VIRTUAL", "VJOY")) for item in normalized):
        raise OwnershipError("virtual_source_rejected")
    if not any(item.startswith(("USB\\", "BTHENUM\\", "BTHLEDEVICE\\")) for item in normalized):
        raise OwnershipError("physical_transport_unverified")
    return "pnp:" + hashlib.sha256("\0".join(normalized).encode()).hexdigest()


class WindowsAncestry:
    def __init__(self):
        self.api = None

    def __call__(self, sdl_path, instance):
        # SDL path is implementation-dependent: unsupported forms fail closed.
        if not isinstance(sdl_path, bytes):
            raise OwnershipError("device_path_unavailable")
        path = sdl_path.decode("utf-8", "strict")
        match = re.fullmatch(r"\\\\\?\\([^#]+)#([^#]+)#([^#]+)#\{[0-9a-fA-F-]{36}\}", path)
        if not match:
            raise OwnershipError("device_interface_path_unrecognized")
        device_id = "\\".join(match.groups())
        if self.api is None:
            if os.name != "nt":
                raise OwnershipError("windows_required")
            self.api = C.WinDLL("cfgmgr32", use_last_error=True)
            for name, args in (
                ("CM_Locate_DevNodeW", [C.POINTER(C.c_uint32), C.c_wchar_p, C.c_uint32]),
                ("CM_Get_Parent", [C.POINTER(C.c_uint32), C.c_uint32, C.c_uint32]),
                ("CM_Get_Device_IDW", [C.c_uint32, C.c_wchar_p, C.c_uint32, C.c_uint32]),
                ("CM_Get_DevNode_Registry_PropertyW", [C.c_uint32, C.c_uint32,
                    C.POINTER(C.c_uint32), C.c_void_p, C.POINTER(C.c_uint32), C.c_uint32]),
            ):
                fn = getattr(self.api, name)
                fn.argtypes, fn.restype = args, C.c_uint32
        node = C.c_uint32()
        if self.api.CM_Locate_DevNodeW(C.byref(node), device_id, 0):
            raise OwnershipError("device_not_present")
        ancestors, seen = [], set()
        for _ in range(32):
            if node.value in seen:
                raise OwnershipError("ancestry_cycle")
            seen.add(node.value)
            buffer = C.create_unicode_buffer(4096)
            if self.api.CM_Get_Device_IDW(node.value, buffer, len(buffer), 0):
                raise OwnershipError("ancestry_read_failed")
            ancestors.append(buffer.value)
            # A ViGEm bus can be ROOT\SYSTEM\000x: the instance name alone is insufficient.
            service = C.create_string_buffer(4096)
            size, kind = C.c_uint32(len(service)), C.c_uint32()
            result = self.api.CM_Get_DevNode_Registry_PropertyW(
                node.value, 5, C.byref(kind), service, C.byref(size), 0)
            if result == 0:
                if kind.value != 1 or not 2 <= size.value <= len(service):
                    raise OwnershipError("ancestry_service_invalid")
                name = service.raw[:size.value].decode("utf-16-le", "strict")
                if not name.endswith("\0"):
                    raise OwnershipError("ancestry_service_invalid")
                if any(word in name.upper() for word in ("VIGEM", "USBIP", "VHCI", "VJOY")):
                    raise OwnershipError("virtual_source_rejected")
            elif result != 0x25:
                raise OwnershipError("ancestry_service_unverified")
            parent = C.c_uint32()
            result = self.api.CM_Get_Parent(C.byref(parent), node.value, 0)
            if result == 0x0D:  # CR_NO_SUCH_DEVNODE: root has no parent.
                if not buffer.value.upper().startswith("HTREE\\ROOT\\"):
                    raise OwnershipError("ancestry_root_unverified")
                return physical_identity(ancestors)
            if result:
                raise OwnershipError("ancestry_parent_failed")
            node = parent
        raise OwnershipError("ancestry_too_deep")

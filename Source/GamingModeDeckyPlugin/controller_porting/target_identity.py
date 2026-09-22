"""Read-only CM correlation of an owned ViGEm PDO, not a VID/PID search."""
import ctypes as C
from dataclasses import dataclass
import os
import re


class TargetIdentityError(RuntimeError):
    pass


@dataclass(frozen=True)
class Node:
    instance: str
    parent: str
    hardware_ids: tuple
    address: int | None
    ui_number: int | None


class WindowsTargetRegistry:
    """Present direct children of a host-approved bus instance. No registry writes."""
    def __init__(self, bus_instance):
        if not isinstance(bus_instance, str) or not bus_instance or len(bus_instance) > 4096:
            raise TargetIdentityError("approved_bus_instance_required")
        self.bus_instance = bus_instance.upper()
        self.api = None

    def _load(self):
        if self.api is not None:
            return
        if os.name != "nt":
            raise TargetIdentityError("windows_required")
        self.api = C.WinDLL("cfgmgr32", use_last_error=True)
        u, p = C.c_uint32, C.POINTER(C.c_uint32)
        declarations = {
            "CM_Locate_DevNodeW": [p, C.c_wchar_p, u],
            "CM_Get_Device_IDW": [u, C.c_wchar_p, u, u],
            "CM_Get_Child": [p, u, u], "CM_Get_Sibling": [p, u, u],
            "CM_Get_Parent": [p, u, u],
            "CM_Get_DevNode_Registry_PropertyW": [u, u, p, C.c_void_p, p, u],
        }
        for name, args in declarations.items():
            fn = getattr(self.api, name)
            fn.argtypes, fn.restype = args, u

    def _id(self, node):
        value = C.create_unicode_buffer(4096)
        if self.api.CM_Get_Device_IDW(node, value, len(value), 0):
            raise TargetIdentityError("device_instance_read_failed")
        return value.value.upper()

    def _property(self, node, key, expected_type):
        data = C.create_string_buffer(16384)
        size, kind = C.c_uint32(len(data)), C.c_uint32()
        result = self.api.CM_Get_DevNode_Registry_PropertyW(
            node, key, C.byref(kind), data, C.byref(size), 0)
        if result == 0x25:  # CR_NO_SUCH_VALUE
            return None
        if result or kind.value != expected_type or size.value > len(data):
            raise TargetIdentityError("device_property_invalid")
        raw = data.raw[:size.value]
        if expected_type == 4:
            if len(raw) != 4:
                raise TargetIdentityError("device_dword_invalid")
            return int.from_bytes(raw, "little")
        text = raw.decode("utf-16-le", "strict")
        if not text.endswith("\0\0" if expected_type == 7 else "\0"):
            raise TargetIdentityError("device_string_invalid")
        return tuple(text.rstrip("\0").upper().split("\0")) if expected_type == 7 else text.rstrip("\0").upper()

    def __call__(self):
        self._load()
        bus = C.c_uint32()
        if self.api.CM_Locate_DevNodeW(C.byref(bus), self.bus_instance, 0):
            raise TargetIdentityError("approved_bus_not_present")
        if self._id(bus.value) != self.bus_instance or self._property(bus.value, 5, 1) != "VIGEMBUS":
            raise TargetIdentityError("approved_bus_changed")
        children, seen, child = [], set(), C.c_uint32()
        result = self.api.CM_Get_Child(C.byref(child), bus.value, 0)
        while result == 0:
            if child.value in seen or len(seen) >= 256:
                raise TargetIdentityError("device_tree_invalid")
            seen.add(child.value)
            parent = C.c_uint32()
            if self.api.CM_Get_Parent(C.byref(parent), child.value, 0) or parent.value != bus.value:
                raise TargetIdentityError("target_parent_changed")
            children.append(Node(self._id(child.value), self._id(parent.value),
                                 self._property(child.value, 2, 7) or (),
                                 self._property(child.value, 0x1D, 4),
                                 self._property(child.value, 0x11, 4)))
            sibling = C.c_uint32()
            result = self.api.CM_Get_Sibling(C.byref(sibling), child.value, 0)
            child = sibling
        if result != 0x0D:
            raise TargetIdentityError("device_enumeration_failed")
        return tuple(children)


class NativeTargetProperties:
    def __init__(self, dll):
        self.dll = dll
        for name, result in (("get_index", C.c_uint32), ("get_vid", C.c_uint16),
                             ("get_pid", C.c_uint16), ("is_attached", C.c_int)):
            fn = getattr(dll, "vigem_target_" + name)
            fn.argtypes, fn.restype = [C.c_void_p], result

    def __call__(self, target):
        d = self.dll
        if not d.vigem_target_is_attached(target):
            raise TargetIdentityError("native_target_not_attached")
        return (d.vigem_target_get_index(target), d.vigem_target_get_vid(target),
                d.vigem_target_get_pid(target))


class OwnedTargetVerifier:
    """Construct before target_add; bind one new PDO and never adopt a replacement.

    The SDK serial is correlated with driver Address/UINumber, not XInput user index.
    Full device instance path stays pinned after the first successful enumeration.
    This is enumeration evidence, not proof of end-application compatibility.
    """
    def __init__(self, snapshot, native_properties, bus_instance):
        self.snapshot, self.properties = snapshot, native_properties
        self.bus = bus_instance.upper()
        self.baseline = {n.instance.upper() for n in snapshot()}
        self.bound = None
        self.native = None
        self.handles = None

    def __call__(self, client, target):
        try:
            properties = self.properties(target)
            serial, vid, pid = properties
            if any(type(v) is not int for v in properties) or not 0 < serial <= 0xFFFFFFFF:
                return False
            if not 0 < vid <= 65535 or not 0 < pid <= 65535:
                return False
            pattern = re.compile(r"^USB\\VID_%04X&PID_%04X(?:&|$)" % (vid, pid))
            matches = [n for n in self.snapshot()
                       if n.parent.upper() == self.bus and n.address == serial
                       and n.ui_number == serial and n.instance.upper() not in self.baseline
                       and any(pattern.match(h.upper()) for h in n.hardware_ids)]
            if len(matches) != 1 or properties != self.properties(target):
                return False
            node = matches[0]
            if self.bound is not None:
                return node == self.bound and properties == self.native and self.handles == (client, target)
            self.bound, self.native, self.handles = node, properties, (client, target)
            return True
        except (TargetIdentityError, OSError, ValueError, TypeError):
            return False

    def absent(self):
        # No successful bind means cleanup cannot be certified by this verifier.
        if self.bound is None:
            return False
        return all(n.instance.upper() != self.bound.instance.upper() for n in self.snapshot())

"""SDL3 public ABI adapter. No HC code, HID encoder, driver or background worker."""

import ctypes as C
import hashlib
import os
from pathlib import Path
import threading
import time
import uuid


class Unavailable(RuntimeError):
    pass


# Public SDL_GamepadButton values, not physical report offsets.
BUTTONS = {"A": 0, "B": 1, "X": 2, "Y": 3, "Back": 4, "Start": 6,
           "LS": 7, "RS": 8, "LB": 9, "RB": 10,
           "Up": 11, "Down": 12, "Left": 13, "Right": 14}
INIT_GAMEPAD = 0x2000
MIN_VERSION = 3004010


def inspect_library(path):
    """File-only observation. Does not load the DLL or claim hardware support."""
    p = Path(path)
    if not p.is_absolute() or not p.is_file():
        raise Unavailable("absolute_library_file_required")
    with p.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    return {"path": str(p.resolve()), "sha256": digest,
            "hardwareValidated": False, "liveMapping": False,
            "virtualOutput": False, "reason": "library_not_loaded"}


class NativeSDL:
    """Explicit native load only. Caller must approve the exact binary hash."""

    def __init__(self, path, sha256, *, authorize_device_io=False):
        if authorize_device_io is not True:
            raise Unavailable("device_io_not_authorized")
        if os.name != "nt":
            raise Unavailable("windows_required")
        observation = inspect_library(path)
        if not isinstance(sha256, str) or observation["sha256"] != sha256.lower():
            raise Unavailable("library_hash_mismatch")
        # SDL uses cdecl. Restrict dependency resolution to DLL directory/System32.
        self.library = C.CDLL(observation["path"], winmode=0x1100)
        self.provenance = {**observation, "kind": "native"}
        signatures = {
            "SDL_GetVersion": (C.c_int, []),
            "SDL_GetError": (C.c_char_p, []),
            "SDL_ClearError": (C.c_bool, []),
            "SDL_InitSubSystem": (C.c_bool, [C.c_uint32]),
            "SDL_QuitSubSystem": (None, [C.c_uint32]),
            "SDL_UpdateGamepads": (None, []),
            "SDL_GetGamepads": (C.POINTER(C.c_uint32), [C.POINTER(C.c_int)]),
            "SDL_free": (None, [C.c_void_p]),
            "SDL_OpenGamepad": (C.c_void_p, [C.c_uint32]),
            "SDL_CloseGamepad": (None, [C.c_void_p]),
            "SDL_GamepadConnected": (C.c_bool, [C.c_void_p]),
            "SDL_GetGamepadID": (C.c_uint32, [C.c_void_p]),
            "SDL_GetGamepadVendor": (C.c_uint16, [C.c_void_p]),
            "SDL_GetGamepadProduct": (C.c_uint16, [C.c_void_p]),
            "SDL_GamepadHasButton": (C.c_bool, [C.c_void_p, C.c_int]),
            "SDL_GamepadHasAxis": (C.c_bool, [C.c_void_p, C.c_int]),
            "SDL_GetGamepadButton": (C.c_bool, [C.c_void_p, C.c_int]),
            "SDL_GetGamepadAxis": (C.c_int16, [C.c_void_p, C.c_int]),
            "SDL_GetGamepadProperties": (C.c_uint32, [C.c_void_p]),
            "SDL_GetBooleanProperty": (C.c_bool, [C.c_uint32, C.c_char_p, C.c_bool]),
            "SDL_RumbleGamepad": (C.c_bool, [C.c_void_p, C.c_uint16, C.c_uint16, C.c_uint32]),
        }
        for name, (result, args) in signatures.items():
            fn = getattr(self.library, name)
            fn.restype, fn.argtypes = result, args
            setattr(self, name, fn)


class ControllerSession:
    """One ephemeral SDL instance, owned references, no automatic reconnect.

    All operations belong on the host's SDL/main thread. start/open/update may
    cause SDL HID writes; these are NEVER passive capability probes.
    """

    def __init__(self, api):
        self.api = api
        self.started = False
        self.handle = None
        self.instance = None
        self.token = None
        self.version = None
        self.rumbling = False

    def _thread(self):
        if threading.current_thread() is not threading.main_thread():
            raise Unavailable("sdl_main_thread_required")

    def _error(self, reason):
        detail = self.api.SDL_GetError()
        return Unavailable(reason + ": " + (detail or b"").decode("utf-8", "replace")[:500])

    def start(self, *, authorize_device_io=False):
        self._thread()
        if authorize_device_io is not True:
            raise Unavailable("device_io_not_authorized")
        if self.started:
            raise Unavailable("already_started")
        version = self.api.SDL_GetVersion()
        if not MIN_VERSION <= version < 4000000:
            raise Unavailable("sdl_3_4_10_or_newer_required")
        if not self.api.SDL_InitSubSystem(INIT_GAMEPAD):
            raise self._error("initialization_failed")
        self.version, self.started = version, True

    def instances(self):
        self._thread()
        if not self.started:
            raise Unavailable("not_started")
        self.api.SDL_UpdateGamepads()
        count = C.c_int()
        items = self.api.SDL_GetGamepads(C.byref(count))
        if not items:
            raise self._error("enumeration_failed")
        try:
            if not 0 <= count.value <= 256:
                raise Unavailable("invalid_device_count")
            return [int(items[i]) for i in range(count.value)]
        finally:
            self.api.SDL_free(items)

    def open(self, instance):
        self._thread()
        if self.handle:
            raise Unavailable("close_current_session_first")
        if type(instance) is not int or not 0 < instance <= 0xffffffff:
            raise ValueError("invalid_instance")
        if instance not in self.instances():
            raise Unavailable("instance_missing")
        handle = self.api.SDL_OpenGamepad(instance)
        if not handle:
            raise self._error("open_failed")
        self.handle, self.instance = handle, instance
        self.token = uuid.uuid4().hex
        try:
            return self.capabilities(self.token)
        except Exception:
            self.close_device()
            raise

    def _checked(self, token):
        self._thread()
        if not self.handle or token != self.token:
            raise Unavailable("stale_session")
        self.api.SDL_UpdateGamepads()
        if (not self.api.SDL_GamepadConnected(self.handle)
                or self.api.SDL_GetGamepadID(self.handle) != self.instance):
            self.close_device()
            raise Unavailable("disconnected_or_changed")
        return self.handle

    def capabilities(self, token):
        handle = self._checked(token)
        props = self.api.SDL_GetGamepadProperties(handle)
        if not props:
            raise self._error("properties_failed")
        return {
            "schemaVersion": 1, "session": token, "instance": self.instance,
            "observedMonotonicNs": time.monotonic_ns(), "sdlVersion": self.version,
            "backendKind": self.api.provenance["kind"],
            "hardware": "%04x:%04x" % (self.api.SDL_GetGamepadVendor(handle),
                                        self.api.SDL_GetGamepadProduct(handle)),
            "buttons": [name for name, value in BUTTONS.items()
                        if self.api.SDL_GamepadHasButton(handle, value)],
            "axes": [i for i in range(6) if self.api.SDL_GamepadHasAxis(handle, i)],
            "rumbleReported": bool(self.api.SDL_GetBooleanProperty(
                props, b"SDL.gamepad.cap.rumble", False)),
            "hardwareValidated": False, "liveMapping": False,
            "virtualOutput": False, "gyro": False, "trackpads": False,
            "exclusiveMode": False, "reason": "hardware_validation_pending",
        }

    def read(self, token):
        caps = self.capabilities(token)
        handle = self.handle
        try:
            # SDL getter failure can look like a centered axis or released button.
            self.api.SDL_ClearError()
            axes = {}
            for axis in caps["axes"]:
                value = self.api.SDL_GetGamepadAxis(handle, axis)
                minimum = 0 if axis >= 4 else -32768
                if type(value) is not int or not minimum <= value <= 32767:
                    raise Unavailable("invalid_axis_value")
                axes[str(axis)] = value / (32768 if value < 0 else 32767)
            buttons = [name for name in caps["buttons"]
                       if self.api.SDL_GetGamepadButton(handle, BUTTONS[name])]
            if self.api.SDL_GetError():
                raise self._error("input_read_failed")
            if (not self.api.SDL_GamepadConnected(handle)
                    or self.api.SDL_GetGamepadID(handle) != self.instance):
                raise Unavailable("disconnected_during_read")
        except Exception:
            self.close_device()
            raise
        return {"schemaVersion": 1, "session": token, "buttons": buttons,
                "axes": axes, "observedMonotonicNs": time.monotonic_ns(),
                "backendKind": caps["backendKind"]}

    def rumble(self, token, low, high, duration_ms, *, authorize_output=False):
        if authorize_output is not True:
            raise Unavailable("output_not_authorized")
        for value, maximum in [(low, 65535), (high, 65535), (duration_ms, 250)]:
            if type(value) is not int or not 0 <= value <= maximum:
                raise ValueError("invalid_rumble_parameter")
        if duration_ms == 0 and (low or high):
            raise ValueError("nonzero_rumble_requires_duration")
        if not self.capabilities(token)["rumbleReported"]:
            raise Unavailable("rumble_unsupported")
        # A failed native call may still have partially reached a device.
        self.rumbling = True
        if not self.api.SDL_RumbleGamepad(self.handle, low, high, duration_ms):
            error = self._error("rumble_failed")
            self.close_device()
            raise error
        self.rumbling = bool(low or high)

    def close_device(self):
        self._thread()
        handle, rumbling = self.handle, self.rumbling
        self.handle = self.instance = self.token = None
        self.rumbling = False
        if handle:
            try:
                if rumbling:
                    self.api.SDL_RumbleGamepad(handle, 0, 0, 0)
            finally:
                self.api.SDL_CloseGamepad(handle)

    def close(self):
        self._thread()
        try:
            self.close_device()
        finally:
            if self.started:
                self.started = False
                self.api.SDL_QuitSubSystem(INIT_GAMEPAD)

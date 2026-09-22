"""Explicit executable test host fixtures. NEVER used by native factory."""
import ctypes as C
import time
from unittest.mock import Mock

from .helper_host import Pipeline
from .output_vigem import OutputLease, SUCCESS, XUSBReport, DS4Report
from .reader_sdl import Reader, SensorEvent


class MockLease:
    def __init__(self, identity):
        self.held = False
    def acquire(self):
        self.held = True
    def valid(self):
        return self.held
    def close(self):
        self.held = False


class MockOutput:
    def __init__(self):
        self.calls = []
    def __getattr__(self, name):
        def call(*args):
            payload = bytes(args[-1]) if args and isinstance(args[-1], (XUSBReport, DS4Report)) else None
            self.calls.append((name, payload))
            return 11 if name == "vigem_alloc" else 22 if name.endswith(("x360_alloc", "ds4_alloc")) else SUCCESS
        return call


def pipeline():
    api = Mock()
    api.provenance = {"kind": "mock"}
    api.SDL_GetVersion.return_value = 3004010
    api.SDL_GetError.return_value = b""
    api.SDL_InitSubSystem.return_value = True
    api.SDL_OpenGamepad.return_value = 99
    api.SDL_GamepadConnected.return_value = True
    api.SDL_GetGamepadID.return_value = 42
    api.SDL_GetGamepadVendor.return_value = 0x1234
    api.SDL_GetGamepadProduct.return_value = 1
    api.SDL_GetGamepadProperties.return_value = 1
    api.SDL_GetBooleanProperty.return_value = False
    api.SDL_GamepadHasButton.return_value = True
    api.SDL_GamepadHasAxis.return_value = True
    api.SDL_GetGamepadButton.side_effect = lambda h, b: b == 0
    api.SDL_GetGamepadAxis.side_effect = lambda h, a: [0, -32768, 0, 0, 0, 32767][a]
    api.SDL_GetGamepadPath.return_value = b"mock-physical-only"
    api.SDL_GamepadHasSensor.return_value = True
    api.SDL_GamepadSensorEnabled.return_value = False
    api.SDL_SetGamepadSensorEnabled.return_value = True
    def enumerate_ids(count):
        C.cast(count, C.POINTER(C.c_int))[0] = 1
        return (C.c_uint32 * 1)(42)
    api.SDL_GetGamepads.side_effect = enumerate_ids
    def events():
        output = []
        now = time.monotonic_ns()
        for sensor, vector in ((1, (0, 9.80665, 0)), (2, (0, 0, 0))):
            event = SensorEvent()
            event.type, event.which, event.sensor, event.sensor_timestamp = 0x659, 42, sensor, now
            event.data[:] = vector
            output.append(event)
        return output
    api.sensor_events.side_effect = events
    backend = MockOutput()
    identity = "explicit-mock-device"
    def make_reader(lease):
        return Reader(api, lambda path, instance: identity if lease.valid() and path == b"mock-physical-only" else None)
    def make_output(kind, reader, lease):
        return OutputLease(backend,
            lambda session, source, target: lease.valid() and reader.reader.token == session and reader.identity == source,
            lambda client, target: client == 11 and target == 22, kind=kind)
    result = Pipeline(make_reader, make_output, MockLease, instance=42,
                      identity=identity, test_mode=True, dsu_port=0)
    result.mock_api, result.mock_output = api, backend
    return result

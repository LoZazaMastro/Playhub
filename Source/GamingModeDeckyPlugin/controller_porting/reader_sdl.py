"""SDL transport only: standard input and optional raw sensor events, no mapper.

Call from the main thread of an exclusively owned SDL host, never a Decky pool.
Constructors do not start SDL. Native loading and device opening need approval.
"""
import ctypes as C
import math
import threading
import time

from .sdl_backend import ControllerSession, NativeSDL, Unavailable

SENSOR_EVENT = 0x659
MAX_AGE_NS = 100_000_000
_OWNER = threading.Lock()


class SensorEvent(C.Structure):
    _fields_ = [("type", C.c_uint32), ("reserved", C.c_uint32),
                ("timestamp", C.c_uint64), ("which", C.c_uint32),
                ("sensor", C.c_int32), ("data", C.c_float * 3),
                ("sensor_timestamp", C.c_uint64)]


class Event(C.Union):
    _fields_ = [("sensor", SensorEvent), ("padding", C.c_uint8 * 128)]


class NativeReader(NativeSDL):
    """Explicit loading of a host-audited existing runtime, not an installer."""
    def __init__(self, approval, *, authorize_device_io=False):
        if (not isinstance(approval, dict) or approval.get("licenseApproved") is not True
                or approval.get("coexistenceApproved") is not True
                or not approval.get("licenseEvidence")):
            raise Unavailable("runtime_audit_required")
        super().__init__(approval["path"], approval["sha256"],
                         authorize_device_io=authorize_device_io)
        for name, result, args in (
            ("SDL_GetGamepadPath", C.c_char_p, [C.c_void_p]),
            ("SDL_GamepadHasSensor", C.c_bool, [C.c_void_p, C.c_int]),
            ("SDL_GamepadSensorEnabled", C.c_bool, [C.c_void_p, C.c_int]),
            ("SDL_SetGamepadSensorEnabled", C.c_bool, [C.c_void_p, C.c_int, C.c_bool]),
            ("SDL_PumpEvents", None, []),
            ("SDL_PeepEvents", C.c_int, [C.POINTER(Event), C.c_int, C.c_int, C.c_uint32, C.c_uint32]),
        ):
            fn = getattr(self.library, name)
            fn.restype, fn.argtypes = result, args
            setattr(self, name, fn)

    def sensor_events(self):
        self.SDL_PumpEvents()
        events = (Event * 256)()
        count = self.SDL_PeepEvents(events, 256, 2, SENSOR_EVENT, SENSOR_EVENT)
        if count < 0 or count >= 256:
            raise Unavailable("sensor_event_queue_failed_or_overloaded")
        return [events[i].sensor for i in range(count)]


class Reader:
    def __init__(self, api, verify_source, *, clock_ns=time.monotonic_ns):
        # verify_source is a trusted host callback, never a renderer boolean:
        # resolve path/instance ancestry, reject virtual outputs, verify host lease.
        self.api = api
        self.verify_source = verify_source
        self.clock_ns = clock_ns
        self.reader = ControllerSession(api)
        self.identity = None
        self.path = None
        self.sequence = 0
        self.owned = False
        self.changed_sensors = []
        self.sensors = {}
        self.motion = {}
        self.last_timestamp = {}

    def _verify(self):
        path = self.api.SDL_GetGamepadPath(self.reader.handle)
        if (not isinstance(path, bytes) or not path or len(path) > 32768
                or path != self.path
                or self.verify_source(path, self.reader.instance) != self.identity):
            raise Unavailable("physical_identity_or_owner_changed")

    def start(self, instance, identity, *, authorize_device_io=False, authorize_sensors=False):
        self.reader._thread()
        if authorize_device_io is not True:
            raise Unavailable("device_io_not_authorized")
        if not isinstance(identity, str) or not identity or len(identity) > 256:
            raise Unavailable("stable_identity_required")
        if self.owned or not _OWNER.acquire(blocking=False):
            raise Unavailable("reader_owner_busy")
        self.owned = True
        try:
            self.reader.start(authorize_device_io=True)
            caps = self.reader.open(instance)
            self.identity = identity
            self.path = self.api.SDL_GetGamepadPath(self.reader.handle)
            self._verify()
            self.sequence = 0
            self.motion.clear()
            self.last_timestamp.clear()
            for sensor, name in ((1, "accel"), (2, "gyro")):
                supported = bool(self.api.SDL_GamepadHasSensor(self.reader.handle, sensor))
                self.sensors[name] = supported and authorize_sensors is True
                if self.sensors[name] and not self.api.SDL_GamepadSensorEnabled(self.reader.handle, sensor):
                    # Record before calling: a failed setter may have mutated state.
                    self.changed_sensors.append(sensor)
                    if not self.api.SDL_SetGamepadSensorEnabled(self.reader.handle, sensor, True):
                        raise Unavailable("sensor_enable_failed")
            self.api.sensor_events()  # discard events preceding this session
            return {"schemaVersion": 1, "session": self.reader.token, "identity": identity,
                    "buttons": caps["buttons"], "axes": caps["axes"],
                    "sensors": dict(self.sensors), "sourceKind": self.api.provenance["kind"],
                    "liveMapping": False, "gyroAim": False, "trackpads": False}
        except BaseException:
            self.close()
            raise

    def _motion(self):
        now = self.clock_ns()
        for event in self.api.sensor_events():
            if event.which != self.reader.instance or event.sensor not in (1, 2):
                continue
            name = "accel" if event.sensor == 1 else "gyro"
            if not self.sensors.get(name):
                continue
            timestamp = int(event.sensor_timestamp)
            values = tuple(float(x) for x in event.data)
            if (timestamp <= self.last_timestamp.get(name, 0)
                    or not all(math.isfinite(x) for x in values)):
                self.motion.pop(name, None)
                continue
            self.last_timestamp[name] = timestamp
            self.motion[name] = {"value": values, "timestampNs": timestamp,
                                 "receivedMonotonicNs": now,
                                 "unit": "m/s^2" if name == "accel" else "rad/s"}
        return {name: dict(self.motion[name]) if name in self.motion
                and 0 <= now - self.motion[name]["receivedMonotonicNs"] <= MAX_AGE_NS else None
                for name in ("accel", "gyro")}

    def poll(self, session, identity):
        self.reader._thread()
        if not self.owned or session != self.reader.token or identity != self.identity:
            raise Unavailable("stale_session_or_identity")
        try:
            self._verify()
            frame = self.reader.read(session)
            motion = self._motion()
            self._verify()
            if (not self.api.SDL_GamepadConnected(self.reader.handle)
                    or self.api.SDL_GetGamepadID(self.reader.handle) != self.reader.instance):
                raise Unavailable("source_disconnected")
            axes = frame["axes"]
            def stick(x, y):
                return [axes[x], -axes[y]] if x in axes and y in axes else None
            self.sequence += 1
            return {"schemaVersion": 1, "session": session, "identity": identity,
                    "sequence": self.sequence, "observedMonotonicNs": self.clock_ns(),
                    "buttons": frame["buttons"],
                    "sticks": {"left": stick("0", "1"), "right": stick("2", "3")},
                    "triggers": {"left": axes.get("4"), "right": axes.get("5")},
                    "stickCoordinateFrame": "positive-y-up",
                    "motionCoordinateFrame": "SDL-right-up-toward-player",
                    "motion": motion, "sensorCapabilities": dict(self.sensors),
                    "sourceKind": "live" if self.api.provenance["kind"] == "native" else "mock"}
        except BaseException:
            self.close()
            raise

    def close(self):
        self.reader._thread()
        failures = []
        try:
            for sensor in reversed(self.changed_sensors):
                try:
                    if not self.reader.handle or not self.api.SDL_SetGamepadSensorEnabled(self.reader.handle, sensor, False):
                        failures.append(sensor)
                except Exception:
                    failures.append(sensor)
            self.changed_sensors.clear()
            self.reader.close()
        finally:
            self.identity = self.path = None
            self.motion.clear()
            self.sensors.clear()
            if self.owned:
                self.owned = False
                _OWNER.release()
        return {"ok": not failures, "sensorRestoreFailures": failures}


def transport_loop(reader, emit, cancelled, *, max_hz=100):
    """Run in the SDL host main thread; cancellation/consumer errors always close."""
    if type(max_hz) not in (int, float) or not math.isfinite(max_hz) or not 1 <= max_hz <= 100:
        raise ValueError("invalid_transport_rate")
    session, identity = reader.reader.token, reader.identity
    try:
        while not cancelled.is_set():
            emit(reader.poll(session, identity))
            cancelled.wait(1 / max_hz)
    finally:
        reader.close()

"""Pure SDL sensor-event to DSU bridge. No SDL import, device or socket access."""

import math
import hashlib
import time

from .dsu import MotionSample, SAMPLE_TTL_NS

SDL_FRAME = "SDL-right-up-toward-player"
CEMU_PARITY = "cemu-sdl-parity-v1"
MAX_PAIR_SKEW_NS = 50_000_000


def _sensor(value, unit):
    if not isinstance(value, dict) or value.get("unit") != unit:
        raise ValueError("missing_sensor_or_wrong_units")
    if value.get("reason") is not None or value.get("valid", True) is not True:
        raise ValueError("sensor_not_valid")
    vector = value.get("value")
    if not isinstance(vector, (list, tuple)) or len(vector) != 3:
        raise ValueError("invalid_sensor_vector")
    if any(type(v) not in (int, float) or not math.isfinite(v) or abs(v) > 1e6 for v in vector):
        raise ValueError("invalid_sensor_value")
    timestamp, received = value.get("timestampNs"), value.get("receivedMonotonicNs")
    if type(timestamp) is not int or not 1000 <= timestamp < 2 ** 64:
        raise ValueError("invalid_sensor_timestamp")
    if type(received) is not int or received <= 0:
        raise ValueError("invalid_sensor_observation")
    return tuple(vector), timestamp, received


def sample_from_sdl_events(*, session, accel, gyro, coordinate_frame, source_kind, mapping):
    """Require BOTH valid events and an explicit coordinate interpretation.

    Cemu parity follows comparison of its SDL and DSU input transformations,
    not HC signs. Source parity is not a physical-controller validation claim.
    The native accelerometer timestamp is preserved; host time is only freshness.
    """
    if coordinate_frame != SDL_FRAME or mapping != CEMU_PARITY:
        raise ValueError("coordinate_mapping_not_selected")
    acceleration, timestamp, accel_received = _sensor(accel, "m/s^2")
    rotation, _, gyro_received = _sensor(gyro, "rad/s")
    if abs(accel_received - gyro_received) > MAX_PAIR_SKEW_NS:
        raise ValueError("sensor_pair_too_far_apart")
    sample = MotionSample(session, timestamp // 1000, min(accel_received, gyro_received),
                          tuple(-v / 9.80665 for v in acceleration),
                          (math.degrees(rotation[0]), -math.degrees(rotation[1]), -math.degrees(rotation[2])),
                          source_kind)
    sample.validate()
    return sample


def bridge_capabilities():
    return {"sensorOnly": True, "mapping": CEMU_PARITY,
            "coordinateEvidence": "cemu-source-parity",
            "hardwareValidated": False, "emulatorValidated": False,
            "aim": False, "remapping": False, "profiles": False}


def sample_from_reader_frame(frame, *, mapping, now_ns=None):
    """Newton reader schema v1; no import or activation of the reader itself."""
    if not isinstance(frame, dict) or frame.get("schemaVersion") != 1:
        raise ValueError("invalid_reader_schema")
    session, identity = frame.get("session"), frame.get("identity")
    if not isinstance(session, str) or not 1 <= len(session) <= 256 or not isinstance(identity, str) or not 1 <= len(identity) <= 4096:
        raise ValueError("invalid_reader_identity")
    caps, motion = frame.get("sensorCapabilities"), frame.get("motion")
    if not isinstance(caps, dict) or caps.get("accel") is not True or caps.get("gyro") is not True or not isinstance(motion, dict):
        raise ValueError("both_sensors_required")
    now = time.monotonic_ns() if now_ns is None else now_ns
    for key, unit in (("accel", "m/s^2"), ("gyro", "rad/s")):
        _, _, received = _sensor(motion.get(key), unit)
        if not 0 <= now - received <= SAMPLE_TTL_NS:
            raise ValueError("stale_or_future_sensor")
    binding = hashlib.sha256((session + "\0" + identity).encode("utf-8")).hexdigest()
    return sample_from_sdl_events(session=binding, accel=motion.get("accel"), gyro=motion.get("gyro"),
                                  source_kind=frame.get("sourceKind"),
                                  coordinate_frame=frame.get("motionCoordinateFrame"), mapping=mapping)

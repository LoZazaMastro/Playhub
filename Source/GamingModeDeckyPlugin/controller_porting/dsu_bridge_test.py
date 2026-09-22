"""Basis and schema fixtures, never physical sensor validation."""

import math
import unittest

from controller_porting.dsu_bridge import sample_from_sdl_events, sample_from_reader_frame, bridge_capabilities, SDL_FRAME, CEMU_PARITY


def sensor(unit, value, timestamp=1_234_567):
    return {"unit": unit, "value": value, "timestampNs": timestamp,
            "receivedMonotonicNs": 2_000_000_000}


def convert(accel_values=(0., 0., 0.), gyro_values=(0., 0., 0.), **changes):
    args = dict(session="explicit-mock", accel=sensor("m/s^2", accel_values), gyro=sensor("rad/s", gyro_values),
                coordinate_frame=SDL_FRAME, source_kind="mock", mapping=CEMU_PARITY)
    args.update(changes)
    return sample_from_sdl_events(**args)


class BridgeTests(unittest.TestCase):
    def test_basis_vectors_and_units_are_explicit_not_hc_signs(self):
        for axis in range(3):
            accel, gyro = [0.] * 3, [0.] * 3
            accel[axis], gyro[axis] = 9.80665, math.pi
            sample = convert(accel, gyro)
            self.assertEqual(sample.accel_g[axis], -1.)
            self.assertEqual(sample.gyro_dps[axis], 180. if axis == 0 else -180.)
            self.assertEqual(sample.timestamp_us, 1234)
            self.assertEqual(sample.sampled_at_ns, 2_000_000_000)

    def test_gyro_only_update_never_changes_accel_timestamp(self):
        first = convert()
        second = convert(gyro=sensor("rad/s", (1., 2., 3.), timestamp=1_999_999))
        self.assertEqual(first.timestamp_us, second.timestamp_us)
        self.assertNotEqual(first.gyro_dps, second.gyro_dps)

    def test_missing_bad_units_basis_and_sensor_error_are_not_zero_fallbacks(self):
        for changes in [{"accel": None}, {"gyro": None}, {"coordinate_frame": "unknown"},
                        {"mapping": "automatic"}, {"accel": sensor("g", (0, 0, 1))},
                        {"gyro": sensor("rad/s", (0, float("nan"), 0))},
                        {"accel": {**sensor("m/s^2", (0, 0, 1)), "valid": False}},
                        {"accel": sensor("m/s^2", (0, 0, 1), timestamp=0)},
                        {"gyro": {**sensor("rad/s", (0, 0, 0)), "reason": "read_failed"}}]:
            with self.assertRaises(ValueError):
                convert(**changes)

    def test_skew_and_oldest_observation_propagate_for_freshness(self):
        old = {**sensor("rad/s", (0, 0, 0)), "receivedMonotonicNs": 1_980_000_000}
        self.assertEqual(convert(gyro=old).sampled_at_ns, 1_980_000_000)
        with self.assertRaises(ValueError):
            convert(gyro={**old, "receivedMonotonicNs": 1_900_000_000})
        self.assertFalse(bridge_capabilities()["hardwareValidated"])
        self.assertFalse(bridge_capabilities()["emulatorValidated"])

    def test_reader_schema_validates_both_freshness_clocks_and_identity(self):
        frame = {"schemaVersion": 1, "session": "session", "identity": "physical-identity",
                 "sourceKind": "mock", "motionCoordinateFrame": SDL_FRAME,
                 "sensorCapabilities": {"accel": True, "gyro": True},
                 "motion": {"accel": sensor("m/s^2", (0., 9.80665, 0.)), "gyro": sensor("rad/s", (0., 0., 0.))}}
        now = 2_000_000_000
        first = sample_from_reader_frame(frame, mapping=CEMU_PARITY, now_ns=now)
        other = sample_from_reader_frame({**frame, "identity": "other"}, mapping=CEMU_PARITY, now_ns=now)
        self.assertNotEqual(first.source_session, other.source_session)
        for changes in [{"identity": ""}, {"sourceKind": "synthetic"}, {"motion": {}},
                        {"sensorCapabilities": {"accel": True, "gyro": False}},
                        {"motion": {**frame["motion"], "gyro": {**frame["motion"]["gyro"], "receivedMonotonicNs": now + 1}}}]:
            with self.assertRaises(ValueError):
                sample_from_reader_frame({**frame, **changes}, mapping=CEMU_PARITY, now_ns=now)
        with self.assertRaises(ValueError):
            sample_from_reader_frame(frame, mapping=CEMU_PARITY, now_ns=now + 300_000_000)

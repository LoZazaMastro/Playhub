"""Native ABI and transport lifecycle exercised only with fake SDL functions."""
import ctypes as C
import threading
import unittest
from unittest.mock import patch

from .reader_sdl import Event, NativeReader, Reader, SensorEvent, transport_loop
from .sdl_backend import Unavailable
from .test_sdl_backend import fixture


class ReaderTests(unittest.TestCase):
    def make(self, sensors=True):
        api, _ = fixture()
        api.SDL_GetGamepadPath.return_value = b"fake-physical-path"
        api.SDL_GamepadHasSensor.return_value = sensors
        api.SDL_GamepadSensorEnabled.return_value = False
        api.SDL_SetGamepadSensorEnabled.return_value = True
        api.sensor_events.return_value = []
        self.now = 1_000_000_000
        self.identity = "physical-container-a"
        reader = Reader(api, lambda p, i: self.identity, clock_ns=lambda: self.now)
        self.addCleanup(reader.close)
        return api, reader

    def open(self, sensors=True):
        api, reader = self.make(sensors)
        caps = reader.start(42, self.identity, authorize_device_io=True, authorize_sensors=sensors)
        return api, reader, caps["session"]

    def event(self, sensor=1, timestamp=1000, values=(0, 9.80665, 0), which=42):
        event = SensorEvent()
        event.type, event.which, event.sensor = 0x659, which, sensor
        event.sensor_timestamp = timestamp
        event.data[:] = values
        return event

    def test_construction_and_missing_authorization_never_open(self):
        api, reader = self.make()
        with self.assertRaises(Unavailable):
            reader.start(42, self.identity)
        api.SDL_InitSubSystem.assert_not_called()
        api.SDL_OpenGamepad.assert_not_called()

    def test_native_audit_blocks_loading(self):
        with patch("ctypes.CDLL") as load:
            with self.assertRaises(Unavailable):
                NativeReader({}, authorize_device_io=True)
            load.assert_not_called()

    def test_standard_passthrough_only_inverts_coordinate_y(self):
        api, reader, token = self.open(False)
        frame = reader.poll(token, self.identity)
        self.assertEqual(frame["sticks"]["left"], [-1, -1])
        self.assertEqual(frame["triggers"], {"left": 0, "right": 1})
        self.assertEqual(frame["buttons"], ["A"])
        self.assertEqual(frame["motion"], {"gyro": None, "accel": None})
        self.assertEqual(frame["sourceKind"], "mock")
        api.SDL_RumbleGamepad.assert_not_called()
        api.SDL_SetGamepadSensorEnabled.assert_not_called()

    def test_raw_sensor_timestamp_and_units_are_not_poll_timestamp(self):
        api, reader, token = self.open()
        api.sensor_events.return_value = [self.event(), self.event(2, 2000, (1, 2, 3))]
        frame = reader.poll(token, self.identity)
        self.assertEqual(frame["motion"]["accel"]["timestampNs"], 1000)
        self.assertEqual(frame["motion"]["gyro"]["unit"], "rad/s")
        self.assertEqual(frame["motion"]["gyro"]["value"], (1, 2, 3))
        api.sensor_events.return_value = []
        self.now += 10_000_000
        self.assertEqual(reader.poll(token, self.identity)["motion"]["accel"]["timestampNs"], 1000)
        self.now += 100_000_001
        self.assertIsNone(reader.poll(token, self.identity)["motion"]["accel"])

    def test_wrong_device_nonfinite_zero_and_regressing_samples_not_motion(self):
        api, reader, token = self.open()
        for event in (self.event(which=43), self.event(timestamp=0), self.event(values=(float("nan"), 0, 0))):
            api.sensor_events.return_value = [event]
            self.assertIsNone(reader.poll(token, self.identity)["motion"]["accel"])
        api.sensor_events.return_value = [self.event(timestamp=5000)]
        reader.poll(token, self.identity)
        api.sensor_events.return_value = [self.event(timestamp=4000)]
        self.assertIsNone(reader.poll(token, self.identity)["motion"]["accel"])

    def test_stale_request_cannot_close_current_owner(self):
        api, reader, token = self.open()
        for session, identity in (("old", self.identity), (token, "other")):
            with self.assertRaises(Unavailable):
                reader.poll(session, identity)
        self.assertTrue(reader.owned)
        reader.poll(token, self.identity)

    def test_hotplug_identity_change_closes_and_never_reselects(self):
        api, reader, token = self.open()
        self.identity = "replacement"
        with self.assertRaises(Unavailable):
            reader.poll(token, "physical-container-a")
        self.assertFalse(reader.owned)
        api.SDL_CloseGamepad.assert_called_once_with(99)

    def test_single_owner_and_cancellation_cleanup(self):
        api, reader, token = self.open()
        other_api, other = self.make()
        with self.assertRaises(Unavailable):
            other.start(42, self.identity, authorize_device_io=True)
        other_api.SDL_InitSubSystem.assert_not_called()
        cancel = threading.Event()
        frames = []
        def consume(frame):
            frames.append(frame)
            cancel.set()
        transport_loop(reader, consume, cancel)
        self.assertEqual(len(frames), 1)
        self.assertFalse(reader.owned)
        api.SDL_QuitSubSystem.assert_called_once()
        self.assertEqual([call.args[2] for call in api.SDL_SetGamepadSensorEnabled.call_args_list],
                         [True, True, False, False])

    def test_consumer_error_closes_owner(self):
        api, reader, token = self.open()
        with self.assertRaisesRegex(RuntimeError, "consumer"):
            transport_loop(reader, lambda _: (_ for _ in ()).throw(RuntimeError("consumer")), threading.Event())
        self.assertFalse(reader.owned)

    def test_sensor_failure_attempts_restore_and_closes(self):
        api, reader = self.make()
        api.SDL_SetGamepadSensorEnabled.side_effect = [False, True]
        with self.assertRaises(Unavailable):
            reader.start(42, self.identity, authorize_device_io=True, authorize_sensors=True)
        self.assertFalse(reader.owned)
        self.assertEqual(api.SDL_SetGamepadSensorEnabled.call_count, 2)

    def test_public_sensor_event_abi(self):
        self.assertEqual(C.sizeof(Event), 128)
        self.assertEqual(SensorEvent.sensor_timestamp.offset, 40)
        self.assertEqual(C.sizeof(SensorEvent), 48)


if __name__ == "__main__":
    unittest.main()

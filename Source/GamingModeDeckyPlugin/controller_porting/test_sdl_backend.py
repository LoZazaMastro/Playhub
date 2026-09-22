"""Mock ABI tests only. No native DLL loading, SDL initialization or device I/O."""

import ctypes as C
from pathlib import Path
import tempfile
import threading
import unittest
from unittest.mock import Mock, patch

from controller_porting.sdl_backend import ControllerSession, NativeSDL, Unavailable, inspect_library


def fixture():
    api = Mock()
    api.provenance = {"kind": "mock"}
    api.SDL_GetVersion.return_value = 3004010
    api.SDL_GetError.return_value = b"mock failure"
    api.SDL_ClearError.side_effect = lambda: setattr(api.SDL_GetError, "return_value", b"")
    api.SDL_InitSubSystem.return_value = True
    api.SDL_OpenGamepad.return_value = 99
    api.SDL_GamepadConnected.return_value = True
    api.SDL_GetGamepadID.return_value = 42
    api.SDL_GetGamepadVendor.return_value = 0x28de
    api.SDL_GetGamepadProduct.return_value = 0x1304
    api.SDL_GetGamepadProperties.return_value = 1
    api.SDL_GetBooleanProperty.return_value = True
    api.SDL_GamepadHasButton.return_value = True
    api.SDL_GamepadHasAxis.return_value = True
    api.SDL_GetGamepadButton.side_effect = lambda h, b: b == 0
    api.SDL_GetGamepadAxis.side_effect = lambda h, a: [-32768, 32767, 0, 0, 0, 32767][a]
    api.SDL_RumbleGamepad.return_value = True

    def enumerate_ids(count):
        C.cast(count, C.POINTER(C.c_int))[0] = 1
        return (C.c_uint32 * 1)(42)

    api.SDL_GetGamepads.side_effect = enumerate_ids
    return api, ControllerSession(api)


def opened():
    api, session = fixture()
    session.start(authorize_device_io=True)
    caps = session.open(42)
    return api, session, caps["session"]


class BackendTests(unittest.TestCase):
    def test_constructor_and_denied_start_are_inert(self):
        api, session = fixture()
        for value in [False, None, 1, "yes"]:
            with self.assertRaises(Unavailable):
                session.start(authorize_device_io=value)
        self.assertEqual(api.mock_calls, [])
        with patch("ctypes.CDLL") as loader:
            with self.assertRaises(Unavailable):
                NativeSDL("absent", "", authorize_device_io=False)
            loader.assert_not_called()

    def test_file_observation_is_not_hardware_support(self):
        with tempfile.TemporaryDirectory(dir=Path(__file__).parent) as folder:
            path = Path(folder) / "mock.dll"
            path.write_bytes(b"not a DLL")
            with patch("ctypes.CDLL") as loader:
                info = inspect_library(path)
                self.assertEqual(len(info["sha256"]), 64)
                self.assertFalse(info["hardwareValidated"])
                with self.assertRaises(Unavailable):
                    NativeSDL(path, "0" * 64, authorize_device_io=True)
                loader.assert_not_called()

    def test_version_and_init_failure_never_open(self):
        for version in [2000000, 3004008, 4000000]:
            api, session = fixture()
            api.SDL_GetVersion.return_value = version
            with self.assertRaises(Unavailable):
                session.start(authorize_device_io=True)
            api.SDL_InitSubSystem.assert_not_called()
        api, session = fixture()
        api.SDL_InitSubSystem.return_value = False
        with self.assertRaises(Unavailable):
            session.start(authorize_device_io=True)
        session.close()
        api.SDL_QuitSubSystem.assert_not_called()

    def test_ctypes_binding_has_explicit_abi_without_initializing(self):
        library = Mock()
        observation = {"path": "C:/approved/SDL3.dll", "sha256": "a" * 64}
        with patch("controller_porting.sdl_backend.os.name", "nt"), \
                patch("controller_porting.sdl_backend.inspect_library", return_value=observation), \
                patch("ctypes.CDLL", return_value=library) as loader:
            native = NativeSDL(observation["path"], "a" * 64, authorize_device_io=True)
        loader.assert_called_once_with(observation["path"], winmode=0x1100)
        self.assertEqual(native.SDL_OpenGamepad.restype, C.c_void_p)
        self.assertEqual(native.SDL_GetGamepadAxis.restype, C.c_int16)
        self.assertEqual(native.SDL_RumbleGamepad.argtypes,
                         [C.c_void_p, C.c_uint16, C.c_uint16, C.c_uint32])
        self.assertEqual(library.mock_calls, [])

    def test_missing_export_fails_before_initialization(self):
        library = Mock()
        del library.SDL_OpenGamepad
        with patch("controller_porting.sdl_backend.os.name", "nt"), \
                patch("controller_porting.sdl_backend.inspect_library",
                      return_value={"path": "C:/approved/SDL3.dll", "sha256": "a" * 64}), \
                patch("ctypes.CDLL", return_value=library):
            with self.assertRaises(AttributeError):
                NativeSDL("unused", "a" * 64, authorize_device_io=True)
        library.SDL_InitSubSystem.assert_not_called()

    def test_null_enumeration_and_invalid_count_fail_closed(self):
        api, session = fixture()
        session.start(authorize_device_io=True)
        api.SDL_GetGamepads.side_effect = None
        api.SDL_GetGamepads.return_value = None
        with self.assertRaises(Unavailable):
            session.instances()
        api.SDL_free.assert_not_called()
        def bad_count(count):
            C.cast(count, C.POINTER(C.c_int))[0] = 257
            return (C.c_uint32 * 1)(42)
        api.SDL_GetGamepads.side_effect = bad_count
        with self.assertRaises(Unavailable):
            session.instances()
        api.SDL_free.assert_called_once()

    def test_mid_read_disconnect_discards_frame(self):
        api, session, token = opened()
        api.SDL_GamepadConnected.side_effect = [True, False]
        with self.assertRaises(Unavailable):
            session.read(token)
        self.assertIsNone(session.token)

    def test_puck_identity_does_not_enable_unimplemented_features(self):
        api, session, token = opened()
        caps = session.capabilities(token)
        self.assertEqual(caps["hardware"], "28de:1304")
        self.assertEqual(caps["backendKind"], "mock")
        for key in ["hardwareValidated", "liveMapping", "virtualOutput", "gyro", "trackpads", "exclusiveMode"]:
            self.assertFalse(caps[key])
        api.SDL_GetBooleanProperty.return_value = False
        self.assertFalse(session.capabilities(token)["rumbleReported"])
        with self.assertRaises(Unavailable):
            session.rumble(token, 10, 10, 50, authorize_output=True)
        api.SDL_RumbleGamepad.assert_not_called()

    def test_real_read_boundary_normalizes_both_extrema(self):
        api, session, token = opened()
        frame = session.read(token)
        self.assertEqual(frame["buttons"], ["A"])
        self.assertEqual(frame["axes"], {"0": -1, "1": 1, "2": 0, "3": 0, "4": 0, "5": 1})
        api.SDL_GamepadHasAxis.return_value = False
        self.assertEqual(session.read(token)["axes"], {})

    def test_getter_error_is_not_returned_as_neutral_input(self):
        api, session, token = opened()
        api.SDL_GetError.return_value = b""
        def read_axis(handle, axis):
            api.SDL_GetError.return_value = b"Invalid gamepad axis"
            return 0
        api.SDL_GetGamepadAxis.side_effect = read_axis
        with self.assertRaisesRegex(Unavailable, "input_read_failed"):
            session.read(token)
        self.assertIsNone(session.token)
        api.SDL_CloseGamepad.assert_called_once_with(99)

    def test_negative_trigger_is_not_silently_clamped_to_neutral(self):
        api, session, token = opened()
        api.SDL_GetGamepadAxis.side_effect = lambda handle, axis: -1 if axis == 4 else 0
        with self.assertRaisesRegex(Unavailable, "invalid_axis_value"):
            session.read(token)
        self.assertIsNone(session.token)
        api.SDL_CloseGamepad.assert_called_once_with(99)

    def test_button_getter_failure_discards_frame_and_stops_owned_rumble(self):
        api, session, token = opened()
        session.rumble(token, 1, 1, 10, authorize_output=True)
        def read_button(handle, button):
            api.SDL_GetError.return_value = b"Invalid gamepad"
            return False
        api.SDL_GetGamepadButton.side_effect = read_button
        with self.assertRaisesRegex(Unavailable, "input_read_failed"):
            session.read(token)
        api.SDL_RumbleGamepad.assert_called_with(99, 0, 0, 0)
        self.assertIsNone(session.token)

    def test_stale_error_is_cleared_before_valid_neutral_frame(self):
        api, session, token = opened()
        api.SDL_GetError.return_value = b"older unrelated error"
        api.SDL_GetGamepadAxis.side_effect = lambda handle, axis: 0
        api.SDL_GetGamepadButton.side_effect = lambda handle, button: False
        frame = session.read(token)
        self.assertEqual(frame["buttons"], [])
        self.assertTrue(all(value == 0 for value in frame["axes"].values()))
        api.SDL_ClearError.assert_called_once()
        api.SDL_CloseGamepad.assert_not_called()

    def test_instance_change_during_frame_invalidates_session(self):
        api, session, token = opened()
        api.SDL_GetGamepadID.side_effect = [42, 43]
        with self.assertRaisesRegex(Unavailable, "disconnected_during_read"):
            session.read(token)
        self.assertIsNone(session.token)

    def test_disconnect_invalidates_token_without_auto_reconnect(self):
        api, session, token = opened()
        api.SDL_GamepadConnected.return_value = False
        with self.assertRaises(Unavailable):
            session.read(token)
        self.assertIsNone(session.token)
        api.SDL_CloseGamepad.assert_called_once_with(99)
        api.SDL_GamepadConnected.return_value = True
        new_token = session.open(42)["session"]
        self.assertNotEqual(token, new_token)
        with self.assertRaises(Unavailable):
            session.rumble(token, 1, 1, 1, authorize_output=True)
        api.SDL_RumbleGamepad.assert_not_called()

    def test_enumeration_frees_and_open_failure_cleans_owned_reference(self):
        api, session = fixture()
        session.start(authorize_device_io=True)
        api.SDL_GetGamepadProperties.return_value = 0
        with self.assertRaises(Unavailable):
            session.open(42)
        api.SDL_free.assert_called_once()
        api.SDL_CloseGamepad.assert_called_once_with(99)
        self.assertIsNone(session.handle)

    def test_invalid_and_unauthorized_output_never_dispatches(self):
        api, session, token = opened()
        with self.assertRaises(Unavailable):
            session.rumble(token, 1, 1, 50)
        for args in [(-1, 0, 1), (65536, 0, 1), (True, 0, 1), (float("nan"), 0, 1),
                     (1, 1, 251), (1, 1, 0), (1, 1, float("inf"))]:
            with self.assertRaises(ValueError):
                session.rumble(token, *args, authorize_output=True)
        api.SDL_RumbleGamepad.assert_not_called()

    def test_output_and_owned_cleanup(self):
        api, session, token = opened()
        session.rumble(token, 100, 200, 50, authorize_output=True)
        api.SDL_RumbleGamepad.assert_called_with(99, 100, 200, 50)
        session.close()
        api.SDL_RumbleGamepad.assert_called_with(99, 0, 0, 0)
        session.close()
        api.SDL_CloseGamepad.assert_called_once_with(99)
        api.SDL_QuitSubSystem.assert_called_once_with(0x2000)

    def test_failed_output_invalidates_session_and_attempts_stop(self):
        api, session, token = opened()
        api.SDL_RumbleGamepad.return_value = False
        with self.assertRaises(Unavailable):
            session.rumble(token, 100, 0, 30, authorize_output=True)
        self.assertIsNone(session.token)
        self.assertEqual(api.SDL_RumbleGamepad.call_count, 2)
        api.SDL_CloseGamepad.assert_called_once()

    def test_wrong_thread_never_calls_native_api(self):
        api, session = fixture()
        errors = []
        def run():
            try:
                session.start(authorize_device_io=True)
            except Unavailable as error:
                errors.append(str(error))
        thread = threading.Thread(target=run)
        thread.start()
        thread.join()
        self.assertEqual(errors, ["sdl_main_thread_required"])
        self.assertEqual(api.mock_calls, [])


if __name__ == "__main__":
    unittest.main()

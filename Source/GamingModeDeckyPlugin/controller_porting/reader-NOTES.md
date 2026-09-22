# Standard input and raw motion transport

2026-09-08 source-only delivery, independent MIT code. No HC code or native binary
copied. No native load, process launch, driver installation or live input executed.

`reader_sdl.py` reuses the existing independent SDL public-ABI adapter and adds
raw SDL sensor-event bindings. It does not implement mapping, deadzones, gyro aim,
calibration UI, profiles, hotkeys or configurable trackpads. Standard stick Y is
inverted once from SDL down-positive to the output adapter's up-positive basis;
this is coordinate conversion, not a user mapping.

## Host contract

Reader(api, verify_source) -> start(instance, stable_identity,
authorize_device_io=True, authorize_sensors=False) -> poll(session, identity)
-> close(). `transport_loop` runs at <=100 Hz and closes on cancellation or a
consumer exception. All calls must run on an SDL host's main thread, separate
from Decky's thread-pool RPC execution. Nothing starts a helper automatically.

The injected host verifier MUST resolve the actual path/instance to the selected
physical identity, reject virtual ancestry, and check an exclusive cross-process
source lease/coexistence policy. The local lock only prevents duplicate readers
in this module's process: it does not exclude Steam or GamingModeAgent by itself.
That host verifier/lease and IPC supervisor are still integration dependencies.
Never substitute a renderer boolean or a VID/PID-only whitelist.

NativeReader requires host approval of exact runtime path/hash, license evidence
and coexistence. Current candidate is the same Steam SDL3.dll found by Playhub's
SdlControllerHaptics.FindLibrary. File metadata reports 03.05.00.00, SHA256
95F4D71B795EFC11DED38271609715FBEEB7C57DBE22AF277DD493BD6858E6B3.
It has NOT been loaded or approved here; no exact SDL 3.4.10 binary is claimed.
The API contract was checked against upstream release-3.4.10 headers. The existing
adapter requires runtime SDL version >=3.4.10 and <4.0. Presence is not approval.

## Frame contract

schemaVersion=1, session, identity, sequence, observedMonotonicNs, buttons,
sticks {left,right}, triggers {left,right}, sourceKind live|mock.
Missing axes remain null, not fake readings. Output must explicitly reject them
or neutralize only declared unsupported axes according to its own target policy.

motionCoordinateFrame = SDL-right-up-toward-player.
motion.accel / motion.gyro are null when absent, stale or invalid, otherwise:
value[3], timestampNs (native sensor clock), receivedMonotonicNs (host observation),
unit m/s^2 or rad/s. Sensor timestamps are NOT necessarily OS-clock synchronized.
Repeated polls never advance the timestamp. sourceKind is mock for injected mocks;
only NativeSDL provenance produces live. Sensor enable is separately authorized.
Sensors already enabled are not disabled on close; only changes owned here are
reverted. Close returns sensorRestoreFailures rather than concealing failure.

Mendel owns DSU: require both valid samples, convert units and explicitly validated
coordinates, use accel timestampNs//1000 and the older receive time for freshness.
No new timestamp for gyro-only changes. Loopback-only opt-in output, <=100 Hz.
Pascal owns Xbox/DS4 output: consume raw standard frame directly, not UI polling;
no controller profile transform. Native DLL approval and output verification are
not provided by this reader. Physical rumble remains separate output authorization
and has no reader RPC or new UI in this increment.

## Evidence and remaining validation

- SDL sensor units: https://wiki.libsdl.org/SDL3/SDL_SensorType
- Sensor enable: https://wiki.libsdl.org/SDL3/SDL_SetGamepadSensorEnabled
- Event ABI/timestamp: https://raw.githubusercontent.com/libsdl-org/SDL/release-3.4.10/include/SDL3/SDL_events.h
- SDL3 has no SDL_GetGamepadSensorDataWithTimestamp in this header release;
  SDL_GamepadSensorEvent supplies sensor_timestamp in nanoseconds instead.
- Upstream SDL is zlib; preserve exact runtime/dependency notices if distributing.
  Reusing the installed file is not permission to redistribute Steam's copy.

Tests: python -B -m unittest controller_porting.reader_test
Mock ABI/lifecycle only. No real frame, DSU consumer, virtual output or handheld
sensor validated. Windows sensor/OEM/serial provider families remain separate
future adapters, not implied by standard SDL support. main.py and deployment
payload were not changed to expose this reader while the parent freeze applies.

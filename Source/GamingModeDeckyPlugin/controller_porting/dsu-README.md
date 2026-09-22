# DSU sensor transport

Independent MIT implementation. Scope: sensor data for DSU/Cemuhook-compatible
emulators only. No gyro aim, button mapping, deadzones, profiles, virtual input,
rumble commands or controller writes. Onboarding files remain untouched.

## Implemented and verified

- DSU protocol v1001 version, port discovery and motion subscriptions.
- Little-endian framing, declared length handling and CRC32 with checksum field
  zeroed during calculation. Motion packets are 100 bytes.
- Four source slots, eight clients maximum, sixteen requests per service tick.
- Loopback IPv4 ONLY, default 127.0.0.1:26760. No remote/LAN option exists.
- Explicit start authorization, exclusive port binding, no automatic worker,
  timer, process, driver, reader activation or discovery.
- Host-scheduled nonblocking tick; rate 1..100 Hz (default 100). No backlog replay:
  latest sample wins. At most one packet per fresh revision/client/slot/tick.
- Client subscriptions expire after five seconds without renewal. Invalid CRC,
  unsupported version/command/flags or invalid slots do not subscribe.
- Source freshness expires after 250 ms. No sample means disconnected port info
  and NO motion packets, not invented zero IMU. Expired motion is not replayed.
- Sensors alone: mandatory button/touch/trigger bytes are neutral, sticks 128.
  These protocol padding/control fields do NOT implement controller input.
- Per-client uint32 packet counter wraps; server ID is stable for a started run.

20 automated tests passed, including REAL UDP sockets on ephemeral loopback
ports. All IMU fixtures are explicitly mock. Mock mode requires port=0 and
normal mode rejects mock samples. No native hardware/SDL/emulator was opened.
Wire tests are transport validation, NOT motion orientation or emulator approval.

## Reader contract agreed with Newton

`sample_from_reader_frame(frame, mapping="cemu-sdl-parity-v1")` in dsu_bridge.py
accepts reader schema v1: session, identity, sourceKind, motionCoordinateFrame,
sensorCapabilities, motion.accel and motion.gyro. Both sensors must be present
and valid. Sensor dictionaries carry value[3], unit, timestampNs (native sensor
clock) and receivedMonotonicNs (host freshness clock). No SDL import is needed.

Required frame: SDL-right-up-toward-player. Required units: accel m/s^2, gyro
rad/s. The transport uses accel timestampNs // 1000, never poll time or a
fabricated counter. It retains the oldest host observation for freshness and
rejects sensor observation pairs more than 50 ms apart. Source session and
identity are bound together with SHA256; they are not exposed on the DSU wire.

Gyro-only updates retain the accelerometer timestamp. They cannot refresh the
250 ms accelerometer progress deadline. Regressed timestamps invalidate the
slot. Missing/error/stale motion must call disconnect(slot) in the host glue;
do not keep feeding its previous sample. On session/device replacement, call
disconnect before publishing the new identity and require renewed subscription.

The selected mapping is derived by comparing Cemu's SDL and DSU input paths:
DSU acceleration = (-SDL x, -SDL y, -SDL z) / 9.80665;
DSU gyro pitch/yaw/roll = (SDL x, -SDL y, -SDL z) * 180/pi.
It is explicit source-parity evidence, not a universal coordinate standard or
hardware calibration. Three-axis basis fixtures test this transform separately
from transport. bridge_capabilities reports hardwareValidated/emulatorValidated
false. Actual controller orientation/gravity and emulator motion remain a live
validation gate. Never guess HC sign permutations or label these fixtures live.

## Exact helper integration API

```python
from controller_porting.dsu import DsuServer
from controller_porting.dsu_bridge import sample_from_reader_frame, CEMU_PARITY

server = DsuServer()  # inert; host owns lifetime
address = server.start(authorized=True)  # only after explicit user opt-in
# In the existing authorized helper loop, not the frontend:
try:
    sample = sample_from_reader_frame(reader_frame, mapping=CEMU_PARITY)
    server.publish(0, sample)  # optional actual mac bytes/connection metadata
except (ValueError, TypeError, OverflowError):
    server.disconnect(0)
server.tick()  # also called when no reader frame; serves discovery/expiry
# In the helper's finally / explicit stop:
server.close()
```

The host must explicitly handle a changed session/device with disconnect before
the first publish. Only trusted reader glue calls publish: it is not a renderer
or network sample injection endpoint. sourceKind='live' is an asserted reader
provenance, not cryptographic proof. Neither serialization nor UDP can prove that
a malicious caller supplied hardware data; production host must own the reader.

`MotionSample(source_session, timestamp_us, sampled_at_ns, accel_g, gyro_dps,
source_kind="live", coordinate_frame="dsu")` is the lower-level accepted sample.
Both vectors are immutable tuples of three finite numbers. Unit-only
sample_from_si additionally requires the caller to explicitly pass DSU-aligned
vectors; it does NOT perform an implicit axis mapping.

Only supply mac/connection if known (six real bytes, USB=1, Bluetooth=2); default
zero means unknown, including puck radio. Model=2 is the DSU full-motion class,
not a claim that Steam Controller hardware is a DualShock. Battery is unknown.

For an isolated mock helper: DsuServer(port=0, test_mode=True). DSU has no
on-wire mock flag, so this is restricted to an explicit test server on an
ephemeral port. Never configure an emulator against a test fixture server.

## Commands and remaining gates

```text
python -B -m unittest controller_porting.dsu_test controller_porting.dsu_bridge_test -v
```

Newton owns reader/helper/host loop integration, Pascal owns separate output.
No main.py, shared manifest, existing runtime or frontend files modified here.
No server left running after tests. No deployment, driver installation, live
input, native writes, build, commit or push. Remaining: owner integrates these
APIs into the explicitly authorized helper, then separately validates actual
sensor/axis behavior in an emulator. No new pip/native dependency for DSU.

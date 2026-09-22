# Integrated controller transport host

Source/test increment, not an installed or hardware-verified feature.
main.py and the frontend remain unchanged during the parent's release freeze.

## Connected implementation

helper_host.Pipeline owns, in order:
1. Source lease acquisition.
2. Reader start and expected physical identity validation.
3. Optional Xbox360/DS4 OutputLease creation, initial neutral and target verifier.
4. Optional loopback DsuServer binding.
5. Local <=100 Hz main-thread loop: Reader.poll -> OutputLease.submit/tick and
   sample_from_reader_frame -> DsuServer.publish/tick.
6. Stop: final output neutral/feedback and owned-target removal, DSU socket close,
   sensor restoration/reader close, source lease release. Cleanup attempts continue
   after individual errors and report failed components.

No UI frame feeder exists. IPC accepts only start/status/heartbeat/stop/shutdown;
start selects output x360/ds4 and DSU/feedback booleans, not paths, devices, frames,
mappings or profile documents. The source identity/instance is host-selected.
There is no viewer-only activation mode, remapper, aim, deadzone or controller
profile feature. DSU sends sensor-only data through Mendel's validated bridge.

helper_client.HelperClient(port, token) connects ONLY to 127.0.0.1. The helper
binds an ephemeral control port, requires a high-entropy host token, limits request
size to 4096 bytes, queues at most 16 commands, and bounds slow client handling.
Keep the token in a private parent environment/IPC channel; never expose it in UI,
logs or a shared public file. A trusted client sends heartbeat below the 2-second
lease interval. Status calls do NOT extend the lease. Losing control triggers
local teardown. High-rate controller frames never cross this control connection.

Status contains state, failurePhase/error, cleanupErrors, sourceReady,
outputState and DSU state. It distinguishes control_lease_expired from transport
or activation failure. A successful mock test is explicitly testMode=true and
never evidence of live device support.

## Thread and ownership

All reader/output/DSU lifecycle and ticks run on the helper main thread; only
socket acceptance is on a background thread. Main-thread enforcement is tested.
Do not run this loop inside a Decky thread-pool worker.

helper_ownership.WindowsLease uses a per-source named mutex. Busy ownership fails;
abandoned ownership requires recovery rather than silently continuing. This lock
coordinates participating Playhub helpers, not Steam or another program that does
not use the mutex. Native startup still requires an approved coexistence policy.

WindowsAncestry resolves a supported Windows SDL HID interface path through
Configuration Manager, walks the actual parent chain to HTREE root, rejects
virtual/ViGEm/USBIP ancestry, and hashes the observed physical chain. Unknown path
formats, unresolvable parents and non-USB/Bluetooth physical transports fail closed.
This is a connection-stable identity, not a promise of persistence after moving
ports/reinstalling drivers. A new identity requires explicit host reselection.
OEM/internal sensor families need their own verified physical provider; they are
not marked supported by this USB/Bluetooth adapter.

## Native factory and explicit remaining gates

helper_native.native_pipeline builds the SAME Pipeline using NativeReader,
NativeViGEm, WindowsAncestry and WindowsLease. Factory creation is inert: native
loads happen only during authorized start after lease acquisition. No server,
driver, library or helper is downloaded/installed/started automatically.

Required host inputs not approved in this delivery:
- Exact SDL binary/license/architecture/coexistence evidence. Existing Agent
  locates Steam/SDL3.dll, observed SHA256
  95F4D71B795EFC11DED38271609715FBEEB7C57DBE22AF277DD493BD6858E6B3 and metadata
  03.05.00.00, NOT an exact verified SDL 3.4.10 runtime. Do not redistribute it.
- Native ViGEmClient binary matching Pascal's pinned SDK/ABI/notices approval;
  an installed ViGEmBus alone is insufficient.
- verify_target(client,target): trusted OS-correlated target verification. This
  callback is mandatory, not replaceable with true from renderer/config. Exact
  native handle-to-device correlation remains with the output owner.
- Parent process supervision/packaging and authenticated control-channel launch
  wiring after the main.py freeze. The CLI deliberately exposes only --mock until
  those native approvals and target correlation are installed in the supervisor.

An indefinitely blocked native call can also block this main-thread watchdog;
production requires parent process monitoring and a verified output-driver
process-exit cleanup policy. This is not yet a crash-recovery guarantee.

## Executed verification

python -B -m unittest controller_porting.helper_test

Includes a real child Python process running helper_host --mock, authenticated
TCP requests, frame progression without UI polling, lease timeout, shutdown and
process exit. Separate integrated test uses the actual Reader, OutputLease encoder
and DsuServer with fake SDL/ViGEm functions, then validates a real 100-byte loopback
DSU packet/CRC. All native device calls in tests are mocks. Tests also cover busy
and abandoned mutex results, Configuration Manager ancestry and virtual rejection,
disconnect, lost ownership, missing sensors and no unauthorized activation.

Code is independent MIT glue over existing Playhub adapters; no HC source/assets,
protocol implementation or binary was copied. See reader-NOTES, output-PROVENANCE
and dsu-NOTICES for separate upstream dependencies and license gates.

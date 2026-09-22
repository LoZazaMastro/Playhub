# Output bridge addendum

Source-only, 2026-09-08. No DLL loaded, target created, driver installed, hardware
input/output exercised, build/package or live integration performed.

## HC is the pipeline reference

Read-only reference: hc-study-80c450afcc76456bb02ef75eedba733a/HandheldCompanion-main.
ControllerManager.Tick:300 pulls the physical reader, emits unmapped input:322,
applies MotionManager:347 and layoutManager.MapController:351, then DS4Touch:375,
VirtualManager:376 and DSUServer:377. ProfileManager Applied/Discarded handlers in
VirtualManager:246/272 choose output behavior. InputsManager is a hotkey consumer,
not the physical reader. Generic SDL and XInput paths exist; missing PID-specific
Steam handling is not evidence that generic reading is unsupported.

In THIS archive Xbox360Target and DualShock4Target both derive VIIPERTarget.
The remaining managed ViGEm assembly reference does not make it the active sink.
DS4Target uses touch contacts and raw gyro/acceleration from GamepadMotion;
DSUServer.UpdateInputs:507 is an independent UDP motion transport, including
timestamps and coordinate conversion. None is replaced by an XInput stick.

HC root is CC-BY-NC-SA-4.0; its mapping/profile/DSU wrappers were NOT copied.
Separate-process execution does not waive HC's noncommercial redistribution
conditions. Reusing the entire HC pipeline needs compatible terms/permission and
an audited integration API, not a renamed copy. Upstream SDL, GamepadMotion and
VIIPER clients are separately auditable dependencies; their HC wrappers do not
automatically inherit an upstream permissive license.

## Implemented boundary

output_vigem.py is only the necessary OS bridge for standard normalized
passthrough frames, not a mapping engine. Xbox360 and basic DS4 target adapters
are present. Native signatures, XUSB_REPORT and DS4_REPORT come
from the pinned MIT ViGEmClient public SDK, recorded in output-artifacts.json.
NativeViGEm loads ONLY after explicit device authorization and a trusted host
approval matching absolute DLL path/hash/revision/license/distribution evidence.
There is deliberately NO approved binary/hash or bundled DLL yet.

OutputLease serializes native calls on one owner thread. It requires physical
source verification before create, neutral-first input, real target enumeration
verification, and exclusion of its virtual target from the physical source.
The host must implement these verifiers using OS instance ancestry and source
lease ownership; returning true blindly is not production integration.
Frames use schemaVersion/session/identity/sequence/observedMonotonicNs,
buttons (SDL standard names), sticks left/right in [-1,1], triggers in [0,1].
Stick +Y means up; an SDL reader must explicitly convert its native +Y-down axis.
Reader and output must share the same monotonic clock domain. Missing sensor data is
not fabricated. This slice requires explicit stick/trigger values, including
neutral values supplied by the standard capability-aware reader adapter.

A bounded local single-frame queue feeds the worker, not frontend RPC polling.
An owner-thread watchdog expires after 100ms without fresh source time. Invalid,
duplicate, stale, foreign or future frames stop output. Cleanup independently
attempts neutral, own-target removal, disconnect and frees. Errors remain visible
as cleanup_error: this is NOT a claim that an orphan was successfully removed.
Rumble registration requires separate explicit authorization and an owned-source
sink. Native callbacks only enqueue the latest motor levels, never call SDL.
Owner-thread draining revalidates source ownership; stop unregisters callbacks,
invalidates late deliveries and requests zero motors on that source session.
The production source sink must reject other sessions/devices and avoid blocking
the output watchdog. No physical rumble was executed in this increment.

## Remaining production gates

- Build/audit the pinned native SDK binary and transitive dependencies; record
  binary hash and reproduce/include notices. No HC DLL extraction or blind download.
- Integrate source ownership, OS ancestry enumeration and cross-process lease.
- Define dedicated process supervision/job ownership for crash cleanup. Current
  worker is a callable loop, not a shipped process service or executable.
- Hardware acceptance: enumeration, real buttons/axes/releases, input-loop
  prevention, disconnect/timeout/crash rollback and Steam coexistence.
- DS4/PS Remote Play still needs real compatibility validation; basic DS4 output
  does not claim raw gyro, touchpad or PS Remote Play acceptance.
- DSU raw gyro/acceleration transport is separately owned by the DSU worker.
  Remapping, deadzones, gyro-aim, controller profiles, trackpad configuration,
  calibration/test UI and hotkeys are excluded from the final approved scope.

Local ViGEmBus 1.21.442.0 was running with Valid Authenticode signature.
Upstream ViGEmBus is BSD-3-Clause and retired; ViGEmClient is MIT and retired.
Local VIIPER exe 0.7.0.0 was not running, SHA256
1868D682F4CC6D62349BBCCBF0727B05D3EB6E22027AC34F0F1D9B1DE56F2DDC.
Stopped mausbip does not prove USBIP readiness. VIIPER add success does not prove
USBIP attach success; real OS enumeration remains mandatory.

Primary references:
- https://github.com/nefarius/ViGEmClient/tree/b66d02d57e32cc8595369c53418b843e958649b4
- https://github.com/nefarius/ViGEmBus
- https://alia5.github.io/VIIPER/stable/api/overview/
- https://alia5.github.io/VIIPER/stable/getting-started/installation/

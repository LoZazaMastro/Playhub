# Controller closure audit - 2026-09-08

Read-only audit of current controllerNative, ControllerSettings, navigation
haptics, main.py, build-plugin.bat and CONTROLLER-SUPPORT.md. Backend fixes are
limited to sdl_backend.py and its isolated tests. No shared/UI files modified.

## Actual current boundary

No Steam Controller 2026+puck hardware operation was verified in this audit.
"Wired" below means an executable code path exists, NOT a successful physical
device test, installed payload verification or a validated firmware combination.

| Feature | Current source status | 2026+puck evidence |
| --- | --- | --- |
| Controller selection/name/USB identity | Wired to Steam ControllerStore | Runtime Steam report, not physical feature detection |
| Steam native input test | Wired route /controller/devicesupport/{index} | Route tests pass with mocks; actual controller navigation unverified |
| Stick calibration | Wired Steam Inputs route, capability bits 2/3 | Native capability gate only; no Playhub hardware calibration |
| Gyro calibration | Wired Steam Gyro route, bit 11 | Opens Steam when reported; no Playhub gyro reader/remapper |
| Steam Input layout editor | Wired running-app route | Steam owns mappings; no HC output engine |
| Native controller settings | Wired Steam settings route | Actual UI/device behavior unverified |
| Navigation haptics | Existing browser/Steam API and /dash/haptic agent paths | Real write-capable code already exists; not exercised or newly validated here |
| SDL passive diagnostics | File-only endpoint implementation and helper exist | No runtime or device support proven; no current main.py/UI wiring found |
| SDL standard input/rumble adapter | Real public ABI bindings with authorization and session guards | Mock-only; no application entrypoint integration, SDL runtime validation or hardware validation |
| Logical profiles/deadzones | Pure source module exists | Synthetic only; current Locke UI no longer mounts ControllerProfileEditor |
| HC-equivalent game remapping, virtual controller, gyro stream, trackpads, exclusive mode | Not implemented/wired | Unsupported as Playhub deliverables, regardless of puck VID:PID |
| TDP | Outside controller scope | No claim or change |

CONTROLLER-SUPPORT.md and the earlier DELIVERABLES.md describe the previous
profile editor UI. The current ControllerSettings.tsx has only native groups
and no profile editor. Treat those older UI descriptions as stale until the UI
owner updates shared documentation. The earlier parent-integration.patch also
targets the previous ControllerSettings; do NOT apply it blindly to the new UI.
Its embedded component tests test the proposal, not the current rendered UI.

Existing haptics evidence: navigationHaptics.ts dispatches browser dual-rumble,
SteamClient.Input haptic APIs and POST /dash/haptic. AgentHost.cs registers that
endpoint; ControllerHapticsService owns SdlControllerHaptics. This audit neither
calls those paths nor treats the new adapter as their implementation.

HC reference was rechecked read-only: SteamControllerTarget derives from
VIIPERTarget; VirtualManager constructs it with 28DE:1102. This virtual original
Steam Controller target does not establish physical 2026+puck handling. No HC
code/report layout was copied. Independent code retains MIT; previous license
audit and SDL/VIIPER redistribution gates remain unchanged.

## Fixed blockers before future live-reader integration

1. SDL axis/button getter failure could be emitted as a valid neutral/released
   frame. Bind SDL_ClearError, clear only before the read, reject reported read
   errors and invalidate/close the session. A pre-existing unrelated SDL error
   does not reject a valid subsequent frame. If this session owns active rumble,
   its normal cleanup attempts stop before closing.
2. Negative SDL trigger values were silently clamped to zero. Validate the
   documented per-axis domain instead; malformed trigger data now fails closed
   rather than looking like a released trigger.
3. Revalidate SDL instance ID at the end of the sample as well as connection,
   discarding a frame if the instance changes during sampling.

The first two regressions were executed against the old implementation and
both failed (Unavailable not raised), then passed after the backend fix.
Five new tests cover getter error, invalid trigger, button-error cleanup,
unrelated stale error and mid-frame identity change.

Primary API semantics:
- https://wiki.libsdl.org/SDL3/SDL_GetGamepadAxis (zero is also the invalid-input result; triggers are nonnegative)
- https://wiki.libsdl.org/SDL3/SDL_GetGamepadButton
- https://wiki.libsdl.org/SDL3/SDL_ClearError (thread-local previous error reset)

## Tests and remaining coordination

Passed: python -B -m unittest controller_porting.test_passive_diagnostics
controller_porting.test_sdl_backend -v (24 tests).

Passed: node --test tests/controllerNative.test.mjs
tests/controllerDiagnostics.test.mjs (13 tests, inert runtime mocks).

Additional check: controllerProfiles.test.mjs passed its six pure model tests,
but its two existing UI tests failed with ControllerProfileEditor is not a
function after Locke's concurrent UI replacement. Parent notified; no shared
test or UI edits made. The combined JS run was 19 pass / 2 fail, not all green.
UI owner must replace/remove the obsolete editor integration tests in line with
the intended new UI. This is not a native backend failure.

Still required for actual HC-like live features: audited SDL binary and notices,
main-thread/ownership integration, per-device/transport hardware validation,
then real UI/RPC wiring. Game remapping additionally requires a system-wide
output backend and duplicate-input/recovery policy. No fake live controls added.
No native DLL load, live input, hardware writes, build, deploy, install, reload,
commit or push occurred. No workers were interrupted.

# Parent handoff: choose an actual deliverable

## Ready for parent review now

`parent-integration.patch` is a concrete, UNAPPLIED patch for current main.py and
src/ControllerSettings.tsx. It exposes get_controller_sdl_diagnostics with NO
renderer arguments and adds one explicitly invoked file-diagnostic action.
It leaves the synthetic profile editor and all Steam actions unchanged.

The new frontend helper uses an injected direct @decky/api call. It must NOT use
controlBackend.call/ensureControlBackend: those can reload the plugin. An absent
endpoint, timeout or malformed response produces unavailable/invalid status,
never a fallback reload or fake hardware support. No automatic polling occurs.
IT/EN copy is included with explicit English fallback for other locales.

The proposed endpoint resolves SteamPath through HKCU/Software/Valve/Steam, then
reads ONLY SDL3.dll as a bounded regular file. It does not load sdl_backend,
SDL3, enumerate controllers, query drivers or read controller input. File hashes
and timestamps are evidence of bytes observed, not approval/compatibility.
No renderer path or Steam controller index is accepted; this is host-scoped
diagnostics, intentionally independent of selected Steam controller identity.
Successful file observation keeps ALL hardware capabilities false.

This narrows/supersedes the earlier README's generic proposed inspect_library
endpoint: use passive_diagnostics.py for the RPC, not the active adapter module.

Review-check from F:/Playhub/Plugin/Playhub:

```text
git apply --recount --check --directory=Source/GamingModeDeckyPlugin Source/GamingModeDeckyPlugin/controller_porting/parent-integration.patch
```

Parent action: review/apply that patch, then include controller_porting/
passive_diagnostics.py and LICENSE in the existing installation payload and
compile the frontend helper through the normal build. The patch does NOT change
build-plugin.bat, so packaging is an explicit parent-owned gate. The passive
payload need not include sdl_backend.py or tests. Import-by-file in the patch
does not depend on Decky's Python sys.path. No service or native dependency is
needed for this diagnostic increment. No patch application/build/reload occurred
in this task; `--check` only validates applicability to current source.

## Capability/deliverable matrix

| Requested experience | Current implementation | Exact missing dependencies/work |
| --- | --- | --- |
| Steam controller settings/test/calibration/layout | Existing Steam route adapter | Real controller/UI validation by parent; not HC remapping |
| Local remap/deadzone profiles | Existing independent synthetic editor | No hardware binding or output; must remain synthetic |
| SDL file diagnostics | New file-only endpoint + frontend helper + concrete parent patch | Parent apply, payload copy, normal frontend build; no SDL install |
| Live standard buttons/sticks/triggers viewer | SDL adapter implemented; 15 mock tests | Audited SDL >=3.4.10 major 3, matching host architecture, exact hash/notices; authorized SDL main-thread host; selected instance lifecycle; real per-device/transport tests; UI/RPC frame contract |
| Physical rumble through this adapter | Real SDL API wrapper, bounded and opt-in; mock-only | Same SDL/ownership/validation; coordinate existing agent haptics, do not expose duplicate control |
| Steam Controller 2026 puck | Upstream Triton/Proteus 28de:1304 evidence, standard API candidate | Build with Triton/HIDAPI enabled; actual receiver/firmware/Steam coexistence tests; receiver VID:PID alone is insufficient |
| Gyro/accelerometer input | NOT implemented in adapter | SDL_GamepadHasSensor/SetGamepadSensorEnabled/GetGamepadSensorData binding; units/timestamps/enable rollback; motion calibration and fixtures; authorized device tests |
| Trackpad/paddle/touch input | NOT implemented in adapter beyond standard 14 buttons | SDL touchpad APIs and extra button mapping; per-device presence and touch/release fixtures; actual puck tests |
| System-wide live remapping/deadzones | NOT implemented | Physical-source identity + polling lifecycle; bind existing pure profile transforms to validated frames; system-wide output sink; neutral/release semantics; latency and reconnect tests |
| Virtual Xbox/original Steam Controller | NOT implemented | Choose exact VIIPER server/client release supporting target, MIT TCP client boundary, approved Windows USBIP client/driver, GPL server delivery audit; owned target lifecycle/feedback and hardware validation |
| Exclusive mode / duplicate-input prevention | NOT implemented | Separately approved device-hiding/filter solution such as HidHide with its own release/license/signing audit; physical/virtual identity and crash recovery; never hide before output validation |
| Desktop/per-game automatic profiles | NOT implemented | Persisted device identity, foreground/app selection, conflicts, rollback and actual output backend; localStorage slots are not these features |

Live standard input and a live viewer are the smallest meaningful HC-like next
feature. They do NOT require virtual output or a new kernel driver when the
audited SDL backend already handles the physical device. SDL initialization/open
can still write HID settings, so live validation needs separate authorization.
Actual remapping to games is a larger deliverable and cannot be honestly sold as
complete by connecting a reader to the synthetic preview alone.

SDL's in-process virtual joystick API is NOT a system-wide Windows output sink.
No VIIPER or HidHide release/driver package is selected or approved here; selecting
one is a concrete dependency decision, not a dependency already available.
Do not infer system-wide output readiness from an existing ViGEm/USBIP install.

## Verification and packaging

Offline commands from GamingModeDeckyPlugin:

```text
python -B -m unittest controller_porting.test_passive_diagnostics controller_porting.test_sdl_backend -v
node --test tests/controllerDiagnostics.test.mjs
node node_modules/typescript/bin/tsc --noEmit
```

Tests include actual proposed frontend panel code extracted from the review
patch, proving no call on mount, duplicate-click suppression and no state update
after unmount with inert React/Decky mocks. Python tests execute the passive
endpoint separately, without importing main.py or its hardware-owning parent.
Real Decky RPC dispatch, rendered layout/focus, hardware input/output and packaged
runtime remain NOT validated. Artifact/license decisions are machine-readable in
license-artifact-manifest.json. No SDL/driver install, live write, reload, commit
or push was performed. Parent remains sole integration coordinator.

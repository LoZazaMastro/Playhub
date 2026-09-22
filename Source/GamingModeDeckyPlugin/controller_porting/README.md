# Controller porting boundary

Status: independent implementation, mock-tested, NOT hardware-validated.
No existing entrypoint, controller editor, agent or installation payload changed.

## Implemented

- `sdl_backend.py`: Windows SDL3 cdecl bindings, exact caller-approved DLL hash,
  runtime version/export checks, explicit I/O authorization, owned subsystem
  reference, enumerated instance selection, session token invalidation,
  standard button/axis reads and separately authorized bounded physical rumble.
- `inspect_library(path)`: file-only SHA256 observation; no DLL loading,
  controller enumeration, device initialization or invented readiness.
- `test_sdl_backend.py`: mocked SDL ABI only. No native library executes.

This is a library boundary, not a running sidecar process. Import and session
construction are inert. Nothing schedules polling, launches a worker, installs a
driver, invokes Steam UI or calls an existing parent backend.

## Input/output capability matrix

| Device/path | Implemented boundary | Not established |
| --- | --- | --- |
| Xbox/XInput-backed SDL gamepad | Runtime-reported buttons/axes and rumble | Local device validation, physical vs virtual identity |
| DualShock/DualSense, Switch-compatible SDL gamepad | Same standard SDL API, only reported controls | Per-model/transport validation, advanced feedback |
| Original Steam Controller / Steam Deck | Same runtime-gated API | Ownership conflict, mapping coverage, local validation |
| Steam Controller 2026 + puck | SDL Triton upstream candidate, same runtime gates | Exact DLL build features, firmware/transport/live behavior |
| Arbitrary HID / receiver without opened SDL gamepad | Unavailable | No VID:PID-based parser or capability inference |
| Virtual gamepad/virtual Steam Controller output | NOT implemented | External backend, driver, distribution and safety gates |

SDL 3.4.10 has `SDL_hidapi_steam_triton.c`; its dongle dispatch references
Proteus/Nereid, and `usb_ids.h` identifies Proteus as PID 0x1304. This is stronger
evidence than inferring support from HC's virtual original-controller target.
See pinned links in PROVENANCE.md. Runtime version alone does not establish
that a custom SDL binary includes Triton or that its firmware path works.

The boundary returns only the standard 14 profile buttons and six SDL axes.
Axes 0/1 and 2/3 are sticks; Y is SDL positive-down. Axes 4/5 are triggers [0,1].
Missing controls are omitted, not synthesized. No deadzone or remapping is
applied. `backendKind=mock` in tests is retained in frames and capabilities.
Instance IDs and tokens are session-local, never persistent device identities.
VID:PID may describe a virtual source and does not establish a physical device.

`rumbleReported` is a runtime API report, NOT permission to show a working UI.
`hardwareValidated`, `liveMapping`, `virtualOutput`, `gyro`, `trackpads` and
`exclusiveMode` remain false. No gyro/trackpad reading or sensor activation is
implemented even if SDL supports those features. No SDL virtual joystick is
used: an in-process virtual joystick is not a system-wide Windows gamepad.

## Exact next parent integration step

1. Add a parent-reviewed diagnostic endpoint that calls ONLY `inspect_library`
   on an explicitly configured file, returning schema/version/hash and disabled
   hardware capabilities. Do not load SDL in this passive endpoint.
2. Audit and pin an official, architecture-matched SDL build with its notices.
   Do not copy Steam's private DLL into the installer. Approving a hash is a
   deployment policy decision, not automatic acceptance of whatever file exists.
3. Establish an SDL main-thread owner in the host. Do not call this API directly
   from arbitrary Decky async/RPC worker threads. Existing agent SDL haptics must
   not concurrently control the same device; coordinate ownership first.
4. After separate live authorization, construct NativeSDL with the approved
   absolute path/hash and `authorize_device_io=True`, create ControllerSession,
   call start with the same explicit authorization, enumerate instances, then
   open the selected instance. Use the returned session token for read/rumble.
   Always call close in finally. No reconnect or index substitution is automatic.
5. Hardware validation must cover unplug/replug, two identical devices, Steam
   running/stopped, mapping fidelity, puck firmware, input latency, rumble timeout,
   close/crash behavior and SDL coexistence. Record the exact binary hash, device,
   transport and firmware. This folder contains NO evidence of these tests.
6. Only then integrate an explicitly labeled live-input view. Feeding existing
   profiles into real game output remains blocked until a system-wide output
   backend and duplicate-input/rollback policy are independently implemented.

IMPORTANT: SDL initialization, enumeration, opening, updating and closing can
cause HID configuration writes (including lizard-mode changes). None is a
read-only diagnostic. The adapter changes no SDL hints, but this does not make
SDL's internal device handling passive. Rumble stop is best effort; an abrupt
process crash cannot guarantee cleanup. A 250 ms command limit is not a tested
hardware watchdog. Parent must not claim crash-safe exclusive ownership.

## Verification

Run from GamingModeDeckyPlugin:

```text
python -B -m unittest controller_porting.test_sdl_backend -v
```

Only new files are tested. No native DLL was loaded, no controller was opened,
no hardware write occurred, and no service/sidecar/game/UI process was started.
The ordinary short-lived test runner is the only execution added for verification.
No commit, push, install, reload or entrypoint integration was performed.

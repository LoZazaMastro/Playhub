# Owned target gate and runtime review

Status: source and mock tests only. No native virtual target was created, no SDL
device opened, and no driver installed. main.py is unchanged by this slice.

## Implemented path

helper_native.native_pipeline accepts host-only target_bus_instance. Its output
factory captures a pre-add snapshot using WindowsTargetRegistry, then constructs
OwnedTargetVerifier before OutputLease starts. Target verification requires all:

- Exact approved parent devnode, whose SERVICE is ViGEmBus.
- New complete device instance path absent from the pre-add snapshot.
- Hardware VID/PID matching the owned native target's getters.
- Both CM_DRP_ADDRESS and CM_DRP_UI_NUMBER equal native get_index (SerialNo).
- Exactly one matching PDO; native properties unchanged across OS enumeration.
- Subsequent reports keep the same client/target handles and full Node identity.

A physical controller with identical VID/PID, an older virtual target, a wrong
parent, replacement instance, ambiguous match, missing or malformed property all
fail closed. The full PnP instance path is bound; a separate HID interface path or
USB string serial is not invented. The driver's decimal instance suffix is not
used as a serial parser. XInput user index is NOT the ViGEm serial.

The native SDK does not expose its selected bus handle publicly. Consequently the
host must supply a previously approved exact bus instance; enumeration on a
different bus fails, never falls back to a global VID/PID match. Driver signature,
binary hash and version approval are separate prerequisites, not established by
SERVICE text. PnP enumeration can lag add: this implementation rejects immediately
and cleans up rather than accepting without evidence.

WindowsAncestry also reads each ancestor SERVICE: ROOT\\SYSTEM\\000x can be a
ViGEm bus despite not spelling VIGEM in the path. Name-only exclusion was fixed.
SteamInput/Agent coexistence approval remains mandatory. A Playhub named mutex
does not lock out Steam or GamingModeAgent. Sensor capability comes from the
opened physical provider and actual events, never VID/PID. A remapped virtual
XInput device cannot supply DSU motion. Sensorless/stale frames disconnect DSU.
No Agent stop, haptic cancellation in another process, or system-wide rumble
reset is implemented. Feedback must remain disabled until exclusive feedback
ownership is approved; this code cannot infer that other clients are idle.

## Process supervision and precise residual

HelperSupervisor owns one Popen process and a bounded HelperClient. Heartbeats run
off the SDL main thread. On control failure it kills only that process handle,
waits with timeout, and requires a host-owned OS absence probe before reporting
closed. Failure is cleanup_unconfirmed or process_exit_unconfirmed, with no
automatic restart and no broad bus reset. Tests kill only a sleeping Python mock.

The supervisor is an integration component, NOT an enabled native launcher.
Remaining wiring before native launch: parent must own the subprocess from birth,
retain independently validated target evidence outside the child, and supply a
bounded read-only absence probe. A callback blocked inside an OS call cannot be
interrupted by Python; its execution is not a hard real-time guarantee. A crash
before successful target binding cannot be certified absent by this verifier.
Parent-death Job Object kill-on-close and approved-driver live crash/removal tests
are still missing. No guarantee of driver cleanup on process death is claimed.

Audit: ViGEmClient commit b66d02d57e32cc8595369c53418b843e958649b4
src/ViGEmClient.cpp get_index returns SerialNo; get_vid/get_pid are local fields;
is_attached is local state. vigem_disconnect waits INFINITE for DS4 pickup thread.
ViGEmBus commit d986e1d93708ec9b11049542fa6027272cce716c:
sys/EmulationTargetPDO.cpp assigns Address/UINumber from SerialNo and formats the
instance ID from it. sys/Driver.cpp Bus_FileClose marks only matching SessionId
children missing, with failure paths. This source commit has NOT been matched to
the installed signed driver binary. KMDF calls FileClose only after outstanding
I/O completes/cancels, so source inspection cannot establish a cleanup deadline.

Sources:
- https://github.com/nefarius/ViGEmClient/blob/b66d02d57e32cc8595369c53418b843e958649b4/src/ViGEmClient.cpp
- https://github.com/nefarius/ViGEmBus/blob/d986e1d93708ec9b11049542fa6027272cce716c/sys/EmulationTargetPDO.cpp
- https://github.com/nefarius/ViGEmBus/blob/d986e1d93708ec9b11049542fa6027272cce716c/sys/Driver.cpp
- https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/wdfdevice/nc-wdfdevice-evt_wdf_file_close
- https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_get_devnode_registry_propertyw

## Proposed reproducible binary intake (not yet approved or packaged)

- SDL official release-3.4.10, source commit
  8e37db5e797b6167f3a00d697d816a684bd259c7, Windows x64, zlib license.
  Mendel owns isolated official release staging and SHA256/notices manifest.
  Steam SDL3.dll is excluded from redistribution and from this artifact choice.
  Staged by Mendel at
  `F:\Playhub\Plugin\Playhub\temp\controller-runtime-20260908-70bb0e23\sdl\SDL3.dll`.
  Independently read on disk (not loaded): PE FileVersion 3.4.10.0, 2804736 bytes,
  SHA256 `C39FBDA24ECA1009B06A4D4E340D12511E3C8B0D44C4898D29E336E2CC7A25F0`.
  This identifies the staged bytes; it does not approve live device access.
- ViGEmClient source b66d02d57e32cc8595369c53418b843e958649b4, MIT;
  build the DLL configuration, not static library. Upstream project is
  src/ViGEmClient.vcxproj / ViGEmClient.sln, toolset v142, Windows SDK defaults
  to floating 10.0, and CI uses VS2019. Pin exact MSVC, MSBuild and Windows SDK
  versions in the build manifest rather than inheriting those defaults.
- Record source archive SHA256, recursive gitlink revisions, build arguments,
  environment, compiler/linker hashes, PE architecture, imports/exports, output
  hashes and complete original licenses. Build twice in clean fixed-path trees;
  compare hashes before asserting bit reproducibility. Review deterministic
  compiler/linker settings and generated version/timestamp inputs if they differ.
- No binary hash is guessed here. A source commit is not a DLL hash or a license
  approval. No Steam DLL, HC binary, CC-BY-NC-SA table, or driver is copied.
  Client MIT and SDL zlib do not license HC's separate CC-BY-NC-SA code; mere
  attribution does not satisfy noncommercial/share-alike obligations.

## Verification command

From Source/GamingModeDeckyPlugin:

```powershell
python -B -m unittest controller_porting.target_identity_test controller_porting.helper_supervisor_test controller_porting.helper_test controller_porting.reader_test controller_porting.output_test controller_porting.dsu_test controller_porting.dsu_bridge_test controller_porting.test_sdl_backend controller_porting.test_passive_diagnostics
```

93 tests passed on 2026-09-08, including real mock-process termination, loopback IPC
and UDP, registry fixtures, stale identity and physical/virtual same-VID/PID cases.
These are not hardware or game compatibility results.

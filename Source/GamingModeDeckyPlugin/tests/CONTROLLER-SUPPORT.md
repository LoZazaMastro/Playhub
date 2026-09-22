# Controller support boundary - 2026-09-08

## Implemented in Playhub source

ControllerSettings.tsx uses controllerNative.ts, an adapter to the existing Steam
ControllerStore and Decky Router. No additional service or HID reader is installed.
The existing navigation-haptics controls are preserved without duplication.

The controller tab now offers device selection, reported name and VID:PID,
Steam controller settings, native input testing, stick/deadzone calibration,
gyro calibration, and the running game's Steam Input layout editor.
These actions are native Steam pages, NOT an implementation of HC's remapping
engine, virtual output, desktop profiles or exclusive device ownership.

ControllerSettings also embeds an independent Playhub logical-profile editor:
- Three local slots, explicit save, discard and draft-only default reset.
- Standard digital-button mapping, including disabled and many-to-one bindings.
- Separate left/right radial deadzones, bounded to 0..50 percent.
- Synthetic button and half-deflection stick previews, never live input.
- Versioned, strictly validated localStorage documents; corrupt reads and failed
  writes are visible and do not silently overwrite existing profiles.
- Slot switching is blocked while a draft differs from the loaded profile.

Implementation: src/controllerProfiles.ts. These are unbound logical templates,
not per-device or automatically selected per-game profiles. Steam device indices
are deliberately not used as persistent hardware identity. Saving does not apply
anything to a controller. Local storage is scoped to the current Steam web origin,
not an agent-backed or cloud-synchronized profile store. New editor strings are
Italian/English, with explicit English fallback for the other existing locales.

The pure mapping/deadzone engine consumes normalized synthetic frames. It has no
HID reader, transport, RPC, virtual-device encoder or hardware output callback.
controllerProfileCapabilities exposes local editing/preview only; live mapping,
virtual output, gyro, trackpads and exclusive mode remain false. HC source and its
report formats are not used in this independent implementation.

Capability data comes from GetControllers().unCapabilities. Gyro uses bit 11;
sticks use bits 2/3. Missing capabilities differ from an unsupported feature.
Index, identity, capabilities and running app are rechecked on click. The adapter
does not write settings, change controller mode, read HID reports, or create a device.
The 2026 puck's VID:PID alone is never treated as evidence of feature support.

Native API evidence: installed Steam steamui/chunk~2dcc5aaf7.js contains
GetControllers/GetController, the capability bit definitions, and routes:
- /settings/controller/controller/{controllerIndex}
- /controller/devicesupport/{controllerIndex}
- /controller/calibration/{controllerIndex}/Inputs
- /controller/calibration/{controllerIndex}/Gyro
- /app/{appid}/controllerconfigurator/main
@decky/ui/dist/modules/Router.d.ts declares Navigate(path: string).

Automated checks: tests/controllerNative.test.mjs. No live UI navigation or
calibration was performed. Parent must test focus, native navigation and return
with the actual controller before describing these actions as live-verified.

## HC virtual Steam Controller is a different feature

Read-only archive root:
C:/Users/Andrea/AppData/Local/Temp/hc-study-80c450afcc76456bb02ef75eedba733a/HandheldCompanion-main

Evidence relative to that root:
- HandheldCompanion/Targets/SteamControllerTarget.cs: VIIPERTarget subclass,
  DeviceType steamcontroller, 64-byte input report, 250 Hz master override;
  encodes controls/motion and processes feedback.
- HandheldCompanion/Managers/VirtualManager.cs:484 constructs that target with
  VID:PID 28DE:1102 (wired original Steam Controller), not the 2026 puck ID.
- HandheldCompanion/Targets/VIIPERTarget.cs: Connect obtains a bus, adds a device,
  registers feedback; input is submitted through ViiperService.SetInput;
  cleanup removes the created device.
- HandheldCompanion/Targets/Viiper/LibViiper.cs: native P/Invoke to libviiper,
  including initialization, bus/device lifecycle, input, feedback and device types.
- HandheldCompanion/HandheldCompanion.csproj:315 packages libVIIPER.dll.
- Controllers/Steam/GordonController.cs is physical original-Steam-controller
  handling; NeptuneController.cs:324 consults SteamControllerMode when unhiding.
  Those physical/Steam Deck paths do not establish physical 2026 support.

HC LICENSE.md is CC BY-NC-SA 4.0. No HC source, report encoder or DLL is copied.

## VIIPER dependencies and distribution gate

Official installation documentation:
https://alia5.github.io/VIIPER/stable/getting-started/installation/

It distinguishes a separate TCP server with MIT client libraries from linking
libVIIPER into an application, which it says requires GPL-compatible licensing.
The core is GPL-3.0-or-later. A separate-process integration is the candidate to
audit for Playhub; it does not remove redistribution obligations for the server.
Do not import HC's P/Invoke adapter or bundle its DLL as a shortcut.

Windows requires a compatible USBIP client/kernel driver. Official docs also
warn about trusted-root-CA installation by some usbip-win2 releases. Driver
version, signing, Secure Boot/HVCI compatibility and packaging need an explicit
audit and separately authorized installation, not an automatic dependency.

Pinned evidence supersedes any missing feature entry in the upstream README:
https://github.com/Alia5/VIIPER/tree/679f7e0e4e8ed73addfd7c570f1bc42489bf6be7/device/steamcontroller
contains the official Steam Controller device implementation and its tests.
Read-only verification of HC's HandheldCompanion/libVIIPER.dll on Windows:
- SHA256 E2802542C7FB632914384AC3DBD4A09FA378A9BE91D04EBE822648B15E4960F7
- Embedded revision 679f7e0e4e8ed73addfd7c570f1bc42489bf6be7.
No DLL was loaded or executed. Embedded metadata establishes the reported build
revision, not independently reproducible binary provenance. This supports the
virtual original Steam Controller target, not physical 2026 PID 1304 parsing.
An arbitrary installed server/release still needs a non-mutating device-type
query before it can qualify for activation. Absence from README is not a reason
to deny virtual Steam Controller support.

Read-only local Win32_SystemDriver query found mausbip stopped/manual and
ViGEmBus running/system. These do not prove compatible usbip-win2 availability.
No VIIPER integration was found in the searched Playhub/GamingModeAgent C# sources.

## Concrete next implementation gates

1. Pin and audit a VIIPER server release/fork that actually advertises the desired
   output type; record hash, source revision, license notices and source delivery.
2. Add an isolated capability probe to the existing agent: server version,
   supported device types, driver readiness and selected physical source identity.
   A probe must not create/attach devices or start/install drivers.
3. Implement a mockable lease with idle/probing/ready/creating/active/restoring/error
   states. No activation until source reader, target type and driver all qualify.
4. Implement the input source separately: 2026 USB/puck reports, gyro, trackpads,
   buttons and reconnect need validated parsing and captured-fixture tests. Steam
   route availability does not provide this input stream.
5. Only after separate live authorization: create one target, verify enumeration,
   stream neutral-first input, route feedback, then remove only the owned target.
   Never hide the physical device before virtual output is confirmed; restore on
   timeout/crash/disconnect. HidHide/exclusive mode is another opt-in, not default.
6. Test duplicate-input prevention, Steam ownership conflict, hotplug, cancellation,
   focus, latency, feedback and per-game profile rollback before exposing modes.

Until these gates pass, live virtual Steam Controller output and HC-equivalent
live remapping remain unimplemented and must not appear as enabled controls.

## Windows verification for the local-profile increment

Executed without Steam navigation, live input, driver changes or agent calls:
- node --test tests/controllerProfiles.test.mjs tests/controllerNative.test.mjs
  (14 passing tests, including actual editor JSX evaluated with inert UI mocks).
- node node_modules/typescript/bin/tsc --noEmit (passed).

Tests cover strict profile schema, isolated persistence, corruption/quota errors,
mapping/release semantics, radial normalization, invalid synthetic input, disabled
capabilities, dirty-draft protection, save/reload and existing native route gates.
Native Decky focus, rendered geometry and real controller use are not live-tested.
No installation payload or parent-owned ControlCenter/haptics files were modified.

## Proposed agent contract (not implemented or called)

Parent review is required before editing existing app/agent files. Suggested
read-only endpoint: controller diagnostics v1 with observation timestamp, physical
source identity and validated reader status, exact external server version/hash,
supported target types, driver readiness and structured unavailable reasons.
No start/install/create/attach/hide/submit operation belongs in that endpoint.
Unknown or stale fields must keep activation disabled. The separate-process MIT
client route needs its own distribution audit before adding any dependency.

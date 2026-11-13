# Playhub control center

The integrated Quick Settings code is copyright its original contributors and
distributed under MIT. Its runtime, dependencies, notices and license texts are
preserved in `quick_settings/`. RyzenAdj attribution and historical license
materials are retained, but its DLL is excluded from the current runtime payload.

Panel de Control by Hooandee was studied for handheld controls, firmware
capability selection, power transactions and panel organization. Its GPL-3.0-only source, compiled
frontend, assets and Linux hardware backends are not included in Playhub.

Handheld Companion was studied as an architectural reference for capability
detection, profile ownership and device-specific controls. Its CC BY-NC-SA 4.0
code and separately licensed controller/driver components are not included.
Playhub does not install its virtual controller, HidHide or hardware drivers.

The new panel navigation and preference handling are original Playhub code.
Tabler icons are supplied through the existing react-icons dependency; the
existing Tabler attribution and license remain applicable.

The experimental MSI Claw 8 EX AI+ CG3EM firmware adapter is an independent
implementation of the ACPI-WMI protocol documented and measured by
Stev3FrencH/ClawConfigurator (MIT), revision
`fdb2e3acfbafe94a584425f1185a616d0e329c42`. Its notice is preserved in
`rgb/LICENSE-ClawConfigurator.txt`. The adapter uses Windows' existing WMI
provider and includes no MSI binaries, third-party kernel driver or firmware.
Hardware references: https://github.com/Stev3FrencH/ClawConfigurator/blob/fdb2e3acfbafe94a584425f1185a616d0e329c42/docs/hardware-notes.md

ASUS Ally preset commands independently implement the public ASUS ACPI-WMI
interface. The `AsusAtkWmi_WMNB` method names and command acknowledgement were
checked against seerge/g-helper, `app/AsusACPI.cs` revision
`0bda6b55317f67c0ad68f3b5366a0e12a9096c53` (GPL-3.0). No G-Helper code or binary
is included. Commands use the device's existing Windows firmware provider;
Playhub does not claim that its acknowledgement measures the active BIOS mode.

The read-only Ally fan sensor uses the same documented DSTS interface. Fan
endpoint identifiers and units were checked against `app/AsusACPI.cs` (GetFan)
and `app/HardwareControl.cs` (ReadSensorsOverlay) at the same G-Helper revision.
This is an independent implementation, not a distribution of G-Helper code.
No battery charge-limit readback is claimed or synthesized from saved settings.

Independent CPU/GPU temperature reads use the separate ASUS endpoints
documented by the same G-Helper revision. Experimental RC73XA sustained/boost
limit transactions were checked against the ASUS protocol reference in
Hooandee/panel-de-control at `ea141c754373629e82471c4d9f54767e40da6da5`.
No source from those projects, ASUS driver or firmware is distributed.
Readback refers to firmware settings, not measured package power.

The experimental OneX type-2 fan adapter independently implements protocol
facts documented by hanxiao1982/steamos-onexplayer at
`69f31be384f11029fe7762ef4c19837637889fbf`, in
`Documentation/wmi/devices/oxp-wmi.rst`, `docs/ec/oxp-wmi.md` and
`docs/ec/onexconsole-dmi-ec-profiles.md`. That project's Linux driver is
GPL-2.0-or-later; its driver code and documentation prose are not included.
Playhub uses the existing Windows firmware provider for fan mode/duty and
independent tachometer/CPU temperature reads. No OneXConsole binary,
Handheld Companion implementation, EC driver or firmware is included.

The experimental Lenovo Go S / Go 2 adapter independently implements the
Other-Mode, capability-data and GameZone WMI protocol facts in Linux revision
`7e1392a42ea288d70a266f6553fe06f57f8647ea`, under
`drivers/platform/x86/lenovo/` and `Documentation/wmi/devices/`.
The Windows MOF/ACPI interface was cross-checked with hhd-dev/hwinfo at
`d9508123bee46ae3a59dc24826030ef51ea95058`; machine identities were checked
against Lenovo PSREF. No kernel implementation, ACPI table, firmware or Lenovo
binary is distributed. Limits and capability bounds come from the machine's
existing Windows firmware provider; they are settings, not measured package
power. Support is experimental and limited to the documented machine identities.

The experimental AYANEO 3 controller adapter adapts the HID wire format and
colour handling from Rayekkk/Ayaneo3Companion at
`bed273c04ccf1c9bfd31caf1ecc4c4c6b20e23c7`. Its BSD-3-Clause license and
attribution are preserved in `quick_settings/licenses/AYANEO3-BSD-3-Clause.txt`
and `AYANEO3-NOTICE.txt`. It configures RGB lighting and vibration intensity
through the existing vendor collection. No gamepad report, driver, firmware or
AYASpace binary is distributed.

## Accade oggi editorial imagery

Bundled editorial images retain their original rights. Per-image source pages, subjects, providers and attribution are recorded in `quick_settings/history_images.json`. Screenshots and covers belong to their respective game publishers and rights holders; IGDB and NFS-Planet are source providers, not copyright owners. Commons portraits retain the attribution and license URLs in that manifest. Editorial text is original Playhub copy, not a Wikipedia extract.

The Shortcuts settings renderer, styles, translations and icon catalog are now
integrated in Playhub Decky, Copyright (C) 2026 LoZazaMastro, GPL-3.0-only.
The complete license is included in LICENSE-Shortcuts. Source is maintained in
src/QamShortcutsView.tsx and src/qamIcons.ts. Tabler icon artwork is MIT; its
original copyright and license notice are preserved in src/qamIcons.ts.

## GPD V1 controller settings

The optional GPD Win4 / Mini 2024 integration uses protocol facts from
OpenWinControls/libOpenWinControls 63fd9062a41003c2e766f556d058d7ad707fc062
and OpenWinControlsCLI b4b191bc3b1f12e97e34745f8a495343b06ebd0a.
Copyright (C) 2026 kylon. These sources and the corresponding GPD adapter are
GPL-3.0-or-later; the license is included as quick_settings/licenses/GPD-GPL-3.0.txt.
Adapter source is distributed as quick_settings/gpd_v1_controller.py and
quick_settings/gpd_v1_windows_hid.py. No HandheldCompanion code, kernel driver,
HIDAPI binary or GPD vendor executable is distributed by this integration.

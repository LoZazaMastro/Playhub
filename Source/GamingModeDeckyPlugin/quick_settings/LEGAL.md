# Licensing & legal notes

Quick Settings is licensed under the **MIT License** (see `LICENSE`). This file
explains how features inspired by **GoTweaks** were integrated while keeping the
project under MIT, and which components were deliberately excluded.

## Can GoTweaks features be reused? Yes - with conditions

GoTweaks has a **layered license**:

- GoTweaks' **own source files** are **MIT**. They may be reused and ported with
  attribution.
- GoTweaks' **shipped binaries** are **GPL-3.0**, because they link
  `libviiper.dll` (VIIPER). Linking a GPL-3.0 library produces a *combined work*
  that must itself be conveyed under GPL-3.0.

### The golden rule we followed
**We never include, link, or derive from `libviiper.dll` or any VIIPER /
controller-emulation code.** Therefore Quick Settings is *not* a combined work
with VIIPER and is *not* forced to GPL-3.0. It stays MIT.

VIIPER in GoTweaks only powers controller emulation (button remapping,
joystick-to-mouse, gyro-to-stick) - all of which is Legion Go / handheld
controller functionality that this project intentionally drops anyway.

## What was actually ported

Only MIT-licensed source or techniques from GoTweaks were adapted for the Decky
stack. GoTweaks is a C#/.NET Xbox Game Bar project; Quick Settings uses a
TypeScript/Python Decky frontend and backend, plus a small standalone ADLX
helper:

- Windows power-mode overlay via `powrprof`.
- Display resolution / refresh-rate via `ChangeDisplaySettingsEx`.
- Historical AMD TDP integration via RyzenAdj (not shipped in the current runtime).
- Radeon feature and display-color access through AMD ADLX.

These are credited in `NOTICE`.

## Historical third-party library: RyzenAdj (LGPL-3.0)

Earlier packages included `bin/libryzenadj.dll` (RyzenAdj), licensed under
**LGPL-3.0-or-later**. The current Playhub runtime excludes this binary; the
legacy loading path is disabled. Attribution and historical source-offer
information remain in `licenses/RyzenAdj-LGPL-3.0.txt`, with the license
materials retained. This exclusion does not remove obligations associated with
earlier distributions. Quick Settings' own code remains MIT.

## Components deliberately excluded

| Component | License / issue | Why excluded |
|---|---|---|
| `libviiper.dll` (VIIPER) | GPL-3.0 | Would force the whole project to GPL-3.0. Only needed for controller emulation (Legion Go). |
| Controller / gyro / RGB / fan / battery-limit code | Legion Go / GPD specific | Out of scope: the goal is "works on any Windows PC". |
| `WinRing0x64.dll`, `inpoutx64.dll` | ring0 drivers, flagged by Windows Defender | Not safe to ship broadly; need elevation and trip antivirus. |
| `GamepadMotion.dll` | MIT but controller-only | Not needed without controller features. |
| RTSS / OSD overlay | RTSS is proprietary freeware | Not redistributable; would require the user's own RTSS install + a native helper. |
| AutoTDP and hardware sensor stack | Requires continuous telemetry plus device-specific libraries | Too invasive for a lightweight Decky settings plugin. |

## Feature portability summary

- **Any Windows PC:** audio, volume, brightness (dimmer), HDR, power mode,
  resolution and refresh rate.
- **AMD only (auto-detected, hidden otherwise):** supported TDP, Radeon 3D
  features and display color controls.

## Disclaimer

This is a good-faith analysis, not legal advice. If you intend to distribute
commercially, consider having the licensing reviewed by a professional.

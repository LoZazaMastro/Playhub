# Third-Party Notices

Quick Settings is distributed under the MIT License (see `LICENSE`). It also
distributes or builds upon the following third-party components.

## RyzenAdj — historical dependency (LGPL-3.0-or-later)

Copyright (c) Jiaxun Yang and the RyzenAdj contributors.
Source: https://github.com/FlyGoat/RyzenAdj

Earlier Quick Settings packages included `libryzenadj.dll` for AMD TDP control.
The current Playhub runtime excludes that binary and does not enable the legacy
RyzenAdj path. Attribution, historical source-offer information and license
materials remain in `licenses/RyzenAdj-LGPL-3.0.txt` and `licenses/GPL-3.0.txt`.

## GoTweaks — techniques and MIT source (MIT)

Copyright (c) Microsoft Corporation and the GoTweaks contributors.
Source: https://github.com/corando98/GoTweaks

Some performance/display/TDP control techniques were reimplemented from
GoTweaks' MIT-licensed source files. No GoTweaks binaries and no
VIIPER/`libviiper.dll` (GPL-3.0) code are included. See `LEGAL.md`.

## Decky Plugin Template (BSD 3-Clause)

Copyright (c) 2022-2024, Steam Deck Homebrew.
Source: https://github.com/SteamDeckHomebrew/decky-plugin-template

The plugin's build/runtime scaffolding is based on this template.

## decky-lsfg-vk (BSD 3-Clause)

Its per-game profile and lifecycle design was studied while implementing the
Windows Lossless Scaling coordinator. Linux and Vulkan implementation code is
not distributed by Quick Settings.

---

For the full reasoning on what is and is not included (and why this project
stays MIT), see `LEGAL.md`.

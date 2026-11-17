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

## Lucide Gamepad2 icon (ISC)

The Dashboard keyboard-shortcut illustration uses the Lucide Gamepad2 path,
with the stroke adjusted to match the keyboard keys.
Source: https://github.com/lucide-icons/lucide/blob/main/icons/gamepad-2.svg

Copyright (c) 2026 Lucide Icons and Contributors

Permission to use, copy, modify, and/or distribute this software for any
purpose with or without fee is hereby granted, provided that the above
copyright notice and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.

---

For the full reasoning on what is and is not included (and why this project
stays MIT), see `LEGAL.md`.

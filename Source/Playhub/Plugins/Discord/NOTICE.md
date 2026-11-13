# Third-party software

Discord for Playhub is a Windows fork of **Steamcord 1.45.1**, by Necrosiak,
from commit `65b183e39e31c253dad7cb9b42c799d67f878f2f`. Its BSD-3-Clause
license, original copyright notices and community credits are retained in
`LICENSE` and `CREDITS.md`. The Windows runtime isolation, offline packaging,
Playhub interface and associated tests are changes by LoZazaMastro.

The plugin includes these separate, unmodified runtime components:

| Component | Version/source | License |
| --- | --- | --- |
| Vesktop | 1.6.8, `4bf170e0acaf6488d3b97b19dba53bbc3c1d655d` | GPL-3.0-or-later |
| Vencord | 1.15.17, `d07030c0968e31e41caa0c35fda0ef8ae63a079e` | GPL-3.0-or-later |
| arRPC | `1dc801e8ea14d96ce8dccff416417614f058afb8` | MIT |
| Electron | 44.9.3, within the official Vesktop portable archive | MIT and bundled third-party notices |

The preferred source archives for Vesktop, Vencord and arRPC are included in
`runtime/sources`. They contain their build scripts and dependency locks.
`runtime/SOURCE.md` documents source provenance and build entry points.
`runtime/licenses` contains full licenses and dependency notices verified
against the corresponding locks. The portable archive retains Electron's
`LICENSE.electron.txt` and `LICENSES.chromium.html` unchanged; both are also
kept when the runtime is unpacked.

Electron source is available at
https://github.com/electron/electron/tree/v44.9.3. Its DEPS and third-party
notices identify the corresponding Chromium and Node sources. Electron's
build instructions are at https://www.electronjs.org/docs/latest/development/build-instructions-gn.

The plugin's vendored Python modules retain their licenses in
`py_modules/dbus_next/LICENSE` and `py_modules/segno/LICENSE`. The frontend
dependency notices are included in `FRONTEND-NOTICES.txt`.

Discord and the Discord logo are trademarks of Discord Inc. This community
plugin, Steamcord and Vesktop are not affiliated with or endorsed by Discord.

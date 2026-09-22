<div align="center">

# Playhub for Decky

### Your Windows gaming controls, together in the Quick Access Menu.

Dashboard, Gaming Mode, Plugin Store and the controls previously offered by Quick Settings, organized into customizable tabs.

[![Version 2.0.0](https://img.shields.io/badge/Version-2.0.0-ffffff?style=for-the-badge&labelColor=111111)](../../README.md)
[![MIT License](https://img.shields.io/badge/License-MIT-ffffff?style=for-the-badge&labelColor=111111)](../../LICENSE)

</div>

## Your panel, your order

Switch between focused tabs and keep the ones you use within reach.

- **Session:** Dashboard, Plugin Store, Gaming/Desktop switching and sign-in mode.
- **Audio:** output and microphone volume, device selection.
- **Display:** brightness, resolution, refresh rate and supported HDR controls.
- **Performance:** Windows power mode, power-source status and supported TDP controls.
- **Graphics:** Lossless Scaling and the Radeon features available on your GPU.
- **Controller:** navigation feedback and intensity.
- **System:** helper status and diagnostic reports.

Use the settings button to reorder or hide tabs. At least one tab stays visible.
Hiding a tab does not disable its features or change its settings. Expandable
sections remember their state. Sliders use Steam's native controls.

The Playhub entry in a game's options retains Lossless Scaling and Radeon
profiles, automatic activation and global-state restoration. Lossless Scaling
must be installed separately. No virtual controller or ring-0 drivers are installed.

## Installation and migration

Install through [Playhub](https://github.com/LoZazaMastro/Playhub). The desktop app ships and repairs the frontend, backend and helpers together.

The standalone Quick Settings folder is retained in `homebrew/playhub-plugin-backups`.
Existing profiles are imported without overwriting profiles already saved in
Playhub. Restart Steam after installing this update so Decky unloads the old
plugin and starts the integrated backend.

## Development

```powershell
pnpm install
pnpm run test
pnpm run build
```

The bundle is generated in `dist/index.js`. Backend tests run with
`python -B -m unittest discover -s tests -p test_panel_backend.py`.

## License and credits

Playhub code is [MIT licensed](../../LICENSE). Quick Settings retains its
original notices and dependency licenses. See [third-party notices](THIRD-PARTY-NOTICES.md).

Thanks to Juan Diego MaLó (Hooandee) for his work on custom QAM tabs, and to
[Tabler](https://tabler.io/icons) for the icons.

<div align="center">

Created and maintained by **[LoZazaMastro](https://github.com/LoZazaMastro)**.

</div>

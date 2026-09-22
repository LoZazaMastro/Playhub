# Provenance and distribution gates

Reviewed 2026-09-08. Engineering license audit, not legal advice.

## Independent code

All Python implementation/tests in this folder are newly written against public
SDL3 function signatures. No HC source, report layout, encoder, native DLL,
driver, SDL implementation or third-party wrapper is copied or translated.
Code is under the existing Playhub MIT license, reproduced in LICENSE.
No new dependency is bundled. Python ctypes/hashlib are standard-library APIs.

## Read-only local references

- Existing `src/controllerNative.ts`, `src/controllerProfiles.ts`, controller
  tests and `tests/CONTROLLER-SUPPORT.md`: preserve synthetic-only profiles.
- Existing GamingModeAgent/GamingMode.Services/SdlControllerHaptics.cs:
  established SDL3 usage, owned handles, duplicate-device caution. Not modified.
- Artwork/Playhub Artworks/src: searched controller references; no relevant
  physical controller backend found. Existing layout/abort controllers are not
  input devices and were not used as hardware evidence.
- Installed Steam steamui/chunk~2dcc5aaf7.js: controller store/routes present;
  native UI routing is not an input stream.
- Steam/SDL3.dll exists, ProductVersion 03.05.00.00, SHA256
  `95f4d71b795efc11ded38271609715fbeeb7c57dbe22af277dd493bd6858e6b3`.
  File metadata/hash only; DLL NOT loaded. Metadata is not SDL_GetVersion, ABI,
  reproducible provenance, redistribution permission or hardware validation.
- Desktop/HandheldCompanion-main.zip SHA256
  `a8484c699b4ec0c902c0bc8403b4a81be3bcfdaa49670edd40f4496f23413fd4`.
  Archive inspected in place, not extracted/modified. Existing extracted HC
  reference from CONTROLLER-SUPPORT.md supplied the root license text.

## License decisions

HC root LICENSE.md: CC BY-NC-SA 4.0, including noncommercial, attribution and
share-alike restrictions. Do not transplant HC code into MIT Playhub on the
assumption that public GitHub access permits unrestricted redistribution.
The archive's hidapi.net/LICENSE and steam-hidapi.net/LICENSE contain GPLv3,
so the root license alone is not a complete dependency audit. None is used.

SDL: zlib license permits commercial use; preserve origin, mark altered source
and retain its notice in source distributions. An official binary deployment
must retain the release's license and audit its included dependencies/notices.
This folder does not redistribute SDL, so no copied SDL notice is required for
these independent bindings. Before bundling, include the exact release LICENSE
and dependency notices with that binary; a link is not a replacement.

- [Pinned SDL license](https://github.com/libsdl-org/SDL/blob/8e37db5e797b6167f3a00d697d816a684bd259c7/LICENSE.txt)
- [Pinned public gamepad ABI](https://github.com/libsdl-org/SDL/blob/8e37db5e797b6167f3a00d697d816a684bd259c7/include/SDL3/SDL_gamepad.h)
- [Pinned Triton backend](https://github.com/libsdl-org/SDL/blob/8e37db5e797b6167f3a00d697d816a684bd259c7/src/joystick/hidapi/SDL_hidapi_steam_triton.c)
- [Pinned USB identifiers](https://github.com/libsdl-org/SDL/blob/8e37db5e797b6167f3a00d697d816a684bd259c7/src/joystick/usb_ids.h)

Release tag release-3.4.10 resolved through GitHub API to commit
8e37db5e797b6167f3a00d697d816a684bd259c7. Source inspected only for capability,
license and side-effect evidence; no HID protocol constants/layouts ported.

VIIPER remains a future virtual-output option, not part of this adapter.
[Official installation guidance](https://alia5.github.io/VIIPER/stable/getting-started/installation/)
distinguishes MIT TCP clients from GPL-compatible direct libVIIPER linking.
Server redistribution still requires a GPL license/source-delivery audit;
separate processes do not erase server obligations. Windows USBIP driver
compatibility/signing/Secure Boot and installation approval are separate gates.
No server contacted, library loaded, driver queried/installed or target created.
HC virtual-original-controller support does not prove a physical 2026 reader.

The conservative compatible path selected here is independent MIT glue plus a
separately audited zlib SDL runtime, not HC redistribution or a copied wrapper.

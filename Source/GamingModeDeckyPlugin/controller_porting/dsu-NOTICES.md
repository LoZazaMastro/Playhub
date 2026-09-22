# DSU source and license record

Reviewed 2026-09-08. New dsu.py, dsu_bridge.py and tests are independent Playhub
code under the existing MIT LICENSE in this folder. No dependency or driver is
vendored. Runtime dependencies are Python standard library socket/struct/zlib,
dataclasses/math/time/threading/secrets/hashlib. Retain Playhub's MIT notice when
redistributing this code; bundled Python itself retains its own license duties.

## Public protocol reference

[v1993 cemuhook-protocol](https://github.com/v1993/cemuhook-protocol)
is dedicated under [The Unlicense](https://raw.githubusercontent.com/v1993/cemuhook-protocol/master/LICENSE).
Protocol fields/units were independently implemented, not a transplanted server.
UTF-8 source hashes of references read in this audit (not downloaded artifacts):

- README.md: b695190f7252009336cbc1f68b8c715ca6fced19b25612860c3c11d6e3315be7
- LICENSE: 88d9b4eb60579c191ec391ca04c16130572d7eedc4a86daa58bf28c6e14c9bcd

GitHub commit lookup was rate-limited; these content hashes identify the viewed
text. Do not mistake a moving branch URL for an immutable source pin.

## Coordinate semantics evidence, no implementation copied

Cemu is MPL-2.0. This work compares mathematical coordinate transformations in
two source paths; it does not copy Cemu code or distribute Cemu artifacts.
If a future change copies implementation, it needs its own MPL notice/source
compliance review; do not assume this independent-code record covers that.

- [DSUControllerProvider.cpp](https://github.com/cemu-project/Cemu/blob/main/src/input/api/DSU/DSUControllerProvider.cpp)
  UTF-8 SHA256 968e0aa1084f5c07cf87aad668ad43b80273246a6bb9e6829b2af31aa5ad815a
- [SDLControllerProvider.cpp](https://github.com/cemu-project/Cemu/blob/main/src/input/api/SDL/SDLControllerProvider.cpp)
  UTF-8 SHA256 65c299f965e976ead5c1364639e8e460275b89d6f33344fbf26452f647fcdf3f
- [Cemu license](https://github.com/cemu-project/Cemu/blob/main/LICENSE.txt)

Both feed the same motion handler. Comparing their input transformations gives
the explicit cemu-sdl-parity-v1 profile documented in dsu-README.md. This is an
inference from source behavior, not a claim of hardware or emulator testing.

## Handheld Companion reference excluded from copying

Read-only existing extracted reference: HandheldCompanion/DSU/DSUServer.cs.
Examined server purpose, imports and declarations; no HC implementation, timer,
CRC wrapper or control mapping copied/translated. Root HC license remains
CC-BY-NC-SA-4.0, incompatible with assuming unrestricted MIT redistribution.
Archive SHA256 from the prior same-task audit:
a8484c699b4ec0c902c0bc8403b4a81be3bcfdaa49670edd40f4496f23413fd4.
That archive hash was not recomputed during this DSU increment.

No Force.Crc32 package, SDL binary, HC assembly, ViGEm, VIIPER, HidHide or USBIP
driver is required by or distributed with this DSU transport. The upstream
reader's SDL runtime/license approval is separately owned by Newton/parent.

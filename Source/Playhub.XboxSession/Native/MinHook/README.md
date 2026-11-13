# MinHook

MinHook is the x86/x64 API hooking library the packaged-game overlay bridge
(`src/WSGM.PackagedLaunch/Bridge`) links statically for its detours. `eng/build-uwp-bridge.ps1`
compiles this tree with the bridge and ships `LICENSE.txt` beside the DLL as `MinHook-LICENSE.txt`.

The files are copied unmodified from the MinHook tree bundled in the `minhook-sys` 0.1.1 crate
(YaLTeR/minhook-sys, commit `e0fff4520df583969056b32944c5cfd638041562`), the same source the Steam
Input Lease payload links through that crate. Only the headers, the sources and the licence files
are kept; MinHook's own Visual Studio and MinGW projects are not needed. Update the tree by
replacing these files with another reviewed MinHook revision.

MinHook is BSD-2-Clause, and the Hacker Disassembler Engine under `src/hde` carries the same terms
(`LICENSE.txt`).

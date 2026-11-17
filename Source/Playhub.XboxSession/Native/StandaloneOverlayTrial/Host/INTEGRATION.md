# Root-only normal private candidate recipe

1. Review `source-map.json` Agent overrides against their exact Source before pins.
   Native preferred subtree is mapped to
   `Source/Playhub.XboxSession/Native/HudDisplay` only as a standalone project;
   do not add it into the companion CMake tree (duplicate static target names).
   Existing runtime companion and callback remain unchanged.
2. Build this standalone project from the preferred Source copy to a NEW output:

```powershell
cmake -S <Source/Playhub.XboxSession/Native/HudDisplay> -B <NEW_BUILD> -A x64 `
  -DPLAYHUB_HUD_CALLBACK_FILE=<EXACT_B582_CALLBACK_DLL>
cmake --build <NEW_BUILD> --config Release --clean-first
ctest --test-dir <NEW_BUILD> -C Release --output-on-failure
```

3. Compute the resulting `PlayhubHudDisplayNative.dll` digest; compile/publish the
   normal Agent using the new conditional project properties:

```powershell
dotnet publish <GamingMode.csproj> -c Release `
  -p:PlayhubHudDisplaySha256=<NEW_NATIVE_SHA256> `
  -p:PlayhubHudDisplayCallbackSha256=<EXACT_CALLBACK_SHA256>
```

   The project emits just those two AssemblyMetadata attributes despite existing
   GenerateAssemblyInfo=false. No pins => OFF. Never embed the Agent's own bundled
   executable digest in itself; runtime pins it and Root independently attests it.
4. The normal installer/bundle needs an explicit new runtime entry
   `PlayhubHudDisplayNative.dll` beside GamingMode.exe and the already packaged
   `PlayhubExactBand.dll`. Add its SHA to package/installed QA and include this
   complete preferred subtree, HashCallback.cmake, callback source, GPL/license,
   generators and recipe in corresponding source. No ad-hoc live DLL copy.
   This packaging change is NOT implemented/promoted by this TEMP patch.
5. Root installs only through the normal setup, obtains fresh installed/loaded
   Agent identity, then enables the existing normal Playhub live performance UI.
   Inspect the copy-only `/session/live-performance/hud-diagnostics` response.
   Surface.NativeDisplay contains native stage/state without exposing the lease.

TEMP build uses the frozen dependency at dependencies/PlayhubExactBand.dll.
This is not a new callback artifact: it is the exact existing B582 runtime.
If Root elects to rebuild the accompanying callback source, supply that exact
new binary to CMake and metadata, and re-peer the resulting pair.

Safe verification regenerates only test fixtures from frozen preferred code,
builds and executes inert tests. Root live execution is intentionally not
provided by the peer test runner.

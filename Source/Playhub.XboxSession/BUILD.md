# Build Playhub Xbox Session

The installer includes the complete corresponding source in `XboxSession/source`.
This directory is self-contained: it has no project references, external source links or third-party NuGet dependencies.
Use the Microsoft .NET 8 SDK on Windows with the .NET 8 Windows x64 runtime packs.

```
dotnet publish Playhub.XboxSession.csproj -c Release -r win-x64 --self-contained true
```

Output: `bin/Release/net8.0-windows/win-x64/publish/Playhub.XboxSession.exe`.
The executable is self-contained and single-file. The executable output type deliberately matches the attended Steam
handoff proof. It hides only its own otherwise unshared console. The managed project builds independently of the
optional native AppContainer proof below.

Normal Xbox imports target the MIT Playhub.GameSession.exe and use original Windows activation and supervision.
Existing shortcuts that directly target Playhub.XboxSession.exe also use original activation with this command:

```
Playhub.XboxSession.exe --uwp "package-family!application-id" "manifest-game.exe" "raw user arguments"
```

The normal command does not inspect Steam components or enter the renderer handoff. It retains the external shell broker
and exact game-process supervision. Missing-game startup has a bounded deadline and never retries through the experiment.

The preserved GDK experiment requires a separate explicit command, never emitted by the importer or MIT launcher:

```
Playhub.XboxSession.exe --gdk-experimental "package-family!application-id" "manifest-game.exe" "raw user arguments"
```

Only that command detects full-trust GDK from the registered package and returned process and attempts Steam renderer
handoff. The caller cannot select a PID, DLL payload or package-debugging exemption. Unmatched packages use original
activation. Failures after activation continue the same session and do not launch a second copy.

Read-only installed-Steam verification:

```
Playhub.XboxSession.exe --check-steam
Playhub.XboxSession.exe --check-uwp "package-family!application-id" "manifest-game.exe"
```

It verifies that a same-session running Steam client matches the registered Steam installation and that the four existing
components are native x64 with valid Valve Authenticode signatures. It does not activate a game or write to a process.
Trust validation uses the local Windows certificate cache and does not download code or update Steam.

## AppContainer preflight

The AppContainer proof builds the native bridge and verifies an installed x64 UWP title. It does not activate the title,
load the bridge, enable package debugging or change the normal Xbox launch path. The bridge sources and MinHook are
vendored, so the native build does not fetch dependencies. Use CMake 3.20 or later and Visual Studio with the desktop
C++ workload and Windows SDK. Run from this directory in a Developer PowerShell with `cmake` on PATH.

```powershell
$native = (Resolve-Path -LiteralPath Native).Path.Replace('\', '/')
$proof = Join-Path $env:TEMP ('Playhub-AppContainer-' + [guid]::NewGuid().ToString('N'))
cmake -S "$native/WsgmBridge" -B "$proof/native" -G 'Visual Studio 17 2022' -A x64 "-DMINHOOK_SOURCE:PATH=$native/MinHook"
cmake --build "$proof/native" --config Release
dotnet publish Playhub.XboxSession.csproj -c Release -r win-x64 --self-contained true -o "$proof/check"
Copy-Item -LiteralPath "$proof/native/Release/WsgmUwpBridge.dll" -Destination "$proof/check"
Copy-Item -LiteralPath "$native/MinHook/LICENSE.txt" -Destination "$proof/check/MinHook-LICENSE.txt"
& "$proof/check/Playhub.XboxSession.exe" --check-appcontainer 'package-family!application-id' 'manifest-game.exe'
```

The static command checks the registered package and application manifest, the bridge's four code exports and
Valve's signed components from the running same-session Steam installation. It returns `2` when the registered
game image cannot be opened due to Windows Store protection, and `0` if the image is also verified as native x64.
When the user has started the game manually, the second command checks the running process instead of reading the
protected image:

```powershell
& "$proof/check/Playhub.XboxSession.exe" --check-running-appcontainer 'package-family!application-id' 'manifest-game.exe'
```

The running check requires a unique exact match and a retained query-only process handle. It verifies the executable
path, native x64, AppContainer token, package full name and family, AUMID, session and creation time. Successful
checks establish readiness for further testing only. Neither command starts or modifies a game or enables debugging.
The IPC fixture uses anonymous Win32 mapping and event handles. `AppContainerBridgeSession` owns the broker and
executes the bridge handshake from a verified target supplied by its caller. Neither existing launch command uses it.

For redistribution of the native proof, keep the executable and DLL together with `LICENSE`, `NOTICE.md`,
`MinHook-LICENSE.txt` and the complete corresponding source directory with this build recipe.

Source checks, from the Playhub repository:

```
dotnet run --project Source/Playhub.XboxSession.Tests/Playhub.XboxSession.Tests.csproj -c Release
dotnet run --project Source/Playhub.GameSession.Tests/Playhub.GameSession.Tests.csproj -c Release
```

Pass `-- --bridge <path-to-WsgmUwpBridge.dll>` to the Xbox Session test command to verify the artifact's export table
and initialize the DLL in a separate fixture process. The fixture does not access a real game process.

## AppContainer process fixture

From the repository root, use the `$proof` directory from the native build above and compile the test child.

```powershell
cmake -S Source/Playhub.XboxSession.Tests/Fixtures -B "$proof/child" -G 'Visual Studio 17 2022' -A x64
cmake --build "$proof/child" --config Release
dotnet run --project Source/Playhub.XboxSession.Tests/Playhub.XboxSession.Tests.csproj -c Release -- --appcontainer-child "$proof/child/Release/PlayhubAppContainerFixture.exe" "$proof/native/Release/WsgmUwpBridge.dll"
```

The test creates a unique temporary AppContainer with no capabilities. The child verifies that it cannot open the
desktop test event directly. The parent uses a retained process handle to transfer the broker transport and initialize
the pinned bridge. The sandbox can then ask the broker for the event and signal it. A second child exits during the
handshake, before the DLL loads. Both cases check that the parent transport handles close, and the test removes its
temporary AppContainer profile and files. It does not load Steam binaries, use a real game or exempt a package from
Windows lifecycle management.

## Steam companion fullscreen snapshot trial

The native companion normally rejects a full-monitor topmost game window. Those window properties do not identify
the game's DXGI presentation mode. An explicit experimental build can instead attempt the real Valve overlay while
holding one captured game frame behind it:

```powershell
$trial = Join-Path $env:TEMP ('Playhub-Companion-' + [guid]::NewGuid().ToString('N'))
cmake -S Native/SteamCompanion -B "$trial/native" -G 'Visual Studio 17 2022' -A x64 -DBUILD_TESTING=ON -DPLAYHUB_COMPANION_FULLSCREEN_SNAPSHOT=ON -DPLAYHUB_COMPANION_GUIDE_WARMUP=OFF
cmake --build "$trial/native" --config Release
ctest --test-dir "$trial/native" -C Release --output-on-failure
```

`PLAYHUB_COMPANION_FULLSCREEN_SNAPSHOT` defaults to `OFF`. To reproduce the normal artifact, omit that flag or set it
to `OFF`. The managed sidecar must still be published with the exact DLL's pinned SHA-256, as for any companion build.

After a verified overlay request, the trial captures the exact foreground game's client area once, before showing or
focusing the companion. It keeps the SDR-display BGRA frame in memory and releases its texture when the attempt ends.
It changes no display mode or game preferences. HDR remains configured by the game and Windows; the temporary snapshot
uses SDR display capture, so HDR colour accuracy needs visual verification. Failed or entirely black captures are refused.
For a captured frame which originally filled its monitor, the companion follows that same monitor's current bounds.
It scales the frame with a D3D11 shader when the desktop resolution differs after a fullscreen focus transition. Matching
dimensions use a direct resource copy. Shader compilation loads Windows' `d3dcompiler_47.dll` from System32; the compiler
is not bundled or searched for beside the game. Snapshot textures and shader resources are released when the surface hides.

If the game minimizes after losing foreground, the companion accepts only the retained HWND, process birth, window
thread, class and saved placement. On close it restores that exact game before hiding its captured frame. A user's
switch to another application cancels the presentation. The trial reports snapshot preparation, minimized-source
retention and game restoration separately from Valve's overlay-open report.

The tests exercise foreground policy, bounded image sizes, monitor geometry at nonzero origins, the actual GDI top-down
BGRA transfer between memory surfaces, and offscreen WARP scaling from 1920x1080 to 3840x2160 with known colours. They
create no visible windows and capture no desktop content. A passing test does not establish live
exclusive-fullscreen support, controller navigation or HDR accuracy. Fullscreen games which switch physical resolution,
protected capture surfaces and game-specific focus behaviour require separate attended tests.

## Registered UWP Steam companion preview

`PlayhubUwpSteamCompanion` is an explicit build property and defaults to `false`. Build both the MIT GameSession launcher
and the GPL XboxSession sidecar with `-p:PlayhubUwpSteamCompanion=true` and the same exact
`-p:PlayhubSteamCompanionSha256=<DLL SHA-256>` to enable the preview. The native companion and managed sidecar must come
from the same source revision: the native `RunSteamCompanion` ABI includes a final UTF-16 expected AUMID argument.
Use a separate native output directory for this ABI; do not replace an artifact belonging to a running session.

The launcher starts its hidden child with a private two-phase standard-input/output handshake. Before the single original
Windows activation, the child authenticates its installed wrapper parent, command line, Windows user/session, creation
time and fresh Steam shortcut tracking. It resolves the current user's registered x64 AppContainer manifest and checks
the complete native package identity, including publisher and version. The manifest remains pinned through the session.
The helper and launcher each pin the native DLL and verify its build digest before activation.

After activation, only the exact returned game PID, birth, package full name, family, AUMID, physical executable,
native x64 architecture, AppContainer token and Windows owner may reach the companion. The sidecar accepts a hosted
ApplicationFrameHost window only for that expected AUMID and the retained game's visible CoreWindow. Direct Win32
sessions pass no AUMID and cannot claim a hosted package frame. The session status pipe binds the same wrapper birth,
game birth and complete Steam shortcut GameID; its OS server PID and exact signed Valve overlay host are checked again
before an overlay command. An ambiguous or unsupported package keeps the original Windows launch.

This route performs no DLL injection into the packaged game and changes no game preference, package debugging state,
controller driver or Windows display mode. The older Cuphead AppContainer input experiment remains separate and is used
by its existing explicit build path; the generic preview does not broaden that injection route. Packaged full-trust GDK,
x86 and ARM applications do not pass this AppContainer companion gate. Live game/physical-controller and fullscreen
coverage remain distinct from the owned fixtures below.

The fixture suites exercise the two handshake phases and bounds, parent arguments, stale/recycled process tuples,
publisher/version/architecture mismatches, manifest subdirectories, removed/ambiguous registrations and native ABI
rejection. A temporary real AppContainer proves sandbox and exact-birth handling while refusing an unregistered package;
it is not a registered Store-game success claim. A read-only installed package check can be run without launching it:

```powershell
dotnet run --project Source/Playhub.XboxSession.Tests/Playhub.XboxSession.Tests.csproj -c Release -- --registered-uwp-readonly 'package-family!application-id' 'manifest-game.exe'
dotnet run --project Source/Playhub.XboxSession.Tests/Playhub.XboxSession.Tests.csproj -c Release -- --companion '<new-native-output>/Release/PlayhubSteamCompanion.dll'
```

Session test projects compile with `PLAYHUB_TEST_FIXTURE`; their explicit per-process scratch log destinations are compiled
out of production. Their marker checks verify fixture log routing and reject writes to the real Playhub logs. The fixtures
remove their own scratch logging directory at exit and accept no environment-variable log redirection.

## Live GPU mirror candidate

The live mirror uses the original Valve composition in a GPU-backed DWM destination. Its source remains in the
companion process; the game retains native foreground. This path is separate from the older captured-frame trial.

Build from `Native/TextureProbe` with `PLAYHUB_COMPANION_LIVE_MIRROR=ON`,
`PLAYHUB_FOREGROUND_VIEW_TEXTURE_PROBE=OFF` and `PLAYHUB_TEXTURE_RENDERER_SHA256` set to the exact installed,
trusted `gameoverlayrenderer64.dll` digest. Both paths default to OFF. The sibling `Native/OverlayMirror` directory
contains the complete callback source, GPL license, CMake files and `HashCallback.cmake` build step.

The same build produces `PlayhubSteamCompanion.dll` and `PlayhubExactBand.dll`. Keep that pair together: the generated
native header pins the exact callback digest. Publish the managed helper with `PlayhubSteamCompanionSha256` and
`PlayhubSteamBandCallbackSha256` matching those binaries. The private setup accepts the callback through
`-XboxSteamBandCallback`; the callback and corresponding preferred source are included in its payload.

The private configuration and control ABIs are versioned at 96 and 248 bytes; the read-only snapshot is 224 bytes.
Preparation keeps the destination hidden. Showing it requires the same renderer generation, a composition receipt,
the actual Valve-open state and a fresh successful present. A separate owned worker renews an already shown lease;
snapshot reads do not renew it. Closing revokes authority and hides both windows before callback retirement.
Controller commands use the managed session's ordered issuer, independently from the renderer's truthful scope1.

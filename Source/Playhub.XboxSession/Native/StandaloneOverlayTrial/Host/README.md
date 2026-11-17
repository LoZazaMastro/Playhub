# Private HUD display-only adapter, not live-qualified

This TEMP project builds `PlayhubHudDisplayNative.dll` and Source-compatible
Agent overrides. Nothing has been written to production Source or executed
against Windows/Steam. Root alone performs a normal private candidate test.

The existing WPF HUD is the real live DWM source. A separate small native popup
owns its own WndProc/cookie and the exact same physical HUD rectangle. The popup
is promoted while hidden through the existing **unchanged B582 callback**. Only
after completion, revocation, quiescence and band16 readback does it register the
live WPF thumbnail, prepare glass, disable its own input, and SHOW|NOACTIVATE.
No game snapshot, screenshot texture, SetForegroundWindow, keyboard, controller,
Steam overlay/layout, fake GameID or WPF GWLP_USERDATA write exists here.

## Authorization

`Authority` has explicit PerformanceHud purpose and a real internal collector
lease/epoch/app/expiry, Agent PID/birth/build digest, game/frame PID/birth, real
game foreground HWND/thread/rectangle and the WPF HWND/full GUID class/thread/
producer instance. The WPF UI publishes only a real Ready sample (or its original
Ready sequence/timestamp during Busy, same epoch and exact game/WPF context,
younger than500ms). Off, QAM suspension, source replacement, AltTab or expiry
withdraws the publisher. A readonly API never begins or renews display authority.

The broker accepts a typed HUD BandLease, distinct from the legacy Steam
SourceLease. Only physical game/frame proof is lowered into the existing callback
wire. Its actual sourceForeground is the GAME/AFH window, never the WPF HUD. Its
target is only the native owned hidden popup. The existing callback has no Steam
GameID/renderer/browser field. The HUD source is checked inside the trusted Agent
publisher/controller, while the callback independently rechecks controller/shell
identity, SID/session/desktop, game/Core identity, real foreground, target nonce,
deadline and permitted hidden target. Callback expiry is capped by the captured
collector deadline as well as the original5s setup and500ms commit budgets.

No public endpoint accepts an HWND/configuration/pulse. Agent assemblymetadata
must pin the native DLL and callback; missing pins leave the feature OFF. The
Agent executable is pinned/hash-correlated at runtime (an embedded hash of its
own bundled executable would be circular). Root's normal installed proof must
independently attest that Agent and the package before the test. The native
source also requires the actual current executable basename GamingMode.exe.

## Lifetime and diagnostics

Begin reserves authority through dequeue and all slow setup before publication.
Close wins over Pulse/new Begin; old generations cannot reopen. Native commit
guards read the latest immutable producer pulse AFTER slow getters adjacent to
Register/Update/SHOW. Source, WPF and target are separate roles. All game/process
handles are retained and original UWP/Win32 callback guards remain strict.

Hide -> unregister thumbnail -> callback retirement -> destroy only our popup.
Unknown retirement keeps the same GUI owner, HWND, userdata and module alive,
hidden, with cleanupComplete=false. Normal Closed releases every per-display
window/thumbnail/process handle. One configured worker, loaded module and pinned
component files intentionally remain until Agent exit; Closed waits without an
idle poll. The exception path keeps pumping retirement before worker exit. There
is no promise to survive an external forced termination of the whole Agent.

`ReadHudDisplayV1` copies a worker snapshot; Agent HUD diagnostics append only a
UI-published copy, no native calls on GET. Fields separate band/readback,
thumbnail relationship, successful update count, visibility, latest source
validation and cleanup. Draw-completed is still WPF drawing completion, not a
desktop/compositor pixel proof. DWM update count is not a presented-frame count.
Closed/Closing clears SourceCurrent; last generation/source are preserved.

## Evidence and remaining live questions

Safe tests are `RunChecks.ps1`; never call Configure/Queue or instantiate the
Windows backend as a peer. Tests compile/run only fake/pure code, including the
entire real AttemptBand+ShellCallback bodies with inert native APIs, actual
backend Attach/Show bodies with inert APIs, lifecycle, mailbox and managed ABI/
Ready-cache. This is not a Windows band, WPF alpha, actual token/signature or
Explorer live proof.

Root's first test should be Cuphead UWP (the source is occluded by the game):
visually prove actual changing HUD text/graph pixels and transparent holes over
the live game, no FG/input change, QAM suspension, Off/AltTab cleanup and normal
exit. Do not infer this from HRESULT, draw/update count or visible=true. On
Win32 where the original WPF source is already visible, duplicating its alpha
may double-blend; that case is not yet qualified. A separate owned-source
occlusion/demotion policy would need review, not a hidden global window change.
Existing strict CleanGui/NoInput pre-hook rules can still refuse normal captured
game input: the stage is reported, not silently relaxed.

## Licensing / primary contracts

Runtime/typed broker derives from Playhub GPL-3.0-only sources. GPL text remains
in OverlayMirror/LICENSE; the complete unchanged callback-v3 source accompanies
the B582 binary. No Valve binary/source is distributed in this project.

* https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmregisterthumbnail
* https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmupdatethumbnailproperties
* https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ns-dwmapi-dwm_thumbnail_properties
* https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enablewindow
* https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-windowfrompoint
* https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/Shared/MS/Win32/HwndWrapper.cs

Only the owned native destination is disabled AFTER callback retirement; callback
validation still sees its original enabled hidden target. BOOL EnableWindow is
previous state, so readback uses IsWindowEnabled instead. No source/game window
is disabled.

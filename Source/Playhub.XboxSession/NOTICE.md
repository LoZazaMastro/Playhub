# Playhub Xbox Session

Copyright 2026 Andrea Sgarro and the WSGM contributors.
This separate executable is distributed under GNU GPL version 3 only. It has no warranty.
The GPL source is not linked into Playhub's MIT application. Normal imports and the MIT launcher use original Windows
activation; the preserved renderer handoff requires this executable's explicit `--gdk-experimental` command.

The `Vendor` directory derives from KillerPixelCrew/WSGM, commit
`1329813f673af665433f618db178ceeea081afbc`, retrieved 2026-10-03.
https://github.com/KillerPixelCrew/WSGM/tree/1329813f673af665433f618db178ceeea081afbc/src/WSGM.PackagedLaunch

`NativeMethods.cs`, `SteamInstallation.cs`, and `PackagedWin32OverlayRoute.cs` retain upstream source.
`GameInjector.cs` was modified on 2026-10-03 to require a retained process-identity guard before every remote operation.
Later changes preserve a failed load's outcome, require the exact loaded component path for remote exports and allow
the owned AppContainer fixture to route diagnostics to its own console.
The complete upstream GPL text is in `LICENSE`.

`Native/WsgmBridge` is the unmodified AppContainer bridge source from KillerPixelCrew/WSGM commit
`526846032069dc991248367f95c8634bfda8f9d9`, at `src/WSGM.PackagedLaunch/Bridge`.
https://github.com/KillerPixelCrew/WSGM/tree/526846032069dc991248367f95c8634bfda8f9d9/src/WSGM.PackagedLaunch/Bridge

`Native/MinHook` contains the unmodified dependency from `external/minhook` at the same revision. Its license and
copyright notices are in `Native/MinHook/LICENSE.txt` and must accompany the compiled DLL. The same revision's
`Injection/OverlayObjectAllowList.cs` is retained in `Vendor`. `OverlayBrokerTransport.cs` adapts that revision's
`Injection/OverlayObjectBroker.cs` for Playhub with reduced handle rights, bounded mapping sizes and no file requests.
The AppContainer preflight, process query and handshake coordinator in `AppContainerProof.cs`,
`RetainedAppContainerTarget.cs` and `AppContainerBridgeSession.cs` are Playhub additions under GPL-3.0-only.
`AppContainerTrial.cs` follows the component initialization order from WSGM's
`Strategies/AppContainerOverlayRoute.cs` at commit `526846032069dc991248367f95c8634bfda8f9d9`.
The Playhub session checks and temporary bridge staging are GPL-3.0-only.

The Steam companion under `Native/SteamCompanion` is an independent GPL-3.0-only implementation
of the external overlay approach documented by shalamowww/steambro, commit
`af09896b66d9e549773248521e78d2c6544855e9`.
https://github.com/shalamowww/steambro/blob/af09896b66d9e549773248521e78d2c6544855e9/src/overlay.rs
The preflight reads the bridge as a PE image without loading it. The coordinator follows the bridge setup sequence
from the pinned `Strategies/AppContainerOverlayRoute.cs` and has been exercised with an owned AppContainer fixture.

The `Runtime` directory adapts Playhub MIT code; the original copyright and license are in `Runtime/MIT-LICENSE`.
`GdkLaunchResolver.cs` was renamed from `GdkLaunchProof.cs`. The copied runtime is included in this GPL executable as a whole.
`UwpShortcutArguments.cs`, `UwpSession.cs` and `XboxShellBrokerClient.cs` retain the corresponding Playhub source behavior.

Playhub additions dated 2026-10-03: generic command handling; registered package, manifest and GDK configuration checks;
exact returned-helper identity and creation-time guards; same-session Steam attribution; signed Valve component verification;
one-shot activation with continued supervision on setup refusal; bounded startup and read-only renderer observation;
private console hiding; rotating logs with no environment values; original activation by default with explicit experimental
handoff only. No actual-game process writes, package lifetime setters,
driver installation, downloaded payloads or persistent injected service are used.

Steam components are loaded from the user's installed Steam client. They are not bundled or licensed by this notice.
The included source project, source modifications and build instructions constitute this executable's corresponding source.
The standalone source directory can be copied, modified, rebuilt and redistributed under the accompanying GPL.

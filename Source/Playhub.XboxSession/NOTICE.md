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
The complete upstream GPL text is in `LICENSE`.

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

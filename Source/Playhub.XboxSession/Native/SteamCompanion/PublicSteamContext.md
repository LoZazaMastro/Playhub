Public Steam context trial
==========================

This private, read-only diagnostic runs once in the real Steam-launched companion PID,
after Surface.Create has returned S_OK from its first hidden creation Present. It creates its own public Steam communication pipe, connects that
pipe to the existing global user, acquires SteamFriends018 and SteamUtils011, and reads
GetAppID. It never reads account identifiers, names, friends, chat or other account data.
It never calls ActivateGameOverlay, changes foreground, emits input or writes preferences.
The compiled default is OFF. No steam_api64.dll is loaded, bundled or required.

Public ABI provenance
---------------------

Declarations are the exact public SDK 1.65 prefixes needed for these calls, not guessed
numeric private vtable indices. Source pin: df2baabf574a738ef1ea90a7e89339107fc0a279.

https://github.com/rlabrecque/SteamworksSDK/blob/df2baabf574a738ef1ea90a7e89339107fc0a279/public/steam/isteamclient.h
https://github.com/rlabrecque/SteamworksSDK/blob/df2baabf574a738ef1ea90a7e89339107fc0a279/public/steam/isteamutils.h
https://github.com/rlabrecque/SteamworksSDK/blob/df2baabf574a738ef1ea90a7e89339107fc0a279/public/steam/steam_api_common.h
https://partner.steamgames.com/doc/api/ISteamClient

The matching interface names are SteamClient023, SteamFriends018 and SteamUtils011.
This code reproduces ABI declarations only and calls the installed Valve implementation.
Existing GPL-3.0-only companion licensing is unchanged. It imports no third-party
implementation or new runtime executable. No private IClientUtils method is invoked.

Identity and lifetime
---------------------

RunSteamCompanion is entered only after existing managed registered-game/Win32 handoff,
retained PID/birth, current-user, Steam tracking and signed/pinned Steam file checks.
TrustedSteamFiles keeps steamclient64.dll open read-only throughout the call. Native code
derives that DLL from the exact, already verified renderer root, opens an additional file
handle without write/delete sharing, rejects reparse/file-path drift, and checks a mandatory
build-specific SHA256. LoadLibraryEx uses only the exact absolute path plus DLL/System32
search, then verifies returned module path and correlates PE machine/timestamp/image sizes
with the pinned file. It checks public interface vtable/function origins in that module
before using the published typed prefix.

Game and wrapper retained lifetime are rechecked between public calls. The public API's
IPC calls have no caller-supplied timeout; a stuck client is a private diagnostic failure,
not a guaranteed bounded success. This flag is not enabled for public distribution.

Only our own pipe/user are released, on the same thread after the D3D surface is destroyed.
Cleanup never calls global shutdown, SteamAPI_Shutdown, ReleaseUser for someone else's
handle, or any method on an unacquired interface. The module/file remain pinned until
that cleanup ends. No per-frame calls, polling worker or idle API timer is added. The first-Present gate is one-shot; an occluded/status-only creation Present does not trigger this experiment, and later frames do not retry it.

Diagnostics
-----------

24: verification flags (hex bitmask). 01 file SHA/pin,02 module path/PE,04 public client,
08 own pipe,10 existing global user,20 friends interface,40 utils interface,80 GetAppID read.
25: actual GetAppID value as raw unsigned32 hex. Zero is reported honestly; a shortcut's
64-bit CGameID is a separate identity. This value alone does not prove overlay association.
26: refusal Win32 error before a public call could proceed.
27: owned cleanup flags:01 user released,02 pipe release returned true.
28: exact context read outcome:0 not started,1 repeated read rejected,2 session changed
before pipe,3 pipe unavailable,4 session changed before Connect,5 Connect returned<=0,
6 session changed before Friends,7 before Utils,8 Utils unavailable,9 before GetAppID,
10 after GetAppID,11 complete,12 first creation Present was not S_OK (trial not exercised).
Values4 and5 discriminate the two branches which both produced flags0F in candidateA210.

No overlay success is inferred from these flags. A subsequent, separately authorized
activation test would still need a real same-companion renderer/browser/menu and foreground
readback; this trial only establishes public API availability and the reported app context.

Build (private output directory)
--------------------------------

    cmake -S <this-directory> -B <private-temp>/on -G "Visual Studio 17 2022" -A x64 \
      -DPLAYHUB_COMPANION_FULLSCREEN_SNAPSHOT=ON \
      -DPLAYHUB_COMPANION_PUBLIC_CONTEXT_TRIAL=ON \
      -DPLAYHUB_PUBLIC_STEAMCLIENT_SHA256=<fresh-Valve-attested-installed-digest>
    cmake --build <private-temp>/on --config Release
    ctest --test-dir <private-temp>/on -C Release --output-on-failure

The policy fixture uses fake API objects only and covers exact version strings, single
acquisition, handle-specific release order, partial failures, identity cancellation between
each call, failed release and all uint32 app ID values including zero. It never loads Steam.

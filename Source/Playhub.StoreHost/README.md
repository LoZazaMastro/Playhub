# Store Library host (Windows)

Original MIT-licensed Playhub code. A per-user host owns one external transfer at a
time; UI clients receive immutable snapshots and revision-checked acknowledgments.
Epic/GOG tools are separate, unmodified, pinned GPL executables with their source
archives and dependency notices. No external Python/Heroic/launcher installation.

## Packaging

1. Publish this project `-c Release -r win-x64 --self-contained true`. The entire
   publish directory is required. Its publish target creates `store-runtime.json`
   with `finalized:false`; this is deliberately unusable by production clients.
2. Publish the actual app and GameSession. Copy the host publish directory to
   `<app>/StoreLibrary`, then run `WriteStoreRuntimeManifest.ps1 -BundleDirectory
   <app>/StoreLibrary -AppDirectory <app> -DeckyBinaryPaths <official loader pair>`.
   The final manifest pins the complete runtime, actual app/helper executables and
   official Decky hashes. Decky resolves only under the current user's existing
   `homebrew/services` installation, never by hash anywhere on the PC.
3. Setup must require `finalized:true`, quiesce the old host before extraction,
   and preserve the per-user account/queue directory.

Runtime and client data use `%LOCALAPPDATA%/Playhub/StoreLibrary`. Persistent tokens
are DPAPI-protected; stock tools temporarily receive private-ACL native credential
files, resealed/deleted at the operation boundary. The host does not import or edit
Epic/Heroic/Galaxy credentials. Source, queue, snapshots and diagnostics contain no
real tokens. Abnormal process death can leave a private native temporary file;
this is not a claim that stock tools operate without plaintext credential memory
or temporary native files.

## Protocol

`Playhub.StoreLibrary.v1.<24 uppercase hex SHA256(current SID)>`: current-user-only
named pipe, UTF-8 newline JSON, one request per connection, 64 KiB request cap.
Request `{protocol:1,id:UUID,method,args}`; reply `{protocol:1,id,ok,data}` or
`{protocol:1,id,ok:false,errorCode}`. Properties and enums serialize camelCase.
All data/grant methods require an exact installed client path and hash.

Public methods: snapshot, downloads, connect-begin, connect-complete, disconnect,
refresh, prepare, enqueue, control, launch-descriptor. The Steam shortcut
registry implements steam-sync-* and steam-owned. `prepare-launch` is
private to the exact pinned GameSession executable; its short-lived arguments and
environment are never included in a UI snapshot, log, registry or queue file.

Steam observer: `steam-register {}` returns a server nonce bound to the actual
pipe-client PID/birth and unique installed Steam PID/birth. `steam-activity` takes
`{ownerId,sequence,observedAt,activeLocalDownload}`. Replays, unknown/future/expired
observations and replaced processes fail. Lease lifetime is 15 seconds. The
trusted Decky bridge also owns document-revision checks. Actual installed Steam
absence permits standalone app downloads; unknown live Steam blocks/pauses ours.
Steam settings and its native queue are never written.

Each install needs a fresh path/space preparation and explicit user confirmation.
The single-use selection nonce is bound to store key, account revision, absolute
final destination and preparation revision for five minutes. Existing nonempty
folders are refused. Resume requires the same owned directory marker; no new
destination dialog. GOG's supported `update` downloader alias preserves the exact
selected path instead of appending its metadata directory. Its current initial
language selection is the upstream default en-US; additional language UI is not
implemented here.

Pause is `pausing` until actual private-console worker drain plus the pinned
upstream interruption/checkpoint path is observed. Resume is completed-file
granularity; inflight bytes may be downloaded again. Cancellation keeps partial
files and never removes pre-existing game data. Exit zero alone is insufficient:
Epic needs its downloader-complete/verification marker and verified installed
metadata; GOG needs its successfully committed downloader manifest and local game
manifest/executable. Physical game launching and real-account transfers require
acceptance tests on the installed build.

`DownloadNow` prioritizes our queue only when Steam is confirmed idle. It waits
for our current transfer's checkpoint before starting another. User-pause intent
is never auto-resumed. Progress ticks advance snapshot revision but not command
revision unless lifecycle/phase changes.

## Maintenance

`quiesce {}` is current-user revocation-only; it grants no data/auth/download
rights and is usable by future installers not pinned in an old release. It honors
an existing valid `maintenance-v1.json` `{protocol:1,ownerId,expiresAt}` or creates
one. The client and new host launches honor a future marker (at most 330 seconds).
The host waits for real owned worker/query cleanup and durable queue state, replies
`{accepted,ownerId,expiresAt,errorCode}`, then exits only after the reply is flushed.
A blocked checkpoint returns failure; it does not force-terminate a downloader.
Setup removes only its own marker in its finally path. No PC shutdown operation.

## Current limits / evidence

106 deterministic checks exercise the actual queue, account race, path nonce,
observer policy, provider parsers/DPAPI, signout, private launch and real named-pipe
roles with inert HTTP/process facts. A separate native fixture proves private
console parent/child checkpoint and complete job drain. Neither constitutes a
real authenticated game download or game-launch test. The stock executables were
not executed during these tests.

Existing Epic Launcher/Galaxy installation discovery is still provided by the
original ImportGames scanners; automatic merging into this new owned catalog is
not yet implemented. Xbox owned-library/direct-download capability is unavailable;
the supported official client handoff must remain distinct and cannot promise an
arbitrary Playhub-selected folder. No private Microsoft Store capability, account
spoofing, DRM bypass, foreign process/token use, store-code copying or renderer
changes are involved.


## Amazon Windows launch plans

The original Playhub provider reads only root `fuel.json` covered by the signed
installed Amazon manifest. Its exact `Main.Command`, argument array and optional
`WorkingSubdirOverride` determine the image and working directory. There is no
largest-executable search, shell-string execution, launcher URI or foreign
launcher/profile scan. The bounded JSON5 reader accepts ordinary quoted and
identifier keys, comments, single/double quoted strings and trailing commas;
ambiguous duplicate keys, unsafe paths and unsupported values fail closed.
The requested install root, signed fuel/image hashes and complete installed
manifest are checked. Relevant metadata and image files are held read-only while
the plan is built. A successfully sealed library refresh binds its byte hash to
the current DPAPI-protected account identity; changed or unsigned-out accounts
cannot inherit cached launch entitlements. Old catalogs need a successful refresh
before first launch. The normal Steam registry and GameSession parser now accept
Amazon. The private plan retains the existing one-shot request and 30-second
contract; no environment/account data is added to public library snapshots.

Nile's five documented environment variables are configured using the same
private SDK directory convention, actual owned entitlement/product SKU and current
account display name. These paths are configuration, not an SDK availability or
runtime-compatibility acknowledgment. This increment does not redistribute or
provision proprietary Amazon SDKs or bypass DRM. In particular,
Nile's own pinned README warns that online FuelPump games may not work. Physical
Amazon game launch/DRM compatibility still requires acceptance on a real owned
game. Full installed hashing remains conservative and may cost I/O on large games.

75 new inert checks exercise the actual JSON5 parser/provider, signed files,
account/cache races, public descriptor, private queue launch boundary and Steam
registry/parser routing; all 156 prior Amazon checks pass. The actual prior
provider compiles and fails the valid signed-fuel launch case. Tests use synthetic
accounts and signed fixture data with fake HTTP/query/worker boundaries; neither
Nile, a game, OAuth, Steam nor a user interface is run.


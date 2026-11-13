# Discord for Playhub

Discord voice and conversations in Steam Big Picture on Windows. This is a
Playhub Windows fork of [Steamcord 1.45.1](https://github.com/Necrosiak/Steamcord),
with its own interface and a private Vesktop client included in the package.

## Use

Open **Discord** from Decky's Quick Access Menu. Scan the QR code with your
Discord phone app and confirm the sign-in there. The private session is saved
for the current Steam account. A CAPTCHA, when required by Discord, can be
opened from the panel. The plugin never imports the official desktop client's
profile or asks you to paste a token.

The panel provides:

- Servers, voice channels, direct conversations and an expanded chat view.
- A persistent call strip with the channel, microphone, deafen and leave actions.
- Participants, individual voice volume, soundboard and native screen sharing.
- Message and incoming-call notifications, subject to your notification settings.
- Optional Steam status synchronization and game activity, both off by default.
- A confirmed stop action that frees the private client's resources and keeps the
  saved login. Starting it again is separate from signing out.

Windows controls use the supported Vesktop/Discord functionality. Linux-only
PipeWire routing, game-audio mixing, evdev shortcuts and Linux in-game overlays
are not presented as Windows features. The plugin does not interact with the
user's other Discord or Vesktop installation.

Windows x64 is the packaged target. First launch unpacks the included, verified
runtime; it does not download another installer. Discord itself needs internet
access. Login, voice and sharing depend on Discord's service and the user's
account permissions.

## Build

Use Node.js, pnpm and Python 3.11 or later as development tools:

```text
pnpm install --frozen-lockfile
node node_modules/typescript/bin/tsc --noEmit
node node_modules/rollup/dist/bin/rollup -c
node --test tests/*.cjs
python -X utf8 -m unittest discover -s tests -p "test_windows_*.py"
python scripts/package.py
```

The Python loopback integration tests use `aiohttp` and `aiohttp-cors`, which
are supplied by Decky in production. Test dependencies are in
`requirements-dev.txt`. No test starts Discord, Steam, a game or a voice call.
The upstream Linux test fixtures require Linux; the Windows runtime tests and
frontend tests are the supported Windows build gate.

`runtime/Vesktop-1.6.8-win.zip` is an official release payload, omitted from Git
because of its size. Obtain it from the URL in `runtime/SOURCE.md`; the package
builder refuses any different SHA-256. The four Vencord assets and their exact
hashes are in `runtime/vencord/manifest.json`. The release ZIP contains all of
these files, the source archives and the notices.

The package builder creates `out/Discord_Installer-1.0.0.zip` with a per-file
SHA-256 manifest. Playhub can install this package through its normal plugin
installer. Runtime settings and saved logins remain outside the plugin folder.
Updates are managed by Playhub; the upstream self-updater cannot overwrite this
fork or restart the loader.

## Verification status

Automated checks cover the Windows runtime, exact process/listener identity,
CDP target restrictions, authenticated loopback WebSockets, private profile
preservation, offline runtime seeding, QR/CAPTCHA state rendering, call-action
failures and the existing frontend regression suite. Native Windows APIs are
used for the local identity and listener checks. Authentication and live voice
have not been exercised with a user's Discord account in this candidate.

## Credits and licenses

Steamcord is by Necrosiak, based on the Steam Deck Homebrew plugin template.
Original notices and contributors are preserved in `LICENSE` and `CREDITS.md`.
Playhub's Windows fork is by LoZazaMastro. See `NOTICE.md` and `runtime/SOURCE.md`
for Vesktop, Vencord, arRPC, Electron, dependency licenses and corresponding
source archives. This community project is not affiliated with Discord Inc.

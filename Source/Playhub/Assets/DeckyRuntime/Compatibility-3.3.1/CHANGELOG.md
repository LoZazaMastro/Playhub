# Private Decky reconnect patch

Based on Decky Loader v3.3.1, commit
ffd103b9c1a4ce5bf305e7c0998eda6e05457dad. The original GPLv2 license and
contributors are preserved. Changes by LoZazaMastro cover the local debugger
connection lifecycle only.

The reinjector now recovers when Steam replaces a target between discovery,
WebSocket handshake and Page.enable. It closes the exact stale tab, clears only
that tab's current-context reference and retries with bounded backoff. Startup
connection races do not consume the existing injected-webhelper crash budget.
Cancellation stops the worker and closes its own connection. The debugger JSON
body is consumed before its HTTP session closes, with direct local requests and
redirects disabled. No code changes Steam files or restarts Steam itself.

Run: python -X utf8 -m unittest discover -s tests -p test_reinjector_recovery.py
Seven fault fixtures passed. They do not connect to live Steam or Decky.

This directory contains the preferred source for the private Windows build
3.3.1+playhub.reconnect.1. The corresponding Windows package includes separate
console and no-console executables. This is a Playhub compatibility build,
not an official newer Decky release. Build
using the included official .github/workflows/build.yml and
build-win-x86_64.yml: Node26, Python3.14.8, Poetry with dynamic-versioning,
backend poetry.lock and frontend pnpm-lock.yaml. Build the frontend first, then
run poetry dynamic-versioning and pyinstaller pyinstaller.spec in backend.
Build once with DECKY_NOCONSOLE unset, and once with DECKY_NOCONSOLE=1;
retain both outputs separately and record their hashes. Explicit private build
provenance must be retained when integrating a custom binary into Playhub; the
patched binary cannot use the official release's checksum.

The private Windows executables were built with the exact locked dependencies.
Page.enable now requires a valid command acknowledgement within three seconds.
Events received before that acknowledgement are retained in a bounded queue;
a closed, cancelled, rejected or unresponsive target is discarded and retried.
The current-context reference is published after that acknowledgement.

The Windows console flag accepts explicit true values; the string "0" builds
the console variant. Both executable resources identify the private Playhub
build. Frozen-bytecode tests cover the seven original fault fixtures per
variant, real loopback HTTP/WebSocket target replacement, close before ACK,
CDP errors, event preservation, cancellation, three-second timeout and bounded
input. The loader executables have not been run against the user's Steam.
See BUILD-WINDOWS.md for the precise toolchain and commands.

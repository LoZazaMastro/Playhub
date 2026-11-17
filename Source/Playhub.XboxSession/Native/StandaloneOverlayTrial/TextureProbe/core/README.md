# Scoped Valve foreground view — private TEMP core

This is an offline prototype, not an installed change or a standalone authenticated endpoint. It contains no automatic initializer, launcher, RPC, focus activation or input sender. Product Source remains unchanged. Its runtime API may be called only by the trusted companion after that companion consumes its existing authenticated TakeGuide request and retains the exact parent/Steam/game session. The `authenticatedGuide` C++ bool is a trusted-caller precondition, not independent authentication. A future adapter must bind it to that real event; callers must not manufacture it from CEF focus.

## Primary basis and licensing

GlosSI has a concrete own-process GetForegroundWindow hook in SteamTarget::keepControllerConfig, returning its own target HWND for the launched game's window list: https://github.com/Alia5/GlosSI/blob/d843a32078ab69de4f0a3dc1880182dd7d335f04/GlosSITarget/SteamTarget.cpp . That file is Copyright2021–2023 Peter Repukat / FlatspotSoftware, Apache-2.0; no GlosSI/subhook implementation is copied here. The present implementation is original GPL-3.0-only and deliberately narrower: only a call site in the exact Valve renderer receives the view; all other callers remain real.

MinHook is copied byte-for-byte from the repository's Native/MinHook tree and includes its full LICENSE.txt/AUTHORS and embedded hde notices. Windows SDK system libraries/static Microsoft C++ runtime are the remaining dependencies. No Valve code is copied or distributed. Pinning only references an already-loaded signed module.

## Runtime contract

`PinnedRenderer::Open(exactCanonicalPath, sha256)` refuses unless an already-loaded GameOverlayRenderer64.dll has the exact canonical file path, valid64-digit digest matching its pinned no-write/no-delete file handle, valid cache-only Authenticode with Valve Corp. signer and organization, and matching disk/memory PE headers. It acquires its own module reference; it never loads or invokes Valve code. Caller membership requires the actual saved return address inside that exact module's executable `.text` section and a currently committed executable MEM_IMAGE page with that allocation base. Read-only data, another image, a JIT/private executable page or unknown caller does not qualify. Installed renderer's on-disk executable section was independently observed as `.text`; no live API was invoked for that observation.

`ScopedForegroundView::Install(pin)` hooks only GetForegroundWindow inside the current helper process. It does not hook the game, Steam main, Explorer, GetFocus or GetActiveWindow. There is one installation opportunity per process; only this library's explicit target is enabled/disabled, never MH_ALL_HOOKS. No source file or module on disk is patched.

`Arm(Request)` must run on the own Surface thread after a real authenticated Guide. It requires a new strictly increasing request sequence and an absolute lifetime of at most2000ms. The request is consumed even if subsequent identity checks fail. It duplicates/retains the game handle, checks PID/birth, same TokenUserSID and session for game/host/current process, source/window/root/thread/class/rectangle/input-desktop identity, actual foreground and clean GUI state. A UWP host requires exact System32 ApplicationFrameHost, property AUMID and the focused retained game CoreWindow/package family; it never guesses the inaccessible CoreWindow desktop or attaches input queues. A direct game window must be owned by the retained game PID. The first successful game's PID/birth, own Surface HWND/object cookie and AUMID become immutable for this instance.

The own target must be a visible/enabled root with no foreign owner in the current process/thread, its exact class, the expected Surface pointer in GWLP_USERDATA and a fresh private window property nonce. The property is removed only if it still matches that same own window and nonce. Each route rechecks retained lifetimes, fast source/target identity and real foreground, then generation/deadline again immediately before returning. It returns the own Surface only while the original real getter still reports that exact game window. A foreign foreground, exited/recycled target, window replacement, nonce/cookie drift, expired/revoked request or unknown caller returns the original result.

Default SurfacePurpose::VisibleBand16 additionally requires actual own band16. The explicitly approved TEMP SurfacePurpose::OccludedTextureProbeBand1 accepts actual band1 only as a texture SOURCE behind UWP. It is never a claim of composition above UWP. ReadCounts records the active expected band. Native Scope and keyboardAt are not modified by this core.

`ActualForeground()` bypasses the view through the original trampoline. All of our ordinary observer/guard calls also remain original because their call sites are outside the renderer. Original getter LastError is restored only after releasing local scope/pin snapshots, including any deferred last-reference handle/property teardown. Reentrancy falls back to the original getter. No COM, signature or file I/O runs inside the detour; those happen before arming.

## Revocation and retained executable storage

Disarm invalidates the generation and clears the active lease. Close does that first, disables only its exact patch and observes inFlight for a cooperative<=1000ms caller-selected budget (default250ms). S_OK means scope revoked, patch disabled and no counted callback observed. It does **not** mean every instruction pointer has passed the detour prologue or that code can be unloaded.

An own acquired module reference, trampoline allocation, MinHook engine storage and renderer pin are deliberately retained through the companion's process exit. A thread may be just before incrementing inFlight, so freeing a trampoline from a zero counter alone would not be justified. No worker, wait loop, input or background activity is retained after Close. On disable/observation failure Close returns ERROR_BUSY, retains everything and makes no full-cleanup claim; the caller must not force-unload the module. Normal companion process exit releases storage. The prototype does not promise a hard real-time bound for OS APIs.

## Offline tests and limits

```powershell
# Default/--pure: pure policy decisions only; no DLL or hook:
& "$env:TEMP\Playhub-Valve-Foreground-View-20261111\build\Release\PolicyTests.exe" --pure
# Additional inert native test, explicitly approved for agents:
& "$env:TEMP\Playhub-Valve-Foreground-View-20261111\build\Release\PolicyTests.exe" --native-fixture
```

The native fixture loads two local inert DLLs owned by this test. It obtains real return addresses and exercises the production PE `.text`/page classifier, then uses MinHook only on a fake getter in its own test executable. It proves allowed/foreign/our-observer caller separation, revoked scope passthrough, and deferred destructor LastError preservation. It never hooks user32, opens a game, loads/invokes a Valve module, creates UI, registers hotkeys or sends input. The single-thread fake fixture can remove its own trampoline after every possible test caller has returned; this is intentionally stronger ownership than an active renderer and is not an unload proof for the runtime core.

Policy fixtures test unknown/absent facts, stale/replayed generations, expiry, source/own-window drift, PID reuse, Surface cookie replacement and code-range boundaries. They do not prove the runtime window/Valve pin factory against live Steam. No authentic Guide, GPU texture, overlay browser, rendering visibility or controller route has been tested. A separate adapter must make a real transparent swapchain draw with NO snapshot/SFW, report real Present outcomes/browser/frame changes, and leave actualScope1/keyboardAt0 truthful. Input/neutralization must be tested separately before interactive claims.
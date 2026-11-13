# Private renderer texture-source adapter

This is a TEMP-only alternate RunSteamCompanion path for an attended root experiment. Product Source is unchanged. The private CMake switch PLAYHUB_FOREGROUND_VIEW_TEXTURE_PROBE defaults OFF. PublicSteamContext is OFF and explicitly incompatible with this private path. No runtime API/UI/input has been executed by agents.

The frozen core is copied byte-exact under core (original proof-files.json supplies its provenance). The ABI still exports only RunSteamCompanion with the existing ten arguments/callbacks. Before the old foreground/snapshot/replay body, an exclusive compile-time branch returns RunTextureProbe. That branch never invokes PrepareSnapshot, SFW, Attach, SwitchToThisWindow, SendInput, RegisterHotKey or SetKeyboardState. It retains the real Valve-hooked D3D11 transparent swapchain, with own NOACTIVATE|TRANSPARENT window styles and SWP_NOACTIVATE positioning. It never substitutes a stale game image.

The exact existing TakeGuide callback is the authority. Each positive native request is counted, not assumed to be a physical click. Only two probes may be admitted in this process: A then B. A only arms the scoped foreground view and draws. B additionally posts Shift/Tab down/up messages to the same current-thread own Surface HWND and exact GWLP_USERDATA cookie, after four draws. It never sends global input and never changes even thread keyboard state. GetAsyncKeyState and GetKeyboardState are read and reported separately; posted messages may be insufficient for Valve's polling, which remains an experimental outcome. The source/expiry/own-window guard is repeated between the two keydown posts. Cleanup keyup is limited to that own window.

A second authenticated request arriving while A is active is consumed and refused. It is not queued and cannot auto-start B after expiry. B requires a later fresh positive request, the first probe stopped, no pending own key-message cleanup and the old core nonce retired. The immutable mode is tied to the admitted probe counter; generation uses the raw native request sequence. A frontend retry can therefore be a refused native request or the later admitted B; root must inspect the mode/sequence timeline. More than two admitted probes are refused.

Each admitted lease lasts at most1800ms and stops on actual source loss/game exit. Both the real game and own Surface identity remain verified by the core. The explicit OccludedTextureProbeBand1 purpose accepts actual band1 only as a capture SOURCE behind UWP. It is not evidence of visibility above the game. The normal default core purpose still requires band16. Frame events20/21 retain their original real Present HRESULT semantics; OCCLUDED is not presented-S_OK.

The render-only branch deliberately emits no legacy overlay2/3/4 events and no keyboard-activation12 event, so raw Valve.Open does not prematurely stop/restore the controller bridge. Raw overlay flags are only diagnostics. Actual foreground/Scope events9/11 remain based on original getters and original eligibility; no fake scope2 or keyboardAt is written. PLAYHUB_COMPANION_FULLSCREEN_SNAPSHOT=ON is retained in this private recipe solely to preserve the existing fullscreen eligibility predicate used for scope1; the private branch never captures a snapshot. The legacy OFF branch remains otherwise unchanged.

Logs are new, shared-readable, PID/birth-bound JSONL files at:

    %TEMP%/Playhub-Renderer-Texture-Probe-20261111/probe-<companionPID>-<birthFILETIME>.jsonl

They include raw native request count, admitted mode, generation/expiry, game/source/Surface identity, actual band/style visibility/foreground, real Present results, allocated texture dimensions, shim counters and own-message posted/dequeued counts. They contain no account/message content or tokens. A maximum256 rows prevents unbounded logging. Dimensions and browser presence are not a pixel/rendering proof. No readback is taken from the DISCARD backbuffer: its post-Present contents are not guaranteed. Root's independent DWM live thumbnail and desktop-pixel readback must prove actual Valve pixels and changes; GDI calibration alone is not that proof. Controller routing/game-input suppression requires a separate later experiment.

## DWM source contract

Source class: PlayhubSteamCompanion. GWLP_USERDATA equals the exact Surface pointer/cookie. The following own-window properties exist only after successful arming and are removed only on the matching own window/value:

- Playhub.TextureProbe.Mode =1(A) or2(B)
- Playhub.TextureProbe.Generation = raw native request sequence
- Playhub.TextureProbe.ExpiresAt = GetTickCount64 absolute expiry
- Playhub.ForegroundView.OwnNonce = nonzero only while the core lease has not retired

A log is not an independent authentication endpoint. The DWM wrapper must first authenticate the installed/current build and original pipe tuple (game/parent/Steam/companion/host/CGameID/PID-birth), then validate the exact Surface owner/birth/class/root/cookie/rectangle/properties and actual game foreground before and after capture. Any generation replacement/expiry/source drift revokes the capture. No arbitrary Steam/chat/browser window should be selected.

## Build and offline checks

Retirement consumes queued request authority without blocking the message loop. The producer's real TakeOpenRequest method accepts requests up to400ms old. After A is hidden and its view revoked, admission discards requests for401ms, then requires an observed zero; that zero never starts B. Only a positive callback on a later iteration can start B. The main message pump dispatches at most64 messages before rechecking stop/expiry. This prevents a request queued before or during A retirement from automatically becoming B and prevents a continuously refilled message queue from starving retirement.

The separate barrier-fixture exercises the same production admission/pump header and a verbatim-extracted managed producer method with a fake clock:43 native checks and9 managed checks, with no OS input or Steam API. These checks supplement the original31 probe and64 core checks. They do not prove an actual renderer or controller interaction.

Use Visual Studio2022 x64/Windows SDK. No external dependency install is needed; core includes the existing MinHook source and full notices. Example:

    cmake -S <this-dir> -B <out> -G "Visual Studio 17 2022" -A x64 -DPLAYHUB_FOREGROUND_VIEW_TEXTURE_PROBE=ON -DPLAYHUB_COMPANION_FULLSCREEN_SNAPSHOT=ON -DPLAYHUB_COMPANION_PUBLIC_CONTEXT_TRIAL=OFF -DPLAYHUB_TEXTURE_RENDERER_SHA256=688CC70E58CD8D1F31345FDF93BA1B9AB08C77C38DF847BD2B0AE2145A437A2F
    cmake --build <out> --config Release
    ctest --test-dir <out> -C Release --output-on-failure
    python -B Verify-Adapter.py

The digest is the installed renderer file observed during preparation, not a future version guarantee. Reverify it for root's current session. The library never loads Valve code; the actual exact module must already be loaded by Steam and pass digest/path/signer/PE checks.

Policy tests create no UI/input. The core's explicit native fixture hooks only its own fake getter and loads two inert owned DLLs; it never hooks user32 or invokes Valve. Structural/PE checks validate the exclusive entry branch, forbidden-call/event absence, frozen core correspondence and single export. No runtime texture, browser or actual controller behavior is claimed by those tests.

The core revokes and disables its patch on exit but retains executable trampoline/module storage until the companion process exits, avoiding unsafe teardown of a concurrent prologue. No worker loop/input remains after Close. See core/README.md. No forced focus return or foreign process stop is performed by the adapter.

GPL-3.0-only original code; original native source notices retained. MinHook source/headers/LICENSE.txt/AUTHORS and embedded hde notices are included. The pinned GlosSI source is a documented pattern only; no GlosSI implementation is copied. The new helper and all required CMake/source/license files must accompany a candidate using this binary. Do not present the old NativeSource tree alone as this binary's corresponding source.

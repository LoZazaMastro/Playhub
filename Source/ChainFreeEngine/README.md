# Playhub Chain Free Engine

The program that starts the Playhub plugin inside Steam without Decky Loader. Its sources are `Host` (the .NET service), `Plugin` (the renderer side) and `Backend` (the Python runner). The published executable is `Playhub.ChainFreeEngine`.

This directory contains a bounded integration trial, not the full Playhub plugin port. Emulation development remains paused.

## Live trial, 2026-09-23

Steam was started with Big Picture and the published .NET host attached over CDP to its SharedJSContext. The frontend resolved React and UI exports from Steam's webpack cache. It mounted the original News/On this day/QAM preference components in a temporary overlay and signalled ready to the independent host. Backend readiness was verified against its exact instance ID. The backend loaded a copy of the real Playhub Python code and used copied settings exclusively, with hardware lifecycle disabled.

- Live mount reported ready after about 356 ms from host startup; Steam was already running and warmed. This is not a comparative benchmark or a full plugin startup measurement.
- A real preference was changed through the renderer-to-host-to-Python RPC path, read back successfully and restored in the isolated copy.
- Dispose removed the root completely. Authenticated shutdown stopped the host PID 22108 and its owned Python PID 7500.
- Hash comparison found no changes to the original plugin preference files or pre-existing Playhub/Shortcuts Steam localStorage entries.
- The existing Decky process and installed Playhub plugin remained running. The trial avoided their APIs and writers, but does not establish end-to-end operation with Decky absent.
- Native host publish passed without warnings/errors. Backend: 7 fixture tests and a real import/read smoke passed. Frontend: 4 bootstrap tests, build and syntax checks passed; ownership: 16 tests passed.

## User feedback and required correction

The user rejected the temporary panel's appearance. It was closed immediately. **Do not ship or expand this temporary interface as the Playhub redesign.** The standalone port must preserve the existing Playhub appearance and navigation. The trial's primary value is the real transport/backend/load/unload validation.

DOM inspection also showed the native dropdown discovery selected an unsuitable Steam control (the country field displayed “Steam Beta Update”). Replace heuristic component matching with a verified compatibility adapter before further UI testing. A CDP screenshot timed out while the Big Picture document reported hidden; visual acceptance has not passed. Do not call this UI production-ready or controller-tested.

## Verified Steam UI adapter, 2026-09-23

The heuristic component matching that picked the wrong native dropdown has been removed. `src/steamUi.ts` now resolves Steam's components through `@decky/ui` (LGPL-2.1, notice in `vendor/DeckyUI-LICENSE`), the same resolver Decky uses, and reads only Steam's own webpack chunk: no Decky loader, no loader globals, no other plugin's state.

- `src/steamUiContract.ts` lists every component and utility the real plugin uses. It is generated from the plugin sources by `tools/steam-ui-contract.mjs`, and `tests/steam-ui-adapter.test.mjs` regenerates and compares it, so a new component cannot silently reach an unverified host.
- The adapter verifies the whole contract on load and throws `Steam UI components unavailable: <names>` instead of substituting a different control.
- `src/standalone-bootstrap.js` now resolves only React, the render root and the visible Steam document. It no longer builds `DFL`.
- The interface itself is unchanged and still the rejected temporary panel: this step fixes how components are obtained, not what is shown. The real port of the existing Playhub interface is still the next task.
- Checks on 2026-09-23: 10 frontend tests (6 bootstrap + 4 adapter, from 4), 16 ownership, 7 backend. The bundle was rebuilt (`dist/playhub-standalone.js`) and carries the resolver. Not tested inside Steam yet.

## The real plugin, loaded by the Chain Free Engine, 2026-09-23

The temporary panel is gone. The standalone entry now loads the plugin's **own build** — `dist/index.js`, the same file Decky loads — so the interface, the screens and the behaviour are the plugin's, 1:1, with nothing re-implemented here.

- `src/pluginBundle.mjs` prepares that build for a host that is not Decky: it rewrites the loader's asset URLs (`http://127.0.0.1:1337/plugins/Playhub/...`) to this host's own asset endpoint, replaces the private loader global with a value passed in, and turns the module's default export into a return. Steam's values (`SP_REACT`, `SP_JSX`, `SP_REACTDOM`, `DFL`) are passed in as locals: **no loader-shaped global is published**, so a running Decky is never shadowed.
- `src/steamHost.ts` provides the two hooks Decky would: routes (`addRoute`/`removeRoute`/`addPatch`/`removePatch`/global components) by wrapping the router's output, and the Quick Access Menu tab by wrapping the QAM view and adding one plain tab object, with the plugin's own key. `src/steamHostCore.mjs` holds the pure parts and is unit tested.
- `src/pluginBundle.mjs` + `steamHost` are wired by `src/standalone-entry.tsx` under the existing ownership lease: dispose calls the plugin's own `onDismount`, then removes the tab and the routes.
- Host (`Host/Program.cs`): new `--plugin-build <dir>` (a copy of the build **inside the workspace**; the live install is never read), `GET /plugin-bundle` (token) and `GET /plugin-assets/{plugin}/{path}` (no token, because `<img>` cannot send one — read-only, loopback, confined to that directory, Playhub only). `__PLAYHUB_HOST_CONFIG__` now carries `bundleUrl` and `assetBase`.
- Startup priority: the bundle is registered with `Page.addScriptToEvaluateOnNewDocument` **before** being evaluated, so on a Steam start or a renderer reload Playhub runs at document start, before the Quick Access Menu mounts and memoizes its view. It is removed again when the host stops. What is still missing for "first thing in Gaming Mode" is the orchestration: the agent must start this host as Gaming Mode starts, and reattach when Steam restarts.
- Checks on 2026-09-23: 24 frontend tests (bootstrap 6, UI adapter 4, host core 9, bundle loader 5 — the last one prepares the **real** 6 MB build: 2556 asset URLs rewritten, loader global replaced, result compiles), 16 ownership, 7 backend. Host builds with 0 warnings.
- Not yet proven: nothing of this has run inside Steam. The QAM tab, the routes and the visual result are unverified on the device, and `Published/Host` still holds the previous build — publish it on Windows before the next trial.

## Started with Gaming Mode, and packaged, 2026-09-23

- **Agent orchestration.** `Packaging\Source\GamingModeAgent\GamingMode.Services\ChainFreeEngineService.cs` starts the engine as the gaming session begins and keeps it up for as long as it lasts: it stops on the way back to Desktop Mode, restarts the engine if it dies during a session, gives up after 3 restarts in 10 minutes instead of fighting Steam, and stops it when the agent shuts down. `AgentHost` reads a descriptor next to the agent's configuration (`%APPDATA%\GamingMode\chain-free-engine.json`); with no descriptor nothing is started and Gaming Mode is unchanged. Sample: `StandaloneHost\chain-free-engine.sample.json`.
- **Installed layout.** The engine refuses inputs outside its workspace during a trial. `--allow-installed-plugin` states that the installed plugin and its settings under the user's profile are the intended inputs, which is what a machine without Decky needs; the renderer bundle must always sit inside the engine's own folder.
- **Preview packaging.** `Packaging\build-preview.ps1` now runs the engine checks, publishes the engine into `Published\Playhub\ChainFreeEngine`, and copies beside it the renderer bundle, the Python runner, the two third-party licences and the sample descriptor. `assemble-preview.py` packs everything under `Published\Playhub`, so the preview installer carries the engine.
- **Checks on 2026-09-23:** 10 engine-policy checks (`Packaging\ChainFreeEngineChecks`), 24 frontend tests, 16 ownership, 7 backend. The agent and the engine both compile with 0 errors.
- The production installer (`Playhub\Source\PlayhubSetup\build-all.bat`) is untouched: the engine ships only in the preview package for now.

## One build, one installer, 2026-09-23

`Playhub\Source\PlayhubSetup\build-all.bat` now builds everything and produces a single `Output\Playhub-Setup.exe` that installs the app, the agent, the plugin **and** the Chain Free Engine.

- Step 4 of the script runs the engine's policy checks and the renderer tests, builds the renderer bundle, publishes the engine into the app payload (`ChainFreeEngine\`), and copies the renderer bundle, the Python runner and the third-party licences next to it.
- **Python is included.** Without Decky Loader a machine has no interpreter for the plugin's Python backend, so the official embeddable distribution (3.12.7, in `Source\PlayhubSetup\Runtime\python\`) is verified by SHA-256 and extracted into `ChainFreeEngine\Python`. The plugin's backend only uses the standard library, so nothing else is needed.
- **No JSON to write.** `ChainFreeEngineDescriptor.Discover` works the configuration out of a normal installation — engine under the Playhub folder, plugin and settings under the user profile — and the agent starts the engine with the gaming session. An explicit `%APPDATA%\GamingMode\chain-free-engine.json` still wins, and `enabled: false` (or no engine folder) means nothing starts.
- The production agent now carries `ChainFreeEngineService`; `Playhub\Source\ChainFreeEngineChecks` tests it (11 checks).

## Next work

Port the existing entry/lifecycle, routes, QAM, dashboard and backend capabilities without replacing the interface. Integrate the tested migration contract, a single authoritative backend and settings provider, and real fenced unload/rollback. Test with Decky absent as well as present. Measure performance against the same Steam version and plugin configuration. No performance superiority is currently established.

The host currently requires local Python and a settings snapshot. It uses an ephemeral loopback port, an instance token for RPC, Steam-origin checks and bounded serialized backend requests. It is intentionally not registered for startup and is **not included in the existing preview installer**. Its native process is published under `Published/Host`; all live-trial logs, fingerprints and snapshots are private under `.local`.

The original code and license notices are retained in `Plugin` and `Plugin/vendor`. The earlier independently tested migration/ownership foundations remain in `Migration`, `MigrationChecks`, `Runtime` and `RuntimeChecks`.

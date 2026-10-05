# Isolated Playhub standalone settings trial

This bundle mounts the original Playhub `HomeNewsSettings`, `HomeHistorySettings` and `QamSettings` components. It is a working settings-surface adapter awaiting verification in the actual Steam renderer, not the complete standalone plugin. It does not start the original `definePlugin` lifecycle, hardware helpers, dashboard watchers, power-menu patches or QAM installation.

## Build

Run `node build.mjs` here. Build dependencies are resolved read-only from the original source installation, default `F:/Playhub/Plugin/Playhub/Source/GamingModeDeckyPlugin`; set `PLAYHUB_PLUGIN_SOURCE` to override. No `node_modules`, binaries or large editorial images are copied. Images in unreachable news/history display code are omitted; the build fails if any omitted image survives tree shaking.

Output: `dist/playhub-standalone.js`, one injectable IIFE. The host sets `window.__PLAYHUB_HOST_CONFIG__ = {baseUrl, token, instanceId}` before evaluation. The adapter uses authenticated loopback `POST /rpc` with `{method,args}` and `POST /runtime-event` with lifecycle state. Credentials are never written to logs or browser storage.

Steam React/ReactDOM/JSX and the four required UI components are discovered from Steam webpack itself. No Decky library globals or APIs are used. The resolver inspects already-loaded modules and, where a cache is unavailable, only factories with matching React/UI markers. It does not instantiate the complete module graph. It refuses startup if the native UI or visible document cannot be found.

`window.__PLAYHUB_STANDALONE__` exposes `state`, `ready`, `diagnostics` and `dispose()`. Ready is sent only after a React commit. Close, Escape and native gamepad cancel dispose the isolated root, listeners and styling. The shared Playhub ownership helper prevents overlapping standalone mounts and retains its lock if cleanup fails.

The surface edits only the backend's isolated preference copy. Steam tabs, the running Decky plugin, live hardware settings and news integration are untouched. QAM has no discovered native/plugin registry in this trial; the original editor reports that limitation. Its migration cache key is changed to `playhub.standalone.experiment.qam.preferences.v1`, so it does not reimport live renderer preferences. News/history notifications remain module-local; no global integration installer is called.

## Source and licenses

The 79 TypeScript sources are copied from `Playhub/Source/GamingModeDeckyPlugin` (Playhub 2.0.1); original headers are retained. Changes are limited to the standalone entry/bootstrap, the `decky.ts` and `controlBackend.ts` adapters and the isolated QAM cache key. The original QAM renderer/icons retain their GPL notices and `LICENSE-Shortcuts`; other original project licensing remains applicable. This directory includes those sources for inspection and modification.

Steam discovery techniques are adapted from [ShelvesHub d237922](https://github.com/santojon/ShelvesHub/tree/d2379229907121840757672574c8eac938932548), `runtime/shelves-host.js`, copyright Jonathan Santos, MIT. Its complete license is in `vendor/ShelvesHub-LICENSE`. The full ShelvesHub runtime is not executed or bundled.

Checks: `node --check dist/playhub-standalone.js`; `node --test tests/bootstrap.test.mjs`; ownership lifecycle tests are in `../RuntimeChecks`.

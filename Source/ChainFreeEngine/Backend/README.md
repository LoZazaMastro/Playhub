# Isolated preference backend

`runner.py` imports the real Playhub `main.Plugin` from the copied `StandaloneHost/Plugin` backend. It provides an explicit minimal `decky` compatibility module containing the logger and plugin/settings/runtime directory constants. It does not import an installed Decky loader.

The runner never calls `_main` or `_unload`: these hooks start hardware helpers, recover pending display transactions, restore AMD profiles and stop processes in the real plugin. Calling unload without owning that lifecycle would also be unsafe. Only the constructor and reviewed preference methods are used. The existing module's DPI-awareness setup affects this child process only.

Invocation, supervised by the host:

```powershell
python -I StandaloneHost/Backend/runner.py --workspace <absolute-StandaloneHost> --plugin-root <absolute-StandaloneHost/Plugin> --settings-dir <absolute-StandaloneHost/.local/test-settings> --instance-id <unique-instance>
```

Add `--allow-isolated-writes` only for a session that may edit its **copied** preferences. Settings must be in `.local` below the explicit workspace; plugin and settings paths must be separate and existing reparse paths are rejected. This is path checking, not an OS sandbox for arbitrary untrusted Python. The host must use the reviewed plugin copy and constrain its own caller/transport.

The process takes an exclusive OS file lock on `.playhub-backend.lock` until exit. A second process using the same copied settings fails before importing the plugin. Requests are served serially; there is no concurrent hardware worker. The lock does not coordinate with the installed Decky backend, which uses different/live data, and is not a live migration authorization.

Protocol: one UTF-8 JSON object per line on stdio. Stdout is reserved for responses; plugin output and logging go to stderr, including writes through file descriptor 1. Requests and responses are bounded to 1 MiB. Example:

```json
{"id":1,"method":"get_panel_preferences","args":null}
{"id":1,"ok":true,"result":{}}
```

`args` can be null, an array of positional arguments, or an object of named arguments. Errors are `{id,ok:false,error:{code,message}}`; argument values and exception details are not echoed. `host.status` returns instance/process identity, `mode:"isolated-preferences"`, `backendReady`, read/write capabilities, `settingsLeaseHeld`, `writerActive`, `hardwareEnabled:false`, and `lifecycleStarted:false`. `backendReady` describes this narrow preference API, not complete hardware support or frontend readiness. `host.shutdown` replies then exits; EOF also exits and releases the lock.

Read allowlist:

- `get_panel_preferences`
- `get_qam_preferences`
- `get_home_news_settings`
- `get_daily_history_settings`

Optional isolated-write allowlist:

- `save_panel_preferences`
- `set_qam_preferences`
- `set_home_news_settings`
- `set_daily_history_settings`

All other plugin methods, including every private/lifecycle method, are rejected. Some native preference reads can perform legacy migration inside the copied settings area if legacy siblings exist; this runner must never be aimed at installed settings. The supplied test snapshot already contains current preferences.

Verification on 2026-09-23:

- `python StandaloneHost/Backend/test_runner.py`: **7/7 tests pass**; channel isolation, method restrictions, default denied writes, enabled isolated writes, duplicate-owner rejection and release, path rejection and no source pycache.
- `python StandaloneHost/Backend/real_import_check.py`: real source copied to a separate `.local` fixture, four existing settings files copied, **six RPC responses succeeded**, exit 0, empty stderr. Original settings SHA256 unchanged. No hardware lifecycle called.
- Host integration copy prepared: eight Python backend files under `StandaloneHost/Plugin`; nine current settings/backup/flag files copied to `StandaloneHost/.local/test-settings`. Original file hashes unchanged. No file contents were printed.

The real-import smoke records only status and names/hashes-derived invariance in `Backend/.local/real-import-result.json`. Its copied preferences stay private and must not enter an installer. All smoke child processes exited; the parent host owns any subsequent live experiment process.

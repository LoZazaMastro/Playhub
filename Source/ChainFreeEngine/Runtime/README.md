# Playhub renderer ownership

This is a tested lifecycle foundation, not a working standalone host. It does not connect to Steam, load Decky, mount Playhub UI or migrate an installation.

The module coordinates one Playhub owner per renderer global: `decky` or `standalone`. Both entry points must use this module and share the same renderer global. Other plugins are unaffected. Separate windows, isolated JavaScript worlds and processes have separate registries; this is not a cross-process lock or security boundary.

```js
import { mountPlayhubRenderer } from './renderer-ownership.mjs';

const lease = mountPlayhubRenderer({
  owner: 'standalone', // The Decky entry point uses 'decky'.
  signal: startupAbortController.signal, // Optional.
  async mount({ signal, defer }) {
    // Example adapter functions, not provided by this module.
    const surface = createPlayhubSurface();
    defer(() => surface.dispose());
    await surface.initialize({ signal });
  },
});

await lease.ready;
// Later, during host shutdown:
await lease.dispose();
```

Claiming is synchronous. A duplicate startup throws `RendererOwnershipError` with code `ALREADY_OWNED` before running its callback. `lease.ready` resolves after mounting; each accepted claim receives a monotonically increasing generation. `getPlayhubRendererOwnership()` returns a read-only snapshot or `null`.

Register cleanup with `defer` before initialization that might fail. Mount may also return a cleanup function. Cleanups run once, in reverse registration order, and can be asynchronous. All initialization must be awaited by mount; work detached from its promise cannot be tracked. Registering cleanup after mount has settled is rejected.

Calling `dispose()` or aborting the optional signal cancels mounting and waits for it to settle, including a cleanup function returned late. The owner remains locked while mount or cleanup is pending. Cancellation is cooperative: a mount that ignores its signal and never settles keeps the slot occupied. There are no timers or polling loops.

A mount failure runs registered cleanup before releasing the slot. If any cleanup fails, remaining cleanup still runs, but ownership stays `failed` and blocks another mount. There is deliberately no force-release or retry API: a safe retry requires a fresh renderer. Repeated `dispose()` calls share the same result; an old lease cannot release a newer generation.

Tests use the optional `renderer` object to model separate globals without touching the live application:

```powershell
node --test StandaloneHost/RuntimeChecks/renderer-ownership.test.mjs
```

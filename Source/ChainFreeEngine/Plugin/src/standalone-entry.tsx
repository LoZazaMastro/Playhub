// Il renderer carica la build originale del plugin, senza ricrearne l'interfaccia.
// Il lease esterno resta l'arbitro; i controlli locali proteggono anche l'attesa asincrona.
import { DFL } from './steamUi';
import { installRouterHook, installQamTab, createToaster } from './steamHost';
import { createLoaderApi, loadPluginBundle } from './pluginBundle.mjs';
import { mountPlayhubRenderer } from '../../Runtime/renderer-ownership.mjs';

const host = (window as any).__PLAYHUB_HOST__;
const config = (window as any).__PLAYHUB_HOST_CONFIG__ ?? {};
const controller = new AbortController();
const status: any = { state: 'mounting', diagnostics: host.diagnostics, dispose: () => lease.dispose() };
(window as any).__PLAYHUB_STANDALONE__ = status;

const endpoint = String(config.baseUrl ?? '').replace(/\/$/, '');
const bundleUrl = String(config.bundleUrl ?? endpoint + '/plugin-bundle');
const assetBase = String(config.assetBase ?? endpoint + '/plugin-assets/');

async function readBundle(signal: AbortSignal): Promise<string> {
  const response = await fetch(bundleUrl, { signal, headers: { Authorization: 'Bearer ' + config.token } });
  if (!response.ok) throw new Error('The Playhub build could not be read: HTTP ' + response.status);
  return response.text();
}

const lease = mountPlayhubRenderer({
  owner: 'standalone',
  renderer: window,
  signal: controller.signal,
  async mount({ defer, signal }: any) {
    if (host.hasForeignRenderer?.()) throw new Error('Another renderer owns Steam.');
    const source = await readBundle(signal);
    if (signal.aborted || host.hasForeignRenderer?.()) throw new Error('Standalone mounting cancelled.');
    const routerHook = installRouterHook();
    defer(() => routerHook.uninstall());
    const toaster = createToaster();
    const loaderApi = createLoaderApi({
      call: (method: string, args: unknown[]) => host.call(method, ...(Array.isArray(args) ? args : [])),
      routerHook,
      toaster,
    });
    const { plugin, prepared } = loadPluginBundle(source, {
      assetBase,
      globals: { SP_REACT: host.React, SP_JSX: host.jsx, SP_REACTDOM: host.ReactDOM, DFL },
      loaderApi,
      evaluate: undefined,
    });
    defer(() => { try { plugin.onDismount?.(); } catch (error) { console.warn('[Playhub standalone] onDismount', error); } });
    const qam = installQamTab({ title: plugin.titleView, icon: plugin.icon, content: plugin.content });
    defer(() => qam.uninstall());
    status.plugin = { name: plugin.name, assets: prepared.assets };
    status.surface = { qamAttached: () => qam.attached, qamInserted: () => qam.inserted, routerPatched: () => routerHook.installed, qamDiagnostics: () => qam.diagnostics };
    await host.event({ state: 'loaded', plugin: plugin.name, assets: prepared.assets });
  },
});

let disposal: Promise<void> | undefined;
const ownershipWatch = setInterval(() => {
  if (host.hasForeignRenderer?.()) void status.dispose().catch((error: unknown) => console.warn('[Playhub standalone] dispose', error));
}, 250);
status.dispose = () => {
  if (disposal) return disposal;
  controller.abort();
  clearInterval(ownershipWatch);
  (window as any).__PLAYHUB_BOOTSTRAP__?.cancel?.();
  disposal = (async () => {
    try { await lease.dispose(); }
    finally {
      // La pulizia controlla l'identita': eventuali global sopraggiunti di Decky
      // non diventano nostri solo perche' hanno lo stesso nome.
      try { host.releaseOwnedGlobals?.(); } catch { /* gia' rimossi */ }
      status.state = 'disposed';
      await host.event({ state: 'disposed' });
    }
  })();
  return disposal;
};
status.ready = lease.ready
  .then(async () => {
    if (controller.signal.aborted) return;
    status.state = 'ready';
    await host.event({ state: 'ready', surface: 'original-plugin-build', diagnostics: host.diagnostics });
  })
  .catch(async (error: unknown) => {
    if (controller.signal.aborted) return;
    status.state = 'failed'; status.error = String(error);
    await host.event({ state: 'failed', reason: String(error) });
  });

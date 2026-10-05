// La scoperta di Steam deriva da ShelvesHub d237922 (MIT, vendor/ShelvesHub-LICENSE).
// @decky/ui continua a risolvere i controlli nativi: nessun componente sostitutivo.
async function bootstrapPlayhubStandalone() {
  const config = window.__PLAYHUB_HOST_CONFIG__;
  if (!config?.baseUrl || !config?.token) throw new Error('Missing Playhub host configuration.');
  if (window.__PLAYHUB_STANDALONE__ && window.__PLAYHUB_STANDALONE__.state !== 'disposed')
    throw new Error('Playhub standalone is already mounted.');
  const endpoint = new URL(config.baseUrl);
  if (endpoint.protocol !== 'http:' || !['127.0.0.1', 'localhost'].includes(endpoint.hostname))
    throw new Error('The Playhub host must be loopback.');
  const owned = new Map();
  const names = ['SP_REACT', 'SP_REACTDOM', 'SP_JSX'];
  const deckyApi = '__DECKY_SECRET_INTERNALS_DO_NOT_USE_OR_YOU_WILL_BE_FIRED_deckyLoaderAPIInit';
  let cancelled = false;
  const boot = { state: 'waiting', instanceId: config.instanceId, injectionId: config.injectionId,
    cancel() { cancelled = true; boot.state = 'disposed'; } };
  window.__PLAYHUB_BOOTSTRAP__ = boot;
  const send = async (path, body) => {
    const response = await fetch(endpoint.origin + path, {
      method: 'POST', headers: { 'Content-Type': 'application/json', Authorization: 'Bearer ' + config.token },
      body: JSON.stringify(body),
    });
    if (!response.ok) throw new Error('Playhub host HTTP ' + response.status);
    return response.json();
  };
  const host = {
    call: async (method, ...args) => {
      const result = await send('/rpc', { method, args });
      if (!result.ok) throw new Error(result.error || 'Playhub RPC failed.');
      return result.result;
    },
    event: data => send('/runtime-event', { ...data, instanceId: config.instanceId, injectionId: config.injectionId }).catch(() => {}),
    hasForeignRenderer: () => !!window[deckyApi] || !!window.deckyLoader || names.some(name =>
      window[name] !== undefined && (!owned.has(name) || window[name] !== owned.get(name))),
    releaseOwnedGlobals() {
      // Decky puo' avere sostituito un valore dopo il bootstrap. In quel caso
      // non cancelliamo il suo global solo perche' prima quel nome era nostro.
      // Se Decky subentra puo' riusare gli stessi oggetti React: in quel caso
      // neppure l'identita' prova che il valore sia ancora esclusivamente nostro.
      const deckyTookOver = !!window[deckyApi] || !!window.deckyLoader;
      for (const [name, value] of owned) {
        try { if (!deckyTookOver && window[name] === value) delete window[name]; } catch {}
      }
      owned.clear();
      host.ownedGlobals = [];
    },
    ownedGlobals: [],
  };
  window.__PLAYHUB_HOST__ = host;
  let lastReason = '';
  const waiting = async reason => {
    if (reason !== lastReason) { lastReason = reason; await host.event({ state: 'waiting', reason }); }
    await new Promise(resolve => setTimeout(resolve, 500));
  };
  let req;
  const modules = new Map();
  let variants = [];
  let scanCount = -1;
  let runtime;
  while (true) {
    if (cancelled || window.__PLAYHUB_HOST_CONFIG__ !== config) throw new Error('Playhub bootstrap cancelled.');
    if (host.hasForeignRenderer()) {
      boot.state = 'blocked';
      await host.event({ state: 'blocked', reason: 'Another renderer owns the Steam globals.' });
      throw new Error('Another renderer owns the Steam globals.');
    }
    if (!req) {
      const chunkKey = Object.keys(window).find(key => key.startsWith('webpackChunk') && Array.isArray(window[key]));
      if (chunkKey) window[chunkKey].push([[Symbol('playhub.standalone.discovery')], {}, value => { req = value; }]);
    }
    if (!req?.m) { await waiting('Steam webpack is not available yet.'); continue; }
    const ids = Object.keys(req.m);
    if (!runtime || scanCount !== ids.length) {
      const remember = (id, value) => { if (value) modules.set(id, value); };
      for (const [id, mod] of Object.entries(req.c || {})) remember(id, mod?.exports);
      // La build verificata non espone req.c: si conservano i risultati di
      // req(id). La mancanza di una finestra non fa ripetere questa scansione.
      for (const id of ids) {
        if (modules.has(id)) continue;
        try { remember(id, req(id)); } catch {}
      }
      variants = [];
      const unique = new Set();
      const add = value => { if (value && !unique.has(value)) { unique.add(value); variants.push(value); } };
      for (const value of modules.values()) {
        for (const candidate of [value, value?.default]) {
          add(candidate);
          if (candidate && (typeof candidate === 'object' || typeof candidate === 'function'))
            for (const key of Object.keys(candidate)) try { add(candidate[key]); } catch {}
        }
      }
      const find = predicate => variants.find(value => { try { return predicate(value); } catch { return false; } });
      const React = find(value => typeof value?.createElement === 'function' && typeof value?.useState === 'function');
      const dom = find(value => typeof value?.createPortal === 'function');
      const client = find(value => typeof value?.createRoot === 'function');
      if (React && (client?.createRoot || dom?.render)) {
        const jsx = find(value => typeof value?.jsx === 'function' && typeof value?.jsxs === 'function') || {
          Fragment: React.Fragment,
          jsx: (type, props, key) => React.createElement(type, key === undefined ? props : { ...props, key }),
          jsxs: (type, props, key) => React.createElement(type, key === undefined ? props : { ...props, key }),
        };
        runtime = { React, ReactDOM: { ...dom, ...client }, jsx,
          router: find(value => value?.Navigate && value?.NavigationManager) || window.SteamUIStore };
      }
      scanCount = ids.length;
    }
    if (!runtime) { await waiting('Steam React renderer is not available yet.'); continue; }
    const candidates = [];
    const add = instance => {
      for (const value of [instance, instance?.window, instance?.m_Window, instance?.m_popup,
        instance?.m_BrowserWindow, instance?.BrowserWindow])
        try { if (value?.document?.body && !candidates.includes(value.document)) candidates.push(value.document); } catch {}
    };
    const store = runtime.router?.WindowStore || window.SteamUIStore?.WindowStore;
    add(store?.GamepadUIMainWindowInstance);
    for (const value of store?.SteamUIWindows || []) add(value);
    try { for (const value of window.g_PopupManager?.GetPopups?.() || []) add(value); } catch {}
    if (document.body && !/SharedJSContext/i.test(document.title)) candidates.push(document);
    const targetDocument = candidates.find(doc => !/SharedJSContext/i.test(doc.title));
    if (!targetDocument) { await waiting('The visible Steam window is not available yet.'); continue; }
    if (host.hasForeignRenderer()) continue;
    const provide = (name, value) => {
      if (value && window[name] === undefined) { window[name] = value; owned.set(name, value); }
    };
    provide('SP_REACT', runtime.React);
    provide('SP_REACTDOM', runtime.ReactDOM);
    provide('SP_JSX', runtime.jsx);
    Object.assign(host, runtime, {
      targetDocument, targetDocuments: candidates, moduleExports: variants, ownedGlobals: [...owned.keys()],
      diagnostics: { moduleCount: modules.size, documentTitle: targetDocument.title },
    });
    boot.state = 'bootstrapped';
    return;
  }
}

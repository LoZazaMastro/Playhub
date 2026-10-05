// Il plugin resta quello originale: qui si collegano soltanto rotte e QAM.
// La tecnica delle liste deriva da ShelvesHub d237922 (MIT, vendor/ShelvesHub-LICENSE).
import { DFL } from './steamUi';
import {
  OUR_ROUTES, applyRoutePatches, createRouteBuilder, injectRoutes, insertQamTab,
  registerTabKey, routeTypeFromList,
} from './steamHostCore.mjs';
import { currentRoot, walkFibers, refreshFiber, mapRenderedTabs, removeOwnedTab } from './react19Refresh.mjs';

type Component = (...args: any[]) => any;
const host = (window as any).__PLAYHUB_HOST__;
const React = host?.React;
const ui = DFL as any;
const source = (value: any) => { try { return String(value?.render || value); } catch { return ''; } };
const warn = (...args: unknown[]) => console.warn('[Playhub standalone]', ...args);
const DECKY_API = '__DECKY_SECRET_INTERNALS_DO_NOT_USE_OR_YOU_WILL_BE_FIRED_deckyLoaderAPIInit';
export const PLAYHUB_TAB_KEY = 0x50484B;
const TAB_NAME = 'Playhub';
const TAB_AFTER = 5;

function allowed(): boolean {
  try { return !(window as any)[DECKY_API] && !host?.hasForeignRenderer?.(); }
  catch { return false; }
}

// Il QAM puo' vivere in una finestra diversa dalla principale. Si cercano solo
// root di React, mai istanze alternate o nodi ricostruiti da un vecchio render.
function roots(): any[] {
  const docs = new Set<any>([document, host?.targetDocument, ...(host?.targetDocuments || [])]);
  const add = (instance: any) => {
    for (const value of [instance, instance?.window, instance?.m_Window, instance?.m_popup,
      instance?.m_BrowserWindow, instance?.BrowserWindow]) {
      try { if (value?.document?.body) docs.add(value.document); } catch { /* finestra chiusa */ }
    }
  };
  try {
    const store = (window as any).SteamUIStore?.WindowStore;
    add(store?.GamepadUIMainWindowInstance);
    for (const value of store?.SteamUIWindows || []) add(value);
    for (const value of (window as any).g_PopupManager?.GetPopups?.() || []) add(value);
  } catch { /* Steam sta ricreando le finestre */ }
  const result = new Set<any>();
  for (const doc of docs) {
    if (!doc) continue;
    let elements: any[] = [];
    try { elements = [doc.getElementById('root'), doc.body, doc.documentElement, ...Array.from(doc.body?.children || []).slice(0, 32)]; }
    catch { continue; }
    for (const element of elements) {
      if (!element) continue;
      try { const root = currentRoot(ui.getReactRoot?.(element)); if (root) result.add(root); } catch { /* adattatore non disponibile qui */ }
      for (const key of Object.keys(element)) {
        if (!key.startsWith('__reactContainer$') && !key.startsWith('__reactFiber$')) continue;
        const root = currentRoot(element[key]);
        if (root) result.add(root);
      }
    }
  }
  return [...result];
}

function fibers(match: (node: any) => boolean): any[] {
  const result = new Set<any>();
  for (const root of roots()) walkFibers(root, (node: any) => { if (match(node)) result.add(node); });
  return [...result];
}

function renderWrapper(inner: Function, after: (value: any) => any): Function {
  const wrapped = function (this: unknown, ...args: unknown[]) {
    return after(inner.apply(this, args));
  };
  try { Object.assign(wrapped, inner); wrapped.toString = () => inner.toString(); } catch { /* funzione non estensibile */ }
  return wrapped;
}

function replaceLiveType(node: any, original: Function, replacement: Function) {
  // SimpleMemoComponent usa fiber.type, non rilegge memo.type. Si aggiornano
  // entrambe le copie, ma solo se non sono state sostituite da qualcun altro.
  for (const copy of [node, node?.alternate])
    if (copy && copy.type === original && [0, 15].includes(copy.tag)) copy.type = replacement;
}

export interface RouterHook {
  addRoute(path: string, component: Component, props?: Record<string, unknown>): void;
  removeRoute(path: string): void;
  addPatch(path: string, patch: Function): Function;
  removePatch(path: string, patch: Function): Function;
  addGlobalComponent(id: string, component?: Component): () => void;
  removeGlobalComponent(id: string): void;
  uninstall(): void;
  readonly installed: boolean;
}

export function installRouterHook(): RouterHook {
  const routes = new Map<string, { component: Component; props: Record<string, unknown> }>();
  const patches = new Map<string, Set<Function>>();
  const globals = new Map<string, Component>();
  const listeners = new Set<() => void>();
  const routeLists = new Set<any[]>();
  const buildRoutes = createRouteBuilder(React);
  let wrapperMounted = false;
  let stopped = false;
  let patch: { owner: any; original: Function; wrapped: Function } | null = null;
  let timer: ReturnType<typeof setTimeout> | undefined;
  let attempts = 0;
  const tried = new WeakSet<object>();
  const bump = () => listeners.forEach(listener => { try { listener(); } catch { /* componente smontato */ } });
  const subscribe = (listener: () => void) => { listeners.add(listener); return () => { listeners.delete(listener); }; };
  const removeSlots = (list: any[]) => {
    for (let i = list.length - 1; i >= 0; i--) if (list[i]?.[OUR_ROUTES]) list.splice(i, 1);
  };

  function process(output: any) {
    if (!output?.props) return;
    const top = output.props.children;
    const containers = Array.isArray(top) ? top : [top];
    const lists: any[][] = [];
    for (const container of containers)
      if (Array.isArray(container?.props?.children)) lists.push(container.props.children);
    for (const list of lists) {
      if (stopped || !allowed()) { removeSlots(list); continue; }
      applyRoutePatches(list, patches, (path: string, error: unknown) => warn('route patch', path, error));
    }
    if (stopped || !allowed() || !lists.length) return;
    if (!routes.size) { removeSlots(lists[0]); return; }
    const Route = routeTypeFromList(lists[0]);
    if (!Route) return;
    injectRoutes(lists[0], buildRoutes(Route, routes, undefined));
    routeLists.add(lists[0]);
    if (routeLists.size > 64) routeLists.delete(routeLists.values().next().value!);
  }

  function RouteWrapper(props: any) {
    const [, set] = React.useState(0);
    React.useEffect(() => subscribe(() => set((value: number) => (value + 1) | 0)), []);
    try { process(props.children); } catch (error) { warn('router wrapper', error); }
    return props.children;
  }
  function GlobalWrapper() {
    const [, set] = React.useState(0);
    React.useEffect(() => subscribe(() => set((value: number) => (value + 1) | 0)), []);
    if (stopped || !allowed() || !globals.size) return null;
    const extras: any[] = [];
    globals.forEach((component, id) => extras.push(React.createElement(component, { key: 'playhub-global:' + id })));
    return React.createElement(React.Fragment, null, extras);
  }
  function after(rendered: any) {
    if (stopped || !allowed() || !rendered?.props) return rendered;
    wrapperMounted = true;
    return React.createElement(React.Fragment, { key: 'playhub-router-root' },
      React.createElement(RouteWrapper, { key: 'playhub-router' }, rendered),
      React.createElement(GlobalWrapper, { key: 'playhub-globals' }));
  }
  function attempt() {
    timer = undefined;
    if (stopped || !allowed()) return;
    if (!patch) {
      const node = fibers(candidate => {
        const type = candidate?.elementType;
        return typeof type?.type === 'function' && source(type.type).includes('Settings.Root()');
      })[0];
      if (node) {
        const owner = node.elementType;
        const original = owner.type;
        const wrapped = renderWrapper(original, after);
        try { owner.type = wrapped; if (owner.type === wrapped) patch = { owner, original, wrapped }; }
        catch (error) { warn('router patch', error); }
      }
    }
    if (patch) {
      const { owner, original, wrapped } = patch;
      for (const node of fibers(candidate => candidate.elementType === owner)) {
        replaceLiveType(node, original, wrapped);
        if (!wrapperMounted && !tried.has(node)) {
          const result = refreshFiber(node, host?.ReactDOM, React?.version);
          host.diagnostics.routerRefresh = result;
          if (result.scheduled) { tried.add(node); if (node.alternate) tried.add(node.alternate); }
        }
      }
    }
    if (!wrapperMounted && attempts++ < 120) timer = setTimeout(attempt, 500);
  }
  function requestUpdate() {
    if (stopped) return;
    if (wrapperMounted) { bump(); return; }
    if (timer === undefined) timer = setTimeout(attempt, 0);
  }
  return {
    get installed() { return !!patch; },
    addRoute(path, component, props) { routes.set(path, { component, props: props || {} }); requestUpdate(); },
    removeRoute(path) { routes.delete(path); requestUpdate(); },
    addPatch(path, value) { if (!patches.has(path)) patches.set(path, new Set()); patches.get(path)!.add(value); requestUpdate(); return value; },
    removePatch(path, value) { patches.get(path)?.delete(value); requestUpdate(); return value; },
    addGlobalComponent(id, component) {
      const key = typeof id === 'string' ? id : 'global:' + globals.size;
      const value = (typeof id === 'string' ? component : id) as Component;
      if (value) { globals.set(key, value); requestUpdate(); }
      return () => { globals.delete(key); requestUpdate(); };
    },
    removeGlobalComponent(id) { globals.delete(id); requestUpdate(); },
    uninstall() {
      if (stopped) return;
      stopped = true;
      if (timer !== undefined) clearTimeout(timer);
      routes.clear(); patches.clear(); globals.clear();
      for (const list of routeLists) { try { removeSlots(list); } catch { /* lista non piu' mutabile */ } }
      routeLists.clear(); bump();
      if (patch) {
        const { owner, original, wrapped } = patch;
        if (owner.type === wrapped) owner.type = original;
        for (const node of fibers(candidate => candidate.elementType === owner)) {
          replaceLiveType(node, wrapped, original);
          refreshFiber(node, host?.ReactDOM, React?.version);
        }
      }
      patch = null;
    },
  };
}

export interface QamTabOptions { title?: any; icon?: any; content: any }

export function installQamTab(options: QamTabOptions) {
  let stopped = false;
  let visible = true;
  let attached = false;
  let owner: any;
  let original: Function | undefined;
  let wrappedView: Function | undefined;
  let tabEnum: any;
  const enumOwned: Array<{ key: string; value: any }> = [];
  let timer: ReturnType<typeof setTimeout> | undefined;
  const seen = new Set<any[]>();
  const tries = new WeakMap<object, number>();
  const wrappers = new WeakMap<Function, Function>();
  const diagnostics: any = { renderCalls: 0, refresh: null, collision: false, mounted: [] };
  const tab: any = {
    key: PLAYHUB_TAB_KEY, strTitle: TAB_NAME,
    title: options.title ?? React.createElement(React.Fragment, null),
    tab: options.icon ?? React.createElement(React.Fragment, null),
    panel: options.content, vrLocation: 'quick-access-menu', __playhubTab: true,
  };

  function transform(tabs: any[]): any[] {
    if (stopped || !allowed()) return tabs;
    if (tabs.some(value => value?.key === PLAYHUB_TAB_KEY && value !== tab)) {
      diagnostics.collision = true;
      return tabs;
    }
    const next = tabs.includes(tab) ? tabs : tabs.slice();
    // La visibilita' si aggiorna fuori dal render. Non chiamiamo setter Steam
    // durante il render del padre, ne' tocchiamo schede che non sono nostre.
    if (!next.includes(tab)) insertQamTab(next, tab, TAB_AFTER, visible);
    seen.add(next);
    if (seen.size > 64) seen.delete(seen.values().next().value!);
    return next;
  }
  function wrap(inner: Function): Function {
    const known = wrappers.get(inner);
    if (known) return known;
    const result = renderWrapper(inner, out => {
      if (stopped || !allowed()) return out;
      diagnostics.renderCalls++;
      try { return mapRenderedTabs(out, transform, React); }
      catch (error) { warn('qam tabs', error); return out; }
    });
    wrappers.set(inner, result);
    return result;
  }
  function liveViews(): any[] {
    return owner ? fibers(node => node.elementType === owner || (typeof wrappedView === 'function' && node.type === wrappedView)) : [];
  }
  function snapshot(): any[] {
    const rows: any[] = [];
    for (const view of liveViews()) {
      // Si visita il sottoalbero del QAM, non i suoi fratelli: un altro menu
      // puo' avere una props tabs senza essere il nostro consumatore.
      const collect = (node: any) => {
        const tabs = node.memoizedProps?.tabs;
        if (Array.isArray(tabs)) rows.push({ tag: node.tag, keys: tabs.map((value: any) => value?.key), own: tabs.includes(tab) });
      };
      collect(view);
      if (view.child) walkFibers(view.child, collect);
    }
    diagnostics.mounted = rows;
    return rows;
  }
  function registerEnum(): boolean {
    tabEnum = ui.QuickAccessTab ?? findTabEnum();
    if (!tabEnum) return false;
    if ((tabEnum[PLAYHUB_TAB_KEY] !== undefined && tabEnum[PLAYHUB_TAB_KEY] !== TAB_NAME) ||
        (tabEnum[TAB_NAME] !== undefined && tabEnum[TAB_NAME] !== PLAYHUB_TAB_KEY)) {
      diagnostics.collision = true; return false;
    }
    const missingNumber = tabEnum[PLAYHUB_TAB_KEY] === undefined;
    const missingName = tabEnum[TAB_NAME] === undefined;
    if (!registerTabKey(tabEnum, PLAYHUB_TAB_KEY, TAB_NAME)) return false;
    if (missingNumber) enumOwned.push({ key: String(PLAYHUB_TAB_KEY), value: TAB_NAME });
    if (missingName && tabEnum[TAB_NAME] === PLAYHUB_TAB_KEY) enumOwned.push({ key: TAB_NAME, value: PLAYHUB_TAB_KEY });
    return true;
  }
  function tick() {
    timer = undefined;
    if (stopped) return;
    if (!allowed()) { uninstall(); return; }
    try {
      if (!attached) {
        owner = findQuickAccessView();
        if (owner && typeof owner.type === 'function' && registerEnum()) {
          original = owner.type;
          wrappedView = wrap(original!);
          owner.type = wrappedView;
          attached = owner.type === wrappedView;
        }
      }
      if (attached && original && wrappedView) {
        for (const node of liveViews()) {
          replaceLiveType(node, original, wrappedView);
          let hasTab = false;
          const check = (candidate: any) => { if (candidate.memoizedProps?.tabs?.includes?.(tab)) hasTab = true; };
          check(node); if (node.child) walkFibers(node.child, check);
          const count = tries.get(node) || 0;
          if (!hasTab && count < 3 && !diagnostics.collision) {
            diagnostics.refresh = refreshFiber(node, host?.ReactDOM, React?.version);
            if (diagnostics.refresh.scheduled) {
              tries.set(node, count + 1);
              if (node.alternate) tries.set(node.alternate, count + 1);
            }
          }
        }
      }
      snapshot();
    } catch (error) { diagnostics.error = String(error); warn('qam attach', error); }
    if (!stopped) timer = setTimeout(tick, 1000);
  }
  function uninstall() {
    if (stopped) return;
    stopped = true;
    if (timer !== undefined) clearTimeout(timer);
    const views = liveViews();
    const collect = (node: any) => { const tabs = node.memoizedProps?.tabs; if (Array.isArray(tabs) && tabs.includes(tab)) seen.add(tabs); };
    for (const view of views) { collect(view); if (view.child) walkFibers(view.child, collect); }
    for (const tabs of seen) { try { removeOwnedTab(tabs, tab); } catch { /* lista congelata: il render nativo la sostituira' */ } }
    if (owner && wrappedView && owner.type === wrappedView) owner.type = original;
    if (original && wrappedView) for (const node of views) {
      replaceLiveType(node, wrappedView, original);
      refreshFiber(node, host?.ReactDOM, React?.version);
    }
    // Durante un passaggio a Decky il valore primitivo puo' essere gia' suo:
    // l'uguaglianza della stringa non dimostra piu' la proprieta' della voce.
    const handoff = !allowed() || diagnostics.collision;
    for (const { key, value } of enumOwned) {
      try { if (!handoff && tabEnum?.[key] === value) delete tabEnum[key]; } catch { /* enum non piu' modificabile */ }
    }
    enumOwned.length = 0; seen.clear(); attached = false;
  }
  // Si parte fuori dal render che installa il plugin: flushSync non va mai
  // richiamato da un componente o da un effetto in corso di commit.
  timer = setTimeout(tick, 0);
  return {
    get inserted() { return !stopped && snapshot().some(row => row.own); },
    get attached() { return attached; },
    get diagnostics() { snapshot(); return { ...diagnostics }; },
    wrapTabsComponent: wrap,
    setVisible(next: boolean) {
      visible = next;
      tab.initialVisibility = next;
      if (!stopped && allowed() && typeof tab.qAMVisibilitySetter === 'function') tab.qAMVisibilitySetter(next);
    },
    uninstall,
  };
}

function findQuickAccessView(): any {
  const carries = (value: any) => !!value && typeof value === 'object' && !!value.$$typeof &&
    typeof value.type === 'function' && source(value.type).includes('QuickAccessMenuBrowserView');
  try {
    // Il bootstrap conserva gli export di req(id): nessuna dipendenza da req.c.
    for (const value of host?.moduleExports || []) if (carries(value)) return value;
    if (typeof ui.findModuleExport === 'function') {
      const value = ui.findModuleExport(carries); if (value) return value;
    }
    return fibers(node => carries(node.elementType))[0]?.elementType;
  } catch { return undefined; }
}
function findTabEnum(): any {
  const carries = (value: any) => value && typeof value === 'object' &&
    typeof value.Notifications === 'number' && typeof value.Settings === 'number' && value[value.Settings] === 'Settings';
  try {
    for (const value of host?.moduleExports || []) if (carries(value)) return value;
    return ui.findModuleByExport?.(carries);
  } catch { return undefined; }
}

export function createToaster() {
  return {
    toast(options: { title?: string; body?: string }) {
      const title = String(options?.title ?? 'Playhub');
      const body = String(options?.body ?? '');
      try {
        const notifications = (window as any).SteamClient?.Notifications;
        if (notifications?.DisplayNotification) { notifications.DisplayNotification(title, body); return; }
      } catch { /* si mantiene il ripiego sul log */ }
      console.info('[Playhub]', title, body);
    },
  };
}

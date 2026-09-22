import type { Capability } from "./deckyHostRuntime";

export const STANDALONE_HOST = Symbol.for("playhub.decky-host-standalone.v1");
type Tab = { id?: number | string; key?: number | string; panel?: any; [key: string]: any };
type Hook = { tabs: Tab[]; render(tabs: Tab[], visible: boolean): unknown };
type ProjectionStore = { getSnapshot(): Tab[]; subscribe(listener: () => void): () => void };
type ProjectionState = { snapshot: Tab[]; listeners: Set<() => void>; pending: boolean; generation: number; store?: ProjectionStore };
type ProjectionRegistry = { version: 1; generation: number; arrays: WeakMap<Tab[], ProjectionState>;
  owners: WeakMap<Tab[], number>; focus: WeakMap<Element, { generation: number; originals: Map<string, string | null>; written: Map<string, string> }> };
const PROJECTIONS = Symbol.for("playhub.decky-tab-projections.v1");
const projectionHost = (typeof window === "undefined" ? globalThis : window) as any;
function sharedProjectionRegistry(): ProjectionRegistry | undefined {
  const existing = projectionHost[PROJECTIONS];
  if (existing !== undefined) {
    return existing?.version === 1 && Number.isSafeInteger(existing.generation)
      && [existing.arrays, existing.owners, existing.focus].every(value => Object.prototype.toString.call(value) === "[object WeakMap]") ? existing : undefined;
  }
  const registry: ProjectionRegistry = { version: 1, generation: 0, arrays: new WeakMap(), owners: new WeakMap(), focus: new WeakMap() };
  try { Object.defineProperty(projectionHost, PROJECTIONS, { value: registry, configurable: true }); }
  catch { return undefined; }
  return registry;
}
const projectionRegistry = sharedProjectionRegistry();
const projectionGeneration = projectionRegistry ? ++projectionRegistry.generation : 0;
const projectionsByArray = projectionRegistry?.arrays ?? new WeakMap<Tab[], ProjectionState>();
export const canShareDeckyTabProjection = () => !!projectionRegistry;
export function getDeckyTabProjection(tabs: Tab[]): ProjectionStore {
  let state = projectionsByArray.get(tabs);
  if (!state) {
    state = { snapshot: [...tabs], listeners: new Set(), pending: false, generation: projectionGeneration };
    projectionsByArray.set(tabs, state);
  }
  // One store object per tab array. useSyncExternalStore compares subscribe by identity:
  // a fresh closure per render resubscribes on every commit and, when the parent renders
  // during the same commit, React tears the subscription down mid-loop and throws.
  const owner = state;
  if (!owner.store) {
    owner.store = {
      getSnapshot: () => owner.snapshot,
      subscribe(listener: () => void) {
        const owned = () => listener();
        owner.listeners.add(owned);
        return () => { owner.listeners.delete(owned); };
      },
    };
  }
  return owner.store;
}
function publishTabProjection(tabs: Tab[]) {
  const state = projectionsByArray.get(tabs);
  if (!state || state.generation > projectionGeneration) return;
  // Retained consumers survive reload. New publishers fence late old cleanup.
  state.generation = projectionGeneration;
  if (state.snapshot.length === tabs.length && state.snapshot.every((tab, index) => tab === tabs[index])) return;
  state.snapshot = [...tabs];
  if (state.pending) return;
  state.pending = true;
  // Notify outside Steam's render stack; multiple projections in one commit coalesce.
  void Promise.resolve().then(() => {
    state.pending = false;
    state.listeners.forEach(listener => listener());
  });
}

/** Retained QAM DOM can outlive its tab-array projection. Restore only our writes. */
export function createNativeDeckyFocusSuppression() {
  const saved = new Map<Element, Map<string, { previous: string | null; written: string }>>();
  const write = (node: Element, name: string, value: string) => {
    if (typeof node?.setAttribute !== "function") return;
    let owner = projectionRegistry?.focus.get(node);
    if (owner && owner.generation > projectionGeneration) return;
    if (!owner) {
      owner = { generation: projectionGeneration, originals: new Map(), written: new Map() };
      projectionRegistry?.focus.set(node, owner);
    }
    owner.generation = projectionGeneration;
    if (!owner.originals.has(name) || (owner.written.has(name) && node.getAttribute(name) !== owner.written.get(name))) {
      owner.originals.set(name, node.getAttribute(name));
    }
    owner.written.set(name, value);
    let attributes = saved.get(node);
    if (!attributes) { attributes = new Map(); saved.set(node, attributes); }
    attributes.set(name, { previous: owner.originals.get(name) ?? null, written: value });
    node.setAttribute(name, value);
  };
  return {
    hide(document: Document) {
      for (const id of ["quickaccess_tab_999", "quickaccess_content_999"]) {
        const root = document.getElementById(id);
        if (!root) continue;
        write(root, "aria-hidden", "true");
        write(root, "inert", "");
        write(root, "tabindex", "-1");
        for (const child of root.querySelectorAll?.('[tabindex],button,a[href],input,select,textarea,[contenteditable="true"]') ?? []) {
          write(child, "tabindex", "-1");
        }
      }
    },
    restore() {
      for (const [node, attributes] of saved) {
        if ((projectionRegistry?.focus.get(node)?.generation ?? 0) > projectionGeneration) continue;
        for (const [name, value] of attributes) {
          if (node.getAttribute(name) !== value.written) continue;
          if (value.previous === null) node.removeAttribute(name);
          else node.setAttribute(name, value.previous);
        }
        projectionRegistry?.focus.delete(node);
      }
      saved.clear();
    },
  };
}

/** Cooperates with the existing QAM owner; never installs its own render wrapper. */
export function createStandaloneDeckyHost(hook: Hook, nativeRender: Hook["render"], tabId: number | (() => number | undefined),
  ownsHook: () => boolean, refresh: () => void) {
  let stopped = false;
  let current: { release(reason: string): void; valid(): boolean; project(tabs: Tab[], visible: boolean): boolean } | undefined;
  const projections = new Set<{ array: { deref(): Tab[] | undefined }; tab: Tab; index: number }>();
  const restore = (only?: Tab[], notify = false) => {
    for (const record of projections) {
      const tabs = record.array.deref();
      if (!tabs) { projections.delete(record); continue; }
      if (only && only !== tabs) continue;
      if ((projectionRegistry?.owners.get(tabs) ?? 0) > projectionGeneration) { projections.delete(record); continue; }
      if (!tabs.some(tab => String(tab.key) === "999")) tabs.splice(Math.min(record.index, tabs.length), 0, record.tab);
      projections.delete(record);
      if (notify) publishTabProjection(tabs);
    }
  };
  let suppressNative = false;
  /** Hiding the native Decky tab must not wait for Playhub's own tab to mount. */
  const suppressTabs = (tabs: Tab[]) => {
    if (!suppressNative || stopped || Object.isFrozen(tabs) || Object.isSealed(tabs)) return;
    try { if (!ownsHook()) return; } catch { return; }
    const currentTabId = typeof tabId === "function" ? tabId() : tabId;
    // Keep native Decky reachable: only hide it while Playhub's own tab is in this array.
    if (currentTabId === undefined || !tabs.some(item => String(item.key) === String(currentTabId))) return;
    const index = tabs.findIndex(item => String(item.key) === "999");
    if (index < 0) return;
    projections.add({ array: new (globalThis as any).WeakRef(tabs), index, tab: tabs[index] });
    tabs.splice(index, 1);
  };
  const capability: Capability = {
    protocol: 1,
    setNativeHidden(hidden: boolean) {
      const next = hidden === true;
      if (next === suppressNative) return;
      suppressNative = next;
      if (!next) restore(undefined, true);
      try { refresh(); } catch { /* A missing owner is reconciled on the next render. */ }
    },
    isAvailable: () => !stopped && ownsHook() && hook.tabs.some(tab => tab.id === 999 && tab.content != null),
    acquire(options) {
      if (current || !capability.isAvailable() || !options.element.isConnected) return null;
      const entry = hook.tabs.find(tab => tab.id === 999);
      if (!entry) return null;
      const root = entry.content;
      const synthetic: Tab[] = [];
      try {
        const probe = Object.create(hook);
        Object.defineProperty(probe, "tabs", { value: [{ ...entry, content: options.createContent(root) }] });
        if (nativeRender.call(probe, synthetic, false) != null || synthetic.length !== 1
          || String(synthetic[0].key) !== "999" || !synthetic[0].panel) return null;
      } catch { return null; }
      const tab = synthetic[0];
      let alive = true;
      let active = false;
      let ready = false;
      let pendingProjection = false;
      let hideNative = options.hideNative !== false;
      const nativeFocus = createNativeDeckyFocusSuppression();
      let visibleArray: { deref(): Tab[] | undefined } | undefined;
      let deadline = Date.now() + 3000;
      const setVisible = (next: boolean) => {
        tab.initialVisibility = next;
        tab.qAMVisibilitySetter?.(next);
        options.onVisibility?.(next);
      };
      const valid = () => {
        try { return alive && !stopped && ownsHook() && Date.now() <= deadline
          && hook.tabs.find(item => item.id === 999)?.content === root
          && options.element.isConnected && options.isHealthy(); } catch { return false; }
      };
      const release = (reason = "host_unmounted") => {
        if (!alive) return;
        restore(undefined, true);
        nativeFocus.restore();
        alive = false;
        clearInterval(timer);
        if (current === owner) current = undefined;
        try { setVisible(false); } catch { /* Restoration must not depend on UI callbacks. */ }
        try { options.onRelease(reason); } catch { /* Original tab is already restored. */ }
      };
      const update = () => {
        if (!valid()) { release("host_invalid"); return false; }
        try { refresh(); return alive; } catch { release("render_error"); return false; }
      };
      // Returns false when this lease did not settle the native tab for this array,
      // so the standing suppression can still honour the user preference.
      const owner = { release, valid, project(tabs: Tab[], qamVisible: boolean): boolean {
        if (!valid()) { release("host_invalid"); return false; }
        const panel = options.element.closest('[id^="quickaccess_content_"]');
        const document = options.element.ownerDocument;
        const currentTabId = typeof tabId === "function" ? tabId() : tabId;
        const mountedHere = currentTabId !== undefined && panel?.id === `quickaccess_content_${currentTabId}` && !!document.getElementById(`quickaccess_tab_${currentTabId}`);
        if (!mountedHere) { pendingProjection = true; nativeFocus.restore(); setVisible(false); return false; }
        if (qamVisible) visibleArray = new (globalThis as any).WeakRef(tabs);
        if (!visibleArray || visibleArray.deref() === tabs) setVisible(qamVisible && active);
        pendingProjection = false;
        if (!hideNative) return true;
        if (!ready) return false;
        if (document.getElementById("quickaccess_tab_999")?.getAttribute("aria-selected") === "true"
          || !tabs.some(item => String(item.key) === String(currentTabId))) {
          nativeFocus.restore();
          pendingProjection = true; return true;
        }
        const index = tabs.findIndex(item => String(item.key) === "999");
        if (index < 0 || Object.isFrozen(tabs) || Object.isSealed(tabs)) return true;
        nativeFocus.hide(document);
        projections.add({ array: new (globalThis as any).WeakRef(tabs), index, tab: tabs[index] });
        tabs.splice(index, 1);
        return true;
      } };
      const timer = setInterval(() => { if (!valid()) release("host_invalid"); }, 500);
      current = owner;
      return { panel: tab.panel,
        renew() {
          if (!valid()) { release("host_invalid"); return false; }
          deadline = Date.now() + 3000;
          return pendingProjection && ready ? update() : true;
        },
        setActive(next) { active = next; if (!next) setVisible(false); return update(); },
        setReady() { ready = true; return update(); },
        setNativeHidden(hidden) {
          hideNative = hidden === true;
          if (!hideNative) { restore(undefined, true); nativeFocus.restore(); }
          return update();
        },
        release,
      };
    },
  };
  return { capability, beforeRender: restore,
    afterRender(tabs: Tab[], visible: boolean) {
      if ((projectionRegistry?.owners.get(tabs) ?? 0) > projectionGeneration) return;
      projectionRegistry?.owners.set(tabs, projectionGeneration);
      if (!(current?.project(tabs, visible) ?? false)) suppressTabs(tabs);
      publishTabProjection(tabs);
    },
    stop() { stopped = true; current?.release("owner_changed"); restore(undefined, true); },
  };
}

/** Older Shortcuts owns tab order but has no Decky-host capability. */
export function createLegacyShortcutsDeckyHost(hook: Hook, bridge: any) {
  const descriptor = Object.getOwnPropertyDescriptor(hook, "render");
  const state = (hook as any)[Symbol.for("panel-de-control.qam-render-adapter")];
  const layout = state?.[Symbol.for("shortcuts.qam-tab-layout-adapter")];
  const stateDescriptor = state && Object.getOwnPropertyDescriptor(state, "wrapper");
  const layoutDescriptor = layout && Object.getOwnPropertyDescriptor(layout, "wrapper");
  const nativeRender = Object.getPrototypeOf(hook)?.render;
  if (bridge?.protocol !== 1 || typeof bridge.register !== "function" || bridge.deckyHost
      || !descriptor || typeof descriptor.value !== "function" || !descriptor.configurable
      || typeof nativeRender !== "function" || state?.protocol !== 2 || state.hook !== hook || state.failure
      || state.wrapper !== descriptor.value || layout?.state !== state || layout.hook !== hook
      || layout.wrapper !== descriptor.value || typeof layout.previous !== "function"
      || !stateDescriptor?.configurable || !stateDescriptor.writable
      || !layoutDescriptor?.configurable || !layoutDescriptor.writable) return null;
  const previous = descriptor.value as Hook["render"];
  const WeakReference = (globalThis as any).WeakRef;
  if (typeof WeakReference !== "function") return null;
  const arrays = new Set<{ deref(): Tab[] | undefined }>();
  const visibility = new WeakMap<Tab[], boolean>();
  let stopped = false;
  const ownsHook = () => !stopped && hook.render === wrapper && state.wrapper === wrapper
    && layout.wrapper === wrapper && state[Symbol.for("shortcuts.qam-tab-layout-adapter")] === layout;
  const currentTabId = () => hook.tabs.find(tab => tab.__shortcutsPlugin === "Playhub")?.id as number | undefined;
  const wrapper: Hook["render"] = function(this: Hook, tabs, visible) {
    if (this !== hook || !Array.isArray(tabs) || Object.isFrozen(tabs)) return previous.call(this, tabs, visible);
    // Same rule as the standalone hook: our own fault detaches instead of escaping into
    // Steam's render stack, where it would crash the overlay rather than one tab.
    try { adapter.beforeRender(tabs); }
    catch { try { stop(); } catch { /* Already detached. */ } return previous.call(this, tabs, visible); }
    let result: unknown;
    try { result = previous.call(this, tabs, visible); }
    catch (error) { try { stop(); } catch { /* Already detached. */ } throw error; }
    try {
      if (!visibility.has(tabs)) arrays.add(new WeakReference(tabs));
      visibility.set(tabs, visible);
      adapter.afterRender(tabs, visible);
    } catch { try { stop(); } catch { /* Already detached. */ } }
    return result;
  };
  const refresh = () => {
    for (const reference of state.observedArrays ?? []) {
      const tabs = reference.deref();
      if (tabs && !visibility.has(tabs)) {
        arrays.add(new WeakReference(tabs));
        visibility.set(tabs, state.arrayStates?.get(tabs)?.visible === true);
      }
    }
    for (const reference of arrays) {
      const tabs = reference.deref();
      if (tabs) wrapper.call(hook, tabs, visibility.get(tabs) ?? false);
      else arrays.delete(reference);
    }
  };
  const adapter = createStandaloneDeckyHost(hook, nativeRender, currentTabId,
    () => ownsHook() && !state.failure && currentTabId() !== undefined && bridge.getVisible("Playhub"), refresh);
  const stop = () => {
      if (stopped) return;
      stopped = true;
      adapter.stop();
      if (hook.render === wrapper) Object.defineProperty(hook, "render", descriptor);
      if (layout.wrapper === wrapper) Object.defineProperty(layout, "wrapper", layoutDescriptor);
      if (Object.getOwnPropertyDescriptor(state, "wrapper")?.get === getWrapper) {
        Object.defineProperty(state, "wrapper", { ...stateDescriptor, value: ownerWrapper === wrapper ? previous : ownerWrapper });
      }
      arrays.clear();
  };
  let ownerWrapper = wrapper;
  const getWrapper = () => ownerWrapper;
  try {
    // Shortcuts detaches its layout by assigning state.wrapper before rendering again.
    // Restore the native projection synchronously, before its registry validation.
    Object.defineProperty(state, "wrapper", { configurable: true, enumerable: stateDescriptor.enumerable,
      get: getWrapper, set(next) {
        if (next !== wrapper) {
          stop();
          Object.defineProperty(state, "wrapper", { ...stateDescriptor, value: next });
        } else ownerWrapper = next;
      },
    });
    Object.defineProperty(layout, "wrapper", { ...layoutDescriptor, value: wrapper });
    Object.defineProperty(hook, "render", { ...descriptor, value: wrapper });
  } catch { stop(); return null; }
  return { capability: adapter.capability, ownsHook, stop,
  };
}

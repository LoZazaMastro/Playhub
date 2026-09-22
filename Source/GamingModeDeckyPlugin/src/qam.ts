import type { ReactNode } from "react";
import { createQamLayoutRuntime } from "./qamLayoutRuntime";
import { createQamTabVisibility } from "./qamTabVisibility";
import { createStandaloneDeckyHost, STANDALONE_HOST } from "./deckyHostStandalone";

const BRIDGE = Symbol.for("shortcuts.qam-bridge.v1");
const INSTANCE = Symbol.for("playhub.qam.v1");
const EVENT = "shortcuts:qam-bridge-changed";
const STORAGE = "playhub.qam.visible.v1";
const TAB_ID = 0x50484B;
type Tab = { key?: number | string; id?: number | string; decky?: boolean; [key: string]: any };
type Hook = { tabs: Tab[]; render: (tabs: Tab[], visible: boolean) => unknown };
export interface PlayhubQamOptions { content: ReactNode; icon: ReactNode; title?: ReactNode }
interface Bridge {
  protocol: number;
  register(entry: PlayhubQamOptions & { name: string; defaultVisible: boolean }): Promise<() => void>;
  getVisible(name: string): boolean;
  setVisible(name: string, visible: boolean): Promise<void>;
  subscribe(listener: () => void): () => void;
}
export interface PlayhubQamState { visible: boolean; available: boolean; pending: boolean }
const listeners = new Set<() => void>();
let state: PlayhubQamState = { visible: true, available: false, pending: false };
let changeVisibility: ((visible: boolean) => Promise<void>) | undefined;
export const getPlayhubQamState = () => state;
export function subscribePlayhubQam(listener: () => void) {
  listeners.add(listener);
  return () => { listeners.delete(listener); };
}
function publish(next: PlayhubQamState) {
  if (JSON.stringify(next) === JSON.stringify(state)) return;
  state = next;
  listeners.forEach((listener) => listener());
}
export async function setPlayhubQamVisible(visible: boolean) {
  await changeVisibility?.(visible);
}
function readVisible(): boolean {
  try { return localStorage.getItem(STORAGE) !== "false"; } catch { return true; }
}
function cacheVisible(visible: boolean) {
  localStorage.setItem(STORAGE, String(visible));
}

// Own only the Playhub entry. Decky's renderer still creates its panel shape.
// Never alter Steam bundles, the registry, or unrelated rendered tab objects.
export function installStandaloneQam(hook: Hook, options: PlayhubQamOptions, isVisible: () => boolean) {
  if (!Array.isArray(hook?.tabs) || typeof hook.render !== "function"
      || !hook.tabs.some((tab) => tab.id === 999)
      || hook.tabs.some((tab) => String(tab.id) === String(TAB_ID))) return null;
  const descriptor = Object.getOwnPropertyDescriptor(hook, "render");
  // Unknown wrappers must be left alone, including older Shortcuts versions.
  if (descriptor) return null;
  const previous = hook.render;
  const probe = Object.create(hook);
  Object.defineProperty(probe, "tabs", { value: [{ id: TAB_ID, ...options, title: options.title ?? "Playhub" }] });
  const sample: Tab[] = [];
  try {
    const result = previous.call(probe, sample, false);
    if (result != null || sample.length !== 1 || String(sample[0].key) !== String(TAB_ID) || sample[0].decky !== true) return null;
  } catch { return null; }
  const WeakReference = (globalThis as any).WeakRef;
  if (typeof WeakReference !== "function") return null;
  const arrays = new Set<{ deref(): Tab[] | undefined }>();
  const arrayStates = new WeakMap<Tab[], { visible: boolean; tab?: Tab }>();
  const own = new WeakSet<object>();
  const clean = (tabs: Tab[]) => {
    for (let i = tabs.length - 1; i >= 0; i--) if (own.has(tabs[i])) tabs.splice(i, 1);
  };
  const wrapper = function(this: Hook, tabs: Tab[], visible: boolean) {
    if (this !== hook || !Array.isArray(tabs) || Object.isFrozen(tabs)) return previous.call(this, tabs, visible);
    // Steam renders the QAM with no error boundary above this hook: a throw that escapes
    // here takes down the overlay and Steam itself. Our own faults therefore uninstall
    // the wrapper and fall back to Decky's render; a fault raised by Decky's own render
    // still propagates, because it would have with or without us.
    const detach = () => {
      try { deckyHost.stop(); layout.stop(); visibility.stop(); } catch { /* Already torn down. */ }
      try { clean(tabs); } catch { /* Leave the array as Steam built it. */ }
      try { if (hook.render === wrapper) delete (hook as Partial<Hook>).render; } catch { /* Frozen hook. */ }
    };
    try { deckyHost.beforeRender(tabs); layout.beforeRender(tabs); clean(tabs); }
    catch { detach(); return previous.call(this, tabs, visible); }
    let result: unknown;
    // Decky's own render failing is not our fault, but our host must not keep a lease
    // on a hook that no longer works: tear down, then let the error propagate as before.
    try { result = previous.call(this, tabs, visible); }
    catch (error) { detach(); throw error; }
    try {
      let record = arrayStates.get(tabs);
      if (!record) { record = { visible }; arrayStates.set(tabs, record); arrays.add(new WeakReference(tabs)); }
      record.visible = visible;
      if (isVisible() && !tabs.some((tab) => String(tab.key) === String(TAB_ID))) {
        const created: Tab[] = record.tab ? [record.tab] : [];
        previous.call(probe, created, visible);
        if (created.length === 1) { record.tab = created[0]; own.add(created[0]); tabs.unshift(created[0]); }
      }
      layout.afterRender(tabs, visible);
      deckyHost.afterRender(tabs, visible);
      visibility.update(layout.getManagedVisibility());
    } catch { detach(); }
    return result;
  };
  try { Object.defineProperty(hook, "render", { configurable: true, writable: true, value: wrapper }); }
  catch { return null; }
  const refresh = () => {
      for (const reference of arrays) {
        const tabs = reference.deref();
        if (tabs) wrapper.call(hook, tabs, arrayStates.get(tabs)?.visible ?? false);
        else arrays.delete(reference);
      }
  };
  const deckyHost = createStandaloneDeckyHost(hook, previous, TAB_ID,
    () => hook.render === wrapper && isVisible(), refresh);
  const layout = createQamLayoutRuntime(hook, previous, TAB_ID, refresh);
  const visibility = createQamTabVisibility();
  (window as any)[STANDALONE_HOST] = deckyHost.capability;
  return {
    refresh, refreshInventory: layout.refreshInventory,
    stop() {
      deckyHost.stop();
      layout.stop();
      visibility.stop();
      if ((window as any)[STANDALONE_HOST] === deckyHost.capability) delete (window as any)[STANDALONE_HOST];
      for (const reference of arrays) { const tabs = reference.deref(); if (tabs) clean(tabs); }
      arrays.clear();
      if (hook.render === wrapper) delete (hook as Partial<Hook>).render;
    }
  };
}

export function initPlayhubQam(options: PlayhubQamOptions): () => void {
  const host = window as any;
  host[INSTANCE]?.();
  let visible = readVisible();
  let stopped = false;
  let generation = 0;
  let bridge: Bridge | undefined;
  let unregister: (() => void) | undefined;
  let unsubscribe: (() => void) | undefined;
  let standalone: ReturnType<typeof installStandaloneQam> = null;
  let boundHook: Hook | undefined;
  const sync = () => {
    if (!bridge) return;
    visible = bridge.getVisible("Playhub");
    try { cacheVisible(visible); } catch { /* Shortcuts remains authoritative. */ }
    publish({ visible, available: true, pending: false });
  };
  const reconcile = () => {
    if (stopped) return;
    const inventory = host.DeckyPluginLoader?.deckyState?.publicState?.();
    const isShortcuts = (plugin: any) => String(typeof plugin === "string" ? plugin : plugin?.name).toLowerCase() === "shortcuts";
    const shortcutsDisabled = (inventory?.disabledPlugins ?? []).some(isShortcuts);
    // Installed entries survive disabling. Explicit disabled state also invalidates a stale bridge.
    const shortcutsActive = !shortcutsDisabled && [...(inventory?.plugins ?? []), ...(inventory?.installedPlugins ?? [])].some(isShortcuts);
    const candidate: Bridge | undefined = !shortcutsDisabled && host[BRIDGE]?.protocol === 1 ? host[BRIDGE] : undefined;
    if (candidate !== bridge) {
      generation++;
      unregister?.(); unsubscribe?.(); unregister = unsubscribe = undefined;
      standalone?.stop(); standalone = null; boundHook = undefined;
      bridge = candidate;
      if (bridge) {
        const current = generation;
        const activeBridge = bridge;
        publish({ visible, available: false, pending: true });
        void bridge.register({ ...options, name: "Playhub", defaultVisible: visible }).then((release) => {
          if (stopped || generation !== current) { release(); return; }
          unregister = release;
          unsubscribe = activeBridge.subscribe(sync);
          sync();
        }).catch(() => { if (!stopped && generation === current) publish({ visible, available: false, pending: false }); });
      }
    }
    if (bridge) return;
    const hook: Hook | undefined = host.__TABS_HOOK_INSTANCE;
    if (boundHook !== hook || shortcutsActive) { standalone?.stop(); standalone = null; boundHook = hook; }
    if (!shortcutsActive && hook && !standalone) standalone = installStandaloneQam(hook, options, () => visible);
    standalone?.refreshInventory();
    publish({ visible, available: !!standalone, pending: false });
  };
  changeVisibility = async (next) => {
    if (stopped || !state.available || state.pending) return;
    publish({ ...state, pending: true });
    try {
      if (bridge) { await bridge.setVisible("Playhub", next); sync(); }
      else { cacheVisible(next); visible = next; standalone?.refresh(); }
    } finally { if (!stopped) publish({ ...state, visible, pending: false }); }
  };
  const onStorage = (event: StorageEvent) => {
    if (!bridge && (event.key === STORAGE || event.key === null)) { visible = readVisible(); standalone?.refresh(); reconcile(); }
  };
  const stop = () => {
    if (stopped) return;
    stopped = true; generation++;
    clearInterval(timer);
    window.removeEventListener(EVENT, reconcile);
    window.removeEventListener("storage", onStorage);
    unsubscribe?.(); unregister?.(); standalone?.stop();
    if (host[INSTANCE] === stop) { delete host[INSTANCE]; changeVisibility = undefined; }
  };
  host[INSTANCE] = stop;
  window.addEventListener(EVENT, reconcile);
  window.addEventListener("storage", onStorage);
  const timer = setInterval(reconcile, 1000);
  reconcile();
  return stop;
}




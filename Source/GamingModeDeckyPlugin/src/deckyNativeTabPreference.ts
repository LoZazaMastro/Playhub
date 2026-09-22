/**
 * Playhub-owned suppression of Steam's native Decky tab.
 *
 * Applied at the QAM tab-projection boundary, so it never depends on Playhub's own
 * tab being mounted and it works with every QAM owner: Playhub standalone, the legacy
 * Shortcuts adapter and the Shortcuts qam-bridge host.
 */
export type ProjectedTab = { id?: number | string; key?: number | string; [key: string]: any };
const PLAYHUB_TAB_ID = 0x50484B;
const NATIVE_DECKY_KEY = "999";
const host = (typeof window === "undefined" ? globalThis : window) as any;
const listeners = new Set<() => void>();
let hidden = false;

export const isNativeDeckyHidden = () => hidden;
export function subscribeNativeDeckyHidden(listener: () => void) {
  listeners.add(listener);
  return () => { listeners.delete(listener); };
}
export function setNativeDeckyHidden(next: boolean) {
  const value = next === true;
  if (value === hidden) return false;
  hidden = value;
  listeners.forEach(listener => { try { listener(); } catch { /* One consumer cannot block the rest. */ } });
  return true;
}

/** The owner may renumber Playhub's tab; never hide Decky without a way back to it. */
export function currentPlayhubTabId(): number | undefined {
  try {
    const hook = host.DeckyPluginLoader?.tabsHook ?? host.__TABS_HOOK_INSTANCE;
    const registered = hook?.tabs?.find((tab: any) => tab?.__shortcutsPlugin === "Playhub");
    if (registered && Number.isSafeInteger(Number(registered.id))) return Number(registered.id);
  } catch { /* Unknown owner falls back to Playhub's own identifier. */ }
  return PLAYHUB_TAB_ID;
}
const tabKey = (tab: ProjectedTab) => String(tab?.key ?? tab?.id);

export function filterNativeDeckyTabs<T extends ProjectedTab>(tabs: T[]): T[] {
  if (!hidden || !Array.isArray(tabs) || !tabs.some(tab => tabKey(tab) === NATIVE_DECKY_KEY)) return tabs;
  const playhub = currentPlayhubTabId();
  // Fail open: without Playhub's tab in this projection, Decky must stay reachable.
  if (playhub === undefined || !tabs.some(tab => tabKey(tab) === String(playhub))) return tabs;
  return tabs.filter(tab => tabKey(tab) !== NATIVE_DECKY_KEY);
}

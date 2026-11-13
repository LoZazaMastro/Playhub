import { DFL } from "../decky";
import { readOwnedGameApp, resolveOwnedGameApp, readOwnedGameRoute, readOwnedNativeOverlay } from "../steamOwnedLaunchDispatcher";
import { call } from "../controlBackend";
import { steamAppId } from "../controlledQamFocus";
import { performanceOverlayMatches, performanceActivationMatches, performanceNativeVisibilityMatches } from "./steamPerformanceOverlayIdentity";
import { createLivePerformanceController, type LivePerformanceRequest, type LivePerformanceResponse } from "./livePerformanceController";
import { steamPerformanceMonitor, type SteamMonitorWindow } from "./steamPerformanceMonitor";

export const PERFORMANCE_SOURCE = "playhub.performance-monitor.source";
export function performanceMonitorHost(): SteamMonitorWindow & Record<string, any> {
  const current = window as any;
  if (current.settingsStore) return current;
  try {
    const main = ((DFL as any).Router?.WindowStore)?.GamepadUIMainWindowInstance;
    return [main?.BrowserWindow, main?.m_BrowserWindow, main?.window, main?.m_Window, main]
      .find(value => value?.settingsStore) ?? current;
  } catch { return current; }
}
const activatedOverlays = new Map<number, { app: number; pid: number; nativeApp: number; active: boolean; entry: any; browserId: number }>();
let observedRunningApp = 0;
function runningApp() {
  const app = readOwnedGameApp(steamAppId(performanceMonitorHost().SteamUIStore?.MainRunningAppID ?? (DFL as any).Router?.MainRunningAppID));
  if (app !== observedRunningApp) { activatedOverlays.clear(); observedRunningApp = app; }
  return app;
}
export function menuOpen() {
  try {
    const app = runningApp();
    if (!app) return true;
    const store = performanceMonitorHost().SteamUIStore?.WindowStore ?? (DFL as any).Router?.WindowStore;
    const native=readOwnedNativeOverlay(app);
    return (store?.OverlayWindows ?? []).some((entry: any) => {
      if (!performanceOverlayMatches(entry, store, app, readOwnedGameRoute(app)?.observedSteamGameId,native)) return false;
      return performanceNativeVisibilityMatches(entry,native,app)&&native?.active===true
        || performanceActivationMatches(entry, activatedOverlays.get(entry?.params?.browserInfo?.m_unPID), app)
        || entry?.DesktopOverlay?.overlay_active === true
        || entry?.IsGamepadUIOverlayWindow?.() === true && (entry.MenuStore?.GetOpenSideMenu?.() ?? 0) > 0;
    }) || (store?.GamepadUIMainWindowInstance?.MenuStore?.GetOpenSideMenu?.() ?? 0) > 0;
  } catch { return true; }
}

const STEAM_HOLD = "__playhubPerformanceSteamHoldV1";
type SteamHold = { version: 1; owners: Set<symbol>; queue: Promise<unknown>; corner: 0 | 1 | 2 | 3 | 4 | 5 | 6 | null };
export async function suspendSteamMonitor(host = performanceMonitorHost()) {
  // Store ownership on the Steam host, not this module. The preceding root may
  // still be disposing while a newly loaded bundle takes over the same counter.
  let shared = host[STEAM_HOLD] as SteamHold | undefined;
  if (!shared) host[STEAM_HOLD] = shared = { version: 1, owners: new Set(), queue: Promise.resolve(), corner: null };
  if (shared.version !== 1 || typeof shared.owners?.add !== "function" || typeof shared.queue?.then !== "function")
    throw new Error("Steam monitor ownership unavailable");
  const hold = shared, owner = Symbol("Playhub performance monitor");
  const serial = <T,>(work: () => Promise<T>) => {
    const result = hold.queue.catch(() => {}).then(work);
    hold.queue = result.catch(() => {});
    return result;
  };
  await serial(async () => {
    const monitor = steamPerformanceMonitor(host), original = monitor?.read();
    if (!monitor || !original) throw new Error("Steam monitor unavailable");
    if (hold.owners.size === 0) {
      // Retain a previous pending restore if Steam rejected its setting write.
      if (hold.corner === null) hold.corner = original.corner;
      if (original.corner !== 0) {
        hold.corner = original.corner;
        await monitor.setEnabled(false);
        if (monitor.read()?.corner !== 0) throw new Error("Steam monitor change not confirmed");
      }
    }
    hold.owners.add(owner);
  });
  let released = false;
  return async () => {
    if (released) return;
    released = true;
    await serial(async () => {
      if (!hold.owners.delete(owner) || hold.owners.size !== 0) return;
      const monitor = steamPerformanceMonitor(host), current = monitor?.read();
      if (!monitor || !current) throw new Error("Steam monitor unavailable");
      // A different nonzero corner is an explicit Steam/user change.
      if (hold.corner && current.corner === 0) {
        await monitor.setCorner(hold.corner);
        if (monitor.read()?.corner !== hold.corner) throw new Error("Steam monitor restore not confirmed");
      }
      hold.corner = null;
    });
  };
}

let controller: ReturnType<typeof createLivePerformanceController> | undefined;
export function livePerformanceRuntime() {
  if (!controller) controller = createLivePerformanceController({
    get: () => call("get_live_performance"),
    send: request => call<[LivePerformanceRequest], LivePerformanceResponse>("set_live_performance", request),
    app: runningApp, resolveApp: async () => resolveOwnedGameApp(steamAppId(performanceMonitorHost().SteamUIStore?.MainRunningAppID ?? (DFL as any).Router?.MainRunningAppID)), suspended: menuOpen,
    schedule: (callback, delay) => window.setTimeout(callback, delay), cancel: timer => window.clearTimeout(timer as number),
    suspendSteam: () => suspendSteamMonitor(),
  });
  return controller;
}
export function refreshLivePerformance() {
  if (controller?.read().state?.enabled) void controller.refresh();
}
function watchSteamMenus(refresh: () => void) {
  const ui = DFL as any;
  const patches = new Map<any, Array<{ unpatch(): void }>>();
  const registrations: Array<{ unregister?(): void }> = [];
  let timer: number | undefined, disposed = false;
  let observedStore: any;
  let storePatch: { unpatch(): void } | undefined;
  const request = () => {
    if (disposed || !controller?.read().state?.enabled) return;
    void controller.menuChanged();
    window.clearTimeout(timer);
    timer = window.setTimeout(() => { timer = undefined; if (!disposed) refresh(); }, 0);
  };
  const attach = () => {
    if (disposed) return;
    const store = performanceMonitorHost().SteamUIStore?.WindowStore ?? ui.Router?.WindowStore;
    if (store !== observedStore) {
      try { storePatch?.unpatch(); } catch { /* Steam replaced the previous store. */ }
      storePatch = undefined;
      observedStore = store;
      if (typeof ui.afterPatch === "function" && typeof store?.UpdateDesiredWindows === "function") {
        try {
          // The native callback belongs to Steam. Observe its completed update
          // without replacing the callback that creates Desktop/Big Picture.
          storePatch = ui.afterPatch(store, "UpdateDesiredWindows", (_args: unknown[], result: unknown) => {
            try { attach(); request(); } catch { /* An observer cannot interrupt Steam's window update. */ }
            return result;
          });
        } catch { /* Overlay activation still refreshes the available menus. */ }
      }
    }
    const menus = new Set([store?.GamepadUIMainWindowInstance, ...(store?.OverlayWindows ?? [])]
      .map((entry: any) => entry?.MenuStore).filter(Boolean));
    for (const [menu, owned] of patches) if (!menus.has(menu)) {
      for (const patch of owned) try { patch.unpatch(); } catch { /* Steam disposed the old menu. */ }
      patches.delete(menu);
    }
    if (typeof ui.afterPatch !== "function") return;
    for (const menu of menus) if (!patches.has(menu)) {
      const owned: Array<{ unpatch(): void }> = [];
      for (const name of ["OpenSideMenu", "CloseSideMenus"]) if (typeof menu[name] === "function") {
        try { owned.push(ui.afterPatch(menu, name, (_args: unknown[], result: unknown) => { request(); return result; })); }
        catch { /* A replaced Steam menu is handled by the next window event. */ }
      }
      patches.set(menu, owned);
    }
  };
  try {
    const steam = performanceMonitorHost().SteamClient as any;
    const overlay = steam?.Overlay?.RegisterForOverlayActivated?.((pid: number, appId: number, active: boolean) => {
      const nativeApp = steamAppId(appId);
      // Track the exact current overlay host. A dead host's last active=true
      // must not suspend a later launch of the same game indefinitely.
      const app = runningApp();
      if (app > 0 && nativeApp > 0 && Number.isInteger(pid) && pid > 0 && typeof active === "boolean") {
        const store = performanceMonitorHost().SteamUIStore?.WindowStore ?? ui.Router?.WindowStore;
        const matches = (store?.OverlayWindows ?? []).filter((entry: any) => performanceOverlayMatches(entry, store, app, readOwnedGameRoute(app)?.observedSteamGameId,readOwnedNativeOverlay(app))
          && entry.params?.browserInfo?.m_unPID === pid && entry.params.browserInfo.m_unAppID === nativeApp
          && Number.isInteger(entry.params.browserInfo.m_nBrowserID) && entry.params.browserInfo.m_nBrowserID > 0);
        if (matches.length === 1) {
          const entry = matches[0];
          activatedOverlays.set(pid, { app, pid, nativeApp, active, entry, browserId: entry.params.browserInfo.m_nBrowserID });
        }
      }
      // Preserve the actual activation signal: a normal overlay can be open
      // with GetOpenSideMenu() === 0 and the game still in the foreground.
      attach(); request();
    });
    if (overlay) registrations.push(overlay);
    attach();
  } catch { /* Foreground checks and the bounded subscription heartbeat remain active. */ }
  return () => {
    disposed = true; window.clearTimeout(timer);
    activatedOverlays.clear();
    try { storePatch?.unpatch(); } catch { /* Steam may have replaced its store. */ }
    storePatch = undefined;
    for (const registration of registrations) try { registration.unregister?.(); } catch { /* Steam may have closed. */ }
    for (const owned of patches.values()) for (const patch of owned) try { patch.unpatch(); } catch { /* Stale Steam menu. */ }
    patches.clear();
  };
}
export function installLivePerformance() {
  const instance = livePerformanceRuntime();
  instance.start();
  try {
    if (performanceMonitorHost().localStorage?.getItem(PERFORMANCE_SOURCE) !== "steam")
      void instance.holdSteamMonitor().catch(() => {});
  } catch { /* A blocked browser store does not change the user's Steam counter. */ }
  const stopMenus = watchSteamMenus(() => { void instance.refresh(); });
  return () => { stopMenus(); instance.dispose(); if (controller === instance) controller = undefined; };
}

import { DFL } from "./decky";
import { API_BASE, logToAgent } from "./api";
import { matchesOverlayApp, selectOverlay } from "./dashboardOverlay";
import { createControlledQamFocus, steamAppId } from "./controlledQamFocus";
import { call } from "./controlBackend";
import { createXboxCompanionOverlay, type VerifiedOverlay } from "./xboxCompanionOverlay";
import { createSteamOverlayFallback } from "./steamOverlayFallback";
import { createSteamMirrorGuideAdmission } from "./steamMirrorGuideAdmission";
import { createOwnedLaunchDispatcher, readSteamGuideReceiver, registerOwnedGameConsumer, type OwnedLaunchRoute } from "./steamOwnedLaunchDispatcher";
import { createSteamMirrorOverlay, type MirrorReply, type MirrorPresentation } from "./steamMirrorOverlay";
import { createSteamNativeGameOverlay, coordinateNativeOriginalMenus, coordinateNativeOriginalClose, type NativeGameReply, type NativeGameProof, type NativeBrowser, type NativeInputReply } from "./steamNativeGameOverlay";

const companionScopeMs = 300;
const mainWindowRefreshDelays = [50, 200, 600, 1500];

type Patch = { unpatch(): void };

function shortcutAppId(gameId: unknown): number {
  if (typeof gameId !== "string" || !/^\d{11,20}$/.test(gameId)) return 0;
  const id = BigInt(gameId);
  if ((id & 0xffffffffn) !== 0x02000000n) return 0;
  const appId = id >> 32n;
  return appId >= 0x80000000n && appId <= 0xffffffffn ? Number(appId) : 0;
}

export function installControlledQamFocus(onMirrorAdmitted?: (app: number, mirror: MirrorPresentation) => void,
  onNativeAdmitted?: (app: number, proof: NativeGameProof, browser: NativeBrowser) => void): () => void {
  const ui = DFL as any;
  const steam = (window as any).SteamClient;
  const windowStore = () => (window as any).SteamUIStore?.WindowStore ?? ui.Router?.WindowStore;
  const observedUiApp = () => {
    const store = (window as any).SteamUIStore;
    const raw = store && "MainRunningAppID" in store ? store.MainRunningAppID : ui.Router?.MainRunningAppID;
    return raw == null ? null : steamAppId(raw);
  };
  const observedRunningApp = () => observedUiApp() ?? 0;
  let ownedDispatcher: ReturnType<typeof createOwnedLaunchDispatcher> | undefined;
  const runningApp = () => ownedDispatcher?.app() ?? observedRunningApp();
  const mainWindow = () => windowStore()?.GamepadUIMainWindowInstance;
  const main = () => {
    const instance = mainWindow();
    return instance?.IsGamepadUIOverlayWindow?.() === true ? undefined : instance?.MenuStore;
  };
  let canUseCompanion = typeof steam?.Overlay?.SetOverlayState === "function"
    && typeof steam.Overlay.RegisterForOverlayActivated === "function"
    && typeof steam.Overlay.GetOverlayBrowserInfo === "function";
  let sessionRevision = 0;
  let homeRevision = 0;
  let disposed = false;
  let systemGuideObserverCurrent = false;
  let coldProbe: { app: number; expires: number; available: Promise<boolean> } | undefined;
  const verify = (app: number, pid: number, keyboard: boolean, menu: boolean) =>
    call<[number, number, boolean, boolean], VerifiedOverlay>("verify_xbox_steam_overlay", app, pid, keyboard, menu);
  const matchingBrowser = (infos: any[], app: number) =>
    infos.some(info => shortcutAppId(info?.gameID) === app || matchesOverlayApp(info, app));
  const bounded = (check: Promise<boolean>) => new Promise<boolean>((resolve, reject) => {
    const timer = window.setTimeout(() => reject(new Error("Companion ownership is still unknown")), companionScopeMs);
    const done = (result: boolean) => { window.clearTimeout(timer); resolve(result); };
    void check.then(done, error => { window.clearTimeout(timer); reject(error); });
  });
  const coldCompanion = (app: number): Promise<boolean> => {
    if (disposed || !canUseCompanion || app < 0x80000000 || runningApp() !== app) return Promise.resolve(false);
    const now = Date.now();
    if (coldProbe?.app === app && now < coldProbe.expires) return coldProbe.available;
    const available = bounded(Promise.resolve().then(() => verify(app, 0, false, false)).then(result => {
      if (result?.ok === false) return false;
      if (result?.ok === true && shortcutAppId(result.gameId) === app
          && Number.isSafeInteger(result.companionPid) && Number(result.companionPid) > 0
          && Number(result.companionPid) <= 0xffffffff) return true;
      throw new Error("Companion ownership was not verified");
    }));
    coldProbe = { app, expires: now + companionScopeMs, available };
    return available;
  };
  const patches: Patch[] = [];
  const storePatches: Patch[] = [];
  const mainPatches: Patch[] = [];
  let observedStore: any;
  let attachedMain: any;
  let attachedMenu: any;
  let refreshTimers: number[] = [];
  let serial = 0;
  const prefix = `qam-${Date.now()}-${Math.random().toString(36).slice(2)}`;
  const send = async (action: string, requestId: string, appId?: number) => {
    const response = await fetch(`${API_BASE}/qam/focus/${action}`, {
      method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ requestId, appId }), signal: AbortSignal.timeout(2000),
    });
    if (!response.ok) throw new Error(`QAM focus ${action}: ${response.status}`);
    return response.json();
  };
  const control = createControlledQamFocus({
    runningApp,
    nativeOverlayAvailable: async app => {
      if (typeof steam?.Overlay?.GetOverlayBrowserInfo !== "function") throw new Error("Native overlay identity unavailable");
      const infos = await steam.Overlay.GetOverlayBrowserInfo();
      if (!Array.isArray(infos)) throw new Error("Native overlay identity unavailable");
      // Browser information itself is evidence of a native overlay; it need
      // not already have a visible gamepad browser window to remain untouched.
      const identity = selectOverlay(infos, app);
      if (identity) return true;
      if (matchingBrowser(infos, app)) throw new Error("Ambiguous native overlay identity");
      return coldCompanion(app);
    },
    mainMenu: main,
    acquire: (id, appId) => send("acquire", id, appId), close: id => send("close", id), release: id => send("release", id),
    requestId: () => `${prefix}-${++serial}`,
    warn: error => console.warn("[Playhub QAM] Controlled focus unavailable", error),
  });
  let mirrorApp = 0;
  const mirror = createSteamMirrorOverlay({
    runningApp,
    prepare: async app => {
      const revision = sessionRevision, started = performance.now();
      logToAgent(`Steam mirror Guide: prepare-start app=${app} rev=${revision}`);
      try {
        const expected = ownedDispatcher?.expected(app);
        if (observedRunningApp() !== app && !expected) return { ok: false };
        const reply = expected
          ? await call<[number, OwnedLaunchRoute], MirrorReply>("prepare_owned_mirror_presentation", app, expected)
          : await call<[number], MirrorReply>("prepare_mirror_presentation", app);
        if (expected && reply?.ok !== true && !reply?.mirror) ownedDispatcher?.invalidate();
        logToAgent(`Steam mirror Guide: prepare-result ok=${reply?.ok === true} receipt=${Boolean(reply?.mirror)} state=${reply?.mirror?.state ?? "none"} elapsed=${Math.round(performance.now() - started)} revChanged=${revision !== sessionRevision} disposed=${disposed}`);
        // Return even a canceled receipt: the coordinator must adopt and retire
        // the exact delivered generation before evaluating its own liveness.
        return reply;
      } catch (error) {
        logToAgent(`Steam mirror Guide: prepare-rejected elapsed=${Math.round(performance.now() - started)} revChanged=${revision !== sessionRevision}`);
        throw error;
      }
    },
    admitted: (app, presentation) => {
      if (!disposed && runningApp() === app) { mirrorApp = app; onMirrorAdmitted?.(app, presentation); }
    },
    read: (app, overlayPid) => call<[number, number], MirrorReply>("get_mirror_presentation", app, overlayPid),
    command: (app, overlayPid, action, expected) =>
      call<[number, number, "show" | "close", MirrorPresentation], { ok: boolean }>(
        "set_mirror_presentation", app, overlayPid, action, expected),
    browserInfo: () => steam.Overlay.GetOverlayBrowserInfo(),
    windows: () => windowStore()?.OverlayWindows,
    navigation: () => (window as any).SteamUIStore?.NavigationManager ?? ui.Router?.NavigationManager,
    setOverlayState: (gameId, state) => Promise.resolve(steam.Overlay.SetOverlayState(gameId, state)),
    delay: ms => new Promise(resolve => window.setTimeout(resolve, ms)),
    now: () => Date.now(),
    trace: stage => logToAgent(`Steam mirror: ${stage}`),
  });
  const mirrorContinuationTimer=typeof window.setInterval==="function"?window.setInterval(()=>{if(!disposed&&mirror.active)void mirror.poll();},50):undefined;
  const nativeGame = createSteamNativeGameOverlay({
    scope: () => ({ app: runningApp(), observedApp: observedUiApp(), revision: sessionRevision, disposed }),
    now: () => performance.now(),
    proof: app => call<[number, OwnedLaunchRoute | undefined], NativeGameReply>("get_native_game_overlay", app, ownedDispatcher?.expected(app)),
    input: request => call<unknown[], NativeInputReply>("set_native_game_overlay_input", request.appId, request.action,
      request.expectedNative, request.browser, request.ownerToken, request.generation, request.callback),
    browserInfo: () => steam.Overlay.GetOverlayBrowserInfo(), windowStore,
    schedule: (callback, ms) => window.setTimeout(callback, ms), cancel: timer => window.clearTimeout(timer as number),
    admitted: (app, proof, browser) => { if (!disposed && runningApp() === app) onNativeAdmitted?.(app, proof, browser); },
    coordinateOriginalMenu: (target, current) => coordinateNativeOriginalMenus(target, current,
      () => systemGuideObserverCurrent && !disposed && canUseCompanion, stage => logToAgent(`Steam native game overlay: ${stage}`)),
    coordinateOriginalClose: (fullGameId, rendererToken, current, target, cleanupCurrent) => coordinateNativeOriginalClose(steam.Overlay, fullGameId, rendererToken, current, target, cleanupCurrent),
    setOverlayState: (gameId, state) => Promise.resolve(steam.Overlay.SetOverlayState(gameId, state)),
    trace: stage => logToAgent(`Steam native game overlay: ${stage}`),
  });
  const routeGuide = async (menu: "main" | "quick") => {
    const app = runningApp(), revision = sessionRevision;
    const result = await nativeGame.guide(menu) as { handled?: boolean };
    // Only the server's positive actual-host absence allows the existing mirror path.
    if (result?.handled === false && !disposed && runningApp() === app && sessionRevision === revision) return mirror.guide(menu);
  };
  const guideAdmission = createSteamMirrorGuideAdmission({
    scope: () => ({ app: runningApp(), revision: sessionRevision, disposed, enabled: canUseCompanion }),
    now: () => performance.now(),
    guide: routeGuide, pending: () => nativeGame.pendingApp === runningApp() || mirror.pendingApp === runningApp(),
    active: () => nativeGame.active || mirror.active,
    verifiedAbsent: async app => (await coldCompanion(app)) === false,
    trace: stage => logToAgent(`Steam mirror Guide: ${stage}`),
  });
  ownedDispatcher = createOwnedLaunchDispatcher({
    scope: () => {
      const store = (window as any).SteamUIStore ?? ui.Router;
      const apps = store?.RunningApps;
      const receiver = readSteamGuideReceiver(mainWindow(), store?.ActiveWindowInstance ?? windowStore()?.ActiveWindowInstance,
        (window as any).FocusedAppWindowStore);
      return { receiver, observedApp: observedUiApp(), revision: sessionRevision, context: windowStore(), disposed, enabled: canUseCompanion,
        runningShortcuts: Array.isArray(apps) ? apps.slice(0, 9).map(value => steamAppId(value?.appid)).filter(value => value >= 0x80000000) : [] };
    },
    now: () => performance.now(),
    discover: (observed, candidate) => call<[number | null, number | undefined], { ok: boolean; route?: unknown; kind?: "absent"; receiver?: "steam-ui" | "ordinary-game" }>("get_owned_game_route", observed, candidate),
    signal: (origin, menu, fallback) => guideAdmission.signal(origin, menu, fallback),
    schedule: (callback, ms) => window.setTimeout(callback, ms), cancel: timer => window.clearTimeout(timer as number),
    trace: stage => logToAgent(`Steam owned launch: ${stage}`),
  });
  const releaseGameConsumer = canUseCompanion ? registerOwnedGameConsumer({
    app: () => ownedDispatcher!.app(), expected: app => ownedDispatcher!.expected(app), resolveApp: () => ownedDispatcher!.resolveApp(),
    nativeOverlay: app => nativeGame.visibility(app),
  }) : () => {};
  const companion = canUseCompanion ? createXboxCompanionOverlay({
      runningApp,
      browserInfo: () => steam.Overlay.GetOverlayBrowserInfo(),
      requestColdOpen: async app => {
        const revision = sessionRevision;
        if (disposed || runningApp() !== app) return { ok: false };
        // RequestOpen authenticates the exact live game/companion and native
        // foreground before writing its one-shot pipe. A second 300 ms
        // availability probe here can reject a healthy authenticated request.
        const result = await call<[number], VerifiedOverlay>("request_xbox_steam_overlay", app);
        if (disposed || sessionRevision !== revision || runningApp() !== app) return { ok: false };
        return result;
      },
      verify,
      windows: () => windowStore()?.OverlayWindows,
      setOverlayState: (gameId, state) => Promise.resolve(steam.Overlay.SetOverlayState(gameId, state)),
      observeMenu: (menu: any, changed) => {
        if (typeof ui.afterPatch !== "function") return undefined;
        const owned: Patch[] = [];
        const stop = () => {
          for (const patch of owned.splice(0).reverse()) try { patch.unpatch(); } catch {}
        };
        try {
          for (const method of ["OpenSideMenu", "CloseSideMenus"]) {
            if (typeof menu?.[method] !== "function") { stop(); return undefined; }
            owned.push(ui.afterPatch(menu, method, (_args: any[], result: any) => { changed(); return result; }));
          }
          return stop;
        } catch { stop(); return undefined; }
      },
      delay: ms => new Promise(resolve => window.setTimeout(resolve, ms)),
      now: () => Date.now(),
      onColdRecovery: () => logToAgent("Xbox Steam companion requested the exact Valve overlay state after cold Guide."),
      trace: stage => logToAgent(`Steam overlay recovery: ${stage}`),
    }) : undefined;
  const fallback = createSteamOverlayFallback({
    runningApp,
    visible: async (app, menu) => {
      if (mirrorApp === app) {
        const proof = await call<[number, number], MirrorReply>("get_mirror_presentation", app, 0);
        if (disposed || runningApp() !== app) return false;
        return proof?.ok === true && proof.mirror?.protocol === 1 && proof.mirror.state === "shown"
          && proof.mirror.shownCurrent === true && proof.mirror.leaseCurrent === true
          && (proof.mirror.actualScope === 1 || proof.mirror.actualScope === 3 && proof.mirror.ownedForegroundPhase === 1);
      }
      const infos = await steam.Overlay.GetOverlayBrowserInfo();
      if (!Array.isArray(infos)) throw new Error("Overlay visibility unavailable");
      const windows = windowStore()?.OverlayWindows;
      if (!Array.isArray(windows)) return false;
      for (const candidate of windows) {
        const info = candidate?.params?.browserInfo;
        if (candidate?.IsGamepadUIOverlayWindow?.() !== true
            || !matchesOverlayApp({ appID: app, gameID: String(info?.m_gameID ?? "") }, app)
            || String(info?.m_gameID ?? "") !== ((BigInt(app) << 32n) | 0x02000000n).toString()
            || !infos.some(value => Number(value?.unPID) === Number(info.m_unPID)
              && Number(value?.nBrowserID) === Number(info.m_nBrowserID) && matchesOverlayApp(value, app))) continue;
        const browser = candidate.BrowserWindow ?? candidate.m_BrowserWindow;
        const menuStore = candidate.MenuStore;
        const side = menuStore?.GetOpenSideMenu?.();
        if (!browser?.document?.body || browser.closed || side !== (menu === "quick" ? 2 : 1)) continue;
        // CEF can retain document focus after Back has returned to the game.
        // Only the native exact companion proof can confirm a usable overlay.
        const proof = await verify(app, Number(info.m_unPID), false, true);
        if (!disposed && runningApp() === app && Array.isArray(windowStore()?.OverlayWindows)
            && windowStore().OverlayWindows.includes(candidate)
            && candidate.MenuStore === menuStore && menuStore?.GetOpenSideMenu?.() === (menu === "quick" ? 2 : 1)
            && (candidate.BrowserWindow ?? candidate.m_BrowserWindow) === browser && !browser.closed
            && proof?.ok && shortcutAppId(proof.gameId) === app && proof.overlayPid === Number(info.m_unPID)
            && Number.isSafeInteger(proof.companionPid) && Number(proof.companionPid) > 0
            && Number(proof.companionPid) <= 0xffffffff
            && proof.foregroundScope === 2 && proof.overlayOpen === true) return true;
      }
      return false;
    },
    recover: async (app, menu) => {
      if (!disposed && canUseCompanion && runningApp() === app) {
        // The mirror-only endpoint authenticates before its one delivery. An
        // absent/unknown mirror cannot trigger the legacy keyboard-open path.
        const revision = sessionRevision;
        const result = await nativeGame.recover(menu) as { handled?: boolean };
        if (result?.handled === false && !disposed && runningApp() === app && sessionRevision === revision) await mirror.recover(menu);
      }
    },
    delay: ms => new Promise(resolve => window.setTimeout(resolve, ms)),
  });
  const applicationSelected = () => {
    sessionRevision++;
    guideAdmission.invalidate();
    ownedDispatcher?.invalidate();
    homeRevision++;
    coldProbe = undefined;
    mirrorApp = 0;
    void nativeGame.invalidate();
    void mirror.invalidate();
    companion?.invalidate();
    fallback.invalidate();
    control.applicationSelected();
  };
  const register = (registration: any) => {
    patches.push({ unpatch: () => {
      if (typeof registration === "function") registration();
      else if (typeof registration?.unregister === "function") registration.unregister();
      else registration?.Unregister?.();
    } });
  };
  const patch = (object: any, method: string, handler: (args: any[], result: any) => any, list = patches) => {
    if (typeof object?.[method] === "function") list.push(ui.afterPatch(object, method, handler));
  };
  const unpatch = (list: Patch[]) => {
    for (const entry of list.splice(0).reverse()) try { entry.unpatch(); } catch {}
  };
  const refreshMainHooks = () => {
    if (disposed) return;
    const store = windowStore();
    if (store !== observedStore) {
      unpatch(storePatches);
      observedStore = store;
      try {
        // Steam updates the desired-window map synchronously in this method.
        patch(store, "UpdateDesiredWindows", (_args, result) => {
          try { windowChanged(); }
          catch (error) { console.warn("[Playhub QAM] Steam window refresh unavailable", error); }
          return result;
        }, storePatches);
      } catch (error) {
        console.warn("[Playhub QAM] Steam window store hook unavailable", error);
      }
    }
    const menu = main();
    const instance = menu ? mainWindow() : undefined;
    if (instance === attachedMain && menu === attachedMenu) return;
    if (attachedMain) applicationSelected();
    unpatch(mainPatches);
    attachedMain = instance;
    attachedMenu = menu;
    if (!instance || !menu) return;
    const current = () => !disposed && mainWindow() === instance && main() === menu;
    try {
      if (companion && typeof ui.replacePatch === "function" && typeof ui.callOriginal === "symbol") {
        for (const [method, intent, origin] of [["OnHomeButtonPressed", "main", "home"],
          ["OnQuickAccessButtonPressed", "quick", "quick"]] as const) {
          if (typeof instance[method] !== "function") continue;
          const ownedPatch = ui.replacePatch(instance, method, function (this: any, args: any[]) {
            if (!current()) return ui.callOriginal;
            const version = ++homeRevision;
            const fallback = () => {
              if (current() && version === homeRevision) ownedPatch.original.apply(this, args);
            };
            return ownedDispatcher!.signal(origin, intent, fallback) ? undefined : ui.callOriginal;
          });
          mainPatches.push(ownedPatch);
        }
      }
      patch(menu, "OpenSideMenu", (args, result) => {
        const side = Number(args[0]);
        if (current() && (side === 1 || side === 2) && menu.GetOpenSideMenu() === side) control.opened(menu.GetQuickAccessTab?.(), side);
        return result;
      }, mainPatches);
      patch(menu, "CloseSideMenus", (_args, result) => {
        if (current() && menu.GetOpenSideMenu() === 0) { homeRevision++; control.closed(); }
        return result;
      }, mainPatches);
    } catch (error) {
      unpatch(mainPatches);
      console.warn("[Playhub QAM] Steam main window hooks unavailable", error);
    }
  };
  const cancelMainRefresh = () => {
    for (const timer of refreshTimers) window.clearTimeout(timer);
    refreshTimers = [];
  };
  const windowChanged = () => {
    if (disposed) return;
    cancelMainRefresh();
    refreshMainHooks();
    // The Steam notification can precede creation of the replacement browser.
    refreshTimers = mainWindowRefreshDelays.map(delay => window.setTimeout(refreshMainHooks, delay));
  };
  const dispose = () => {
    disposed = true; systemGuideObserverCurrent = false;if(mirrorContinuationTimer!==undefined)window.clearInterval(mirrorContinuationTimer);
    guideAdmission.invalidate();
    releaseGameConsumer();
    ownedDispatcher?.dispose();
    cancelMainRefresh();
    companion?.dispose();
    void nativeGame.dispose();
    void mirror.dispose();
    fallback.dispose();
    control.dispose();
    unpatch(mainPatches);
    unpatch(storePatches);
    unpatch(patches);
  };
  try {
    // These are global Steam menu events, independent of the mounted Decky tab.
    refreshMainHooks();
    if (!attachedMain) windowChanged();
    // Steam owns the native desired-window callback slot. Observe its completed
    // store update above so Desktop/Big Picture creation keeps that callback.
    patch(ui.Navigation, "OpenQuickAccessMenu", (args, result) => { refreshMainHooks(); control.opened(args[0]); return result; });
    // Library navigation selects Steam/an app. QAM pages (including Playhub
    // Dashboard) must retain the handoff while the user works inside them.
    const navigating = (args: any[]) => {
      if (typeof args[0] === "string" && /^\/library(?:\/|$)/.test(args[0])) applicationSelected();
    };
    if (typeof ui.Router?.Navigate === "function") patches.push(ui.beforePatch(ui.Router, "Navigate", navigating));
    if (typeof ui.Navigation?.Navigate === "function") patches.push(ui.beforePatch(ui.Navigation, "Navigate", navigating));
    if (typeof steam?.Apps?.RunGame === "function") patches.push(ui.beforePatch(steam.Apps, "RunGame", (args: any[]) => {
      refreshMainHooks();
      applicationSelected();
      ownedDispatcher?.noteLaunch(args[0]);
    }));
    try {
      patch(steam?.Apps, "RaiseWindowForGame", (args, result) => {
        const appId = shortcutAppId(args[0]);
        if (appId && main()) void call<[number], { ok: boolean }>("resume_xbox_game", appId).catch(() => {});
        return result;
      });
    } catch (error) {
      console.warn("[Playhub] Xbox resume hook unavailable", error);
    }
    // The event's nAppID describes the input configuration (the real desktop
    // layout can report 413080 or 0), not necessarily the running shortcut.
    // Native acquire verifies the actual foreground game and selected app.
    const system = steam?.System?.UI;
    if (typeof system?.RegisterForSystemKeyEvents === "function") {
      const systemGuideRegistration = system.RegisterForSystemKeyEvents((event: any) => {
        refreshMainHooks();
        const app = runningApp(), version = sessionRevision;
        if (canUseCompanion && (event?.eKey === 0 || event?.eKey === 1) && event.ePressed !== false && event.bPressed !== false
            ) {
          if (ownedDispatcher!.signal("system", event.eKey === 1 ? "quick" : "main"))
            return; // One common pipeline; no parallel legacy route or replay.
        }
        companion?.guide(event);
        fallback.guide(event);
        control.systemKey(event);
      });
      register(systemGuideRegistration);
      systemGuideObserverCurrent = typeof systemGuideRegistration === "function" || typeof systemGuideRegistration?.unregister === "function"
        || typeof systemGuideRegistration?.Unregister === "function";
    }
    if (companion) {
      try { register(steam.Overlay.RegisterForOverlayActivated((pid: number, appId: number, active: boolean, userInitiated?: boolean) => {
        if (nativeGame.owns(runningApp())) {
          nativeGame.activated(pid, appId, active);
          return; // Actual game-host lease never feeds the sidecar/mirror input route.
        }
        if (mirrorApp === runningApp()) {
          if (active === false && mirror.shown && userInitiated !== false
              && matchesOverlayApp({ appID: appId, gameID: ((BigInt(runningApp()) << 32n) | 0x02000000n).toString() }, runningApp()))
            void mirror.close();
        } else companion.activated(pid, appId, active, userInitiated);
        if (active === false && matchesOverlayApp({ appID: appId,
          gameID: ((BigInt(runningApp()) << 32n) | 0x02000000n).toString() }, runningApp())) fallback.invalidate();
      })); }
      catch (error) {
        canUseCompanion = false;
        companion.dispose();
        console.warn("[Playhub] Xbox companion overlay events unavailable", error);
      }
    }
  } catch (error) {
    dispose();
    console.warn("[Playhub QAM] Menu hooks unavailable", error);
    return () => {};
  }
  return dispose;
}

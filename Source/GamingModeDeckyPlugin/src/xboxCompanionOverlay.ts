import { findOverlayWindow, matchesOverlayApp, selectOverlay, waitForOverlay, type OverlayIdentity } from "./dashboardOverlay";

export interface VerifiedOverlay {
  ok: boolean;
  gameId?: string;
  companionPid?: number;
  keyboardAt?: number;
  overlayPid?: number;
  foregroundScope?: number;
  overlayOpen?: boolean;
}

interface CompanionOverlayDependencies {
  runningApp(): number;
  browserInfo(): Promise<unknown>;
  requestColdOpen(app: number): Promise<VerifiedOverlay>;
  verify(app: number, overlayPid: number, keyboard: boolean, menu: boolean): Promise<VerifiedOverlay>;
  windows(): unknown;
  setOverlayState(gameId: string, state: number): Promise<unknown>;
  useSteamMenuLayout?(): void;
  observeMenu?(menu: unknown, changed: () => void): (() => void) | undefined;
  delay(ms: number): Promise<void>;
  now(): number;
  onColdRecovery?(): void;
  trace?(stage: string): void;
}

const guideDebounceMs = 220;
const coldBrowserWaits = [220, ...Array<number>(34).fill(80)];
const shortcutGameId = (app: number) => ((BigInt(app) << 32n) | 0x02000000n).toString();

export function createXboxCompanionOverlay(deps: CompanionOverlayDependencies) {
  let disposed = false;
  let activeRequest: object | undefined;
  let pendingCold: object | undefined;
  let lastGuideAt = -Infinity;
  let menuIntent: { app: number; menu: "main" | "quick" } | undefined;
  let lastKeyboard: { gameId: string; at: number } | undefined;
  let lastClose: { app: number; pid: number; at: number } | undefined;
  let menuPreparation: object | undefined;
  let coldOwner: { request: object; app: number; companionPid: number; overlayPid: number;
    phase: "bootstrap" | "menu"; overlay?: OverlayIdentity; window?: any } | undefined;

  const matchingBrowser = (infos: any[], app: number, gameId: string) =>
    infos.some(info => String(info?.gameID ?? "") === gameId || matchesOverlayApp(info, app));
  const closedSince = (app: number, since: number, overlayPid: number, companionPid?: number) => lastClose?.app === app
    && (lastClose.pid === overlayPid || lastClose.pid === companionPid) && lastClose.at >= since;
  const wantedMenu = (app: number) => menuIntent?.app === app ? menuIntent.menu : "main";
  const menuMethod = (app: number) => wantedMenu(app) === "quick" ? "OpenQuickAccessMenu" : "OpenMainMenu";

  async function showMenu(app: number, overlay: OverlayIdentity, proof: VerifiedOverlay, keyboard: boolean,
    canceled: () => boolean, recovery = false) {
    deps.trace?.(`menu-state-request keyboard=${keyboard} recovery=${recovery}`);
    const target = await waitForOverlay(() => findOverlayWindow(deps.windows(), overlay), canceled, () => deps.delay(40));
    if (canceled() || typeof target?.MenuStore?.[menuMethod(app)] !== "function") {
      deps.trace?.(`menu-window-unavailable recovery=${recovery} canceled=${canceled()}`);
      return;
    }
    // Wait for the exact browser before the final foreground/keyboard proof.
    await deps.delay(40);
    if (canceled()) { deps.trace?.("menu-delay-expired"); return; }
    // A cold Valve browser can be ready while the exact game still owns
    // foreground. Authenticate the recent native replay before mounting its
    // menu; requiring an already-open menu here creates a bootstrap cycle.
    const current = await deps.verify(app, overlay.unPID, keyboard, !keyboard);
    if (canceled() || !current.ok || current.gameId !== overlay.gameID || current.companionPid !== proof.companionPid
        || !Number.isSafeInteger(current.companionPid) || Number(current.companionPid) <= 0 || Number(current.companionPid) > 0xffffffff
        || current.overlayPid !== overlay.unPID
        || (keyboard && current.keyboardAt !== proof.keyboardAt)) {
      deps.trace?.(`menu-ownership-changed recovery=${recovery}`);
      return;
    }
    if (keyboard && current.foregroundScope !== 1 && current.foregroundScope !== 2) return;
    const currentWindow = findOverlayWindow(deps.windows(), overlay);
    const method = menuMethod(app);
    if (currentWindow === target && typeof currentWindow?.MenuStore?.[method] === "function") {
      const menu = currentWindow.MenuStore;
      let preparing = false, menuChanged = false;
      const stopObserving = deps.observeMenu?.(menu, () => { if (!preparing) menuChanged = true; });
      if (!stopObserving) { deps.trace?.("menu-ownership-unavailable"); return; }
      let side = method === "OpenQuickAccessMenu" ? 2 : 1;
      const before = menu.GetOpenSideMenu?.();
      const preparation = {};
      const ownerRequest = activeRequest;
      const preparedAt = deps.now();
      menuPreparation = preparation;
      let committed = false;
      try {
        // Guide+A can change the requested side while this same replay starts.
        // Follow that intent once, without issuing another native open request.
        for (let attempt = 0; attempt < 2; attempt++) {
          if (canceled() || menuChanged || findOverlayWindow(deps.windows(), overlay) !== currentWindow
              || currentWindow.MenuStore !== menu) return;
          const requestedMethod = menuMethod(app);
          side = requestedMethod === "OpenQuickAccessMenu" ? 2 : 1;
          if (coldOwner && coldOwner.request === ownerRequest) coldOwner.phase = "menu";
          preparing = true;
          try {
            if (requestedMethod === "OpenQuickAccessMenu") menu.OpenQuickAccessMenu(menu.GetQuickAccessTab?.() ?? 0);
            else menu.OpenMainMenu();
          } finally { preparing = false; }
          if (menu.GetOpenSideMenu?.() !== side) { deps.trace?.("menu-preparation-suppressed"); return; }
          // Steam's own composition observer must publish the mounted menu.
          // No native foreground rule or focus API is changed by this step.
          if (keyboard) await deps.delay(150);
          if (canceled() || menuChanged) return;
          if (requestedMethod !== menuMethod(app)) continue;
          const shown = await deps.verify(app, overlay.unPID, keyboard, true);
          if (canceled() || menuChanged) return;
          if (requestedMethod !== menuMethod(app)) continue;
          if (!shown.ok || shown.gameId !== overlay.gameID
              || shown.companionPid !== current.companionPid || shown.overlayPid !== overlay.unPID
              || shown.foregroundScope !== 2 || shown.overlayOpen !== true
              || (keyboard && shown.keyboardAt !== current.keyboardAt)
              || findOverlayWindow(deps.windows(), overlay) !== currentWindow
              || currentWindow.MenuStore !== menu || menu.GetOpenSideMenu?.() !== side) {
            deps.trace?.("menu-composition-unconfirmed"); return;
          }
          deps.useSteamMenuLayout?.();
          await deps.setOverlayState(overlay.gameID, 2);
          committed = true;
          if (canceled() || menuChanged || findOverlayWindow(deps.windows(), overlay) !== currentWindow
              || currentWindow.MenuStore !== menu || menu.GetOpenSideMenu?.() !== side) return;
          if (requestedMethod !== menuMethod(app)) continue;
          if (recovery) deps.onColdRecovery?.();
          deps.trace?.(`menu-requested recovery=${recovery}`);
          return;
        }
      } finally {
        stopObserving();
        if (menuPreparation === preparation) {
          menuPreparation = undefined;
          // Roll back only a menu this request mounted into an empty exact
          // browser. Never close a pre-existing or subsequently replaced menu.
          if (!committed && !menuChanged && before === 0 && !disposed && deps.runningApp() === app && activeRequest === ownerRequest
              && !closedSince(app, preparedAt, overlay.unPID, current.companionPid)
              && findOverlayWindow(deps.windows(), overlay) === currentWindow
              && currentWindow.MenuStore === menu && menu.GetOpenSideMenu?.() === side)
            menu.CloseSideMenus?.();
        }
      }
    }
  }

  async function recoverCold(app: number, gameId: string, request: object, started: number, delivery: VerifiedOverlay,
    retained?: { overlay: OverlayIdentity; window: any; menu: any }) {
    const { companionPid, overlayPid, keyboardAt } = delivery;
    if (!delivery.ok || delivery.gameId !== gameId || !Number.isInteger(companionPid) || !companionPid || companionPid < 0
        || !Number.isInteger(overlayPid) || !overlayPid || overlayPid < 0
        || !Number.isSafeInteger(keyboardAt) || keyboardAt == null || keyboardAt < 0) {
      deps.trace?.("cold-delivery-refused");
      return;
    }
    if (disposed || pendingCold !== request || deps.runningApp() !== app) return;
    const owner = { request, app, companionPid, overlayPid, phase: "bootstrap" as "bootstrap" | "menu",
      overlay: retained?.overlay as OverlayIdentity | undefined, window: retained?.window as any };
    coldOwner = owner;
    deps.trace?.(`cold-delivered elapsed=${Math.round(deps.now() - started)} pending=${pendingCold === request}`);
    // Delivery starts the native replay. Its first Valve browser can register
    // after 780 ms; give that initialization its own bounded window rather
    // than spending it on the preceding identity/RPC checks.
    const browserDeadline = deps.now() + 3000;
    const invalid = () => disposed || deps.runningApp() !== app || deps.now() > browserDeadline
      || closedSince(app, started, overlayPid, companionPid)
      || (retained && retained.window.MenuStore !== retained.menu)
      || (owner.window && findOverlayWindow(deps.windows(), owner.overlay!) !== owner.window);
    const canceled = () => pendingCold !== request || invalid();
    for (const wait of coldBrowserWaits) {
      await deps.delay(wait);
      if (canceled()) { deps.trace?.(`cold-canceled elapsed=${Math.round(deps.now() - started)} active=${Boolean(activeRequest)}`); return; }
      if (activeRequest) continue;
      const infos = await deps.browserInfo();
      if (canceled() || !Array.isArray(infos)) { deps.trace?.("cold-browser-query-expired"); return; }
      const overlay = selectOverlay(infos.filter(info => Number(info?.unPID) === overlayPid
        && String(info?.gameID) === gameId), app);
      if (!overlay) {
        if (matchingBrowser(infos.filter(info => Number(info?.unPID) === overlayPid), app, gameId)) {
          deps.trace?.("cold-browser-identity-ambiguous"); return;
        }
        continue;
      }
      if (overlay.gameID !== gameId || overlay.unPID !== overlayPid) return;
      owner.overlay = overlay;
      const initialWindow = findOverlayWindow(deps.windows(), overlay);
      if (owner.window && initialWindow !== owner.window) return;
      owner.window = initialWindow;
      deps.trace?.("native-browser-ready");
      const proof = await deps.verify(app, overlayPid, true, false);
      if (canceled()) return;
      if (activeRequest) continue;
      if (!proof.ok || proof.gameId !== gameId || proof.companionPid !== companionPid || proof.overlayPid !== overlayPid
          || (proof.foregroundScope !== 1 && proof.foregroundScope !== 2)
          || !Number.isSafeInteger(proof.keyboardAt) || Number(proof.keyboardAt) <= keyboardAt) {
        deps.trace?.("native-replay-proof-refused");
        return;
      }
      const target = findOverlayWindow(deps.windows(), overlay);
      const side = target?.MenuStore?.GetOpenSideMenu?.();
      if (proof.foregroundScope === 2 && proof.overlayOpen === true && side === (wantedMenu(app) === "quick" ? 2 : 1)) {
        deps.useSteamMenuLayout?.();
        return;
      }
      if (typeof proof.overlayOpen !== "boolean") return;
      // A validated activation callback and this retry share the same state/menu path.
      pendingCold = undefined;
      activeRequest = request;
      lastKeyboard = { gameId, at: Number(proof.keyboardAt) };
      await showMenu(app, overlay, proof, true, () => activeRequest !== request || invalid(), true);
      return;
    }
    deps.trace?.(`cold-browser-wait-finished active=${Boolean(activeRequest)}`);
  }

  async function open(keyboard: boolean, eventPid = 0, eventAppId = 0, controllerGuide = false) {
    const app = deps.runningApp();
    if (disposed || activeRequest || app < 0x80000000) return;
    const started = deps.now();
    if (!keyboard && started - lastGuideAt < guideDebounceMs) return;
    if (!keyboard) { lastGuideAt = started; pendingCold = undefined; }
    const request = {};
    activeRequest = request;
    const canceled = () => disposed || activeRequest !== request || deps.runningApp() !== app || deps.now() - started > 1500;
    const closedWithGuide = (overlayPid: number, companionPid?: number) => !keyboard
      && closedSince(app, started - guideDebounceMs, overlayPid, companionPid);
    try {
      const infos = await deps.browserInfo();
      if (canceled() || !Array.isArray(infos)) return;
      const exactGameId = shortcutGameId(app);
      const overlay = selectOverlay(infos, app);
      if (!overlay) {
        if (!controllerGuide || (lastClose?.app === app && lastClose.at >= started - guideDebounceMs)
            || matchingBrowser(infos, app, exactGameId)) return;
        // The native replay can create the first Valve browser before the RPC returns.
        pendingCold = request;
        activeRequest = undefined;
        const delivery = await deps.requestColdOpen(app);
        if (pendingCold === request) await recoverCold(app, exactGameId, request, started, delivery);
        return;
      }
      if (overlay.gameID !== exactGameId || closedWithGuide(overlay.unPID)
          || (keyboard && !matchesOverlayApp({ appID: eventAppId, gameID: exactGameId }, app))) return;
      const verified = await deps.verify(app, overlay.unPID, keyboard, false);
      if (canceled() || closedWithGuide(overlay.unPID, verified.companionPid)) return;
      if (!verified.ok) {
        if (!controllerGuide || !deps.useSteamMenuLayout) return;
        // Valve can open a registered overlay before the open request checks
        // the game's foreground. Confirm the already-open companion in place.
        for (const wait of [40, 120, 200]) {
          await deps.delay(wait);
          if (canceled() || closedWithGuide(overlay.unPID)) return;
          const current = await deps.verify(app, overlay.unPID, false, true);
          if (canceled() || closedWithGuide(overlay.unPID, current.companionPid)) return;
          if (current.ok && current.gameId === exactGameId && current.overlayPid === overlay.unPID
              && current.foregroundScope === 2 && current.overlayOpen === true) {
            const target = findOverlayWindow(deps.windows(), overlay);
            if (target?.MenuStore?.GetOpenSideMenu?.() === (wantedMenu(app) === "quick" ? 2 : 1))
              deps.useSteamMenuLayout();
            else await showMenu(app, overlay, current, false, () => canceled() || closedWithGuide(overlay.unPID, current.companionPid));
            return;
          }
        }
        return;
      }
      if (verified.gameId !== exactGameId) return;
      if (!keyboard && controllerGuide && verified.foregroundScope === 1 && verified.overlayOpen === false) {
        // Back leaves Valve's browser registered. Its document may still
        // report focus while the real game owns foreground. Every new Guide
        // must therefore request a new authenticated native activation, just
        // as the first Guide did, before mounting anything in that browser.
        if (verified.overlayPid !== overlay.unPID || !Number.isSafeInteger(verified.companionPid)
            || Number(verified.companionPid) <= 0 || Number(verified.companionPid) > 0xffffffff) return;
        const retainedWindow = findOverlayWindow(deps.windows(), overlay);
        const retainedMenu = retainedWindow?.MenuStore;
        if (!retainedWindow || !retainedMenu) return;
        pendingCold = request;
        activeRequest = undefined;
        const delivery = await deps.requestColdOpen(app);
        if (pendingCold === request && delivery.companionPid === verified.companionPid
            && delivery.overlayPid === overlay.unPID && findOverlayWindow(deps.windows(), overlay) === retainedWindow
            && retainedWindow.MenuStore === retainedMenu)
          await recoverCold(app, exactGameId, request, started, delivery,
            { overlay, window: retainedWindow, menu: retainedMenu });
        return;
      }
      if (keyboard) {
        if (eventPid !== overlay.unPID && eventPid !== verified.companionPid) return;
        const at = verified.keyboardAt ?? 0;
        if (!Number.isSafeInteger(at) || at <= 0 || (lastKeyboard?.gameId === exactGameId && at <= lastKeyboard.at)) return;
        lastKeyboard = { gameId: exactGameId, at };
        pendingCold = undefined;
      }
      await showMenu(app, overlay, verified, keyboard, () => canceled() || closedWithGuide(overlay.unPID, verified.companionPid));
    } catch {
      deps.trace?.("activation-failed");
      // Steam retains its native handling if the scoped companion is unavailable.
    } finally {
      if (activeRequest === request) activeRequest = undefined;
      if (pendingCold === request) pendingCold = undefined;
      if (coldOwner?.request === request) coldOwner = undefined;
    }
  }

  return {
    async recover(app: number, menu: "main" | "quick" = "main") {
      if (!disposed && deps.runningApp() === app) menuIntent = { app, menu };
      if (disposed || activeRequest || pendingCold || deps.runningApp() !== app || app < 0x80000000) return;
      const started = deps.now(), request = {};
      deps.trace?.("fallback-started");
      pendingCold = request;
      try {
        // The same authenticated native replay also handles a Valve browser
        // that exists but never displayed the menu after the initial Guide.
        const proof = await deps.verify(app, 0, false, false);
        if (disposed || pendingCold !== request || deps.runningApp() !== app || !proof.ok
            || (lastClose?.app === app && lastClose.at >= started)
            || proof.gameId !== shortcutGameId(app) || proof.overlayOpen || proof.foregroundScope !== 1) return;
        const delivery = await deps.requestColdOpen(app);
        if (pendingCold === request) await recoverCold(app, shortcutGameId(app), request, started, delivery);
        else deps.trace?.("cold-request-replaced");
      } catch { deps.trace?.("cold-request-failed"); } finally {
        if (pendingCold === request) pendingCold = undefined;
        if (activeRequest === request) activeRequest = undefined;
        if (coldOwner?.request === request) coldOwner = undefined;
      }
    },
    guide(event: { eKey?: number; ePressed?: boolean; bPressed?: boolean; nControllerIndex?: number }) {
      if ((event?.eKey === 0 || event?.eKey === 1) && event.ePressed !== false && event.bPressed !== false) {
        const app = deps.runningApp(), menu = event.eKey === 1 ? "quick" : "main";
        const index = event.nControllerIndex;
        const controllerGuide = typeof index === "number" && Number.isInteger(index) && index >= 0;
        if (controllerGuide && menuIntent?.app === app && menuIntent.menu === menu
            && (pendingCold || coldOwner?.app === app && activeRequest === coldOwner.request)
            && deps.now() - lastGuideAt >= guideDebounceMs) {
          // A second real Guide is a new user intent, not a startup close
          // notification. Retire this replay instead of reopening its menu.
          activeRequest = pendingCold = undefined;
          coldOwner = undefined;
          lastGuideAt = deps.now();
          return;
        }
        if (menuIntent?.app === app && menuIntent.menu !== menu) {
          // Guide+A arrives as a second edge while the same native replay is
          // starting. Retain that replay and let its final menu read the intent.
          if (activeRequest || pendingCold) { menuIntent = { app, menu }; return; }
          lastGuideAt = -Infinity;
        }
        menuIntent = { app, menu };
        void open(false, 0, 0, controllerGuide);
      }
    },
    activated(pid: number, appId: number, active: boolean, userInitiated?: boolean) {
      if (disposed || !Number.isInteger(pid) || pid <= 0) return;
      if (active === true) void open(true, pid, appId);
      else if (active === false) {
        const app = deps.runningApp();
        if (app >= 0x80000000 && matchesOverlayApp({ appID: appId, gameID: shortcutGameId(app) }, app)) {
          const owner = coldOwner;
          if (userInitiated === false && owner?.app === app && owner.overlayPid === pid && owner.phase === "bootstrap"
              && (pendingCold === owner.request || activeRequest === owner.request)) {
            try {
              const current = owner.overlay ? findOverlayWindow(deps.windows(), owner.overlay) : undefined;
              // An old browser may retain a mounted DOM menu after Back.
              // It is not native activation evidence during this new replay.
              if (!owner.window || current === owner.window) {
                deps.trace?.("cold-initial-inactive");
                return;
              }
            } catch { /* An uncertain or replaced menu remains a close. */ }
          }
          lastClose = { app, pid, at: deps.now() };
        }
      }
    },
    invalidate() { activeRequest = pendingCold = undefined; coldOwner = undefined; lastClose = undefined; menuIntent = undefined; },
    dispose() { disposed = true; activeRequest = pendingCold = undefined; coldOwner = undefined; menuIntent = undefined; },
  };
}

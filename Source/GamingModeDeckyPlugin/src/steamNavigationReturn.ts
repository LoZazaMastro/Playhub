type SteamNavigation = {
  hasDocumentFocus: () => boolean;
  hasNativeForeground?: () => boolean;
  hasNavigationFocus: () => boolean;
  requestKeyFocus: () => void;
  focusApplication: () => void;
};

type SteamNavTree = {
  GetLastFocusedNode?: () => { Element?: Element; BHasFocus?: () => boolean } | undefined;
};

type NavigationWindow = { closed?: boolean; document?: Pick<Document, "hasFocus"> };
type NavigationContext = {
  RootWindow?: NavigationWindow;
  ActiveWindow?: NavigationWindow;
  OnDeactivate?: (window: NavigationWindow) => void;
};
export type SteamReturnOverlayWindow = {
  BrowserWindow?: NavigationWindow;
  m_BrowserWindow?: NavigationWindow;
  params?: { browserInfo?: { m_unAppID?: unknown; m_gameID?: unknown } };
  IsGamepadUIOverlayWindow?: () => boolean;
  GetFocusNavContext?: () => NavigationContext | undefined;
};

type OverlayAppMatches = (overlay: SteamReturnOverlayWindow, appId: number) => boolean;
const exactOverlayApp: OverlayAppMatches = (overlay, appId) => overlay.params?.browserInfo?.m_unAppID === appId;

export type SteamOverlayScope = { appId: number; root: NavigationWindow; context: NavigationContext };

function readOverlayScope(overlay: SteamReturnOverlayWindow, appId: number, matchesApp: OverlayAppMatches): SteamOverlayScope | undefined {
  try {
    if (overlay.IsGamepadUIOverlayWindow?.() !== true || !matchesApp(overlay, appId)) return;
    const root = overlay.BrowserWindow ?? overlay.m_BrowserWindow;
    const context = overlay.GetFocusNavContext?.();
    if (root && context?.RootWindow === root) return { appId, root, context };
  } catch { /* The Steam browser may be closing. */ }
}

export function createSteamOverlayReturnScope(matchesApp = exactOverlayApp, now = () => performance.now()) {
  let watchedApp = 0;
  let retained: SteamOverlayScope | undefined;
  let stoppedAt: number | undefined;
  function clear() { watchedApp = 0; retained = undefined; stoppedAt = undefined; }
  return {
    observe(appId: number, windows: SteamReturnOverlayWindow[] | undefined, active?: NavigationContext) {
      if (appId <= 0) {
        // Steam can clear the game before native exit proof arrives.
        stoppedAt ??= now();
        if (now() - stoppedAt >= 15_000) clear();
        return;
      }
      stoppedAt = undefined;
      if (appId !== watchedApp) { watchedApp = appId; retained = undefined; }
      const candidates = windows?.flatMap(window => {
        const scope = readOverlayScope(window, appId, matchesApp);
        return scope && scope.root.closed !== true ? [scope] : [];
      }) ?? [];
      const activeCandidates = candidates.filter(scope => scope.context === active);
      if (activeCandidates.length === 1) retained = activeCandidates[0];
      else if (!retained && candidates.length === 1) retained = candidates[0];
    },
    get(appId: number) {
      if (stoppedAt !== undefined && now() - stoppedAt >= 15_000) clear();
      return retained?.appId === appId ? retained : undefined;
    },
    clear,
  };
}

export function releaseExitedSteamOverlay(
  exitedApp: number,
  mainWindow: NavigationWindow,
  canRestore: () => boolean,
  getActiveContext: () => NavigationContext | undefined,
  getActiveTree: () => { WindowContext?: NavigationContext; Window?: NavigationWindow } | undefined,
  getOverlays: (appId: number) => SteamReturnOverlayWindow[] | undefined,
  retained?: SteamOverlayScope,
  matchesApp = exactOverlayApp,
  hasNativeForeground?: () => boolean,
): boolean {
  const mainIsFocused = () => canRestore() &&
    (mainWindow.document?.hasFocus() === true || hasNativeForeground?.() === true);
  if (!Number.isInteger(exitedApp) || exitedApp <= 0 || !mainIsFocused()) return false;
  const active = getActiveContext();
  const overlayWindow = active?.RootWindow;
  const activeWindow = active?.ActiveWindow;
  if (!active || !overlayWindow || !activeWindow || overlayWindow === mainWindow || activeWindow === mainWindow
      || typeof active.OnDeactivate !== "function") return false;
  const retainedMatch = retained?.appId === exitedApp && retained.root === overlayWindow && retained.context === active;
  if (!retainedMatch) {
    const matching = getOverlays(exitedApp)?.filter(overlay => {
      const scope = readOverlayScope(overlay, exitedApp, matchesApp);
      return scope?.root === overlayWindow && scope.context === active;
    });
    if (matching?.length !== 1) return false;
  }
  const tree = getActiveTree();
  // Steam can leave hasFocus() true in the dead game's overlay. The caller's
  // exact native exit/foreground proof or focused main browser is authoritative.
  if (tree?.WindowContext !== active || tree.Window !== activeWindow
      || !mainIsFocused() || getActiveContext() !== active || getActiveTree() !== tree || active.ActiveWindow !== activeWindow) return false;
  try { active.OnDeactivate(activeWindow); }
  catch { /* Steam can detach the context before a closing browser throws. */ }
  return getActiveContext() !== active;
}

export function hasVisibleSteamNavigationFocus(document: Document, tree?: SteamNavTree): boolean {
  const visible = (element?: Element) => element?.ownerDocument === document && element.isConnected &&
    element.getClientRects().length > 0 && !element.closest('[hidden],[inert],[aria-hidden="true"]') &&
    element.checkVisibility?.({ opacityProperty: true, visibilityProperty: true }) !== false;

  if (tree?.GetLastFocusedNode) {
    const node = tree.GetLastFocusedNode();
    return visible(node?.Element) && (node?.BHasFocus?.() ?? node?.Element?.matches(".gpfocus")) === true;
  }
  return Array.from(document.querySelectorAll(".gpfocus")).some(visible);
}

export function focusReturnedSteamGameAction(
  document: Document,
  route: unknown,
  appId: number,
  current: Element | undefined,
  classes: { AppActionButton?: unknown; ActionButtonAndStatusPanel?: unknown } | undefined,
): boolean {
  if (route !== `/library/app/${appId}` || current?.closest('a,button,[role="button"],[role="tab"],[role="menuitem"]')) return false;
  const button = classes?.AppActionButton;
  const panel = classes?.ActionButtonAndStatusPanel;
  if (typeof button !== "string" || typeof panel !== "string" ||
      !/^[\w-]+$/.test(button) || !/^[\w-]+$/.test(panel)) return false;

  const actions = document.querySelectorAll<HTMLElement>(`.${panel} .${button} [role="button"][tabindex]`);
  const target = Array.from(actions).find(action => action.tabIndex >= 0 && action.isConnected &&
    action.getClientRects().length > 0 && !action.closest('[hidden],[inert],[aria-hidden="true"],[aria-disabled="true"]') &&
    action.checkVisibility?.({ opacityProperty: true, visibilityProperty: true }) !== false);
  if (!target) return false;
  target.focus({ preventScroll: true });
  return document.activeElement === target;
}

export function navigateReturnedSteamGame(
  main: {
    m_locationPathname?: unknown;
    BCanNavigate?: () => boolean;
    Navigate?: (route: string, replace: boolean, onlyWhenDifferent: boolean) => void;
  },
  appId: number,
): boolean {
  if (!Number.isInteger(appId) || appId <= 0 || appId > 0xffffffff) return false;
  const route = `/library/app/${appId}`;
  if (main.m_locationPathname === route || main.BCanNavigate?.() !== true || typeof main.Navigate !== "function") return false;
  // Retire only Steam's transient running-game placeholder. Other pages are
  // real history entries: preserve them so Back returns to the user's library.
  main.Navigate(route, main.m_locationPathname === "/apprunning", true);
  return true;
}

// The caller has just verified the exact Win32 foreground. Steam's child
// browser can remain blurred after an overlay closes; repair its navigation
// while CEF catches up, but confirm success only after its real input focus.
export function recoverSteamNavigation(steam: SteamNavigation): boolean {
  try {
    const mayRestore = () => steam.hasDocumentFocus() || steam.hasNativeForeground?.() === true;
    if (!steam.hasDocumentFocus()) steam.requestKeyFocus();
    if (!mayRestore()) return false;
    if (!steam.hasNavigationFocus()) {
      steam.focusApplication();
      // Changing the active tree can blur the old browser after the first
      // SetKeyFocus call. Request keys again for the now-current main tree.
      if (mayRestore() && !steam.hasDocumentFocus()) steam.requestKeyFocus();
    }
    return mayRestore() && steam.hasDocumentFocus() && steam.hasNavigationFocus();
  } catch {
    return false;
  }
}

export type SteamReturnConfirmation = { ok: boolean; appId: number; usedCompanion?: boolean; gamePid?: number; gameBirth?: string };

export type SteamNavigationRecoveryContext = {
  generation: number;
  exitedGame?: Readonly<{ pid: number; birth: string }>;
  signal: AbortSignal;
  isCurrent: () => boolean;
  confirmForeground: () => Promise<boolean>;
};

type SteamKeyFocusReset = {
  context: SteamNavigationRecoveryContext;
  canRestore: () => boolean;
  hasRealFocus: () => boolean;
  setKeyFocus: (focused: boolean) => void | Promise<void>;
};

// Steam can retain a stale native "keys focused" bit while its CEF child is
// blurred. A single real false/true edge repairs that state; never manufacture
// document focus or repeat the edge on every 500 ms recovery poll.
export function createSteamKeyFocusReset(
  schedule: (callback: () => void, delay: number) => number,
  cancelTimer: (timer: number) => void,
) {
  let spentGeneration: number | undefined;
  let pending: { generation: number; promise: Promise<boolean> } | undefined;

  function wait(context: SteamNavigationRecoveryContext): Promise<boolean> {
    return new Promise(resolve => {
      if (context.signal.aborted) { resolve(false); return; }
      let timer: number | undefined;
      const finish = (elapsed: boolean) => {
        if (timer !== undefined) { cancelTimer(timer); timer = undefined; }
        context.signal.removeEventListener("abort", abort);
        resolve(elapsed);
      };
      const abort = () => finish(false);
      context.signal.addEventListener("abort", abort, { once: true });
      timer = schedule(() => finish(true), 50);
    });
  }

  return {
    reset(request: SteamKeyFocusReset): Promise<boolean> {
      const { context } = request;
      if (pending?.generation === context.generation) return pending.promise;
      const valid = () => !context.signal.aborted && context.isCurrent() && request.canRestore();
      if (!valid() || request.hasRealFocus() || spentGeneration === context.generation) return Promise.resolve(false);
      const operation = (async () => {
        try {
          if (!await context.confirmForeground() || !valid() || request.hasRealFocus()) return false;
          spentGeneration = context.generation;
          await request.setKeyFocus(false);
          if (!valid() || !await wait(context) || !valid()) return false;
          // The user can Alt-Tab during the edge. Recheck the actual native
          // foreground after the delay and immediately before returning keys.
          if (!await context.confirmForeground() || !valid()) return false;
          await request.setKeyFocus(true);
          return valid(); // Dispatch proof only; callers still verify CEF + Play.
        } catch { return false; }
      })();
      pending = { generation: context.generation, promise: operation };
      void operation.finally(() => { if (pending?.promise === operation) pending = undefined; });
      return operation;
    },
  };
}

export function isSteamNavigationReturnApp(runningApp: number, exitedApp: number): boolean {
  return runningApp === 0 || (exitedApp > 0 && runningApp === exitedApp);
}

export function isConfirmedSteamIdle(value: unknown, anyAppRunning?: unknown): boolean {
  // Current Steam returns undefined for an empty running-app list. Its own
  // IsAnyAppRunning() must confirm that state; a missing store is not proof.
  return (value === 0 || value === "0") && anyAppRunning !== true ||
    value === undefined && anyAppRunning === false;
}

// Steam's focus event can precede the native process-exit confirmation.
// Coalesce duplicate focus events and briefly check again for that proof.
export function createSteamNavigationWakeup(
  poll: () => void,
  schedule: (callback: () => void, delay: number) => number,
  cancel: (timer: number) => void,
) {
  const delays = [65, 95, 145, 235, 400];
  let timer: number | undefined;
  let stopped = false;

  function next(index: number) {
    timer = index < delays.length ? schedule(() => {
      if (stopped) return;
      next(index + 1);
      poll();
    }, delays[index]) : undefined;
  }

  return {
    wake() {
      if (stopped || timer !== undefined) return;
      next(0);
      poll();
    },
    stop() {
      stopped = true;
      if (timer !== undefined) cancel(timer);
      timer = undefined;
    },
  };
}

export function createSteamNavigationRecovery(
  confirmForeground: (version: number) => Promise<SteamReturnConfirmation>,
  recoverNavigation: (exitedApp: number, usedCompanion: boolean | undefined, context: SteamNavigationRecoveryContext) => boolean | Promise<boolean>,
  runningApp: () => number,
  now = () => performance.now(),
) {
  let version = 0;
  let deadline = 0;
  let generation = 0;
  let launchBlockedUntil = 0;
  let abort = new AbortController();

  function cancel(nextVersion = version) {
    abort.abort();
    abort = new AbortController();
    version = nextVersion;
    deadline = 0;
    generation++;
  }

  async function observe(nextVersion: number, steamForeground: boolean) {
    if (nextVersion !== version) {
      cancel(nextVersion);
      if (now() >= launchBlockedUntil && Number.isSafeInteger(nextVersion) && nextVersion > 0)
        deadline = now() + 12_000;
    }
    if (!deadline || now() >= deadline || !steamForeground) return;

    const expected = generation;
    const before = runningApp();
    const confirmed = await confirmForeground(nextVersion);
    if (!confirmed.ok || expected !== generation || now() >= deadline
        || !isSteamNavigationReturnApp(before, confirmed.appId)
        || !isSteamNavigationReturnApp(runningApp(), confirmed.appId)) return;
    const current = () => expected === generation && deadline > 0 && now() < deadline
      && isSteamNavigationReturnApp(runningApp(), confirmed.appId);
    const context: SteamNavigationRecoveryContext = {
      generation: expected,
      exitedGame: confirmed.gamePid != null && confirmed.gameBirth != null
        ? Object.freeze({ pid: confirmed.gamePid, birth: confirmed.gameBirth }) : undefined,
      signal: abort.signal,
      isCurrent: current,
      async confirmForeground() {
        if (!current()) return false;
        const before = runningApp();
        const proof = await confirmForeground(nextVersion);
        return current() && proof.ok && proof.appId === confirmed.appId
          && proof.gamePid === confirmed.gamePid && proof.gameBirth === confirmed.gameBirth
          && isSteamNavigationReturnApp(before, confirmed.appId);
      },
    };
    if (await recoverNavigation(confirmed.appId, confirmed.usedCompanion, context) && current()) cancel();
  }

  return {
    observe, cancel,
    launchStarted() {
      // Ignore any late proof from the previous game until it has expired or
      // the native watcher confirms it is supervising a fresh game process.
      launchBlockedUntil = now() + 20_000;
      cancel();
    },
    gameArmed() { launchBlockedUntil = 0; },
  };
}

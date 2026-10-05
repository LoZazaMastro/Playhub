export function steamAppId(value: unknown): number {
  const id = Number(value);
  return Number.isInteger(id) && id >= -2147483648 && id <= 4294967295 ? id >>> 0 : 0;
}

export interface QamFocusDependencies {
  runningApp(): number;
  nativeOverlayAvailable(appId: number): Promise<boolean>;
  gameBar?(requestId: string, appId: number, pressed: boolean): Promise<{ ok: boolean }>;
  mainMenu(): { GetOpenSideMenu(): number; OpenQuickAccessMenu(tab?: number): void; OpenSideMenu?(side: number): void } | undefined;
  acquire(requestId: string, appId: number): Promise<{ ok: boolean }>;
  close(requestId: string): Promise<unknown>;
  release(requestId: string): Promise<unknown>;
  requestId(): string;
  warn(error: unknown): void;
  now?(): number;
}

// Explicit main-window QAM handoff. The game's native overlay stays owned by Steam.
export function createControlledQamFocus(deps: QamFocusDependencies) {
  let pending: { id: string; app: number; side: number; acquired: boolean } | undefined;
  let disposed = false;
  let openingMain = false;
  let closedAt = -Infinity, closedApp = 0;
  let delegatedAt = -Infinity, delegatedApp = 0;
  const systemKeys = new Map<string, number>();
  const now = deps.now ?? (() => Date.now());
  const report = (promise: Promise<unknown>) => { void promise.catch(deps.warn); };
  const invalidate = (restore: boolean) => {
    const request = pending;
    pending = undefined;
    if (request) report(restore ? deps.close(request.id) : deps.release(request.id));
  };
  return {
    opened(tab?: number, side = 2) {
      if (disposed || openingMain) return;
      if (pending) { pending.side = side; return; }
      const app = deps.runningApp();
      if (!(app > 0) || !deps.mainMenu()) return;
      if (app === delegatedApp && now() - delegatedAt < 220) return;
      const request = { id: deps.requestId(), app, side, acquired: false };
      pending = request;
      void (async () => {
        try {
          if (deps.gameBar) {
            let delegated = false;
            try { delegated = (await deps.gameBar(request.id, app, true)).ok === true; }
            finally { try { await deps.gameBar(request.id, app, false); } catch (error) { deps.warn(error); } }
            if (disposed || pending !== request) return;
            if (delegated) { delegatedApp = app; delegatedAt = now(); pending = undefined; return; }
          }
          // Unknown/failed native-overlay enumeration fails closed: it cannot
          // justify moving the user's foreground away from a working overlay.
          if (await deps.nativeOverlayAvailable(app)) {
            if (pending === request) pending = undefined;
            return;
          }
          if (disposed || pending !== request) return;
          if (deps.runningApp() !== app) { invalidate(false); return; }
          const main = deps.mainMenu();
          if (!main) { invalidate(false); return; }
          // Ask native policy before opening anything in Steam. A delegated
          // UWP/Game Bar session must not receive a forced main-menu open.
          const acquired = await deps.acquire(request.id, app);
          if (disposed || pending !== request || deps.runningApp() !== app) {
            if (pending === request) pending = undefined;
            report(deps.release(request.id));
            return;
          }
          if (!acquired.ok) { pending = undefined; return; }
          request.acquired = true;
          if (deps.mainMenu() !== main) { invalidate(true); return; }
          if (main.GetOpenSideMenu() !== request.side) {
            openingMain = true;
            try {
              if (request.side === 2) main.OpenQuickAccessMenu(tab);
              else if (typeof main.OpenSideMenu === "function") main.OpenSideMenu(1);
              else { invalidate(true); return; }
            } finally { openingMain = false; }
          }
          if (disposed || pending !== request || deps.runningApp() !== app || main.GetOpenSideMenu() !== request.side) {
            if (pending === request) invalidate(deps.runningApp() === app);
            else report(deps.release(request.id));
            return;
          }
        } catch (error) {
          if (pending === request) invalidate(request.acquired);
          deps.warn(error);
        }
      })();
    },
    systemKey(event: { eKey?: number; nControllerIndex?: number; ePressed?: boolean; bPressed?: boolean }) {
      if (disposed || event?.ePressed === false || event?.bPressed === false || (event?.eKey !== 0 && event?.eKey !== 1)) return;
      const time = now(), key = `${event.nControllerIndex ?? -1}:${event.eKey}`;
      if (time - (systemKeys.get(key) ?? -Infinity) < 220) return;
      systemKeys.set(key, time);
      if (systemKeys.size > 64) { systemKeys.clear(); systemKeys.set(key,time); }
      // Steam may already have closed the menu for this same key event.
      if (deps.runningApp() === closedApp && time - closedAt < 220) return;
      this.opened(undefined, event.eKey === 1 ? 2 : 1);
    },
    closed() {
      if (!openingMain) { closedAt = now(); closedApp = deps.runningApp(); invalidate(true); }
    },
    applicationSelected() { invalidate(false); },
    dispose() { disposed = true; invalidate(false); },
  };
}

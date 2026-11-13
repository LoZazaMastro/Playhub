interface OverlayFallbackDependencies {
  runningApp(): number;
  visible(app: number, menu: "main" | "quick"): Promise<boolean>;
  recover(app: number, menu: "main" | "quick"): Promise<void>;
  delay(ms: number): Promise<void>;
  now?(): number;
  schedule?(callback: () => void, ms: number): unknown;
  cancel?(timer: unknown): void;
}

// A finite, user-triggered visibility check. A registered but hidden Valve
// browser does not mean that the Guide command produced a usable menu.
export function createSteamOverlayFallback(deps: OverlayFallbackDependencies) {
  let generation = 0, disposed = false;
  const now = deps.now ?? (() => Date.now());
  const schedule = deps.schedule ?? ((callback, ms) => setTimeout(callback, ms));
  const cancel = deps.cancel ?? (timer => clearTimeout(timer as ReturnType<typeof setTimeout>));
  const visibleBefore = (app: number, deadline: number, menu: "main" | "quick") => new Promise<boolean>(resolve => {
    const timer = schedule(() => resolve(false), Math.max(0, deadline - now()));
    // A cold or closing browser can reject enumeration. The native recovery
    // still authenticates the game and its own surface before changing focus.
    void Promise.resolve().then(() => deps.visible(app, menu)).then(
      value => { cancel(timer); resolve(value === true); },
      () => { cancel(timer); resolve(false); });
  });
  return {
    guide(event: { eKey?: number; ePressed?: boolean; bPressed?: boolean; nControllerIndex?: number }) {
      if (disposed || (event?.eKey !== 0 && event?.eKey !== 1) || event.ePressed === false || event.bPressed === false
          || !Number.isInteger(event.nControllerIndex) || Number(event.nControllerIndex) < 0) return;
      const version = ++generation, app = deps.runningApp(), deadline = now() + 1500;
      const menu = event.eKey === 1 ? "quick" : "main";
      if (!Number.isInteger(app) || app < 0x80000000 || app > 0xffffffff) return;
      const cancelled = () => disposed || version !== generation || deps.runningApp() !== app;
      void (async () => {
        try {
          // Observe early opens too: closing a menu before the deadline must
          // never cause this request to bring it back.
          for (;;) {
            if (cancelled() || await visibleBefore(app, deadline, menu) || cancelled()) return;
            const remaining = deadline - now();
            if (remaining <= 0) break;
            await deps.delay(Math.min(100, remaining));
          }
          if (!cancelled()) await deps.recover(app, menu);
        } catch { /* Steam's own input handling remains available. */ }
      })();
    },
    invalidate() { generation++; },
    dispose() { disposed = true; generation++; },
  };
}

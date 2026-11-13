export type LivePerformanceOptions = {
  enabled: boolean; detail: 1 | 2 | 3 | 4; corner: 1 | 2 | 3 | 4 | 5 | 6;
  transparent: boolean; refreshMs: 20 | 50 | 100; textColor?: "white" | "yellow" | "red" | "green";
};
export type LivePerformanceState = LivePerformanceOptions & {
  status: string; appId: number; lease?: string | null; sequence?: number; sampleAgeMs?: number | null; suspended?: boolean;
};
export type LivePerformanceRequest = ({ action: "configure"; appId: number; lease?: string; suspended?: boolean } & LivePerformanceOptions)
  | { action: "renew"; lease: string; suspended?: boolean } | { action: "stop"; lease: string };
export type LivePerformanceResponse = { ok?: boolean; state?: unknown; error?: string };
export type LivePerformanceView = {
  state: LivePerformanceState | null; loading: boolean; busy: boolean; failed: boolean;
};
type Dependencies = {
  get(): Promise<unknown>;
  send(request: LivePerformanceRequest): Promise<LivePerformanceResponse>;
  app(): number;
  resolveApp?(): Promise<number>;
  suspended?(): boolean;
  schedule(callback: () => void, delay: number): unknown;
  cancel(timer: unknown): void;
  // The Steam counter is suspended only while this controller owns a live lease.
  suspendSteam?(): Promise<() => Promise<void>>;
};

const isApp = (value: unknown): value is number => typeof value === "number" && Number.isInteger(value) && value >= 0 && value <= 0xffffffff;
const isLease = (value: unknown): value is string => typeof value === "string" && /^[a-f0-9]{32}$/.test(value);
export function livePerformanceState(raw: unknown): LivePerformanceState | null {
  if (!raw || typeof raw !== "object") return null;
  const value = raw as LivePerformanceState;
  if (typeof value.enabled !== "boolean" || typeof value.transparent !== "boolean"
      || ![1, 2, 3, 4].includes(value.detail) || ![1, 2, 3, 4, 5, 6].includes(value.corner)
      || ![20, 50, 100].includes(value.refreshMs) || !isApp(value.appId)
      || typeof value.status !== "string" || value.status.length > 80
      || (value.textColor !== undefined && !["white", "yellow", "red", "green"].includes(value.textColor))
      || (value.lease != null && !isLease(value.lease))) return null;
  return { ...value, textColor: value.textColor ?? "white" };
}

export function createLivePerformanceController(deps: Dependencies) {
  let state: LivePerformanceState | null = null;
  let loading = false, busy = false, failed = false, disposed = false;
  let started = false, lease: string | undefined, timer: unknown;
  let restoreSteam: (() => Promise<void>) | undefined;
  let menuRevision = 0, menuFlight: Promise<void> | undefined;
  let queue: Promise<unknown> = Promise.resolve();
  const listeners = new Set<() => void>();
  const notify = () => { if (!disposed) listeners.forEach(listener => listener()); };
  const app = () => { try { const value = deps.app(); return isApp(value) ? value : 0; } catch { return 0; } };
  const resolvedApp = async () => {try {const value=deps.resolveApp?await deps.resolveApp():app();return isApp(value)?value:0;}catch{return 0;}};
  const options = (value: LivePerformanceOptions): LivePerformanceOptions => ({ enabled: value.enabled,
    detail: value.detail, corner: value.corner, transparent: value.transparent, refreshMs: 20, textColor: value.textColor ?? "white" });
  const matches = (a: LivePerformanceOptions, b: LivePerformanceOptions) =>
    Object.keys(options(a)).every(key => a[key as keyof LivePerformanceOptions] === b[key as keyof LivePerformanceOptions]);
  function serial<T>(work: () => Promise<T>): Promise<T> {
    const result = queue.catch(() => {}).then(work);
    queue = result.catch(() => {});
    return result;
  }
  function cancelTimer() { if (timer !== undefined) deps.cancel(timer); timer = undefined; }
  function next() {
    cancelTimer();
    if (!disposed && state?.enabled) timer = deps.schedule(() => { timer = undefined; void refresh(); }, 2000);
  }
  async function release(includeSteam = true) {
    const old = lease;
    lease = undefined;
    if (old) {
      try { await deps.send({ action: "stop", lease: old }); } catch { /* The native watchdog expires an unrenewed lease. */ }
    }
    if (includeSteam) {
      const restore = restoreSteam;
      restoreSteam = undefined;
      if (restore) await restore();
    }
  }
  async function accept(reply: LivePerformanceResponse, expected?: LivePerformanceOptions) {
    const actual = livePerformanceState(reply?.state);
    if (reply?.ok !== true || !actual || (expected && !matches(actual, expected))) {
      if (reply?.ok === true && actual && isLease(actual.lease) && (!lease || actual.lease === lease)) {
        lease = actual.lease;
        await release();
      }
      if (reply?.error === "ExpiredLease" || reply?.error === "InvalidLease") await release();
      throw new Error("Performance overlay state not confirmed");
    }
    if (actual.enabled && !isLease(actual.lease)) throw new Error("Performance overlay lease not confirmed");
    if (actual.enabled) lease = actual.lease!;
    state = actual;
    failed = false;
  }
  async function configure(value: LivePerformanceOptions, selected?: number) {
    const appId=selected??await resolvedApp();
    const reply = await deps.send({ action: "configure", appId, ...options(value),
      suspended: deps.suspended?.() ?? false, ...(lease ? { lease } : {}) });
    await accept(reply, options(value));
    if (disposed || !value.enabled) await release(disposed);
    else if (!restoreSteam && deps.suspendSteam) {
      try { restoreSteam = await deps.suspendSteam(); }
      catch { await release(); throw new Error("Steam monitor setting not confirmed"); }
    }
  }
  async function reconcile() {
    if (disposed) return;
    const selected=await resolvedApp();
    if(disposed)return;
    const actual = livePerformanceState(await deps.get());
    if (!actual) throw new Error("Performance overlay unavailable");
    state = actual;
    if (!state.enabled) { await release(false); return; }
    if (lease && state.lease === lease && state.appId === selected) {
      await accept(await deps.send({ action: "renew", lease, suspended: deps.suspended?.() ?? false }));
    } else if (lease && state.lease === lease) await configure(state,selected);
    else {
      // A replaced Steam context lets its old six-second lease expire. It never
      // adopts another context's lease and cannot renew a stale game session.
      if (lease) await release();
      if (!state.lease) await configure(state,selected);
    }
  }
  async function refresh() {
    return serial(async () => {
      if (disposed) return;
      loading = !state; notify();
      try { await reconcile(); failed = false; }
      catch { failed = true; }
      finally { loading = false; next(); notify(); }
    });
  }
  function menuChanged(): Promise<void> {
    // Opening Steam's normal overlay does not necessarily change the game's
    // foreground HWND. Send the current lease's visibility immediately, ahead
    // of slow status reads; native code remains the authority for this lease.
    ++menuRevision;
    if (menuFlight) return menuFlight;
    menuFlight = (async () => {
      while (!disposed && lease && state?.enabled) {
        const revision = menuRevision, owned = lease;
        const suspended = deps.suspended?.() ?? true;
        try {
          const reply = await deps.send({ action: "renew", lease: owned, suspended });
          if (disposed || lease !== owned) return;
          const actual = livePerformanceState(reply?.state);
          if (revision === menuRevision && reply?.ok === true && actual?.lease === owned
              && state?.appId === actual.appId && typeof actual.suspended === "boolean"
              && actual.suspended === (deps.suspended?.() ?? true)) {
            // This lightweight reply can arrive after a configure under the
            // same lease. It owns visibility only, never newer saved options.
            state = { ...state, suspended: actual.suspended,
              status: actual.suspended ? "Suspended" : state.suspended ? "NoData" : state.status };
            notify();
          }
        } catch { /* The regular bounded heartbeat reports transport failures. */ }
        if (revision === menuRevision) return;
      }
    })().finally(() => { menuFlight = undefined; });
    return menuFlight;
  }
  return {
    read(): LivePerformanceView { return { state, loading, busy, failed }; },
    subscribe(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener); }; },
    start() { if (disposed || started) return; started = true; void refresh(); },
    refresh,
    menuChanged,
    holdSteamMonitor() { return serial(async () => {
      if (!disposed && !restoreSteam && deps.suspendSteam) restoreSteam = await deps.suspendSteam();
    }); },
    restoreSteamMonitor() { return serial(() => release()); },
    change(changes: Partial<LivePerformanceOptions>) {
      return serial(async () => {
        if (disposed || !state) return false;
        busy = true; failed = false; cancelTimer(); notify();
        try {
          const desired = { ...options(state), ...changes };
          if (!livePerformanceState({ ...desired, status: "Pending", appId: app() })) throw new Error("Invalid overlay options");
          await configure(desired);
          return !disposed;
        } catch { failed = true; return false; }
        finally { busy = false; next(); notify(); }
      });
    },
    dispose() {
      if (disposed) return;
      disposed = true; cancelTimer(); listeners.clear();
      void serial(release).catch(() => {});
    },
  };
}

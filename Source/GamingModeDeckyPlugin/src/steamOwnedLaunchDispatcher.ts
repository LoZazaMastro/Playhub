// Routing receipts are not authorization. Native prepare/show independently
// reprove ownership, real foreground and exact process/session identities.
type Origin = "system" | "home" | "quick";
type Intent = "main" | "quick";
export interface OwnedLaunchRoute {
  uiObservedApp: number | null;
  canonicalShortcutAppId: number;
  originalGameId: string;
  observedSteamGameId: string | null;
  launchProvenance?: "steam-child" | "epic-protocol";
  gamePid: number; gameBirth: string;
  wrapperPid: number; wrapperBirth: string;
  steamPid: number; steamBirth: string;
  observedFresh: boolean;
  routingOnly: true;
}
export interface SteamGuideReceiver {
  main: object; browser: object; document: object;
  focused: boolean; windowFocus: boolean; activeMain: boolean;
  focusStore: object | undefined; focusApp: number | null; focusPid: number | null; focusedApp: number; focusedWindow: number;
}
// Read-only veto facts. They do not prove native foreground or grant authority.
export function readSteamGuideReceiver(main: any, active: unknown, focusStore: any): SteamGuideReceiver | undefined {
  try {
    const browser = main?.BrowserWindow ?? main?.m_BrowserWindow, doc = browser?.document;
    if (!main || !browser || browser.closed === true || !doc || typeof doc.hasFocus !== "function"
        || typeof doc.body?.classList?.contains !== "function") return;
    const focused = doc.hasFocus(), windowFocus = doc.body.classList.contains("WindowFocus");
    if (!focusStore) return;
    const focusedApp = focusStore.GetFocusedAppID?.() ?? focusStore.m_unFocusedAppID;
    const focusedWindow = focusStore.GetFocusedWindowID?.() ?? focusStore.m_nFocusedWindowID;
    const app = focusStore ? focusStore.GetFocusedOverlayAppID?.() ?? focusStore.m_unFocusedOverlayAppID : null;
    const pid = focusStore ? focusStore.GetFocusedOverlayPID?.() ?? focusStore.m_unFocusedOverlayPID : null;
    const known = (v: unknown) => typeof v === "number" && Number.isInteger(v) && v >= 0 && v <= 0xffffffff;
    if (typeof focused !== "boolean" || typeof windowFocus !== "boolean" || !known(focusedApp) || !known(focusedWindow) || !known(app) || !known(pid)
        || (main.BrowserWindow ?? main.m_BrowserWindow) !== browser || browser.document !== doc) return;
    return { main, browser, document: doc, focused, windowFocus, activeMain: active === main,
      focusStore, focusApp: app, focusPid: pid, focusedApp, focusedWindow };
  } catch { return; }
}
type ReceiverKind = "steam-ui" | "ordinary-game";
type Discovered = { route: OwnedLaunchRoute } | { receiver: ReceiverKind };
interface Scope {
  observedApp: number | null;
  revision: number;
  context: object | undefined;
  runningShortcuts: readonly number[];
  receiver?: SteamGuideReceiver;
  disposed: boolean;
  enabled: boolean;
}
interface Dependencies {
  scope(): Scope;
  now(): number;
  discover(observedApp: number | null, candidateApp?: number): Promise<{ ok: boolean; route?: unknown; kind?: "absent"; receiver?: ReceiverKind }>;
  signal(origin: Origin, menu: Intent, fallback?: () => void): boolean;
  schedule(callback: () => void, ms: number): unknown;
  cancel(timer: unknown): void;
  trace?(stage: string): void;
}
const app = (v: unknown): v is number => typeof v === "number" && Number.isInteger(v) && v > 0 && v <= 0xffffffff;
const canonical = (v: unknown): v is number => app(v) && v >= 0x80000000;
const uint64 = (v: unknown): v is string => typeof v === "string" && /^[1-9]\d{0,19}$/.test(v) && BigInt(v) <= 0xffffffffffffffffn;
const observed = (v: string) => BigInt(v) <= 0xffffffffn ? Number(v) : Number(BigInt(v) >> 32n);
export function parseOwnedLaunchRoute(value: unknown, observedApp: number | null): OwnedLaunchRoute | undefined {
  if (!value || typeof value !== "object") return;
  const r = value as OwnedLaunchRoute;
  if (!canonical(r.canonicalShortcutAppId) || r.routingOnly !== true || typeof r.observedFresh !== "boolean"
    || ![r.gamePid, r.wrapperPid, r.steamPid].every(app) || new Set([r.gamePid, r.wrapperPid, r.steamPid]).size !== 3
    || ![r.originalGameId, r.gameBirth, r.wrapperBirth, r.steamBirth].every(uint64)
    || BigInt(r.originalGameId) !== (BigInt(r.canonicalShortcutAppId) << 32n | 0x02000000n)
    || r.uiObservedApp !== observedApp
    || (r.observedSteamGameId === null
      ? r.launchProvenance !== "epic-protocol" || r.observedFresh !== false || (observedApp !== null && observedApp !== 0 && observedApp !== r.canonicalShortcutAppId)
      : !uint64(r.observedSteamGameId) || (observedApp !== null && observedApp !== 0 && observed(r.observedSteamGameId) !== observedApp))
    || (r.launchProvenance !== undefined && r.launchProvenance !== "steam-child" && r.launchProvenance !== "epic-protocol")
    || BigInt(r.steamBirth) > BigInt(r.wrapperBirth) || BigInt(r.wrapperBirth) > BigInt(r.gameBirth)) return;
  return Object.freeze({ ...r });
}

// One lookup can serve delayed Home/System/Quick observers, but each edge still
// enters the existing common Guide admission and its unchanged coalescing rules.
export function createOwnedLaunchDispatcher(deps: Dependencies) {
  let disposed = false, version = 0;
  let launch: { canonical: number; at: number } | undefined;
  let nativeLaunch: number | undefined;
  let cache: { scope: Scope; route: OwnedLaunchRoute; version: number } | undefined;
  let pending: { scope: Scope; version: number; lookup: Promise<Discovered | undefined>; stop?: () => void } | undefined;
  const trace = (s: string) => { try { deps.trace?.(s); } catch {} };
  const scope = () => { try { const s = deps.scope(); return { ...s, runningShortcuts: [...s.runningShortcuts], receiver: s.receiver ? { ...s.receiver } : undefined }; } catch { return; } };
  const same = (a: Scope, b: Scope) => a.context === b.context && a.revision === b.revision && a.observedApp === b.observedApp
    && !b.disposed && b.enabled && Boolean(b.context);
  const sameReceiver = (a: Scope, b: Scope, kind: ReceiverKind) => {
    const first = a.receiver, last = b.receiver;
    return Boolean(first && last && first.main === last.main && first.browser === last.browser && first.document === last.document
      && first.focused === last.focused && first.windowFocus === last.windowFocus && first.activeMain === last.activeMain
      && first.focusStore === last.focusStore && first.focusApp === last.focusApp && first.focusPid === last.focusPid
      && first.focusedApp === last.focusedApp && first.focusedWindow === last.focusedWindow
      && (kind !== "steam-ui" || (last.focused && last.windowFocus && last.activeMain)));
  };
  const activeCandidate = (s: Scope) => {
    if (nativeLaunch === s.observedApp) return false;
    const running = s.runningShortcuts.filter(canonical);
    if (running.length > 8) return undefined; // Candidate overflow is unknown, never ordinary-game proof.
    let now: number;
    try { now = deps.now(); } catch { return undefined; }
    if (!Number.isFinite(now) || now < 0) return undefined;
    if (launch && now < launch.at) return undefined;
    return running.length > 0 || Boolean(launch && Number.isFinite(launch.at));
  };
  const cached = (s: Scope) => cache?.version === version && same(cache.scope, s) && activeCandidate(s) ? cache.route : undefined;
  const current = (s: Scope, v: number) => { const n = scope(); return !disposed && v === version && n && same(s, n) && activeCandidate(n); };
  const lookup = (s: Scope) => {
    const existing = cached(s);
    if (existing && s.observedApp !== null && s.observedApp !== 0) return Promise.resolve<Discovered>({ route: existing });
    if (pending && pending.version === version && same(pending.scope, s)) return pending.lookup;
    const v = version;
    const request: { scope: Scope; version: number; lookup: Promise<Discovered | undefined>; stop?: () => void } = {
      scope: s, version: v, lookup: Promise.resolve(undefined),
    };
    request.lookup = (async () => {
      try {
        if (!current(s, v)) return;
        const reply = await new Promise<{ ok: boolean; route?: unknown; kind?: "absent"; receiver?: ReceiverKind }>((resolve) => {
          let finished = false;
          let timer: unknown;
          const finish = (reply: { ok: boolean; route?: unknown; kind?: "absent"; receiver?: ReceiverKind }) => {
            if (finished) return; finished = true;
            if (timer !== undefined) deps.cancel(timer);
            resolve(reply);
          };
          request.stop = () => finish({ ok: false });
          timer = deps.schedule(() => finish({ ok: false }), 1800);
          if (finished && timer !== undefined) deps.cancel(timer);
          if (finished || !current(s, v)) { finish({ ok: false }); return; }
          try { Promise.resolve(deps.discover(s.observedApp, launch?.canonical ?? (s.runningShortcuts.length === 1 ? s.runningShortcuts[0] : undefined))).then(finish, () => finish({ ok: false })); }
          catch { finish({ ok: false }); }
        });
        if (!current(s, v)) return;
        if (reply?.ok !== true) { cache = undefined; return; }
        if (reply.kind === "absent") {
          cache = undefined;
          if (reply.receiver !== "steam-ui" && reply.receiver !== "ordinary-game") return;
          return { receiver: reply.receiver } as Discovered;
        }
        const route = parseOwnedLaunchRoute(reply.route, s.observedApp);
        if (!route || !current(s, v)) return;
        // A client launch hint is not proof; a conflicting receipt cannot
        // silently select another candidate. Reload may use running shortcuts.
        if (launch && route.canonicalShortcutAppId !== launch.canonical
            && !s.runningShortcuts.includes(route.canonicalShortcutAppId)) return;
        cache = { scope: s, route, version: v };
        trace("owned-route-cached"); return { route } as Discovered;
      } catch { trace("owned-route-unknown"); }
      finally { if (pending === request) pending = undefined; }
    })();
    pending = request;
    return request.lookup;
  };
  return {
    app(): number {
      const s = scope();
      if (!s || disposed || s.disposed) return 0;
      return canonical(s.observedApp) ? s.observedApp : cached(s)?.canonicalShortcutAppId ?? s.observedApp ?? 0;
    },
    async resolveApp(): Promise<number> {
      const s=scope();
      if(!s||disposed||s.disposed||!s.enabled)return 0;
      if(canonical(s.observedApp))return s.observedApp;
      const candidate=activeCandidate(s);
      if(candidate===undefined)return 0;
      if(!candidate)return s.observedApp??0;
      if(!s.context||((s.observedApp===null||s.observedApp===0)&&!launch&&s.runningShortcuts.length!==1))return 0;
      const v=version,result=await lookup(s);
      if(!current(s,v)||!result)return 0;
      if("route" in result)return cached(s)===result.route?result.route.canonicalShortcutAppId:0;
      return result.receiver==="ordinary-game"?s.observedApp??0:0;
    },
    expected(canonicalApp: number): OwnedLaunchRoute | undefined {
      const s = scope(); if (!s || canonical(s.observedApp)) return;
      const r = cached(s); return r?.canonicalShortcutAppId === canonicalApp ? r : undefined;
    },
    noteLaunch(gameId: unknown) {
      ++version; cache = undefined; pending?.stop?.(); pending = undefined; launch = undefined; nativeLaunch = undefined;
      try {
        if (typeof gameId !== "string" || !uint64(gameId)) return;
        const id = BigInt(gameId), candidate = Number(id >> 32n);
        if ((id & 0xffffffffn) === 0x02000000n && canonical(candidate)) launch = { canonical: candidate, at: deps.now() };
        else if (id <= 0xffffffffn) nativeLaunch = Number(id);
      } catch { /* A malformed hint grants no discovery eligibility. */ }
    },
    signal(origin: Origin, menu: Intent, fallback?: () => void): boolean {
      const s = scope();
      if (!s) return true; // Unknown is never permission to call native Home.
      if (disposed || s.disposed || !s.enabled) return false;
      if (s.observedApp !== null && s.observedApp !== 0 && !app(s.observedApp)) return true;
      if (canonical(s.observedApp)) return deps.signal(origin, menu, fallback); // no discovery/RPC
      const candidate = activeCandidate(s);
      if (candidate === undefined) return true;
      if (!candidate || !s.context) return false;
      if ((s.observedApp === null || s.observedApp === 0) && !launch && s.runningShortcuts.length !== 1) return true;
      if (cached(s) && s.observedApp !== null && s.observedApp !== 0) return deps.signal(origin, menu, fallback);
      const v = version;
      void lookup(s).then(result => {
        if (!result || !current(s, v)) return;
        if ("receiver" in result) {
          const latest = scope();
          if (!latest || !current(s, v) || !sameReceiver(s, latest, result.receiver)) return;
          try { fallback?.(); } catch {} return;
        }
        if (cached(s) === result.route) deps.signal(origin, menu, fallback);
      });
      return true;
    },
    invalidate() { ++version; cache = undefined; pending?.stop?.(); pending = undefined; },
    dispose() { disposed = true; ++version; cache = undefined; pending?.stop?.(); pending = undefined; launch = undefined; },
  };
}

// Shared routing only for existing game consumers. Every native operation still
// authenticates its own retained process/window; this never grants input/focus.
// Display-only evidence from the already authenticated original native owner.
// active=true records a real callback; no value here is native Closed or input authority.
export interface OwnedNativeOverlayVisibility {
  app:number;entry:any;pid:number;browserId:number;nativeApp:number;rendererToken:string;active:boolean;serial:string;
}
type OwnedGameConsumer={app():number;expected(app:number):OwnedLaunchRoute|undefined;resolveApp():Promise<number>;
  nativeOverlay?(app:number):OwnedNativeOverlayVisibility|undefined};
let gameConsumer:OwnedGameConsumer|undefined;
export function registerOwnedGameConsumer(value:OwnedGameConsumer):()=>void {
  gameConsumer=value;return()=>{if(gameConsumer===value)gameConsumer=undefined;};
}
export function readOwnedNativeOverlay(selected:number):OwnedNativeOverlayVisibility|undefined {
  const owner=gameConsumer;if(!owner||!canonical(selected))return;
  try {
    if(owner.app()!==selected)return;
    const value=owner.nativeOverlay?.(selected);
    if(!value||value.app!==selected||!value.entry||![value.pid,value.browserId,value.nativeApp].every(app)
      ||!uint64(value.rendererToken)||typeof value.active!=="boolean"||!/^\d{1,20}$/.test(value.serial))return;
    const copy=Object.freeze({...value});
    return gameConsumer===owner&&owner.app()===selected&&gameConsumer===owner?copy:undefined;
  } catch{return;}
}
export function readOwnedGameRoute(selected:number):OwnedLaunchRoute|undefined {
  const owner=gameConsumer;if(!owner)return;
  try {
    if(owner.app()!==selected)return;
    const route=owner.expected(selected);
    if(!route||!parseOwnedLaunchRoute(route,route.uiObservedApp)||route.canonicalShortcutAppId!==selected)return;
    return gameConsumer===owner&&owner.app()===selected&&owner.expected(selected)===route?route:undefined;
  } catch{return;}
}
export function readOwnedGameApp(raw:number):number {
  const owner=gameConsumer;if(!owner)return raw;
  try {
    const selected=owner.app();if(selected===raw)return selected;
    const route=readOwnedGameRoute(selected);
    if(!route||gameConsumer!==owner)return raw;
    return raw===0||raw===route.canonicalShortcutAppId||route.observedSteamGameId!==null&&observed(route.observedSteamGameId)===raw?selected:raw;
  } catch{return raw;}
}
export function resolveOwnedGameApp(raw:number):number|Promise<number> {
  const owner=gameConsumer;if(!owner)return raw;
  try {return owner.resolveApp().then(selected=>gameConsumer===owner&&selected>0&&readOwnedGameApp(raw)===selected?selected:0,()=>0);}catch{return 0;}
}

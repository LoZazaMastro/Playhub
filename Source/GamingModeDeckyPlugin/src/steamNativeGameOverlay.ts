// GPL-3.0-only. Original game-host overlay; this grants no mirror scope.
import type { OwnedNativeOverlayVisibility } from "./steamOwnedLaunchDispatcher";
export type NativeMenu = "main" | "quick";
export interface NativeGameProof {
  protocol: 1; canonicalAppId: number; fullGameId: string; observedGameId: string; sessionId: string;
  gamePid: number; gameBirth: string; wrapperPid: number; wrapperBirth: string;
  steamPid: number; steamBirth: string; companionPid: number; companionBirth: string; overlayPid: number; overlayBirth: string;
  gameWindow: string; observedAt: string; expiresAt: string; routingOnly: true;
}
export interface NativeGameReply { ok: boolean; kind?: "native-game" | "absent" | "packaged"; nativeGame?: NativeGameProof }
export interface NativeBrowser { overlayPid: number; browserId: number; appId: number }
export interface NativeCallback extends NativeBrowser { active: boolean; serial: string }
export interface NativeInputReply {
  ok: boolean; inputMode?: "owned-relay" | "ordinary" | "ordinary-menu"; inputReady?: boolean; ordinaryReady?: boolean; noOwnedOverride?: boolean; menuLayoutReady?: boolean;
  closed?: boolean; aborted?: boolean; cleanupComplete?: boolean; resumeAllowed?: boolean;
  ownerToken?: string; generation?: string; nativeGame?: NativeGameProof; proofProducedAfterMs?: number | null;
}
export interface NativeInputRequest {
  appId: number; action: "prepare" | "active" | "closed" | "cancel" | "renew" | "abort"; expectedNative: NativeGameProof;
  browser?: NativeBrowser; ownerToken?: string; generation?: string; callback?: NativeCallback;
}
interface Scope { app: number; observedApp?: number | null; revision: number; disposed: boolean }
interface Dependencies {
  scope(): Scope; now(): number; proof(app: number): Promise<NativeGameReply>;
  input(request: NativeInputRequest): Promise<NativeInputReply>; browserInfo(): Promise<unknown>;
  windowStore(): any; setOverlayState(token: string, state: 0 | 2): Promise<unknown>;
  schedule(callback: () => void, ms: number): unknown; cancel(timer: unknown): void;
  admitted?(app: number, proof: NativeGameProof, browser: NativeBrowser): void;
  coordinateOriginalMenu?(target: any, current: () => boolean): (() => void) | undefined;
  coordinateOriginalClose?(fullGameId: string, rendererToken: string, current: () => boolean, target?: any, cleanupCurrent?: () => boolean): (() => void) | undefined;
  trace?(stage: string): void;
}
const uint = (value: unknown): value is number => Number.isInteger(value) && Number(value) > 0 && Number(value) <= 0xffffffff;
const decimal = (value: unknown): value is string => typeof value === "string" && /^[1-9]\d{0,19}$/.test(value) && BigInt(value) <= 0xffffffffffffffffn;
const hex = (value: unknown): value is string => typeof value === "string" && /^[a-f0-9]{64}$/i.test(value);
const token = (value: unknown): value is string => typeof value === "string" && /^[a-f0-9]{32}$/.test(value);
const identity = ["canonicalAppId", "fullGameId", "observedGameId", "sessionId", "gamePid", "gameBirth", "wrapperPid", "wrapperBirth", "steamPid", "steamBirth", "companionPid", "companionBirth", "overlayPid", "overlayBirth", "gameWindow"] as const;
export const sameNativeGame = (a: NativeGameProof, b: NativeGameProof) => identity.every(key => a[key] === b[key]);
export function validNativeGame(p: NativeGameProof | undefined, app: number, elapsed = 0): p is NativeGameProof {
  return !!p && p.protocol === 1 && p.routingOnly === true && p.canonicalAppId === app && uint(app) && app >= 0x80000000
    && decimal(p.fullGameId) && p.fullGameId === ((BigInt(app) << 32n) | 0x02000000n).toString()
    && decimal(p.observedGameId) && hex(p.sessionId)
    && [p.gamePid, p.wrapperPid, p.steamPid, p.companionPid, p.overlayPid].every(uint)
    && p.gamePid !== p.wrapperPid && p.overlayPid !== p.gamePid && p.overlayPid !== p.companionPid
    && [p.gameBirth, p.wrapperBirth, p.steamBirth, p.companionBirth, p.overlayBirth, p.gameWindow, p.observedAt, p.expiresAt].every(decimal)
    && Number.isFinite(elapsed) && elapsed >= 0 && BigInt(p.expiresAt) > BigInt(p.observedAt)
    && BigInt(p.expiresAt) - BigInt(p.observedAt) <= 1500n
    && BigInt(p.expiresAt) - BigInt(p.observedAt) > BigInt(Math.ceil(elapsed) + 50);
}
export function bindNativeGameBrowser(proof: NativeGameProof, infos: unknown, store: any) {
  if (!Array.isArray(infos)) return undefined;
  const matches = infos.filter(value => value?.unPID === proof.overlayPid
    && (String(value.gameID) === proof.observedGameId
      || proof.observedGameId === proof.fullGameId && String(value.gameID) === String(proof.canonicalAppId))
    && uint(value.nBrowserID) && uint(value.appID));
  if (matches.length !== 1) return undefined;
  const info = matches[0], rendererToken = String(info.gameID), browserId = info.nBrowserID as number, appId = info.appID as number;
  const target = store?.m_mapOverlayPopupByPID?.get(proof.overlayPid);
  const win = target?.BrowserWindow ?? target?.m_BrowserWindow, doc = win?.document, menu = target?.MenuStore;
  if (!target || !win || !doc || !menu || typeof menu.OpenMainMenu !== "function"
      || typeof menu.OpenQuickAccessMenu !== "function" || typeof menu.GetQuickAccessTab !== "function") return undefined;
  const current = () => store?.m_mapOverlayPopupByPID?.get(proof.overlayPid) === target && Array.isArray(store?.OverlayWindows)
    && store.OverlayWindows.includes(target) && target.IsGamepadUIOverlayWindow?.() === true
    && (target.BrowserWindow ?? target.m_BrowserWindow) === win && win.closed !== true && win.document === doc && target.MenuStore === menu
    && target.params?.browserInfo?.m_unPID === proof.overlayPid && target.params.browserInfo.m_nBrowserID === browserId
    && target.params.browserInfo.m_unAppID === appId && String(target.params.browserInfo.m_gameID) === rendererToken;
  if (!current()) return undefined;
  return { browser: { overlayPid: proof.overlayPid, browserId, appId }, rendererToken, target, win, doc, menu, store, current };
}
// Coordinate only a synchronous original native-window system-key consumer.
// The registered Playhub SystemKey observer owns the same press; no time-based
// deduplication, native callback, input event or focus state is fabricated here.
export function coordinateNativeOriginalMenus(target: any, current: () => boolean, observerCurrent: () => boolean, trace?: (stage: string) => void): (() => void) | undefined {
  let enabled = true, depth = 0;
  const installed: { object: any; key: string; original: PropertyDescriptor | undefined; wrapper: Function }[] = [];
  const stop = () => {
    enabled = false;
    for (const hook of installed.splice(0).reverse()) try {
      if (Object.getOwnPropertyDescriptor(hook.object, hook.key)?.value !== hook.wrapper) continue;
      if (hook.original) Object.defineProperty(hook.object, hook.key, hook.original);
      else Reflect.deleteProperty(hook.object, hook.key);
    } catch { /* A retained wrapper is passive after enabled=false. */ }
  };
  try {
    const context = target?.GetFocusNavContext?.(), nav = context?.m_controller, sources = nav?.m_rgGamepadInputSources;
    if (!context || !nav || !Array.isArray(sources)) return;
    const matches = sources.filter(source => source?.m_NavController === nav
      && typeof source.OnSystemButtonPress === "function" && typeof source.HandleSystemKeyEvents === "function");
    if (matches.length !== 1 || typeof target.OnHomeButtonPressed !== "function" || typeof target.OnQuickAccessButtonPressed !== "function") return;
    const source = matches[0];
    const live = () => {
      try {
        if (!enabled || !observerCurrent() || !current()) return false;
        const next = target.GetFocusNavContext?.();
        return next === context && next.m_controller === nav && nav.m_rgGamepadInputSources === sources && sources.includes(source)
          && source.m_NavController === nav && installed.every(hook => Object.getOwnPropertyDescriptor(hook.object, hook.key)?.value === hook.wrapper)
          && observerCurrent() && current() && enabled;
      } catch { return false; }
    };
    const install = (object: any, key: string, make: (original: Function) => Function) => {
      const descriptor = Object.getOwnPropertyDescriptor(object, key), original = object[key];
      if (typeof original !== "function" || descriptor && !("value" in descriptor)) throw new Error("Original menu accessor unavailable");
      const wrapper = make(original);
      Object.defineProperty(object, key, { configurable: true, writable: true, enumerable: descriptor?.enumerable ?? false, value: wrapper });
      installed.push({ object, key, original: descriptor, wrapper });
    };
    install(source, "OnSystemButtonPress", original => function (this: any, ...args: any[]) {
      if (this !== source) return Reflect.apply(original, this, args);
      depth++;
      try { return Reflect.apply(original, this, args); } finally { depth--; }
    });
    for (const [key, menu] of [["OnHomeButtonPressed", "main"], ["OnQuickAccessButtonPressed", "quick"]] as const) {
      install(target, key, original => function (this: any, ...args: any[]) {
        if (this === target && depth > 0 && live()) {
          try { trace?.(`coordinated-original-system-menu intent=${menu}`); } catch {}
          return; // The original SystemKey source and Playhub observer still execute.
        }
        return Reflect.apply(original, this, args);
      });
    }
    if (!live()) { stop(); return; }
    return stop;
  } catch { stop(); return; }
}

// Lower only the original Hidden request for this authenticated native session.
// Opens, foreign calls and stale grants keep the original receiver/arguments.
export function coordinateNativeOriginalClose(api: any, fullGameId: string, rendererToken: string, current: () => boolean,
  target?: any, cleanupCurrent?: () => boolean): (() => void) | undefined {
  if (!decimal(fullGameId) || !decimal(rendererToken) || fullGameId === rendererToken || !api) return;
  let enabled = true, closing = false;
  let descriptor: PropertyDescriptor | undefined, original: Function, wrapper: Function;
  let nav: any, cancelOriginal: Function | undefined, cancelWrapper: Function | undefined, cancelDescriptor: PropertyDescriptor | undefined;
  let cancelFrame: { nativeAttempted: boolean; cleanup: () => boolean } | undefined;
  const stop = () => {
    enabled = false;
    try {
      if (nav && Object.getOwnPropertyDescriptor(nav, "OnButtonActionInternal")?.value === cancelWrapper) {
        if (cancelDescriptor) Object.defineProperty(nav, "OnButtonActionInternal", cancelDescriptor);
        else Reflect.deleteProperty(nav, "OnButtonActionInternal");
      }
    } catch { /* A retained wrapper is passive after enabled=false. */ }
    try {
      if (Object.getOwnPropertyDescriptor(api, "SetOverlayState")?.value !== wrapper) return;
      if (descriptor) Object.defineProperty(api, "SetOverlayState", descriptor);
      else Reflect.deleteProperty(api, "SetOverlayState");
    } catch { /* A retained wrapper is passive after enabled=false. */ }
  };
  try {
    descriptor = Object.getOwnPropertyDescriptor(api, "SetOverlayState");
    if (descriptor && !("value" in descriptor)) return;
    original = api.SetOverlayState;
    if (typeof original !== "function") return;
    wrapper = function (this: any, ...args: any[]) {
      if (!enabled) return Reflect.apply(original, this, args);
      let forwarded = args; const frame = cancelFrame;
      if (this === api && args.length === 2 && args[0] === fullGameId && args[1] === 0) try {
        if (Object.getOwnPropertyDescriptor(api, "SetOverlayState")?.value === wrapper && (current() || frame && frame === cancelFrame && frame.cleanup())
            && Object.getOwnPropertyDescriptor(api, "SetOverlayState")?.value === wrapper && enabled) forwarded = [rendererToken, args[1]];
      } catch { /* Unknown ownership delegates unchanged; never invent Closed. */ }
      if (frame && frame === cancelFrame && this === api && forwarded.length === 2
          && forwarded[1] === 0 && forwarded[0] === rendererToken) frame.nativeAttempted = true;
      return Reflect.apply(original, this, forwarded);
    };
    Object.defineProperty(api, "SetOverlayState", descriptor ? { ...descriptor, value: wrapper }
      : { configurable: true, writable: true, enumerable: false, value: wrapper });
    if (target && cleanupCurrent) {
      const context = target.GetFocusNavContext?.(), root = target.BrowserWindow ?? target.m_BrowserWindow, menu = target.MenuStore;
      nav = context?.m_controller;
      const bound = () => enabled && target.GetFocusNavContext?.() === context && context?.m_controller === nav
        && context.RootWindow === root && (target.BrowserWindow ?? target.m_BrowserWindow) === root && target.MenuStore === menu
        && Object.getOwnPropertyDescriptor(api, "SetOverlayState")?.value === wrapper
        && Object.getOwnPropertyDescriptor(nav, "OnButtonActionInternal")?.value === cancelWrapper;
      const cleanup = () => bound() && cleanupCurrent() && bound() && enabled && !closing;
      cancelDescriptor = nav && Object.getOwnPropertyDescriptor(nav, "OnButtonActionInternal");
      if (nav && (!cancelDescriptor || "value" in cancelDescriptor) && typeof nav.OnButtonActionInternal === "function"
          && typeof menu?.GetOpenSideMenu === "function" && typeof nav.GetActiveContext === "function") {
        cancelOriginal = nav.OnButtonActionInternal;
        cancelWrapper = function (this: any, ...args: any[]) {
          let frame: { nativeAttempted: boolean; cleanup: () => boolean } | undefined;
          try {
            // Installed Steam pR.CANCEL=2; source 1=gamepad, 2=keyboard simulator.
            const tree = context?.m_LastActiveNavTree, child = tree?.Window, doc = child?.document;
            // OnButtonDown is captured by Steam's input-source registration. Its
            // original body calls this method dynamically on the same manager.
            if (this === nav && args[0] === true && args[1] === 2 && (args[2] === 1 || args[2] === 2)
                && args[4] !== true && args[5] === undefined && args[6] === undefined
                && current() && cleanup() && nav.GetActiveContext() === context && context.BIsGamepadInputSuppressed?.() === false
                && (tree?.id === "MainNavMenuContainer" || tree?.id === "QuickAccess-NA") && tree.WindowContext === context
                && context.ActiveWindow === child && tree.Root?.Element?.ownerDocument === doc && doc?.hasFocus?.() === true
                && menu.GetOpenSideMenu() === (tree.id === "MainNavMenuContainer" ? 1 : 2) && cleanup()
                && context.m_LastActiveNavTree === tree && context.ActiveWindow === child && nav.GetActiveContext() === context) frame = { nativeAttempted: false, cleanup };
          } catch { /* An unknown receiver delegates without a native effect. */ }
          const previous = cancelFrame;
          if (frame) cancelFrame = frame;
          let result: any;
          try { result = Reflect.apply(cancelOriginal!, this, args); }
          finally { cancelFrame = previous; }
          if (frame && !frame.nativeAttempted) try {
            // Original B/Escape already hid this exact menu. Supply only the
            // missing native Hidden dispatch; actual FALSE still owns cleanup.
            if (menu.GetOpenSideMenu() === 0 && cleanup()) {
              closing = true;
              const sent = Reflect.apply(original, api, [rendererToken, 0]);
              void Promise.resolve(sent).catch(() => {});
            }
          } catch { /* Invoked outcome is unknown; never repeat or invent FALSE. */ }
          return result;
        };
        Object.defineProperty(nav, "OnButtonActionInternal", cancelDescriptor ? { ...cancelDescriptor, value: cancelWrapper }
          : { configurable: true, writable: true, enumerable: false, value: cancelWrapper });
      }
    }
    return stop;
  } catch { stop(); return; }
}

type Binding = NonNullable<ReturnType<typeof bindNativeGameBrowser>>;
// Authenticated producer work ends BEFORE the final routing-proof Read.
// Network transit and the complete final Read remain in this upper age bound.
export function nativeInputProofAgeUpper(reply: NativeInputReply, elapsed: number): number | undefined {
  if (!Number.isFinite(elapsed) || elapsed < 0 || !Number.isSafeInteger(Math.ceil(elapsed))) return;
  const duration = reply?.proofProducedAfterMs;
  if (duration === undefined || duration === null) return Math.ceil(elapsed); // Legacy remains conservative.
  if (!Number.isSafeInteger(duration) || duration < 0 || duration > elapsed) return;
  return Math.ceil(elapsed) - duration;
}
export const readyForNativeMenu = (reply: NativeInputReply) => reply?.ok === true
  && (reply.inputMode === "owned-relay" && reply.inputReady === true && reply.ordinaryReady !== true
    || reply.inputMode === "ordinary" && reply.ordinaryReady === true && reply.noOwnedOverride === true && reply.inputReady !== true
    || reply.inputMode === "ordinary-menu" && reply.ordinaryReady === true && reply.inputReady === false
      && reply.noOwnedOverride === false && reply.menuLayoutReady === true);
type Owner = { app: number; revision: number; proof: NativeGameProof; at: number; binding: Binding; token?: string; generation?: string;
  inputMode?: NativeInputReply["inputMode"];
  readyGranted: boolean; dispatched: boolean; trueSeen: boolean; nativeActive?: boolean; closed: boolean; closing: boolean; zeroRequested: boolean;
  idleInvalidated: boolean; callbackSerial: bigint; lastTrue?: NativeCallback; eventSerial: Promise<unknown>; cancelSent: boolean; cancelConfirmed?: boolean; renewTimer?: unknown; renewInFlight: boolean; preReadyLast?: NativeCallback; preReadyCleanup: boolean; queuedPreReadyCallback: bigint; releaseOriginalMenu?: () => void; releaseOriginalClose?: () => void };
export function createSteamNativeGameOverlay(deps: Dependencies) {
  let disposed = false, version = 0, wanted = false, intent: NativeMenu = "main", intentApp = 0, opening = 0;
  let owner: Owner | undefined, serial: Promise<unknown> = Promise.resolve();
  const trace = (stage: string) => { try { deps.trace?.(stage); } catch {} };
  // Diagnostics read only JSON own data descriptors, never invoke DTO getters.
  const ownData = (value: unknown, key: string): unknown => value && typeof value === "object"
    ? Object.getOwnPropertyDescriptor(value, key)?.value : undefined;
  const scalar = (value: unknown) => typeof value === "boolean" || typeof value === "number" ? String(value)
    : typeof value === "string" && value.length <= 64 ? value : "unknown";
  const scope = () => { try { return deps.scope(); } catch { return undefined; } };
  const current = (app: number, revision: number, v: number) => {
    const s = scope(); return !disposed && !s?.disposed && s?.app === app && s.revision === revision && version === v && wanted && intentApp === app;
  };
  const uiCurrent = (o: Owner) => {
    const b = o.binding, sameStore = deps.windowStore() === b.store, sameBrowser = b.current(), now = deps.now(), s = scope();
    const contextCurrent = sameStore && sameBrowser && !disposed && !s?.disposed && s?.app === o.app && s.revision === o.revision;
    const proofCurrent = contextCurrent && validNativeGame(o.proof, o.app, now - o.at);
    const okay = proofCurrent && owner === o;
    if (!okay) trace(`ui-current-refused store=${sameStore} browser=${sameBrowser} context=${contextCurrent} proof=${proofCurrent} own=${owner === o}`);
    return okay;
  };
  const cleanupBinding = (o: Owner) => {
    // A retained original callback is cleanup evidence after expiry, never a renewed UI/input grant.
    const sameStore = deps.windowStore() === o.binding.store, sameBrowser = o.binding.current();
    return sameStore && sameBrowser && owner === o;
  };
  const input = (o: Owner, action: NativeInputRequest["action"], callback?: NativeCallback) => deps.input({
    appId: o.app, action, expectedNative: o.proof, browser: o.binding.browser, ownerToken: o.token, generation: o.generation, callback,
  });
  const stopRenew = (o: Owner) => { if (o.renewTimer !== undefined) { deps.cancel(o.renewTimer); o.renewTimer = undefined; } };
  const cancel = async (o: Owner) => {
    stopRenew(o);
    if (!o.token || o.cancelSent) return; o.cancelSent = true;
    // Cancel revokes this request; it NEVER fabricates a native false/resume.
    try {
      const result = await input(o, "cancel");
      o.cancelConfirmed = result?.ok === true && result.ownerToken === o.token && result.generation === o.generation
        && !!result.nativeGame && sameNativeGame(o.proof, result.nativeGame);
    } catch { trace("cancel-uncertain"); }
  };
  const confirmActualClosed = (o: Owner, callback: NativeCallback, result: NativeInputReply) => {
    // A newer actual TRUE during the0ACK await owns newer cleanup debt.
    if (o.callbackSerial !== BigInt(callback.serial) || result?.ok !== true || result.closed !== true || result.cleanupComplete !== true) return;
    o.closed = true;
    if (owner === o) { owner = undefined; wanted = false; version++; }
    trace("closed-native-confirmed");
  };
  const flushPreReadyCallback = (o: Owner) => {
    const callback = o.preReadyLast;
    if (!o.token || !callback || BigInt(callback.serial) <= o.queuedPreReadyCallback) return;
    o.queuedPreReadyCallback = BigInt(callback.serial);
    o.eventSerial = o.eventSerial.catch(() => {}).then(async () => {
      if (o.closed || o.callbackSerial !== BigInt(callback.serial)) return;
      if (!o.cancelConfirmed) { trace("deferred-callback-cancel-unconfirmed"); return; }
      // Actual TRUE is cleanup evidence on the already-revoked bridge, never
      // a readiness/renew grant. A superseded FALSE cannot retire this owner.
      const result = await input(o, callback.active ? "active" : "closed", callback);
      if (!callback.active) confirmActualClosed(o, callback, result);
    }).catch(() => trace("deferred-callback-uncertain"));
  };
  const abortBeforeDispatch = async (o: Owner) => {
    if (o.dispatched || o.trueSeen || !o.token || !o.generation) return false;
    try {
      const reply = await input(o, "abort");
      return !o.dispatched && !o.trueSeen && reply?.ok === true && reply.aborted === true && reply.closed !== true
        && reply.cleanupComplete === true && reply.ownerToken === o.token && reply.generation === o.generation
        && !!reply.nativeGame && sameNativeGame(o.proof, reply.nativeGame);
    } catch { trace("abort-before-dispatch-uncertain"); return false; }
  };
  const releaseOriginalMenu = (o: Owner) => {
    const close = o.releaseOriginalClose, menu = o.releaseOriginalMenu;
    o.releaseOriginalClose = undefined; o.releaseOriginalMenu = undefined;
    try { close?.(); } catch {} try { menu?.(); } catch {}
  };
  const retire = async (o: Owner, closeOnly = false) => {
    o.closing = true; releaseOriginalMenu(o);
    stopRenew(o);
    if (!o.dispatched && await abortBeforeDispatch(o)) {
      if (owner === o) owner = undefined;
      trace("aborted-before-dispatch-cleanup-confirmed"); // Local retirement, never native FALSE/Closed or a resume claim.
      return;
    }
    if (o.readyGranted && !o.dispatched) await cancel(o);
    if ((o.readyGranted || o.dispatched) && !o.closed && !o.zeroRequested && !uiCurrent(o) && cleanupBinding(o)) {
      // A long-open UI may outlive its routing proof. Refresh the SAME tuple before Set0; no new input grant.
      const started = deps.now(), s = scope();
      if (s?.app === o.app && s.revision === o.revision && !s.disposed && !disposed) try {
        const fresh = await deps.proof(o.app);
        if (fresh?.ok && fresh.kind === "native-game" && validNativeGame(fresh.nativeGame, o.app, deps.now() - started)
            && sameNativeGame(o.proof, fresh.nativeGame) && owner === o) { o.proof = fresh.nativeGame; o.at = started; }
      } catch { trace("close-proof-unknown"); }
    }
    if ((o.readyGranted || o.dispatched) && !o.closed && !o.zeroRequested && uiCurrent(o)) {
      // Set0 is only the exact original renderer token while its original game is current.
      o.zeroRequested = true;
      try { await deps.setOverlayState(o.binding.rendererToken, 0); } catch { trace("close-uncertain"); }
      if (closeOnly) return; // Keep callback binding until the actual false/0-ACK, even on an uncertain Set0.
    }
    await cancel(o);
    if (o.readyGranted || o.dispatched) return; // Keep cleanup debt through exact false/0-ACK.
    if (owner === o) owner = undefined;
  };
  const scheduleRenew = (o: Owner) => {
    if (o.renewTimer !== undefined || o.renewInFlight || owner !== o || disposed || o.closing || o.cancelSent || o.closed || !o.trueSeen) return;
    o.renewTimer = deps.schedule(() => {
      o.renewTimer = undefined;
      if (owner !== o || disposed || o.closing || o.cancelSent || o.closed || !o.trueSeen) return;
      o.renewInFlight = true;
      void (async () => {
        const started = deps.now(), reply = await deps.proof(o.app);
        if (owner !== o || disposed || o.closing || o.cancelSent || o.closed) return;
        if (!reply?.ok || reply.kind !== "native-game" || !validNativeGame(reply.nativeGame, o.app, deps.now() - started)
            || !sameNativeGame(o.proof, reply.nativeGame) || !cleanupBinding(o)) throw new Error("Native renewal ownership unavailable");
        o.proof = reply.nativeGame; o.at = started;
        if (!uiCurrent(o)) throw new Error("Native renewal no longer current");
        const renewed = await input(o, "renew", o.lastTrue);
        if (owner !== o || disposed || o.closing || o.cancelSent || o.closed) return;
        if (!readyForNativeMenu(renewed) || renewed.inputMode !== o.inputMode) throw new Error("Native renewal refused");
      })().catch(async () => { trace("renew-uncertain"); await retire(o); }).finally(() => {
        o.renewInFlight = false;
        scheduleRenew(o); // One actual settlement, one next timer; never a queued RPC backlog.
      });
    }, 400);
  };
  const menu = (o: Owner, v: number) => {
    if (!current(o.app, o.revision, v) || !uiCurrent(o)) return false;
    if (intent === "quick") {
      const tab = o.binding.menu.GetQuickAccessTab();
      if (!current(o.app, o.revision, v) || !uiCurrent(o)) return false;
      o.binding.menu.OpenQuickAccessMenu(tab, true);
    } else o.binding.menu.OpenMainMenu();
    return current(o.app, o.revision, v) && uiCurrent(o);
  };
  const open = async (app: number, revision: number, v: number, initial?: { reply: NativeGameReply; at: number }) => {
    let held: Owner | undefined, stage = "proof";
    const start = initial?.at ?? deps.now();
    try {
      const reply = initial?.reply ?? await deps.proof(app);
      if (!current(app, revision, v)) { trace("open-refused stage=post-proof-intent"); return { handled: true }; }
      if (reply?.ok === true && (reply.kind === "absent" || reply.kind === "packaged") && !reply.nativeGame) return { handled: false };
      if (reply?.ok !== true || reply.kind !== "native-game" || !validNativeGame(reply.nativeGame, app, deps.now() - start)) { trace("open-refused stage=proof-reply"); return { handled: true }; }
      stage = "browser-info";
      const proof = reply.nativeGame, infos = await deps.browserInfo();
      if (!current(app, revision, v) || !validNativeGame(proof, app, deps.now() - start)) { trace("open-refused stage=post-browser-intent-proof"); return { handled: true }; }
      const binding = bindNativeGameBrowser(proof, infos, deps.windowStore());
      if (!binding) { trace("open-refused stage=original-browser-binding"); return { handled: true }; }
      held = { app, revision, proof, at: start, binding, readyGranted: false, dispatched: false, trueSeen: false, closed: false,
        closing: false, zeroRequested: false, idleInvalidated: false, callbackSerial: 0n, eventSerial: Promise.resolve(), cancelSent: false, renewInFlight: false, preReadyCleanup: false, queuedPreReadyCallback: 0n };
      owner = held;
      const readyAt = deps.now();
      stage = "input-prepare";
      const ready = await input(held, "prepare");
      const readyProof = ownData(ready, "nativeGame");
      trace(`prepare-receipt mode=${scalar(ownData(ready, "inputMode"))} ok=${scalar(ownData(ready, "ok"))} inputReady=${scalar(ownData(ready, "inputReady"))} ordinaryReady=${scalar(ownData(ready, "ordinaryReady"))} noOwnedOverride=${scalar(ownData(ready, "noOwnedOverride"))} menuLayoutReady=${scalar(ownData(ready, "menuLayoutReady"))} producerMs=${scalar(ownData(ready, "proofProducedAfterMs"))} observed=${scalar(ownData(readyProof, "observedAt"))} expires=${scalar(ownData(readyProof, "expiresAt"))}`);
      const readyElapsed = deps.now() - readyAt, readyAge = nativeInputProofAgeUpper(ready, readyElapsed);
      // Adopt even a late receipt so cancellation can retire its exact owned request.
      if (token(ready?.ownerToken) && decimal(ready?.generation)) { held.token = ready.ownerToken; held.generation = ready.generation; }
      held.readyGranted = readyForNativeMenu(ready) && !!held.token && !!held.generation
        && !!ready.nativeGame && sameNativeGame(proof, ready.nativeGame);
      if (held.preReadyLast) {
        // These are actual original-host events observed before receipt adoption.
        // Never draw. Revoke first, then forward only the still-latest actual
        // callback with its original serial as cleanup-only provenance.
        held.preReadyCleanup = true; held.closing = true;
        held.eventSerial = held.eventSerial.catch(() => {}).then(() => cancel(held!));
        flushPreReadyCallback(held);
        await held.eventSerial;
        return { handled: true };
      }
      if (!readyForNativeMenu(ready) || !held.token || !held.generation || readyAge === undefined || readyElapsed >= 4950 || !validNativeGame(ready.nativeGame, app, readyAge)
          || !sameNativeGame(proof, ready.nativeGame)) { trace(`open-refused stage=input-ready-receipt retainedReady=${held.readyGranted}`); return { handled: true }; }
      held.inputMode = ready.inputMode;
      held.proof = ready.nativeGame; held.at = readyAt + (Math.ceil(readyElapsed) - readyAge);
      if (!current(app, revision, v) || !uiCurrent(held)) { trace("open-refused stage=post-ready-ui"); return { handled: true }; }
      const originalOwner = held;
      held.releaseOriginalMenu = deps.coordinateOriginalMenu?.(binding.target, () => owner === originalOwner
        && originalOwner.readyGranted && !originalOwner.closed && !originalOwner.closing && !originalOwner.cancelSent && uiCurrent(originalOwner));
      held.releaseOriginalClose = deps.coordinateOriginalClose?.(held.proof.fullGameId, binding.rendererToken, () => {
        if (owner !== originalOwner || !wanted || intentApp !== originalOwner.app || !originalOwner.readyGranted || !originalOwner.dispatched
            || !originalOwner.trueSeen || originalOwner.closed || originalOwner.closing || originalOwner.cancelSent || !uiCurrent(originalOwner)) return false;
        // uiCurrent contains browser/scope getters: recheck local revocation last.
        return owner === originalOwner && wanted && intentApp === originalOwner.app && originalOwner.readyGranted && originalOwner.dispatched
          && originalOwner.trueSeen && !originalOwner.closed && !originalOwner.closing && !originalOwner.cancelSent && !disposed;
      }, binding.target, () => {
        if (owner !== originalOwner || !originalOwner.readyGranted || !originalOwner.dispatched || !originalOwner.trueSeen
            || originalOwner.closed || originalOwner.closing || originalOwner.cancelSent || disposed || !cleanupBinding(originalOwner)) return false;
        return owner === originalOwner && originalOwner.readyGranted && originalOwner.dispatched && originalOwner.trueSeen
          && !originalOwner.closed && !originalOwner.closing && !originalOwner.cancelSent && !disposed;
      });
      if (deps.coordinateOriginalClose && held.proof.fullGameId !== binding.rendererToken && !held.releaseOriginalClose) {
        trace("open-refused stage=original-close-routing"); return { handled: true };
      }
      if (!current(app, revision, v) || !uiCurrent(held)) { trace("open-refused stage=post-menu-lane-ui"); return { handled: true }; }
      deps.admitted?.(app, held.proof, binding.browser); // Cleanup receipt only, not Mirror.Shown or a scope grant.
      if (!current(app, revision, v) || !uiCurrent(held)) { trace("open-refused stage=post-admitted-ui"); return { handled: true }; }
      held.dispatched = true; // Any throw after this point is an uncertain dispatch, never Closed.
      stage = "set2";
      await deps.setOverlayState(held.binding.rendererToken, 2);
      trace(`set2-returned app=${app} host=${held.proof.overlayPid} browser=${binding.browser.browserId}`);
      stage = "menu";
      if (!menu(held, v)) { trace("open-refused stage=post-set2-menu"); return { handled: true }; }
      trace(`opened app=${app} host=${held.proof.overlayPid} browser=${binding.browser.browserId}`);
      return { handled: true };
    } catch { trace(`open-unknown stage=${stage}`); return { handled: true }; }
    finally {
      if (held && (!current(app, revision, v) || !held.dispatched)) await retire(held);
      if (opening === app) opening = 0;
      if (!owner && version === v) wanted = false;
    }
  };
  const queue = (operation: () => Promise<unknown>) => { serial = serial.catch(() => {}).then(operation); return serial; };
  return {
    guide(next: NativeMenu): Promise<unknown> {
      const s = scope();
      if (disposed || !s || s.disposed || !uint(s.app) || s.app < 0x80000000) return Promise.resolve({ handled: true });
      const same = wanted && intentApp === s.app;
      if (same && intent === next) { wanted = false; version++; return queue(async () => { if (owner) await retire(owner, true); return { handled: true }; }); }
      if (same && opening === s.app) { intent = next; return serial; }
      intent = next; intentApp = s.app; wanted = true; const v = ++version;
      return queue(async () => {
        if (!current(s.app, s.revision, v)) return { handled: true };
        if (owner?.closing) {
          const old = owner;
          if (!old.idleInvalidated) return { handled: true }; // Unknown close is never absence.
          const at = deps.now(), reply = await deps.proof(s.app), fresh = reply?.nativeGame;
          if (!current(s.app, s.revision, v) || owner !== old || reply?.ok !== true || reply.kind !== "native-game"
              || !validNativeGame(fresh, s.app, deps.now() - at) || fresh.sessionId === old.proof.sessionId
              || fresh.gamePid === old.proof.gamePid && fresh.gameBirth === old.proof.gameBirth
              || fresh.wrapperPid === old.proof.wrapperPid && fresh.wrapperBirth === old.proof.wrapperBirth) return { handled: true };
          // Positive new retained session after observed app0 permits LOCAL forget only.
          // No Set0/Closed/resume is sent for either session; the new server input receipt stands alone.
          stopRenew(old); releaseOriginalMenu(old); owner = undefined;
          opening = s.app; return open(s.app, s.revision, v, { reply, at });
        }
        if (owner && owner.app === s.app && uiCurrent(owner) && !owner.closed) {
          menu(owner, v); return { handled: true };
        }
        if (owner) await retire(owner);
        if (!current(s.app, s.revision, v)) return { handled: true };
        opening = s.app; return open(s.app, s.revision, v);
      });
    },
    activated(pid: number, appId: number, active: boolean) {
      const o = owner;
      if (!o || typeof active !== "boolean" || pid !== o.binding.browser.overlayPid || appId !== o.binding.browser.appId) return;
      // Native callback has no BID/birth: bind it to this retained browser/session. A missing UI is not Closed.
      if (!cleanupBinding(o)) { trace("callback-owner-unknown"); return; }
      o.nativeActive = active;
      if (o.preReadyCleanup || !o.dispatched && !o.readyGranted) {
        if (active) { o.trueSeen = true; trace("pre-ready-native-true-cleanup-only"); }
        if (o.trueSeen) {
          o.preReadyLast = { ...o.binding.browser, active, serial: (++o.callbackSerial).toString() };
          if (o.preReadyCleanup) flushPreReadyCallback(o);
        }
        return; // Actual cleanup provenance only; no active/renew/Set2 grant.
      }
      // The server's sticky revoke/expiry fence may record a late TRUE for cleanup,
      // but must never renew 769/readiness from this callback intent.
      if (active) o.trueSeen = true;
      else if (!o.trueSeen && !o.cancelSent) return;
      if (!active) { o.closing = true; stopRenew(o); releaseOriginalMenu(o); }
      const callback = { ...o.binding.browser, active, serial: (++o.callbackSerial).toString() };
      if (active) o.lastTrue = callback;
      o.eventSerial = o.eventSerial.catch(() => {}).then(async () => {
        if (o.closed) return;
        const result = await input(o, active ? "active" : "closed", callback);
        if (active && readyForNativeMenu(result) && result.inputMode === o.inputMode) scheduleRenew(o);
        if (!active) confirmActualClosed(o, callback, result);
      }).catch(() => trace("callback-uncertain"));
    },
    invalidate() {
      if (owner && scope()?.observedApp === 0) owner.idleInvalidated = true;
      wanted = false; version++; return queue(async () => { if (owner) await retire(owner); });
    },
    dispose() { disposed = true; wanted = false; version++; return queue(async () => { if (owner) await retire(owner); }); },
    visibility(app: number): OwnedNativeOverlayVisibility | undefined {
      const o=owner;
      if(disposed||!o||o.app!==app||!o.readyGranted||!o.dispatched||o.closed||!cleanupBinding(o))return;
      const serial=o.callbackSerial,nativeActive=o.nativeActive;
      const copy=Object.freeze({app:o.app,entry:o.binding.target,pid:o.binding.browser.overlayPid,browserId:o.binding.browser.browserId,
        nativeApp:o.binding.browser.appId,rendererToken:o.binding.rendererToken,active:nativeActive===true,serial:serial.toString()});
      // This copy can only hide the HUD. Expiry/cancel is not a native FALSE,
      // so keep the exact binding until the real callback or owner retirement.
      return !disposed&&owner===o&&!o.closed&&o.readyGranted&&o.dispatched
        &&o.callbackSerial===serial&&o.nativeActive===nativeActive?copy:undefined;
    },
    get active() { return !!owner && !owner.closed; }, get pendingApp() { return opening; },
    owns(app: number) { return owner?.app === app || opening === app; },
    recover(next: NativeMenu) { return wanted && intentApp === scope()?.app ? serial : this.guide(next); },
    settled() { return serial; }, eventsSettled() { return owner?.eventSerial ?? Promise.resolve(); },
  };
}

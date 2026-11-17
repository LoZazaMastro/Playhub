import { findOverlayWindow, selectOverlay, type OverlayIdentity } from "./dashboardOverlay";
import { steamMirrorNavigation, steamNativeVisibleNavigation, type NativeVisibleNavigation, type MirrorNavigation } from "./steamMirrorNavigation";

export interface SteamNativeVisible {
 protocol: 1; phase: 1 | 2; sessionId: string; generation: string; gameId: string; expiresAt: string;
 observedAt: string; readAt: string; sourceHwnd: string; sourceCookie: string; ownerPid: number; ownerBirth: string; gamePid: number; gameBirth: string;
 physicalObservedAt: string; physicalExpiresAt: string; guideEpoch: string; receiptFrame: string; sourceFrame: string;
 valveOpen: boolean; ownCleanupComplete: true; inputAuthority: false; visualAuthority: false;
 baselineSamples: number; baselineMatched: number; outputSamples: number; outputMatched: number;
}
const number64 = (v: unknown, zero = false): v is string => typeof v === "string" && (zero ? /^(0|[1-9]\d{0,19})$/ : /^[1-9]\d{0,19}$/).test(v) && BigInt(v) <= 0xffffffffffffffffn;
const nativePid = (v: unknown): v is number => typeof v === "number" && Number.isInteger(v) && v > 0 && v <= 0xffffffff;
const count = (v: unknown): v is number => typeof v === "number" && Number.isInteger(v) && v >= 0 && v <= 2304;

// This produces NO MirrorPresentation, input proof, navigation lease or Shown.
export function nativeVisibleRecord(reply: MirrorReply, parent: MirrorPresentation, phase: 1 | 2 = 1): SteamNativeVisible | undefined {
 const n = reply?.nativeVisible;
 if (reply?.ok !== true || !n || n.protocol !== 1 || n.phase !== phase || !/^[0-9a-f]{64}$/.test(n.sessionId)
   || !nativePid(n.ownerPid) || !nativePid(n.gamePid) || reply.companionPid !== n.ownerPid || !nativePid(reply.overlayPid) || reply.gameId !== n.gameId
   || n.inputAuthority !== false || n.visualAuthority !== false || n.ownCleanupComplete !== true || typeof n.valveOpen !== "boolean") return;
 for (const k of ["generation","gameId","expiresAt","observedAt","readAt","sourceHwnd","sourceCookie","ownerBirth","gameBirth","guideEpoch"] as const) if (!number64(n[k])) return;
 for (const k of ["physicalObservedAt","physicalExpiresAt","receiptFrame","sourceFrame"] as const) if (!number64(n[k], true)) return;
 for (const k of ["sessionId","generation","gameId","sourceHwnd","sourceCookie","ownerPid","ownerBirth","gamePid","gameBirth"] as const) if (n[k] !== parent[k]) return;
 const seen = BigInt(n.observedAt), read = BigInt(n.readAt);
 if (seen > read || read - seen > 250n) return;
 if (phase === 2) {
  if (n.physicalObservedAt !== "0" || n.physicalExpiresAt !== "0") return;
 } else {
  const physical = BigInt(n.physicalObservedAt), end = BigInt(n.physicalExpiresAt), lease = BigInt(n.expiresAt);
  if (!n.valveOpen || physical <= 0n || physical > seen || read - physical > 250n || end <= read || end - physical > 250n || end > lease
   || lease <= read || lease - read > 5000n || BigInt(n.sourceFrame) <= BigInt(n.receiptFrame)) return;
  if (![n.baselineSamples,n.baselineMatched,n.outputSamples,n.outputMatched].every(count)
   || n.baselineSamples < 16 || n.outputSamples < 16 || n.baselineMatched > n.baselineSamples || n.outputMatched > n.outputSamples
   || n.baselineMatched * 100 < n.baselineSamples * 95 || n.outputMatched * 100 < n.outputSamples * 95) return;
 }
 return Object.freeze({ ...n });
}


export interface NativeVisibleInputStatus {
 protocol:1;state:"neutralizing"|"menu"|"returning"|"closed"|"uncertain";sessionId:string;generation:string;gameId:string;guideEpoch:string;
 ownerPid:number;ownerBirth:string;gamePid:number;gameBirth:string;sourceHwnd:string;sourceCookie:string;observedAt:string;readAt:string;expiresAt:string;
 neutralHeld:boolean;menuLayoutReady:boolean;inputReady:boolean;automaticSettled:boolean;sourceHidden:boolean;
}
export function nativeInputReady(reply:MirrorReply,n:SteamNativeVisible):NativeVisibleInputStatus|undefined {
 const i=reply.nativeVisibleInput;
 if(!i||i.protocol!==1||i.state!=="menu"||i.inputReady!==true||i.menuLayoutReady!==true||i.neutralHeld!==true||i.sourceHidden!==false||i.automaticSettled!==false)return;
 for(const k of ["sessionId","generation","gameId","guideEpoch","ownerPid","ownerBirth","gamePid","gameBirth","sourceHwnd","sourceCookie"] as const)if(i[k]!==n[k])return;
 if(![i.observedAt,i.readAt,i.expiresAt].every(v=>number64(v)))return;
 const seen=BigInt(i.observedAt),read=BigInt(i.readAt),expiry=BigInt(i.expiresAt);
 if(seen>read||read-seen>250n||read>BigInt(n.readAt)||BigInt(n.readAt)-seen>250n||expiry<=BigInt(n.readAt)||expiry>BigInt(n.physicalExpiresAt)||expiry-seen>250n)return;
 return Object.freeze({...i});
}

export interface MirrorPresentation {
  protocol: 1;
  state: "preparing" | "prepared" | "shown" | "cleanup-pending" | "closed";
  sessionId: string;
  generation: string;
  gameId: string;
  expiresAt: string;
  observedAt: string;
  readAt?: string;
  sourceHwnd: string;
  sourceCookie: string;
  destinationHwnd: string;
  destinationCookie: string;
  ownerPid: number;
  ownerBirth: string;
  gamePid: number;
  gameBirth: string;
  preparedCurrent: boolean;
  shownCurrent: boolean;
  leaseCurrent: boolean;
  cleanupComplete: boolean;
  actualScope: number;
  ownedForegroundPhase?: 0 | 1 | 2;
}

export interface MirrorContinuation {
 protocol:1;phase:1|2|3|4;sessionId:string;guideEpoch:string;parentGeneration:string;resumedGeneration:string;gapDeadline:string;observedAt:string;readAt:string;
 ownerPid:number;gamePid:number;ownerBirth:string;gameBirth:string;gameId:string;sourceHwnd:string;sourceCookie:string;ticketLow:string;ticketHigh:string;
 inputAuthority:false;visualAuthority:false;cleanupComplete:boolean;originalLeaseExpiry:string;preparationExpiresAt:string;
}
export interface MirrorReply {
  ok: boolean;
  gameId?: string;
  companionPid?: number;
  overlayPid?: number;
  mirror?: MirrorPresentation;
  continuation?: MirrorContinuation;
  nativeVisible?: SteamNativeVisible; nativeVisibleInput?: NativeVisibleInputStatus;
  // Read-only authenticated session discovery, not presentation authority.
  mirrorSessionId?: string;
  error?: string;
}

interface Dependencies {
  runningApp(): number;
  prepare(app: number): Promise<MirrorReply>;
  read(app: number, overlayPid: number): Promise<MirrorReply>;
  command(app: number, overlayPid: number, action: "show" | "close", expected: MirrorPresentation): Promise<{ ok: boolean }>;
  closeNativeVisible?(app: number, overlayPid: number, expected: SteamNativeVisible): Promise<{ ok: boolean }>;
  browserInfo(): Promise<unknown>;
  windows(): unknown;
  navigation?(): any;
  setOverlayState(gameId: string, state: number): Promise<unknown>;
  delay(ms: number): Promise<void>;
  now(): number;
  // Routing evidence only, emitted after a real native generation receipt.
  admitted?(app: number, presentation: MirrorPresentation): void;
  trace?(stage: string): void;
}

type MenuReceipt = { browser: OverlayIdentity; window: any; menu: any; parent: any; document: any; side: 1 | 2 };
type Owner = { nativeRevocationRequested?:boolean; nativeVisible?: SteamNativeVisible; nativeSourceSeen?:SteamNativeVisible; nativeNavigation?:NativeVisibleNavigation; nativeNavigationIntent?:"main"|"quick"; nativeNavigationAttempted?:boolean; nativeNavigationConfirmed?:boolean; nativeInputReadySeen?:boolean; nativeInputUntil?:number; nativeLocalUntil?: number; continuation?: { marker:MirrorContinuation; parent:MirrorPresentation; localDeadline:number }; menuReceipt?: MenuReceipt; nativeCloseRequired?: boolean; closeStateAttempted?: boolean; app: number; overlayPid: number; mirror: MirrorPresentation; browser?: OverlayIdentity; window?: any; menu?: any; navigation?: MirrorNavigation };
const uint64 = (value: unknown): value is string => typeof value === "string" && /^[1-9]\d{0,19}$/.test(value)
  && BigInt(value) <= 0xffffffffffffffffn;
const pid = (value: unknown): value is number => typeof value === "number" && Number.isInteger(value) && value > 0 && value <= 0xffffffff;
const identityKeys = ["sessionId", "generation", "gameId", "sourceHwnd", "sourceCookie", "destinationHwnd", "destinationCookie",
  "ownerPid", "ownerBirth", "gamePid", "gameBirth"] as const;

function identifiedMirror(reply: MirrorReply): MirrorPresentation | undefined {
  const value = reply?.mirror;
  if (!reply?.ok || !value || value.protocol !== 1 || !/^[0-9a-f]{64}$/i.test(value.sessionId)
      || !pid(reply.overlayPid) || !pid(reply.companionPid) || reply.gameId !== value.gameId
      || value.ownerPid !== reply.companionPid || !pid(value.gamePid)
      || !identityKeys.filter(key => !["sessionId", "ownerPid", "gamePid"].includes(key)).every(key => uint64(value[key]))
      || !uint64(value.expiresAt) || !uint64(value.observedAt)) return;
  return Object.freeze({ ...value });
}

export function currentMirror(reply: MirrorReply, state?: "prepared" | "shown"): MirrorPresentation | undefined {
  const value = identifiedMirror(reply);
  if (!value || BigInt(value.expiresAt) <= BigInt(value.observedAt)
      || value.leaseCurrent !== true || value.preparedCurrent !== true
      || (value.actualScope !== 1 && !(value.actualScope === 3 && value.ownedForegroundPhase === 1 && value.state === "shown" && value.shownCurrent === true))
      || (value.state !== "prepared" && value.state !== "shown")
      || (state && value.state !== state) || (value.state === "shown" && value.shownCurrent !== true)) return;
  return Object.freeze({ ...value });
}

function same(a: MirrorPresentation, b: MirrorPresentation) {
  return identityKeys.every(key => a[key] === b[key]);
}
function sameRetired(a: MirrorPresentation, b: MirrorPresentation) {
  // DestroyOwned clears the destination HWND/cookie. The retained source,
  // generation and process identities still identify this terminal snapshot.
  return a.state === "closed" && a.leaseCurrent === false && a.cleanupComplete === true
    && identityKeys.filter(key => key !== "destinationHwnd" && key !== "destinationCookie").every(key => a[key] === b[key])
    && (a.destinationHwnd === "0" || a.destinationHwnd === b.destinationHwnd)
    && (a.destinationCookie === "0" || a.destinationCookie === b.destinationCookie);
}

// Read-only lineage. This function NEVER returns a current mirror or input proof.
export function mirrorContinuation(reply:MirrorReply,parent:MirrorPresentation):MirrorContinuation|undefined {
 const c=reply?.continuation;if(!reply.ok||!c||c.protocol!==1||![1,2,3,4].includes(c.phase)||c.inputAuthority!==false||c.visualAuthority!==false||typeof c.cleanupComplete!=="boolean")return;
 if(reply.companionPid!==parent.ownerPid||reply.gameId!==parent.gameId||c.ownerPid!==parent.ownerPid||c.ownerBirth!==parent.ownerBirth||c.gamePid!==parent.gamePid||c.gameBirth!==parent.gameBirth||c.gameId!==parent.gameId||c.sessionId!==parent.sessionId||c.sourceHwnd!==parent.sourceHwnd||c.sourceCookie!==parent.sourceCookie||c.parentGeneration!==parent.generation)return;
 for(const k of ["guideEpoch","parentGeneration","gapDeadline","observedAt","readAt","ownerBirth","gameBirth","gameId","sourceHwnd","sourceCookie","originalLeaseExpiry"] as const)if(!uint64(c[k]))return;
 const canonicalZero=(v:string)=>v==="0"||uint64(v);
 if(!canonicalZero(c.ticketLow)||!canonicalZero(c.ticketHigh)||(c.ticketLow==="0"&&c.ticketHigh==="0")||!canonicalZero(c.resumedGeneration)||!canonicalZero(c.preparationExpiresAt))return;
 const seen=BigInt(c.observedAt),read=BigInt(c.readAt),gap=BigInt(c.gapDeadline),oldEnd=BigInt(c.originalLeaseExpiry);
 if(read<seen||read-seen>250n||gap>oldEnd)return;
 if(c.phase===1&&(c.resumedGeneration!=="0"||c.preparationExpiresAt!=="0"||gap<=read||gap-read>500n))return;
 if((c.phase===2||c.phase===4)&&(!uint64(c.resumedGeneration)||BigInt(c.resumedGeneration)<=BigInt(c.parentGeneration)||!uint64(c.preparationExpiresAt)||BigInt(c.preparationExpiresAt)<=read||BigInt(c.preparationExpiresAt)-read>5000n))return;
 if(c.phase===3&&c.resumedGeneration!=="0"&&BigInt(c.resumedGeneration)<=BigInt(c.parentGeneration))return;
 return Object.freeze({...c});
}
// Composition belongs to this serial queue; the native worker owns controller
// layout commands. Native scope 3 identifies its confirmed owned overlay handoff.
export function createSteamMirrorOverlay(deps: Dependencies) {
  let disposed = false, revision = 0, wanted = false, openingVersion = 0, openingApp = 0;
  let intent: "main" | "quick" = "main";
  let owner: Owner | undefined;
  let serial: Promise<unknown> = Promise.resolve();
  const trace = (stage: string) => { try { deps.trace?.(stage); } catch {} };
  const enqueue = (work: () => Promise<void>) => {
    serial = serial.then(work).catch(() => { trace("mirror-operation-failed"); });
    return serial;
  };
  const live = (app: number, version: number) => !disposed && revision === version && deps.runningApp() === app;
  const wantedFor = (app: number) => wanted && (owner?.app === app
    || openingVersion !== 0 && openingApp === app);
  function captureMenu(value: Owner, side: 1 | 2): MenuReceipt | undefined {
    if (!value.browser || !value.window || !value.menu) return;
    const parent = value.window.BrowserWindow ?? value.window.m_BrowserWindow, document = parent?.document;
    if (!document || parent.closed === true) return;
    return { browser: value.browser, window: value.window, menu: value.menu, parent, document, side };
  }
  function closeOwnedMenu(value: Owner, version: number): boolean {
    const held = value.menuReceipt;
    if (!held) return true; // No original menu was mounted by this owner.
    const local = () => owner === value && value.menuReceipt === held && revision === version;
    const bound = () => local() && value.browser === held.browser && value.window === held.window && value.menu === held.menu
      && held.window.MenuStore === held.menu && (held.window.BrowserWindow ?? held.window.m_BrowserWindow) === held.parent
      && held.parent.closed !== true && held.parent.document === held.document && local();
    const current = () => local() && deps.runningApp() === value.app
      && findOverlayWindow(deps.windows(), held.browser) === held.window && bound();
    // Replacement/user navigation is not ours to close, even after native cleanup.
    if (!current()) return true;
    const side = held.menu.GetOpenSideMenu?.();
    if (!local()) return true;
    if (side !== held.side) {
      if (side === 0 || side === 1 || side === 2) { value.menuReceipt = undefined; return true; }
      return false;
    }
    const close = held.menu.CloseSideMenus;
    if (typeof close !== "function") return false;
    if (!current()) return true;
    // Finish provider calls before the last side read. Only the retained local
    // tuple/document/revision barrier follows it before invoking the original.
    const finalSide = held.menu.GetOpenSideMenu?.();
    if (!bound()) return true;
    if (finalSide !== held.side) return finalSide === 0 || finalSide === 1 || finalSide === 2;
    try { Reflect.apply(close, held.menu, []); } catch { /* Readback decides cleanup, not the call's return/throw. */ }
    if (!current()) return true;
    const after = held.menu.GetOpenSideMenu?.();
    if (!local()) return true;
    if (after !== 0) return false;
    value.menuReceipt = undefined; trace("mirror-owned-menu-closed"); return true;
  }
  function noteContinuation(value:Owner,reply:MirrorReply,started:number) {
    const parent=value.continuation?.parent??value.mirror,c=mirrorContinuation(reply,parent);
    if(!c||c.phase===3||!value.menuReceipt||reply.overlayPid!==value.overlayPid){value.continuation=undefined;return;}
    const old=value.continuation?.marker;
    if(old&&(c.ticketLow!==old.ticketLow||c.ticketHigh!==old.ticketHigh||c.guideEpoch!==old.guideEpoch))return;
    const remaining=Number(BigInt(c.phase===1?c.gapDeadline:c.preparationExpiresAt)-BigInt(c.readAt));
    const limit=started+remaining;
    value.continuation={marker:c,parent,localDeadline:old?.phase===c.phase?Math.min(value.continuation!.localDeadline,limit):limit};
    return c;
  }
  function passiveCurrent(value:Owner) {return owner===value&&wanted&&value.continuation?.marker.phase===1&&deps.now()<value.continuation.localDeadline;}
  async function reread(value: Owner, state?: "prepared" | "shown") {
    const version=revision,started=deps.now(),reply = await deps.read(value.app, value.overlayPid);
    noteContinuation(value,reply,started);
    if(adoptNative(value,reply,started,version))return;
    const proof = currentMirror(reply, state);
    return proof && reply.overlayPid === value.overlayPid && same(proof, value.mirror) ? proof : undefined;
  }
  function replacementSession(reply: MirrorReply, value: Owner, app = value.app) {
    const session = reply?.mirrorSessionId;
    return pid(app) && app >= 0x80000000 && typeof session === "string" && /^[0-9a-f]{64}$/.test(session)
      && session !== value.mirror.sessionId.toLowerCase()
      && reply.gameId === ((BigInt(app) << 32n) | 0x02000000n).toString() && pid(reply.companionPid)
      && (pid(reply.overlayPid) && (reply.ok === true && reply.mirror?.sessionId === session
          || reply.ok === false && reply.error === "mirror-not-prepared" && reply.mirror == null)
        || reply.ok === false && reply.error === "mirror-session-discovered" && reply.overlayPid === 0
          && reply.mirror == null && reply.nativeVisible == null && reply.nativeVisibleInput == null && reply.continuation == null);
  }
  async function releaseReplacedOwner(value: Owner, observed?: MirrorReply) {
    if (owner !== value) return true;
    const app = deps.runningApp();
    if (!pid(app) || app < 0x80000000) return false;
    const prior = app === value.app ? observed : undefined;
    if (prior?.ok === true && !replacementSession(prior, value, app)) return false;
    const version = revision;
    // A missing old host is unknown, not proof of retirement. Discovery must
    // authenticate a different wrapper/game/companion session (including birth).
    const reply = prior && replacementSession(prior, value, app) ? prior : await deps.read(app, 0);
    if (owner !== value) return true;
    if (revision !== version || deps.runningApp() !== app || !replacementSession(reply, value, app)) return false;
    owner = undefined;
    trace("mirror-session-replaced");
    // Drop only this local receipt. Never send old Close/Set0 to the new session.
    return true;
  }
  function nativeUiCurrent(value: Owner, version: number): boolean {
    const held=value.menuReceipt, browser=value.browser;
    return owner===value && live(value.app,version) && Boolean(browser && value.window && value.menu && held
      && findOverlayWindow(deps.windows(),browser)===value.window && value.window.MenuStore===value.menu
      && held.menu===value.menu && held.window===value.window && held.parent?.closed!==true && held.parent?.document===held.document
      && (value.window.BrowserWindow??value.window.m_BrowserWindow)===held.parent)
      && owner===value && live(value.app,version);
  }
  function adoptNative(value:Owner,reply:MirrorReply,started:number,version:number):boolean {
    const n=nativeVisibleRecord(reply,value.mirror),now=deps.now();
    if(!deps.closeNativeVisible || reply.overlayPid!==value.overlayPid || !n || value.nativeVisible && value.nativeVisible.guideEpoch!==n.guideEpoch || !nativeUiCurrent(value,version) || !Number.isFinite(started) || !Number.isFinite(now) || now<started)return false;
    // Remember the separately authenticated source mode for cleanup only.
    // A slow RPC grants no local display/navigation/input freshness, and its
    // already-closed own destination can never prove this source is hidden.
    value.nativeSourceSeen=n;
    const remaining=Number(BigInt(n.physicalExpiresAt)-BigInt(n.readAt));
    if(now-started>=remaining){trace("native-visible-rpc-budget-pending");return false;}
    value.nativeVisible=n;value.nativeLocalUntil=now+remaining-(now-started);value.continuation=undefined;
    const input=nativeInputReady(reply,n);
    value.nativeInputUntil=input?now+Math.min(remaining,Number(BigInt(input.expiresAt)-BigInt(n.readAt)))-(now-started):undefined;
    if(input && value.nativeInputUntil!==undefined && value.nativeInputUntil>now && deps.navigation){
      if(!value.nativeInputReadySeen){value.nativeInputReadySeen=true;trace("native-visible-input-ready");}
      if(value.nativeNavigationIntent!==intent){value.nativeNavigation=undefined;value.nativeNavigationAttempted=false;value.nativeNavigationIntent=intent;}
      value.nativeNavigation ??= steamNativeVisibleNavigation({app:value.app,browser:value.browser!,overlay:value.window,menu:value.menu,
        nativeVisible:n,intent},deps.navigation,deps.windows,()=>nativeUiCurrent(value,version)&&nativeCurrent(value)
          &&value.nativeInputUntil!==undefined&&deps.now()<value.nativeInputUntil,deps.now);
      if(value.nativeNavigation && !value.nativeNavigationAttempted && !value.nativeNavigation.read(n).confirmed){
        value.nativeNavigationAttempted=value.nativeNavigation.activate().attempted;
        if(value.nativeNavigationAttempted)trace("native-visible-navigation-requested");
      }
      if(value.nativeNavigation?.read(n).confirmed&&!value.nativeNavigationConfirmed){value.nativeNavigationConfirmed=true;trace("native-visible-navigation-confirmed");}
    } else value.nativeNavigation=undefined;
    return nativeUiCurrent(value,version);
  }
  function nativeCurrent(value:Owner):boolean {
    const now=deps.now();return Boolean(value.nativeVisible && value.nativeLocalUntil && Number.isFinite(now) && now<value.nativeLocalUntil);
  }
  function nativeClosedForOwner(value:Owner,reply:MirrorReply):SteamNativeVisible|undefined {
    const terminal=nativeVisibleRecord(reply,value.mirror,2);
    return terminal&&(!value.nativeSourceSeen||terminal.guideEpoch===value.nativeSourceSeen.guideEpoch)?terminal:undefined;
  }
  // Explicit user revocation is not a presentation/input capability. A stale
  // native sample can identify our retained original menu, never make it Shown.
  async function revokeNativeFromUser(value:Owner,version:number,reply:MirrorReply):Promise<boolean> {
    const held=value.menuReceipt,seen=value.nativeSourceSeen;
    if(value.nativeRevocationRequested || !held || !seen || replacementSession(reply,value))return false;
    const keys=["sessionId","generation","gameId","sourceHwnd","sourceCookie","ownerPid","ownerBirth","gamePid","gameBirth"] as const;
    if(!keys.every(k=>seen[k]===value.mirror[k]) || typeof reply.mirrorSessionId==="string"&&reply.mirrorSessionId!==seen.sessionId
      || reply.gameId && reply.gameId!==seen.gameId
      || reply.companionPid && reply.companionPid!==seen.ownerPid
      || reply.nativeVisible && [...keys,"guideEpoch" as const].some(k=>reply.nativeVisible![k]!==seen[k]))return false;
    const bound=()=>owner===value&&revision===version&&!disposed&&value.menuReceipt===held&&value.nativeSourceSeen===seen
      &&value.browser===held.browser&&value.window===held.window&&value.menu===held.menu&&held.window.MenuStore===held.menu
      &&(held.window.BrowserWindow??held.window.m_BrowserWindow)===held.parent&&held.parent.closed!==true
      &&held.parent.document===held.document&&owner===value&&revision===version;
    const current=()=>bound()&&nativeUiCurrent(value,version)&&bound();
    if(!current())return false;
    const side=held.menu.GetOpenSideMenu?.(),close=held.menu.CloseSideMenus;
    if(!current() || side!==held.side&&side!==0)return false;
    if(!current())return false;
    const finalSide=held.menu.GetOpenSideMenu?.();
    if(!bound()||finalSide!==held.side&&finalSide!==0)return false;
    value.nativeVisible=undefined;value.nativeLocalUntil=undefined;value.nativeInputUntil=undefined;value.nativeNavigation=undefined;
    if(finalSide!==0&&typeof close==="function"){try{Reflect.apply(close,held.menu,[]);}catch{/* Set0 is still an explicit revocation if this exact menu remains ours. */}}
    if(!current())return false;
    const closedSide=held.menu.GetOpenSideMenu?.();
    if(!bound()||closedSide!==0&&closedSide!==held.side)return false;
    value.nativeRevocationRequested=true; // At most one SDK attempt, including unknown completion.
    await deps.setOverlayState(seen.gameId,0);
    trace("native-visible-user-revocation");
    return current(); // Not source-hidden; only a subsequent exact phase2 acknowledges it.
  }
  async function retireNative(value:Owner,version:number,explicitUserClose=false):Promise<boolean> {
    if(!(value.nativeVisible||value.nativeSourceSeen) || !deps.closeNativeVisible)return false;
    const started=deps.now(),first=await deps.read(value.app,value.overlayPid);
    if(owner!==value)return true;
    if(revision!==version)return false;
    if(!first?.ok&&await releaseReplacedOwner(value,first))return true;
    if(owner!==value)return true;
    if(revision!==version)return false;
    const elapsed=deps.now()-started;if(first?.overlayPid!==value.overlayPid||!Number.isFinite(elapsed)||elapsed<0||elapsed>250){value.nativeVisible=undefined;value.nativeLocalUntil=undefined;return false;}
    if(owner!==value)return true;
    const active=nativeVisibleRecord(first,value.mirror),terminal=nativeClosedForOwner(value,first);
    if(terminal){owner=undefined;trace("native-visible-source-closed");return true;}
    if(value.nativeRevocationRequested)return false;
    if(explicitUserClose){
      if(!await revokeNativeFromUser(value,version,first))return false;
      if(active&&nativeUiCurrent(value,version))await deps.closeNativeVisible(value.app,value.overlayPid,active);
      for(let i=0;i<4&&owner===value&&revision===version;i++){
        const reply=await deps.read(value.app,value.overlayPid);
        if(owner!==value||revision!==version)return owner!==value;
        if(nativeClosedForOwner(value,reply)){owner=undefined;trace("native-visible-source-closed");return true;}
        if(i<3)await deps.delay(25);
      }
      return false;
    }
    if(active && nativeUiCurrent(value,version)){
      // An explicit close/invalidation owns this original source. Successful
      // passthrough never calls this routine or tears down Valve's menu.
      if(active.valveOpen){trace("native-visible-explicit-set0");await deps.setOverlayState(value.mirror.gameId,0);}
      if(owner!==value)return true;
      if(active)await deps.closeNativeVisible(value.app,value.overlayPid,active);
      for(let i=0;i<4 && owner===value;i++){
        const reply=await deps.read(value.app,value.overlayPid);
        if(nativeClosedForOwner(value,reply)){owner=undefined;trace("native-visible-source-closed");return true;}
        if(i<3)await deps.delay(25);
      }
      return false;
    }
    // No native-visible authority survives unknown/expiry. The pre-existing
    // exact-owner retirement performs its own authenticated cleanup checks.
    value.nativeVisible=undefined;value.nativeLocalUntil=undefined;return false;
  }
  async function retire(value: Owner,explicitUserClose=false) {
    if (owner !== value) return true;
    const version = revision;
    if(value.nativeVisible||value.nativeSourceSeen){
      // A missing old document is not a closed source. Only a different
      // authenticated current launch may retire its local bookkeeping.
      if(!nativeUiCurrent(value,version)&&await releaseReplacedOwner(value))return true;
      if(owner!==value)return true;
      if(revision!==version)return false;
      // Automatic observation failure while still wanted does not close a
      // native source. Only explicit close/invalidation or a terminal source
      // receipt may retire it; never fall through to old own-Snapshot cleanup.
      if(wanted&&live(value.app,version)&&!value.nativeRevocationRequested){trace("native-visible-passive-awaiting-fresh-proof");return false;}
      return retireNative(value,version,explicitUserClose);
    }
    // Keep the owner through a rejected/unknown close so retry or dispose can
    // still retire it. A snapshot never grants authority over a replacement.
    for (let attempt = 0; attempt < 4 && owner === value; attempt++) {
      try {
        const before = await deps.read(value.app, value.overlayPid);
        const active = currentMirror(before);
        if (active && !same(active, value.mirror)) { owner = undefined; return true; }
        if (!before?.ok && deps.runningApp() !== value.app) { owner = undefined; return true; }
        if (!before?.ok && await releaseReplacedOwner(value, before)) return true;
        const terminal = before?.ok && before.mirror?.protocol === 1 && sameRetired(before.mirror, value.mirror);
        if (!terminal) await deps.command(value.app, value.overlayPid, "close", active ?? value.mirror);
        const after = terminal ? before : await deps.read(value.app, value.overlayPid);
        const closed = after?.mirror;
        if (after?.ok === true && closed?.protocol === 1 && sameRetired(closed, value.mirror)) {
          value.nativeCloseRequired ||= value.mirror.ownedForegroundPhase === 1 || closed.ownedForegroundPhase === 1;
          if (owner === value && revision === version && deps.runningApp() === value.app && (!value.nativeCloseRequired || !value.closeStateAttempted)) {
            if (value.nativeCloseRequired) value.closeStateAttempted = true;
            await deps.setOverlayState(value.mirror.gameId, 0);
          }
          // Native visual cleanup and focus restoration are distinct from the
          // real renderer becoming inactive. Retain this exact owner meanwhile.
          if (value.nativeCloseRequired && closed.ownedForegroundPhase !== 2) {
            trace("mirror-native-close-pending");
            if (attempt < 3) await deps.delay(40 * (attempt + 1));
            continue;
          }
          if (!closeOwnedMenu(value, version)) {
            trace("mirror-owned-menu-close-unconfirmed");
            if (attempt < 3) await deps.delay(40 * (attempt + 1));
            continue;
          }
          if (owner === value) owner = undefined;
          trace("mirror-retired"); return true;
        }
        const replacement = currentMirror(after);
        if (replacement && !same(replacement, value.mirror)) { owner = undefined; return true; }
      } catch { trace("mirror-close-unconfirmed"); }
      if (attempt < 3) await deps.delay(40 * (attempt + 1));
    }
    return owner !== value;
  }
  // Only a new explicit Guide/QuickAccess edge may retire an old local
  // native owner in favour of a strictly newer authenticated generation.
  async function reopenNativeGeneration(value:Owner,app:number,version:number):Promise<boolean> {
    const previous=value.nativeSourceSeen??value.nativeVisible;
    if(!previous||owner!==value||value.app!==app||!live(app,version))return false;
    const lineage=["sessionId","gameId","sourceHwnd","sourceCookie","ownerPid","ownerBirth","gamePid","gameBirth"] as const;
    const newer=(parent:MirrorPresentation,guide:string)=>lineage.every(k=>parent[k]===value.mirror[k])
      &&uint64(parent.generation)&&uint64(guide)&&BigInt(parent.generation)>BigInt(previous.generation)&&BigInt(guide)>BigInt(previous.guideEpoch);
    const local=()=>owner===value&&!disposed&&revision===version;
    const current=()=>local()&&deps.runningApp()===app&&local();
    const nativeCandidate=(reply:MirrorReply)=>{
      const parent=reply?.mirror,n=parent&&nativeVisibleRecord(reply,parent);
      if(!parent||reply.mirrorSessionId!==parent.sessionId||parent.protocol!==1||parent.state!=="closed"||parent.cleanupComplete!==true
        ||parent.preparedCurrent!==false||parent.shownCurrent!==false||parent.leaseCurrent!==false||parent.actualScope!==0
        ||parent.destinationHwnd!=="0"||parent.destinationCookie!=="0"||!n||!newer(parent,n.guideEpoch))return;
      return {parent:Object.freeze({...parent}),native:n};
    };
    const budget=(n:SteamNativeVisible,started:number)=>{
      const now=deps.now(),remaining=Number(BigInt(n.physicalExpiresAt)-BigInt(n.readAt));
      return Number.isFinite(now)&&Number.isFinite(started)&&now>=started&&now-started<remaining&&current();
    };
    const started=deps.now(),first=await deps.read(app,value.overlayPid);
    if(!current())return false;
    const candidate=nativeCandidate(first);
    if(!candidate){
      const parent=currentMirror(first,"prepared"),continuation=mirrorContinuation(first,value.mirror);
      if(!parent||first.mirrorSessionId!==parent.sessionId||!continuation||!newer(parent,continuation.guideEpoch)||continuation.resumedGeneration!==parent.generation
        ||!(continuation.phase===2||continuation.phase===4)||!uint64(parent.readAt)
        ||BigInt(parent.readAt)<BigInt(parent.observedAt)||BigInt(parent.readAt)>=BigInt(parent.expiresAt))return false;
      const now=deps.now(),remaining=Number(BigInt(parent.expiresAt)-BigInt(parent.readAt));
      if(!Number.isFinite(now)||now<started||now-started>=Math.min(250,remaining)||!current())return false;
      owner=undefined;wanted=true;trace("native-visible-explicit-generation-replaced");
      await open(app,version,first);return true; // Actual Prepared admission, no second Open delivery.
    }
    if(first.overlayPid!==value.overlayPid||!budget(candidate.native,started))return false;
    const infos=await deps.browserInfo();if(!current())return false;
    const browser=Array.isArray(infos)?selectOverlay(infos.filter(info=>Number(info?.unPID)===value.overlayPid
      &&String(info?.gameID)===candidate.parent.gameId),app):undefined;
    const window=browser?findOverlayWindow(deps.windows(),browser):undefined,menu=window?.MenuStore;
    if(!browser||!window||!menu||!current())return false;
    const checkedAt=deps.now(),reply=await deps.read(app,value.overlayPid),fresh=nativeCandidate(reply);
    if(!fresh||fresh.native.generation!==candidate.native.generation||fresh.native.guideEpoch!==candidate.native.guideEpoch
      ||reply.overlayPid!==value.overlayPid||!budget(fresh.native,checkedAt)||findOverlayWindow(deps.windows(),browser)!==window
      ||window.MenuStore!==menu||!current())return false;
    const next:Owner={app,overlayPid:value.overlayPid,mirror:fresh.parent,browser,window,menu};
    const receipt=captureMenu(next,intent==="quick"?2:1);
    if(!receipt||!budget(fresh.native,checkedAt)||!current())return false;
    // No old menu close, native close, Set0, synthetic Shown or input lease.
    owner=next;wanted=true;next.menuReceipt=receipt;
    next.nativeSourceSeen=fresh.native;next.nativeVisible=fresh.native;
    next.nativeLocalUntil=checkedAt+Number(BigInt(fresh.native.physicalExpiresAt)-BigInt(fresh.native.readAt));
    deps.admitted?.(app,fresh.parent);
    // Guide+A may coalesce while the authenticated reads are in flight.
    // Capture the final intent after those reads/providers, never their caller's old side.
    for(let attempt=0;attempt<2;attempt++){
      const requested=intent,method=menu[requested==="quick"?"OpenQuickAccessMenu":"OpenMainMenu"],tab=requested==="quick"?menu.GetQuickAccessTab?.()??0:undefined;
      const currentReceipt=captureMenu(next,requested==="quick"?2:1);
      if(typeof method!=="function"||!currentReceipt)return true;
      next.menuReceipt=currentReceipt;
      if(!live(app,version)||owner!==next||!nativeUiCurrent(next,version)||!nativeCurrent(next))return true;
      const commitAt=deps.now();
      if(!Number.isFinite(commitAt)||commitAt<checkedAt||commitAt>=next.nativeLocalUntil!||owner!==next||disposed||revision!==version)return true;
      if(intent!==requested)continue; // Only local guards follow the last provider before the real API.
      Reflect.apply(method,menu,requested==="quick"?[tab]:[]);
      if(!live(app,version)||owner!==next||!nativeUiCurrent(next,version))return true;
      if(intent!==requested)continue;
      // Navigation still requires the NEW generation's actual neutral/769 ACK.
      if(adoptNative(next,reply,checkedAt,version))trace("native-visible-explicit-generation-replaced");
      return true;
    }
    return true;
  }
  async function open(app: number, version: number, admitted?:MirrorReply) {
    if (!live(app, version)) return;
    if (owner && !await retire(owner)) return;
    if (!live(app, version)) return;
    const delivery = admitted ?? await deps.prepare(app);
    if (!delivery?.ok || !pid(delivery.overlayPid) || !pid(delivery.companionPid)) return;
    const deadline = deps.now() + 3500;
    const receipt = identifiedMirror(delivery);
    let value: Owner | undefined = receipt && (receipt.state === "preparing" || receipt.state === "prepared" || receipt.state === "shown")
      ? { app, overlayPid: delivery.overlayPid, mirror: receipt } : undefined;
    if (!value) return; // The mirror-only endpoint must return its real admission.
    if (value) owner = value;
    trace("mirror-admitted");
    let mountedIntent = intent;
    let finalMount: ((requested: "main" | "quick") => boolean) | undefined;
    let finalUiCurrent: (() => boolean) | undefined;
    let finalNavigation: ((proof: MirrorPresentation) => void) | undefined;
    try {
      if (!live(app, version)) return;
      if (value) deps.admitted?.(app, value.mirror);
      let preparedSource: MirrorPresentation | undefined;
      while (live(app, version) && deps.now() < deadline) {
        const reply = await deps.read(app, delivery.overlayPid);
        if (!live(app, version)) return;
        const mirror = currentMirror(reply);
        if (mirror && mirror.gameId === delivery.gameId && mirror.ownerPid === delivery.companionPid) {
          if (value && !same(value.mirror, mirror)) return;
          value = { app, overlayPid: delivery.overlayPid, mirror };
          owner = value;
          preparedSource = mirror;
          break;
        }
        await deps.delay(25);
      }
      // This is the just-returned, exact Prepared read. No await occurred after
      // it; a second identical RPC adds latency without closing an async gap.
      if (!value || !preparedSource || !live(app, version)) return;
      trace("mirror-prepared");
      // Set2 creates Valve's cold gamepad browser. Waiting for MenuStore first
      // would make the first opening depend on a browser that does not exist.
      await deps.setOverlayState(value.mirror.gameId, 2);
      if (!live(app, version)) return;
      while (live(app, version) && deps.now() < deadline) {
        const infos = await deps.browserInfo();
        if (!live(app, version)) return;
        const browser = Array.isArray(infos) ? selectOverlay(infos.filter(info => Number(info?.unPID) === value!.overlayPid
          && String(info?.gameID) === value!.mirror.gameId), app) : undefined;
        const target = browser ? findOverlayWindow(deps.windows(), browser) : undefined;
        if (target?.MenuStore && browser) {
          value.browser = browser; value.window = target; value.menu = target.MenuStore;
          break;
        }
        await deps.delay(25);
      }
      if (!value.browser || !value.window || !live(app, version)) return;
      const uiCurrent = () => Boolean(value?.browser && live(app, version)
        && findOverlayWindow(deps.windows(), value.browser) === value.window && value.window.MenuStore === value.menu
        && live(app, version)); // A UI getter may synchronously reenter cancellation.
      // Keep this read: Set2/browser creation above is asynchronous. No menu
      // mutation is allowed without a fresh same-generation native lease.
      const proof = await reread(value);
      if (!proof || !uiCurrent()) return;
      mountedIntent = intent;
      finalUiCurrent = uiCurrent;
      const mount = (requested: "main" | "quick") => {
        if (!value?.browser || !live(app, version) || findOverlayWindow(deps.windows(), value.browser) !== value.window
            || value.window.MenuStore !== value.menu) return false;
        const method = value.menu[requested === "quick" ? "OpenQuickAccessMenu" : "OpenMainMenu"];
        const tab = requested === "quick" ? value.menu.GetQuickAccessTab?.() ?? 0 : undefined;
        const receipt = captureMenu(value, requested === "quick" ? 2 : 1);
        if (typeof method !== "function" || !receipt || !uiCurrent()) return false;
        value.menuReceipt = receipt;
        Reflect.apply(method, value.menu, requested === "quick" ? [tab] : []);
        mountedIntent = requested;
        return live(app, version) && value.menu.GetOpenSideMenu?.() === (requested === "quick" ? 2 : 1);
      };
      finalMount = mount;
      const focusShown = (shown: MirrorPresentation) => {
        if (!value) return;
        value.mirror = shown;
        const held = value, requested = mountedIntent;
        held.navigation = deps.navigation && steamMirrorNavigation({ app, browser: held.browser!, overlay: held.window,
          menu: held.menu, mirror: shown, intent: requested }, deps.navigation, deps.windows,
          () => live(app, version) && owner === held && intent === requested && same(held.mirror, shown), deps.now);
        if (held.navigation?.activate().attempted) trace("mirror-navigation-requested");
      };
      finalNavigation = focusShown;
      if (!mount(mountedIntent)) return;
      trace("mirror-menu-ready");
      // Mount is synchronous but may reenter/replace a store. Check the exact
      // UI again. Show independently reauthenticates native ownership, current
      // expiry and renderer readiness; client snapshot flags are not authority.
      if (!uiCurrent()) return;
      const queued = await deps.command(app, value.overlayPid, "show", proof);
      if (queued?.ok === true) trace("mirror-show-queued");
      if (queued?.ok !== true || !live(app, version)) return;
      while (live(app, version) && deps.now() < deadline) {
        const shown = await reread(value, "shown");
        if (!live(app, version)) return;
        if(nativeCurrent(value) && nativeUiCurrent(value,version)){if(intent!==mountedIntent&&!mount(intent))return;trace("native-visible-passthrough");return;}
        if (intent !== mountedIntent) {
          const current = shown ?? await reread(value);
          if (!current || !live(app, version) || !mount(intent)) return;
          continue;
        }
        if (shown) {
          focusShown(shown);
          if (!live(app, version)) return;
          if (intent !== mountedIntent) { if (!mount(intent)) return; continue; }
          trace("mirror-shown"); return;
        }
        await deps.delay(25);
      }
    } finally {
      if (value && owner === value) {
        let keep = false;
        try {
          const final = live(app, version) ? await reread(value, "shown") : undefined;
          if(nativeCurrent(value) && nativeUiCurrent(value,version)){keep=true;trace("native-visible-retained");}
          keep = (keep||Boolean(final)||passiveCurrent(value)) && live(app, version);
          if (keep && final) {
            // An intent can arrive during this last native await. Reuse its
            // fresh Shown proof; never prepare, compose or show a second time.
            for (let attempt = 0; keep && intent !== mountedIntent && attempt < 2; attempt++) {
              if (!finalUiCurrent?.() || !finalMount?.(intent)) { keep = false; break; }
              finalNavigation?.(final);
            }
            keep = keep && live(app, version) && intent === mountedIntent && (!finalUiCurrent || finalUiCurrent());
            if (keep && value.navigation) trace(value.navigation.read(final).confirmed
              ? "mirror-navigation-confirmed" : "mirror-navigation-pending");
          }
        }
        catch { keep = false; /* A failed observer retires the owned tuple through native validation. */ }
        if (!keep) await retire(value);
      }
    }
  }
  async function switchNativeMenu(value:Owner,version:number,requested:"main"|"quick"):Promise<boolean>{
    if(!value.nativeVisible)return false;
    const started=deps.now(),reply=await deps.read(value.app,value.overlayPid);
    if(!adoptNative(value,reply,started,version)||!nativeUiCurrent(value,version))return false;
    const side=requested==="quick"?2:1,method=value.menu?.[requested==="quick"?"OpenQuickAccessMenu":"OpenMainMenu"];
    const receipt=captureMenu(value,side),tab=requested==="quick"?value.menu?.GetQuickAccessTab?.()??0:undefined;
    if(typeof method!=="function"||!receipt||!nativeCurrent(value)||!nativeUiCurrent(value,version))return false;
    value.menuReceipt=receipt;Reflect.apply(method,value.menu,requested==="quick"?[tab]:[]);
    if(!nativeUiCurrent(value,version))return true;
    trace(value.menu.GetOpenSideMenu?.()===side?"native-visible-menu-switched":"native-visible-menu-switch-unconfirmed");return true;
  }
  async function switchShownMenu(value: Owner, app: number, version: number, previous: "main" | "quick", requested: "main" | "quick") {
    if (!value.browser || !value.window || !value.menu) return false;
    const browser = value.browser, window = value.window, menu = value.menu;
    const uiCurrent = () => owner === value && live(app, version) && intent === requested
      && findOverlayWindow(deps.windows(), browser) === window && window.MenuStore === menu
      && owner === value && live(app, version) && intent === requested;
    const first = await reread(value, "shown");
    if (!first || !uiCurrent()) return false;
    const infos = await deps.browserInfo();
    const observed = Array.isArray(infos) ? selectOverlay(infos.filter(info => Number(info?.unPID) === value.overlayPid
      && String(info?.gameID) === first.gameId), app) : undefined;
    if (!observed || observed.unPID !== browser.unPID || observed.nBrowserID !== browser.nBrowserID
      || observed.appID !== browser.appID || observed.gameID !== browser.gameID || !uiCurrent()) return false;
    // Switching sides uses the already shown native generation. Retiring it
    // here introduces a visible gap and races the next native preparation.
    const proofStartedAt = deps.now();
    const proof = await reread(value, "shown");
    const proofReturnedAt = deps.now();
    if (!proof || !uiCurrent()) return false;
    if (!uint64(proof.readAt) || BigInt(proof.readAt) < BigInt(proof.observedAt)
        || BigInt(proof.readAt) >= BigInt(proof.expiresAt)) {
      trace("mirror-menu-switch-expired");
      return true;
    }
    const proofLifetime = Number(BigInt(proof.expiresAt) - BigInt(proof.readAt));
    const commitCurrent = () => {
      if (!uiCurrent()) return false;
      const now = deps.now();
      return Number.isFinite(now) && now >= proofReturnedAt && proofReturnedAt >= proofStartedAt
        && now - proofReturnedAt <= 250 && now - proofStartedAt + 50 < proofLifetime
        && owner === value && live(app, version) && intent === requested;
    };
    const selected = menu.GetOpenSideMenu?.(), desired = requested === "quick" ? 2 : 1;
    if (selected !== (previous === "quick" ? 2 : 1) && selected !== desired) return false;
    const method = requested === "quick" ? menu.OpenQuickAccessMenu : menu.OpenMainMenu;
    const tab = requested === "quick" ? menu.GetQuickAccessTab?.() ?? 0 : undefined;
    const receipt = captureMenu(value, requested === "quick" ? 2 : 1);
    if (typeof method !== "function" || !receipt) return false;
    if (!commitCurrent()) { trace("mirror-menu-switch-expired"); return true; }
    value.menuReceipt = receipt;
    try {
      if (selected !== desired) {
        if (requested === "quick") method.call(menu, tab, true);
        else method.call(menu);
      }
      if (!uiCurrent() || menu.GetOpenSideMenu?.() !== (requested === "quick" ? 2 : 1)) return true;
      value.mirror = proof;
      value.navigation = deps.navigation && steamMirrorNavigation({ app, browser, overlay: window, menu, mirror: proof, intent: requested },
        deps.navigation, deps.windows, () => uiCurrent() && same(value.mirror, proof), deps.now);
      value.navigation?.activate();
      const after = await reread(value, "shown");
      if (after && uiCurrent() && menu.GetOpenSideMenu?.() === (requested === "quick" ? 2 : 1)) {
        value.mirror = after;
        trace("mirror-menu-switched");
      }
    } catch { trace("mirror-menu-switch-unconfirmed"); }
    // Once the original menu API was called, a replacement or user action
    // owns the current UI. Do not retry by closing and creating another menu.
    return true;
  }
  let polling=false,pollQueued=false;
  async function pollContinuation() {
    const value=owner,version=revision;
    if(disposed||!value||!wanted&&!value.nativeRevocationRequested||openingVersion||polling)return;
    polling=true;
    try {
      const started=deps.now(),reply=await deps.read(value.app,value.overlayPid);
      if(owner!==value||disposed||revision!==version||!wanted&&!value.nativeRevocationRequested)return;
      if(deps.runningApp()!==value.app){await releaseReplacedOwner(value);return;}
      if(value.nativeRevocationRequested){
        if(nativeClosedForOwner(value,reply)){owner=undefined;trace("native-visible-source-closed");}
        else if(await releaseReplacedOwner(value,reply)){/* Local old-session receipt only; no Close/Set0/authority transfer. */}
        else trace("native-visible-user-close-awaiting-source-hidden");
        return; // Passive acknowledgement only; never repeat Set0 or mint input/Shown.
      }
      if(value.nativeVisible&&value.nativeNavigation&&nativeCurrent(value)&&nativeUiCurrent(value,version)
        &&value.menu.GetOpenSideMenu?.()===0&&nativeUiCurrent(value,version)){
        wanted=false;await retire(value);trace("native-visible-original-menu-closed");return;
      }
      if(adoptNative(value,reply,started,version)){trace("native-visible-current");return;}
      const marker=noteContinuation(value,reply,started),proof=currentMirror(reply),identified=identifiedMirror(reply);
      if(proof&&same(proof,value.mirror)){value.mirror=proof;return;}
      if(marker?.phase===1&&passiveCurrent(value)){trace("mirror-passive-intent-retained");return;}
      if(marker?.phase===2&&value.continuation&&deps.now()<value.continuation.localDeadline&&identified?.state==="preparing"&&identified.generation===marker.resumedGeneration
        &&identified.preparedCurrent===false&&identified.shownCurrent===false&&identified.leaseCurrent===false&&identified.actualScope===0
        &&identityKeys.filter(k=>k!=="generation"&&k!=="destinationHwnd"&&k!=="destinationCookie").every(k=>identified[k]===value.continuation!.parent[k])) {
        trace("mirror-fresh-continuation-preparing");return; // no display, input, Set2 or stale Show authority.
      }
      if(marker&&(marker.phase===2||marker.phase===4)&&value.continuation&&deps.now()<value.continuation.localDeadline&&proof&&proof.generation===marker.resumedGeneration) {
        const parent=value.continuation.parent;
        if(!identityKeys.filter(k=>k!=="generation"&&k!=="destinationHwnd"&&k!=="destinationCookie").every(k=>proof[k]===parent[k])||!value.browser||findOverlayWindow(deps.windows(),value.browser)!==value.window||value.window.MenuStore!==value.menu||!value.menuReceipt||value.menu.GetOpenSideMenu?.()!==value.menuReceipt.side)return;
        const browser=value.window.BrowserWindow??value.window.m_BrowserWindow;
        if(browser!==value.menuReceipt.parent||browser.closed===true||browser.document!==value.menuReceipt.document)return;
        value.mirror=proof;value.navigation=undefined;
        deps.admitted?.(value.app,proof); // fresh tuple, no old lease/menu/focus replay.
        if(proof.state==="prepared") {
          const queued=await deps.command(value.app,value.overlayPid,"show",proof);
          if(owner!==value||!live(value.app,version)||!wanted)return;
          if(!queued?.ok){await retire(value);return;}
          const after=currentMirror(await deps.read(value.app,value.overlayPid),"shown");
          if(after&&owner===value&&live(value.app,version)&&same(after,proof)){value.mirror=after;trace("mirror-fresh-continuation-shown");}
        } else trace("mirror-fresh-continuation-shown");
        return;
      }
      if(proof&&!same(proof,value.mirror)&&!marker)return; // unrelated tuple is never adopted.
      await retire(value);
    } catch { if(owner===value&&!passiveCurrent(value))await retire(value); }
    finally {polling=false;}
  }
  return {
    guide(menu: "main" | "quick", explicitNewIntent = true) {
      const app = deps.runningApp();
      if (disposed || app < 0x80000000 || app > 0xffffffff) return;
      if (wanted && intent !== menu && openingVersion !== 0 && openingApp === app) {
        // Guide+A emits Guide first, then Quick Access. Both edges belong to
        // one opening; the existing preparation mounts the final intent.
        intent = menu;
        return serial;
      }
      // An intent from a previous game cannot consume this game's first Guide.
      // Only the same app's admitted owner or still-pending opening can toggle closed.
      const previousIntent = intent;
      const switching = wantedFor(app) && owner?.app === app && intent !== menu && openingVersion === 0 ? owner : undefined;
      const closing = wantedFor(app) && intent === menu;
      const previousOpening = openingVersion, pendingAtIntent = previousOpening !== 0;
      wanted = !closing;
      intent = menu;
      const version = ++revision;
      if (!closing && !switching) { openingVersion = version; openingApp = app; }
      return enqueue(async () => {
        const oldNative=owner;
        if(explicitNewIntent&&oldNative&&await reopenNativeGeneration(oldNative,app,version))return;
        if (switching && owner === switching && (switching.nativeVisible ? await switchNativeMenu(switching,version,menu) : await switchShownMenu(switching, app, version, previousIntent, menu))) return;
        if (!live(app, version)) return;
        let reopen = !closing;
        if (closing && owner && !pendingAtIntent) {
          // Back can close native presentation without a CEF activation edge.
          // Its next Guide starts a new generation rather than consuming an
          // extra press to dismiss a stale JavaScript owner.
          const held = owner, reply = await deps.read(app, held.overlayPid);
          const terminal = !held.nativeSourceSeen && !held.nativeVisible && !nativeVisibleRecord(reply,held.mirror) && reply?.ok === true && reply.mirror?.protocol === 1 && sameRetired(reply.mirror, held.mirror);
          const replaced = live(app, version) && !terminal && await releaseReplacedOwner(held, reply);
          if (live(app, version) && (terminal || replaced)) {
            reopen = wanted = true; openingVersion = version; openingApp = app;
          }
        }
        if (!reopen) { if (owner) await retire(owner,true); }
        else {
          try { await open(app, version); }
          finally { if (live(app, version) && !owner) wanted = false; }
        }
      }).finally(() => {
        if (openingVersion === version || closing && openingVersion === previousOpening) { openingVersion = 0; openingApp = 0; }
      });
    },
    poll() { if(pollQueued||disposed||!owner||!wanted&&!owner.nativeRevocationRequested)return serial;pollQueued=true;return enqueue(pollContinuation).finally(()=>{pollQueued=false;}); },
    close() { wanted = false; ++revision; return enqueue(async () => { if (owner) await retire(owner,true); }); },
    recover(menu: "main" | "quick") {
      if (disposed || wantedFor(deps.runningApp())) return serial;
      return this.guide(menu, false);
    },
    invalidate() { wanted = false; ++revision; return enqueue(async () => { if (owner) await retire(owner); }); },
    dispose() { disposed = true; wanted = false; ++revision; return enqueue(async () => { if (owner) await retire(owner); }); },
    get active() { return owner !== undefined; },
    get nativeVisible() { return Boolean(owner && nativeCurrent(owner)); },
    get pendingApp() { return !disposed && wanted && openingVersion !== 0 && openingApp === deps.runningApp() ? openingApp : 0; },
    get shown() { return owner?.mirror.state === "shown"&&!(owner.continuation&&(owner.continuation.marker.phase===1||owner.continuation.marker.phase===2)&&owner.mirror.generation===owner.continuation.parent.generation); },
    settled() { return serial; },
  };
}

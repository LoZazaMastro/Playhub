import type { ReactNode } from "react";
import type { OnboardingControlStep } from "./onboardingControls";
import { observeOnboardingAnchors, visibleOnboardingAnchor, convertOnboardingAnchor, onboardingFallbackQamBounds, type AnchorRect } from "./onboardingAnchors";
import { ONBOARDING_STORAGE_KEY, readOnboardingState, reduceOnboarding, type OnboardingFacts, type OnboardingState } from "./onboardingState";

export type OnboardingAction = "quick-access" | "confirm" | "cancel";
export interface OnboardingEnvironment {
  steps?: readonly OnboardingState["step"][];
  steamReady?: boolean;
  document: Document | null;
  mainDocument?: Document | null;
  qamBounds?: AnchorRect | null;
  qamOpen: boolean;
  playhubSelected: boolean;
  deckyEnabled: boolean;
  deckyVisible: boolean;
  /** The current owner's tab ID, when Shortcuts does not use Playhub's standalone ID. */
  playhubTabId?: number;
  footerInset?: number;
  controlsReady?: boolean;
  controlStep?: OnboardingControlStep | null;
  controlReady?: boolean;
  storeVisible?: boolean;
  layoutStable?: boolean;
  geometryVisible?: boolean;
}
export interface PlayhubOnboardingOptions {
  storage?: Pick<Storage, "getItem" | "setItem">;
  native: { renderHint(action: OnboardingAction): ReactNode; subscribe(listener: () => void): () => void };
  getEnvironment(): OnboardingEnvironment;
  subscribeEnvironment(listener: () => void): () => void;
  /** Navigate using the current native QAM owner. Never change user visibility preferences. */
  enterPlayhub(): void | Promise<void>;
  openQuickAccess?(): void | Promise<void>;
  /** Used only by the closing slide, which is presented with the menu shut. */
  closeQuickAccess?(): void | Promise<void>;
  showControlStep?(step: OnboardingControlStep | null): void;
}
type View = Exclude<OnboardingState["step"], "done"> | "decky-off" | null;
interface Snapshot {
  controlStep?: OnboardingControlStep | null;
  controlReady?: boolean;
  controlsReady?: boolean;
  playhubSelected?: boolean;
  introExiting?: boolean;
  /** A confirmed step is settling: input is held until the next slide is on screen. */
  advancing?: boolean;
  steamReady?: boolean;
  qamOpen?: boolean;
  anchorDocumentTitle?: string | null;
  layoutStable?: boolean;
  qamBoundsEstimated?: boolean;
  state: OnboardingState;
  view: View;
  document: Document | null;
  anchor: AnchorRect | null;
  qamBounds?: AnchorRect | null;
  width: number;
  height: number;
  footerInset: number;
  revision: number;
}
const listeners = new Set<() => void>();
let snapshot: Snapshot = { state: readOnboardingState(null), view: null, document: null,
  anchor: null, width: 0, height: 0, footerInset: 96, revision: 0 };
let session: { options: PlayhubOnboardingOptions; refresh(): void; save(state: OnboardingState): void; stop(): void } | undefined;
let presented: View = null;
let advancingFrom: View = null;
// A slide can be on screen before its geometry settles. A press there is kept, not lost:
// one input always advances one slide.
let pendingConfirm: View = null;
let pendingConfirmSince = 0;
// A slide that never reports itself as settled must not swallow the press for ever:
// past this grace the guide advances anyway. One press always moves one slide.
const PENDING_CONFIRM_GRACE = 1200;
let advancingSince = 0;
const ADVANCE_SHIELD_TIMEOUT = 2500;
let currentFacts: OnboardingFacts = { qamOpen: false, playhubSelected: false, playhubAnchor: false,
  deckyEnabled: false, deckyVisible: false, deckyAnchor: false };
export const getPlayhubOnboardingSnapshot = () => snapshot;
export function subscribePlayhubOnboarding(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener); }; }
function publish(next: Snapshot) {
  const { document: oldDocument, ...old } = snapshot;
  const { document: newDocument, ...value } = next;
  if (oldDocument === newDocument && JSON.stringify(old) === JSON.stringify(value)) return;
  if (snapshot.view !== next.view || oldDocument !== newDocument) presented = null;
  snapshot = next; listeners.forEach(listener => listener());
}
export function markPlayhubOnboardingPresented(view: View) {
  if (view === null || view === snapshot.view) presented = view;
  if (view !== null && pendingConfirm === view) {
    pendingConfirm = null;
    // Outside the render phase: the confirmation changes state the component is reading.
    void Promise.resolve().then(() => { if (presented === view && snapshot.view === view) reportPlayhubOnboardingAction("confirm"); });
  }
}
export function renderPlayhubOnboardingHint(action: OnboardingAction): ReactNode {
  try { return session?.options.native.renderHint(action) ?? null; } catch { return null; }
}
export function reportPlayhubOnboardingAction(action: "confirm" | "cancel", force = false): boolean {
  // A dismissal wins over pending navigation observations in the same event turn.
  if (action === "cancel" && session && !snapshot.state.completed) {
    advancingFrom = null; pendingConfirm = null;
    session.save(reduceOnboarding(snapshot.state, { type: "pause" })); session.refresh(); return true;
  }
  // A press between slides must never fall through to the Steam menu behind the guide.
  if (action === "confirm" && snapshot.advancing) return true;
  const requestedView = snapshot.view;
  const wasPresented = requestedView !== null && presented === requestedView;
  session?.refresh();
  if (!session || !snapshot.view || snapshot.state.paused || snapshot.state.completed) return false;
  if (action === "cancel") {
    session.save(reduceOnboarding(snapshot.state, { type: "pause" })); session.refresh(); return true;
  }
  if (!force && (!wasPresented || requestedView !== snapshot.view || presented !== snapshot.view)) {
    // Visible but not yet confirmable: keep the press and apply it once the slide is ready,
    // or once the grace above expires — whichever comes first.
    if (snapshot.view !== null && requestedView === snapshot.view) {
      if (pendingConfirm !== snapshot.view) pendingConfirmSince = Date.now();
      pendingConfirm = snapshot.view;
      return true;
    }
    return false;
  }
  if (!force && snapshot.view !== "intro" && snapshot.layoutStable === false) return false;
  if (snapshot.view === "intro") {
    if (snapshot.introExiting) return false;
    if (!session.options.openQuickAccess) return false;
    const owner = session;
    const restoreIntro = () => {
      if (session === owner && snapshot.view === "intro" && snapshot.introExiting)
        publish({ ...snapshot, introExiting: false });
    };
    publish({ ...snapshot, introExiting: true });
    try { Promise.resolve(session.options.openQuickAccess()).catch(restoreIntro); } catch { restoreIntro(); }
    session.refresh(); return true;
  }
  if (snapshot.view !== "outro") {
    advancingFrom = snapshot.view;
    advancingSince = Date.now();
    publish({ ...snapshot, advancing: true });
  }
  if (snapshot.view === "playhub") {
    session.save(reduceOnboarding(snapshot.state, { type: "confirm", facts: currentFacts }));
    try { Promise.resolve(session.options.enterPlayhub()).catch(() => {}); } catch { /* Remain on this step until native selection succeeds. */ }
    session.refresh();
    return true;
  }
  session.save(reduceOnboarding(snapshot.state, { type: "confirm", facts: currentFacts })); session.refresh(); return true;
}
export function resumePlayhubOnboarding() {
  if (!session || snapshot.state.completed) return;
  session.save(reduceOnboarding(snapshot.state, { type: "resume" })); session.refresh();
}

/** Mount once per plugin, not per QAM opening. The parent owns all Steam event subscriptions. */
export function initPlayhubOnboarding(options: PlayhubOnboardingOptions): () => void {
  session?.stop();
  let storage = options.storage;
  try { storage ??= window.localStorage; } catch { /* Session-only guide when storage is unavailable. */ }
  let state = readOnboardingState(null);
  try { state = readOnboardingState(storage?.getItem(ONBOARDING_STORAGE_KEY) ?? null); } catch { /* Keep session state usable. */ }
  let stopped = false;
  let enteringHome = false;
  let closingQam = false;
  // Grace period before trusting an estimated QAM band: a freshly opened QAM must not
  // be annotated before Steam reports its real bounds.
  const FALLBACK_BOUNDS_DELAY = 1500;
  let boundsMissingSince = 0;
  let fallbackTimer: ReturnType<typeof setTimeout> | undefined;
  let pendingTimer: ReturnType<typeof setTimeout> | undefined;
  // The guide must never hold the QAM in showcase with nothing on screen: if no step can
  // be presented for this long, it pauses itself and hands the menu back to the user.
  const SHOWCASE_STUCK_DELAY = 8000;
  let showcaseStuckSince = 0;
  let stuckTimer: ReturnType<typeof setTimeout> | undefined;
  const clearStuck = () => {
    showcaseStuckSince = 0;
    if (stuckTimer !== undefined) { clearTimeout(stuckTimer); stuckTimer = undefined; }
  };
  let document: Document | null = null;
  let stopAnchors = () => {};
  let stopEnvironment = () => {};
  let stopNative = () => {};
  const save = (next: OnboardingState) => {
    if (JSON.stringify(next) === JSON.stringify(state)) return;
    state = next;
    try { storage?.setItem(ONBOARDING_STORAGE_KEY, JSON.stringify(state)); } catch { /* Never block Steam navigation on persistence failure. */ }
  };
  let refreshing = false;
  let refreshPending = false;
  const refresh = () => {
    if (refreshing) { refreshPending = true; return; }
    refreshing = true;
    try {
      // A synchronous control-store commit must not publish underneath a stale outer read.
      do { refreshPending = false; refreshOnce(); } while (refreshPending && !stopped);
    } finally { refreshing = false; }
  };
  const refreshOnce = () => {
    if (stopped) return;
    if (state.completed) {
      options.showControlStep?.(null);
      stopAnchors(); stopAnchors = () => {};
      publish({ ...snapshot, state, view: null });
      return;
    }
    try {
      const env = options.getEnvironment();
      const observed = { steamReady: env.steamReady !== false, qamOpen: env.qamOpen,
        anchorDocumentTitle: env.document?.title ?? null, layoutStable: env.layoutStable,
        controlStep: env.controlStep, controlReady: env.controlReady,
        controlsReady: env.controlsReady, playhubSelected: env.playhubSelected };
      if (document !== env.document) {
        stopAnchors(); document = env.document;
        stopAnchors = document ? observeOnboardingAnchors(document, refresh) : () => {};
      }
      if (env.steamReady === false) {
        options.showControlStep?.(null);
        presented = null;
        publish({ ...snapshot, ...observed, state, view: null,
          document: env.mainDocument === undefined ? document : env.mainDocument, anchor: null, qamBounds: null });
        return;
      }
      const playhub = document ? visibleOnboardingAnchor(document, `#quickaccess_tab_${env.playhubTabId ?? 0x50484B}`) : null;
      const decky = document ? visibleOnboardingAnchor(document, '[data-playhub-onboarding="decky"]') : null;
      const store = document ? visibleOnboardingAnchor(document, '[data-playhub-onboarding="store"]') : null;
      const customize = document ? visibleOnboardingAnchor(document, '[data-playhub-onboarding="customize"]') : null;
      currentFacts = { ...env, playhubAnchor: !!playhub, deckyAnchor: !!decky, storeAnchor: !!store, customizeAnchor: !!customize,
        activeReady: env.controlReady && env.controlStep === (state.step === "playhub" ? "home" : state.step),
        controlShowcase: env.controlStep !== undefined && env.controlsReady === true,
        customizeAvailable: env.controlsReady === undefined ? undefined : env.controlsReady };
      save(reduceOnboarding(state, { type: "observe", facts: currentFacts }));
      if (!env.qamOpen || env.playhubSelected) enteringHome = false;
      if (!env.qamOpen || state.step !== "outro") closingQam = false;
      if (state.step === "outro" && !state.paused && env.qamOpen && options.closeQuickAccess && !closingQam) {
        closingQam = true;
        try { Promise.resolve(options.closeQuickAccess()).catch(() => { closingQam = false; }); }
        catch { closingQam = false; }
      }
      if ((state.step === "playhub" || env.steps) && !state.paused && env.qamOpen && playhub && !env.playhubSelected
        && options.showControlStep && !enteringHome) {
        enteringHome = true;
        try { Promise.resolve(options.enterPlayhub()).catch(() => { enteringHome = false; }); }
        catch { enteringHome = false; }
      }
      const internalStep = state.step === "playhub" ? "home"
        : state.step !== "intro" && state.step !== "outro" && state.step !== "done" ? state.step : null;
      const desired = !state.paused && env.qamOpen && env.playhubSelected && env.controlsReady !== false ? internalStep : null;
      options.showControlStep?.(desired);
      let view: View = null;
      const internalAnchor = document && internalStep ? visibleOnboardingAnchor(document, `[data-playhub-onboarding="${internalStep}"]`) : null;
      if (document?.body && !state.completed && !state.paused) {
        if (!env.qamOpen) {
          if (state.step === "outro") view = "outro";
          else if (state.step === "intro" && !state.awaitingTabs) view = "intro";
        }
        // The closing slide belongs to a closed menu; no tab step may claim it.
        else if (state.step === "outro") view = null;
        else if ((!env.playhubSelected || state.step === "playhub") && playhub) view = "playhub";
        else if (env.playhubSelected && state.step !== "intro") {
          if (env.steps && internalAnchor && state.step !== "done") view = state.step;
          else if (env.steps) view = null;
          else if (state.step === "store") { if (store) view = "store"; }
          else if (state.step === "customize") { if (customize) view = "customize"; }
          else if (!env.deckyEnabled || !env.deckyVisible) view = "decky-off";
          else if (decky) view = "decky";
        }
      }
      if (internalStep && env.controlStep !== undefined && (view !== "playhub" || state.step === "playhub")
        && (!env.playhubSelected || env.controlStep !== internalStep || !env.controlReady)) view = null;
      if (view !== "intro" && view !== "outro" && env.steps && (!env.playhubSelected || !env.steps.length)) view = null;
      const targetDocument = env.mainDocument === undefined ? document : env.mainDocument;
      const target = view === "playhub" ? playhub : env.steps ? internalAnchor : view === "decky" ? decky : view === "store" ? store : view === "customize" ? customize : playhub;
      // Missing native bounds must not leave the guide invisible while the QAM is captured.
      // After the grace period, estimate the QAM band instead of dropping the step.
      const now = Date.now();
      // The shield lifts as soon as the next slide can be computed, or after its timeout.
      if (pendingConfirm !== null && (view !== pendingConfirm || state.paused || state.completed)) pendingConfirm = null;
      if (pendingTimer !== undefined) { clearTimeout(pendingTimer); pendingTimer = undefined; }
      if (pendingConfirm !== null) {
        const waited = now - pendingConfirmSince;
        if (waited >= PENDING_CONFIRM_GRACE) {
          // The slide is on screen but never declared itself ready. Advance regardless:
          // a guide the user cannot get past is worse than one measured imprecisely.
          const held = pendingConfirm;
          pendingConfirm = null;
          void Promise.resolve().then(() => {
            if (!stopped && snapshot.view === held) reportPlayhubOnboardingAction("confirm", true);
          });
        } else if (typeof setTimeout === "function") {
          pendingTimer = setTimeout(() => { pendingTimer = undefined; if (!stopped) refresh(); },
            PENDING_CONFIRM_GRACE - waited + 16);
        }
      }
      if (advancingFrom !== null && ((view !== null && view !== advancingFrom) || state.paused || state.completed
        || !env.qamOpen || now - advancingSince > ADVANCE_SHIELD_TIMEOUT)) advancingFrom = null;
      if (!view || view === "intro" || view === "outro" || !env.qamOpen || env.qamBounds) boundsMissingSince = 0;
      else if (!boundsMissingSince) boundsMissingSince = now;
      const overdue = boundsMissingSince > 0 && now - boundsMissingSince >= FALLBACK_BOUNDS_DELAY;
      if (fallbackTimer !== undefined) { clearTimeout(fallbackTimer); fallbackTimer = undefined; }
      if (boundsMissingSince > 0 && !overdue && typeof setTimeout === "function") {
        fallbackTimer = setTimeout(() => { fallbackTimer = undefined; if (!stopped) refresh(); },
          FALLBACK_BOUNDS_DELAY - (now - boundsMissingSince) + 16);
      }
      const estimated = overdue && view !== "outro" && targetDocument
        ? onboardingFallbackQamBounds({ width: targetDocument.defaultView?.innerWidth ?? 0,
          height: targetDocument.defaultView?.innerHeight ?? 0 }) : null;
      const bounds = env.qamBounds ?? estimated;
      const anchor = target && bounds && document !== targetDocument
        ? convertOnboardingAnchor(target, bounds, { width: document?.defaultView?.innerWidth ?? 0, height: document?.defaultView?.innerHeight ?? 0 }) : target;
      if (view !== "intro" && view !== "outro" && env.mainDocument !== undefined && (!bounds || !targetDocument)) view = null;
      if (view === null && desired !== null && env.qamOpen && !state.paused && !state.completed) {
        if (!showcaseStuckSince) showcaseStuckSince = now;
        if (now - showcaseStuckSince >= SHOWCASE_STUCK_DELAY) {
          clearStuck();
          options.showControlStep?.(null);
          save(reduceOnboarding(state, { type: "pause" }));
          refresh();
          return;
        }
        if (stuckTimer === undefined && typeof setTimeout === "function") {
          stuckTimer = setTimeout(() => { stuckTimer = undefined; if (!stopped) refresh(); },
            SHOWCASE_STUCK_DELAY - (now - showcaseStuckSince) + 16);
        }
      } else clearStuck();
      publish({ ...snapshot, ...observed, state, view, introExiting: state.step === "intro" && snapshot.introExiting,
        layoutStable: estimated || view === "outro" ? true : observed.layoutStable, qamBoundsEstimated: !!estimated,
        advancing: advancingFrom !== null,
        document: targetDocument, anchor, qamBounds: bounds ?? null,
        width: targetDocument?.defaultView?.innerWidth ?? 0, height: targetDocument?.defaultView?.innerHeight ?? 0,
        footerInset: Math.max(64, env.footerInset ?? 96) });
    } catch {
      options.showControlStep?.(null);
      publish({ ...snapshot, state, view: null, steamReady: false, anchor: null, qamBounds: null });
    }
  };
  const stop = () => {
    if (stopped) return;
    stopped = true;
    advancingFrom = null; pendingConfirm = null;
    if (fallbackTimer !== undefined) { clearTimeout(fallbackTimer); fallbackTimer = undefined; }
    if (pendingTimer !== undefined) { clearTimeout(pendingTimer); pendingTimer = undefined; }
    boundsMissingSince = 0;
    clearStuck();
    options.showControlStep?.(null);
    stopAnchors(); stopEnvironment(); stopNative();
    if (session === own) { session = undefined; presented = null; publish({ ...snapshot, view: null, document: null }); }
  };
  const own = { options, refresh, save, stop };
  session = own;
  publish({ ...snapshot, state, view: null, document: null, anchor: null, qamBounds: null,
    steamReady: false, qamOpen: false, anchorDocumentTitle: null, introExiting: false });
  try {
    stopEnvironment = options.subscribeEnvironment(refresh);
    stopNative = options.native.subscribe(() => { publish({ ...snapshot, revision: snapshot.revision + 1 }); refresh(); });
    refresh();
  } catch { stop(); }
  return stop;
}

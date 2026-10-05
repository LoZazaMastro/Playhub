import { DFL, SP_REACT as React, routerHook } from "./decky";
import { PlayhubOnboarding, initPlayhubOnboarding, getPlayhubOnboardingSnapshot, reportPlayhubOnboardingAction } from "./PlayhubOnboarding";
import { createOnboardingNativeAdapter } from "./onboardingNative";
import { observeSteamTourCompletion, probeSteamTourRenderer } from "./onboardingSteamGate";
import { observeOnboardingAnchors, readOnboardingBrowserViewBounds, readOnboardingQamPlaceholderBounds, findOnboardingBrowserViewDocument, visibleOnboardingContent, createOnboardingBoundsStability, invalidateOnboardingBrowserViewOwners, onboardingAnimationFrameHost } from "./onboardingAnchors";
import { requestOnboardingControlStep, onboardingVisibleSteps, type OnboardingControlStep } from "./onboardingControls";
import type { OnboardingStep } from "./onboardingState";
import type { PlayhubOnboardingOptions, OnboardingEnvironment } from "./onboardingRuntime";

const localeListeners = new Set<() => void>();
let replayGuide: (() => void) | undefined;
export function replayPlayhubOnboarding() { replayGuide?.(); }
export function refreshOnboardingLocale() { localeListeners.forEach(listener => listener()); }
export function resolveOnboardingPlayhubTabId(host: any): number {
  const hook = host.DeckyPluginLoader?.tabsHook ?? host.__TABS_HOOK_INSTANCE;
  const registered = hook?.tabs?.find((tab: any) => tab.__shortcutsPlugin === "Playhub");
  return registered && Number.isSafeInteger(Number(registered.id)) ? Number(registered.id) : 0x50484B;
}
const qamDocuments = new Map<Document, number>();
const documentListeners = new Set<(document?: Document) => void>();
export function registerOnboardingQamDocument(document: Document) {
  qamDocuments.set(document, (qamDocuments.get(document) ?? 0) + 1);
  // The QAM BrowserView can become the controller-focus owner in the same turn
  // in which it mounts. Bind it synchronously: waiting for the environment
  // microtask leaves a real one-press gap in which Steam consumes A as focus.
  documentListeners.forEach(listener => listener(document));
  return () => {
    const count = qamDocuments.get(document) ?? 0;
    if (count <= 1) qamDocuments.delete(document); else qamDocuments.set(document, count - 1);
    documentListeners.forEach(listener => listener());
  };
}
export function selectOnboardingQamDocument(mainDocument: Document | null, candidates: Document[], tabId: number) {
  for (const document of [...candidates, ...(mainDocument ? [mainDocument] : [])]) {
    try { if (!document.defaultView?.closed && document.getElementById(`quickaccess_tab_${tabId}`)) return document; } catch { /* Closed popup. */ }
  }
  return null;
}

/** Only semantic Steam navigation events; Guide/QuickMenu and unrelated input pass untouched.
 *  While a slide is on screen the guide owns confirm and back: the presentation is completed,
 *  not dismissed, so neither key may reach the menu behind it. */
export function createOnboardingInputState() { return {
  pressed: new Set<number>(), suppressed: new Set<number>(), pressedAt: new Map<number, number>()
}; }
// vgp events use Steam's semantic enum, not the browser Gamepad API indexes.
// In particular 0 is INVALID, 1 is OK and 2 is CANCEL.
const confirmButton = DFL?.GamepadButton?.OK ?? 1;
const cancelButton = DFL?.GamepadButton?.CANCEL ?? 2;
function onboardingButtonValue(value: unknown): number | null {
  if (typeof value === "number" && Number.isFinite(value)) return value;
  if (typeof value !== "string") return null;
  const normalized = value.trim().toLowerCase();
  if (normalized === "a" || normalized === "ok" || normalized === "confirm" || normalized === "button_a") return confirmButton;
  if (normalized === "b" || normalized === "cancel" || normalized === "back" || normalized === "button_b") return cancelButton;
  if (/^[12]$/.test(normalized)) return Number(normalized);
  return null;
}
export function bindOnboardingInput(document: Document, input = createOnboardingInputState()) {
  const { pressed, suppressed, pressedAt } = input;
  const down = (event: Event) => {
    const detail = (event as CustomEvent).detail;
    const button = onboardingButtonValue(detail?.button);
    // Keep both actions inside the guide so the first A can never focus the QAM.
    if (button !== confirmButton && button !== cancelButton) return;
    if (button === null) return;
    const now = Date.now();
    // Steam can deliver the release to the old BrowserView after focus has
    // already moved to the next slide. A later, non-repeat down is a new
    // physical press and must not be discarded because that release was lost.
    const staleHeld = pressed.has(button) && !detail?.is_repeat
      && now - (pressedAt.get(button) ?? now) > 500;
    if (staleHeld) { pressed.delete(button); suppressed.delete(button); }
    const held = pressed.has(button);
    pressed.add(button);
    pressedAt.set(button, now);
    if (detail?.is_repeat || held) { if (suppressed.has(button)) { event.preventDefault(); event.stopPropagation(); } return; }
    const guide = getPlayhubOnboardingSnapshot();
    // Anche fra una slide confermata e la successiva l'input resta della guida:
    // altrimenti quella pressione finisce sul menu di Steam dietro.
    if (!guide.view && guide.advancing !== true) return;
    // A advances the guide; B is intentionally consumed but never dismisses it.
    // The runtime queues a press while the visible slide is settling.
    if (button === confirmButton) reportPlayhubOnboardingAction("confirm");
    suppressed.add(button); event.preventDefault(); event.stopPropagation();
  };
  const up = (event: Event) => {
    const button = onboardingButtonValue((event as CustomEvent).detail?.button);
    if (button === null) return;
    pressed.delete(button);
    pressedAt.delete(button);
    if (suppressed.delete(button)) { event.preventDefault(); event.stopPropagation(); }
  };
  const keydown = (event: KeyboardEvent) => {
    const active = () => { const guide = getPlayhubOnboardingSnapshot(); return !!guide.view || guide.advancing === true; };
    if (event.key === "Escape" && active()) {
      event.preventDefault(); event.stopPropagation(); return;
    }
    if (event.key !== "Enter" || event.isComposing || event.altKey || event.ctrlKey || event.metaKey) return;
    const target = event.target as Element | null;
    if (target?.closest?.('input, textarea, select, [contenteditable="true"], [role="textbox"]')
      || (target?.closest?.('[role="dialog"], [aria-modal="true"]')
        && !target.closest('[data-playhub-onboarding-overlay="true"]'))) return;
    if (event.repeat) {
      if (getPlayhubOnboardingSnapshot().view) { event.preventDefault(); event.stopPropagation(); }
      return;
    }
    const held = pressed.has(-1); pressed.add(-1);
    if (held) {
      if (suppressed.has(-1)) { event.preventDefault(); event.stopPropagation(); }
      return;
    }
    if (!active()) return;
    reportPlayhubOnboardingAction("confirm");
    suppressed.add(-1); event.preventDefault(); event.stopPropagation();
  };
  const keyup = (event: KeyboardEvent) => {
    if (event.key !== "Enter") return;
    pressed.delete(-1);
    if (suppressed.delete(-1)) { event.preventDefault(); event.stopPropagation(); }
  };
  document.addEventListener("vgp_onbuttondown", down, true);
  document.addEventListener("vgp_onbuttonup", up, true);
  document.addEventListener("keydown", keydown, true);
  document.addEventListener("keyup", keyup, true);
  const resetInput = () => { pressed.clear(); suppressed.clear(); pressedAt.clear(); };
  document.defaultView?.addEventListener("blur", resetInput);
  return () => { document.removeEventListener("vgp_onbuttondown", down, true); document.removeEventListener("vgp_onbuttonup", up, true);
    document.removeEventListener("keydown", keydown, true); document.removeEventListener("keyup", keyup, true);
    document.defaultView?.removeEventListener("blur", resetInput); resetInput(); };
}

export function installPlayhubOnboarding(getLocale: () => string): () => void {
  const native = createOnboardingNativeAdapter();
  const host = window as any;
  const name = "PlayhubOnboarding140";
  let stopped = false;
  let replayAwaitClose = false;
  let wasQamOpen = false;
  let fadeTimer: ReturnType<typeof setTimeout> | undefined;
  let openCheckTimer: ReturnType<typeof setTimeout> | undefined;
  let rejectOpening: ((reason: Error) => void) | undefined;
  const cancelOpening = () => {
    if (fadeTimer !== undefined) clearTimeout(fadeTimer);
    if (openCheckTimer !== undefined) clearTimeout(openCheckTimer);
    fadeTimer = openCheckTimer = undefined;
    rejectOpening?.(new Error("onboarding_open_cancelled")); rejectOpening = undefined;
  };
  let currentDocument: Document | null = null;
  let mainDocument: Document | null = null;
  const stability = createOnboardingBoundsStability();
  let stabilityFrame: number | undefined;
  let frameWindow: Window | null = null;
  let sampling = false;
  const resetStability = () => {
    if (stabilityFrame !== undefined) frameWindow?.cancelAnimationFrame(stabilityFrame);
    stabilityFrame = undefined; stability.reset();
  };
  let stopMainObserver = () => {};
  const inputState = createOnboardingInputState();
  const boundInputs = new Map<Document, () => void>();
  const bindInputs = (documents: (Document | null | undefined)[]) => {
    const wanted = new Set(documents.filter((value): value is Document => !!value));
    for (const [document, stop] of boundInputs) {
      if (!wanted.has(document)) { stop(); boundInputs.delete(document); }
    }
    for (const document of wanted) {
      if (!boundInputs.has(document)) boundInputs.set(document, bindOnboardingInput(document, inputState));
    }
  };
  const stopAllInputs = () => { boundInputs.forEach(stop => stop()); boundInputs.clear(); };
  let manager: any;
  let modalSubscription: any;
  let notify = () => {};
  let schedulePending = false;
  const schedule = () => {
    if (stopped || schedulePending) return;
    schedulePending = true;
    queueMicrotask(() => { schedulePending = false; if (!stopped) notify(); });
  };
  const qamDocumentChanged = (registered?: Document) => {
    bindInputs([mainDocument, registered, ...qamDocuments.keys()]);
    if (mainDocument) invalidateOnboardingBrowserViewOwners(mainDocument);
    schedule();
  };
  const tour = observeSteamTourCompletion(host.SteamClient, schedule);
  let registry: any;
  try { registry = (DFL as any).findModuleExport?.((value: any) => value?.m_mapModalManager?.get
    && typeof value.RegisterModalManager === "function"); } catch { /* Unknown modal ownership blocks the guide. */ }
  const main = () => native.router?.WindowStore?.GamepadUIMainWindowInstance;
  const environment = (): OnboardingEnvironment => {
    const instance = main();
    const browser = instance?.BrowserWindow ?? instance?.m_BrowserWindow;
    const nextMainDocument: Document | null = browser?.document ?? null;
    if (nextMainDocument !== mainDocument) {
      resetStability();
      stopMainObserver(); mainDocument = nextMainDocument;
      tour.gate.observeRenderer(false, false, null);
      stopMainObserver = mainDocument ? observeOnboardingAnchors(mainDocument, schedule) : () => {};
    }
    const menu = instance?.MenuStore ?? instance?.m_MenuStore;
    const qamOpen = menu?.GetOpenSideMenu?.() === 2;
    if (qamOpen && !wasQamOpen && mainDocument) invalidateOnboardingBrowserViewOwners(mainDocument);
    wasQamOpen = qamOpen;
    if (!qamOpen) replayAwaitClose = false;
    const playhubTabId = resolveOnboardingPlayhubTabId(host);
    const candidates = [...qamDocuments.keys()];
    for (const win of native.router?.WindowStore?.SteamUIWindows ?? []) {
      try { const doc = (win.BrowserWindow ?? win.m_BrowserWindow)?.document; if (doc) candidates.push(doc); } catch {}
    }
    const document = qamOpen ? (mainDocument ? findOnboardingBrowserViewDocument(mainDocument, playhubTabId) : null)
      ?? selectOnboardingQamDocument(mainDocument, candidates, playhubTabId) : mainDocument;
    currentDocument = document;
    bindInputs([mainDocument, document, ...candidates]);
    // Read only the registered manager. GetModalManager would create an empty one.
    const nextManager = instance?.m_ModalManager ?? (browser ? registry?.m_mapModalManager?.get(browser) : undefined);
    if (manager !== nextManager) {
      modalSubscription?.Unregister?.(); manager = nextManager;
      modalSubscription = manager?.ModalCountChangedCallbacks?.Register?.(schedule);
    }
    const probe = mainDocument ? probeSteamTourRenderer(mainDocument) : { known: false, active: false, startupKnown: false, startupPlaying: false };
    tour.gate.observeRenderer(probe.known, probe.active, Array.isArray(manager?.modals) ? manager.modals.length : null);
    const selected = Number(menu?.GetQuickAccessTab?.()) === playhubTabId;
    const panel = document?.getElementById(`quickaccess_content_${playhubTabId}`);
    const controls = panel?.querySelector('[data-playhub-onboarding-controls="true"]');
    const active = controls?.getAttribute("data-onboarding-active-tab");
    const controlStep = ["home", "decky", "store", "customize", "audio", "performance", "graphics", "controller"].includes(active ?? "") ? active as OnboardingControlStep : null;
    let steps: OnboardingStep[] = [];
    try {
      const visible = JSON.parse(controls?.getAttribute("data-onboarding-visible-tabs") ?? "null");
      if (Array.isArray(visible)) steps = onboardingVisibleSteps(visible) as OnboardingStep[];
    } catch { /* Wait for committed navigation markers. */ }
    const contentSelector = controlStep === "decky" ? '[data-playhub-decky-host="true"]'
      : `[data-playhub-onboarding-content="${controlStep}"]`;
    const controlReady = !!document && !!controlStep && controls?.getAttribute("data-onboarding-content-ready") === "true"
      && visibleOnboardingContent(document, contentSelector);
    // Native bounds first; Steam's own placeholder element is an equally exact fallback.
    const qamBounds = qamOpen && mainDocument && document
      ? readOnboardingBrowserViewBounds(mainDocument, document) ?? readOnboardingQamPlaceholderBounds(mainDocument) : null;
    const viewport = { width: mainDocument?.defaultView?.innerWidth ?? 0, height: mainDocument?.defaultView?.innerHeight ?? 0 };
    const state = getPlayhubOnboardingSnapshot().state;
    const geometryVisible = qamOpen && !replayAwaitClose && !state.paused && !state.completed && !!qamBounds
      && !!document && visibleOnboardingContent(document, `#quickaccess_tab_${playhubTabId}`, true);
    if (!geometryVisible) resetStability();
    else {
      const nextFrameWindow = mainDocument ? onboardingAnimationFrameHost(mainDocument) : null;
      if (stabilityFrame !== undefined && frameWindow !== nextFrameWindow) resetStability();
      if (!stability.matches(qamBounds, viewport)) stability.reset();
      // Sample on rendered frames only, never on repeated environment reads or fixed timers.
      if (!sampling && !stability.ready() && stabilityFrame === undefined && !stopped) {
        frameWindow = nextFrameWindow;
        stabilityFrame = frameWindow?.requestAnimationFrame(() => {
          stabilityFrame = undefined; sampling = true;
          try {
            const next = environment();
            stability.sample(next.qamBounds ?? null, next.geometryVisible === true, {
              width: next.mainDocument?.defaultView?.innerWidth ?? 0,
              height: next.mainDocument?.defaultView?.innerHeight ?? 0 });
          } finally { sampling = false; }
          schedule();
        });
      }
    }
    return { document, mainDocument, qamBounds, qamOpen, playhubTabId, controlStep, controlReady, steps,
      geometryVisible, layoutStable: geometryVisible && stability.ready(),
      controlsReady: controls?.getAttribute("data-onboarding-ready") === "true",
      storeVisible: controls?.getAttribute("data-onboarding-store-visible") === "true",
      steamReady: !replayAwaitClose && tour.gate.ready() && probe.startupKnown && !probe.startupPlaying
        && (controls?.getAttribute("data-onboarding-customizing") !== "true" || controls?.getAttribute("data-onboarding-showcasing") === "true"),
      playhubSelected: qamOpen && selected && controls?.getAttribute("data-onboarding-ready") === "true",
      deckyEnabled: controls?.getAttribute("data-onboarding-decky-enabled") === "true",
      deckyVisible: controls?.getAttribute("data-onboarding-decky-visible") === "true" };
  };
  const options: PlayhubOnboardingOptions = { native, getEnvironment: environment,
    showControlStep(step) {
      requestOnboardingControlStep(step);
      if (step === null) queueMicrotask(() => {
        if (getPlayhubOnboardingSnapshot().state.completed) {
          stopMainObserver(); resetStability(); mainDocument = null;
        }
      });
    },
    subscribeEnvironment(listener) {
      notify = listener;
      const stopReaction = typeof native.autorun === "function" ? native.autorun(() => {
        if (!getPlayhubOnboardingSnapshot().state.completed) { environment(); schedule(); }
      }) : () => {};
      host.addEventListener("shortcuts:qam-bridge-changed", schedule);
      host.addEventListener("focus", schedule);
      documentListeners.add(qamDocumentChanged);
      return () => { stopReaction(); documentListeners.delete(qamDocumentChanged); host.removeEventListener("shortcuts:qam-bridge-changed", schedule); host.removeEventListener("focus", schedule); };
    },
    openQuickAccess() {
      cancelOpening();
      return new Promise<void>((resolve, reject) => {
        rejectOpening = reject;
        fadeTimer = setTimeout(() => {
          fadeTimer = undefined;
          try {
            const snapshot = getPlayhubOnboardingSnapshot();
            const navigation = (DFL as any).Navigation;
            if (stopped || snapshot.view !== "intro" || !snapshot.introExiting || snapshot.state.paused
              || !environment().steamReady || typeof navigation?.OpenQuickAccessMenu !== "function") {
              cancelOpening(); return;
            }
            navigation.OpenQuickAccessMenu();
          } catch { cancelOpening(); return; }
          openCheckTimer = setTimeout(() => {
            openCheckTimer = undefined; rejectOpening = undefined;
            try {
              if (environment().qamOpen) resolve(); else reject(new Error("onboarding_qam_not_open"));
            } catch { reject(new Error("onboarding_qam_unavailable")); }
          }, 1000);
        }, 200);
      });
    },
    closeQuickAccess() {
      const close = (DFL as any).Navigation?.CloseSideMenus;
      if (typeof close === "function") close();
    },
    enterPlayhub() {
      const env = environment();
      if (env.steamReady && env.qamOpen && env.document?.getElementById(`quickaccess_tab_${env.playhubTabId}`)) {
        const instance = main();
        (instance?.MenuStore ?? instance?.m_MenuStore)?.OpenQuickAccessMenu?.(env.playhubTabId);
      }
    },
  };
  let stopRuntime = initPlayhubOnboarding(options);
  let preview = false;
  const previewKey = Symbol.for("playhub.onboarding.preview.v1");
  const previewApi = {
    start() {
      if (stopped) return;
      const close = (DFL as any).Navigation?.CloseSideMenus;
      if (typeof close !== "function") return;
      replayAwaitClose = true;
      try { close(); } catch { replayAwaitClose = false; return; }
      stopRuntime(); cancelOpening(); resetStability(); preview = true;
      const memory = new Map<string, string>();
      stopRuntime = initPlayhubOnboarding({ ...options, storage: {
        getItem: key => memory.get(key) ?? null, setItem: (key, value) => { memory.set(key, value); }
      } });
      return previewApi.getState();
    },
    stop() { if (!stopped && preview) { stopRuntime(); cancelOpening(); resetStability(); preview = false; stopRuntime = initPlayhubOnboarding(options); } },
    getState() { const state = getPlayhubOnboardingSnapshot(); return { preview, step: state.state.step, view: state.view,
      paused: state.state.paused, completed: state.state.completed, steamReady: state.steamReady,
      qamOpen: state.qamOpen, layoutStable: state.layoutStable, documentTitle: state.document?.title ?? null,
      controlStep: state.controlStep, controlReady: state.controlReady, controlsReady: state.controlsReady,
      playhubSelected: state.playhubSelected,
      anchorDocumentTitle: state.anchorDocumentTitle, qamBounds: state.qamBounds, anchor: state.anchor,
      width: state.width, height: state.height, footerInset: state.footerInset,
      registeredQamDocuments: qamDocuments.size }; },
  };
  host[previewKey] = previewApi;
  const replay = () => { previewApi.start(); };
  replayGuide = replay;
  const Surface = () => {
    React.useSyncExternalStore(subscribeLocaleRefresh, getLocale);
    return <PlayhubOnboarding locale={getLocale()} />;
  };
  // Native controller/environment changes also refresh localized copy without a timer.
  function subscribeLocaleRefresh(listener: () => void) {
    localeListeners.add(listener);
    return () => { localeListeners.delete(listener); };
  }
  try { routerHook.addGlobalComponent(name, Surface); } catch { stopRuntime(); }
  return () => {
    if (stopped) return;
    stopped = true; stopRuntime(); stopAllInputs(); stopMainObserver(); modalSubscription?.Unregister?.(); tour.stop();
    cancelOpening(); resetStability();
    if (replayGuide === replay) replayGuide = undefined;
    if (host[previewKey] === previewApi) delete host[previewKey];
    routerHook.removeGlobalComponent(name);
  };
}

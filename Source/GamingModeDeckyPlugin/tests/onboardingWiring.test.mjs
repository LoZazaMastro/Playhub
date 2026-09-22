import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";
function load(file, context = {}) {
  const exports = {};
  const source = ts.transpileModule(readFileSync(new URL(`../src/${file}`, import.meta.url), "utf8"), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX }
  }).outputText;
  vm.runInNewContext(source, { exports, ...context }); return exports;
}
const gateApi = load("onboardingSteamGate.ts");
test("Steam first-run unknown blocks; seen-at-mount cannot release an active tour", () => {
  const g = gateApi.createSteamTourGate();
  assert.equal(g.ready(), false);
  g.setHistoricalSeen(true); assert.equal(g.ready(), false, "storage alone is not readiness");
  g.observeRenderer(true, true, 1); assert.equal(g.ready(), false);
  g.setHistoricalSeen(true); assert.equal(g.ready(), false);
  g.observeRenderer(true, false, 1); assert.equal(g.ready(), false, "modal still exists");
  g.observeRenderer(true, false, 0); assert.equal(g.ready(), true);
  g.observeRenderer(true, true, 1); assert.equal(g.ready(), false, "replay blocks immediately");
  g.observeRenderer(false, false, 0); assert.equal(g.ready(), false, "route unmount is not completion");
});
test("first-run with no history needs observed lifecycle completion; existing users need known renderer and manager", () => {
  const g = gateApi.createSteamTourGate();
  g.setHistoricalSeen(false); g.observeRenderer(true, false, 0); assert.equal(g.ready(), false);
  g.observeRenderer(true, true, 1); g.observeRenderer(true, false, 0); assert.equal(g.ready(), true);
  const old = gateApi.createSteamTourGate(); old.setHistoricalSeen(true);
  old.observeRenderer(true, false, null); assert.equal(old.ready(), false);
  old.observeRenderer(true, false, 0); assert.equal(old.ready(), true);
});
test("read-only probe checks current committed native renderer, not an independent tour hook", () => {
  function nativeRenderer() { const { bShowTour, onComplete } = neverCalled(); return bShowTour ? { active: !0, onComplete } : null; }
  const tour = { type: nativeRenderer, child: {} };
  const root = { child: tour, memoizedProps: { bPlayingStartupMovie: false } }; root.stateNode = { current: root };
  const element = { __reactFiber$test: { return: root } };
  const doc = { querySelectorAll: () => [element] };
  assert.equal(gateApi.probeSteamTourRenderer(doc).active, true);
  assert.equal(gateApi.probeSteamTourRenderer(doc).startupKnown, true);
  root.memoizedProps.bPlayingStartupMovie = true;
  assert.equal(gateApi.probeSteamTourRenderer(doc).startupPlaying, true);
  tour.child = null; assert.equal(gateApi.probeSteamTourRenderer(doc).active, false);
  root.child = null; assert.equal(gateApi.probeSteamTourRenderer(doc).known, false);
});
test("native completion subscription is read-only and fully released", async () => {
  let callback; let count = 0; let unregistered = 0;
  const client = { Storage: { GetString: async () => "1" }, OpenVR: { PathProperties: {
    RegisterForPathPropertyChange: (_path, fn) => { callback = fn; return () => { unregistered++; }; },
    GetInt32PathProperty: async () => 7
  } } };
  const result = gateApi.observeSteamTourCompletion(client, () => count++);
  await new Promise(resolve => setImmediate(resolve));
  result.gate.observeRenderer(true, true, 1); await callback();
  assert.equal(result.gate.ready(), false);
  result.gate.observeRenderer(true, false, 0); assert.equal(result.gate.ready(), true);
  result.stop(); const before = count; await callback(); assert.equal(count, before); assert.equal(unregistered, 1);
});
const jsx = { jsx: (type, props) => ({ type, props }), jsxs: (type, props) => ({ type, props }), Fragment: "Fragment" };
const native = load("onboardingNative.tsx", { require: name => name === "./decky" ? { DFL: {}, SP_REACT: {} } : jsx });
test("cached custom QAM chord is controller-specific and rejects complex or stale bindings", () => {
  const source = { key: 3, active_group: { inputs: [{ key: 8, activators: [{ activation: 1,
    bindings: [{ type: 7, controller_action: { action: 61 } }] }] }] } };
  const store = { ChordConfiguration: { url: "config://custom", sets: [{ source_bindings: [source] }] },
    CurrentAppConfigInfo: (app, index) => { assert.equal(app, 443510); return { URL: index === 2 ? "config://custom" : "config://other" }; } };
  assert.equal(native.readOnboardingChords(store, { nControllerIndex: 2 }).length, 1);
  assert.equal(native.readOnboardingChords(store, { nControllerIndex: 3 }).length, 0);
  source.active_group.inputs[0].activators[0].activation = 4;
  assert.equal(native.readOnboardingChords(store, { nControllerIndex: 2 }).length, 0);
  const text = readFileSync(new URL("../src/onboardingNative.tsx", import.meta.url), "utf8");
  assert.doesNotMatch(text, /\.(?:LoadChordConfig|PreviewConfigForAppAndController|SetSelectedConfigForApp)\s*\(/);
});
test("while a slide is on screen the guide owns confirm and back, including matching release", () => {
  const handlers = new Map(); const actions = [];
  const api = load("onboardingIntegration.tsx", { require: name => name === "./PlayhubOnboarding" ? {
    getPlayhubOnboardingSnapshot: () => ({ view: "intro" }),
    reportPlayhubOnboardingAction: action => { actions.push(action); return true; }
  } : {} });
  const doc = { addEventListener: (name, fn) => handlers.set(name, fn), removeEventListener: name => handlers.delete(name) };
  const stop = api.bindOnboardingInput(doc);
  const make = button => ({ detail: { button }, stopped: 0, prevented: 0, preventDefault() { this.prevented++; }, stopPropagation() { this.stopped++; } });
  const guide = make(27); handlers.get("vgp_onbuttondown")(guide); assert.equal(guide.stopped, 0);
  const qam = make(28); handlers.get("vgp_onbuttondown")(qam); assert.equal(qam.stopped, 0);
  for (const button of [1, 2]) {
    const event = make(button); handlers.get("vgp_onbuttondown")(event); assert.equal(event.stopped, 1);
    const up = make(button); handlers.get("vgp_onbuttonup")(up); assert.equal(up.stopped, 1);
  }
  // The presentation is completed, never dismissed: B is consumed and advances nothing.
  assert.deepEqual(actions, ["confirm"]); stop(); assert.equal(handlers.size, 0);
});
test("a confirm is reported once per fresh press, whatever it is aimed at", () => {
  const handlers = new Map(); const actions = [];
  const api = load("onboardingIntegration.tsx", { require: name => name === "./PlayhubOnboarding" ? {
    getPlayhubOnboardingSnapshot: () => ({ view: "playhub" }),
    reportPlayhubOnboardingAction: action => { actions.push(action); return true; }
  } : {} });
  const stop = api.bindOnboardingInput({ addEventListener: (name, fn) => handlers.set(name, fn), removeEventListener() {} });
  handlers.get("vgp_onbuttondown")({ detail: { button: 1 }, target: { closest: () => ({}) },
    preventDefault() {}, stopPropagation() {} });
  assert.deepEqual(actions, ["confirm"]); stop();
});

test("a fresh A survives a release lost while Steam moves focus between QAM BrowserViews", () => {
  const handlers = new Map(); const actions = [];
  const api = load("onboardingIntegration.tsx", { require: name => name === "./PlayhubOnboarding" ? {
    getPlayhubOnboardingSnapshot: () => ({ view: "customize", advancing: false }),
    reportPlayhubOnboardingAction: action => { actions.push(action); return true; }
  } : {} });
  const input = api.createOnboardingInputState();
  const stop = api.bindOnboardingInput({ addEventListener: (name, fn) => handlers.set(name, fn), removeEventListener() {} }, input);
  const event = (repeat = false) => ({ detail: { button: 1, is_repeat: repeat }, preventDefault() {}, stopPropagation() {} });
  handlers.get("vgp_onbuttondown")(event());
  assert.deepEqual(actions, ["confirm"]);
  input.pressedAt.set(1, Date.now() - 1000);
  handlers.get("vgp_onbuttondown")(event());
  assert.deepEqual(actions, ["confirm", "confirm"], "a later physical press is not mistaken for the lost old hold");
  handlers.get("vgp_onbuttondown")(event(true));
  assert.deepEqual(actions, ["confirm", "confirm"], "an actual held repeat remains suppressed");
  stop();
});
test("pointer coach and CTA request confirmation once; pause cannot bubble and carried clicks are ignored", () => {
  for (const view of ["intro", "playhub", "store", "customize"]) {
    const actions = [];
    const snapshot = { view, document: { body: {} }, width: 1280, height: 800, footerInset: 96, revision: 0 };
    const React = { Component: class {}, useSyncExternalStore: (_sub, get) => get(), useRef: () => ({ current: null }),
      useState: () => [220, () => {}], useLayoutEffect: () => {} };
    const api = load("PlayhubOnboarding.tsx", { require: name => name === "./decky" ? { SP_REACT: React }
      : name === "react-dom" ? { createPortal: node => node }
      : name === "./onboardingRuntime" ? { getPlayhubOnboardingSnapshot: () => snapshot, subscribePlayhubOnboarding: () => () => {},
        renderPlayhubOnboardingHint: () => null, reportPlayhubOnboardingAction: action => actions.push(action) }
      : name === "./onboardingAnchors" ? { positionOnboardingCoach: () => ({ left: 20, top: 20, width: 360 }), positionOnboardingShowcase: () => null }
      : name === "./onboardingTabLocale" ? { getOnboardingTabCopy: () => ({}) }
      : name === "./onboardingLocale" ? { getOnboardingCopy: () => ({}), getOnboardingExtraCopy: () => ({}), getOnboardingActionCopy: () => ({}) } : jsx });
    const tree = api.PlayhubOnboarding({ locale: "en" });
    const nodes = [];
    const walk = node => { if (!node || typeof node !== "object") return; nodes.push(node); for (const child of [node.props?.children].flat(Infinity)) walk(child); };
    walk(tree); const coach = nodes.find(node => node.props?.role === "status");
    const event = () => ({ detail: 1, preventDefault() {}, stopPropagation() { this.stopped = true; } });
    coach.props.onClick(event()); assert.deepEqual(actions, [], "carried click has no pointerdown on this slide");
    coach.props.onPointerDown();
    const click = event(); coach.props.onClick(click); assert.equal(click.stopped, true);
    assert.deepEqual(actions, ["confirm"]);
    const buttons = nodes.filter(node => node.type === "button");
    assert.equal(buttons[0].props.style.minWidth, 0);
    assert.equal(buttons[0].props.style.maxWidth, "100%");
    assert.equal(buttons[0].props.style.whiteSpace, "normal");
    assert.equal(buttons[0].props.style.overflowWrap, "anywhere");
    coach.props.onPointerDown();
    assert.equal(buttons.length, 1, "only native confirm is shown");
    const confirm = event(); buttons[0].props.onClick(confirm); assert.equal(confirm.stopped, true);
    assert.equal(actions.at(-1), "confirm");
    if (view === "intro") {
      assert.equal(tree.props.style.backdropFilter, "blur(12px)");
      assert.equal(coach.props.style.background, "transparent");
      assert.equal(coach.props.style.textAlign, "center");
      assert.equal(coach.props.style.border, 0);
    } else {
      assert.equal(tree.props.style.backdropFilter, undefined);
      assert.equal(coach.props.style.position, "absolute");
    }
  }
});

test("held A that skips Steam video needs release and a fresh down before onboarding confirmation", () => {
  let view = null; const actions = []; const handlers = new Map();
  const api = load("onboardingIntegration.tsx", { require: name => name === "./PlayhubOnboarding" ? {
    getPlayhubOnboardingSnapshot: () => ({ view }), reportPlayhubOnboardingAction: action => { actions.push(action); return true; }
  } : {} });
  const doc = { addEventListener: (name, fn) => handlers.set(name, fn), removeEventListener: name => handlers.delete(name) };
  const stop = api.bindOnboardingInput(doc);
  const event = repeat => ({ detail: { button: 1, is_repeat: repeat }, preventDefault() {}, stopPropagation() { this.stopped = true; } });
  const skipVideo = event(false); handlers.get("vgp_onbuttondown")(skipVideo); assert.equal(skipVideo.stopped, undefined);
  view = "intro";
  handlers.get("vgp_onbuttondown")(event(true)); handlers.get("vgp_onbuttondown")(event(false));
  assert.deepEqual(actions, []);
  const release = event(false); handlers.get("vgp_onbuttonup")(release); assert.deepEqual(actions, []); assert.equal(release.stopped, undefined);
  handlers.get("vgp_onbuttondown")(event(false)); assert.deepEqual(actions, ["confirm"]); stop();
});

test("actual QAM document and dynamic owner tab ID win over main document", () => {
  const api = load("onboardingIntegration.tsx", { require: () => ({}) });
  const main = { defaultView: {}, getElementById: () => null };
  const qam = { defaultView: {}, getElementById: id => id === "quickaccess_tab_1401877092" ? {} : null };
  assert.equal(api.selectOnboardingQamDocument(main, [qam], 1401877092), qam);
  assert.equal(api.selectOnboardingQamDocument(main, [qam], 5261387), null);
  qam.defaultView.closed = true;
  assert.equal(api.selectOnboardingQamDocument(main, [qam], 1401877092), null);
});
test("plugin init/mount/unload and ControlCenter anchors/replay are wired without resume button", () => {
  const index = readFileSync(new URL("../src/index.tsx", import.meta.url), "utf8");
  const controls = readFileSync(new URL("../src/ControlCenter.tsx", import.meta.url), "utf8");
  const integration = readFileSync(new URL("../src/onboardingIntegration.tsx", import.meta.url), "utf8");
  assert.match(index, /const uninstallOnboarding = installPlayhubOnboarding/);
  // Onboarding must be torn down on dismount. Other teardowns may run first, so
  // assert it happens inside onDismount rather than that it happens first.
  const dismount = index.match(/onDismount\(\)\s*\{[\s\S]*?\n    \}/);
  assert.ok(dismount, "index.tsx must declare onDismount");
  const calls = [];
  const names = ["uninstallCircles", "disposeDisplayConfirmation", "uninstallOnboarding", "uninstallDeckyHost", "uninstallQuickSettings", "uninstallHomeNews", "clearDashboardChrome", "stopOpenRequestWatcher", "uninstallNavigationHaptics", "uninstallPowerMenuPatch", "uninstallPlayhubQam"];
  const scope = Object.fromEntries(names.map(name => [name, () => { calls.push(name); if (name === "uninstallCircles") throw Error("fixture teardown failure"); }]));
  vm.runInNewContext(`({${dismount[0]}}).onDismount()`, { ...scope, steamFocusRecovery: { uninstall() {} }, routerHook: { removeRoute() {} }, DASHBOARD_ROUTE: "/dashboard", PLUGIN_STORE_ROUTE: "/store", console: { warn() {} } });
  assert.equal(calls.filter(name => name === "uninstallOnboarding").length, 1);
  assert.equal(calls.at(-1), "uninstallPlayhubQam", "one teardown failure must not prevent later cleanup");
  assert.match(controls, /data-playhub-onboarding-controls/); assert.doesNotMatch(controls, /resumePlayhubOnboarding/);
  assert.match(controls, /replayPlayhubOnboarding\(\)/);
  assert.match(integration, /addGlobalComponent\(name, Surface\)/); assert.match(integration, /removeGlobalComponent\(name\)/);
  assert.doesNotMatch(integration, /GetModalManager\s*\(/);
  assert.match(integration, /nextFrameWindow = mainDocument \? onboardingAnimationFrameHost\(mainDocument\)/);
  assert.match(integration, /frameWindow !== nextFrameWindow\) resetStability\(\)/);
  assert.doesNotMatch(integration, /frameWindow = mainDocument\?\.defaultView/);
});

test("B cannot leave the guide or open Steam's side menus behind it", () => {
  const handlers = new Map(); const actions = [];
  const api = load("onboardingIntegration.tsx", { require: name => name === "./PlayhubOnboarding" ? {
    getPlayhubOnboardingSnapshot: () => ({ view: "decky" }),
    reportPlayhubOnboardingAction: action => { actions.push(action); return true; }
  } : {} });
  const stop = api.bindOnboardingInput({ addEventListener: (name, fn) => handlers.set(name, fn), removeEventListener() {} });
  let stopped = 0;
  const event = { detail: { button: 2 }, preventDefault() {}, stopPropagation() { stopped++; } };
  handlers.get("vgp_onbuttondown")(event); handlers.get("vgp_onbuttonup")(event);
  assert.equal(stopped, 2, "B is consumed while a slide is on screen");
  assert.deepEqual(actions, [], "and it neither pauses nor advances the guide");
  stop();
});

test("onboarding follows current loader owner ID across legacy and standalone Decky hosting", () => {
  const api = load("onboardingIntegration.tsx", { require: () => ({}) });
  const host = { DeckyPluginLoader: { tabsHook: { tabs: [{ id: 42, __shortcutsPlugin: "Playhub" }] } },
    __TABS_HOOK_INSTANCE: { tabs: [{ id: 123, __shortcutsPlugin: "Playhub" }] } };
  assert.equal(api.resolveOnboardingPlayhubTabId(host), 42);
  host.DeckyPluginLoader.tabsHook.tabs = [{ id: 999 }];
  assert.equal(api.resolveOnboardingPlayhubTabId(host), 0x50484B);
  delete host.DeckyPluginLoader;
  assert.equal(api.resolveOnboardingPlayhubTabId(host), 123);
});

test("integration samples QAM stability on animation frames, not reads; movement and close hide immediately", async () => {
  const anchors = load("onboardingAnchors.ts");
  const frames = new Map(); let id = 0, options, open = true, visible = true, invalidations = 0, confirmations = 0;
  let bounds = { left: 900, top: 0, width: 855, height: 682 };
  const view = { innerWidth: 1280, innerHeight: 800, addEventListener() {}, removeEventListener() {},
    requestAnimationFrame: fn => { frames.set(++id, fn); return id; }, cancelAnimationFrame: key => frames.delete(key) };
  const doc = { defaultView: view, body: {}, addEventListener() {}, removeEventListener() {}, getElementById: () => null };
  const qam = { ...doc };
  const snapshot = { view: "playhub", state: { paused: false, completed: false } };
  const host = { SteamClient: {}, addEventListener() {}, removeEventListener() {} };
  const api = load("onboardingIntegration.tsx", { window: host, queueMicrotask,
    require: name => name === "./decky" ? { DFL: {}, routerHook: { addGlobalComponent() {}, removeGlobalComponent() {} } }
      : name === "./onboardingNative" ? { createOnboardingNativeAdapter: () => ({ router: { WindowStore: {
        GamepadUIMainWindowInstance: { BrowserWindow: { document: doc }, m_ModalManager: { modals: [] },
          MenuStore: { GetOpenSideMenu: () => open ? 2 : 0 } } } } }) }
      : name === "./onboardingSteamGate" ? { observeSteamTourCompletion: () => ({ gate: { observeRenderer() {}, ready: () => true }, stop() {} }),
        probeSteamTourRenderer: () => ({ known: true, startupKnown: true }) }
      : name === "./onboardingAnchors" ? { ...anchors, observeOnboardingAnchors: () => () => {},
        invalidateOnboardingBrowserViewOwners: () => invalidations++,
        findOnboardingBrowserViewDocument: () => qam, readOnboardingBrowserViewBounds: () => bounds,
        visibleOnboardingContent: () => visible }
      : name === "./PlayhubOnboarding" ? { getPlayhubOnboardingSnapshot: () => snapshot,
        reportPlayhubOnboardingAction: action => { if (action === "confirm") confirmations++; return true; },
        initPlayhubOnboarding: value => { options = value; return value.subscribeEnvironment(() => value.getEnvironment()); } } : {} });
  const stop = api.installPlayhubOnboarding(() => "en");
  options.getEnvironment();
  const beforeRegistration = invalidations;
  const unregister = api.registerOnboardingQamDocument(qam);
  assert.equal(invalidations, beforeRegistration + 1, "late QAM mount invalidates the main document's cached early discovery miss");
  // Real regression: a freshly mounted QAM BrowserView can receive focus before
  // the scheduled environment refresh. If its input listener is also deferred,
  // the first A merely transfers focus and only the second advances the slide.
  const lateHandlers = new Map();
  const lateQam = { ...qam, addEventListener: (name, fn) => lateHandlers.set(name, fn),
    removeEventListener: name => lateHandlers.delete(name) };
  const unregisterLate = api.registerOnboardingQamDocument(lateQam);
  assert.equal(typeof lateHandlers.get("vgp_onbuttondown"), "function", "new focus owner is bound in its mount turn");
  const firstA = { detail: { button: 1 }, preventDefault() { this.prevented = true; }, stopPropagation() { this.stopped = true; } };
  lateHandlers.get("vgp_onbuttondown")(firstA);
  lateHandlers.get("vgp_onbuttonup")({ detail: { button: 1 }, preventDefault() {}, stopPropagation() {} });
  assert.equal(confirmations, 1, "the first fresh A advances instead of being absorbed by QAM focus");
  assert.equal(firstA.prevented, true); assert.equal(firstA.stopped, true);
  unregisterLate();
  for (let i = 0; i < 20; i++) assert.equal(options.getEnvironment().layoutStable, false);
  assert.equal(frames.size, 1);
  const frame = async () => { const [key, fn] = frames.entries().next().value; frames.delete(key); fn(); await Promise.resolve(); };
  for (let i = 0; i < 3; i++) { await frame(); assert.equal(options.getEnvironment().layoutStable, false); }
  await frame(); assert.equal(options.getEnvironment().layoutStable, true);
  assert.equal(frames.size, 0, "stable QAM stops the frame loop");
  for (let i = 0; i < 20; i++) options.getEnvironment();
  assert.equal(frames.size, 0, "unchanged reads do not restart sampling");
  bounds = { ...bounds, left: 920 };
  assert.equal(options.getEnvironment().layoutStable, false);
  for (let i = 0; i < 4; i++) await frame();
  assert.equal(options.getEnvironment().layoutStable, true);
  visible = false; assert.equal(options.getEnvironment().layoutStable, false); assert.equal(frames.size, 0);
  visible = true; options.getEnvironment(); assert.equal(frames.size, 1);
  open = false; assert.equal(options.getEnvironment().layoutStable, false); assert.equal(frames.size, 0);
  unregister(); stop(); assert.equal(frames.size, 0);
});

test("Enter requires fresh down/up across documents and readiness, ignores editors and prevents native synthetic click", () => {
  let ready = false, advances = 0;
  const api = load("onboardingIntegration.tsx", { require: name => name === "./PlayhubOnboarding" ? {
    getPlayhubOnboardingSnapshot: () => ({ view: ready ? "playhub" : null }),
    reportPlayhubOnboardingAction: () => { if (!ready) return false; advances++; return true; }
  } : {} });
  const input = api.createOnboardingInputState();
  const main = new Map(), qam = new Map();
  const bind = handlers => api.bindOnboardingInput({ addEventListener: (name, fn) => handlers.set(name, fn),
    removeEventListener: name => handlers.delete(name) }, input);
  const stopMain = bind(main), stopQam = bind(qam);
  const event = (extra = {}) => ({ key: "Enter", target: { closest: () => null },
    preventDefault() { this.prevented = true; }, stopPropagation() {}, ...extra });
  main.get("keydown")(event()); ready = true;
  qam.get("keydown")(event()); assert.equal(advances, 0, "startup key cannot carry into guide");
  qam.get("keyup")(event());
  const fresh = event(); main.get("keydown")(fresh);
  assert.equal(advances, 1); assert.equal(fresh.prevented, true, "prevent default keyboard button activation");
  qam.get("keydown")(event({ repeat: true })); assert.equal(advances, 1);
  const up = event(); qam.get("keyup")(up); assert.equal(up.prevented, true);
  main.get("keydown")(event({ target: { closest: () => ({}) } })); assert.equal(advances, 1);
  main.get("keydown")(event()); assert.equal(advances, 2);
  stopMain(); stopQam(); assert.equal(main.size, 0); assert.equal(qam.size, 0);
});

test("lost Enter release clears on popup blur or detach without advancing", () => {
  let count = 0; const handlers = new Map(), windowHandlers = new Map();
  const api = load("onboardingIntegration.tsx", { require: name => name === "./PlayhubOnboarding" ? {
    getPlayhubOnboardingSnapshot: () => ({ view: "playhub" }),
    reportPlayhubOnboardingAction: () => { count++; return true; }
  } : {} });
  const input = api.createOnboardingInputState();
  const stop = api.bindOnboardingInput({ defaultView: { addEventListener: (name, fn) => windowHandlers.set(name, fn), removeEventListener: name => windowHandlers.delete(name) },
    addEventListener: (name, fn) => handlers.set(name, fn), removeEventListener: name => handlers.delete(name) }, input);
  const event = repeat => ({ key: "Enter", repeat, preventDefault() {}, stopPropagation() {} });
  handlers.get("keydown")(event(false)); assert.equal(count, 1);
  windowHandlers.get("blur")(); assert.equal(count, 1); assert.equal(input.pressed.size, 0);
  const repeated = event(true); repeated.preventDefault = () => { repeated.prevented = true; };
  handlers.get("keydown")(repeated); assert.equal(count, 1);
  assert.equal(input.pressed.size, 0, "repeat does not become a new held press after lost release");
  assert.equal(repeated.prevented, true, "repeat cannot activate the native focused button as a synthetic click");
  windowHandlers.get("blur")(); handlers.get("keydown")(event(false)); assert.equal(count, 2);
  stop(); assert.equal(input.pressed.size, 0); assert.equal(input.suppressed.size, 0); assert.equal(windowHandlers.size, 0);
});

test("native QAM opener times out cleanly and replay/unload cancel pending intro timers", async () => {
  const timers = new Map(); let timerId = 0, options, opens = 0, closes = 0, qamOpen = false, environmentReads = 0;
  const doc = { body: {}, addEventListener() {}, removeEventListener() {}, getElementById: () => null };
  const host = { SteamClient: {}, addEventListener() {}, removeEventListener() {} };
  const snapshot = { view: "intro", introExiting: true, state: { paused: false, completed: false } };
  const api = load("onboardingIntegration.tsx", { window: host,
    setTimeout: (fn, ms) => { const id = ++timerId; timers.set(id, { fn, ms }); return id; },
    clearTimeout: id => timers.delete(id), queueMicrotask,
    require: name => name === "./decky" ? { DFL: { Navigation: {
      OpenQuickAccessMenu: () => { opens++; }, CloseSideMenus: () => { closes++; qamOpen = false; }
    } }, routerHook: { addGlobalComponent() {}, removeGlobalComponent() {} } }
      : name === "./onboardingNative" ? { createOnboardingNativeAdapter: () => ({ router: { WindowStore: {
        GamepadUIMainWindowInstance: { BrowserWindow: { document: doc }, m_ModalManager: { modals: [] },
          MenuStore: { GetOpenSideMenu: () => { environmentReads++; return qamOpen ? 2 : 0; } } }
      } } }) }
      : name === "./onboardingSteamGate" ? { observeSteamTourCompletion: () => ({ gate: { observeRenderer() {}, ready: () => true }, stop() {} }),
        probeSteamTourRenderer: () => ({ known: true, active: false, startupKnown: true, startupPlaying: false }) }
      : name === "./onboardingAnchors" ? { observeOnboardingAnchors: () => () => {},
        createOnboardingBoundsStability: () => ({ reset() {}, ready: () => false }),
        findOnboardingBrowserViewDocument: () => null, readOnboardingBrowserViewBounds: () => null }
      : name === "./PlayhubOnboarding" ? { getPlayhubOnboardingSnapshot: () => snapshot,
        initPlayhubOnboarding: value => {
          assert.equal(qamOpen, false, "replay must close native QAM before initializing the intro");
          options = value; return () => {};
        } } : {} });
  const tick = ms => { const [id, timer] = [...timers].find(([, timer]) => timer.ms === ms); timers.delete(id); timer.fn(); };
  const stop = api.installPlayhubOnboarding(() => "en");
  const preview = host[Symbol.for("playhub.onboarding.preview.v1")];
  const readsBefore = environmentReads;
  assert.equal(preview.getState().view, "intro");
  assert.equal(environmentReads, readsBefore, "diagnostic snapshot must never refresh/subcribe/mutate environment");
  const failed = assert.rejects(options.openQuickAccess(), /qam_not_open/);
  tick(200); assert.equal(opens, 1); tick(1000); await failed;
  const replayCancelled = assert.rejects(options.openQuickAccess(), /open_cancelled/);
  qamOpen = true;
  api.replayPlayhubOnboarding(); await replayCancelled; assert.equal(timers.size, 0);
  assert.equal(closes, 1);
  qamOpen = true; preview.start(); assert.equal(qamOpen, false); assert.equal(closes, 2);
  const unloadCancelled = assert.rejects(options.openQuickAccess(), /open_cancelled/);
  stop(); await unloadCancelled; assert.equal(timers.size, 0); assert.equal(opens, 1);
});

/** Fra una slide confermata e la successiva la pressione non deve raggiungere Steam:
 *  e' cosi' che A finiva sul footer "Sessione" invece di portare all'ultima slide. */
test("the guide still owns input between a confirmed slide and the next one", () => {
  const handlers = new Map(); const actions = [];
  let snapshot = { view: null, advancing: true };
  const api = load("onboardingIntegration.tsx", { require: name => name === "./PlayhubOnboarding" ? {
    getPlayhubOnboardingSnapshot: () => snapshot,
    reportPlayhubOnboardingAction: action => { actions.push(action); return true; }
  } : {} });
  const stop = api.bindOnboardingInput({ addEventListener: (name, fn) => handlers.set(name, fn), removeEventListener() {} });
  const press = button => {
    let stopped = 0;
    const event = { detail: { button }, preventDefault() {}, stopPropagation() { stopped++; } };
    handlers.get("vgp_onbuttondown")(event);
    // Rilascio esplicito: una pressione tenuta segue la sua regola, non questa.
    handlers.get("vgp_onbuttonup")({ detail: { button }, preventDefault() {}, stopPropagation() {} });
    return stopped;
  };
  assert.equal(press(1), 1, "A e' consumata mentre la slide successiva si prepara");
  assert.equal(press(2), 1, "e B non puo' scappare dalla guida in quella finestra");

  // A guida ferma, invece, l'input torna a Steam: non teniamo il menu in ostaggio.
  snapshot = { view: null, advancing: false };
  assert.equal(press(1), 0);
  assert.equal(press(2), 0);
  stop();
});

test("input is bound on every candidate document, not only the selected one", () => {
  const source = readFileSync(new URL("../src/onboardingIntegration.tsx", import.meta.url), "utf8");
  // Quale documento riceva l'evento non deve fare differenza.
  assert.match(source, /bindInputs\(\[mainDocument, document, \.\.\.candidates\]\)/);
  assert.match(source, /const boundInputs = new Map<Document, \(\) => void>\(\)/);
  assert.doesNotMatch(source, /stopMainInput/, "niente piu' binding singolo sul solo documento principale");
});

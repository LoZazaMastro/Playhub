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
  vm.runInNewContext(source, { exports, ...context });
  return exports;
}
const state = load("onboardingState.ts");
const facts = { qamOpen: false, playhubSelected: false, playhubAnchor: false, deckyEnabled: true, deckyVisible: true, deckyAnchor: false };
test("versioned guide advances only on native facts and explicit final confirmation", () => {
  let s = state.readOnboardingState(null);
  assert.equal(s.completed, false);
  assert.match(state.ONBOARDING_STORAGE_KEY, /2\.0\.0/);
  s = state.reduceOnboarding(s, { type: "confirm", facts });
  assert.equal(s.step, "intro");
  s = state.reduceOnboarding(s, { type: "observe", facts: { ...facts, qamOpen: true } });
  assert.equal(s.step, "playhub", "actual QAM opening dismisses intro; tooltip still needs a mounted anchor");
  s = state.reduceOnboarding(s, { type: "observe", facts: { ...facts, qamOpen: true, playhubAnchor: true } });
  assert.equal(s.step, "playhub");
  s = state.reduceOnboarding(s, { type: "confirm", facts: { ...facts, qamOpen: true, playhubAnchor: true } });
  assert.equal(s.step, "playhub", "a navigation request is not proof of native selection");
  const selected = { ...facts, qamOpen: true, playhubSelected: true };
  s = state.reduceOnboarding(s, { type: "observe", facts: selected });
  assert.equal(s.step, "decky");
  assert.equal(state.reduceOnboarding(s, { type: "confirm", facts: selected }).completed, false);
  s = state.reduceOnboarding(s, { type: "confirm", facts: { ...selected, deckyAnchor: true } });
  assert.equal(s.step, "outro", "the last tab hands over to the closing slide, it does not finish the guide");
  assert.equal(s.completed, false);
  assert.equal(state.readOnboardingState(JSON.stringify(s)).step, "outro");
  s = state.reduceOnboarding(s, { type: "confirm", facts: { ...facts, qamOpen: false } });
  assert.equal(s.completed, true, "the closing slide is confirmed with the menu closed");
  assert.equal(state.readOnboardingState(JSON.stringify(s)).completed, true);
});
/** The closing slide is shown with the menu closed, so no tab list exists: a confirm there
 *  must complete the guide instead of falling into the visible-tabs branch and doing nothing. */
test("the closing slide completes with no visible tab list, so A can never leave it stuck", () => {
  const closing = { step: "outro", paused: false, completed: false, visited: ["playhub", "decky"] };
  for (const steps of [undefined, [], ["playhub", "decky"]]) {
    const observed = state.reduceOnboarding(closing, { type: "observe", facts: { ...facts, steps, qamOpen: false } });
    assert.equal(observed.step, "outro", "observations never move the closing slide");
    const done = state.reduceOnboarding(closing, { type: "confirm", facts: { ...facts, steps, qamOpen: false } });
    assert.equal(done.completed, true, `a single confirm finishes the guide (steps: ${JSON.stringify(steps)})`);
    assert.equal(done.step, "done");
  }
  assert.equal(state.readOnboardingState('{"step":"outro","completed":false}').step, "outro");
});
test("pause/resume and interrupted progress preserve completion semantics", () => {
  let s = state.readOnboardingState('{"step":"decky","completed":false}');
  s = state.reduceOnboarding(s, { type: "pause" });
  s = state.readOnboardingState(JSON.stringify(s));
  assert.equal(s.paused, true);
  assert.equal(s.step, "decky");
  const selected = { ...facts, qamOpen: true, playhubSelected: true, deckyEnabled: false };
  assert.equal(state.reduceOnboarding(s, { type: "confirm", facts: selected }).completed, false);
  s = state.reduceOnboarding(s, { type: "resume" });
  const closing = state.reduceOnboarding(s, { type: "confirm", facts: selected });
  assert.equal(closing.step, "outro");
  assert.equal(state.reduceOnboarding(closing, { type: "confirm", facts }).completed, true);
  assert.equal(state.readOnboardingState('{"completed":true,"step":"intro"}').completed, false);
  assert.equal(state.readOnboardingState("broken").step, "intro");
});
const anchors = load("onboardingAnchors.ts");
test("portal geometry converts scaled and offset QAM coordinates into its visible local bounds", () => {
  const doc = { defaultView: { innerWidth: 855, innerHeight: 682,
    visualViewport: { offsetLeft: 0, offsetTop: 0, width: 855, height: 682 } } };
  const portal = { clientWidth: 800, clientHeight: 700,
    getBoundingClientRect: () => ({ left: 300, top: 40, width: 960, height: 840 }) };
  const g = anchors.onboardingPortalGeometry(doc, portal, { left: 780, top: 400, width: 48, height: 48 });
  assert.equal(g.width, 462.5);
  assert.equal(g.anchor.left, 400);
  const p = anchors.positionOnboardingCoach(g.anchor, g, { width: 360, height: 250 });
  assert.ok(300 + (p.left + p.width) * 1.2 <= 855);
  assert.ok(40 + (p.top + 250) * 1.2 <= 682);
  portal.getBoundingClientRect = () => ({ left: 0, top: 0, width: 0, height: 0 });
  assert.equal(anchors.onboardingPortalGeometry(doc, portal, null), null);
});
test("855px transparent BrowserView clamps coach to actual 300px Steam content pane", () => {
  const panel = { parentElement: null, getBoundingClientRect: () => ({ left: 48, right: 348, top: 14, bottom: 682 }) };
  const controls = { parentElement: panel, getBoundingClientRect: () => ({ left: 48, right: 348, width: 300, height: 1000 }) };
  const doc = { querySelector: () => controls, defaultView: { innerWidth: 855, innerHeight: 682,
    getComputedStyle: () => ({ overflowX: "hidden", overflowY: "auto" }) } };
  const portal = { clientWidth: 855, clientHeight: 682,
    getBoundingClientRect: () => ({ left: 0, top: 0, width: 855, height: 682 }) };
  const g = anchors.onboardingPortalGeometry(doc, portal, { left: 0, top: 90, width: 48, height: 60 }, true);
  const p = anchors.positionOnboardingCoach(g.anchor, g, { width: 360, height: 300 });
  assert.equal(p.width, 276);
  assert.ok(g.left + p.left >= 60);
  assert.ok(g.left + p.left + p.width <= 336);
  const body = { parentElement: null, getBoundingClientRect: () => ({ left: 0, right: 855, top: 0, bottom: 0 }) };
  doc.body = body;
  panel.parentElement = body;
  assert.equal(anchors.onboardingPortalGeometry(doc, portal, null, true).width, 300,
    "zero-height overflow-hidden body cannot erase the fixed QAM pane");
  panel.parentElement = { parentElement: body,
    getBoundingClientRect: () => ({ left: 0, right: 855, top: 0, bottom: 0 }) };
  const fixed = anchors.onboardingPortalGeometry(doc, portal, null, true);
  assert.equal(fixed.width, 300);
  assert.equal(fixed.height, 668, "zero-area layout wrapper is ignored; actual native pane still clips");
  doc.querySelector = () => null;
  assert.equal(anchors.onboardingPortalGeometry(doc, portal, null, true), null);
});
test("anchor observer coalesces and cleans old documents without polling", () => {
  let callback;
  let frame;
  let disconnects = 0;
  let cancelled = 0;
  const events = new Set();
  const view = { MutationObserver: class { constructor(fn) { callback = fn; } observe() {} disconnect() { disconnects++; } },
    requestAnimationFrame: fn => { frame = fn; return 1; }, cancelAnimationFrame: () => { cancelled++; },
    addEventListener: name => events.add(name), removeEventListener: name => events.delete(name) };
  const doc = { defaultView: view, documentElement: {}, addEventListener: name => events.add(name), removeEventListener: name => events.delete(name) };
  let changes = 0;
  const stop = anchors.observeOnboardingAnchors(doc, () => changes++);
  const records = [{ type: "attributes", target: { matches: () => true } }];
  callback(records); callback(records); frame(); assert.equal(changes, 1);
  callback(records); stop(); stop(); frame();
  assert.equal(changes, 1);
  assert.equal(disconnects, 1);
  assert.equal(cancelled, 1);
  assert.equal(events.size, 0);
});
test("coach geometry keeps text region above footer on handheld and desktop", () => {
  for (const width of [320, 800, 1280, 1920]) {
    const viewport = { width, height: 720 };
    const p = anchors.positionOnboardingCoach({ left: width - 70, top: 550, width: 40, height: 40 }, viewport, { width: 360, height: 190 });
    assert.ok(p.left >= 12 && p.left + p.width <= width - 12);
    assert.ok(p.top >= 12 && p.top + 190 <= 720 - 96);
    assert.ok(p.top + 190 < 550, "place above anchor when footer leaves no room below");
  }
});

test("hidden native BrowserView schedules readiness on the visible shared renderer, not its suspended rAF", () => {
  let callback, sharedFrame, nativeFrames = 0, changes = 0, cancelled = 0, nativeCancelled = 0;
  const events = new Map();
  const shared = { document: { visibilityState: "visible" }, requestAnimationFrame: fn => { sharedFrame = fn; return 7; },
    cancelAnimationFrame: () => cancelled++ };
  const api = load("onboardingAnchors.ts", { window: shared });
  const view = { MutationObserver: class { constructor(fn) { callback = fn; } observe() {} disconnect() {} },
    requestAnimationFrame: () => { nativeFrames++; return 8; }, cancelAnimationFrame: () => nativeCancelled++, addEventListener() {}, removeEventListener() {} };
  const document = { visibilityState: "hidden", defaultView: view, documentElement: {}, addEventListener: (name, fn) => events.set(name, fn), removeEventListener: name => events.delete(name) };
  const stop = api.observeOnboardingAnchors(document, () => changes++);
  assert.equal(api.onboardingAnimationFrameHost(document), shared);
  assert.equal(api.onboardingAnimationFrameHost({ ...document, visibilityState: "visible" }), view);
  callback([{ type: "attributes", target: { matches: () => true } }]);
  assert.equal(nativeFrames, 0); assert.equal(typeof sharedFrame, "function");
  sharedFrame(); assert.equal(changes, 1);
  callback([{ type: "attributes", target: { matches: () => true } }]);
  document.visibilityState = "visible"; events.get("visibilitychange")();
  assert.equal(cancelled, 1); assert.equal(nativeFrames, 1);
  document.visibilityState = "hidden"; events.get("visibilitychange")();
  assert.equal(nativeCancelled, 1);
  stop(); assert.equal(cancelled, 2);
  sharedFrame(); assert.equal(changes, 1, "late frame after disposal cannot publish readiness");
});
function fixture(values = new Map()) {
  let change;
  let nativeChange;
  let anchorsChanged;
  let cleanup = 0;
  let enters = 0;
  const env = { document: { body: {}, defaultView: { innerWidth: 1280, innerHeight: 800 } }, qamOpen: false,
    playhubSelected: false, deckyEnabled: true, deckyVisible: true };
  const found = new Map();
  const api = load("onboardingRuntime.ts", { require: name => name === "./onboardingState" ? state : {
    convertOnboardingAnchor: anchors.convertOnboardingAnchor,
    onboardingFallbackQamBounds: anchors.onboardingFallbackQamBounds,
    visibleOnboardingAnchor: (_doc, selector) => found.get(selector) ?? null,
    observeOnboardingAnchors: (_doc, fn) => { anchorsChanged = fn; return () => { cleanup++; }; }
  } });
  const options = { storage: { getItem: key => values.get(key) ?? null, setItem: (key, value) => values.set(key, value) },
    native: { renderHint: action => ({ native: action }), subscribe: fn => { nativeChange = fn; return () => { cleanup++; }; } },
    getEnvironment: () => env, subscribeEnvironment: fn => { change = fn; return () => { cleanup++; }; },
    enterPlayhub: () => { enters++; } };
  return { api, env, found, values, options, change: () => change(), nativeChange: () => nativeChange(),
    anchorChange: () => anchorsChanged(), cleanup: () => cleanup, enters: () => enters };
}
test("QAM is only the anchor source; main BPM owns the tooltip and missing bounds hide it", () => {
  const f = fixture();
  const main = { body: {}, defaultView: { innerWidth: 1920, innerHeight: 1080 } };
  f.env.mainDocument = main;
  f.env.document.defaultView = { innerWidth: 855, innerHeight: 682 };
  f.env.qamOpen = true;
  f.found.set("#quickaccess_tab_5261387", { left: 0, top: 200, width: 48, height: 48 });
  const stop = f.api.initPlayhubOnboarding(f.options);
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, null);
  f.env.qamBounds = { left: 900, top: 20, width: 855, height: 682 }; f.change();
  const snapshot = f.api.getPlayhubOnboardingSnapshot();
  assert.equal(snapshot.document, main);
  assert.equal(snapshot.view, "playhub");
  assert.equal(snapshot.anchor.left, 900);
  assert.equal(snapshot.anchor.top, 220);
  assert.equal(snapshot.width, 1920);
  f.env.qamOpen = false; f.change();
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, null);
  assert.equal(f.api.getPlayhubOnboardingSnapshot().state.step, "playhub");
  stop();
});

test("pause wins before a pending native selection and subsequent unrelated navigation", () => {
  const f = fixture(); const stop = f.api.initPlayhubOnboarding(f.options);
  f.env.qamOpen = true;
  f.found.set("#quickaccess_tab_5261387", { left: 20, top: 20, width: 30, height: 30 });
  f.change(); f.api.markPlayhubOnboardingPresented("playhub");
  f.api.reportPlayhubOnboardingAction("confirm");
  f.env.playhubSelected = true;
  f.api.reportPlayhubOnboardingAction("cancel");
  f.change();
  assert.equal(f.api.getPlayhubOnboardingSnapshot().state.step, "playhub");
  assert.equal(f.api.getPlayhubOnboardingSnapshot().state.paused, true);
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, null);
  assert.equal(f.api.reportPlayhubOnboardingAction("confirm"), false);
  assert.equal(f.api.getPlayhubOnboardingSnapshot().state.completed, false);
  stop();
});
test("closing QAM hides current slide without restarting intro; reopening resumes progress", () => {
  const f = fixture(); const stop = f.api.initPlayhubOnboarding(f.options);
  f.env.qamOpen = true;
  f.found.set("#quickaccess_tab_5261387", { left: 20, top: 20, width: 30, height: 30 });
  f.change();
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, "playhub");
  f.env.qamOpen = false; f.change();
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, null);
  assert.equal(f.api.getPlayhubOnboardingSnapshot().state.step, "playhub");
  f.env.qamOpen = true; f.change();
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, "playhub");
  assert.equal(f.api.getPlayhubOnboardingSnapshot().state.completed, false);
  stop();
});
test("runtime waits for QAM, owner anchor and native selection; completes once after presentation", () => {
  const f = fixture();
  const stop = f.api.initPlayhubOnboarding(f.options);
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, "intro");
  // The press is consumed and kept for the slide, never handed to the menu behind it.
  assert.equal(f.api.reportPlayhubOnboardingAction("confirm"), true);
  assert.equal(f.api.getPlayhubOnboardingSnapshot().state.step, "intro", "and it advances nothing on its own");
  f.env.qamOpen = true; f.change();
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, null);
  f.env.playhubTabId = 42;
  f.found.set("#quickaccess_tab_42", { left: 1, top: 2, width: 30, height: 30 }); f.anchorChange();
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, "playhub");
  assert.equal(f.api.getPlayhubOnboardingSnapshot().anchor.left, 1, "first anchor is the current owner's native Playhub tab");
  f.api.markPlayhubOnboardingPresented("playhub");
  f.api.reportPlayhubOnboardingAction("confirm");
  assert.equal(f.enters(), 1);
  assert.equal(f.api.getPlayhubOnboardingSnapshot().state.step, "playhub");
  f.env.playhubSelected = true; f.change();
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, null, "no false Decky anchor");
  f.found.set('[data-playhub-onboarding="decky"]', { left: 50, top: 60, width: 30, height: 30 }); f.anchorChange();
  assert.equal(f.api.getPlayhubOnboardingSnapshot().anchor.left, 50, "later anchor is the internal Decky tab, never native tab 999");
  // Consumed and kept, but the explanation must be rendered before anything advances.
  assert.equal(f.api.reportPlayhubOnboardingAction("confirm"), true);
  assert.equal(f.api.getPlayhubOnboardingSnapshot().state.step, "decky");
  f.api.markPlayhubOnboardingPresented("decky"); f.api.markPlayhubOnboardingPresented(null);
  assert.equal(f.api.reportPlayhubOnboardingAction("confirm"), true);
  assert.equal(f.api.getPlayhubOnboardingSnapshot().state.step, "decky", "an unmounted guide cannot complete");
  f.api.markPlayhubOnboardingPresented("decky"); f.api.reportPlayhubOnboardingAction("confirm");
  assert.equal(f.api.getPlayhubOnboardingSnapshot().state.step, "outro");
  f.env.qamOpen = false; f.change();
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, "outro");
  f.api.markPlayhubOnboardingPresented("outro"); f.api.reportPlayhubOnboardingAction("confirm");
  assert.equal(f.api.getPlayhubOnboardingSnapshot().state.completed, true);
  stop(); assert.equal(f.cleanup(), 3);
  const restart = fixture(f.values); const end = restart.api.initPlayhubOnboarding(restart.options);
  assert.equal(restart.api.getPlayhubOnboardingSnapshot().view, null); end();
});
test("disabled Decky stays disabled, B pauses, controller/doc changes refresh and cleanup", () => {
  const f = fixture(); const stop = f.api.initPlayhubOnboarding(f.options);
  f.api.reportPlayhubOnboardingAction("cancel");
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, null);
  assert.equal(JSON.parse(f.values.get(state.ONBOARDING_STORAGE_KEY)).completed, false);
  f.api.resumePlayhubOnboarding();
  f.found.set("#quickaccess_tab_5261387", { left: 1, top: 2, width: 30, height: 30 });
  f.env.qamOpen = true; f.env.playhubSelected = true; f.env.deckyEnabled = false; f.change();
  f.api.markPlayhubOnboardingPresented("playhub"); f.api.reportPlayhubOnboardingAction("confirm");
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, "decky-off");
  assert.equal(f.env.deckyEnabled, false);
  const revision = f.api.getPlayhubOnboardingSnapshot().revision;
  f.nativeChange(); assert.ok(f.api.getPlayhubOnboardingSnapshot().revision > revision);
  f.env.document = { body: {}, defaultView: { innerWidth: 800, innerHeight: 600 } }; f.change();
  assert.equal(f.cleanup(), 1);
  f.api.markPlayhubOnboardingPresented("decky-off"); f.api.reportPlayhubOnboardingAction("confirm");
  stop(); assert.equal(f.cleanup(), 4);
});
test("all twelve locales supply complete distinct copy without fixed controller combinations", () => {
  const locale = load("pluginStoreLocale.ts");
  const api = load("onboardingLocale.ts", { require: () => locale });
  const titles = new Set();
  for (const key of ["en", "it", "es", "fr", "de", "pt", "uk", "zh", "ja", "ko", "hi", "ru"]) {
    const copy = api.getOnboardingCopy(key); titles.add(copy.title);
    assert.equal(Object.values(copy).length, 14);
    assert.ok(copy.outroTitle.length > 0 && copy.outro.length > 0, "the closing slide is localized too");
    for (const title of [copy.playhubTitle, copy.deckyTitle, copy.storeTitle, copy.customizeTitle])
      assert.ok(!["Playhub", "Decky", "Plugin Store"].includes(title));
    assert.ok(Object.values(api.getOnboardingExtraCopy(key)).every(value => value.length > 0));
    const extra = api.getOnboardingExtraCopy(key);
    assert.match(extra.store, /Playhub/); assert.match(extra.store, /Decky Store/); assert.match(extra.store, /GitHub/);
    assert.equal(copy.deckyOff, copy.decky);
    assert.match(copy.decky, /Decky/);
    assert.match(copy.decky, /Playhub/);
    assert.doesNotMatch(copy.decky, /Steam|optional|facoltativ/);
    if (key === "en") {
      assert.equal(copy.deckyTitle, "Your plugins, together");
      assert.equal(copy.decky, "The Decky menu is now part of Playhub, always available to browse your installed plugins. You can restore the Decky tab at any time from Playhub settings.");
    }
    if (key === "it") {
      assert.equal(copy.decky, "Ora il menu di Decky è parte di Playhub e puoi accedervi in qualsiasi momento per vedere la lista dei tuoi plugin installati. Puoi anche ripristinare la tab Decky in qualsiasi momento dalle impostazioni di Playhub.");
      assert.doesNotMatch(JSON.stringify({ ...copy, ...extra }), /schede|scheda|facoltativ|attivarla|più fonti/);
      assert.match(extra.customize, /Riordina le tab/);
    }
    assert.ok(Object.values(copy).every(value => value.length > 0));
    assert.doesNotMatch(JSON.stringify(copy), /Guide\s*\+\s*A|Xbox/);
  }
  assert.equal(titles.size, 12);
});

test("storage failure, interrupted restart and failed native routing never mark completed", () => {
  const f = fixture();
  f.options.storage = { getItem() { throw new Error("denied"); }, setItem() { throw new Error("denied"); } };
  f.options.enterPlayhub = () => { throw new Error("native_not_ready"); };
  const stop = f.api.initPlayhubOnboarding(f.options);
  f.env.qamOpen = true;
  f.found.set("#quickaccess_tab_5261387", { left: 1, top: 2, width: 30, height: 30 }); f.change();
  f.api.markPlayhubOnboardingPresented("playhub");
  assert.doesNotThrow(() => f.api.reportPlayhubOnboardingAction("confirm"));
  assert.equal(f.api.getPlayhubOnboardingSnapshot().state.completed, false);
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, "playhub");
  stop();
  const values = new Map([[state.ONBOARDING_STORAGE_KEY, '{"step":"decky","paused":false,"completed":false}']]);
  const resumed = fixture(values); const end = resumed.api.initPlayhubOnboarding(resumed.options);
  assert.equal(resumed.api.getPlayhubOnboardingSnapshot().view, null, "closed QAM hides guide without losing progress");
  assert.equal(resumed.api.getPlayhubOnboardingSnapshot().state.step, "decky");
  end();
});

test("onboarding UI uses a transparent nonmodal portal and no global controller interception", () => {
  const source = readFileSync(new URL("../src/PlayhubOnboarding.tsx", import.meta.url), "utf8");
  assert.match(source, /createPortal/);
  // The fading intro must stop capturing input immediately; nothing outside the intro is pointer-blocking.
  assert.match(source, /pointerEvents: fullscreen && !snapshot\.introExiting \? "auto" : "none"/);
  assert.match(source, /pointerEvents: fullscreen && snapshot\.introExiting \? "none" : "auto"/);
  assert.doesNotMatch(source, /autoFocus|showModal|RegisterForController|\.focus\(/);
  assert.match(source, /renderPlayhubOnboardingHint\("confirm"\)/);
});

test("Store and customization require a separate manual confirmation each", () => {
  const f = fixture(new Map([[state.ONBOARDING_STORAGE_KEY, '{"step":"decky","completed":false}']]));
  const stop = f.api.initPlayhubOnboarding(f.options);
  f.env.qamOpen = true; f.env.playhubSelected = true;
  for (const key of ["decky", "store", "customize"]) f.found.set(`[data-playhub-onboarding="${key}"]`, { left: 1, top: 2, width: 30, height: 30 });
  f.change();
  for (const view of ["decky", "store", "customize"]) {
    assert.equal(f.api.getPlayhubOnboardingSnapshot().view, view);
    for (let i = 0; i < 3; i++) f.change();
    assert.equal(f.api.getPlayhubOnboardingSnapshot().view, view, "observations cannot advance a slide");
    f.api.markPlayhubOnboardingPresented(view); f.api.reportPlayhubOnboardingAction("confirm");
  }
  assert.equal(f.api.getPlayhubOnboardingSnapshot().state.step, "outro");
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, null, "the closing slide never shows over an open menu");
  f.env.qamOpen = false; f.change();
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, "outro");
  f.api.markPlayhubOnboardingPresented("outro"); f.api.reportPlayhubOnboardingAction("confirm");
  assert.equal(f.api.getPlayhubOnboardingSnapshot().state.completed, true); stop();
});

test("Steam guide blocks start and mid-guide actions without completing Playhub", () => {
  const f = fixture(); f.env.steamReady = false;
  const stop = f.api.initPlayhubOnboarding(f.options);
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, null);
  assert.equal(f.api.reportPlayhubOnboardingAction("confirm"), false);
  f.env.steamReady = true; f.change(); assert.equal(f.api.getPlayhubOnboardingSnapshot().view, "intro");
  f.env.qamOpen = true; f.env.playhubSelected = true;
  f.found.set("#quickaccess_tab_5261387", { left: 1, top: 2, width: 30, height: 30 }); f.change();
  f.api.markPlayhubOnboardingPresented("playhub");
  f.env.steamReady = false;
  assert.equal(f.api.reportPlayhubOnboardingAction("confirm"), false, "fresh native gate is checked before consuming input");
  assert.equal(f.api.getPlayhubOnboardingSnapshot().state.completed, false);
  f.env.steamReady = true; f.change();
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, "playhub"); stop();
});

test("intro confirm opens the native QAM but never advances before actual open and correct anchor", () => {
  const f = fixture(); let opened = 0;
  f.options.openQuickAccess = () => { opened++; };
  const stop = f.api.initPlayhubOnboarding(f.options);
  f.api.markPlayhubOnboardingPresented("intro");
  assert.equal(f.api.reportPlayhubOnboardingAction("confirm"), true);
  assert.equal(opened, 1); assert.equal(f.api.getPlayhubOnboardingSnapshot().view, "intro");
  f.env.qamOpen = true; f.change(); assert.equal(f.api.getPlayhubOnboardingSnapshot().view, null);
  f.found.set("#quickaccess_tab_5261387", { left: 1, top: 2, width: 30, height: 30 }); f.change();
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, "playhub"); stop();
});

test("unstable QAM rejects confirmation even with an old presentation, then accepts stable presentation", () => {
  const f = fixture();
  f.env.qamOpen = true; f.env.layoutStable = false;
  f.found.set("#quickaccess_tab_5261387", { left: 0, top: 85, width: 48, height: 64 });
  const stop = f.api.initPlayhubOnboarding(f.options);
  f.api.markPlayhubOnboardingPresented("playhub");
  assert.equal(f.api.reportPlayhubOnboardingAction("confirm"), false);
  assert.equal(f.api.getPlayhubOnboardingSnapshot().state.step, "playhub");
  f.env.layoutStable = true; f.change();
  f.api.markPlayhubOnboardingPresented("playhub");
  assert.equal(f.api.reportPlayhubOnboardingAction("confirm"), true);
  stop();
});

test("partially clipped anchors are never used for a coachmark", () => {
  let rect = { left: -1, top: 20, width: 30, height: 30, right: 29, bottom: 50 };
  const element = { isConnected: true, closest: () => null, getBoundingClientRect: () => rect };
  const doc = { querySelector: () => element, defaultView: { innerWidth: 800, innerHeight: 600, getComputedStyle: () => ({}) } };
  assert.equal(anchors.visibleOnboardingAnchor(doc, "#correct"), null);
  rect = { left: 10, top: 580, width: 30, height: 30, right: 40, bottom: 610 };
  assert.equal(anchors.visibleOnboardingAnchor(doc, "#correct"), null);
  rect = { left: 10, top: 20, width: 30, height: 30, right: 40, bottom: 50 };
  assert.ok(anchors.visibleOnboardingAnchor(doc, "#correct"));
});

test("failed QAM open restores visible intro and an old session cannot mutate replay", async () => {
  const f = fixture(); let rejectOpen;
  f.options.openQuickAccess = () => new Promise((_resolve, reject) => { rejectOpen = reject; });
  const stop = f.api.initPlayhubOnboarding(f.options);
  f.api.markPlayhubOnboardingPresented("intro"); f.api.reportPlayhubOnboardingAction("confirm");
  assert.equal(f.api.getPlayhubOnboardingSnapshot().introExiting, true);
  rejectOpen(new Error("native unavailable")); await new Promise(resolve => setImmediate(resolve));
  assert.equal(f.api.getPlayhubOnboardingSnapshot().introExiting, false);
  f.api.reportPlayhubOnboardingAction("confirm"); const oldReject = rejectOpen;
  stop(); const stopReplay = f.api.initPlayhubOnboarding(f.options);
  f.api.markPlayhubOnboardingPresented("intro"); f.api.reportPlayhubOnboardingAction("confirm");
  oldReject(new Error("old request")); await new Promise(resolve => setImmediate(resolve));
  assert.equal(f.api.getPlayhubOnboardingSnapshot().introExiting, true);
  stopReplay();
});

test("closing QAM before native anchor mounts never resurrects the intro", () => {
  const f = fixture(); const stop = f.api.initPlayhubOnboarding(f.options);
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, "intro");
  f.env.qamOpen = true; f.change();
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, null);
  assert.equal(f.api.getPlayhubOnboardingSnapshot().state.step, "playhub");
  f.env.qamOpen = false; f.change();
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, null);
  assert.equal(f.api.getPlayhubOnboardingSnapshot().introExiting, false);
  f.env.qamOpen = true;
  f.found.set("#quickaccess_tab_5261387", { left: 0, top: 85, width: 48, height: 64 }); f.change();
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, "playhub");
  stop();
});

test("blocked Steam state clears stale bounds while retaining last observed native diagnostics", () => {
  const f = fixture(); f.env.document.title = "QuickAccess_uid37";
  f.env.mainDocument = { body: {}, title: "BPM", defaultView: { innerWidth: 1280, innerHeight: 800 } };
  f.env.qamOpen = true; f.env.qamBounds = { left: 900, top: 0, width: 855, height: 682 };
  f.found.set("#quickaccess_tab_5261387", { left: 0, top: 85, width: 48, height: 64 });
  const stop = f.api.initPlayhubOnboarding(f.options);
  assert.equal(f.api.getPlayhubOnboardingSnapshot().qamOpen, true);
  assert.equal(f.api.getPlayhubOnboardingSnapshot().anchorDocumentTitle, "QuickAccess_uid37");
  f.env.steamReady = false; f.change();
  const snapshot = f.api.getPlayhubOnboardingSnapshot();
  assert.equal(snapshot.steamReady, false); assert.equal(snapshot.view, null);
  assert.equal(snapshot.qamBounds, null); assert.equal(snapshot.anchor, null);
  assert.equal(snapshot.document, f.env.mainDocument); stop();
});

test("Decky, Store and settings activate before tooltip; held A cannot advance the newly mounted step", () => {
  const f = fixture(new Map([[state.ONBOARDING_STORAGE_KEY, '{"step":"decky","completed":false}']]));
  const requests = []; f.options.showControlStep = step => requests.push(step);
  Object.assign(f.env, { qamOpen: true, playhubSelected: true, controlsReady: true, controlStep: null, controlReady: false, storeVisible: true });
  for (const step of ["decky", "store"]) f.found.set(`[data-playhub-onboarding="${step}"]`, { left: 50, top: 90, width: 40, height: 40 });
  const stop = f.api.initPlayhubOnboarding(f.options);
  assert.equal(requests.at(-1), "decky"); assert.equal(f.api.getPlayhubOnboardingSnapshot().view, null);
  assert.equal(f.api.reportPlayhubOnboardingAction("confirm"), false);
  f.env.controlStep = "decky"; f.change();
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, null, "selection alone is not content readiness");
  f.env.controlReady = true; f.change(); f.api.markPlayhubOnboardingPresented("decky");
  const handlers = new Map();
  const input = load("onboardingIntegration.tsx", { require: name => name === "./PlayhubOnboarding" ? {
    getPlayhubOnboardingSnapshot: f.api.getPlayhubOnboardingSnapshot, reportPlayhubOnboardingAction: f.api.reportPlayhubOnboardingAction
  } : {} });
  const stopInput = input.bindOnboardingInput({ addEventListener: (name, fn) => handlers.set(name, fn), removeEventListener() {} });
  const press = () => handlers.get("vgp_onbuttondown")({ detail: { button: 1 }, preventDefault() {}, stopPropagation() {} });
  const release = () => handlers.get("vgp_onbuttonup")({ detail: { button: 1 }, preventDefault() {}, stopPropagation() {} });
  press(); assert.equal(requests.at(-1), "store"); assert.equal(f.api.getPlayhubOnboardingSnapshot().view, null);
  f.env.controlStep = "store"; f.env.controlReady = true; f.change(); f.api.markPlayhubOnboardingPresented("store");
  press(); assert.equal(f.api.getPlayhubOnboardingSnapshot().state.step, "store", "same held A does not count twice");
  release(); press(); assert.equal(requests.at(-1), "customize");
  f.env.controlStep = "customize"; f.env.controlReady = true; f.change();
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, null, "settings content still needs its own mounted heading anchor");
  f.found.set('[data-playhub-onboarding="customize"]', { left: 64, top: 100, width: 230, height: 24 }); f.change();
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, "customize");
  f.api.markPlayhubOnboardingPresented("customize"); release(); press();
  assert.equal(f.api.getPlayhubOnboardingSnapshot().state.step, "outro"); assert.equal(requests.at(-1), null);
  f.env.qamOpen = false; f.change();
  f.api.markPlayhubOnboardingPresented("outro"); release(); press();
  assert.equal(f.api.getPlayhubOnboardingSnapshot().state.completed, true);
  stopInput(); stop();
});

test("first step opens actual Home before text regardless of previous tab and waits for mounted content", () => {
  const f = fixture(); const requests = []; let entered = 0;
  f.options.showControlStep = step => requests.push(step);
  f.options.enterPlayhub = () => { entered++; };
  Object.assign(f.env, { qamOpen: true, controlsReady: true, controlStep: "store", controlReady: true });
  f.found.set("#quickaccess_tab_5261387", { left: 0, top: 85, width: 48, height: 64 });
  const stop = f.api.initPlayhubOnboarding(f.options);
  assert.equal(entered, 1); assert.equal(f.api.getPlayhubOnboardingSnapshot().view, null);
  f.change(); assert.equal(entered, 1, "native selection is not repeated each refresh");
  f.env.playhubSelected = true; f.change(); assert.equal(requests.at(-1), "home");
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, null);
  f.env.controlStep = "home"; f.env.controlReady = false; f.change();
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, null);
  f.env.controlReady = true; f.change(); assert.equal(f.api.getPlayhubOnboardingSnapshot().view, "playhub");
  f.env.qamOpen = false; f.change(); assert.equal(requests.at(-1), null);
  assert.equal(f.api.getPlayhubOnboardingSnapshot().state.step, "playhub"); stop();
});

test("synchronous tab commit cannot be overwritten by the outer stale refresh", () => {
  for (const step of ["decky", "customize"]) {
  const f = fixture(new Map([[state.ONBOARDING_STORAGE_KEY, JSON.stringify({ step, completed: false })]]));
  Object.assign(f.env, { qamOpen: true, playhubSelected: true, controlsReady: true,
    controlStep: "home", controlReady: true, storeVisible: true });
  f.found.set(`[data-playhub-onboarding="${step}"]`, { left: 50, top: 90, width: 40, height: 40 });
  f.options.getEnvironment = () => ({ ...f.env });
  f.options.showControlStep = step => {
    if (step && f.env.controlStep !== step) { f.env.controlStep = step; f.change(); }
  };
  const stop = f.api.initPlayhubOnboarding(f.options);
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, step);
  assert.equal(f.api.getPlayhubOnboardingSnapshot().controlStep, step);
  assert.equal(f.api.getPlayhubOnboardingSnapshot().controlReady, true);
  stop();
  }
});

test("confirm cannot consume a slide first presented synchronously during its readiness refresh", () => {
  const f = fixture(new Map([[state.ONBOARDING_STORAGE_KEY, '{"step":"decky","completed":false}']]));
  Object.assign(f.env, { qamOpen: true, playhubSelected: true, controlsReady: true,
    controlStep: "decky", controlReady: false });
  f.found.set('[data-playhub-onboarding="decky"]', { left: 50, top: 90, width: 40, height: 40 });
  const stop = f.api.initPlayhubOnboarding(f.options);
  const unsubscribe = f.api.subscribePlayhubOnboarding(() => f.api.markPlayhubOnboardingPresented(f.api.getPlayhubOnboardingSnapshot().view));
  f.env.controlReady = true;
  assert.equal(f.api.reportPlayhubOnboardingAction("confirm"), false);
  assert.equal(f.api.getPlayhubOnboardingSnapshot().state.step, "decky");
  unsubscribe(); stop();
});

test("visible navigation drives every slide in real order, with readiness and monotone hidden-step progress", () => {
  const steps = ["controller", "store", "playhub", "audio", "performance", "graphics", "decky", "customize"];
  const facts = { qamOpen: true, playhubSelected: true, steps, activeReady: true };
  let progress = state.readOnboardingState(null);
  progress = state.reduceOnboarding(progress, { type: "observe", facts });
  assert.equal(progress.step, "controller", "custom order overrides historical Home-first behavior");
  for (const step of steps) {
    assert.equal(progress.step, step);
    const blocked = state.reduceOnboarding(progress, { type: "confirm", facts: { ...facts, activeReady: false } });
    assert.equal(blocked.step, step);
    progress = state.reduceOnboarding(progress, { type: "confirm", facts });
  }
  assert.equal(progress.step, "outro", "the tab tour hands over to the closing slide");
  assert.equal(state.reduceOnboarding(progress, { type: "confirm", facts }).completed, true);
  progress = { step: "audio", completed: false, paused: false, visited: ["controller", "store", "playhub"] };
  const hidden = { ...facts, steps: ["controller", "store", "playhub", "graphics", "decky", "customize"] };
  progress = state.reduceOnboarding(progress, { type: "observe", facts: hidden });
  assert.equal(progress.step, "graphics"); assert.ok(progress.visited.includes("audio"));
  progress = state.reduceOnboarding(progress, { type: "confirm", facts });
  assert.equal(progress.step, "performance", "only unseen tabs may be visited after configuration changes");
});

test("runtime opens each newly supported tab and waits for its actual content and anchor", () => {
  for (const step of ["audio", "performance", "graphics", "controller"]) {
    const f = fixture(); const requests = [];
    Object.assign(f.env, { steps: [step, "customize"], qamOpen: true, playhubSelected: true, controlsReady: true,
      controlStep: "store", controlReady: true });
    f.options.showControlStep = value => requests.push(value);
    const stop = f.api.initPlayhubOnboarding(f.options);
    assert.equal(requests.at(-1), step); assert.equal(f.api.getPlayhubOnboardingSnapshot().view, null);
    f.env.controlStep = step; f.env.controlReady = false; f.change();
    f.found.set(`[data-playhub-onboarding="${step}"]`, { left: 80, top: 90, width: 32, height: 34 }); f.change();
    assert.equal(f.api.getPlayhubOnboardingSnapshot().view, null);
    f.env.controlReady = true; f.change(); assert.equal(f.api.getPlayhubOnboardingSnapshot().view, step);
    f.api.markPlayhubOnboardingPresented(step);
    assert.equal(f.api.reportPlayhubOnboardingAction("confirm"), true);
    assert.equal(f.api.getPlayhubOnboardingSnapshot().state.step, "customize"); stop();
  }
});

test("Steam OK advances customize with one press, closes QAM, and cannot skip the outro while held", () => {
  const f = fixture();
  Object.assign(f.env, { steps: ["customize"], qamOpen: true, playhubSelected: true,
    controlsReady: true, controlReady: true, controlStep: "customize" });
  f.found.set('[data-playhub-onboarding="customize"]', { left: 80, top: 90, width: 32, height: 34 });
  let closes = 0;
  f.options.closeQuickAccess = () => { closes++; f.env.qamOpen = false; };
  const stop = f.api.initPlayhubOnboarding(f.options);
  f.api.markPlayhubOnboardingPresented("customize");
  const handlers = new Map();
  const input = load("onboardingIntegration.tsx", { require: name => name === "./PlayhubOnboarding" ? f.api : {} });
  const unbind = input.bindOnboardingInput({ addEventListener: (name, fn) => handlers.set(name, fn), removeEventListener() {} });
  const event = (button, repeat = false) => ({ detail: { button, is_repeat: repeat },
    prevented: false, preventDefault() { this.prevented = true; }, stopPropagation() {} });
  const invalid = event(0); handlers.get("vgp_onbuttondown")(invalid);
  assert.equal(invalid.prevented, false, "Steam INVALID is not a browser A button");
  const back = event(2); handlers.get("vgp_onbuttondown")(back); handlers.get("vgp_onbuttonup")(event(2));
  assert.equal(back.prevented, true); assert.equal(closes, 0);
  const confirm = event(1); handlers.get("vgp_onbuttondown")(confirm);
  assert.equal(confirm.prevented, true);
  assert.equal(closes, 1); assert.equal(f.api.getPlayhubOnboardingSnapshot().state.step, "outro");
  f.change(); f.api.markPlayhubOnboardingPresented("outro");
  handlers.get("vgp_onbuttondown")(event(1, true));
  assert.equal(f.api.getPlayhubOnboardingSnapshot().state.completed, false);
  handlers.get("vgp_onbuttonup")(event(1));
  handlers.get("vgp_onbuttondown")(event("OK"));
  assert.equal(f.api.getPlayhubOnboardingSnapshot().state.completed, true);
  unbind(); stop();
});

test("intro remains visible before dynamic tab markers exist and opening waits for their real order", () => {
  const f = fixture(); f.env.steps = []; f.options.showControlStep = () => {};
  const stop = f.api.initPlayhubOnboarding(f.options);
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, "intro");
  f.env.qamOpen = true; f.change(); assert.equal(f.api.getPlayhubOnboardingSnapshot().view, null);
  f.env.qamOpen = false; f.change(); assert.equal(f.api.getPlayhubOnboardingSnapshot().view, null, "closing before nav mounts must not resurrect intro");
  f.env.qamOpen = true;
  f.env.steps = ["graphics", "customize"]; f.change();
  assert.equal(f.api.getPlayhubOnboardingSnapshot().state.step, "graphics");
  stop();
});

test("all twelve locales cover the four added tabs and actual settings, preserving approved Home and Decky", () => {
  const locale = load("pluginStoreLocale.ts");
  const api = load("onboardingTabLocale.ts", { require: () => locale });
  for (const lang of ["en", "it", "es", "fr", "de", "pt", "uk", "zh", "ja", "ko", "hi", "ru"])
    for (const tab of ["audio", "performance", "graphics", "controller", "customize"]) {
      const value = api.getOnboardingTabCopy(lang, tab);
      assert.ok(value.title?.length > 0 && value.body?.length > 0);
      if (tab === "customize") { assert.match(value.body, /Playhub/); assert.match(value.body, /Decky/); }
    }
  assert.equal(api.getOnboardingTabCopy("en", "customize").title, "Make Playhub yours");
  assert.match(api.getOnboardingTabCopy("en", "customize").body, /device information and diagnostics/);
});

test("hidden internal tabs are skipped without changing preferences and QAM close releases temporary selection", () => {
  const f = fixture(new Map([[state.ONBOARDING_STORAGE_KEY, '{"step":"decky","completed":false}']]));
  const requests = []; f.options.showControlStep = step => requests.push(step);
  Object.assign(f.env, { qamOpen: true, playhubSelected: true, controlsReady: true,
    controlStep: null, controlReady: false, deckyVisible: false, storeVisible: false });
  const stop = f.api.initPlayhubOnboarding(f.options);
  assert.equal(f.api.getPlayhubOnboardingSnapshot().state.step, "customize");
  assert.equal(requests.at(-1), "customize");
  assert.equal(requests.includes("decky"), false); assert.equal(requests.includes("store"), false);
  assert.equal(f.env.deckyVisible, false); assert.equal(f.env.storeVisible, false);
  f.env.qamOpen = false; f.change(); assert.equal(requests.at(-1), null);
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, null);
  assert.equal(f.api.getPlayhubOnboardingSnapshot().state.step, "customize");
  f.env.qamOpen = true; f.change(); assert.equal(requests.at(-1), "customize");
  stop(); assert.equal(requests.at(-1), null);
});

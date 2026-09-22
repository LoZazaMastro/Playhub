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
const anchors = load("onboardingAnchors.ts");

test("the estimated QAM band is a right-aligned full-height overestimate, or nothing", () => {
  assert.equal(anchors.onboardingFallbackQamBounds({ width: 0, height: 0 }), null);
  assert.equal(anchors.onboardingFallbackQamBounds({ width: 480, height: 1080 }), null);
  assert.equal(anchors.onboardingFallbackQamBounds({ width: NaN, height: 1080 }), null);
  const bounds = anchors.onboardingFallbackQamBounds({ width: 1920, height: 1080 });
  assert.deepEqual({ ...bounds }, { left: 1056, top: 0, width: 864, height: 1080 });
  assert.ok(bounds.width >= 1920 * 0.4, "overestimates the menu so fallback text never covers it");
  const showcase = anchors.positionOnboardingShowcase({ left: 0, top: 200, width: 48, height: 48 },
    bounds, { width: 1920, height: 1080 }, { width: 360, height: 220 }, 96);
  assert.ok(showcase, "text still has a usable band left of the estimated menu");
  assert.ok(showcase.left + showcase.width <= bounds.left - 24, "never overlaps the menu");
});

function fixture(now) {
  let change = () => {};
  let anchorsChanged = () => {};
  const values = new Map();
  const env = { document: { body: {}, title: "qam" }, mainDocument: null, qamOpen: false,
    playhubSelected: false, deckyEnabled: true, deckyVisible: true };
  const found = new Map();
  const FakeDate = { now: () => now() };
  const api = load("onboardingRuntime.ts", { Date: FakeDate, require: name => name === "./onboardingState" ? state : {
    convertOnboardingAnchor: anchors.convertOnboardingAnchor,
    onboardingFallbackQamBounds: anchors.onboardingFallbackQamBounds,
    visibleOnboardingAnchor: (_doc, selector) => found.get(selector) ?? null,
    observeOnboardingAnchors: (_doc, fn) => { anchorsChanged = fn; return () => {}; }
  } });
  let enters = 0;
  const options = { storage: { getItem: key => values.get(key) ?? null, setItem: (key, value) => values.set(key, value) },
    native: { renderHint: () => null, subscribe: () => () => {} },
    getEnvironment: () => env, subscribeEnvironment: fn => { change = fn; return () => {}; },
    enterPlayhub: () => { enters++; } };
  return { api, env, found, options, change: () => change(), anchorChange: () => anchorsChanged(), enters: () => enters };
}

test("withheld native bounds fall back to an estimated band instead of a captured, empty QAM", () => {
  let clock = 1000;
  const f = fixture(() => clock);
  f.env.mainDocument = { body: {}, defaultView: { innerWidth: 1920, innerHeight: 1080 } };
  f.env.document.defaultView = { innerWidth: 855, innerHeight: 682 };
  f.env.qamOpen = true;
  f.found.set("#quickaccess_tab_5261387", { left: 0, top: 200, width: 48, height: 48 });
  const stop = f.api.initPlayhubOnboarding(f.options);

  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, null, "a freshly opened QAM is never annotated early");
  clock += 1400; f.change();
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, null, "still inside the grace period");

  clock += 200; f.change();
  const fallback = f.api.getPlayhubOnboardingSnapshot();
  assert.equal(fallback.view, "playhub", "the guide presents instead of leaving a blocked QAM with nothing to see");
  assert.equal(fallback.qamBoundsEstimated, true);
  assert.deepEqual({ ...fallback.qamBounds }, { left: 1056, top: 0, width: 864, height: 1080 });
  assert.equal(fallback.layoutStable, true, "estimated geometry is stable by construction");
  assert.equal(fallback.document, f.env.mainDocument);

  // Real bounds always win once Steam reports them, and reset the grace period.
  f.env.qamBounds = { left: 900, top: 20, width: 855, height: 682 };
  f.change();
  const real = f.api.getPlayhubOnboardingSnapshot();
  assert.equal(real.qamBoundsEstimated, false);
  assert.equal(real.anchor.left, 900);
  assert.equal(real.anchor.top, 220);

  delete f.env.qamBounds; f.change();
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, null, "losing bounds restarts the grace period");
  clock += 1600; f.change();
  assert.equal(f.api.getPlayhubOnboardingSnapshot().qamBoundsEstimated, true);

  f.env.qamOpen = false; f.change();
  assert.equal(f.api.getPlayhubOnboardingSnapshot().qamBoundsEstimated, false);
  stop();
});

test("a viewport too small for a readable band keeps the step withheld rather than misplaced", () => {
  let clock = 1000;
  const f = fixture(() => clock);
  f.env.mainDocument = { body: {}, defaultView: { innerWidth: 420, innerHeight: 300 } };
  f.env.document.defaultView = { innerWidth: 420, innerHeight: 300 };
  f.env.qamOpen = true;
  f.found.set("#quickaccess_tab_5261387", { left: 0, top: 100, width: 48, height: 48 });
  const stop = f.api.initPlayhubOnboarding(f.options);
  clock += 5000; f.change();
  const snapshot = f.api.getPlayhubOnboardingSnapshot();
  assert.equal(snapshot.view, null);
  assert.equal(snapshot.qamBoundsEstimated, false);
  stop();
});

/** "It blocks the QAM and there is nothing to see" must be unreachable, whatever the cause. */
test("a showcase that can never present hands the QAM back instead of holding it captive", () => {
  let clock = 1000;
  const f = fixture(() => clock);
  const requested = [];
  f.options.showControlStep = step => { requested.push(step); f.env.controlStep = step; };
  f.env.steps = ["home", "decky"];
  f.env.controlsReady = true;
  f.env.mainDocument = { body: {}, defaultView: { innerWidth: 1920, innerHeight: 1080 } };
  f.env.document.defaultView = { innerWidth: 855, innerHeight: 682 };
  f.env.qamOpen = true;
  f.env.playhubSelected = true;
  f.env.qamBounds = { left: 900, top: 0, width: 855, height: 682 };
  // Content never reports ready, so no step can ever be presented.
  f.env.controlReady = false;
  f.found.set("#quickaccess_tab_5261387", { left: 0, top: 200, width: 48, height: 48 });
  const stop = f.api.initPlayhubOnboarding(f.options);

  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, null);
  assert.ok(requested.includes("home"), "the guide first asks the tab to showcase itself");
  clock += 7000; f.change();
  assert.equal(f.api.getPlayhubOnboardingSnapshot().state.paused, false, "not given up too early");

  clock += 1500; f.change();
  const snapshot = f.api.getPlayhubOnboardingSnapshot();
  assert.equal(snapshot.state.paused, true, "the guide pauses itself rather than freezing the menu");
  assert.equal(snapshot.view, null);
  assert.equal(requested.at(-1), null, "the QAM showcase is released");
  stop();
});

test("a presentable step is never mistaken for a stuck showcase", () => {
  let clock = 1000;
  const f = fixture(() => clock);
  const requested = [];
  f.options.showControlStep = step => { requested.push(step); f.env.controlStep = step; };
  f.env.steps = ["home"];
  f.env.controlsReady = true;
  f.env.controlReady = true;
  f.env.mainDocument = { body: {}, defaultView: { innerWidth: 1920, innerHeight: 1080 } };
  f.env.document.defaultView = { innerWidth: 855, innerHeight: 682 };
  f.env.qamOpen = true;
  f.env.playhubSelected = true;
  f.env.qamBounds = { left: 900, top: 0, width: 855, height: 682 };
  f.found.set("#quickaccess_tab_5261387", { left: 0, top: 200, width: 48, height: 48 });
  f.found.set('[data-playhub-onboarding="home"]', { left: 100, top: 200, width: 300, height: 60 });
  const stop = f.api.initPlayhubOnboarding(f.options);
  clock += 30000; f.change();
  const snapshot = f.api.getPlayhubOnboardingSnapshot();
  assert.equal(snapshot.state.paused, false);
  assert.equal(snapshot.view, "home");
  assert.equal(requested.at(-1), "home");
  stop();
});

/** Steam's own placeholder element is an exact source when the native call is unavailable. */
test("the QAM band is measured from Steam's placeholder before any estimate is used", () => {
  const anchorsApi = anchors;
  const styles = new Map();
  const element = (rect, style, children = 0) => ({
    children: [], childElementCount: children,
    getBoundingClientRect: () => rect,
    register(view) { styles.set(this, style); return this; },
  });
  const make = (rect, style, children = 0) => {
    const node = element(rect, style, children);
    styles.set(node, style);
    return node;
  };
  // Matches ._2orc8-PBheKd3BvuGXV6ZL in the installed Steam CSS: absolute, right-aligned,
  // fixed width, full height, and empty because it only hosts the native view.
  const placeholder = make({ left: 2501, top: 60, right: 3355, bottom: 1400, width: 854, height: 1340 }, { position: "absolute" });
  const sidebar = make({ left: 0, top: 0, right: 240, bottom: 1400, width: 240, height: 1400 }, { position: "absolute" });
  const populated = make({ left: 2501, top: 0, right: 3355, bottom: 1400, width: 854, height: 1400 }, { position: "absolute" }, 3);
  const body = { children: [sidebar, placeholder, populated], childElementCount: 3, getBoundingClientRect: () => ({}) };
  const main = { body, defaultView: { innerWidth: 3355, innerHeight: 1400, getComputedStyle: node => styles.get(node) ?? {} } };

  const bounds = anchorsApi.readOnboardingQamPlaceholderBounds(main);
  assert.deepEqual({ ...bounds }, { left: 2501, top: 60, width: 854, height: 1340 });
  assert.notDeepEqual({ ...bounds }, { ...anchorsApi.onboardingFallbackQamBounds({ width: 3355, height: 1400 }) },
    "the measurement is used instead of the estimate");

  // Nothing matching means nothing invented.
  const empty = { body: { children: [sidebar], childElementCount: 1 },
    defaultView: { innerWidth: 3355, innerHeight: 1400, getComputedStyle: node => styles.get(node) ?? {} } };
  assert.equal(anchorsApi.readOnboardingQamPlaceholderBounds(empty), null);
  assert.equal(anchorsApi.readOnboardingQamPlaceholderBounds({ body: null, defaultView: null }), null);
});

test("a confirmed step holds input until the next slide exists, and never past its timeout", () => {
  let clock = 1000;
  const f = fixture(() => clock);
  f.env.mainDocument = { body: {}, defaultView: { innerWidth: 1920, innerHeight: 1080 } };
  f.env.document.defaultView = { innerWidth: 855, innerHeight: 682 };
  f.env.qamOpen = true;
  f.env.qamBounds = { left: 1066, top: 0, width: 854, height: 1080 };
  f.found.set("#quickaccess_tab_5261387", { left: 0, top: 200, width: 48, height: 48 });
  const stop = f.api.initPlayhubOnboarding(f.options);
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, "playhub");

  f.api.markPlayhubOnboardingPresented("playhub");
  assert.equal(f.api.reportPlayhubOnboardingAction("confirm"), true);
  assert.equal(f.api.getPlayhubOnboardingSnapshot().advancing, true, "the slide is settling");

  // A second press between slides is consumed by the guide, not by the Steam menu behind it.
  const step = f.api.getPlayhubOnboardingSnapshot().state.step;
  assert.equal(f.api.reportPlayhubOnboardingAction("confirm"), true);
  assert.equal(f.api.getPlayhubOnboardingSnapshot().state.step, step, "a held press cannot skip a slide");

  // B still pauses while the shield is up.
  assert.equal(f.api.reportPlayhubOnboardingAction("cancel"), true);
  assert.equal(f.api.getPlayhubOnboardingSnapshot().state.paused, true);
  assert.equal(f.api.getPlayhubOnboardingSnapshot().advancing, false, "pausing lowers the shield");
  stop();
});

test("the shield is released once the next slide is computed", () => {
  let clock = 1000;
  const f = fixture(() => clock);
  f.env.mainDocument = { body: {}, defaultView: { innerWidth: 1920, innerHeight: 1080 } };
  f.env.document.defaultView = { innerWidth: 855, innerHeight: 682 };
  f.env.qamOpen = true;
  f.env.qamBounds = { left: 1066, top: 0, width: 854, height: 1080 };
  f.found.set("#quickaccess_tab_5261387", { left: 0, top: 200, width: 48, height: 48 });
  const stop = f.api.initPlayhubOnboarding(f.options);
  f.api.markPlayhubOnboardingPresented("playhub");
  f.api.reportPlayhubOnboardingAction("confirm");
  assert.equal(f.api.getPlayhubOnboardingSnapshot().advancing, true);
  f.env.playhubSelected = true;
  f.found.set('[data-playhub-onboarding="decky"]', { left: 40, top: 300, width: 200, height: 40 });
  f.change();
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, "decky");
  assert.equal(f.api.getPlayhubOnboardingSnapshot().advancing, false, "the next slide lowers the shield");
  stop();
});

/** "Two presses to advance": a slide drawn before its geometry settles must keep the press. */
test("a single input always advances one slide, even pressed before the slide settles", () => {
  let clock = 1000;
  const f = fixture(() => clock);
  f.env.mainDocument = { body: {}, defaultView: { innerWidth: 1920, innerHeight: 1080 } };
  f.env.document.defaultView = { innerWidth: 855, innerHeight: 682 };
  f.env.qamOpen = true;
  f.env.qamBounds = { left: 1066, top: 0, width: 854, height: 1080 };
  f.found.set("#quickaccess_tab_5261387", { left: 0, top: 200, width: 48, height: 48 });
  const stop = f.api.initPlayhubOnboarding(f.options);
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, "playhub");

  // The slide is visible but the component has not reported it as settled yet.
  assert.equal(f.api.reportPlayhubOnboardingAction("confirm"), true, "the press is consumed, never lost to the menu");
  assert.equal(f.api.getPlayhubOnboardingSnapshot().state.step, "playhub", "and it does not advance on unsettled geometry");

  // As soon as the slide settles, the kept press is applied without asking again.
  f.api.markPlayhubOnboardingPresented("playhub");
  return Promise.resolve().then(() => Promise.resolve()).then(() => {
    assert.equal(f.api.getPlayhubOnboardingSnapshot().advancing, true, "the kept press advanced the slide");
    assert.equal(f.enters(), 1, "exactly one advance for one input");
    stop();
  });
});

test("a kept press is dropped when the guide moves on or is paused instead of firing later", () => {
  let clock = 1000;
  const f = fixture(() => clock);
  f.env.mainDocument = { body: {}, defaultView: { innerWidth: 1920, innerHeight: 1080 } };
  f.env.document.defaultView = { innerWidth: 855, innerHeight: 682 };
  f.env.qamOpen = true;
  f.env.qamBounds = { left: 1066, top: 0, width: 854, height: 1080 };
  f.found.set("#quickaccess_tab_5261387", { left: 0, top: 200, width: 48, height: 48 });
  const stop = f.api.initPlayhubOnboarding(f.options);
  f.api.reportPlayhubOnboardingAction("confirm");
  f.env.qamOpen = false; f.change();
  f.env.qamOpen = true; f.change();
  f.api.markPlayhubOnboardingPresented("playhub");
  return Promise.resolve().then(() => Promise.resolve()).then(() => {
    assert.equal(f.enters(), 0, "a stale press never advances a slide the user is looking at now");
    stop();
  });
});

/** Reported after a rebuild: A stopped advancing the slides at all.
 *
 *  A press on a slide the overlay had not yet reported as settled was parked in
 *  pendingConfirm, and pendingConfirm was cleared only when the view changed — which
 *  it never did, because advancing was the very thing being waited for. One missing
 *  measurement turned the guide into a wall the user could not get past, and B is
 *  deliberately not an escape. The press must land regardless. */
test("a press lands even when the slide never reports itself as settled", async () => {
  let clock = 1000;
  const f = fixture(() => clock);
  f.env.mainDocument = { body: {}, defaultView: { innerWidth: 1920, innerHeight: 1080 } };
  f.env.document.defaultView = { innerWidth: 855, innerHeight: 682 };
  f.env.qamOpen = true;
  f.found.set("#quickaccess_tab_5261387", { left: 0, top: 200, width: 48, height: 48 });
  const stop = f.api.initPlayhubOnboarding(f.options);
  clock += 1600; f.change();
  assert.equal(f.api.getPlayhubOnboardingSnapshot().view, "playhub");

  // The overlay never calls markPlayhubOnboardingPresented: the press is held.
  assert.equal(f.api.reportPlayhubOnboardingAction("confirm"), true, "the press is taken, not dropped");
  assert.equal(f.enters(), 0, "nothing confirmed while the slide may still settle");

  clock += 400; f.change();
  assert.equal(f.enters(), 0, "still inside the grace");

  clock += 1000; f.change();
  await Promise.resolve(); await Promise.resolve();
  assert.equal(f.enters(), 1, "the grace expires and the held press advances the guide");
  stop();
});

test("a slide that does settle is confirmed at once, without waiting out the grace", async () => {
  let clock = 1000;
  const f = fixture(() => clock);
  f.env.mainDocument = { body: {}, defaultView: { innerWidth: 1920, innerHeight: 1080 } };
  f.env.document.defaultView = { innerWidth: 855, innerHeight: 682 };
  f.env.qamOpen = true;
  f.found.set("#quickaccess_tab_5261387", { left: 0, top: 200, width: 48, height: 48 });
  const stop = f.api.initPlayhubOnboarding(f.options);
  clock += 1600; f.change();
  f.api.markPlayhubOnboardingPresented("playhub");
  assert.equal(f.api.reportPlayhubOnboardingAction("confirm"), true);
  assert.equal(f.enters(), 1, "no delay when the slide is ready");
  stop();
});

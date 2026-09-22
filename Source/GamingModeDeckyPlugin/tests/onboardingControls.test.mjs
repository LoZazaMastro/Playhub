import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";

function load(file) {
  const exports = {};
  vm.runInNewContext(ts.transpileModule(readFileSync(new URL(`../src/${file}`, import.meta.url), "utf8"), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
  }).outputText, { exports });
  return exports;
}

test("showcase activates real internal views temporarily without changing persisted selection/hidden tabs", () => {
  const api = load("onboardingControls.ts");
  const visible = Object.freeze(["home", "decky", "store", "controller"]);
  const prefs = Object.freeze({ active: "controller", customizing: false });
  let changes = 0; const stop = api.subscribeOnboardingControlStep(() => changes++);
  for (const step of ["decky", "store", "customize"]) {
    api.requestOnboardingControlStep(step);
    const view = api.resolveOnboardingControlShowcase(api.getOnboardingControlStep(), true, visible, prefs.active, prefs.customizing);
    assert.equal(view.active, step === "customize" ? "controller" : step);
    assert.equal(view.customizing, step === "customize"); assert.equal(view.showcasing, true);
    api.requestOnboardingControlStep(step);
  }
  assert.equal(changes, 3, "repeated observations are idempotent");
  api.requestOnboardingControlStep(null);
  const restored = api.resolveOnboardingControlShowcase(api.getOnboardingControlStep(), true, visible, prefs.active, prefs.customizing);
  assert.equal(restored.active, "controller"); assert.equal(restored.customizing, false);
  assert.equal(restored.showcasing, false); stop();
  const hidden = api.resolveOnboardingControlShowcase("store", true, ["home"], "home", false);
  assert.equal(hidden.active, "home"); assert.equal(hidden.showcasing, false);
  const pending = api.resolveOnboardingControlShowcase("decky", false, visible, "controller", false);
  assert.equal(pending.active, "controller"); assert.equal(pending.showcasing, false);
  const hiddenHome = api.resolveOnboardingControlShowcase("home", true, Object.freeze(["store"]), "store", true);
  assert.equal(hiddenHome.active, "store"); assert.equal(hiddenHome.customizing, true);
  assert.deepEqual(Array.from(api.onboardingVisibleSteps(["controller", "store", "home", "audio"])), ["controller", "store", "playhub", "audio", "customize"]);
  assert.deepEqual(Array.from(api.onboardingVisibleSteps(["graphics", "decky"])), ["graphics", "decky", "customize"]);
  assert.equal(api.resolveOnboardingControlShowcase(null, true, ["store"], "store", true).active, "store");
});

test("content readiness needs mounted, visible content and respects real ancestor clipping", () => {
  const { visibleOnboardingContent } = load("onboardingAnchors.ts");
  const bounds = { left: 48, top: 140, width: 300, height: 900, right: 348, bottom: 1040 };
  const body = { parentElement: null, getBoundingClientRect: () => ({ width: 855, height: 0 }), style: { overflowY: "hidden" } };
  const clip = { parentElement: body, getBoundingClientRect: () => ({ left: 48, top: 120, width: 300, height: 480, right: 348, bottom: 600 }),
    style: { overflowY: "auto" } };
  const element = { isConnected: true, childElementCount: 1, closest: () => null, parentElement: clip,
    getBoundingClientRect: () => bounds, style: {} };
  const doc = { querySelector: () => element, defaultView: { innerWidth: 855, innerHeight: 682, getComputedStyle: node => node.style } };
  assert.equal(visibleOnboardingContent(doc, "#content"), true, "partially scrolled content can be genuinely visible");
  clip.style.opacity = "0.5";
  assert.equal(visibleOnboardingContent(doc, "#content"), true);
  assert.equal(visibleOnboardingContent(doc, "#content", true), false, "native opening fade must finish before frame stability begins");
  clip.style.opacity = "1";
  assert.equal(visibleOnboardingContent(doc, "#content", true), true);
  element.childElementCount = 0; assert.equal(visibleOnboardingContent(doc, "#content"), false);
  element.childElementCount = 1; element.isConnected = false; assert.equal(visibleOnboardingContent(doc, "#content"), false);
  element.isConnected = true; clip.style.display = "none"; assert.equal(visibleOnboardingContent(doc, "#content"), false);
  delete clip.style.display; bounds.top = 610; assert.equal(visibleOnboardingContent(doc, "#content"), false);
  bounds.top = 140; element.closest = () => ({}); assert.equal(visibleOnboardingContent(doc, "#content"), false);
});

test("ControlCenter uses ephemeral selection and committed content readiness without changing host focus ownership", () => {
  const source = readFileSync(new URL("../src/ControlCenter.tsx", import.meta.url), "utf8");
  assert.match(source, /useOnboardingControlShowcase\(origin === "qam", ready, visible, manualActive, manualCustomizing\)/);
  assert.match(source, /data-onboarding-active-tab=\{customizing \? "customize" : active\}/);
  assert.match(source, /data-onboarding-content-ready=\{ready && \(active !== "decky" \|\| customizing \|\| deckyHost.ready\)\}/);
  assert.match(source, /data-playhub-onboarding-content=\{origin === "qam" \? "store" : undefined\}/);
  assert.match(source, /data-playhub-onboarding-content=\{origin === "qam" \? "home" : undefined\}/);
  assert.match(source, /data-playhub-onboarding-content=\{origin === "qam" \? "customize" : undefined\}/);
  assert.match(source, /const hostActive = !customizing && active === "decky" && deckyAvailable/);
  assert.doesNotMatch(source, /resumePlayhubOnboarding/);
});


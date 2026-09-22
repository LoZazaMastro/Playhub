import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";
const exports = {};
vm.runInNewContext(ts.transpileModule(readFileSync(new URL("../src/onboardingAnchors.ts", import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText, { exports });

test("native 855x682 QAM anchors convert from the exact BrowserView into main CSS coordinates", () => {
  const bounds = { left: 925, top: 30, width: 855, height: 682 };
  for (const local of [{ left: 0, top: 200, width: 48, height: 48 }, { left: 180, top: 90, width: 40, height: 40 }]) {
    const anchor = exports.convertOnboardingAnchor(local, bounds, { width: 855, height: 682 });
    const card = exports.positionOnboardingShowcase(anchor, bounds, { width: 1920, height: 1080 }, { width: 360, height: 220 });
    assert.ok(card.left + card.width < bounds.left, "text does not cover QAM");
    assert.equal(card.pointer, undefined);
    assert.equal(card.left + card.width / 2, (16 + bounds.left - 24) / 2);
    assert.equal(card.top + 110, (1080 - 96) / 2);
    assert.ok(card.left >= 16 && card.top >= 16);
  }
});

test("different BrowserView scales and offsets are measured, never derived from the 300px controls pane", () => {
  const rect = exports.convertOnboardingAnchor({ left: 48, top: 100, width: 300, height: 40 },
    { left: 800, top: 60, width: 427.5, height: 341 }, { width: 855, height: 682 });
  assert.deepEqual(JSON.parse(JSON.stringify(rect)), { left: 824, top: 110, width: 150, height: 20 });
  assert.equal(exports.convertOnboardingAnchor(rect, rect, { width: 0, height: 682 }), null);
});

test("unmeasured or cramped QAM hides the tooltip instead of overlapping or clamping its caret", () => {
  const anchor = { left: 900, top: 200, width: 48, height: 48 };
  const view = { width: 1280, height: 720 }, size = { width: 360, height: 220 };
  assert.equal(exports.positionOnboardingShowcase(anchor, null, view, size), null);
  assert.equal(exports.positionOnboardingShowcase(anchor, { left: 250 }, view, size), null);
  assert.equal(exports.positionOnboardingShowcase({ ...anchor, top: 710 }, { left: 900 }, view, size), null);
  const card = exports.positionOnboardingShowcase({ ...anchor, top: 560 }, { left: 900 }, view, size);
  assert.equal(card.top + 110, (720 - 96) / 2);
  assert.ok(card.top + 220 <= 720 - 96 - 16);
});

test("native BrowserView lookup binds by QAM document identity and only reads GetBounds", () => {
  const qam = {};
  let reads = 0;
  const owner = { GetViewWindow: () => ({ document: qam }), GetBrowserView: () => ({
    GetBounds: () => { reads++; return { x: 700, y: 0, width: 855, height: 682 }; }
  }) };
  const element = { __reactFiber$test: { memoizedState: { memoizedState: owner } } };
  const main = { querySelectorAll: () => [element] };
  assert.equal(exports.readOnboardingBrowserViewBounds(main, {}), null);
  assert.equal(exports.readOnboardingBrowserViewBounds(main, qam).left, 700);
  assert.equal(reads, 1);
});

test("unframed text has no pointer, card or extra blur; Store stacking remains isolated", () => {
  const coach = readFileSync(new URL("../src/PlayhubOnboarding.tsx", import.meta.url), "utf8");
  assert.doesNotMatch(coach, /data-playhub-onboarding-pointer|showcase\.pointer|blur\(18px\)|saturate\(/);
  assert.match(coach, /padding: 0, borderRadius: 0, border: 0, background: "transparent"/);
  assert.match(coach, /import \{ OnboardingIllustration \} from "\.\/OnboardingIllustration"/);
  // The block beside the menu is 60% wider than the old tooltip, and the art scales with it.
  assert.match(coach, /const blockWidth = fullscreen \? 532 : 576/);
  assert.match(coach, /\{ width: 470, height: 208, scale: 1\.6 \}/);
  assert.match(coach, /maxWidth: art\.width, height: art\.height/);
  assert.match(coach, /: visible && <OnboardingIllustration view=\{view\} scale=\{art\.scale\} height=\{art\.height\} \/>\}/);
  assert.match(coach, /setHeight\(element\.offsetHeight\)/, "illustration participates in measured text bounds");
  assert.doesNotMatch(coach, /height: 3, background|rotate\(45deg\)|#45cdb5/);
  const store = readFileSync(new URL("../src/PluginStoreIntro.tsx", import.meta.url), "utf8");
  assert.match(store, /\.ph-store-intro\{isolation:isolate;/);
  assert.match(store, /\.ph-store-launch\{position:relative;z-index:1\}/);
  assert.match(store, /\.ph-store-download\{position:relative;z-index:0;[^}]*pointer-events:none/);
  assert.match(store, /\.ph-store-download\{[^}]*overflow:hidden/);
  assert.match(store, /\.ph-store-download-viewport\{position:absolute;[^}]*pointer-events:none/);
});

test("actual Store component measures wrapper padding and clips exactly at the real button bottom", () => {
  const source = ts.transpileModule(readFileSync(new URL("../src/PluginStoreIntro.tsx", import.meta.url), "utf8"), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX }
  }).outputText;
  for (const width of [174, 300, 480]) {
    for (const [buttonHeight, wrapperGap] of [32, 56, 80, 400].flatMap(height => [0, 8, 10, 24].map(gap => [height, gap]))) {
      const states = []; let stateIndex = 0, refIndex = 0; const effects = [];
      const buttonTop = 12, buttonBottom = buttonTop + buttonHeight, launchBottom = buttonBottom + wrapperGap;
      const refs = [{ clientWidth: width, style: { setProperty() {} } }, {
        getBoundingClientRect: () => ({ bottom: launchBottom }),
        querySelector: () => ({ getBoundingClientRect: () => ({ top: buttonTop, bottom: buttonBottom, height: buttonHeight }) })
      }];
      const React = { useRef: () => ({ current: refs[refIndex++] }),
        useState: initial => { const index = stateIndex++; if (!(index in states)) states[index] = initial;
          return [states[index], value => { states[index] = value; }]; },
        useLayoutEffect: effect => effects.push(effect) };
      const jsx = { jsx: (type, props) => ({ type, props }), jsxs: (type, props) => ({ type, props }) };
      const module = {};
      vm.runInNewContext(source, { exports: module,
        ResizeObserver: class { observe() {} disconnect() {} },
        require: name => name === "./decky" ? { SP_REACT: React } : name === "react/jsx-runtime" ? jsx : {} });
      module.PluginStoreIntro({ description: "Store", button: {} });
      const cleanup = effects[0](); stateIndex = refIndex = 0;
      const tree = module.PluginStoreIntro({ description: "Store", button: {} });
      const outer = tree.props.children.find(node => node.props?.className === "ph-store-download");
      const inner = outer.props.children;
      const height = Math.ceil(84 * width / 174), overlap = buttonHeight / 2;
      assert.equal(outer.props.style.height, Math.max(0, height - overlap));
      assert.equal(outer.props.style.marginTop, -wrapperGap);
      assert.equal(outer.props.style.top, undefined);
      assert.equal(inner.props.className, "ph-store-download-viewport");
      assert.equal(inner.props.style.top, -overlap);
      assert.equal(inner.props.style.height, height);
      const outerTop = launchBottom + outer.props.style.marginTop;
      assert.equal(outerTop, buttonBottom, "clip rectangle starts at the real button bottom, with no gap");
      assert.equal(outerTop + inner.props.style.top, buttonTop + buttonHeight / 2);
      assert.ok(outerTop + outer.props.style.height >= buttonBottom, "clip rectangle never enters button bounds");
      assert.ok(inner.props.style.top + inner.props.style.height <= outer.props.style.height,
        "visible bottom stays inside the outer flow height");
      cleanup();
    }
  }
});

test("BrowserView discovery uses committed fibers before Playhub content registers", () => {
  const qam = { getElementById: id => id === "quickaccess_tab_42" ? {} : null };
  const owner = { GetViewWindow: () => ({ document: qam }), GetBrowserView: () => ({
    GetBounds: () => ({ x: 940, y: 10, width: 855, height: 682 })
  }) };
  const current = { child: { memoizedState: { memoizedState: owner } } };
  const stale = { stateNode: { current }, child: { memoizedState: null } };
  const main = { querySelectorAll: () => [{ __reactFiber$test: { return: stale } }] };
  assert.equal(exports.findOnboardingBrowserViewDocument(main, 42), qam);
  assert.equal(exports.findOnboardingBrowserViewDocument(main, 999), null);
  assert.equal(exports.readOnboardingBrowserViewBounds(main, qam).left, 940);
});

test("BrowserView discovery is cached across document lookup and bounds reads, invalidated on replacement or mount", () => {
  let scans = 0, boundsReads = 0, closed = false;
  let qam = { getElementById: () => ({}) };
  let bounds = { x: 900, y: 0, width: 855, height: 682 };
  const owner = { GetViewWindow: () => ({ document: qam, closed }), GetBrowserView: () => ({
    GetBounds: () => { boundsReads++; return bounds; } }) };
  const root = { memoizedState: { memoizedState: owner } };
  const main = { querySelectorAll: selector => {
    assert.notEqual(selector, "*"); scans++; return [{ __reactContainer$test: { stateNode: { current: root } } }];
  } };
  for (let i = 0; i < 20; i++) {
    assert.equal(exports.findOnboardingBrowserViewDocument(main, 42), qam);
    assert.equal(exports.readOnboardingBrowserViewBounds(main, qam).left, 900);
  }
  assert.equal(scans, 1); assert.equal(boundsReads, 20, "native bounds remain fresh on every read");
  bounds = { ...bounds, width: 0 };
  assert.equal(exports.readOnboardingBrowserViewBounds(main, qam), null);
  assert.equal(scans, 1);
  qam = { getElementById: () => ({}) };
  assert.equal(exports.findOnboardingBrowserViewDocument(main, 42), qam); assert.equal(scans, 2);
  closed = true; assert.equal(exports.findOnboardingBrowserViewDocument(main, 42), null); assert.equal(scans, 3);
  for (let i = 0; i < 20; i++) exports.findOnboardingBrowserViewDocument(main, 42);
  assert.equal(scans, 3, "cached misses do not rescan on animations");
  closed = false; exports.invalidateOnboardingBrowserViewOwners(main);
  assert.equal(exports.findOnboardingBrowserViewDocument(main, 42), qam); assert.equal(scans, 4);
});

test("showcase matrix centers unframed text independently of anchor, within footer boundaries", () => {
  for (const [width, height] of [[800, 600], [1280, 720], [1280, 800], [1920, 1080], [2560, 1440]]) {
    for (const contentHeight of [120, 220, 380, 1200]) {
      for (const targetY of [40, 110, height / 2, height - 140]) {
        const qam = { left: width - 348, top: 0, width: 855, height: 682 };
        const anchor = { left: qam.left, top: targetY - 20, width: 48, height: 40 };
        const card = exports.positionOnboardingShowcase(anchor, qam, { width, height }, { width: 360, height: contentHeight });
        assert.ok(card, `${width}x${height}, content ${contentHeight}, target ${targetY}`);
        assert.ok(card.left >= 16);
        assert.ok(card.left + card.width + 10 <= qam.left - 10);
        assert.ok(card.top >= 16);
        assert.ok(card.top + Math.min(contentHeight, card.maxHeight) <= height - 96 - 16);
        assert.equal(card.left + card.width / 2, (16 + qam.left - 24) / 2);
        assert.equal(card.top + Math.min(contentHeight, card.maxHeight) / 2, (height - 96) / 2);
        assert.equal(card.pointer, undefined);
      }
    }
  }
});

test("scaled main portal keeps centered text entirely left of native QAM", () => {
  for (const scale of [0.8, 1, 1.25, 1.5, 2]) {
    const doc = { defaultView: { innerWidth: 1920, innerHeight: 1080 } };
    const portal = { clientWidth: 1920 / scale, clientHeight: 1080 / scale,
      getBoundingClientRect: () => ({ left: 0, top: 0, width: 1920, height: 1080 }) };
    const qam = { left: 1500, top: 0, width: 855, height: 682 };
    const anchor = { left: 1500, top: 200, width: 48, height: 48 };
    const geometry = exports.onboardingPortalGeometry(doc, portal, anchor);
    const qamGeometry = exports.onboardingPortalGeometry(doc, portal, qam);
    const card = exports.positionOnboardingShowcase(geometry.anchor, qamGeometry.anchor, geometry, { width: 360, height: 220 });
    assert.ok(card);
    assert.equal(card.left + card.width / 2, (16 + qamGeometry.anchor.left - 24) / 2);
    assert.ok((card.left + card.width + 10) * scale < qam.left);
  }
});

test("observer ignores own overlays and unrelated assets but retains readiness and mount signals", () => {
  const relevant = exports.isOnboardingMutationRelevant;
  assert.equal(relevant({ type: "attributes", target: { closest: () => ({}) } }), false);
  assert.equal(relevant({ type: "attributes", target: { matches: () => false, closest: () => null, querySelector: () => null } }), false);
  assert.equal(relevant({ type: "attributes", target: { matches: () => true } }), true);
  assert.equal(relevant({ type: "childList", target: {}, addedNodes: [{ matches: () => false }], removedNodes: [] }), false);
  assert.equal(relevant({ type: "childList", target: {}, addedNodes: [{ matches: selector => selector.includes('role="dialog"') }], removedNodes: [] }), true);
  assert.equal(relevant({ type: "childList", target: {}, addedNodes: [], removedNodes: [{ id: "root" }] }), true);
});

test("QAM stability requires consecutive visible frames and resets on movement, closure and resize", () => {
  const tracker = exports.createOnboardingBoundsStability();
  const view = { width: 1920, height: 1080 };
  const bounds = { left: 1500, top: 0, width: 855, height: 682 };
  for (const left of [1900, 1800, 1600, 1500]) assert.equal(tracker.sample({ ...bounds, left }, true, view), false);
  assert.equal(tracker.sample(bounds, true, view), false);
  assert.equal(tracker.sample(bounds, true, view), false);
  assert.equal(tracker.sample(bounds, true, view), true);
  assert.equal(tracker.sample(bounds, false, view), false);
  for (let i = 0; i < 3; i++) assert.equal(tracker.sample(bounds, true, view), false);
  assert.equal(tracker.sample(bounds, true, view), true);
  assert.equal(tracker.sample(bounds, true, { ...view, width: 1800 }), false);
  tracker.reset();
  for (const delta of [0, .3, .6, .9, 1.2, 1.5]) {
    assert.equal(tracker.sample({ ...bounds, left: bounds.left + delta }, true, view), false, "slow cumulative drift is not stable");
  }
  assert.equal(tracker.sample(null, true, view), false);
  assert.equal(tracker.ready(), false);
});

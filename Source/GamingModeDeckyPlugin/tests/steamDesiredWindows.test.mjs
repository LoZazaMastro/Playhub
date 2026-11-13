import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";

const source = name => ts.transpileModule(readFileSync(new URL(`../src/${name}.ts`, import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText;
const qamCode = source("controlledQamIntegration"), hudCode = source("quickSettings/livePerformanceRuntime");
const focusCode = source("steamFocusEvents");

function fixture() {
  let desired = 7, updates = 0, registrations = 0, readFailure = false, currentMain;
  const timers = new Map(), patched = new WeakMap();
  let nextTimer = 0;
  function afterPatch(object, key, handler) {
    let slots = patched.get(object);
    if (!slots) patched.set(object, slots = new Map());
    let slot = slots.get(key);
    if (!slot) {
      slot = { original: object[key], handlers: new Map() };
      slots.set(key, slot);
      object[key] = function (...args) {
        let result = slot.original.apply(this, args);
        for (const callback of [...slot.handlers.values()]) result = callback(args, result);
        return result;
      };
    }
    const id = Symbol(); slot.handlers.set(id, handler);
    return { unpatch() { slot.handlers.delete(id); if (!slot.handlers.size) { object[key] = slot.original; slots.delete(key); } } };
  }
  function makeMain() {
    let side = 0;
    return { IsGamepadUIOverlayWindow: () => false, OnHomeButtonPressed() {}, MenuStore: {
      GetOpenSideMenu: () => side, GetQuickAccessTab: () => 4,
      OpenSideMenu(value) { side = value; }, CloseSideMenus() { side = 0; },
    } };
  }
  const store = {
    OverlayWindows: [],
    get GamepadUIMainWindowInstance() { if (readFailure) throw new Error("Observer read failed"); return currentMain; },
    UpdateDesiredWindows(mode) { updates++; currentMain = mode === 4 ? makeMain() : undefined; return "Valve result"; },
  };
  const nativeUpdate = store.UpdateDesiredWindows;
  // This is deliberately a SINGLE native callback slot, not a subscriber list.
  // The regression replaced this listener with an observer of the stale store.
  let nativeFocusListener, focusRegistrations = 0;
  const focused = { m_unFocusedOverlayAppID: 0, m_unFocusedOverlayPID: 0,
    OnOverlayGameWindowFocusChanged(appId, pid) {
      this.m_unFocusedOverlayAppID = appId; this.m_unFocusedOverlayPID = pid; return "Valve focus result";
    } };
  focused.OnOverlayGameWindowFocusChanged = focused.OnOverlayGameWindowFocusChanged.bind(focused);
  const steam = { UI: { RegisterDesiredSteamUIWindowsChanged(callback) { registrations++; nativeListener = callback; } },
    System: { UI: { RegisterForOverlayGameWindowFocusChanged(callback) { focusRegistrations++; nativeFocusListener = callback; } } },
    Overlay: { RegisterForOverlayActivated() { return { unregister() {} }; } } };
  let nativeListener;
  const steamListener = () => store.UpdateDesiredWindows(desired);
  steam.UI.RegisterDesiredSteamUIWindowsChanged(steamListener);
  const steamFocusListener = focused.OnOverlayGameWindowFocusChanged;
  steam.System.UI.RegisterForOverlayGameWindowFocusChanged(steamFocusListener);
  const window = { settingsStore: {}, SteamClient: steam, FocusedAppWindowStore: focused,
    SteamUIStore: { MainRunningAppID: 0, WindowStore: store },
    setTimeout(callback) { const id = ++nextTimer; timers.set(id, callback); return id; },
    clearTimeout(id) { timers.delete(id); } };
  const ui = { Router: { MainRunningAppID: 0, WindowStore: store }, afterPatch };
  const noop = () => {};
  function load(code, imports) {
    const exports = {};
    vm.runInNewContext(code, { exports, window, console: { warn: noop }, AbortSignal, Date, Math,
      require(name) { assert.ok(name in imports, `Unexpected dependency ${name}`); return imports[name]; } });
    return exports;
  }
  const owned = load(source("steamOwnedLaunchDispatcher"), {});
  const imports = {
    "./steamNativeGameOverlay": load(source("steamNativeGameOverlay"), {}),
    "./steamOwnedLaunchDispatcher": { ...owned, createOwnedLaunchDispatcher: () => ({ app: () => 0, signal: () => false, dispose: noop, invalidate: noop, noteLaunch: noop, expected: () => undefined }) },
    "./steamMirrorGuideAdmission": {createSteamMirrorGuideAdmission:()=>({signal:()=>false,invalidate:noop})},
    "./decky": { DFL: ui }, "./api": { API_BASE: "fixture", logToAgent: noop }, "./controlBackend": { call: noop },
    "./dashboardOverlay": { matchesOverlayApp: () => false, selectOverlay: () => undefined },
    "./controlledQamFocus": { steamAppId: value => value, createControlledQamFocus: () => ({ dispose: noop, applicationSelected: noop }) },
    "./xboxCompanionOverlay": {}, "./steamOverlayFallback": { createSteamOverlayFallback: () => ({ dispose: noop, invalidate: noop }) },
    "./steamMirrorOverlay": { createSteamMirrorOverlay: () => ({ dispose: noop, invalidate: noop }) },
    "./steamReturnControllerLayout": { activeSteamController: () => undefined },
  };
  const fakeController = () => ({ read: () => ({ state: { enabled: false } }), start: noop, dispose: noop });
  return {
    mountQam: () => load(qamCode, imports).installControlledQamFocus(),
    mountHud: () => load(hudCode, { "../decky": { DFL: ui }, "../controlBackend": { call: noop },
      "../steamOwnedLaunchDispatcher": owned,
      "../controlledQamFocus": { steamAppId: value => value }, "./steamPerformanceMonitor": {},
      "./steamPerformanceOverlayIdentity": load(source("quickSettings/steamPerformanceOverlayIdentity"), {}),
      "./livePerformanceController": { createLivePerformanceController: fakeController } }).installLivePerformance(),
    mountFocus: changed => load(focusCode, {}).observeSteamOverlayFocus(() => window.FocusedAppWindowStore, changed),
    focus(appId, pid) { return nativeFocusListener(appId, pid); },
    get focused() { return focused; }, get focusRegistrations() { return focusRegistrations; },
    get focusOriginalRetained() { return nativeFocusListener === steamFocusListener; },
    change(mode) { desired = mode; return nativeListener(); },
    failObserver(value) { readFailure = value; },
    get main() { return currentMain; }, get updates() { return updates; }, get registrations() { return registrations; },
    get originalRetained() { return nativeListener === steamListener; },
    get methodRestored() { return store.UpdateDesiredWindows === nativeUpdate; }, timers,
  };
}

test("QAM and HUD preserve Steam's single desired-window callback through both mode transitions and unload orders", () => {
  for (const order of [["mountQam", "mountHud"], ["mountHud", "mountQam"]]) {
    for (const reverse of [false, true]) {
      const f = fixture(), cleanup = order.map(name => f[name]());
      try {
        assert.equal(f.originalRetained, true); assert.equal(f.registrations, 1);
        assert.equal(f.change(7), "Valve result"); assert.equal(f.main, undefined);
        assert.equal(f.change(4), "Valve result"); const first = f.main; assert.ok(first);
        assert.equal(f.change(7), "Valve result"); assert.equal(f.main, undefined);
        assert.equal(f.change(4), "Valve result"); assert.ok(f.main); assert.notEqual(f.main, first);
        const remove = reverse ? cleanup.pop() : cleanup.shift(); remove();
        f.change(7); f.change(4); assert.ok(f.main);
        assert.equal(f.originalRetained, true); assert.equal(f.registrations, 1); assert.equal(f.updates, 6);
      } finally { cleanup.forEach(stop => stop()); }
      assert.equal(f.originalRetained, true); assert.equal(f.methodRestored, true); assert.equal(f.timers.size, 0);
      f.change(7); f.change(4); assert.ok(f.main); assert.equal(f.updates, 8);
    }
  }
});

test("an unavailable observer cannot interrupt Valve window creation or replace its callback", () => {
  const f = fixture(), cleanup = [f.mountQam(), f.mountHud()];
  try {
    f.failObserver(true);
    assert.equal(f.change(4), "Valve result"); assert.ok(f.main); assert.equal(f.updates, 1);
    assert.equal(f.change(7), "Valve result"); assert.equal(f.main, undefined); assert.equal(f.updates, 2);
    assert.equal(f.originalRetained, true); assert.equal(f.registrations, 1);
  } finally { f.failObserver(false); cleanup.forEach(stop => stop()); }
  assert.equal(f.methodRestored, true); assert.equal(f.timers.size, 0);
});

test("QAM and HUD preserve the captured native focus callback while the return poll observes committed app and PID", () => {
  for (const order of [["mountQam", "mountHud"], ["mountHud", "mountQam"]]) {
    const f = fixture(), changes = [], cleanup = order.map(name => f[name]());
    const focus = f.mountFocus((...args) => changes.push(args));
    try {
      focus.refresh(); f.change(4);
      assert.equal(f.focus(0xa1234567, 1234), "Valve focus result"); focus.refresh(); focus.refresh();
      assert.equal(f.focus(0, 0), "Valve focus result"); focus.refresh();
      assert.deepEqual(changes, [[0xa1234567, 1234], [0, 0]]);
      assert.equal(f.focused.m_unFocusedOverlayAppID, 0); assert.equal(f.focused.m_unFocusedOverlayPID, 0);
      assert.equal(f.focusRegistrations, 1); assert.equal(f.focusOriginalRetained, true);
      cleanup.shift()(); f.focus(42, 2); focus.refresh(); assert.deepEqual(changes.at(-1), [42, 2]);
    } finally { focus.dispose(); cleanup.forEach(stop => stop()); }
    f.focus(0, 0); focus.refresh(); assert.equal(changes.length, 3);
    assert.equal(f.focusRegistrations, 1); assert.equal(f.focusOriginalRetained, true); assert.equal(f.timers.size, 0);
  }
});

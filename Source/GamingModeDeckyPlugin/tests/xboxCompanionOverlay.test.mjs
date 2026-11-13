import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

function load(name, require) {
  const exports = {};
  const source = readFileSync(new URL(`../src/${name}.ts`, import.meta.url), "utf8");
  vm.runInNewContext(ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText, { exports, require });
  return exports;
}

const control = load("controlledQamFocus");
const overlay = load("dashboardOverlay", () => control);
const { createXboxCompanionOverlay } = load("xboxCompanionOverlay", () => overlay);
const settle = () => new Promise(resolve => setImmediate(resolve));
const app = 2485417537;
const gameID = "10674787038353424384";
const browser = { appID: 2389569, gameID, unPID: 31704, nBrowserID: 0 };
const verified = { ok: true, gameId: gameID, companionPid: 27156, keyboardAt: 9000,
  overlayPid: 31704, foregroundScope: 2, overlayOpen: true };
const acceptedCold = { ...verified, overlayPid: 31704, foregroundScope: 1, overlayOpen: false };
const recoveryProof = { ...acceptedCold, foregroundScope: 2, keyboardAt: 10000, overlayOpen: true };

function fixture(options = {}) {
  const calls = [];
  let runningApp = app;
  let now = 10000;
  let browsers = "browsers" in options ? options.browsers : [browser];
  const observers = new Map();
  const watches = { started: 0, stopped: 0, active: 0 };
  const target = (pid) => {
    let side = options.sideMenu ?? 0;
    const menu = { GetOpenSideMenu: () => side,
      OpenSideMenu(next) { side = next; for (const notify of observers.get(menu) ?? []) notify(); },
      OpenMainMenu() { calls.push(["menu", pid]); if (!options.suppressMenu) menu.OpenSideMenu(1); },
      OpenQuickAccessMenu() { calls.push(["quick", pid]); if (!options.suppressMenu) menu.OpenSideMenu(2); },
      CloseSideMenus() { calls.push(["close-menu", pid]); side = 0; for (const notify of observers.get(menu) ?? []) notify(); } };
    return {
    IsGamepadUIOverlayWindow: () => true,
    params: { browserInfo: { m_gameID: gameID, m_unPID: pid, m_nBrowserID: 0 } },
    BrowserWindow: { document: { body: {} } },
    MenuStore: menu,
    };
  };
  let windows = [target(27156), target(31704)];
  const route = createXboxCompanionOverlay({
    runningApp: () => runningApp,
    browserInfo: async () => browsers,
    requestColdOpen: async appId => {
      calls.push(["cold-open", appId]);
      return options.requestColdOpen ? options.requestColdOpen() : { ok: true };
    },
    verify: async (...args) => {
      calls.push(["verify", ...args]);
      return options.verify ? options.verify(...args) : verified;
    },
    windows: () => windows,
    setOverlayState: async (...args) => { calls.push(["state", ...args]); await options.setOverlayState?.(...args); },
    useSteamMenuLayout: options.useSteamMenuLayout ? () => calls.push(["menu-layout"]) : undefined,
    observeMenu: options.noMenuWatcher ? undefined : (menu, notify) => {
      watches.started++; watches.active++;
      const listeners = observers.get(menu) ?? new Set();
      listeners.add(notify); observers.set(menu, listeners);
      return () => { assert.ok(listeners.delete(notify)); watches.stopped++; watches.active--; };
    },
    delay: options.delay ?? (async () => {}),
    now: () => now,
    onColdRecovery: () => calls.push(["cold-recovery"]),
    trace: options.trace,
  });
  return {
    route, calls, watches, changeApp: next => { runningApp = next; }, advance: ms => { now += ms; },
    setBrowsers: next => { browsers = next; },
    window: () => windows[1],
    replaceWindow: () => { windows = [windows[0], target(31704)]; },
  };
}

test("Guide opens Valve state and the exact overlay's own menu once", async () => {
  const { route, calls } = fixture();
  route.guide({ eKey: 0 });
  route.guide({ eKey: 0 });
  await settle();
  assert.deepEqual(calls, [["verify", app, 31704, false, false],
    ["verify", app, 31704, false, true], ["menu", 31704], ["verify", app, 31704, false, true], ["state", gameID, 2]]);
});

test("Steam never receives an active composition for an empty menu browser", async () => {
  let f;
  f = fixture({ setOverlayState: async () => {
    // Steam's composition observer immediately hides an active empty browser.
    if (!f.calls.some(([name]) => name === "menu")) {
      f.calls.push(["hidden-empty-browser"]);
      f.route.activated(31704, app, false);
    }
  } });
  f.route.guide({ eKey: 0, nControllerIndex: 0 });
  await settle();
  assert.equal(f.calls.some(([name]) => name === "hidden-empty-browser"), false);
  assert.equal(f.calls.filter(([name]) => name === "menu").length, 1);
  assert.equal(f.calls.filter(([name]) => name === "state").length, 1);
});

test("a completed fallback releases ownership so the next real Guide can open its menu", async () => {
  let f;
  f = fixture({ browsers: [], requestColdOpen: () => acceptedCold,
    verify: (_app, pid) => pid === 0 ? acceptedCold : recoveryProof,
    delay: async () => f.setBrowsers([browser]) });
  await f.route.recover(app);
  assert.equal(f.calls.filter(([name]) => name === "menu").length, 1);
  f.route.activated(31704, app, false);
  f.advance(300);
  f.route.guide({ eKey: 0, nControllerIndex: 0 });
  await settle();
  assert.equal(f.calls.filter(([name]) => name === "menu").length, 2);
});

test("each Guide or Quick Access after Back reactivates the exact retained native companion", async () => {
  let f, nativeActive = false, opens = 0;
  f = fixture({
    requestColdOpen: () => { nativeActive = true; opens++; return { ...acceptedCold, keyboardAt: 9000 + opens * 100 }; },
    verify: (_app, _pid, keyboard, menu) => ({
      ...acceptedCold, keyboardAt: keyboard ? 9001 + opens * 100 : 0,
      foregroundScope: nativeActive && menu ? 2 : 1, overlayOpen: nativeActive && menu,
    }),
  });
  for (const key of [0, 0, 1, 0, 1]) {
    f.advance(300);
    f.route.guide({ eKey: key, nControllerIndex: 0 });
    await settle();
    assert.equal(opens, f.calls.filter(([name]) => name === "state").length);
    assert.equal(f.window().MenuStore.GetOpenSideMenu(), key === 1 ? 2 : 1);
    const cold = f.calls.findLastIndex(([name]) => name === "cold-open");
    const mounted = f.calls.findLastIndex(([name]) => name === (key === 1 ? "quick" : "menu"));
    assert.ok(cold >= 0 && mounted > cold, "fresh native request must precede every menu mount");
    f.window().MenuStore.CloseSideMenus();
    nativeActive = false;
    f.route.activated(31704, app, false, true);
  }
  assert.equal(opens, 5);
  assert.equal(f.watches.active, 0);
});

test("a rejected or replaced native reopen never mounts its stale registered browser", async () => {
  for (const delivery of [{ ok: false }, { ...acceptedCold, companionPid: 88 }, { ...acceptedCold, overlayPid: 99 }]) {
    const f = fixture({ verify: () => acceptedCold, requestColdOpen: () => delivery });
    f.route.guide({ eKey: 0, nControllerIndex: 0 });
    await settle();
    assert.equal(f.calls.filter(([name]) => name === "cold-open").length, 1);
    assert.equal(f.calls.some(([name]) => name === "menu" || name === "state"), false);
  }
});

test("changing games while a retained-browser native reopen waits cannot open either menu", async () => {
  let finish;
  const f = fixture({ verify: () => acceptedCold, requestColdOpen: () => new Promise(resolve => { finish = resolve; }) });
  f.route.guide({ eKey: 1, nControllerIndex: 0 });
  await settle();
  f.changeApp(42);
  finish(acceptedCold);
  await settle();
  assert.equal(f.calls.some(([name]) => name === "quick" || name === "menu" || name === "state"), false);
});

test("replacing a retained browser or its menu during native reopen invalidates that request", async () => {
  for (const replace of [f => f.replaceWindow(), f => { f.window().MenuStore = { GetOpenSideMenu: () => 0 }; }]) {
    let finish;
    // First prove the game is foreground with the old browser still registered.
    const waiting = fixture({ verify: () => acceptedCold,
      requestColdOpen: () => new Promise(resolve => { finish = resolve; }) });
    waiting.route.guide({ eKey: 0, nControllerIndex: 0 });
    await settle();
    replace(waiting);
    finish(acceptedCold);
    await settle();
    assert.equal(waiting.calls.some(([name]) => name === "menu" || name === "state"), false);
  }
});

test("a retained DOM menu cannot turn the new replay's initial inactive callback into a user close", async () => {
  let f, replay = false, inactive = false;
  f = fixture({ sideMenu: 1, requestColdOpen: () => { replay = true; return acceptedCold; },
    verify: (_app, _pid, keyboard, menu) => {
      if (!replay) return acceptedCold;
      if (!inactive) { inactive = true; f.route.activated(31704, app, false, false); }
      return { ...recoveryProof, foregroundScope: menu ? 2 : 1, overlayOpen: menu };
    } });
  f.route.guide({ eKey: 0, nControllerIndex: 0 });
  await settle();
  assert.equal(inactive, true);
  assert.equal(f.calls.filter(([name]) => name === "cold-open").length, 1);
  assert.equal(f.calls.filter(([name]) => name === "state").length, 1);
  assert.equal(f.window().MenuStore.GetOpenSideMenu(), 1);
});

test("a retained browser remains pinned while its new native replay waits for composition", async () => {
  let f, nativeRequested = false;
  f = fixture({ requestColdOpen: () => { nativeRequested = true; return acceptedCold; },
    verify: () => nativeRequested ? recoveryProof : acceptedCold,
    delay: async () => { if (nativeRequested) f.replaceWindow(); } });
  f.route.guide({ eKey: 0, nControllerIndex: 0 });
  await settle();
  assert.equal(f.calls.filter(([name]) => name === "cold-open").length, 1);
  assert.equal(f.calls.some(([name]) => name === "menu" || name === "state"), false);
});

test("native Shift+Tab activation uses its verified companion and deduplicates Valve callbacks", async () => {
  const { route, calls } = fixture();
  route.activated(27156, 2389569, true);
  await settle();
  route.activated(31704, 2389569, true);
  await settle();
  assert.deepEqual(calls, [["verify", app, 31704, true, false],
    ["verify", app, 31704, true, false], ["menu", 31704], ["verify", app, 31704, true, true], ["state", gameID, 2], ["verify", app, 31704, true, false]]);
});

test("foreign, ambiguous and malformed overlay browsers never reach a native request", async () => {
  for (const browsers of [[], [browser, { ...browser, nBrowserID: 9 }],
    [{ ...browser, gameID: "10674787038353424385", appID: app }]]) {
    const { route, calls } = fixture({ browsers });
    route.guide({ eKey: 0 });
    await settle();
    assert.deepEqual(calls, []);
  }
});

test("native rejection and foreign keyboard PID leave the Valve overlay alone", async () => {
  const rejected = fixture({ verify: () => ({ ok: false }) });
  rejected.route.guide({ eKey: 0 });
  await settle();
  assert.deepEqual(rejected.calls, [["verify", app, 31704, false, false]]);
  const foreign = fixture();
  foreign.route.activated(99, app, true);
  await settle();
  assert.deepEqual(foreign.calls, [["verify", app, 31704, true, false]]);
});

test("unrelated keys, button releases, Steam app changes and plugin teardown do not open the companion", async () => {
  for (const invalidate of [f => f.changeApp(42), f => f.route.invalidate(), f => f.route.dispose()]) {
    let finish;
    const response = new Promise(resolve => { finish = resolve; });
    const f = fixture({ verify: () => response });
    f.route.guide({ eKey: 2 });
    f.route.guide({ eKey: 0, bPressed: false });
    f.route.activated(31704, app, false);
    await settle();
    assert.deepEqual(f.calls, []);
    f.advance(250);
    f.route.guide({ eKey: 0 });
    await settle();
    invalidate(f);
    finish(verified);
    await settle();
    assert.deepEqual(f.calls, [["verify", app, 31704, false, false]]);
  }
});

test("Quick Access uses the exact Valve browser's QAM rather than its main menu", async () => {
  const f = fixture();
  f.route.guide({ eKey: 1, nControllerIndex: 0 });
  await settle();
  assert.equal(f.calls.filter(([name]) => name === "quick").length, 1);
  assert.equal(f.calls.some(([name]) => name === "menu"), false);
});

test("a cold Quick Access request retains its QAM destination through native initialization", async () => {
  let f;
  f = fixture({ browsers: [], requestColdOpen: () => acceptedCold, verify: () => recoveryProof,
    delay: async () => f.setBrowsers([browser]) });
  f.route.guide({ eKey: 1, nControllerIndex: 0 });
  await settle();
  assert.equal(f.calls.filter(([name]) => name === "quick").length, 1);
  assert.equal(f.calls.some(([name]) => name === "menu"), false);
});

test("Guide+A keeps a single in-flight cold replay and changes only its final menu", async () => {
  let f;
  f = fixture({ browsers: [], requestColdOpen: () => acceptedCold, verify: () => recoveryProof,
    delay: async () => { f.setBrowsers([browser]); } });
  f.route.guide({ eKey: 0, nControllerIndex: 0 });
  f.route.guide({ eKey: 1, nControllerIndex: 0 });
  await settle();
  assert.equal(f.calls.filter(([action]) => action === "cold-open").length, 1);
  assert.equal(f.calls.filter(([action]) => action === "quick").length, 1);
  assert.equal(f.calls.some(([action]) => action === "menu"), false);
});

test("Alt+Tab, replacement companion or a second key edge between Steam activation and menu cancels", async () => {
  const cases = [
    { ok: false },
    { ...verified, companionPid: 29 },
    { ...verified, keyboardAt: 9001 },
  ];
  for (const reply of cases) {
    let checks = 0;
    const { route, calls } = fixture({ verify: () => ++checks === 1 ? verified : reply });
    route.activated(27156, app, true);
    await settle();
    assert.deepEqual(calls, [["verify", app, 31704, true, false],
      ["verify", app, 31704, true, false]]);
  }
});

test("Guide does not reopen an exact overlay Valve just closed and the next separate press works", async () => {
  const { route, calls, advance } = fixture();
  route.activated(31704, app, false);
  route.guide({ eKey: 0 });
  await settle();
  assert.deepEqual(calls, []);
  advance(250);
  route.guide({ eKey: 0 });
  await settle();
  assert.deepEqual(calls, [["verify", app, 31704, false, false],
    ["verify", app, 31704, false, true], ["menu", 31704], ["verify", app, 31704, false, true], ["state", gameID, 2]]);
});

test("exact companion close during native Guide verification cancels reopening", async () => {
  let finish;
  const response = new Promise(resolve => { finish = resolve; });
  const { route, calls } = fixture({ verify: () => response });
  route.guide({ eKey: 0 });
  await settle();
  route.activated(27156, 2389569, false);
  finish(verified);
  await settle();
  assert.deepEqual(calls, [["verify", app, 31704, false, false]]);
});

test("exact close after activation cancels the delayed Guide menu", async () => {
  let finishDelay;
  const { route, calls } = fixture({ delay: () => new Promise(resolve => { finishDelay = resolve; }) });
  route.guide({ eKey: 0 });
  await settle();
  route.activated(31704, app, false);
  finishDelay();
  await settle();
  assert.deepEqual(calls, [["verify", app, 31704, false, false]]);
});

test("unrelated closes do not suppress Guide and a startup flicker does not suppress Shift+Tab", async () => {
  const foreign = fixture();
  foreign.route.activated(77, app, false);
  foreign.route.guide({ eKey: 0 });
  await settle();
  assert.ok(foreign.calls.some(([name, pid]) => name === "menu" && pid === 31704));
  const keyboard = fixture();
  keyboard.route.activated(31704, app, false);
  keyboard.route.activated(27156, app, true);
  await settle();
  assert.ok(keyboard.calls.some(([name, pid]) => name === "menu" && pid === 31704));
});

test("real cold controller Guide requests native replay once and follows the registered activation", async () => {
  let finish;
  const delivery = new Promise(resolve => { finish = resolve; });
  const { route, calls, setBrowsers } = fixture({ browsers: [], requestColdOpen: () => delivery });
  route.guide({ eKey: 0, nControllerIndex: 0 });
  route.guide({ eKey: 0, nControllerIndex: 0 });
  await settle();
  assert.deepEqual(calls, [["cold-open", app]]);
  setBrowsers([browser]);
  route.activated(27156, 2389569, true);
  await settle();
  assert.deepEqual(calls, [["cold-open", app], ["verify", app, 31704, true, false],
    ["verify", app, 31704, true, false], ["menu", 31704], ["verify", app, 31704, true, true], ["state", gameID, 2]]);
  finish({ ok: true });
  await settle();
});

test("cold Guide refuses untrusted browser data, invalid controller edges, and recent closes", async () => {
  for (const browsers of [null, [browser, { ...browser, nBrowserID: 9 }], [{ ...browser, appID: null, nBrowserID: -1 }]]) {
    const { route, calls } = fixture({ browsers });
    route.guide({ eKey: 0, nControllerIndex: 4 });
    await settle();
    assert.deepEqual(calls, []);
  }
  const invalid = fixture({ browsers: [] });
  invalid.route.activated(27156, app, true);
  invalid.route.guide({ eKey: 1, nControllerIndex: 4 });
  invalid.route.guide({ eKey: 0, nControllerIndex: 4, bPressed: false });
  invalid.route.guide({ eKey: 0 });
  await settle();
  assert.deepEqual(invalid.calls, []);
  invalid.advance(250);
  invalid.route.activated(31704, app, false);
  invalid.route.guide({ eKey: 0, nControllerIndex: 4 });
  await settle();
  assert.deepEqual(invalid.calls, []);
});

test("refused native cold Guide does not try to open a browser overlay", async () => {
  const { route, calls } = fixture({ browsers: [], requestColdOpen: () => ({ ok: false }) });
  route.guide({ eKey: 0, nControllerIndex: 4 });
  await settle();
  assert.deepEqual(calls, [["cold-open", app]]);
});

test("accepted cold Guide recovers the exact browser only once after an absent Valve activation", async () => {
  const waits = [];
  let f;
  f = fixture({ browsers: [], requestColdOpen: () => acceptedCold, verify: () => recoveryProof, delay: async ms => {
    waits.push(ms);
    f.advance(ms);
    if (waits.length === 2) f.setBrowsers([browser]);
  } });
  f.route.guide({ eKey: 0, nControllerIndex: 0 });
  await settle();
  assert.deepEqual(waits, [220, 80, 40, 150]);
  assert.deepEqual(f.calls, [["cold-open", app], ["verify", app, 31704, true, false],
    ["verify", app, 31704, true, false], ["menu", 31704], ["verify", app, 31704, true, true], ["state", gameID, 2], ["cold-recovery"]]);
  f.route.activated(27156, app, true);
  await settle();
  assert.equal(f.calls.filter(([action]) => action === "state").length, 1);
});

test("cold Guide recovery stays bounded when Valve does not register a browser", async () => {
  const waits = [];
  const f = fixture({ browsers: [], requestColdOpen: () => acceptedCold, delay: async ms => { waits.push(ms); } });
  f.route.guide({ eKey: 0, nControllerIndex: 0 });
  await settle();
  assert.deepEqual(f.calls, [["cold-open", app]]);
  assert.ok(waits.reduce((total, ms) => total + ms, 0) <= 3000);
});

test("an already-open cold overlay selects the menu layout without reopening Valve's menu", async () => {
  let f;
  f = fixture({ browsers: [], sideMenu: 1, requestColdOpen: () => acceptedCold,
    verify: () => ({ ...recoveryProof, overlayOpen: true }), useSteamMenuLayout: true,
    delay: async () => { f.setBrowsers([browser]); } });
  f.route.guide({ eKey: 0, nControllerIndex: 0 });
  await settle();
  assert.deepEqual(f.calls, [["cold-open", app], ["verify", app, 31704, true, false], ["menu-layout"]]);
});

test("cold Valve browser arriving after one second still opens its verified menu", async () => {
  let elapsed = 0, f;
  f = fixture({ browsers: [], requestColdOpen: () => acceptedCold, verify: () => recoveryProof,
    delay: async ms => { elapsed += ms; f.advance(ms); if (elapsed >= 1000) f.setBrowsers([browser]); } });
  f.route.guide({ eKey: 0, nControllerIndex: 0 });
  await settle();
  assert.equal(f.calls.filter(([name]) => name === "menu").length, 1);
  assert.ok(elapsed >= 1000 && elapsed < 1500);
});

test("the observed initial inactive callback during a 361 ms cold proof does not cancel its exact empty browser", async () => {
  let finish, elapsed = 0, verifying = 0, f;
  const trace = [], deferred = new Promise(resolve => { finish = resolve; });
  f = fixture({ browsers: [], requestColdOpen: () => ({ ...acceptedCold, keyboardAt: 0 }), trace: stage => trace.push(stage),
    verify: (_app, pid, _keyboard, menu) => pid === 0 ? acceptedCold
      : ++verifying === 1 ? deferred : { ...recoveryProof, overlayOpen: menu },
    delay: async ms => { elapsed += ms; f.advance(ms); if (elapsed >= 1000) f.setBrowsers([browser]); } });
  const pending = f.route.recover(app); await settle();
  assert.ok(trace.includes("native-browser-ready"));
  f.advance(95); f.route.activated(browser.unPID, app, false, false);
  f.advance(266); finish({ ...recoveryProof, overlayOpen: false }); await pending;
  assert.ok(trace.includes("cold-initial-inactive"));
  assert.equal(f.calls.filter(([name]) => name === "cold-open").length, 1);
  assert.equal(f.calls.filter(([name]) => name === "menu").length, 1);
  assert.equal(f.calls.filter(([name]) => name === "state").length, 1);
  assert.equal(f.calls.filter(([name]) => name === "cold-recovery").length, 1);
});

test("explicit or ambiguous close, another companion and window replacement still cancel an unmounted cold menu", async () => {
  for (const close of [
    f => f.route.activated(browser.unPID, app, false, true),
    f => f.route.activated(browser.unPID, app, false),
    f => f.route.activated(acceptedCold.companionPid, app, false, false),
    f => { f.replaceWindow(); f.route.activated(browser.unPID, app, false, false); },
  ]) {
    let finish, f;
    const deferred = new Promise(resolve => { finish = resolve; });
    f = fixture({ browsers: [], requestColdOpen: () => acceptedCold,
      verify: (_app, pid) => pid === 0 ? acceptedCold : deferred,
      delay: async ms => { f.advance(ms); f.setBrowsers([browser]); } });
    const pending = f.route.recover(app); await settle(); close(f); finish(recoveryProof); await pending;
    assert.equal(f.calls.some(([name]) => name === "menu" || name === "state"), false);
  }
});

test("a second real Guide cancels its pending cold replay while Guide-to-Quick remains a side change", async () => {
  let finish, f;
  const deferred = new Promise(resolve => { finish = resolve; });
  f = fixture({ browsers: [], requestColdOpen: () => acceptedCold, verify: () => deferred,
    delay: async ms => { f.advance(ms); f.setBrowsers([browser]); } });
  f.route.guide({ eKey: 0, nControllerIndex: 0 }); await settle();
  f.advance(300); f.route.guide({ eKey: 0, nControllerIndex: 0 }); finish(recoveryProof); await settle();
  assert.equal(f.calls.filter(([name]) => name === "cold-open").length, 1);
  assert.equal(f.calls.some(([name]) => name === "menu" || name === "state"), false);
});

test("an inactive callback after the cold menu was mounted remains an actual close", async () => {
  let f;
  const trace = [];
  f = fixture({ browsers: [], requestColdOpen: () => acceptedCold, trace: stage => trace.push(stage),
    verify: (_app, pid) => pid === 0 ? acceptedCold : recoveryProof,
    delay: async ms => {
      f.advance(ms); f.setBrowsers([browser]);
      if (ms === 150) { f.window().MenuStore.CloseSideMenus(); f.route.activated(browser.unPID, app, false, false); }
    } });
  await f.route.recover(app);
  assert.equal(f.calls.filter(([name]) => name === "menu").length, 1);
  assert.equal(f.calls.some(([name]) => name === "state" || name === "cold-recovery"), false);
  assert.equal(trace.includes("cold-initial-inactive"), false);
  assert.equal(f.window().MenuStore.GetOpenSideMenu(), 0);
});

test("native open flag with an empty Steam menu still requests the actual menu", async () => {
  let f;
  f = fixture({ browsers: [], requestColdOpen: () => acceptedCold,
    verify: () => ({ ...recoveryProof, overlayOpen: true }),
    delay: async () => { f.setBrowsers([browser]); } });
  f.route.guide({ eKey: 0, nControllerIndex: 0 });
  await settle();
  assert.equal(f.calls.filter(([name]) => name === "state").length, 1);
  assert.equal(f.calls.filter(([name]) => name === "menu").length, 1);
});

test("a Guide press during Valve's own activation adopts only the exact open companion", async () => {
  for (const pid of [31704, 31705]) {
    const f = fixture({ useSteamMenuLayout: true, sideMenu: 1,
      verify: (_app, _pid, _keyboard, menu) => menu
        ? { ...recoveryProof, overlayPid: pid, overlayOpen: true }
        : { ok: false } });
    f.route.guide({ eKey: 0, nControllerIndex: 0 });
    await settle();
    assert.equal(f.calls.some(([action]) => action === "menu-layout"), pid === 31704);
    assert.equal(f.calls.some(([action]) => action === "state"), false);
  }
});

test("a natively open companion with the wrong side still opens the requested Quick Access menu", async () => {
  const f = fixture({ useSteamMenuLayout: true, sideMenu: 1,
    verify: (_app, _pid, _keyboard, menu) => menu ? { ...recoveryProof, overlayOpen: true } : { ok: false } });
  f.route.guide({ eKey: 1, nControllerIndex: 0 });
  await settle();
  assert.equal(f.calls.filter(([action]) => action === "state").length, 1);
  assert.equal(f.calls.filter(([action]) => action === "quick").length, 1);
  assert.equal(f.calls.some(([action]) => action === "menu"), false);
});

test("recovery refuses a wrong browser or unverified, background and stale companion states", async () => {
  for (const [infos, proof, expectedVerify] of [
    [[{ ...browser, unPID: 10 }], recoveryProof, false],
    [[browser, { ...browser, nBrowserID: 9 }], recoveryProof, false],
    [[browser], { ...recoveryProof, ok: false }, true],
    [[browser], { ...recoveryProof, companionPid: 29 }, true],
    [[browser], { ...recoveryProof, overlayPid: 29 }, true],
    [[browser], { ...recoveryProof, foregroundScope: 0 }, true],
    [[browser], { ...recoveryProof, keyboardAt: acceptedCold.keyboardAt }, true],
  ]) {
    let f;
    f = fixture({ browsers: [], requestColdOpen: () => acceptedCold, verify: () => proof, useSteamMenuLayout: true,
      delay: async () => { f.setBrowsers(infos); } });
    f.route.guide({ eKey: 0, nControllerIndex: 0 });
    await settle();
    assert.deepEqual(f.calls, expectedVerify
      ? [["cold-open", app], ["verify", app, 31704, true, false]] : [["cold-open", app]]);
  }
});

test("a close, selection change, or teardown while verifying the cold browser cancels recovery", async () => {
  for (const cancel of [f => f.route.activated(31704, app, false), f => f.changeApp(42),
    f => f.route.invalidate(), f => f.route.dispose()]) {
    let finish, f;
    const proof = new Promise(resolve => { finish = resolve; });
    f = fixture({ browsers: [], requestColdOpen: () => acceptedCold, verify: () => proof,
      delay: async () => { f.setBrowsers([browser]); } });
    f.route.guide({ eKey: 0, nControllerIndex: 0 });
    await settle();
    cancel(f);
    finish(recoveryProof);
    await settle();
    assert.deepEqual(f.calls, [["cold-open", app], ["verify", app, 31704, true, false]]);
  }
});

test("delayed recovery selects its authenticated companion when the same game also has a native browser", async () => {
  let f;
  f = fixture({ browsers: [browser, { ...browser, unPID: 81 }], requestColdOpen: () => acceptedCold,
    verify: (_app, pid) => pid === 0 ? acceptedCold : recoveryProof,
    delay: async () => {}, useSteamMenuLayout: true });
  await f.route.recover(app);
  assert.equal(f.calls.filter(([name]) => name === "cold-open").length, 1);
  assert.equal(f.calls.some(([name, id]) => name === "verify" && id === app), true);
  assert.equal(f.calls.some(([name]) => name === "state"), true);
  assert.equal(f.calls.some(([name, pid]) => name === "menu" && pid === 31704), true);
  assert.equal(f.calls.some(([name, pid]) => name === "menu" && pid === 81), false);
});

test("delayed recovery refuses open, background and mismatched sessions", async () => {
  for (const state of [{ ...acceptedCold, overlayOpen: true }, { ...acceptedCold, foregroundScope: 0 },
    { ...acceptedCold, gameId: "11" }, { ok: false }]) {
    const f = fixture({ verify: () => state, requestColdOpen: () => acceptedCold });
    await f.route.recover(app);
    assert.equal(f.calls.some(([name]) => name === "cold-open" || name === "state"), false);
  }
});

function coldPreparation(options = {}) {
  let f;
  f = fixture({ browsers: [], requestColdOpen: () => acceptedCold,
    verify: (_app, pid, keyboard, menu) => {
      if (pid === 0) return acceptedCold;
      assert.equal(keyboard, true);
      if (!menu) return { ...recoveryProof, foregroundScope: 1, overlayOpen: false };
      return options.post ? options.post(f) : recoveryProof;
    },
    delay: async () => f.setBrowsers([browser]), ...options });
  return f;
}

test("cold Scope1 prepares only the exact menu before requiring native Scope2 and open state", async () => {
  const f = coldPreparation();
  await f.route.recover(app);
  const menu = f.calls.findIndex(([name]) => name === "menu");
  const state = f.calls.findIndex(([name]) => name === "state");
  assert.ok(menu > 0 && state > menu);
  assert.ok(f.calls.slice(0, menu).filter(([name, , pid]) => name === "verify" && pid === 31704)
    .every(([, , , keyboard, nativeMenu]) => keyboard && nativeMenu === false));
  assert.deepEqual(f.calls[state - 1], ["verify", app, 31704, true, true]);
  assert.equal(f.window().MenuStore.GetOpenSideMenu(), 1);
  assert.deepEqual(f.watches, { started: 1, stopped: 1, active: 0 });
});

test("a warm controller overlay keeps the native menu=true guard", async () => {
  const f = fixture({ useSteamMenuLayout: true,
    verify: (_app, _pid, keyboard, menu) => {
      assert.equal(keyboard, false);
      return menu ? recoveryProof : { ok: false };
    } });
  f.route.guide({ eKey: 0, nControllerIndex: 0 });
  await settle();
  assert.equal(f.calls.filter(([name]) => name === "state").length, 1);
  assert.equal(f.calls.filter(([name]) => name === "cold-open").length, 0);
  assert.ok(f.calls.filter(([name]) => name === "verify").slice(1).every(([, , , , menu]) => menu === true));
  assert.equal(f.watches.active, 0);
});

test("unconfirmed native composition rolls back only its newly mounted exact menu", async () => {
  for (const rejected of [{ ok: false }, { ...recoveryProof, foregroundScope: 1 },
    { ...recoveryProof, overlayOpen: false }, { ...recoveryProof, companionPid: 29 },
    { ...recoveryProof, overlayPid: 29 }, { ...recoveryProof, gameId: "99" },
    { ...recoveryProof, keyboardAt: 10001 }]) {
    const f = coldPreparation({ post: () => rejected });
    await f.route.recover(app);
    assert.equal(f.calls.filter(([name]) => name === "menu").length, 1);
    assert.equal(f.calls.filter(([name]) => name === "close-menu").length, 1);
    assert.equal(f.window().MenuStore.GetOpenSideMenu(), 0);
    assert.equal(f.calls.some(([name]) => name === "state" || name === "cold-recovery"), false);
    assert.deepEqual(f.watches, { started: 1, stopped: 1, active: 0 });
  }
});

test("a menu which existed before preparation is never closed by rollback", async () => {
  const f = coldPreparation({ sideMenu: 1, post: () => ({ ok: false }) });
  await f.route.recover(app);
  assert.equal(f.window().MenuStore.GetOpenSideMenu(), 1);
  assert.equal(f.calls.some(([name]) => name === "close-menu" || name === "state"), false);
  assert.equal(f.watches.active, 0);
});

test("missing menu ownership observation and Steam menu suppression fail before activation", async () => {
  for (const options of [{ noMenuWatcher: true }, { suppressMenu: true }]) {
    const f = coldPreparation(options);
    await f.route.recover(app);
    assert.equal(f.calls.some(([name]) => name === "state" || name === "cold-recovery"), false);
    assert.equal(f.window().MenuStore.GetOpenSideMenu(), 0);
    if (options.noMenuWatcher) assert.equal(f.calls.some(([name]) => name === "menu"), false);
    assert.equal(f.watches.active, 0);
  }
});

test("a replacement WindowStore object with the same IDs invalidates the pre-mount proof", async () => {
  let reads = 0, f;
  f = coldPreparation({ verify: (_app, pid) => {
    if (pid === 0) return acceptedCold;
    if (++reads === 2) f.replaceWindow();
    return { ...recoveryProof, foregroundScope: 1, overlayOpen: false };
  } });
  await f.route.recover(app);
  assert.equal(f.calls.some(([name]) => name === "menu" || name === "quick" || name === "state"), false);
  assert.equal(f.watches.active, 0);
});

test("Guide+A during the post-proof retains one replay and commits the latest side", async () => {
  let finish, posts = 0;
  const waiting = new Promise(resolve => { finish = resolve; });
  const f = coldPreparation({ post: () => ++posts === 1 ? waiting : recoveryProof });
  const work = f.route.recover(app);
  await settle();
  assert.equal(f.window().MenuStore.GetOpenSideMenu(), 1);
  f.route.guide({ eKey: 1, nControllerIndex: 0 });
  finish(recoveryProof);
  await work;
  assert.equal(f.window().MenuStore.GetOpenSideMenu(), 2);
  assert.equal(f.calls.filter(([name]) => name === "cold-open").length, 1);
  assert.equal(f.calls.filter(([name]) => name === "state").length, 1);
  assert.equal(f.calls.filter(([name]) => name === "quick").length, 1);
  assert.equal(f.calls.some(([name]) => name === "close-menu"), false);
  assert.equal(f.watches.active, 0);
});

test("Guide+A during Steam state activation follows the latest intent without another replay", async () => {
  let finish, commits = 0;
  const waiting = new Promise(resolve => { finish = resolve; });
  const f = coldPreparation({ setOverlayState: () => ++commits === 1 ? waiting : undefined });
  const work = f.route.recover(app);
  await settle();
  assert.equal(f.window().MenuStore.GetOpenSideMenu(), 1);
  assert.equal(commits, 1);
  f.route.guide({ eKey: 1, nControllerIndex: 0 });
  finish();
  await work;
  assert.equal(f.window().MenuStore.GetOpenSideMenu(), 2);
  assert.equal(f.calls.filter(([name]) => name === "cold-open").length, 1);
  assert.equal(f.calls.filter(([name]) => name === "state").length, 2);
  assert.equal(f.calls.filter(([name]) => name === "quick").length, 1);
  assert.equal(f.calls.filter(([name]) => name === "cold-recovery").length, 1);
  assert.equal(f.calls.some(([name]) => name === "close-menu"), false);
  assert.equal(f.watches.active, 0);
});

test("new context or user menu actions during state activation preserve the already published menu", async () => {
  for (const change of [f => f.replaceWindow(), f => f.changeApp(42), f => f.route.invalidate(),
    f => f.route.dispose(), f => { f.window().MenuStore.CloseSideMenus(); f.window().MenuStore.OpenMainMenu(); },
    f => f.window().MenuStore.OpenQuickAccessMenu()]) {
    let finish;
    const waiting = new Promise(resolve => { finish = resolve; });
    const f = coldPreparation({ setOverlayState: () => waiting });
    const original = f.window();
    const work = f.route.recover(app);
    await settle();
    assert.equal(original.MenuStore.GetOpenSideMenu(), 1);
    f.route.guide({ eKey: 1, nControllerIndex: 0 });
    change(f);
    const priorCalls = f.calls.length;
    const originalSide = original.MenuStore.GetOpenSideMenu(), currentSide = f.window().MenuStore.GetOpenSideMenu();
    finish();
    await work;
    assert.equal(original.MenuStore.GetOpenSideMenu(), originalSide);
    assert.equal(f.window().MenuStore.GetOpenSideMenu(), currentSide);
    assert.equal(f.calls.slice(priorCalls).some(([name]) => name === "menu" || name === "quick" || name === "close-menu" || name === "state"), false);
    assert.equal(f.calls.some(([name]) => name === "cold-recovery"), false);
    assert.equal(f.watches.active, 0);
  }
});

test("combined intent and WindowStore replacement cannot mutate the detached or new menu", async () => {
  let finish;
  const waiting = new Promise(resolve => { finish = resolve; });
  const f = coldPreparation({ post: () => waiting });
  const work = f.route.recover(app);
  await settle();
  const original = f.window();
  f.replaceWindow();
  f.route.guide({ eKey: 1, nControllerIndex: 0 });
  finish(recoveryProof);
  await work;
  assert.equal(original.MenuStore.GetOpenSideMenu(), 1);
  assert.equal(f.window().MenuStore.GetOpenSideMenu(), 0);
  assert.equal(f.calls.some(([name]) => name === "quick" || name === "close-menu" || name === "state"), false);
  assert.equal(f.watches.active, 0);
});

test("a user closing and reopening the same side owns the newer menu", async () => {
  let finish;
  const waiting = new Promise(resolve => { finish = resolve; });
  const f = coldPreparation({ post: () => waiting });
  const work = f.route.recover(app);
  await settle();
  f.window().MenuStore.CloseSideMenus();
  f.window().MenuStore.OpenMainMenu();
  finish({ ok: false });
  await work;
  assert.equal(f.window().MenuStore.GetOpenSideMenu(), 1);
  assert.equal(f.calls.filter(([name]) => name === "close-menu").length, 1);
  assert.equal(f.calls.some(([name]) => name === "state"), false);
  assert.equal(f.watches.active, 0);
});

test("a direct user menu change during native verification is preserved", async () => {
  let finish;
  const waiting = new Promise(resolve => { finish = resolve; });
  const f = coldPreparation({ post: () => waiting });
  const work = f.route.recover(app);
  await settle();
  f.window().MenuStore.OpenQuickAccessMenu();
  finish(recoveryProof);
  await work;
  assert.equal(f.window().MenuStore.GetOpenSideMenu(), 2);
  assert.equal(f.calls.some(([name]) => name === "close-menu" || name === "state"), false);
  assert.equal(f.watches.active, 0);
});

test("post-mount cancellation unpatches without activating or closing a newer context", async () => {
  for (const cancel of [f => f.changeApp(42), f => f.route.invalidate(), f => f.route.dispose(),
    f => f.route.activated(31704, app, false)]) {
    let finish;
    const waiting = new Promise(resolve => { finish = resolve; });
    const f = coldPreparation({ post: () => waiting });
    const work = f.route.recover(app);
    await settle();
    cancel(f);
    finish(recoveryProof);
    await work;
    assert.equal(f.calls.some(([name]) => name === "close-menu" || name === "state"), false);
    assert.deepEqual(f.watches, { started: 1, stopped: 1, active: 0 });
  }
});

test("rollback and ownership watchers also finish when Steam state activation throws", async () => {
  const f = coldPreparation({ setOverlayState: () => { throw Error("Rejected fixture state"); } });
  await f.route.recover(app);
  assert.equal(f.window().MenuStore.GetOpenSideMenu(), 0);
  assert.equal(f.calls.filter(([name]) => name === "close-menu").length, 1);
  assert.equal(f.calls.some(([name]) => name === "cold-recovery"), false);
  assert.equal(f.watches.active, 0);
});

test("warm activation rejects reused, older and malformed native replay timestamps", async () => {
  for (const stale of [9000, 8999, NaN, Infinity]) {
    let stamp = 9000;
    const f = fixture({ verify: () => ({ ...verified, keyboardAt: stamp }) });
    f.route.activated(27156, app, true);
    await settle();
    stamp = stale;
    f.route.activated(27156, app, true);
    await settle();
    assert.equal(f.calls.filter(([name]) => name === "menu").length, 1);
    assert.equal(f.calls.filter(([name]) => name === "state").length, 1);
    assert.equal(f.watches.active, 0);
  }
});

test("a malformed companion identity cannot be made valid by matching two empty proofs", async () => {
  for (const companionPid of [undefined, 0, -1, 1.5, 0x100000000]) {
    const f = fixture({ verify: () => ({ ...verified, companionPid }) });
    f.route.activated(31704, app, true);
    await settle();
    assert.equal(f.calls.some(([name]) => name === "menu" || name === "state"), false);
    assert.equal(f.watches.active, 0);
  }
});

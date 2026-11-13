import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";
import ts from "typescript";

const source = fs.readFileSync(new URL("../src/steamNavigationReturn.ts", import.meta.url), "utf8");
const compiled = ts.transpile(source, { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ES2022 });
const { createSteamKeyFocusReset, createSteamNavigationRecovery, createSteamNavigationWakeup, createSteamOverlayReturnScope, focusReturnedSteamGameAction, hasVisibleSteamNavigationFocus, isConfirmedSteamIdle, navigateReturnedSteamGame, recoverSteamNavigation, releaseExitedSteamOverlay } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString("base64")}`);

function backstackFixture(routes, navigate) {
  const history = [...routes];
  let focuses = 0, sideMenu = false;
  const main = {
    m_locationPathname: history.at(-1),
    BCanNavigate: () => true,
    BRouteMatch: route => main.m_locationPathname === route,
    FocusApplicationRoot: () => { focuses++; },
    EnsureVROverlayVisible() {},
    m_history: {
      push(route) { history.push(route); main.m_locationPathname = route; },
      replace(route) { history[history.length - 1] = route; main.m_locationPathname = route; },
    },
  };
  main.Navigate = navigate ?? function(route, replace = false, onlyWhenDifferent = false) {
    if (!this.BCanNavigate()) return;
    if (!onlyWhenDifferent || !this.BRouteMatch(route)) this.m_history[replace ? "replace" : "push"](route);
    this.FocusApplicationRoot(); this.EnsureVROverlayVisible();
  };
  return { main, history, focuses: () => focuses, sideMenu: () => sideMenu,
    back() {
      if (history.length > 1) { history.pop(); main.m_locationPathname = history.at(-1); }
      else sideMenu = true;
    },
  };
}

function checkReturnBackstack(navigate) {
  for (const route of ["/library/home", "/playhub/dashboard", "/library/collections"]) {
    const f = backstackFixture([route], navigate);
    assert.equal(navigateReturnedSteamGame(f.main, 42), true);
    assert.deepEqual(f.history, [route, "/library/app/42"]);
    f.back();
    assert.equal(f.main.m_locationPathname, route);
    assert.equal(f.sideMenu(), false, "Back retains a real page ancestor instead of opening the root side menu");
  }
  const placeholder = backstackFixture(["/library/home", "/apprunning"], navigate);
  assert.equal(navigateReturnedSteamGame(placeholder.main, 42), true);
  assert.deepEqual(placeholder.history, ["/library/home", "/library/app/42"]);
  placeholder.back(); assert.equal(placeholder.main.m_locationPathname, "/library/home");
  const same = backstackFixture(["/library/home", "/library/app/42"], navigate);
  assert.equal(navigateReturnedSteamGame(same.main, 42), false);
  assert.deepEqual(same.history, ["/library/home", "/library/app/42"]);
  assert.equal(same.focuses(), 0, "an already-correct game page keeps its existing Play/controller focus");
}

test("return navigation preserves real page ancestors and replaces only the running-game placeholder", () => {
  checkReturnBackstack();
});

test("invalid apps and unavailable navigation cannot change the return backstack", () => {
  for (const app of [0, -1, NaN, Infinity, 1.5, 0x100000000]) {
    const f = backstackFixture(["/library/home"]);
    assert.equal(navigateReturnedSteamGame(f.main, app), false);
    assert.deepEqual(f.history, ["/library/home"]);
  }
  const f = backstackFixture(["/library/home"]);
  f.main.BCanNavigate = () => false;
  assert.equal(navigateReturnedSteamGame(f.main, 42), false);
  assert.deepEqual(f.history, ["/library/home"]);
});

const installedSteamSource = [process.env.PLAYHUB_STEAM_UI_SOURCE,
  "C:/Program Files (x86)/Steam/steamui/sp.js", "C:/Program Files/Steam/steamui/sp.js"]
  .find(path => typeof path === "string" && fs.existsSync(path));
test("installed Steam Navigate applies the same production return policy without losing Back", { skip: !installedSteamSource }, () => {
  const steam = fs.readFileSync(installedSteamSource, "utf8");
  // Execute only this small local Valve method against inert history objects;
  // never evaluate the Steam bundle or interact with a Steam browser/process.
  const methods = [...steam.matchAll(/Navigate\(([^)]{0,100})\)\{(this\.BCanNavigate\(\)&&.{1,600}?this\.EnsureVROverlayVisible\(\)\))\}/g)];
  const unique = [...new Set(methods.map(match => `function Navigate(${match[1]}){${match[2]}}`))];
  assert.equal(unique.length, 1, "one verified local Steam main-window Navigate implementation is required");
  checkReturnBackstack(Function(`return (${unique[0]});`)());
});

function keyFocusFixture() {
  const timers = new Map(), changes = [], abort = new AbortController();
  let timerId = 0, current = true, foreground = true, realFocus = false, proofs = 0;
  const edge = createSteamKeyFocusReset((callback, delay) => {
    assert.equal(delay, 50); timers.set(++timerId, callback); return timerId;
  }, timer => timers.delete(timer));
  const context = { generation: 1, signal: abort.signal, isCurrent: () => current,
    async confirmForeground() { proofs++; return foreground; } };
  const request = { context, canRestore: () => current, hasRealFocus: () => realFocus,
    setKeyFocus(value) { changes.push(value); if (value && changes.includes(false)) realFocus = true; } };
  return { edge, request, timers, changes, abort,
    setCurrent: value => current = value, setForeground: value => foreground = value,
    setFocus: value => realFocus = value, proofCount: () => proofs,
    async settle() { for (let i = 0; i < 8; i++) await Promise.resolve(); },
    fire() { const task = timers.entries().next().value; if (task) { timers.delete(task[0]); task[1](); } },
  };
}

test("the CEF key-focus edge is emitted once per verified return generation", async () => {
  const f = keyFocusFixture();
  const first = f.edge.reset(f.request), duplicate = f.edge.reset(f.request);
  assert.equal(first, duplicate);
  await f.settle(); assert.deepEqual(f.changes, [false]); assert.equal(f.timers.size, 1);
  f.fire(); assert.equal(await first, true);
  assert.deepEqual(f.changes, [false, true]); assert.equal(f.proofCount(), 2); assert.equal(f.timers.size, 0);
  f.setFocus(false); assert.equal(await f.edge.reset(f.request), false);
  assert.deepEqual(f.changes, [false, true], "polling cannot repeatedly blur the main browser");
});

test("Alt-Tab and a replaced document veto the return-key edge", async () => {
  for (const cause of ["foreground", "document"]) {
    const f = keyFocusFixture(), pending = f.edge.reset(f.request); await f.settle();
    if (cause === "foreground") f.setForeground(false); else f.setCurrent(false);
    f.fire(); assert.equal(await pending, false); assert.deepEqual(f.changes, [false]);
    assert.equal(f.timers.size, 0);
  }
});

test("launch cancellation clears the key-edge timer immediately without refocusing", async () => {
  const f = keyFocusFixture(), pending = f.edge.reset(f.request); await f.settle();
  assert.equal(f.timers.size, 1); f.abort.abort();
  assert.equal(f.timers.size, 0); assert.equal(await pending, false); assert.deepEqual(f.changes, [false]);
});

test("already focused CEF and failed native proof never cause a blur", async () => {
  let f = keyFocusFixture(); f.setFocus(true);
  assert.equal(await f.edge.reset(f.request), false); assert.equal(f.proofCount(), 0); assert.deepEqual(f.changes, []);
  f = keyFocusFixture(); f.setForeground(false);
  assert.equal(await f.edge.reset(f.request), false); assert.deepEqual(f.changes, []); assert.equal(f.timers.size, 0);
});

test("a dispatched edge is not evidence that CEF focus actually recovered", async () => {
  const f = keyFocusFixture(); f.request.setKeyFocus = value => f.changes.push(value);
  const pending = f.edge.reset(f.request); await f.settle(); f.fire(); assert.equal(await pending, true);
  assert.equal(f.request.hasRealFocus(), false, "native requests do not fabricate document.hasFocus or WindowFocus");
});

test("new return generations can repair keys after an earlier completed edge", async () => {
  const f = keyFocusFixture();
  let pending = f.edge.reset(f.request); await f.settle(); f.fire(); await pending;
  f.setFocus(false); f.request.context = { ...f.request.context, generation: 2 };
  pending = f.edge.reset(f.request); await f.settle(); f.fire(); assert.equal(await pending, true);
  assert.deepEqual(f.changes, [false, true, false, true]);
});

test("asynchronous navigation recovery is awaited and cannot cancel a newer return", async () => {
  const contexts = [], completes = [];
  const recovery = createSteamNavigationRecovery(async () => ({ ok: true, appId: 42 }),
    (_app, _companion, context) => { contexts.push(context); return new Promise(resolve => completes.push(resolve)); }, () => 0);
  const first = recovery.observe(1, true); for (let i = 0; i < 5; i++) await Promise.resolve();
  assert.equal(contexts[0].signal.aborted, false);
  const second = recovery.observe(2, true); for (let i = 0; i < 5; i++) await Promise.resolve();
  assert.equal(contexts[0].signal.aborted, true); assert.equal(contexts[1].signal.aborted, false);
  completes[0](true); await first;
  assert.equal(contexts[1].signal.aborted, false, "an old resolved Promise must not cancel the new generation");
  recovery.cancel(); assert.equal(contexts[1].signal.aborted, true); completes[1](false); await second;
});

test("fresh recovery context foreground proof keeps exact app and cancellation scope", async () => {
  let proof = { ok: true, appId: 42 }, captured, app = 0;
  const recovery = createSteamNavigationRecovery(async () => proof,
    async (_app, _companion, context) => { captured = context; return false; }, () => app);
  await recovery.observe(1, true); assert.equal(await captured.confirmForeground(), true);
  proof = { ok: false, appId: 42 }; assert.equal(await captured.confirmForeground(), false);
  proof = { ok: true, appId: 43 }; assert.equal(await captured.confirmForeground(), false);
  proof = { ok: true, appId: 42 }; app = 43; assert.equal(await captured.confirmForeground(), false);
  app = 0; recovery.launchStarted(); assert.equal(captured.isCurrent(), false);
  assert.equal(captured.signal.aborted, true); assert.equal(await captured.confirmForeground(), false);
});

test("missing Steam running-app state cannot masquerade as a retired shortcut", () => {
  assert.equal(isConfirmedSteamIdle(0), true);
  assert.equal(isConfirmedSteamIdle("0"), true);
  for (const value of [undefined, null, false, NaN, "", "unknown", 2485417537, "2485417537"])
    assert.equal(isConfirmedSteamIdle(value), false);
  assert.equal(isConfirmedSteamIdle(undefined, false), true);
  for (const value of [undefined, 0, "0", 2485417537])
    assert.equal(isConfirmedSteamIdle(value, true), false);
  assert.equal(isConfirmedSteamIdle(2485417537, false), false);
  assert.equal(isConfirmedSteamIdle(null, false), false);
});

test("the returned game's primary action replaces inert panel focus only on its own Steam route", () => {
  const focused = [];
  const action = { tabIndex: 0, isConnected: true, getClientRects: () => [{}], closest: () => null,
    focus(options) { document.activeElement = action; focused.push(options.preventScroll); } };
  const document = { querySelectorAll(selector) {
    assert.equal(selector, '.status .action [role="button"][tabindex]');
    return [{ ...action, getClientRects: () => [] }, action];
  } };
  const classes = { ActionButtonAndStatusPanel: "status", AppActionButton: "action" };
  focusReturnedSteamGameAction(document, "/library/app/42", 41, undefined, classes);
  focusReturnedSteamGameAction(document, "/library/app/42", 42, { closest: () => action }, classes);
  focusReturnedSteamGameAction(document, "/library/app/42", 42, undefined, { ...classes, AppActionButton: "action,body" });
  assert.deepEqual(focused, []);
  assert.equal(focusReturnedSteamGameAction(document, "/library/app/42", 42, undefined, classes), true);
  assert.deepEqual(focused, [true]);
});

test("the exact exited Valve overlay releases its stale tree even when its closing browser throws", () => {
  const appId = 2485417537;
  const main = { document: { hasFocus: () => true } };
  const overlay = { document: { hasFocus: () => true } };
  const deactivated = [];
  const context = { RootWindow: overlay, ActiveWindow: overlay, OnDeactivate(window) {
    deactivated.push(window);
    active = undefined;
    throw new Error("Steam closed the overlay browser");
  } };
  let active = context;
  const tree = { WindowContext: context, Window: overlay };
  const entry = { BrowserWindow: overlay, params: { browserInfo: { m_unAppID: appId } },
    IsGamepadUIOverlayWindow: () => true, GetFocusNavContext: () => context };
  assert.equal(releaseExitedSteamOverlay(appId, main, () => true, () => active, () => tree, id => {
    assert.equal(id, appId);
    return [entry];
  }), true);
  assert.deepEqual(deactivated, [overlay]);
  assert.equal(active, undefined);
});

test("foreign or ambiguous overlay entries and a changed main focus proof remain untouched", () => {
  const appId = 2485417537;
  let mainFocused = true;
  const main = { document: { hasFocus: () => mainFocused } };
  const overlay = { document: { hasFocus: () => true } };
  const context = { RootWindow: overlay, ActiveWindow: overlay, OnDeactivate() { assert.fail("unsafe deactivation"); } };
  const tree = { WindowContext: context, Window: overlay };
  const entry = { BrowserWindow: overlay, params: { browserInfo: { m_unAppID: appId } },
    IsGamepadUIOverlayWindow: () => true, GetFocusNavContext: () => context };
  const release = (id, proof, activeTree, instances) =>
    releaseExitedSteamOverlay(id, main, proof, () => context, () => activeTree, () => instances);
  assert.equal(release(appId + 1, () => true, tree, [entry]), false);
  assert.equal(release(appId, () => true, tree, [entry, entry]), false);
  assert.equal(release(appId, () => true, { ...tree, Window: main }, [entry]), false);
  assert.equal(release(appId, () => true, tree, [{ ...entry, GetFocusNavContext: () => ({}) }]), false);
  assert.equal(release(appId, () => true, tree, []), false);
  let checked = 0;
  assert.equal(release(appId, () => ++checked === 1, tree, [entry]), false);
  mainFocused = false;
  assert.equal(release(appId, () => true, tree, [entry]), false);
});

test("an observed overlay can close and leave its exact child context active before native exit proof", () => {
  const appId = 2485417537;
  let now = 0;
  const scope = createSteamOverlayReturnScope(undefined, () => now);
  const main = { document: { hasFocus: () => true } };
  const root = { closed: false };
  const child = { closed: true };
  const context = { RootWindow: root, ActiveWindow: child, OnDeactivate(window) {
    assert.equal(window, child);
    active = undefined;
  } };
  let active = context;
  const tree = { WindowContext: context, Window: child };
  const entry = { BrowserWindow: root, params: { browserInfo: { m_unAppID: appId } },
    IsGamepadUIOverlayWindow: () => true, GetFocusNavContext: () => context };
  scope.observe(appId, [entry, entry]);
  assert.equal(scope.get(appId), undefined);
  scope.observe(appId, [entry], active);
  root.closed = true;
  scope.observe(0, []);
  assert.equal(releaseExitedSteamOverlay(appId, main, () => true, () => active, () => tree, () => [], scope.get(appId)), true);
  now = 15_000;
  assert.equal(scope.get(appId), undefined);
  root.closed = false;
  scope.observe(appId, [entry]);
  assert.ok(scope.get(appId));
  scope.observe(appId + 1, []);
  assert.equal(scope.get(appId), undefined);
  scope.observe(appId, [entry]);
  scope.clear();
  assert.equal(scope.get(appId), undefined);
});

test("Steam focus events trigger an immediate bounded wakeup and cancel on unload", () => {
  const calls = [];
  const timers = new Map();
  let now = 0, id = 0;
  const wakeup = createSteamNavigationWakeup(() => calls.push(now), (callback, delay) => {
    timers.set(++id, { callback, at: now + delay });
    return id;
  }, timer => timers.delete(timer));
  wakeup.wake();
  wakeup.wake();
  assert.deepEqual(calls, [0]);
  while (timers.size) {
    const [timer, task] = timers.entries().next().value;
    timers.delete(timer);
    now = task.at;
    task.callback();
  }
  assert.deepEqual(calls, [0, 65, 160, 305, 540, 940]);
  now += 100;
  wakeup.wake();
  assert.equal(timers.size, 1);
  wakeup.stop();
  assert.equal(timers.size, 0);
  wakeup.wake();
  assert.equal(calls.at(-1), 1040);
  assert.equal(calls.length, 7);
});

test("a blurred Big Picture browser requests key focus before moving gamepad navigation", () => {
  let documentFocus = false;
  let navigationFocus = false;
  const actions = [];
  const steam = {
    hasDocumentFocus: () => documentFocus,
    hasNavigationFocus: () => navigationFocus,
    requestKeyFocus: () => actions.push("keys"),
    focusApplication: () => { actions.push("navigation"); navigationFocus = true; },
  };
  assert.equal(recoverSteamNavigation(steam), false);
  assert.deepEqual(actions, ["keys"]);
  documentFocus = true;
  assert.equal(recoverSteamNavigation(steam), true);
  assert.deepEqual(actions, ["keys", "navigation"]);
  assert.equal(recoverSteamNavigation(steam), true);
  assert.deepEqual(actions, ["keys", "navigation"]);
});

test("native foreground proof repairs navigation but never reports real focus while CEF remains blurred", () => {
  let nativeForeground = true;
  const actions = [];
  const document = { hasFocus: () => false, querySelectorAll: () => [play] };
  const stale = { ownerDocument: document, isConnected: true, getClientRects: () => [], closest: () => null };
  const play = { ownerDocument: document, tabIndex: 0, isConnected: true, getClientRects: () => [{}], closest: () => null,
    focus() { document.activeElement = play; selected = play; } };
  const next = { ...play };
  let selected = stale;
  const tree = { GetLastFocusedNode: () => ({ Element: selected, BHasFocus: () => true }) };
  const navigation = {
    hasDocumentFocus: () => document.hasFocus(),
    hasNativeForeground: () => nativeForeground,
    hasNavigationFocus: () => hasVisibleSteamNavigationFocus(document, tree),
    requestKeyFocus: () => actions.push("keys"),
    focusApplication() {
      actions.push("navigation");
      focusReturnedSteamGameAction(document, "/library/app/42", 42, undefined,
        { ActionButtonAndStatusPanel: "panel", AppActionButton: "action" });
    },
  };
  assert.equal(hasVisibleSteamNavigationFocus(document, tree), false);
  assert.equal(recoverSteamNavigation(navigation), false);
  assert.equal(document.hasFocus(), false, "native ownership does not fabricate DOM focus");
  assert.deepEqual(actions, ["keys", "navigation", "keys"]);
  // Steam's GetEventTarget dispatches through its active context/tree, without
  // gating gamepad input on document.hasFocus(). The first intended direction
  // must have a visible target after recovery.
  const firstDown = () => {
    if (!nativeForeground || !hasVisibleSteamNavigationFocus(document, tree)) return false;
    selected = next;
    return true;
  };
  assert.equal(firstDown(), true);
  assert.equal(selected, next);
});

test("native return recovery stops when foreground proof expires during key-focus or navigation handoff", () => {
  for (const loseAt of ["keys", "navigation"]) {
    let current = true, focused = false;
    const calls = [];
    assert.equal(recoverSteamNavigation({
      hasDocumentFocus: () => false,
      hasNativeForeground: () => current,
      hasNavigationFocus: () => focused,
      requestKeyFocus() { calls.push("keys"); if (loseAt === "keys") current = false; },
      focusApplication() { calls.push("navigation"); focused = true; current = false; },
    }), false);
    assert.deepEqual(calls, loseAt === "keys" ? ["keys"] : ["keys", "navigation"]);
  }
});

test("native main-window proof can release only the observed exited overlay while CEF remains blurred", () => {
  const app = 42, main = { document: { hasFocus: () => false } }, overlay = {};
  let current = true, active;
  const context = { RootWindow: overlay, ActiveWindow: overlay, OnDeactivate(window) {
    assert.equal(window, overlay); active = undefined;
  } };
  active = context;
  const tree = { WindowContext: context, Window: overlay };
  const retained = { appId: app, root: overlay, context };
  const release = proof => releaseExitedSteamOverlay(app, main, () => current, () => active, () => tree,
    () => [], retained, undefined, proof);
  assert.equal(release(() => false), false);
  assert.equal(release(() => true), true);
  active = context;
  current = false;
  assert.equal(release(() => true), false, "native proof cannot override an invalidated game return");
});

test("a hidden stale marker cannot satisfy focus or block virtual D-pad recovery", () => {
  const document = {};
  const element = (visible, ownerDocument = document) => ({
    ownerDocument, isConnected: true, getClientRects: () => visible ? [{}] : [],
    closest: () => null, matches: () => true,
  });
  const stale = element(false);
  const visible = element(true);
  let selection = stale;
  document.querySelectorAll = () => [stale, visible];
  const activeTree = {GetLastFocusedNode: () => ({Element:selection,BHasFocus:() => true})};
  assert.equal(hasVisibleSteamNavigationFocus(document), true, "legacy paths can use a visible marker");
  assert.equal(hasVisibleSteamNavigationFocus(document, activeTree), false, "the active tree's hidden node must be repaired");
  assert.equal(hasVisibleSteamNavigationFocus(document, {GetLastFocusedNode: () => ({Element:element(true, {}),BHasFocus:() => true})}), false);
  const controller = {
    DispatchVirtualButtonClick() {
      if (!hasVisibleSteamNavigationFocus(document, activeTree)) return false;
      selection = element(true);
      return true;
    },
  };
  assert.equal(controller.DispatchVirtualButtonClick(), false);
  assert.equal(recoverSteamNavigation({
    hasDocumentFocus: () => true,
    hasNavigationFocus: () => hasVisibleSteamNavigationFocus(document, activeTree),
    requestKeyFocus: () => assert.fail("document already owns key focus"),
    focusApplication: () => { selection = visible; },
  }), true);
  assert.equal(controller.DispatchVirtualButtonClick(), true);
  assert.notEqual(selection, visible);
});

test("a fresh native version on the first poll retries until CEF navigation recovers", async () => {
  let confirm = false;
  let recovered = false;
  let attempts = 0;
  const confirmedVersions = [];
  const recovery = createSteamNavigationRecovery(async version => {
    confirmedVersions.push(version);
    return { ok: confirm, appId: 0 };
  }, () => { attempts++; return recovered; }, () => 0);

  await recovery.observe(4, false);
  assert.deepEqual(confirmedVersions, []);
  await recovery.observe(4, true);
  assert.deepEqual(confirmedVersions, [4]);
  assert.equal(attempts, 0);
  confirm = true;
  await recovery.observe(4, true);
  recovered = true;
  await recovery.observe(4, true);
  await recovery.observe(4, true);
  assert.deepEqual(confirmedVersions, [4, 4, 4]);
  assert.equal(attempts, 2);
});

test("the exact exited shortcut can recover navigation while Steam still tracks its helper", async () => {
  const shortcut = 2485417537;
  const recoveredApps = [];
  const recovery = createSteamNavigationRecovery(async () => ({ ok: true, appId: shortcut }),
    appId => { recoveredApps.push(appId); return true; }, () => shortcut);

  await recovery.observe(1, true);
  await recovery.observe(1, true);
  assert.deepEqual(recoveredApps, [shortcut]);
});

test("a foreign running app or absent native proof blocks navigation repair", async () => {
  let activeApp = 43;
  let proof = { ok: true, appId: 42 };
  let afterProof = 42;
  let attempts = 0;
  const recovery = createSteamNavigationRecovery(async () => {
    activeApp = afterProof;
    return proof;
  }, () => { attempts++; return true; }, () => activeApp);

  await recovery.observe(1, true);
  assert.equal(attempts, 0, "a different app was running when proof was requested");
  afterProof = 43;
  await recovery.observe(1, true);
  assert.equal(attempts, 0, "a different app started while awaiting proof");
  activeApp = afterProof = 42;
  proof = { ok: false, appId: 42 };
  await recovery.observe(1, true);
  proof = { ok: true, appId: 0 };
  await recovery.observe(1, true);
  assert.equal(attempts, 0, "failed and unscoped proof cannot bypass Steam's running app");
  activeApp = afterProof = 0;
  await recovery.observe(1, true);
  assert.equal(attempts, 1);
});

test("relaunching the same shortcut invalidates in-flight and delayed proof", async () => {
  const shortcut = 2485417537;
  const releases = new Map();
  const proof = { ok: true, appId: shortcut };
  let attempts = 0;
  const recovery = createSteamNavigationRecovery(version => new Promise(resolve => releases.set(version, resolve)),
    () => { attempts++; return true; }, () => shortcut);

  const stale = recovery.observe(6, true);
  recovery.launchStarted();
  releases.get(6)(proof);
  await stale;
  await recovery.observe(7, true);
  assert.equal(releases.has(7), false);
  assert.equal(attempts, 0);

  recovery.gameArmed();
  await recovery.observe(7, true);
  assert.equal(releases.has(7), false);
  const fresh = recovery.observe(8, true);
  releases.get(8)(proof);
  await fresh;
  assert.equal(attempts, 1);
});

test("a newer version and explicit cancellation invalidate asynchronous foreground proof", async () => {
  const releases = new Map();
  let attempts = 0;
  const recovery = createSteamNavigationRecovery(version => new Promise(resolve => releases.set(version, resolve)),
    () => { attempts++; return true; }, () => 0);
  const proof = { ok: true, appId: 0 };

  const old = recovery.observe(3, true);
  const current = recovery.observe(4, true);
  releases.get(3)(proof);
  await old;
  assert.equal(attempts, 0);
  recovery.cancel(4);
  releases.get(4)(proof);
  await current;
  await recovery.observe(4, true);
  assert.equal(attempts, 0);
  const next = recovery.observe(5, true);
  releases.get(5)(proof);
  await next;
  assert.equal(attempts, 1);
});

test("an expired return cannot change navigation after delayed foreground proof", async () => {
  let now = 0;
  let confirm;
  let attempts = 0;
  const recovery = createSteamNavigationRecovery(() => new Promise(resolve => { confirm = resolve; }),
    () => { attempts++; return true; }, () => 0, () => now);

  const pending = recovery.observe(1, true);
  now = 12_001;
  confirm({ ok: true, appId: 0 });
  await pending;
  await recovery.observe(1, true);
  assert.equal(attempts, 0);
});

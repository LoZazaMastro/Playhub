import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";

function load(name, require) {
  const exports = {};
  vm.runInNewContext(ts.transpileModule(readFileSync(new URL(`../src/${name}.ts`, import.meta.url), "utf8"), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText, { exports, require });
  return exports;
}
const navigationApi = load("steamMirrorNavigation");
const { steamMirrorNavigation } = navigationApi;
const app = 2485417537, gameID = "10674787038353424384";
function fixture(intent = "quick") {
  const calls = [], doc = {}, parent = { document: {}, closed: false };
  const win = { document: doc, closed: false };
  let f;
  const nodeElement = { isConnected: true, ownerDocument: doc };
  const element = { isConnected: true, ownerDocument: doc, getClientRects: () => { f.onRect?.(); return [{}]; }, contains: e => e === nodeElement };
  const node = { Element: nodeElement, BHasFocus: () => f.focused };
  const context = { RootWindow: parent, m_rgGamepadNavigationTrees: [], BIsActive: () => f.active,
    BIsGamepadInputSuppressed: () => f.suppressed, OnActivate(w) { calls.push("context"); f.active = true; this.ActiveWindow = w; nav.context = this; f.onContext?.(); } };
  const tree = { id: intent === "quick" ? "QuickAccess-NA" : "MainNavMenuContainer", WindowContext: context,
    Window: win, Root: { Element: element }, BIsEnabled: () => true, GetLastFocusedNode: () => f.focused ? node : undefined,
    Activate(take) { assert.equal(take, true); calls.push("tree"); nav.tree = this; if (intent === "main") f.focused = true; f.onTree?.(); } };
  context.m_rgGamepadNavigationTrees.push(tree);
  const nav = { GetActiveContext: () => nav.context, GetActiveNavTree: () => nav.tree };
  context.m_controller = nav;
  const menu = { GetOpenSideMenu: () => f.side, SetQuickAccessContentFocusRequested(v) { assert.equal(v, true); calls.push("content"); f.focused = true; f.onContent?.(); } };
  const browser = { gameID, unPID: 31704, nBrowserID: 0, appID: 2389569 };
  const overlay = { BrowserWindow: parent, MenuStore: menu, IsGamepadUIOverlayWindow: () => true, GetFocusNavContext: () => context,
    params: { browserInfo: { m_gameID: gameID, m_unPID: browser.unPID, m_nBrowserID: 0 } } };
  const mirror = { protocol: 1, state: "shown", sessionId: "a".repeat(64), generation: "1", gameId: gameID,
    sourceHwnd: "41001", sourceCookie: "18446744073709551614", destinationHwnd: "41002", destinationCookie: "18446744073709551615",
    ownerPid: 27156, ownerBirth: "134389155136169895", gamePid: 48584, gameBirth: "134389155148953005", expiresAt: "1400", observedAt: "1000",
    actualScope: 1, preparedCurrent: true, shownCurrent: true, leaseCurrent: true, cleanupComplete: false };
  f = { calls, doc, parent, win, nodeElement, element, node, context, tree, nav, menu, browser, overlay, mirror,
    tick: 0, current: true, focused: false, active: false, suppressed: false, side: intent === "quick" ? 2 : 1 };
  f.create = () => steamMirrorNavigation({ app, browser, overlay, menu, mirror, intent }, () => nav,
    () => [overlay], () => f.current, () => f.tick);
  return f;
}
for (const intent of ["main", "quick"]) test(`${intent}: only original context/tree/content calls, no OS or app-root focus`, () => {
  const f = fixture(intent), target = f.create();
  Object.defineProperty(f.overlay, "FocusApplicationRoot", { get() { throw Error("forbidden"); } });
  assert.ok(target); const state = target.activate();
  assert.deepEqual(f.calls, intent === "quick" ? ["context", "tree", "content"] : ["context", "tree"]);
  assert.equal(state.confirmed, true); assert.equal(target.read(f.mirror).confirmed, true);
});
for (const [name, change] of [
  ["Prepared", f => f.mirror.state = "prepared"], ["scope0", f => f.mirror.actualScope = 0],
  ["lease revoked", f => f.mirror.leaseCurrent = false], ["different game", f => f.mirror.gameId = "2"],
]) test(`no navigation admission from ${name}`, () => { const f = fixture(); change(f); assert.equal(f.create(), undefined); assert.deepEqual(f.calls, []); });
test("no positive undefined-context equality", () => { const f = fixture(); assert.equal(f.create().read(f.mirror).confirmed, false); assert.deepEqual(f.calls, []); });
test("suppressed context remains untouched", () => { const f = fixture(); f.suppressed = true; const state = f.create().activate(); assert.equal(state.attempted, false); assert.deepEqual(f.calls, []); });
test("duplicate target trees are refused", () => { const f = fixture(); f.context.m_rgGamepadNavigationTrees.push({ ...f.tree }); assert.equal(f.create(), undefined); });
for (const [name, mutate] of [
  ["menu close", f => f.side = 0], ["dispose", f => f.current = false],
  ["child window", f => f.tree.Window = { document: f.doc }], ["root", f => f.tree.Root = { Element: f.element }],
]) test(`reentrant ${name} during OnActivate prevents subsequent calls`, () => {
  const f = fixture(), target = f.create(); f.onContext = () => mutate(f); target.activate(); assert.deepEqual(f.calls, ["context"]);
});
test("tree activation reentry cannot request stale content focus", () => { const f = fixture(), target = f.create(); f.onTree = () => f.current = false; target.activate(); assert.deepEqual(f.calls, ["context", "tree"]); });
test("slow local getter prevents late activation", () => { const f = fixture(), target = f.create(); f.onRect = () => f.tick = 251; assert.equal(target.activate().attempted, false); assert.deepEqual(f.calls, []); });
test("getter reentry is checked before mutation", () => { const f = fixture(), target = f.create(); f.onRect = () => f.current = false; assert.equal(target.activate().attempted, false); assert.deepEqual(f.calls, []); });
test("final native generation replacement cannot confirm old navigation", () => { const f = fixture(), target = f.create(); target.activate(); assert.equal(target.read({ ...f.mirror, generation: "2" }).confirmed, false); });
test("last focused node must belong to the exact child document", () => { const f = fixture(), target = f.create(); target.activate(); f.nodeElement.ownerDocument = {}; assert.equal(target.read(f.mirror).confirmed, false); });

// Execute the production coordinator and its permanent fixture, without any
// Steam, network, timers, windows or synthetic events in the VM.
const control = load("controlledQamFocus"), browserApi = load("dashboardOverlay", () => control);
const coordinator = load("steamMirrorOverlay", n => n === "./steamMirrorNavigation" ? navigationApi : browserApi);
const permanent = readFileSync(new URL("steamMirrorOverlay.test.mjs", import.meta.url), "utf8");
function integrated(options = {}) {
  const nav = {}, sandbox = { createSteamMirrorOverlay: coordinator.createSteamMirrorOverlay, assert, options: { navigation: nav, ...options } };
  vm.runInNewContext(permanent.slice(permanent.indexOf("const app = "), permanent.indexOf('\ntest("first Guide'))
    + "\nglobalThis.result = fixture(options);", sandbox);
  const f = sandbox.result, source = f.target.BrowserWindow, doc = {}, win = { document: doc, closed: false };
  const e = { ownerDocument: doc, isConnected: true }, root = { ownerDocument: doc, isConnected: true, getClientRects: () => [{}], contains: x => x === e };
  const ctx = { RootWindow: source, m_controller: nav, m_rgGamepadNavigationTrees: [], BIsActive: () => true,
    OnActivate(w) { nav.context = this; this.ActiveWindow = w; f.calls.push("nav-context"); }, BIsGamepadInputSuppressed: () => false };
  const main = { id: "MainNavMenuContainer", Root: { Element: root }, WindowContext: ctx, Window: win, BIsEnabled: () => true,
    GetLastFocusedNode: () => ({ Element: e, BHasFocus: () => true }), Activate() { nav.tree = this; f.calls.push("nav-main"); f.afterMain?.(); } };
  const quick = { ...main, id: "QuickAccess-NA", Activate() { nav.tree = this; f.calls.push("nav-quick"); } };
  ctx.m_rgGamepadNavigationTrees.push(main, quick); nav.GetActiveContext = () => nav.context; nav.GetActiveNavTree = () => nav.tree;
  f.target.GetFocusNavContext = () => ctx;
  f.menu.SetQuickAccessContentFocusRequested = () => f.calls.push("nav-content");
  return f;
}
test("actual coordinator focuses only after Shown and retains four native reads", async () => {
  const f = integrated(); await f.route.guide("quick");
  assert.equal(f.route.shown, true); assert.equal(f.reads, 4); assert.ok(f.calls.indexOf("nav-context") > f.calls.indexOf("show"));
  assert.equal(f.calls.filter(x => x === "nav-content").length, 1); await f.route.dispose();
});
test("latest Quick during original Main activation is reconciled without Prepare/Set2/Show repetition", async () => {
  const f = integrated(); f.afterMain = () => { f.afterMain = undefined; void f.route.guide("quick"); };
  await f.route.guide("main"); await f.route.settled(); assert.equal(f.route.shown, true);
  for (const name of ["prepare", "set2", "show"]) assert.equal(f.calls.filter(x => x === name).length, 1);
  assert.ok(f.calls.includes("nav-quick")); assert.equal(f.menu.GetOpenSideMenu(), 2); await f.route.dispose();
});

test("Quick during the final native read is mounted and focused with no repeated native opening", async () => {
  const f = integrated({ read: ctx => { if (ctx.reads === 4) void ctx.route.guide("quick"); } });
  await f.route.guide("main"); await f.route.settled();
  assert.equal(f.route.shown, true); assert.equal(f.menu.GetOpenSideMenu(), 2); assert.equal(f.reads, 4);
  for (const n of ["prepare", "set2", "show"]) assert.equal(f.calls.filter(x => x === n).length, 1);
  assert.ok(f.calls.includes("nav-quick")); await f.route.dispose();
});
test("final-read Quick cannot focus a replaced menu and retires only the owned generation", async () => {
  let f; f = integrated({ read: ctx => { if (ctx.reads === 4) { void ctx.route.guide("quick"); f.target.MenuStore = {}; } } });
  await f.route.guide("main"); await f.route.settled(); assert.equal(f.route.active, false);
  assert.equal(f.calls.includes("nav-quick"), false); assert.equal(f.calls.filter(x => x === "prepare").length, 1);
});
test("reentrant final reconciliation is bounded and never repeats Prepare, Set2 or Show", async () => {
  const f = integrated({ read: ctx => { if (ctx.reads === 4) void ctx.route.guide("quick"); } });
  f.menu.SetQuickAccessContentFocusRequested = () => { f.calls.push("nav-content"); void f.route.guide("main"); };
  f.afterMain = () => { if (f.reads >= 4) void f.route.guide("quick"); };
  await f.route.guide("main"); await f.route.settled(); assert.equal(f.route.active, false);
  for (const n of ["prepare", "set2", "show"]) assert.equal(f.calls.filter(x => x === n).length, 1);
});

import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";

function compile(path) {
  const source = readFileSync(new URL(path, import.meta.url), "utf8");
  const js = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText;
  const exports = {};
  vm.runInNewContext(js, { exports });
  return exports;
}
const { createDashboardExit, consumeDashboardCancel } = compile("../src/dashboardExit.ts");
function exitFixture(present = true) {
  const calls = [], timers = [];
  const exit = createDashboardExit({ hideOverlay: () => calls.push("hide"), back: () => calls.push("back"), library: () => calls.push("library"), closeMenus: () => calls.push("menus"), isPresent: () => present, schedule: (fn) => timers.push(fn) });
  return { calls, timers, exit };
}
test("empty or repeated history falls back to Steam library once", () => {
  const { calls, timers, exit } = exitFixture();
  assert.equal(exit.leave(), true);
  assert.equal(exit.leave(), false);
  timers.forEach((fn) => fn());
  assert.deepEqual(calls, ["hide", "back", "library", "menus"]);
});
test("successful desktop route return preserves the previous page", () => {
  const { calls, timers, exit } = exitFixture(false);
  exit.leave(); timers.forEach((fn) => fn());
  assert.deepEqual(calls, ["hide", "back", "menus"]);
});
test("overlay close and route observer cannot issue two history back actions", () => {
  const { calls, exit } = exitFixture();
  exit.leave(); exit.leave(); exit.leave();
  assert.deepEqual(calls, ["hide", "back"]);
});
test("a newly opened Dashboard cancels the preceding delayed exit", () => {
  const { calls, timers, exit } = exitFixture();
  exit.leave(); exit.reset(); timers[0]();
  assert.deepEqual(calls, ["hide", "back"]);
  assert.equal(exit.leave(), true);
});
test("missing or throwing history still reaches the bounded fallback", () => {
  const calls = [], timers = [];
  const exit = createDashboardExit({ hideOverlay() {}, back() { throw Error("no history"); }, library() { calls.push("library"); }, closeMenus() {}, isPresent: () => true, schedule: (fn) => timers.push(fn) });
  exit.leave(); timers[0](); assert.deepEqual(calls, ["library"]);
});
test("duplicate cancel and held Escape are consumed without dismissing another view", () => {
  let consumed = 0;
  const event = { preventDefault() { consumed++; }, stopPropagation() { consumed++; } };
  assert.equal(consumeDashboardCancel(event, 1000, -Infinity), true);
  assert.equal(consumeDashboardCancel(event, 1010, 1000), false);
  assert.equal(consumeDashboardCancel({ ...event, repeat: true }, 1600, 1000), false);
  assert.equal(consumeDashboardCancel(event, 2000, 1000, true), false);
  assert.equal(consumeDashboardCancel(event, 2200, 1000, false), true);
  assert.equal(consumed, 10);
});

const source = readFileSync(new URL("../src/DashboardPage.tsx", import.meta.url), "utf8");
const ast = ts.createSourceFile("page.tsx", source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
let closeNode;
function visit(node) { if (ts.isVariableDeclaration(node) && node.name.getText(ast) === "closeEntry") closeNode = node; ts.forEachChild(node, visit); }
visit(ast);
const closeJs = ts.transpileModule(`globalThis.action = ${closeNode.initializer.getText(ast)}`, { compilerOptions: { target: ts.ScriptTarget.ES2022 } }).outputText;
function closeFixture(response) {
  const entries = [{ handle: "11", processId: 123, title: "Game" }, { handle: "22", processId: 456, title: "Other" }];
  const calls = [], notices = [];
  const context = { useCallback: (fn) => fn, closingHandles: { current: new Set() }, setCloseNotice: (message) => notices.push(message), windows: entries, sortSwitcherWindows: (value) => value, windowCloseCopy: () => ({ pending: "pending", failed: "failed" }), detectSteamLocale: () => "en", closeWindow: async (handle, pid) => { calls.push([handle, pid]); return typeof response === "function" ? response() : response; }, setWindows: (update) => { context.windows = update(context.windows); }, cachedSwitcherWindows: entries, refresh: async () => {}, window: { requestAnimationFrame: (fn) => fn() }, activateDashboardSteamContext() {}, focusDashboard: () => true, ensureDashboardFocus: () => true, onReady() {} };
  vm.runInNewContext(closeJs, context);
  return { context, calls, notices, entry: entries[0] };
}
test("close waits for native completion and supplies the window owner PID", async () => {
  const f = closeFixture({ ok: true, closed: true, pending: false });
  await f.context.action(f.entry, 0);
  assert.deepEqual(f.calls, [["11", 123]]);
  assert.equal(f.context.windows.length, 1);
  assert.equal(f.context.windows[0].handle, "22");
});
test("save prompt retains the app card and displays its pending result", async () => {
  const f = closeFixture({ ok: true, closed: false, pending: true });
  await f.context.action(f.entry, 0);
  assert.equal(f.context.windows.length, 2);
  assert.equal(f.notices.at(-1).text, "Game: pending");
});
test("close network failure is visible and retry remains available", async () => {
  const f = closeFixture(() => { throw Error("offline"); });
  await f.context.action(f.entry, 0);
  assert.equal(f.context.windows.length, 2);
  assert.equal(f.notices.at(-1).text, "Game: failed");
  assert.equal(f.context.closingHandles.current.size, 0);
});
test("repeated close while a request is pending sends only one WM_CLOSE request", async () => {
  let resolve;
  const f = closeFixture(() => new Promise((done) => { resolve = done; }));
  const first = f.context.action(f.entry, 0);
  await f.context.action(f.entry, 0);
  assert.equal(f.calls.length, 1);
  resolve({ ok: true, closed: true, pending: false }); await first;
});
test("window close feedback is present in every shipped locale", () => {
  const { windowCloseCopy } = compile("../src/windowCloseCopy.ts");
  for (const locale of ["it", "es", "fr", "de", "pt", "uk", "zh", "ja", "ko", "hi", "ru"]) {
    assert.notEqual(windowCloseCopy(locale).pending, windowCloseCopy("en").pending);
    assert.ok(windowCloseCopy(locale).failed);
  }
});

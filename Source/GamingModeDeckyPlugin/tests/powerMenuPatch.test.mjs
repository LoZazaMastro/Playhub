import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import test, { mock } from "node:test";
import ts from "typescript";
import { afterPatch } from "../node_modules/@decky/ui/dist/utils/patcher.js";

mock.method(console, "debug", () => {});

const source = fs.readFileSync(new URL("../src/powerMenuPatch.ts", import.meta.url), "utf8");
const compiled = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText;

function fixture() {
  const timers = new Map();
  const observers = new Set();
  let sequence = 0;
  let rows = [];
  const React = {
    createElement: (type, props) => ({ type, key: props?.key, props: props ?? {}, element: true }),
    isValidElement: (node) => node?.element === true,
    cloneElement: (node, props, children) => ({ ...node, props: { ...node.props, ...props, children } }),
  };
  const doc = { documentElement: {}, querySelectorAll: () => rows };
  class Observer {
    constructor(callback) { this.callback = callback; }
    observe() { observers.add(this); }
    disconnect() { observers.delete(this); }
  }
  const win = {
    document: doc, MutationObserver: Observer,
    setTimeout: (callback) => { const id = ++sequence; timers.set(id, callback); return id; },
    clearTimeout: (id) => timers.delete(id),
    requestAnimationFrame: (callback) => { const id = ++sequence; timers.set(id, callback); return id; },
    cancelAnimationFrame: (id) => timers.delete(id),
    setInterval: () => ++sequence, clearInterval: () => {},
  };
  doc.defaultView = win;
  const DFL = { afterPatch, MenuItem: "MenuItem", MenuSeparator: "Separator", findSP: () => win };
  const exports = {};
  vm.runInNewContext(compiled, { exports, window: win, require: () => ({ DFL, SP_REACT: React }) });
  const calls = [];
  const labels = { gaming: "Riavvia in Gaming Mode", desktop: "Riavvia in Desktop Mode" };
  class Menu {
    constructor({ bound = false, singleton = false, restart = true } = {}) {
      const child = React.createElement("NativeItem", { strDisplayNameLocToken: restart ? "#Quit_Restart" : "#Quit_Shutdown" });
      this.props = { children: singleton ? child : [child] };
      this.refreshes = 0;
      if (bound) this.render = this.render.bind(this);
      this.result = this.render();
    }
    render() { return React.createElement("Container", { children: this.props.children }); }
    forceUpdate() { this.refreshes++; this.result = this.render(); }
  }
  function show(menu, text = "Restart") {
    for (const row of rows) row.isConnected = false;
    rows = [{ isConnected: true, textContent: text, ownerDocument: doc, __reactFiber$test: { stateNode: null, return: { stateNode: menu } } }];
    for (const observer of observers) observer.callback();
  }
  function flush() {
    for (let iteration = 0; timers.size && iteration < 10; iteration++) {
      const jobs = [...timers.values()]; timers.clear(); jobs.forEach((job) => job());
    }
    assert.equal(timers.size, 0, "refresh loop must terminate");
  }
  function actions(node) {
    if (Array.isArray(node)) return node.flatMap(actions);
    if (!node) return [];
    return (node.key?.startsWith("playhub-restart-") && node.type === "MenuItem" ? [node] : []).concat(actions(node.props?.children));
  }
  return { Menu, show, flush, actions, calls, timers, install: () => exports.installPowerMenuPatch(() => labels, (mode) => calls.push(mode)) };
}

test("first mounted menu gains both actions without reopening", () => {
  const f = fixture(); const menu = new f.Menu(); f.show(menu); const stop = f.install();
  try {
    f.flush(); const actions = f.actions(menu.result); assert.equal(actions.length, 2);
    actions.forEach((item) => item.props.onSelected()); assert.deepEqual(f.calls, ["gaming", "desktop"]);
  } finally { stop(); }
});
test("replacement instance before first refresh also gains actions", () => {
  const f = fixture(); f.show(new f.Menu()); const stop = f.install();
  try { const actual = new f.Menu(); f.show(actual); f.flush(); assert.equal(f.actions(actual.result).length, 2); assert.ok(actual.refreshes > 0); }
  finally { stop(); }
});
test("render bound on first instance is patched", () => {
  const f = fixture(); const menu = new f.Menu({ bound: true }); f.show(menu); const stop = f.install();
  try { f.flush(); assert.equal(f.actions(menu.result).length, 2); } finally { stop(); }
});
test("replaced DOM row does not cancel refresh of the same menu", () => {
  const f = fixture(); const menu = new f.Menu(); f.show(menu); const stop = f.install();
  try { f.show(menu); f.flush(); assert.equal(f.actions(menu.result).length, 2); }
  finally { stop(); }
});
test("translated row does not depend on LocalizationManager being ready", () => {
  const f = fixture(); const menu = new f.Menu(); f.show(menu, "Riavvia"); const stop = f.install();
  try { f.flush(); assert.equal(f.actions(menu.result).length, 2); } finally { stop(); }
});
test("unload cancels pending refresh", () => {
  const f = fixture(); const menu = new f.Menu(); f.show(menu); const stop = f.install(); stop(); f.flush();
  assert.equal(menu.refreshes, 0); assert.equal(f.actions(menu.render()).length, 0);
});
test("40 reopenings do not duplicate actions", () => {
  const f = fixture(); const stop = f.install();
  try { for (let i = 0; i < 40; i++) { const menu = new f.Menu(); f.show(menu); f.flush(); assert.equal(f.actions(menu.result).length, 2); } }
  finally { stop(); }
});
test("ordinary menu stays untouched", () => {
  const f = fixture(); const menu = new f.Menu({ restart: false }); f.show(menu); const stop = f.install();
  try { f.flush(); assert.equal(menu.refreshes, 0); assert.equal(f.actions(menu.result).length, 0); } finally { stop(); }
});
test("binding an already patched prototype does not duplicate actions", () => {
  const f = fixture(); f.show(new f.Menu()); const stop = f.install();
  try { f.flush(); const bound = new f.Menu({ bound: true }); f.show(bound); f.flush(); assert.equal(f.actions(bound.result).length, 2); }
  finally { stop(); }
});
test("single restart child gains both actions", () => {
  const f = fixture(); const menu = new f.Menu({ singleton: true }); f.show(menu); const stop = f.install();
  try { f.flush(); assert.equal(f.actions(menu.result).length, 2); } finally { stop(); }
});
test("unload also disables a bound inherited wrapper", () => {
  const f = fixture(); f.show(new f.Menu()); const stop = f.install(); f.flush();
  const bound = new f.Menu({ bound: true }); f.show(bound); f.flush(); stop();
  assert.equal(f.actions(bound.render()).length, 0);
});

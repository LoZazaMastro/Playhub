import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import test, { mock } from "node:test";
import ts from "typescript";
import { afterPatch, beforePatch } from "../node_modules/@decky/ui/dist/utils/patcher.js";

mock.method(console, "debug", () => {});

const source = fs.readFileSync(new URL("../src/powerMenuPatch.ts", import.meta.url), "utf8");
const compiled = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText;

function fixture({ popupImmediately = true } = {}) {
  const timers = new Map();
  const intervals = new Map();
  const windows = [];
  let sequence = 0;

  function makeWindow(title) {
    const observers = new Set();
    const doc = {
      title,
      rows: [],
      observers,
      documentElement: null,
      querySelectorAll: () => doc.rows,
    };
    doc.documentElement = { ownerDocument: doc };
    class Observer {
      constructor(callback) { this.callback = callback; this.doc = null; }
      observe(element) { this.doc = element.ownerDocument; this.doc.observers.add(this); }
      disconnect() { this.doc?.observers.delete(this); this.doc = null; }
    }
    const win = {
      document: doc,
      closed: false,
      opener: null,
      MutationObserver: Observer,
      setTimeout: (callback) => { const id = ++sequence; timers.set(id, callback); return id; },
      clearTimeout: (id) => timers.delete(id),
      requestAnimationFrame: (callback) => { const id = ++sequence; timers.set(id, callback); return id; },
      cancelAnimationFrame: (id) => timers.delete(id),
      setInterval: (callback) => { const id = ++sequence; intervals.set(id, callback); return id; },
      clearInterval: (id) => intervals.delete(id),
    };
    win.parent = win;
    win.top = win;
    doc.defaultView = win;
    windows.push(win);
    return win;
  }

  const host = makeWindow("QuickAccess");
  const shared = makeWindow("SharedJSContext");
  host.opener = shared;
  shared.g_PopupManager = { m_mapPopups: new Map() };
  let steamWindow = null;

  function addSteamPopup() {
    if (steamWindow && !steamWindow.closed) return steamWindow;
    steamWindow = makeWindow("SP");
    steamWindow.opener = shared;
    shared.g_PopupManager.m_mapPopups.set("SP", { m_popup: steamWindow });
    return steamWindow;
  }

  function removeSteamPopup() {
    if (!steamWindow) return;
    steamWindow.closed = true;
    shared.g_PopupManager.m_mapPopups.delete("SP");
    steamWindow = null;
  }

  if (popupImmediately) addSteamPopup();

  const React = {
    createElement: (type, props) => ({ type, key: props?.key, props: props ?? {}, element: true }),
    isValidElement: (node) => node?.element === true,
    cloneElement: (node, props, children) => ({ ...node, props: { ...node.props, ...props, children } }),
  };
  const manager = { CreateContextMenuInstance: (element) => element };
  const registry = {
    GetContextMenuManager: () => manager,
    GetContextMenuManagerFromWindow: () => manager,
  };
  const DFL = {
    afterPatch,
    beforePatch,
    findModuleExport: (predicate) => predicate(registry) ? registry : undefined,
    MenuItem: "MenuItem",
    MenuSeparator: "Separator",
    findSP: () => ({ window: steamWindow }),
    getGamepadNavigationTrees: () => [],
  };
  const exports = {};
  vm.runInNewContext(compiled, {
    exports,
    window: host,
    require: () => ({ DFL, SP_REACT: React }),
  });

  const calls = [];
  const labels = { gaming: "Riavvia in Gaming Mode", desktop: "Riavvia in Desktop Mode" };
  class Menu {
    constructor({ bound = false, singleton = false, restart = true } = {}) {
      const child = React.createElement("NativeItem", {
        strDisplayNameLocToken: restart ? "#Quit_Restart" : "#Quit_Shutdown",
      });
      this.props = { children: singleton ? child : [child] };
      this.refreshes = 0;
      if (bound) this.render = this.render.bind(this);
      this.result = this.render();
    }
    render() { return React.createElement("Container", { children: this.props.children }); }
    forceUpdate() { this.refreshes += 1; this.result = this.render(); }
  }

  function show(menu, text = "Restart") {
    if (!steamWindow) throw new Error("Steam popup is not available");
    for (const row of steamWindow.document.rows) row.isConnected = false;
    steamWindow.document.rows = [{
      isConnected: true,
      textContent: text,
      ownerDocument: steamWindow.document,
      __reactFiber$test: { stateNode: null, return: { stateNode: menu, memoizedProps: menu.props } },
    }];
    for (const observer of steamWindow.document.observers) observer.callback();
  }

  function flush() {
    for (let iteration = 0; timers.size && iteration < 10; iteration += 1) {
      const jobs = [...timers.values()];
      timers.clear();
      jobs.forEach((job) => job());
    }
    assert.equal(timers.size, 0, "refresh loop must terminate");
  }

  function tickIntervals() {
    [...intervals.values()].forEach((callback) => callback());
  }

  function actions(node) {
    if (Array.isArray(node)) return node.flatMap(actions);
    if (!node) return [];
    return (node.key?.startsWith("playhub-restart-") && node.type === "MenuItem" ? [node] : [])
      .concat(actions(node.props?.children));
  }

  return {
    React,
    manager,
    Menu,
    show,
    flush,
    tickIntervals,
    actions,
    calls,
    timers,
    intervals,
    windows,
    addSteamPopup,
    removeSteamPopup,
    observerCount: () => windows.reduce((total, win) => total + win.document.observers.size, 0),
    install: () => exports.installPowerMenuPatch(
      () => labels,
      (mode) => calls.push(mode),
    ),
  };
}

test("native functional observer power menu is injected before its first mount", () => {
  const f = fixture();
  const original = f.manager.CreateContextMenuInstance;
  const stop = f.install();
  const PowerMenu = { type: () => f.React.createElement("NativeMenu", {
    children: [f.React.createElement("NativeItem", { strDisplayNameLocToken: "#Quit_Restart" })],
  }) };
  try {
    const element = f.manager.CreateContextMenuInstance(f.React.createElement(PowerMenu, {}));
    const result = element.type(element.props);
    assert.equal(f.actions(result).length, 2);
    f.actions(result).forEach(item => item.props.onSelected());
    assert.deepEqual(f.calls, ["gaming", "desktop"]);
  } finally { stop(); }
  assert.equal(f.manager.CreateContextMenuInstance, original);
});

test("menu already mounted in Steam popup gains both actions", () => {
  const f = fixture();
  const menu = new f.Menu();
  f.show(menu);
  const stop = f.install();
  try {
    f.flush();
    const actions = f.actions(menu.result);
    assert.equal(actions.length, 2);
    actions.forEach((item) => item.props.onSelected());
    assert.deepEqual(f.calls, ["gaming", "desktop"]);
  } finally { stop(); }
});

test("menu mounted after installation is patched on its first opening", () => {
  const f = fixture();
  const stop = f.install();
  try {
    const menu = new f.Menu();
    f.show(menu);
    f.flush();
    assert.equal(f.actions(menu.result).length, 2);
  } finally { stop(); }
});

test("Steam popup created after installation is discovered and patched", () => {
  const f = fixture({ popupImmediately: false });
  const stop = f.install();
  try {
    f.addSteamPopup();
    const menu = new f.Menu();
    f.show(menu);
    f.tickIntervals();
    f.flush();
    assert.equal(f.actions(menu.result).length, 2);
  } finally { stop(); }
});

test("recreated Steam popup receives a fresh idempotent patch", () => {
  const f = fixture();
  const first = new f.Menu();
  f.show(first);
  const stop = f.install();
  try {
    f.flush();
    assert.equal(f.actions(first.result).length, 2);
    f.removeSteamPopup();
    f.addSteamPopup();
    const replacement = new f.Menu();
    f.show(replacement);
    f.tickIntervals();
    f.flush();
    assert.equal(f.actions(replacement.result).length, 2);
  } finally { stop(); }
});

test("plugin reload removes the old patch and reapplies it once", () => {
  const f = fixture();
  const menu = new f.Menu();
  f.show(menu);
  const firstStop = f.install();
  f.flush();
  assert.equal(f.actions(menu.result).length, 2);
  firstStop();
  menu.forceUpdate();
  assert.equal(f.actions(menu.result).length, 0);

  const secondStop = f.install();
  try {
    f.flush();
    assert.equal(f.actions(menu.result).length, 2);
  } finally { secondStop(); }
});

test("overlapping reload lifecycles keep one patch until the last cleanup", () => {
  const f = fixture();
  const menu = new f.Menu();
  f.show(menu);
  const firstStop = f.install();
  f.flush();
  const secondStop = f.install();
  f.flush();
  assert.equal(f.actions(menu.result).length, 2);

  firstStop();
  menu.forceUpdate();
  assert.equal(f.actions(menu.result).length, 2);
  secondStop();
  menu.forceUpdate();
  assert.equal(f.actions(menu.result).length, 0);
});

test("cleanup cancels work, disconnects observers and restores native render", () => {
  const f = fixture();
  const menu = new f.Menu({ bound: true });
  f.show(menu);
  const stop = f.install();
  stop();
  f.flush();
  menu.forceUpdate();
  assert.equal(f.actions(menu.result).length, 0);
  assert.equal(menu.refreshes, 1);
  assert.equal(f.timers.size, 0);
  assert.equal(f.intervals.size, 0);
  assert.equal(f.observerCount(), 0);
});

test("40 menu reconstructions never duplicate actions", () => {
  const f = fixture();
  const stop = f.install();
  try {
    for (let index = 0; index < 40; index += 1) {
      const menu = new f.Menu();
      f.show(menu);
      f.flush();
      assert.equal(f.actions(menu.result).length, 2);
    }
  } finally { stop(); }
});

test("bound Steam menu render is patched", () => {
  const f = fixture();
  const menu = new f.Menu({ bound: true });
  f.show(menu);
  const stop = f.install();
  try { f.flush(); assert.equal(f.actions(menu.result).length, 2); }
  finally { stop(); }
});

test("translated native row does not affect token-based discovery", () => {
  const f = fixture();
  const menu = new f.Menu();
  f.show(menu, "Riavvia il sistema");
  const stop = f.install();
  try { f.flush(); assert.equal(f.actions(menu.result).length, 2); }
  finally { stop(); }
});

test("ordinary menus stay untouched", () => {
  const f = fixture();
  const menu = new f.Menu({ restart: false });
  f.show(menu);
  const stop = f.install();
  try {
    f.flush();
    assert.equal(menu.refreshes, 0);
    assert.equal(f.actions(menu.result).length, 0);
  } finally { stop(); }
});

test("single restart child gains both actions", () => {
  const f = fixture();
  const menu = new f.Menu({ singleton: true });
  f.show(menu);
  const stop = f.install();
  try { f.flush(); assert.equal(f.actions(menu.result).length, 2); }
  finally { stop(); }
});

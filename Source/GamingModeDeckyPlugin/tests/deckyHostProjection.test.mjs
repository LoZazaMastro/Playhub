import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
import ts from "typescript";
const compile = file => ts.transpileModule(readFileSync(new URL(`../src/${file}`, import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX },
}).outputText;
function preference(sharedWindow = {}) {
  const exports = {};
  vm.runInNewContext(compile("deckyNativeTabPreference.ts"), { exports, window: sharedWindow });
  return exports;
}
function fixture(sharedWindow = {}) {
  const api = {};
  vm.runInNewContext(compile("deckyHostStandalone.ts"), { exports: api, window: sharedWindow, WeakRef, setInterval: () => 1, clearInterval() {} });
  let arrays = [[]]; let healthy = true; let notifications = 0;
  const native = { id: 999, content: {} }, own = { id: 1401877092, content: {} };
  const hook = { tabs: [own, native], render(tabs) {
    if (!tabs.length) for (const entry of this.tabs) tabs.push({ key: entry.id, panel: entry.content });
  } };
  const adapter = api.createStandaloneDeckyHost(hook, hook.render, own.id, () => true, () => {
    for (const tabs of arrays) { adapter.beforeRender(tabs); hook.render(tabs); adapter.afterRender(tabs, true); }
  });
  const element = { isConnected: true, closest: () => ({ id: "quickaccess_content_1401877092" }),
    ownerDocument: { getElementById: () => ({ getAttribute: () => "false" }) } };
  const lease = adapter.capability.acquire({ element, createContent: root => root,
    isHealthy: () => healthy, onRelease() {} });
  const observe = tabs => {
    const store = api.getDeckyTabProjection(tabs); let rendered;
    const render = () => { rendered = store.getSnapshot().map(tab => tab.key); };
    render(); const unsubscribe = store.subscribe(() => { notifications++; render(); });
    return { read: () => Array.from(rendered), unsubscribe };
  };
  return { api, adapter, lease, observe, element, arrays, setArrays: next => { arrays = next; },
    notifications: () => notifications, fail: () => { healthy = false; } };
}
test("mounted tab observer sees host-ready removal without native render, input or resize", async () => {
  const f = fixture(); f.lease.setActive(false);
  const observer = f.observe(f.arrays[0]);
  assert.deepEqual(observer.read(), [1401877092, 999]);
  f.lease.setReady(); await Promise.resolve();
  assert.deepEqual(observer.read(), [1401877092]);
  assert.equal(f.notifications(), 1);
  f.lease.setReady(); f.lease.renew(); await Promise.resolve();
  assert.equal(f.notifications(), 1, "same final projection cannot trigger a rerender loop");
  f.lease.setNativeHidden(false); await Promise.resolve();
  assert.deepEqual(observer.read(), [1401877092, 999]);
  observer.unsubscribe(); f.adapter.stop();
});
test("QAM reopen replaces array and DOM with same committed lease; first observer frame is projected", async () => {
  const f = fixture(); f.lease.setReady();
  const old = f.observe(f.arrays[0]);
  const replacement = [];
  f.element.ownerDocument = { getElementById: () => ({ getAttribute: () => "false" }) };
  f.setArrays([replacement]);
  // Native QAM render runs its registered afterRender before consumers mount.
  f.lease.setActive(true);
  const reopened = f.observe(replacement);
  assert.deepEqual(reopened.read(), [1401877092]);
  f.fail(); f.lease.renew(); await Promise.resolve();
  assert.deepEqual(reopened.read(), [1401877092, 999], "unhealthy host restores both array and rendered observer");
  assert.deepEqual(old.read(), [1401877092, 999]);
  old.unsubscribe(); reopened.unsubscribe(); f.adapter.stop();
});
test("reactive bridge wraps native tab consumers only and uses immutable subscribed snapshots", () => {
  const f = fixture(); const exports = {};
  const jsx = (type, props, key) => ({ type, props, key });
  const react = { isValidElement: node => !!node?.props,
    cloneElement: (node, props, ...children) => ({ ...node, props: { ...node.props, ...props, ...(children.length ? { children: children[0] } : {}) } }),
    useMemo: fn => fn(), useSyncExternalStore: (_subscribe, read) => read() };
  vm.runInNewContext(compile("deckyHostProjection.tsx"), { exports, require: name =>
    name === "./decky" ? { DFL: {}, SP_REACT: react } : name === "./deckyHostStandalone" ? f.api : name === "./deckyNativeTabPreference" ? preference() : { jsx } });
  const tabs = [{ key: 999 }]; const panel = jsx("NativeTabStrip", { tabs });
  const result = exports.projectDeckyTabViews(jsx("container", { children: [panel, "native text"] }));
  const wrapper = result.props.children[0];
  assert.equal(wrapper.type, exports.DeckyTabProjection);
  assert.equal(wrapper.props.element, panel);
  const rendered = exports.DeckyTabProjection(wrapper.props);
  assert.equal(rendered.type, "NativeTabStrip");
  assert.notEqual(rendered.props.tabs, tabs);
  assert.equal(rendered.props.tabs[0], tabs[0]);
  assert.equal(result.props.children[1], "native text");
  f.adapter.stop();
});
test("projection bridge retries late QAM module once, unpatches idempotently, and stops retained handlers", () => {
  const exports = {}; let module; let installs = 0, removals = 0;
  const handlers = [];
  const DFL = {
    findModuleByExport: () => module,
    findInReactTree: tree => tree,
    createReactTreePatcher: (_steps, handler) => { handlers.push(handler); return handler; },
    afterPatch: () => { installs++; return { unpatch: () => { removals++; } }; },
  };
  vm.runInNewContext(compile("deckyHostProjection.tsx"), { exports, document: { getElementById: () => null }, require: name =>
    name === "./decky" ? { DFL, SP_REACT: {} } : { canShareDeckyTabProjection: () => true } });
  const bridge = exports.installDeckyTabProjection();
  assert.equal(installs, 0);
  module = { browser: { type: function QuickAccessMenuBrowserView() {} }, embedded: { type: function QuickAccessMenuEmbedded() {} } };
  bridge.reconcile(); bridge.reconcile(); assert.equal(installs, 2);
  bridge.stop(); bridge.stop(); bridge.reconcile();
  assert.equal(removals, 2);
  const retainedTree = {};
  for (const handler of handlers) assert.equal(handler([], retainedTree), retainedTree);
  const fresh = exports.installDeckyTabProjection(); assert.equal(installs, 4); fresh.stop();
});

test("installed Steam tab consumer mounts subscription through retained QAM fiber and actual DFL tree patcher", async (t) => {
  const steam = readFileSync(process.env.PLAYHUB_STEAM_QAM_SOURCE || "C:/Program Files (x86)/Steam/steamui/chunk~2dcc5aaf7.js", "utf8");
  const start = steam.indexOf("function at(e){let{tabs:");
  if (start < 0) { t.skip("Installed Steam QAM chunk changed; provide matching PLAYHUB_STEAM_QAM_SOURCE snapshot to run this captured integration adapter"); return; }
  const source = steam.slice(start, steam.indexOf("function ot(", start));
  const jsx = (type, props, key) => ({ type, props, key });
  const native = vm.runInNewContext(`(${source})`, { i: { jsx }, st: "NativeTab", d: { qE: {} },
    M: { Z: "NativeTabList" }, Y: () => ({}), x: { iU: {} } });
  const loadDFL = (path, require = () => ({})) => {
    const exports = {};
    const code = ts.transpileModule(readFileSync(new URL(`../node_modules/@decky/ui/dist/${path}`, import.meta.url), "utf8"), {
      compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
    }).outputText;
    vm.runInNewContext(code, { exports, require, console: { debug() {} } });
    return exports;
  };
  const patcher = loadDFL("utils/patcher.js");
  const treepatcher = loadDFL("utils/react/treepatcher.js", name => name === "../patcher" ? patcher :
    name === "../../logger" ? { default: class {} } : {});
  const find = (node, predicate) => {
    if (!node || typeof node !== "object") return;
    if (predicate(node)) return node;
    for (const child of Array.isArray(node) ? node : [node.props?.children]) {
      const found = find(child, predicate); if (found) return found;
    }
  };
  const f = fixture(); f.lease.setActive(false);
  function NativeQAM() { return jsx("root", { children: jsx(native, { tabs: f.arrays[0], activeTab: 1401877092 }) }); }
  const renderer = { type: function QuickAccessMenuBrowserView() {
    return jsx(NativeQAM, { onFocusNavDeactivated() {} });
  } };
  const original = renderer.type;
  const fiber = { elementType: renderer, type: original, alternate: { type: original } };
  const exports = {}; const subscriptions = [];
  const react = { isValidElement: n => !!n?.props,
    cloneElement: (n, p, ...c) => ({ ...n, props: { ...n.props, ...p, ...(c.length ? { children: c[0] } : {}) } }),
    useMemo: fn => fn(), useSyncExternalStore: (subscribe, read) => { subscriptions.push({ subscribe, read }); return read(); } };
  const DFL = { ...patcher, ...treepatcher, findModuleByExport: () => ({ renderer }),
    getReactRoot: () => fiber, findInReactTree: find };
  vm.runInNewContext(compile("deckyHostProjection.tsx"), { exports, document: { getElementById: () => ({}) },
    require: name => name === "./decky" ? { DFL, SP_REACT: react } : name === "./deckyHostStandalone" ? f.api : name === "./deckyNativeTabPreference" ? preference() : { jsx } });
  const bridge = exports.installDeckyTabProjection();
  assert.notEqual(fiber.type, original);
  assert.equal(fiber.type, renderer.type);
  assert.equal(fiber.alternate.type, renderer.type);
  const render = () => {
    const qam = fiber.type({}); const tree = qam.type(qam.props);
    const wrapped = tree.props.children;
    assert.equal(wrapped.type, exports.DeckyTabProjection, "actual tree patcher must reach component return");
    const element = wrapped.type(wrapped.props);
    return Array.from(element.type(element.props).props.children, n => n.props.tab.key);
  };
  assert.deepEqual(render(), [1401877092, 999]);
  let observed;
  const unsubscribe = subscriptions[0].subscribe(() => { observed = render(); });
  f.lease.setReady(); await Promise.resolve();
  assert.deepEqual(observed, [1401877092]);
  f.setArrays([[]]); f.lease.setActive(true);
  assert.deepEqual(render(), [1401877092], "reopened native consumer uses fresh projected array before input");
  const foreign = () => null; fiber.alternate.type = foreign;
  bridge.stop();
  assert.equal(fiber.type, original);
  assert.equal(fiber.alternate.type, foreign, "foreign fiber updates survive cleanup");
  assert.equal(renderer.type, original);
  unsubscribe(); f.adapter.stop();
});

/** The reported defect: with the preference on, native Decky stayed visible until the
 *  user physically opened Playhub's tab. No lease may be required to honour it. */
test("installed Steam tab consumer drops native Decky on preference alone, with no lease acquired", (t) => {
  const steam = readFileSync(process.env.PLAYHUB_STEAM_QAM_SOURCE || "C:/Program Files (x86)/Steam/steamui/chunk~2dcc5aaf7.js", "utf8");
  const start = steam.indexOf("function at(e){let{tabs:");
  if (start < 0) { t.skip("Installed Steam QAM chunk changed; provide matching PLAYHUB_STEAM_QAM_SOURCE snapshot to run this captured integration adapter"); return; }
  const source = steam.slice(start, steam.indexOf("function ot(", start));
  const jsx = (type, props, key) => ({ type, props, key });
  const native = vm.runInNewContext(`(${source})`, { i: { jsx }, st: "NativeTab", d: { qE: {} },
    M: { Z: "NativeTabList" }, Y: () => ({}), x: { iU: {} } });
  const loadDFL = (path, require = () => ({})) => {
    const exports = {};
    const code = ts.transpileModule(readFileSync(new URL(`../node_modules/@decky/ui/dist/${path}`, import.meta.url), "utf8"), {
      compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
    }).outputText;
    vm.runInNewContext(code, { exports, require, console: { debug() {} } });
    return exports;
  };
  const patcher = loadDFL("utils/patcher.js");
  const treepatcher = loadDFL("utils/react/treepatcher.js", name => name === "../patcher" ? patcher :
    name === "../../logger" ? { default: class {} } : {});
  const find = (node, predicate) => {
    if (!node || typeof node !== "object") return;
    if (predicate(node)) return node;
    for (const child of Array.isArray(node) ? node : [node.props?.children]) {
      const found = find(child, predicate); if (found) return found;
    }
  };
  const f = fixture();
  // The Shortcuts qam-bridge owns hosting here: Playhub holds no lease at all.
  f.lease.release();
  f.arrays[0].push({ key: 1401877092, panel: {} }, { key: 999, panel: {} });
  const pref = preference({ DeckyPluginLoader: { tabsHook: { tabs: [{ id: 1401877092, __shortcutsPlugin: "Playhub" }, { id: 999 }] } } });
  function NativeQAM() { return jsx("root", { children: jsx(native, { tabs: f.arrays[0], activeTab: 1401877092 }) }); }
  const renderer = { type: function QuickAccessMenuBrowserView() {
    return jsx(NativeQAM, { onFocusNavDeactivated() {} });
  } };
  const fiber = { elementType: renderer, type: renderer.type, alternate: { type: renderer.type } };
  const exports = {};
  const react = { isValidElement: n => !!n?.props,
    cloneElement: (n, p, ...c) => ({ ...n, props: { ...n.props, ...p, ...(c.length ? { children: c[0] } : {}) } }),
    useMemo: fn => fn(), useSyncExternalStore: (_subscribe, read) => read() };
  const DFL = { ...patcher, ...treepatcher, findModuleByExport: () => ({ renderer }),
    getReactRoot: () => fiber, findInReactTree: find };
  vm.runInNewContext(compile("deckyHostProjection.tsx"), { exports, document: { getElementById: () => ({}) },
    require: name => name === "./decky" ? { DFL, SP_REACT: react } : name === "./deckyHostStandalone" ? f.api
      : name === "./deckyNativeTabPreference" ? pref : { jsx } });
  const bridge = exports.installDeckyTabProjection();
  const render = () => {
    const qam = fiber.type({}); const tree = qam.type(qam.props);
    const wrapped = tree.props.children;
    const element = wrapped.type(wrapped.props);
    return Array.from(element.type(element.props).props.children, n => n.props.tab.key);
  };
  assert.deepEqual(render(), [1401877092, 999]);
  pref.setNativeDeckyHidden(true);
  assert.deepEqual(render(), [1401877092], "hidden by the preference, without opening Playhub's tab");
  pref.setNativeDeckyHidden(false);
  assert.deepEqual(render(), [1401877092, 999], "turning the preference off restores the native tab");
  bridge.stop(); f.adapter.stop();
});

test("late mounted root attaches once and failed unpatch does not skip owned fiber cleanup", () => {
  const exports = {}; let root; let installs = 0;
  const original = function QuickAccessMenuBrowserView() {};
  const renderer = { type: original };
  const DFL = { findModuleByExport: () => ({ renderer }), getReactRoot: () => root,
    findInReactTree: (node, predicate) => predicate(node) ? node : undefined,
    createReactTreePatcher: () => () => {},
    afterPatch: (object, property) => {
      installs++;
      const replacement = function QuickAccessMenuBrowserView() {};
      object[property] = replacement;
      return { unpatch() { throw Error("foreign owner replaced patch chain"); } };
    } };
  vm.runInNewContext(compile("deckyHostProjection.tsx"), { exports, document: { getElementById: () => ({}) },
    require: name => name === "./decky" ? { DFL, SP_REACT: {} } : { canShareDeckyTabProjection: () => true } });
  const bridge = exports.installDeckyTabProjection();
  assert.equal(installs, 1);
  root = { elementType: renderer, type: original, alternate: { type: original } };
  bridge.reconcile(); bridge.reconcile();
  assert.equal(installs, 1);
  assert.equal(root.type, renderer.type);
  bridge.stop(); bridge.stop();
  assert.equal(root.type, original);
  assert.equal(root.alternate.type, original);
});

test("foreign fiber installed before late attachment is preserved and reports fail-closed conflict", () => {
  const exports = {}; let root; let conflicts = 0;
  const original = function QuickAccessMenuBrowserView() {};
  const renderer = { type: original };
  const DFL = { findModuleByExport: () => ({ renderer }), getReactRoot: () => root,
    findInReactTree: (node, predicate) => predicate(node) ? node : undefined,
    createReactTreePatcher: () => () => {},
    afterPatch: object => { object.type = function QuickAccessMenuBrowserView() {}; return { unpatch() { object.type = original; } }; } };
  vm.runInNewContext(compile("deckyHostProjection.tsx"), { exports, document: { getElementById: () => ({}) },
    require: name => name === "./decky" ? { DFL, SP_REACT: {} } : { canShareDeckyTabProjection: () => true } });
  const bridge = exports.installDeckyTabProjection(() => conflicts++);
  const foreign = () => null;
  root = { elementType: renderer, type: foreign, alternate: { type: original } };
  bridge.reconcile(); bridge.reconcile();
  assert.equal(root.type, foreign);
  assert.equal(root.alternate.type, original);
  assert.equal(conflicts, 1);
  bridge.stop();
  assert.equal(root.type, foreign);
});

test("old mounted subscriber receives new module content and late old cleanup cannot restore over new owner", async () => {
  const host = {}, old = fixture(host);
  const tabs = old.arrays[0];
  old.lease.setReady();
  const store = old.api.getDeckyTabProjection(tabs);
  const content = { type: "new Content" };
  let seen, notices = 0;
  const unsubscribe = store.subscribe(() => { seen = store.getSnapshot(); notices++; });
  const next = fixture(host);
  next.setArrays([tabs]);
  tabs.splice(0, tabs.length, { key: 1401877092, panel: content }, { key: 999, panel: {} });
  next.lease.setReady();
  await Promise.resolve();
  assert.equal(seen[0].panel, content, "subscriber from old evaluated module sees current content identity");
  assert.deepEqual(Array.from(seen, t => t.key), [1401877092]);
  old.adapter.stop(); await Promise.resolve();
  assert.deepEqual(Array.from(tabs, t => t.key), [1401877092], "late old cleanup cannot mutate new owner's raw array");
  assert.equal(store.getSnapshot()[0].panel, content);
  assert.equal(notices, 1);
  next.adapter.stop(); await Promise.resolve();
  assert.deepEqual(Array.from(store.getSnapshot(), t => t.key), [1401877092, 999], "current unload restores native access for retained subscriber");
  assert.equal(notices, 2);
  unsubscribe();
  const registry = host[Symbol.for("playhub.decky-tab-projections.v1")];
  assert.deepEqual(Object.keys(registry).sort(), ["arrays", "focus", "generation", "owners", "version"]);
});

test("subscriptions are individually owned and pending old notification reads newest published snapshot", async () => {
  const host = {}, old = fixture(host), tabs = old.arrays[0];
  const store = old.api.getDeckyTabProjection(tabs);
  let calls = 0;
  const listener = () => calls++;
  const first = store.subscribe(listener), second = store.subscribe(listener);
  old.lease.setReady();
  const next = fixture(host), content = {};
  tabs.splice(0, tabs.length, { key: 1401877092, panel: content });
  next.adapter.afterRender(tabs, false);
  first(); first(); await Promise.resolve();
  assert.equal(calls, 1);
  assert.equal(store.getSnapshot()[0].panel, content);
  second(); old.adapter.stop(); next.adapter.stop();
});

test("new generation focus owner preserves native baseline across late old restore", () => {
  const host = {}, old = fixture(host);
  const attributes = new Map([["tabindex", "0"]]);
  const node = { getAttribute: k => attributes.get(k) ?? null, setAttribute: (k,v) => attributes.set(k,v),
    removeAttribute: k => attributes.delete(k), querySelectorAll: () => [] };
  const doc = { getElementById: id => id === "quickaccess_tab_999" ? node : null };
  const first = old.api.createNativeDeckyFocusSuppression(); first.hide(doc);
  const next = fixture(host), second = next.api.createNativeDeckyFocusSuppression(); second.hide(doc);
  first.restore();
  assert.equal(attributes.get("inert"), "");
  assert.equal(attributes.get("tabindex"), "-1");
  second.restore();
  assert.deepEqual([...attributes], [["tabindex", "0"]]);
  old.adapter.stop(); next.adapter.stop();
});

test("repeated hide preserves a foreign focus baseline changed between writes", () => {
  const f = fixture(), attributes = new Map([["tabindex", "0"]]);
  const node = { getAttribute: k => attributes.get(k) ?? null, setAttribute: (k,v) => attributes.set(k,v),
    removeAttribute: k => attributes.delete(k), querySelectorAll: () => [] };
  const doc = { getElementById: id => id === "quickaccess_tab_999" ? node : null };
  const suppression = f.api.createNativeDeckyFocusSuppression();
  suppression.hide(doc);
  attributes.set("tabindex", "7");
  suppression.hide(doc); suppression.restore();
  assert.deepEqual([...attributes], [["tabindex", "7"]]);
  f.adapter.stop();
});

test("foreign registry remains untouched and bridge fails closed without patching Steam", () => {
  const symbol = Symbol.for("playhub.decky-tab-projections.v1"), foreign = {};
  const host = { [symbol]: foreign }, f = fixture(host), exports = {};
  let conflicts = 0;
  vm.runInNewContext(compile("deckyHostProjection.tsx"), { exports,
    require: name => name === "./decky" ? { DFL: {}, SP_REACT: {} } : name === "./deckyHostStandalone" ? f.api : name === "./deckyNativeTabPreference" ? preference() : {} });
  const bridge = exports.installDeckyTabProjection(() => conflicts++);
  bridge.reconcile(); bridge.stop();
  assert.equal(conflicts, 1);
  assert.equal(host[symbol], foreign);
  f.adapter.stop();
});

test("cross-generation component marker prevents nested projection wrappers", () => {
  const f = fixture(), modules = [];
  const jsx = (type, props) => ({ type, props });
  for (let i = 0; i < 2; i++) {
    const exports = {};
    vm.runInNewContext(compile("deckyHostProjection.tsx"), { exports,
      require: name => name === "./decky" ? { DFL: {}, SP_REACT: { isValidElement: n => !!n?.props } }
        : name === "./deckyHostStandalone" ? f.api : name === "./deckyNativeTabPreference" ? preference() : { jsx } });
    modules.push(exports);
  }
  const oldElement = jsx(modules[0].DeckyTabProjection, { element: jsx("Native", { tabs: [] }) });
  assert.equal(modules[1].projectDeckyTabViews(oldElement), oldElement);
  f.adapter.stop();
});

test("exact deployed legacy local-store consumer migrates once by wrapper key on native render", () => {
  const f = fixture(), exports = {};
  const jsx = (type, props, key) => ({ type, props, key });
  const react = { isValidElement: n => !!n?.props,
    cloneElement: (n,p) => ({...n, props: {...n.props,...p}}),
    useMemo: fn => fn(), useSyncExternalStore: (_subscribe, read) => read() };
  const deployed = `function DeckyTabProjection({ element }) {
    const store = _global_SP_REACT.useMemo(() => getDeckyTabProjection(element.props.tabs), [element.props.tabs]);
    const tabs = _global_SP_REACT.useSyncExternalStore(store.subscribe, store.getSnapshot, store.getSnapshot);
    return _global_SP_REACT.cloneElement(element, { tabs });
}`;
  const tabs = [{key:1401877092,panel:"old"}], oldSnapshot = [...tabs];
  const legacy = vm.runInNewContext(`(${deployed})`, { _global_SP_REACT:react,
    getDeckyTabProjection: () => ({subscribe() {},getSnapshot:()=>oldSnapshot}) });
  const oldElement = jsx(legacy, {element:jsx("NativeTabContents", {tabs})}, "null");
  tabs[0] = {key:1401877092,panel:"new"};
  assert.equal(legacy(oldElement.props).props.tabs[0].panel, "old");
  vm.runInNewContext(compile("deckyHostProjection.tsx"), { exports, require: name =>
    name === "./decky" ? { DFL:{}, SP_REACT:react } : name === "./deckyHostStandalone" ? f.api : name === "./deckyNativeTabPreference" ? preference() : {jsx} });
  const migrated = exports.projectDeckyTabViews(oldElement);
  assert.notEqual(migrated.key, oldElement.key, "next native render must replace old keyed fiber");
  assert.notEqual(migrated.type, legacy);
  assert.equal(migrated.type(migrated.props).props.tabs[0].panel, "new");
  const nextExports = {};
  vm.runInNewContext(compile("deckyHostProjection.tsx"), { exports:nextExports, require:name =>
    name === "./decky" ? { DFL:{}, SP_REACT:react } : name === "./deckyHostStandalone" ? f.api : name === "./deckyNativeTabPreference" ? preference() : {jsx} });
  assert.equal(nextExports.projectDeckyTabViews(migrated), migrated, "shared wrapper is not remounted again");
  const foreign = jsx(function DeckyTabProjection() {}, {element:oldElement.props.element}, "foreign");
  let conflicts = 0;
  assert.equal(exports.projectDeckyTabViews(foreign, undefined, () => conflicts++), foreign, "same function name is not ownership proof");
  assert.equal(conflicts, 1, "unknown similar wrapper keeps native fallback");
  f.adapter.stop();
});

/** Reported by a tester: "sometimes opening the QAM menu will crash steam overlay and
 *  steam itself". Steam renders the QAM with no error boundary above the tab hook, so
 *  anything we throw from inside that render takes the whole overlay down. Nothing we
 *  own may escape into Steam's render stack. */
test("render fault leaves Steam untouched and latches passthrough until reinstallation", async () => {
  const exports = {}; let module; const handlers = [];
  const DFL = {
    findModuleByExport: () => module,
    findInReactTree: tree => tree,
    createReactTreePatcher: (_steps, handler) => { handlers.push(handler); return handler; },
    afterPatch: () => ({ unpatch() {} }),
  };
  let conflicts = 0;
  vm.runInNewContext(compile("deckyHostProjection.tsx"), { exports, document: { getElementById: () => null },
    Promise, require: name => name === "./decky" ? { DFL, SP_REACT: {} } : { canShareDeckyTabProjection: () => true } });
  module = { browser: { type: function QuickAccessMenuBrowserView() {} } };
  const bridge = exports.installDeckyTabProjection(() => { conflicts++; });
  bridge.reconcile();
  assert.equal(handlers.length, 1);
  // A tree the projection walker cannot handle: the getter throws mid-walk.
  const tree = { get props() { throw new Error("steam_tree_shape_changed"); } };
  let result;
  assert.doesNotThrow(() => { result = handlers[0]([], tree); });
  assert.equal(result, tree, "Steam must get its own tree back, unmodified");
  assert.equal(conflicts, 0, "host state cannot be published inside Steam render");
  await Promise.resolve();
  assert.equal(conflicts, 1);
  let reads = 0;
  const nextTree = { get props() { reads++; throw new Error("must not walk again"); } };
  assert.equal(handlers[0]([], nextTree), nextTree);
  assert.equal(reads, 0, "a latched fault must skip subsequent tree walks");
  await Promise.resolve();
  assert.equal(conflicts, 1, "report the fault only once");
  bridge.stop();
});

test("foreign projection conflict releases the host only after Steam render returns", async () => {
  const exports = {}, handlers = [];
  const nativeModule = { browser: { type: function QuickAccessMenuBrowserView() {} } };
  const DFL = { findModuleByExport: () => nativeModule, findInReactTree: tree => tree,
    createReactTreePatcher: (_steps, handler) => { handlers.push(handler); return handler; },
    afterPatch: () => ({ unpatch() {} }) };
  const react = { isValidElement: node => !!node?.props };
  vm.runInNewContext(compile("deckyHostProjection.tsx"), { exports, Promise,
    document: { getElementById: () => null }, require: name => name === "./decky"
      ? { DFL, SP_REACT: react } : { canShareDeckyTabProjection: () => true } });
  let conflicts = 0;
  const bridge = exports.installDeckyTabProjection(() => conflicts++);
  const foreign = { type: function DeckyTabProjection() {}, props: {
    element: { type: "Native", props: { tabs: [] } } } };
  assert.equal(handlers[0]([], foreign), foreign);
  assert.equal(conflicts, 0);
  await Promise.resolve();
  assert.equal(conflicts, 1);
  assert.equal(exports.isDeckyTabProjectionDisabled(), true);
  bridge.stop();
});

test("render burst guard disables the projection instead of looping Steam's renderer", () => {
  const f = fixture(); const exports = {};
  const jsx = (type, props, key) => ({ type, props, key });
  const react = { isValidElement: node => !!node?.props,
    cloneElement: (node, props) => ({ ...node, props: { ...node.props, ...props } }),
    useMemo: fn => fn(), useSyncExternalStore: (_subscribe, read) => read() };
  vm.runInNewContext(compile("deckyHostProjection.tsx"), { exports, Date, Promise, require: name =>
    name === "./decky" ? { DFL: {}, SP_REACT: react } : name === "./deckyHostStandalone" ? f.api : name === "./deckyNativeTabPreference" ? preference() : { jsx } });
  const tabs = [{ key: 999 }];
  const element = jsx("NativeTabStrip", { tabs });
  assert.equal(exports.isDeckyTabProjectionDisabled(), false);
  let rendered;
  for (let i = 0; i < 200; i++) rendered = exports.DeckyTabProjection({ element });
  assert.equal(exports.isDeckyTabProjectionDisabled(), true, "a runaway render loop must trip the guard");
  assert.equal(rendered, element, "once disabled, Steam's own element passes through untouched");
  exports.resetDeckyTabProjectionGuard();
  assert.equal(exports.isDeckyTabProjectionDisabled(), false);
  f.adapter.stop();
});

/** useSyncExternalStore compares subscribe by identity: a fresh closure per render made
 *  React tear the subscription down and rebuild it on every commit. */
test("one tab array yields one store object, so subscribe identity is stable across renders", () => {
  const f = fixture();
  const tabs = f.arrays[0];
  const first = f.api.getDeckyTabProjection(tabs);
  const second = f.api.getDeckyTabProjection(tabs);
  assert.equal(first, second);
  assert.equal(first.subscribe, second.subscribe);
  assert.equal(first.getSnapshot, second.getSnapshot);
  assert.notEqual(f.api.getDeckyTabProjection([]), first, "a different array keeps its own store");
  const seen = [];
  const stopA = first.subscribe(() => seen.push("a"));
  const stopB = second.subscribe(() => seen.push("b"));
  stopA();
  assert.equal(seen.length, 0);
  stopB();
  f.adapter.stop();
});

test("retained Decky sibling wrapper is preserved while projection attaches and cleans up", () => {
  const exports = {}; let conflicts = 0, calls = 0;
  const original = function QuickAccessMenuBrowserView() { calls++; };
  const sibling = function retainedDeckyWrapper() { return original(); };
  sibling.__deckyOrig = original;
  const renderer = { type: original };
  const root = { elementType: renderer, type: sibling, alternate: { type: sibling } };
  const DFL = { findModuleByExport: () => ({ renderer }), getReactRoot: () => root,
    findInReactTree: (node, predicate) => predicate(node) ? node : undefined,
    createReactTreePatcher: () => () => {},
    afterPatch: (object, property) => {
      const before = object[property];
      object[property] = function attached() { return before(); };
      object[property].__deckyOrig = before;
      return { unpatch() { object[property] = before; } };
    } };
  vm.runInNewContext(compile("deckyHostProjection.tsx"), { exports, document: { getElementById: () => ({}) },
    require: name => name === "./decky" ? { DFL, SP_REACT: {} } : { canShareDeckyTabProjection: () => true } });
  const bridge = exports.installDeckyTabProjection(() => conflicts++);
  assert.equal(conflicts, 0);
  assert.equal(root.type.__deckyOrig, sibling);
  root.type(); assert.equal(calls, 1);
  bridge.stop(); assert.equal(root.type, sibling); assert.equal(root.alternate.type, sibling);
});

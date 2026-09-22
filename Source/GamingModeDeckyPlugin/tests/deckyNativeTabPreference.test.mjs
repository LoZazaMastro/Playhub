import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";

const compile = file => ts.transpileModule(readFileSync(new URL(`../src/${file}`, import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX }
}).outputText;
function load(file, context = {}) {
  const exports = {};
  vm.runInNewContext(compile(file), { exports, ...context });
  return exports;
}
const PLAYHUB = 0x50484B;
const hookWindow = (tabs) => ({ DeckyPluginLoader: { tabsHook: { tabs } } });

test("suppression is preference-driven, notifies once per change and fails open without Playhub's tab", () => {
  const window = hookWindow([{ id: PLAYHUB, __shortcutsPlugin: "Playhub" }, { id: 999 }]);
  const api = load("deckyNativeTabPreference.ts", { window });
  const tabs = [{ key: PLAYHUB }, { key: 999 }, { key: 7 }];

  assert.equal(api.isNativeDeckyHidden(), false);
  assert.equal(api.filterNativeDeckyTabs(tabs), tabs, "an untouched projection keeps its identity");

  let notifications = 0;
  const unsubscribe = api.subscribeNativeDeckyHidden(() => { notifications++; });
  assert.equal(api.setNativeDeckyHidden(true), true);
  assert.equal(api.setNativeDeckyHidden(true), false, "no redundant notification");
  assert.equal(notifications, 1);

  assert.deepEqual(api.filterNativeDeckyTabs(tabs).map(tab => tab.key), [PLAYHUB, 7]);
  assert.deepEqual(tabs.map(tab => tab.key), [PLAYHUB, 999, 7], "the owner's array is never mutated");

  const orphan = [{ key: 999 }, { key: 7 }];
  assert.equal(api.filterNativeDeckyTabs(orphan), orphan, "Decky stays reachable without Playhub's tab");

  api.setNativeDeckyHidden(false);
  assert.equal(notifications, 2);
  assert.deepEqual(api.filterNativeDeckyTabs(tabs).map(tab => tab.key), [PLAYHUB, 999, 7]);
  unsubscribe();
  api.setNativeDeckyHidden(true);
  assert.equal(notifications, 2);
});

test("a renumbered Playhub tab from the current owner is honoured", () => {
  const window = hookWindow([{ id: 4242, __shortcutsPlugin: "Playhub" }, { id: 999 }]);
  const api = load("deckyNativeTabPreference.ts", { window });
  api.setNativeDeckyHidden(true);
  assert.equal(api.currentPlayhubTabId(), 4242);
  assert.deepEqual(api.filterNativeDeckyTabs([{ key: 4242 }, { key: 999 }]).map(tab => tab.key), [4242]);
  assert.deepEqual(api.filterNativeDeckyTabs([{ key: PLAYHUB }, { key: 999 }]).map(tab => tab.key), [PLAYHUB, 999],
    "a stale identifier must not strand Decky");
  window.DeckyPluginLoader = null;
  assert.equal(api.currentPlayhubTabId(), PLAYHUB, "no owner falls back to Playhub's own identifier");
});

/** The Shortcuts qam-bridge owns rendering; Playhub only owns this projection component. */
test("the projection component hides the native tab for any QAM owner, with no lease involved", () => {
  const window = hookWindow([{ id: PLAYHUB, __shortcutsPlugin: "Playhub" }, { id: 999 }]);
  const preference = load("deckyNativeTabPreference.ts", { window });
  const hooks = [];
  let index = 0;
  const react = {
    useMemo(factory, deps) {
      const slot = hooks[index] ??= {};
      if (!slot.deps || slot.deps.length !== deps.length || slot.deps.some((d, i) => d !== deps[i])) {
        slot.deps = deps; slot.value = factory();
      }
      index++; return slot.value;
    },
    useSyncExternalStore(_subscribe, getSnapshot) { index++; return getSnapshot(); },
    cloneElement: (element, props) => ({ ...element, props: { ...element.props, ...props } }),
    isValidElement: () => true,
  };
  const projection = load("deckyHostProjection.tsx", { window, require: name =>
    name === "./decky" ? { DFL: {}, SP_REACT: react } :
    name === "./deckyHostStandalone" ? { canShareDeckyTabProjection: () => true,
      getDeckyTabProjection: tabs => ({ subscribe: () => () => {}, getSnapshot: () => tabs }) } :
    name === "./deckyNativeTabPreference" ? preference : {} });

  const tabs = [{ key: PLAYHUB }, { key: 999 }, { key: 7 }];
  const element = { props: { tabs } };
  const render = () => { index = 0; return projection.DeckyTabProjection({ element }); };

  assert.deepEqual(render().props.tabs.map(tab => tab.key), [PLAYHUB, 999, 7]);
  preference.setNativeDeckyHidden(true);
  assert.deepEqual(render().props.tabs.map(tab => tab.key), [PLAYHUB, 7],
    "the preference alone hides it, without entering Playhub's tab");
  preference.setNativeDeckyHidden(false);
  assert.deepEqual(render().props.tabs.map(tab => tab.key), [PLAYHUB, 999, 7]);
  assert.deepEqual(tabs.map(tab => tab.key), [PLAYHUB, 999, 7]);
});

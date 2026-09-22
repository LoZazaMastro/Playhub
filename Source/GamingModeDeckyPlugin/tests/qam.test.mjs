import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";

const compile = (file) => ts.transpileModule(readFileSync(new URL(`../src/${file}`, import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText;
const compiled = compile("qam.ts");
const iconCode = compile("qamIcons.ts");
class Hook {
  tabs = [{ id: 999, content: {} }];
  render(tabs, visible) {
    if (tabs.filter((tab) => tab.decky).length === this.tabs.length) {
      for (const tab of tabs) if (tab.decky) tab.initialVisibility = visible;
      return;
    }
    for (const tab of this.tabs) tabs.push({ key: tab.id, decky: true, panel: tab.content, initialVisibility: visible });
  }
}
function fixture(values = new Map()) {
  const exports = {};
  const host = new EventTarget();
  host.__TABS_HOOK_INSTANCE = new Hook();
  const context = { exports, window: host, WeakRef, localStorage: {
    getItem: (key) => values.get(key) ?? null, setItem: (key, value) => values.set(key, value)
  }, setInterval: () => 1, clearInterval: () => {}, require: (name) => {
    if (name === "./qamTabVisibility") return {createQamTabVisibility: () => ({update(){},stop(){}})};
    if (name === "./qamLayoutRuntime") {
      const layout = {}; const icons = {};
      vm.runInNewContext(iconCode, { exports: icons, require: () => ({ createElement: (type, props) => ({ type, props }) }) });
      vm.runInNewContext(compile("qamLayoutRuntime.ts"), { exports: layout, window: host, WeakRef, require: () => icons });
      return layout;
    }
    const standalone = {};
    vm.runInNewContext(compile("deckyHostStandalone.ts"), { exports: standalone, WeakRef, setInterval: () => 1, clearInterval: () => {} });
    return standalone;
  } };
  vm.runInNewContext(compiled, context);
  return { api: exports, host, hook: host.__TABS_HOOK_INSTANCE, values };
}
const options = { content: {}, icon: {} };
test("cold native registry and QAM DOM defer suppression until ready, without duplicate projection", () => {
  const api = {};
  vm.runInNewContext(compile("deckyHostStandalone.ts"), { exports: api, WeakRef, setInterval: () => 1, clearInterval() {} });
  const hook = new Hook(); const entry = hook.tabs.pop();
  const id = 1401877092; hook.tabs.push({ id, content: {} });
  const tabs = []; let renders = 0;
  const adapter = api.createStandaloneDeckyHost(hook, Hook.prototype.render, id, () => true, () => {
    renders++; adapter.beforeRender(tabs); hook.render(tabs, true); adapter.afterRender(tabs, true);
  });
  assert.equal(adapter.capability.isAvailable(), false);
  hook.tabs.push(entry);
  assert.equal(adapter.capability.isAvailable(), true);
  let mounted = false, selected = true; const releases = [];
  const attributes = new Map([["tabindex", "0"], ["aria-hidden", "false"]]);
  const nativeButton = { getAttribute: name => name === "aria-selected" ? String(selected) : attributes.get(name) ?? null,
    setAttribute: (name, value) => attributes.set(name, value), removeAttribute: name => attributes.delete(name), querySelectorAll: () => [] };
  const lease = adapter.capability.acquire({ element: { isConnected: true,
    closest: () => mounted ? { id: `quickaccess_content_${id}` } : null,
    ownerDocument: { getElementById: key => key === "quickaccess_tab_999"
      ? nativeButton : mounted ? {} : null },
  }, createContent: root => root, isHealthy: () => true, onRelease: reason => releases.push(reason), hideNative: true });
  assert.ok(lease); assert.equal(lease.setReady(), true);
  const native = tabs.find(tab => tab.key === 999);
  assert.ok(native); assert.deepEqual(releases, []);
  mounted = true; lease.renew();
  assert.ok(tabs.includes(native), "selected native tab remains safe");
  selected = false; lease.renew();
  assert.equal(tabs.includes(native), false);
  assert.equal(attributes.get("inert"), "");
  assert.equal(attributes.get("tabindex"), "-1");
  const stableRenders = renders;
  for (let i = 0; i < 5; i++) lease.renew();
  assert.equal(renders, stableRenders, "successful suppression has no polling writes");
  lease.setNativeHidden(false);
  assert.deepEqual(Object.fromEntries(attributes), { tabindex: "0", "aria-hidden": "false" });
  for (let i = 0; i < 5; i++) lease.renew();
  assert.equal(tabs.filter(tab => tab.key === 999).length, 1);
  assert.equal(tabs.find(tab => tab.key === 999), native);
  assert.equal(hook.tabs.find(tab => tab.id === 999), entry);
  adapter.stop();
});
test("older Shortcuts keeps ownership while Playhub hosts and hides Decky", () => {
  const api = {};
  vm.runInNewContext(compile("deckyHostStandalone.ts"), { exports: api, WeakRef, setInterval: () => 1, clearInterval: () => {} });
  const hook = new Hook();
  const id = 1401877092;
  hook.tabs.unshift({ id, __shortcutsPlugin: "Playhub", content: {} });
  const previous = hook.render;
  const sharedKey = Symbol.for("panel-de-control.qam-render-adapter");
  const layoutKey = Symbol.for("shortcuts.qam-tab-layout-adapter");
  const state = { protocol: 2, hook, observedArrays: [], arrayStates: new WeakMap() };
  const layout = { state, hook, previous };
  const reconcileTabLayout = () => {
    assert.equal(hook.render, state.wrapper, "Shortcuts render owner stays valid");
    assert.equal(state.wrapper, layout.wrapper, "Shortcuts layout owner stays valid");
  };
  hook.render = function(tabs, visible) { const result = previous.call(this, tabs, visible); reconcileTabLayout(); return result; };
  state.wrapper = layout.wrapper = hook.render;
  state[layoutKey] = layout;
  hook[sharedKey] = state;
  const shortcutsRender = hook.render;
  const adapter = api.createLegacyShortcutsDeckyHost(hook, { protocol: 1, register() {}, getVisible: () => true });
  assert.ok(adapter);
  const tabs = [{ key: 1 }]; hook.render(tabs, true);
  const originalDecky = tabs.find(tab => tab.key === 999);
  const lease = adapter.capability.acquire({ element: { isConnected: true,
    closest: () => ({ id: `quickaccess_content_${id}` }), ownerDocument: { getElementById: () => ({ getAttribute: () => "false" }) } },
    createContent: root => ({ native: root }), isHealthy: () => true, onRelease() {} });
  assert.ok(lease);
  lease.setReady();
  assert.equal(tabs.some(tab => tab.key === 999), false);
  assert.equal(hook.tabs.some(tab => tab.id === 999), true, "native registry is never deleted");
  lease.setNativeHidden(false);
  for (let i = 0; i < 20; i++) hook.render(tabs, true);
  assert.equal(tabs.filter(tab => tab.key === 999).length, 1);
  assert.equal(tabs.find(tab => tab.key === 999), originalDecky);
  adapter.stop();
  assert.equal(hook.render, shortcutsRender);
  assert.equal(state.wrapper, shortcutsRender);
  assert.equal(layout.wrapper, shortcutsRender);
  assert.equal(Object.getOwnPropertyDescriptor(state, "wrapper").get, undefined);
  assert.equal(tabs.find(tab => tab.key === 999), originalDecky);
  assert.equal(api.createLegacyShortcutsDeckyHost(hook, { protocol: 1, register() {}, deckyHost: {} }), null);
});
test("standalone first tab, identity, hide/show, cleanup and restart cache", async () => {
  const f = fixture();
  const original = f.hook.render;
  const stop = f.api.initPlayhubQam(options);
  const native = { key: 4 };
  const tabs = [native];
  f.hook.render(tabs, true);
  const own = tabs[0];
  assert.equal(own.key, 0x50484B);
  for (let i = 0; i < 5; i++) f.hook.render(tabs, true);
  assert.equal(tabs.length, 3);
  assert.equal(tabs[0], own);
  assert.equal(tabs[1], native);
  const decky = tabs[2];
  await f.api.setPlayhubQamVisible(false);
  assert.deepEqual(tabs, [native, decky]);
  await f.api.setPlayhubQamVisible(true);
  assert.equal(tabs[0], own);
  await f.api.setPlayhubQamVisible(false);
  stop();
  assert.equal(f.hook.render, original);
  const restart = fixture(f.values);
  const stopRestart = restart.api.initPlayhubQam(options);
  const next = [{ key: 4 }]; restart.hook.render(next, true);
  assert.equal(next.length, 2);
  assert.equal(restart.api.getPlayhubQamState().visible, false);
  stopRestart();
});
test("bridge takes ownership synchronously, preferences win, and toggle delegates", async () => {
  const f = fixture();
  const original = f.hook.render;
  const stop = f.api.initPlayhubQam(options);
  const tabs = [{ key: 4 }]; f.hook.render(tabs, true);
  let visible = false;
  let notify;
  let released = false;
  f.host[Symbol.for("shortcuts.qam-bridge.v1")] = {
    protocol: 1,
    register: async (entry) => { assert.equal(entry.name, "Playhub"); return () => { released = true; }; },
    getVisible: () => visible,
    setVisible: async (_name, value) => { visible = value; notify?.(); },
    subscribe: (listener) => { notify = listener; return () => { notify = undefined; }; }
  };
  f.host.dispatchEvent(new Event("shortcuts:qam-bridge-changed"));
  assert.equal(f.hook.render, original);
  assert.equal(tabs.length, 2);
  await new Promise((resolve) => setImmediate(resolve));
  assert.equal(f.api.getPlayhubQamState().visible, false);
  await f.api.setPlayhubQamVisible(true);
  assert.equal(visible, true);
  visible = false; notify();
  assert.equal(f.api.getPlayhubQamState().visible, false);
  stop(); assert.equal(released, true);
});
test("unknown renderer and older Shortcuts are not wrapped", () => {
  const f = fixture();
  f.hook.render = () => {};
  assert.equal(f.api.installStandaloneQam(f.hook, options, () => true), null);
  const other = fixture();
  other.host.DeckyPluginLoader = { deckyState: { publicState: () => ({ installedPlugins: [{ name: "Shortcuts" }] }) } };
  const stop = other.api.initPlayhubQam(options);
  assert.equal(Object.hasOwn(other.hook, "render"), false);
  assert.equal(other.api.getPlayhubQamState().available, false);
  stop();
});
for (const saved of [true, false]) {
  test(`boot with Shortcuts disabled preserves Playhub preference ${saved}`, () => {
    const f = fixture(new Map([["playhub.qam.visible.v1", String(saved)]]));
    f.host.DeckyPluginLoader = { deckyState: { publicState: () => ({
      plugins: [{ name: "Playhub" }],
      installedPlugins: [{ name: "Shortcuts", version: "1.2.1" }],
      disabledPlugins: [{ name: "Shortcuts", version: "1.2.1" }]
    }) } };
    const original = f.hook.render;
    const stop = f.api.initPlayhubQam(options);
    const tabs = []; f.hook.render(tabs, true);
    assert.equal(f.api.getPlayhubQamState().available, true);
    assert.equal(f.api.getPlayhubQamState().visible, saved);
    assert.equal(tabs.some((tab) => tab.key === 0x50484B), saved);
    assert.equal(tabs.filter((tab) => tab.key === 999).length, 1);
    stop(); assert.equal(f.hook.render, original);
  });
  for (const staleBridge of [false, true]) {
    test(`disabling Shortcuts restores standalone, saved=${saved}, stale bridge=${staleBridge}`, async () => {
      const f = fixture();
      const inventory = { plugins: [{ name: "Shortcuts" }], installedPlugins: [{ name: "Shortcuts" }], disabledPlugins: [] };
      f.host.DeckyPluginLoader = { deckyState: { publicState: () => inventory } };
      let released = 0;
      let unsubscribed = 0;
      const symbol = Symbol.for("shortcuts.qam-bridge.v1");
      f.host[symbol] = {
        protocol: 1,
        register: async () => () => { released++; },
        getVisible: () => saved,
        subscribe: () => () => { unsubscribed++; }
      };
      const original = f.hook.render;
      const stop = f.api.initPlayhubQam(options);
      await new Promise((resolve) => setImmediate(resolve));
      assert.equal(f.values.get("playhub.qam.visible.v1"), String(saved));
      inventory.plugins = [{ name: "Playhub" }];
      inventory.disabledPlugins = [{ name: "Shortcuts", version: "1.2.1" }];
      if (!staleBridge) delete f.host[symbol];
      f.host.dispatchEvent(new Event("shortcuts:qam-bridge-changed"));
      assert.equal(released, 1);
      assert.equal(unsubscribed, 1);
      assert.equal(f.api.getPlayhubQamState().available, true);
      const tabs = []; f.hook.render(tabs, true);
      for (let i = 0; i < 3; i++) f.hook.render(tabs, true);
      assert.equal(tabs.filter((tab) => tab.key === 0x50484B).length, saved ? 1 : 0);
      assert.equal(tabs.filter((tab) => tab.key === 999).length, 1);
      stop(); assert.equal(f.hook.render, original);
    });
  }
}

test("all twelve locales have dedicated labels", () => {
  const base = {};
  vm.runInNewContext(compile("pluginStoreLocale.ts"), { exports: base });
  const copy = {};
  vm.runInNewContext(compile("qamLocale.ts"), { exports: copy, require: () => base });
  const locales = ["en", "it", "es", "fr", "de", "pt", "uk", "zh", "ja", "ko", "hi", "ru"];
  const labels = locales.map(copy.getPlayhubQamLabel);
  assert.equal(new Set(labels).size, 12);
  assert.ok(labels.every((label) => label.includes("Playhub")));
});

test("standalone host preserves native root, hides only after commit, survives inactive tab and restores", () => {
  const f = fixture();
  const nativeRender = f.hook.render;
  const stop = f.api.initPlayhubQam(options);
  const wrapper = f.hook.render;
  const tabs = [{ key: 4 }]; f.hook.render(tabs, true);
  const nativeTab = tabs.find(tab => tab.key === 999);
  const capability = f.host[Symbol.for("playhub.decky-host-standalone.v1")];
  assert.equal(capability.isAvailable(), true);
  let selected = false;
  let visible;
  const releases = [];
  const lease = capability.acquire({
    element: { isConnected: true, closest: () => ({ id: "quickaccess_content_5261387" }),
      ownerDocument: { getElementById: id => id === "quickaccess_tab_999"
        ? { getAttribute: () => String(selected) } : {} } },
    createContent: root => { assert.equal(root, f.hook.tabs[0].content); return { isolated: root }; },
    isHealthy: () => true, onVisibility: value => { visible = value; }, onRelease: reason => releases.push(reason)
  });
  assert.ok(lease);
  assert.notEqual(lease.panel, nativeTab.panel);
  assert.equal(f.hook.render, wrapper, "host must not install another wrapper");
  assert.equal(lease.setActive(true), true);
  assert.ok(tabs.includes(nativeTab), "no suppression before commit");
  assert.equal(lease.setReady(), true);
  assert.equal(tabs.includes(nativeTab), false);
  assert.equal(visible, true);
  lease.setNativeHidden(false);
  for (let i = 0; i < 5; i++) f.hook.render(tabs, true);
  assert.equal(tabs.filter(tab => tab.key === 999).length, 1);
  assert.equal(tabs.find(tab => tab.key === 999), nativeTab);
  assert.equal(lease.renew(), true);
  assert.equal(releases.length, 0);
  lease.setNativeHidden(true);
  assert.equal(tabs.includes(nativeTab), false);
  lease.setActive(false);
  assert.equal(visible, false);
  assert.equal(tabs.includes(nativeTab), false, "Decky remains hidden on another internal tab");
  for (let i = 0; i < 4; i++) f.hook.render(tabs, true);
  assert.deepEqual(tabs.map(tab => tab.key), [0x50484B, 4]);
  selected = true; f.hook.render(tabs, true);
  assert.ok(tabs.includes(nativeTab), "do not suppress a currently selected original tab");
  selected = false; f.hook.render(tabs, true);
  assert.equal(tabs.includes(nativeTab), false);
  lease.release(); lease.release();
  assert.equal(tabs.filter(tab => tab.key === 999).length, 1);
  assert.equal(tabs.find(tab => tab.key === 999), nativeTab);
  assert.equal(releases.length, 1);
  assert.deepEqual(f.hook.tabs.map(tab => tab.id), [999]);
  stop(); assert.equal(f.hook.render, nativeRender);
  assert.equal(f.host[Symbol.for("playhub.decky-host-standalone.v1")], undefined);
});

for (const cause of ["hidden", "health", "root", "unload", "bridge", "render_error"]) {
  test(`standalone host restores original Decky on ${cause}`, async () => {
    const f = fixture();
    const stop = f.api.initPlayhubQam(options);
    const tabs = []; f.hook.render(tabs, true);
    const nativeTab = tabs.find(tab => tab.key === 999);
    let healthy = true;
    let releases = 0;
    const capability = f.host[Symbol.for("playhub.decky-host-standalone.v1")];
    const lease = capability.acquire({
      element: { isConnected: true, closest: () => ({ id: `quickaccess_content_${0x50484B}` }),
        ownerDocument: { getElementById: () => ({ getAttribute: () => "false" }) } },
      createContent: root => root, isHealthy: () => healthy, onRelease: () => { releases++; }
    });
    lease.setReady(); assert.equal(tabs.includes(nativeTab), false);
    if (cause === "hidden") await f.api.setPlayhubQamVisible(false);
    if (cause === "health") { healthy = false; f.hook.render(tabs, true); }
    if (cause === "root") { f.hook.tabs[0].content = {}; f.hook.render(tabs, true); }
    if (cause === "unload") stop();
    if (cause === "bridge") {
      f.host[Symbol.for("shortcuts.qam-bridge.v1")] = { protocol: 1,
        register: async () => () => {}, getVisible: () => true, subscribe: () => () => {} };
      f.host.dispatchEvent(new Event("shortcuts:qam-bridge-changed"));
      await new Promise(resolve => setImmediate(resolve));
    }
    if (cause === "render_error") {
      Object.defineProperty(f.hook, "tabs", { get() { throw new Error("native_failure"); } });
      assert.throws(() => f.hook.render(tabs, true), /native_failure/);
    }
    assert.equal(tabs.filter(tab => tab.key === 999).length, 1);
    assert.equal(tabs.find(tab => tab.key === 999), nativeTab);
    assert.equal(releases, 1);
    stop(); assert.equal(releases, 1);
  });
}

/** Steam's QAM render has no error boundary above this hook, so a fault of ours would
 *  crash the overlay and Steam with it. Detach and keep rendering Decky instead. */
test("a fault in our own render work detaches the hook instead of crashing Steam", () => {
  const f = fixture();
  const stop = f.api.initPlayhubQam(options);
  const wrapper = f.hook.render;
  assert.notEqual(wrapper, Hook.prototype.render);
  const tabs = [];
  f.hook.render(tabs, true);
  assert.ok(tabs.some((tab) => String(tab.key) === String(0x50484B)), "Playhub tab is projected while healthy");
  // A tab object Steam owns whose key cannot be read: only our own pass touches it.
  const poisoned = [];
  poisoned.push({ get key() { throw new Error("playhub_projection_fault"); } });
  let result;
  assert.doesNotThrow(() => { result = f.hook.render(poisoned, true); });
  assert.equal(result, undefined);
  assert.equal(f.hook.render, Hook.prototype.render, "the wrapper uninstalls itself after its own fault");
  assert.ok(poisoned.some((tab) => tab.decky === true), "Decky's own tabs still rendered");
  const after = [];
  assert.doesNotThrow(() => f.hook.render(after, true));
  assert.equal(after.some((tab) => String(tab.key) === String(0x50484B)), false);
  stop();
});





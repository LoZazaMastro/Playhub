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
  vm.runInNewContext(compile(file), { exports, EventTarget, Event, ...context });
  return exports;
}
function realState() {
  const calls = [];
  const real = { eventBus: new EventTarget(),
    snapshot: { plugins: [{ name: "Playhub" }, { name: "Artwork" }], activePlugin: { name: "Playhub" },
      hiddenPlugins: [], pluginOrder: ["Artwork", "Playhub"], installedPlugins: [], disabledPlugins: [] },
    publicState() { return this.snapshot; },
  };
  for (const name of ["setVersionInfo", "setIsLoaderUpdating", "setPluginOrder", "setDisabledPlugins"]) {
    real[name] = function(value) { assert.equal(this, real); calls.push([name, value]); };
  }
  return { real, calls };
}
const stateApi = load("deckyHostState.ts");
test("native state is locally navigated, updated, filtered and disposed", () => {
  const { real, calls } = realState();
  const original = JSON.stringify(real.snapshot);
  const { state, dispose, setVisible } = stateApi.createScopedDeckyState(real);
  assert.deepEqual(Array.from(state.publicState().plugins, p => p.name), ["Artwork"]);
  assert.equal(state.publicState().activePlugin, null);
  state.setActivePlugin("Artwork");
  assert.equal(state.publicState().activePlugin.name, "Artwork");
  setVisible(false);
  assert.equal(state.publicState().activePlugin, null, "inactive host cannot render an alwaysRender plugin");
  setVisible(true);
  assert.equal(state.publicState().activePlugin.name, "Artwork", "local selection survives horizontal navigation");
  state.setActivePlugin("Playhub");
  assert.equal(state.publicState().activePlugin, null);
  assert.equal(JSON.stringify(real.snapshot), original);
  let updates = 0;
  state.eventBus.addEventListener("update", () => updates++);
  real.eventBus.dispatchEvent(new Event("update"));
  assert.equal(updates, 1);
  state.setPluginOrder(["Artwork"]);
  assert.equal(calls[0][0], "setPluginOrder");
  state.setActivePlugin("Artwork");
  real.snapshot.plugins = [];
  assert.equal(state.publicState().activePlugin, null, "unloaded plugins cannot remain active");
  dispose(); dispose();
  const count = updates;
  real.eventBus.dispatchEvent(new Event("update"));
  assert.equal(updates, count);
});
test("unsupported native state is rejected before subscribing", () => {
  assert.throws(() => stateApi.createScopedDeckyState({}), /unsupported_decky_state/);
  const { real } = realState();
  real.snapshot.plugins = null;
  assert.throws(() => stateApi.createScopedDeckyState(real), /unsupported_plugin_inventory/);
});

function fixture(withCapability = true, projectionConflict = false) {
  const host = new EventTarget();
  const timers = new Map();
  let nextTimer = 0;
  host.setInterval = fn => { timers.set(++nextTimer, fn); return nextTimer; };
  host.clearInterval = id => timers.delete(id);
  let available = true;
  const acquired = [];
  const capability = { protocol: 1, isAvailable: () => available,
    acquire(options) {
      let released = false;
      const content = options.createContent?.({ type: "Provider", props: { deckyState: realState().real, children: { type: "PluginView" } } });
      const lease = { panel: content ?? {}, active: false, releases: 0, nativeHidden: options.hideNative,
        renew: () => !released,
        setActive(value) { lease.active = value; return !released; },
        setReady() { return !released; },
        setNativeHidden(value) { lease.nativeHidden = value; return !released; },
        release(reason) { if (released) return; released = true; lease.releases++; options.onRelease?.(reason); } };
      acquired.push(lease);
      return lease;
    }
  };
  const bridge = { protocol: 1, ...(withCapability ? { deckyHost: capability } : {}) };
  host[Symbol.for("shortcuts.qam-bridge.v1")] = bridge;
  const preference = load("deckyNativeTabPreference.ts", { window: host });
  const api = load("deckyHostRuntime.ts", { window: host,
    require: name => { if (name === "./deckyHostProjection") return { installDeckyTabProjection: onConflict => {
      if (projectionConflict) onConflict();
      return { reconcile() {}, stop() {} };
    } };
      if (name === "./deckyNativeTabPreference") return preference;
      assert.equal(name, "./deckyHostStandalone"); return load("deckyHostStandalone.ts", {
      WeakRef, setInterval: host.setInterval, clearInterval: host.clearInterval,
    }); },
  });
  return { api, host, bridge, acquired, timers, setProjectionConflict: value => { projectionConflict = value; }, setAvailable: value => { available = value; }, tick: () => [...timers.values()].forEach(fn => fn()) };
}
const options = { element: {}, createContent: () => ({}), isHealthy: () => true, onRelease: () => {} };

test("synchronous projection ownership conflict survives bootstrap and toggles until a fresh runtime", async () => {
  const f = fixture(true, true);
  const stop = f.api.initDeckyHost(async () => ({ deckyHostEnabled: true }));
  await new Promise(resolve => setImmediate(resolve));
  f.tick();
  f.api.setDeckyHostEnabled(false); f.api.setDeckyHostEnabled(true);
  assert.equal(f.api.getDeckyHostSnapshot().reason, "native_projection_owner_conflict");
  assert.equal(f.api.getDeckyHostSnapshot().available, false);
  assert.equal(f.api.mountDeckyHost(options), null);
  assert.equal(f.acquired.length, 0);
  stop(); f.setProjectionConflict(false);
  const fresh = f.api.initDeckyHost(async () => ({ deckyHostEnabled: true }));
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(f.api.getDeckyHostSnapshot().available, true);
  fresh();
});

test("cold start retries persisted preference and deferred host without opening settings", async () => {
  for (const hidden of [true, false]) {
    const f = fixture(); let reads = 0;
    f.setAvailable(false);
    const stop = f.api.initDeckyHost(async () => {
      if (++reads === 1) throw Error("backend starting");
      return { deckyHostEnabled: hidden };
    });
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(f.api.getDeckyHostSnapshot().enabled, false);
    f.tick(); await new Promise(resolve => setImmediate(resolve));
    assert.equal(f.api.getDeckyHostSnapshot().enabled, true);
    assert.equal(f.api.mountDeckyHost(options), null);
    f.setAvailable(true); f.tick();
    const lease = f.api.mountDeckyHost(options);
    assert.ok(lease); lease.setReady();
    assert.equal(f.acquired[0].nativeHidden, hidden);
    // acquire receives persisted suppression even before ControlCenter has mounted.
    f.api.setDeckyNativeHidden(false);
    assert.equal(f.acquired[0].nativeHidden, false);
    assert.equal(f.api.getDeckyHostSnapshot().available, true, "native toggle must not remove internal host");
    stop();
    const restart = fixture();
    const stopRestart = restart.api.initDeckyHost(async () => ({ deckyHostEnabled: hidden }));
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(restart.api.getDeckyHostSnapshot().enabled, true);
    assert.ok(restart.api.mountDeckyHost(options));
    assert.equal(restart.acquired[0].nativeHidden, hidden);
    assert.equal(reads, 2); stopRestart();
  }
});

test("late bootstrap preference cannot override a user toggle or revive an unloaded runtime", async () => {
  for (const unload of [false, true]) {
    const f = fixture(); let resolve;
    const stop = f.api.initDeckyHost(() => new Promise(done => { resolve = done; }));
    f.api.setDeckyNativeHidden(false);
    f.api.setDeckyHostEnabled(true);
    if (unload) stop();
    resolve({ deckyHostEnabled: true }); await new Promise(done => setImmediate(done));
    assert.equal(f.api.getDeckyHostSnapshot().enabled, !unload);
    if (!unload) {
      const lease = f.api.mountDeckyHost(options); assert.ok(lease);
      assert.equal(f.acquired[0].nativeHidden, false);
    }
    stop();
  }
});

test("native visibility toggles keep the committed Playhub host mounted", () => {
  const f = fixture();
  const stop = f.api.initDeckyHost();
  f.api.setDeckyNativeHidden(false);
  f.api.setDeckyHostEnabled(true);
  const lease = f.api.mountDeckyHost(options);
  lease.setReady();
  for (const hidden of [false, true, false]) {
    assert.equal(f.api.setDeckyNativeHidden(hidden), true);
    assert.equal(f.acquired[0].nativeHidden, hidden);
    assert.equal(f.acquired[0].releases, 0);
    assert.equal(f.api.getDeckyHostSnapshot().enabled, true);
    assert.equal(f.api.getDeckyHostSnapshot().ready, true);
  }
  stop();
});

test("legacy host without independent visibility never receives suppressing readiness", () => {
  const f = fixture();
  const acquire = f.bridge.deckyHost.acquire;
  let commits = 0;
  f.bridge.deckyHost.acquire = options => {
    const lease = acquire(options);
    delete lease.setNativeHidden;
    lease.setReady = () => { commits++; return true; };
    return lease;
  };
  const stop = f.api.initDeckyHost();
  f.api.setDeckyHostEnabled(true);
  const lease = f.api.mountDeckyHost(options);
  assert.equal(lease.setReady(), true);
  assert.equal(commits, 0);
  f.api.setDeckyNativeHidden(false);
  assert.equal(f.acquired[0].releases, 0);
  stop();
});

test("disabling host publishes one atomic restored OFF snapshot", () => {
  const f = fixture();
  const stop = f.api.initDeckyHost(); f.api.setDeckyHostEnabled(true);
  f.api.mountDeckyHost(options).setReady();
  const seen = [];
  const unsubscribe = f.api.subscribeDeckyHost(() => seen.push({ ...f.api.getDeckyHostSnapshot(), releases: f.acquired[0].releases }));
  f.api.setDeckyHostEnabled(false);
  assert.equal(seen.length, 1, "no intermediate enabled snapshot may trigger reacquisition");
  assert.equal(seen[0].enabled, false);
  assert.equal(seen[0].releases, 1, "restore must finish before publishing OFF");
  unsubscribe(); stop();
});

test("failed native restore keeps host ownership and can retry OFF", () => {
  const f = fixture();
  const stop = f.api.initDeckyHost(); f.api.setDeckyHostEnabled(true);
  f.api.mountDeckyHost(options).setReady();
  const original = f.acquired[0].release;
  f.acquired[0].release = () => { throw new Error("restore failed"); };
  assert.doesNotThrow(() => f.api.setDeckyHostEnabled(false));
  assert.equal(f.api.getDeckyHostSnapshot().enabled, true);
  assert.equal(f.api.getDeckyHostSnapshot().reason, "native_restore_failed");
  f.acquired[0].release = original;
  f.api.setDeckyHostEnabled(false);
  assert.equal(f.acquired[0].releases, 1);
  assert.equal(f.api.getDeckyHostSnapshot().enabled, false);
  stop();
});
test("OFF bootstrap, opt-in, commit, OFF release, idempotent unload", () => {
  const f = fixture();
  const stop = f.api.initDeckyHost();
  assert.equal(f.api.getDeckyHostSnapshot().enabled, false);
  assert.equal(f.api.mountDeckyHost(options), null);
  f.api.setDeckyHostEnabled(true);
  const lease = f.api.mountDeckyHost(options);
  assert.ok(lease);
  assert.equal(f.api.getDeckyHostSnapshot().ready, false);
  assert.equal(f.api.mountDeckyHost(options), null);
  lease.setActive(true);
  lease.setReady();
  assert.equal(f.api.getDeckyHostSnapshot().ready, true);
  lease.setActive(false);
  assert.equal(f.api.getDeckyHostSnapshot().ready, true, "inactive horizontal tab does not release readiness");
  assert.equal(f.acquired[0].releases, 0);
  f.api.setDeckyHostEnabled(false);
  assert.equal(f.acquired[0].releases, 1);
  assert.equal(f.api.getDeckyHostSnapshot().ready, false);
  stop(); stop();
  assert.equal(f.timers.size, 0);
});
test("older bridge cannot acquire or suppress; capability loss restores", () => {
  const older = fixture(false);
  const stopOld = older.api.initDeckyHost();
  older.api.setDeckyHostEnabled(true);
  assert.equal(older.api.getDeckyHostSnapshot().available, false);
  assert.equal(older.api.mountDeckyHost(options), null);
  stopOld();
  const f = fixture();
  const stop = f.api.initDeckyHost(); f.api.setDeckyHostEnabled(true);
  f.api.mountDeckyHost(options).setActive(true);
  f.setAvailable(false); f.tick();
  assert.equal(f.acquired[0].releases, 1);
  assert.equal(f.api.getDeckyHostSnapshot().available, false);
  stop();
});
test("root failures latch safely until explicit opt-in retry; pagehide restores", () => {
  const f = fixture();
  const stop = f.api.initDeckyHost(); f.api.setDeckyHostEnabled(true);
  f.api.mountDeckyHost(options).setActive(true);
  f.api.failDeckyHost("native_root_error"); f.tick();
  assert.equal(f.acquired[0].releases, 1);
  assert.equal(f.api.getDeckyHostSnapshot().available, false);
  assert.equal(f.api.getDeckyHostSnapshot().reason, "native_root_error");
  f.api.setDeckyHostEnabled(true);
  f.api.mountDeckyHost(options).setActive(true);
  f.host.dispatchEvent(new Event("pagehide"));
  assert.equal(f.acquired[1].releases, 1);
  assert.equal(f.api.getDeckyHostSnapshot().enabled, false);
  stop();
});

test("runtime selects standalone without Shortcuts and switches cooperative ownership", () => {
  const f = fixture();
  const bridgeSymbol = Symbol.for("shortcuts.qam-bridge.v1");
  const standaloneSymbol = Symbol.for("playhub.decky-host-standalone.v1");
  const standalone = { ...f.bridge.deckyHost };
  delete f.host[bridgeSymbol];
  f.host[standaloneSymbol] = standalone;
  const stop = f.api.initDeckyHost();
  assert.equal(f.api.getDeckyHostSnapshot().enabled, false);
  assert.equal(f.api.getDeckyHostSnapshot().available, true);
  f.api.setDeckyHostEnabled(true);
  f.api.mountDeckyHost(options).setReady();
  const revision = f.api.getDeckyHostSnapshot().ownerRevision;
  f.host[bridgeSymbol] = f.bridge; f.tick();
  assert.equal(f.acquired[0].releases, 1);
  assert.ok(f.api.getDeckyHostSnapshot().ownerRevision > revision);
  f.api.mountDeckyHost(options).setReady();
  f.host.DeckyPluginLoader = { deckyState: { publicState: () => ({ disabledPlugins: [{ name: "Shortcuts" }] }) } };
  f.tick();
  assert.equal(f.acquired[1].releases, 1);
  assert.equal(f.api.getDeckyHostSnapshot().available, true, "disabled stale bridge yields to standalone");
  assert.ok(f.api.mountDeckyHost(options));
  stop();
});

test("a healthy replacement owner clears only the previous owner's failure", () => {
  const f = fixture();
  const stop = f.api.initDeckyHost();
  f.api.setDeckyHostEnabled(true);
  f.api.mountDeckyHost(options).setReady();
  f.api.failDeckyHost("native_root_error");
  f.tick();
  assert.equal(f.api.getDeckyHostSnapshot().available, false);
  const replacement = { ...f.bridge.deckyHost };
  f.bridge.deckyHost = replacement;
  f.setAvailable(false);
  f.tick();
  assert.equal(f.api.getDeckyHostSnapshot().reason, "native_root_error", "unhealthy replacement cannot erase diagnostic failure");
  f.bridge.deckyHost = { ...replacement };
  f.setAvailable(true);
  f.tick();
  assert.equal(f.api.getDeckyHostSnapshot().available, true);
  assert.ok(f.api.mountDeckyHost(options));
  f.api.failDeckyHost("native_root_error");
  f.tick();
  assert.equal(f.api.getDeckyHostSnapshot().available, false, "same owner still fails closed");
  stop();
});
test("host component keeps native root and commits before activation; rejects CSS ancestors", () => {
  for (const bleed of [false, true]) {
    const f = fixture();
    const stop = f.api.initDeckyHost(); f.api.setDeckyHostEnabled(true);
    const effects = [];
    let panel;
    const element = { isConnected: true, closest: () => bleed ? {} : null };
    const React = { Component: class {},
      useSyncExternalStore: (_subscribe, snapshot) => snapshot(),
      useRef: value => ({ current: value === null ? element : value }),
      useState: () => [panel, value => { panel = value; }],
      useCallback: fn => fn, useEffect: fn => effects.push(fn), useLayoutEffect: fn => fn(),
      isValidElement: value => Boolean(value?.type),
      cloneElement: (element, changes) => ({ ...element, props: { ...element.props, ...changes } }),
    };
    // Ref slots must be independent: only the DOM ref is initialized by the renderer.
    let refs = 0;
    React.useRef = value => ({ current: refs++ === 0 ? element : value });
    const api = load("deckyHost.tsx", { window: f.host,
      require: name => name === "./decky" ? { SP_REACT: React, DFL: { Focusable: "focusable" } } : name === "./deckyHostState" ? stateApi
        : name === "./deckyHostRuntime" ? f.api : { jsx: (type, props) => ({ type, props }), jsxs: (type, props) => ({ type, props }) }
    });
    const root = api.DeckyHost({ active: true });
    assert.equal(root.props.childFocusDisabled, false);
    assert.equal(root.props.focusable, false);
    assert.equal(root.props["aria-hidden"], false);
    const cleanup = effects[0]();
    if (bleed) {
      assert.equal(f.acquired.length, 0);
      assert.equal(f.api.getDeckyHostSnapshot().reason, "settings_css_ancestor");
    } else {
      assert.equal(f.acquired[0].active, false);
      const [native, commit] = panel.props.children;
      assert.equal(native.type, "Provider");
      assert.equal(native.props.children.type, "PluginView");
      assert.deepEqual(Array.from(native.props.deckyState.publicState().plugins, p => p.name), ["Artwork"]);
      commit.type(commit.props);
      assert.equal(f.acquired[0].active, true);
      panel.props.onError();
      assert.equal(f.acquired[0].releases, 1);
      cleanup();
    }
    stop();
  }
});

test("inactive host is excluded from Steam's child focus graph and browser focus", () => {
  const api = load("deckyHost.tsx", { window: {}, require: name => {
    if (name === "./decky") return { DFL: { Focusable: "focusable" }, SP_REACT: {
      Component: class {}, useSyncExternalStore: () => ({ enabled: true, available: true }),
      useRef: () => ({ current: null }), useState: () => [null, () => {}], useCallback: fn => fn,
      useEffect() {}, useLayoutEffect() {},
    } };
    if (name === "./deckyHostState" || name === "./deckyHostRuntime") return {};
    return { jsx: (type, props) => ({ type, props }) };
  } });
  const root = api.DeckyHost({ active: false });
  assert.equal(root.props.childFocusDisabled, true);
  assert.equal(root.props.inert, "");
  assert.equal(root.props["aria-hidden"], true);
  assert.equal(root.props.style.display, "none");
});

import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";
import ts from "typescript";

const source = fs.readFileSync(new URL("../src/quickSettings/steamPerformanceMonitor.ts", import.meta.url), "utf8");
const compiled = ts.transpile(source, { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ES2022 });
const { steamPerformanceMonitor, steamShortcutOverlay } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString("base64")}`);

test("turning off Steam FPS leaves its detail and global overlay enabled; re-enabling restores its position", async () => {
  const settings = { overlay_fps_counter_corner: 6, overlay_fps_counter_detail_level: 4, enable_overlay: true };
  const changes = [];
  const monitor = steamPerformanceMonitor({ settingsStore: { GetClientSetting(key) {
    return [settings[key], value => { changes.push(key); settings[key] = value; }];
  } } });
  assert.deepEqual(monitor.read(), { corner: 6, detail: 4, overlay: true });
  await monitor.setEnabled(false);
  assert.deepEqual(monitor.read(), { corner: 0, detail: 4, overlay: true });
  await monitor.setEnabled(true);
  assert.deepEqual(monitor.read(), { corner: 6, detail: 4, overlay: true });
  assert.deepEqual(changes, ["overlay_fps_counter_corner", "overlay_fps_counter_corner"]);
});

test("Steam's latest confirmed corner survives disabling and a fresh Playhub context", async () => {
  const settings = { overlay_fps_counter_corner: 2, overlay_fps_counter_detail_level: 3, enable_overlay: true };
  const values = new Map();
  const makeHost = () => ({
    localStorage: { getItem: key => values.get(key), setItem: (key, value) => values.set(key, value) },
    settingsStore: { GetClientSetting: key => [settings[key], value => { settings[key] = value; }] },
  });
  const monitor = steamPerformanceMonitor(makeHost());
  assert.equal(monitor.read().corner, 2);
  settings.overlay_fps_counter_corner = 6; // Changed directly in Steam before disabling in Playhub.
  await monitor.setEnabled(false);
  assert.equal(settings.overlay_fps_counter_corner, 0);
  const reload = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString("base64")}#reload`);
  await reload.steamPerformanceMonitor(makeHost()).setEnabled(true);
  assert.deepEqual(settings, { overlay_fps_counter_corner: 6, overlay_fps_counter_detail_level: 3, enable_overlay: true });
  await reload.steamPerformanceMonitor(makeHost()).setEnabled(false);
  values.set([...values.keys()][0], "6px");
  await reload.steamPerformanceMonitor(makeHost()).setEnabled(true);
  assert.equal(settings.overlay_fps_counter_corner, 1);
});

test("blocked storage leaves a confirmed in-memory corner per Steam window", async () => {
  function createHost(initial) {
    let corner = initial;
    return {
      get localStorage() { throw new Error("Storage denied"); },
      settingsStore: { GetClientSetting: key => key === "overlay_fps_counter_corner" ? [corner, value => { corner = value; }]
        : [key === "enable_overlay" ? true : 4, () => assert.fail("only the corner may be written")] },
    };
  }
  const first = createHost(5);
  await steamPerformanceMonitor(first).setEnabled(false);
  const second = steamPerformanceMonitor(createHost(0));
  await second.setEnabled(true);
  assert.equal(second.read().corner, 1);
  const remounted = steamPerformanceMonitor(first);
  await remounted.setEnabled(true);
  assert.equal(remounted.read().corner, 5);
});

test("Steam schema changes disable the monitor control before any setting is written", async () => {
  const monitor = steamPerformanceMonitor({ settingsStore: { GetClientSetting(key) {
    return [key === "enable_overlay" ? true : 99, () => assert.fail("invalid schema must not write")];
  } } });
  assert.equal(monitor.read(), null);
  await assert.rejects(() => monitor.setEnabled(true));
});

test("transparent Steam statistics restore the previous background across contexts without changing FPS settings", async () => {
  const settings = { overlay_fps_counter_corner: 1, overlay_fps_counter_detail_level: 4, enable_overlay: true,
    overlay_fps_counter_bgopacity: .6 };
  const values = new Map(), changes = [];
  const makeHost = () => ({
    localStorage: { getItem: key => values.get(key), setItem: (key, value) => values.set(key, value) },
    settingsStore: { GetClientSetting: key => [settings[key], value => { changes.push([key, value]); settings[key] = value; }] },
  });
  const monitor = steamPerformanceMonitor(makeHost());
  assert.equal(monitor.read().backgroundOpacity, .6);
  await monitor.setTransparent(true);
  assert.equal(monitor.read().backgroundOpacity, 0);
  await steamPerformanceMonitor(makeHost()).setTransparent(false);
  assert.equal(settings.overlay_fps_counter_bgopacity, .6);
  assert.deepEqual(changes, [["overlay_fps_counter_bgopacity", 0], ["overlay_fps_counter_bgopacity", .6]]);
  assert.equal(settings.overlay_fps_counter_corner, 1);
  assert.equal(settings.overlay_fps_counter_detail_level, 4);
  assert.equal(settings.enable_overlay, true);
});

test("unsupported Steam background settings keep the main monitor usable and refuse appearance writes", async () => {
  for (const opacity of [undefined, NaN, Infinity, -.1, 1.1, "0"] ) {
    const writes = [];
    const monitor = steamPerformanceMonitor({ settingsStore: { GetClientSetting(key) {
      const value = key === "overlay_fps_counter_bgopacity" ? opacity : key === "enable_overlay" ? true : 1;
      return [value, next => writes.push([key, next])];
    } } });
    assert.equal(monitor.read().corner, 1);
    assert.equal(monitor.read().backgroundOpacity, undefined);
    await assert.rejects(() => monitor.setTransparent(true));
    assert.deepEqual(writes, []);
  }
});

test("an existing transparent Steam background uses a bounded saved opacity and tolerates blocked storage", async () => {
  for (const stored of ["NaN", "-1", "4", "0", "", "0.4"] ) {
    let opacity = 0;
    const host = { localStorage: { getItem: () => stored, setItem() {} }, settingsStore: { GetClientSetting(key) {
      return key === "overlay_fps_counter_bgopacity" ? [opacity, value => { opacity = value; }] : [key === "enable_overlay" ? true : 1, () => {}];
    } } };
    await steamPerformanceMonitor(host).setTransparent(false);
    assert.equal(opacity, stored === "0.4" ? .4 : .8);
  }
  let opacity = .7;
  const host = { get localStorage() { throw Error("denied"); }, settingsStore: { GetClientSetting(key) {
    return key === "overlay_fps_counter_bgopacity" ? [opacity, value => { opacity = value; }] : [key === "enable_overlay" ? true : 1, () => {}];
  } } };
  const monitor = steamPerformanceMonitor(host);
  await monitor.setTransparent(true);
  await monitor.setTransparent(false);
  assert.equal(opacity, .7);
});

test("shortcut repair only enables the exact known non-Steam app, even if Steam changes it while Video is open", async () => {
  const id = 0xba55abcd, calls = [];
  let detail = { unAppID: id, bOverlayEnabled: false };
  let changed;
  const host = {
    appStore: { GetAppOverviewByAppID: () => ({ BIsShortcut: () => true }) },
    appDetailsStore: {
      GetAppDetails: () => detail,
      RegisterForAppData: (id, callback) => { changed = callback; return { unregister: () => { changed = undefined; } }; },
    },
    SteamClient: { Apps: { ToggleEnableSteamOverlayForApp(appId) {
      calls.push(appId);
      detail = { ...detail, bOverlayEnabled: !detail.bOverlayEnabled };
      changed?.(detail);
    } } },
  };
  assert.equal(steamShortcutOverlay(host, 570), null);
  const shortcut = steamShortcutOverlay(host, id), events = [];
  const unsubscribe = shortcut.subscribe(value => events.push(value));
  await assert.rejects(() => shortcut.enable(() => false));
  await Promise.all([shortcut.enable(() => true), shortcut.enable(() => true)]);
  await shortcut.enable(() => true);
  detail = { unAppID: id + 1, bOverlayEnabled: false };
  assert.equal(shortcut.read(), null);
  await assert.rejects(() => shortcut.enable(() => true));
  unsubscribe();
  assert.deepEqual(calls, [id]);
  assert.deepEqual(events, [true]);
  assert.equal(changed, undefined);
});

test("a delayed Steam readback cannot make a second click undo the first repair", async () => {
  const id = 0x82223344;
  let observed = false, actual = false, calls = 0;
  const host = {
    appStore: { GetAppOverviewByAppID: () => ({ BIsShortcut: () => true }) },
    appDetailsStore: {
      GetAppDetails: () => ({ unAppID: id, bOverlayEnabled: observed }),
      RegisterForAppData: () => ({ unregister() {} }),
    },
    SteamClient: { Apps: { ToggleEnableSteamOverlayForApp() { calls++; actual = !actual; } } },
  };
  const shortcut = steamShortcutOverlay(host, id);
  await assert.rejects(() => shortcut.enable(() => true));
  await assert.rejects(() => steamShortcutOverlay(host, id).enable(() => true));
  assert.equal(actual, true);
  assert.equal(calls, 1);
  observed = true;
  await shortcut.enable(() => true);
  assert.equal(calls, 1);
});

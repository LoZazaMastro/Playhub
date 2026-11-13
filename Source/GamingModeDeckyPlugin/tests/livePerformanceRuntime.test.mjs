import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";
import ts from "typescript";

const compile = name => ts.transpile(fs.readFileSync(new URL(`../src/quickSettings/${name}.ts`, import.meta.url), "utf8"),
  { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS });
const monitorCode = compile("steamPerformanceMonitor"), runtimeCode = compile("livePerformanceRuntime");
const ownedModule = { exports: {} };
Function("module","exports",ts.transpile(fs.readFileSync(new URL("../src/steamOwnedLaunchDispatcher.ts",import.meta.url),"utf8"),{target:ts.ScriptTarget.ES2022,module:ts.ModuleKind.CommonJS}))(ownedModule,ownedModule.exports);
const identityModule = { exports: {} };
Function("module", "exports", compile("steamPerformanceOverlayIdentity"))(identityModule, identityModule.exports);
function load(overrides = {}) {
  const monitor = { exports: {} };
  Function("module", "exports", monitorCode)(monitor, monitor.exports);
  const module = { exports: {} };
  const imports = {
    "../steamOwnedLaunchDispatcher": ownedModule.exports,
    "../decky": { DFL: {} }, "../controlBackend": { call() { throw new Error("No native RPC in this fixture"); } },
    "../controlledQamFocus": { steamAppId: value => value }, "./livePerformanceController": {},
    "./steamPerformanceMonitor": monitor.exports, "./steamPerformanceOverlayIdentity": identityModule.exports, ...overrides,
  };
  Function("require", "module", "exports", runtimeCode)(name => {
    assert.ok(name in imports, `Unexpected dependency: ${name}`); return imports[name];
  }, module, module.exports);
  return module.exports;
}
function fixture() {
  let corner = 3;
  const changes = [];
  let deferWrite;
  const host = { settingsStore: { GetClientSetting(key) {
    return key === "overlay_fps_counter_corner" ? [corner, async value => {
      changes.push(value); if (deferWrite) await deferWrite(); corner = value;
    }] : [key === "enable_overlay" ? true : 2, () => assert.fail("Only the FPS corner may change")];
  } } };
  return { host, changes, get corner() { return corner; }, set corner(value) { corner = value; },
    defer(value) { deferWrite = value; } };
}

test("new bundle root shares the first original corner until its last holder releases", async () => {
  const f = fixture(), oldRoot = load(), newRoot = load();
  const oldRestore = await oldRoot.suspendSteamMonitor(f.host);
  const newRestore = await newRoot.suspendSteamMonitor(f.host);
  assert.equal(f.corner, 0);
  await oldRestore(); assert.equal(f.corner, 0);
  await oldRestore(); assert.equal(f.corner, 0);
  await newRestore(); assert.equal(f.corner, 3);
  assert.deepEqual(f.changes, [0, 3]);
});

test("desktop overlay activity and actual Valve callbacks suspend HUD even with a closed side menu", async () => {
  const previous = globalThis.window;
  let activated, changed = 0, refreshed = 0, removed = 0;
  const timers = new Map(); let id = 0;
  const desktop = { params: { browserInfo: { m_gameID: "42", m_unPID:100, m_unAppID:42, m_nBrowserID:202 } }, DesktopOverlay: { overlay_active: true }, MenuStore: { GetOpenSideMenu: () => 0 } };
  const store = { OverlayWindows: [desktop], GamepadUIMainWindowInstance: { MenuStore: { GetOpenSideMenu: () => 0 } } };
  globalThis.window = { settingsStore: {}, SteamUIStore: { MainRunningAppID: 42, WindowStore: store },
    SteamClient: { Overlay: { RegisterForOverlayActivated(callback) { activated = callback; return { unregister() { removed++; } }; } } },
    setTimeout(callback) { timers.set(++id, callback); return id; }, clearTimeout(key) { timers.delete(key); } };
  const fake = { read: () => ({ state: { enabled: true } }), start() {}, dispose() {}, refresh: async () => { refreshed++; },
    menuChanged: async () => { changed++; }, holdSteamMonitor: async () => {} };
  try {
    const runtime = load({ "./livePerformanceController": { createLivePerformanceController: () => fake } });
    assert.equal(runtime.menuOpen(), true);
    desktop.DesktopOverlay.overlay_active = false; assert.equal(runtime.menuOpen(), false);
    const cleanup = runtime.installLivePerformance();
    activated(100, 42, true, true); assert.equal(runtime.menuOpen(), true); assert.equal(changed, 1);
    desktop.params.browserInfo.m_unPID = 200; assert.equal(runtime.menuOpen(), false, "A replaced overlay host cannot inherit stale active=true");
    desktop.params.browserInfo.m_unPID = 100;
    activated(100, 42, false, true); assert.equal(runtime.menuOpen(), false); assert.equal(changed, 2);
    desktop.params.browserInfo.m_gameID = "43"; desktop.DesktopOverlay.overlay_active = true;
    assert.equal(runtime.menuOpen(), false, "Another game's persistent browser cannot suspend this game");
    cleanup(); assert.equal(removed, 1); assert.equal(timers.size, 0); assert.equal(refreshed, 0);
  } finally { globalThis.window = previous; }
});

test("late old-root restoration and new-root acquisition are serialized", async () => {
  const f = fixture(), oldRoot = load(), newRoot = load();
  const oldRestore = await oldRoot.suspendSteamMonitor(f.host);
  let finish;
  f.defer(() => new Promise(resolve => { finish = resolve; }));
  const disposing = oldRestore();
  await new Promise(resolve => setImmediate(resolve));
  const acquiring = newRoot.suspendSteamMonitor(f.host);
  f.defer(undefined); finish();
  await disposing;
  const newRestore = await acquiring;
  assert.equal(f.corner, 0);
  await newRestore(); assert.equal(f.corner, 3);
  assert.deepEqual(f.changes, [0, 3, 0, 3]);
});

test("compact original game host suspends HUD independently of companion callbacks", () => {
  const previous = globalThis.window;
  const app = 2979426017, host = 28788;
  let activated;
  const entry = { params:{ browserInfo:{ m_gameID:String(app), m_unPID:host, m_nBrowserID:1310737, m_unAppID:9858785 } },
    IsGamepadUIOverlayWindow:()=>true, MenuStore:{ GetOpenSideMenu:()=>0 } };
  const companion = { params:{ browserInfo:{ m_gameID:'12796537303900094464', m_unPID:46444, m_nBrowserID:1310738, m_unAppID:app } },
    IsGamepadUIOverlayWindow:()=>true, MenuStore:{ GetOpenSideMenu:()=>0 } };
  const store={ OverlayWindows:[entry,companion], m_mapOverlayPopupByPID:new Map([[host,entry],[46444,companion]]) };
  globalThis.window={ settingsStore:{}, SteamUIStore:{MainRunningAppID:app,WindowStore:store},
    SteamClient:{Overlay:{RegisterForOverlayActivated(cb){activated=cb;return{unregister(){}};}}}, setTimeout:()=>1,clearTimeout(){} };
  const fake={read:()=>({state:{enabled:true}}),start(){},dispose(){},refresh:async()=>{},menuChanged:async()=>{}};
  try {
    const runtime=load({'./livePerformanceController':{createLivePerformanceController:()=>fake}});
    const cleanup=runtime.installLivePerformance();
    assert.equal(runtime.menuOpen(),false);
    activated(host,9858785,true); assert.equal(runtime.menuOpen(),true);
    activated(46444,app,false); assert.equal(runtime.menuOpen(),true,'A companion FALSE cannot replace the real host TRUE');
    activated(host,app,true); assert.equal(runtime.menuOpen(),true,'An unrelated AppID cannot replace the exact native activity');
    entry.params.browserInfo.m_nBrowserID=1310740;
    assert.equal(runtime.menuOpen(),false,'A replacement browser cannot inherit old host activity');
    entry.params.browserInfo.m_nBrowserID=1310737;
    activated(host,9858785,true); assert.equal(runtime.menuOpen(),true);
    store.m_mapOverlayPopupByPID.delete(host); assert.equal(runtime.menuOpen(),false,'Expired compact browser cannot keep HUD hidden');
    store.m_mapOverlayPopupByPID.set(host,entry);
    activated(host,9858785,false); assert.equal(runtime.menuOpen(),false);
    window.SteamUIStore.MainRunningAppID=3134433872; assert.equal(runtime.menuOpen(),false);
    window.SteamUIStore.MainRunningAppID=app; assert.equal(runtime.menuOpen(),false,'A new launch cannot inherit old native activity');
    cleanup();
  } finally {globalThis.window=previous;}
});

test("user Steam corner changes survive overlapping roots and all finalizers", async () => {
  const f = fixture(), oldRoot = load(), newRoot = load();
  const oldRestore = await oldRoot.suspendSteamMonitor(f.host);
  f.corner = 6;
  const newRestore = await newRoot.suspendSteamMonitor(f.host);
  assert.equal(f.corner, 6);
  await oldRestore(); await newRestore();
  assert.equal(f.corner, 6); assert.deepEqual(f.changes, [0]);
});

test("failed restore keeps its original corner for a subsequent root's retry", async () => {
  const f = fixture(), runtime = load();
  const restore = await runtime.suspendSteamMonitor(f.host);
  f.defer(() => Promise.reject(new Error("Steam temporarily unavailable")));
  await assert.rejects(restore);
  f.defer(undefined);
  const retry = await load().suspendSteamMonitor(f.host);
  await retry(); assert.equal(f.corner, 3);
});

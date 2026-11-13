import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';
import ts from 'typescript';
const compilerOptions = {module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,jsx:ts.JsxEmit.React};
const compile = path => ts.transpileModule(readFileSync(new URL(path, import.meta.url), 'utf8'), {compilerOptions}).outputText;
const compiled = compile('../src/index.tsx');
const steamNavigationReturn = {};
vm.runInNewContext(compile('../src/steamNavigationReturn.ts'), {exports:steamNavigationReturn,performance,AbortController});
const steamFocusEvents = {};
vm.runInNewContext(compile('../src/steamFocusEvents.ts'), {exports:steamFocusEvents});
const steamModeCommands = {};
vm.runInNewContext(compile('../src/steamModeCommands.ts'), {exports:steamModeCommands});
const steamOwnedLaunch = {};
vm.runInNewContext(compile("../src/steamOwnedLaunchDispatcher.ts"),{exports:steamOwnedLaunch});
const steamConfiguratorReturn = {};
vm.runInNewContext(compile("../src/steamConfiguratorReturn.ts"), {exports:steamConfiguratorReturn});
function fixture(failing, runtime = {}) {
  let factory;
  let tick;
  const calls = [], disposed = [], warnings = [];
  const install = name => () => {
    calls.push(name);
    if (failing === name) throw new Error(`Simulated unavailable Steam hook: ${name}`);
    return () => disposed.push(name);
  };
  const noop = () => {};
  const modules = {
    './decky': {definePlugin: value => {factory=value;return value;},DFL:{},SP_REACT:{createElement:(type,props,...children)=>({type,props,children})}, routerHook:{addRoute:noop,removeRoute:noop}},
    './circlesScreensaver':{installCirclesScreensaver:install('screensaver')},
    './onboardingIntegration':{installPlayhubOnboarding:install('onboarding')},
    './deckyHost':{initDeckyHost:install('decky host')},
    './quickSettings':{installQuickSettings:install('quick settings'),disposeDisplayConfirmation:noop},
    './controlledQamIntegration':{installControlledQamFocus:install('qam focus')},
    './quickSettings/livePerformanceRuntime':{installLivePerformance:install('performance monitor')},
    './QamSettings':{initializeQamPreferences:()=>Promise.resolve()},
    './HomeNews':{installHomeNews:install('home news')},
    './DashboardPage':{preloadDashboardWindows:()=>Promise.resolve(),clearDashboardChrome:noop},
    './pluginStoreData':{preloadPluginStoreCatalog:()=>Promise.resolve()},
    './navigationHaptics':{installNavigationHaptics:install('haptics'),getNavigationHapticsRevision:()=>0,configureNavigationHaptics:noop},
    './qam':{initPlayhubQam:install('qam')},
    './powerMenuPatch':{installPowerMenuPatch:install('power menu')},
    './deckyIpcRecovery':{installDeckyIpcRecovery:install('ipc recovery')},
    './steamNavigationReturn':steamNavigationReturn,
    './steamFocusEvents':steamFocusEvents,
    './steamModeCommands':steamModeCommands,
    './steamConfiguratorReturn':steamConfiguratorReturn,
    './steamOwnedLaunchDispatcher':steamOwnedLaunch,
    './api':{readSettings:()=>Promise.resolve({}),writeSettings:()=>Promise.resolve(),
      acknowledgeSteamModeCommand:runtime.acknowledgeSteamModeCommand??(async()=>true),
      consumeOpenRequest:runtime.consumeOpenRequest??(async()=>({focusRecovery:0,steamForeground:false}))},
    './controlBackend':{call:runtime.call},
    './controlledQamFocus':{steamAppId:value=>Number(value) >>> 0},
  };
  const window = {setInterval:callback=>{tick=callback;return 1;},clearInterval:noop,setTimeout:()=>2,clearTimeout:noop,
    SteamUIStore:runtime.steam,SteamClient:runtime.steamClient,FocusedAppWindowStore:runtime.focusStore};
  vm.runInNewContext(compiled,{exports:{},require:name=>modules[name]??{}, window,document:{documentElement:{lang:'en'}},navigator:{language:'en'},console:{warn:(...items)=>warnings.push(items),log:noop,debug:noop},Promise,Set,performance,Date:{now:runtime.now??Date.now}});
  return {factory,calls,disposed,warnings,tick:()=>tick()};
}
for (const name of ['ipc recovery','screensaver','onboarding','decky host','quick settings','qam focus','performance monitor','haptics','power menu']) {
  test(`Unavailable ${name} hook does not prevent the real plugin and QAM from loading`, async () => {
    const f = fixture(name);
    const plugin = f.factory();
    await Promise.resolve();
    assert.equal(plugin.name,'Playhub');
    assert.ok(f.calls.includes('qam'));
    assert.equal(f.warnings.length,1);
    plugin.onDismount();
    assert.ok(f.disposed.includes('qam'));
    assert.ok(!f.disposed.includes(name));
  });
}
test('Successful startup keeps all integrations and releases every hook',async()=>{
  const f=fixture();const plugin=f.factory();await Promise.resolve();plugin.onDismount();
  assert.equal(f.warnings.length,0);assert.equal(f.calls.length,11);assert.equal(f.disposed.length,11);
});

test('the production watcher acknowledges desktop only after actual Steam mode and desired-window readback',async()=>{
 const calls=[];let mode=4;
 const f=fixture(undefined,{
  steamClient:{UI:{ExitBigPictureMode(){calls.push('exit');mode=7;},GetUIMode:async()=>{calls.push('mode');return mode;},
   GetDesiredSteamUIWindows:async()=>{calls.push('desired');return[{windowType:5}];}}},
  consumeOpenRequest:async()=>({steamModeCommand:{revision:100,kind:'exit',token:'0'.repeat(31)+'1'},focusRecovery:0}),
  acknowledgeSteamModeCommand:async(cmd,complete)=>{calls.push(['ack',cmd.revision,complete]);return true;},
 });
 const plugin=f.factory();await f.tick();
 assert.deepEqual(calls,['exit','mode','desired',['ack',100,true]]);
 assert.equal(f.warnings.length,0);plugin.onDismount();
});

test('a game that gets a window after the fast arming period can still return to Big Picture',async()=>{
  let now=1000;
  let windowReady=false;
  const attempts=[];
  const f=fixture(undefined,{
    steam:{MainRunningAppID:2485417537,WindowStore:{GamepadUIMainWindowInstance:{IsGamepadUIOverlayWindow:()=>false}}},
    now:()=>now,
    call:async (method,appId)=>{if(method==='arm_big_picture_return') attempts.push([now,appId]);return {ok:windowReady};},
  });
  const plugin=f.factory();
  await f.tick();
  now+=25000;
  windowReady=true;
  await f.tick();
  now+=5000;
  await f.tick();
  assert.deepEqual(attempts,[[1000,2485417537],[26000,2485417537]]);
  plugin.onDismount();
});

test('Steam committed overlay focus wakes the real return watcher without replacing the native callback', async () => {
  let focus, unregistered = 0;
  const requests = [];
  const subscription = { unregister: () => unregistered++ };
  const signal = { focusRecovery: 0, steamForeground: false };
  const focusStore = { m_unFocusedOverlayAppID: 0, m_unFocusedOverlayPID: 0,
    OnOverlayGameWindowFocusChanged(appId, pid) { this.m_unFocusedOverlayAppID = appId; this.m_unFocusedOverlayPID = pid; } };
  // Native Steam retains the bound function object before Playhub is loaded.
  const overlayFocus = focusStore.OnOverlayGameWindowFocusChanged.bind(focusStore);
  const f = fixture(undefined, {
    focusStore,
    steam: { WindowStore: { GamepadUIMainWindowInstance: { IsGamepadUIOverlayWindow: () => false } } },
    steamClient: { System: { UI: {
      RegisterForFocusChangeEvents: callback => { focus = callback; return subscription; },
      RegisterForOverlayGameWindowFocusChanged: () => { throw new Error('Valve owns this native callback'); },
    } } },
    consumeOpenRequest: () => new Promise(resolve => requests.push(resolve)),
  });
  const plugin = f.factory();
  overlayFocus(42, 123);
  assert.equal(requests.length, 0);
  const ordinary = f.tick();
  focus();
  overlayFocus(0, 0);
  assert.equal(requests.length, 1);
  requests[0](signal);
  await ordinary;
  assert.equal(requests.length, 2);
  plugin.onDismount();
  assert.equal(unregistered, 1);
  focus();
  requests[1](signal);
  assert.equal(requests.length, 2);
});


test('actual return watcher arms exact resolved owned canonical when Steam raw app remains zero',async()=>{
 const canonical=2262950469;let context={},lookups=0;const attempts=[];
 const route={uiObservedApp:0,canonicalShortcutAppId:canonical,originalGameId:'9719298256856416256',observedSteamGameId:'480',gamePid:54572,gameBirth:'134389769499138508',wrapperPid:51052,wrapperBirth:'134389769497796417',steamPid:52872,steamBirth:'134389620696131260',observedFresh:true,routingOnly:true};
 const dispatcher=steamOwnedLaunch.createOwnedLaunchDispatcher({scope:()=>({observedApp:0,revision:1,context,disposed:false,enabled:true,runningShortcuts:[canonical]}),now:()=>1000,discover:async()=>{lookups++;return{ok:true,route};},signal:()=>assert.fail('resolution sends no input/menu request'),schedule:()=>1,cancel(){}});
 const release=steamOwnedLaunch.registerOwnedGameConsumer(dispatcher);
 const f=fixture(undefined,{steam:{MainRunningAppID:0,WindowStore:{GamepadUIMainWindowInstance:{IsGamepadUIOverlayWindow:()=>false}}},call:async(method,appId)=>{if(method==='arm_big_picture_return')attempts.push(appId);return{ok:true};}});
 const plugin=f.factory();try{
  await f.tick();await new Promise(resolve=>setImmediate(resolve));
  assert.deepEqual(attempts,[canonical]);assert.equal(lookups,1);
  await f.tick();assert.deepEqual(attempts,[canonical],'same live route cannot rearm spuriously');
  context={};assert.equal(steamOwnedLaunch.readOwnedGameApp(0),0,'old cached game cannot be selected after store replacement');
 }finally{plugin.onDismount();release();dispatcher.dispose();}
});

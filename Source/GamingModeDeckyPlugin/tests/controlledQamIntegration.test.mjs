import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";
function load(name, require, globals={}) {
  const exports={};
  vm.runInNewContext(ts.transpileModule(readFileSync(new URL(`../src/${name}.ts`,import.meta.url),"utf8"),{
    compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022},
  }).outputText,{exports,require,...globals});
  return exports;
}
const control=load("controlledQamFocus"), overlay=load("dashboardOverlay", () => control);
const companion=load("xboxCompanionOverlay", () => overlay);
const mirrorNavigation=load("steamMirrorNavigation"), nativeGame=load("steamNativeGameOverlay");
const mirror=load("steamMirrorOverlay", name => name === "./steamMirrorNavigation" ? mirrorNavigation : overlay);
const fallback=load("steamOverlayFallback");
const steamLayout=load("steamReturnControllerLayout");
const admission=load("steamMirrorGuideAdmission");
const ownedLaunch=load("steamOwnedLaunchDispatcher");
const settle=()=>new Promise(resolve=>setImmediate(resolve));
function fixture(infos=[], nativeReply={ok:true}, appId=42, options={}) {
  const calls=[], deckyCalls=[];
  function createMainWindow(label="native-home") {
    let visible=0;
    const menu={GetOpenSideMenu:()=>visible,GetQuickAccessTab:()=>4,
      OpenSideMenu(side){visible=side;},OpenQuickAccessMenu(tab){calls.push(["main",tab]);this.OpenSideMenu(2);},
      CloseSideMenus(){visible=0;}};
    const originalHome=function(){
      calls.push([label]);
      if(visible===1) this.MenuStore.CloseSideMenus();
      else this.MenuStore.OpenSideMenu(1);
    };
    return {MenuStore:menu,OnHomeButtonPressed:originalHome,originalHome,originalOpen:menu.OpenSideMenu};
  }
  const mainWindow=createMainWindow(), main=mainWindow.MenuStore;
  const windowStore={GamepadUIMainWindowInstance:mainWindow,
    OverlayWindows:options.overlayWindows,
    UpdateDesiredWindows(instance){this.GamepadUIMainWindowInstance=instance;}};
  const observedStore={WindowStore:windowStore};
  if(Object.hasOwn(options,"uiObserved"))Object.defineProperty(observedStore,"MainRunningAppID",{get:()=>options.uiObserved,configurable:true});
  const stale={OpenQuickAccessMenu(){calls.push(["stale"]);}};
  const patch=(object,key,handler,before)=>{
    const original=object[key];
    object[key]=function(...args){if(before)handler(args);const result=original.apply(this,args);return before?result:handler(args,result);};
    return{unpatch(){object[key]=original;options.afterUnpatch?.(object,key);}};
  };
  const callOriginal=Symbol("fixture-original");
  const replacePatch=(object,key,handler)=>{
    const original=object[key];
    object[key]=function(...args){
      const result=handler.call(this,args);
      return result===callOriginal?original.apply(this,args):result;
    };
    return {original,unpatch(){object[key]=original;}};
  };
  const ui={Router:{MainRunningAppID:appId,WindowStore:{GamepadUIMainWindowInstance:{MenuStore:stale}},Navigate(){calls.push(["navigate"]);}},
    Navigation:{OpenQuickAccessMenu(tab){stale.OpenQuickAccessMenu(tab);}},
    afterPatch:(o,k,h)=>patch(o,k,h,false),beforePatch:(o,k,h)=>patch(o,k,h,true),replacePatch,callOriginal};
  let systemCallback,overlayCallback,windowCallback=()=>windowStore.UpdateDesiredWindows(windowStore.GamepadUIMainWindowInstance);let unregistered=0,windowRegistered=0,windowUnregistered=0;
  const steam={Overlay:{GetOverlayBrowserInfo:async()=>infos},Apps:{RunGame(){calls.push(["run"]);},RaiseWindowForGame(gameId){calls.push(["raise",gameId]);return "steam-result";}},
    URL:{ExecuteSteamURL(url){calls.push(["steam-url",url]);}},
    UI:{RegisterDesiredSteamUIWindowsChanged(callback){windowRegistered++;windowCallback=callback;
      return{unregister(){windowUnregistered++;}};}},
    System:{UI:{RegisterForSystemKeyEvents(callback){systemCallback=callback;return{unregister(){unregistered++;}};}}}};
  if(options.companion) Object.assign(steam.Overlay,{SetOverlayState(){calls.push(["overlay-state"]);},
    RegisterForOverlayActivated(callback){overlayCallback=callback;return {unregister(){}};}});
  const api=load("controlledQamIntegration",name=>name==="./controlBackend"?{call:async (...args)=>{
    deckyCalls.push(args);
    if(args[0]==="get_native_game_overlay") return {ok:true,kind:args[1]===2485417537?"packaged":"absent"};
    return options.rpc?options.rpc(...args):{ok:true};
  }}:name==="./decky"?{DFL:ui}:name==="./api"?{API_BASE:"http://fixture",logToAgent() {}}:name==="./controlledQamFocus"?control
    :name==="./steamNativeGameOverlay"?nativeGame:name==="./steamOwnedLaunchDispatcher"?ownedLaunch:name==="./steamMirrorGuideAdmission"?admission:name==="./steamMirrorOverlay"?mirror:name==="./xboxCompanionOverlay"?(options.createCompanion?{createXboxCompanionOverlay:options.createCompanion}:companion)
    :name==="./steamOverlayFallback"?(options.createFallback?{createSteamOverlayFallback:options.createFallback}:fallback):name==="./steamReturnControllerLayout"?steamLayout:overlay,{
    window:{SteamClient:steam,SteamUIStore:observedStore,ControllerStore:options.controllerStore,setTimeout,clearTimeout},
    fetch:async(url,options)=>{const body=JSON.parse(options.body);calls.push([url.split("/").at(-1),body.requestId]);
      if(url.endsWith("acquire"))assert.equal(body.appId,appId,"actual selected Steam app must reach native guard");return{ok:true,json:async()=>nativeReply};},
    performance:{now:()=>Date.now()},AbortSignal:{timeout:()=>undefined},console:{warn(){}},Date,Math,
  });
  return{calls,deckyCalls,main,mainWindow,ui,observedStore,apps:steam.Apps,stop:api.installControlledQamFocus(),
    system:event=>systemCallback(event),unregistered:()=>unregistered,createMainWindow,
    overlayEvent:(...args)=>overlayCallback?.(...args),
    setMain:(instance,sync=true)=>{if(sync)windowStore.UpdateDesiredWindows(instance);else windowStore.GamepadUIMainWindowInstance=instance;},
    notifyWindowChange:()=>windowCallback(),windowSubscriptions:()=>[windowRegistered,windowUnregistered]};
}

test("the frontend leaves controller layout writes to the ordered native issuer",()=>{
  const controller={nControllerIndex:0,bRemoteDevice:false,unVendorID:0x28de,unProductID:0x1304,eControllerType:44};
  const store=(value,type="controller_steamcontroller_triton")=>({
    GetControllers:()=>[value],GetController:()=>value,GetControllerTypeString:()=>type,
  });
  const cases=[
    [undefined,false],
    [store({...controller,unVendorID:0x045e},"controller_xboxone"),false],
    [store({...controller,unVendorID:0x054c},"controller_ps5"),false],
    [store({...controller,bRemoteDevice:true}),false],
    [store(controller),true],
  ];
  for(const [controllerStore] of cases){
    let select;
    const f=fixture([],{ok:false},2485417537,{companion:true,controllerStore,createCompanion:deps=>{
      select=deps.useSteamMenuLayout;return {activated(){},invalidate(){},dispose(){},guide(){}};
    }});
    try {
      assert.equal(select,undefined);
      assert.deepEqual(f.calls,[]);
    } finally {f.stop();}
  }
});

test("the raw Valve user-initiated flag reaches the companion without being inferred from activity",()=>{
  const events=[];
  const f=fixture([], {ok:false}, 2485417537, {companion:true,createCompanion:()=>({
    activated:(...args)=>events.push(args),invalidate(){},dispose(){},guide(){},
  })});
  try {
    f.overlayEvent(31704,2485417537,false,false);
    f.overlayEvent(31704,2485417537,false,true);
    f.overlayEvent(31704,2485417537,false);
    assert.deepEqual(events,[[31704,2485417537,false,false],[31704,2485417537,false,true],[31704,2485417537,false,undefined]]);
  } finally { f.stop(); }
});

test("stale CEF document focus cannot hide the need for native companion reactivation",async()=>{
  const app=2485417537,gameId=((BigInt(app)<<32n)|0x02000000n).toString();
  let visible,proof={ok:true,gameId,companionPid:27156,overlayPid:31704,foregroundScope:1,overlayOpen:false};
  const candidate={IsGamepadUIOverlayWindow:()=>true,
    params:{browserInfo:{m_gameID:gameId,m_unPID:31704,m_nBrowserID:0}},
    BrowserWindow:{document:{body:{},hasFocus:()=>true}},MenuStore:{GetOpenSideMenu:()=>1}};
  const infos=[{gameID:gameId,appID:app,unPID:31704,nBrowserID:0}];
  const f=fixture(infos,{ok:false},app,{companion:true,overlayWindows:[candidate],rpc:async()=>proof,
    createFallback:deps=>{visible=deps.visible;return {guide(){},invalidate(){},dispose(){}};}});
  try {
    assert.equal(await visible(app,"main"),false);
    proof={...proof,foregroundScope:2,overlayOpen:true};
    assert.equal(await visible(app,"main"),true);
    for(const changed of [{gameId:"180388626432"},{overlayPid:99},{companionPid:undefined}]){
      const prior=proof;proof={...proof,...changed};
      assert.equal(await visible(app,"main"),false);
      proof=prior;
    }
  } finally {f.stop();}
});

test("a stale native visibility reply cannot validate a replaced overlay menu",async()=>{
  const app=2485417537,gameId=((BigInt(app)<<32n)|0x02000000n).toString();
  let visible,finish;
  const candidate={IsGamepadUIOverlayWindow:()=>true,
    params:{browserInfo:{m_gameID:gameId,m_unPID:31704,m_nBrowserID:0}},
    BrowserWindow:{document:{body:{},hasFocus:()=>true}},MenuStore:{GetOpenSideMenu:()=>1}};
  const f=fixture([{gameID:gameId,appID:app,unPID:31704,nBrowserID:0}],{ok:false},app,
    {companion:true,overlayWindows:[candidate],rpc:()=>new Promise(resolve=>{finish=resolve;}),
    createFallback:deps=>{visible=deps.visible;return {guide(){},invalidate(){},dispose(){}};}});
  try {
    const pending=visible(app,"main");await settle();
    candidate.MenuStore={GetOpenSideMenu:()=>1};
    finish({ok:true,gameId,companionPid:27156,overlayPid:31704,foregroundScope:2,overlayOpen:true});
    assert.equal(await pending,false);
  } finally {f.stop();}
});
test("actual main MenuStore events acquire and close without mounted QAM component",async()=>{
  const f=fixture();f.main.OpenSideMenu(2);await settle();
  assert.equal(f.calls[0][0],"acquire");f.main.CloseSideMenus();await settle();assert.equal(f.calls[1][0],"close");f.stop();
});
test("stale focused overlay cannot choose fallback target over explicit SteamUI main",async()=>{
  const f=fixture();f.ui.Navigation.OpenQuickAccessMenu(7);await settle();
  assert.deepEqual(f.calls.map(row=>row[0]),["stale","acquire","main"]);assert.equal(f.calls[2][1],7);f.stop();
});
test("native overlay registration leaves normal Steam menu path untouched",async()=>{
  const f=fixture([{appID:42,gameID:"42",unPID:10,nBrowserID:0}]);f.ui.Navigation.OpenQuickAccessMenu(7);await settle();
  assert.deepEqual(f.calls,[["stale"]]);f.stop();
});
test("ambiguous native identity fails closed without native focus acquire",async()=>{
  const f=fixture([{appID:42,gameID:"42",unPID:10,nBrowserID:0},{appID:42,gameID:"42",unPID:11,nBrowserID:1}]);
  f.main.OpenSideMenu(2);await settle();assert.deepEqual(f.calls.map(row=>row[0]),["release"]);f.stop();
});
test("app navigation releases before action and unpatch restores menu handlers",async()=>{
  const f=fixture();f.main.OpenSideMenu(2);await settle();f.ui.Router.Navigate("/library");await settle();
  assert.deepEqual(f.calls.map(row=>row[0]),["acquire","release","navigate"]);f.stop();f.main.CloseSideMenus();f.main.OpenSideMenu(2);await settle();
  assert.equal(f.calls.length,3);
});
test("real Steam system-key subscription retains native Main/QAM semantics and tears down",async()=>{
  const f=fixture();f.system({eKey:0,nAppID:413080,nControllerIndex:5});await settle();
  assert.equal(f.main.GetOpenSideMenu(),1);assert.equal(f.calls[0][0],"acquire");
  f.stop();assert.equal(f.unregistered(),1);assert.equal(f.calls.at(-1)[0],"release");
});
test("navigation fallback rejection cannot force-open the authoritative main menu",async()=>{
  const f=fixture([], {ok:false});f.ui.Navigation.OpenQuickAccessMenu(7);await settle();
  assert.equal(f.main.GetOpenSideMenu(),0);assert.deepEqual(f.calls.map(row=>row[0]),["stale","acquire"]);f.stop();
});

test("verified UWP with no native overlay invokes neither Game Bar nor a forced menu for Guide or QAM",async()=>{
  for (const eKey of [0,1]) {
    const f=fixture([], {ok:false}, 2485417537);
    f.system({eKey,nAppID:413080,nControllerIndex:5});await settle();
    f.system({eKey,nControllerIndex:5,bPressed:false});f.main.CloseSideMenus();await settle();
    assert.equal(f.main.GetOpenSideMenu(),0);
    assert.deepEqual(f.calls.map(row=>row[0]),["acquire"]);
    assert.deepEqual(f.deckyCalls,[]);
    f.stop();assert.equal(f.unregistered(),1);
  }
});

test("unrelated keys and explicit release do not request a menu or an alternate overlay",async()=>{
  const f=fixture([], {ok:false}, 2485417537);
  for(const event of [{eKey:2},{eKey:0,ePressed:false},{eKey:0,bPressed:false}])f.system(event);
  await settle();assert.deepEqual(f.calls,[]);assert.deepEqual(f.deckyCalls,[]);f.stop();
});

test("Resume preserves Valve's result and sends only an exact shortcut identity for verification",async()=>{
  const f=fixture([], {ok:false}, 2485417537);
  const gameId="10674787038353424384";
  assert.equal(f.apps.RaiseWindowForGame(gameId),"steam-result");
  f.apps.RaiseWindowForGame(10674787038353424384);
  f.apps.RaiseWindowForGame("620");
  f.apps.RaiseWindowForGame("10674787038353424385");
  await settle();
  assert.deepEqual(f.deckyCalls,[["resume_xbox_game",2485417537]]);
  f.stop();
  f.apps.RaiseWindowForGame(gameId);
  assert.equal(f.deckyCalls.length,1);
});

const cupheadApp=2485417537;
const cupheadScope={ok:true,gameId:"10674787038353424384",companionPid:27156};
// These older fixtures exercise only Home's independent ownership probe.
// Direct mirror preparation is now allowed concurrently; it must be a single
// mirror-only call, never the old legacy fallback endpoint.
function homeProbeCalls(f,maxPrepares=1) {
  const prepares=f.deckyCalls.filter(row=>row[0]==="prepare_mirror_presentation");
  assert.ok(prepares.length<=maxPrepares,"one direct preparation per actual Home/System intent");
  for(const row of prepares)assert.deepEqual(row,["prepare_mirror_presentation",cupheadApp]);
  assert.equal(f.deckyCalls.some(row=>row[0]==="request_xbox_steam_overlay"),false,"no legacy open fallback");
  const classifications=f.deckyCalls.filter(row=>row[0]==="get_native_game_overlay");
  for(const row of classifications)assert.deepEqual(row,["get_native_game_overlay",cupheadApp,undefined],"actual explicit packaged classification before mirror fallback");
  assert.ok(classifications.length<=maxPrepares,"one native classification per Home/System intent");
  return f.deckyCalls.filter(row=>row[0]!=="prepare_mirror_presentation"&&row[0]!=="get_native_game_overlay");
}

test("Home ownership probe holds native Home independently of direct mirror-only Guide preparation",async()=>{
  for(const nativeFirst of [true,false]){
    let resolve;
    const scope=new Promise(done=>{resolve=done;});
    const f=fixture([], {ok:true}, cupheadApp,{companion:true,rpc:name=>
      name==="verify_xbox_steam_overlay"?scope:{ok:true}});
    if(nativeFirst) f.mainWindow.OnHomeButtonPressed();
    f.system({eKey:0,nControllerIndex:0,nAppID:413080});
    if(!nativeFirst) f.mainWindow.OnHomeButtonPressed();
    await settle();
    assert.deepEqual(homeProbeCalls(f),[["verify_xbox_steam_overlay",cupheadApp,0,false,false]]);
    assert.equal(f.deckyCalls.filter(row=>row[0]==="prepare_mirror_presentation").length,1,"prepare does not await the unresolved preliminary probe");
    assert.deepEqual(f.calls,[]);
    resolve(cupheadScope);
    await settle();
    assert.deepEqual(homeProbeCalls(f),[["verify_xbox_steam_overlay",cupheadApp,0,false,false]]);
    assert.deepEqual(f.calls,[]);
    assert.equal(f.main.GetOpenSideMenu(),0);
    f.stop();
  }
});

test("explicit companion absence leaves the authoritative native Home behavior available",async()=>{
  const f=fixture([], {ok:false}, cupheadApp,{companion:true,rpc:()=>({ok:false})});
  f.mainWindow.OnHomeButtonPressed();
  f.system({eKey:0,nControllerIndex:0});
  await settle();
  assert.deepEqual(f.calls.map(row=>row[0]).sort(),["acquire","native-home"]);
  assert.equal(f.main.GetOpenSideMenu(),1);
  assert.deepEqual(homeProbeCalls(f),[["verify_xbox_steam_overlay",cupheadApp,0,false,false]]);
  f.stop();
});

test("foreign or malformed companion identity cannot authorize either main-window focus fallback",async()=>{
  for(const scope of [undefined,{ok:true},{...cupheadScope,gameId:"10674787042648391680"},
    {...cupheadScope,companionPid:0},{...cupheadScope,companionPid:Infinity},
    {...cupheadScope,companionPid:1.5},{...cupheadScope,companionPid:0x100000000}]){
    const f=fixture([], {ok:false}, cupheadApp,{companion:true,rpc:()=>scope});
    try {
      f.mainWindow.OnHomeButtonPressed();
      f.system({eKey:0,nControllerIndex:0});
      await settle();
      assert.equal(f.calls.some(row=>row[0]==="acquire"||row[0]==="native-home"),false);
      assert.equal(f.main.GetOpenSideMenu(),0);
      assert.deepEqual(homeProbeCalls(f),[["verify_xbox_steam_overlay",cupheadApp,0,false,false]]);
    } finally { f.stop(); }
  }
});

test("companion transport failure remains unknown and never acquires Big Picture focus",async()=>{
  const f=fixture([], {ok:false}, cupheadApp,{companion:true,rpc:()=>Promise.reject(new Error("transport closed"))});
  try {
    f.mainWindow.OnHomeButtonPressed();
    f.system({eKey:0,nControllerIndex:0});
    await settle();
    assert.equal(f.calls.some(row=>row[0]==="acquire"||row[0]==="native-home"),false);
    assert.equal(f.main.GetOpenSideMenu(),0);
  } finally { f.stop(); }
});

test("ordinary Steam Home is immediate and a registered unrelated shortcut retains native Home",async()=>{
  const regular=fixture([], {ok:false}, 42,{companion:true});
  regular.mainWindow.OnHomeButtonPressed();
  assert.equal(regular.calls[0][0],"native-home");
  await settle();
  assert.deepEqual(regular.deckyCalls,[]);
  regular.stop();
  const registered=fixture([{appID:cupheadApp,gameID:cupheadScope.gameId,unPID:31704,nBrowserID:0}],
    {ok:false},cupheadApp,{companion:true,rpc:()=>({ok:false})});
  registered.mainWindow.OnHomeButtonPressed();
  await settle();
  assert.deepEqual(registered.calls,[["native-home"]]);
  assert.deepEqual(registered.deckyCalls,[["get_native_game_overlay",cupheadApp,undefined],["prepare_mirror_presentation",cupheadApp],["verify_xbox_steam_overlay",cupheadApp,0,false,false]]);
  registered.stop();
});

test("a browser registered during cold Guide cannot make Valve Home acquire companion foreground",async()=>{
  const infos=[];
  let resolve;
  const scope=new Promise(done=>{resolve=done;});
  const f=fixture(infos,{ok:true},cupheadApp,{companion:true,rpc:name=>
    name==="verify_xbox_steam_overlay"?scope:{ok:true}});
  f.system({eKey:0,nControllerIndex:0,nAppID:413080});
  await settle();
  infos.push({appID:cupheadApp,gameID:cupheadScope.gameId,unPID:31704,nBrowserID:0});
  f.mainWindow.OnHomeButtonPressed();
  await settle();
  assert.deepEqual(f.calls,[]);
  assert.deepEqual(homeProbeCalls(f),[["verify_xbox_steam_overlay",cupheadApp,0,false,false]]);
  resolve(cupheadScope);
  await settle();
  assert.deepEqual(homeProbeCalls(f),[["verify_xbox_steam_overlay",cupheadApp,0,false,false]]);
  assert.deepEqual(f.calls,[]);
  assert.equal(f.main.GetOpenSideMenu(),0);
  f.stop();
});

test("recreated Big Picture immediately owns Home, with one hook and no pending call from its predecessor",async()=>{
  let resolveStale,checks=0;
  const staleProof=new Promise(done=>{resolveStale=done;});
  const f=fixture([],{ok:true},cupheadApp,{companion:true,rpc:name=>{
    if(name!=="verify_xbox_steam_overlay")return {ok:true};
    return ++checks===1?staleProof:cupheadScope;
  }});
  f.mainWindow.OnHomeButtonPressed();
  await settle();
  f.setMain(undefined);
  const next=f.createMainWindow("replacement-home");
  f.setMain(next);
  const activeHook=next.OnHomeButtonPressed;
  assert.notEqual(activeHook,next.originalHome);
  f.setMain(next);
  assert.equal(next.OnHomeButtonPressed,activeHook);
  assert.equal(f.mainWindow.OnHomeButtonPressed,f.mainWindow.originalHome);
  assert.equal(f.main.OpenSideMenu,f.mainWindow.originalOpen);
  next.OnHomeButtonPressed();
  f.system({eKey:0,nControllerIndex:0,nAppID:413080});
  resolveStale({ok:false});
  await settle();
  assert.deepEqual(homeProbeCalls(f,2),[["verify_xbox_steam_overlay",cupheadApp,0,false,false],
    ["verify_xbox_steam_overlay",cupheadApp,0,false,false]]);
  assert.deepEqual(f.calls,[]);
  assert.deepEqual(f.windowSubscriptions(),[0,0]);
  f.stop();
  assert.equal(next.OnHomeButtonPressed,next.originalHome);
  assert.equal(next.MenuStore.OpenSideMenu,next.originalOpen);
  assert.deepEqual(f.windowSubscriptions(),[0,0]);
});

test("Steam window notification also finds a replacement created asynchronously",async()=>{
  const f=fixture();
  const next=f.createMainWindow();
  f.notifyWindowChange();
  f.setMain(next,false);
  await new Promise(done=>setTimeout(done,80));
  next.MenuStore.OpenSideMenu(2);
  await settle();
  assert.deepEqual(f.calls.map(row=>row[0]),["acquire"]);
  assert.equal(f.main.OpenSideMenu,f.mainWindow.originalOpen);
  f.stop();
  assert.deepEqual(f.windowSubscriptions(),[0,0]);
});

test("a shortcut change cancels the deferred Valve Home and native Guide request",async()=>{
  let resolve;
  const scope=new Promise(done=>{resolve=done;});
  const f=fixture([], {ok:true}, cupheadApp,{companion:true,rpc:()=>scope});
  f.mainWindow.OnHomeButtonPressed();
  f.system({eKey:0,nControllerIndex:0});
  await settle();
  f.ui.Router.Navigate("/library");
  resolve(cupheadScope);
  await settle();
  assert.deepEqual(homeProbeCalls(f),[]);
  assert.equal(f.deckyCalls.filter(row=>row[0]==="prepare_mirror_presentation").length,1);
  assert.equal(f.calls.some(row=>row[0]==="native-home"||row[0]==="acquire"),false);
  f.stop();
});

test("a Home proof beyond300ms stays unknown; direct mirror preparation never waits for it or retries legacy",async()=>{
  let resolve;
  const scope=new Promise(done=>{resolve=done;});
  const f=fixture([], {ok:false}, cupheadApp,{companion:true,rpc:()=>scope});
  try {
    f.mainWindow.OnHomeButtonPressed();
    f.system({eKey:0,nControllerIndex:0});
    await new Promise(done=>setTimeout(done,340));
    assert.equal(f.calls.some(row=>row[0]==="acquire"||row[0]==="native-home"),false);
    assert.equal(f.main.GetOpenSideMenu(),0);
    resolve(cupheadScope);
    await settle();
    assert.equal(f.calls.some(row=>row[0]==="acquire"||row[0]==="native-home"),false);
    assert.deepEqual(homeProbeCalls(f),[["verify_xbox_steam_overlay",cupheadApp,0,false,false]]);
  } finally { f.stop(); }
});

test("late companion absence after timeout and main-window replacement cannot revive the old Home request",async()=>{
  let resolve;
  const scope=new Promise(done=>{resolve=done;});
  const f=fixture([], {ok:false}, cupheadApp,{companion:true,rpc:()=>scope});
  try {
    f.mainWindow.OnHomeButtonPressed();
    f.system({eKey:0,nControllerIndex:0});
    await new Promise(done=>setTimeout(done,340));
    const next=f.createMainWindow("replacement-home");
    f.setMain(next);
    resolve({ok:false});
    await settle();
    assert.equal(f.calls.some(row=>row[0]==="acquire"||row[0]==="native-home"||row[0]==="replacement-home"),false);
    assert.equal(next.MenuStore.GetOpenSideMenu(),0);
    assert.equal(f.main.GetOpenSideMenu(),0);
  } finally { f.stop(); }
});

test("an owned menu unpatch failure cannot skip the other cleanup or prevent preparation rollback",async()=>{
  let side=0,verifications=0;
  const removed=[];
  const menu={
    GetOpenSideMenu:()=>side,
    OpenSideMenu(next){side=next;},
    OpenMainMenu(){this.OpenSideMenu(1);},
    CloseSideMenus(){side=0;},
  };
  const originalOpen=menu.OpenSideMenu,originalClose=menu.CloseSideMenus;
  const browser={appID:cupheadApp,gameID:cupheadScope.gameId,unPID:31704,nBrowserID:0};
  const target={IsGamepadUIOverlayWindow:()=>true,
    params:{browserInfo:{m_gameID:browser.gameID,m_unPID:browser.unPID,m_nBrowserID:browser.nBrowserID}},
    BrowserWindow:{document:{body:{}}},MenuStore:menu};
  const proof={...cupheadScope,overlayPid:31704,foregroundScope:2,overlayOpen:true,keyboardAt:9500};
  const f=fixture([browser],{ok:true},cupheadApp,{companion:true,
    createCompanion:dependencies=>companion.createXboxCompanionOverlay({...dependencies,
      windows:()=>[target],delay:async()=>{},now:()=>10000,
      verify:async()=>++verifications===3?{ok:false}:proof,
    }),
    afterUnpatch:(object,key)=>{
      if(object!==menu)return;
      removed.push(key);
      if(key==="CloseSideMenus")throw new Error("Closing Steam descriptor is no longer available");
    },
  });
  try {
    f.overlayEvent(31704,cupheadApp,true);
    await settle();
    assert.equal(verifications,3);
    assert.deepEqual(removed,["CloseSideMenus","OpenSideMenu"]);
    assert.equal(menu.OpenSideMenu,originalOpen);
    assert.equal(menu.CloseSideMenus,originalClose);
    assert.equal(side,0,"finally must roll back its own newly prepared menu after both unpatch attempts");
    assert.equal(f.calls.some(row=>row[0]==="overlay-state"),false);
  } finally { f.stop(); }
});

test("Home alone reaches authenticated mirror preparation and Shown without a SystemKey callback",async()=>{
  const gameId="10674787038353424384";let side=0;const state={protocol:1,state:"prepared",sessionId:"a".repeat(64),generation:"1",gameId,
    expiresAt:"1400",observedAt:"1000",sourceHwnd:"41001",sourceCookie:"18446744073709551614",destinationHwnd:"41002",destinationCookie:"18446744073709551615",
    ownerPid:27156,ownerBirth:"134389155136169895",gamePid:48584,gameBirth:"134389155148953005",preparedCurrent:true,shownCurrent:false,leaseCurrent:true,cleanupComplete:false,actualScope:1};
  const reply=()=>({ok:true,gameId,overlayPid:31704,companionPid:27156,mirror:{...state}});
  const menu={GetOpenSideMenu:()=>side,OpenMainMenu(){side=1;},OpenQuickAccessMenu(){side=2;},GetQuickAccessTab:()=>0};
  const target={IsGamepadUIOverlayWindow:()=>true,params:{browserInfo:{m_gameID:gameId,m_unPID:31704,m_nBrowserID:0}},MenuStore:menu,BrowserWindow:{document:{body:{}}}};
  const f=fixture([{appID:cupheadApp,gameID:gameId,unPID:31704,nBrowserID:0}],{ok:true},cupheadApp,{companion:true,overlayWindows:[target],rpc:async(name,...args)=>{
    if(name==="prepare_mirror_presentation"||name==="get_mirror_presentation")return reply();
    if(name==="set_mirror_presentation"){if(args[2]==="show"){state.state="shown";state.shownCurrent=true;}else{state.state="closed";state.preparedCurrent=false;state.shownCurrent=false;state.leaseCurrent=false;state.cleanupComplete=true;state.destinationHwnd="0";state.destinationCookie="0";}return{ok:true};}
    throw Error("No ownership preprobe or legacy fallback is expected");
  }});
  f.mainWindow.OnHomeButtonPressed();await settle();
  assert.equal(state.state,"shown");assert.equal(side,1);
  assert.equal(f.deckyCalls.filter(row=>row[0]==="prepare_mirror_presentation").length,1);
  assert.equal(f.deckyCalls.some(row=>row[0]==="verify_xbox_steam_overlay"||row[0]==="request_xbox_steam_overlay"),false);
  assert.equal(f.calls.some(row=>row[0]==="native-home"||row[0]==="acquire"),false);f.stop();
});

const ownedRouteFixture={uiObservedApp:480,canonicalShortcutAppId:2262950469,originalGameId:"9719298256856416256",observedSteamGameId:"480",gamePid:300,gameBirth:"134389155148953005",wrapperPid:200,wrapperBirth:"134389155136169895",steamPid:100,steamBirth:"134389155100000000",observedFresh:true,routingOnly:true};
test("actual SystemKey alias dispatch uses one readonly discovery and expected canonical prepare",async()=>{
 const f=fixture([], {ok:false},480,{companion:true,rpc:(method)=>method==="get_owned_game_route"?{ok:true,route:ownedRouteFixture}:{ok:false}});
 try{f.apps.RunGame(ownedRouteFixture.originalGameId);f.system({eKey:0,nAppID:0});for(let i=0;i<5;i++)await settle();
 assert.equal(f.deckyCalls.filter(c=>c[0]==="get_owned_game_route").length,1);
 const prepare=f.deckyCalls.find(c=>c[0]==="prepare_owned_mirror_presentation");assert.ok(prepare);assert.equal(prepare[1],2262950469);assert.equal(prepare[2].gameBirth,ownedRouteFixture.gameBirth);
 assert.equal(f.deckyCalls.some(c=>c[0]==="request_xbox_steam_overlay"),false);assert.equal(f.ui.Router.MainRunningAppID,480);
 }finally{f.stop();}
});
test("actual native RunGame480 never creates an owned alias lookup",async()=>{
 const f=fixture([], {ok:false},480,{companion:true});try{f.apps.RunGame("480");f.system({eKey:0,nAppID:480});for(let i=0;i<3;i++)await settle();assert.equal(f.deckyCalls.some(c=>c[0]==="get_owned_game_route"),false);}finally{f.stop();}
});
test("new canonical launch during readonly alias discovery cannot dispatch old expected tuple",async()=>{
 let resolve;const pending=new Promise(r=>resolve=r);const f=fixture([], {ok:false},480,{companion:true,rpc:method=>method==="get_owned_game_route"?pending:{ok:false}});
 try{f.apps.RunGame(ownedRouteFixture.originalGameId);f.system({eKey:0});await settle();f.apps.RunGame(((2979426017n<<32n)|0x02000000n).toString());resolve({ok:true,route:ownedRouteFixture});for(let i=0;i<4;i++)await settle();assert.equal(f.deckyCalls.some(c=>c[0]==="prepare_owned_mirror_presentation"),false);}finally{f.stop();}
});

const dawnSnapshot={uiObservedApp:null,canonicalShortcutAppId:2979426017,originalGameId:"12796537303900094464",observedSteamGameId:"12796537303900094464",gamePid:42208,gameBirth:"134389331028380054",wrapperPid:53920,wrapperBirth:"134389331027672812",steamPid:44684,steamBirth:"134388877303141800",observedFresh:true,routingOnly:true};
for(const ui of [undefined,0])test(`actual nullable running-app getter ${ui} retains original Dawn route and never uses stale Router as observed ID`,async()=>{
 const expected={...dawnSnapshot,uiObservedApp:ui===undefined?null:ui};const f=fixture([],{ok:false},2979426017,{companion:true,uiObserved:ui,rpc:method=>method==="get_owned_game_route"?{ok:true,route:expected}:{ok:false}});
 try{f.apps.RunGame(dawnSnapshot.originalGameId);f.system({eKey:0,nAppID:0});for(let i=0;i<6;i++)await settle();
 const discover=f.deckyCalls.find(x=>x[0]==="get_owned_game_route");assert.ok(discover);assert.equal(discover[1],expected.uiObservedApp);assert.equal(discover[2],2979426017);
 const prepare=f.deckyCalls.find(x=>x[0]==="prepare_owned_mirror_presentation");assert.ok(prepare);assert.equal(prepare[1],2979426017);assert.equal(prepare[2].observedSteamGameId,dawnSnapshot.originalGameId);assert.equal(prepare[2].gameBirth,"134389331028380054");
 assert.equal(f.observedStore.MainRunningAppID,ui);assert.equal(f.ui.Router.MainRunningAppID,2979426017);assert.equal(f.deckyCalls.some(x=>x[0]==="request_xbox_steam_overlay"),false);
 }finally{f.stop();}
});

test("missing optional overlay APIs keep ordinary HUD/return routing synchronous and unchanged",()=>{
 const f=fixture([],{ok:false},42);
 try{assert.equal(ownedLaunch.resolveOwnedGameApp(42),42);assert.equal(ownedLaunch.readOwnedGameApp(42),42);}finally{f.stop();}
});

import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';
import ts from 'typescript';
const compile=name=>ts.transpileModule(fs.readFileSync(new URL(`../src/${name}.ts`,import.meta.url),'utf8'),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText;
function load(name,imports={},globals={}){const exports={};vm.runInNewContext(compile(name),{exports,require:key=>{assert.ok(key in imports,key);return imports[key];},...globals});return exports;}
const canonical=2262950469,full='9719298256856416256';
function fixture(observed=0){
 const module=load('steamOwnedLaunchDispatcher');let revision=1,context={},disposed=false,raw=observed,calls=0,signals=0,pending;
 const route=()=>({uiObservedApp:raw,canonicalShortcutAppId:canonical,originalGameId:full,observedSteamGameId:'480',gamePid:54572,gameBirth:'134389769499138508',wrapperPid:51052,wrapperBirth:'134389769497796417',steamPid:52872,steamBirth:'134389620696131260',observedFresh:true,routingOnly:true});
 const dispatcher=module.createOwnedLaunchDispatcher({scope:()=>({observedApp:raw,revision,context,disposed,enabled:true,runningShortcuts:[canonical]}),now:()=>1000,
 discover:async()=>{calls++;if(pending)return await pending;return{ok:true,route:route()};},signal:()=>{signals++;return true;},schedule:()=>1,cancel(){}});
 const release=module.registerOwnedGameConsumer(dispatcher);
 return{module,dispatcher,release,route,get calls(){return calls;},get signals(){return signals;},set raw(v){raw=v;},set context(v){context=v;},set revision(v){revision=v;},set disposed(v){disposed=v;},defer(v){pending=v;}};
}
for(const observed of [0,480])test(`readonly consumers resolve original canonical from exact observed ${observed} without Guide/input`,async()=>{
 const f=fixture(observed);assert.equal(f.module.readOwnedGameApp(observed),observed);assert.equal(await f.module.resolveOwnedGameApp(observed),canonical);
 assert.equal(f.module.readOwnedGameApp(0),canonical);assert.equal(f.module.readOwnedGameApp(480),canonical);assert.equal(f.module.readOwnedGameApp(42),42);
 assert.equal(f.module.readOwnedGameRoute(canonical).originalGameId,full);assert.equal(f.calls,1);assert.equal(f.signals,0);f.release();assert.equal(f.module.readOwnedGameApp(0),0);
});
for(const field of ['context','revision','disposed'])test(`consumer cached routing cannot survive ${field} replacement`,async()=>{
 const f=fixture();await f.module.resolveOwnedGameApp(0);f[field]=field==='context'?{}:field==='revision'?2:true;
 assert.equal(f.module.readOwnedGameApp(0),0);assert.equal(f.module.readOwnedGameRoute(canonical),undefined);f.release();
});
test('late old consumer disposal cannot clear the next plugin root',async()=>{
 const f=fixture();await f.module.resolveOwnedGameApp(0);const second={app:()=>42,expected:()=>undefined,resolveApp:async()=>42};
 const release=f.module.registerOwnedGameConsumer(second);f.release();assert.equal(await f.module.resolveOwnedGameApp(42),42);release();assert.equal(f.module.resolveOwnedGameApp(42),42);
});
test('awaited discovery cannot publish a replaced consumer owner',async()=>{
 const f=fixture();let finish;f.defer(new Promise(resolve=>finish=resolve));const before=f.module.resolveOwnedGameApp(0);
 const release=f.module.registerOwnedGameConsumer({app:()=>42,expected:()=>undefined,resolveApp:async()=>42});finish({ok:true,route:f.route()});assert.equal(await before,0);f.release();release();
});
test('failed readonly lookup cannot relabel an ordinary raw identity as the owned shortcut',async()=>{
 const f=fixture(480);f.defer(Promise.resolve({ok:false}));assert.equal(await f.module.resolveOwnedGameApp(480),0);assert.equal(f.signals,0);f.release();
});
test('actual HUD controller configures canonical after readonly resolution and never mutates input',async()=>{
 const f=fixture(), controllerModule=load('quickSettings/livePerformanceController');let state={enabled:true,detail:4,corner:1,transparent:true,refreshMs:20,status:'Idle',appId:0,lease:null};const sent=[];
 const controller=controllerModule.createLivePerformanceController({app:()=>f.module.readOwnedGameApp(0),resolveApp:async()=>f.module.resolveOwnedGameApp(0),suspended:()=>false,get:async()=>state,
 send:async request=>{sent.push(request);state={...state,...request,lease:request.action==='stop'?null:'1'.repeat(32),status:'Ready'};return{ok:true,state};},schedule:()=>1,cancel(){}});
 await controller.refresh();assert.equal(sent[0].action,'configure');assert.equal(sent[0].appId,canonical);assert.equal(controller.read().state.appId,canonical);assert.equal(f.signals,0);controller.dispose();f.release();
});
test('actual HUD menu watcher matches exact mapped observed token and keeps unrelated callback separate',async()=>{
 const f=fixture();await f.module.resolveOwnedGameApp(0);let activated;const host=54592;
 const entry={params:{browserInfo:{m_gameID:'480',m_unPID:host,m_nBrowserID:9175043,m_unAppID:480}},IsGamepadUIOverlayWindow:()=>true,MenuStore:{GetOpenSideMenu:()=>0}};
 const companion={params:{browserInfo:{m_gameID:full,m_unPID:56716,m_nBrowserID:22,m_unAppID:canonical}},IsGamepadUIOverlayWindow:()=>true,MenuStore:{GetOpenSideMenu:()=>0}};
 const store={OverlayWindows:[entry,companion],m_mapOverlayPopupByPID:new Map([[host,entry],[56716,companion]])};
 const window={settingsStore:{},SteamUIStore:{MainRunningAppID:0,WindowStore:store},SteamClient:{Overlay:{RegisterForOverlayActivated(cb){activated=cb;return{unregister(){}};}}},setTimeout:()=>1,clearTimeout(){}};
 const fake={read:()=>({state:{enabled:true}}),start(){},dispose(){},refresh:async()=>{},menuChanged:async()=>{}};
 const runtime=load('quickSettings/livePerformanceRuntime',{'../decky':{DFL:{}},'../steamOwnedLaunchDispatcher':f.module,'../controlBackend':{call(){assert.fail('no RPC');}},'../controlledQamFocus':{steamAppId:x=>Number(x)>>>0},'./livePerformanceController':{createLivePerformanceController:()=>fake},'./steamPerformanceMonitor':load('quickSettings/steamPerformanceMonitor'),'./steamPerformanceOverlayIdentity':load('quickSettings/steamPerformanceOverlayIdentity')},{window});
 const cleanup=runtime.installLivePerformance();assert.equal(runtime.menuOpen(),false);activated(host,480,true);assert.equal(runtime.menuOpen(),true);activated(56716,canonical,false);assert.equal(runtime.menuOpen(),true);
 entry.params.browserInfo.m_nBrowserID++;assert.equal(runtime.menuOpen(),false);entry.params.browserInfo.m_nBrowserID--;activated(host,480,false);assert.equal(runtime.menuOpen(),false);
 store.m_mapOverlayPopupByPID.delete(host);entry.MenuStore.GetOpenSideMenu=()=>2;assert.equal(runtime.menuOpen(),false);cleanup();f.release();
});

test('actual canonical UI with alias renderer suspends HUD through the admitted native owner without alias discovery',async()=>{
 const shared=load('steamOwnedLaunchDispatcher'),nativeModule=load('steamNativeGameOverlay');let side=0,activate;const calls=[];
 const proof={protocol:1,canonicalAppId:canonical,fullGameId:full,observedGameId:'480',sessionId:'a'.repeat(64),
  gamePid:46176,gameBirth:'134389799095389220',wrapperPid:28508,wrapperBirth:'134389799094389220',steamPid:52872,steamBirth:'134389620696131260',
  companionPid:41308,companionBirth:'134389799096389220',overlayPid:42248,overlayBirth:'134389799097389220',gameWindow:'100',observedAt:'1000',expiresAt:'2500',routingOnly:true};
 const menu={GetOpenSideMenu:()=>side,GetQuickAccessTab:()=>0,OpenMainMenu(){side=1},OpenQuickAccessMenu(){side=2}};
 const info={unPID:42248,nBrowserID:524295,appID:480,gameID:'480'};
 const entry={BrowserWindow:{document:{}},MenuStore:menu,params:{browserInfo:{m_unPID:42248,m_nBrowserID:524295,m_unAppID:480,m_gameID:'480'}},IsGamepadUIOverlayWindow:()=>true};
 const store={OverlayWindows:[entry],m_mapOverlayPopupByPID:new Map([[42248,entry]])};
 const native=nativeModule.createSteamNativeGameOverlay({scope:()=>({app:canonical,observedApp:canonical,revision:1,disposed:false}),now:()=>1000,
  proof:async()=>({ok:true,kind:'native-game',nativeGame:proof}),browserInfo:async()=>[info],windowStore:()=>store,
  input:async request=>{calls.push(request.action);return{ok:true,inputMode:'owned-relay',inputReady:true,ownerToken:'b'.repeat(32),generation:'1',nativeGame:proof,closed:request.action==='closed',cleanupComplete:request.action==='closed'}},
  setOverlayState:async()=>{calls.push('native-dispatch')},schedule:()=>1,cancel(){}});
 const dispatcher=shared.createOwnedLaunchDispatcher({scope:()=>({observedApp:canonical,revision:1,context:store,disposed:false,enabled:true,runningShortcuts:[canonical]}),now:()=>1000,
  discover:async()=>assert.fail('canonical path needs no alias RPC'),signal:()=>assert.fail('no input'),schedule:()=>1,cancel(){}});
 const release=shared.registerOwnedGameConsumer({app:()=>dispatcher.app(),expected:a=>dispatcher.expected(a),resolveApp:()=>dispatcher.resolveApp(),nativeOverlay:a=>native.visibility(a)});
 const window={settingsStore:{},SteamUIStore:{MainRunningAppID:canonical,WindowStore:store},SteamClient:{Overlay:{RegisterForOverlayActivated(cb){activate=cb;return{unregister(){}}}}},setTimeout:()=>1,clearTimeout(){}};
 const fake={read:()=>({state:{enabled:true}}),start(){},dispose(){},refresh:async()=>{},menuChanged:async()=>{}};
 const runtime=load('quickSettings/livePerformanceRuntime',{'../decky':{DFL:{}},'../steamOwnedLaunchDispatcher':shared,'../controlBackend':{call(){assert.fail('no RPC');}},'../controlledQamFocus':{steamAppId:x=>Number(x)>>>0},'./livePerformanceController':{createLivePerformanceController:()=>fake},'./steamPerformanceMonitor':load('quickSettings/steamPerformanceMonitor'),'./steamPerformanceOverlayIdentity':load('quickSettings/steamPerformanceOverlayIdentity')},{window});
 const cleanup=runtime.installLivePerformance();assert.equal(shared.readOwnedGameRoute(canonical),undefined);
 assert.equal(runtime.menuOpen(),false);await native.guide('main');assert.equal(runtime.menuOpen(),true,'native owner binding hides HUD over Main even when canonical needs no alias route');
 native.activated(42248,480,true);await native.eventsSettled();activate(42248,480,true);side=0;
 assert.equal(runtime.menuOpen(),true,'exact actual native TRUE still hides HUD with side-menu0');
 const count=calls.length;runtime.menuOpen();shared.readOwnedNativeOverlay(canonical);assert.equal(calls.length,count,'display getters dispatch no RPC/input');
 assert.equal(shared.readOwnedGameApp(480),480,'visibility binding is never a global480 routing map');
 entry.params.browserInfo.m_nBrowserID++;assert.equal(runtime.menuOpen(),false,'replacement BID cannot borrow native visibility');entry.params.browserInfo.m_nBrowserID--;
 native.activated(42248,480,false);await native.eventsSettled();activate(42248,480,false);assert.equal(runtime.menuOpen(),false,'real false and cleanup retirement permit HUD again');
 cleanup();release();await native.dispose();
});
test('visibility consumer rejects late root replacement without clearing its successor',()=>{
 const m=load('steamOwnedLaunchDispatcher');let releaseNext;
 const release=m.registerOwnedGameConsumer({app:()=>canonical,expected:()=>undefined,resolveApp:async()=>canonical,nativeOverlay:()=>{
  releaseNext=m.registerOwnedGameConsumer({app:()=>42,expected:()=>undefined,resolveApp:async()=>42});
  return{app:canonical,entry:{},pid:1,browserId:2,nativeApp:480,rendererToken:'480',active:true,serial:'1'};}});
 assert.equal(m.readOwnedNativeOverlay(canonical),undefined);release();assert.equal(m.readOwnedGameApp(42),42);releaseNext();
});

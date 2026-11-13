import assert from "node:assert/strict";
import {readFileSync,existsSync} from "node:fs";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";
const exports = {};
vm.runInNewContext(ts.transpileModule(readFileSync(new URL("../src/steamOwnedLaunchDispatcher.ts",import.meta.url),"utf8"),
  {compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText,{exports});
const {createOwnedLaunchDispatcher,parseOwnedLaunchRoute,readSteamGuideReceiver}=exports;
const canonical=2262950469,full=((BigInt(canonical)<<32n)|0x02000000n).toString();
const route={uiObservedApp:480,canonicalShortcutAppId:canonical,originalGameId:full,observedSteamGameId:"480",gamePid:300,gameBirth:"134389155148953005",
  wrapperPid:200,wrapperBirth:"134389155136169895",steamPid:100,steamBirth:"134389155100000000",observedFresh:true,routingOnly:true};
const flush=async()=>{for(let i=0;i<8;i++)await Promise.resolve();};
function fixture(){
 const scope={observedApp:480,revision:1,context:{},runningShortcuts:[],disposed:false,enabled:true};
 scope.receiver={main:{},browser:{},document:{},focused:true,windowFocus:true,activeMain:true,focusStore:{},focusApp:0,focusPid:0,focusedApp:0,focusedWindow:0};
 const calls=[],timers=new Map();let clock=1000,id=0,reads=0,resolve;
 const reply=new Promise(r=>resolve=r);
 const f={scope,calls,timers,route,get reads(){return reads;},get clock(){return clock;},set clock(v){clock=v;},resolve};
 f.router=createOwnedLaunchDispatcher({scope:()=>scope,now:()=>clock,
  discover:async observed=>{reads++;assert.equal(observed,scope.observedApp);return f.nextReply??reply;},
  signal:(...args)=>{calls.push(args);return true;},schedule:(cb,ms)=>{assert.equal(ms,1800);timers.set(++id,cb);return id;},cancel:key=>timers.delete(key)});
 return f;
}
test("raw480 alone is ordinary and causes no discovery or canonical override",()=>{const f=fixture();assert.equal(f.router.signal("home","main"),false);assert.equal(f.reads,0);assert.equal(f.router.app(),480);});
test("ordinary canonical guide has no extra discovery RPC",()=>{const f=fixture();f.scope.observedApp=canonical;assert.equal(f.router.signal("system","quick"),true);assert.equal(f.reads,0);assert.equal(f.router.app(),canonical);assert.equal(f.calls.length,1);});
test("canonical RunGame candidate permits only native-verified alias routing",async()=>{const f=fixture();f.router.noteLaunch(full);assert.equal(f.router.signal("home","main"),true);assert.equal(f.router.app(),480);f.resolve({ok:true,route});await flush();assert.equal(f.reads,1);assert.equal(f.router.app(),canonical);assert.equal(f.calls.length,1);assert.equal(f.router.expected(canonical).gameBirth,route.gameBirth);assert.equal(f.timers.size,0);});
test("late System/Home callbacks share one lookup but retain original origins",async()=>{const f=fixture();f.router.noteLaunch(full);f.router.signal("home","main");f.clock+=220;f.router.signal("system","main");f.resolve({ok:true,route});await flush();assert.equal(f.reads,1);assert.deepEqual(f.calls.map(x=>x[0]),["home","system"]);});
test("Quick during lookup reaches the same common admission after Main",async()=>{const f=fixture();f.router.noteLaunch(full);f.router.signal("system","main");f.router.signal("quick","quick");f.resolve({ok:true,route});await flush();assert.deepEqual(f.calls.map(x=>x[1]),["main","quick"]);assert.equal(f.reads,1);});
test("cache routes repeated alias guides without discovery and preserves same-origin edges",async()=>{const f=fixture();f.router.noteLaunch(full);f.router.signal("system","main");f.resolve({ok:true,route});await flush();f.router.signal("system","main");assert.equal(f.reads,1);assert.equal(f.calls.length,2);});
test("native RunGame480 clears candidate and never creates a480mapping",()=>{const f=fixture();f.scope.runningShortcuts=[canonical];f.router.noteLaunch("480");assert.equal(f.router.signal("home","main"),false);assert.equal(f.reads,0);});
test("reload can use an active running-shortcut candidate, still requiring exact native proof",async()=>{const f=fixture();f.scope.runningShortcuts=[canonical];f.router.signal("home","main");f.resolve({ok:true,route});await flush();assert.equal(f.router.app(),canonical);assert.equal(f.reads,1);});
for(const [name,change]of [["context",f=>f.scope.context={}],["revision",f=>f.scope.revision++],["observed",f=>f.scope.observedApp=570],["dispose",f=>f.router.dispose()]])
 test(`${name} replacement during lookup cannot dispatch late`,async()=>{const f=fixture();f.router.noteLaunch(full);f.router.signal("home","main");change(f);f.resolve({ok:true,route});await flush();assert.equal(f.calls.length,0);assert.notEqual(f.router.app(),canonical);assert.equal(f.timers.size,0);});
test("unconfirmed lookup never calls original Home fallback",async()=>{const f=fixture();let original=0;f.router.noteLaunch(full);f.router.signal("home","main",()=>original++);f.resolve({ok:false});await flush();assert.equal(original,0);assert.equal(f.calls.length,0);});
test("bounded timeout revokes lookup without a late dispatch",async()=>{const f=fixture();f.router.noteLaunch(full);f.router.signal("home","main");[...f.timers.values()][0]();await flush();f.resolve({ok:true,route});await flush();assert.equal(f.calls.length,0);assert.equal(f.timers.size,0);});
test("different launch while lookup pending cancels old receipt",async()=>{const f=fixture();f.router.noteLaunch(full);f.router.signal("system","main");f.router.noteLaunch(((2979426017n<<32n)|0x02000000n).toString());f.resolve({ok:true,route});await flush();assert.equal(f.calls.length,0);assert.equal(f.timers.size,0);});
for(const [name,bad]of [["rounded birth",{...route,gameBirth:Number(route.gameBirth)}],["different observed",{...route,observedSteamGameId:"570"}],["fake full ID",{...route,originalGameId:"480"}],["reused role",{...route,gamePid:200}],["authorization flag",{...route,routingOnly:false}]])
 test(`rejects ${name}`,()=>assert.equal(parseOwnedLaunchRoute(bad,480),undefined));
test("all births/full IDs remain opaque exact strings",()=>{const r=parseOwnedLaunchRoute(route,480);assert.equal(r.gameBirth,"134389155148953005");assert.equal(r.originalGameId,full);});

test("positive ordinary proof permits original Home but not an alias route",async()=>{const f=fixture();let original=0;f.scope.runningShortcuts=[canonical];f.router.signal("system","main");f.router.signal("home","main",()=>original++);f.resolve({ok:true,kind:"absent",receiver:"ordinary-game"});await flush();assert.equal(original,1);assert.equal(f.calls.length,0);assert.equal(f.router.app(),480);assert.equal(f.reads,1);});
test("late ordinary proof cannot call fallback after context replacement",async()=>{const f=fixture();let original=0;f.scope.runningShortcuts=[canonical];f.router.signal("home","main",()=>original++);f.scope.context={};f.resolve({ok:true,kind:"absent",receiver:"ordinary-game"});await flush();assert.equal(original,0);});
test("unconfirmed kind absent is not fallback permission",async()=>{const f=fixture();let original=0;f.scope.runningShortcuts=[canonical];f.router.signal("home","main",()=>original++);f.resolve({ok:false,kind:"absent",receiver:"ordinary-game"});await flush();assert.equal(original,0);});

test("candidate overflow remains unknown with a valid hint and never invokes original Home",async()=>{
 const f=fixture();let original=0;f.router.noteLaunch(full);
 f.scope.runningShortcuts=Array.from({length:9},(_,i)=>canonical+i);
 assert.equal(f.router.signal("home","main",()=>original++),true);
 await flush();assert.equal(f.reads,0);assert.equal(original,0);assert.equal(f.calls.length,0);
 assert.equal(f.router.expected(canonical),undefined);assert.equal(f.router.app(),480);
});

for(const ui of [null,0])test(`UI${ui} is no global alias; long lived original hint needs fresh server proof for each Guide`,async()=>{const f=fixture();f.scope.observedApp=ui;assert.equal(f.router.signal("home","main"),false);assert.equal(f.reads,0);f.router.noteLaunch(full);f.clock+=7200000;f.router.signal("system","main");const raw={...route,uiObservedApp:ui,observedSteamGameId:full};f.resolve({ok:true,route:raw});await flush();assert.equal(f.router.app(),canonical);assert.equal(f.calls.length,1);assert.equal(f.router.expected(canonical).uiObservedApp,ui);assert.equal(f.router.expected(canonical).observedSteamGameId,full);f.router.signal("system","main");await flush();assert.equal(f.reads,2);assert.equal(f.calls.length,2);});
test("raw-null actual480 remains a separate log identity",()=>{const f=parseOwnedLaunchRoute({...route,uiObservedApp:null},null);assert.equal(f.observedSteamGameId,"480");assert.equal(f.uiObservedApp,null);assert.equal(parseOwnedLaunchRoute({...route,uiObservedApp:0},null),undefined);});
test("raw-null unknown after prior proof cannot dispatch from routing cache",async()=>{const f=fixture();f.scope.observedApp=null;f.router.noteLaunch(full);f.resolve({ok:true,route:{...route,uiObservedApp:null,observedSteamGameId:full}});f.router.signal("system","main");await flush();assert.equal(f.calls.length,1);f.nextReply=Promise.resolve({ok:false});let fallback=0;f.router.signal("home","main",()=>fallback++);await flush();assert.equal(f.reads,2);assert.equal(f.calls.length,1);assert.equal(fallback,0);assert.equal(f.router.app(),0);});
test("positive ordinary receiver proof preserves original Home and never retires game by UI idle",async()=>{const f=fixture();f.scope.observedApp=null;f.router.noteLaunch(full);f.clock+=999999;let home=0;f.router.signal("home","main",()=>home++);f.resolve({ok:true,kind:"absent",receiver:"ordinary-game"});await flush();assert.equal(home,1);assert.equal(f.router.app(),0);f.router.signal("system","main");await flush();assert.equal(f.reads,2);});
test("nullable UI change while awaiting cannot deliver an old receipt",async()=>{const f=fixture();f.scope.observedApp=null;f.router.noteLaunch(full);f.router.signal("system","main");f.scope.observedApp=0;f.resolve({ok:true,route:{...route,uiObservedApp:null,observedSteamGameId:full}});await flush();assert.equal(f.calls.length,0);});

test("missing UI and multiple reload candidates remain unknown without an unbound query",()=>{const f=fixture();f.scope.observedApp=null;f.scope.runningShortcuts=[2262950469,2979426017];assert.equal(f.router.signal("home","main"),true);assert.equal(f.reads,0);});
test("retained candidate is not retired by an uncertain monotonic clock",()=>{const f=fixture();f.scope.observedApp=null;f.router.noteLaunch(full);f.clock=1;assert.equal(f.router.signal("home","main"),true);assert.equal(f.reads,0);});

for(const field of ["main","browser","document","focused","windowFocus","activeMain","focusStore","focusApp","focusPid","focusedApp","focusedWindow"])
 test(`late Steam UI absence cannot call original after receiver ${field} changes`,async()=>{const f=fixture();f.scope.observedApp=null;f.router.noteLaunch(full);let home=0;f.router.signal("home","main",()=>home++);const old=f.scope.receiver[field];f.scope.receiver[field]=typeof old==="boolean"?!old:typeof old==="number"?old+1:{};f.resolve({ok:true,kind:"absent",receiver:"steam-ui"});await flush();assert.equal(home,0);assert.equal(f.calls.length,0);});
test("positive current Steam UI receiver may preserve original Home",async()=>{const f=fixture();f.scope.observedApp=null;f.router.noteLaunch(full);let home=0;f.router.signal("home","main",()=>home++);f.resolve({ok:true,kind:"absent",receiver:"steam-ui"});await flush();assert.equal(home,1);});
test("unknown receiver cannot use a server absence claim as client focus authority",async()=>{const f=fixture();f.scope.observedApp=null;f.scope.receiver=undefined;f.router.noteLaunch(full);let home=0;f.router.signal("home","main",()=>home++);f.resolve({ok:true,kind:"absent",receiver:"steam-ui"});await flush();assert.equal(home,0);});
test("receiver facts never invalidate an owned routing receipt or become native foreground proof",async()=>{const f=fixture();f.scope.observedApp=null;f.router.noteLaunch(full);f.router.signal("system","main");f.scope.receiver.focused=false;f.resolve({ok:true,route:{...route,uiObservedApp:null,observedSteamGameId:full}});await flush();assert.equal(f.calls.length,1);assert.equal(f.router.app(),canonical);});
test("receiver reader uses only exact readonly refs and committed native focus fields",()=>{const doc={hasFocus:()=>true,body:{classList:{contains:()=>true}}},browser={document:doc},main={BrowserWindow:browser},store={GetFocusedOverlayAppID:()=>0,GetFocusedOverlayPID:()=>0,GetFocusedAppID:()=>413080,GetFocusedWindowID:()=>1};const r=readSteamGuideReceiver(main,main,store);assert.equal(r.document,doc);assert.equal(r.activeMain,true);assert.equal(r.focusPid,0);store.GetFocusedOverlayPID=()=>{throw Error("unknown")};assert.equal(readSteamGuideReceiver(main,main,store),undefined);});

test("missing actual focus token is not permission for late ordinary fallback",()=>{const doc={hasFocus:()=>true,body:{classList:{contains:()=>true}}},browser={document:doc},main={BrowserWindow:browser};assert.equal(readSteamGuideReceiver(main,main,undefined),undefined);assert.equal(readSteamGuideReceiver(main,main,{GetFocusedOverlayAppID:()=>0,GetFocusedOverlayPID:()=>0}),undefined);});
const steamSourcePath=process.env.PLAYHUB_STEAM_UI_SOURCE??"C:/Program Files (x86)/Steam/steamui/sp.js";
test("installed Valve focus getters prove source-linked ordinary receiver change independently of overlay tuple",{skip:!existsSync(steamSourcePath)},()=>{const source=readFileSync(steamSourcePath,"utf8");const names=["GetFocusedAppID","GetFocusedWindowID","GetFocusedOverlayAppID","GetFocusedOverlayPID"];const bodies=names.map(name=>{const start=source.indexOf(name+"(){return this.");assert.ok(start>=0);const end=source.indexOf("}",start);return source.slice(start,end+1);});const store=vm.runInNewContext("({m_unFocusedAppID:413080,m_nFocusedWindowID:1,m_unFocusedOverlayAppID:0,m_unFocusedOverlayPID:0,"+bodies.join(",")+"})");const doc={hasFocus:()=>true,body:{classList:{contains:()=>true}}},browser={document:doc},main={BrowserWindow:browser};const first=readSteamGuideReceiver(main,main,store);store.m_unFocusedAppID=2979426017;store.m_nFocusedWindowID=42;const next=readSteamGuideReceiver(main,main,store);assert.equal(first.focusPid,next.focusPid);assert.equal(first.focusApp,next.focusApp);assert.notEqual(first.focusedApp,next.focusedApp);assert.notEqual(first.focusedWindow,next.focusedWindow);});

const epicRoute={...route,uiObservedApp:null,observedSteamGameId:null,observedFresh:false,launchProvenance:"epic-protocol"};
test("explicit Epic provenance preserves nullable UI/game observation without forging480",()=>{const r=parseOwnedLaunchRoute(epicRoute,null);assert.ok(r);assert.equal(r.observedSteamGameId,null);assert.equal(r.observedFresh,false);});
test("Epic no-observation route accepts only canonical/null UI hints",()=>{assert.ok(parseOwnedLaunchRoute({...epicRoute,uiObservedApp:canonical},canonical));assert.equal(parseOwnedLaunchRoute({...epicRoute,uiObservedApp:480},480),undefined);});
for(const change of [{launchProvenance:undefined},{launchProvenance:"steam-child"},{observedFresh:true},{observedSteamGameId:undefined}])
 test(`nullable route refuses missing or false provenance ${JSON.stringify(change)}`,()=>assert.equal(parseOwnedLaunchRoute({...epicRoute,...change},null),undefined));
test("unknown provenance cannot relabel an actual observed Steam route",()=>assert.equal(parseOwnedLaunchRoute({...route,launchProvenance:"other"},480),undefined));

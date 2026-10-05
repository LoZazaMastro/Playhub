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
const control=load("controlledQamFocus"), overlay=load("dashboardOverlay");
const settle=()=>new Promise(resolve=>setImmediate(resolve));
function fixture(infos=[], nativeReply={ok:true}, gameBarReply={ok:false}) {
  const calls=[], gameBarCalls=[]; let visible=0;
  const main={GetOpenSideMenu:()=>visible,GetQuickAccessTab:()=>4,
    OpenSideMenu(side){visible=side;},OpenQuickAccessMenu(tab){calls.push(["main",tab]);this.OpenSideMenu(2);},
    CloseSideMenus(){visible=0;}};
  const stale={OpenQuickAccessMenu(){calls.push(["stale"]);}};
  const patch=(object,key,handler,before)=>{
    const original=object[key];
    object[key]=function(...args){if(before)handler(args);const result=original.apply(this,args);return before?result:handler(args,result);};
    return{unpatch(){object[key]=original;}};
  };
  const ui={Router:{MainRunningAppID:42,WindowStore:{GamepadUIMainWindowInstance:{MenuStore:stale}},Navigate(){calls.push(["navigate"]);}},
    Navigation:{OpenQuickAccessMenu(tab){stale.OpenQuickAccessMenu(tab);}},
    afterPatch:(o,k,h)=>patch(o,k,h,false),beforePatch:(o,k,h)=>patch(o,k,h,true)};
  let systemCallback;let unregistered=0;
  const steam={Overlay:{GetOverlayBrowserInfo:async()=>infos},Apps:{RunGame(){calls.push(["run"]);}},
    System:{UI:{RegisterForSystemKeyEvents(callback){systemCallback=callback;return{unregister(){unregistered++;}};}}}};
  const api=load("controlledQamIntegration",name=>name==="@decky/api"?{call:async(method,body)=>{assert.equal(method,"request_uwp_gamebar");gameBarCalls.push({...body});return gameBarReply;}}:name==="./decky"?{DFL:ui}:name==="./api"?{API_BASE:"http://fixture"}:name==="./controlledQamFocus"?control:overlay,{
    window:{SteamClient:steam,SteamUIStore:{WindowStore:{GamepadUIMainWindowInstance:{MenuStore:main}}}},
    fetch:async(url,options)=>{const body=JSON.parse(options.body);calls.push([url.split("/").at(-1),body.requestId]);
      if(url.endsWith("acquire"))assert.equal(body.appId,42,"actual selected Steam app must reach native guard");return{ok:true,json:async()=>nativeReply};},
    AbortSignal:{timeout:()=>undefined},console:{warn(){}},Date,Math,
  });
  return{calls,gameBarCalls,main,ui,stop:api.installControlledQamFocus(),system:event=>systemCallback(event),unregistered:()=>unregistered};
}
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
test("real SystemKey subscription delegates rejected normal UWP to Game Bar without opening main",async()=>{
  const f=fixture([], {ok:false});f.system({eKey:0,nAppID:413080,nControllerIndex:5});await settle();
  assert.equal(f.main.GetOpenSideMenu(),0);assert.deepEqual(f.calls.map(row=>row[0]),["acquire"]);f.stop();
});
test("navigation fallback rejection cannot force-open the authoritative main menu",async()=>{
  const f=fixture([], {ok:false});f.ui.Navigation.OpenQuickAccessMenu(7);await settle();
  assert.equal(f.main.GetOpenSideMenu(),0);assert.deepEqual(f.calls.map(row=>row[0]),["stale","acquire"]);f.stop();
});

test("completed native Guide press routes verified Xbox to one Game Bar pulse, never QAM focus",async()=>{
  const f=fixture([], {ok:false}, {ok:true});
  f.system({eKey:0,nAppID:413080,nControllerIndex:5});
  f.main.OpenSideMenu(1);await settle();
  assert.deepEqual(f.gameBarCalls.map(x=>[x.appId,x.pressed]),[[42,true],[42,false]]);
  assert.equal(f.gameBarCalls[0].requestId,f.gameBarCalls[1].requestId);
  assert.deepEqual(f.calls,[]);
  f.main.OpenSideMenu(1);f.system({eKey:0,nControllerIndex:5});await settle();
  assert.equal(f.gameBarCalls.length,2,"same-press menu and SystemKey duplicates are ignored");
  f.main.CloseSideMenus();await settle();assert.equal(f.gameBarCalls.length,2,"normal back/close never sends Win+G");
  f.stop();assert.equal(f.unregistered(),1);
});

test("unrelated keys and explicit release never invoke Game Bar",async()=>{
  const f=fixture([], {ok:false}, {ok:true});
  for(const event of [{eKey:2},{eKey:0,ePressed:false},{eKey:0,bPressed:false}])f.system(event);
  await settle();assert.equal(f.gameBarCalls.length,0);f.stop();
});

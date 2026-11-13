import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";
const root = new URL("../src/", import.meta.url);
const helper = fs.readFileSync(new URL("steamConfiguratorReturn.ts", root), "utf8");
const exports = {};
vm.runInNewContext(ts.transpileModule(helper, {compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText,{exports});
const { createSteamConfiguratorReturn } = exports;
const index = fs.readFileSync(new URL("index.tsx", root), "utf8");
const ast = ts.createSourceFile("index.tsx", index, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
let callback;
function visit(node) {
  if (ts.isCallExpression(node) && node.expression.getText(ast) === "createSteamNavigationRecovery") {
    assert.equal(callback, undefined); callback = node.arguments[1].getText(ast);
  }
  ts.forEachChild(node, visit);
}
visit(ast); assert.ok(callback);
const callbackCode = ts.transpileModule(`exports.callback=${callback}`, {compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText;
const app=2979426017;
const presentation={protocol:1,state:"preparing",actualScope:0,sessionId:"a".repeat(64),gamePid:43364,
  gameBirth:"134389380406335517",gameId:((BigInt(app)<<32n)|0x02000000n).toString()};
function fixture(admit=true) {
  const lane=createSteamConfiguratorReturn(), calls=[], abort=new AbortController();
  const f={lane,calls,proof:true,current:true,visible:true,focused:true,anyRunning:false,running:0,alive:true,proofs:0};
  const button={matches:()=>f.highlighted!==false,querySelector:()=>null};
  const document={hasFocus:()=>f.focused,body:{classList:{contains:()=>f.windowFocused!==false}},activeElement:button};
  const input={ForceConfiguratorFocus(value){assert.equal(this,input);calls.push(value);f.dispatch?.();}};
  const browser={document,SteamClient:{Window:{SetKeyFocus(){throw Error("unexpected key-focus edge");}}}};
  const context={RootWindow:browser,ActiveWindow:browser,BIsActive:()=>true,BIsGamepadInputSuppressed:()=>false};
  const tree={};
  const main={m_BrowserWindow:browser,m_locationPathname:`/library/app/${app}`,m_ModalManager:{modals:[]},
    MenuStore:{GetOpenSideMenu:()=>0},GetFocusNavContext:()=>context,IsGamepadUIOverlayWindow:()=>false};
  const nav={GetActiveContext:()=>context,GetActiveNavTree:()=>tree};
  const store={get MainRunningAppID(){return f.running;},IsAnyAppRunning:()=>f.anyRunning};
  const window={SteamClient:{Input:input},SteamUIStore:store};
  const recovery={generation:1,exitedGame:{pid:presentation.gamePid,birth:presentation.gameBirth},signal:abort.signal,isCurrent:()=>f.current,async confirmForeground(){f.proofs++;await f.onProof?.(f.proofs);return f.proof;}};
  Object.assign(f,{input,browser,context,tree,main,nav,window,document,button,store,recovery,abort});
  if(admit) lane.admitted(app,presentation,input);
  const globals={exports:{},window,DFL:{findModule:()=>({})},alive:true,
    bigPictureMainWindow:()=>f.main,steamNavigationController:()=>nav,
    isConfirmedSteamIdle:(value,any)=>(value===0||value==="0")&&any!==true||value===undefined&&any===false,
    keyFocusReset:{async reset(){return false;}},
    restoreSteamNavigationFocus:()=>f.focusedNavigation!==false,launchPatch:{},overlayScope:{get(){},clear(){}},
    currentSteamGame:()=>f.running,layout:{begin(){throw Error("no layout mutation");}},navigateReturnedSteamGame(){},
    focusReturnedSteamGameAction:()=>f.play!==false,hasVisibleSteamNavigationFocus:()=>f.visible,
    configuratorReturn:lane,logToAgent:()=>{},focusDashboardSurface(){}};
  vm.runInNewContext(callbackCode,globals);
  f.run=()=>globals.exports.callback(app,false,recovery);
  f.direct=()=>lane.release(app,recovery,()=>f.current,()=>window.SteamClient.Input);
  return f;
}
test("actual return callback releases native lane once after retained native exit, Main and real Play proofs",async()=>{
  const f=fixture(); assert.equal(await f.run(),true);assert.deepEqual(f.calls,[false]);assert.equal(f.proofs,2);
  assert.equal(await f.run(),true);assert.deepEqual(f.calls,[false]);
});
test("visual/native UI focus without an admitted owned mirror grants no lane reset",async()=>{
  const f=fixture(false);assert.equal(await f.run(),true);assert.deepEqual(f.calls,[]);
});
test("SourceLost can leave claim pending; Desktop acquisition can be cleaned after Gaming return",async()=>{
  const f=fixture();f.main.uiMode="Gaming";assert.equal(f.lane.pending(app),true);
  assert.equal(await f.run(),true);assert.deepEqual(f.calls,[false]);
});
for (const [name,change] of Object.entries({
  "native retained exit/foreground denied":f=>{f.proof=false;},
  "another game running":f=>{f.anyRunning=true;},
  "null running identity":f=>{f.running=null;},
  "other canonical app":f=>{f.running=app+1;},
  "foreign active context":f=>{f.nav.GetActiveContext=()=>({});},
  "unknown active context":f=>{f.nav.GetActiveContext=()=>undefined;},
  "foreign root":f=>{f.context.RootWindow={};},
  "foreign context window":f=>{f.context.ActiveWindow={};},
  "inactive context":f=>{f.context.BIsActive=()=>false;},
  "suppressed controller input":f=>{f.context.BIsGamepadInputSuppressed=()=>true;},
  "unknown suppression":f=>{f.context.BIsGamepadInputSuppressed=()=>undefined;},
  "foreign modal":f=>{f.main.m_ModalManager.modals=[{}];},
  "unknown modal collection":f=>{f.main.m_ModalManager.modals=undefined;},
  "menu still open":f=>{f.main.MenuStore.GetOpenSideMenu=()=>2;},
  "configurator route":f=>{f.main.m_locationPathname="/app/769/controllerconfigurator/main";},
  "no actual Play":f=>{f.play=false;},
  "hidden navigation node":f=>{f.visible=false;},
  "Play no longer highlighted":f=>{f.highlighted=false;},
  "real document blur":f=>{f.focused=false;},
  "real WindowFocus removed":f=>{f.windowFocused=false;},
})) test(`actual callback refuses release: ${name}`,async()=>{const f=fixture();change(f);assert.equal(await f.run(),false);assert.deepEqual(f.calls,[]);});
test("Main/UI/configurator replacement during final native await cannot release",async()=>{
  for(const change of [f=>{f.main={...f.main};},f=>{f.main.m_BrowserWindow={...f.browser};},
    f=>{f.context.ActiveWindow={};},f=>{f.main.m_ModalManager.modals=[{}];},f=>{f.window.SteamClient.Input={ForceConfiguratorFocus(){throw Error("foreign");}};},
    f=>{f.lane.forget();},f=>{f.current=false;},f=>{f.document.activeElement={};}]){
    const f=fixture();f.onProof=n=>{if(n===2)change(f);};await f.run();assert.deepEqual(f.calls,[]);
  }
});
test("new RunGame invalidates the old mirror cleanup claim even for the same canonical app",async()=>{
  const f=fixture();f.lane.forget();assert.equal(await f.direct(),false);assert.deepEqual(f.calls,[]);
  assert.match(index,/beforePatch\(apps, "RunGame", \(\) => \{\s*configuratorReturn\.forget\(\)/);
});
test("replacement admitted while native proof is pending invalidates old cleanup",async()=>{
  const f=fixture();f.onProof=()=>f.lane.admitted(app,{...presentation,sessionId:"b".repeat(64),gamePid:43365},f.input);
  assert.equal(await f.direct(),false);assert.deepEqual(f.calls,[]);
});
test("actual helper rechecks claim after reentrant context getter and input getter",async()=>{
  for(const site of ["context","input"]){
    const f=fixture();let count=0;
    if(site==="context")f.recovery.isCurrent=()=>{if(++count===3)f.lane.forget();return true;};
    else Object.defineProperty(f.window.SteamClient,"Input",{get(){if(++count===3)f.lane.forget();return f.input;}});
    assert.equal(await f.direct(),false);assert.deepEqual(f.calls,[]);
  }
});
test("dispatch reservation prevents reentrant duplicate and ambiguous exception retry",async()=>{
  const f=fixture();let nested;f.dispatch=()=>{nested=f.direct();throw Error("unknown after native side effect");};
  assert.equal(await f.direct(),false);assert.equal(await nested,false);assert.equal(await f.direct(),false);assert.deepEqual(f.calls,[false]);
});
test("malformed, wrong canonical, foreign and nonmirror receipts cannot create cleanup authority",async()=>{
  for(const delta of [{protocol:2},{actualScope:2},{state:"closed"},{sessionId:"unknown"},{gamePid:0},{gameBirth:"0"},
    {gameBirth:"18446744073709551616"},{gameId:"480"}]){
    const f=fixture(false);f.lane.admitted(app,{...presentation,...delta},f.input);assert.equal(await f.direct(),false);assert.deepEqual(f.calls,[]);
  }
});
test("repeated generations of same owned game retain one attempted cleanup, new session creates new receipt",async()=>{
  const f=fixture();assert.equal(await f.direct(),true);f.lane.admitted(app,{...presentation,generation:"2"},f.input);
  assert.equal(await f.direct(),false);assert.deepEqual(f.calls,[false]);
  f.lane.admitted(app,{...presentation,sessionId:"b".repeat(64)},f.input);assert.equal(await f.direct(),true);assert.deepEqual(f.calls,[false,false]);
});
test("same-app missed RunGame or PID reuse cannot substitute the retained admitted process death",async()=>{
  for(const value of [undefined,{pid:presentation.gamePid+1,birth:presentation.gameBirth},
    {pid:presentation.gamePid,birth:(BigInt(presentation.gameBirth)+1n).toString()}]){
    const f=fixture();f.recovery.exitedGame=value;assert.equal(await f.direct(),false);assert.deepEqual(f.calls,[]);
  }
});
test("tuple replacement during final native proof refuses cleanup",async()=>{
  const f=fixture();f.onProof=()=>{f.recovery.exitedGame={pid:presentation.gamePid+1,birth:presentation.gameBirth};};
  assert.equal(await f.direct(),false);assert.deepEqual(f.calls,[]);
});
test("real mirror coordinator initially delivers Preparing scope0 to cleanup-only admission",async()=>{
  const sourceRoot=new URL("../src/",import.meta.url);
  const loaded=new Map();function load(name){if(loaded.has(name))return loaded.get(name);const out={};loaded.set(name,out);
    const code=ts.transpileModule(fs.readFileSync(new URL(name+".ts",sourceRoot),"utf8"),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText;
    vm.runInNewContext(code,{exports:out,require:n=>load(n.replace(/^\.\//,""))});return out;}
  const {createSteamMirrorOverlay}=load("steamMirrorOverlay");const f=fixture(false);let admissions=0;
  const base={...presentation,generation:"1",expiresAt:"2000",observedAt:"1000",sourceHwnd:"41001",sourceCookie:"41003",
    destinationHwnd:"41002",destinationCookie:"41004",ownerPid:7756,ownerBirth:"134389380407581330",
    preparedCurrent:false,shownCurrent:false,leaseCurrent:false,cleanupComplete:false};
  let state=base;const reply=()=>({ok:true,gameId:base.gameId,companionPid:7756,overlayPid:52836,mirror:{...state}});
  const browser={appID:app&0xffffff,gameID:base.gameId,unPID:52836,nBrowserID:1};let side=0;
  const menu={GetOpenSideMenu:()=>side,OpenMainMenu(){side=1;}};
  const target={IsGamepadUIOverlayWindow:()=>true,BrowserWindow:{document:{body:{}}},MenuStore:menu,params:{browserInfo:{m_gameID:base.gameId,m_unPID:52836,m_nBrowserID:1}}};
  let clock=1000;const route=createSteamMirrorOverlay({runningApp:()=>app,now:()=>clock,prepare:async()=>reply(),
    admitted:(a,p)=>{admissions++;assert.equal(p.state,"preparing");assert.equal(p.actualScope,0);f.lane.admitted(a,p,f.input);},
    read:async()=>{if(state.state==="preparing")state={...state,state:"prepared",actualScope:1,preparedCurrent:true,leaseCurrent:true};return reply();},
    command:async(_a,_p,action)=>{if(action==="show")state={...state,state:"shown",shownCurrent:true};return{ok:true};},
    browserInfo:async()=>[browser],windows:()=>[target],setOverlayState:async()=>{},delay:async(ms)=>{clock+=ms;}});
  await route.guide("main");assert.equal(admissions,1);assert.equal(route.shown,true);
  assert.equal(await f.direct(),true);assert.deepEqual(f.calls,[false]);
});
test("installed Valve activation pairs true/false and explains removal or mode-switch skipped cleanup",()=>{
  const steam=fs.readFileSync("C:/Program Files (x86)/Steam/steamui/sp.js","utf8");
  const expression=steam.match(/Ai\.IsGamepadUIOverlayWindow\(\)&&this\.m_mainInstanceUIMode!=r\.\$l2&&SteamClient\.Input\.ForceConfiguratorFocus\(lr\)/)?.[0];assert.ok(expression);
  const original=Function("Ai","r","SteamClient","lr",`return (${expression})`);const calls=[],client={Input:{ForceConfiguratorFocus:v=>calls.push(v)}};
  const gamepad=Symbol("Gamepad"),desktop=Symbol("Desktop"),receiver={m_mainInstanceUIMode:desktop};
  original.call(receiver,{IsGamepadUIOverlayWindow:()=>true},{$l2:gamepad},client,true);
  original.call(receiver,{IsGamepadUIOverlayWindow:()=>true},{$l2:gamepad},client,false);assert.deepEqual(calls,[true,false]);
  calls.length=0;original.call(receiver,{IsGamepadUIOverlayWindow:()=>true},{$l2:gamepad},client,true);
  receiver.m_mainInstanceUIMode=gamepad;original.call(receiver,{IsGamepadUIOverlayWindow:()=>true},{$l2:gamepad},client,false);
  assert.deepEqual(calls,[true]);assert.match(steam,/OnOverlayActivated\([^)]*\)\{const Ai=fn\.WindowStore\.GetOverlayInstance\([^)]*\);if\(Ai\)/);
});

test("late input receiver getter invalidates Main/UI guard without changing recovery generation",async()=>{
  const f=fixture();let count=0,allowed=true;
  Object.defineProperty(f.window.SteamClient,"Input",{get(){if(++count===3)allowed=false;return f.input;}});
  assert.equal(await f.lane.release(app,f.recovery,()=>allowed,()=>f.window.SteamClient.Input),false);
  assert.deepEqual(f.calls,[]);
});
test("actual API parser preserves birth as UInt64 string and refuses malformed PID/birth",async()=>{
  const source=fs.readFileSync(new URL("api.ts",root),"utf8");const file=ts.createSourceFile("api.ts",source,ts.ScriptTarget.Latest,true);
  const method=file.statements.find(n=>ts.isFunctionDeclaration(n)&&n.name?.text==="confirmBigPictureForeground");assert.ok(method);
  const text=method.getText(file).replace(/^export /,"");
  for(const dto of [{ok:true,appId:app,gamePid:presentation.gamePid,gameBirth:presentation.gameBirth},
    {ok:true,appId:app,gamePid:0,gameBirth:123},{ok:true,appId:app,gamePid:1.5,gameBirth:"18446744073709551616"}]){
    const out={};vm.runInNewContext(ts.transpileModule(text+"\nexports.parse=confirmBigPictureForeground;",{compilerOptions:{target:ts.ScriptTarget.ES2022,module:ts.ModuleKind.CommonJS}}).outputText,
      {exports:out,request:async()=>dto});
    const result=await out.parse(1);
    assert.equal(result.gamePid,dto.gamePid===presentation.gamePid?presentation.gamePid:undefined);
    assert.equal(result.gameBirth,dto.gameBirth===presentation.gameBirth?presentation.gameBirth:undefined);
  }
});
test("actual recovery context pins death tuple across final confirm; same app PID/birth replacement refuses",async()=>{
  const out={};vm.runInNewContext(ts.transpileModule(fs.readFileSync(new URL("steamNavigationReturn.ts",root),"utf8"),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText,
    {exports:out,AbortController,performance});
  for(const delta of [{gamePid:presentation.gamePid+1},{gameBirth:(BigInt(presentation.gameBirth)+1n).toString()},{}]){
    let count=0,confirmed;
    const recovery=out.createSteamNavigationRecovery(async()=>({ok:true,appId:app,gamePid:presentation.gamePid,gameBirth:presentation.gameBirth,...(++count===2?delta:{})}),
      async(_a,_c,context)=>{assert.equal(context.exitedGame.pid,presentation.gamePid);assert.equal(context.exitedGame.birth,presentation.gameBirth);confirmed=await context.confirmForeground();return false;},()=>0,()=>1000);
    await recovery.observe(1,true);assert.equal(confirmed,Object.keys(delta).length===0);
  }
});
test("managed process-death tuple is derived from retained Source and serialized as invariant string",()=>{
  const native=fs.readFileSync(new URL("../../GamingModeAgent/GamingMode.Services/BigPictureGameReturn.cs",import.meta.url),"utf8");
  const host=fs.readFileSync(new URL("../../GamingModeAgent/GamingMode.Services/AgentHost.cs",import.meta.url),"utf8");
  assert.match(native,/ForegroundProof\(uint AppId, bool UsedCompanion, uint GamePid = 0, long GameBirth = 0\)/);
  assert.match(native,/new\(session\.VerifiedAppId,[\s\S]{0,150}session\.Source\.GamePid, session\.Source\.GameBirth\)/);
  assert.match(host,/gamePid = proof\?\.GamePid \?\? 0, gameBirth = \(proof\?\.GameBirth \?\? 0\)\.ToString\(System\.Globalization\.CultureInfo\.InvariantCulture\)/);
});

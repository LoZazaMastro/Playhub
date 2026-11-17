import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";
const root=new URL("../src/",import.meta.url);
const path=process.env.PLAYHUB_RETURN_INDEX;
const source=fs.readFileSync(path??new URL("index.tsx",root),"utf8");
const ast=ts.createSourceFile("index.tsx",source,ts.ScriptTarget.Latest,true,ts.ScriptKind.TSX);
const body=name=>{const n=ast.statements.find(n=>ts.isFunctionDeclaration(n)&&n.name?.text===name);assert.ok(n,name);return n.getText(ast);};
const compiled=ts.transpileModule([body("restoreSteamNavigationFocus"),body("startOpenRequestWatcher"),"exports.start=startOpenRequestWatcher;"].join("\n"),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText;
function load(name){const out={};vm.runInNewContext(ts.transpileModule(fs.readFileSync(new URL(name+".ts",root),"utf8"),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText,{exports:out,AbortController,performance});return out;}
const navigation=load("steamNavigationReturn"),configurator=load("steamConfiguratorReturn");
const app=2485417537,gamePid=42,gameBirth="134391000000000000";
const steam=fs.readFileSync("C:/Program Files (x86)/Steam/steamui/sp.js","utf8");
const navigateMethods=[...steam.matchAll(/Navigate\(([^)]{0,100})\)\{(this\.BCanNavigate\(\)\&\&.{1,600}?this\.EnsureVROverlayVisible\(\)\))\}/g)];
const nativeMethods=[...new Set(navigateMethods.map(m=>`function Navigate(${m[1]}){${m[2]}}`))];
assert.equal(nativeMethods.length,1,"exact installed Steam Main.Navigate method required");
const nativeNavigate=Function(`return (${nativeMethods[0]});`)();
function gate(){let resolve;const promise=new Promise(r=>resolve=r);return{promise,resolve};}
function fixture({route=`/library/app/${app}`,focused=true,visible=true,claim=true}={}){
 const f={route,focused,visible,side:0,modals:[],running:0,version:1,time:0,proofs:0,navigations:[],focuses:0,keys:[],releases:[],logs:[],history:route==="/library/home"?[route]:["/library/home",route]};
 const doc={hasFocus:()=>f.focused,body:{classList:{contains:()=>f.focused}},querySelector:()=>null};
 function node(name){return{name,ownerDocument:doc,isConnected:true,getClientRects:()=>f.visible?[{}]:[],closest:()=>null,matches:selector=>selector===".gpfocus"&&f.visible,querySelector:()=>null};}
 f.play=node("play");f.other=node("other");f.home=node("home");f.node=f.play;
 Object.defineProperty(doc,"activeElement",{get:()=>f.node});
 const win={document:doc,closed:false,SteamClient:{Window:{SetKeyFocus(v){f.keys.push(v);f.focused=v;},BringToFront(){}}}};
 const context={RootWindow:win,ActiveWindow:win,BIsActive:()=>true,BIsGamepadInputSuppressed:()=>false};
 const tree={Window:win,WindowContext:context,GetLastFocusedNode:()=>({Element:f.node,BHasFocus:()=>f.visible})};
 const nav={GetActiveContext:()=>context,GetActiveNavTree:()=>tree};
 const main={m_BrowserWindow:win,MenuStore:{GetOpenSideMenu:()=>f.side},m_ModalManager:{get modals(){return f.modals;}},
   GetFocusNavContext:()=>context,FocusApplicationRoot(){f.focuses++;f.visible=true;f.node=f.play;},BCanNavigate:()=>true,
   BRouteMatch:route=>f.route===route,EnsureVROverlayVisible(){},
   m_history:{push(route){f.history.push(route);f.route=route;},replace(route){f.history[f.history.length-1]=route;f.route=route;}},
   Navigate(...args){f.navigations.push(args[0]);nativeNavigate.apply(main,args);}};
 f.back=()=>{if(f.history.length>1)f.history.pop();f.route=f.history.at(-1);f.node=f.home;};
 Object.defineProperty(main,"m_locationPathname",{get:()=>f.route});
 const input={ForceConfiguratorFocus(v){f.releases.push(v);}};
 const steam={Apps:{RunGame(){}},Input:input};
 const store={get MainRunningAppID(){return f.running;},IsAnyAppRunning:()=>f.running!==0};
 const window={SteamClient:steam,SteamUIStore:store,performance:{now:()=>f.time},setInterval(callback){f.interval=callback;return 1;},clearInterval(){},setTimeout(callback){queueMicrotask(callback);return 1;},clearTimeout(){}};
 const lane=configurator.createSteamConfiguratorReturn();
 if(claim)lane.admitted(app,{protocol:1,state:"preparing",actualScope:0,sessionId:"a".repeat(64),gamePid,gameBirth,gameId:((BigInt(app)<<32n)|0x02000000n).toString()},input);
 const globals={exports:{},...navigation,window,AbortController,performance:{now:()=>f.time},console,
   DFL:{beforePatch(_apps,_name,callback){f.runGame=callback;return{unpatch(){}};},findModule:()=>({})},
   bigPictureMainWindow:()=>f.mainMissing?undefined:main,steamNavigationController:()=>nav,currentSteamGame:()=>f.running,
   matchesReturnOverlay:()=>false,returnOverlayWindows:()=>[],activeSteamController:()=>false,
   async confirmBigPictureForeground(){const n=++f.proofs;await f.beforeProof?.(n);return{ok:true,appId:app,gamePid,gameBirth};},
   installSteamModeCommands:()=>({process:async()=>{}}),readOwnedGameApp:()=>0,resolveOwnedGameApp:()=>0,
   controlCall:()=>{throw Error("no arm action expected");},consumeOpenRequest:async()=>({focusRecovery:f.version,steamForeground:true}),
   observeSteamOverlayFocus:()=>({refresh(){},dispose(){}}),acknowledgeSteamModeCommand(){},
   logToAgent:message=>f.logs.push(message),focusDashboardSurface(){throw Error("no Dashboard takeover");}};
 globals._global_DFL=globals.DFL;globals.call=globals.controlCall;
 vm.runInNewContext(compiled,globals);f.stop=globals.exports.start(lane);f.tick=()=>f.interval();f.lane=lane;f.document=doc;f.main=main;
 return f;
}

test("Home Back during pending native confirmation is never routed back to the exited game",async()=>{
 const f=fixture(),g=gate();f.beforeProof=n=>n===1?g.promise:undefined;
 const pending=f.tick();for(let i=0;i<5;i++)await Promise.resolve();
 f.route="/library/home";f.node=f.home;g.resolve();await pending;
 assert.equal(f.route,"/library/home");assert.deepEqual(f.navigations,[]);assert.equal(f.focuses,0);
 assert.deepEqual(f.releases,[false],"owned configurator cleanup still settles without forcing Play");f.stop();
});

test("real existing navigation is sufficient; another control is not refocused to Play",async()=>{
 const f=fixture();f.node=f.other;await f.tick();
 assert.equal(f.node,f.other);assert.equal(f.focuses,0);assert.deepEqual(f.navigations,[]);assert.deepEqual(f.releases,[false]);f.stop();
});

test("focus moved to another node while confirmation waits stays on that node",async()=>{
 const f=fixture(),g=gate();f.beforeProof=n=>n===1?g.promise:undefined;
 const pending=f.tick();for(let i=0;i<5;i++)await Promise.resolve();f.node=f.other;g.resolve();await pending;
 assert.equal(f.node,f.other);assert.equal(f.focuses,0);assert.deepEqual(f.releases,[false]);f.stop();
});

for(const what of ["side menu","modal"])test(`${what} opened during async confirmation permanently yields navigation but retains owned cleanup`,async()=>{
 const f=fixture({visible:false}),g=gate();f.beforeProof=n=>n===1?g.promise:undefined;
 const pending=f.tick();for(let i=0;i<5;i++)await Promise.resolve();
 if(what==="side menu")f.side=1;else f.modals=[{}];g.resolve();await pending;
 assert.equal(f.focuses,0);assert.deepEqual(f.navigations,[]);assert.deepEqual(f.releases,[]);assert.equal(f.lane.pending(app),true);
 f.side=0;f.modals=[];f.visible=true;f.node=f.other;await f.tick();
 assert.equal(f.node,f.other);assert.equal(f.focuses,0);assert.deepEqual(f.releases,[false]);f.stop();
});

test("retiring shortcut polls do not undo repeated Back; cleanup remains one-shot when Steam becomes idle",async()=>{
 const f=fixture();f.running=app;await f.tick();assert.equal(f.lane.pending(app),true);
 f.route="/library/home";f.node=f.home;f.running=0;
 for(let i=0;i<12;i++)await f.tick();
 assert.equal(f.route,"/library/home");assert.equal(f.node,f.home);assert.deepEqual(f.navigations,[]);assert.deepEqual(f.releases,[false]);
 const proofs=f.proofs;await f.tick();assert.equal(f.proofs,proofs,"settled version is not retried");f.stop();
});

test("genuine first return replaces only apprunning once and settles its owned cleanup",async()=>{
 const f=fixture({route:"/apprunning",visible:false});await f.tick();
 assert.deepEqual(f.navigations,[`/library/app/${app}`]);assert.equal(f.node,f.play);assert.deepEqual(f.releases,[false]);
 await f.tick();assert.equal(f.navigations.length,1);f.stop();
});

test("genuine blurred Main still gets its first key/navigation recovery",async()=>{
 const f=fixture({focused:false,visible:false});await f.tick();
 assert.ok(f.keys.includes(true));assert.equal(f.visible,true);assert.equal(f.focuses,1);assert.deepEqual(f.releases,[false]);f.stop();
});

test("relaunch invalidates a pending return; a newer exit gets independent navigation ownership",async()=>{
 const f=fixture(),g=gate();f.beforeProof=n=>n===1?g.promise:undefined;
 const pending=f.tick();for(let i=0;i<5;i++)await Promise.resolve();f.runGame();f.route="/library/home";f.node=f.home;g.resolve();await pending;
 assert.deepEqual(f.releases,[]);assert.deepEqual(f.navigations,[]);
 f.version=2;f.time=21001;await f.tick();assert.equal(f.route,"/library/home");assert.equal(f.node,f.home);assert.deepEqual(f.navigations,[]);f.stop();
});

test("unload cancels an outstanding proof without navigation or native cleanup dispatch",async()=>{
 const f=fixture(),g=gate();f.beforeProof=n=>n===1?g.promise:undefined;
 const pending=f.tick();for(let i=0;i<5;i++)await Promise.resolve();f.stop();g.resolve();await pending;
 assert.deepEqual(f.releases,[]);assert.deepEqual(f.navigations,[]);
});

test("first physical Back after real return leaves the game page once, with no duplicate history or key reset",async()=>{
 const f=fixture();f.running=app;await f.tick();
 assert.deepEqual(f.history,["/library/home",`/library/app/${app}`]);
 assert.deepEqual(f.keys,[],"real focus must not get a false/true edge");
 f.back();assert.equal(f.route,"/library/home");f.running=0;
 for(let i=0;i<8;i++)await f.tick();
 assert.equal(f.route,"/library/home");assert.deepEqual(f.history,["/library/home"]);assert.deepEqual(f.navigations,[]);assert.deepEqual(f.keys,[]);assert.deepEqual(f.releases,[false]);f.stop();
});

test("placeholder return uses actual Steam replace then first Back reaches Home while cleanup awaits",async()=>{
 const f=fixture({route:"/apprunning",visible:false});f.running=app;await f.tick();
 assert.deepEqual(f.history,["/library/home",`/library/app/${app}`]);assert.equal(f.navigations.length,1);
 f.back();assert.equal(f.route,"/library/home");f.running=0;await f.tick();await f.tick();
 assert.deepEqual(f.history,["/library/home"]);assert.equal(f.navigations.length,1);assert.deepEqual(f.releases,[false]);f.stop();
});

test("Main not yet available during first exit poll can still receive its genuine first focus later",async()=>{
 const f=fixture({focused:false,visible:false});f.mainMissing=true;await f.tick();assert.equal(f.focuses,0);
 f.mainMissing=false;await f.tick();assert.equal(f.focuses,1);assert.ok(f.keys.includes(true));assert.deepEqual(f.releases,[false]);f.stop();
});

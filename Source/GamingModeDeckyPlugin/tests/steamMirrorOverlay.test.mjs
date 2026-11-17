import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

function load(name, require) {
  const exports = {};
  const source = readFileSync(name==="steamMirrorOverlay"&&process.env.PLAYHUB_NATIVE_VISIBLE_SOURCE ? process.env.PLAYHUB_NATIVE_VISIBLE_SOURCE : new URL(`../src/${name}.ts`, import.meta.url), "utf8");
  vm.runInNewContext(ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText, { exports, require });
  return exports;
}
const control = load("controlledQamFocus");
const browserApi = load("dashboardOverlay", () => control);
const navigationApi = load("steamMirrorNavigation");
const { createSteamMirrorOverlay, currentMirror } = load("steamMirrorOverlay", name => name === "./steamMirrorNavigation" ? navigationApi : browserApi);
const app = 2485417537, gameID = "10674787038353424384";
const base = {
  protocol: 1, state: "prepared", sessionId: "a".repeat(64), generation: "1", gameId: gameID,
  expiresAt: "1400", observedAt: "1000", readAt: "1000", sourceHwnd: "41001", sourceCookie: "18446744073709551614",
  destinationHwnd: "41002", destinationCookie: "18446744073709551615", ownerPid: 27156,
  ownerBirth: "134389155136169895", gamePid: 48584, gameBirth: "134389155148953005",
  preparedCurrent: true, shownCurrent: false, leaseCurrent: true, cleanupComplete: false, actualScope: 1,
};
const browser = { appID: 2389569, gameID, unPID: 31704, nBrowserID: 0 };
function fixture(options = {}) {
  let state = { ...base, ...options.initialState }, running = app, clock = 1000, side = 0, composed = false, reads = 0;
  const calls = []; let menuCloses = 0; let nativePhase=0;
  const menu = { CloseSideMenus() { menuCloses++; options.closeMenu?.(context()); side = 0; }, GetOpenSideMenu: () => side, GetQuickAccessTab: () => 0,
    OpenMainMenu() { calls.push("main"); side = 1; options.mount?.(context()); }, OpenQuickAccessMenu() { calls.push("quick"); side = 2; options.mount?.(context()); } };
  const target = { IsGamepadUIOverlayWindow: () => true, params: { browserInfo: {
    m_gameID: gameID, m_unPID: 31704, m_nBrowserID: 0 } }, MenuStore: menu, BrowserWindow: { document: { body: {} } } };
  const reply = () => ({ ok: true, mirrorSessionId:state.sessionId, gameId: gameID, overlayPid: 31704, companionPid: 27156, mirror: { ...state },
    nativeVisible: nativePhase ? (options.nativeMutation?.({
      protocol:1,phase:nativePhase,sessionId:state.sessionId,generation:state.generation,gameId:state.gameId,expiresAt:state.expiresAt,
      observedAt:String(clock),readAt:String(clock),sourceHwnd:state.sourceHwnd,sourceCookie:state.sourceCookie,ownerPid:state.ownerPid,ownerBirth:state.ownerBirth,gamePid:state.gamePid,gameBirth:state.gameBirth,
      physicalObservedAt:nativePhase===1?String(clock):"0",physicalExpiresAt:nativePhase===1?String(Math.min(clock+250,Number(state.expiresAt))):"0",
      guideEpoch:state.generation,receiptFrame:"10",sourceFrame:"15",valveOpen:nativePhase===1,ownCleanupComplete:true,inputAuthority:false,visualAuthority:false,
      baselineSamples:100,baselineMatched:100,outputSamples:100,outputMatched:100,
    }) ?? {protocol:1,phase:nativePhase,sessionId:state.sessionId,generation:state.generation,gameId:state.gameId,expiresAt:state.expiresAt,
      observedAt:String(clock),readAt:String(clock),sourceHwnd:state.sourceHwnd,sourceCookie:state.sourceCookie,ownerPid:state.ownerPid,ownerBirth:state.ownerBirth,gamePid:state.gamePid,gameBirth:state.gameBirth,
      physicalObservedAt:nativePhase===1?String(clock):"0",physicalExpiresAt:nativePhase===1?String(Math.min(clock+250,Number(state.expiresAt))):"0",
      guideEpoch:state.generation,receiptFrame:"10",sourceFrame:"15",valveOpen:nativePhase===1,ownCleanupComplete:true,inputAuthority:false,visualAuthority:false,
      baselineSamples:100,baselineMatched:100,outputSamples:100,outputMatched:100}) : undefined });
  const context = () => ({ target, menu, state, route, reads, setState: value => state = value,
    setRunning: value => running = value, advance: value => clock += value, setSide: value => side = value });
  const route = createSteamMirrorOverlay({
    runningApp: () => running, now: () => clock, trace: stage => options.trace?.(stage),
    admitted: (app, proof) => options.admitted?.(app, proof),
    prepare: async () => {
      calls.push("prepare"); await options.prepare?.();
      if (state.state === "closed") {nativePhase=0;state = { ...base, generation: (BigInt(state.generation) + 1n).toString(),expiresAt:String(clock+1400),observedAt:String(clock),readAt:String(clock) };}
      return options.delivery ? options.delivery() : reply();
    },
    read: async () => { calls.push("read"); reads++; await options.read?.(context()); const response=reply();options.reply?.(response,context());return response; },
    command: async (_app, _pid, action, expected) => {
      assert.equal(expected.sourceCookie, base.sourceCookie);
      if (action === "show" && options.nativeShowCurrent && !options.nativeShowCurrent(context())) {
        calls.push("show-native-refused"); return { ok: false };
      }
      if (action === "close" && options.rejectClose?.()) { calls.push("close-refused"); return { ok: false }; }
      if (expected.generation !== state.generation || expected.sourceHwnd !== state.sourceHwnd) {
        calls.push(`${action}-refused`); return { ok: false };
      }
      calls.push(action);
      if (action === "show") await options.show?.();
      state = action === "show" ? options.keepPreparedAfterShow ? state : { ...state, state: "shown", shownCurrent: true }
        : { ...state, state: "closed", shownCurrent: false, preparedCurrent: false, leaseCurrent: false, cleanupComplete: true,
          destinationHwnd: "0", destinationCookie: "0" };
      if(action==="show"&&options.nativeMode){nativePhase=1;state={...state,state:"closed",preparedCurrent:false,shownCurrent:false,leaseCurrent:false,cleanupComplete:true,destinationHwnd:"0",destinationCookie:"0"};}
      return { ok: true };
    },
    closeNativeVisible: options.nativeMode ? async (_app,_pid,expected) => {calls.push("native-close");assert.equal(expected.phase,1);if(options.nativeCloseFail)return {ok:false};nativePhase=2;return {ok:true};} : undefined,
    browserInfo: async () => options.cold && !composed ? [] : [browser], windows: () => options.cold && !composed ? [] : [target],
    navigation: options.navigation ? () => options.navigation : undefined,
    setOverlayState: async (_game, value) => {
      assert.equal(_game, gameID); calls.push(`set${value}`);
      if (value === 2) composed = true;
      if (value === 2) await options.set2?.(context());
      if (value === 0) await options.set0?.(context());
    },
    delay: async ms => { clock += ms; },
  });
  return { route, calls, reply, target, menu, get menuCloses() { return menuCloses; }, get reads() { return reads; },advance:value=>clock+=value,setPhase:value=>nativePhase=value, setState: value => state = value, setRunning: value => running = value };
}

test("first Guide prepares hidden GPU target before Set2 and shows only the exact generation", async () => {
  const f = fixture(); await f.route.guide("main");
  assert.equal(f.route.active, true);
  assert.ok(f.calls.indexOf("prepare") < f.calls.indexOf("main"));
  assert.ok(f.calls.indexOf("set2") < f.calls.indexOf("main"));
  assert.ok(f.calls.indexOf("set2") < f.calls.indexOf("show"));
  assert.equal(f.calls.filter(x => x === "set2").length, 1);
  assert.equal(f.calls.includes("set769"), false);
  await f.route.close();
  assert.ok(f.calls.indexOf("close") < f.calls.indexOf("set0"));
});

test("switching Main and Quick Access retains the shown generation without another native preparation", async () => {
  const f = fixture(); await f.route.guide("main");
  await f.route.guide("quick");
  assert.equal(f.menu.GetOpenSideMenu(), 2);
  await f.route.guide("main");
  assert.equal(f.menu.GetOpenSideMenu(), 1);
  assert.equal(f.reply().mirror.generation, "1");
  assert.equal(f.calls.filter(x => x === "prepare").length, 1);
  assert.equal(f.calls.filter(x => x === "set2").length, 1);
  assert.equal(f.calls.filter(x => x === "show").length, 1);
  assert.equal(f.calls.includes("close"), false);
  await f.route.close();
});

test("game replacement during a warm side switch cannot mount a late Quick Access menu", async () => {
  const f = fixture({ read: c => { if (c.reads === 6) c.setRunning(app + 1); } });
  await f.route.guide("main"); await f.route.guide("quick");
  assert.equal(f.calls.includes("quick"), false);
  assert.equal(f.calls.filter(x => x === "prepare").length, 1);
  await f.route.dispose();
});

test("a menu replaced while the warm API runs does not trigger a second preparation", async () => {
  let switched = false;
  const f = fixture({ mount: c => {
    if (!switched) return;
    c.setState({ ...base, state: "closed", shownCurrent: false, preparedCurrent: false, leaseCurrent: false, cleanupComplete: true });
  } });
  await f.route.guide("main"); switched = true; await f.route.guide("quick");
  assert.equal(f.calls.filter(x => x === "quick").length, 1);
  assert.equal(f.calls.filter(x => x === "prepare").length, 1);
  await f.route.dispose();
});

test("new Guide intent during a warm read uses the current side without closing its presentation", async () => {
  let release, pause = false;
  const gate = new Promise(resolve => { release = resolve; });
  const f = fixture({ read: async () => { if (pause) { pause = false; await gate; } } });
  await f.route.guide("main"); pause = true;
  const quick = f.route.guide("quick"); await new Promise(resolve => setImmediate(resolve));
  const main = f.route.guide("main"); release(); await quick; await main;
  assert.equal(f.menu.GetOpenSideMenu(), 1);
  assert.equal(f.calls.includes("quick"), false);
  assert.equal(f.calls.filter(x => x === "prepare").length, 1);
  assert.equal(f.calls.includes("close"), false);
  await f.route.close();
});
test("late MenuStore getter cancellation cannot mount a warm Quick Access menu", async () => {
  const f = fixture(); await f.route.guide("main"); let armed = false;
  f.menu.GetQuickAccessTab = () => { armed = true; return 0; };
  Object.defineProperty(f.target, "MenuStore", { configurable: true, get() {
    if (armed) { armed = false; void f.route.dispose(); }
    return f.menu;
  } });
  await f.route.guide("quick"); await f.route.settled();
  assert.equal(f.calls.includes("quick"), false);
});

test("late browser getter invalidation cannot mount a warm Quick Access menu", async () => {
  const f = fixture(); await f.route.guide("main"); let armed = false;
  f.menu.GetQuickAccessTab = () => { armed = true; return 0; };
  const info = f.target.params.browserInfo;
  Object.defineProperty(info, "m_gameID", { configurable: true, get() {
    if (armed) { armed = false; void f.route.invalidate(); }
    return gameID;
  } });
  await f.route.guide("quick"); await f.route.settled();
  assert.equal(f.calls.includes("quick"), false);
});

test("a late clock advance cannot commit a warm menu after its native proof lifetime", async () => {
  let current; const f = fixture({ read: context => { current = context; } });
  await f.route.guide("main");
  f.menu.GetQuickAccessTab = () => { current.advance(5000); return 0; };
  await f.route.guide("quick");
  assert.equal(f.calls.includes("quick"), false);
  await f.route.dispose();
});

test("a warm menu uses fresh readAt residual time rather than the older publication lifetime", async () => {
  let current; const f = fixture({ initialState: { readAt: "1390" }, read: context => { current = context; } });
  await f.route.guide("main");
  f.menu.GetQuickAccessTab = () => { current.advance(20); return 0; };
  await f.route.guide("quick");
  assert.equal(f.calls.includes("quick"), false);
  await f.route.dispose();
});

test("pending intent is not ownership; only a real current admission receipt notifies routing", async () => {
  let complete; const gate=new Promise(resolve=>{complete=resolve;}); const admitted=[];
  const f=fixture({prepare:()=>gate,admitted:(app,proof)=>admitted.push([app,proof])});
  const open=f.route.guide("main");await new Promise(resolve=>setImmediate(resolve));
  assert.equal(f.route.pendingApp,app);assert.equal(admitted.length,0);assert.equal(f.route.active,false);
  complete();await open;
  assert.equal(f.route.pendingApp,0);assert.equal(admitted.length,1);assert.equal(admitted[0][0],app);
  assert.equal(admitted[0][1].generation,base.generation);assert.equal(admitted[0][1].sourceCookie,base.sourceCookie);
  await f.route.close();
});
test("missing or malformed admission cannot adopt a status snapshot or set routing ownership", async () => {
  for(const delivery of [{ok:false},{ok:true,gameId:gameID,overlayPid:31704,companionPid:27156},
    {ok:true,gameId:gameID,overlayPid:31704,companionPid:27156,mirror:{...base,sourceCookie:42}}]){
    const admitted=[];const f=fixture({delivery:()=>delivery,admitted:()=>admitted.push(true)});
    await f.route.guide("main");assert.equal(admitted.length,0);assert.equal(f.route.active,false);
    assert.deepEqual(f.calls,["prepare"]);assert.equal(f.route.pendingApp,0);
  }
});
test("canceled admission retires its exact generation without notifying ownership or composing", async () => {
  let complete;const gate=new Promise(resolve=>{complete=resolve;});let admitted=0;
  const f=fixture({prepare:()=>gate,admitted:()=>admitted++});
  const open=f.route.guide("main");await new Promise(resolve=>setImmediate(resolve));
  const close=f.route.close();complete();await Promise.all([open,close]);
  assert.equal(admitted,0);assert.equal(f.calls.includes("close"),true);assert.equal(f.calls.includes("set2"),false);
  assert.equal(f.route.pendingApp,0);assert.equal(f.route.active,false);
});
test("uint64 identities stay exact strings; numeric birth/cookie and overflow are refused", () => {
  const f = fixture(); assert.equal(currentMirror(f.reply()).sourceCookie, base.sourceCookie);
  for (const [key, value] of [["ownerBirth", Number(base.ownerBirth)], ["sourceCookie", 42],
    ["generation", "18446744073709551616"], ["ownerBirth", "0"], ["expiresAt", "900"]]) {
    f.setState({ ...base, [key]: value }); assert.equal(currentMirror(f.reply()), undefined, key);
  }
});
test("replacement source during Set2 cannot inherit Show or be closed by the older owner", async () => {
  const f = fixture({ set2: ({ setState }) => setState({ ...base, generation: "2", sourceHwnd: "42001" }) });
  await f.route.guide("main"); assert.equal(f.calls.includes("show"), false);
  assert.equal(f.calls.includes("close"), false); assert.equal(f.calls.includes("set0"), false);
  assert.equal(f.calls.includes("close-refused"), false);
});
test("application change during Set2 closes only its retained native target, never another composition", async () => {
  const f = fixture({ set2: ({ setRunning }) => setRunning(99887766) });
  await f.route.guide("main"); assert.equal(f.calls.includes("show"), false);
  assert.equal(f.calls.includes("close"), true); assert.equal(f.calls.includes("set0"), false);
});
test("Set2 failure retires its own hidden presentation", async () => {
  const f = fixture({ set2: () => { throw new Error("CEF unavailable"); } });
  await f.route.guide("quick"); assert.equal(f.route.active, false);
  assert.equal(f.calls.includes("close"), true); assert.equal(f.calls.includes("show"), false);
});
test("a second Guide closes the existing presentation in the same serial composition queue", async () => {
  const f = fixture(); await f.route.guide("main"); await f.route.guide("main");
  assert.equal(f.route.active, false); assert.equal(f.calls.filter(x => x === "prepare").length, 1);
  assert.equal(f.calls.filter(x => x === "close").length, 1); assert.equal(f.calls.at(-1), "set0");
});
test("a canceled pending Guide cannot mount a late menu", async () => {
  let release;
  const wait = new Promise(resolve => { release = resolve; });
  const f = fixture({ prepare: () => wait }); const first = f.route.guide("main");
  await new Promise(resolve => setImmediate(resolve)); const close = f.route.close(); release();
  await first; await close;
  assert.equal(f.calls.includes("set2"), false); assert.equal(f.calls.includes("show"), false);
  assert.equal(f.calls.includes("close"), true); assert.equal(f.route.active, false);
});
test("scope1 is truthful and Prepared alone cannot be treated as Shown", () => {
  const f = fixture(); assert.equal(currentMirror(f.reply(), "shown"), undefined);
  f.setState({ ...base, state: "shown", shownCurrent: true, actualScope: 2 });
  assert.equal(currentMirror(f.reply()), undefined);
});
test("Back without a CEF event leaves the next Guide available to open a fresh native generation", async () => {
  const f = fixture(); await f.route.guide("main");
  f.setState({ ...base, state: "closed", preparedCurrent: false, shownCurrent: false, leaseCurrent: false, cleanupComplete: true,
    destinationHwnd: "0", destinationCookie: "0" });
  await f.route.guide("main");
  assert.equal(f.calls.filter(x => x === "prepare").length, 2);
  assert.equal(f.route.shown, true); assert.equal(f.reply().mirror.generation, "2");
  await f.route.dispose();
});
test("Guide+A coalesces into one preparation and opens Quick Access on the first chord", async () => {
  let release;
  const wait = new Promise(resolve => { release = resolve; });
  const f = fixture({ prepare: () => wait });
  const guide = f.route.guide("main");
  await new Promise(resolve => setImmediate(resolve));
  const quick = f.route.guide("quick"); release();
  await guide; await quick;
  assert.equal(f.calls.filter(x => x === "prepare").length, 1);
  assert.equal(f.calls.filter(x => x === "set2").length, 1);
  assert.equal(f.calls.includes("quick"), true); assert.equal(f.calls.includes("main"), false);
  await f.route.dispose();
});
test("a cold browser is bootstrapped by Set2 before any MenuStore exists", async () => {
  const f = fixture({ cold: true }); await f.route.guide("main");
  assert.equal(f.calls.filter(x => x === "set2").length, 1);
  assert.equal(f.route.shown, true); await f.route.dispose();
});
test("a rejected close keeps ownership and retries before allowing another preparation", async () => {
  let rejected = false;
  const f = fixture({ rejectClose: () => { if (rejected) return false; rejected = true; return true; } });
  await f.route.guide("main"); await f.route.close();
  assert.equal(f.calls.filter(x => x === "close-refused").length, 1);
  assert.equal(f.calls.filter(x => x === "close").length, 1);
  assert.equal(f.route.active, false);
});
test("an unconfirmed close retains the exact owner for a later explicit retry", async () => {
  let reject = true;
  const f = fixture({ rejectClose: () => reject }); await f.route.guide("main");
  await f.route.close(); assert.equal(f.route.active, true);
  reject = false; await f.route.close(); assert.equal(f.route.active, false);
});
test("Quick Access intent received during native show stays on the same presentation", async () => {
  let releaseShow;
  const wait = new Promise(resolve => { releaseShow = resolve; });
  const f = fixture({ show: () => wait }), first = f.route.guide("main");
  for (let i = 0; i < 10 && !f.calls.includes("show"); i++) await new Promise(resolve => setImmediate(resolve));
  assert.ok(f.calls.includes("show"));
  f.route.guide("quick"); releaseShow(); await first;
  assert.ok(f.calls.includes("quick"));
  assert.equal(f.calls.filter(x => x === "prepare").length, 1);
  assert.equal(f.calls.filter(x => x === "set2").length, 1);
  await f.route.dispose();
});

test("latency stages are emitted once in actual lifecycle order",async()=>{const stages=[];const f=fixture({trace:s=>stages.push(s)});await f.route.guide("main");assert.deepEqual(stages,["mirror-admitted","mirror-prepared","mirror-menu-ready","mirror-show-queued","mirror-shown"]);await f.route.dispose();});

test("one open uses four native reads, keeping asynchronous boundaries and final cleanup confirmation",async()=>{
  const f=fixture();await f.route.guide("main");
  assert.equal(f.reads,4,"Prepared, afterSet2/beforemount, Shown and final confirmation");
  assert.deepEqual(f.calls.slice(0,8),["prepare","read","set2","read","main","show","read","read"]);
  await f.route.dispose();
});
test("the fresh Prepared reply still rejects cancellation or source replacement before Set2",async()=>{
  for(const mutation of [ctx=>ctx.setRunning(1234),ctx=>ctx.setState({...ctx.state,sourceCookie:"77"})]){
    const f=fixture({read:ctx=>{if(ctx.reads===1)mutation(ctx);}});await f.route.guide("main");
    assert.equal(f.calls.includes("set2"),false);assert.equal(f.calls.includes("show"),false);
  }
});
test("the retained postSet2 read refuses a retired native lease before any menu mutation",async()=>{
  const f=fixture({set2:ctx=>ctx.setState({...ctx.state,state:"closed",preparedCurrent:false,shownCurrent:false,
    leaseCurrent:false,cleanupComplete:true,destinationHwnd:"0",destinationCookie:"0"})});
  await f.route.guide("main");assert.equal(f.calls.includes("main"),false);assert.equal(f.calls.includes("show"),false);
});
test("window and menu replacements during the retained native read cannot inherit menu or Show",async()=>{
  for(const replace of [ctx=>{ctx.target.MenuStore={...ctx.menu};},ctx=>{ctx.target.params={browserInfo:{m_gameID:gameID,m_unPID:999,m_nBrowserID:0}};}]){
    const f=fixture({read:ctx=>{if(ctx.reads===2)replace(ctx);}});await f.route.guide("main");
    assert.equal(f.calls.includes("main"),false);assert.equal(f.calls.includes("show"),false);
  }
});
test("synchronous mount reentry must keep exact browser and MenuStore before the Show command",async()=>{
  for(const replace of [ctx=>{ctx.target.MenuStore={...ctx.menu};},ctx=>{ctx.target.params={browserInfo:{m_gameID:gameID,m_unPID:999,m_nBrowserID:0}};},ctx=>{void ctx.route.dispose();}]){
    const f=fixture({mount:replace});await f.route.guide("main");await f.route.settled();
    assert.equal(f.calls.includes("show"),false,"no command for changed UI/revision");
  }
});
test("native Show refusal remains authoritative when the lease retires during synchronous mount",async()=>{
  const f=fixture({mount:ctx=>ctx.setState({...ctx.state,state:"closed",preparedCurrent:false,shownCurrent:false,
    leaseCurrent:false,cleanupComplete:true,destinationHwnd:"0",destinationCookie:"0"}),
    nativeShowCurrent:ctx=>ctx.state.state==="prepared"&&ctx.state.leaseCurrent===true});
  await f.route.guide("main");assert.equal(f.calls.includes("show-native-refused"),true);
  assert.equal(f.route.shown,false);assert.equal(f.route.active,false);
});
test("a post-mount MenuStore getter reentering dispose cannot dispatch a late Show",async()=>{
  const f=fixture({mount:ctx=>Object.defineProperty(ctx.target,"MenuStore",{get(){void ctx.route.dispose();return ctx.menu;}})});
  await f.route.guide("main");await f.route.settled();
  assert.equal(f.calls.includes("show"),false);assert.equal(f.route.active,false);
});
test("the final native confirmation is retained when the presentation closes just after Shown",async()=>{
  let f;f=fixture({trace:stage=>{if(stage==="mirror-shown")f.setState({...base,state:"closed",preparedCurrent:false,
    shownCurrent:false,leaseCurrent:false,cleanupComplete:true,destinationHwnd:"0",destinationCookie:"0"});}});
  await f.route.guide("main");assert.equal(f.route.active,false);assert.equal(f.calls.includes("set0"),true);
});

test("owned handoff retirement sends one exact Hidden0 and waits for actual native closed phase",async()=>{
  const f=fixture({read:ctx=>{if(ctx.state.state==="shown")ctx.setState({...ctx.state,actualScope:3,ownedForegroundPhase:1});
    if(ctx.state.state==="closed")ctx.setState({...ctx.state,actualScope:0});},
    set0:ctx=>ctx.setState({...ctx.state,ownedForegroundPhase:2})});
  await f.route.guide("main");assert.equal(f.route.shown,true);await f.route.close();
  assert.equal(f.route.active,false);assert.equal(f.calls.filter(x=>x==="set0").length,1);
});
test("visual cleanup with phase1 debt retains owner and cannot prepare another native generation",async()=>{
  const f=fixture({read:ctx=>{if(ctx.state.state==="shown")ctx.setState({...ctx.state,actualScope:3,ownedForegroundPhase:1});
    if(ctx.state.state==="closed")ctx.setState({...ctx.state,actualScope:0});}});
  await f.route.guide("main");await f.route.close();assert.equal(f.route.active,true);
  await f.route.guide("main");assert.equal(f.calls.filter(x=>x==="prepare").length,1);
  assert.equal(f.calls.filter(x=>x==="set0").length,1);
  f.setState({...f.reply().mirror,ownedForegroundPhase:2});await f.route.close();assert.equal(f.route.active,false);
});


test("timed-out prepared QAM retires native then closes only its mounted logical menu", async () => {
  const f=fixture({keepPreparedAfterShow:true});
  await f.route.guide("quick");
  assert.equal(f.reply().mirror.state,"closed");
  assert.equal(f.menu.GetOpenSideMenu(),0);
  assert.equal(f.menuCloses,1);
  assert.equal(f.route.active,false);
});
test("a successful warm Main-to-QAM owner retires its last mounted side",async()=>{
  const f=fixture();await f.route.guide("main");await f.route.guide("quick");
  assert.equal(f.menu.GetOpenSideMenu(),2);await f.route.close();
  assert.equal(f.menu.GetOpenSideMenu(),0);assert.equal(f.menuCloses,1);
});
test("unknown native cleanup cannot close a logical menu",async()=>{
  const f=fixture({keepPreparedAfterShow:true,rejectClose:()=>true});await f.route.guide("quick");
  assert.equal(f.menu.GetOpenSideMenu(),2);assert.equal(f.menuCloses,0);assert.equal(f.route.active,true);
});
test("retirement preserves replaced window document or MenuStore and another selected side",async()=>{
  for(const changed of [c=>c.target.BrowserWindow.document={body:{}},c=>c.target.MenuStore={...c.menu},c=>c.setSide(1),c=>c.setRunning(app+1)]){
    const f=fixture({keepPreparedAfterShow:true,set0:changed});await f.route.guide("quick");
    assert.equal(f.menuCloses,0);
  }
});
test("original menu close must read back0 before discarding its same owner",async()=>{
  const f=fixture({keepPreparedAfterShow:true,closeMenu:()=>{throw Error('refused');}});await f.route.guide("quick");
  assert.equal(f.menu.GetOpenSideMenu(),2);assert.equal(f.route.active,true);assert.equal(f.menuCloses,4);
});
test("a close throwing after its effect is settled by readback without a second call",async()=>{
  const f=fixture({keepPreparedAfterShow:true,closeMenu:c=>{c.setSide(0);throw Error('late');}});await f.route.guide("quick");
  assert.equal(f.menu.GetOpenSideMenu(),0);assert.equal(f.route.active,false);assert.equal(f.menuCloses,1);
});

test("last side or close-method getter document drift prevents logical cleanup mutation",async()=>{
  for(const kind of ["side","method"]){
    const f=fixture({keepPreparedAfterShow:true,set0:c=>{
      if(kind==="method"){
        const close=c.menu.CloseSideMenus;
        Object.defineProperty(c.menu,"CloseSideMenus",{get(){c.target.BrowserWindow.document={body:{}};return close;}});
      }else{
        const read=c.menu.GetOpenSideMenu;let calls=0;
        c.menu.GetOpenSideMenu=()=>{if(++calls===2)c.target.BrowserWindow.document={body:{}};return read();};
      }
    }});await f.route.guide("quick");assert.equal(f.menuCloses,0);
  }
});

test("retirement does not let a late browser provider close a different selected side",async()=>{
  const sideAtClose=[];
  const f=fixture({keepPreparedAfterShow:true,closeMenu:c=>sideAtClose.push(c.menu.GetOpenSideMenu()),set0:c=>{
    const side=c.menu.GetOpenSideMenu,isOverlay=c.target.IsGamepadUIOverlayWindow;
    let sideCalls=0,armed=false;
    c.menu.GetOpenSideMenu=()=>{if(++sideCalls===2)armed=true;return side();};
    c.target.IsGamepadUIOverlayWindow=()=>{if(armed){armed=false;c.setSide(1);}return isOverlay();};
  }});
  await f.route.guide("quick");
  assert.equal(sideAtClose.includes(1),false,"the original close must never receive the other side");
  assert.equal(f.menuCloses,1);
});

test("native-visible source stays open while owned layer is retired, without synthetic navigation", async () => {
 const traces=[];const f=fixture({nativeMode:true,trace:s=>traces.push(s),navigation:{}});
 await f.route.guide("main");
 assert.equal(f.route.active,true);assert.equal(f.route.nativeVisible,true);assert.equal(f.route.shown,false);
 assert.equal(f.calls.includes("set0"),false);assert.equal(f.menuCloses,0);
 assert.equal(traces.some(s=>s.startsWith("mirror-navigation")),false);
 assert.equal(traces.includes("native-visible-passthrough"),true);
 await f.route.poll();assert.equal(f.route.nativeVisible,true);assert.equal(f.calls.includes("set0"),false);
 await f.route.close();assert.equal(f.route.active,false);
  assert.equal(f.calls.filter(x=>x==="native-close").length,1);
  assert.equal(f.menuCloses,1,"explicit user close revokes our retained original menu");
  assert.equal(f.menu.GetOpenSideMenu(),0);
  assert.equal(f.calls.filter(x=>x==="set0").length,1,"exactly one explicit SDK revocation");
});
test("five native-visible Guide reopen/close cycles use fresh generations and never activate our tree", async () => {
 const traces=[];const f=fixture({nativeMode:true,trace:s=>traces.push(s),navigation:{}});
 for(let i=0;i<5;i++){await f.route.guide("main");assert.equal(f.route.nativeVisible,true);await f.route.guide("main");assert.equal(f.route.active,false);}
 assert.equal(f.calls.filter(x=>x==="prepare").length,5);assert.equal(f.calls.filter(x=>x==="native-close").length,5);
  assert.equal(traces.some(s=>s.startsWith("mirror-navigation")),false);
  assert.equal(f.menuCloses,5,"one original-menu close per explicit Guide close");
  assert.equal(f.menu.GetOpenSideMenu(),0);
  assert.equal(f.calls.filter(x=>x==="set0").length,5,"one SDK revocation per fresh native owner");
});
test("native-visible Main and QAM switch original menus with no second preparation or navigation", async () => {
 const traces=[];const f=fixture({nativeMode:true,trace:s=>traces.push(s),navigation:{}});
 await f.route.guide("main");await f.route.guide("quick");assert.equal(f.menu.GetOpenSideMenu(),2);
 await f.route.guide("main");assert.equal(f.menu.GetOpenSideMenu(),1);
 assert.equal(f.calls.filter(x=>x==="prepare").length,1);assert.equal(f.calls.includes("set0"),false);
 assert.equal(traces.some(s=>s.startsWith("mirror-navigation")),false);await f.route.close();
});
for (const [label,mutate] of [
 ["stale",n=>({...n,physicalObservedAt:"500",physicalExpiresAt:"750"})],
 ["no Valve activity",n=>({...n,valveOpen:false})],
 ["missing identity",n=>({...n,sourceCookie:"0"})],
 ["foreign generation",n=>({...n,generation:"999"})],
 ["partial pixels",n=>({...n,outputMatched:94})],
 ["partial baseline",n=>({...n,baselineMatched:94})],
 ["cleanup failure",n=>({...n,ownCleanupComplete:false})],
 ["unexpected authority",n=>({...n,inputAuthority:true})],
]) test(`native-visible refuses ${label} and uses original exact cleanup`,async()=>{
 const traces=[];const f=fixture({nativeMode:true,nativeMutation:mutate,trace:s=>traces.push(s),navigation:{}});
 await f.route.guide("main");assert.equal(f.route.nativeVisible,false);assert.equal(f.route.active,false);
 assert.equal(traces.includes("native-visible-passthrough"),false);assert.equal(traces.some(s=>s.startsWith("mirror-navigation")),false);
 assert.equal(f.calls.includes("set0"),true);
});
test("native-visible source-close has to be acknowledged separately from already-closed own mirror",async()=>{
 const f=fixture({nativeMode:true,nativeCloseFail:true});await f.route.guide("main");await f.route.close();
  assert.equal(f.route.active,true,"closed own layer is not native source-hidden");
  assert.equal(f.menuCloses,1,"user revocation may close only its captured original menu");
  assert.equal(f.menu.GetOpenSideMenu(),0);
  assert.equal(f.calls.filter(x=>x==="set0").length,1);
  await f.route.poll();
  assert.equal(f.route.active,true,"missing phase2 keeps the original source owner pending");
  assert.equal(f.route.shown,false);
  assert.equal(f.calls.filter(x=>x==="set0").length,1,"pending acknowledgement never repeats Set0");
 assert.equal(f.calls.filter(x=>x==="prepare").length,1);
});

test("native-visible preserves the real five-second initial preparation lease without inventing new time",async()=>{
 const f=fixture({nativeMode:true,initialState:{expiresAt:"6000"}});await f.route.guide("main");
 assert.equal(f.route.nativeVisible,true);assert.equal(f.reply().nativeVisible.expiresAt,"6000");
 assert.equal(f.calls.includes("set0"),false);await f.route.close();assert.equal(f.route.active,false);
});

// Real race: nativeg2 arrives before the JS receipt of g1source-hidden.
async function pendingNative(options={}){
 const f=fixture({nativeMode:true,nativeCloseFail:true,...options});
 await f.route.guide("main");await f.route.close();assert.equal(f.route.active,true);
 // Real SteamMirrorStatus.ToDto emits ActualScope0 when PreparedCurrentfalse.
 f.setState({...f.reply().mirror,generation:"2",expiresAt:"2400",observedAt:"1000",readAt:"1000",actualScope:0});
 return f;
}
test("explicit QuickAccess adopts fresh g2 without extra Open or stale g1 close",async()=>{
 const stages=[],f=await pendingNative({trace:s=>stages.push(s),navigation:{}}),before=f.calls.slice();
 await f.route.guide("quick");
 assert.equal(f.menu.GetOpenSideMenu(),2,"g2 must not remain behind the old pending source-hidden receipt");
 assert.equal(f.route.nativeVisible,true);assert.equal(f.route.shown,false);
 for(const call of ["prepare","set0","close","native-close"])assert.equal(f.calls.filter(v=>v===call).length,before.filter(v=>v===call).length,call);
 assert.equal(stages.includes("native-visible-explicit-generation-replaced"),true);
 assert.equal(stages.includes("native-visible-input-ready"),false,"visual receipt cannot invent g2neutral/769ACK");
});
test("explicit same-side Guide reopens g2 instead of closing it",async()=>{
 const f=await pendingNative();await f.route.guide("main");assert.equal(f.menu.GetOpenSideMenu(),1);
 assert.equal(f.calls.filter(x=>x==="prepare").length,1);assert.equal(f.calls.filter(x=>x==="set0").length,1);
});
test("coalesced Guide then QuickAccess mounts the final side on fresh g2",async()=>{
 const f=await pendingNative(),first=f.route.guide("main");f.route.guide("quick");await first;await f.route.settled();
 assert.equal(f.menu.GetOpenSideMenu(),2);assert.equal(f.calls.filter(x=>x==="prepare").length,1);assert.equal(f.calls.filter(x=>x==="set0").length,1);
});
test("post-admission coalescing uses a freshly captured receipt for the final side",async()=>{
 let f;f=await pendingNative({admitted:(_app,p)=>{if(p.generation==="2")void f.route.guide("quick");}});
 await f.route.guide("main");assert.equal(f.menu.GetOpenSideMenu(),2);assert.equal(f.calls.filter(x=>x==="prepare").length,1);
});
test("passive poll never adopts a different generation in the same session",async()=>{
 const f=await pendingNative(),calls=f.calls.slice();await f.route.poll();assert.equal(f.menu.GetOpenSideMenu(),0);
 for(const call of ["prepare","quick","main","set0","native-close"])assert.equal(f.calls.filter(x=>x===call).length,calls.filter(x=>x===call).length);
});
test("automatic recovery cannot retire g1 from g2 without a new explicit user edge",async()=>{
 const f=await pendingNative(),calls=f.calls.slice();await f.route.recover("quick");assert.equal(f.menu.GetOpenSideMenu(),0);
 for(const call of ["prepare","quick","set0","native-close"])assert.equal(f.calls.filter(x=>x===call).length,calls.filter(x=>x===call).length);
});
for(const [label,mutate] of [
 ["same generation",p=>({...p,generation:"1"})], ["rewound generation",p=>({...p,generation:"0"})],
 ["foreign full session",p=>({...p,sessionId:"b".repeat(64)})], ["foreign owner birth",p=>({...p,ownerBirth:"999"})],
 ["foreign game birth",p=>({...p,gameBirth:"999"})], ["foreign source HWND",p=>({...p,sourceHwnd:"99"})],
 ["foreign source cookie",p=>({...p,sourceCookie:"99"})], ["unclean own destination",p=>({...p,destinationHwnd:"99"})],
])test(`explicit g2 refuses ${label} without stale close`,async()=>{
 const f=await pendingNative();f.setState(mutate(f.reply().mirror));const closes=f.calls.filter(x=>x==="set0"||x==="native-close").length;
 await f.route.guide("quick");assert.equal(f.menu.GetOpenSideMenu(),0);assert.equal(f.route.active,true);
 assert.equal(f.calls.filter(x=>x==="set0"||x==="native-close").length,closes);assert.equal(f.calls.filter(x=>x==="prepare").length,1);
});
for(const [label,mutate] of [
 ["old Guide epoch",n=>({...n,guideEpoch:"1"})], ["expired physical proof",n=>({...n,physicalObservedAt:"500",physicalExpiresAt:"750"})],
 ["no original source pixels",n=>({...n,outputSamples:0,outputMatched:0})], ["unexpected input claim",n=>({...n,inputAuthority:true})],
])test(`explicit g2 rejects ${label}`,async()=>{
 const f=await pendingNative({nativeMutation:n=>n.generation==="2"?mutate(n):n});
 await f.route.guide("quick");assert.equal(f.menu.GetOpenSideMenu(),0);assert.equal(f.calls.filter(x=>x==="prepare").length,1);
 assert.equal(f.calls.filter(x=>x==="set0").length,1);
});
test("slow first RPC does not convert a fresh server sample into client freshness",async()=>{
 const f=await pendingNative({read:c=>{if(c.state.generation==="2")c.advance(300);}});
 await f.route.guide("quick");assert.equal(f.menu.GetOpenSideMenu(),0);assert.equal(f.calls.filter(x=>x==="prepare").length,1);
});
test("source replacement during the final fresh g2 read cannot inherit the menu",async()=>{
 let second=0;const f=await pendingNative({read:c=>{if(c.state.generation==="2"&&++second===2)c.target.MenuStore={...c.menu};}});
 await f.route.guide("quick");assert.equal(f.menu.GetOpenSideMenu(),0);assert.equal(f.calls.filter(x=>x==="prepare").length,1);
});
test("post-admission reentry invalidates g2 without dispatching the requested menu",async()=>{
 let f;f=await pendingNative({admitted:(_app,p)=>{if(p.generation==="2")void f.route.invalidate();}});
 await f.route.guide("quick");await f.route.settled();assert.equal(f.calls.includes("quick"),false);
});
test("post-admission slow callback cannot use an expired physical proof to mount g2",async()=>{
 let f;f=await pendingNative({admitted:(_app,p)=>{if(p.generation==="2")f.advance(300);}});
 await f.route.guide("quick");assert.equal(f.calls.includes("quick"),false);assert.equal(f.calls.filter(x=>x==="set0").length,1);
});
test("post-admission clock reversal cannot mount g2",async()=>{
 let f;f=await pendingNative({admitted:(_app,p)=>{if(p.generation==="2")f.advance(-10);}});
 await f.route.guide("quick");assert.equal(f.calls.includes("quick"),false);assert.equal(f.calls.filter(x=>x==="set0").length,1);
});
for(const label of ["missing","foreign"])test(`g2 ${label} canonical envelope session is refused`,async()=>{
 const f=await pendingNative({reply:(reply,c)=>{if(c.state.generation==="2")reply.mirrorSessionId=label==="missing"?undefined:"b".repeat(64);}});
 await f.route.guide("quick");assert.equal(f.menu.GetOpenSideMenu(),0);assert.equal(f.calls.filter(x=>x==="prepare").length,1);
});
test("actual fresh g2 Prepared continuation reuses admission without second Open",async()=>{
 const f=await pendingNative({reply:(reply,c)=>{
  if(c.state.generation!=="2"||c.state.state!=="prepared")return;
  reply.continuation={protocol:1,phase:2,sessionId:base.sessionId,guideEpoch:"2",parentGeneration:"1",resumedGeneration:"2",
   gapDeadline:"1400",observedAt:"1000",readAt:"1000",ownerPid:base.ownerPid,ownerBirth:base.ownerBirth,gamePid:base.gamePid,gameBirth:base.gameBirth,
   gameId:base.gameId,sourceHwnd:base.sourceHwnd,sourceCookie:base.sourceCookie,ticketLow:"1",ticketHigh:"2",inputAuthority:false,visualAuthority:false,
   cleanupComplete:true,originalLeaseExpiry:"1400",preparationExpiresAt:"2400"};
 }});
 f.setState({...base,generation:"2",expiresAt:"2400"});f.setPhase(0);
 await f.route.guide("quick");assert.equal(f.menu.GetOpenSideMenu(),2);assert.equal(f.calls.filter(x=>x==="prepare").length,1);
 assert.equal(f.route.nativeVisible,true);assert.equal(f.calls.filter(x=>x==="set0").length,1);
});

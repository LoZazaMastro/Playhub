import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

function load(name, require) {
  const exports = {};
  const source = readFileSync(name === "steamMirrorOverlay" && process.env.PLAYHUB_BEFORE_OVERLAY ? process.env.PLAYHUB_BEFORE_OVERLAY : new URL(`../src/${name}.ts`, import.meta.url), "utf8");
  vm.runInNewContext(ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText, { exports, require });
  return exports;
}
const control = load("controlledQamFocus");
const browserApi = load("dashboardOverlay", () => control);
const navigationApi = load("steamMirrorNavigation");
const { createSteamMirrorOverlay, currentMirror, mirrorContinuation } = load("steamMirrorOverlay", name => name === "./steamMirrorNavigation" ? navigationApi : browserApi);
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
  const calls = []; let menuCloses = 0;
  const menu = { CloseSideMenus() { menuCloses++; options.closeMenu?.(context()); side = 0; }, GetOpenSideMenu: () => side, GetQuickAccessTab: () => 0,
    OpenMainMenu() { calls.push("main"); side = 1; options.mount?.(context()); }, OpenQuickAccessMenu() { calls.push("quick"); side = 2; options.mount?.(context()); } };
  const target = { IsGamepadUIOverlayWindow: () => true, params: { browserInfo: {
    m_gameID: gameID, m_unPID: 31704, m_nBrowserID: 0 } }, MenuStore: menu, BrowserWindow: { document: { body: {} } } };
  const reply = () => ({ ok: true, gameId: gameID, overlayPid: 31704, companionPid: 27156, mirror: { ...state }, continuation: options.continuation?.() });
  const context = () => ({ target, menu, state, route, reads, setState: value => state = value,
    setRunning: value => running = value, advance: value => clock += value, setSide: value => side = value });
  const route = createSteamMirrorOverlay({
    runningApp: () => running, now: () => clock, trace: stage => options.trace?.(stage),
    admitted: (app, proof) => options.admitted?.(app, proof),
    prepare: async () => {
      calls.push("prepare"); await options.prepare?.();
      if (state.state === "closed") state = { ...base, generation: (BigInt(state.generation) + 1n).toString() };
      return options.delivery ? options.delivery() : reply();
    },
    read: async () => { calls.push("read"); reads++; await options.read?.(context()); return reply(); },
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
      return { ok: true };
    },
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
  return { route, calls, reply, target, menu, get menuCloses() { return menuCloses; }, get reads() { return reads; }, setState: value => state = value, setRunning: value => running = value, advance: value => clock += value };
}


const retired=()=>({...base,state:"closed",shownCurrent:false,preparedCurrent:false,leaseCurrent:false,cleanupComplete:true,destinationHwnd:"0",destinationCookie:"0"});
const lineage=(phase=1,changes={})=>({protocol:1,phase,sessionId:base.sessionId,guideEpoch:"7",parentGeneration:"1",resumedGeneration:phase===1?"0":"2",gapDeadline:"1350",observedAt:"1000",readAt:"1000",ownerPid:base.ownerPid,gamePid:base.gamePid,ownerBirth:base.ownerBirth,gameBirth:base.gameBirth,gameId:base.gameId,sourceHwnd:base.sourceHwnd,sourceCookie:base.sourceCookie,ticketLow:"111",ticketHigh:"222",inputAuthority:false,visualAuthority:false,cleanupComplete:true,originalLeaseExpiry:"1400",preparationExpiresAt:phase===1?"0":"5500",...changes});
const fresh=()=>({...base,generation:"2",expiresAt:"5500",observedAt:"1100",readAt:"1100",destinationHwnd:"51002",destinationCookie:"51003"});
test("actual overlay owner retains original menu during passive null and consumes only a fresh native Prepared generation",async()=>{
 let c;const admitted=[];const f=fixture({continuation:()=>c,admitted:(a,p)=>admitted.push(p.generation)});await f.route.guide("main");
 f.setState(retired());c=lineage();await f.route.poll?.();
 assert.equal(f.route.active,true);assert.equal(f.route.shown,false);assert.equal(f.menu.GetOpenSideMenu(),1);assert.equal(f.calls.includes("set0"),false);
 f.advance(100);c=lineage(2,{observedAt:"1100",readAt:"1100"});f.setState(fresh());await f.route.poll?.();
 assert.equal(f.calls.filter(x=>x==="show").length,2,"fresh receipt requires a new exact Show, never a fake Guide");
 assert.equal(f.route.shown,true);assert.deepEqual(admitted,["1","2"]);assert.equal(f.calls.filter(x=>x==="prepare").length,1);assert.equal(f.calls.filter(x=>x==="set2").length,1);assert.equal(f.calls.filter(x=>x==="main").length,1);assert.equal(f.menuCloses,0);await f.route.close();
});
test("passive ticket expiry retires the original captured menu and sends only Hidden0",async()=>{
 let c;const f=fixture({continuation:()=>c});await f.route.guide("main");f.setState(retired());c=lineage();await f.route.poll?.();f.advance(400);await f.route.poll?.();assert.equal(f.route.active,false);assert.equal(f.menuCloses,1);assert.equal(f.calls.filter(x=>x==="set0").length,1);assert.equal(f.calls.filter(x=>x==="show").length,1);
});
test("native cancellation immediately retires passive UI instead of renewing intent",async()=>{
 let c;const f=fixture({continuation:()=>c});await f.route.guide("main");f.setState(retired());c=lineage();await f.route.poll?.();c=lineage(3,{resumedGeneration:"0",preparationExpiresAt:"0"});await f.route.poll?.();assert.equal(f.route.active,false);assert.equal(f.menuCloses,1);assert.equal(f.calls.filter(x=>x==="show").length,1);
});
test("explicit close cancels passive original UI without preparing or showing anything",async()=>{
 let c;const f=fixture({continuation:()=>c});await f.route.guide("quick");f.setState(retired());c=lineage();await f.route.poll?.();await f.route.close();assert.equal(f.route.active,false);assert.equal(f.menuCloses,1);assert.equal(f.calls.filter(x=>x==="prepare").length,1);assert.equal(f.calls.filter(x=>x==="show").length,1);
});
test("replacement document or MenuStore cannot inherit recovered native generation",async()=>{
 for(const mutate of [f=>{f.target.BrowserWindow.document={body:{}};},f=>{f.target.MenuStore={};}]){
  let c;const f=fixture({continuation:()=>c});await f.route.guide("main");f.setState(retired());c=lineage();await f.route.poll?.();mutate(f);f.advance(100);f.setState(fresh());c=lineage(2,{observedAt:"1100",readAt:"1100"});await f.route.poll?.();assert.equal(f.calls.filter(x=>x==="show").length,1);assert.equal(f.menuCloses,0);await f.route.dispose();
 }
});
test("malformed lineage, authority flags, wrong session and changed ticket cannot adopt a fresh generation",async()=>{
 for(const patch of [{inputAuthority:true},{visualAuthority:true},{guideEpoch:"01"},{ownerBirth:"9"},{gameId:"9"},{sourceCookie:"9"},{sessionId:"b".repeat(64)},{parentGeneration:"2"},{resumedGeneration:"1"},{readAt:"99999"},{ticketLow:"999"}]){
  let c;const f=fixture({continuation:()=>c});await f.route.guide("main");f.setState(retired());c=lineage();await f.route.poll?.();f.advance(100);f.setState(fresh());c=lineage(2,{observedAt:"1100",readAt:"1100",...patch});await f.route.poll?.();assert.equal(f.calls.filter(x=>x==="show").length,1,JSON.stringify(patch));await f.route.dispose();
 }
});
test("repeated timer polls do not accumulate unbounded native reads",async()=>{
 let release,block=false;const gate=new Promise(r=>release=r);const f=fixture({read:async()=>{if(block)await gate;}});await f.route.guide("main");block=true;const previous=f.reads;const pending=Array.from({length:40},()=>f.route.poll?.());await new Promise(r=>setImmediate(r));release();await Promise.all(pending);assert.equal(f.reads-previous,1);await f.route.close();
});

test("fresh native AwaitingBand retains only passive UI until actual Prepared receipt",async()=>{
 let c;const f=fixture({continuation:()=>c});await f.route.guide("quick");f.setState(retired());c=lineage();await f.route.poll?.();
 f.advance(100);c=lineage(2,{observedAt:"1100",readAt:"1100"});f.setState({...fresh(),state:"preparing",preparedCurrent:false,shownCurrent:false,leaseCurrent:false,actualScope:0});await f.route.poll?.();
 assert.equal(f.route.active,true);assert.equal(f.route.shown,false);assert.equal(f.calls.includes("set0"),false);assert.equal(f.calls.filter(x=>x==="show").length,1);
 f.setState(fresh());await f.route.poll?.();assert.equal(f.route.shown,true);assert.equal(f.calls.filter(x=>x==="show").length,2);assert.equal(f.calls.filter(x=>x==="prepare").length,1);await f.route.close();
});

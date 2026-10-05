import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";
const exports = {};
vm.runInNewContext(ts.transpileModule(readFileSync(new URL("../src/controlledQamFocus.ts", import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText, { exports });
const settle = () => new Promise(resolve => setImmediate(resolve));
test("Steam signed shortcut IDs retain exact unsigned identity at native boundary",()=>{
  assert.equal(exports.steamAppId(-1809549759),2485417537);
  assert.equal(exports.steamAppId(2485417537),2485417537);
  assert.equal(exports.steamAppId(620),620);
  for(const value of [undefined,NaN,Infinity,10674787038353424384,1.5])assert.equal(exports.steamAppId(value),0);
});
function fixture(gameBar) {
  const calls = []; let app = 42, visible = 2, native = false, serial = 0, clock = 1000;
  let probe = async () => native, acquire = async () => ({ok:true});
  const main = { GetOpenSideMenu: () => visible, OpenQuickAccessMenu: tab => { calls.push(["main",tab]); visible=2; },
    OpenSideMenu: side => {calls.push(["side",side]);visible=side;} };
  const control = exports.createControlledQamFocus({
    runningApp: () => app, mainMenu: () => main, nativeOverlayAvailable: id => probe(id),
    acquire: id => { calls.push(["acquire",id]); return acquire(id); },
    close: async id => calls.push(["close",id]), release: async id => calls.push(["release",id]),
    requestId: () => `r${++serial}`, warn: () => {}, now:()=>clock,
    gameBar,
  });
  return {control,calls,setNative: value=>native=value,setVisible:value=>visible=value,setApp:value=>app=value,
    probe: fn=>probe=fn, acquire: fn=>acquire=fn, main, advance:ms=>clock+=ms};
}

test("separate completed Guide presses get separate paired pulses without held repeats or B",async()=>{
  const pulses=[];
  const f=fixture(async(id,app,pressed)=>{pulses.push([id,app,pressed]);return{ok:true};});
  f.control.systemKey({eKey:0,nControllerIndex:1});await settle();
  f.control.systemKey({eKey:0,nControllerIndex:1});await settle();
  assert.deepEqual(pulses,[["r1",42,true],["r1",42,false]]);
  f.control.closed();f.advance(500);
  f.control.systemKey({eKey:0,nControllerIndex:1});await settle();
  assert.deepEqual(pulses.slice(2),[["r2",42,true],["r2",42,false]]);
  assert.deepEqual(f.calls,[]);
});

test("failed Game Bar request still releases paired edge and cannot open QAM",async()=>{
  const pulses=[];
  const f=fixture(async(id,app,pressed)=>{pulses.push(pressed);if(pressed)throw Error("offline");return{ok:false};});
  f.control.systemKey({eKey:0});await settle();
  assert.deepEqual(pulses,[true,false]);
  assert.equal(f.calls.some(x=>x[0]==="main"||x[0]==="side"||x[0]==="acquire"),false);
});
test("working native overlay receives no main-window/focus operations", async () => {
  const f=fixture(); f.setNative(true); f.setVisible(0); f.control.opened(4); await settle(); f.control.closed();
  assert.deepEqual(f.calls,[]);
});
test("missing native overlay acquires before opening main QAM once", async () => {
  const f=fixture(); f.setVisible(0); f.control.opened(7); f.control.opened(7); await settle();
  assert.deepEqual(f.calls, [["acquire","r1"],["main",7]]); f.control.closed(); f.control.closed();
  assert.deepEqual(f.calls.at(-1),["close","r1"]); assert.equal(f.calls.length,3);
});
test("close during native identity query never opens/reacquires a menu", async () => {
  const f=fixture(); let resolve; f.probe(()=>new Promise(r=>resolve=r)); f.control.opened(); f.control.closed(); resolve(false); await settle();
  assert.deepEqual(f.calls,[["close","r1"]]);
});
test("close during acquire releases late completion", async () => {
  const f=fixture(); let resolve; f.acquire(()=>new Promise(r=>resolve=r)); f.control.opened(); await settle();
  f.setVisible(0); f.control.closed(); resolve({ok:true}); await settle();
  assert.deepEqual(f.calls,[["acquire","r1"],["close","r1"],["release","r1"]]);
});
test("application selection and unload release without game restore", async () => {
  const f=fixture(); f.control.opened(); await settle(); f.control.applicationSelected(); f.control.dispose();
  assert.deepEqual(f.calls,[["acquire","r1"],["release","r1"]]);
});
test("changing running app while enumerating prevents stale acquisition", async () => {
  const f=fixture(); let resolve; f.probe(()=>new Promise(r=>resolve=r)); f.control.opened(); f.setApp(99); f.control.applicationSelected(); resolve(false); await settle();
  assert.deepEqual(f.calls,[["release","r1"]]);
});
test("unknown native identity cannot justify moving foreground", async () => {
  const f=fixture(); f.probe(async()=>{throw Error("unavailable");}); f.control.opened(); await settle();
  assert.deepEqual(f.calls,[["release","r1"]]);
});
test("no running game or idle work produces no requests", async () => {
  const f=fixture(); await settle(); assert.deepEqual(f.calls,[]); f.setApp(0); f.control.opened(); await settle(); assert.deepEqual(f.calls,[]);
});
test("Guide opens Main1 while real quick-menu event opens QuickAccess2",async()=>{
  const f=fixture();f.setVisible(0);f.control.systemKey({eKey:0,nControllerIndex:5,nAppID:413080});await settle();
  assert.deepEqual(f.calls,[["acquire","r1"],["side",1]]);f.control.closed();f.setVisible(0);f.advance(500);
  f.control.systemKey({eKey:1,nControllerIndex:5,nAppID:0});await settle();
  assert.deepEqual(f.calls.slice(-2),[["acquire","r2"],["main",undefined]]);
});
test("held/released/duplicate SystemKey events cause one handoff",async()=>{
  const f=fixture();f.setVisible(0);const event={eKey:1,nControllerIndex:5};
  f.control.systemKey({...event,bPressed:false});f.control.systemKey(event);f.control.systemKey(event);f.advance(20);f.control.systemKey(event);await settle();
  assert.deepEqual(f.calls,[["acquire","r1"],["main",undefined]]);
});
test("native overlay and no running app ignore raw SystemKey for fallback",async()=>{
  const f=fixture();f.setNative(true);f.setVisible(0);f.control.systemKey({eKey:0,nControllerIndex:5});await settle();assert.deepEqual(f.calls,[]);
  f.setApp(0);f.advance(500);f.control.systemKey({eKey:1,nControllerIndex:5});await settle();assert.deepEqual(f.calls,[]);
});
test("same SystemKey that Steam used to close menu cannot reopen it",async()=>{
  const f=fixture();f.control.opened();await settle();f.setVisible(0);f.control.closed();f.control.systemKey({eKey:1,nControllerIndex:5});await settle();
  assert.deepEqual(f.calls,[["acquire","r1"],["close","r1"]]);
});
test("app switch while raw key checks native identity cannot acquire old game",async()=>{
  const f=fixture();let resolve;f.probe(()=>new Promise(r=>resolve=r));f.setVisible(0);f.control.systemKey({eKey:1,nControllerIndex:5});
  f.setApp(99);f.control.applicationSelected();resolve(false);await settle();assert.deepEqual(f.calls,[["release","r1"]]);
});
test("native UWP/Game Bar denial never force-opens Guide or quick menu",async()=>{
  for(const eKey of [0,1]){
    const f=fixture();f.setVisible(0);f.acquire(async()=>({ok:false}));
    f.control.systemKey({eKey,nControllerIndex:5});await settle();
    assert.deepEqual(f.calls,[["acquire","r1"]]);assert.equal(f.main.GetOpenSideMenu(),0);
    f.control.closed();assert.deepEqual(f.calls,[["acquire","r1"]]);
  }
});
test("late acquire after closed unopened menu cannot open Steam",async()=>{
  const f=fixture();let resolve;f.setVisible(0);f.acquire(()=>new Promise(r=>resolve=r));
  f.control.opened();await settle();assert.deepEqual(f.calls,[["acquire","r1"]]);
  f.control.closed();resolve({ok:true});await settle();
  assert.deepEqual(f.calls,[["acquire","r1"],["close","r1"],["release","r1"]]);assert.equal(f.main.GetOpenSideMenu(),0);
});
test("application change during acquire never force-opens old game menu",async()=>{
  const f=fixture();let resolve;f.setVisible(0);f.acquire(()=>new Promise(r=>resolve=r));
  f.control.opened();await settle();f.setApp(99);f.control.applicationSelected();resolve({ok:true});await settle();
  assert.deepEqual(f.calls,[["acquire","r1"],["release","r1"],["release","r1"]]);assert.equal(f.main.GetOpenSideMenu(),0);
});
test("unload during acquire releases late completion without menu writes",async()=>{
  const f=fixture();let resolve;f.setVisible(0);f.acquire(()=>new Promise(r=>resolve=r));
  f.control.opened();await settle();f.control.dispose();resolve({ok:true});await settle();
  assert.deepEqual(f.calls,[["acquire","r1"],["release","r1"],["release","r1"]]);assert.equal(f.main.GetOpenSideMenu(),0);
});
test("failed main menu open after successful acquisition closes owned handoff",async()=>{
  const f=fixture();f.setVisible(0);f.main.OpenQuickAccessMenu=()=>{throw Error("menu unavailable")};
  f.control.opened();await settle();assert.deepEqual(f.calls,[["acquire","r1"],["close","r1"]]);
});
test("main menu refusing requested state restores owned game handoff",async()=>{
  const f=fixture();f.setVisible(0);f.main.OpenQuickAccessMenu=()=>{};
  f.control.opened();await settle();assert.deepEqual(f.calls,[["acquire","r1"],["close","r1"]]);
});

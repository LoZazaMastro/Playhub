import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";
const exports = {};
vm.runInNewContext(ts.transpileModule(readFileSync(new URL("../src/steamOverlayFallback.ts", import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText, { exports });
const settle = () => new Promise(resolve => setImmediate(resolve));
function fixture() {
  let app = 2979426017, visible = false, clock = 0;
  const calls = [], waits = [], menus = [];
  const schedule = (callback, ms) => { const timer = { at: clock + ms, callback }; waits.push(timer); return timer; };
  const cancel = timer => { const index = waits.indexOf(timer); if (index >= 0) waits.splice(index, 1); };
  let probe;
  const control = exports.createSteamOverlayFallback({ runningApp: () => app,
    now: () => clock,
    visible: () => probe ? probe() : Promise.resolve(visible),
    recover: async (id, menu) => { calls.push([id, clock]); menus.push(menu); },
    delay: ms => new Promise(resolve => schedule(resolve, ms)), schedule, cancel,
  });
  const tick = async () => { await settle(); waits.sort((a,b) => a.at-b.at); const timer=waits.shift(); if(timer){clock=Math.max(clock,timer.at);timer.callback();} await settle(); };
  return { control, calls, menus, setVisible: value => visible = value, setApp: value => app = value, probe: value => probe=value,
    tick, async drain() { await settle(); while (waits.length) await tick(); },
  };
}
const guide = { eKey: 0, ePressed: true, nControllerIndex: 0 };
test("invisible registered overlay retries once at 1500 ms, never before", async () => {
  const f = fixture(); f.control.guide(guide);
  for (let i = 0; i < 14; i++) await f.tick();
  assert.deepEqual(f.calls, []); await f.tick();
  assert.deepEqual(f.calls, [[2979426017, 1500]]); await f.drain();
  assert.equal(f.calls.length, 1);
});
test("an unresponsive visibility IPC cannot postpone the 1500 ms deadline", async () => {
  const f=fixture(); let reply; f.probe(()=>new Promise(resolve=>reply=resolve)); f.control.guide(guide);
  await f.tick(); assert.deepEqual(f.calls,[[2979426017,1500]]);
  reply(true); await f.drain(); assert.equal(f.calls.length,1);
});
test("cold browser enumeration failures keep the authenticated fallback at 1500 ms", async () => {
  for (const probe of [() => Promise.reject(Error("browser initializing")), () => { throw Error("browser closed"); }]) {
    const f = fixture(); f.probe(probe); f.control.guide(guide);
    await f.drain();
    assert.deepEqual(f.calls, [[2979426017, 1500]]);
  }
});
test("invalid shortcut identities never schedule a fallback", async () => {
  for (const app of [NaN, Infinity, 2979426017.5, 0x100000000, -1, 570]) {
    const f = fixture(); f.setApp(app); f.control.guide(guide); await f.drain();
    assert.deepEqual(f.calls, []);
  }
});
test("a working overlay receives no retry", async () => {
  const f = fixture(); f.setVisible(true); f.control.guide(guide); await f.drain(); assert.deepEqual(f.calls, []);
});
test("an early open followed by close never reopens the menu", async () => {
  const f = fixture(); f.control.guide(guide); await f.tick(); f.setVisible(true); await f.tick();
  f.setVisible(false); await f.drain(); assert.deepEqual(f.calls, []);
});
test("app switch, close notification and plugin teardown cancel delayed recovery", async () => {
  for (const cancel of [f => f.setApp(42), f => f.control.invalidate(), f => f.control.dispose()]) {
    const f = fixture(); f.control.guide(guide); await f.tick(); cancel(f); await f.drain(); assert.deepEqual(f.calls, []);
  }
});
test("keyboard, releases, unrelated keys and invalid controller indices do not schedule recovery", async () => {
  for (const event of [{eKey:0}, {...guide,nControllerIndex:-1}, {...guide,ePressed:false}, {...guide,bPressed:false}, {...guide,eKey:2}]) {
    const f = fixture(); f.control.guide(event); await f.drain(); assert.deepEqual(f.calls, []);
  }
});

test("Quick Access recovers its own menu and supersedes an earlier Guide edge", async () => {
  const f = fixture();
  f.control.guide(guide);
  await f.tick();
  f.control.guide({ ...guide, eKey: 1 });
  await f.drain();
  assert.equal(f.calls.length, 1);
  assert.deepEqual(f.menus, ["quick"]);
});

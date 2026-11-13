import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";
import ts from "typescript";

const code = ts.transpile(fs.readFileSync(new URL("../src/quickSettings/livePerformanceController.ts", import.meta.url), "utf8"),
  { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ES2022 });
const { createLivePerformanceController, livePerformanceState } = await import(`data:text/javascript;base64,${Buffer.from(code).toString("base64")}`);
const lease = "a".repeat(32), foreign = "b".repeat(32);
function fixture(initial = {}) {
  let state = { enabled: false, detail: 2, corner: 1, transparent: false, refreshMs: 100,
    status: "Off", appId: 0, lease: null, ...initial };
  let app = 42, suspended = false, restored = 0, held = 0;
  const calls = [], timers = new Map();
  let custom;
  const runtime = createLivePerformanceController({
    get: async () => ({ ...state }), app: () => app, suspended: () => suspended,
    schedule(callback, delay) { const id = {}; timers.set(id, { callback, delay }); return id; },
    cancel(id) { timers.delete(id); },
    suspendSteam: async () => { held++; return async () => { restored++; }; },
    send: async request => {
      calls.push(request);
      if (custom) return custom(request);
      if (request.action === "configure") {
        state = { ...state, ...request, appId: request.enabled ? request.appId : 0,
          lease: request.enabled ? lease : null, status: request.enabled ? "NoData" : "Off" };
      } else if (request.action === "stop") state = { ...state, lease: null, status: "Stopped", appId: 0 };
      else state = { ...state, suspended: request.suspended };
      return { ok: true, state: { ...state } };
    },
  });
  return { runtime, calls, timers, get state() { return state; }, set state(value) { state = value; },
    setApp(value) { app = value; }, setSuspended(value) { suspended = value; },
    custom(value) { custom = value; }, get restored() { return restored; }, get held() { return held; } };
}

test("off starts no timer, provider or Steam setting writes", async () => {
  const f = fixture();
  await f.runtime.refresh();
  assert.equal(f.runtime.read().state.enabled, false);
  assert.equal(f.timers.size, 0); assert.equal(f.calls.length, 0); assert.equal(f.held, 0);
  f.runtime.dispose();
});

test("one enabled subscription renews at two seconds and rebinds the exact current game", async () => {
  const f = fixture();
  await f.runtime.refresh();
  assert.equal(await f.runtime.change({ enabled: true, refreshMs: 50, transparent: true }), true);
  assert.equal(f.calls[0].refreshMs, 20);
  assert.equal(f.runtime.read().state.refreshMs, 20);
  assert.equal(f.held, 1); assert.equal(f.timers.size, 1);
  assert.equal([...f.timers.values()][0].delay, 2000);
  await f.runtime.refresh();
  assert.deepEqual(f.calls[1], { action: "renew", lease, suspended: false });
  f.setApp(2485417537); f.setSuspended(true);
  await f.runtime.refresh();
  assert.equal(f.calls[2].action, "configure"); assert.equal(f.calls[2].appId, 2485417537);
  assert.equal(f.calls[2].lease, lease); assert.equal(f.calls[2].suspended, true);
  f.runtime.dispose();
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(f.calls.at(-1).action, "stop"); assert.equal(f.restored, 1); assert.equal(f.timers.size, 0);
});

test("disabled live counter keeps the selected Steam counter suspended until changing source", async () => {
  const f = fixture(); await f.runtime.refresh();
  await f.runtime.change({ enabled: true }); await f.runtime.change({ enabled: false });
  assert.equal(f.runtime.read().state.enabled, false); assert.equal(f.restored, 0); assert.equal(f.timers.size, 0);
  await f.runtime.restoreSteamMonitor(); assert.equal(f.restored, 1);
  f.runtime.dispose();
});

test("late configure after disposal releases its own new lease without keeping any heartbeat", async () => {
  const f = fixture(); await f.runtime.refresh();
  let finish;
  f.custom(request => request.action === "configure" ? new Promise(resolve => { finish = resolve; })
    : Promise.resolve({ ok: true, state: f.state }));
  const pending = f.runtime.change({ enabled: true });
  await new Promise(resolve => setImmediate(resolve)); f.runtime.dispose();
  finish({ ok: true, state: { ...f.state, enabled: true, lease, appId: 42, status: "NoData" } });
  assert.equal(await pending, false);
  assert.equal(f.calls.filter(value => value.action === "stop").length, 1);
  assert.equal(f.held, 0); assert.equal(f.timers.size, 0);
});

test("a new Steam context waits for the existing lease instead of stealing or renewing it", async () => {
  const f = fixture({ enabled: true, lease: foreign, status: "Ready", appId: 42 });
  await f.runtime.refresh(); assert.equal(f.calls.length, 0); assert.equal(f.held, 0);
  f.state = { ...f.state, lease: null, status: "LeaseExpired", appId: 0 };
  await f.runtime.refresh(); assert.equal(f.calls[0].action, "configure"); assert.equal(f.calls[0].lease, undefined);
  f.runtime.dispose();
});

test("unconfirmed settings and malformed native states stay visibly failed", async () => {
  const f = fixture(); await f.runtime.refresh();
  f.custom(async () => ({ ok: true, state: { ...f.state, enabled: true, refreshMs: 100, lease } }));
  assert.equal(await f.runtime.change({ enabled: true, refreshMs: 50 }), false);
  assert.equal(f.runtime.read().failed, true); assert.equal(f.held, 0);
  for (const change of [{ detail: NaN }, { corner: 7 }, { refreshMs: 10 }, { enabled: 1 }, { lease: "bad" }, { appId: -1 }])
    assert.equal(livePerformanceState({ ...f.state, ...change }), null);
  f.runtime.dispose();
});

test("a lost renewal releases the owned lease and restores Steam rather than faking an active service", async () => {
  const f = fixture(); await f.runtime.refresh(); await f.runtime.change({ enabled: true });
  f.custom(async request => request.action === "renew" ? { ok: false, error: "ExpiredLease", state: f.state }
    : { ok: true, state: f.state });
  await f.runtime.refresh();
  assert.equal(f.runtime.read().failed, true); assert.equal(f.restored, 1);
  assert.equal(f.calls.at(-1).action, "stop");
  f.runtime.dispose();
});

test("legacy 50/100ms preferences configure the automatic minimum and require real 20ms readback", async () => {
  for (const refreshMs of [20, 50, 100]) {
    const f = fixture({ refreshMs, enabled: true });
    await f.runtime.refresh();
    assert.equal(f.calls[0].refreshMs, 20);
    assert.equal(f.runtime.read().state.refreshMs, 20);
    assert.equal(f.runtime.read().failed, false);
    f.runtime.dispose();
  }
});

test("Steam visibility bypasses a delayed status read and serializes rapid open/close without reviving stale HUD state", async () => {
  const f = fixture(); await f.runtime.refresh(); await f.runtime.change({ enabled: true, textColor: "green" });
  assert.equal(f.state.textColor, "green");
  let complete;
  f.custom(request => request.action === "renew" ? new Promise(resolve => { complete = () => resolve({ ok: true, state: { ...f.state, suspended: request.suspended } }); })
    : Promise.resolve({ ok: true, state: f.state }));
  f.setSuspended(true); const opening = f.runtime.menuChanged();
  assert.equal(f.calls.at(-1).suspended, true);
  f.setSuspended(false); const closing = f.runtime.menuChanged();
  assert.equal(f.calls.filter(request => request.action === "renew").length, 1, "Only one visibility request may be in flight");
  complete(); await new Promise(resolve => setImmediate(resolve));
  assert.equal(f.calls.at(-1).suspended, false); complete(); await Promise.all([opening, closing]);
  assert.equal(f.runtime.read().state.suspended, false);
  assert.equal(f.runtime.read().state.textColor, "green"); f.runtime.dispose();
});

test("a delayed visibility renewal preserves newer detail, position and text color configured under the same lease", async () => {
  const f = fixture(); await f.runtime.refresh(); await f.runtime.change({ enabled: true, textColor: "white" });
  let reply;
  const old = {...f.state};
  f.custom(request => request.action === "renew" ? new Promise(resolve => { reply = () => resolve({ok:true,state:{...old,suspended:true}}); })
    : (f.state = {...f.state,...request}, Promise.resolve({ok:true,state:{...f.state}})));
  f.setSuspended(true); const menu = f.runtime.menuChanged();
  assert.equal(await f.runtime.change({detail:4,corner:6,textColor:"red"}),true);
  reply(); await menu;
  assert.equal(f.runtime.read().state.textColor,"red");assert.equal(f.runtime.read().state.detail,4);assert.equal(f.runtime.read().state.corner,6);
  assert.equal(f.runtime.read().state.suspended,true);f.runtime.dispose();
});

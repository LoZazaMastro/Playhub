import assert from 'node:assert/strict';
import fs from 'node:fs';
import test from 'node:test';
import ts from 'typescript';
const source = fs.readFileSync(new URL('../src/steamOverlayControllerLayout.ts', import.meta.url), 'utf8');
const compiled = ts.transpile(source, { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ES2022 });
const { createSteamOverlayControllerLayout } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);
const app = 2979426017, gameId = ((BigInt(app) << 32n) | 0x02000000n).toString();
const sessionId = 'A'.repeat(64), sourceId = 'B'.repeat(64);
const menuProof = { ok: true, gameId, sessionId, companionPid: 41, overlayPid: 42, foregroundScope: 2, overlayOpen: true };
const gameProof = { ...menuProof, kind: 'owned-session', foregroundScope: 1, overlayOpen: false };
const input = { ok: true, sessionId, sourceId, epoch: 2, sequence: 100, sampleAgeMs: 10, ready: true, route: 'bridge', foregroundScope: 1, gameAlive: true };
const menuUrl = 'steam://forceinputappid/769', automaticUrl = 'steam://forceinputappid/0';
const settle = () => new Promise(resolve => setImmediate(resolve));
function fixture(options = {}) {
  let controller = {}, authority, currentApp = app, contextCurrent = true, closed = false, now = 0, timerId = 0, inputReads = 0;
  const context = { window: {}, menu: {} }, commands = [], timers = new Map(), traces = [], reads = [];
  const layout = createSteamOverlayControllerLayout({
    controller: () => { if (options.controllerThrows?.()) throw Error("inventory unavailable"); return controller; }, currentApp: () => currentApp, contextCurrent: () => contextCurrent, menuClosed: () => closed,
    readSnapshot: async (...args) => { reads.push(args); return options.readSnapshot ? options.readSnapshot(...args) : gameProof; },
    claim: owner => { authority = owner; return true; }, owns: owner => authority === owner,
    forget: owner => { if (authority === owner) authority = undefined; },
    execute: (url, guard) => {
      options.beforeExecute?.(url);
      if (!guard() || options.noDispatch?.(url)) return false;
      commands.push(url);
      if (options.throwDispatch?.(url)) throw Error('transport uncertain');
      return true;
    },
    schedule: (fn, ms) => { timers.set(++timerId, { fn, at: now + ms }); return timerId; }, cancel: id => timers.delete(id), now: () => now,
    trace: stage => traces.push(stage), inputBaseline: () => options.baseline === null ? undefined : options.baseline ?? input,
    readGameInput: options.noInputRead ? undefined : async (...args) => {
      inputReads++;
      return options.readGameInput ? options.readGameInput(...args) : { ...input, epoch: 3, sequence: inputReads };
    },
  });
  return {
    layout, context, commands, traces, reads, timers, acquire: (proof = menuProof) => layout.acquire(app, proof, context),
    close: () => { closed = true; layout.closed(context); }, controller: value => { controller = value; },
    external: () => { authority = {}; layout.authorityChanged(); }, app: value => { currentApp = value; },
    contextCurrent: value => { contextCurrent = value; }, menuClosed: value => { closed = value; }, advance: ms => { now += ms; },
    flush: async () => {
      for (let i = 0; i < 35; i++) {
        await settle();
        if (timers.size) {
          const [id, timer] = [...timers].sort((a, b) => a[1].at - b[1].at)[0]; timers.delete(id); now = Math.max(now, timer.at); timer.fn();
        } else { await settle(); if (!timers.size) return; }
      }
      throw Error('Unexpected repeating timer');
    },
  };
}

test('a verified SC menu owns one769 and closes with one automatic0', async () => {
  const f = fixture(); assert.equal(f.acquire(), true); f.acquire(); assert.deepEqual(f.commands, [menuUrl]);
  f.close(); assert.deepEqual(f.commands, [menuUrl, automaticUrl]); await f.flush();
  assert.equal(f.layout.cleanupPending, false); assert.equal(f.layout.phase, 'input-observed'); assert.equal(f.timers.size, 0);
});
test('raw overlay-open lag does not block automatic cleanup or same-game input observation', async () => {
  const f = fixture({ readSnapshot: () => ({ ...gameProof, overlayOpen: true }) }); f.acquire(); f.close(); await f.flush();
  assert.equal(f.layout.phase, 'input-observed');
});
test('automatic0 is immediate while scope2 delays only input-readiness observation', async () => {
  let reads = 0; const f = fixture({ readSnapshot: () => ++reads === 1 ? { ...gameProof, foregroundScope: 2 } : gameProof });
  f.acquire(); f.close(); assert.deepEqual(f.commands, [menuUrl, automaticUrl]); await f.flush(); assert.equal(f.layout.phase, 'input-observed');
});
test('disconnect releases our769 even with the menu still open', () => {
  const f = fixture(); f.acquire(); f.controller(undefined); f.layout.controllerChanged();
  assert.deepEqual(f.commands, [menuUrl, automaticUrl]); assert.equal(f.layout.cleanupPending, false); assert.equal(f.reads.length, 0);
});
test('Alt+Tab releases our override without selecting a foreign profile or waiting on native scope', async () => {
  const f = fixture({ readSnapshot: () => ({ ...gameProof, foregroundScope: 0 }) }); f.acquire(); f.layout.leftContext();
  assert.deepEqual(f.commands, [menuUrl, automaticUrl]); await f.flush(); assert.equal(f.layout.phase, 'input-unverified');
});
test('Xbox and unknown inventory never acquire a Steam Controller override', () => {
  const f = fixture(); f.controller(undefined); assert.equal(f.acquire(), false); assert.deepEqual(f.commands, []);
});
test('wrong session/host/game/foreign readback never blocks owned0 and never claims input restored', async () => {
  for (const change of [{ sessionId: 'C'.repeat(64) }, { gameId: '480' }, { overlayPid: 77 }, { companionPid: 88 }, { foregroundScope: 0 }, { kind: 'other' }]) {
    const f = fixture({ readSnapshot: () => ({ ...gameProof, ...change }) }); f.acquire(); f.close(); await f.flush();
    assert.deepEqual(f.commands, [menuUrl, automaticUrl]); assert.equal(f.layout.cleanupPending, false); assert.equal(f.layout.phase, 'input-unverified');
  }
});
test('malformed acquisition cannot create a global override', () => {
  for (const change of [{ sessionId: undefined }, { sessionId: 'unknown' }, { gameId: 480 }, { gameId: '0123' }, { companionPid: 0 }, { overlayPid: 0x100000000 }, { overlayOpen: false }, { foregroundScope: 1 }]) {
    const f = fixture(); assert.equal(f.acquire({ ...menuProof, ...change }), false); assert.deepEqual(f.commands, []);
  }
});
test('a newer authority retains its override across our close/disconnect/dispose', () => {
  for (const action of [f => f.close(), f => { f.controller(undefined); f.layout.controllerChanged(); }, f => f.layout.dispose()]) {
    const f = fixture(); f.acquire(); f.external(); action(f); assert.deepEqual(f.commands, [menuUrl]); assert.equal(f.layout.cleanupPending, false);
  }
});
test('authority changed adjacent to automatic dispatch vetoes0 without touching the new override', () => {
  let f; f = fixture({ beforeExecute: url => { if (url === automaticUrl) f.external(); } }); f.acquire(); f.close();
  assert.deepEqual(f.commands, [menuUrl]); assert.equal(f.layout.cleanupPending, false);
});
test('a new menu cancels an old pending return response without overwriting its new769', async () => {
  let finish; const f = fixture({ readSnapshot: () => new Promise(resolve => { finish = resolve; }) });
  f.acquire(); f.close(); await settle(); f.menuClosed(false); assert.equal(f.acquire(), true); finish(gameProof); await f.flush();
  assert.deepEqual(f.commands, [menuUrl, automaticUrl, menuUrl]); assert.equal(f.layout.cleanupPending, true); assert.equal(f.layout.phase, 'menu'); assert.equal(f.timers.size, 0);
});
test('replaced native context does not prevent cleanup of an override we still own', async () => {
  const f = fixture({ beforeExecute: url => { if (url === automaticUrl) f.contextCurrent(false); } }); f.acquire(); f.close(); await f.flush();
  assert.deepEqual(f.commands, [menuUrl, automaticUrl]); assert.equal(f.layout.cleanupPending, false);
});
test('dispose cancels a pending readonly response and every timer without another0', async () => {
  let finish; const f = fixture({ readSnapshot: () => new Promise(resolve => { finish = resolve; }) });
  f.acquire(); f.close(); await settle(); f.layout.dispose(); finish(gameProof); await f.flush();
  assert.equal(f.reads.length, 1); assert.deepEqual(f.commands, [menuUrl, automaticUrl]); assert.equal(f.timers.size, 0); assert.equal(f.layout.phase, 'released');
});
test('dispatch refused during teardown retains only our cleanup lease and permits an explicit retry', async () => {
  let refused = true; const f = fixture({ noDispatch: url => url === automaticUrl && refused }); f.acquire(); f.layout.dispose();
  assert.deepEqual(f.commands, [menuUrl]); assert.equal(f.layout.cleanupPending, true); assert.equal(f.timers.size, 0);
  refused = false; assert.equal(f.layout.retryCleanup(), true); assert.deepEqual(f.commands, [menuUrl, automaticUrl]); assert.equal(f.layout.cleanupPending, false); assert.equal(f.acquire(), false);
});
test('an uncertain dispatch retains ownership until0 cleanup, rather than pretending nothing was sent', () => {
  const f = fixture({ throwDispatch: url => url === menuUrl }); assert.equal(f.acquire(), false); assert.equal(f.layout.cleanupPending, true);
  assert.equal(f.layout.retryCleanup(), true); assert.deepEqual(f.commands, [menuUrl, automaticUrl]); assert.equal(f.layout.cleanupPending, false);
});
test('a never-settling snapshot is bounded1200ms and is not an input-ready result', async () => {
  const f = fixture({ readSnapshot: () => new Promise(() => {}) }); f.acquire(); f.close(); await f.flush();
  assert.deepEqual(f.commands, [menuUrl, automaticUrl]); assert.equal(f.layout.phase, 'input-unverified'); assert.equal(f.timers.size, 0);
});
test('a never-settling game-input readback is bounded by the same deadline', async () => {
  const f = fixture({ readGameInput: () => new Promise(() => {}) }); f.acquire(); f.close(); await f.flush();
  assert.equal(f.layout.phase, 'input-unverified'); assert.equal(f.timers.size, 0);
});
test('a late snapshot never becomes input-ready after its deadline', async () => {
  let finish; const f = fixture({ readSnapshot: () => new Promise(resolve => { finish = resolve; }) });
  f.acquire(); f.close(); await settle(); f.advance(1201); finish(gameProof); await f.flush();
  assert.equal(f.layout.phase, 'input-unverified'); assert.equal(f.timers.size, 0);
});
test('new external authority invalidates an in-flight input observation after our0 completed', async () => {
  let finish; const f = fixture({ readGameInput: () => new Promise(resolve => { finish = resolve; }) });
  f.acquire(); f.close(); await settle(); f.external(); finish({ ...input, epoch: 3, sequence: 1 }); await f.flush();
  assert.equal(f.layout.phase, 'external'); assert.deepEqual(f.commands, [menuUrl, automaticUrl]); assert.equal(f.timers.size, 0);
});
test('game-exit cleanup requires an exact retained exit proof', () => {
  const f = fixture(); f.acquire(); const good = { ok: true, sessionId, gameId, sourceExited: true, steamForeground: true, otherGameRunning: false };
  for (const change of [{ sessionId: 'C'.repeat(64) }, { sourceExited: false }, { steamForeground: false }, { otherGameRunning: true }, { sessionId: 13 }]) assert.equal(f.layout.exited({ ...good, ...change }), false);
  assert.equal(f.commands.length, 1); assert.equal(f.layout.exited(good), true); assert.equal(f.layout.phase, 'released');
});
test('URL submission alone never becomes an input-readiness claim', async () => {
  for (const options of [{ noInputRead: true }, { baseline: null }]) {
    const f = fixture(options); f.acquire(); f.close(); await f.flush(); assert.equal(f.commands.length, 2); assert.equal(f.layout.phase, 'input-unverified');
  }
});
test('old epoch/repeated sequence/stale samples/wrong source never prove reacquisition', async () => {
  for (const bad of [{ epoch: 2 }, { sequence: 1 }, { sampleAgeMs: 501 }, { sourceId: 'C'.repeat(64) }, { ready: false }, { gameAlive: false }, { foregroundScope: 0 }, { route: 'unknown' }, { sessionId: 'C'.repeat(64) }]) {
    const f = fixture({ readGameInput: () => ({ ...input, epoch: 3, sequence: 1, ...bad }) }); f.acquire(); f.close(); await f.flush(); assert.equal(f.layout.phase, 'input-unverified');
  }
});
test('two fresh samples on one new source epoch prove observation, not button consumption', async () => {
  let n = 0; const f = fixture({ readGameInput: () => ({ ...input, epoch: 3, sequence: ++n }) }); f.acquire(); f.close(); await f.flush();
  assert.equal(f.layout.phase, 'input-observed'); assert.equal(n, 2); assert.ok(f.traces.includes('game-input-new-epoch-observed'));
});
test('a second game cannot inherit an owned override without an authority handoff', () => {
  const f = fixture(); f.acquire(); const otherApp = app + 1; f.app(otherApp);
  assert.equal(f.layout.acquire(otherApp, { ...menuProof, gameId: ((BigInt(otherApp) << 32n) | 0x02000000n).toString(), sessionId: 'C'.repeat(64) }, f.context), false);
  assert.deepEqual(f.commands, [menuUrl]);
});
test('controller or app replacement cancels input-readiness observation after cleanup', async () => {
  for (const change of [f => f.controller({}), f => f.app(app + 1)]) {
    let finish; const f = fixture({ readSnapshot: () => new Promise(resolve => { finish = resolve; }) }); f.acquire(); f.close(); await settle(); change(f); finish(gameProof); await f.flush(); assert.equal(f.layout.phase, 'input-unverified');
  }
});
test('known dispatch refusal creates no false override lease', () => {
  const f = fixture({ noDispatch: () => true }); assert.equal(f.acquire(), false); assert.equal(f.layout.cleanupPending, false); assert.deepEqual(f.commands, []);
});

test('inventory failure during a reload still releases only our currently-owned769', () => {
  let unknown = false; const f = fixture({ controllerThrows: () => unknown }); f.acquire(); unknown = true;
  f.layout.controllerChanged(); assert.deepEqual(f.commands, [menuUrl, automaticUrl]); assert.equal(f.layout.cleanupPending, false);
});

test('immediate dispose cancels the queued readonly dispatch itself', async () => {
  const f = fixture(); f.acquire(); f.close(); f.layout.dispose(); await f.flush();
  assert.equal(f.reads.length, 0); assert.equal(f.timers.size, 0); assert.deepEqual(f.commands, [menuUrl, automaticUrl]);
});
test('authority change before the microtask prevents the stale snapshot RPC', async () => {
  const f = fixture(); f.acquire(); f.close(); f.external(); await f.flush();
  assert.equal(f.reads.length, 0); assert.equal(f.layout.phase, 'external'); assert.equal(f.timers.size, 0);
});

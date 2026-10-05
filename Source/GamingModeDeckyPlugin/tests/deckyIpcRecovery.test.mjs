import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import ts from 'typescript';
const exports = {};
vm.runInNewContext(ts.transpileModule(fs.readFileSync(new URL('../src/deckyIpcRecovery.ts', import.meta.url), 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText, { exports });
const failure = { folder: 'Playhub-Artworks', name: 'Playhub Artworks', session: '11884:100', log: '2026-10-03 10.42.56.log' };
const settle = () => new Promise(resolve => setImmediate(resolve));
function setup(overrides = {}) {
  const timers = [], reloads = [], cancelled = [], warnings = [];
  let scans = 0, claims = 0;
  const stop = exports.installDeckyIpcRecovery({
    scan: async () => { scans++; return [failure]; },
    claim: async () => { claims++; return { ok: true, name: failure.name }; },
    reload: async name => { reloads.push(name); },
    schedule: (callback, delay) => { const timer = { callback, delay }; timers.push(timer); return timer; },
    cancel: timer => cancelled.push(timer), warn: error => warnings.push(error), ...overrides
  });
  return { timers, reloads, cancelled, warnings, stop, scans: () => scans, claims: () => claims };
}
test('three spaced startup checks reload only the proven plugin once and create no idle timers', async () => {
  const state = setup();
  assert.deepEqual(state.timers.map(timer => timer.delay), [3000, 10000, 25000]);
  for (const timer of state.timers) { timer.callback(); await settle(); }
  assert.equal(state.scans(), 3); assert.equal(state.claims(), 1);
  assert.deepEqual(state.reloads, ['Playhub Artworks']); assert.equal(state.timers.length, 3);
});
test('healthy startup and rejected durable session claim do not reload', async () => {
  for (const overrides of [{ scan: async () => [] }, { claim: async () => ({ ok: false }) }, { claim: async () => ({ ok: true, name: 'unrelated' }) }]) {
    const state = setup(overrides);
    for (const timer of state.timers) { timer.callback(); await settle(); }
    assert.deepEqual(state.reloads, []);
  }
});
test('failed reload is never retried and scan errors are bounded', async () => {
  let count = 0;
  const state = setup({ reload: async () => { count++; throw new Error('denied'); } });
  for (const timer of state.timers) { timer.callback(); await settle(); }
  assert.equal(count, 1); assert.equal(state.warnings.length, 1);
  const offline = setup({ scan: async () => { throw new Error('offline'); } });
  for (const timer of offline.timers) { timer.callback(); await settle(); }
  assert.equal(offline.warnings.length, 3); assert.deepEqual(offline.reloads, []);
});
test('cleanup cancels startup callbacks and suppresses a pending result', async () => {
  let resolveScan;
  const state = setup({ scan: () => new Promise(resolve => { resolveScan = resolve; }) });
  state.timers[0].callback(); state.stop(); resolveScan([failure]); await settle();
  for (const timer of state.timers) timer.callback();
  assert.equal(state.cancelled.length, 3); assert.deepEqual(state.reloads, []); assert.equal(state.claims(), 0);
});
test('overlapping startup checks share the one-attempt guard', async () => {
  const state = setup();
  state.timers.forEach(timer => timer.callback()); await settle();
  assert.equal(state.claims(), 1); assert.deepEqual(state.reloads, ['Playhub Artworks']);
});

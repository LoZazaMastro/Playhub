import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import ts from 'typescript';
function fixture(read, update) {
  const timers = new Map(); let id = 0; const exports = {};
  vm.runInNewContext(ts.transpileModule(fs.readFileSync(new URL('../src/statusPoll.ts', import.meta.url), 'utf8'), {
    compilerOptions: {module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022}
  }).outputText, {exports, AbortController, setTimeout: (fn, delay) => { timers.set(++id, {fn, delay}); return id; }, clearTimeout: id => timers.delete(id)});
  return {stop: exports.pollStatus(read, update), timers};
}
const settle = () => new Promise(resolve => setImmediate(resolve));
test('offline polls back off, recover automatically and stop on unload', async () => {
  let offline = true; const values = [];
  const f = fixture(async () => { if (offline) throw Error('Failed to fetch'); return 'ready'; }, value => values.push(value));
  await settle();
  for (const expected of [10000, 20000, 30000]) {
    const [id, timer] = [...f.timers][0]; assert.equal(timer.delay, expected);
    f.timers.delete(id); timer.fn(); await settle();
  }
  offline = false;
  const [id, timer] = [...f.timers][0]; f.timers.delete(id); timer.fn(); await settle();
  assert.equal(values.at(-1), 'ready');
  assert.equal([...f.timers.values()][0].delay, 5000);
  f.stop(); assert.equal(f.timers.size, 0);
});
test('unload aborts pending fetch and does not deliver a stale state update', async () => {
  let signal; let finish; const values = [];
  const f = fixture(s => { signal = s; return new Promise(resolve => {finish = resolve;}); }, value => values.push(value));
  f.stop(); assert.equal(signal.aborted, true); finish('late'); await settle();
  assert.equal(values.length, 0); assert.equal(f.timers.size, 0);
});
test('a stalled service is aborted before scheduling one backed-off retry', async () => {
  let active = 0; let maximum = 0; const values = [];
  const f = fixture(signal => new Promise((resolve, reject) => {
    active++; maximum = Math.max(maximum, active);
    signal.addEventListener('abort', () => { active--; reject(new Error('aborted')); }, {once: true});
  }), value => values.push(value));
  assert.equal(f.timers.size, 1);
  const [deadlineId, deadline] = [...f.timers][0];
  assert.equal(deadline.delay, 4000);
  f.timers.delete(deadlineId); deadline.fn(); await settle();
  assert.equal(active, 0); assert.deepEqual(values, [undefined]);
  const [retryId, retry] = [...f.timers][0];
  assert.equal(retry.delay, 10000);
  f.timers.delete(retryId); retry.fn(); await settle();
  assert.equal(maximum, 1);
  f.stop(); await settle();
  assert.equal(active, 0); assert.equal(f.timers.size, 0);
  assert.deepEqual(values, [undefined]);
});

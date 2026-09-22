import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';
import ts from 'typescript';

const exports = {};
vm.runInNewContext(ts.transpileModule(fs.readFileSync(new URL('../src/panelIntegration/settledWriter.ts', import.meta.url), 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText, { exports });
const { createSettledWriter } = exports;
const flush = async () => { for (let i = 0; i < 12; i++) await Promise.resolve(); };
function fixture() {
  let now = 0;
  const timers = [], calls = [], successes = [], errors = [], requests = [];
  const writer = createSettledWriter({ delayMs: 100,
    scheduler: { schedule(fn, delay) {
      const timer = { fn, at: now + delay, cancelled: false, fired: false };
      timers.push(timer); return () => { timer.cancelled = true; };
    } },
    write(value) { calls.push(value); return new Promise((resolve, reject) => requests.push({ resolve, reject })); },
    onSettled: (result, value) => successes.push([result, value]),
    onError: (error, value) => errors.push([error.message, value]),
  });
  return { writer, calls, successes, errors, requests, timers,
    async advance(ms) {
      now += ms;
      for (const timer of timers) if (!timer.cancelled && !timer.fired && timer.at <= now) {
        timer.fired = true; timer.fn();
      }
      await flush();
    },
  };
}

test('controller repeats coalesce only after the last input settles', async () => {
  const f = fixture();
  f.writer.enqueue(10); await f.advance(90); f.writer.enqueue(20);
  await f.advance(90); assert.deepEqual(f.calls, []);
  await f.advance(10); assert.deepEqual(f.calls, [20]);
  f.requests[0].resolve(19); await flush();
  assert.deepEqual(f.successes, [[19, 20]]);
});

test('slow writes serialize and stale readback cannot replace newer intent', async () => {
  const f = fixture();
  f.writer.enqueue(10); await f.advance(100);
  f.writer.enqueue(20); await f.advance(100); f.writer.enqueue(30);
  f.requests[0].resolve(10); await flush();
  assert.deepEqual(f.calls, [10]); assert.deepEqual(f.successes, []);
  await f.advance(100); assert.deepEqual(f.calls, [10, 30]);
  f.requests[1].resolve(29); await flush();
  assert.deepEqual(f.successes, [[29, 30]]);
});

test('ready latest value starts when in-flight write settles, even on failure', async () => {
  const f = fixture();
  f.writer.enqueue(1); await f.advance(100); f.writer.enqueue(2); await f.advance(100);
  f.requests[0].reject(new Error('stale failure')); await flush();
  assert.deepEqual(f.calls, [1, 2]); assert.deepEqual(f.errors, []);
  f.requests[1].reject(new Error('offline')); await flush();
  assert.deepEqual(f.errors, [['offline', 2]]);
  f.writer.enqueue(3); await f.advance(100); assert.deepEqual(f.calls, [1, 2, 3]);
});

test('unmount cancels unsent writes and ignores callbacks already queued by host', async () => {
  const f = fixture(); f.writer.enqueue(1); f.writer.dispose(); f.writer.dispose();
  f.timers[0].fn(); await f.advance(100);
  assert.deepEqual(f.calls, []); assert.equal(f.writer.enqueue(2), false);
});

test('unmount does not undo an in-flight write or publish its late result', async () => {
  const f = fixture(); f.writer.enqueue(1); await f.advance(100);
  f.writer.enqueue(2); f.writer.dispose(); f.requests[0].resolve(1);
  await f.advance(100);
  assert.deepEqual(f.calls, [1]); assert.deepEqual(f.successes, []);
});

test('resource change cancels queued work and invalidates previous readback', async () => {
  const f = fixture(); f.writer.enqueue(1); await f.advance(100);
  f.writer.enqueue(2); f.writer.cancelPending(); f.requests[0].resolve(1);
  await f.advance(100); assert.deepEqual(f.successes, []); assert.deepEqual(f.calls, [1]);
  f.writer.enqueue(3); await f.advance(100); assert.deepEqual(f.calls, [1, 3]);
});

test('superseded host timer cannot flush a newer request early', async () => {
  const f = fixture(); f.writer.enqueue(1); f.writer.enqueue(2);
  f.timers[0].fn(); await flush(); assert.deepEqual(f.calls, []);
  await f.advance(100); assert.deepEqual(f.calls, [2]);
});

test('invalid settle duration is rejected', () => {
  for (const delayMs of [-1, NaN, Infinity]) {
    assert.throws(() => createSettledWriter({ delayMs }), /finite and non-negative/);
  }
});

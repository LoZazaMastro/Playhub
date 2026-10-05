import test from 'node:test';
import assert from 'node:assert/strict';
import {
  mountPlayhubRenderer, getPlayhubRendererOwnership, RendererOwnershipError,
} from '../Runtime/renderer-ownership.mjs';

function deferred() {
  let resolve, reject;
  const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
  return { promise, resolve, reject };
}

function assertOwned(renderer, owner, state) {
  const value = getPlayhubRendererOwnership({ renderer });
  assert.equal(value?.owner, owner);
  assert.equal(value?.state, state);
  assert.ok(Object.isFrozen(value));
}

function assertBlocked(renderer, owner = 'standalone') {
  let called = false;
  assert.throws(() => mountPlayhubRenderer({
    renderer, owner, mount() { called = true; },
  }), error => error instanceof RendererOwnershipError && error.code === 'ALREADY_OWNED');
  assert.equal(called, false);
}

test('claim is synchronous; both same-owner and cross-owner duplicate starts are rejected', async () => {
  const renderer = {};
  const gate = deferred();
  const lease = mountPlayhubRenderer({ renderer, owner: 'decky', mount: () => gate.promise });
  assertOwned(renderer, 'decky', 'mounting');
  assertBlocked(renderer, 'decky');
  assertBlocked(renderer, 'standalone');
  gate.resolve();
  assert.equal(await lease.ready, lease);
  assertOwned(renderer, 'decky', 'mounted');
  await lease.dispose();
  assert.equal(getPlayhubRendererOwnership({ renderer }), null);
});

test('asynchronous cleanup holds ownership and runs only once', async () => {
  const renderer = {};
  const entered = deferred(), cleanupGate = deferred();
  let calls = 0;
  const lease = mountPlayhubRenderer({ renderer, owner: 'standalone', mount: () => async () => {
    calls++;
    entered.resolve();
    await cleanupGate.promise;
  } });
  await lease.ready;
  const first = lease.dispose();
  assert.equal(lease.dispose(), first);
  await entered.promise;
  assertOwned(renderer, 'standalone', 'disposing');
  assertBlocked(renderer);
  cleanupGate.resolve();
  await first;
  assert.equal(calls, 1);
  assert.equal(lease.snapshot().state, 'disposed');
});

test('cancellation during mount waits for late cleanup before allowing a new owner', async () => {
  const renderer = {};
  const entered = deferred(), mountGate = deferred(), cleanupEntered = deferred(), cleanupGate = deferred();
  const abort = new AbortController();
  let receivedSignal;
  const lease = mountPlayhubRenderer({ renderer, owner: 'decky', signal: abort.signal, async mount({ signal }) {
    receivedSignal = signal;
    entered.resolve();
    await mountGate.promise;
    return async () => { cleanupEntered.resolve(); await cleanupGate.promise; };
  } });
  const readyRejected = assert.rejects(lease.ready, { name: 'AbortError' });
  await entered.promise;
  abort.abort('host stopped');
  assert.equal(receivedSignal.aborted, true);
  assertOwned(renderer, 'decky', 'disposing');
  assertBlocked(renderer);
  mountGate.resolve();
  await cleanupEntered.promise;
  assertBlocked(renderer);
  cleanupGate.resolve();
  await readyRejected;
  await lease.dispose();
  assert.equal(getPlayhubRendererOwnership({ renderer }), null);
});

test('dispose before the mount microtask prevents mount execution', async () => {
  const renderer = {};
  let called = false;
  const lease = mountPlayhubRenderer({ renderer, owner: 'decky', mount() { called = true; } });
  const done = lease.dispose();
  assertBlocked(renderer);
  await assert.rejects(lease.ready, { name: 'AbortError' });
  await done;
  assert.equal(called, false);
  assert.equal(getPlayhubRendererOwnership({ renderer }), null);
});

test('mount failure unwinds partial cleanup in reverse order before releasing', async () => {
  const renderer = {};
  const order = [], cleanupEntered = deferred(), cleanupGate = deferred();
  const failure = new Error('initialization failed');
  const lease = mountPlayhubRenderer({ renderer, owner: 'standalone', async mount({ defer }) {
    defer(() => order.push('first'));
    defer(async () => { order.push('second'); cleanupEntered.resolve(); await cleanupGate.promise; });
    throw failure;
  } });
  const rejected = assert.rejects(lease.ready, error => error === failure);
  await cleanupEntered.promise;
  assertBlocked(renderer);
  cleanupGate.resolve();
  await rejected;
  assert.deepEqual(order, ['second', 'first']);
  assert.equal(getPlayhubRendererOwnership({ renderer }), null);
});

test('cleanup failure keeps the owner failed, runs remaining cleanup and cannot be unlocked by retry', async () => {
  const renderer = {};
  const order = [];
  const lease = mountPlayhubRenderer({ renderer, owner: 'decky', mount({ defer }) {
    defer(() => order.push('remaining'));
    return () => { order.push('failed'); throw new Error('cleanup failed'); };
  } });
  await lease.ready;
  const disposal = lease.dispose();
  await assert.rejects(disposal, AggregateError);
  assert.equal(lease.dispose(), disposal);
  assert.deepEqual(order, ['failed', 'remaining']);
  assertOwned(renderer, 'decky', 'failed');
  assert.equal(getPlayhubRendererOwnership({ renderer }).failure, 'cleanup-failed');
  assertBlocked(renderer);
});

test('mount and cleanup failure are both reported while ownership remains blocked', async () => {
  const renderer = {};
  const mountFailure = new Error('mount failed'), cleanupFailure = new Error('cleanup failed');
  const lease = mountPlayhubRenderer({ renderer, owner: 'standalone', mount({ defer }) {
    defer(async () => { throw cleanupFailure; });
    throw mountFailure;
  } });
  await assert.rejects(lease.ready, error => error instanceof AggregateError &&
    error.errors[0] === mountFailure && error.errors[1].errors[0] === cleanupFailure);
  assertOwned(renderer, 'standalone', 'failed');
  assertBlocked(renderer);
});

test('old leases and abort signals cannot dispose a newer generation', async () => {
  const renderer = {};
  const abort = new AbortController();
  const first = mountPlayhubRenderer({ renderer, owner: 'decky', signal: abort.signal, mount() {} });
  await first.ready;
  await first.dispose();
  const second = mountPlayhubRenderer({ renderer, owner: 'standalone', mount() {} });
  await second.ready;
  assert.equal(second.generation, first.generation + 1);
  abort.abort();
  await first.dispose();
  assertOwned(renderer, 'standalone', 'mounted');
  assert.equal(getPlayhubRendererOwnership({ renderer }).generation, second.generation);
  await second.dispose();
});

test('separately evaluated module instances coordinate through the Playhub Symbol registry', async () => {
  const secondModule = await import('../Runtime/renderer-ownership.mjs?separate-evaluation');
  const renderer = { unrelatedPlugin: { mounted: true } };
  const lease = mountPlayhubRenderer({ renderer, owner: 'decky', mount() {} });
  await lease.ready;
  assert.throws(() => secondModule.mountPlayhubRenderer({ renderer, owner: 'standalone', mount() {} }),
    error => error.code === 'ALREADY_OWNED');
  assert.equal(secondModule.getPlayhubRendererOwnership({ renderer }).generation, lease.generation);
  assert.deepEqual(Object.keys(renderer), ['unrelatedPlugin']);
  assert.deepEqual(Object.getOwnPropertySymbols(renderer).map(Symbol.keyFor), ['playhub.renderer.ownership.v1']);
  await lease.dispose();
  const next = secondModule.mountPlayhubRenderer({ renderer, owner: 'standalone', mount() {} });
  await next.ready;
  assert.equal(next.generation, 2);
  await next.dispose();
});

test('independent renderer globals can own different Playhub instances', async () => {
  const one = {}, two = {};
  const a = mountPlayhubRenderer({ renderer: one, owner: 'decky', mount() {} });
  const b = mountPlayhubRenderer({ renderer: two, owner: 'standalone', mount() {} });
  await Promise.all([a.ready, b.ready]);
  await a.dispose();
  assertOwned(two, 'standalone', 'mounted');
  await b.dispose();
});

test('already-aborted startup does not claim the renderer or call mount', () => {
  const renderer = {}, abort = new AbortController();
  abort.abort();
  let called = false;
  assert.throws(() => mountPlayhubRenderer({ renderer, owner: 'decky', signal: abort.signal,
    mount() { called = true; } }), { name: 'AbortError' });
  assert.equal(called, false);
  assert.equal(getPlayhubRendererOwnership({ renderer }), null);
  assert.equal(Object.getOwnPropertySymbols(renderer).length, 0);
});

test('deferred cleanup identity is deduplicated and registration closes after mount settles', async () => {
  const renderer = {};
  let register, calls = 0;
  const cleanup = () => { calls++; };
  const lease = mountPlayhubRenderer({ renderer, owner: 'standalone', mount({ defer }) {
    register = defer;
    defer(cleanup);
    return cleanup;
  } });
  await lease.ready;
  assert.throws(() => register(() => {}), error => error.code === 'LATE_CLEANUP_REGISTRATION');
  await lease.dispose();
  assert.equal(calls, 1);
});

test('even null thrown by mount is a failure and releases only after cleanup', async () => {
  const renderer = {};
  let cleaned = false;
  const lease = mountPlayhubRenderer({ renderer, owner: 'decky', mount({ defer }) {
    defer(() => { cleaned = true; });
    throw null;
  } });
  let rejected = false;
  try { await lease.ready; } catch (error) { rejected = true; assert.equal(error, null); }
  assert.equal(rejected, true);
  assert.equal(cleaned, true);
  assert.equal(getPlayhubRendererOwnership({ renderer }), null);
});

test('invalid owner or mount callback never creates a registry', () => {
  const renderer = {};
  assert.throws(() => mountPlayhubRenderer({ renderer, owner: 'other-plugin', mount() {} }), TypeError);
  assert.throws(() => mountPlayhubRenderer({ renderer, owner: 'decky', mount: true }), TypeError);
  assert.equal(Object.getOwnPropertySymbols(renderer).length, 0);
});

test('abort after readiness runs asynchronous cleanup and retains the lock until completion', async () => {
  const renderer = {}, abort = new AbortController();
  const entered = deferred(), cleanupGate = deferred();
  const lease = mountPlayhubRenderer({ renderer, owner: 'standalone', signal: abort.signal,
    mount: () => async () => { entered.resolve(); await cleanupGate.promise; },
  });
  await lease.ready;
  abort.abort('host shutdown');
  await entered.promise;
  assert.equal(lease.signal.reason, 'host shutdown');
  assertOwned(renderer, 'standalone', 'disposing');
  assertBlocked(renderer);
  cleanupGate.resolve();
  await lease.dispose();
  assert.equal(getPlayhubRendererOwnership({ renderer }), null);
});

test('a renderer never inherits another renderer ownership from its prototype', async () => {
  const parent = {};
  const first = mountPlayhubRenderer({ renderer: parent, owner: 'decky', mount() {} });
  await first.ready;
  const renderer = Object.create(parent);
  assert.equal(getPlayhubRendererOwnership({ renderer }), null);
  const second = mountPlayhubRenderer({ renderer, owner: 'standalone', mount() {} });
  await second.ready;
  assertOwned(parent, 'decky', 'mounted');
  assertOwned(renderer, 'standalone', 'mounted');
  await Promise.all([first.dispose(), second.dispose()]);
});

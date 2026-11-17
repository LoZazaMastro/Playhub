import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';
import ts from 'typescript';

const source = fs.readFileSync(process.env.PLAYHUB_LS_UI_SOURCE ?? new URL('../src/quickSettings/index.tsx', import.meta.url), 'utf8');
const ast = ts.createSourceFile('index.tsx', source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
const declarations = new Map();
function visit(node) {
  if (ts.isVariableStatement(node)) for (const declaration of node.declarationList.declarations) {
    if (ts.isIdentifier(declaration.name)) declarations.set(declaration.name.text, node.getText(ast));
  }
  ts.forEachChild(node, visit);
}
visit(ast);
const names = ['losslessEpoch', 'losslessWrites', 'losslessDraft', 'losslessQueue',
  'losslessMounted', 'losslessReadFlight', 'losslessReadAgain', 'losslessConfirmed', 'losslessUncertain',
  'losslessControlToken', 'losslessResolveToken', 'losslessQueueGeneration', 'validLosslessToken',
  'boundedLossless', 'invalidateLossless', 'acceptLosslessStatus', 'loadLossless', 'queueLosslessChange',
  'onScalingToggle', 'onFrameGen'];
const deferred = () => { let resolve, reject; const promise = new Promise((yes, no) => { resolve = yes; reject = no; }); return { promise, resolve, reject }; };
const settle = async () => { for (let i = 0; i < 3; i++) await new Promise(resolve => setImmediate(resolve)); };
const token = n => n.toString(16).padStart(32, '0');
const status = (extra = {}) => ({ ok: true, installed: true, can_scale: true, scaling_active: false,
  scaling_requested: false, capture_active: false, capture_state: 'inactive', control_busy: false,
  control_token: token(1), current_game: { app_id: 42 }, ...extra });
function fixture(initial = status()) {
  let snapshot = initial, pending = false, unknown = false, reading = false, updates = 0, serial = 1;
  let read = async () => status({ control_token: token(serial) });
  let write = async () => ({ ok: true, control_token: token(++serial) });
  const calls = [], requests = [], reads = [], notices = [], timers = new Map(); let nextTimer = 0;
  const context = { useRef: value => ({ current: value }), Promise,
    window: { setTimeout: (fn, ms) => { timers.set(++nextTimer, { fn, ms }); return nextTimer; }, clearTimeout: id => timers.delete(id) },
    lossless: snapshot, local: { notConnected: 'offline', losslessGameOnlyHint: 'game required' },
    getLosslessStatus: resolve => { reads.push(resolve); return read(resolve); },
    setLosslessScaling: (enabled, expected, app) => { calls.push(['scale', enabled]); requests.push([expected, app]); return write(enabled, expected, app); },
    setLosslessSetting: (key, value, expected, app) => { calls.push([key, value]); requests.push([expected, app]); return write(value, expected, app); },
    setLossless: value => { ++updates; snapshot = typeof value === 'function' ? value(snapshot) : value; },
    setLosslessPending: value => { ++updates; pending = value; },
    setLosslessUnknown: value => { ++updates; unknown = value; },
    setLosslessReading: value => { ++updates; reading = value; }, notify: value => notices.push(value),
  };
  const code = names.filter(name => declarations.has(name)).map(name => declarations.get(name)).join('\n') + `
    if (typeof acceptLosslessStatus !== 'undefined') acceptLosslessStatus(lossless, 0);
    this.api = { load: loadLossless, toggle: onScalingToggle, frameGen: onFrameGen,
      mounted: losslessMounted, epoch: losslessEpoch, writes: losslessWrites,
      unmount: () => { losslessMounted.current=false; ++losslessEpoch.current;
        if(typeof losslessQueueGeneration !== 'undefined') ++losslessQueueGeneration.current; } };`;
  vm.runInNewContext(ts.transpileModule(code, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText, context);
  return { ...context.api, calls, requests, reads, notices, read: value => { read = value; }, write: value => { write = value; },
    expire: () => { const current = [...timers.values()]; timers.clear(); for (const t of current) { assert.equal(t.ms, 16000); t.fn(); } },
    state: () => ({ snapshot, pending, unknown, reading, updates }) };
}

test('native command completion alone does not paint capture active; actual readback does', async () => {
  const f = fixture(), command = deferred(), receipt = deferred();
  f.write(() => command.promise); f.read(() => receipt.promise);
  f.toggle(true); await settle();
  assert.equal(f.state().pending, true); assert.equal(f.state().snapshot.scaling_active, false);
  command.resolve({ ok: true, active: true, control_token: token(2) }); await settle();
  assert.equal(f.state().snapshot.scaling_active, false);
  assert.equal(f.state().unknown, true);
  receipt.resolve(status({ scaling_active: true, capture_active: true, capture_state: 'active', control_token: token(3) })); await settle();
  assert.equal(f.state().snapshot.scaling_active, true); assert.equal(f.state().unknown, false);
});

test('rapid toggle and frame generation commands execute serially with consumed tokens', async () => {
  const f = fixture(), first = deferred(); let count = 0;
  f.write(() => ++count === 1 ? first.promise : Promise.resolve({ ok: true, control_token: token(count + 1) }));
  f.toggle(true); f.frameGen('LSFG3'); f.toggle(false); await settle();
  assert.deepEqual(f.calls, [['scale', true]]);
  first.resolve({ ok: true, control_token: token(2) }); await settle();
  assert.deepEqual(f.calls, [['scale', true], ['frame_gen', 'LSFG3'], ['scale', false]]);
  assert.deepEqual(f.requests, [[token(1),42], [token(2),42], [token(3),42]]);
  assert.equal(f.writes.current, 0); assert.equal(f.state().pending, false);
});

test('old status poll cannot overwrite a completed new toggle; fresh reconciliation follows', async () => {
  const f = fixture(), old = deferred(); let reads = 0;
  f.read(() => ++reads === 1 ? old.promise : Promise.resolve(status({ scaling_active: true, capture_state: 'active', capture_active: true, control_token: token(3) })));
  const poll = f.load(); f.toggle(true); await settle();
  old.resolve(status()); await poll; await settle();
  assert.equal(reads, 2); assert.equal(f.state().snapshot.scaling_active, true);
});

test('failed command discards queued writes until an explicit readback and new user intent', async () => {
  const f = fixture(); f.write(async () => { throw new Error('transport'); });
  f.toggle(true); f.toggle(false); await settle();
  assert.deepEqual(f.calls, [['scale', true]]); assert.deepEqual(f.notices, ['offline']);
  assert.equal(f.state().pending, false); assert.equal(f.state().unknown, true);
  f.toggle(false); await settle(); assert.equal(f.calls.length, 1);
  await f.load(); f.write(async () => ({ ok: true, control_token: token(2) }));
  f.toggle(false); await settle(); assert.equal(f.calls.length, 2);
});

test('unmounted QAM cannot apply a late read or start the next queued capture', async () => {
  const f = fixture(), command = deferred(); f.write(() => command.promise);
  f.toggle(true); f.toggle(false); await settle(); f.unmount(); const updates = f.state().updates;
  command.resolve({ ok: true, control_token: token(2) }); await settle();
  assert.deepEqual(f.calls, [['scale', true]]); assert.equal(f.state().updates, updates);
});

test('successful stop followed by failed readback is unknown, never a stale active claim', async () => {
  const f = fixture(status({ scaling_active: true, capture_active: true, capture_state: 'active' }));
  f.read(async () => { throw new Error('read transport'); });
  f.toggle(false); await settle();
  assert.equal(f.state().unknown, true); assert.equal(f.state().snapshot.capture_state, 'unknown');
  assert.equal(f.state().reading, false); f.toggle(true); await settle(); assert.equal(f.calls.length, 1);
});

test('write deadline unlocks read-only Retry, fences the old token, and ignores a late receipt', async () => {
  const f = fixture(), old = deferred(); f.write(() => old.promise);
  f.toggle(true); f.toggle(false); await settle(); f.expire(); await settle();
  assert.equal(f.state().unknown, true); assert.equal(f.state().pending, false); assert.equal(f.calls.length, 1);
  f.read(async expected => { assert.equal(expected, token(1)); return status({ control_token: token(3) }); });
  await f.load(); assert.equal(f.state().unknown, false);
  const updates = f.state().updates; old.resolve({ ok: true, active: true, control_token: token(2) }); await settle();
  assert.equal(f.state().updates, updates); assert.equal(f.state().snapshot.capture_active, false);
  f.read(async () => status({ control_token: token(5) })); f.write(async () => ({ ok: true, control_token: token(4) }));
  f.toggle(false); await settle(); assert.deepEqual(f.requests[1], [token(3),42]);
});

test('read deadline clears the flight so Retry can recover; late old read cannot overwrite it', async () => {
  const f = fixture(), old = deferred(); f.read(() => old.promise);
  const first = f.load(); assert.equal(f.state().reading, true); f.expire(); await first;
  assert.equal(f.state().reading, false); assert.equal(f.state().unknown, true);
  f.read(async () => status({ control_token: token(3) })); await f.load(); const updates = f.state().updates;
  old.resolve(status({ capture_state: 'active', capture_active: true })); await settle();
  assert.equal(f.state().updates, updates); assert.equal(f.state().unknown, false); assert.equal(f.state().snapshot.capture_active, false);
});

for (const extra of [{ok:false}, {control_busy:true}, {control_token:''}, {current_game:{app_id:-1}}, {capture_state:'unknown'}, {capture_active:undefined}]) {
  test(`unconfirmed status cannot restore writes: ${JSON.stringify(extra)}`, async () => {
    const f = fixture(); f.read(async () => status(extra)); await f.load();
    assert.equal(f.state().unknown, true); f.toggle(true); await settle(); assert.equal(f.calls.length, 0);
  });
}

test('unmount during read deadline and late completion leaves all visible state untouched', async () => {
  const f = fixture(), old = deferred(); f.read(() => old.promise); const read = f.load();
  f.unmount(); const updates = f.state().updates; f.expire(); await read;
  old.resolve(status()); await settle(); assert.equal(f.state().updates, updates);
});

test('actual LS controls are disabled while unknown and expose localized read-only Retry', () => {
  for (const prefix of ['<QuickDropdown disabled={losslessUnknown', '<QuickSlider disabled={losslessUnknown', 'disabled={losslessUnknown || losslessPending']) assert.ok(source.includes(prefix));
  assert.ok(source.includes('losslessUnknown ? "unknown"'));
  assert.ok(source.includes('disabled={losslessReading} onClick={() => void loadLossless()}'));
});

test('unknown status has a distinct nonempty label in all 12 languages', () => {
  const locale = fs.readFileSync(new URL('../src/quickSettings/losslessCaptureLocale.ts', import.meta.url), 'utf8');
  const ctx = { exports: {} };
  vm.runInNewContext(ts.transpileModule(locale, { compilerOptions: { module: ts.ModuleKind.CommonJS } }).outputText, ctx);
  for (const language of ['en','it','de','es','fr','pt','ru','uk','zh','ja','ko','hi']) {
    const label = ctx.exports.losslessCaptureLabel(language,'unknown'); assert.ok(label.length > 0);
    assert.notEqual(label, ctx.exports.losslessCaptureLabel(language,'active'));
  }
});

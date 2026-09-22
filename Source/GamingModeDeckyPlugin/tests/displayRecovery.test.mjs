import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';
import ts from 'typescript';
import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { fileURLToPath } from 'node:url';

const source = fs.readFileSync(new URL('../src/quickSettings/index.tsx', import.meta.url), 'utf8');
// Keep the real hooks, effects and handlers; replace only the visual return surface.
const exposeHandlers = context => file => ts.visitNode(file, function visit(node) {
  if (ts.isFunctionDeclaration(node) && node.name?.text === 'QuickSettingsContent') {
    const statements = [...node.body.statements];
    const boundary = statements.findIndex(statement => ts.isVariableStatement(statement)
      && statement.declarationList.declarations.some(item => item.name.getText(file) === 'outputOptions'));
    assert.ok(boundary >= 0, 'Update the test adapter if the render boundary changes');
    const result = ts.factory.createReturnStatement(ts.factory.createObjectLiteralExpression([
      ts.factory.createShorthandPropertyAssignment('changeDisplay'),
      ...['onOutput', 'onInput', 'onMicVolume', 'loadAudio', 'audio', 'display', 'hdr'].map(name => ts.factory.createShorthandPropertyAssignment(name)),
    ]));
    return ts.factory.updateFunctionDeclaration(node, node.modifiers, node.asteriskToken,
      node.name, node.typeParameters, node.parameters, node.type,
      ts.factory.createBlock([...statements.slice(0, boundary), result], true));
  }
  return ts.visitEachChild(node, visit, context);
});
const code = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.React },
  transformers: { before: [exposeHandlers] },
}).outputText;
const flush = async () => { for (let i = 0; i < 30; i++) await Promise.resolve(); };
const settledWriter = {};
vm.runInNewContext(ts.transpileModule(fs.readFileSync(new URL('../src/panelIntegration/settledWriter.ts', import.meta.url), 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText, { exports: settledWriter });

function backend() {
  const state = { pending: null, beginCalls: 0, finishCalls: 0, reads: 0,
    loseBegin: false, loseFinish: false, loseRead: false, results: new Map() };
  return Object.assign(state, {
    async beginDisplayChange() {
      state.beginCalls++;
      if (state.pending) return { ok: false, busy: true };
      state.pending = { state: 'previewing', token: `tx-${state.beginCalls}`, secondsRemaining: 9 };
      if (state.loseBegin) { state.loseBegin = false; throw new Error('begin response lost after apply'); }
      return { ok: true, ...state.pending, expiresAt: Date.now() + 9000 };
    },
    async getDisplayChange(token) {
      state.reads++;
      if (state.loseRead) { state.loseRead = false; throw new Error('state unavailable'); }
      if (state.results.has(token)) return { state: 'completed', token, ...state.results.get(token) };
      return state.pending ?? { state: 'idle' };
    },
    async finishDisplayChange(token, keep) {
      state.finishCalls++;
      let result = state.results.get(token);
      if (!result && state.pending?.token === token) {
        result = { ok: true, terminal: true, kept: keep };
        state.results.set(token, result);
        state.pending = null;
      }
      if (state.loseFinish) { state.loseFinish = false; throw new Error('finish response lost after commit'); }
      return result ?? { ok: false, terminal: false };
    },
  });
}

function renderer(api, realRpc) {
  let slots = [], cursor = 0, effects = [], cleanups = [], first = true;
  let timerId = 0;
  const intervals = new Map(), timeouts = new Map(), modals = [];
  const React = {
    createElement: (type, props, ...children) => ({ type, props: props ?? {}, children }),
    useRef(value) { const i = cursor++; return slots[i] ??= { current: value }; },
    useState(value) {
      const i = cursor++;
      if (!(i in slots)) slots[i] = typeof value === 'function' ? value() : value;
      return [slots[i], next => { slots[i] = typeof next === 'function' ? next(slots[i]) : next; }];
    },
    useEffect(callback) { if (first) effects.push(callback); },
  };
  let rpc = new Proxy({ ...api, getInitialState: async () => ({ capabilities: {} }) }, {
    get: (target, name) => target[name] ?? (async () => ({})),
  });
  if (realRpc) {
    const client = { exports: {} };
    const clientCode = ts.transpileModule(fs.readFileSync(new URL('../src/quickSettings/backend.ts', import.meta.url), 'utf8'), {
      compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
    }).outputText;
    vm.runInNewContext(clientCode, { exports: client.exports,
      require: name => { assert.equal(name, '../controlBackend'); return { call: realRpc }; },
      window: { setTimeout, clearTimeout }, AbortController,
      fetch: async () => { throw new Error('Hardware HTTP forbidden in test'); },
    });
    rpc = client.exports;
  }
  const exports = {};
  vm.runInNewContext(code, {
    exports, React, Date, console, performance: { now: () => 0 },
    window: {
      setInterval: fn => { intervals.set(++timerId, fn); return timerId; },
      clearInterval: id => intervals.delete(id),
      setTimeout: fn => { timeouts.set(++timerId, fn); return timerId; },
      clearTimeout: id => timeouts.delete(id),
    },
    require(name) {
      if (name === 'react') return React;
      if (name === './backend') return rpc;
      if (name === '../panelIntegration/settledWriter') return settledWriter;
      if (name === '@decky/api') return { toaster: { toast() {} } };
      if (name === '@decky/ui') return { showModal: element => {
        const modal = { element, closed: false, Close() { this.closed = true; } };
        modals.push(modal); return modal;
      } };
      if (name === './translations') return { t: () => ({}) };
      return {};
    },
  });
  return {
    modals,
    render() {
      cursor = 0;
      return exports.QuickSettingsContent({ page: 'audio', collapsed: [], onToggle() {}, locale: 'en' });
    },
    async mount() {
      cursor = 0;
      const handlers = exports.QuickSettingsContent({ page: 'audio', collapsed: [], onToggle() {}, locale: 'en' });
      first = false;
      cleanups = effects.map(effect => effect()); effects = [];
      await flush(); return handlers;
    },
    async poll() { for (const fn of [...intervals.values()]) fn(); await flush(); },
    async settleSliders() {
      const queued = [...timeouts.values()]; timeouts.clear();
      for (const fn of queued) fn(); await flush();
    },
    async unmount() { for (const cleanup of cleanups) cleanup?.(); await flush(); },
  };
}

test('audio polling preserves a pending microphone draft while updating endpoint readback', async () => {
  let snapshot = { ok: true, outputs: [], inputs: [], input_volume: 20, default_output_id: 'old' };
  const ui = renderer({ ...backend(), getAudioDevices: async () => snapshot });
  const handlers = await ui.mount();
  handlers.onMicVolume(70);
  snapshot = { ...snapshot, default_output_id: 'new' };
  await handlers.loadAudio();
  assert.equal(ui.render().audio.input_volume, 70);
  assert.equal(ui.render().audio.default_output_id, 'new');
  await ui.unmount();
});

test('rapid endpoint selection does not install the earlier acknowledgement', async () => {
  let snapshot = { ok: true, outputs: [], inputs: [], default_output_id: 'old' };
  const pending = [];
  const ui = renderer({ ...backend(), getAudioDevices: async () => snapshot,
    setAudioOutput: id => new Promise(resolve => pending.push(() => {
      snapshot = { ...snapshot, default_output_id: id }; resolve(snapshot);
    })),
  });
  const handlers = await ui.mount();
  const first = handlers.onOutput('a'), second = handlers.onOutput('b');
  await flush(); assert.equal(pending.length, 1);
  pending[0](); await first; await flush();
  assert.equal(ui.render().audio.default_output_id, 'b');
  assert.equal(pending.length, 2);
  pending[1](); await second;
  assert.equal(ui.render().audio.default_output_id, 'b');
  await ui.unmount();
});

test('input switch cancels microphone debounce before any volume RPC', async () => {
  let snapshot = { ok: true, inputs: [], outputs: [], default_input_id: 'a', input_volume: 20 };
  const writes = [];
  const ui = renderer({ ...backend(), getAudioDevices: async () => snapshot,
    setMicrophoneVolumeLevel: async (...args) => { writes.push(args); return {}; },
    setAudioInput: async id => (snapshot = { ...snapshot, default_input_id: id, input_volume: 80 }),
  });
  const h = await ui.mount(); h.onMicVolume(30);
  await h.onInput('b'); await ui.settleSliders();
  assert.deepEqual(writes, []);
  assert.equal(ui.render().audio.input_volume, 80); await ui.unmount();
});

test('in-flight microphone write completes on old endpoint before input switch; old ack cannot rewrite new state', async () => {
  let snapshot = { ok: true, inputs: [], outputs: [], default_input_id: 'a', input_volume: 20 };
  let finishMic;
  const events = [];
  const ui = renderer({ ...backend(), getAudioDevices: async () => snapshot,
    setMicrophoneVolumeLevel: (level, endpoint) => new Promise(resolve => {
      events.push(['mic', endpoint, level]);
      finishMic = () => resolve({ ok: true, endpoint_id: endpoint, input_volume: level });
    }),
    setAudioInput: async id => { events.push(['input', id]); return snapshot = { ...snapshot, default_input_id: id, input_volume: 80 }; },
  });
  const h = await ui.mount(); h.onMicVolume(30); await ui.settleSliders();
  const change = h.onInput('b'); await flush();
  assert.deepEqual(events, [['mic', 'a', 30]]);
  finishMic(); await change; await flush();
  assert.deepEqual(events, [['mic', 'a', 30], ['input', 'b']]);
  assert.equal(ui.render().audio.default_input_id, 'b');
  assert.equal(ui.render().audio.input_volume, 80); await ui.unmount();
});

test('microphone input made during endpoint switch targets only the confirmed new endpoint', async () => {
  let snapshot = { ok: true, inputs: [], outputs: [], default_input_id: 'a', input_volume: 20 };
  let finishInput;
  const writes = [];
  const ui = renderer({ ...backend(), getAudioDevices: async () => snapshot,
    setAudioInput: id => new Promise(resolve => { finishInput = () => resolve(snapshot = { ...snapshot, default_input_id: id, input_volume: 80 }); }),
    setMicrophoneVolumeLevel: async (level, endpoint) => {
      writes.push([level, endpoint]); return { ok: true, endpoint_id: endpoint, input_volume: level };
    },
  });
  const h = await ui.mount(); const change = h.onInput('b'); await flush();
  h.onMicVolume(40); await ui.settleSliders(); assert.deepEqual(writes, []);
  finishInput(); await change; await flush();
  assert.deepEqual(writes, [[40, 'b']]);
  assert.equal(ui.render().audio.input_volume, 40); await ui.unmount();
});

test('successful display change opens one modal and blocks overlapping begin', async () => {
  const api = backend(), ui = renderer(api), handlers = await ui.mount();
  await handlers.changeDisplay({ kind: 'hdr', enabled: true });
  await handlers.changeDisplay({ kind: 'display', hz: 120 });
  assert.equal(api.beginCalls, 1);
  assert.equal(ui.modals.length, 1);
  assert.equal(await ui.modals[0].element.props.onKeep(), true);
  await handlers.changeDisplay({ kind: 'display', hz: 120 });
  assert.equal(api.beginCalls, 2);
  await ui.unmount();
});

test('failed audio write restores authoritative selection instead of leaving optimistic state', async () => {
  const snapshot = { ok: true, outputs: [], inputs: [], default_output_id: 'old', default_input_id: 'mic' };
  const api = { ...backend(), getAudioDevices: async () => snapshot,
    setAudioOutput: async () => ({ ...snapshot, ok: false }) };
  const ui = renderer(api), handlers = await ui.mount();
  await handlers.onOutput('new'); await flush();
  assert.equal(ui.render().audio.default_output_id, 'old');
  await ui.unmount();
});

test('audio polling started before a write cannot overwrite its readback', async () => {
  const snapshot = { ok: true, outputs: [], inputs: [], default_output_id: 'old', default_input_id: 'mic' };
  let release, reads = 0;
  const api = { ...backend(), getAudioDevices: async () => ++reads === 1 ? snapshot : new Promise(resolve => { release = resolve; }),
    setAudioOutput: async () => ({ ...snapshot, default_output_id: 'new' }) };
  const ui2 = renderer(api);
  const h = await ui2.mount();
  const polling = h.loadAudio(); await flush();
  await h.onOutput('new'); await flush();
  release(snapshot); await polling;
  assert.equal(ui2.render().audio.default_output_id, 'new');
  await ui2.unmount();
});

test('real TS handlers/client and Python coordinator apply, read back, keep, rollback and recover lost RPCs', async t => {
  const child = spawn(process.env.PYTHON || 'python', ['-u', fileURLToPath(new URL('./display_audio_rpc_fixture.py', import.meta.url))], { stdio: ['pipe', 'pipe', 'pipe'] });
  const lines = createInterface({ input: child.stdout });
  const pending = new Map(); let id = 0, errors = '', lostMethod;
  child.stderr.on('data', data => { errors += data; });
  lines.on('line', line => {
    const result = JSON.parse(line), request = pending.get(result.id);
    pending.delete(result.id);
    if (result.error) request.reject(new Error(result.error));
    else request.resolve(result.result);
  });
  child.on('exit', code => { for (const request of pending.values()) request.reject(new Error(`fixture exit ${code}: ${errors}`)); });
  const rpc = async (method, ...args) => {
    if (child.exitCode !== null) throw new Error(`fixture already exited: ${errors}`);
    const requestId = ++id;
    const result = await new Promise((resolve, reject) => {
      pending.set(requestId, { resolve, reject });
      child.stdin.write(`${JSON.stringify({ id: requestId, method, args })}\n`);
    });
    if (method === lostMethod) { lostMethod = undefined; throw new Error('response lost after execution'); }
    return result;
  };
  t.after(async () => {
    const exited = child.exitCode !== null ? Promise.resolve() : new Promise(resolve => child.once('exit', resolve));
    child.stdin.end(); await exited; lines.close();
  });
  let ui = renderer({}, rpc), handlers = await ui.mount();
  // Crossing the process boundary flushes earlier bootstrap RPCs as well.
  await rpc('_inspect');
  for (const request of [{ kind: 'display', width: 1280, height: 720 }, { kind: 'display', hz: 120 }, { kind: 'hdr', enabled: false }]) {
    lostMethod = 'begin_display_change';
    await handlers.changeDisplay(request);
    const modal = ui.modals.at(-1);
    assert.ok(modal && !modal.closed, 'Lost begin restores the confirmation modal');
    assert.ok(modal.element.props.secondsRemaining > 0 && modal.element.props.secondsRemaining <= (request.kind === 'hdr' ? 30 : 15));
    const applied = await rpc('_inspect');
    assert.equal(applied.pending.state, 'previewing');
    lostMethod = 'finish_display_change';
    assert.equal(await modal.element.props.onKeep(), true);
    const kept = await rpc('_inspect');
    assert.equal(kept.pending, null);
    if (request.kind === 'display') assert.deepEqual(kept.current, kept.registered);
    else assert.deepEqual(kept.hdr, { 'screen-a': false, 'screen-b': false });
  }
  const before = await rpc('_inspect');
  await rpc('_fail_display');
  const modalCount = ui.modals.length;
  await handlers.changeDisplay({ kind: 'display', width: 1024, height: 768 });
  assert.equal(ui.modals.length, modalCount, 'Failed apply/readback never shows a successful preview');
  assert.deepEqual((await rpc('_inspect')).current, before.current);
  assert.equal((await rpc('_inspect')).pending, null, 'Failed readback rolls back before releasing the transaction');
  await handlers.changeDisplay({ kind: 'display', width: 1024, height: 768 });
  await ui.unmount();
  ui = renderer({}, rpc); handlers = await ui.mount(); await rpc('_inspect');
  await ui.poll(); await rpc('_inspect');
  assert.equal(ui.modals.length, 1, 'Remount reconciles the real pending transaction');
  await rpc('_expire'); await ui.poll(); await rpc('_inspect'); await flush();
  assert.deepEqual((await rpc('_inspect')).current, before.current);
  assert.equal(ui.render().display.current.width, before.current.width, 'Watchdog rollback updates dropdown readback');
  lostMethod = 'set_audio_output';
  await handlers.onOutput('speaker-b');
  await handlers.onInput('mic-b');
  assert.equal(ui.render().audio.default_output_id, 'speaker-b');
  assert.equal(ui.render().audio.default_input_id, 'mic-b');
  await rpc('_fail_audio');
  await handlers.onOutput('speaker-a');
  assert.equal(ui.render().audio.default_output_id, 'speaker-b', 'Failed native apply restores the readback');
  await ui.unmount();
});

test('input and output endpoint writes are serialized and preserve the latest full snapshot', async () => {
  let snapshot = { ok: true, outputs: [], inputs: [], default_output_id: 'old', default_input_id: 'mic' };
  let release, inputCalls = 0;
  const ui = renderer({ ...backend(), getAudioDevices: async () => snapshot,
    setAudioOutput: async id => {
      await new Promise(resolve => { release = resolve; });
      snapshot = { ...snapshot, default_output_id: id }; return snapshot;
    },
    setAudioInput: async id => { inputCalls++; snapshot = { ...snapshot, default_input_id: id }; return snapshot; },
  });
  const handlers = await ui.mount();
  const output = handlers.onOutput('new'), input = handlers.onInput('new-mic');
  await flush(); assert.equal(inputCalls, 0);
  release(); await Promise.all([output, input]);
  assert.equal(ui.render().audio.default_output_id, 'new');
  assert.equal(ui.render().audio.default_input_id, 'new-mic');
  await ui.unmount();
});

test('lost finish response can be retried idempotently without starting another transaction', async () => {
  const api = backend(), ui = renderer(api), handlers = await ui.mount();
  await handlers.changeDisplay({ kind: 'hdr', enabled: true });
  api.loseFinish = true;
  api.loseRead = true;
  assert.equal(await ui.modals[0].element.props.onKeep(), false);
  assert.equal(api.pending, null);
  assert.equal(await ui.modals[0].element.props.onKeep(), true);
  assert.equal(api.beginCalls, 1);
  assert.equal(api.finishCalls, 2);
  await ui.unmount();
});

test('remount reconciles a backend preview without a second begin', async () => {
  const api = backend(), before = renderer(api), handlers = await before.mount();
  await handlers.changeDisplay({ kind: 'display', hz: 120 });
  await before.unmount();
  const after = renderer(api);
  await after.mount(); await after.poll();
  assert.ok(api.reads > 0, 'Remount must read backend transaction state');
  assert.equal(after.modals.length, 1, 'Existing preview needs its confirmation UI');
  assert.equal(after.modals[0].element.props.secondsRemaining, 9);
  assert.equal(api.beginCalls, 1);
  await after.unmount();
});

test('lost begin response reconciles the applied preview and restores its confirmation', async () => {
  const api = backend(); api.loseBegin = true;
  const ui = renderer(api), handlers = await ui.mount();
  await handlers.changeDisplay({ kind: 'hdr', enabled: true });
  await ui.poll();
  assert.equal(api.pending?.token, 'tx-1');
  assert.ok(api.reads > 0, 'Lost begin response must trigger state reconciliation');
  assert.equal(ui.modals.length, 1);
  assert.equal(api.beginCalls, 1);
  await ui.unmount();
});

test('lost terminal response eventually releases local busy state after backend becomes idle', async () => {
  const api = backend(), ui = renderer(api), handlers = await ui.mount();
  await handlers.changeDisplay({ kind: 'display', hz: 120 });
  api.loseFinish = true;
  await ui.modals[0].element.props.onKeep();
  await ui.poll();
  await handlers.changeDisplay({ kind: 'hdr', enabled: true });
  assert.equal(api.beginCalls, 2, 'An idle backend must not leave the local change lock stuck');
  await ui.unmount();
});

test('lost keep response recovers the terminal result without sending an inverse write', async () => {
  const api = backend(), ui = renderer(api), handlers = await ui.mount();
  await handlers.changeDisplay({ kind: 'hdr', enabled: true });
  api.loseFinish = true;
  assert.equal(await ui.modals[0].element.props.onKeep(), true);
  assert.equal(api.results.get('tx-1').kept, true);
  assert.equal(api.finishCalls, 1);
  await ui.unmount();
});

test('recovery_required blocks begin until the backend reports idle', async () => {
  const api = backend();
  api.pending = { state: 'recovery_required', token: 'recovery-1' };
  const ui = renderer(api), handlers = await ui.mount();
  await handlers.changeDisplay({ kind: 'hdr', enabled: true });
  assert.equal(api.beginCalls, 0);
  assert.equal(ui.modals.length, 0);
  api.pending = null;
  await ui.poll();
  await handlers.changeDisplay({ kind: 'hdr', enabled: true });
  assert.equal(api.beginCalls, 1);
  await ui.unmount();
});

test('unavailable transaction status fails closed on first mount', async () => {
  const api = backend();
  api.getDisplayChange = async () => { throw new Error('offline'); };
  const ui = renderer(api), handlers = await ui.mount();
  await handlers.changeDisplay({ kind: 'hdr', enabled: true });
  assert.equal(api.beginCalls, 0);
  assert.equal(ui.modals.length, 0);
  await ui.unmount();
});

test('late begin response after unmount does not open an orphan modal', async () => {
  const api = backend();
  const original = api.beginDisplayChange;
  let release;
  api.beginDisplayChange = async () => {
    const result = await original();
    await new Promise(resolve => { release = resolve; });
    return result;
  };
  const ui = renderer(api), handlers = await ui.mount();
  const request = handlers.changeDisplay({ kind: 'hdr', enabled: true });
  await flush();
  assert.equal(typeof release, 'function');
  await ui.unmount(); release(); await request;
  assert.equal(ui.modals.length, 0);
  const remounted = renderer(api);
  await remounted.mount();
  assert.equal(remounted.modals.length, 1);
  assert.equal(api.beginCalls, 1);
  await remounted.unmount();
});

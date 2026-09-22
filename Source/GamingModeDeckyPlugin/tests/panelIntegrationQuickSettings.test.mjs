import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';
import ts from 'typescript';

const source = fs.readFileSync(new URL('../src/quickSettings/index.tsx', import.meta.url), 'utf8');
const ast = ts.createSourceFile('index.tsx', source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
const hook = ast.statements.find(n => ts.isFunctionDeclaration(n) && n.name?.text === 'useDebounced');
const component = ast.statements.find(n => ts.isFunctionDeclaration(n) && n.name?.text === 'QuickSettingsContent');
const names = ['micVolumePending', 'micEndpointEpoch', 'commitVolume', 'commitBrightness', 'commitMicVolume', 'onVolume', 'onBrightness', 'onMicVolume'];
const declarations = component.body.statements.filter(n => ts.isVariableStatement(n)
  && n.declarationList.declarations.some(d => names.includes(d.name.getText(ast))));
assert.equal(declarations.length, names.length, 'Test must execute all real call sites and pending state');
const helpers = ast.statements.filter(n => ts.isVariableStatement(n)
  && n.declarationList.declarations.some(d => ['clampPercent', 'brightnessToDimmer'].includes(d.name.getText(ast))));
const compile = text => ts.transpileModule(text, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText;
const helperExports = {};
vm.runInNewContext(compile(fs.readFileSync(new URL('../src/panelIntegration/settledWriter.ts', import.meta.url), 'utf8')), { exports: helperExports });
const flush = async () => { for (let i = 0; i < 20; i++) await Promise.resolve(); };

test('real microphone RPC client forwards the expected endpoint without dropping it', async () => {
  const client = {}, calls = [];
  vm.runInNewContext(compile(fs.readFileSync(new URL('../src/quickSettings/backend.ts', import.meta.url), 'utf8')), {
    exports: client,
    require(name) { assert.equal(name, '../controlBackend'); return { call: async (...args) => { calls.push(args); return { ok: false }; } }; },
  });
  await client.setMicrophoneVolumeLevel(40, 'mic-a');
  assert.equal(calls[0][0], 'set_microphone_volume');
  assert.equal(calls[0][1].level, 40);
  assert.equal(calls[0][1].expected_endpoint, 'mic-a');
});

test('audio/video dropdowns use full-width native layout without fabricated endpoint defaults', () => {
  const found = [];
  const visit = node => {
    if (ts.isJsxSelfClosingElement(node) && node.tagName.getText(ast) === 'QuickDropdown') {
      const attrs = node.attributes.properties;
      const label = attrs.find(a => a.name?.getText(ast) === 'label')?.initializer?.getText(ast);
      if (['{local.audioOutput}', '{local.microphoneInput}', '{local.resolution}', '{local.refreshRate}'].includes(label)) {
        found.push(label);
        assert.ok(attrs.some(a => a.name?.getText(ast) === 'fullWidth'));
        if (label === '{local.audioOutput}' || label === '{local.microphoneInput}') {
          assert.doesNotMatch(attrs.find(a => a.name?.getText(ast) === 'value').getText(ast), /Options\[0\]/);
          assert.match(attrs.find(a => a.name?.getText(ast) === 'disabled').getText(ast), /!audio\.ok/);
        }
      }
    }
    ts.forEachChild(node, visit);
  };
  visit(ast); assert.equal(found.length, 4);
});

function harness() {
  let cursor = 0, now = 0, nextTimer = 0;
  const slots = [], effects = [], timers = new Map(), calls = [], notices = [], draft = {};
  const context = {
    ...helperExports, SLIDER_DEBOUNCE: 260,
    useRef(initial) { const i = cursor++; return slots[i] ??= { current: initial }; },
    useEffect(fn, deps) {
      const i = cursor++, old = slots[i];
      if (!old || deps.some((v, j) => v !== old.deps[j])) {
        old?.cleanup?.();
        slots[i] = { deps }; effects.push(() => { slots[i].cleanup = fn(); });
      }
    },
    window: {
      setTimeout(fn, ms) { const id = ++nextTimer; timers.set(id, { fn, at: now + ms }); return id; },
      clearTimeout(id) { timers.delete(id); },
    },
    local: { notConnected: 'offline', audioDevicesUnavailable: 'mic offline' },
    notify: message => notices.push(message),
    audioConfirmed: { current: { input_volume: 17, default_input_id: 'mic-a' } },
    audioQueue: { current: Promise.resolve() },
    getAudioDevices: async () => ({ ok: true, input_volume: 17, default_input_id: 'mic-a' }),
    ...Object.fromEntries(['Volume', 'Dimmer', 'Audio'].map(name => [`set${name}`, fn => {
      draft[name] = fn(draft[name] ?? {});
    }])),
    ...Object.fromEntries(['setVolumeLevel', 'setDimmerLevel', 'setMicrophoneVolumeLevel'].map(name => [name, (value, endpoint) =>
      new Promise((resolve, reject) => calls.push({ name, value, endpoint, resolve, reject }))])),
  };
  vm.createContext(context);
  vm.runInContext(compile(`${helpers.map(n => n.getText(ast)).join('\n')}\n${hook.getText(ast)}\nfunction render() {
    ${declarations.map(n => n.getText(ast)).join('\n')}
    return { onVolume, onBrightness, onMicVolume };
  }`), context);
  return { calls, notices, draft, context,
    render() { cursor = 0; const result = context.render(); while (effects.length) effects.shift()(); return result; },
    async advance(ms) {
      now += ms;
      for (const [id, timer] of [...timers]) if (timer.at <= now) { timers.delete(id); timer.fn(); }
      await flush();
    },
    unmount() { for (const slot of slots) slot.cleanup?.(); },
  };
}

test('actual volume, microphone and brightness handlers preserve conversion and timing', async () => {
  const h = harness(), ui = h.render();
  ui.onVolume(110); ui.onBrightness(70); ui.onMicVolume(42.6);
  assert.equal(h.draft.Volume.level, 100); assert.equal(h.draft.Dimmer.level, 30);
  assert.equal(h.draft.Audio.input_volume, 43);
  await h.advance(34); assert.equal(h.calls.length, 0);
  await h.advance(1);
  assert.deepEqual(h.calls.map(c => [c.name, c.value]), [['setVolumeLevel', 100], ['setDimmerLevel', 30]]);
  await h.advance(145); assert.equal(h.calls[2].name, 'setMicrophoneVolumeLevel');
  assert.equal(h.calls[2].value, 43); h.unmount();
});

for (const [handler, delay] of [['onVolume', 35], ['onBrightness', 35], ['onMicVolume', 180]]) {
  test(`${handler}: actual RPC promise blocks overlap and suppresses stale failure`, async () => {
    const h = harness(), ui = h.render();
    ui[handler](10); await h.advance(delay);
    ui[handler](20); ui[handler](30); await h.advance(delay);
    assert.equal(h.calls.length, 1, 'Returning void here would launch a second RPC');
    h.calls[0].reject(new Error('old failure')); await flush();
    assert.equal(h.calls.length, 2); assert.deepEqual(h.notices, []);
    assert.equal(h.calls[1].value, handler === 'onBrightness' ? 70 : 30);
    h.calls[1].resolve({ ok: false }); await flush();
    assert.deepEqual(h.notices, [handler === 'onMicVolume' ? 'mic offline' : 'offline']);
    h.unmount();
  });
}

test('rerender retains queued work but uses current callbacks and localized error', async () => {
  const h = harness(), ui = h.render(); ui.onVolume(25);
  h.context.local = { notConnected: 'new language', audioDevicesUnavailable: 'new mic' };
  let latestWrite = false;
  h.context.setVolumeLevel = async () => { latestWrite = true; throw new Error('offline'); };
  h.render(); await h.advance(35);
  assert.equal(latestWrite, true); assert.deepEqual(h.notices, ['new language']); h.unmount();
});

test('actual hook cleanup cancels all queued channels and late errors', async () => {
  const h = harness(), ui = h.render();
  ui.onVolume(1); await h.advance(35);
  ui.onVolume(2); ui.onBrightness(3); ui.onMicVolume(4);
  h.unmount(); h.calls[0].reject(new Error('late'));
  ui.onVolume(5); await h.advance(1000);
  assert.equal(h.calls.length, 1); assert.deepEqual(h.notices, []);
});

test('microphone accepts verified readback without replacing other audio fields', async () => {
  const h = harness(), ui = h.render();
  h.draft.Audio = { default_input_id: 'mic-a', ok: true };
  ui.onMicVolume(50); await h.advance(180);
  assert.equal(h.calls[0].endpoint, 'mic-a');
  h.calls[0].resolve({ ok: true, endpoint_id: 'mic-a', input_volume: 49, default_input_id: 'unrelated' }); await flush();
  assert.equal(h.draft.Audio.input_volume, 49);
  assert.equal(h.draft.Audio.default_input_id, 'mic-a');
  assert.deepEqual(h.notices, []); h.unmount();
});

for (const result of [{ ok: false, input_volume: 50 }, { ok: true, input_volume: 50 }, { ok: true, endpoint_id: 'mic-b', input_volume: 50 }, {}, null,
  { ok: true }, { ok: true, input_volume: '50' }, { ok: true, input_volume: NaN },
  { ok: true, input_volume: Infinity }, { ok: true, input_volume: -1 },
  { ok: true, input_volume: 101 }, { ok: true, input_volume: 20 }]) {
  test(`microphone rejects unconfirmed readback ${JSON.stringify(result)}`, async () => {
    const h = harness(), ui = h.render(); ui.onMicVolume(50); await h.advance(180);
    h.calls[0].resolve(result); await flush();
    assert.deepEqual(h.notices, ['mic offline']);
    assert.equal(h.draft.Audio.input_volume, 17, 'Failed write restores authoritative readback');
    h.unmount();
  });
}

test('microphone stale success cannot replace newer draft, and late success cannot update after unmount', async () => {
  const h = harness(), ui = h.render(); ui.onMicVolume(50); await h.advance(180);
  ui.onMicVolume(60); await h.advance(180);
  h.calls[0].resolve({ ok: true, endpoint_id: 'mic-a', input_volume: 49 }); await flush();
  assert.equal(h.draft.Audio.input_volume, 60);
  h.unmount(); h.calls[1].resolve({ ok: true, endpoint_id: 'mic-a', input_volume: 59 }); await flush();
  assert.equal(h.draft.Audio.input_volume, 60);
});

test('microphone slow failure reconciliation cannot overwrite rapid new input', async () => {
  const h = harness(), ui = h.render();
  let recover;
  h.context.getAudioDevices = () => new Promise(resolve => { recover = resolve; });
  ui.onMicVolume(50); await h.advance(180);
  h.calls[0].resolve({ ok: false }); await flush();
  ui.onMicVolume(70); await h.advance(180);
  assert.equal(h.calls.length, 1);
  recover({ ok: true, input_volume: 20 }); await flush();
  assert.equal(h.draft.Audio.input_volume, 70); assert.deepEqual(h.notices, []);
  assert.equal(h.calls.length, 2);
  h.calls[1].resolve({ ok: true, endpoint_id: 'mic-a', input_volume: 69 }); await flush();
  assert.equal(h.draft.Audio.input_volume, 69); h.unmount();
});

test('microphone offline failure restores last confirmed value without claiming success', async () => {
  const h = harness(), ui = h.render();
  h.context.getAudioDevices = async () => { throw new Error('offline'); };
  ui.onMicVolume(50); await h.advance(180); h.calls[0].reject(new Error('offline')); await flush();
  assert.equal(h.draft.Audio.input_volume, 17);
  assert.deepEqual(h.notices, ['mic offline']); h.unmount();
});

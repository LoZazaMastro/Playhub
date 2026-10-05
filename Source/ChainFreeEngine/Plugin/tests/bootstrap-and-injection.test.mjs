import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import fs from 'node:fs';
const bootstrap = fs.readFileSync(new URL('../src/standalone-bootstrap.js', import.meta.url), 'utf8');
const program = fs.readFileSync(new URL('../../Host/Program.cs', import.meta.url), 'utf8');
const DECKY = '__DECKY_SECRET_INTERNALS_DO_NOT_USE_OR_YOU_WILL_BE_FIRED_deckyLoaderAPIInit';
function context(onEvent = () => {}) {
  const events = [];
  let calls = 0;
  const store = { GamepadUIMainWindowInstance: null, SteamUIWindows: [] };
  const React = { createElement() {}, useState() {}, version: '19.1.1' };
  const exports = { React, dom: { createPortal() {}, createRoot() {}, flushSync() {} },
    jsx: { jsx() {}, jsxs() {} }, router: { Navigate() {}, NavigationManager: {}, WindowStore: store } };
  const req = () => { calls++; return exports; }; req.m = { 1: true };
  const chunk = []; chunk.push = entry => entry[2](req);
  const window = { __PLAYHUB_HOST_CONFIG__: { baseUrl: 'http://127.0.0.1:1234', token: 'fixture', instanceId: 'a', injectionId: '1' }, webpackChunkfixture: chunk };
  const sandbox = { window, document: { body: {}, title: 'SharedJSContext' }, URL, setTimeout: fn => setImmediate(fn),
    fetch: async (_url, options) => { const event = JSON.parse(options.body); events.push(event); onEvent(event, store, window); return { ok: true, json: async () => ({}) }; } };
  return { sandbox, window, store, events, calls: () => calls };
}
test('bootstrap attende la finestra senza rilanciare la scansione webpack priva di req.c', async () => {
  const f = context((event, store) => {
    if (event.reason?.includes('visible')) store.GamepadUIMainWindowInstance = { document: { body: {}, title: 'Big Picture' } };
  });
  await vm.runInNewContext(bootstrap + ';bootstrapPlayhubStandalone()', f.sandbox);
  assert.equal(f.calls(), 1);
  assert.equal(f.window.__PLAYHUB_BOOTSTRAP__.state, 'bootstrapped');
  assert.equal(f.events[0].state, 'waiting');
  assert.equal(f.window.SP_REACT.version, '19.1.1');
});
test('releaseOwnedGlobals non cancella un valore sostituito da Decky', async () => {
  const f = context(); f.store.GamepadUIMainWindowInstance = { document: { body: {}, title: 'Big Picture' } };
  await vm.runInNewContext(bootstrap + ';bootstrapPlayhubStandalone()', f.sandbox);
  const foreign = {}; f.window.SP_REACT = foreign;
  f.window.__PLAYHUB_HOST__.releaseOwnedGlobals();
  assert.equal(f.window.SP_REACT, foreign);
  assert.equal(f.window.SP_REACTDOM, undefined);
});
test('il subentro di Decky conserva anche gli stessi oggetti React condivisi', async () => {
  const f = context(); f.store.GamepadUIMainWindowInstance = { document: { body: {}, title: 'Big Picture' } };
  await vm.runInNewContext(bootstrap + ';bootstrapPlayhubStandalone()', f.sandbox);
  const before = [f.window.SP_REACT, f.window.SP_REACTDOM, f.window.SP_JSX];
  f.window[DECKY] = {};
  f.window.__PLAYHUB_HOST__.releaseOwnedGlobals();
  assert.deepEqual([f.window.SP_REACT, f.window.SP_REACTDOM, f.window.SP_JSX], before);
  assert.equal(f.window.__PLAYHUB_HOST__.ownedGlobals.length, 0);
});
test('Decky preesistente blocca il bootstrap prima della pubblicazione dei global', async () => {
  const f = context(); const foreign = {}; f.window[DECKY] = foreign;
  await assert.rejects(vm.runInNewContext(bootstrap + ';bootstrapPlayhubStandalone()', f.sandbox), /Another renderer/);
  assert.equal(f.window[DECKY], foreign);
  assert.equal(f.window.SP_REACT, undefined);
  assert.equal(f.calls(), 0);
});
test('un bootstrap in attesa e’ cancellabile senza caricare il plugin', async () => {
  const f = context((_event, _store, window) => window.__PLAYHUB_BOOTSTRAP__.cancel());
  await assert.rejects(vm.runInNewContext(bootstrap + ';bootstrapPlayhubStandalone()', f.sandbox), /cancelled/);
  assert.equal(f.window.SP_REACT, undefined);
});
function injection(window) {
  const body = program.match(/return \$\$"""\n([\s\S]*?)\n    """;/)[1]
    .replace('{{config}}', JSON.stringify({ instanceId: 'new', injectionId: 'generation' }))
    .replace('{{bundleSource}}', 'window.executions=(window.executions||0)+1;');
  return vm.runInNewContext(body, { window });
}
test('lo script di iniezione non duplica un renderer gia’ pronto della stessa generation', async () => {
  const window = { __PLAYHUB_HOST_CONFIG__: { instanceId: 'new', injectionId: 'generation' }, __PLAYHUB_STANDALONE__: { state: 'ready' } };
  assert.equal(await injection(window), 'already-active');
  assert.equal(window.executions, undefined);
});
test('lo script smonta un renderer orfano prima della nuova iniezione', async () => {
  let disposed = 0, released = 0;
  const window = { __PLAYHUB_HOST_CONFIG__: { instanceId: 'old' },
    __PLAYHUB_STANDALONE__: { state: 'ready', dispose: async () => { disposed++; } },
    __PLAYHUB_HOST__: { releaseOwnedGlobals: () => { released++; } } };
  assert.equal(await injection(window), 'evaluated');
  assert.equal(disposed, 1); assert.equal(released, 1); assert.equal(window.executions, 1);
});
test('lo script con Decky attivo non esegue il bundle ne’ cambia i suoi global', async () => {
  const foreign = {}; const window = { [DECKY]: foreign, SP_REACT: foreign };
  assert.equal(await injection(window), 'blocked');
  assert.equal(window.executions, undefined); assert.equal(window.SP_REACT, foreign);
});
test('una pulizia fallita non autorizza una seconda mount sopra la prima', async () => {
  const window = { __PLAYHUB_HOST_CONFIG__: { instanceId: 'old' },
    __PLAYHUB_STANDALONE__: { state: 'failed', dispose: async () => { throw new Error('fixture disposal'); } } };
  await assert.rejects(injection(window), /fixture disposal/);
  assert.equal(window.executions, undefined);
});

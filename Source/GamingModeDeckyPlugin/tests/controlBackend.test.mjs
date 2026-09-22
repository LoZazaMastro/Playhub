import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';
import ts from 'typescript';

function client(backendCall, reload) {
  const exports = {};
  vm.runInNewContext(ts.transpileModule(fs.readFileSync(new URL('../src/controlBackend.ts', import.meta.url), 'utf8'), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText, { exports, require: () => ({call:backendCall}), window: { setTimeout: fn => { fn(); return 1; }, DeckyBackend: {call:reload} } });
  return exports;
}
test('passive upgrade reloads only Playhub once before concurrent RPC calls', async () => {
  let passive = true;
  const reloaded = [];
  const api = client(async name => {
    if (passive) throw new Error('This plugin is passive (aka does not implement main.py)');
    return name === 'get_panel_preferences' ? {} : name;
  }, async (...args) => { reloaded.push(args); passive = false; });
  assert.deepEqual(await Promise.all([api.call('get_audio_devices'), api.call('get_capabilities')]), ['get_audio_devices', 'get_capabilities']);
  assert.deepEqual(reloaded, [['loader/reload_plugin', 'Playhub']]);
});
test('ordinary backend errors do not restart plugins or Decky', async () => {
  let reloads = 0;
  const api = client(async () => { throw new Error('Permission denied'); }, async () => { reloads++; });
  await assert.rejects(api.call('get_capabilities'), /Permission denied/);
  assert.equal(reloads, 0);
});

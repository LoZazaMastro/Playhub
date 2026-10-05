import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import ts from 'typescript';
test('absent optional agent backs off all background callers and status recovery unlocks them', () => {
  const exports = {};
  vm.runInNewContext(ts.transpileModule(fs.readFileSync(new URL('../src/agentAvailability.ts', import.meta.url), 'utf8'), {
    compilerOptions: {module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022}
  }).outputText, {exports});
  let now = 100;
  const state = exports.createAgentAvailability(() => now);
  assert.equal(state.canRequest(), true);
  state.unavailable();
  for (let i=0;i<60;i++) { assert.equal(state.canRequest(), false); now += 499; }
  now = 30100; assert.equal(state.canRequest(), true);
  state.unavailable(); assert.equal(state.canRequest(), false);
  state.available(); assert.equal(state.canRequest(), true);
});

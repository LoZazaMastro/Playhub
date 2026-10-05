import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { collectSteamUiNames } from '../tools/steam-ui-contract.mjs';

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const pluginSource = process.env.PLAYHUB_PLUGIN_SOURCE || 'F:/Playhub/Plugin/Playhub/Source/GamingModeDeckyPlugin';
const read = (relative) => readFileSync(path.join(root, relative), 'utf8');
const listed = (source) => [...source.matchAll(/'([A-Za-z_][A-Za-z0-9_]*)',/g)].map((match) => match[1]);

test('the generated contract still matches what the plugin actually uses', () => {
  const sources = [path.join(pluginSource, 'src'), path.join(root, 'src')].filter((dir) => existsSync(dir));
  assert.ok(sources.length >= 1, 'no source tree to scan');
  const expected = collectSteamUiNames(sources);
  assert.deepEqual(listed(read('src/steamUiContract.ts')), expected);
  assert.ok(expected.includes('DropdownItem') && expected.includes('Focusable'));
});

test('the adapter resolves the UI through @decky/ui and verifies every name', () => {
  const adapter = read('src/steamUi.ts');
  assert.match(adapter, /from '@decky\/ui'/);
  assert.match(adapter, /verifySteamUi/);
  assert.ok(existsSync(path.join(root, 'vendor/DeckyUI-LICENSE')), 'the LGPL notice must ship with the bundle');
});

test('missing components fail loudly instead of substituting another control', async () => {
  const module = await import('./helpers/steam-ui-verify.mjs');
  const complete = Object.fromEntries(listed(read('src/steamUiContract.ts')).map((name) => [name, () => {}]));
  assert.deepEqual(module.missing(complete), []);
  delete complete.DropdownItem;
  assert.deepEqual(module.missing(complete), ['DropdownItem']);
  assert.throws(() => module.verify(complete), /Steam UI components unavailable: DropdownItem/);
});

test('the bootstrap no longer identifies components by heuristic source matching', () => {
  const bootstrap = read('src/standalone-bootstrap.js');
  for (const marker of ['dropDownControlRef&&', 'const DFL = {', '"_DialogLayout"', 'focusWithinClassName'])
    assert.ok(!bootstrap.includes(marker), 'bootstrap still matches components: ' + marker);
  assert.ok(!/DFL\./.test(bootstrap), 'bootstrap must not build DFL any more');
});

test('the built bundle carries the verified resolver', () => {
  const bundle = read('dist/playhub-standalone.js');
  assert.match(bundle, /Webpack Module Init/, 'the @decky/ui resolver is missing from the bundle');
  assert.match(bundle, /Steam UI components unavailable/);
});

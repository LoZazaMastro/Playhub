import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { createLoaderApi, loadPluginBundle, prepareBundle, DECKY_LOADER_GLOBAL } from '../src/pluginBundle.mjs';

const pluginSource = process.env.PLAYHUB_PLUGIN_SOURCE || 'F:/Playhub/Plugin/Playhub/Source/GamingModeDeckyPlugin';
const realBundle = path.join(pluginSource, 'dist/index.js');
const ASSET_BASE = 'http://127.0.0.1:47993/plugin-assets/';

const synthetic = `const manifest = {"name":"Playhub"};
const api = window.${DECKY_LOADER_GLOBAL}.connect(2, manifest.name);
var img0 = 'http://127.0.0.1:1337/plugins/Playhub/assets/day-01-01-0.jpg';
const index = (fn => (...args) => fn(...args))(() => ({
  name: 'Playhub', icon: 'icon', titleView: 'title', content: 'content',
  api, image: img0, onDismount() { globalThis.__playhubTestDismounted = true; },
}));
${'/'}/# sourceMappingURL=index.js.map
export { index as default };
${' '.repeat(1100)}`;

test('the loader global and the loader asset URLs are replaced', () => {
  const prepared = prepareBundle(synthetic, { assetBase: ASSET_BASE });
  assert.equal(prepared.assets, 1);
  assert.equal(prepared.loaderReferences, 1);
  assert.ok(!prepared.code.includes(DECKY_LOADER_GLOBAL));
  assert.ok(!prepared.code.includes('127.0.0.1:1337'));
  assert.ok(prepared.code.includes(ASSET_BASE + 'Playhub/assets/day-01-01-0.jpg'));
  assert.ok(prepared.code.includes('return index;'));
  assert.ok(!/sourceMappingURL/.test(prepared.code));
});

test('the build runs with Steam values passed in, publishing no globals', () => {
  const calls = [];
  const loaderApi = createLoaderApi({ call: async (method) => { calls.push(method); return {}; }, routerHook: {}, toaster: {} });
  const globals = { SP_REACT: {}, SP_JSX: {}, SP_REACTDOM: {}, DFL: {} };
  const { plugin, prepared } = loadPluginBundle(synthetic, { assetBase: ASSET_BASE, globals, loaderApi });
  assert.equal(plugin.name, 'Playhub');
  assert.equal(plugin.api._version, 2);
  assert.equal(plugin.image, ASSET_BASE + 'Playhub/assets/day-01-01-0.jpg');
  assert.equal(prepared.assets, 1);
  assert.equal(globalThis[DECKY_LOADER_GLOBAL], undefined, 'the loader global must not be published');
  plugin.onDismount();
  assert.equal(globalThis.__playhubTestDismounted, true);
  delete globalThis.__playhubTestDismounted;
});

test('a bundle that is not the expected build is refused', () => {
  assert.throws(() => prepareBundle('short', { assetBase: ASSET_BASE }), /empty or truncated/);
  assert.throws(() => prepareBundle(synthetic, { assetBase: 'https://example.com/' }), /loopback/);
  assert.throws(() => prepareBundle(synthetic.replace('export { index as default };', ''), { assetBase: ASSET_BASE }), /no default export/);
  assert.throws(() => prepareBundle(synthetic + '\nimport x from "react";', { assetBase: ASSET_BASE }), /module imports or exports/);
});

test('missing Steam values stop the load instead of half-starting the plugin', () => {
  const loaderApi = createLoaderApi({ call: async () => ({}) });
  assert.throws(
    () => loadPluginBundle(synthetic, { assetBase: ASSET_BASE, globals: { SP_REACT: {}, DFL: {} }, loaderApi }),
    /Steam globals unavailable: SP_JSX, SP_REACTDOM/,
  );
});

test('the real Playhub build is accepted, rewritten and compiles', { skip: !existsSync(realBundle) && 'the plugin build is not available here' }, () => {
  const prepared = prepareBundle(readFileSync(realBundle, 'utf8'), { assetBase: ASSET_BASE });
  assert.ok(prepared.assets > 0, 'expected bundled covers/UI assets to be rewritten');
  assert.ok(prepared.code.includes('assets/on-this-day/v1/'), 'streamed editorial URLs must remain remote');
  assert.equal(prepared.loaderReferences, 1);
  assert.ok(!prepared.code.includes('127.0.0.1:1337'));
  const factory = new Function('SP_REACT', 'SP_JSX', 'SP_REACTDOM', 'DFL', '__playhubStandaloneLoaderApi', '"use strict";\n' + prepared.code);
  assert.equal(typeof factory, 'function');
});

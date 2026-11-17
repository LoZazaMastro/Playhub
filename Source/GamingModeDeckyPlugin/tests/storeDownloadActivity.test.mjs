import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import vm from 'node:vm';
import { createRequire } from 'node:module';
const require = createRequire(new URL('../package.json', import.meta.url));
const ts = require('typescript');
const source = readFileSync(new URL('../src/storeDownloadActivity.ts', import.meta.url), 'utf8');
const exports = {};
vm.runInNewContext(ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText, { exports, TextEncoder });
const local = { remote_client_id: '0', paused: false, update_appid: 620, update_state: 'Updating', update_network_bytes_per_second: 0, update_disc_bytes_per_second: 0 };
test('Steam transfer priority includes disk and stalled active phases, never remote or unknown', () => {
  for (const update_state of ['Starting','Downloading','Patching','Verifying','Installing','Updating']) assert.equal(exports.localSteamDownloadActivity({ ...local, update_state }), true);
  assert.equal(exports.localSteamDownloadActivity({ ...local, remote_client_id: '1' }), null);
  assert.equal(exports.localSteamDownloadActivity({ ...local, paused: true }), false);
  assert.equal(exports.localSteamDownloadActivity({ ...local, update_appid: 0, update_state: 'None' }), false);
  assert.equal(exports.localSteamDownloadActivity({ ...local, update_state: 'unknown' }), null);
  assert.equal(exports.localSteamDownloadActivity({ ...local, update_appid: -1 }), null);
  assert.equal(exports.localSteamDownloadActivity({ ...local, update_is_shader: true }), true);
  assert.equal(exports.localSteamDownloadActivity({ ...local, update_is_workshop: true }), true);
});
test('shortcut ACK requires exact application, wrapper, arguments and working directory', () => {
  const plan = { executable: 'C:\\Apps\\Playhub.GameSession.exe', arguments: '--store-library epic test', startDirectory: 'C:\\Apps' };
  const proof = { unAppID: 3000000000, strShortcutExe: '"c:\\apps\\Playhub.GameSession.exe"', strShortcutLaunchOptions: plan.arguments, strShortcutStartDir: '"C:\\Apps"' };
  assert.equal(exports.matchingShortcut(proof, plan, proof.unAppID), true);
  for (const altered of [{ unAppID: 9 }, { strShortcutExe: 'foreign.exe' }, { strShortcutLaunchOptions: '--game anything' }, { strShortcutStartDir: 'C:\\Other' }]) assert.equal(exports.matchingShortcut({ ...proof, ...altered }, plan, proof.unAppID), false);
  assert.equal(exports.matchingShortcut(null, plan, proof.unAppID), false);
});
test('shortcut identity remains stable across installation and uses UTF8 CRC', () => {
  // Independently calculated with Python zlib.crc32 over the UTF8 shortcut identity.
  assert.equal(exports.shortcutAppId('"C:\\Apps\\Playhub.GameSession.exe"', 'Fixture Game'), 2526399220);
  assert.notEqual(exports.shortcutAppId('"C:\\Apps\\Playhub.GameSession.exe"', 'Fixture Game (Epic)'), exports.shortcutAppId('"C:\\Apps\\Playhub.GameSession.exe"', 'Fixture Game (Gog)'));
});
test('confirmation may refresh progress but never targets another account, path or job', () => {
  const original={jobId:'job-1', key:{store:'epic',id:'game'}, accountRevision:3, installPath:'D:\\Games\\One'};
  assert.equal(exports.sameStoreDownload(original,{...original,revision:999,progressPercent:60}),true);
  for(const change of [{jobId:'replacement'}, {key:{store:'gog',id:'game'}}, {key:{store:'epic',id:'different'}}, {accountRevision:4}, {installPath:'E:\\Other'}])
    assert.equal(exports.sameStoreDownload(original,{...original,...change}),false);
});

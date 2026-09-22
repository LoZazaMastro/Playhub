import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';

const source = readFileSync(new URL('../../Playhub/Services/GamingModeService.cs', import.meta.url), 'utf8');
const migration = source.slice(source.indexOf('private static void MigrateStandaloneQuickSettings'), source.indexOf('public async Task<bool> SetDefaultModeViaAgentAsync'));

test('standalone migration serializes loader shutdown, bounded archive retries and one guarded restart', () => {
  assert.match(migration, /DeckyStartupGuard\.RunExclusive/);
  assert.match(migration, /process\.SessionId != sessionId/);
  assert.match(migration, /Path\.GetDirectoryName\(Path\.GetFullPath\(executable\)\), services/);
  assert.match(migration, /WaitForExit\(10000\)/);
  assert.match(migration, /attempt < 39/);
  assert.match(migration, /Thread\.Sleep\(250\)/);
  assert.match(migration, /finally[\s\S]*!AnyDeckyLoaderRunning\(services, sessionId\)/);
  assert.equal((migration.match(/Process\.Start\(/g) ?? []).length, 1);
});

test('migration retains the old plugin outside Decky scanning and never overwrites user profiles', () => {
  assert.match(migration, /playhub-plugin-backups/);
  assert.match(migration, /File\.Exists\(oldProfiles\) && !File\.Exists\(newProfiles\)/);
  assert.match(migration, /File\.Copy\(oldProfiles, newProfiles, overwrite: false\)/);
  assert.match(migration, /CopyDirectory\(previous, target\)/);
  assert.match(migration, /File\.Move\(manifest, Path\.Combine\(previous, "plugin.json.retired"\)/);
  assert.ok(migration.indexOf('CopyDirectory(previous, target)') < migration.indexOf('File.Move(manifest'));
  assert.match(migration, /Directory\.Move\(previous, target \+ "-original"\)/);
  assert.match(migration, /Process\.GetProcessesByName\("QuickSettingsAgent"\)/);
  assert.match(migration, /StartsWith\(previous \+ Path\.DirectorySeparatorChar/);
  assert.doesNotMatch(migration, /Directory\.Delete|File\.Delete/);
  const catalog = JSON.parse(readFileSync(new URL('../../../catalog/plugins.json', import.meta.url), 'utf8'));
  assert.equal(catalog.plugins.find(plugin => plugin.repository === 'LoZazaMastro/Quick-Settings').active, false);
});

// Every RPC name the frontend sends must exist on the Python plugin, and the
// display transaction must stay wired end to end. A method that only exists on
// one side of the bridge is invisible until a control silently does nothing --
// which is exactly how `get_display_change` was once left unconsumed.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('..', import.meta.url));
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8');

function sources(directory, found = []) {
  for (const entry of fs.readdirSync(path.join(root, directory), { withFileTypes: true })) {
    const relative = `${directory}/${entry.name}`;
    if (entry.isDirectory()) sources(relative, found);
    else if (/\.tsx?$/.test(entry.name)) found.push(relative);
  }
  return found;
}

const backendMethods = () => {
  const names = new Set();
  for (const file of ['main.py', 'quick_settings/main.py']) {
    for (const match of read(file).matchAll(/^\s*async def ([a-z_][a-z0-9_]*)\s*\(/gm)) {
      names.add(match[1]);
    }
  }
  return names;
};

// `call<[Args], Result>("name")`, `call("name")` and `deckyCall("name")`.
const callSites = () => {
  const sites = [];
  for (const file of sources('src')) {
    const text = read(file);
    for (const match of text.matchAll(/\b(?:decky)?[Cc]all\s*(?:<[\s\S]*?>)?\(\s*"([a-z_][a-z0-9_]*)"/g)) {
      sites.push({ file, method: match[1] });
    }
  }
  return sites;
};

test('every RPC the frontend calls exists on the Python plugin', () => {
  const methods = backendMethods();
  const sites = callSites();
  assert.ok(sites.length > 20, `expected the panel call sites to be found, got ${sites.length}`);
  const missing = sites.filter(site => !methods.has(site.method) && site.method !== 'loader/reload_plugin');
  assert.deepEqual(missing, [], `unknown backend methods: ${missing.map(s => `${s.method} (${s.file})`).join(', ')}`);
});

test('the display transaction RPCs are declared, exported and consumed', () => {
  const methods = backendMethods();
  const client = read('src/quickSettings/backend.ts');
  const panel = read('src/quickSettings/index.tsx');
  for (const [method, exported] of [
    ['begin_display_change', 'beginDisplayChange'],
    ['finish_display_change', 'finishDisplayChange'],
    ['get_display_change', 'getDisplayChange'],
  ]) {
    assert.ok(methods.has(method), `${method} is missing from the backend`);
    assert.match(client, new RegExp(`export const ${exported}[\\s\\S]{0,400}?"${method}"`),
      `${exported} does not call ${method}`);
    assert.match(panel, new RegExp(`\\b${exported}\\b`), `${exported} is never used by the panel`);
  }
  // Risky writes must not have a bypass around the confirmation coordinator.
  for (const legacy of ['set_display_mode', 'set_refresh_rate', 'set_hdr_enabled']) {
    assert.match(read('main.py'),
      new RegExp(`async def ${legacy}[\\s\\S]{0,200}?display_transaction_required`),
      `${legacy} must refuse to bypass the confirmation transaction`);
  }
});

test('the confirmation dialog offers keep and revert with a countdown', () => {
  const panel = read('src/quickSettings/index.tsx');
  assert.match(panel, /function HdrConfirmModal\b/);
  assert.match(panel, /onKeep=\{\(\) => finish\(true\)\}/);
  assert.match(panel, /onRevert=\{\(\) => finish\(false\)\}/);
  // The backend owns the rollback; the dialog only shows the countdown. The window
  // is now per change kind (HDR needs longer to resync a panel), so assert the arming
  // and the values rather than one hard-coded literal.
  const backend = read('main.py');
  assert.match(backend, /preview_seconds = 30 if kind == "hdr" else 15/);
  assert.match(backend, /self\._arm_display_timer\(preview_seconds, token\)/);
});

test('a failed audio change reports its own reason, not "no devices"', () => {
  const panel = read('src/quickSettings/index.tsx');
  assert.match(panel, /notify\(\(error as \{ detail\?: string \}\)\?\.detail \|\| local\.audioChangeFailed\)/);
  // The empty-list label may only be reached when the enumeration succeeded.
  assert.match(panel, /audio\.ok === false && \(audio\.code \|\| audio\.message\)/);
  const strings = read('src/quickSettings/translations.ts');
  for (const key of ['audioChangeFailed', 'displayChangeFailed', 'powerModeFailed']) {
    assert.equal((strings.match(new RegExp(`\\b${key}:`, 'g')) ?? []).length, 3,
      `${key} must be declared and translated into every locale in translations.ts`);
  }
});

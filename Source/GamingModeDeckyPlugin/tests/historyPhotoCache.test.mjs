import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import {webcrypto, createHash} from 'node:crypto';
import ts from 'typescript';
const code = ts.transpileModule(fs.readFileSync(new URL('../src/historyPhotoCache.ts', import.meta.url), 'utf8'), {
  compilerOptions: {module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022}
}).outputText;
const bytes = Buffer.from('verified photo fixture');
const expected = {sha256: createHash('sha256').update(bytes).digest('hex'), bytes: bytes.length};
const response = () => new Response(bytes, {headers: {'content-type': 'image/jpeg'}});
const flush = () => new Promise(resolve => setImmediate(resolve));
function fixture(fetch, saved = new Map(), cacheAvailable = true) {
  const cache = {match: async key => saved.get(String(key))?.clone(), delete: async key => saved.delete(String(key)),
    put: async (key, value) => saved.set(String(key), value.clone()), keys: async () => [...saved.keys()]};
  const exports = {};
  vm.runInNewContext(code, {exports, Blob, Response, Uint8Array, AbortController, crypto: webcrypto, fetch,
    caches: {open: async () => {if (!cacheAvailable) throw Error('Storage blocked'); return cache;}},
    setTimeout: (fn, ms) => setTimeout(fn, ms === 500 ? 0 : ms), clearTimeout});
  return {load: exports.loadHistoryPhoto, saved};
}
test('deduplicates simultaneous loads; verifies hash; persistent cache survives a fresh runtime offline', async () => {
  let requests = 0; const saved = new Map();
  const online = fixture(async () => {requests++; return response();}, saved);
  const [a,b] = await Promise.all([online.load('https://example.test/a', expected), online.load('https://example.test/a', expected)]);
  assert.equal(requests, 1); assert.equal(a, b); assert.equal(a.size, bytes.length);
  for(let i=0;i<5;i++) await flush();
  const offline = fixture(async () => {throw Error('No network allowed');}, saved);
  assert.equal((await offline.load('https://example.test/a', expected)).size, bytes.length);
});
test('corrupt cached bytes are discarded and replaced with verified network bytes', async () => {
  const saved = new Map([['https://example.test/a', new Response(Buffer.alloc(bytes.length))]]);
  let requests = 0;
  const f = fixture(async () => {requests++; return response();}, saved);
  assert.equal((await f.load('https://example.test/a', expected)).size, bytes.length);
  assert.equal(requests, 1);
});
test('network failure is bounded to two attempts then cooldown; nothing is cached', async () => {
  let requests = 0; const f = fixture(async () => {requests++; throw Error('Offline');});
  assert.equal(await f.load('https://example.test/a', expected), null);
  assert.equal(await f.load('https://example.test/a', expected), null);
  assert.equal(requests, 2); assert.equal(f.saved.size, 0);
});
test('hash mismatch and oversized payloads never render or persist', async () => {
  for(const data of [Buffer.alloc(bytes.length), Buffer.alloc(bytes.length+1)]) {
    const f = fixture(async () => new Response(data));
    assert.equal(await f.load('https://example.test/a', expected), null); assert.equal(f.saved.size, 0);
  }
});
test('cache unavailable does not stop a verified photo; 404 is not retried', async () => {
  const f = fixture(async () => response(), new Map(), false);
  assert.equal((await f.load('https://example.test/a', expected)).size, bytes.length);
  let requests=0; const missing=fixture(async()=>{requests++;return new Response('',{status:404});});
  assert.equal(await missing.load('https://example.test/a',expected),null); assert.equal(requests,1);
});
test('at most three downloads are in flight across many different images', async () => {
  let active=0, peak=0; const releases=[];
  const f=fixture(async()=>{active++;peak=Math.max(peak,active);await new Promise(resolve=>releases.push(resolve));active--;return response();});
  const jobs=Array.from({length:8},(_,i)=>f.load(`https://example.test/${i}`,expected));
  for(let turn=0;turn<30;turn++){await flush();releases.splice(0).forEach(resolve=>resolve());}
  assert.ok((await Promise.all(jobs)).every(Boolean)); assert.equal(peak,3);
});

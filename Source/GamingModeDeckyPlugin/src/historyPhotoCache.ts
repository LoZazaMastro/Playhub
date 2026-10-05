export type PhotoIntegrity = { sha256: string; bytes: number };
const CACHE = 'playhub-history-photos-v1';
const MAX_BYTES = 256 * 1024 * 1024;
const memory = new Map<string, Blob>();
const pending = new Map<string, Promise<Blob | null>>();
const failedUntil = new Map<string, number>();
let active = 0;
const queue: Array<() => void> = [];
let cacheWrites: Promise<void> = Promise.resolve();

async function slot<T>(work: () => Promise<T>): Promise<T> {
  if (active >= 3) await new Promise<void>(resolve => queue.push(resolve));
  else active++;
  try { return await work(); }
  finally { const next = queue.shift(); if (next) next(); else active--; }
}
async function verify(response: Response, expected: PhotoIntegrity): Promise<Blob> {
  if (!response.ok) throw new Error('Photo unavailable');
  const length = response.headers.get('content-length');
  if (length && Number(length) !== expected.bytes) throw new Error('Photo size mismatch');
  // Bound the stream before allocating it. A server error must not exhaust Steam's renderer.
  const reader = response.body?.getReader();
  if (!reader) throw new Error('Photo stream unavailable');
  const chunks: Uint8Array[] = []; let size = 0;
  try {
    while (true) {
      const {done, value} = await reader.read();
      if (done) break;
      size += value.byteLength;
      if (size > expected.bytes) throw new Error('Photo exceeds expected size');
      chunks.push(value);
    }
  } catch (error) { await reader.cancel().catch(() => {}); throw error; }
  finally { reader.releaseLock(); }
  if (size !== expected.bytes) throw new Error('Incomplete photo');
  const bytes = new Uint8Array(size); let offset = 0;
  for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.length; }
  const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', bytes));
  if (Array.from(digest, value => value.toString(16).padStart(2, '0')).join('') !== expected.sha256) throw new Error('Photo integrity mismatch');
  return new Blob([bytes], {type: response.headers.get('content-type') || 'image/jpeg'});
}
async function persistent(): Promise<Cache | null> {
  try { return await caches.open(CACHE); } catch { return null; }
}
function store(cache: Cache, url: string, blob: Blob) {
  cacheWrites = cacheWrites.then(async () => {
    await cache.delete(url);
    await cache.put(url, new Response(blob, {headers: {'content-type': blob.type, 'content-length': String(blob.size)}}));
    const keys = await cache.keys(); let bytes = 0; let count = 0;
    for (const key of [...keys].reverse()) {
      const saved = await cache.match(key);
      bytes += Number(saved?.headers.get('content-length') || MAX_BYTES);
      if (++count > 256 || bytes > MAX_BYTES) await cache.delete(key);
    }
  }).catch(() => { /* Storage may be unavailable/full; the verified photo still displays. */ });
}

/** Immutable URLs and embedded hashes keep remote/cache contents out of the trust boundary. */
export function loadHistoryPhoto(url: string, expected: PhotoIntegrity): Promise<Blob | null> {
  if (memory.has(url)) {
    const value = memory.get(url)!; memory.delete(url); memory.set(url, value);
    return Promise.resolve(value);
  }
  if ((failedUntil.get(url) || 0) > Date.now()) return Promise.resolve(null);
  const existing = pending.get(url); if (existing) return existing;
  const job = slot(async () => {
    let blob: Blob | null = null;
    const cache = await persistent();
    try {
      const cached = await cache?.match(url);
      if (cached) blob = await verify(cached, expected);
    } catch { await cache?.delete(url).catch(() => {}); }
    for (let attempt = 0; !blob && attempt < 2; attempt++) {
      const controller = new AbortController();
      const timeout = setTimeout(() => controller.abort(), 10000);
      try {
        const response = await fetch(url, {signal: controller.signal, credentials: 'omit', cache: 'no-cache'});
        if (response.status >= 400 && response.status < 500 && response.status !== 408 && response.status !== 429) break;
        blob = await verify(response, expected);
      } catch { /* No background notification. The article remains readable. */ }
      finally { clearTimeout(timeout); }
      if (!blob && attempt === 0) await new Promise(resolve => setTimeout(resolve, 500));
    }
    if (blob) {
      memory.set(url, blob);
      let memoryBytes = Array.from(memory.values()).reduce((sum, item) => sum + item.size, 0);
      while (memory.size > 24 || memoryBytes > 32 * 1024 * 1024) {
        const oldest = memory.keys().next().value!;
        memoryBytes -= memory.get(oldest)!.size;
        memory.delete(oldest);
      }
      if (cache) store(cache, url, blob);
    } else {
      failedUntil.set(url, Date.now() + 300000);
      if (failedUntil.size > 256) failedUntil.delete(failedUntil.keys().next().value!);
    }
    return blob;
  }).catch(() => null).finally(() => pending.delete(url));
  pending.set(url, job); return job;
}

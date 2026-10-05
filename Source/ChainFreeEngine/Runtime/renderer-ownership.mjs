// Cooperative ownership for Playhub within one renderer/globalThis. This module
// neither starts a host nor imports Steam, Decky or any application entry point.
const REGISTRY = Symbol.for('playhub.renderer.ownership.v1');
const PROTOCOL = 'playhub-renderer-ownership/1';
const OWNERS = new Set(['decky', 'standalone']);

export class RendererOwnershipError extends Error {
  constructor(code, message, ownership = null) {
    super(message);
    this.name = 'RendererOwnershipError';
    this.code = code;
    this.ownership = ownership;
  }
}

function snapshot(record) {
  return record ? Object.freeze({
    owner: record.owner,
    generation: record.generation,
    state: record.state,
    failure: record.failure,
  }) : null;
}

function registryFor(renderer, create) {
  if ((typeof renderer !== 'object' && typeof renderer !== 'function') || renderer === null) {
    throw new TypeError('renderer must be an object representing one renderer global.');
  }
  let registry = Object.hasOwn(renderer, REGISTRY) ? renderer[REGISTRY] : undefined;
  if (registry === undefined && create) {
    registry = Object.seal({ protocol: PROTOCOL, generation: 0, active: null });
    Object.defineProperty(renderer, REGISTRY, { value: registry });
  }
  if (registry !== undefined && (registry?.protocol !== PROTOCOL || !Number.isSafeInteger(registry.generation))) {
    throw new RendererOwnershipError('INCOMPATIBLE_REGISTRY', 'The Playhub renderer registry has an incompatible protocol.');
  }
  return registry;
}

function abortError(reason) {
  const error = new Error('Playhub renderer mounting was cancelled.', { cause: reason });
  error.name = 'AbortError';
  return error;
}

/** Returns the active Playhub owner, or null. Does not create or modify a slot. */
export function getPlayhubRendererOwnership({ renderer = globalThis } = {}) {
  return snapshot(registryFor(renderer, false)?.active);
}

/**
 * Atomically claims one renderer for Playhub. Duplicate attempts throw before
 * their mount callback runs. The returned lease is available during mounting.
 *
 * mount({signal, owner, generation, defer}) may return an async cleanup function.
 * Register partial cleanup with defer before initialization that can fail.
 * All initialization must be awaited by mount; detached work is not tracked.
 */
export function mountPlayhubRenderer({ owner, mount, signal, renderer = globalThis } = {}) {
  if (!OWNERS.has(owner)) throw new TypeError('owner must be decky or standalone.');
  if (typeof mount !== 'function') throw new TypeError('mount must be a function.');
  if (signal !== undefined && (typeof signal?.addEventListener !== 'function' || typeof signal?.removeEventListener !== 'function' || typeof signal?.aborted !== 'boolean')) {
    throw new TypeError('signal must be an AbortSignal.');
  }
  if (signal?.aborted) throw abortError(signal.reason);

  const registry = registryFor(renderer, true);
  if (registry.active) {
    throw new RendererOwnershipError('ALREADY_OWNED', 'Playhub already owns this renderer.', snapshot(registry.active));
  }
  if (registry.generation === Number.MAX_SAFE_INTEGER) {
    throw new RendererOwnershipError('GENERATION_EXHAUSTED', 'The Playhub renderer generation limit was reached.');
  }

  const controller = new AbortController();
  const record = { owner, generation: ++registry.generation, state: 'mounting', failure: null };
  registry.active = record;
  const cleanups = [];
  const registered = new Set();
  let mounting = true;
  let disposalRequested = false;
  let disposalPromise;
  let mountOutcome;
  let lease;

  function defer(cleanup) {
    if (typeof cleanup !== 'function') throw new TypeError('cleanup must be a function.');
    if (!mounting || registry.active !== record) {
      throw new RendererOwnershipError('LATE_CLEANUP_REGISTRATION', 'Register cleanup while mount is still pending.');
    }
    if (!registered.has(cleanup)) {
      registered.add(cleanup);
      cleanups.push(cleanup);
    }
  }

  function finalize() {
    if (disposalPromise) return disposalPromise;
    record.state = 'disposing';
    disposalPromise = (async () => {
      // A cancelled mount may still resolve with cleanup. Wait for that outcome
      // before taking the stack, retaining ownership for the entire interval.
      await mountOutcome;
      const failures = [];
      for (const cleanup of cleanups.reverse()) {
        try { await cleanup(); }
        catch (error) { failures.push(error); }
      }
      cleanups.length = 0;
      signal?.removeEventListener('abort', onAbort);
      if (failures.length) {
        record.state = 'failed';
        record.failure = 'cleanup-failed';
        // No force-unlock: a failed cleanup cannot establish safe handover.
        throw new AggregateError(failures, 'Playhub renderer cleanup failed; ownership remains blocked.');
      }
      record.state = 'disposed';
      if (registry.active === record) registry.active = null;
    })();
    // Callers still receive the rejection, but an AbortSignal listener need not
    // manufacture an unhandled rejection before the caller awaits the lease.
    disposalPromise.catch(() => {});
    return disposalPromise;
  }

  function dispose(reason) {
    if (!disposalRequested) {
      disposalRequested = true;
      controller.abort(reason);
    }
    return finalize();
  }

  function onAbort() { void dispose(signal.reason); }

  // Defer invocation until after the lease and abort listener are established.
  mountOutcome = Promise.resolve().then(async () => {
    if (disposalRequested) {
      mounting = false;
      return { ok: false, error: abortError(controller.signal.reason) };
    }
    try {
      const cleanup = await mount(Object.freeze({
        owner, generation: record.generation, signal: controller.signal, defer,
      }));
      if (cleanup !== undefined && cleanup !== null) defer(cleanup);
      return { ok: true };
    }
    catch (error) { return { ok: false, error }; }
    finally { mounting = false; }
  });

  const ready = mountOutcome.then(async ({ ok, error }) => {
    if (!ok || disposalRequested) {
      let cleanupError;
      try { await dispose(error ?? controller.signal.reason); }
      catch (failure) { cleanupError = failure; }
      if (cleanupError && !ok) {
        throw new AggregateError([error, cleanupError], 'Playhub mount and cleanup failed; ownership remains blocked.');
      }
      if (cleanupError) throw cleanupError;
      if (!ok) throw error;
      throw abortError(controller.signal.reason);
    }
    record.state = 'mounted';
    return lease;
  });
  ready.catch(() => {});
  lease = Object.freeze({
    owner, generation: record.generation, signal: controller.signal,
    ready, dispose, snapshot: () => snapshot(record),
  });
  signal?.addEventListener('abort', onAbort, { once: true });
  return lease;
}

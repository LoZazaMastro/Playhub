export interface WriteScheduler {
  schedule(callback: () => void, delayMs: number): () => void;
}

export interface SettledWriterOptions<T, R> {
  delayMs: number;
  write: (value: T) => Promise<R>;
  onSettled: (result: R, value: T) => void;
  onError: (error: unknown, value: T) => void;
  scheduler?: WriteScheduler;
}

/** One writer per independent setting/resource; callbacks receive only current results. */
export function createSettledWriter<T, R>(options: SettledWriterOptions<T, R>) {
  if (!Number.isFinite(options.delayMs) || options.delayMs < 0) {
    throw new RangeError("delayMs must be finite and non-negative");
  }
  const scheduler = options.scheduler ?? {
    schedule(callback: () => void, delayMs: number) {
      const id = globalThis.setTimeout(callback, delayMs);
      return () => globalThis.clearTimeout(id);
    },
  };
  let disposed = false;
  let running = false;
  let revision = 0;
  let pending: { value: T; revision: number; ready: boolean } | undefined;
  let cancelTimer: (() => void) | undefined;

  async function drain(): Promise<void> {
    if (disposed || running || !pending?.ready) return;
    const request = pending;
    pending = undefined;
    running = true;
    let outcome: { ok: true; result: R } | { ok: false; error: unknown };
    try {
      outcome = { ok: true, result: await options.write(request.value) };
    } catch (error) {
      outcome = { ok: false, error };
    }
    running = false;
    try {
      if (!disposed && revision === request.revision) {
        if (outcome.ok) options.onSettled(outcome.result, request.value);
        else options.onError(outcome.error, request.value);
      }
    } finally {
      void drain();
    }
  }

  return {
    enqueue(value: T): boolean {
      if (disposed) return false;
      cancelTimer?.();
      const request = { value, revision: ++revision, ready: false };
      pending = request;
      cancelTimer = scheduler.schedule(() => {
        // A cancelled callback may already be queued by the host event loop.
        if (disposed || pending !== request) return;
        cancelTimer = undefined;
        request.ready = true;
        void drain();
      }, options.delayMs);
      return true;
    },
    cancelPending(): void {
      ++revision;
      cancelTimer?.();
      cancelTimer = undefined;
      pending = undefined;
    },
    dispose(): void {
      disposed = true;
      ++revision;
      cancelTimer?.();
      cancelTimer = undefined;
      pending = undefined;
    },
  };
}

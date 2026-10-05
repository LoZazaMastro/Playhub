export interface IpcFailure { folder: string; name: string; session: string; log: string }
interface RecoveryOptions {
  scan(): Promise<IpcFailure[]>;
  claim(failure: IpcFailure): Promise<{ ok: boolean; name?: string }>;
  reload(name: string): Promise<unknown>;
  schedule(callback: () => void, delay: number): unknown;
  cancel(timer: unknown): void;
  warn(error: unknown): void;
}

/** Three startup checks only. Evidence and the durable session claim live in Python. */
export function installDeckyIpcRecovery(options: RecoveryOptions): () => void {
  let alive = true;
  const attempted = new Set<string>();
  async function check() {
    try {
      const failures = await options.scan();
      for (const failure of failures) {
        if (!alive) return;
        const key = `${failure.session}:${failure.folder}`;
        if (attempted.has(key)) continue;
        attempted.add(key);
        const claim = await options.claim(failure);
        if (alive && claim.ok && claim.name === failure.name) await options.reload(claim.name);
      }
    } catch (error) { if (alive) options.warn(error); }
  }
  const timers = [3000, 10000, 25000].map(delay => options.schedule(() => { if (alive) void check(); }, delay));
  return () => { alive = false; timers.forEach(options.cancel); };
}

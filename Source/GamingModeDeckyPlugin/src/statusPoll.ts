/** Background availability checks never notify: startup/offline is not an action failure. */
export function pollStatus<T>(read: (signal: AbortSignal) => Promise<T>, update: (value: T | undefined) => void) {
  let stopped = false;
  let failures = 0;
  let timer: ReturnType<typeof setTimeout>;
  let controller: AbortController;
  const tick = async () => {
    controller = new AbortController();
    const deadline = setTimeout(() => controller.abort(), 4000);
    try {
      const value = await read(controller.signal);
      failures = 0;
      if (!stopped) update(value);
    } catch {
      failures++;
      if (!stopped) update(undefined);
    } finally {
      clearTimeout(deadline);
      if (!stopped) timer = setTimeout(tick, Math.min(30000, 5000 * 2 ** Math.min(failures, 3)));
    }
  };
  void tick();
  return () => { stopped = true; clearTimeout(timer); controller?.abort(); };
}

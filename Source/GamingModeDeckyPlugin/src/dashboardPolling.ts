type Timer = ReturnType<typeof setTimeout>;

type PollOptions<T> = {
  read: (signal: AbortSignal) => Promise<T | null>;
  publish: (value: T) => void;
  interval: number;
  visible: () => boolean;
  now?: () => number;
  schedule?: (run: () => void, delay: number) => Timer;
  cancel?: (timer: Timer) => void;
};

// Each feed owns its clock and has at most one request in flight. An explicit
// refresh invalidates a sample taken before the process action completed.
export function createDashboardPoll<T>({
  read, publish, interval, visible, now = Date.now, schedule = setTimeout, cancel = clearTimeout,
}: PollOptions<T>) {
  let active = false;
  let disposed = false;
  let generation = 0;
  let timer: Timer | undefined;
  let inFlight: AbortController | undefined;
  let refreshAt: number | undefined;

  const canPoll = () => active && !disposed && visible();
  const clear = () => {
    if (timer !== undefined) cancel(timer);
    timer = undefined;
  };
  const arm = (delay: number) => {
    clear();
    timer = schedule(() => { void poll(); }, delay);
  };
  async function poll() {
    timer = undefined;
    if (!canPoll() || inFlight) return;
    refreshAt = undefined;
    const controller = new AbortController();
    inFlight = controller;
    const visit = generation;
    try {
      const sample = await read(controller.signal);
      if (sample !== null && visit === generation && canPoll()) publish(sample);
    } catch {
      // A failed sample leaves the last successful one visible.
    } finally {
      inFlight = undefined;
      if (canPoll()) arm(refreshAt === undefined ? interval : Math.max(0, refreshAt - now()));
    }
  }

  function setActive(next: boolean) {
    if (disposed || next === active) return;
    active = next;
    generation++;
    clear();
    refreshAt = undefined;
    if (!active) inFlight?.abort();
    else if (inFlight) refreshAt = now();
    else void poll();
  }

  return {
    setActive,
    refresh(delay = 0) {
      if (!canPoll()) return;
      generation++;
      refreshAt = now() + delay;
      if (!inFlight) arm(delay);
    },
    dispose() {
      setActive(false);
      disposed = true;
    },
  };
}

export function dashboardSystemVisible(surface: HTMLElement, routeActive: boolean, intersects: boolean): boolean {
  if (!routeActive || !intersects || !surface.isConnected || surface.ownerDocument.hidden) return false;
  const view = surface.ownerDocument.defaultView;
  if (!view || view.closed) return false;
  try {
    return surface.getClientRects().length > 0 && view.getComputedStyle(surface).visibility === "visible";
  } catch {
    return false;
  }
}

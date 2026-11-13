export type FrameSample = {
  presentedFps: number;
  medianFrameTimeMs: number;
  p95FrameTimeMs: number;
  samples: number;
};

type Comparison = {
  appId: number;
  busy: boolean;
  first?: FrameSample;
  second?: FrameSample;
  error?: string;
};

function validSample(raw: unknown): FrameSample | undefined {
  if (!raw || typeof raw !== "object") return;
  const sample = raw as Partial<FrameSample> & { status?: string };
  const { presentedFps, medianFrameTimeMs, p95FrameTimeMs, samples } = sample;
  if (sample.status !== "Ready" || typeof presentedFps !== "number" || !Number.isFinite(presentedFps) || presentedFps <= 0 ||
      typeof medianFrameTimeMs !== "number" || !Number.isFinite(medianFrameTimeMs) || medianFrameTimeMs <= 0 ||
      typeof p95FrameTimeMs !== "number" || !Number.isFinite(p95FrameTimeMs) || p95FrameTimeMs < medianFrameTimeMs ||
      typeof samples !== "number" || !Number.isSafeInteger(samples) || samples < 8) return;
  return { presentedFps, medianFrameTimeMs, p95FrameTimeMs, samples };
}

export function createGameFrameComparison() {
  let state: Comparison = { appId: 0, busy: false };
  const listeners = new Set<() => void>();
  const update = (next: Comparison) => {
    state = next;
    listeners.forEach(listener => listener());
  };

  return {
    read(appId: number): Comparison {
      return state.appId === appId ? state : { appId, busy: state.busy };
    },
    subscribe(listener: () => void) {
      listeners.add(listener);
      return () => { listeners.delete(listener); };
    },
    reset(appId: number) {
      if (!state.busy && state.appId === appId) update({ appId, busy: false });
    },
    async capture(appId: number, measure: (id: number) => Promise<unknown>) {
      if (!Number.isInteger(appId) || appId < 1 || appId > 0xffffffff || state.busy) return;
      const previous: Comparison = state.appId === appId ? state : { appId, busy: false };
      update({ ...previous, busy: true, error: undefined });
      let response: unknown;
      try { response = await measure(appId); }
      catch { response = { status: "ProviderUnavailable" }; }
      const sample = validSample(response);
      if (sample) {
        update(previous.first ? { ...previous, second: sample, error: undefined } : { appId, busy: false, first: sample });
      } else {
        const status = (response as { status?: unknown } | null)?.status;
        update({ ...previous, error: typeof status === "string" && status !== "Ready" ? status : "NoData" });
      }
    },
  };
}

export const gameFrameComparison = createGameFrameComparison();

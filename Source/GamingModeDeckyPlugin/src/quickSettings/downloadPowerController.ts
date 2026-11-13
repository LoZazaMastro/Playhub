const activeStages = new Set([
  "Downloading", "Updating", "Preloading", "Preallocating", "Patching", "Unpacking", "Installing", "Verifying", "Validating", "Transferring",
]);

type SteamDownloadOverview = {
  remote_client_id?: unknown;
  paused?: unknown;
  update_appid?: unknown;
  update_state?: unknown;
  update_is_shader?: unknown;
  update_is_workshop?: unknown;
  update_is_upload?: unknown;
  update_is_prefetch_estimate?: unknown;
  update_network_bytes_per_second?: unknown;
  update_disc_bytes_per_second?: unknown;
  progress?: unknown;
};

function nonnegative(value: unknown): value is number {
  return typeof value === "number" && Number.isFinite(value) && value >= 0;
}

export function activeLocalDownload(raw: unknown): number {
  if (raw === null || typeof raw !== "object") return 0;
  const entry = raw as SteamDownloadOverview;
  const appId = entry.update_appid;
  if (typeof appId !== "number" || !Number.isInteger(appId) || appId <= 0 || appId > 0xffffffff ||
      entry.remote_client_id !== "0" || entry.paused !== false || typeof entry.update_state !== "string" || !activeStages.has(entry.update_state) ||
      entry.update_is_shader !== false || entry.update_is_workshop !== false || entry.update_is_upload !== false || entry.update_is_prefetch_estimate !== false) return 0;
  const network = entry.update_network_bytes_per_second;
  const disk = entry.update_disc_bytes_per_second;
  if (!nonnegative(network) || !nonnegative(disk) || (network === 0 && disk === 0)) return 0;
  if (!Array.isArray(entry.progress) || !entry.progress.some(part => nonnegative(part?.bytes_in_progress) &&
      nonnegative(part?.bytes_total) && part.bytes_total > part.bytes_in_progress)) return 0;
  return appId;
}

export type DownloadPowerRequest = { action: "start" | "renew" | "stop"; appId: number; lease?: string };
type Response = { ok?: boolean; lease?: string };
type Session = { appId: number; lease: string };
type State = { available: boolean; active: boolean; busy: boolean; failed: boolean };

export function createSteamDownloadPower(read: () => number, send: (request: DownloadPowerRequest) => Promise<Response>) {
  let session: Session | undefined;
  let busy = false;
  let renewing = false;
  let failed = false;
  let timer: ReturnType<typeof setInterval> | undefined;
  let generation = 0;
  const listeners = new Set<() => void>();
  const notify = () => listeners.forEach(listener => listener());

  async function stop() {
    generation++;
    const old = session;
    session = undefined;
    clearInterval(timer);
    timer = undefined;
    notify();
    if (old) {
      try { await send({ action: "stop", ...old }); }
      catch { /* Without renewal, the native lease expires. */ }
    }
  }

  async function tick() {
    const current = session;
    if (!current) return;
    if (read() !== current.appId) { await stop(); return; }
    if (renewing) return;
    renewing = true;
    try {
      const result = await send({ action: "renew", ...current });
      if (session === current && result?.ok !== true) await stop();
    } catch { if (session === current) await stop(); }
    finally { renewing = false; }
  }

  return {
    read(): State { return { available: read() > 0, active: !!session, busy, failed }; },
    subscribe(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener); }; },
    refresh() { notify(); if (session && read() !== session.appId) void stop(); },
    async start() {
      const appId = read();
      if (!appId || busy || session) return;
      const current = ++generation;
      busy = true;
      failed = false;
      notify();
      try {
        const reply = await send({ action: "start", appId });
        if (reply?.ok !== true || typeof reply.lease !== "string" || !/^[a-f0-9]{32}$/.test(reply.lease)) {
          failed = generation === current;
          return;
        }
        if (generation !== current || read() !== appId) {
          await send({ action: "stop", appId, lease: reply.lease });
          return;
        }
        session = { appId, lease: reply.lease };
        timer = setInterval(() => void tick(), 2_000);
      } catch { failed = generation === current; }
      finally { busy = false; notify(); }
    },
    stop,
    tick,
  };
}

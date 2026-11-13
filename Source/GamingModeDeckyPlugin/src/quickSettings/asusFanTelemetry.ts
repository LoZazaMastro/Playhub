export type AsusFans = { cpu_rpm: number | null; gpu_rpm: number | null };
export type AsusTemperatures = { cpu_c: number | null; gpu_c: number | null };
type Reading = { token: string; fans: AsusFans | null; temperatures: AsusTemperatures | null; sampled_at_ms: number };
export type AsusFanView = { available: boolean; failed: boolean; fans: AsusFans | null; temperatures?: AsusTemperatures | null };
const validRpm = (value: unknown) => value === null || (typeof value === "number" && Number.isInteger(value)
  && value >= 0 && value <= 12000 && value % 100 === 0);
const validTemperature = (value: unknown) => value === null || (typeof value === "number" && Number.isInteger(value)
  && value >= 1 && value <= 125);
export function asusFanTelemetry(raw: unknown, now: number): Reading | false | null {
  if (!raw || typeof raw !== "object") return null;
  const value = raw as any;
  if (value.available === false) return false;
  if (value.available !== true || value.ok !== true || value.provider !== "asus-ally"
      || typeof value.token !== "string" || !/^[a-f0-9]{64}$/.test(value.token) || typeof value.capabilities?.fan_rpm !== "boolean"
      || !value.fans || !validRpm(value.fans.cpu_rpm) || !validRpm(value.fans.gpu_rpm)
      || !Number.isSafeInteger(value.sampled_at_ms) || value.sampled_at_ms <= 0
      || now - value.sampled_at_ms < -1000 || now - value.sampled_at_ms >= 6000) return null;
  const hasFans = value.fans.cpu_rpm !== null || value.fans.gpu_rpm !== null;
  if (value.capabilities.fan_rpm !== hasFans) return null;
  let temperatures: AsusTemperatures | null = null;
  if (value.temperatures !== undefined || value.capabilities.temperature_c !== undefined) {
    const t = value.temperatures;
    if (!t || Object.keys(t).length !== 2 || !validTemperature(t.cpu_c) || !validTemperature(t.gpu_c)) return null;
    const hasTemperatures = t.cpu_c !== null || t.gpu_c !== null;
    if (value.capabilities.temperature_c !== hasTemperatures) return null;
    if (hasTemperatures) temperatures = { cpu_c: t.cpu_c, gpu_c: t.gpu_c };
  }
  if (!hasFans && !temperatures) return null;
  return { token: value.token, fans: hasFans ? { cpu_rpm: value.fans.cpu_rpm, gpu_rpm: value.fans.gpu_rpm } : null,
    temperatures, sampled_at_ms: value.sampled_at_ms };
}

type Dependencies = { get(): Promise<unknown>; visible(): boolean; now(): number;
  schedule(callback: () => void, milliseconds: number): unknown; cancel(timer: unknown): void;
  changed(view: AsusFanView): void };
export function createAsusFanMonitor(deps: Dependencies) {
  let disposed = false, running = false, revision = 0, timer: unknown, expiry: unknown;
  let view: AsusFanView = { available: false, failed: false, fans: null, temperatures: null };
  const update = (value: AsusFanView) => { view = value; if (!disposed) deps.changed(value); };
  const clear = () => { if (timer !== undefined) deps.cancel(timer); if (expiry !== undefined) deps.cancel(expiry); timer = expiry = undefined; };
  async function refresh() {
    if (disposed || running || !deps.visible()) return;
    const observed = revision;
    running = true;
    try {
      const reply = await deps.get();
      if (disposed || observed !== revision || !deps.visible()) return;
      const value = asusFanTelemetry(reply, deps.now());
      if (expiry !== undefined) deps.cancel(expiry);
      expiry = undefined;
      if (value === false) update({ available: false, failed: false, fans: null, temperatures: null });
      else if (!value) update({ available: view.available, failed: true, fans: null, temperatures: null });
      else {
        update({ available: true, failed: false, fans: value.fans, temperatures: value.temperatures });
        expiry = deps.schedule(() => { expiry = undefined; update({ available: true, failed: true, fans: null, temperatures: null }); },
          Math.max(1, 6000 - Math.max(0, deps.now() - value.sampled_at_ms)));
      }
    } catch {
      if (!disposed && observed === revision && deps.visible()) update({ available: view.available, failed: true, fans: null, temperatures: null });
    } finally {
      running = false;
      if (!disposed && deps.visible()) timer = deps.schedule(() => { timer = undefined; void refresh(); }, observed === revision ? 3000 : 0);
    }
  }
  return {
    visibilityChanged() {
      revision++;
      if (timer !== undefined) deps.cancel(timer);
      timer = undefined;
      if (!deps.visible()) { clear(); update({ ...view, fans: null, temperatures: null }); }
      else void refresh();
    },
    dispose() { disposed = true; revision++; clear(); },
  };
}

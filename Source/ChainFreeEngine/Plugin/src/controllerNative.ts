export type ControllerAction = "settings" | "test" | "sticks" | "gyro" | "layout";
export type ControllerReason = "ready" | "noController" | "nativeUnavailable" | "unknownCapabilities" | "unsupported" | "noGame" | "changed";
export interface SteamController {
  index: number;
  name: string;
  identity: string;
  hardware: string;
  capabilities: bigint | null;
}
const integer = (value: unknown): value is number => typeof value === "number" && Number.isSafeInteger(value) && value >= 0;
function capabilityBits(value: unknown): bigint | null {
  try {
    if (typeof value === "bigint") return value >= 0n ? value : null;
    if (integer(value)) return BigInt(value);
    if (typeof value === "string" && /^\d+$/.test(value)) return BigInt(value);
  } catch { /* Malformed native snapshots are not evidence of support. */ }
  return null;
}
export function readSteamControllers(store: any): SteamController[] {
  try {
    const list = store?.GetControllers?.();
    if (!Array.isArray(list)) return [];
    const result: SteamController[] = [];
    const seen = new Set<number>();
    for (const item of list) {
      if (!item || !integer(item.nControllerIndex) || seen.has(item.nControllerIndex)) continue;
      seen.add(item.nControllerIndex);
      const hex = (value: unknown) => integer(value) && value <= 0xffff ? value.toString(16).padStart(4, "0") : "????";
      const hardware = `${hex(item.unVendorID)}:${hex(item.unProductID)}`;
      const name = typeof item.strName === "string" ? item.strName.slice(0, 120) : "";
      result.push({ index: item.nControllerIndex, name, hardware,
        identity: `${item.nControllerIndex}:${hardware}:${typeof item.strSerialNumber === "string" ? item.strSerialNumber : name}`,
        capabilities: capabilityBits(item.unCapabilities) });
    }
    return result;
  } catch { return []; }
}
export function controllerActionReason(action: ControllerAction, device: SteamController | undefined, router: any): ControllerReason {
  if (!device) return "noController";
  if (typeof router?.Navigate !== "function") return "nativeUnavailable";
  if (action === "layout" && !runningAppId(router)) return "noGame";
  if (action === "sticks" || action === "gyro") {
    if (device.capabilities === null) return "unknownCapabilities";
    // Steam's installed controller attribute protocol: sticks bits 2/3, gyro bit 11.
    const mask = action === "gyro" ? 1n << 11n : (1n << 2n) | (1n << 3n);
    if ((device.capabilities & mask) === 0n) return "unsupported";
  }
  return "ready";
}
function runningAppId(router: any): number | null {
  const id = router?.MainRunningApp?.appid ?? router?.MainRunningAppID;
  return integer(id) && id > 0 && id <= 0xffffffff ? id : null;
}
export function openControllerAction(action: ControllerAction, expected: SteamController | undefined, store: any, router: any): ControllerReason {
  const device = readSteamControllers(store).find(item => item.identity === expected?.identity);
  if (!device) return expected ? "changed" : "noController";
  const reason = controllerActionReason(action, device, router);
  if (reason !== "ready") return reason;
  const routes: Record<ControllerAction, string> = {
    settings: `/settings/controller/controller/${device.index}`,
    test: `/controller/devicesupport/${device.index}`,
    sticks: `/controller/calibration/${device.index}/Inputs`,
    gyro: `/controller/calibration/${device.index}/Gyro`,
    layout: `/app/${runningAppId(router)}/controllerconfigurator/main`,
  };
  try {
    router.CloseSideMenus?.();
    router.Navigate(routes[action]);
    return "ready";
  } catch { return "nativeUnavailable"; }
}

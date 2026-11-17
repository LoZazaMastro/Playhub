// ControllerStore uses Valve EControllerType: SteamController=2, Triton=10.
// This is NOT the differently numbered public ESteamInputType enum.
// Protocol source: libsdl-org/SDL src/joystick/controller_type.h (Valve),
// also verified against the installed Steam controller-icon/type module.
const steamControllerTypes = new Set([2, 10]);

export function translationController(store: any): string | null {
  try {
    const devices = store?.GetControllers?.();
    if (!Array.isArray(devices)) return null;
    const matching = devices.filter(item => item && Number.isSafeInteger(item.nControllerIndex) && item.nControllerIndex >= 0
      && item.nControllerIndex < 0xffffffff && steamControllerTypes.has(item.eControllerType));
    if (!matching.length) return null;
    // ControllerStore only contains connected devices (Steam removes disconnects).
    return matching.map(item => `${item.nControllerIndex}:${item.eControllerType}:${String(item.strSerialNumber ?? "")}`).sort().join("|");
  } catch { return null; }
}

const virtualVendor = 0x045e;
const virtualProduct = 0x7eb0;
export const virtualControllerName = "Xbox 360 Controller";

/** Rename only Playhub's dedicated ViGEm identity, through Steam's own API. */
export function createVirtualControllerNamer(getStore: () => any, input: any, clock = Date.now) {
  const attempted = new WeakMap<object, { count: number; at: number }>();
  return () => {
    try {
      const store = getStore(), controllers = store?.GetControllers?.();
      if (!Array.isArray(controllers) || controllers.length > 16 || typeof store.GetController !== "function"
          || typeof input?.SetControllerName !== "function") return;
      for (const controller of controllers) {
        const index = controller?.nControllerIndex;
        if (!Number.isSafeInteger(index) || index < 0 || index >= 16 || controller.bRemoteDevice !== false
            || controller.unVendorID !== virtualVendor || controller.unProductID !== virtualProduct
            || store.GetController(index) !== controller || controller.strName === virtualControllerName) continue;
        const previous = attempted.get(controller), now = clock();
        if (previous && (previous.count >= 3 || now - previous.at < 5000)) continue;
        attempted.set(controller, { count: (previous?.count ?? 0) + 1, at: now });
        // Recheck the native object adjacent to the setter; an index reused by
        // a physical controller never inherits a queued rename.
        if (store !== getStore() || store.GetController(index) !== controller) continue;
        Promise.resolve(input.SetControllerName(index, virtualControllerName)).catch(() => undefined);
      }
    } catch { /* Native inventory can disappear during Steam/plugin unload. */ }
  };
}

export function installVirtualControllerName() {
  const tick = createVirtualControllerNamer(() => (window as any).ControllerStore, (window as any).SteamClient?.Input);
  const timer = window.setInterval(tick, 1000); tick();
  return () => window.clearInterval(timer);
}

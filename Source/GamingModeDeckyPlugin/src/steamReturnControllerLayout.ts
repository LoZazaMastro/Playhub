const menuLayout = "steam://forceinputappid/769";
const automaticLayout = "steam://forceinputappid/0";
const layoutTimeoutMs = 4000;

/** Resolve a connected physical Steam Controller using Steam's own type map. */
export function activeSteamController(store: any): object | undefined {
  try {
    if (typeof store?.GetControllers !== "function" || typeof store.GetController !== "function"
        || typeof store.GetControllerTypeString !== "function") return undefined;
    const controllers = store.GetControllers();
    if (!Array.isArray(controllers) || controllers.length > 16) return undefined;
    const indices = new Set<number>();
    for (const controller of controllers) {
      const index = controller?.nControllerIndex;
      if (!Number.isSafeInteger(index) || index < 0 || index >= 16 || indices.has(index)) return undefined;
      indices.add(index);
    }
    for (const controller of controllers) {
      if (controller.bRemoteDevice !== false || controller.unVendorID !== 0x28de
          || !Number.isSafeInteger(controller.eControllerType)
          || store.GetController(controller.nControllerIndex) !== controller) continue;
      const type = store.GetControllerTypeString(controller.eControllerType);
      // These are native enum keys, not translated names or user-editable labels.
      const original = type === "controller_steamcontroller_gordon"
        && [0x1101, 0x1102, 0x1105, 0x1106, 0x1142].includes(controller.unProductID);
      const triton = type === "controller_steamcontroller_triton"
        && [0x1302, 0x1304].includes(controller.unProductID);
      if (original || triton) return controller;
    }
  } catch { /* An unknown/reloading native inventory must not force a layout. */ }
  return undefined;
}

export function createSteamReturnControllerLayout(
  execute: (url: string) => void,
  schedule: (callback: () => void, delay: number) => number,
  cancelTimer: (timer: number) => void,
  connectedController: () => object | undefined = () => undefined,
) {
  let app = 0;
  let attemptedApp = 0;
  let timer: number | undefined;
  let controller: object | undefined;

  function release() {
    if (!app) return;
    app = 0;
    controller = undefined;
    if (timer !== undefined) cancelTimer(timer);
    timer = undefined;
    execute(automaticLayout);
  }

  return {
    begin(exitedApp: number, runningApp: number) {
      if (app || attemptedApp === exitedApp || exitedApp < 0x80000000 || runningApp !== exitedApp) return;
      const current = connectedController();
      if (!current || typeof current !== "object") return;
      execute(menuLayout);
      controller = current;
      attemptedApp = exitedApp;
      app = exitedApp;
      timer = schedule(release, layoutTimeoutMs);
    },
    observe(runningApp: number, mainFocused: boolean) {
      if (app && (app !== runningApp || !mainFocused || connectedController() !== controller)) release();
    },
    release() { release(); attemptedApp = 0; },
  };
}

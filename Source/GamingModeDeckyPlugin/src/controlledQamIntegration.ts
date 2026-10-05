import { DFL } from "./decky";
import { API_BASE } from "./api";
import { selectOverlay } from "./dashboardOverlay";
import { createControlledQamFocus, steamAppId } from "./controlledQamFocus";
import { call } from "@decky/api";

export function installControlledQamFocus(): () => void {
  const ui = DFL as any;
  const steam = (window as any).SteamClient;
  const main = () => {
    const store = (window as any).SteamUIStore?.WindowStore ?? ui.Router?.WindowStore;
    const instance = store?.GamepadUIMainWindowInstance;
    return instance?.IsGamepadUIOverlayWindow?.() === true ? undefined : instance?.MenuStore;
  };
  const patches: any[] = [];
  let serial = 0;
  const prefix = `qam-${Date.now()}-${Math.random().toString(36).slice(2)}`;
  const send = async (action: string, requestId: string, appId?: number) => {
    const response = await fetch(`${API_BASE}/qam/focus/${action}`, {
      method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ requestId, appId }), signal: AbortSignal.timeout(2000),
    });
    if (!response.ok) throw new Error(`QAM focus ${action}: ${response.status}`);
    return response.json();
  };
  const control = createControlledQamFocus({
    runningApp: () => steamAppId((window as any).SteamUIStore?.MainRunningAppID ?? ui.Router?.MainRunningAppID),
    nativeOverlayAvailable: async app => {
      if (typeof steam?.Overlay?.GetOverlayBrowserInfo !== "function") throw new Error("Native overlay identity unavailable");
      const infos = await steam.Overlay.GetOverlayBrowserInfo();
      if (!Array.isArray(infos)) throw new Error("Native overlay identity unavailable");
      // Browser information itself is evidence of a native overlay; it need
      // not already have a visible gamepad browser window to remain untouched.
      const identity = selectOverlay(infos, app);
      if (!identity && infos.some(info => Number(info?.appID) === app)) throw new Error("Ambiguous native overlay identity");
      return identity !== null;
    },
    mainMenu: main,
    gameBar: (requestId, appId, pressed) => call("request_uwp_gamebar", { requestId, appId, pressed }),
    acquire: (id, appId) => send("acquire", id, appId), close: id => send("close", id), release: id => send("release", id),
    requestId: () => `${prefix}-${++serial}`,
    warn: error => console.warn("[Playhub QAM] Controlled focus unavailable", error),
  });
  const patch = (object: any, method: string, handler: (args: any[], result: any) => any) => {
    if (typeof object?.[method] === "function") patches.push(ui.afterPatch(object, method, handler));
  };
  try {
    // These are global Steam menu events, independent of the mounted Decky tab.
    const menu = main();
    patch(menu, "OpenSideMenu", (args, result) => {
      const side = Number(args[0]);
      if ((side === 1 || side === 2) && menu.GetOpenSideMenu() === side) control.opened(menu.GetQuickAccessTab?.(), side);
      return result;
    });
    patch(menu, "CloseSideMenus", (_args, result) => {
      if (menu.GetOpenSideMenu() === 0) control.closed();
      return result;
    });
    patch(ui.Navigation, "OpenQuickAccessMenu", (args, result) => { control.opened(args[0]); return result; });
    // Library navigation selects Steam/an app. QAM pages (including Playhub
    // Dashboard) must retain the handoff while the user works inside them.
    const navigating = (args: any[]) => {
      if (typeof args[0] === "string" && /^\/library(?:\/|$)/.test(args[0])) control.applicationSelected();
    };
    if (typeof ui.Router?.Navigate === "function") patches.push(ui.beforePatch(ui.Router, "Navigate", navigating));
    if (typeof ui.Navigation?.Navigate === "function") patches.push(ui.beforePatch(ui.Navigation, "Navigate", navigating));
    if (typeof steam?.Apps?.RunGame === "function") patches.push(ui.beforePatch(steam.Apps, "RunGame", () => control.applicationSelected()));
    // The event's nAppID describes the input configuration (the real desktop
    // layout can report 413080 or 0), not necessarily the running shortcut.
    // Native acquire verifies the actual foreground game and selected app.
    const system = steam?.System?.UI;
    if (typeof system?.RegisterForSystemKeyEvents === "function") {
      const registration = system.RegisterForSystemKeyEvents((event: any) => control.systemKey(event));
      patches.push({ unpatch: () => {
        if (typeof registration === "function") registration();
        else if (typeof registration?.unregister === "function") registration.unregister();
        else registration?.Unregister?.();
      } });
    }
  } catch (error) {
    control.dispose();
    for (const entry of patches.reverse()) try { entry.unpatch(); } catch {}
    console.warn("[Playhub QAM] Menu hooks unavailable", error);
    return () => {};
  }
  return () => {
    control.dispose();
    for (const entry of patches.reverse()) try { entry.unpatch(); } catch {}
  };
}

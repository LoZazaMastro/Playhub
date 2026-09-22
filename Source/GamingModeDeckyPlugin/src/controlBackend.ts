import { call as deckyCall } from "@decky/api";

let ready: Promise<void> | undefined;
export function ensureControlBackend(): Promise<void> {
  if (!ready) ready = (async () => {
    try {
      await deckyCall("get_panel_preferences");
      return;
    } catch (error: any) {
      const detail = `${error?.message ?? error} ${error?.stack ?? ""}`;
      if (!/passive|does not implement main\.py|get_panel_preferences.*attribute|no attribute.*get_panel_preferences/i.test(detail)) throw error;
      const bridge = (window as any).DeckyBackend;
      if (typeof bridge?.call !== "function") throw error;
      // A formerly frontend-only plugin needs a backend reload after its first upgrade.
      // Reload only Playhub, never Decky or any other installed plugin.
      await bridge.call("loader/reload_plugin", "Playhub");
    }
    for (let attempt = 0; attempt < 12; attempt++) {
      await new Promise(resolve => window.setTimeout(resolve, 500));
      try { await deckyCall("get_panel_preferences"); return; } catch (error) { if (attempt === 11) throw error; }
    }
  })().catch(error => { ready = undefined; throw error; });
  return ready;
}

export async function call<Args extends unknown[] = unknown[], Result = unknown>(method: string, ...args: Args): Promise<Result> {
  await ensureControlBackend();
  return deckyCall<Args, Result>(method, ...args);
}

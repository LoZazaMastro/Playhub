const CORNER = "overlay_fps_counter_corner";
const DETAIL = "overlay_fps_counter_detail_level";
const OVERLAY = "enable_overlay";
const OPACITY = "overlay_fps_counter_bgopacity";
const LAST_CORNER = "playhub.steam-performance-monitor.last-corner";
const LAST_OPACITY = "playhub.steam-performance-monitor.background-opacity";

export type MonitorCorner = 0 | 1 | 2 | 3 | 4 | 5 | 6;
export type MonitorDetail = 1 | 2 | 3 | 4;
export type MonitorState = { corner: MonitorCorner; detail: MonitorDetail; overlay: boolean; backgroundOpacity?: number };
type Values = { [CORNER]: MonitorCorner; [DETAIL]: MonitorDetail; [OVERLAY]: boolean; [OPACITY]: number };
type SteamSettings = { GetClientSetting: <K extends keyof Values>(key: K) => [unknown, (value: Values[K]) => unknown] };
type SettingsEvents = { RegisterForSettingsArrayChanges?: (listener: () => void) => { unregister: () => void } };
type AppDetails = { unAppID?: number; bOverlayEnabled?: boolean };
type AppDetailsStore = {
  GetAppDetails: (appId: number) => AppDetails | undefined;
  RegisterForAppData: (appId: number, listener: (details?: AppDetails) => void) => { unregister: () => void };
};
export type SteamMonitorWindow = {
  localStorage?: Pick<Storage, "getItem" | "setItem">;
  settingsStore?: SteamSettings;
  appStore?: { GetAppOverviewByAppID?: (appId: number) => { BIsShortcut?: () => boolean } | undefined };
  appDetailsStore?: AppDetailsStore;
  SteamClient?: { Settings?: SettingsEvents; Apps?: { ToggleEnableSteamOverlayForApp?: (appId: number) => unknown } };
  LocalizationManager?: { LocalizeString: (token: string) => unknown };
};

export const monitorCorners = [1, 5, 2, 4, 6, 3] as const;
export const monitorDetails = [1, 2, 3, 4] as const;
const lastCorners = new WeakMap<SteamMonitorWindow, { corner: Exclude<MonitorCorner, 0>; saved: boolean }>();
const lastOpacities = new WeakMap<SteamMonitorWindow, number>();
const shortcutWrites = new WeakMap<AppDetailsStore, Map<number, Promise<void>>>();
const unconfirmedShortcuts = new WeakMap<AppDetailsStore, Set<number>>();

function rememberCorner(host: SteamMonitorWindow, corner: Exclude<MonitorCorner, 0>) {
  if (lastCorners.get(host)?.corner === corner) return;
  let saved = false;
  try {
    const storage = host.localStorage;
    if (storage) {
      storage.setItem(LAST_CORNER, String(corner));
      saved = true;
    }
  } catch { /* Some Steam windows block access to browser storage. */ }
  lastCorners.set(host, { corner, saved });
}

function preferredCorner(host: SteamMonitorWindow) {
  const fallback = lastCorners.get(host);
  if (fallback?.saved === false) return fallback.corner;
  try {
    const stored = host.localStorage?.getItem(LAST_CORNER);
    const corner = monitorCorners.find(value => String(value) === stored);
    if (corner !== undefined) return corner;
  } catch { /* The confirmed value in this window remains available. */ }
  return fallback?.corner ?? 1;
}

export function steamPerformanceMonitor(host: SteamMonitorWindow) {
  const settings = host.settingsStore;
  if (typeof settings?.GetClientSetting !== "function") return null;

  function read(): MonitorState | null {
    try {
      const [corner, setCorner] = settings!.GetClientSetting(CORNER);
      const [detail, setDetail] = settings!.GetClientSetting(DETAIL);
      const [overlay, setOverlay] = settings!.GetClientSetting(OVERLAY);
      if (typeof setCorner !== "function" || typeof setDetail !== "function" || typeof setOverlay !== "function" ||
        typeof overlay !== "boolean" || (corner !== 0 && !monitorCorners.some(value => value === corner)) ||
        !monitorDetails.some(value => value === detail)) return null;
      if (corner !== 0) rememberCorner(host, corner as Exclude<MonitorCorner, 0>);
      const state: MonitorState = { corner: corner as MonitorCorner, detail: detail as MonitorDetail, overlay };
      try {
        const [opacity, setOpacity] = settings!.GetClientSetting(OPACITY);
        if (typeof setOpacity === "function" && typeof opacity === "number" && Number.isFinite(opacity) && opacity >= 0 && opacity <= 1) {
          state.backgroundOpacity = opacity;
          if (opacity > 0 && lastOpacities.get(host) !== opacity) {
            lastOpacities.set(host, opacity);
            try { host.localStorage?.setItem(LAST_OPACITY, String(opacity)); } catch { /* Retain the value in this window. */ }
          }
        }
      } catch { /* Older clients retain the basic monitor controls. */ }
      return state;
    } catch { return null; }
  }

  async function write<K extends keyof Values>(key: K, value: Values[K] | ((state: MonitorState) => Values[K])) {
    const state = read();
    if (!state) throw new Error("Steam performance monitor unavailable");
    const desired = typeof value === "function" ? value(state) : value;
    const [current, set] = settings!.GetClientSetting(key);
    if (current !== desired) await set(desired);
  }

  return {
    read,
    setEnabled: (enabled: boolean) => write(CORNER, state => enabled ? state.corner || preferredCorner(host) : 0),
    setCorner: (corner: Exclude<MonitorCorner, 0>) => write(CORNER, corner),
    setDetail: (detail: MonitorDetail) => write(DETAIL, detail),
    enableOverlay: () => write(OVERLAY, true),
    setTransparent: (transparent: boolean) => {
      const state = read();
      if (state?.backgroundOpacity === undefined) return Promise.reject(new Error("Steam monitor appearance unavailable"));
      let opacity = lastOpacities.get(host) ?? .8;
      if (!lastOpacities.has(host)) {
        try {
          const stored = host.localStorage?.getItem(LAST_OPACITY);
          const value = stored ? Number(stored) : NaN;
          if (Number.isFinite(value) && value > 0 && value <= 1) opacity = value;
        } catch { /* Use Steam's usual opacity when storage is unavailable. */ }
      }
      return write(OPACITY, transparent ? 0 : opacity);
    },
    subscribe(listener: () => void) {
      try {
        let subscribed = true;
        const connection = host.SteamClient?.Settings?.RegisterForSettingsArrayChanges?.(() => {
          queueMicrotask(() => { if (subscribed) listener(); });
        });
        return () => { subscribed = false; connection?.unregister(); };
      } catch { return () => {}; }
    },
  };
}

export function steamShortcutOverlay(host: SteamMonitorWindow, appId: number) {
  const details = host.appDetailsStore;
  const apps = host.SteamClient?.Apps;
  try {
    if (appId < 0x80000000 || appId > 0xffffffff || !Number.isInteger(appId) ||
      host.appStore?.GetAppOverviewByAppID?.(appId)?.BIsShortcut?.() !== true ||
      typeof details?.GetAppDetails !== "function" || typeof details.RegisterForAppData !== "function" ||
      typeof apps?.ToggleEnableSteamOverlayForApp !== "function") return null;
  } catch { return null; }

  const value = (current?: AppDetails) => current?.unAppID === appId && typeof current.bOverlayEnabled === "boolean"
    ? current.bOverlayEnabled : null;
  return {
    read: () => value(details.GetAppDetails(appId)),
    subscribe(listener: (enabled: boolean | null) => void) {
      const connection = details.RegisterForAppData(appId, current => listener(value(current)));
      return () => connection.unregister();
    },
    async enable(isCurrentGame: () => boolean) {
      const writes = shortcutWrites.get(details) ?? new Map<number, Promise<void>>();
      shortcutWrites.set(details, writes);
      if (writes.has(appId)) return writes.get(appId);
      const pending = Promise.resolve().then(async () => {
        // Steam exposes a toggle, so use fresh details and running-game identity.
        const current = value(details.GetAppDetails(appId));
        if (current === null || !isCurrentGame()) throw new Error("Steam shortcut no longer active");
        const unconfirmed = unconfirmedShortcuts.get(details) ?? new Set<number>();
        unconfirmedShortcuts.set(details, unconfirmed);
        if (current) { unconfirmed.delete(appId); return; }
        if (unconfirmed.has(appId)) throw new Error("Check this shortcut's overlay setting in Steam");
        // A second native toggle can undo a successful first one if the store
        // hasn't caught up. Require positive confirmation before issuing another.
        unconfirmed.add(appId);
        const request = Promise.resolve(apps.ToggleEnableSteamOverlayForApp!(appId));
        let timeout: ReturnType<typeof setTimeout> | undefined;
        try {
          await Promise.race([request, new Promise<void>(resolve => { timeout = setTimeout(resolve, 800); })]);
        } finally { clearTimeout(timeout); }
        for (let retry = 0; retry < 8; retry++) {
          if (value(details.GetAppDetails(appId)) === true) { unconfirmed.delete(appId); return; }
          await new Promise<void>(resolve => setTimeout(resolve, 100));
        }
        throw new Error("Steam shortcut setting not confirmed");
      });
      writes.set(appId, pending);
      try { await pending; } finally { writes.delete(appId); }
    },
  };
}


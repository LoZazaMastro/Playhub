import { DialogButton, DropdownItem, ToggleField } from "@decky/ui";
import { useEffect, useRef, useState } from "react";
import { DFL } from "../decky";
import { controlLocale } from "../controlCenterLocale";
import { steamAppId } from "../controlledQamFocus";
import {
  monitorCorners, monitorDetails, steamPerformanceMonitor, steamShortcutOverlay,
  type MonitorCorner, type MonitorDetail, type MonitorState, type SteamMonitorWindow,
} from "./steamPerformanceMonitor";

const cornerTokens = ["TopLeft", "TopCenter", "TopRight", "BottomLeft", "BottomCenter", "BottomRight"];
const cornerLabels = ["Top left", "Top center", "Top right", "Bottom left", "Bottom center", "Bottom right"];
const detailTokens = ["ClassicFPSOnly", "FPSDetailsAndGraph", "FPSCPUGPUSummary", "FPSCPUGPURAMDetails"];
const detailLabels = ["FPS only", "FPS and graph", "FPS, CPU and GPU", "Full details"];
const transparentLabels: Record<string, string> = {
  en: "Transparent background", it: "Sfondo trasparente", de: "Transparenter Hintergrund", es: "Fondo transparente",
  fr: "Arrière-plan transparent", pt: "Fundo transparente", ru: "Прозрачный фон", uk: "Прозоре тло",
  ja: "背景を透明にする", ko: "투명 배경", zh: "透明背景", hi: "पारदर्शी पृष्ठभूमि",
};

function getHost(): SteamMonitorWindow {
  const current = window as unknown as SteamMonitorWindow;
  if (current.settingsStore) return current;
  try {
    const store = (DFL as any).Router?.WindowStore;
    const item = store?.GamepadUIMainWindowInstance;
    return [item, item?.window, item?.m_Window].find(value => value?.settingsStore) ?? current;
  } catch { return current; }
}

function label(host: SteamMonitorWindow, token: string, fallback: string): string {
  try {
    const value = host.LocalizationManager?.LocalizeString(token);
    if (typeof value === "string" && value.trim() && value !== token) return value;
  } catch { /* The Steam UI may still be loading its language. */ }
  return fallback;
}

export function SteamPerformanceSettings({ locale, appId }: { locale: string; appId: number }) {
  const [state, setState] = useState<MonitorState | null>(null);
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);
  const [loading, setLoading] = useState(true);
  const [attempt, setAttempt] = useState(0);
  const running = useRef(false);
  const mounted = useRef(false);
  const monitorControl = useRef<HTMLDivElement>(null);
  const copy = controlLocale(locale);
  const host = getHost();

  useEffect(() => {
    mounted.current = true;
    let timer: number | undefined;
    let unsubscribe: (() => void) | undefined;
    const connect = (remaining: number) => {
      const monitor = steamPerformanceMonitor(getHost());
      const initial = monitor?.read();
      if (monitor && initial) {
        setState(initial);
        setLoading(false);
        unsubscribe = monitor.subscribe(() => setState(monitor.read()));
      } else if (remaining > 0) {
        timer = window.setTimeout(() => connect(remaining - 1), 250);
      } else setLoading(false);
    };
    connect(8);
    return () => {
      mounted.current = false;
      window.clearTimeout(timer);
      unsubscribe?.();
    };
  }, [attempt]);

  function returnFocus() {
    const owner = monitorControl.current?.ownerDocument;
    if (!owner?.activeElement?.closest("[data-overlay-repair]")) return;
    owner.defaultView?.requestAnimationFrame(() => {
      if (owner.activeElement !== owner.body && !owner.activeElement?.closest("[data-overlay-repair]")) return;
      monitorControl.current?.querySelector<HTMLElement>("[role=switch], button, [tabindex='0']")?.focus({ preventScroll: true });
    });
  }

  async function change(write: (monitor: NonNullable<ReturnType<typeof steamPerformanceMonitor>>) => Promise<void>,
    matches: (value: MonitorState) => boolean, repaired = false) {
    if (running.current) return;
    running.current = true;
    setBusy(true);
    setFailed(false);
    const monitor = steamPerformanceMonitor(getHost());
    try {
      if (!monitor) throw new Error("Steam settings unavailable");
      await write(monitor);
      for (let retry = 0; retry < 8 && mounted.current; retry++) {
        const actual = monitor.read();
        if (actual && matches(actual)) {
          if (repaired) returnFocus();
          setState(actual);
          return;
        }
        await new Promise<void>(resolve => window.setTimeout(resolve, 100));
      }
      if (mounted.current) setFailed(true);
    } catch {
      if (mounted.current) setFailed(true);
    } finally {
      running.current = false;
      if (mounted.current) setBusy(false);
    }
  }

  const title = label(host, "#Settings_InGame_Overlay_PerformanceMonitor", "Steam performance monitor");
  const overlayLabel = label(host, "#Settings_InGame_EnableOverlay", "Enable the Steam Overlay");
  const enabled = state?.corner !== 0 && state?.overlay === true;

  return <>
    <div ref={monitorControl}><ToggleField bottomSeparator="none" label={title} checked={enabled} disabled={busy || !state?.overlay}
      onChange={value => void change(monitor => monitor.setEnabled(value), result => (result.corner !== 0) === value)} /></div>
    {state?.overlay === false && <div data-overlay-repair><DialogButton disabled={busy} onClick={() => void change(
      monitor => monitor.enableOverlay(), result => result.overlay, true)}>{overlayLabel}</DialogButton></div>}
    {state?.overlay === true && appId !== 0 && <ShortcutOverlayAction key={appId} appId={appId} error={copy.saveError} onRepaired={returnFocus} />}
    {!state && <div className="qsMeta" role="status">{loading ? copy.loading : <>{copy.unavailable} <DialogButton onClick={() => {
      setLoading(true);
      setAttempt(value => value + 1);
    }}>{copy.retry}</DialogButton></>}</div>}
    {enabled && <>
      {state.backgroundOpacity !== undefined && <ToggleField bottomSeparator="none"
        label={transparentLabels[locale.split(/[-_]/)[0]] ?? transparentLabels.en} disabled={busy}
        checked={state.backgroundOpacity === 0} onChange={value => void change(monitor => monitor.setTransparent(value),
          result => result.backgroundOpacity !== undefined && (result.backgroundOpacity === 0) === value)} />}
      <div className="qsDropdownFullWidth"><DropdownItem bottomSeparator="none" layout="below"
        label={label(host, "#Settings_InGame_PerfOverlayDetailLevel", "Detail level")} disabled={busy}
        rgOptions={monitorDetails.map((data, index) => ({ data, label: label(host, `#Settings_InGame_PerfOverlay_${detailTokens[index]}`, detailLabels[index]) }))}
        selectedOption={state.detail}
        onChange={option => { const data = option.data as MonitorDetail; void change(monitor => monitor.setDetail(data), result => result.detail === data); }} /></div>
      <div className="qsDropdownFullWidth"><DropdownItem bottomSeparator="none" layout="below"
        label={label(host, "#Settings_InGame_PerformanceMonitor", "Position")} disabled={busy}
        rgOptions={monitorCorners.map((data, index) => ({ data, label: label(host, `#Corner_${cornerTokens[index]}`, cornerLabels[index]) }))}
        selectedOption={state.corner}
        onChange={option => { const data = option.data as Exclude<MonitorCorner, 0>; void change(monitor => monitor.setCorner(data), result => result.corner === data); }} /></div>
    </>}
    {failed && <div role="status" className="qsMeta">{copy.saveError}</div>}
  </>;
}

function ShortcutOverlayAction({ appId, error, onRepaired }: { appId: number; error: string; onRepaired: () => void }) {
  const [disabled, setDisabled] = useState(false);
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);
  const mounted = useRef(false);
  useEffect(() => {
    mounted.current = true;
    let unsubscribe: (() => void) | undefined;
    try {
      const shortcut = steamShortcutOverlay(getHost(), appId);
      if (shortcut) {
        setDisabled(shortcut.read() === false);
        unsubscribe = shortcut.subscribe(enabled => setDisabled(enabled === false));
      }
    } catch { /* Steam can dispose its game details on mode changes. */ }
    return () => { mounted.current = false; unsubscribe?.(); };
  }, [appId]);
  if (!disabled) return null;

  async function enable() {
    setBusy(true);
    setFailed(false);
    try {
      const shortcut = steamShortcutOverlay(getHost(), appId);
      if (!shortcut) throw new Error("Steam shortcut unavailable");
      await shortcut.enable(() => steamAppId((getHost() as any).SteamUIStore?.MainRunningAppID ?? (DFL as any).Router?.MainRunningAppID) === appId);
      if (mounted.current) { onRepaired(); setDisabled(false); }
    } catch { if (mounted.current) setFailed(true); }
    finally { if (mounted.current) setBusy(false); }
  }

  return <>
    <div data-overlay-repair><DialogButton disabled={busy} onClick={() => void enable()}>
      {label(getHost(), "#AppProperties_EnableOverlay", "Enable Steam Overlay for this game")}
    </DialogButton></div>
    {failed && <div role="status" className="qsMeta">{error}</div>}
  </>;
}

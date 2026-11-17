import { DialogButton, DropdownItem, SliderField } from "@decky/ui";
import { useEffect, useRef, useState } from "react";
import { FaMicrochip } from "react-icons/fa";
import { ControlSection } from "../ControlSection";
import { call } from "../controlBackend";
import { controlLocale } from "../controlCenterLocale";
import { nvidiaLocale } from "./nvidiaLocale";

type Feature = "frame_limit" | "vsync" | "power";
type SettingValue = number | "default";
type FpsMode = "default" | "off" | "limit" | "current";
export type NvidiaSetting = {
  id: Feature; value: number | null; default: boolean; supported: boolean; editable: boolean;
  values?: number[] | null; min?: number | null; max?: number | null; custom: boolean;
};
export type NvidiaState = {
  available: true; global: true; status: string; token: string; settings: NvidiaSetting[];
  ok?: boolean;
};
const features: Feature[] = ["frame_limit", "vsync", "power"];
export const NVIDIA_VSYNC = { game: 0x60925292, off: 0x08416747, on: 0x47814940 } as const;
const statuses = ["ready", "persisted", "unchanged", "changed", "permission", "busy", "unsupported", "driver-reload-required", "reboot-required", "restored", "failed", "lost-state", "unavailable", "invalid"];
const uint = (value: unknown): value is number => typeof value === "number" && Number.isInteger(value) && value >= 0 && value <= 0xffffffff;

export function validNvidiaState(value: unknown): value is NvidiaState {
  if (!value || typeof value !== "object") return false;
  const state = value as NvidiaState;
  if (state.available !== true || state.global !== true || !statuses.includes(state.status)
      || typeof state.token !== "string" || !/^[a-f0-9]{64}$/i.test(state.token)
      || !Array.isArray(state.settings) || state.settings.length < 1 || state.settings.length > 3) return false;
  const seen = new Set<string>();
  return state.settings.every(setting => {
    if (!setting || !features.includes(setting.id) || seen.has(setting.id)) return false;
    seen.add(setting.id);
    if (typeof setting.default !== "boolean" || typeof setting.supported !== "boolean" || typeof setting.editable !== "boolean"
        || typeof setting.custom !== "boolean" || setting.value !== null && !uint(setting.value) || setting.supported && setting.value === null
        || setting.editable && (!setting.supported || setting.custom || setting.value === null)) return false;
    if (setting.values != null && (!Array.isArray(setting.values) || setting.values.length > 32 || !setting.values.every(uint))) return false;
    if (setting.id === "frame_limit" && setting.supported && (!uint(setting.min) || !uint(setting.max)
        || setting.min < 20 || setting.max > 1000 || setting.min > setting.max)) return false;
    return true;
  });
}

function numericOptions(setting: NvidiaSetting): number[] {
  const known = setting.id === "vsync" ? Object.values(NVIDIA_VSYNC) : setting.id === "power" ? [1] : [];
  return known.filter(value => setting.values == null || setting.values.includes(value));
}
function canWrite(setting: NvidiaSetting, value: SettingValue): boolean {
  if (!setting.supported) return false;
  if (value === "default") return true;
  if (!setting.editable || setting.custom || !uint(value)) return false;
  return setting.id === "frame_limit"
    ? value === 0 || value >= setting.min! && value <= setting.max!
    : numericOptions(setting).includes(value);
}
const confirmed = (setting: NvidiaSetting): SettingValue | null => setting.default ? "default" : setting.value;

export function NvidiaControls({ locale, collapsed, onToggle }: { locale: string; collapsed: boolean; onToggle: (id: string) => void }) {
  const copy = nvidiaLocale(locale), common = controlLocale(locale);
  const [state, setState] = useState<NvidiaState>();
  const [busy, setBusy] = useState(false);
  const [detected, setDetected] = useState(false);
  const [error, setError] = useState<"unavailable" | "changed" | "failed">();
  const [fpsMode, setFpsMode] = useState<FpsMode>("default");
  const [fpsDraft, setFpsDraft] = useState(60);
  const snapshot = useRef<NvidiaState | undefined>(undefined);
  const mounted = useRef(false), editing = useRef(false), collapsedRef = useRef(collapsed);
  const readOnly = useRef(true);
  const epoch = useRef(0), lastRead = useRef(0);
  const lifecycle = useRef(0);
  const flight = useRef<{ generation: number; lifecycle: number; expired: boolean } | undefined>(undefined);
  collapsedRef.current = collapsed;

  function reportError(next?: "unavailable" | "changed" | "failed") {
    readOnly.current = next !== undefined;
    setError(next);
  }

  function accept(next: NvidiaState, preserveDraft = false) {
    snapshot.current = next; setState(next);
    if (!preserveDraft) {
      const fps = next.settings.find(setting => setting.id === "frame_limit");
      setFpsMode(!fps || fps.default ? "default" : fps.custom ? "current" : fps.value === 0 ? "off" : "limit");
      if (fps && fps.value !== null && fps.value >= 20 && fps.value <= 1000) setFpsDraft(fps.value);
      editing.current = false;
    }
  }

  async function request(feature?: Feature, value?: SettingValue) {
    if (!mounted.current || flight.current) return;
    const current = snapshot.current;
    const setting = current?.settings.find(item => item.id === feature);
    if (feature && (!current || !setting || readOnly.current || value === undefined || !canWrite(setting, value)
        || confirmed(setting) === value)) return;
    const operation = { generation: epoch.current, lifecycle: lifecycle.current, expired: false };
    flight.current = operation; setBusy(true); lastRead.current = Date.now();
    const valid = () => mounted.current && epoch.current === operation.generation && !operation.expired;
    // A stalled transport retires this reply and unlocks read-only recovery.
    // Writes stay blocked until a new read confirms a fresh token/state. The
    // backend mutex/CAS serializes any late delivery of the original request.
    const deadline = window.setTimeout(() => {
      if (valid()) {
        operation.expired = true; ++epoch.current; reportError("unavailable");
        if (flight.current === operation) { flight.current = undefined; setBusy(false); }
      }
    }, 16_000);
    try {
      const next = feature
        ? await call<[{ token: string; feature: Feature; value: SettingValue }], unknown>("set_nvidia_setting", { token: current!.token, feature, value: value! })
        : await call<[], unknown>("get_nvidia_settings");
      if (!valid()) return;
      // Only the existing authenticated backend enumeration, or a valid driver
      // snapshot, establishes the provider. A DLL filename never establishes it.
      if (next && typeof next === "object" && (next as any).global === true
          && (next as any).detected === true && statuses.includes((next as any).status)) setDetected(true);
      if (!validNvidiaState(next)) { reportError("unavailable"); return; }
      setDetected(true);
      const preserve = editing.current && feature !== "frame_limit";
      accept(next, preserve); lastRead.current = Date.now();
      if (!feature) { reportError(next.status === "ready" ? undefined : "unavailable"); return; }
      const actual = next.settings.find(item => item.id === feature);
      const applied = next.ok === true && (next.status === "persisted" || next.status === "unchanged")
        && actual && confirmed(actual) === value;
      reportError(applied ? undefined : next.status === "changed" ? "changed" : "failed");
    } catch { if (valid()) reportError("unavailable"); }
    finally {
      window.clearTimeout(deadline);
      const ownsFlight = flight.current === operation;
      if (ownsFlight) flight.current = undefined;
      if (mounted.current) {
        if (ownsFlight) setBusy(false);
        // React effect replay may remount this view while an old read finishes.
        // Retire that reply and issue a fresh read only after it settles.
        if (!operation.expired && lifecycle.current !== operation.lifecycle && !flight.current) void request();
      }
    }
  }

  useEffect(() => {
    mounted.current = true; ++epoch.current; ++lifecycle.current;
    void request();
    const visible = () => {
      if (document.visibilityState !== "hidden" && (!snapshot.current || !collapsedRef.current) && !editing.current
          && Date.now() - lastRead.current >= 5000) void request();
    };
    window.addEventListener("focus", visible);
    document.addEventListener("visibilitychange", visible);
    return () => {
      mounted.current = false; ++epoch.current;
      window.removeEventListener("focus", visible);
      document.removeEventListener("visibilitychange", visible);
    };
  }, []);
  useEffect(() => {
    if (!collapsed && state && !editing.current && Date.now() - lastRead.current >= 5000) void request();
  }, [collapsed]);

  if ((!state || !state.settings.some(setting => setting.supported)) && (!detected || !error)) return null;
  const disabled = busy || !!error;
  const fps = state?.settings.find(setting => setting.id === "frame_limit" && setting.supported);
  const wantedFps: SettingValue | undefined = fpsMode === "default" ? "default" : fpsMode === "off" ? 0 : fpsMode === "limit" ? fpsDraft : undefined;
  const fpsChanged = fps && wantedFps !== undefined && confirmed(fps) !== wantedFps;
  const label = (setting: NvidiaSetting) => {
    if (setting.default) return copy.driverDefault;
    if (setting.id === "frame_limit" && !setting.custom) return setting.value === 0 ? copy.off : `${setting.value} FPS`;
    if (!setting.custom && setting.id === "vsync") {
      if (setting.value === NVIDIA_VSYNC.game) return copy.game;
      if (setting.value === NVIDIA_VSYNC.off) return copy.off;
      if (setting.value === NVIDIA_VSYNC.on) return copy.on;
    }
    if (!setting.custom && setting.id === "power" && setting.value !== null) return setting.value === 1 ? copy.maximum : copy.powerCurrent[setting.value]
      ?? `${copy.current} · 0x${setting.value.toString(16).toUpperCase().padStart(8, "0")}`;
    return `${copy.current} · ${setting.value === null ? "—" : `0x${setting.value.toString(16).toUpperCase().padStart(8, "0")}`}`;
  };
  return <ControlSection id="nvidia" title="NVIDIA" icon={<FaMicrochip />} locale={locale} collapsed={collapsed} onToggle={onToggle}>
    <div className="qsCardBody qsDropdownFullWidth" aria-busy={busy}>
      {fps && <>
        <DropdownItem label={copy.fps} description={`${copy.current}: ${label(fps)}`} layout="below" bottomSeparator="none"
          selectedOption={fpsMode} strDefaultLabel={label(fps)}
          rgOptions={[{ data: "default", label: copy.driverDefault }, ...fps.editable && !fps.custom ? [{ data: "off", label: copy.off }, { data: "limit", label: `${fps.min}–${fps.max} FPS` }] : []]}
          disabled={disabled} onChange={({ data }) => {
            if (disabled || !["default", ...fps.editable && !fps.custom ? ["off", "limit"] : []].includes(data)) return;
            editing.current = true; setFpsMode(data as FpsMode);
          }} />
        {fpsMode === "limit" && fps.editable && !fps.custom && <SliderField label={copy.fps} min={fps.min!} max={fps.max!} step={1}
          value={fpsDraft} valueSuffix=" FPS" showValue editableValue bottomSeparator="none" disabled={disabled}
          onChange={value => { if (!disabled && canWrite(fps, value) && value !== 0) { editing.current = true; setFpsDraft(value); } }} />}
        {fpsChanged && <DialogButton disabled={disabled || !canWrite(fps, wantedFps!)} onClick={() => void request("frame_limit", wantedFps)}>{copy.apply}</DialogButton>}
      </>}
      {(["vsync", "power"] as const).map(id => {
        const setting = state?.settings.find(item => item.id === id && item.supported);
        if (!setting) return null;
        const options = [{ data: "default" as SettingValue, label: copy.driverDefault },
          ...setting.editable && !setting.custom ? numericOptions(setting).map(value => ({ data: value, label: id === "power" ? copy.maximum
            : value === NVIDIA_VSYNC.game ? copy.game : value === NVIDIA_VSYNC.off ? copy.off : copy.on })) : []];
        return <DropdownItem key={id} label={id === "vsync" ? copy.vsync : copy.power} layout="below" bottomSeparator="none"
          selectedOption={confirmed(setting)} strDefaultLabel={label(setting)} renderButtonValue={() => label(setting)}
          rgOptions={options} disabled={disabled} onChange={({ data }) => void request(id, data)} />;
      })}
      {error && <div className="qsMeta" role="status">{copy[error]}</div>}
      {error && <DialogButton disabled={busy} onClick={() => void request()}>{common.retry}</DialogButton>}
    </div>
  </ControlSection>;
}

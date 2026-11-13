import { ButtonItem, DropdownItem, SliderField } from "@decky/ui";
import { useEffect, useRef, useState } from "react";
import { call } from "../controlBackend";
import { controlLocale } from "../controlCenterLocale";
import { deviceInfoLocale, deviceLanguage } from "../deviceInfoLocale";
import { handheldPerformanceModeName } from "./HandheldPowerMode";

const words = {
  en: ["Sustained power", "Short boost", "Peak boost", "Apply", "Restore previous limits"],
  it: ["Potenza continua", "Boost breve", "Boost di picco", "Applica", "Ripristina i limiti precedenti"],
  de: ["Dauerleistung", "Kurzer Boost", "Spitzenleistung", "Anwenden", "Vorherige Grenzwerte wiederherstellen"],
  es: ["Potencia sostenida", "Boost breve", "Potencia máxima", "Aplicar", "Restaurar los límites anteriores"],
  fr: ["Puissance soutenue", "Boost court", "Puissance de pointe", "Appliquer", "Rétablir les limites précédentes"],
  pt: ["Potência contínua", "Boost breve", "Potência de pico", "Aplicar", "Restaurar os limites anteriores"],
  ru: ["Постоянная мощность", "Короткий boost", "Пиковая мощность", "Применить", "Восстановить предыдущие лимиты"],
  uk: ["Постійна потужність", "Короткий boost", "Пікова потужність", "Застосувати", "Відновити попередні ліміти"],
  ja: ["持続電力", "短時間ブースト", "ピーク電力", "適用", "以前の制限に戻す"],
  ko: ["지속 전력", "단기 부스트", "최대 부스트", "적용", "이전 제한 복원"],
  zh: ["持续功率", "短时睿频", "峰值睿频", "应用", "恢复之前的功率限制"],
  hi: ["निरंतर पावर", "छोटा बूस्ट", "पीक बूस्ट", "लागू करें", "पिछली सीमाएँ बहाल करें"],
} as const;
const rails = ["spl", "sppt", "fppt"] as const;
type Rail = typeof rails[number];
type Limits = Record<Rail, number>;
type Bound = { minimum: number; maximum: number; step: number; default: number };
type State = { ok: true; available: true; provider: "lenovo-other-tdp"; token: string; deviceToken: string;
  nativeUnit: "W"; modeControlAvailable: boolean;
  mode: number; realMode: number;
  externalPower: boolean | null; writable: boolean; customModeAvailable: boolean;
  restoreAvailable: boolean; manualRecoveryRequired: boolean } & (
    { tdpAvailable: true; limitsWatts: Limits; boundsWatts: Record<Rail, Bound> }
    | { tdpAvailable: false; limitsWatts: null; boundsWatts: null; writable: false });
type Reply = Partial<Omit<State, "ok" | "available">> & { ok?: boolean; available?: boolean; code?: string };
const integer = (value: unknown, minimum: number, maximum: number): value is number => typeof value === "number" && Number.isInteger(value) && value >= minimum && value <= maximum;
const ordered = (limits: Limits) => limits.spl <= limits.sppt && limits.sppt <= limits.fppt;
const aligned = (value: number, bound: Bound) => integer(value, bound.minimum, bound.maximum) && (value - bound.minimum) % bound.step === 0;
const sameBounds = (first: State, last: State) => first.tdpAvailable === last.tdpAvailable && (!first.tdpAvailable
  || last.tdpAvailable && rails.every(rail =>
    (["minimum", "maximum", "step", "default"] as const).every(key => first.boundsWatts[rail][key] === last.boundsWatts[rail][key])));
export function validLenovoPowerState(value: Reply): value is State {
  if (value?.ok !== true || value.available !== true || value.provider !== "lenovo-other-tdp"
    || value.nativeUnit !== "W"
    || ![value.token, value.deviceToken].every(token => typeof token === "string" && /^[a-f0-9]{64}$/.test(token))
    || !integer(value.mode, 1, 255) || !integer(value.realMode, 1, 255)
    || ![1, 2, 3, 224, 255].includes(value.mode) || ![1, 2, 3, 224, 255].includes(value.realMode)
    || ![value.writable, value.customModeAvailable, value.modeControlAvailable, value.restoreAvailable, value.manualRecoveryRequired].every(flag => typeof flag === "boolean")
    || (typeof value.externalPower !== "boolean" && value.externalPower !== null)) return false;
  if (value.modeControlAvailable && (value.mode !== value.realMode || ![1, 2, 3, 255].includes(value.mode) || typeof value.externalPower !== "boolean")) return false;
  if (value.customModeAvailable && (!value.modeControlAvailable || value.mode !== value.realMode || ![1, 2, 3].includes(value.mode) || typeof value.externalPower !== "boolean" || value.writable)) return false;
  if (value.tdpAvailable === false) return value.boundsWatts === null && value.limitsWatts === null && value.writable === false && !value.restoreAvailable;
  if (value.tdpAvailable !== true || !value.boundsWatts || !value.limitsWatts) return false;
  for (const rail of rails) {
    const bound = value.boundsWatts[rail];
    if (!bound || !integer(bound.minimum, 1, 255) || !integer(bound.maximum, bound.minimum, 255)
      || !integer(bound.step, 1, 255) || !aligned(bound.default, bound) || !integer(value.limitsWatts[rail], 1, 255)) return false;
  }
  return ordered(value.limitsWatts)
    && (!value.modeControlAvailable || value.mode === value.realMode && [1, 2, 3, 255].includes(value.mode) && typeof value.externalPower === "boolean")
    && (!value.customModeAvailable || value.modeControlAvailable === true && value.mode === value.realMode && [1, 2, 3].includes(value.mode)
      && typeof value.externalPower === "boolean" && !value.writable)
    && (!value.writable || value.mode === 255 && value.realMode === 255
    && typeof value.externalPower === "boolean" && !value.manualRecoveryRequired
    && rails.every(rail => aligned(value.limitsWatts![rail], value.boundsWatts![rail])));
}

export function LenovoPowerLimits({ locale }: { locale: string }) {
  const copy = words[deviceLanguage(locale)], common = controlLocale(locale), device = deviceInfoLocale(locale);
  const [state, setState] = useState<State>(), [draft, setDraft] = useState<Limits>();
  const [busy, setBusy] = useState(false), [failed, setFailed] = useState(false);
  const mounted = useRef(false), writing = useRef(false), editing = useRef(false), revision = useRef(0);
  const accept = (value: Reply) => {
    const current = validLenovoPowerState(value) ? value : undefined;
    setState(current); setDraft(current?.tdpAvailable ? { ...current.limitsWatts } : undefined); editing.current = false;
  };
  useEffect(() => {
    mounted.current = true;
    let stopped = false, reading = false;
    const refresh = async () => {
      if (stopped || reading || writing.current || editing.current || document.visibilityState === "hidden") return;
      reading = true;
      const generation = revision.current;
      try {
        const result = await call<[], Reply>("get_lenovo_power_limits");
        if (!stopped && generation === revision.current && !editing.current) {
          accept(result); setFailed(result.manualRecoveryRequired === true);
        }
      } catch { if (!stopped && generation === revision.current) setFailed(true); }
      finally { reading = false; }
    };
    const visible = () => void refresh();
    void refresh(); window.addEventListener("focus", visible); document.addEventListener("visibilitychange", visible);
    return () => {
      stopped = true; mounted.current = false; revision.current++;
      window.removeEventListener("focus", visible); document.removeEventListener("visibilitychange", visible);
    };
  }, []);
  const run = async (action: "apply" | "mode" | "restore" | "refresh", selectedMode?: number) => {
    if (!mounted.current || writing.current || action !== "refresh" && (!state || state.manualRecoveryRequired)
      || action === "mode" && (!state || ![1, 2, 3, 255].includes(selectedMode!) || selectedMode === state.mode || !state.modeControlAvailable)
      || action === "restore" && (!state?.writable || !state.restoreAvailable)
      || action === "apply" && (!state?.tdpAvailable || !state.writable || !draft || !ordered(draft)
        || !rails.every(rail => aligned(draft[rail], state.boundsWatts[rail])))) return;
    const target = draft ? { ...draft } : undefined;
    writing.current = true; revision.current++; setBusy(true); setFailed(false);
    try {
      let result: Reply | undefined;
      if (action !== "refresh") {
        const method = action === "apply" ? "set_lenovo_power_limits" : action === "mode" ? "set_lenovo_performance_mode" : "restore_lenovo_power_limits";
        result = await call<[Record<string, unknown>], Reply>(method,
          { token: state!.token, confirmed: true, ...(action === "apply" ? { limitsWatts: target } : action === "mode" ? { mode: selectedMode } : {}) });
        if (!mounted.current) return;
      }
      const current = await call<[], Reply>("get_lenovo_power_limits");
      if (!mounted.current) return;
      accept(current);
      const valid = validLenovoPowerState(current);
      const confirmed = action === "refresh" || !!result && validLenovoPowerState(result) && valid && current.deviceToken === state!.deviceToken
        && current.token === result.token && current.deviceToken === result.deviceToken
        && current.mode === result.mode && current.realMode === result.realMode && current.externalPower === result.externalPower
        && sameBounds(current, result)
        && (action === "mode" || current.mode === state!.mode && current.realMode === state!.realMode
          && current.externalPower === state!.externalPower && sameBounds(current, state!))
        && (action === "mode" ? current.mode === selectedMode && current.realMode === selectedMode
          : current.tdpAvailable && result.tdpAvailable && rails.every(rail => current.limitsWatts[rail] === result.limitsWatts[rail]
            && (action !== "apply" || current.limitsWatts[rail] === target?.[rail])));
      setFailed(!valid || !confirmed || current.manualRecoveryRequired === true);
    } catch { if (mounted.current) { setState(undefined); setDraft(undefined); setFailed(true); } }
    finally { writing.current = false; if (mounted.current) setBusy(false); }
  };
  if (!state) return failed ? <div className="qsCardBody" aria-busy={busy}>
    <div className="qsMeta" role="status">{common.saveError}</div>
    <ButtonItem layout="below" bottomSeparator="none" disabled={busy} onClick={() => void run("refresh")}>{common.retry}</ButtonItem>
  </div> : null;
  const changed = state.tdpAvailable && !!draft && editing.current && rails.some(rail => draft[rail] !== state.limitsWatts[rail]);
  return <div className="qsCardBody qsDropdownFullWidth" aria-busy={busy}>
    <div className="qsMeta">Lenovo · {device.experimental} · {state.externalPower === null ? common.unavailable : state.externalPower ? device.ac : device.battery}</div>
    <DropdownItem label={device.power} layout="below" bottomSeparator="none" selectedOption={state.mode}
      disabled={busy || state.manualRecoveryRequired || !state.modeControlAvailable}
      rgOptions={[1, 2, 3, 255, ...(state.mode === 224 ? [224] : [])].map(mode => ({ data: mode, label: handheldPerformanceModeName(locale, mode) }))}
      onChange={({ data }) => void run("mode", data)} />
    {state.tdpAvailable && draft && (state.writable ? rails.map((rail, index) => {
      const bound = state.boundsWatts[rail];
      const lower = Math.max(bound.minimum, index > 0 ? draft[rails[index - 1]] : bound.minimum);
      const upper = Math.min(bound.maximum, index < 2 ? draft[rails[index + 1]] : bound.maximum);
      const minimum = bound.minimum + Math.ceil((lower - bound.minimum) / bound.step) * bound.step;
      const maximum = bound.minimum + Math.floor((upper - bound.minimum) / bound.step) * bound.step;
      if (minimum === maximum) return <div key={rail} className="qsMeta">{copy[index]}: {draft[rail]} W</div>;
      return <SliderField key={rail} label={copy[index]} bottomSeparator="none" min={minimum} max={maximum} step={bound.step}
        value={draft[rail]} valueSuffix=" W" showValue editableValue disabled={busy || state.manualRecoveryRequired}
        onChange={value => {
          if (!writing.current && aligned(value, bound) && value >= minimum && value <= maximum) {
            editing.current = true; setDraft(current => current ? { ...current, [rail]: value } : current);
          }
        }} />;
    }) : <>
      {rails.map((rail, index) => <div key={rail} className="qsMeta">{copy[index]}: {state.limitsWatts[rail]} W</div>)}
    </>)}
    {changed && <ButtonItem layout="below" bottomSeparator="none" disabled={busy || !state.writable || state.manualRecoveryRequired}
      onClick={() => void run("apply")}>{copy[3]}</ButtonItem>}
    {state.restoreAvailable && <ButtonItem layout="below" bottomSeparator="none" disabled={busy || !state.writable || state.manualRecoveryRequired}
      onClick={() => void run("restore")}>{copy[4]}</ButtonItem>}
    {(failed || state.manualRecoveryRequired) && <>
      <div className="qsMeta" role="status">{common.saveError}</div>
      <ButtonItem layout="below" bottomSeparator="none" disabled={busy} onClick={() => void run("refresh")}>{common.retry}</ButtonItem>
    </>}
  </div>;
}

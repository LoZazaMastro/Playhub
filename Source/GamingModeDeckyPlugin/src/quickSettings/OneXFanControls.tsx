import { ButtonItem, DropdownItem, SliderField } from "@decky/ui";
import { useEffect, useRef, useState } from "react";
import { call } from "../controlBackend";
import { controlLocale } from "../controlCenterLocale";
import { deviceInfoLocale, deviceLanguage } from "../deviceInfoLocale";

const words = {
  en: ["Fan control", "Automatic", "Manual", "Fan speed", "Apply", "Restore previous settings", "Current"],
  it: ["Controllo ventola", "Automatico", "Manuale", "Velocità ventola", "Applica", "Ripristina le impostazioni precedenti", "Attuale"],
  de: ["Lüftersteuerung", "Automatisch", "Manuell", "Lüftergeschwindigkeit", "Anwenden", "Vorherige Einstellungen wiederherstellen", "Aktuell"],
  es: ["Control del ventilador", "Automático", "Manual", "Velocidad del ventilador", "Aplicar", "Restaurar los ajustes anteriores", "Actual"],
  fr: ["Commande du ventilateur", "Automatique", "Manuel", "Vitesse du ventilateur", "Appliquer", "Rétablir les réglages précédents", "Actuelle"],
  pt: ["Controle da ventoinha", "Automático", "Manual", "Velocidade da ventoinha", "Aplicar", "Restaurar as definições anteriores", "Atual"],
  ru: ["Управление вентилятором", "Автоматически", "Вручную", "Скорость вентилятора", "Применить", "Восстановить предыдущие настройки", "Сейчас"],
  uk: ["Керування вентилятором", "Автоматично", "Вручну", "Швидкість вентилятора", "Застосувати", "Відновити попередні налаштування", "Поточна"],
  ja: ["ファン制御", "自動", "手動", "ファン速度", "適用", "以前の設定に戻す", "現在"],
  ko: ["팬 제어", "자동", "수동", "팬 속도", "적용", "이전 설정 복원", "현재"],
  zh: ["风扇控制", "自动", "手动", "风扇速度", "应用", "恢复之前的设置", "当前"],
  hi: ["पंखा नियंत्रण", "स्वचालित", "मैनुअल", "पंखे की रफ़्तार", "लागू करें", "पिछली सेटिंग बहाल करें", "वर्तमान"],
} as const;

type FanState = { mode: 0 | 1; pwm: number };
type Snapshot = {
  ok: true; available: true; provider: "onex-type2-fan"; token: string; state: FanState;
  telemetry: { fanRpm: number; cpuTemperatureC: number }; restoreAvailable: boolean;
  manualRecoveryRequired: boolean; readbackKind: "firmware-register";
};
type Reply = Partial<Omit<Snapshot, "ok" | "available">> & { ok?: boolean; available?: boolean; code?: string };
export function validOneXFanSnapshot(value: Reply): value is Snapshot {
  return value?.ok === true && value.available === true && value.provider === "onex-type2-fan"
    && typeof value.token === "string" && /^[a-f0-9]{64}$/.test(value.token)
    && value.readbackKind === "firmware-register" && typeof value.restoreAvailable === "boolean"
    && typeof value.manualRecoveryRequired === "boolean" && !!value.state && [0, 1].includes(value.state.mode)
    && Number.isInteger(value.state.pwm) && value.state.pwm >= 0 && value.state.pwm <= 184
    && !!value.telemetry && Number.isInteger(value.telemetry.fanRpm) && value.telemetry.fanRpm >= 0 && value.telemetry.fanRpm <= 65535
    && Number.isInteger(value.telemetry.cpuTemperatureC) && value.telemetry.cpuTemperatureC >= 0 && value.telemetry.cpuTemperatureC <= 255;
}
const sameControl = (left: FanState, right?: FanState) => !!right && left.mode === right.mode && (left.mode === 0 || left.pwm === right.pwm);

export function OneXFanControls({ locale }: { locale: string }) {
  const copy = words[deviceLanguage(locale)], common = controlLocale(locale), device = deviceInfoLocale(locale);
  const [state, setState] = useState<Snapshot>();
  const [mode, setMode] = useState<0 | 1>(0), [percent, setPercent] = useState(50);
  const [busy, setBusy] = useState(false), [failed, setFailed] = useState(false);
  const mounted = useRef(false), writing = useRef(false), editing = useRef(false), revision = useRef(0);
  const polling = useRef<(available: boolean) => void>(() => {});
  const accept = (reply: Reply) => {
    const current = validOneXFanSnapshot(reply) ? reply : undefined;
    setState(current);
    if (current) {
      setMode(current.state.mode);
      setPercent(Math.max(30, Math.min(100, Math.round(current.state.pwm * 100 / 184))));
    }
    editing.current = false;
  };
  useEffect(() => {
    mounted.current = true;
    let stopped = false, reading = false, supported = false, timer: number | undefined;
    const schedule = () => {
      window.clearTimeout(timer);
      if (!stopped && supported) {
        timer = window.setTimeout(() => void refresh(), 3000);
      }
    };
    polling.current = available => { supported = available; schedule(); };
    const refresh = async () => {
      if (stopped || reading) return;
      window.clearTimeout(timer);
      if (writing.current || editing.current || document.visibilityState === "hidden") { schedule(); return; }
      reading = true;
      const generation = revision.current;
      try {
        const result = await call<[], Reply>("get_onex_fan_controls");
        if (!stopped && generation === revision.current && !editing.current) {
          supported = validOneXFanSnapshot(result);
          accept(result); setFailed(result.manualRecoveryRequired === true);
        }
      } catch { if (!stopped && generation === revision.current) setFailed(true); }
      finally { reading = false; schedule(); }
    };
    const visible = () => void refresh();
    void refresh();
    window.addEventListener("focus", visible); document.addEventListener("visibilitychange", visible);
    return () => {
      stopped = true; mounted.current = false; revision.current++;
      polling.current = () => {};
      window.clearTimeout(timer);
      window.removeEventListener("focus", visible); document.removeEventListener("visibilitychange", visible);
    };
  }, []);
  const run = async (action: "apply" | "restore" | "refresh") => {
    if (!mounted.current || writing.current || (action !== "refresh" && (!state || state.manualRecoveryRequired))) return;
    const target = { mode, pwm: Math.floor((percent * 184 + 50) / 100) };
    writing.current = true; revision.current++; setBusy(true); setFailed(false);
    try {
      let result: Reply | undefined;
      if (action !== "refresh") {
        result = await call<[Record<string, string | number | boolean>], Reply>(
          action === "restore" ? "restore_onex_fan_controls" : "set_onex_fan_controls",
          action === "restore" ? { token: state!.token, confirmed: true } : { token: state!.token, mode, percent, confirmed: true });
        if (!mounted.current) return;
      }
      const current = await call<[], Reply>("get_onex_fan_controls");
      if (!mounted.current) return;
      accept(current);
      const valid = validOneXFanSnapshot(current);
      polling.current(valid);
      const confirmed = action === "refresh" || (result?.ok === true && valid && current.token === state!.token
        && sameControl(current.state, result.state) && (action === "restore" || sameControl(current.state, target)));
      setFailed(!valid || !confirmed || current.manualRecoveryRequired === true);
    } catch { if (mounted.current) { setState(undefined); setFailed(true); polling.current(false); } }
    finally { writing.current = false; if (mounted.current) setBusy(false); }
  };
  if (!state) return failed ? <div className="qsCardBody" aria-busy={busy}>
    <div className="qsMeta" role="status">{common.saveError}</div>
    <ButtonItem layout="below" bottomSeparator="none" disabled={busy} onClick={() => void run("refresh")}>{common.retry}</ButtonItem>
  </div> : null;
  const changed = editing.current && (mode !== state.state.mode || mode === 1 && Math.floor((percent * 184 + 50) / 100) !== state.state.pwm);
  const rpm = state.telemetry.fanRpm <= 20000 ? `${state.telemetry.fanRpm} RPM` : "";
  const temperature = state.telemetry.cpuTemperatureC >= 1 && state.telemetry.cpuTemperatureC <= 125 ? `CPU ${state.telemetry.cpuTemperatureC} °C` : "";
  return <div className="qsCardBody qsDropdownFullWidth" aria-busy={busy}>
    <DropdownItem label={copy[0]} description={`ONEXPLAYER · ${device.experimental}`} layout="below" bottomSeparator="none"
      selectedOption={mode} disabled={busy || state.manualRecoveryRequired}
      rgOptions={[0, 1].map(value => ({ data: value, label: copy[value + 1] }))}
      onChange={({ data }) => {
        if (!writing.current && (data === 0 || data === 1)) { editing.current = true; setMode(data); }
      }} />
    <div className="qsMeta">{copy[6]}: {state.state.mode === 0 ? copy[1] : `${Math.round(state.state.pwm * 100 / 184)}%`}
      {[rpm, temperature].filter(Boolean).map(value => <span key={value}> · {value}</span>)}</div>
    {mode === 1 && <SliderField label={copy[3]} bottomSeparator="none" min={30} max={100} step={1} value={percent}
      valueSuffix="%" showValue editableValue disabled={busy || state.manualRecoveryRequired}
      onChange={value => {
        if (!writing.current && Number.isInteger(value) && value >= 30 && value <= 100) { editing.current = true; setPercent(value); }
      }} />}
    {changed && <ButtonItem layout="below" bottomSeparator="none" disabled={busy || state.manualRecoveryRequired}
      onClick={() => void run("apply")}>{copy[4]}</ButtonItem>}
    {state.restoreAvailable && <ButtonItem layout="below" bottomSeparator="none" disabled={busy || state.manualRecoveryRequired}
      onClick={() => void run("restore")}>{copy[5]}</ButtonItem>}
    {(failed || state.manualRecoveryRequired) && <>
      <div className="qsMeta" role="status">{common.saveError}</div>
      <ButtonItem layout="below" bottomSeparator="none" disabled={busy} onClick={() => void run("refresh")}>{common.retry}</ButtonItem>
    </>}
  </div>;
}

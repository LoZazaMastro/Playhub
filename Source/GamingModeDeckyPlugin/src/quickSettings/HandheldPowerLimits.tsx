import { ButtonItem, SliderField } from "@decky/ui";
import { useEffect, useRef, useState } from "react";
import { call } from "../controlBackend";
import { controlLocale } from "../controlCenterLocale";
import { deviceInfoLocale, deviceLanguage } from "../deviceInfoLocale";

const words = {
  en: ["Power limit", "Current", "Apply", "Restore previous settings"],
  it: ["Limite di potenza", "Attuale", "Applica", "Ripristina le impostazioni precedenti"],
  de: ["Leistungsgrenze", "Aktuell", "Anwenden", "Vorherige Einstellungen wiederherstellen"],
  es: ["Límite de potencia", "Actual", "Aplicar", "Restaurar los ajustes anteriores"],
  fr: ["Limite de puissance", "Actuelle", "Appliquer", "Rétablir les réglages précédents"],
  pt: ["Limite de potência", "Atual", "Aplicar", "Restaurar as definições anteriores"],
  ru: ["Лимит мощности", "Сейчас", "Применить", "Восстановить предыдущие настройки"],
  uk: ["Ліміт потужності", "Поточний", "Застосувати", "Відновити попередні налаштування"],
  ja: ["電力上限", "現在", "適用", "以前の設定に戻す"],
  ko: ["전력 제한", "현재", "적용", "이전 설정 복원"],
  zh: ["功率上限", "当前", "应用", "恢复之前的设置"],
  hi: ["पावर सीमा", "वर्तमान", "लागू करें", "पिछली सेटिंग बहाल करें"],
} as const;
const managedBy = {
  en: "Managed by another performance app", it: "Gestito da un’altra app per le prestazioni", de: "Von einer anderen Leistungs-App verwaltet",
  es: "Gestionado por otra aplicación de rendimiento", fr: "Géré par une autre application de performances", pt: "Gerenciado por outro aplicativo de desempenho",
  ru: "Управляется другим приложением производительности", uk: "Керується іншим застосунком продуктивності", ja: "別のパフォーマンスアプリで管理中",
  ko: "다른 성능 앱에서 관리 중", zh: "由其他性能应用管理", hi: "अन्य प्रदर्शन ऐप द्वारा नियंत्रित",
} as const;

type PowerState = {
  ok: true; available: true; provider: "asus-rc73xa-tdp"; token: string;
  limitsWatts: { spl: number; sppt: number; fppt: number };
  minimumWatts: number; maximumWatts: number; externalPower: boolean;
  readbackKind: "firmware-register"; restoreAvailable: boolean; manualRecoveryRequired: boolean;
  writeAvailable: boolean; oemControllerActive: boolean;
};
type Reply = Partial<Omit<PowerState, "ok" | "available">> & { ok?: boolean; available?: boolean; code?: string };
export function validHandheldPowerState(value: Reply): value is PowerState {
  const limits = value?.limitsWatts;
  return value?.ok === true && value.available === true && value.provider === "asus-rc73xa-tdp"
    && typeof value.token === "string" && /^[a-f0-9]{64}$/.test(value.token)
    && value.readbackKind === "firmware-register" && typeof value.externalPower === "boolean"
    && typeof value.restoreAvailable === "boolean" && typeof value.manualRecoveryRequired === "boolean"
    && typeof value.writeAvailable === "boolean" && typeof value.oemControllerActive === "boolean"
    && (!value.writeAvailable || !value.oemControllerActive && !value.manualRecoveryRequired)
    && value.minimumWatts === 7 && value.maximumWatts === (value.externalPower ? 35 : 25)
    && !!limits && [limits.spl, limits.sppt, limits.fppt].every(v => Number.isInteger(v) && v >= 1 && v <= 255)
    && limits.spl <= limits.sppt && limits.sppt <= limits.fppt;
}

export function HandheldPowerLimits({ locale }: { locale: string }) {
  const copy = words[deviceLanguage(locale)];
  const device = deviceInfoLocale(locale), common = controlLocale(locale);
  const [state, setState] = useState<PowerState>();
  const [draft, setDraft] = useState<number>();
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);
  const mounted = useRef(false), writing = useRef(false), editing = useRef(false);
  const revision = useRef(0);
  const accept = (value: Reply) => {
    const current = validHandheldPowerState(value) ? value : undefined;
    setState(current);
    setDraft(current ? Math.max(current.minimumWatts, Math.min(current.maximumWatts, current.limitsWatts.spl)) : undefined);
    editing.current = false;
  };
  useEffect(() => {
    mounted.current = true;
    let stopped = false, reading = false;
    const refresh = async () => {
      if (stopped || reading || writing.current || editing.current || document.visibilityState === "hidden") return;
      reading = true;
      const expected = revision.current;
      try {
        const result = await call<[], Reply>("get_handheld_tdp");
        if (!stopped && expected === revision.current && !editing.current) {
          accept(result);
          setFailed(result.manualRecoveryRequired === true);
        }
      } catch {
        if (!stopped && expected === revision.current) setFailed(true);
      }
      finally { reading = false; }
    };
    void refresh();
    const visible = () => void refresh();
    window.addEventListener("focus", visible);
    document.addEventListener("visibilitychange", visible);
    return () => {
      stopped = true; mounted.current = false; revision.current++;
      window.removeEventListener("focus", visible);
      document.removeEventListener("visibilitychange", visible);
    };
  }, []);

  const write = async (restore: boolean) => {
    if (!mounted.current || !state || writing.current || !state.writeAvailable || state.manualRecoveryRequired || (!restore && !Number.isInteger(draft))) return;
    const targetWatts = draft;
    writing.current = true; revision.current++; setBusy(true); setFailed(false);
    try {
      const result = await call<[Record<string, string | number | boolean>], Reply>(
        restore ? "restore_handheld_tdp" : "set_handheld_tdp",
        restore ? { token: state.token, confirmed: true } : { token: state.token, watts: targetWatts!, confirmed: true });
      if (!mounted.current) return;
      const current = await call<[], Reply>("get_handheld_tdp");
      if (!mounted.current) return;
      accept(current);
      const confirmedLimits = result.limitsWatts;
      const matches = validHandheldPowerState(current) && current.token === state.token
        && !!confirmedLimits && ["spl", "sppt", "fppt"].every(key => {
          const part = key as keyof PowerState["limitsWatts"];
          return current.limitsWatts[part] === confirmedLimits[part]
            && (restore || current.limitsWatts[part] === targetWatts);
        });
      setFailed(result.ok !== true || !matches || current.manualRecoveryRequired === true);
    } catch { if (mounted.current) { setState(undefined); setDraft(undefined); setFailed(true); } }
    finally { writing.current = false; if (mounted.current) setBusy(false); }
  };
  const retry = async () => {
    if (!mounted.current || writing.current) return;
    writing.current = true; revision.current++; setBusy(true);
    try {
      const current = await call<[], Reply>("get_handheld_tdp");
      if (!mounted.current) return;
      accept(current);
      setFailed(!validHandheldPowerState(current) || current.manualRecoveryRequired);
    } catch { if (mounted.current) setFailed(true); }
    finally { writing.current = false; if (mounted.current) setBusy(false); }
  };
  if (!state) return failed ? <div className="qsCardBody" aria-busy={busy}>
    <div className="qsMeta" role="status">{common.saveError}</div>
    <ButtonItem layout="below" bottomSeparator="none" disabled={busy} onClick={() => void retry()}>{common.retry}</ButtonItem>
  </div> : null;
  const changed = editing.current && [state.limitsWatts.spl, state.limitsWatts.sppt, state.limitsWatts.fppt].some(value => value !== draft);
  return <div className="qsCardBody qsDropdownFullWidth" aria-busy={busy}>
    <SliderField bottomSeparator="none" label={copy[0]}
      description={`${copy[1]}: ${state.limitsWatts.spl} W · ${state.externalPower ? device.ac : device.battery} · ${device.experimental}`}
      min={state.minimumWatts} max={state.maximumWatts} step={1} value={draft ?? state.limitsWatts.spl}
      valueSuffix=" W" showValue editableValue disabled={busy || !state.writeAvailable || state.manualRecoveryRequired}
      onChange={value => {
        if (writing.current || !Number.isInteger(value) || value < state.minimumWatts || value > state.maximumWatts) return;
        editing.current = true; setDraft(value);
      }} />
    {state.oemControllerActive && <div className="qsMeta" role="status">{managedBy[deviceLanguage(locale)]}</div>}
    {changed && <ButtonItem layout="below" bottomSeparator="none" disabled={busy || !state.writeAvailable || state.manualRecoveryRequired}
      onClick={() => void write(false)}>{copy[2]}</ButtonItem>}
    {state.restoreAvailable && <ButtonItem layout="below" bottomSeparator="none" disabled={busy || !state.writeAvailable || state.manualRecoveryRequired}
      onClick={() => void write(true)}>{copy[3]}</ButtonItem>}
    {(failed || state.manualRecoveryRequired) && <div className="qsMeta" role="status">{common.saveError}</div>}
    {(failed || state.manualRecoveryRequired) && <ButtonItem layout="below" bottomSeparator="none" disabled={busy}
      onClick={() => void retry()}>{common.retry}</ButtonItem>}
  </div>;
}

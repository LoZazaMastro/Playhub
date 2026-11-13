import { DropdownItem } from "@decky/ui";
import { useEffect, useRef, useState } from "react";
import { call } from "../controlBackend";
import { controlLocale } from "../controlCenterLocale";
import { deviceInfoLocale } from "../deviceInfoLocale";

const translations = {
  en: ["CPU boost", "Extra CPU speed and power use.", "Off", "On", "Maximum", "Efficient", "Efficient maximum", "Windows preset"],
  it: ["Boost CPU", "Velocità aggiuntiva della CPU e consumi.", "Disattivato", "Attivato", "Massimo", "Efficiente", "Massimo efficiente", "Profilo Windows"],
  de: ["CPU-Boost", "Zusätzliche CPU-Leistung und Energieverbrauch.", "Aus", "Ein", "Maximum", "Effizient", "Effizientes Maximum", "Windows-Vorgabe"],
  es: ["Turbo de CPU", "Velocidad adicional de CPU y consumo.", "Desactivado", "Activado", "Máximo", "Eficiente", "Máximo eficiente", "Perfil de Windows"],
  fr: ["Boost du processeur", "Vitesse supplémentaire du processeur et consommation.", "Désactivé", "Activé", "Maximum", "Efficace", "Maximum efficace", "Profil Windows"],
  pt: ["Boost da CPU", "Velocidade adicional da CPU e consumo.", "Desativado", "Ativado", "Máximo", "Eficiente", "Máximo eficiente", "Perfil do Windows"],
  ru: ["Ускорение процессора", "Дополнительная скорость процессора и энергопотребление.", "Выкл.", "Вкл.", "Максимум", "Эффективный", "Эффективный максимум", "Профиль Windows"],
  uk: ["Прискорення процесора", "Додаткова швидкість процесора та енергоспоживання.", "Вимкнено", "Увімкнено", "Максимум", "Ефективний", "Ефективний максимум", "Профіль Windows"],
  ja: ["CPU ブースト", "CPU の追加性能と消費電力。", "オフ", "オン", "最大", "効率優先", "効率を考慮した最大", "Windows の設定"],
  ko: ["CPU 부스트", "CPU의 추가 성능과 전력 사용량.", "꺼짐", "켜짐", "최대", "효율 우선", "효율적인 최대 성능", "Windows 설정"],
  zh: ["CPU 睿频", "CPU 额外性能和功耗。", "关闭", "开启", "最高性能", "节能", "节能最高性能", "Windows 预设"],
  hi: ["CPU बूस्ट", "CPU की अतिरिक्त गति और बिजली का उपयोग।", "बंद", "चालू", "अधिकतम", "कुशल", "कुशल अधिकतम", "Windows प्रीसेट"],
} as const;

type BoostState = { available: true; plan: string; source: "ac" | "battery"; mode: number; modes: number[] };
type Response = (BoostState | { available?: false }) & { ok?: boolean; code?: string };

export function ProcessorBoost({ locale }: { locale: string }) {
  const key = locale.toLowerCase().split(/[-_]/)[0] as keyof typeof translations;
  const copy = translations[key] ?? translations.en;
  const [state, setState] = useState<BoostState>();
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);
  const mounted = useRef(false);
  const writing = useRef(false);
  const revision = useRef(0);

  useEffect(() => {
    mounted.current = true;
    let stopped = false, reading = false;
    const refresh = async () => {
      if (stopped || reading || writing.current || document.visibilityState === "hidden") return;
      reading = true;
      const observedRevision = revision.current;
      try {
        const result = await call<[], Response>("get_processor_boost");
        if (!stopped && revision.current === observedRevision) setState(result.available ? result : undefined);
      } catch { /* Preserve the last confirmed policy through a transient RPC failure. */ }
      finally { reading = false; }
    };
    void refresh();
    const timer = window.setInterval(() => void refresh(), 15_000);
    const onVisible = () => void refresh();
    window.addEventListener("focus", onVisible);
    document.addEventListener("visibilitychange", onVisible);
    return () => {
      stopped = true;
      mounted.current = false;
      window.clearInterval(timer);
      window.removeEventListener("focus", onVisible);
      document.removeEventListener("visibilitychange", onVisible);
    };
  }, []);

  if (!state) return null;
  const device = deviceInfoLocale(locale);
  const supply = state.source === "ac" ? device.ac : device.battery;
  const modes = state.modes.filter(mode => Number.isInteger(mode) && mode >= 0 && mode <= 6 && (mode <= 4 || mode === state.mode));
  const choose = async (mode: number) => {
    if (writing.current || !modes.includes(mode) || mode === state.mode) return;
    revision.current++;
    writing.current = true;
    setBusy(true);
    setFailed(false);
    try {
      const result = await call<[{ plan: string; source: "ac" | "battery"; mode: number }], Response>("set_processor_boost", {
        plan: state.plan, source: state.source, mode,
      });
      if (!mounted.current) return;
      if (result.available) setState(result);
      else if (result.code === "unavailable") setState(undefined);
      setFailed(result.ok !== true);
    } catch { if (mounted.current) setFailed(true); }
    finally {
      writing.current = false;
      if (mounted.current) setBusy(false);
    }
  };

  return <div className="qsCardBody qsDropdownFullWidth" aria-busy={busy}>
    <DropdownItem label={copy[0]} description={`${supply} · ${copy[1]}`} bottomSeparator="none" layout="below"
      rgOptions={modes.map(mode => ({ data: mode, label: mode <= 4 ? copy[mode + 2] : copy[7] }))}
      selectedOption={state.mode} disabled={busy} onChange={({ data }) => void choose(data)} />
    {failed && <div className="qsMeta" role="status">{controlLocale(locale).saveError}</div>}
  </div>;
}

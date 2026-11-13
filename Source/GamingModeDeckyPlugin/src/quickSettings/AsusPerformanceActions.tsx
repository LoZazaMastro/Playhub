import { DialogButton } from "@decky/ui";
import { useEffect, useRef, useState } from "react";
import { call } from "../controlBackend";
import { controlLocale } from "../controlCenterLocale";
import { deviceInfoLocale } from "../deviceInfoLocale";

const translations = {
  en: ["Device performance", "ASUS firmware", "Silent", "Balanced", "Turbo"],
  it: ["Prestazioni dispositivo", "Firmware ASUS", "Silenzioso", "Bilanciato", "Turbo"],
  de: ["Geräteleistung", "ASUS-Firmware", "Leise", "Ausgewogen", "Turbo"],
  es: ["Rendimiento del dispositivo", "Firmware de ASUS", "Silencioso", "Equilibrado", "Turbo"],
  fr: ["Performances de l’appareil", "Firmware ASUS", "Silencieux", "Équilibré", "Turbo"],
  pt: ["Desempenho do dispositivo", "Firmware ASUS", "Silencioso", "Equilibrado", "Turbo"],
  ru: ["Производительность устройства", "Прошивка ASUS", "Тихий", "Сбалансированный", "Турбо"],
  uk: ["Продуктивність пристрою", "Прошивка ASUS", "Тихий", "Збалансований", "Турбо"],
  ja: ["デバイスのパフォーマンス", "ASUS ファームウェア", "静音", "バランス", "ターボ"],
  ko: ["장치 성능", "ASUS 펌웨어", "조용하게", "균형", "터보"],
  zh: ["设备性能", "ASUS 固件", "安静", "均衡", "增强"],
  hi: ["डिवाइस का प्रदर्शन", "ASUS फ़र्मवेयर", "शांत", "संतुलित", "टर्बो"],
} as const;

type Capability = { available: true; token: string; commands: number[] };
type Response = (Capability | { available?: false }) & { ok?: boolean; code?: string; acknowledged?: boolean };

export function AsusPerformanceActions({ locale }: { locale: string }) {
  const key = locale.toLowerCase().split(/[-_]/)[0] as keyof typeof translations;
  const copy = translations[key] ?? translations.en;
  const [state, setState] = useState<Capability>();
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
        const result = await call<[], Response>("get_asus_performance_commands");
        if (!stopped && revision.current === observedRevision) setState(result.available ? result : undefined);
      } catch { /* Keep verified command capability through a transient RPC failure. */ }
      finally { reading = false; }
    };
    void refresh();
    const onVisible = () => void refresh();
    window.addEventListener("focus", onVisible);
    document.addEventListener("visibilitychange", onVisible);
    return () => {
      stopped = true;
      mounted.current = false;
      window.removeEventListener("focus", onVisible);
      document.removeEventListener("visibilitychange", onVisible);
    };
  }, []);

  if (!state) return null;
  const apply = async (mode: number) => {
    if (writing.current || ![2, 0, 1].includes(mode) || !state.commands.includes(mode)) return;
    revision.current++;
    writing.current = true;
    setBusy(true);
    setFailed(false);
    try {
      const result = await call<[{ token: string; mode: number }], Response>("apply_asus_performance_command", { token: state.token, mode });
      if (!mounted.current) return;
      if (result.available) setState(result);
      else if (result.code === "unavailable") setState(undefined);
      setFailed(result.ok !== true || result.acknowledged !== true);
    } catch { if (mounted.current) setFailed(true); }
    finally {
      writing.current = false;
      if (mounted.current) setBusy(false);
    }
  };

  return <div className="qsCardBody" aria-busy={busy}>
    <div>{copy[0]}</div>
    <div className="qsMeta">{copy[1]} · {deviceInfoLocale(locale).experimental}</div>
    {[2, 0, 1].filter(mode => state.commands.includes(mode)).map(mode => <DialogButton key={mode}
      disabled={busy} onClick={() => void apply(mode)} style={{ whiteSpace: "normal", height: "auto", minHeight: 40 }}>
      {copy[[2, 0, 1].indexOf(mode) + 2]}
    </DialogButton>)}
    {failed && <div className="qsMeta" role="status">{controlLocale(locale).saveError}</div>}
  </div>;
}

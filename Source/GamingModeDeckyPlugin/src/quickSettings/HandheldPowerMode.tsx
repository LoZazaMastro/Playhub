import { DropdownItem } from "@decky/ui";
import { useEffect, useRef, useState } from "react";
import { call } from "../controlBackend";
import { controlLocale } from "../controlCenterLocale";
import { deviceInfoLocale, deviceLanguage } from "../deviceInfoLocale";

const translations = {
  en: ["Device performance", "Lenovo firmware", "Quiet", "Balanced", "Performance", "Custom", "Custom profile managed by Lenovo firmware."],
  it: ["Prestazioni dispositivo", "Firmware Lenovo", "Silenzioso", "Bilanciato", "Prestazioni", "Personalizzato", "Profilo personalizzato gestito dal firmware Lenovo."],
  de: ["Geräteleistung", "Lenovo-Firmware", "Leise", "Ausgewogen", "Leistung", "Benutzerdefiniert", "Benutzerdefiniertes Profil der Lenovo-Firmware."],
  es: ["Rendimiento del dispositivo", "Firmware de Lenovo", "Silencioso", "Equilibrado", "Rendimiento", "Personalizado", "Perfil personalizado gestionado por el firmware de Lenovo."],
  fr: ["Performances de l’appareil", "Firmware Lenovo", "Silencieux", "Équilibré", "Performances", "Personnalisé", "Profil personnalisé géré par le firmware Lenovo."],
  pt: ["Desempenho do dispositivo", "Firmware Lenovo", "Silencioso", "Equilibrado", "Desempenho", "Personalizado", "Perfil personalizado gerenciado pelo firmware Lenovo."],
  ru: ["Производительность устройства", "Прошивка Lenovo", "Тихий", "Сбалансированный", "Производительность", "Свой профиль", "Своим профилем управляет прошивка Lenovo."],
  uk: ["Продуктивність пристрою", "Прошивка Lenovo", "Тихий", "Збалансований", "Продуктивність", "Власний профіль", "Власним профілем керує прошивка Lenovo."],
  ja: ["デバイスのパフォーマンス", "Lenovo ファームウェア", "静音", "バランス", "パフォーマンス", "カスタム", "カスタムプロファイルは Lenovo ファームウェアで管理します。"],
  ko: ["장치 성능", "Lenovo 펌웨어", "조용하게", "균형", "성능", "사용자 지정", "사용자 지정 프로필은 Lenovo 펌웨어에서 관리합니다."],
  zh: ["设备性能", "Lenovo 固件", "安静", "均衡", "性能", "自定义", "自定义配置由 Lenovo 固件管理。"],
  hi: ["डिवाइस का प्रदर्शन", "Lenovo फ़र्मवेयर", "शांत", "संतुलित", "प्रदर्शन", "कस्टम", "कस्टम प्रोफ़ाइल Lenovo फ़र्मवेयर संभालता है।"],
} as const;

type NativeMode = { available: true; mode: number; modes: number[]; editable: boolean; token: string };
type Response = (NativeMode | { available?: false }) & { ok?: boolean; code?: string };

export function handheldPerformanceModeName(locale: string, mode: number): string {
  const copy = translations[deviceLanguage(locale)];
  return mode >= 1 && mode <= 3 ? copy[mode + 1] : mode === 224 ? "Extreme" : copy[5];
}

export function HandheldPowerMode({ locale }: { locale: string }) {
  const key = deviceLanguage(locale);
  const copy = translations[key] ?? translations.en;
  const [state, setState] = useState<NativeMode>();
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
        const result = await call<[], Response>("get_handheld_power_mode");
        if (!stopped && revision.current === observedRevision) setState(result.available ? result : undefined);
      } catch { /* Keep the last verified firmware state through transient RPC errors. */ }
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
  const modes = [...state.modes];
  if (!modes.includes(state.mode)) modes.push(state.mode);
  const choose = async (mode: number) => {
    if (writing.current || !state.editable || !state.modes.includes(mode) || mode === state.mode) return;
    revision.current++;
    writing.current = true;
    setBusy(true);
    setFailed(false);
    try {
      const result = await call<[{ token: string; mode: number; expected: number }], Response>("set_handheld_power_mode", {
        token: state.token, expected: state.mode, mode,
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
    <DropdownItem label={copy[0]} description={`${copy[1]} · ${deviceInfoLocale(locale).experimental}`}
      bottomSeparator="none" layout="below" selectedOption={state.mode} disabled={busy || !state.editable}
      rgOptions={modes.map(mode => ({ data: mode, label: mode >= 1 && mode <= 3 ? copy[mode + 1] : copy[5] }))}
      onChange={({ data }) => void choose(data)} />
    {!state.editable && <div className="qsMeta">{copy[6]}</div>}
    {failed && <div className="qsMeta" role="status">{controlLocale(locale).saveError}</div>}
  </div>;
}

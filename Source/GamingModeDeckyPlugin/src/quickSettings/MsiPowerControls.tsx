import { ButtonItem, DropdownItem, SliderField } from "@decky/ui";
import { useEffect, useRef, useState } from "react";
import { call } from "../controlBackend";
import { controlLocale } from "../controlCenterLocale";
import { deviceInfoLocale, deviceLanguage } from "../deviceInfoLocale";

const translations = {
  en: ["Device performance", "MSI firmware", "AI Engine", "Endurance", "Manual", "Sustained power", "Boost power", "Apply", "Manual gives you control over sustained and boost power."],
  it: ["Prestazioni dispositivo", "Firmware MSI", "AI Engine", "Autonomia", "Manuale", "Potenza continua", "Potenza boost", "Applica", "La modalità manuale permette di regolare la potenza continua e il boost."],
  de: ["Geräteleistung", "MSI-Firmware", "AI Engine", "Ausdauer", "Manuell", "Dauerleistung", "Boost-Leistung", "Anwenden", "Im manuellen Modus regelst du Dauer- und Boost-Leistung."],
  es: ["Rendimiento del dispositivo", "Firmware de MSI", "AI Engine", "Autonomía", "Manual", "Potencia sostenida", "Potencia boost", "Aplicar", "El modo manual permite ajustar la potencia sostenida y el boost."],
  fr: ["Performances de l’appareil", "Firmware MSI", "AI Engine", "Autonomie", "Manuel", "Puissance soutenue", "Puissance boost", "Appliquer", "Le mode manuel permet de régler la puissance soutenue et le boost."],
  pt: ["Desempenho do dispositivo", "Firmware MSI", "AI Engine", "Autonomia", "Manual", "Potência contínua", "Potência boost", "Aplicar", "O modo manual permite ajustar a potência contínua e o boost."],
  ru: ["Производительность устройства", "Прошивка MSI", "AI Engine", "Автономность", "Вручную", "Постоянная мощность", "Мощность boost", "Применить", "Ручной режим позволяет настроить постоянную мощность и boost."],
  uk: ["Продуктивність пристрою", "Прошивка MSI", "AI Engine", "Автономність", "Вручну", "Постійна потужність", "Потужність boost", "Застосувати", "Ручний режим дає змогу налаштувати постійну потужність і boost."],
  ja: ["デバイスのパフォーマンス", "MSI ファームウェア", "AI Engine", "長時間駆動", "手動", "持続電力", "ブースト電力", "適用", "手動モードで持続電力とブースト電力を調整できます。"],
  ko: ["장치 성능", "MSI 펌웨어", "AI Engine", "배터리 절약", "수동", "지속 전력", "부스트 전력", "적용", "수동 모드에서 지속 전력과 부스트 전력을 조절할 수 있습니다."],
  zh: ["设备性能", "MSI 固件", "AI Engine", "续航", "手动", "持续功率", "睿频功率", "应用", "手动模式可调节持续功率和睿频功率。"],
  hi: ["डिवाइस का प्रदर्शन", "MSI फ़र्मवेयर", "AI Engine", "बैटरी बचत", "मैनुअल", "निरंतर पावर", "बूस्ट पावर", "लागू करें", "मैनुअल मोड में निरंतर और बूस्ट पावर चुन सकते हैं।"],
} as const;

type NativePower = { available: true; mode: number; token: string; tdpAvailable: boolean; pl1?: number; pl2?: number };
type Response = (NativePower | { available?: false }) & { ok?: boolean; code?: string };
type Limits = { pl1: number; pl2: number };

export function MsiPowerControls({ locale }: { locale: string }) {
  const key = deviceLanguage(locale);
  const copy = translations[key] ?? translations.en;
  const [state, setState] = useState<NativePower>();
  const [draft, setDraft] = useState<Limits>({ pl1: 17, pl2: 19 });
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);
  const mounted = useRef(false);
  const writing = useRef(false);
  const editing = useRef(false);
  const revision = useRef(0);
  const accept = (result: Response) => {
    setState(result.available ? result : undefined);
    if (result.available && result.tdpAvailable && result.pl1 !== undefined && result.pl2 !== undefined)
      setDraft({ pl1: result.pl1, pl2: result.pl2 });
    editing.current = false;
  };

  useEffect(() => {
    mounted.current = true;
    let stopped = false, reading = false;
    const refresh = async () => {
      if (stopped || reading || writing.current || editing.current || document.visibilityState === "hidden") return;
      reading = true;
      const observedRevision = revision.current;
      try {
        const result = await call<[], Response>("get_msi_claw_firmware");
        if (!stopped && revision.current === observedRevision && !editing.current) accept(result);
      } catch { /* Preserve the last verified device state through a transient RPC error. */ }
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
  const write = async (method: string, request: Record<string, string | number>) => {
    if (writing.current) return;
    revision.current++;
    writing.current = true;
    setBusy(true);
    setFailed(false);
    try {
      const result = await call<[Record<string, string | number>], Response>(method, request);
      if (!mounted.current) return;
      if (result.available || result.code === "unavailable") accept(result);
      setFailed(result.ok !== true);
    } catch { if (mounted.current) setFailed(true); }
    finally {
      writing.current = false;
      if (mounted.current) setBusy(false);
    }
  };
  const chooseMode = (mode: number) => {
    if (![1, 2, 6].includes(mode) || mode === state.mode) return;
    void write("set_msi_claw_power_mode", { token: state.token, expectedMode: state.mode, mode });
  };
  const changeLimit = (which: keyof Limits, value: number) => {
    if (writing.current || state.mode !== 6 || !state.tdpAvailable || !Number.isInteger(value)) return;
    editing.current = true;
    if (which === "pl1") {
      const pl1 = Math.max(8, Math.min(35, value));
      setDraft(current => ({ pl1, pl2: Math.max(current.pl2, pl1 + 2) }));
    } else {
      const pl2 = Math.max(10, Math.min(45, value));
      setDraft(current => ({ pl1: Math.min(current.pl1, pl2 - 2), pl2 }));
    }
  };
  const apply = () => {
    if (!state.tdpAvailable || state.mode !== 6 || state.pl1 === undefined || state.pl2 === undefined) return;
    void write("set_msi_claw_power_limits", { token: state.token, expectedMode: state.mode,
      expectedPl1: state.pl1, expectedPl2: state.pl2, ...draft });
  };
  const manual = state.mode === 6 && state.tdpAvailable;
  const changed = draft.pl1 !== state.pl1 || draft.pl2 !== state.pl2;

  return <div className="qsCardBody qsDropdownFullWidth" aria-busy={busy}>
    <DropdownItem label={copy[0]} description={`${copy[1]} · ${deviceInfoLocale(locale).experimental}`}
      bottomSeparator="none" layout="below" selectedOption={state.mode} disabled={busy}
      rgOptions={[1, 2, 6].map((mode, index) => ({ data: mode, label: copy[index + 2] }))}
      onChange={({ data }) => chooseMode(data)} />
    {manual ? <>
      <SliderField bottomSeparator="none" label={copy[5]} min={8} max={35} step={1} value={draft.pl1}
        valueSuffix=" W" showValue editableValue disabled={busy} onChange={value => changeLimit("pl1", value)} />
      <SliderField bottomSeparator="none" label={copy[6]} min={10} max={45} step={1} value={draft.pl2}
        valueSuffix=" W" showValue editableValue disabled={busy} onChange={value => changeLimit("pl2", value)} />
      {changed && <ButtonItem layout="below" bottomSeparator="none" disabled={busy} onClick={apply}>{copy[7]}</ButtonItem>}
    </> : state.tdpAvailable && <div className="qsMeta">{copy[8]}</div>}
    {failed && <div className="qsMeta" role="status">{controlLocale(locale).saveError}</div>}
  </div>;
}

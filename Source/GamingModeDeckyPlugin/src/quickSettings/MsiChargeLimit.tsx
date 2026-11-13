import { DropdownItem } from "@decky/ui";
import { useEffect, useRef, useState } from "react";
import { call } from "../controlBackend";
import { controlLocale } from "../controlCenterLocale";
import { deviceInfoLocale } from "../deviceInfoLocale";

const translations = {
  en: ["Charge limit", "MSI firmware"],
  it: ["Limite di carica", "Firmware MSI"],
  de: ["Ladegrenze", "MSI-Firmware"],
  es: ["Límite de carga", "Firmware de MSI"],
  fr: ["Limite de charge", "Firmware MSI"],
  pt: ["Limite de carga", "Firmware MSI"],
  ru: ["Предел заряда", "Прошивка MSI"],
  uk: ["Межа заряду", "Прошивка MSI"],
  ja: ["充電上限", "MSI ファームウェア"],
  ko: ["충전 한도", "MSI 펌웨어"],
  zh: ["充电上限", "MSI 固件"],
  hi: ["चार्ज की सीमा", "MSI फ़र्मवेयर"],
} as const;

type NativeLimit = { available: true; chargeLimit: number; token: string };
type Response = (NativeLimit | { available?: false }) & { ok?: boolean; code?: string };

export function MsiChargeLimit({ locale }: { locale: string }) {
  const key = locale.toLowerCase().split(/[-_]/)[0] as keyof typeof translations;
  const copy = translations[key] ?? translations.en;
  const [state, setState] = useState<NativeLimit>();
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
        const result = await call<[], Response>("get_msi_claw_firmware");
        if (!stopped && revision.current === observedRevision) setState(result.available ? result : undefined);
      } catch { /* Retain verified firmware state through a transient RPC failure. */ }
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
  const choices = [...new Set([60, 80, 100, state.chargeLimit])].sort((a, b) => a - b);
  const choose = async (percent: number) => {
    if (writing.current || !choices.includes(percent) || percent === state.chargeLimit) return;
    revision.current++;
    writing.current = true;
    setBusy(true);
    setFailed(false);
    try {
      const result = await call<[{ token: string; percent: number; expected: number }], Response>("set_msi_claw_charge_limit", {
        token: state.token, expected: state.chargeLimit, percent,
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
      bottomSeparator="none" layout="below" selectedOption={state.chargeLimit} disabled={busy}
      rgOptions={choices.map(percent => ({ data: percent, label: `${percent}%` }))}
      onChange={({ data }) => void choose(data)} />
    {failed && <div className="qsMeta" role="status">{controlLocale(locale).saveError}</div>}
  </div>;
}

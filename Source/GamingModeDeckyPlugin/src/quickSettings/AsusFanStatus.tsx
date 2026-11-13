import { useEffect, useState } from "react";
import { call } from "../controlBackend";
import { controlLocale } from "../controlCenterLocale";
import { deviceInfoLocale } from "../deviceInfoLocale";
import { createAsusFanMonitor, type AsusFanView } from "./asusFanTelemetry";

const labels: Record<string, readonly string[]> = {
  en: ["Fans", "Fan 1", "Fan 2", "Temperatures"], it: ["Ventole", "Ventola 1", "Ventola 2", "Temperature"],
  de: ["Lüfter", "Lüfter 1", "Lüfter 2", "Temperaturen"], es: ["Ventiladores", "Ventilador 1", "Ventilador 2", "Temperaturas"],
  fr: ["Ventilateurs", "Ventilateur 1", "Ventilateur 2", "Températures"], pt: ["Ventoinhas", "Ventoinha 1", "Ventoinha 2", "Temperaturas"],
  ru: ["Вентиляторы", "Вентилятор 1", "Вентилятор 2", "Температуры"], uk: ["Вентилятори", "Вентилятор 1", "Вентилятор 2", "Температури"],
  ja: ["ファン", "ファン 1", "ファン 2", "温度"], ko: ["팬", "팬 1", "팬 2", "온도"],
  zh: ["风扇", "风扇 1", "风扇 2", "温度"], hi: ["पंखे", "पंखा 1", "पंखा 2", "तापमान"],
};
const aliases: Record<string, string> = { english: "en", italian: "it", german: "de", spanish: "es", latam: "es",
  french: "fr", brazilian: "pt", portuguese: "pt", russian: "ru", ukrainian: "uk", japanese: "ja",
  koreana: "ko", schinese: "zh", tchinese: "zh", hindi: "hi" };

export function AsusFanStatus({ locale }: { locale: string }) {
  const [view, setView] = useState<AsusFanView>({ available: false, failed: false, fans: null });
  useEffect(() => {
    const monitor = createAsusFanMonitor({ get: () => call<[], unknown>("get_asus_device_controls"),
      visible: () => document.visibilityState !== "hidden", now: Date.now,
      schedule: (callback, delay) => window.setTimeout(callback, delay), cancel: timer => window.clearTimeout(timer as number),
      changed: setView });
    const visible = () => monitor.visibilityChanged();
    document.addEventListener("visibilitychange", visible);
    window.addEventListener("focus", visible);
    visible();
    return () => { monitor.dispose(); document.removeEventListener("visibilitychange", visible); window.removeEventListener("focus", visible); };
  }, []);
  if (!view.available) return null;
  const key = locale.trim().toLowerCase();
  const canonical = aliases[key] ?? key.replace(/_/g, "-");
  const copy = labels[canonical.split("-")[0]] ?? labels.en;
  let number = (rpm: number) => String(rpm);
  try { const formatter = new Intl.NumberFormat(canonical); number = rpm => formatter.format(rpm); }
  catch { /* Unknown interface locales retain a readable integer. */ }
  return <div className="qsCardBody">
    <div>{view.fans || !view.temperatures ? copy[0] : copy[3]}</div>
    <div className="qsMeta">ASUS · {deviceInfoLocale(locale).experimental}</div>
    {view.fans ? <div style={{ display: "flex", gap: 16, flexWrap: "wrap" }}>
      {[view.fans.cpu_rpm, view.fans.gpu_rpm].map((rpm, index) => rpm === null ? null : <div key={index}>
        <div className="qsMeta">{copy[index + 1]}</div><div>{number(rpm)} RPM</div>
      </div>)}
    </div> : !view.temperatures && <div className="qsMeta" role="status">{controlLocale(locale).unavailable}</div>}
    {view.temperatures && <>
      {view.fans && <div className="qsMeta">{copy[3]}</div>}
      <div style={{ display: "flex", gap: 16, flexWrap: "wrap" }}>
        {[view.temperatures.cpu_c, view.temperatures.gpu_c].map((celsius, index) => celsius === null ? null : <div key={index}>
          <div className="qsMeta">{index === 0 ? "CPU" : "GPU"}</div><div>{number(celsius)} °C</div>
        </div>)}
      </div>
    </>}
  </div>;
}

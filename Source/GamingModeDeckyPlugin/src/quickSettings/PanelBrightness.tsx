import { SliderField } from "@decky/ui";
import { useEffect, useRef, useState } from "react";
import { call } from "../controlBackend";
import { controlLocale } from "../controlCenterLocale";
import { createSettledWriter } from "../panelIntegration/settledWriter";

const brightnessLabels: Record<string, readonly [string, string]> = {
  en: ["Built-in display brightness", "Software brightness"],
  it: ["Luminosità dello schermo integrato", "Luminosità software"],
  de: ["Helligkeit des integrierten Displays", "Software-Helligkeit"],
  es: ["Brillo de la pantalla integrada", "Brillo por software"],
  fr: ["Luminosité de l’écran intégré", "Luminosité logicielle"],
  pt: ["Brilho da tela integrada", "Brilho por software"],
  ru: ["Яркость встроенного экрана", "Программная яркость"],
  uk: ["Яскравість вбудованого екрана", "Програмна яскравість"],
  ja: ["内蔵ディスプレイの明るさ", "ソフトウェアでの明るさ"],
  ko: ["내장 디스플레이 밝기", "소프트웨어 밝기"],
  zh: ["内置屏幕亮度", "软件亮度"],
  hi: ["अंतर्निहित स्क्रीन की चमक", "सॉफ़्टवेयर की चमक"],
};

const language = (locale: string) => locale.toLowerCase().split(/[-_]/)[0];
const labels = (locale: string) => brightnessLabels[language(locale)] ?? brightnessLabels.en;
export const softwareBrightnessLabel = (locale: string) => labels(locale)[1];

type Brightness = { available: true; token: string; level: number; levels: number[] };
type Response = (Brightness | { available?: false }) & { ok?: boolean; code?: string };
type Change = { token: string; level: number };

export function PanelBrightness({ locale }: { locale: string }) {
  const [state, setState] = useState<Brightness>();
  const [failed, setFailed] = useState(false);
  const confirmed = useRef<Brightness | undefined>(undefined);
  const draft = useRef<number | undefined>(undefined);
  const pending = useRef(false);
  const revision = useRef(0);
  const writer = useRef<ReturnType<typeof createSettledWriter<Change, Response>> | undefined>(undefined);

  useEffect(() => {
    let mounted = true;
    let reading = false;
    const restore = () => {
      pending.current = false;
      draft.current = confirmed.current?.level;
      setState(confirmed.current);
      setFailed(true);
    };
    const currentWriter = createSettledWriter<Change, Response>({
      delayMs: 400,
      write: request => call<[Change], Response>("set_panel_brightness", request),
      onSettled: result => {
        pending.current = false;
        if (result.available) confirmed.current = result;
        else if (result.code === "panel_changed" || result.code === "unavailable") confirmed.current = undefined;
        draft.current = confirmed.current?.level;
        setState(confirmed.current);
        setFailed(result.ok !== true);
      },
      onError: restore,
    });
    writer.current = currentWriter;
    const refresh = async () => {
      if (!mounted || reading || pending.current || document.visibilityState === "hidden") return;
      reading = true;
      const readingRevision = revision.current;
      try {
        const result = await call<[], Response>("get_panel_brightness");
        if (!mounted || pending.current || revision.current !== readingRevision) return;
        confirmed.current = result.available ? result : undefined;
        draft.current = confirmed.current?.level;
        setState(confirmed.current);
      } catch { /* Keep the last confirmed hardware value on a transient RPC failure. */ }
      finally { reading = false; }
    };
    void refresh();
    const onVisible = () => void refresh();
    window.addEventListener("focus", onVisible);
    document.addEventListener("visibilitychange", onVisible);
    return () => {
      mounted = false;
      pending.current = false;
      currentWriter.dispose();
      if (writer.current === currentWriter) writer.current = undefined;
      window.removeEventListener("focus", onVisible);
      document.removeEventListener("visibilitychange", onVisible);
    };
  }, []);

  if (!state) return null;
  const key = language(locale);
  const formatted = new Intl.NumberFormat(brightnessLabels[key] ? key : "en", { style: "percent" }).format(state.level / 100);
  return <div>
    <SliderField bottomSeparator="none" label={labels(locale)[0]} description={formatted}
      min={0} max={state.levels.length - 1} step={1} value={state.levels.indexOf(state.level)}
      minimumDpadGranularity={1} onChange={index => {
        const level = state.levels[Math.round(index)];
        if (level === undefined || level === draft.current || !writer.current) return;
        draft.current = level;
        revision.current++;
        pending.current = true;
        setFailed(false);
        setState({ ...state, level });
        writer.current.enqueue({ token: state.token, level });
      }} />
    {failed && <div className="qsMeta" role="status">{controlLocale(locale).saveError}</div>}
  </div>;
}

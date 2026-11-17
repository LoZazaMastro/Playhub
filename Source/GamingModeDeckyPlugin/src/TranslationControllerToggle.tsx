import { DFL, SP_REACT as React } from "./decky";
import { readSettings, writeSettings } from "./api";
import { translationController } from "./translationController";

const { PanelSectionRow, ToggleField } = DFL as any;
const descriptions: Record<string, string> = {
  en: "Makes the Steam Controller appear as an Xbox controller.",
  it: "Fa riconoscere lo Steam Controller come un pad Xbox.",
  es: "Hace que Steam Controller se reconozca como un mando Xbox.",
  fr: "Fait reconnaître le Steam Controller comme une manette Xbox.",
  de: "Lässt den Steam Controller als Xbox-Controller erkennen.",
  pt: "Faz o Steam Controller ser reconhecido como um controle Xbox.",
  uk: "Дає змогу розпізнавати Steam Controller як контролер Xbox.",
  zh: "让 Steam 控制器被识别为 Xbox 手柄。",
  ja: "Steam Controller を Xbox コントローラーとして認識させます。",
  ko: "Steam Controller를 Xbox 컨트롤러로 인식하게 합니다.",
  hi: "Steam Controller को Xbox कंट्रोलर के रूप में पहचानने देता है।",
  ru: "Позволяет распознавать Steam Controller как контроллер Xbox.",
};

export function TranslationControllerToggle({ locale }: { locale: string }) {
  const [connected, setConnected] = React.useState(false);
  const [enabled, setEnabled] = React.useState<boolean | null>(null);
  const [busy, setBusy] = React.useState(false);
  const [available, setAvailable] = React.useState(false);
  const mounted = React.useRef(false);
  const writing = React.useRef(false);
  const revision = React.useRef(0);
  React.useEffect(() => {
    mounted.current = true;
    const detect = () => { if (mounted.current) setConnected(translationController((window as any).ControllerStore) !== null); };
    const refresh = () => {
      if (writing.current) return;
      const stamp = ++revision.current;
      void readSettings().then(settings => {
        if (mounted.current && revision.current === stamp) {
          const known = typeof settings?.steamControllerTranslationEnabled === "boolean";
          setAvailable(known);
          if (known) setEnabled(settings!.steamControllerTranslationEnabled!);
        }
      }).catch(() => { if (mounted.current && revision.current === stamp) setAvailable(false); });
    };
    detect(); refresh();
    const timer = window.setInterval(detect, 1500);
    window.addEventListener("focus", refresh);
    return () => { mounted.current = false; ++revision.current; window.clearInterval(timer); window.removeEventListener("focus", refresh); };
  }, []);
  if (!connected || enabled === null) return null;
  return <PanelSectionRow>
    <ToggleField bottomSeparator="none" label="Translation Controller"
      description={descriptions[locale] ?? descriptions.en} checked={enabled} disabled={busy || !available}
      onChange={async (value: boolean) => {
        if (typeof value !== "boolean" || writing.current || !available || !mounted.current || !translationController((window as any).ControllerStore)) return;
        writing.current = true; setBusy(true);
        const stamp = ++revision.current;
        try {
          const settings = await writeSettings({ steamControllerTranslationEnabled: value });
          if (mounted.current && revision.current === stamp) {
            const known = typeof settings?.steamControllerTranslationEnabled === "boolean";
            setAvailable(known);
            if (known) setEnabled(settings!.steamControllerTranslationEnabled!);
          }
        } catch { if (mounted.current && revision.current === stamp) setAvailable(false); }
        finally { writing.current = false; if (mounted.current && revision.current === stamp) setBusy(false); }
      }} />
  </PanelSectionRow>;
}

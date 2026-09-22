import { DFL, SP_REACT as React } from "./decky";
import { readSettings, writeSettings } from "./api";
import { readSteamControllers } from "./controllerNative";

const { Focusable, PanelSectionRow, ToggleField } = DFL as any;
const copy = (locale: string) => /^(it|italian)([-_]|$)/i.test(locale) ? {
  title: "Controller nativo SDL3",
  description: "Usa il riconoscimento nativo dello Steam Controller nei giochi che bypassano Steam Input, soprattutto negli emulatori.",
  enabled: "Modalità SDL3",
  enabledDescription: "Avvia le app desktop con SDL3 attivo. Puoi modificare questa scelta nelle Game options del singolo gioco.",
} : {
  title: "SDL3 native controller",
  description: "Use native Steam Controller recognition in games that bypass Steam Input, especially emulators.",
  enabled: "SDL3 mode",
  enabledDescription: "Launch desktop apps with SDL3 enabled. You can override this in the individual game's options.",
};
const css = `.ph-sdl3-controller{width:100%;padding:0 16px 14px;box-sizing:border-box}.ph-sdl3-controller *{box-sizing:border-box}.ph-sdl3-title{font-size:15px;font-weight:650;line-height:1.35;margin:0 0 5px}.ph-sdl3-copy{font-size:12px;line-height:1.45;color:rgba(255,255,255,.68);margin:0 0 12px}`;

export function ControllerSettings({ locale }: { locale: string }) {
  const labels = copy(locale);
  const [enabled, setEnabled] = React.useState(false);
  const [detected, setDetected] = React.useState(false);
  React.useEffect(() => {
    let alive = true;
    const refresh = () => {
      const devices = readSteamControllers((window as any).ControllerStore);
      const steamController = devices.some((device) => /steam controller/i.test(device.name) || /^28de:/i.test(device.hardware));
      if (alive) setDetected(steamController);
    };
    void readSettings().then((settings) => { if (alive && settings) setEnabled(Boolean(settings.sdl3NativeControllerEnabled)); });
    refresh();
    const timer = window.setInterval(refresh, 1500);
    return () => { alive = false; window.clearInterval(timer); };
  }, []);
  return <Focusable className="ph-sdl3-controller" flow-children="column">
    <style>{css}</style>
    {detected ? <>
      <h3 className="ph-sdl3-title">{labels.title}</h3>
      <p className="ph-sdl3-copy">{labels.description}</p>
      <PanelSectionRow>
        <ToggleField bottomSeparator="none" label={labels.enabled} description={labels.enabledDescription} checked={enabled}
          onChange={(value: boolean) => { setEnabled(value); void writeSettings({ sdl3NativeControllerEnabled: value }); }} />
      </PanelSectionRow>
    </> : null}
  </Focusable>;
}

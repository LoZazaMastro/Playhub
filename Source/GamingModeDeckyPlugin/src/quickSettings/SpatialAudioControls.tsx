import { DialogButton, DropdownItem } from "@decky/ui";
import { useEffect, useRef, useState } from "react";
import { call } from "../controlBackend";
import { controlLocale } from "../controlCenterLocale";

const words = {
  en: ["Spatial audio", "Off", "Windows could not apply this audio format.", "Dolby and DTS use the licenses installed on this PC.", "Selected format:"],
  it: ["Audio spaziale", "Disattivato", "Windows non ha applicato questo formato audio.", "Dolby e DTS usano le licenze installate su questo PC.", "Formato selezionato:"],
  de: ["Raumklang", "Aus", "Windows konnte dieses Audioformat nicht anwenden.", "Dolby und DTS verwenden die auf diesem PC installierten Lizenzen.", "Gewähltes Format:"],
  es: ["Sonido espacial", "Desactivado", "Windows no pudo aplicar este formato de audio.", "Dolby y DTS utilizan las licencias instaladas en este PC.", "Formato seleccionado:"],
  fr: ["Son spatial", "Désactivé", "Windows n’a pas pu appliquer ce format audio.", "Dolby et DTS utilisent les licences installées sur ce PC.", "Format sélectionné :"],
  pt: ["Áudio espacial", "Desativado", "O Windows não conseguiu aplicar este formato de áudio.", "Dolby e DTS usam as licenças instaladas neste PC.", "Formato selecionado:"],
  ru: ["Пространственный звук", "Выкл.", "Windows не удалось применить этот аудиоформат.", "Dolby и DTS используют лицензии, установленные на этом ПК.", "Выбранный формат:"],
  uk: ["Просторовий звук", "Вимкнено", "Windows не вдалося застосувати цей аудіоформат.", "Dolby та DTS використовують ліцензії, установлені на цьому ПК.", "Вибраний формат:"],
  ja: ["立体音響", "オフ", "Windows でこの音声形式を適用できませんでした。", "Dolby と DTS はこの PC にインストールされたライセンスを使用します。", "選択した形式:"],
  ko: ["공간 음향", "끄기", "Windows에서 이 오디오 형식을 적용하지 못했습니다.", "Dolby와 DTS는 이 PC에 설치된 라이선스를 사용합니다.", "선택한 형식:"],
  zh: ["空间音效", "关闭", "Windows 未能应用此音频格式。", "Dolby 和 DTS 使用此电脑上安装的许可证。", "所选格式："],
  hi: ["स्पेशल ऑडियो", "बंद", "Windows यह ऑडियो फ़ॉर्मैट लागू नहीं कर सका।", "Dolby और DTS इस PC पर स्थापित लाइसेंस का उपयोग करते हैं।", "चुना गया फ़ॉर्मैट:"],
} as const;
type Option = { id: string; label: string; supported: boolean; requiresProviderLicense: boolean };
type Snapshot = { ok: boolean; code: string; endpointId: string; selectedFormat: string; activeFormat: string;
  options: Option[]; canSet: boolean; cleanupComplete: boolean };
const off = "{00000000-0000-0000-0000-000000000000}";
const guid = (value: unknown): value is string => typeof value === "string" && /^\{[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\}$/i.test(value);
function renderEndpoint(value: string): string | undefined {
  const native = /^\{0\.0\.0\.00000000\}\.\{[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\}$/i;
  const id = value.toLowerCase(), prefix = "\\\\?\\swd#mmdevapi#", suffix = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
  if (native.test(id)) return id;
  if (id.startsWith(prefix) && id.endsWith(suffix)) {
    const inner = id.slice(prefix.length, -suffix.length);
    if (native.test(inner)) return inner;
  }
  return undefined;
}
const sameOutput = (parent: string, actual: string) => !!renderEndpoint(parent) && renderEndpoint(parent) === renderEndpoint(actual);
function usable(value: Snapshot | undefined): value is Snapshot {
  return !!value && typeof value.endpointId === "string" && value.endpointId.length > 0
    && guid(value.selectedFormat) && guid(value.activeFormat) && typeof value.canSet === "boolean"
    && typeof value.cleanupComplete === "boolean" && Array.isArray(value.options) && value.options.length <= 16
    && value.options.every(item => guid(item.id) && typeof item.label === "string" && typeof item.supported === "boolean");
}
async function bounded<T>(operation: Promise<T>): Promise<T> {
  let timer: number | undefined;
  try { return await Promise.race([operation, new Promise<never>((_, reject) => {
    timer = window.setTimeout(() => reject(new Error("Spatial audio response timed out")), 12_000);
  })]); } finally { window.clearTimeout(timer); }
}

export function SpatialAudioControls({ locale, endpointId }: { locale: string; endpointId: string }) {
  const copy = words[locale.toLowerCase().split(/[-_]/)[0] as keyof typeof words] ?? words.en;
  const [snapshot, setSnapshot] = useState<Snapshot>();
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);
  const runtime = useRef({ mounted: false, generation: 0, revision: 0, writing: false, endpoint: "" });
  const refresh = useRef<() => void>(() => {});
  useEffect(() => {
    const current = runtime.current;
    const generation = ++current.generation;
    current.mounted = true; current.endpoint = endpointId; current.writing = false;
    let reading = false;
    setSnapshot(undefined); setBusy(false); setFailed(false);
    const live = () => current.mounted && current.generation === generation;
    const read = async () => {
      if (!renderEndpoint(endpointId) || !live() || reading || current.writing || document.visibilityState === "hidden") return;
      reading = true; const revision = current.revision;
      try {
        const result = await bounded(call<[], Snapshot>("get_spatial_audio"));
        if (live() && !current.writing && current.revision === revision) {
          setSnapshot(result?.ok && usable(result) && sameOutput(endpointId, result.endpointId) ? result : undefined);
        }
      } catch { /* A later focus or refresh retries the read. */ }
      finally { reading = false; if (live() && !current.writing && current.revision !== revision) void read(); }
    };
    refresh.current = () => { void read(); };
    void read();
    const timer = window.setInterval(() => void read(), 15_000);
    const visible = () => { void read(); };
    window.addEventListener("focus", visible); document.addEventListener("visibilitychange", visible);
    return () => {
      if (current.generation === generation) { current.mounted = false; ++current.generation; refresh.current = () => {}; }
      window.clearInterval(timer);
      window.removeEventListener("focus", visible); document.removeEventListener("visibilitychange", visible);
    };
  }, [endpointId]);

  if (!snapshot) return null;
  const options = snapshot.options.filter(item => item.supported === true);
  if (options.length < 2 || !options.some(item => item.id === snapshot.activeFormat)) return null;
  const choose = async (format: string) => {
    const current = runtime.current;
    if (!current.mounted || current.endpoint !== endpointId || current.writing || !snapshot.canSet || !sameOutput(endpointId, snapshot.endpointId)
        || format === snapshot.activeFormat || !options.some(item => item.id === format)) return;
    const generation = current.generation, revision = ++current.revision;
    let accepted = false;
    current.writing = true; setBusy(true); setFailed(false);
    try {
      const result = await bounded(call<[{ endpointId: string; expectedFormat: string; format: string }], Snapshot>("set_spatial_audio", {
        endpointId: snapshot.endpointId, expectedFormat: snapshot.selectedFormat, format,
      }));
      if (!current.mounted || current.generation !== generation || current.revision !== revision) return;
      const confirmed = result?.ok === true && result.cleanupComplete === true && usable(result)
        && result.endpointId === snapshot.endpointId && sameOutput(endpointId, result.endpointId)
        && result.activeFormat === format && result.selectedFormat === format;
      accepted = confirmed;
      setSnapshot(confirmed ? result : { ...snapshot, canSet: false });
      setFailed(!confirmed);
    } catch { if (current.mounted && current.generation === generation) { setFailed(true); setSnapshot({ ...snapshot, canSet: false }); } }
    finally {
      if (current.generation === generation) {
        current.writing = false;
        if (current.mounted) { setBusy(false); if (!accepted) refresh.current(); }
      }
    }
  };
  return <div className="qsDropdownField qsDropdownFullWidth">
    <DropdownItem bottomSeparator="none" label={copy[0]} layout="below" childrenContainerWidth="max" disabled={busy || !snapshot.canSet}
      selectedOption={snapshot.activeFormat} rgOptions={options.map(item => ({ data: item.id, label: item.id === off ? copy[1] : item.label }))}
      onChange={item => { void choose(String(item.data)); }} />
    {snapshot.selectedFormat !== snapshot.activeFormat && snapshot.options.some(item => item.id === snapshot.selectedFormat)
      && <div className="qsMeta" role="status">{copy[4]} {snapshot.options.find(item => item.id === snapshot.selectedFormat)?.label}</div>}
    {options.some(item => item.requiresProviderLicense) && <div className="qsMeta">{copy[3]}</div>}
    {failed && <div role="status" className="qsMeta">{copy[2]} <DialogButton onClick={() => refresh.current()}>{controlLocale(locale).retry}</DialogButton></div>}
  </div>;
}

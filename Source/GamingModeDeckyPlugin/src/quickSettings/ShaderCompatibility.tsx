import { DialogButton, DropdownItem, ToggleField } from "@decky/ui";
import { useEffect, useRef, useState } from "react";
import { call } from "../controlBackend";
import { controlLocale } from "../controlCenterLocale";
import { shaderCacheCopy } from "./shaderCacheLocale";

type Nvidia = { available: boolean; status: string; token?: string; enabled?: boolean; limit?: number; driverDefault?: number; defaultLimit?: boolean };
type Cache = { id: string; state: "missing" | "ready" | "partial" | "unavailable"; bytes: number; files: number; modified: number | null };
type CacheReport = { status: string; caches: Cache[]; nvidia: Nvidia; preflight?: Report };
const DriverDropdown = DropdownItem as any;
const cacheNames: Record<string, string> = { directx:"DirectX", "nvidia-dx":"NVIDIA · DirectX", "nvidia-gl":"NVIDIA · OpenGL", "nvidia-low-dx":"NVIDIA · DirectX", "amd-dx":"AMD · DirectX", "amd-dxc":"AMD · DXC", "amd-gl":"AMD · OpenGL", "amd-vulkan":"AMD · Vulkan", intel:"Intel" };
export function shaderBytes(bytes: number) {
  if (!Number.isFinite(bytes) || bytes < 0) return "—";
  if (bytes < 1048576) return `${(bytes / 1024).toFixed(bytes < 10240 ? 1 : 0)} KiB`;
  if (bytes < 1073741824) return `${(bytes / 1048576).toFixed(1)} MiB`;
  return `${(bytes / 1073741824).toFixed(1)} GiB`;
}
export function shaderLimit(value: number) { return value === 0xffffffff ? "∞" : shaderBytes(value * 1048576); }
function bounded<T>(operation: Promise<T>): Promise<T> {
  return new Promise((resolve,reject) => {
    const timer = window.setTimeout(() => reject(new Error("Shader request timed out")), 20000);
    operation.then(value => { window.clearTimeout(timer); resolve(value); }, error => { window.clearTimeout(timer); reject(error); });
  });
}

type Status = "eligible" | "unsupported" | "unverified";
type Report = {
  adapters: { name: string; microsoft_asd: Status; intel_precompiled: Status }[];
  game: { state: "no_game" | "unverified" | "not_installed" | "provider_listed"; title: string | null };
};

const copy: Record<string, readonly string[]> = {
  en: ["Check shader support", "Graphics requirements met", "Requirements unmet", "Needs verification", "Listed by Intel · cache unverified", "Graphics adapter unavailable"],
  it: ["Verifica supporto shader", "Requisiti grafici soddisfatti", "Fuori requisiti", "Da verificare", "Nel catalogo Intel · cache da verificare", "Scheda grafica da rilevare"],
  de: ["Shader-Unterstützung prüfen", "Grafikanforderungen erfüllt", "Nicht unterstützt", "Nicht geprüft", "Bei Intel gelistet · Cache nicht geprüft", "Keine Grafikkarte erkannt"],
  es: ["Comprobar shaders", "Requisitos gráficos cumplidos", "No compatible", "Sin verificar", "En el catálogo de Intel · caché sin verificar", "No se detectó ninguna tarjeta gráfica"],
  fr: ["Vérifier la compatibilité des shaders", "Configuration graphique compatible", "Non pris en charge", "Non vérifié", "Répertorié par Intel · cache non vérifié", "Aucune carte graphique détectée"],
  pt: ["Verificar suporte a shaders", "Requisitos gráficos atendidos", "Sem suporte", "Não verificado", "No catálogo da Intel · cache não verificado", "Nenhuma placa de vídeo detectada"],
  ru: ["Проверить поддержку шейдеров", "Требования к графике выполнены", "Не поддерживается", "Не проверено", "В каталоге Intel · кэш не проверен", "Видеокарта не обнаружена"],
  uk: ["Перевірити підтримку шейдерів", "Графічні вимоги виконано", "Не підтримується", "Не перевірено", "У каталозі Intel · кеш не перевірено", "Відеокарту не виявлено"],
  ja: ["シェーダーの対応を確認", "グラフィック要件を満たしています", "非対応", "未確認", "Intel の一覧に掲載 · キャッシュ未確認", "グラフィックアダプターが見つかりません"],
  ko: ["셰이더 지원 확인", "그래픽 요구 사항 충족", "지원 안 함", "확인되지 않음", "Intel 목록에 있음 · 캐시 미확인", "그래픽 어댑터를 찾을 수 없음"],
  zh: ["检查着色器支持", "满足图形要求", "不支持", "未验证", "已列入 Intel 目录 · 缓存未验证", "未检测到显卡"],
  hi: ["शेडर सपोर्ट जाँचें", "ग्राफ़िक्स की ज़रूरतें पूरी हैं", "सपोर्ट नहीं है", "पुष्टि नहीं हुई", "Intel की सूची में है · कैश की पुष्टि नहीं हुई", "कोई ग्राफ़िक्स अडैप्टर नहीं मिला"],
};
const aliases: Record<string, string> = { italian: "it", german: "de", spanish: "es", latam: "es", french: "fr", brazilian: "pt", portuguese: "pt", russian: "ru", ukrainian: "uk", japanese: "ja", koreana: "ko", schinese: "zh", tchinese: "zh", hindi: "hi" };

export function ShaderCompatibility({ appId, locale }: { appId: number; locale: string }) {
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);
  const [report, setReport] = useState<Report>();
  const [inventory, setInventory] = useState<CacheReport>();
  const [enabled, setEnabled] = useState(true);
  const [limit, setLimit] = useState<number | "default">("default");
  const [notice, setNotice] = useState("");
  const mounted = useRef(false);
  const running = useRef(false);
  const epoch = useRef(0);
  useEffect(() => {
    mounted.current = true; running.current = false;
    const generation = ++epoch.current;
    setInventory(undefined); setReport(undefined);
    void inspect(generation);
    return () => { mounted.current = false; ++epoch.current; };
  }, [appId]);
  const text = shaderCacheCopy(locale);
  const [check, eligible, unsupported, unknown, listed, noAdapter] = copy[aliases[locale] ?? locale.split(/[-_]/)[0]] ?? copy.en;
  const labels = { eligible, unsupported, unverified: unknown };
  const current = (generation:number) => mounted.current && epoch.current === generation;
  function accept(next: CacheReport, generation:number) {
    if (!current(generation)) return;
    setInventory(next); setReport(next.preflight);
    if (next.nvidia.available) { setEnabled(next.nvidia.enabled === true); setLimit(next.nvidia.defaultLimit ? "default" : next.nvidia.limit!); }
  }
  async function inspect(generation = epoch.current) {
    if (running.current) return;
    running.current = true;
    setBusy(true);
    setFailed(false);
    setNotice("");
    try {
      const next = await bounded(call<[number], CacheReport>("get_shader_cache", appId));
      if (next?.status !== "ready" || !Array.isArray(next.caches)) throw new Error("Shader inventory unavailable");
      accept(next, generation);
    } catch { if (current(generation)) setFailed(true); }
    finally { if (current(generation)) { running.current = false; setBusy(false); } }
  }
  async function apply() {
    if (running.current || !inventory?.nvidia.available || !inventory.nvidia.token) return;
    const generation = epoch.current;
    running.current = true; setBusy(true); setNotice("");
    try {
      const next = await bounded(call<[{token:string;enabled:boolean;limit:number|"default"}], Nvidia>("set_nvidia_shader_cache", { token:inventory.nvidia.token,enabled,limit }));
      if (current(generation)) {
        setInventory(current => current ? { ...current,nvidia:next } : current);
        if (next.available) { setEnabled(next.enabled === true); setLimit(next.defaultLimit ? "default" : next.limit!); }
        if (next.status !== "applied") setNotice(next.status === "changed" ? text.changed : next.status === "restored" ? text.restored : text.failed);
      }
    } catch { if (current(generation)) setNotice(text.failed); }
    finally { if (current(generation)) { running.current = false; setBusy(false); } }
  }
  const driver = inventory?.nvidia;
  const dirty = driver?.available && (enabled !== driver.enabled || limit !== (driver.defaultLimit ? "default" : driver.limit));
  const options: {data:number|"default";label:string}[] = [{data:"default",label:`${text.driverDefault}${driver?.driverDefault !== undefined ? ` · ${shaderLimit(driver.driverDefault)}` : ""}`},
    ...[1024,4096,10240,16384,102400].map(value => ({data:value,label:shaderLimit(value)}))];
  // Preserve custom/unknown limits without silently selecting another preset.
  if (typeof limit === "number" && !options.some(value => value.data === limit)) options.push({data:limit,label:`${text.current} · ${shaderLimit(limit)}`});

  return <div className="qsCardBody">
    <DialogButton disabled={busy} onClick={() => void inspect()}>{busy ? controlLocale(locale).loading : text.refresh}</DialogButton>
    {failed && <div className="qsMeta" role="status">{controlLocale(locale).unavailable}</div>}
    {inventory && <div aria-live="polite" style={{display:"grid",gap:10}}>
      <strong>{text.saved}</strong>
      {inventory.caches.filter(cache => cache.state === "ready" || cache.state === "partial").map(cache => <div key={cache.id} className="qsMeta">
        <div style={{display:"flex",justifyContent:"space-between",gap:12,flexWrap:"wrap"}}><span>{cacheNames[cache.id] ?? (cache.id.startsWith("steam") ? "Steam" : cache.id)}</span><strong>{shaderBytes(cache.bytes)}</strong></div>
        <span>{cache.files.toLocaleString()} {text.files}{cache.modified ? ` · ${new Date(cache.modified * 1000).toLocaleDateString()}` : ""}{cache.state === "partial" ? ` · ${text.partial}` : ""}</span>
      </div>)}
      {!inventory.caches.some(cache => cache.files > 0) && <div className="qsMeta">{inventory.caches.some(cache => cache.state === "unavailable" || cache.state === "partial") ? controlLocale(locale).unavailable : text.empty}</div>}
    </div>}
    {driver?.available && <div style={{display:"grid",gap:10}}>
      <strong>{text.nvidia}</strong>
      <ToggleField bottomSeparator="none" label={text.enabled} checked={enabled} disabled={busy} onChange={setEnabled} />
      <DriverDropdown label={text.limit} layout="below" bottomSeparator="none" selectedOption={limit} rgOptions={options} disabled={busy || !enabled}
        onChange={(option:{data:number|"default"}) => setLimit(option.data)} />
      <DialogButton disabled={busy || !dirty} onClick={() => void apply()}>{text.apply}</DialogButton>
    </div>}
    {notice && <div className="qsMeta" role="status">{notice}</div>}
    {report && <div className="qsMeta" aria-live="polite" style={{ display: "grid", gap: 8 }}>
      {report.adapters.length === 0 ? noAdapter : report.adapters.map((adapter, i) => <div key={`${adapter.name}-${i}`}>
        <strong>{adapter.name}</strong><br />Advanced Shader Delivery · {labels[adapter.microsoft_asd]}
        {adapter.intel_precompiled !== "unverified" && <><br />Intel Precompiled Shaders · {labels[adapter.intel_precompiled]}</>}
      </div>)}
      {appId !== 0 && <div>{report.game.state === "provider_listed" ? `${report.game.title} — ${listed}` : unknown}</div>}
    </div>}
  </div>;
}

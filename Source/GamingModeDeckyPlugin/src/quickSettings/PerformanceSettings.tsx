import { DialogButton, DropdownItem, ToggleField } from "@decky/ui";
import { useEffect, useRef, useState } from "react";
import { controlLocale } from "../controlCenterLocale";
import { livePerformanceRuntime, performanceMonitorHost, PERFORMANCE_SOURCE } from "./livePerformanceRuntime";
import type { LivePerformanceOptions } from "./livePerformanceController";
import { SteamPerformanceSettings } from "./SteamPerformanceSettings";
import { steamPerformanceMonitor } from "./steamPerformanceMonitor";

const labels: Record<string, readonly string[]> = {
  en: ["Performance monitor", "Show performance", "Transparent background", "Start a game to see its performance.", "Waiting for game frames.", "Visible while playing."],
  it: ["Monitor prestazioni", "Mostra prestazioni", "Sfondo trasparente", "Avvia un gioco per vedere le prestazioni.", "In attesa dei fotogrammi del gioco.", "Visibile durante il gioco."],
  de: ["Leistungsanzeige", "Leistung anzeigen", "Transparenter Hintergrund", "Starte ein Spiel, um die Leistung zu sehen.", "Warte auf Spielbilder.", "Während des Spielens sichtbar."],
  es: ["Monitor de rendimiento", "Mostrar rendimiento", "Fondo transparente", "Inicia un juego para ver su rendimiento.", "Esperando fotogramas del juego.", "Visible durante el juego."],
  fr: ["Suivi des performances", "Afficher les performances", "Arrière-plan transparent", "Lancez un jeu pour voir ses performances.", "En attente d’images du jeu.", "Visible pendant le jeu."],
  pt: ["Monitor de desempenho", "Mostrar desempenho", "Fundo transparente", "Inicia um jogo para ver o desempenho.", "A aguardar fotogramas do jogo.", "Visível durante o jogo."],
  ru: ["Монитор производительности", "Показывать производительность", "Прозрачный фон", "Запустите игру, чтобы увидеть производительность.", "Ожидание кадров игры.", "Отображается во время игры."],
  uk: ["Монітор продуктивності", "Показувати продуктивність", "Прозоре тло", "Запустіть гру, щоб побачити продуктивність.", "Очікування кадрів гри.", "Видно під час гри."],
  ja: ["パフォーマンスモニター", "パフォーマンスを表示", "背景を透明にする", "ゲームを起動するとパフォーマンスが表示されます。", "ゲームのフレームを待っています。", "プレイ中に表示されます。"],
  ko: ["성능 모니터", "성능 표시", "투명 배경", "게임을 시작하면 성능이 표시됩니다.", "게임 프레임을 기다리는 중입니다.", "플레이 중에 표시됩니다."],
  zh: ["效能監視器", "顯示效能", "透明背景", "啟動遊戲以查看效能。", "正在等候遊戲畫面。", "遊玩時顯示。"],
  hi: ["परफ़ॉर्मेंस मॉनिटर", "परफ़ॉर्मेंस दिखाएँ", "पारदर्शी पृष्ठभूमि", "परफ़ॉर्मेंस देखने के लिए गेम शुरू करें।", "गेम के फ़्रेम की प्रतीक्षा है।", "खेलते समय दिखाई देता है।"],
};
const colorLabels: Record<string, readonly string[]> = {
  en: ["Text color", "White", "Yellow", "Red", "Green"], it: ["Colore del testo", "Bianco", "Giallo", "Rosso", "Verde"],
  de: ["Textfarbe", "Weiß", "Gelb", "Rot", "Grün"], es: ["Color del texto", "Blanco", "Amarillo", "Rojo", "Verde"],
  fr: ["Couleur du texte", "Blanc", "Jaune", "Rouge", "Vert"], pt: ["Cor do texto", "Branco", "Amarelo", "Vermelho", "Verde"],
  ru: ["Цвет текста", "Белый", "Жёлтый", "Красный", "Зелёный"], uk: ["Колір тексту", "Білий", "Жовтий", "Червоний", "Зелений"],
  ja: ["文字色", "白", "黄", "赤", "緑"], ko: ["글자 색상", "흰색", "노란색", "빨간색", "초록색"],
  zh: ["文字颜色", "白色", "黄色", "红色", "绿色"], hi: ["टेक्स्ट का रंग", "सफ़ेद", "पीला", "लाल", "हरा"],
};
const colorValues = ["white", "yellow", "red", "green"] as const;
const localeAliases: Record<string, string> = { english: "en", italian: "it", german: "de", spanish: "es", latam: "es",
  french: "fr", portuguese: "pt", brazilian: "pt", russian: "ru", ukrainian: "uk", japanese: "ja", koreana: "ko",
  schinese: "zh", tchinese: "zh", hindi: "hi" };
const cornerTokens = ["TopLeft", "TopCenter", "TopRight", "BottomLeft", "BottomCenter", "BottomRight"];
const cornerFallback = ["Top left", "Top center", "Top right", "Bottom left", "Bottom center", "Bottom right"];
const details = ["ClassicFPSOnly", "FPSDetailsAndGraph", "FPSCPUGPUSummary", "FPSCPUGPURAMDetails"];
const detailFallback = ["FPS only", "FPS and graph", "FPS, CPU and GPU", "Full details"];
function steamLabel(token: string, fallback: string) {
  try {
    const value = performanceMonitorHost().LocalizationManager?.LocalizeString(token);
    return typeof value === "string" && value.trim() && value !== token ? value : fallback;
  } catch { return fallback; }
}
function selectedSource(): "live" | "steam" {
  try {
    const saved = performanceMonitorHost().localStorage?.getItem(PERFORMANCE_SOURCE);
    if (saved === "live" || saved === "steam") return saved;
  } catch { /* The default remains Playhub for this session. */ }
  return "live";
}

export function PerformanceSettings({ locale, appId }: { locale: string; appId: number }) {
  const runtime = livePerformanceRuntime();
  const [view, setView] = useState(runtime.read);
  const [source, setSource] = useState(selectedSource);
  const [changing, setChanging] = useState(false);
  const [sourceFailed, setSourceFailed] = useState(false);
  const mounted = useRef(false);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  const key = localeAliases[locale.toLowerCase()] ?? locale.toLowerCase().split(/[-_]/)[0];
  const copy = labels[key] ?? labels.en, colors = colorLabels[key] ?? colorLabels.en;
  const common = controlLocale(locale);
  useEffect(() => {
    const update = () => setView(runtime.read());
    const unsubscribe = runtime.subscribe(update);
    update(); runtime.start();
    return unsubscribe;
  }, [runtime]);
  useEffect(() => {
    if (!view.state?.enabled) return;
    try {
      if (performanceMonitorHost().localStorage?.getItem(PERFORMANCE_SOURCE) !== "steam") setSource("live");
    } catch { setSource("live"); }
  }, [view.state?.enabled]);

  async function choose(next: "live" | "steam") {
    if (changing || next === source) return;
    setChanging(true); setSourceFailed(false);
    try {
      if (!runtime.read().state) { await runtime.refresh(); if (!runtime.read().state) throw new Error("Unavailable"); }
      if (next === "live") {
        const steam = steamPerformanceMonitor(performanceMonitorHost())?.read();
        const mapped = steam ? ([1, 3, 6, 4, 2, 5][steam.corner - 1] ?? 1) as LivePerformanceOptions["corner"] : 1;
        const accepted = await runtime.change({ enabled: steam ? steam.corner !== 0 : false,
          detail: steam?.detail ?? 1, corner: mapped, transparent: steam?.backgroundOpacity === 0 });
        if (!accepted) throw new Error("Unconfirmed");
        await runtime.holdSteamMonitor();
      } else {
        if (!await runtime.change({ enabled: false })) throw new Error("Unconfirmed");
        await runtime.restoreSteamMonitor();
      }
      try { performanceMonitorHost().localStorage?.setItem(PERFORMANCE_SOURCE, next); } catch { /* Selection remains valid for this session. */ }
      if (mounted.current) setSource(next);
    } catch { if (mounted.current) setSourceFailed(true); }
    finally { if (mounted.current) setChanging(false); }
  }
  const state = view.state;
  const disabled = view.busy || changing || !state;
  const change = (values: Partial<LivePerformanceOptions>) => { void runtime.change(values); };
  const description = !state?.appId || state.status === "TargetUnavailable" ? copy[3]
    : state.status === "NoData" ? copy[4]
    : state.suspended || state.status === "GameNotForeground" || state.status === "Suspended" ? copy[5]
    : state.status === "Ready" || state.status === "Disabled" ? "" : common.unavailable;
  return <>
    <div className="qsDropdownFullWidth"><DropdownItem bottomSeparator="none" layout="below" label={copy[0]}
      rgOptions={[{ data: "live", label: "Playhub" }, { data: "steam", label: "Steam" }]} selectedOption={source}
      disabled={view.busy || changing} onChange={option => { if (option.data === "live" || option.data === "steam") void choose(option.data); }} /></div>
    {source === "steam" ? <SteamPerformanceSettings locale={locale} appId={appId} /> : <>
      <ToggleField bottomSeparator="none" label={copy[1]} checked={state?.enabled === true} disabled={disabled}
        onChange={value => change({ enabled: value })} />
      {state?.enabled && <>
        <ToggleField bottomSeparator="none" label={copy[2]} checked={state.transparent} disabled={disabled}
          onChange={value => change({ transparent: value })} />
        <div className="qsDropdownFullWidth"><DropdownItem bottomSeparator="none" layout="below" label={colors[0]}
          selectedOption={state.textColor ?? "white"} disabled={disabled}
          rgOptions={colorValues.map((value, index) => ({ data: value, label: colors[index + 1] }))}
          onChange={option => change({ textColor: option.data as LivePerformanceOptions["textColor"] })} /></div>
        <div className="qsDropdownFullWidth"><DropdownItem bottomSeparator="none" layout="below"
          label={steamLabel("#Settings_InGame_PerfOverlayDetailLevel", "Detail level")} disabled={disabled}
          selectedOption={state.detail} rgOptions={details.map((value, index) => ({ data: index + 1,
            label: steamLabel(`#Settings_InGame_PerfOverlay_${value}`, detailFallback[index]) }))}
          onChange={option => change({ detail: option.data as LivePerformanceOptions["detail"] })} /></div>
        <div className="qsDropdownFullWidth"><DropdownItem bottomSeparator="none" layout="below"
          label={steamLabel("#Settings_InGame_PerformanceMonitor", "Position")} disabled={disabled}
          selectedOption={state.corner} rgOptions={cornerTokens.map((value, index) => ({ data: index + 1,
            label: steamLabel(`#Corner_${value}`, cornerFallback[index]) }))}
          onChange={option => change({ corner: option.data as LivePerformanceOptions["corner"] })} /></div>
        {description && <div className="qsMeta" role="status">{description}</div>}
      </>}
      {!state && <div className="qsMeta" role="status">{view.loading ? common.loading : common.unavailable}</div>}
    </>}
    {((source === "live" && view.failed) || sourceFailed) && <div className="qsMeta" role="status">{common.saveError} <DialogButton
      disabled={changing || view.busy} onClick={() => { setSourceFailed(false); void runtime.refresh(); }}>{common.retry}</DialogButton></div>}
  </>;
}

import { call } from "./controlBackend";
import { DFL, SP_REACT as React } from "./decky";

const { ButtonItem, PanelSection, PanelSectionRow } = DFL as any;
const copy: Record<string, string[]> = {
  en: ["Frame timing", "Measure FPS", "Measuring…", "Four-second sample of the current game.", "Presented FPS", "Frame time", "No game frames captured. Try during play.", "Frame measurement is unavailable.", "Windows denied access to frame data."],
  it: ["Tempi dei fotogrammi", "Misura FPS", "Misurazione…", "Campione di quattro secondi del gioco in corso.", "FPS presentati", "Tempo per fotogramma", "Nessun fotogramma rilevato. Riprova durante il gioco.", "Misurazione dei fotogrammi non disponibile.", "Windows ha negato l’accesso ai dati dei fotogrammi."],
  de: ["Framezeiten", "FPS messen", "Messung läuft…", "Vier Sekunden aus dem laufenden Spiel.", "Ausgegebene FPS", "Framezeit", "Keine Frames erfasst. Versuche es im Spiel erneut.", "Frame-Messung nicht verfügbar.", "Windows hat den Zugriff auf Frame-Daten verweigert."],
  es: ["Tiempo de fotogramas", "Medir FPS", "Midiendo…", "Muestra de cuatro segundos del juego actual.", "FPS presentados", "Tiempo por fotograma", "No se capturaron fotogramas. Prueba durante la partida.", "La medición de fotogramas no está disponible.", "Windows denegó el acceso a los datos de fotogramas."],
  fr: ["Temps d’image", "Mesurer les FPS", "Mesure en cours…", "Échantillon de quatre secondes du jeu en cours.", "FPS présentées", "Temps par image", "Aucune image capturée. Réessayez en jeu.", "La mesure des images est indisponible.", "Windows a refusé l’accès aux données d’image."],
  pt: ["Tempos dos quadros", "Medir FPS", "A medir…", "Amostra de quatro segundos do jogo em curso.", "FPS apresentados", "Tempo por quadro", "Nenhum quadro capturado. Tente durante o jogo.", "A medição de quadros não está disponível.", "O Windows negou acesso aos dados dos quadros."],
  uk: ["Час кадрів", "Виміряти FPS", "Вимірювання…", "Чотирисекундний зразок поточної гри.", "Подані FPS", "Час кадру", "Кадри не зафіксовано. Повторіть під час гри.", "Вимірювання кадрів недоступне.", "Windows відхилила доступ до даних кадрів."],
  ru: ["Время кадра", "Измерить FPS", "Измерение…", "Четыре секунды текущей игры.", "Переданные FPS", "Время кадра", "Кадры не получены. Повторите во время игры.", "Измерение кадров недоступно.", "Windows отказала в доступе к данным кадров."],
  ja: ["フレーム時間", "FPS を測定", "測定中…", "実行中のゲームを4秒間測定します。", "提示 FPS", "フレーム時間", "フレームを取得できませんでした。プレイ中にお試しください。", "フレーム測定を利用できません。", "Windows がフレームデータへのアクセスを拒否しました。"],
  ko: ["프레임 시간", "FPS 측정", "측정 중…", "실행 중인 게임을 4초간 측정합니다.", "제출 FPS", "프레임 시간", "프레임을 캡처하지 못했습니다. 플레이 중 다시 시도하세요.", "프레임을 측정할 수 없습니다.", "Windows가 프레임 데이터 접근을 거부했습니다."],
  zh: ["影格時間", "測量 FPS", "測量中…", "擷取目前遊戲的四秒樣本。", "提交的 FPS", "每格耗時", "未擷取到影格。請在遊玩時再試。", "無法測量影格。", "Windows 拒絕存取影格資料。"],
  hi: ["फ़्रेम का समय", "FPS मापें", "माप जारी है…", "चल रहे गेम का चार सेकंड का नमूना।", "प्रस्तुत FPS", "प्रति फ़्रेम समय", "कोई फ़्रेम नहीं मिला। खेलते समय फिर कोशिश करें।", "फ़्रेम का माप उपलब्ध नहीं है।", "Windows ने फ़्रेम डेटा की अनुमति नहीं दी।"],
};

type Measurement = { status: string; presentedFps?: number; medianFrameTimeMs?: number; p95FrameTimeMs?: number };

export function GameFrameTiming({ appId, locale }: { appId: number; locale: string }) {
  const [pending, setPending] = React.useState(false);
  const [result, setResult] = React.useState<Measurement | null>(null);
  const operation = React.useRef(0);
  React.useEffect(() => () => { operation.current++; }, []);
  const [title, measure, measuring, hint, fps, frame, empty, unavailable, denied] = copy[locale.split(/[-_]/)[0]] ?? copy.en;
  const ready = result?.status === "Ready" && Number.isFinite(result.presentedFps) && Number.isFinite(result.medianFrameTimeMs);
  const measureFrames = async () => {
    if (pending) return;
    const id = ++operation.current;
    setPending(true);
    setResult(null);
    let response: Measurement;
    try { response = await call<[number], Measurement>("measure_game_frames", appId); }
    catch { response = { status: "ProviderUnavailable" }; }
    if (id !== operation.current) return;
    setPending(false);
    setResult(response);
  };
  const format = (value: number | undefined) => value?.toLocaleString(locale.replace(/_/g, "-"), { maximumFractionDigits: 1 });
  return <PanelSection title={title}>
    <PanelSectionRow><ButtonItem layout="below" bottomSeparator="none" disabled={pending} onClick={() => void measureFrames()}>
      <span style={{ whiteSpace: "normal", overflowWrap: "anywhere" }}>{pending ? measuring : measure}</span>
    </ButtonItem></PanelSectionRow>
    <div role="status" style={{ padding: "4px 16px 12px", fontSize: 13, lineHeight: 1.5, overflowWrap: "anywhere" }}>
      {ready ? <dl style={{ margin: 0, display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(min(100%, 120px), 1fr))", gap: 8 }}>
        <div><dt>{fps}</dt><dd style={{ margin: 0, fontWeight: 650 }}>{format(result.presentedFps)}</dd></div>
        <div><dt>{frame}</dt><dd style={{ margin: 0, fontWeight: 650 }}>{format(result.medianFrameTimeMs)} ms</dd>
          {Number.isFinite(result.p95FrameTimeMs) && <dd style={{ margin: 0, opacity: .72 }}>P95 · {format(result.p95FrameTimeMs)} ms</dd>}</div>
      </dl> : !result ? hint : result.status === "PermissionDenied" ? denied :
        ["NoData", "TargetUnavailable"].includes(result.status) ? empty : unavailable}
    </div>
  </PanelSection>;
}

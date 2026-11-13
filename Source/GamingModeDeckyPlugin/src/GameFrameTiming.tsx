import { call } from "./controlBackend";
import { DFL, SP_REACT as React } from "./decky";
import { gameFrameComparison } from "./gameFrameComparison";

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

const comparisonCopy: Record<string, readonly string[]> = {
  en: ["Measure first run", "Measure second run", "Repeat second run", "Clear results", "Use the same route and graphics settings for both runs.", "Close Quick Access within 3 seconds. The sample lasts 4 seconds.", "First", "Second", "Change", "Return to the game before the sample starts."],
  it: ["Misura prima prova", "Misura seconda prova", "Ripeti seconda prova", "Cancella risultati", "Usa lo stesso percorso e le stesse impostazioni grafiche per entrambe le prove.", "Chiudi il menu rapido entro 3 secondi. La misura dura 4 secondi.", "Prima", "Seconda", "Variazione", "Torna al gioco prima che inizi la misurazione."],
  de: ["Ersten Lauf messen", "Zweiten Lauf messen", "Zweiten Lauf wiederholen", "Ergebnisse löschen", "Nutze in beiden Läufen dieselbe Strecke und dieselben Grafikeinstellungen.", "Schließe den Schnellzugriff in 3 Sekunden. Die Messung dauert 4 Sekunden.", "Erster", "Zweiter", "Änderung", "Wechsle vor Messbeginn zurück ins Spiel."],
  es: ["Medir primera prueba", "Medir segunda prueba", "Repetir segunda prueba", "Borrar resultados", "Usa el mismo recorrido y los mismos ajustes gráficos en ambas pruebas.", "Cierra el acceso rápido en 3 segundos. La medición dura 4 segundos.", "Primera", "Segunda", "Cambio", "Vuelve al juego antes de que comience la medición."],
  fr: ["Mesurer le premier essai", "Mesurer le second essai", "Refaire le second essai", "Effacer les résultats", "Suivez le même parcours et gardez les mêmes réglages graphiques pour les deux essais.", "Fermez l’accès rapide sous 3 secondes. La mesure dure 4 secondes.", "Premier", "Second", "Écart", "Revenez au jeu avant le début de la mesure."],
  pt: ["Medir primeiro teste", "Medir segundo teste", "Repetir segundo teste", "Limpar resultados", "Use o mesmo percurso e as mesmas definições gráficas nos dois testes.", "Feche o acesso rápido em 3 segundos. A medição dura 4 segundos.", "Primeiro", "Segundo", "Variação", "Volte ao jogo antes de a medição começar."],
  uk: ["Виміряти перший прохід", "Виміряти другий прохід", "Повторити другий прохід", "Очистити результати", "Використовуйте той самий маршрут і налаштування графіки в обох проходах.", "Закрийте швидкий доступ за 3 секунди. Вимірювання триває 4 секунди.", "Перший", "Другий", "Зміна", "Поверніться до гри до початку вимірювання."],
  ru: ["Измерить первый проход", "Измерить второй проход", "Повторить второй проход", "Очистить результаты", "Используйте один маршрут и одинаковые настройки графики в обоих проходах.", "Закройте быстрый доступ за 3 секунды. Измерение длится 4 секунды.", "Первый", "Второй", "Изменение", "Вернитесь в игру до начала измерения."],
  ja: ["1 回目を測定", "2 回目を測定", "2 回目を再測定", "結果を消去", "同じルートとグラフィック設定で 2 回プレイしてください。", "3 秒以内にクイックアクセスを閉じてください。測定は 4 秒間です。", "1 回目", "2 回目", "差分", "測定開始前にゲームに戻ってください。"],
  ko: ["첫 번째 측정", "두 번째 측정", "두 번째 다시 측정", "결과 지우기", "두 번 모두 같은 경로와 그래픽 설정을 사용하세요.", "3초 안에 빠른 메뉴를 닫으세요. 측정은 4초간 진행됩니다.", "첫 번째", "두 번째", "차이", "측정이 시작되기 전에 게임으로 돌아가세요."],
  zh: ["測量第一次", "測量第二次", "重測第二次", "清除結果", "兩次測量請使用相同路線與畫面設定。", "請在 3 秒內關閉快速存取選單。測量將持續 4 秒。", "第一次", "第二次", "差異", "請在測量開始前返回遊戲。"],
  hi: ["पहली बार मापें", "दूसरी बार मापें", "दूसरा माप दोहराएँ", "नतीजे साफ़ करें", "दोनों बार एक ही रास्ता और ग्राफ़िक्स सेटिंग इस्तेमाल करें।", "3 सेकंड में क्विक ऐक्सेस बंद करें। माप 4 सेकंड चलेगा।", "पहला", "दूसरा", "अंतर", "माप शुरू होने से पहले गेम में लौटें।"],
};

export function GameFrameTiming({ appId, locale }: { appId: number; locale: string }) {
  const language = locale.split(/[-_]/)[0];
  const [title, , measuring, , fps, frame, empty, unavailable, denied] = copy[language] ?? copy.en;
  const [measureFirst, measureSecond, repeat, clear, hint, prepare, firstLabel, secondLabel, change, returnToGame] = comparisonCopy[language] ?? comparisonCopy.en;
  const [result, setResult] = React.useState(() => gameFrameComparison.read(appId));
  React.useEffect(() => {
    const sync = () => setResult(gameFrameComparison.read(appId));
    const unsubscribe = gameFrameComparison.subscribe(sync);
    sync();
    return unsubscribe;
  }, [appId]);
  const { busy, first, second, error } = result;
  const measure = second ? repeat : first ? measureSecond : measureFirst;
  const format = (value: number, signed = false) => value.toLocaleString(locale.replace(/_/g, "-"), {
    maximumFractionDigits: 2, signDisplay: signed ? "exceptZero" : "auto",
  });
  const message = busy ? prepare : error === "GameNotForeground" ? returnToGame :
    error === "PermissionDenied" ? denied : error === "NoData" || error === "TargetUnavailable" ? empty : error ? unavailable : hint;
  const rows = first ? [
    { label: fps, before: first.presentedFps, after: second?.presentedFps, unit: "" },
    { label: frame, before: first.medianFrameTimeMs, after: second?.medianFrameTimeMs, unit: " ms" },
    { label: "P95", before: first.p95FrameTimeMs, after: second?.p95FrameTimeMs, unit: " ms" },
  ] : [];
  return <PanelSection title={title}>
    <PanelSectionRow><ButtonItem layout="below" bottomSeparator="none" disabled={busy} onClick={() => void gameFrameComparison.capture(appId,
      id => call<[number, boolean], unknown>("measure_game_frames", id, true))}>
      <span style={{ whiteSpace: "normal", overflowWrap: "anywhere" }}>{busy ? measuring : measure}</span>
    </ButtonItem></PanelSectionRow>
    <div role="status" style={{ padding: "4px 16px 12px", fontSize: 13, lineHeight: 1.5, overflowWrap: "anywhere" }}>
      <div>{message}</div>
      {!busy && !error && <div style={{ opacity: .74 }}>{prepare}</div>}
      {first && <dl style={{ display: "grid", gap: 8, margin: "12px 0 0" }}>
        {rows.map(({ label, before, after, unit }) => <div key={label}>
          <dt style={{ fontWeight: 650 }}>{label}</dt>
          <dd style={{ margin: 0, display: "flex", flexWrap: "wrap", columnGap: 12 }}>
            <span>{firstLabel} · {format(before)}{unit}</span>
            {after !== undefined && <><span>{secondLabel} · {format(after)}{unit}</span>
              <span>{change} · {format(Number((after - before).toFixed(2)), true)}{unit}</span></>}
          </dd>
        </div>)}
      </dl>}
    </div>
    {first && <PanelSectionRow><ButtonItem layout="below" bottomSeparator="none" disabled={busy} onClick={() => gameFrameComparison.reset(appId)}>
      <span style={{ whiteSpace: "normal", overflowWrap: "anywhere" }}>{clear}</span>
    </ButtonItem></PanelSectionRow>}
  </PanelSection>;
}

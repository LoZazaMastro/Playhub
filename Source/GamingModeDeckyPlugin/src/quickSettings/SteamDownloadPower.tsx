import { DialogButton } from "@decky/ui";
import { useEffect, useState } from "react";
import { call } from "../controlBackend";
import { steamAppId } from "../controlledQamFocus";
import { DFL } from "../decky";
import { activeLocalDownload, createSteamDownloadPower, type DownloadPowerRequest } from "./downloadPowerController";

const copy: Record<string, readonly string[]> = {
  en: ["Finish Steam download", "Stop keeping awake", "On AC power, Windows can turn off the display while Steam finishes the current download.", "PC stays awake for this download.", "Could not start download power mode."],
  it: ["Completa il download Steam", "Termina modalità download", "Con l’alimentazione collegata, Windows può spegnere lo schermo mentre Steam completa il download.", "Il PC resta sveglio per questo download.", "Impossibile avviare la modalità download."],
  de: ["Steam-Download abschließen", "Wachhalten beenden", "Am Netz kann Windows den Bildschirm ausschalten, während Steam den Download beendet.", "Der PC bleibt für diesen Download wach.", "Downloadmodus konnte nicht gestartet werden."],
  es: ["Completar descarga de Steam", "Dejar de mantener activo", "Con el cargador conectado, Windows puede apagar la pantalla mientras Steam completa la descarga.", "El PC se mantiene activo durante la descarga.", "No se pudo iniciar el modo de descarga."],
  fr: ["Terminer le téléchargement Steam", "Autoriser la veille", "Sur secteur, Windows peut éteindre l’écran pendant que Steam termine le téléchargement.", "Le PC reste éveillé pendant ce téléchargement.", "Impossible de démarrer le mode téléchargement."],
  pt: ["Concluir transferência do Steam", "Permitir suspensão", "Ligado à corrente, o Windows pode desligar o ecrã enquanto o Steam termina a transferência.", "O PC fica ativo durante esta transferência.", "Não foi possível iniciar o modo de transferência."],
  uk: ["Завершити завантаження Steam", "Дозволити перехід у сон", "Під час живлення від мережі Windows може вимкнути екран, поки Steam завершує завантаження.", "ПК не засинатиме під час цього завантаження.", "Не вдалося запустити режим завантаження."],
  ru: ["Завершить загрузку Steam", "Разрешить переход в сон", "При питании от сети Windows может отключить экран, пока Steam завершает загрузку.", "ПК не будет переходить в сон во время загрузки.", "Не удалось запустить режим загрузки."],
  ja: ["Steam のダウンロードを完了", "スリープを許可", "電源接続中は、Steam のダウンロード中に Windows が画面を消灯できます。", "このダウンロード中はスリープしません。", "ダウンロードモードを開始できませんでした。"],
  ko: ["Steam 다운로드 완료하기", "절전 모드 허용", "전원에 연결되어 있으면 Steam 다운로드 중에 Windows가 화면을 끌 수 있습니다.", "이 다운로드가 끝날 때까지 PC가 깨어 있습니다.", "다운로드 모드를 시작할 수 없습니다."],
  zh: ["完成 Steam 下載", "允許睡眠", "接上電源時，Steam 下載期間 Windows 可以關閉顯示器。", "這次下載完成前，電腦會保持喚醒。", "無法啟動下載模式。"],
  hi: ["Steam डाउनलोड पूरा करें", "स्लीप की अनुमति दें", "बिजली से जुड़े होने पर Steam डाउनलोड के दौरान Windows स्क्रीन बंद कर सकता है।", "इस डाउनलोड के लिए पीसी चालू रहेगा।", "डाउनलोड मोड शुरू नहीं हो सका।"],
};

let unsubscribe: (() => void) | undefined;
let latestApp = 0;
let latestAt = -Infinity;
const host = () => window as any;
function activeDownload() {
  try {
    const steam = host();
    const router = (DFL as any).Router;
    const main = (steam.SteamUIStore?.WindowStore ?? router?.WindowStore)?.GamepadUIMainWindowInstance;
    const running = steamAppId(steam.SteamUIStore?.MainRunningAppID ?? router?.MainRunningAppID);
    return !running && main?.IsGamepadUIOverlayWindow?.() === false && performance.now() - latestAt < 7_000 ? latestApp : 0;
  } catch { return 0; }
}

const downloadPower = createSteamDownloadPower(activeDownload, request =>
  call<[DownloadPowerRequest], { ok?: boolean; lease?: string }>("set_steam_download_power", request));

function observeSteamDownloads() {
  if (unsubscribe) return;
  const downloads = host().SteamClient?.Downloads;
  if (typeof downloads?.RegisterForDownloadOverview !== "function") return;
  try {
    const connection = downloads.RegisterForDownloadOverview((entry: { remote_client_id?: string }) => {
      if (entry?.remote_client_id !== "0") return;
      latestAt = performance.now();
      latestApp = activeLocalDownload(entry);
      downloadPower.refresh();
    });
    unsubscribe = () => connection?.unregister?.();
  } catch { /* Unsupported Steam client or browser. */ }
}

export function stopSteamDownloadPower() {
  unsubscribe?.();
  unsubscribe = undefined;
  latestAt = -Infinity;
  latestApp = 0;
  void downloadPower.stop();
}

export function SteamDownloadPower({ locale }: { locale: string }) {
  const [state, setState] = useState(downloadPower.read);
  useEffect(() => {
    const update = () => setState(downloadPower.read());
    const remove = downloadPower.subscribe(update);
    observeSteamDownloads();
    update();
    const timer = window.setInterval(update, 2_000);
    return () => { remove(); window.clearInterval(timer); };
  }, []);

  if (!state.available && !state.active) return null;
  const [start, stop, detail, active, failed] = copy[locale.split(/[-_]/)[0]] ?? copy.en;
  return <div className="qsCardBody" aria-busy={state.busy}>
    <DialogButton disabled={state.busy} onClick={() => void (state.active ? downloadPower.stop() : downloadPower.start())}>
      <span style={{ whiteSpace: "normal", overflowWrap: "anywhere" }}>{state.active ? stop : start}</span>
    </DialogButton>
    <div className="qsMeta" role="status">{state.failed ? failed : state.active ? active : detail}</div>
  </div>;
}

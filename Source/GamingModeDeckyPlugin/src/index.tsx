import { readOwnedGameApp, resolveOwnedGameApp } from "./steamOwnedLaunchDispatcher";
import { installCirclesScreensaver } from "./circlesScreensaver";
import { pollStatus } from "./statusPoll";
import { agentAvailability } from "./agentAvailability";
import { optionalAgentCopy } from "./optionalAgentLocale";
import { disposeDisplayConfirmation } from "./quickSettings";
import { initializeQamPreferences } from "./QamSettings";
import { installHomeNews, notifyHomeNewsLanguageChanged } from "./HomeNews";
import { definePlugin, routerHook, toaster, DFL, SP_REACT as React } from "./decky";
import { acknowledgeSteamModeCommand, captureDashboardSourceFocus, confirmBigPictureForeground, consumeOpenRequest, logToAgent, readEnvironment, readSettings, writeSettings, API_BASE, requestDashboardSteamFocus, restoreDashboardSourceFocus } from "./api";
import { createSteamKeyFocusReset, createSteamNavigationRecovery, createSteamNavigationWakeup, createSteamOverlayReturnScope, focusReturnedSteamGameAction, hasVisibleSteamNavigationFocus, isConfirmedSteamIdle, isSteamNavigationReturnApp, navigateReturnedSteamGame, recoverSteamNavigation, releaseExitedSteamOverlay, type SteamOverlayScope, type SteamReturnOverlayWindow } from "./steamNavigationReturn";
import { activeSteamController, createSteamReturnControllerLayout } from "./steamReturnControllerLayout";
import { observeSteamOverlayFocus } from "./steamFocusEvents";
import { installSteamModeCommands } from "./steamModeCommands";
import { matchesOverlayApp } from "./dashboardOverlay";
import { DashboardPage, DASHBOARD_ROUTE, clearDashboardChrome, focusDashboardSurface, markDashboardChrome, preloadDashboardWindows, prepareDashboardOverlay, resetDashboardExit } from "./DashboardPage";
import { configureNavigationHaptics, installNavigationHaptics, getNavigationHapticsConfig, getNavigationHapticsRevision, subscribeNavigationHaptics } from "./navigationHaptics";
import { installPowerMenuPatch } from "./powerMenuPatch";
import { initPlayhubQam } from "./qam";
import { PlayhubQamToggle } from "./PlayhubQamToggle";
import { getPlayhubDiscoverCopy, getModeQuestion } from "./qamLocale";
import { PluginStorePage, PLUGIN_STORE_ROUTE } from "./PluginStorePage";
import { preloadPluginStoreCatalog } from "./pluginStoreData";
import { PlayhubIcon } from "./PlayhubIcon";
import playhubWordmark from "../assets/playhub-wordmark.png";
import { TbShoppingBag } from "react-icons/tb";
import { ControlCenter } from "./ControlCenter";
import { GameFrameTiming } from "./GameFrameTiming";
import { steamAppId } from "./controlledQamFocus";
import { PluginStoreIntro } from "./PluginStoreIntro";
import { initDeckyHost } from "./deckyHost";
import { call as controlCall } from "./controlBackend";
import { installPlayhubOnboarding, refreshOnboardingLocale } from "./onboardingIntegration";
import { installQuickSettings } from "./quickSettings";
import { installDeckyIpcRecovery, type IpcFailure } from "./deckyIpcRecovery";
import { installControlledQamFocus } from "./controlledQamIntegration";
import { createSteamConfiguratorReturn, type SteamConfiguratorReturn } from "./steamConfiguratorReturn";
import { installLivePerformance } from "./quickSettings/livePerformanceRuntime";

const { useState, useEffect, useMemo } = React;
let hapticSettingsWrite = Promise.resolve();
function saveHapticSettings(changes: Parameters<typeof writeSettings>[0]) {
  hapticSettingsWrite = hapticSettingsWrite.catch(() => {}).then(async () => { await writeSettings(changes); });
}
const playhubTitle = <img src={playhubWordmark} alt="Playhub" style={{ display: "block", width: 144, maxWidth: "100%", height: 36, objectFit: "contain", objectPosition: "left center" }} />;
function ModeButtonLabel({ mode }: { mode: "gaming" | "desktop" }) {
  return <span className="ph-mode-action-label"><span>{mode === "gaming" ? "Gaming" : "Desktop"}</span><span>Mode</span></span>;
}
const { PanelSection, PanelSectionRow, ButtonItem, DialogButton, Focusable, DropdownItem, ToggleField, SliderField, Navigation, Router, staticClasses, ConfirmModal, showModal } = DFL as any;

// IL PLUGIN GAMING MODE.
//
// Questo file contiene TUTTO quello che il plugin ha sempre fatto - cambio
// modalita', avvio predefinito, stato dell'agente, traduzioni, salvataggio del
// primo piano - piu' la nuova pagina Playhub Dashboard. Niente e' stato tolto:
// se aggiungi qualcosa qui dentro, controlla di non star sostituendo un pezzo
// che serviva gia' a qualcuno.
//
// SUL CONTROLLER. Quando l'interfaccia di Steam ha il fuoco, il pad lo gestisce
// Steam: navigazione, evidenziazione e icone dei tasti arrivano gratis. Noi non
// leggiamo nessun controller, mai. E' esattamente la strada che faceva
// impazzire lo Steam Controller.

// ---------------------------------------------------------------------------
// Traduzioni
// ---------------------------------------------------------------------------

interface Strings {
  controlCentre: string;
  mode: string;
  switchGaming: string;
  switchDesktop: string;
  defaultStartup: string;
  desktopMode: string;
  gamingMode: string;
  notConnected: string;
  agentReturned: string;
  dashboard: string;
  openDashboard: string;
  dashboardShortcut: string;
  dashboardSteamInput: string;
  haptics: string;
  hapticsDescription: string;
  hapticsIntensity: string;
  restartGaming: string;
  restartDesktop: string;
  restartConfirm: string;
  restartNow: string;
  cancel: string;
}

const strings: Record<string, Strings> = {
  en: {
    controlCentre: "Control center",
    mode: "Startup mode",
    switchGaming: "Switch to Gaming Mode",
    switchDesktop: "Switch to Desktop Mode",
    defaultStartup: "Default startup",
    desktopMode: "Desktop Mode",
    gamingMode: "Gaming Mode",
    notConnected: "Agent not connected",
    agentReturned: "Agent returned",
    dashboard: "Playhub Dashboard",
    openDashboard: "Open Dashboard",
    dashboardShortcut: "Open it with CTRL + ALT + P",
    dashboardSteamInput: "In Steam Input, assign this shortcut to Guide + any button for instant controller access.",
    haptics: "Controller feedback",
    hapticsDescription: "Adds tactile feedback while navigating Steam.",
    hapticsIntensity: "Feedback intensity",
    restartGaming: "Restart in Gaming Mode", restartDesktop: "Restart in Desktop Mode",
    restartConfirm: "The system will restart now. Continue?", restartNow: "Restart", cancel: "Cancel",
  },
  it: {
    controlCentre: "Centro di controllo",
    mode: "Modalità del PC",
    switchGaming: "Passa alla modalità Gaming",
    switchDesktop: "Passa alla modalità Desktop",
    defaultStartup: "Avvio predefinito",
    desktopMode: "Modalità Desktop",
    gamingMode: "Modalità Gaming",
    notConnected: "Agent non collegato",
    agentReturned: "Agent ha risposto",
    dashboard: "Playhub Dashboard",
    openDashboard: "Apri Dashboard",
    dashboardShortcut: "Aprila con CTRL + ALT + P",
    dashboardSteamInput: "In Steam Input, assegna questa scorciatoia a Guida + un pulsante per aprirla subito dal controller.",
    haptics: "Feedback del controller",
    hapticsDescription: "Aggiunge una risposta tattile mentre navighi in Steam.",
    hapticsIntensity: "Intensità del feedback",
    restartGaming: "Riavvia in Gaming Mode", restartDesktop: "Riavvia in Desktop Mode",
    restartConfirm: "Il sistema verrà riavviato ora. Vuoi continuare?", restartNow: "Riavvia", cancel: "Annulla",
  },
  es: {
    controlCentre: "Centro de control",
    mode: "Modo de inicio",
    switchGaming: "Cambiar al modo Gaming",
    switchDesktop: "Cambiar al modo Escritorio",
    defaultStartup: "Inicio predeterminado",
    desktopMode: "Modo Escritorio",
    gamingMode: "Modo Gaming",
    notConnected: "Agente no conectado",
    agentReturned: "El agente devolvió",
    dashboard: "Playhub Dashboard",
    openDashboard: "Abrir Dashboard",
    dashboardShortcut: "Ábrelo con CTRL + ALT + P",
    dashboardSteamInput: "En Steam Input, asigna este atajo a Guía + un botón para abrirlo al instante con el mando.",
    haptics: "Respuesta del mando", hapticsDescription: "Añade respuesta táctil al navegar por Steam.", hapticsIntensity: "Intensidad",
    restartGaming: "Reiniciar en modo Gaming", restartDesktop: "Reiniciar en modo Escritorio",
    restartConfirm: "El sistema se reiniciará ahora. ¿Quieres continuar?", restartNow: "Reiniciar", cancel: "Cancelar",
  },
  fr: {
    controlCentre: "Centre de contrôle",
    mode: "Mode de démarrage",
    switchGaming: "Passer en mode Gaming",
    switchDesktop: "Passer en mode Bureau",
    defaultStartup: "Démarrage par défaut",
    desktopMode: "Mode Bureau",
    gamingMode: "Mode Gaming",
    notConnected: "Agent non connecté",
    agentReturned: "Agent a renvoyé",
    dashboard: "Playhub Dashboard",
    openDashboard: "Ouvrir le Dashboard",
    dashboardShortcut: "Ouvrez-le avec CTRL + ALT + P",
    dashboardSteamInput: "Dans Steam Input, associez ce raccourci à Guide + un bouton pour l'ouvrir instantanément avec la manette.",
    haptics: "Retour de la manette", hapticsDescription: "Ajoute un retour tactile lors de la navigation dans Steam.", hapticsIntensity: "Intensité",
    restartGaming: "Redémarrer en mode Gaming", restartDesktop: "Redémarrer en mode Bureau",
    restartConfirm: "Le système va redémarrer maintenant. Continuer ?", restartNow: "Redémarrer", cancel: "Annuler",
  },
  de: {
    controlCentre: "Kontrollzentrum",
    mode: "Startmodus",
    switchGaming: "In den Gaming-Modus wechseln",
    switchDesktop: "In den Desktop-Modus wechseln",
    defaultStartup: "Standardstart",
    desktopMode: "Desktop-Modus",
    gamingMode: "Gaming-Modus",
    notConnected: "Agent nicht verbunden",
    agentReturned: "Agent meldete",
    dashboard: "Playhub Dashboard",
    openDashboard: "Dashboard öffnen",
    dashboardShortcut: "Mit CTRL + ALT + P öffnen",
    dashboardSteamInput: "Weise diese Tastenkombination in Steam Input Guide + einer Taste zu, um das Dashboard direkt per Controller zu öffnen.",
    haptics: "Controller-Feedback", hapticsDescription: "Fügt beim Navigieren in Steam taktiles Feedback hinzu.", hapticsIntensity: "Intensität",
    restartGaming: "Im Gaming-Modus neu starten", restartDesktop: "Im Desktop-Modus neu starten",
    restartConfirm: "Das System wird jetzt neu gestartet. Fortfahren?", restartNow: "Neu starten", cancel: "Abbrechen",
  },
  pt: {
    controlCentre: "Central de controle",
    mode: "Modo de arranque",
    switchGaming: "Mudar para modo Gaming",
    switchDesktop: "Mudar para modo Desktop",
    defaultStartup: "Arranque predefinido",
    desktopMode: "Modo Desktop",
    gamingMode: "Modo Gaming",
    notConnected: "Agente não ligado",
    agentReturned: "Agente devolveu",
    dashboard: "Playhub Dashboard",
    openDashboard: "Abrir Dashboard",
    dashboardShortcut: "Abra com CTRL + ALT + P",
    dashboardSteamInput: "No Steam Input, atribua este atalho a Guia + um botão para abrir imediatamente pelo comando.",
    haptics: "Resposta do controle", hapticsDescription: "Adiciona resposta tátil ao navegar pelo Steam.", hapticsIntensity: "Intensidade",
    restartGaming: "Reiniciar no modo Gaming", restartDesktop: "Reiniciar no modo Desktop",
    restartConfirm: "O sistema será reiniciado agora. Continuar?", restartNow: "Reiniciar", cancel: "Cancelar",
  },
  uk: {
    controlCentre: "Центр керування",
    mode: "Режим запуску", switchGaming: "Перейти в ігровий режим", switchDesktop: "Перейти в режим робочого столу",
    defaultStartup: "Типовий запуск", desktopMode: "Режим робочого столу", gamingMode: "Ігровий режим",
    notConnected: "Агент не підключено", agentReturned: "Агент повернув",
    dashboard: "Панель Playhub", openDashboard: "Відкрити панель",
    dashboardShortcut: "Відкрийте за допомогою CTRL + ALT + P",
    dashboardSteamInput: "У Steam Input призначте це сполучення на Guide + будь-яку кнопку для миттєвого доступу з контролера.",
    haptics: "Відгук контролера", hapticsDescription: "Додає тактильний відгук під час навігації Steam.", hapticsIntensity: "Інтенсивність",
    restartGaming: "Перезапустити в ігровому режимі", restartDesktop: "Перезапустити в режимі робочого столу",
    restartConfirm: "Систему буде перезапущено зараз. Продовжити?", restartNow: "Перезапустити", cancel: "Скасувати",
  },
  zh: {
    controlCentre: "控制中心",
    mode: "启动模式", switchGaming: "切换到游戏模式", switchDesktop: "切换到桌面模式",
    defaultStartup: "默认启动模式", desktopMode: "桌面模式", gamingMode: "游戏模式",
    notConnected: "代理未连接", agentReturned: "代理返回",
    dashboard: "Playhub 控制面板", openDashboard: "打开控制面板",
    dashboardShortcut: "按 CTRL + ALT + P 打开",
    dashboardSteamInput: "在 Steam Input 中，将此快捷键绑定到 Guide + 任意按钮，即可通过控制器快速打开。",
    haptics: "手柄反馈", hapticsDescription: "在 Steam 中导航时提供触觉反馈。", hapticsIntensity: "反馈强度",
    restartGaming: "重启到游戏模式", restartDesktop: "重启到桌面模式",
    restartConfirm: "系统将立即重启。是否继续？", restartNow: "重启", cancel: "取消",
  },
  ja: {
    controlCentre: "コントロールセンター",
    mode: "起動モード", switchGaming: "ゲーミングモードに切り替え", switchDesktop: "デスクトップモードに切り替え",
    defaultStartup: "既定の起動", desktopMode: "デスクトップモード", gamingMode: "ゲーミングモード",
    notConnected: "エージェントに接続されていません", agentReturned: "エージェントの応答",
    dashboard: "Playhub ダッシュボード", openDashboard: "ダッシュボードを開く",
    dashboardShortcut: "CTRL + ALT + P で開きます",
    dashboardSteamInput: "Steam Input でこのショートカットを Guide + 任意のボタンに割り当てると、コントローラーからすぐに開けます。",
    haptics: "コントローラーのフィードバック", hapticsDescription: "Steam の操作に触覚フィードバックを加えます。", hapticsIntensity: "フィードバックの強さ",
    restartGaming: "ゲーミングモードで再起動", restartDesktop: "デスクトップモードで再起動",
    restartConfirm: "システムを今すぐ再起動します。続行しますか？", restartNow: "再起動", cancel: "キャンセル",
  },
  ko: {
    controlCentre: "제어 센터",
    mode: "시작 모드", switchGaming: "게이밍 모드로 전환", switchDesktop: "데스크톱 모드로 전환",
    defaultStartup: "기본 시작", desktopMode: "데스크톱 모드", gamingMode: "게이밍 모드",
    notConnected: "에이전트가 연결되지 않음", agentReturned: "에이전트 응답",
    dashboard: "Playhub 대시보드", openDashboard: "대시보드 열기",
    dashboardShortcut: "CTRL + ALT + P로 열기",
    dashboardSteamInput: "Steam Input에서 이 단축키를 Guide + 원하는 버튼에 지정하면 컨트롤러로 즉시 열 수 있습니다.",
    haptics: "컨트롤러 피드백", hapticsDescription: "Steam을 탐색할 때 촉각 피드백을 추가합니다.", hapticsIntensity: "피드백 강도",
    restartGaming: "게이밍 모드로 다시 시작", restartDesktop: "데스크톱 모드로 다시 시작",
    restartConfirm: "시스템을 지금 다시 시작합니다. 계속하시겠습니까?", restartNow: "다시 시작", cancel: "취소",
  },
  hi: {
    controlCentre: "कंट्रोल सेंटर",
    mode: "स्टार्टअप मोड", switchGaming: "गेमिंग मोड पर जाएं", switchDesktop: "डेस्कटॉप मोड पर जाएं",
    defaultStartup: "डिफ़ॉल्ट स्टार्टअप", desktopMode: "डेस्कटॉप मोड", gamingMode: "गेमिंग मोड",
    notConnected: "एजेंट कनेक्ट नहीं है", agentReturned: "एजेंट ने लौटाया",
    dashboard: "Playhub डैशबोर्ड", openDashboard: "डैशबोर्ड खोलें",
    dashboardShortcut: "CTRL + ALT + P से खोलें",
    dashboardSteamInput: "कंट्रोलर से तुरंत खोलने के लिए Steam Input में इस शॉर्टकट को Guide + किसी बटन से जोड़ें।",
    haptics: "कंट्रोलर फ़ीडबैक", hapticsDescription: "Steam में नेविगेट करते समय स्पर्श प्रतिक्रिया जोड़ता है।", hapticsIntensity: "फ़ीडबैक की तीव्रता",
    restartGaming: "गेमिंग मोड में रीस्टार्ट करें", restartDesktop: "डेस्कटॉप मोड में रीस्टार्ट करें",
    restartConfirm: "सिस्टम अब रीस्टार्ट होगा। जारी रखें?", restartNow: "रीस्टार्ट", cancel: "रद्द करें",
  },
  ru: {
    controlCentre: "Центр управления",
    mode: "Режим запуска", switchGaming: "Перейти в игровой режим", switchDesktop: "Перейти в режим рабочего стола",
    defaultStartup: "Запуск по умолчанию", desktopMode: "Режим рабочего стола", gamingMode: "Игровой режим",
    notConnected: "Агент не подключен", agentReturned: "Агент вернул",
    dashboard: "Панель Playhub", openDashboard: "Открыть панель",
    dashboardShortcut: "Откройте с помощью CTRL + ALT + P",
    dashboardSteamInput: "В Steam Input назначьте это сочетание на Guide + любую кнопку для мгновенного доступа с контроллера.",
    haptics: "Отклик контроллера", hapticsDescription: "Добавляет тактильный отклик при навигации в Steam.", hapticsIntensity: "Интенсивность",
    restartGaming: "Перезапустить в игровом режиме", restartDesktop: "Перезапустить в режиме рабочего стола",
    restartConfirm: "Система будет перезапущена сейчас. Продолжить?", restartNow: "Перезапустить", cancel: "Отмена",
  },
};

const STEAM_LOCALE_ALIASES: Record<string, string> = {
  english: "en", en: "en", italian: "it", it: "it", spanish: "es", latam: "es", es: "es",
  french: "fr", fr: "fr", german: "de", de: "de", brazilian: "pt", portuguese: "pt", pt: "pt",
  ukrainian: "uk", uk: "uk", schinese: "zh", tchinese: "zh", zh: "zh", japanese: "ja", ja: "ja",
  koreana: "ko", korean: "ko", ko: "ko", hindi: "hi", hi: "hi", russian: "ru", ru: "ru",
};

function normalizeSteamLocale(value: unknown): string | null {
  const candidate = String(value ?? "").trim().toLowerCase().replace(/_/g, "-");
  if (!candidate) return null;
  return STEAM_LOCALE_ALIASES[candidate] ?? STEAM_LOCALE_ALIASES[candidate.split("-")[0]] ?? null;
}

function detectSteamLocale(): string {
  const win = window as any;
  const candidates: unknown[] = [];
  [win.LocalizationManager, win.g_LocalizationManager, win.SteamLocalizationManager].filter(Boolean).forEach((manager: any) => {
    candidates.push(manager?.m_strLanguage, manager?.m_language, manager?.m_strCurrentLanguage, manager?.language);
    ["GetLanguage", "GetCurrentLanguage"].forEach((method) => {
      try { const value = manager?.[method]?.(); if (typeof value === "string") candidates.push(value); } catch {}
    });
  });
  candidates.push(win.g_strLanguage, win.g_rgAppContextData?.language, document.documentElement.lang);
  candidates.push(...Array.from(navigator.languages ?? []), navigator.language);
  for (const candidate of candidates) {
    const locale = normalizeSteamLocale(candidate);
    if (locale) return locale;
  }
  return "en";
}

async function readSteamLocale(): Promise<string> {
  try {
    const value = await (window as any).SteamClient?.Settings?.GetCurrentLanguage?.();
    const locale = normalizeSteamLocale(value);
    if (locale) return locale;
  } catch {}
  return detectSteamLocale();
}

let currentLocale = detectSteamLocale();

function t(): Strings {
  return strings[currentLocale] ?? strings.en;
}

function confirmModeSwitch(mode: "gaming" | "desktop", action: () => void) {
  const local = t();
  showModal(
    <ConfirmModal
      strTitle={getModeQuestion(currentLocale, mode)}
      strOKButtonText="Ok"
      strCancelButtonText={local.cancel}
      onOK={action}
    />
  );
}

// ---------------------------------------------------------------------------
// Agente
// ---------------------------------------------------------------------------

interface AgentStatus {
  currentMode?: string;
  defaultMode?: string;
  [key: string]: unknown;
}

interface AgentResult {
  ok?: boolean;
  message?: string;
  status?: AgentStatus;
}

async function getStatus(signal?: AbortSignal): Promise<AgentStatus> {
  try {
    const response = await fetch(`${API_BASE}/status`, { signal });
    if (!response.ok) throw new Error(`${t().agentReturned} ${response.status}`);
    const status = await response.json();
    agentAvailability.available();
    return status;
  } catch (error) {
    agentAvailability.unavailable();
    throw error;
  }
}

async function post(path: string): Promise<AgentResult> {
  const response = await fetch(`${API_BASE}${path}`, { method: "POST" });
  if (!response.ok) {
    throw new Error(`${t().agentReturned} ${response.status}`);
  }
  const result = await response.json() as AgentResult;
  if (result.ok === false) throw new Error(result.message || t().notConnected);
  return result;
}

// ---------------------------------------------------------------------------
// Apertura della Dashboard richiesta da fuori
// ---------------------------------------------------------------------------

// Mentre giochi, l'interfaccia di Steam non riceve input: nessun codice che
// vive qui dentro potrebbe accorgersi della scorciatoia da tastiera. Se ne
// occupa l'agente, che pero' non apre niente per conto suo: alza una
// bandierina, e qui sotto c'e' chi la raccoglie. La bandierina si consuma alla
// lettura, quindi due schede in ascolto non aprono la pagina due volte.
//
// QUESTO NON E' UN COMPONENTE, ED E' IL PUNTO.
//
// Prima l'attesa viveva dentro il pannello del Quick Access Menu. Decky monta
// il pannello di UN plugin per volta - quello selezionato - quindi la
// scorciatoia funzionava solo quando Gaming Mode era la voce aperta, e
// "rinveniva" appena si tornava sul suo pannello. Il sintomo era esattamente
// quello: premi la combinazione e non succede niente, apri il QAM e di colpo
// riparte. Qui l'attesa e' agganciata al plugin, non alla sua interfaccia:
// resta accesa finche' Decky e' caricato, QAM aperto o chiuso, pannello
// selezionato o no.
// APRIRE LA DASHBOARD.
//
// UNA SOLA INTERFACCIA, SEMPRE LA STESSA.
//
// Per un po' ce ne sono state due: una schermata disegnata da Steam e una
// finestra nativa. Era uno spreco e una confusione. Chi usa Playhub deve vedere
// la stessa cosa che apra dal Quick Access Menu o dalla scorciatoia, dentro o
// fuori da un gioco, e non deve chiedersi perche' a volte e' diversa.
//
// Il plugin Decky disegna la Dashboard e possiede navigazione e input. Non
// vengono create finestre native o mirror DWM: se Steam non puo' mostrare la
// route, la richiesta termina senza sovrapporre superfici al gioco.
async function openDashboard(reason = "richiesta") {
  logToAgent("apertura richiesta");
  const environmentRequest = readEnvironment();
  const dashboardWindowsReady = preloadDashboardWindows();
  const environment = await environmentRequest;
  if (!environment?.enabled) {
    toaster.toast({ title: "Gaming Mode", body: optionalAgentCopy(currentLocale).AgentRequired });
    return;
  }
  resetDashboardExit();
  void captureDashboardSourceFocus();
  const [useSteamOverlay] = await Promise.all([prepareDashboardOverlay(), dashboardWindowsReady]);
  if (useSteamOverlay) {
    try {
      // Keep a content route active before closing QAM. Steam otherwise emits
      // an overlay-off transition between the menu and the Dashboard route.
      Navigation?.Navigate?.(DASHBOARD_ROUTE);
      Navigation?.CloseSideMenus?.();
    } catch (error) {
      logToAgent(`navigazione overlay NON riuscita: ${error}`);
    }
    [80, 180, 360, 700].forEach((delay) => window.setTimeout(focusDashboardSurface, delay));
    return;
  }
  // A running game's Dashboard belongs to its native Steam overlay. Never
  // replace a failed overlay hand-off by stealing focus into desktop Steam.
  if (Number(Router?.MainRunningAppID || Router?.MainRunningApp?.appid || 0) > 0) {
    logToAgent("Dashboard: native game overlay not ready; desktop fallback suppressed.");
    return;
  }
  await requestDashboardSteamFocus();
  markDashboardChrome();
  try {
    try { (window as any).SteamClient?.Window?.BringToFront?.(EWindowBringToFront_AndForceOS); } catch {}
    Navigation?.CloseSideMenus?.();
    window.setTimeout(() => Navigation?.Navigate?.(DASHBOARD_ROUTE), 40);
    // One delayed hand-off is enough for the newly mounted route. Repeating
    // it after the user has already started moving can steal focus back to a
    // tab or another fallback target.
    window.setTimeout(focusDashboardSurface, 140);
  } catch (error) {
    clearDashboardChrome();
    void restoreDashboardSourceFocus();
    logToAgent(`navigazione NON riuscita: ${error}`);
  }
}

function openPluginStore() {
  try {
    Navigation?.CloseSideMenus?.();
    window.setTimeout(() => Navigation?.Navigate?.(PLUGIN_STORE_ROUTE), 40);
  } catch (error) {
    console.warn("Playhub Plugin Store: rotta non raggiungibile", error);
  }
}

function currentSteamGame(): number {
  return steamAppId((window as any).SteamUIStore?.MainRunningAppID ?? (DFL as any).Router?.MainRunningAppID);
}

function steamWindowStore() {
  return (window as any).SteamUIStore?.WindowStore ?? (DFL as any)?.Router?.WindowStore;
}

function bigPictureMainWindow() {
  const main = steamWindowStore()?.GamepadUIMainWindowInstance;
  return main?.IsGamepadUIOverlayWindow?.() === false ? main : undefined;
}

function steamNavigationController() {
  return (DFL as any)?.getFocusNavController?.() ?? (window as any).FocusNavController;
}

function returnOverlayWindows(appId: number): SteamReturnOverlayWindow[] | undefined {
  const store = steamWindowStore();
  const windows = store?.OverlayWindows;
  return Array.isArray(windows) ? windows : store?.GetOverlayInstances?.(appId);
}

function matchesReturnOverlay(overlay: SteamReturnOverlayWindow, appId: number): boolean {
  const info = overlay.params?.browserInfo;
  return matchesOverlayApp({ appID: info?.m_unAppID, gameID: info?.m_gameID }, appId);
}

function restoreSteamNavigationFocus(reason: string, exitedApp: number, retainedOverlay?: SteamOverlayScope,
  nativeForegroundConfirmed = false): boolean {
  const main = bigPictureMainWindow();
  const browserWindow = main?.m_BrowserWindow ?? main?.BrowserWindow;
  const context = main?.GetFocusNavContext?.() ?? main?.m_FocusNavContext;
  const canRestore = () => bigPictureMainWindow() === main && isSteamNavigationReturnApp(currentSteamGame(), exitedApp);
  if (!main || !browserWindow || !context || !canRestore() || context.BIsGamepadInputSuppressed?.() === true) return false;

  const controller = steamNavigationController();
  const activeContext = () => typeof controller?.GetActiveContext === "function"
    ? controller.GetActiveContext() : controller?.m_ActiveContext;
  const pointsToMain = () => controller ? activeContext() === context : context.BIsActive?.() === true;
  const ownsContext = () => pointsToMain() && context.BIsActive?.() !== false
    && (context.ActiveWindow == null || context.ActiveWindow === browserWindow);
  const nativeForeground = () => nativeForegroundConfirmed && canRestore();
  const mayFocusMain = () => canRestore() && (browserWindow.document?.hasFocus?.() === true || nativeForeground());
  const focused = recoverSteamNavigation({
    hasDocumentFocus: () => canRestore() && browserWindow.document?.hasFocus?.() === true,
    hasNativeForeground: nativeForeground,
    hasNavigationFocus: () => {
      if (!canRestore() || !ownsContext()) return false;
      if (typeof controller?.GetActiveNavTree === "function") {
        const tree = controller.GetActiveNavTree();
        return Boolean(tree && hasVisibleSteamNavigationFocus(browserWindow.document, tree));
      }
      return hasVisibleSteamNavigationFocus(browserWindow.document, context.m_LastActiveFocusNavTree ?? context.m_LastActiveNavTree);
    },
    requestKeyFocus: () => {
      if (!canRestore() || bigPictureMainWindow() !== main) return;
      // Foregrounding SDL alone can leave Win32 focus on the container. Steam's
      // own activation targets its CEF child as well, under the fresh exit proof.
      if (nativeForegroundConfirmed) browserWindow.SteamClient?.Window?.BringToFront?.(1);
      if (canRestore()) browserWindow.SteamClient?.Window?.SetKeyFocus?.(true);
    },
    focusApplication: () => {
      if (!canRestore()) return;
      if (!ownsContext()) {
        const previous = activeContext();
        const released = previous != null && previous !== context && releaseExitedSteamOverlay(exitedApp, browserWindow,
          canRestore, activeContext, () => controller?.GetActiveNavTree?.(), returnOverlayWindows, retainedOverlay,
          matchesReturnOverlay, nativeForeground);
        if (previous != null && previous !== context && !released) return;
        const mayActivateMain = () => mayFocusMain() && (activeContext() == null || activeContext() === context);
        if (!mayActivateMain()) return;
        if (!ownsContext()) context.OnActivate?.(browserWindow);
        // An overlay can leave the main context internally active even though
        // Steam's navigation controller no longer points to it.
        if (!ownsContext() && mayActivateMain()
            && context.BIsActive?.() === true && context.ActiveWindow === browserWindow) {
          context.OnDeactivate?.(browserWindow);
          if (mayActivateMain()) context.OnActivate?.(browserWindow);
        }
      }
      if (!canRestore() || !ownsContext()) return;
      if (browserWindow.document?.querySelector?.(".ph-dashboard")) focusDashboardSurface();
      else {
        main.FocusApplicationRoot?.();
        const tree = controller?.GetActiveNavTree?.();
        if (canRestore() && ownsContext() && exitedApp > 0
            && main.MenuStore?.GetOpenSideMenu?.() === 0 && !main.m_ModalManager?.modals?.length
            && !hasVisibleSteamNavigationFocus(browserWindow.document, tree)) {
          // Steam's root activation preserves an existing node, even when that
          // node belongs to a hidden running-game panel. Focus the actual game
          // action on this exact route before declaring navigation restored.
          const classes = (DFL as any).findModule?.((value: any) =>
            value?.AppActionButton && value?.ActionButtonAndStatusPanel && value?.AppDetailSectionList);
          focusReturnedSteamGameAction(browserWindow.document, main.m_locationPathname, exitedApp, undefined, classes);
        }
      }
    },
  });
  return focused;
}

function startOpenRequestWatcher(configuratorReturn: SteamConfiguratorReturn): () => void {
  let alive = true;
  let inFlight = false;
  let runAgain = false;
  let launchRevision = 0;
  let armGame = 0, fastArmUntil = 0, nextArm = 0, armed = false, arming = false;
  let launchPatch: { unpatch(): void } | undefined;
  const steam = (window as any).SteamClient;
  const overlayScope = createSteamOverlayReturnScope(matchesReturnOverlay);
  const rememberOverlay = (appId: number) => {
    if (appId <= 0) { overlayScope.observe(appId, undefined); return; }
    // A new RunGame can precede Steam clearing the old app's overlay list.
    if (launchRevision > 0 && (!armed || armGame !== appId)) {
      overlayScope.observe(appId, undefined);
      return;
    }
    try {
      const controller = steamNavigationController();
      overlayScope.observe(appId, returnOverlayWindows(appId), controller?.GetActiveContext?.() ?? controller?.m_ActiveContext);
    } catch { /* Steam can remove the browser while the event is delivered. */ }
  };
  const layout = typeof steam?.URL?.ExecuteSteamURL === "function"
    ? createSteamReturnControllerLayout(url => {
      try {
        steam.URL.ExecuteSteamURL(url);
        logToAgent(url.endsWith("/769") ? "Steam Input menu layout restored after game exit" : "Steam Input automatic layout restored");
      }
      catch (error) { console.warn("Playhub Dashboard: Steam Input layout unavailable", error); }
    }, (callback, delay) => window.setTimeout(callback, delay), timer => window.clearTimeout(timer),
      () => activeSteamController((window as any).ControllerStore))
    : undefined;
  const keyFocusReset = createSteamKeyFocusReset((callback, delay) => window.setTimeout(callback, delay),
    timer => window.clearTimeout(timer));
  const focusRecovery = createSteamNavigationRecovery(
    confirmBigPictureForeground,
    async (appId, usedCompanion, recovery) => {
      const target = bigPictureMainWindow();
      const targetWindow = target?.m_BrowserWindow ?? target?.BrowserWindow;
      const targetDocument = targetWindow?.document;
      const mayResetKeys = () => {
        const runningStore = (window as any).SteamUIStore ?? (DFL as any).Router;
        const currentWindow = target?.m_BrowserWindow ?? target?.BrowserWindow;
        const context = target?.GetFocusNavContext?.() ?? target?.m_FocusNavContext;
        return alive && recovery.isCurrent() && appId > 0 && target && bigPictureMainWindow() === target
          && targetWindow && targetWindow.closed !== true && currentWindow === targetWindow
          && targetWindow.document === targetDocument && targetDocument
          && isConfirmedSteamIdle(runningStore?.MainRunningAppID, runningStore?.IsAnyAppRunning?.())
          && context && context.RootWindow === targetWindow && context.BIsGamepadInputSuppressed?.() !== true
          && target.MenuStore?.GetOpenSideMenu?.() === 0 && !target.m_ModalManager?.modals?.length
          && typeof targetWindow.SteamClient?.Window?.SetKeyFocus === "function";
      };
      if (mayResetKeys()) {
        await keyFocusReset.reset({
          context: recovery,
          canRestore: mayResetKeys,
          hasRealFocus: () => targetDocument?.hasFocus?.() === true
            && targetDocument.body?.classList?.contains("WindowFocus") === true,
          setKeyFocus: value => targetWindow.SteamClient.Window.SetKeyFocus(value),
        });
      }
      if (!alive || !recovery.isCurrent() || !await recovery.confirmForeground()) return false;
      const focused = restoreSteamNavigationFocus("chiusura gioco", launchPatch ? appId : 0, overlayScope.get(appId), true);
      if (focused) overlayScope.clear();
      if (launchPatch && appId > 0) {
        const main = bigPictureMainWindow();
        const returnedWindow = main?.m_BrowserWindow ?? main?.BrowserWindow;
        const document = returnedWindow?.document;
        // A verified exit may leave Steam routing input to the running-game
        // view while the launcher helper retires.
        if (currentSteamGame() === appId && (usedCompanion || main?.m_locationPathname === "/apprunning"))
          layout?.begin(appId, currentSteamGame());
        if (document?.hasFocus?.() === true && main?.MenuStore?.GetOpenSideMenu?.() === 0 && !main?.m_ModalManager?.modals?.length) {
          // The running-game page is a placeholder. Windows has confirmed
          // exit, so replace it with the game page while Steam retires the shortcut.
          if (main.m_locationPathname === "/apprunning") navigateReturnedSteamGame(main, appId);
          // A still-retiring shortcut renders Resume/Stop and can replace the
          // focused button later. Wait for Steam to retire it before selecting
          // the actual Play action and completing this recovery.
          const runningStore = (window as any).SteamUIStore ?? (DFL as any).Router;
          if (!isConfirmedSteamIdle(runningStore?.MainRunningAppID, runningStore?.IsAnyAppRunning?.())) return false;
          if (main.m_locationPathname !== `/library/app/${appId}`) {
            navigateReturnedSteamGame(main, appId);
            return false;
          }
          const classes = (DFL as any).findModule?.((value: any) =>
            value?.AppActionButton && value?.ActionButtonAndStatusPanel && value?.AppDetailSectionList);
          const playFocused = focusReturnedSteamGameAction(document, main.m_locationPathname, appId, undefined, classes);
          const tree = steamNavigationController()?.GetActiveNavTree?.();
          const button = document.activeElement;
          const highlighted = button?.matches?.(".gpfocus") === true || Boolean(button?.querySelector?.(".gpfocus"));
          if (!focused || !playFocused || !document.hasFocus() || !document.body?.classList?.contains("WindowFocus")
              || !highlighted || !hasVisibleSteamNavigationFocus(document, tree)) return false;
          const input = () => (window as any).SteamClient?.Input;
          const canReleaseConfigurator = () => {
            const store = (window as any).SteamUIStore;
            const currentMain = bigPictureMainWindow();
            const currentWindow = currentMain?.m_BrowserWindow ?? currentMain?.BrowserWindow;
            const context = currentMain?.GetFocusNavContext?.() ?? currentMain?.m_FocusNavContext;
            const controller = steamNavigationController();
            const active = controller?.GetActiveContext?.();
            const modals = currentMain?.m_ModalManager?.modals;
            const activeTree = controller?.GetActiveNavTree?.();
            return alive && recovery.isCurrent() && currentMain === main && currentWindow === returnedWindow && currentWindow?.document === document
              && currentWindow?.closed !== true && store?.IsAnyAppRunning?.() === false
              && isConfirmedSteamIdle(store?.MainRunningAppID, false)
              && currentMain?.m_locationPathname === `/library/app/${appId}`
              && context != null && active === context && context.RootWindow === currentWindow
              && context.ActiveWindow === currentWindow && context.BIsActive?.() === true
              && context.BIsGamepadInputSuppressed?.() === false
              && currentMain?.MenuStore?.GetOpenSideMenu?.() === 0 && Array.isArray(modals) && modals.length === 0
              && document.hasFocus() === true && document.body?.classList?.contains("WindowFocus") === true
              && document.activeElement === button && (button?.matches?.(".gpfocus") === true || Boolean(button?.querySelector?.(".gpfocus")))
              && hasVisibleSteamNavigationFocus(document, activeTree);
          };
          if (await configuratorReturn.release(appId, recovery, canReleaseConfigurator, input))
            logToAgent("Steam native configurator focus released after owned mirror game exit");
          if (!alive || !recovery.isCurrent() || configuratorReturn.pending(appId)) return false;
          logToAgent("Steam Play action focused after game exit");
          return true;
        }
        return false;
      }
      return focused;
    },
    currentSteamGame,
  );
  const apps = steam?.Apps;
  try {
    if (typeof apps?.RunGame === "function" && typeof (DFL as any).beforePatch === "function") {
      launchPatch = (DFL as any).beforePatch(apps, "RunGame", () => {
        configuratorReturn.forget();
        layout?.release();
        overlayScope.clear();
        launchRevision++;
        armGame = 0;
        armed = false;
        focusRecovery.launchStarted();
      });
    }
  } catch (error) {
    console.warn("Playhub Dashboard: could not observe Steam game launches", error);
  }

  const modeCommands = installSteamModeCommands(window, {
    alive: () => alive,
    exit: () => {
      layout?.release();
      overlayScope.clear();
      focusRecovery.cancel(0);
      clearDashboardChrome();
      Navigation?.CloseSideMenus?.();
      if (typeof steam?.UI?.ExitBigPictureMode !== "function") throw new Error("Steam desktop transition unavailable");
      return steam.UI.ExitBigPictureMode();
    },
    readDesktop: async () => {
      if (typeof steam?.UI?.GetUIMode !== "function" || typeof steam?.UI?.GetDesiredSteamUIWindows !== "function") return false;
      let timer = 0;
      try {
        const observation = Promise.all([steam.UI.GetUIMode(), steam.UI.GetDesiredSteamUIWindows()]).then(([mode, desired]) =>
          mode === 7 && Array.isArray(desired) && desired.some(value => value?.windowType === 5)
          && !desired.some(value => value?.windowType === 0));
        return await Promise.race([observation, new Promise<boolean>(resolve => { timer = window.setTimeout(() => resolve(false), 200); })]);
      } finally { window.clearTimeout(timer); }
    },
    ack: acknowledgeSteamModeCommand,
    now: () => performance.now(),
    delay: ms => new Promise(resolve => window.setTimeout(resolve, ms)),
  });

  async function poll(urgent = false) {
    if (!alive) return;
    overlayFocusObserver.refresh();
    if (inFlight) {
      runAgain ||= urgent;
      return;
    }
    inFlight = true;
    try {
      const activeApp = currentSteamGame();
      rememberOverlay(activeApp);
      const main = bigPictureMainWindow();
      layout?.observe(activeApp, (main?.m_BrowserWindow ?? main?.BrowserWindow)?.document?.hasFocus?.() === true);
      const knownArmApp=readOwnedGameApp(activeApp);
      const selectedArmApp=armed&&armGame===knownArmApp?knownArmApp:resolveOwnedGameApp(activeApp);
      const armApp=typeof selectedArmApp==="number"?selectedArmApp:await selectedArmApp;
      if(!alive)return;
      const now = Date.now();
      if (armGame !== armApp) {
        armGame = armApp;
        fastArmUntil = now + 20000;
        nextArm = 0;
        armed = false;
      }
      if (armApp && !armed && !arming && now >= nextArm && bigPictureMainWindow()) {
        const revision = launchRevision;
        arming = true;
        // Steam reports the shortcut before some launchers create a game window.
        nextArm = now + (now < fastArmUntil ? 1000 : 5000);
        void controlCall<[number], { ok: boolean }>("arm_big_picture_return", armApp).then(result => {
          if (!alive || revision !== launchRevision || armGame !== armApp || readOwnedGameApp(currentSteamGame()) !== armApp) return;
          armed = result?.ok === true;
          if (armed) {
            rememberOverlay(armApp);
            focusRecovery.gameArmed();
          }
        }).catch(() => {}).finally(() => { arming = false; });
      }
      const signal = await consumeOpenRequest();
      if (!alive) return;
      if (signal.steamModeCommand) {
        await modeCommands.process(signal.steamModeCommand);
        return;
      }
      if (signal.open) {
        focusRecovery.cancel(signal.focusRecovery);
        openDashboard();
        return;
      }
      await focusRecovery.observe(signal.focusRecovery, signal.steamForeground);
    } catch (error) {
      console.warn("Playhub Dashboard: attesa dell'apertura non riuscita", error);
    } finally {
      inFlight = false;
      if (runAgain) {
        runAgain = false;
        void poll();
      }
    }
  }

  const wakeup = createSteamNavigationWakeup(() => { void poll(true); },
    (callback, delay) => window.setTimeout(callback, delay), id => window.clearTimeout(id));
  const onFocus = () => { overlayFocusObserver.refresh(); if (bigPictureMainWindow()) wakeup.wake(); };
  const overlayFocusObserver = observeSteamOverlayFocus(
    () => (window as any).FocusedAppWindowStore,
    (appId, pid) => {
      if (appId === 0 || pid === 0) onFocus();
      else rememberOverlay(currentSteamGame());
    },
  );
  overlayFocusObserver.refresh();
  const timer = window.setInterval(() => poll(), 500);
  const steamUi = (window as any).SteamClient?.System?.UI;
  const subscriptions: Array<{ unregister?: () => void }> = [];
  try {
    const focus = steamUi?.RegisterForFocusChangeEvents?.(onFocus);
    if (focus) subscriptions.push(focus);
  } catch (error) {
    console.warn("Playhub Dashboard: could not observe Steam focus changes", error);
  }

  return () => {
    alive = false;
    focusRecovery.cancel();
    configuratorReturn.forget();
    layout?.release();
    overlayScope.clear();
    overlayFocusObserver.dispose();
    wakeup.stop();
    window.clearInterval(timer);
    for (const subscription of subscriptions) {
      try { subscription.unregister?.(); } catch { /* Steam may have already disposed the browser view. */ }
    }
    launchPatch?.unpatch();
  };
}

// ---------------------------------------------------------------------------
// Pannello del Quick Access Menu
// ---------------------------------------------------------------------------

function Content({ origin = "decky" }: { origin?: "qam" | "decky" }) {
  const [locale, setLocale] = useState(currentLocale);
  const local = strings[locale] ?? strings.en;
  const optional = optionalAgentCopy(locale);
  const [status, setStatus] = useState<AgentStatus | undefined>();
  const [busy, setBusy] = useState(false);
  const [dashboardEnabled, setDashboardEnabled] = useState(true);
  const { enabled: hapticsEnabled, intensity: hapticsIntensity } = React.useSyncExternalStore(subscribeNavigationHaptics, getNavigationHapticsConfig);

  const defaultOptions = useMemo(
    () => [
      { data: "Desktop", label: "Desktop" },
      { data: "Gaming", label: "Gaming" },
    ],
    []
  );

  const refresh = async () => {
    try {
      setStatus(await getStatus());
    } catch {
      setStatus(undefined);
    }
  };

  const run = async (path: string, title: string) => {
    if (!status) return;
    setBusy(true);
    try {
      const result = await post(path);
      toaster.toast({ title, body: result.message });
      if (result.status) {
        setStatus(result.status);
      } else {
        await refresh();
      }
    } catch (error) {
      toaster.toast({
        title,
        body: error instanceof Error ? error.message : local.notConnected,
      });
    } finally {
      setBusy(false);
    }
  };

  const setDefault = async (option: { data: string }) => {
    await run(option.data === "Gaming" ? "/default/gaming" : "/default/desktop", local.defaultStartup);
  };

  useEffect(() => {
    let alive = true;
    void readSteamLocale().then((value) => {
      const localeChanged = currentLocale !== value;
      currentLocale = value;
      if (localeChanged) notifyHomeNewsLanguageChanged();
      refreshOnboardingLocale();
      if (alive) setLocale(value);
    });
    const stopStatus = pollStatus(getStatus, setStatus);
    void readEnvironment().then((environment) => setDashboardEnabled(environment?.enabled !== false));
    const hapticRevision = getNavigationHapticsRevision();
    void hapticSettingsWrite.catch(() => {}).then(() => readSettings()).then((settings) => {
      if (!alive || !settings || hapticRevision !== getNavigationHapticsRevision()) return;
      const enabled = settings.navigationHapticsEnabled === true;
      const intensity = Math.max(5, Math.min(100, Number(settings.navigationHapticsIntensity) || 55));
      configureNavigationHaptics({ enabled, intensity });
    });
    return () => { alive = false; stopStatus(); };
  }, []);

  const gameAppId = steamAppId((window as any).SteamUIStore?.MainRunningAppID ?? (DFL as any).Router?.MainRunningAppID);
  return (
    <ControlCenter locale={locale} origin={origin} homeTitle={local.controlCentre} gameAppId={gameAppId} gamingMode={status?.currentMode === "Gaming"}
      frameTiming={origin === "qam" && status && gameAppId !== 0 ? <GameFrameTiming key={gameAppId} appId={gameAppId} locale={locale} /> : null} session={<>
      {!status && <PanelSection><PanelSectionRow>
        <div role="status" style={{ margin: "8px 0 16px", padding: "16px", borderRadius: 10,
          background: "rgba(255,255,255,.055)", border: "1px solid rgba(255,255,255,.12)" }}>
          <div style={{ fontSize: 16, fontWeight: 650, marginBottom: 8 }}>{optional.Title}</div>
          <div style={{ fontSize: 13, lineHeight: 1.5, opacity: .8 }}>{optional.AgentRequired}</div>
        </div>
      </PanelSectionRow></PanelSection>}
      <PanelSection>
        <PanelSectionRow>
          <ButtonItem
            disabled={!dashboardEnabled || !status}
            bottomSeparator="none"
            layout="below"
            onClick={() => openDashboard()}
          >
            Playhub Dashboard
          </ButtonItem>
        </PanelSectionRow>
        <PanelSectionRow>
          <style>{`
            @keyframes phShortcutPulse { 0%,100% { opacity:.62; transform:scale(.96); } 50% { opacity:1; transform:scale(1); } }
            @keyframes phShortcutTravel { 0%,100% { transform:translateX(-3px); opacity:.35; } 50% { transform:translateX(3px); opacity:1; } }
            .ph-dashboard-shortcut { box-sizing:border-box; width:100%; padding:9px 16px 14px; color:rgba(255,255,255,.88); }
            .ph-dashboard-shortcut-figure { width:min(100%,292px); min-height:42px; margin:0 auto; display:flex; align-items:center; justify-content:center; gap:7px; color:#fff; }
            .ph-dashboard-keys { display:flex; align-items:center; gap:4px; }
            .ph-dashboard-key { min-width:28px; height:24px; padding:0 6px; display:grid; place-items:center; border:1.5px solid rgba(255,255,255,.88); border-radius:6px; font-size:9px; line-height:1; font-weight:700; background:transparent; }
            .ph-dashboard-plus { font-size:13px; opacity:.58; }
            .ph-dashboard-flow { width:22px; height:15px; flex:0 0 auto; animation:phShortcutTravel 1.6s ease-in-out infinite; }
            .ph-dashboard-flow path { fill:rgba(255,255,255,.82); }
            .ph-dashboard-pad { width:48px; height:29px; flex:0 0 auto; animation:phShortcutPulse 1.6s ease-in-out infinite; }
            .ph-dashboard-pad .pad-shell, .ph-dashboard-pad .pad-detail { fill:none; stroke:currentColor; stroke-width:1.7; stroke-linecap:round; stroke-linejoin:round; }
            .ph-dashboard-pad .pad-guide { fill:#fff; stroke:#fff; stroke-width:1; filter:drop-shadow(0 0 3px rgba(255,255,255,.7)); }
            .ph-dashboard-pad .pad-button { fill:#fff; opacity:.92; }
            .ph-dashboard-shortcut-title { margin-top:3px; text-align:center; font-size:14px; line-height:1.3; font-weight:650; }
            .ph-dashboard-shortcut-copy { width:calc(100% - 42px); margin:6px auto 0; max-width:278px; text-align:center; color:rgba(255,255,255,.58); font-size:12px; line-height:1.4; }
          `}</style>
          <div className="ph-dashboard-shortcut">
            <div className="ph-dashboard-shortcut-figure" aria-hidden="true">
              <div className="ph-dashboard-keys">
                <span className="ph-dashboard-key">CTRL</span><span className="ph-dashboard-plus">+</span>
                <span className="ph-dashboard-key">ALT</span><span className="ph-dashboard-plus">+</span>
                <span className="ph-dashboard-key">P</span>
              </div>
              <svg className="ph-dashboard-flow" viewBox="0 0 28 18" focusable="false">
                <path d="M1 7.25h18.4l-4.2-4.2L17.25 1 25 8.75l-7.75 7.75-2.05-2.05 4.2-4.2H1z" />
              </svg>
              <svg className="ph-dashboard-pad" viewBox="0 0 64 38" focusable="false">
                <path className="pad-shell" d="M12 7.5h40c3.5 0 5.8 2.1 6.5 5.4l2.1 10.7c.8 4.1-1.8 7-5.8 7H9.2c-4 0-6.6-2.9-5.8-7l2.1-10.7c.7-3.3 3-5.4 6.5-5.4Z" />
                <path className="pad-detail" d="M17 14v10M12 19h10" />
                <circle className="pad-guide" cx="32" cy="19" r="3" />
                <circle className="pad-button" cx="47" cy="14.7" r="1.7" />
                <circle className="pad-button" cx="51.3" cy="19" r="1.7" />
                <circle className="pad-button" cx="47" cy="23.3" r="1.7" />
                <circle className="pad-button" cx="42.7" cy="19" r="1.7" />
              </svg>
            </div>
            <div className="ph-dashboard-shortcut-title">{local.dashboardShortcut}</div>
            <div className="ph-dashboard-shortcut-copy">{local.dashboardSteamInput}</div>
          </div>
        </PanelSectionRow>
      </PanelSection>

      <PanelSection title={local.mode}>
        <PanelSectionRow>
          <style>{`
            .ph-mode-actions{display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1fr);gap:10px;width:100%;box-sizing:border-box;padding:4px 0 10px}
            .ph-mode-actions .ph-mode-action{width:100%!important;min-width:0!important;max-width:none!important;height:96px!important;padding:10px 6px!important;box-sizing:border-box;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:10px;transform:none!important}
            .ph-mode-action-icons{height:32px;display:flex;align-items:center;justify-content:center;gap:7px;font-family:'Segoe Fluent Icons','Segoe MDL2 Assets';font-size:30px;font-weight:400;line-height:1}
            .ph-mode-action-label{display:grid;grid-template-rows:repeat(2,1.2em);width:100%;text-align:center;font-size:13px;line-height:1.2;white-space:normal;overflow-wrap:anywhere}
          `}</style>
          <Focusable className="ph-mode-actions" flow-children="row">
            <DialogButton className="ph-mode-action" disabled={busy || !status} onClick={() => confirmModeSwitch("gaming", () => { void run("/mode/gaming/switch", local.gamingMode); })}>
              <span className="ph-mode-action-icons" aria-hidden="true">{'\uE7FC'}</span>
              <ModeButtonLabel mode="gaming" />
            </DialogButton>
            <DialogButton className="ph-mode-action" disabled={busy || !status} onClick={() => confirmModeSwitch("desktop", () => { void run("/mode/desktop/switch", local.desktopMode); })}>
              <span className="ph-mode-action-icons" aria-hidden="true"><span>{'\uE765'}</span><span style={{fontSize:24}}>{'\uE962'}</span></span>
              <ModeButtonLabel mode="desktop" />
            </DialogButton>
          </Focusable>
        </PanelSectionRow>
        <PanelSectionRow>
          <DropdownItem
            label={local.defaultStartup}
            bottomSeparator="none"
            disabled={busy || !status}
            rgOptions={defaultOptions}
            selectedOption={status?.defaultMode ?? "Desktop"}
            onChange={setDefault}
          />
        </PanelSectionRow>
      </PanelSection>

    </>} store={<PanelSection>
        <PanelSectionRow>
          <PluginStoreIntro description={getPlayhubDiscoverCopy(locale)[1]} button={<ButtonItem bottomSeparator="none" layout="below" onClick={openPluginStore}>
            <span style={{ display: "inline-flex", alignItems: "center", gap: "10px" }}>
              <TbShoppingBag aria-hidden="true" />
              <span>Plugin Store</span>
            </span>
          </ButtonItem>} />
        </PanelSectionRow>
      </PanelSection>
    } controller={<PanelSection>
        <PanelSectionRow>
          <ToggleField
            bottomSeparator="none"
            label={local.haptics}
            description={local.hapticsDescription}
            checked={hapticsEnabled}
            onChange={(enabled: boolean) => {
              configureNavigationHaptics({ enabled });
              saveHapticSettings({ navigationHapticsEnabled: enabled });
            }}
          />
        </PanelSectionRow>
        {hapticsEnabled && (
          <PanelSectionRow>
            <SliderField
              bottomSeparator="none"
              label={local.hapticsIntensity}
              value={hapticsIntensity}
              min={5}
              max={100}
              step={5}
              valueSuffix="%"
              showValue
              onChange={(intensity: number) => {
                const value = Math.max(5, Math.min(100, Math.round(intensity)));
                configureNavigationHaptics({ intensity: value });
                saveHapticSettings({ navigationHapticsIntensity: value });
              }}
            />
          </PanelSectionRow>
        )}
      </PanelSection>} settings={<PlayhubQamToggle locale={locale} />} />
  );
}

const EWindowBringToFront_AndForceOS = 1;

// ---------------------------------------------------------------------------

function installOptionalHook(name: string, install: () => () => void): () => void {
  try { return install(); }
  catch (error) {
    console.warn(`[Playhub] ${name} unavailable; continuing startup`, error);
    return () => {};
  }
}

export default definePlugin(() => {
  const stopDeckyIpcRecovery = installOptionalHook("Decky IPC recovery", () => installDeckyIpcRecovery({
    scan: () => controlCall<[], IpcFailure[]>("get_decky_ipc_failures"),
    claim: failure => controlCall<[string, string, string], { ok: boolean; name?: string }>("claim_decky_ipc_recovery", failure.folder, failure.session, failure.log),
    reload: name => {
      const bridge = (window as any).DeckyBackend;
      if (typeof bridge?.call !== "function") return Promise.reject(new Error("Decky reload bridge unavailable"));
      return bridge.call("loader/reload_plugin", name);
    },
    schedule: (callback, delay) => window.setTimeout(callback, delay),
    cancel: timer => window.clearTimeout(timer as number),
    warn: error => console.warn("[Playhub] Decky IPC startup recovery unavailable", error),
  }));
  const uninstallCircles = installOptionalHook("Screensaver", () => installCirclesScreensaver(() => currentLocale));
  const uninstallOnboarding = installOptionalHook("Onboarding", () => installPlayhubOnboarding(() => currentLocale));
  const uninstallDeckyHost = installOptionalHook("Decky host", () => initDeckyHost(() => controlCall<[], { deckyHostEnabled?: boolean }>("get_panel_preferences")));
  const uninstallQuickSettings = installOptionalHook("Quick settings", installQuickSettings);
  const configuratorReturn = createSteamConfiguratorReturn();
  const uninstallControlledQamFocus = installOptionalHook("QAM focus", () => installControlledQamFocus(
    (app, presentation) => configuratorReturn.admitted(app, presentation, (window as any).SteamClient?.Input),
    (app, proof, browser) => configuratorReturn.nativeAdmitted(app, proof, browser, (window as any).SteamClient?.Input)));
  const uninstallLivePerformance = installOptionalHook("Performance monitor", installLivePerformance);
  void initializeQamPreferences().catch(error=>console.warn('[Playhub QAM] Preferences unavailable',error));
  let uninstallHomeNews = () => {};
  try { uninstallHomeNews = installHomeNews(() => currentLocale); }
  catch (error) { console.warn("[Playhub News] Home integration unavailable", error); }
  // Riempie la cache prima della prima apertura: la dashboard monta subito la
  // finestra primaria invece di mostrarla soltanto dopo il primo effect React.
  void preloadDashboardWindows();
  void preloadPluginStoreCatalog().catch(() => {});
  const stopOpenRequestWatcher = startOpenRequestWatcher(configuratorReturn);
  const uninstallNavigationHaptics = installOptionalHook("Navigation haptics", installNavigationHaptics);
  const uninstallPlayhubQam = initPlayhubQam({ content: <Content origin="qam" />, icon: <PlayhubIcon />, title: <div style={{ paddingInline: 16, boxSizing: "border-box" }}>{playhubTitle}</div> });
  const uninstallPowerMenuPatch = installOptionalHook("Power menu", () => installPowerMenuPatch(
    () => ({ gaming: t().switchGaming, desktop: t().switchDesktop }),
    (mode) => confirmModeSwitch(mode, () => { void post(`/mode/${mode}/switch`).catch((error) => toaster.toast({ title: "Playhub", body: String(error) })); })
  ));

  const hapticRevision = getNavigationHapticsRevision();
  void hapticSettingsWrite.catch(() => {}).then(() => readSettings()).then((settings) => {
    if (settings && hapticRevision === getNavigationHapticsRevision()) configureNavigationHaptics({
      enabled: settings.navigationHapticsEnabled === true,
      intensity: settings.navigationHapticsIntensity,
    });
  });

  try {
    routerHook?.addRoute?.(DASHBOARD_ROUTE, () => <DashboardPage />, { exact: true });
  } catch (error) {
    console.warn("Playhub Dashboard: rotta non aggiunta", error);
  }
  try {
    routerHook?.addRoute?.(PLUGIN_STORE_ROUTE, () => <PluginStorePage />, { exact: true });
  } catch (error) {
    console.warn("Playhub Plugin Store: rotta non aggiunta", error);
  }

  return {
    name: "Playhub",
    titleView: <div className={staticClasses?.Title}>{playhubTitle}</div>,
    content: <Content />,
    icon: <PlayhubIcon />,
    onDismount() {
      const cleanups = [
        stopDeckyIpcRecovery,
        uninstallCircles, disposeDisplayConfirmation, uninstallOnboarding,
        uninstallDeckyHost, uninstallQuickSettings, uninstallControlledQamFocus, uninstallLivePerformance, uninstallHomeNews,
        clearDashboardChrome,
        stopOpenRequestWatcher, uninstallNavigationHaptics,
        uninstallPowerMenuPatch, uninstallPlayhubQam,
      ];
      for (const cleanup of cleanups) {
        try { cleanup(); } catch (error) {
          console.warn("[Playhub] Cleanup failed; continuing remaining cleanup", error);
        }
      }
      try {
        routerHook?.removeRoute?.(DASHBOARD_ROUTE);
      } catch (error) {
        console.warn("Playhub Dashboard: rotta non rimossa", error);
      }
      try {
        routerHook?.removeRoute?.(PLUGIN_STORE_ROUTE);
      } catch (error) {
        console.warn("Playhub Plugin Store: rotta non rimossa", error);
      }
    },
  };
});

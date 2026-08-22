import { definePlugin, routerHook, toaster, DFL, SP_REACT as React } from "./decky";
import { FaGamepad } from "react-icons/fa";
import { consumeOpenRequest, logToAgent, readEnvironment, API_BASE, requestDashboardSteamFocus, restoreDashboardSourceFocus } from "./api";
import { DashboardPage, DASHBOARD_ROUTE, clearDashboardChrome, focusDashboardSurface, markDashboardChrome, prepareDashboardOverlay } from "./DashboardPage";

const { useState, useEffect, useMemo } = React;
const { PanelSection, PanelSectionRow, ButtonItem, DropdownItem, Navigation, staticClasses } = DFL as any;

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
  dashboardWindowSwitch: string;
}

const strings: Record<string, Strings> = {
  en: {
    mode: "Mode",
    switchGaming: "Switch to Gaming Mode",
    switchDesktop: "Switch to Desktop Mode",
    defaultStartup: "Default startup",
    desktopMode: "Desktop Mode",
    gamingMode: "Gaming Mode",
    notConnected: "Agent not connected",
    agentReturned: "Agent returned",
    dashboard: "Playhub Dashboard",
    openDashboard: "Open Dashboard",
    dashboardShortcut: "Open it anywhere with CTRL + ALT + P",
    dashboardSteamInput: "In Steam Input, assign this shortcut to Guide + any button for instant controller access.",
    dashboardWindowSwitch: "You can also map ALT + TAB in Steam Input to switch windows immediately.",
  },
  it: {
    mode: "Modalità",
    switchGaming: "Passa alla modalità Gaming",
    switchDesktop: "Passa alla modalità Desktop",
    defaultStartup: "Avvio predefinito",
    desktopMode: "Modalità Desktop",
    gamingMode: "Modalità Gaming",
    notConnected: "Agent non collegato",
    agentReturned: "Agent ha risposto",
    dashboard: "Playhub Dashboard",
    openDashboard: "Apri Dashboard",
    dashboardShortcut: "Aprila ovunque con CTRL + ALT + P",
    dashboardSteamInput: "In Steam Input, assegna questa scorciatoia a Guida + un pulsante per aprirla subito dal controller.",
    dashboardWindowSwitch: "Puoi anche associare tramite Steam Input la combinazione ALT + TAB per cambiare immediatamente finestra.",
  },
  es: {
    mode: "Modo",
    switchGaming: "Cambiar al modo Gaming",
    switchDesktop: "Cambiar al modo Escritorio",
    defaultStartup: "Inicio predeterminado",
    desktopMode: "Modo Escritorio",
    gamingMode: "Modo Gaming",
    notConnected: "Agente no conectado",
    agentReturned: "El agente devolvió",
    dashboard: "Playhub Dashboard",
    openDashboard: "Abrir Dashboard",
    dashboardShortcut: "Ábrelo desde cualquier lugar con CTRL + ALT + P",
    dashboardSteamInput: "En Steam Input, asigna este atajo a Guía + un botón para abrirlo al instante con el mando.",
    dashboardWindowSwitch: "También puedes asignar ALT + TAB en Steam Input para cambiar de ventana al instante.",
  },
  fr: {
    mode: "Mode",
    switchGaming: "Passer en mode Gaming",
    switchDesktop: "Passer en mode Bureau",
    defaultStartup: "Démarrage par défaut",
    desktopMode: "Mode Bureau",
    gamingMode: "Mode Gaming",
    notConnected: "Agent non connecté",
    agentReturned: "Agent a renvoyé",
    dashboard: "Playhub Dashboard",
    openDashboard: "Ouvrir le Dashboard",
    dashboardShortcut: "Ouvrez-le partout avec CTRL + ALT + P",
    dashboardSteamInput: "Dans Steam Input, associez ce raccourci à Guide + un bouton pour l'ouvrir instantanément avec la manette.",
    dashboardWindowSwitch: "Vous pouvez aussi associer ALT + TAB dans Steam Input pour changer immédiatement de fenêtre.",
  },
  de: {
    mode: "Modus",
    switchGaming: "In den Gaming-Modus wechseln",
    switchDesktop: "In den Desktop-Modus wechseln",
    defaultStartup: "Standardstart",
    desktopMode: "Desktop-Modus",
    gamingMode: "Gaming-Modus",
    notConnected: "Agent nicht verbunden",
    agentReturned: "Agent meldete",
    dashboard: "Playhub Dashboard",
    openDashboard: "Dashboard öffnen",
    dashboardShortcut: "Überall mit CTRL + ALT + P öffnen",
    dashboardSteamInput: "Weise diese Tastenkombination in Steam Input Guide + einer Taste zu, um das Dashboard direkt per Controller zu öffnen.",
    dashboardWindowSwitch: "Du kannst in Steam Input auch ALT + TAB zuweisen, um sofort zwischen Fenstern zu wechseln.",
  },
  pt: {
    mode: "Modo",
    switchGaming: "Mudar para modo Gaming",
    switchDesktop: "Mudar para modo Desktop",
    defaultStartup: "Arranque predefinido",
    desktopMode: "Modo Desktop",
    gamingMode: "Modo Gaming",
    notConnected: "Agente não ligado",
    agentReturned: "Agente devolveu",
    dashboard: "Playhub Dashboard",
    openDashboard: "Abrir Dashboard",
    dashboardShortcut: "Abra em qualquer lugar com CTRL + ALT + P",
    dashboardSteamInput: "No Steam Input, atribua este atalho a Guia + um botão para abrir imediatamente pelo comando.",
    dashboardWindowSwitch: "Também pode atribuir ALT + TAB no Steam Input para mudar imediatamente de janela.",
  },
};

function t(): Strings {
  const language = navigator.language.split("-")[0];
  return strings[language] ?? strings.en;
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
  message?: string;
  status?: AgentStatus;
}

async function getStatus(): Promise<AgentStatus> {
  const response = await fetch(`${API_BASE}/status`);
  if (!response.ok) {
    throw new Error(`${t().agentReturned} ${response.status}`);
  }
  return await response.json();
}

async function post(path: string): Promise<AgentResult> {
  const response = await fetch(`${API_BASE}${path}`, { method: "POST" });
  if (!response.ok) {
    throw new Error(`${t().agentReturned} ${response.status}`);
  }
  return await response.json();
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
  const environment = await readEnvironment();
  if (!environment?.enabled) return;
  const useSteamOverlay = await prepareDashboardOverlay();
  if (useSteamOverlay) {
    try {
      Navigation?.CloseSideMenus?.();
      Navigation?.Navigate?.(DASHBOARD_ROUTE);
    } catch (error) {
      logToAgent(`navigazione overlay NON riuscita: ${error}`);
    }
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

function startOpenRequestWatcher(): () => void {
  let alive = true;
  let inFlight = false;

  const timer = window.setInterval(async () => {
    if (!alive || inFlight) return;
    inFlight = true;
    try {
      if (await consumeOpenRequest()) {
        openDashboard();
      }
    } catch (error) {
      console.warn("Playhub Dashboard: attesa dell'apertura non riuscita", error);
    } finally {
      inFlight = false;
    }
  }, 500);

  return () => {
    alive = false;
    window.clearInterval(timer);
  };
}

// ---------------------------------------------------------------------------
// Pannello del Quick Access Menu
// ---------------------------------------------------------------------------

function Content() {
  const local = t();
  const [status, setStatus] = useState<AgentStatus | undefined>();
  const [busy, setBusy] = useState(false);
  const [dashboardEnabled, setDashboardEnabled] = useState(true);

  const defaultOptions = useMemo(
    () => [
      { data: "Desktop", label: local.desktopMode },
      { data: "Gaming", label: local.gamingMode },
    ],
    [local.desktopMode, local.gamingMode]
  );

  const refresh = async () => {
    try {
      setStatus(await getStatus());
    } catch (error) {
      setStatus(undefined);
      toaster.toast({
        title: "Gaming Mode",
        body: error instanceof Error ? error.message : local.notConnected,
      });
    }
  };

  const run = async (path: string, title: string) => {
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
    refresh();
    void readEnvironment().then((environment) => setDashboardEnabled(environment?.enabled !== false));
    const timer = window.setInterval(refresh, 5000);
    return () => window.clearInterval(timer);
  }, []);

  return (
    <>
      <PanelSection title={local.mode}>
        <PanelSectionRow>
          <ButtonItem disabled={busy} layout="below" onClick={() => run("/mode/gaming/switch", local.gamingMode)}>
            {local.switchGaming}
          </ButtonItem>
        </PanelSectionRow>
        <PanelSectionRow>
          <ButtonItem disabled={busy} layout="below" onClick={() => run("/mode/desktop/switch", local.desktopMode)}>
            {local.switchDesktop}
          </ButtonItem>
        </PanelSectionRow>
        <PanelSectionRow>
          <DropdownItem
            label={local.defaultStartup}
            disabled={busy}
            rgOptions={defaultOptions}
            selectedOption={status?.defaultMode ?? "Desktop"}
            onChange={setDefault}
          />
        </PanelSectionRow>
      </PanelSection>

      {/* La Dashboard sta sotto ai tre comandi della Gaming Mode: quelli sono
          le voci storiche del pannello, e chi le cerca le trova dove sono
          sempre state. */}
      <PanelSection title={local.dashboard}>
        <PanelSectionRow>
          <ButtonItem
            disabled={!dashboardEnabled}
            layout="below"
            onClick={() => openDashboard()}
          >
            {local.openDashboard}
          </ButtonItem>
        </PanelSectionRow>
        <PanelSectionRow>
          <style>{`
            @keyframes phShortcutPulse { 0%,100% { opacity:.62; transform:scale(.96); } 50% { opacity:1; transform:scale(1); } }
            @keyframes phShortcutTravel { 0% { transform:translateX(-3px); opacity:.35; } 50%,100% { transform:translateX(3px); opacity:1; } }
            .ph-dashboard-shortcut { box-sizing:border-box; width:100%; padding:9px 16px 14px; color:rgba(255,255,255,.88); }
            .ph-dashboard-shortcut-figure { width:min(100%,292px); min-height:42px; margin:0 auto; display:flex; align-items:center; justify-content:center; gap:7px; color:#fff; }
            .ph-dashboard-keys { display:flex; align-items:center; gap:4px; }
            .ph-dashboard-key { min-width:28px; height:24px; padding:0 6px; display:grid; place-items:center; border:1.5px solid rgba(255,255,255,.88); border-radius:6px; font-size:9px; line-height:1; font-weight:700; background:transparent; }
            .ph-dashboard-plus { font-size:13px; opacity:.58; }
            .ph-dashboard-flow { width:22px; height:15px; flex:0 0 auto; animation:phShortcutTravel 1.6s ease-in-out infinite; }
            .ph-dashboard-flow path { fill:rgba(255,255,255,.82); }
            .ph-dashboard-pad { width:48px; height:34px; flex:0 0 auto; animation:phShortcutPulse 1.6s ease-in-out infinite; }
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
              <svg className="ph-dashboard-pad" viewBox="0 0 64 44" focusable="false">
                <path className="pad-shell" d="M18.4 12.2h27.2c4.8 0 8.4 3.1 9.7 7.6l4.1 13.7c.9 3-1.4 6.1-4.6 6.1-1.5 0-2.9-.7-3.8-1.9l-6.2-8H19.2l-6.2 8a4.8 4.8 0 0 1-8.4-4.2l4.1-13.7c1.3-4.5 4.9-7.6 9.7-7.6Z" />
                <path className="pad-detail" d="M19.3 20.2v9.1M14.8 24.75h9M25.3 15.8h3.2M35.5 15.8h3.2" />
                <circle className="pad-guide" cx="32" cy="20.2" r="2.35" />
                <circle className="pad-button" cx="46.3" cy="21.2" r="1.65" />
                <circle className="pad-button" cx="50.4" cy="25.2" r="1.65" />
                <circle className="pad-button" cx="46.3" cy="29.2" r="1.65" />
                <circle className="pad-button" cx="42.2" cy="25.2" r="1.65" />
                <circle className="pad-detail" cx="25.1" cy="33.2" r="3.1" />
                <circle className="pad-detail" cx="38.9" cy="33.2" r="3.1" />
              </svg>
            </div>
            <div className="ph-dashboard-shortcut-title">{local.dashboardShortcut}</div>
            <div className="ph-dashboard-shortcut-copy">{local.dashboardSteamInput}</div>
            <div className="ph-dashboard-shortcut-copy">{local.dashboardWindowSwitch}</div>
          </div>
        </PanelSectionRow>
      </PanelSection>
    </>
  );
}

// ---------------------------------------------------------------------------
// Focus rescue
// ---------------------------------------------------------------------------
//
// Su Windows il "foreground lock" impedisce a Steam di portare la finestra Big
// Picture sopra un gioco quando si preme il tasto Steam/QAM (suono si',
// finestra no). Qui, dentro la CEF di Steam, intercettiamo l'attivazione
// dell'overlay e:
//   1. proviamo la via interna: SteamClient.Window.BringToFront(AndForceOS);
//   2. avvisiamo l'helper Win32 di Playhub (porta 47992) che toglie il TOPMOST
//      al gioco e forza Big Picture in primo piano.
// A overlay chiuso l'helper ripristina TOPMOST e focus del gioco.
// Se l'overlay e' correttamente agganciato in-game (utenti senza il problema)
// non facciamo nulla: zero regressioni. Il borderless resta intatto.

const EWindowBringToFront_AndForceOS = 1;

async function overlayHookedInGame(appId: number): Promise<boolean> {
  try {
    const infos = await (window as any).SteamClient.Overlay.GetOverlayBrowserInfo();
    return Array.isArray(infos) && infos.some((info: any) => info && info.appID === appId && (info.unPID ?? 0) > 0);
  } catch {
    return false;
  }
}

function installFocusRescue(): () => void {
  let overlayWasActive = false;
  let registration: any;
  try {
    registration = (window as any).SteamClient.Overlay.RegisterForOverlayActivated(
      async (_overlayPid: number, appId: number, active: boolean) => {
        try {
          if (active) {
            overlayWasActive = true;
            // Se l'overlay Steam e' gia' agganciato in-game, il menu appare
            // dentro il gioco: non interferire.
            if (await overlayHookedInGame(appId)) {
              return;
            }
            try {
              (window as any).SteamClient.Window.BringToFront(EWindowBringToFront_AndForceOS);
            } catch {}
            void requestDashboardSteamFocus();
            // Retry: se il primo tentativo e' arrivato mentre Windows stava
            // ancora negando il cambio di primo piano.
            window.setTimeout(() => { void requestDashboardSteamFocus(); }, 450);
          } else if (overlayWasActive) {
            overlayWasActive = false;
            void restoreDashboardSourceFocus();
          }
        } catch {}
      }
    );
  } catch {}
  return () => {
    try {
      registration?.unregister?.();
    } catch {}
  };
}

// ---------------------------------------------------------------------------

export default definePlugin(() => {
  const uninstallFocusRescue = installFocusRescue();
  const stopOpenRequestWatcher = startOpenRequestWatcher();

  try {
    routerHook?.addRoute?.(DASHBOARD_ROUTE, () => <DashboardPage />, { exact: true });
  } catch (error) {
    console.warn("Playhub Dashboard: rotta non aggiunta", error);
  }

  return {
    name: "Gaming Mode",
    titleView: <div className={staticClasses?.Title}>Gaming Mode</div>,
    content: <Content />,
    icon: <FaGamepad />,
    onDismount() {
      clearDashboardChrome();
      uninstallFocusRescue();
      stopOpenRequestWatcher();
      try {
        routerHook?.removeRoute?.(DASHBOARD_ROUTE);
      } catch (error) {
        console.warn("Playhub Dashboard: rotta non rimossa", error);
      }
    },
  };
});

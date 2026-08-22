import { DFL, SP_REACT as React } from "./decky";
import { API_BASE, logToAgent } from "./api";

const { useEffect, useRef } = React;
const { Focusable, Navigation } = DFL as any;

export const BRIDGE_ROUTE = "/playhub-bridge";

// IL PONTE: STEAM LEGGE IL PAD, LA FINESTRA NATIVA DISEGNA.
//
// Questa schermata non mostra niente ed e' esattamente cio' che deve fare.
//
// Il problema che risolve. Su Windows non esiste il compositore che sullo Steam
// Deck mette la Big Picture sopra il gioco: una schermata di Steam, dentro un
// gioco a schermo intero, sta sotto e non si vede. L'unica cosa che si vede
// davvero e' una finestra nativa sempre in primo piano.
//
// Ma una finestra nativa, per essere navigabile, dovrebbe leggere il
// controller, ed e' esattamente l'errore che ha distrutto Steam Input nella
// 1.2.0: due programmi che aprono lo stesso dispositivo.
//
// Qui le due cose sono separate:
//
//   - il CONTROLLER lo legge Steam, come sempre, perche' la sua interfaccia ha
//     il fuoco. Noi non apriamo nessun dispositivo, mai;
//   - questa pagina riceve i comandi del pad dalle API di Steam e li SPEDISCE
//     all'agente, che li consegna alla finestra nativa;
//   - la finestra nativa disegna e sposta la selezione. Non legge niente.
//
// Il gioco non riceve niente perche' il fuoco resta a Steam, e Steam non fa
// niente perche' questa pagina si mangia tutti i comandi invece di lasciarli
// scorrere verso la sua interfaccia.
//
// PERCHE' LA SCHERMATA E' VUOTA. Perche' e' coperta dalla finestra nativa e
// nessuno la vedra' mai. Disegnarci qualcosa sarebbe lavoro sprecato, e
// soprattutto: qualsiasi cosa disegnassimo qui resterebbe comunque sotto al
// gioco, che e' il problema da cui siamo partiti.

// I numeri dei pulsanti sono quelli di Steam, presi dall'enumerazione di
// @decky/ui. Si traducono in nomi perche' la finestra nativa non deve sapere
// niente di Steam.
const BUTTON_NAMES: Record<number, string> = {
  1: "ok",
  2: "cancel",
  3: "secondary",
  4: "options",
  5: "tabprev",
  6: "tabnext",
  9: "up",
  10: "down",
  11: "left",
  12: "right",
};

function send(path: string, body?: Record<string, unknown>): Promise<any> {
  return fetch(`${API_BASE}${path}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: body ? JSON.stringify(body) : undefined,
  })
    .then((response) => (response.ok ? response.json() : null))
    .catch(() => null);
}

export function OverlayBridge() {
  const alive = useRef(true);
  const anchor = useRef<HTMLDivElement | null>(null);
  const seen = useRef(0);

  useEffect(() => {
    alive.current = true;
    logToAgent("ponte aperto: da qui i comandi di Steam vanno alla finestra nativa");

    void send("/dash/overlay/show").then((result) => {
      if (!result?.ok) {
        // La finestra non si e' aperta: inutile restare su una schermata vuota.
        logToAgent("ponte: la finestra nativa non si e' aperta, torno indietro");
        try { Navigation?.NavigateBack?.(); } catch { /* niente da fare */ }
      }
    });

    // Il battito e' la corda di sicurezza. Se questa pagina sparisce senza
    // passare dalla sua chiusura, per esempio perche' Steam ricarica
    // l'interfaccia, l'agente smette di riceverlo e chiude la finestra da solo.
    // Senza questo, una finestra sempre in primo piano potrebbe restare a
    // schermo per sempre.
    const beat = window.setInterval(() => {
      if (!alive.current) return;
      void send("/dash/overlay/heartbeat").then((result) => {
        // La finestra si e' chiusa per conto suo (per esempio l'utente ha
        // scelto una voce): questa pagina non ha piu' motivo di esistere.
        if (result && result.open === false) {
          try { Navigation?.NavigateBack?.(); } catch { /* niente da fare */ }
        }
      });
    }, 2000);

    // IL FUOCO DEVE STARE QUI DENTRO.
    //
    // Steam consegna i comandi del pad all'elemento che ha il fuoco nella sua
    // interfaccia. Se il fuoco resta altrove, questa pagina non riceve niente e
    // la finestra nativa non si muove. Non basta chiederlo una volta: nel
    // momento in cui la pagina nasce, Steam sta ancora finendo di navigare.
    let attempts = 0;
    const grab = window.setInterval(() => {
      attempts++;
      const node = anchor.current;
      if (node) {
        try {
          const target = node.querySelector<HTMLElement>("[tabindex]:not([tabindex='-1'])") ?? node;
          target.focus({ preventScroll: true });
        } catch {
          /* si riprova al giro dopo */
        }
      }
      if (attempts > 10) window.clearInterval(grab);
    }, 120);

    // Dopo qualche secondo si dice nel log quanti comandi sono arrivati. Se
    // sono zero, il problema e' il fuoco dentro Steam e non la finestra.
    const report = window.setTimeout(() => {
      logToAgent(`ponte: comandi ricevuti da Steam finora: ${seen.current}`);
    }, 5000);

    return () => {
      alive.current = false;
      window.clearInterval(beat);
      window.clearInterval(grab);
      window.clearTimeout(report);
      void send("/dash/overlay/hide");
      logToAgent(`ponte chiuso (comandi ricevuti: ${seen.current})`);
    };
  }, []);

  // Un comando che arriva qui non deve piu' andare da nessuna parte.
  const stop = (event: any) => {
    try {
      event?.preventDefault?.();
      event?.stopPropagation?.();
      event?.stopImmediatePropagation?.();
    } catch {
      /* se l'evento non lo consente, pazienza */
    }
  };

  const forward = (button: number) => {
    const name = BUTTON_NAMES[button];
    if (!name) return;

    seen.current++;
    if (seen.current <= 3) logToAgent(`ponte: primo comando ricevuto (${name})`);

    void send("/dash/overlay/input", { button: name }).then((result) => {
      if (result && result.open === false) {
        try { Navigation?.NavigateBack?.(); } catch { /* niente da fare */ }
      }
    });
  };

  return (
    <Focusable
      ref={anchor}
      // Prende il fuoco appena appare, altrimenti i comandi finirebbero altrove.
      preferredFocus
      // Nero pieno: non si vedra' comunque, la copre la finestra nativa.
      style={{ width: "100%", height: "100%", background: "#000" }}
      // Tutti i comandi passano di qui e non proseguono verso l'interfaccia di
      // Steam. E' cosi' che si evita di muovere la libreria alle spalle della
      // Dashboard mentre la si usa.
      onButtonDown={(event: any) => {
        // NIENTE DEVE PROSEGUIRE OLTRE.
        //
        // Se un comando sfugge, Steam lo usa per navigare la sua interfaccia
        // alle nostre spalle: bastava un paio di "su" perche' il fuoco finisse
        // sulla barra di ricerca in cima, e da li' in poi succedeva di tutto.
        // Qui ogni comando viene fermato: prima si blocca, poi si inoltra.
        stop(event);
        forward(Number(event?.detail?.button));
      }}
      onGamepadDirection={(event: any) => {
        stop(event);
        forward(Number(event?.detail?.button));
      }}
      onCancelButton={(event: any) => { stop(event); forward(2); }}
      onOKButton={(event: any) => { stop(event); forward(1); }}
      onSecondaryButton={(event: any) => { stop(event); forward(3); }}
      onOptionsButton={(event: any) => { stop(event); forward(4); }}
      onMenuButton={(event: any) => stop(event)}
    >
      <div />
    </Focusable>
  );
}

// La versione 1.0.2 di @decky/rollup esporta la funzione solo come default;
// versioni piu' recenti la espongono anche col nome. Accettiamo entrambe le
// forme, cosi' un aggiornamento del pacchetto non ferma la build.
import * as deckyRollup from "@decky/rollup";

const deckyPlugin = deckyRollup.deckyPlugin ?? deckyRollup.default;

export default deckyPlugin({});

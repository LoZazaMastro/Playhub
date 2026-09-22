// La versione 1.0.2 di @decky/rollup esporta la funzione solo come default;
// versioni piu' recenti la espongono anche col nome. Accettiamo entrambe le
// forme, cosi' un aggiornamento del pacchetto non ferma la build.
import * as deckyRollup from "@decky/rollup";
import { readFileSync } from "node:fs";

const deckyPlugin = deckyRollup.deckyPlugin ?? deckyRollup.default;

const config = deckyPlugin({});
config.plugins.unshift({
  name: "editorial-webp",
  load(id) {
    if (id.endsWith(".webp")) {
      return `export default ${JSON.stringify("data:image/webp;base64," + readFileSync(id).toString("base64"))};`;
    }
  },
});
export default config;

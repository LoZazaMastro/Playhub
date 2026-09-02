<div align="center">

# Playhub Gaming Mode per Decky

### Il ponte fra Steam e la Gaming Mode di Playhub.

Passaggio fra Gaming e Desktop, Dashboard richiamabile dal controller, riavvii guidati e feedback aptico direttamente nel QAM.

[![Versione 1.4.0](https://img.shields.io/badge/Versione-1.4.0-ffffff?style=for-the-badge&labelColor=111111)](../../README.md)
[![Licenza MIT](https://img.shields.io/badge/Licenza-MIT-ffffff?style=for-the-badge&labelColor=111111)](../../LICENSE)

</div>

## Il compagno Decky di Gaming Mode

Questo plugin collega Steam Big Picture all'agente locale installato da Playhub. Non modifica direttamente shell, servizi o processi di Windows: invia richieste all'agente su `127.0.0.1:47991`, che esegue e verifica le operazioni di sistema.

- passaggio a Gaming Mode o Desktop Mode;
- scelta della modalità predefinita all'accesso;
- stato di Steam, Decky e del server di streaming configurato;
- Playhub Dashboard aperta come pagina nativa di Steam e controllabile dal gamepad;
- ritorno al gioco e ripristino del focus di Steam;
- feedback aptico per navigazione, selezione, tab, interruttori e slider, con intensità regolabile;
- riavvio diretto in Gaming Mode o Desktop Mode con conferma;
- pulsanti di riavvio aggiunti anche al menu di alimentazione di Steam.

## Requisiti e installazione

Il plugin è un componente di [Playhub](https://github.com/LoZazaMastro/Playhub) e richiede Gaming Mode con il relativo agente locale. L'installer e gli strumenti di riparazione dell'app mantengono allineati plugin, agente e configurazione.

Per un'installazione normale usa quindi Playhub. Lo ZIP Decky separato è destinato allo sviluppo e alle operazioni di ripristino.

## Sviluppo

```powershell
pnpm install
pnpm run test
pnpm run build
```

Il bundle viene generato in `dist/index.js`. Dopo una sostituzione del frontend, Steam può richiedere un riavvio completo per svuotare la cache di Decky.

## Licenza

Playhub Gaming Mode per Decky fa parte di Playhub ed è distribuito con licenza [MIT](../../LICENSE).

<div align="center">

Creato e mantenuto da **[LoZazaMastro](https://github.com/LoZazaMastro)**.

</div>

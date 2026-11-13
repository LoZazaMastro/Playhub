<div align="center">

# Weather

### Il meteo che ti serve, senza lasciare Steam.

Condizioni attuali, previsioni giornaliere e dettaglio orario in un pannello compatto, leggibile dal divano e completamente navigabile con il controller.

[![Versione 2.1.1](https://img.shields.io/badge/Versione-2.1.1-ffffff?style=for-the-badge&labelColor=111111)](https://github.com/LoZazaMastro/Weather)
[![Licenza MIT](https://img.shields.io/badge/Licenza-MIT-ffffff?style=for-the-badge&labelColor=111111)](LICENSE)

</div>

## Il meteo dentro Gaming Mode

Weather porta nel QAM una panoramica immediata delle condizioni attuali, dei prossimi cinque giorni e delle prossime 24 ore. Il layout è progettato per lo spazio ridotto del menu rapido, senza testi tagliati o scorrimenti scomodi.

- meteo attuale, previsione a cinque giorni e dettaglio delle prossime 24 ore;
- temperatura opzionale accanto all'orologio di Steam;
- icona meteo della barra superiore attivabile separatamente;
- ricerca per città o coordinate;
- unità metriche e imperiali;
- dati [Open-Meteo](https://open-meteo.com), senza chiave API;
- lingua rilevata automaticamente da Steam;
- inglese, italiano, francese, spagnolo, portoghese, portoghese brasiliano, tedesco, olandese, ucraino, cinese e giapponese;
- navigazione completa con il controller.

Il badge della barra superiore viene gestito direttamente dal plugin: non richiede CSS Loader.

## Prima configurazione

La località iniziale è Milano. Apri **Weather → Impostazioni**, cerca una città oppure inserisci coordinate come `45.4642,9.19`, quindi salva. Nella stessa pagina puoi scegliere le unità e decidere se mostrare temperatura e icona accanto all'orologio.

## Installazione

Puoi installare e aggiornare Weather dal Plugin Store di [Playhub](https://github.com/LoZazaMastro/Playhub), oppure scaricare lo ZIP dall'[ultima release](https://github.com/LoZazaMastro/Weather/releases/latest) e installarlo da **Decky → Impostazioni → Sviluppatore → Installa plugin da ZIP**.

Dopo il primo aggiornamento alla versione 2.1.0 può essere necessario riavviare Steam una volta, così il canale CEF usato dal badge nella barra superiore viene inizializzato correttamente.

## Sviluppo

```powershell
npm install
npm run build
```

La build genera `dist/index.js`, caricato da Decky insieme a `main.py`.

## Licenza

Weather è distribuito con licenza [MIT](LICENSE). Le attribuzioni aggiuntive sono raccolte in [NOTICE](NOTICE).

<div align="center">

Creato e mantenuto da **[LoZazaMastro](https://github.com/LoZazaMastro)**.

</div>

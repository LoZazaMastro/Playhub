# Home News, Audio e Video — verifica 9 settembre 2026

## Modifiche
- Progetto e build in F:\Playhub\Plugin\Playhub.
- Audio e Video mostrano il titolo della tab e i controlli direttamente, senza categoria richiudibile. Gli ID persistiti delle tab rimangono compatibili.
- Impostazioni > News: categoria espandibile, interruttore e dropdown SteamUI per il paese. Disattivata per impostazione predefinita. La lingua segue Playhub; catalogo limitato alle sue 12 lingue.
- Novità: news editoriali, titoli aggiornati nativi, attività/notizie native. Le ultime due righe conservano gli oggetti React e il comportamento di Steam. Amici e Consigliati non vengono modificati.
- Feed aggiornati ogni 30 minuti mentre la riga è montata; cache backend di 30 minuti anche tra aperture. Articoli recenti, deduplicazione, alternanza delle testate, immagini RSS con ripiego sul logo. Fallback testuale se anche il logo è irraggiungibile. Nessuna traduzione automatica degli articoli.
- Solo titoli, immagini fornite dai feed, fonte e collegamento. Nessun testo integrale degli articoli. Paese e lingua vengono passati separatamente alla ricerca regionale.
- Se un feed fallisce, gli altri possono contribuire; se falliscono tutti, rimangono gli ultimi risultati della sessione. Nessuna cache offline dopo il riavvio.
- Struttura Steam non riconosciuta o errore React: Home originale conservata/ripristinata. Disattivazione e smontaggio rimuovono la modifica.

## Verifica
- 395 test frontend passati, zero saltati; include test browser della suite esistente.
- 251 test Python passati, inclusi opt-in senza rete, validazione impostazioni, cache/guasto rete, metadati RSS, payload e XML non ammessi, paese distinto dalla lingua.
- TypeScript e build-plugin.bat completati.
- Prova temporanea del modulo TS nella Home di Steam già avviata, con risposte feed reali raccolte dal backend Python e trasporto RPC simulato: 24 schede; tre righe nell’ordine richiesto; larghezza schede 300 px; immagini caricate. La prova non certifica il ciclo RPC della versione installata.
- Freccia destra: focus dalla prima alla seconda scheda. Clic su articolo: browser interno Steam aperto sull’URL di Multiplayer.it.
- Smontaggio preview: zero schede Playhub e cinque contenitori originali ripristinati.
- Page.captureScreenshot di CEF non ha risposto: la verifica estetica salvata in temp/news-layout.png è un rendering in Chrome dell’HTML effettivamente montato in Steam, non uno screenshot nativo Steam.
- 15 test del trasporto DSU esistente passati, con socket UDP loopback. NON certificano sensori fisici o integrazione DSU nella distribuzione.

## Limiti aperti
- UDP: il trasporto controller_porting è sperimentale e non distribuito. Non è stato aggiunto un interruttore che finga l’esistenza di una sorgente sensori. Va chiarito se la richiesta riguarda DSU/Cemuhook o un controller via rete e completata la relativa integrazione.
- Il crash QAM/Steam segnalato dall’utente, i tasti OEM Ally/Xbox Ally e la scrittura HDR restano non certificati come risolti. Questa modifica non abilita TDP, PawnIO, RTSS o profili energetici.
- Non effettuata un’installazione completa della nuova app; l’installer candidato contiene app/agent compilati nel checkpoint precedente, identici, più i nuovi asset del plugin verificati.

## Riferimenti consultati
- News (MIT): catalogo feed, approccio immagini/logo e navigazione browser. Implementazione del nuovo lettore RSS indipendente.
- Playhub Metadata e Playhub Artworks (GPL-3.0): studio delle integrazioni native e dei contratti Steam. Nessun loro modulo o asset redistribuito/copincollato.
- Home Concealer (MIT): identifica le aree native; nessun suo foglio CSS applicato globalmente.
- Steam installato: componente WhatsNew e identificatori funzionali BasicHomeUpdates/eventsToShow e RecentlyCompletedCarousel; misura 300 px.
- 4Gamer, documentazione ufficiale dei feed: https://www.4gamer.net/rss/rss.shtml e https://www.4gamer.net/games/000/G000000/20120706060/.

## Spazio su C:
- Trasferita la build valida da Documents\Codex\2026-09-09\c\work\release a temp/release nel progetto: circa 1,55 GiB liberati su C:, dati conservati su F: e SHA256 verificato.
- Copiati nel progetto script di verifica, CDP e precedente installer.
- Cancellazione di work/outputs e successivamente delle sole build obsolete checkpoint/final/revision2 rifiutata dalla revisione automatica (blocked by policy, nessun motivo più specifico). Le cartelle non sono state cancellate. Nessun altro progetto o configurazione Codex rimosso.

## Installer candidato
- Percorso: F:\Playhub\Plugin\Playhub\Installer\Playhub Setup.exe
- SHA256: ad8c69c0afd2562ad1021463e622f3e70daa4e980a3aef4f44adf141ffb3196d
- Payload ZIP: CRC completo verificato, footer PLHB e lunghezza verificati, bundle confrontato col sorgente compilato; assenti cpu_power, rtss_fps e bytecode Python.


## Aggiornamento: regressioni e News (verifica della versione installata)
Queste verifiche sostituiscono le informazioni precedenti sulla preview e sul candidato installer.
- News attive per default; una disattivazione salvata resta rispettata. Catalogo di sole testate specializzate per paese. Italia: Multiplayer.it, SpazioGames, IGN Italia; Everyeye rimosso. Feed diretto IGN Italia verificato HTTP 200.
- Backend realmente avviato nel Python congelato di Decky: rimosso xml.etree, assente in quel runtime. L'inizializzazione News è facoltativa e non blocca i controlli.
- RPC reali per capacità, audio e display riusciti. Tab Video e Decky selezionate con Enter via CEF; non una certificazione del pulsante A fisico.
- Proiezione Decky: wrapper trattenuti da Steam dopo reload preservati tramite antenati Decky comuni, senza accettare wrapper estranei. Live available=true, ready=true, elenco plugin presente.
- News usa Carousel e classi nativi Steam. 24 notizie reali via RPC; navigazione direzionale fino all'ultima, completamente visibile (bordo destro 1316 px su viewport 1353 px).
- Fonte solo sopra la card. Misure in Steam: card 300x335, copertina 300x169, uguali anche dopo errore di caricamento immagine. Titolo: line-height 20px, max-height 80px; corretto il limite nativo di 52px che tagliava le righe. Quattro righe complete; titoli più lunghi hanno ellissi.
- Test: 397 frontend passati senza skip, più un nuovo test browser sulla regressione delle quattro righe; 254 Python passati. TypeScript e build completati.
- Una riga nativa temporaneamente assente non ripristina più tutta la Home. Test dei render con righe mancanti e diagnostica tramite Symbol.for('playhub.home-news.diagnostics'). Nessuna scomparsa osservata durante le verifiche successive; non certifica stabilità indefinita.
- CSS finale applicato alla Home aperta senza scaricare il plugin. Bundle installato e installer contengono la correzione per i prossimi avvii.
- Nuovo installer: F:/Playhub/Plugin/Playhub/Source/PlayhubSetup/Output/Playhub Setup.exe
- SHA256: d75599f4f65c6078ebf3b4ef11c7a9750a1ab2c0dcc280b4e5279a606bc25e6c. Footer, hash payload e CRC ZIP verificati.
- Rimozione del vecchio Installer/Playhub Setup.exe rifiutata dalla revisione automatica con blocked by policy; il file resta presente. Nessun aggiramento tentato.
- Accade oggi: ricerca preliminare effettuata; implementazione non inclusa in questo installer. Restano i limiti hardware e UDP indicati sopra.

# Calendario editoriale senza articoli ripetuti

## Stato e misura corretta del lavoro

Il catalogo in completamento contiene 51 articoli tematici originali, non 365. Le dodici traduzioni di una scheda sono versioni dello stesso articolo; due o tre capitoli sono parti dello stesso articolo. Nessuno dei due numeri aumenta la copertura del calendario.

Per assegnare un articolo diverso a ogni giorno di un anno ordinario servono almeno 314 articoli aggiuntivi. Per un anno bisestile ne servono almeno 315. Questi sono limiti inferiori: diventano maggiori se una scheda rimane riservata a un evento datato, se alcune ricorrenze devono essere trattate insieme o se un articolo esistente non è adatto al giorno assegnato. Un archivio che debba evitare ripetizioni anche fra anni diversi richiede altri 365 o 366 articoli originali per ogni anno successivo.

La selezione deterministica modulo il numero di temi garantisce stabilità, ma non unicità. Non può essere presentata come soluzione al requisito. Cambiare titolo, copertina, ordine dei capitoli o etichetta della data non produce un nuovo articolo.

## Contratto proposto

Tenere separati gli articoli e le assegnazioni annuali:

- articles: identificatore editoriale stabile, soggetto principale, soggetti collegati, angolo specifico, introduzione e capitoli originali nelle dodici lingue, fonti verificate, piano immagini e stato di revisione.
- editions: anno e data ISO completa, article_id, eventuali eventi documentati del giorno, regione pertinente e stato di pubblicabilità.
- Ogni article_id compare al massimo una volta nella stessa edizione annuale.
- La data della ricorrenza è verificata separatamente dal testo interpretativo.
- Una scheda non verificata o non localizzata non entra nell'edizione pubblicabile. Un giorno vuoto resta un gap dichiarato nel report di sviluppo; non viene riempito con un clone.
- Anche cambiando article_id, la sostanziale duplicazione di introduzione e capitoli rende due schede lo stesso articolo ai fini editoriali.

Il controllo automatico deve verificare le 365 o 366 date, unicità degli identificatori assegnati, assenza di testi identici fra articoli, dodici localizzazioni complete, fonti e immagini previste. Un controllo di similarità segnala le parafrasi troppo vicine, ma la revisione umana decide se l'angolo sia realmente diverso.

## Collisioni già presenti da curare

| Giorno | Materiali in conflitto | Trattamento editoriale da scrivere |
| --- | --- | --- |
| 21 febbraio | Zelda in Giappone; Rayman 3 in Europa | Un articolo principale completo e un approfondimento distinto per l'altra ricorrenza, oppure un percorso comparativo originale sul disegno dell'avventura. Non riciclare entrambe le schede integrali altrove nello stesso anno. |
| 23 giugno | Sonic in Nord America; Super Mario 64 in Giappone | Possibile articolo originale sulle due grammatiche del movimento, con regioni e anni espliciti. Le schede esistenti restano materiale di partenza, non due nuove date. |
| 9 settembre | Crash Bandicoot; PlayStation e Dreamcast nordamericane | Percorso originale fra mascotte e lancio di console, mantenendo distinte le tre date storiche. |
| 3 dicembre | Accessibilità; PlayStation in Giappone | Curatela che dia priorità alla ricorrenza globale senza eliminare il lancio documentato: approfondimento distinto, non sostituzione casuale. |
| 19 novembre 2026 | Uscita prevista GTA VI; anniversario europeo Underground 2 | Edizione speciale GTA condizionata all'annuncio ufficiale, con spazio separato per Underground 2. Nessuna dichiarazione anticipata di uscita avvenuta. |

Le due ricorrenze regionali PlayStation non autorizzano a usare due volte lo stesso articolo. Un secondo pezzo deve avere un oggetto diverso, per esempio il progetto della macchina e la ricezione nei mercati, con fonti e immagini pertinenti a ciascuno.

## Produzione del materiale mancante

Prima si costruisce una scaletta annuale di 365 soggetti e angoli, poi si scrivono i testi: generare centinaia di schede prima di conoscere il calendario rischia di lasciare ricorrenze scoperte e duplicazioni.

Una distribuzione di lavoro per i 314 pezzi mancanti può essere:

| Famiglia | Nuovi articoli | Criterio |
| --- | ---: | --- |
| Giochi e serie | 125 | Opere importanti, diverse epoche e generi; meccaniche, luoghi, musica o ricezione documentata. |
| Autori e mestieri | 63 | Designer, compositori, artisti, programmatori: ruolo preciso e almeno un'opera mostrata. |
| Studi ed editori | 47 | Progetti concreti e lavoro collettivo; distinguere sviluppo, pubblicazione e distribuzione. |
| Tecnologie, interfacce e conservazione | 47 | Oggetti, strumenti e pratiche con una storia dimostrabile, non definizioni enciclopediche brevi. |
| Eventi e connessioni culturali | 32 | Ricorrenze globali e storie contestuali originali, senza anniversari inventati. |
| Totale | 314 | Dodici traduzioni per articolo, non conteggiate come articoli aggiuntivi. |

Ogni gruppo viene consegnato in blocchi piccoli revisionabili: scaletta e fonti; testo originale italiano e inglese; altre dieci lingue; immagini e diritti; verifica incrociata con l'archivio; assegnazione alla data. Le fonti Wikipedia, IGDB, archivi e siti ufficiali aiutano a documentare: non sostituiscono la scrittura.

Le icone richieste dall'utente rimangono punti fermi del calendario. Un gioco può tornare come confronto all'interno di un altro articolo, ma un articolo sul suo compositore deve approfondire il lavoro musicale, non ripetere il pezzo sulla velocità o sul combattimento cambiando il soggetto del titolo.

## Criterio di completamento

Il lavoro annuale è completo quando ogni giorno selezionabile mostra un articolo distinto, completo e localizzato con immagini pertinenti; tutte le ricorrenze obbligatorie sono rappresentate e le collisioni hanno una decisione editoriale verificabile. I 51 articoli attuali costituiscono un primo blocco, non il completamento dell'anno.

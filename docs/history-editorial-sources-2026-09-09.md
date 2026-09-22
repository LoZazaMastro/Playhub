# Accade oggi: catalogo editoriale e fonti

Revisione: 9 settembre 2026. File dati: Source/GamingModeDeckyPlugin/quick_settings/history_editorial.json.

## Contenuto

48 schede originali: 35 percorsi centrati su giochi, 6 su autori, 4 su storia e oggetti, 2 su eventi stagionali, 1 su uno studio. Ogni scheda ha un titolo editoriale e un'introduzione in tutte le 12 lingue Playhub: en, it, de, es, fr, pt, ru, uk, ja, ko, zh, hi. IT/EN: 45–85 parole. Le altre lingue hanno testi autonomi più concisi, senza riempire le traduzioni mancanti con inglese. Le traduzioni non hanno ricevuto una revisione umana madrelingua.

Questa è una selezione iniziale riutilizzabile lungo l'anno, non 365 articoli unici. Il backend deve alternare i percorsi generici in modo deterministico, mantenendo priorità a date speciali ed eventi documentati. Le schede includono titoli richiesti: Underground 2, Rayman 3, Mario 64, Crash, Metal Gear Solid, Kojima, Miyamoto e Koji Kondo. Terra Nil è stato rimosso; la Giornata della Terra è dedicata a Final Fantasy VII. Nessun titolo nato su mobile è incluso.

I titoli invitano a una lettura: “Quanto costa una città accesa”, “La notte ha il suo suono”, “La melodia indica la strada”, “Il peso di un timbro”. I paragrafi sono critica e interpretazione editoriali, non citazioni delle fonti.

## Contratto per l'integrazione

- themes contiene id, kind, topic (titolo Wikipedia EN), related_topic, title12, intro12, sources.
- calendar contiene 16 chiavi MM-DD → theme_id. occasion aggiunge date, label12 e source.
- calendar_only:true esclude i temi stagionali dalla rotazione generica: non mostrare “per la Giornata della Terra” in un giorno casuale.
- anniversaries è un array di 18 ricorrenze con date MM-DD, year, region, platform, theme_id, topic, kind, title12, intro12 e sources. Non usare una mappa uno-a-uno: più icone condividono una data.
- Usare l'introduzione dell'anniversario se presente: Minecraft il 17 maggio ha un testo che non chiama quel giorno Giornata dell'educazione.
- kind birth è una nascita, release un'uscita regionale, commemoration una celebrazione convenzionale. Non etichettarli tutti “oggi usciva”.
- Il 6 giugno di Tetris è registrato come commemoration/World Tetris Day, non certificazione di una pubblicazione commerciale storicamente univoca.
- events contiene un evento singolo 2026-11-19, status scheduled, dedicato alla storia di Grand Theft Auto per GTA VI. verified_on 2026-09-09. Non trasformarlo automaticamente in anniversario annuale o in uscita già avvenuta. Il tema gta ha event_only:true.
- region usa JP, NA, EU, WORLD; platform conserva distinzione di edizione/piattaforma. Tradurre le etichette nell'interfaccia.
- Le fonti sono collegamenti consultabili. Gli estratti Wikipedia e le immagini devono mantenere attribuzione e collegamento alla pagina/file; il catalogo non concede diritti su immagini esterne.

## Date e fonti

Le giornate internazionali si riferiscono al [calendario ONU](https://www.un.org/en/observances/list-days-weeks). Fonti UNESCO specifiche: [educazione, 24 gennaio](https://www.unesco.org/en/days/education), [arte, 15 aprile](https://www.unesco.org/en/days/world-art), [libro, 23 aprile](https://www.unesco.org/en/days/world-book-and-copyright), [scienza, 10 novembre](https://www.unesco.org/en/days/science-peace-development). Altre date: donne e ragazze nella scienza 11 febbraio, foreste 21 marzo, acqua 22 marzo, Terra 22 aprile, oceani 8 giugno, amicizia 30 luglio, pace 21 settembre, città 31 ottobre, persone con disabilità 3 dicembre, montagna 11 dicembre.

Natale è un aggancio editoriale al 25 dicembre, non una data universale di tutte le tradizioni religiose. Il testo precisa che [Toy Day di Animal Crossing è il 24 dicembre](https://play.nintendo.com/news-tips/news/acnh-in-game-holiday-events/); lo stesso articolo Nintendo documenta il conto alla rovescia del 31 dicembre.

Anniversari regionali:

| Data | Soggetto | Riferimento |
|---|---|---|
| 21 febbraio 1986 | Zelda, Giappone/Famicom Disk System | [Nintendo History](https://www.nintendo.com/jp/character/zelda/history/index.html) |
| 21 febbraio 2003 | Rayman 3, Europa/GameCube | [Wikipedia, tabella uscite con riferimenti](https://en.wikipedia.org/wiki/Rayman_3:_Hoodlum_Havoc) |
| 27 febbraio 1996 | Pokémon Rosso/Verde, Giappone/Game Boy | [Pokémon ufficiale](https://www.pokemon.co.jp/info/2020/01/200131_cm01.html) |
| 17 maggio 2009 | Minecraft, prima alpha pubblica | [Mojang, 15º anniversario](https://www.minecraft.net/en-us/article/the-15th-anniversary-cape) |
| 6 giugno | World Tetris Day | [Tetris ufficiale](https://www.tetris.com/) |
| 23 giugno 1991 | Sonic, Nord America/Genesis | [Sega Sonic Channel](https://sonic.sega.jp/SonicChannel/gametitle/SonicTheHedgehog.html) |
| 23 giugno 1996 | Super Mario 64, Giappone/N64 | [Catalogo Nintendo 1996](https://www.nintendo.co.jp/n01/n64/software/1996.html) |
| 13 agosto 1961 | Nascita Koji Kondo | [Wikipedia](https://en.wikipedia.org/wiki/Koji_Kondo) |
| 24 agosto 1963 | Nascita Hideo Kojima | [Wikipedia](https://en.wikipedia.org/wiki/Hideo_Kojima) |
| 3 settembre 1998 | Metal Gear Solid, Giappone/PlayStation | [Wikipedia con riferimenti contemporanei](https://en.wikipedia.org/wiki/Metal_Gear_Solid_(1998_video_game)) |
| 9 settembre 1996 | Crash, Nord America/PlayStation | [Wikipedia](https://en.wikipedia.org/wiki/Crash_Bandicoot_(video_game)) |
| 9 settembre 1995 | PlayStation, Nord America | [Sony, 20º anniversario](https://blog.playstation.com/2015/09/09/20-years-ago-today-2) |
| 9 settembre 1999 | Dreamcast, Nord America | [Relazione annuale Sega 1999, archivio](https://segaretro.org/images/a/ad/AnnualReport1999_English.pdf) |
| 13 settembre 1985 | Super Mario Bros., Giappone/Famicom | [Nintendo](https://www.nintendo.com/jp/famicom/software/smb1/index.html) |
| 16 novembre 1952 | Nascita Shigeru Miyamoto | [Documento societario Nintendo](https://www.nintendo.co.jp/ir/pdf/2016/convocation_notice1606e.pdf) |
| 19 novembre 2004 | Underground 2, Europa/PC e console | [Wikipedia](https://en.wikipedia.org/wiki/Need_for_Speed:_Underground_2) |
| 3 dicembre 1994 | PlayStation, Giappone | [Sony](https://blog.playstation.com/2015/09/09/20-years-ago-today-2) |
| 10 dicembre 1993 | Doom, shareware DOS | [Bethesda](https://bethesda.net/ko-KR/news/doom-turns-22) |

Per Underground 2 la fonte musicale è il [comunicato EA ripubblicato integralmente](https://nintendoworldreport.com/pr/10054/snoop-dogg-headlining-nfsu-2-soundtrack), che conferma EA TRAX e il remix con Snoop Dogg. Non sono riprodotti testi di canzoni. La ricorrenza usa l'uscita europea per separarla da annunci di spedizione nordamericani non sempre omogenei fra gli archivi.

Per Koji Kondo il legame musica/interazione è documentato dalle [conversazioni Nintendo su Ocarina of Time](https://iwataasks.nintendo.com/interviews/3ds/zelda-ocarina-of-time/0/0/) e dalla [discussione sulle transizioni musicali](https://www.nintendo.com/en-gb/Iwata-Asks/Iwata-Asks-The-Legend-of-Zelda-Ocarina-of-Time-3D/Vol-1-Sound/2-Koji-Kondo-Upends-the-Tea-Table/2-Koji-Kondo-Upends-the-Tea-Table-231296.html). Il testo resta originale.

Per Final Fantasy VII, [Square Enix](https://finalfantasyviipc.square-enix-games.com/au) descrive l'estrazione di Mako dal pianeta da parte della Shinra. Il collegamento alla Giornata della Terra è una nostra scelta interpretativa, non una campagna ufficiale.

GTA VI: data prevista 19 novembre 2026 verificata sul [Rockstar Store](https://store.rockstargames.com/game/buy-gta-vi) il 9 settembre 2026. Il percorso storico usa [Grand Theft Auto](https://en.wikipedia.org/wiki/Grand_Theft_Auto) e la [pagina Rockstar di Vice City](https://www.rockstargames.com/games/vicecity). La data futura deve essere ricontrollata prima di presentarla come evento avvenuto.

## Riferimenti visivi e originalità

Consultati [Jerry Lawson / The Strong su Google Arts & Culture](https://artsandculture.google.com/story/sAXRgC0NUykHLA) e la [raccolta videogiochi](https://artsandculture.google.com/search/exhibit?em=m01mw1&categoryId=topic). Principio ricavato: oggetto o immagine significativa, breve capitolo, cambio di scala verso persone e contesto, crediti visibili. Nessun codice, layout proprietario, articolo o immagine GAC copiato nel catalogo. Le quattro storie escluse dall'utente non sono incluse né adattate.

L'alternanza delle immagini e la navigazione controller appartengono al renderer e devono essere verificate separatamente. La presenza di related_topic offre al renderer un secondo soggetto; non garantisce che Wikimedia disponga di una foto utilizzabile.

## Verifiche eseguite

Validazione locale passata: JSON UTF-8, 48 ID unici, 576 titoli e 576 introduzioni non vuoti, esattamente 12 lingue, IT/EN entro 45–85 parole, tutte le date valide, 16 riferimenti calendario risolti, 18 anniversari risolti, evento GTA distinto e futuro, assenza di Terra Nil.

Verifica REST Wikipedia tentata su 86 titoli iniziali: 29 risposte standard, poi HTTP 429. La verifica è stata fermata; non viene dichiarata completa. Nessuna ripetizione automatica aggressiva eseguita. Tramite ricerca web sono stati inoltre verificati i titoli Pokémon Red, Blue, and Yellow, Giant Squid (company), Yoshinori Kitase e Will Wright (game designer); quest'ultimo è stato disambiguato nel catalogo. Gli altri titoli non hanno tutti una verifica REST completata: il backend deve trattare pagine mancanti/rate limit come recuperabili e conservare il testo editoriale locale.

Questo documento certifica contenuti e struttura del catalogo, non il funzionamento della UI Steam, l'effettiva resa di immagini remote o le API dei controlli video.

## Ampliamento editoriale del 10 settembre

Ogni scheda viene ampliata con almeno due capitoli originali nelle stesse dodici lingue; Underground 2 conserva i tre capitoli della prova approvata, con apertura musicale, garage e attraversamento di Bayview. Lo schema aggiunto è chapters, contenente title e body localizzati e image_role (game oppure related). I testi non incorporano collegamenti o attribuzioni inline: sources rimane metadato per la gestione dei crediti.

Il nuovo tema Yuji Naka distingue esplicitamente programmazione, disegno del personaggio e progettazione dei livelli. Il riferimento curatoriale è [ACMI, Game Masters Education Resource, pagina 16](https://acmi-website-media-prod.s3.amazonaws.com/static/documents/Game_Masters_Education_Resource_V2_31.8.17.pdf), che identifica Naka, Naoto Ohshima e Hirokazu Yasuhara nei diversi ruoli. Il testo non attribuisce Sonic a un singolo autore.

Il nuovo tema SEGA collega Sonic e Virtua Fighter, mantenendo distinti marchio e squadre di sviluppo. Riferimenti primari: [storia societaria SEGA](https://www.sega.co.jp/en/company/history/), [cronologia SEGA 1960–2020](https://www.sega.jp/history/companyTimeline/en/) e [storia ufficiale di Virtua Fighter](https://virtua-fighter.com/30th/en/history/).

Aggiunta la fondazione di Nintendo, 23 settembre 1889, con kind foundation e regione JP. La [documentazione societaria Nintendo depositata presso SEC](https://www.sec.gov/Archives/edgar/vprr/0800/08006168.pdf) riporta il giorno preciso; il [profilo societario attuale](https://www.nintendo.co.jp/corporate/en/outline/index.html) conferma settembre 1889 e distingue la costituzione societaria del novembre 1947. Non sono aggiunte ricorrenze di morte.

Il tema Hidetaka Miyazaki collega il ritratto alla costruzione spaziale di Dark Souls e a Elden Ring, distinguendo direzione e lavoro collettivo FromSoftware. Riferimenti primari: [intervista PlayStation del 28 gennaio 2022](https://blog.playstation.com/2022/01/28/an-interview-with-fromsoftwares-hidetaka-miyazki/) e [pagina Bandai Namco di Elden Ring](https://www.bandainamcoent.com/games/elden-ring).

La copertura dell'anno senza ripetizioni non è risolta da questo ampliamento: il catalogo di 51 articoli resta un primo blocco. Quantificazione, collisioni e contratto delle assegnazioni annuali sono documentati in history-annual-editorial-plan-2026-09-10.md.

### Verifica finale del blocco di 51 articoli

Il 10 settembre è stato validato lo snapshot finale: 51 ID unici, 103 capitoli, 1.236 titoli di capitolo e 1.236 corpi localizzati; esattamente dodici lingue per ciascuno. Ogni tema ha due capitoli, Underground 2 ne ha tre. Tutti i testi IT/EN dei capitoli hanno 35–60 parole, con la sola eccezione dei capitoli NFS approvati, entro 70. Non ci sono corpi identici fra capitoli nella stessa lingua, URL inline o traduzioni sostituite dal testo inglese. Date e riferimenti di 16 ricorrenze globali, 19 anniversari e un evento datato sono validi.

Il capitolo Iwata cita il lavoro concreto sulla versione NES di Balloon Fight, verificato nell'[intervista Nintendo dedicata](https://www.nintendo.com/en-gb/News/2016/November/Nintendo-Classic-Mini-NES-special-interview-Volume-2-Balloon-Fight-1154453.html). Non attribuisce l'intero gioco a una sola persona.

Il report strutturato è history-editorial-validation-2026-09-10.json. PASS riguarda contenuti e schema del blocco di 51 articoli; non certifica il requisito di 365 articoli distinti, la resa della UI o la disponibilità delle immagini.
## Verifica delle date di uscita

Per le ricorrenze legate alle pubblicazioni viene usato anche l'indice pubblico di The Cutting Room Floor/Data Crystal, che organizza i giochi per anno, mese e giorno di uscita: https://datacrystal.tcrf.net/wiki/Category:Games_by_release_date

La fonte serve a verificare una data già documentata; non viene usata per inventare un'associazione quando il catalogo non contiene una ricorrenza precisa.

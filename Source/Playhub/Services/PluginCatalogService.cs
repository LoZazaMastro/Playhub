using Playhub.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using ExternalPluginDefinition = Playhub.Models.RemotePluginCatalogEntry;

namespace Playhub.Services;

public sealed partial class PluginCatalogService
{
    private const string Owner = "LoZazaMastro";
    private const string InstalledReleaseMarker = ".playhub-release.json";
    private const string MissingInstalledVersion = "Manifest senza versione";
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly ReadmeLoader SharedReadmes = new(new HttpClient(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    }));
    private readonly ReadmeLoader _readmes;

    public PluginCatalogService() => _readmes = SharedReadmes;

    internal PluginCatalogService(HttpClient detailsHttp, Func<DateTimeOffset>? clock = null,
        TimeSpan? requestTimeout = null)
    {
        _readmes = new ReadmeLoader(detailsHttp, clock, requestTimeout);
        _releaseHttp = detailsHttp;
    }
    private static readonly IReadOnlyDictionary<string, string> PlayhubKeywords =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Playhub-Artworks"] = "artwork cover banner hero logo icone SteamGridDB libreria",
            ["Playhub-Metadata"] = "metadata achievement RetroAchievements Xbox immagini giochi non Steam",
            ["ThemeDeck-Windows"] = "musica soundtrack YouTube yt-dlp audio personalizzazione",
            ["Launch-Curtain"] = "avvio curtain fullscreen overlay logo soundbites",
            ["TrailerHero"] = "trailer hero video Steam YouTube personalizzazione",
            ["Now-Playing"] = "musica player Spotify YouTube Music sessione media surround",
            ["Playhub-Surround"] = "surround stereo 5.1 7.1 altoparlanti audio",
            ["Quick-Settings"] = "volume microfono HDR display impostazioni rapide Windows",
            ["Shortcuts"] = "shortcuts tabs QAM quick access menu plugins icons Decky",
            ["Playhub-Notifications"] = "notifiche toast achievement temi suoni overlay",
            ["News"] = "news RSS Atom feed articoli informazioni",
            ["Weather"] = "meteo previsioni temperatura Open-Meteo",
            ["Proton-VPN"] = "VPN Proton rete connessione privacy Windows"
        };
    private static readonly IReadOnlyDictionary<string, string> PlayhubCatalogVersions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Playhub-Artworks"] = "1.0.0",
            ["Playhub-Metadata"] = "1.8.0",
            ["ThemeDeck-Windows"] = "3.3.2",
            ["Launch-Curtain"] = "2.5.1",
            ["TrailerHero"] = "1.5.0",
            ["Now-Playing"] = "2.5.0",
            ["Playhub-Surround"] = "1.2.1",
            ["Quick-Settings"] = "2.3.1",
            ["Shortcuts"] = "1.0.0",
            ["Playhub-Notifications"] = "1.3.0",
            ["News"] = "1.0.0",
            ["Weather"] = "2.1.0",
            ["Proton-VPN"] = "1.0.0"
        };
    private static readonly JsonSerializerOptions ExternalCatalogJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
    private static readonly HashSet<string> BlockedExternalRepositories = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> BlockedExternalOwnerNames = new(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyList<PluginDefinition> Definitions = new[]
    {
        new PluginDefinition(
            "Playhub-Artworks",
            "Artwork",
            "Playhub Artworks",
            "playhub-artworks",
            ((char)0xE91B).ToString(),
            "Le copertine giuste per ogni gioco della tua libreria.",
            @"Playhub Artworks porta la gestione degli artwork dentro Big Picture, pensata per il controller: cerchi, scegli e applichi copertine, banner, hero, loghi e icone senza mai tornare al desktop.

## Cosa fa
• Cerca gli artwork su SteamGridDB, IGDB, PlayStation Store, Nintendo eShop, Xbox, AlphaCoders, iiDB e IGN.
• Ricorda filtri e ultima fonte usata separatamente per ogni tipo di artwork.
• Crea Perfect Hero e Perfect Banner fondendo sfondo e logo, con posizione, scala, opacità e ombra regolabili.
• Permette di escludere il logo dalla composizione quando vuoi solo lo sfondo.
• Posiziona e ridimensiona il logo del gioco come fa Steam.
• Mostra le copertine quadrate nella libreria e in tutte le file della Home, banner di Steam compreso.
• Completa in background gli artwork mancanti di tutta la libreria.

## Nota
• Per le ricerche che usano SteamGridDB serve una chiave API personale: resta salvata solo sul tuo PC."),
        new PluginDefinition(
            "Launch-Curtain",
            "Launch Curtain",
            "Launch Curtain",
            "launch-curtain",
            ((char)0xE8F1).ToString(),
            "Un portale diretto verso il mondo di gioco.",
            @"Launch Curtain trasforma l'avvio dei giochi PC in qualcosa che assomiglia a una console. Quando lanci un gioco da Steam Big Picture, cala una schermata di caricamento a tutto schermo che nasconde i flash del desktop, i launcher e le finestre fuori posto, lasciando in primo piano il logo del gioco con una dissolvenza morbida. È un plugin esclusivo per Windows.

## Cosa fa
• Avvia una schermata personalizzabile non appena si lancia un gioco.
• Personalizza la tua schermata di Launch Curtain come vuoi: scegli dove posizionare il logo e la sua grandezza, scegli quale sfondo utilizzare e la sua opacità, e scegli se applicare uno zoom animato al logo.
• Si nasconde da sola quando la finestra di gioco raggiunge il fullscreen, o dopo un timeout di sicurezza.
• Si può chiudere con ESC o con il tasto Indietro/Chiudi dei controller più comuni.

## Personalizzazione
• Puoi scegliere un logo tuo (PNG, JPG, WebP o BMP) al posto di quello di Playhub quando non è presente il logo del gioco.
• Puoi regolare quanto a lungo Launch Curtain resta visibile dopo che il gioco è pronto."),
        new PluginDefinition(
            "Now-Playing",
            "Now Playing",
            "Now Playing",
            "now-playing",
            ((char)0xE189).ToString(),
            "Le tue canzoni preferite sempre con te.",
            @"Now Playing è il tuo compagno musicale in stile console: porta la sessione multimediale attiva di Windows dentro il menu rapido di Steam, con copertina, titolo e controlli sempre a portata di gamepad.

## Cosa fa
• Mostra nel menu rapido la sessione media attiva di Windows.
• Visualizza titolo, artista, copertina dell'album e avanzamento del brano.
• Offre i controlli play/pausa, precedente, successivo, shuffle e ripeti quando il player li espone.
• Avvia al volo le app musicali più diffuse: Spotify, TIDAL, Apple Music, Deezer, Amazon Music e SoundCloud.
• Include una vista Now Playing a schermo intero con visualizer.
• Dialoga con Windows tramite un helper dedicato per leggere le sessioni multimediali."),
        new PluginDefinition(
            "Playhub-Metadata",
            "Metadata",
            "Playhub Metadata",
            "playhub-metadata",
            ((char)0xE946).ToString(),
            "Dettagli, immagini e achievement per i tuoi giochi.",
            @"Playhub Metadata rende la libreria Big Picture più curata, ricca e console-like, soprattutto per i giochi non-Steam: titoli PC esterni, Game Pass, app Xbox ed emulatori. Aggiunge metadati, immagini e video della community, categorie e persino achievement.

## Metadati e immagini
• Trova automaticamente i metadati mancanti dei giochi.
• Aggiunge descrizioni, sviluppatori, publisher, date di uscita, valutazioni e schede informative.
• Aggiunge screenshot e media della community quando disponibili.
• Ti lascia modificare manualmente i metadati di ogni gioco.

## Achievement
• Mostra gli achievement dei giochi non-Steam dentro Big Picture.
• Supporta RetroAchievements per ROM ed emulatori.
• Supporta gli achievement Xbox / Game Pass / Microsoft Store tramite OpenXBL (serve importare i giochi tramite la tab Importa Giochi di Playhub).
• Permette di scegliere la fonte per ogni gioco: Auto, RetroAchievements, Xbox o Disattivata.
• Offre cache flessibili (oraria, giornaliera, settimanale, a sessione o manuale) per limitare le chiamate API.

## Nota
• Gli achievement non diventano achievement Steam veri: vengono solo mostrati dentro Big Picture."),
        new PluginDefinition(
            "Quick-Settings",
            "Quick Settings",
            "Quick Settings",
            "quick-settings",
            ((char)0xE713).ToString(),
            "Le impostazioni importanti, sempre a portata di mano.",
            @"Quick Settings porta le impostazioni rapide di Windows dentro Steam Big Picture, tramite un piccolo agente locale avviato dal plugin. Tutto quello che ti serve regolare resta raggiungibile dal menu rapido, senza tornare al desktop.

## Controlli disponibili
• Volume del dispositivo.
• Volume del microfono.
• Overlay per attenuare lo schermo (dimmer).
• Selettori di uscita audio e ingresso microfono.
• Interruttore HDR con conferma a 10 secondi.
• Stato HDR letto direttamente da Windows (DisplayConfig / Advanced Color) invece di affidarsi a uno stato salvato dal plugin."),
        new PluginDefinition(
            "Shortcuts",
            "Shortcuts",
            "Shortcuts",
            "shortcuts",
            ((char)0xE71B).ToString(),
            "Your favourite plugins, directly in the Quick Access Menu.",
            @"Shortcuts brings the Decky plugins you use most into the main Quick Access Menu tab bar. Choose compatible panels, assign an icon and arrange them as you like without removing their original Decky entries.

## What it does
• Turns loaded Decky plugin QAM panels into independent tabs.
• Keeps the original access point inside Decky.
• Lets you use the original icon or choose from the included Tabler icons.
• Reorders and removes only the tabs created by Shortcuts.
• Saves your preferences and automatically restores temporarily unavailable plugins.

## Note
• It uses Decky's internal QAM tab registry because there is no public API for independent top-level tabs yet. A future Decky update may require an adjustment to the plugin."),
        new PluginDefinition(
            "Playhub-Notifications",
            "Playhub Notifications",
            "Playhub Notifications",
            "playhub-notifications",
            ((char)0xEA8F).ToString(),
            "Notifiche più belle, chiare e personali.",
            @"Playhub Notifications sostituisce i popup visibili e i suoni delle notifiche di Steam con temi animati pensati per Big Picture, mantenendo intatta la cronologia nativa delle notifiche.

## Cosa fa
• Offre sette temi dedicati: Xbox Console, PlayStation, GOG Galaxy, Epic Games Launcher, Nintendo, Android e Playhub.
• Personalizza achievement, messaggi, inviti, download, screenshot, controller, avvisi, notifiche di sistema e community.
• Usa l'artwork reale degli achievement fornito da Steam quando disponibile.
• Permette di scegliere posizione, durata e volume delle notifiche, con un intervallo da 0% a 200%.
• Mostra il volume di sistema con un overlay coordinato al tema scelto.
• Include anteprime per provare ogni tipo di notifica direttamente dal menu rapido.

## Note
• Sostituisce soltanto il popup visibile e il relativo suono: la cronologia originale di Steam resta disponibile.
• L'overlay non prende il focus e non intercetta controller, tastiera o mouse mentre giochi."),
        new PluginDefinition(
            "ThemeDeck-Windows",
            "ThemeDeck",
            "ThemeDeck",
            "themedeck",
            ((char)0xE790).ToString(),
            "Le colonne sonore, come meritano di essere ascoltate.",
            @"ThemeDeck dà una colonna sonora alla tua libreria: riproduce una traccia musicale quando apri la pagina di un gioco in Gaming Mode, con musica ambientale opzionale per l'interfaccia e un brano dedicato allo Store. È un fork pensato per Windows e dentro Decky resta col nome ThemeDeck.

## Cosa fa
• Riproduce una traccia personalizzata all'apertura della pagina di dettaglio di un gioco.
• Ti lascia scegliere file audio locali o cercare su YouTube con yt-dlp.
• Scarica e assegna tracce dai risultati di YouTube, con anteprima prima di confermare.
• Supporta volume, skip iniziale e loop per singolo gioco.
• Offre una traccia globale/ambientale per le pagine non di gioco e un brano separato per lo Store.
• Ferma la musica quando un gioco viene avviato o è in esecuzione.
• Può assegnare automaticamente le tracce mancanti cercandole su YouTube.

## Note
• Controlla solo il proprio audio: non tocca il volume di sistema di Windows.
• La release Windows include yt-dlp.exe per far funzionare ricerca e download.
• L'interfaccia si traduce da sola in base alla lingua di Steam/Decky (11 lingue supportate)."),
        new PluginDefinition(
            "TrailerHero",
            "TrailerHero",
            "TrailerHero",
            "trailerhero",
            ((char)0xE714).ToString(),
            "I trailer dei tuoi giochi, alla portata di gamepad.",
            @"TrailerHero fa sembrare Steam Big Picture la dashboard di una console. Quando apri la pagina di un gioco, mantiene l'artwork originale per tre secondi e poi sfuma un trailer in muto nello stesso riquadro hero, scegliendo prima i trailer di Steam e passando a YouTube quando serve.

## Controlli principali
• Enabled attiva o disattiva l'effetto.
• Enable on home riproduce i trailer anche nella home della libreria Big Picture.
• Game page logo sposta il logo del gioco in basso a sinistra durante il trailer e lo ripristina quando esci.
• Automatic CRT applica un effetto CRT discreto ai trailer a bassa risoluzione.
• Source sceglie per ogni gioco la modalità automatica, Steam o YouTube.
• Quality imposta la qualità preferita (720p, 1080p o 2160p) per Steam e YouTube.
• Steam video ti lascia scegliere qualsiasi video Steam del gioco da un menu, non solo il trailer in evidenza.
• Trim start / Trim end salvano il taglio del video per ogni gioco.
• Custom YouTube link salva un trailer YouTube specifico; senza link, l'auto-ricerca preferisce risultati 4K e mantiene rigoroso il match del titolo.

## Note
• È nato su e per Windows, anche se dovrebbe funzionare su Linux.
• Legge e adatta gli elementi dell'interfaccia di Big Picture, che Steam aggiorna spesso: alcuni selettori potrebbero richiedere aggiornamenti nel tempo."),
        new PluginDefinition(
            "News",
            "News",
            "News",
            "news",
            ((char)0xE12A).ToString(),
            "Le notizie che contano, raccolte in un solo posto.",
            @"News porta le tue fonti preferite nel menu rapido e in una comoda edicola a schermo intero, pensata per essere letta anche con il controller.

## Cosa fa
• Raccoglie feed RSS e Atom senza richiedere una API key.
• Organizza fonti e articoli per categorie.
• Offre titoli nel menu rapido e una vista completa in Big Picture.
• Include un lettore pulito, ricerca e navigazione in quattro direzioni.
• Mantiene le fonti configurate e aggiorna i contenuti senza interrompere la navigazione."),
        new PluginDefinition(
            "Weather",
            "Weather",
            "Weather",
            "weather",
            ((char)0xE706).ToString(),
            "Il meteo, semplice e discreto, nel menu rapido.",
            @"Weather è un plugin compatto che porta meteo attuale, previsioni giornaliere e orarie dentro il menu rapido. È pensato per Big Picture e la navigazione con controller, con un layout stretto e sicuro che evita testi tagliati e overflow.

## Cosa fa
• Meteo attuale, previsioni a 5 giorni e prossime 24 ore.
• Backend Open-Meteo, senza bisogno di API key.
• Unità metriche o imperiali.
• Vista impostazioni dedicata per cercare città o coordinate.
• Navigazione controller-friendly (su, giù, sinistra, destra).
• Interfaccia scura e minimale con piccoli dettagli animati.
• Rilevamento automatico della lingua (11 lingue supportate)."),
        new PluginDefinition(
            "Playhub-Surround",
            "Playhub Surround",
            "Playhub Surround",
            "playhub-surround",
            ((char)0xE767).ToString(),
            "Metti alla prova i tuoi altoparlanti, canale per canale.",
            @"Playhub Surround è un piccolo strumento per verificare la disposizione dei tuoi altoparlanti in stereo, 5.1 e 7.1. Mostra una mappa in stile salotto e riproduce suoni di test sintetizzati ispirati ai videogiochi classici - nessun campione protetto da copyright: ogni suono è generato dal vivo con la Web Audio API.

## Cosa fa
• Mostra una mappa degli altoparlanti in stile salotto.
• Supporta i layout stereo, 5.1 e 7.1.
• Riproduce suoni di test sintetizzati, ispirati ai videogiochi classici.
• Genera ogni suono dal vivo con la Web Audio API, senza campioni protetti.
• Include un test sequenziale dei canali, controllo del volume e preset di suoni.
• Navigazione con controller su layout, mappa, preset, volume e pulsante di test.
• Interfaccia tradotta automaticamente nella lingua di Steam (11 lingue).

## Note
• Funziona su Windows; Linux non è testato.
• La riproduzione multicanale dipende da Steam/Chromium e dal dispositivo di uscita scelto: se il sistema espone solo due canali, i test posteriori, centrale e LFE possono essere mixati verso il basso."),
        new PluginDefinition(
            "Proton-VPN",
            "Proton VPN",
            "Proton VPN",
            "proton-vpn",
            ((char)0xE774).ToString(),
            "La tua VPN, senza lasciare la Gaming Mode.",
            @"Proton VPN porta i controlli essenziali del client Windows nel menu rapido, così puoi proteggere o cambiare la connessione senza tornare al desktop.

## Cosa fa
• Mostra lo stato reale della connessione VPN.
• Connette e disconnette Proton VPN dal menu rapido.
• Consente di scegliere una posizione disponibile.
• Mantiene la navigazione semplice e adatta al controller.

## Nota
• Richiede l'app ufficiale Proton VPN per Windows già installata e configurata.")
    };

    private static readonly Lazy<RemotePluginCatalog> BundledStoreCatalog = new(ReadBundledStoreCatalog);

    public static RemotePluginCatalog GetBundledCatalog() => BundledStoreCatalog.Value;

    private static RemotePluginCatalog ReadBundledStoreCatalog()
    {
        var builtIns = Definitions.Select(definition => new RemotePluginCatalogEntry
        {
            Name = definition.DisplayName,
            InstallFolder = definition.Cover,
            Author = Owner,
            Repository = $"{Owner}/{definition.RepositoryName}",
            RepositoryUrl = $"https://github.com/{Owner}/{definition.RepositoryName}",
            Version = PlayhubCatalogVersions.GetValueOrDefault(definition.RepositoryName, ""),
            Category = "Playhub",
            ShortDescription = definition.ShortDescription,
            LongDescription = definition.LongDescription,
            IconGlyph = definition.IconGlyph,
            CatalogStatus = "playhub",
            CatalogSource = "playhub",
            Keywords = PlayhubKeywords.GetValueOrDefault(definition.RepositoryName, "").Split(' ', StringSplitOptions.RemoveEmptyEntries),
            Aliases = new[] { definition.DisplayName, definition.LocalFolder, definition.Cover, definition.RepositoryName }
        });
        var fallback = new RemotePluginCatalog { Plugins = builtIns.Concat(LoadExternalDefinitions()).ToArray() };
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "PluginCatalog", "store-catalog.json");
            using var file = File.OpenRead(path);
            if (file.Length > RemotePluginCatalogService.MaxDocumentBytes) return fallback;
            var bytes = new byte[(int)file.Length];
            file.ReadExactly(bytes);
            return RemotePluginCatalogService.Merge(fallback, RemotePluginCatalogService.Parse(bytes));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            return fallback;
        }
    }

    public Task<IReadOnlyList<DeckyPluginInfo>> LoadAsync(string pluginRoot, string deckyPluginsPath,
        RemotePluginCatalog? catalog = null)
        => Task.Run(() => LoadCatalog(pluginRoot, deckyPluginsPath, catalog ?? GetBundledCatalog()));

    private static IReadOnlyList<DeckyPluginInfo> LoadCatalog(string pluginRoot, string deckyPluginsPath,
        RemotePluginCatalog catalog)
    {
        var plugins = new List<DeckyPluginInfo>();
        var claimedInstalledFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bundled = Definitions.ToDictionary(definition => $"{Owner}/{definition.RepositoryName}", StringComparer.OrdinalIgnoreCase);
        var releases = new Dictionary<string, ReleaseInfo>(StringComparer.OrdinalIgnoreCase);
        var repositories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in catalog.Plugins.Where(entry => entry.Active))
        {
            if (!IsValidRepositorySlug(definition.Repository) ||
                IsBlockedPluginIdentity(new[] { definition.Name, definition.InstallFolder, definition.Repository }
                    .Concat(definition.Aliases).ToArray()) || !repositories.Add(definition.Repository))
                continue;
            var repositoryName = definition.Repository[(definition.Repository.IndexOf('/') + 1)..];
            var isPlayhub = definition.CatalogSource == "playhub" && definition.CatalogStatus == "playhub" &&
                definition.Repository.StartsWith(Owner + "/", StringComparison.OrdinalIgnoreCase);
            bundled.TryGetValue(definition.Repository, out var bundledDefinition);
            var localFolder = bundledDefinition is null ? null : FindLocalFolder(pluginRoot, bundledDefinition.LocalFolder);
            var sourceFolder = localFolder is null ? "" : FindSourceFolder(localFolder);
            var cachedRelease = new ReleaseInfo(null, null, null, null, null);
#if !PLAYHUB_UI_REVIEW
            cachedRelease = LoadReleaseCache(ReleaseCacheKey(definition.Repository, definition.CatalogSource));
#endif
            releases[definition.Repository] = cachedRelease;
            var catalogVersion = isPlayhub
                ? SelectNewestVersion(definition.Version, PlayhubCatalogVersions.GetValueOrDefault(repositoryName, ""),
                    string.IsNullOrWhiteSpace(sourceFolder) ? "" : ReadInstalledVersion(sourceFolder, repositoryName),
                    cachedRelease.Version ?? "")
                : SelectNewestVersion(definition.Version, cachedRelease.Version ?? "");
            var aliases = definition.Aliases.Append(definition.Name).Append(definition.InstallFolder)
                .Append(repositoryName).Append(definition.Repository).Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var installFolder = SafeInstallFolderName(definition.InstallFolder, definition.Name);
            var cover = !string.IsNullOrWhiteSpace(definition.CoverUrl) ? definition.CoverUrl
                : bundledDefinition is null ? null : ResolveCover(bundledDefinition.Cover);
            var manifestRelease = !string.IsNullOrWhiteSpace(definition.CatalogReleaseUrl) &&
                VersionsEquivalent(catalogVersion, definition.Version);
            var published = manifestRelease ? FormatDate(definition.ReleasePublishedAt) : cachedRelease.PublishedAt ?? "";

            plugins.Add(new DeckyPluginInfo
            {
                Name = definition.Name,
                FolderName = installFolder,
                Author = definition.Author,
                Version = catalogVersion,
                ShortDescription = definition.ShortDescription,
                LongDescription = definition.LongDescription,
                Readme = definition.LongDescription,
                IconGlyph = string.IsNullOrWhiteSpace(definition.IconGlyph)
                    ? bundledDefinition?.IconGlyph ?? ((char)0xE8B7).ToString() : definition.IconGlyph,
                SourceFolder = sourceFolder,
                InstallerZip = localFolder is null ? null : FindInstallerZip(localFolder),
                Image = cover,
                CoverImage = cover,
                RepositoryUrl = string.IsNullOrWhiteSpace(definition.RepositoryUrl)
                    ? $"https://github.com/{definition.Repository}" : definition.RepositoryUrl,
                RepositoryName = repositoryName,
                RepositorySlug = definition.Repository,
                ReleaseAssetName = manifestRelease ? definition.ReleaseAssetName : "",
                CatalogReleaseZipUrl = manifestRelease ? definition.CatalogReleaseUrl : cachedRelease.ZipUrl,
                InstallAliases = aliases,
                Category = isPlayhub ? definition.Category : NormalizeExternalCategory(definition.Category),
                Keywords = string.Join(' ', definition.Keywords),
                IsPlayhubPlugin = isPlayhub,
                CatalogStatus = isPlayhub ? "playhub" : NormalizeCatalogStatus(definition.CatalogStatus),
                CatalogSource = isPlayhub ? "playhub" : NormalizeCatalogSource(definition.CatalogSource),
                CatalogPluginId = definition.CatalogPluginId,
                Compatibility = definition.Compatibility,
                ReleasePageUrl = cachedRelease.PageUrl ?? definition.RepositoryUrl,
                ReleasePublishedAt = published,
                UpdatedAt = published,
                InstalledFolder = Path.Combine(deckyPluginsPath, installFolder)
            });
        }

        // One installed-folder pass hydrates both bundled and newly published definitions.
        AppendUncataloguedInstalledPlugins(plugins, deckyPluginsPath, claimedInstalledFolders);
#if !PLAYHUB_UI_REVIEW
        foreach (var plugin in plugins)
        {
            var cacheKey = ReleaseCacheKey(plugin.RepositorySlug, plugin.CatalogSource);
            var latest = releases.GetValueOrDefault(plugin.RepositorySlug) ?? LoadReleaseCache(cacheKey);
            ApplyLatestRelease(plugin, latest);
            if (plugin.HasUpdate && !VersionsEquivalent(latest.Version, plugin.Version))
                latest = new ReleaseInfo(null, null, null, null, null);
            var changelog = SelectChangelog(cacheKey, plugin.IsInstalled, plugin.InstalledVersion,
                plugin.HasUpdate, latest);
            plugin.ReleaseNotes = changelog.Notes ?? "";
            plugin.ReleaseNotesVersion = changelog.Version ?? "";
            plugin.ReleaseNotesPublishedAt = changelog.PublishedAt ?? "";
        }
#endif

        var displayOrder = new[]
        {
            "Playhub-Artworks",
            "Playhub-Metadata",
            "ThemeDeck-Windows",
            "Launch-Curtain",
            "TrailerHero",
            "Now-Playing",
            "Playhub-Surround",
            "Quick-Settings",
            "Playhub-Notifications",
            "News",
            "Weather",
            "Proton-VPN"
        };

        IReadOnlyList<DeckyPluginInfo> result = plugins
            .OrderBy(p =>
            {
                if (!p.IsPlayhubPlugin)
                {
                    return int.MaxValue;
                }
                var index = Array.IndexOf(displayOrder, p.RepositoryName);
                return index >= 0 ? index : int.MaxValue;
            })
            .ThenBy(p => p.IsPlayhubPlugin ? "" : p.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(p => p.IsPlayhubPlugin ? "" : p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return result;
    }

    public async Task EnsurePluginDetailsAsync(DeckyPluginInfo plugin)
    {
        if (plugin.IsPlayhubPlugin || !IsValidRepositorySlug(plugin.RepositorySlug))
        {
            return;
        }

        try
        {
            var readme = await _readmes.GetAsync(plugin.RepositorySlug);
            if (!string.IsNullOrWhiteSpace(readme.Text))
            {
                plugin.Readme = readme.Text;
                plugin.LongDescription = readme.Text;
            }
            // Replace stale media even when the README is empty; never erase useful descriptions.
            // Copies keep UI failure handling from mutating the shared cache.
            plugin.Media = readme.Media.Select(media => new PluginMediaInfo
                { Url = media.Url, Kind = media.Kind, Alt = media.Alt }).ToList();
        }
        catch
        {
            plugin.Media = new List<PluginMediaInfo>();
        }
    }

    internal async Task<string?> FindPluginPreviewAsync(DeckyPluginInfo plugin, ISet<string> rejected)
    {
        async Task<string?> FirstImageAsync(IEnumerable<string?> candidates)
        {
            foreach (var candidate in candidates.Where(value => !string.IsNullOrWhiteSpace(value))
                         .Distinct(StringComparer.Ordinal).Take(8))
            {
                if (rejected.Contains(candidate!)) continue;
                if (File.Exists(candidate)) return candidate;
                if (IsRemoteUri(candidate!, out var uri) && !rejected.Contains(uri.AbsoluteUri) &&
                    await _readmes.GetMediaKindAsync(uri.AbsoluteUri).ConfigureAwait(false) == "image")
                    return uri.AbsoluteUri;
            }
            return null;
        }

        var image = await FirstImageAsync(new[] { plugin.CoverImage, plugin.Image }
            .Concat(plugin.Media.Where(media => media.Kind == "image").Select(media => media.Url)))
            .ConfigureAwait(false);
        if (image is not null || plugin.IsPlayhubPlugin || !IsValidRepositorySlug(plugin.RepositorySlug))
            return image;

        // Only a missing/failed card asks for README media; reuse detail validation and single-flight caches.
        var readme = await _readmes.GetAsync(plugin.RepositorySlug).ConfigureAwait(false);
        image = await FirstImageAsync(readme.Media.Where(media => media.Kind == "image")
            .Select(media => media.Url)).ConfigureAwait(false);
        return image ?? await FirstImageAsync(new[]
            { $"https://opengraph.githubassets.com/1/{plugin.RepositorySlug}" }).ConfigureAwait(false);
    }

    private static IReadOnlyList<ExternalPluginDefinition> LoadExternalDefinitions()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "PluginCatalog", "external-plugins.json");
        if (!File.Exists(path))
        {
            return Array.Empty<ExternalPluginDefinition>();
        }

        try
        {
            var catalog = JsonSerializer.Deserialize<ExternalPluginCatalog>(
                File.ReadAllText(path),
                ExternalCatalogJsonOptions);
            if (catalog?.Plugins is null)
            {
                return Array.Empty<ExternalPluginDefinition>();
            }

            var repositories = new HashSet<string>(
                Definitions.Select(definition => NormalizeRepositorySlug($"{Owner}/{definition.RepositoryName}")),
                StringComparer.OrdinalIgnoreCase);
            var ownerNames = new HashSet<string>(
                Definitions.Select(definition => $"{Normalize(Owner)}/{Normalize(definition.DisplayName)}"),
                StringComparer.OrdinalIgnoreCase);
            return catalog.Plugins
                .OrderBy(plugin => string.Equals(plugin.CatalogSource, "decky-store", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .Where(plugin => plugin.Active &&
                                 !IsBlockedExternalPlugin(plugin) &&
                                 IsValidRepositorySlug(plugin.Repository) &&
                                 !string.IsNullOrWhiteSpace(plugin.Name) &&
                                 !string.IsNullOrWhiteSpace(plugin.Author) &&
                                 !string.IsNullOrWhiteSpace(plugin.Category) &&
                                 !string.IsNullOrWhiteSpace(plugin.ShortDescription) &&
                                 !string.IsNullOrWhiteSpace(plugin.LongDescription) &&
                                 !string.IsNullOrWhiteSpace(plugin.Compatibility) &&
                                 IsValidCatalogStatus(plugin.CatalogStatus) &&
                                 !string.IsNullOrWhiteSpace(plugin.Version) &&
                                 !string.IsNullOrWhiteSpace(plugin.ReleaseAssetName) &&
                                 !string.IsNullOrWhiteSpace(plugin.CatalogReleaseUrl) &&
                                 repositories.Add(NormalizeRepositorySlug(plugin.Repository)) &&
                                 ownerNames.Add(ExternalOwnerName(plugin)))
                .ToList();
        }
        catch
        {
            return Array.Empty<ExternalPluginDefinition>();
        }
    }

    private static bool IsBlockedExternalPlugin(ExternalPluginDefinition plugin)
    {
        if (IsBlockedPluginIdentity(
                plugin.Name,
                plugin.InstallFolder,
                plugin.Repository,
                plugin.RepositoryUrl))
        {
            return true;
        }

        var repository = NormalizeRepositorySlug(plugin.Repository);
        if (BlockedExternalRepositories.Contains(repository))
        {
            return true;
        }

        var ownerName = ExternalOwnerName(plugin);
        var authorName = $"{Normalize(plugin.Author)}/{Normalize(plugin.Name)}";
        return BlockedExternalOwnerNames.Contains(ownerName) ||
               BlockedExternalOwnerNames.Contains(authorName);
    }

    private static string NormalizeRepositorySlug(string value)
    {
        var parts = (value ?? "")
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2
            ? $"{parts[0].ToLowerInvariant()}/{parts[1].ToLowerInvariant()}"
            : "";
    }

    private static bool IsBlockedPluginIdentity(params string[] identities)
    {
        foreach (var identity in identities.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var normalized = Normalize(identity);
            if (normalized is "varta" or "vartaplugin" or "gamingmode" or "playhubgamingmode")
            {
                return true;
            }

            var repository = ExtractGithubRepositorySlug(identity);
            if (!string.IsNullOrWhiteSpace(repository) &&
                Normalize(repository[(repository.IndexOf('/') + 1)..]) is "varta" or "vartaplugin" or "gamingmode" or "playhubgamingmode")
            {
                return true;
            }
        }

        return false;
    }

    private static string ExternalOwnerName(ExternalPluginDefinition plugin)
    {
        var parts = (plugin.Repository ?? "")
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var owner = parts.Length == 2 ? parts[0] : plugin.Author;
        return $"{Normalize(owner)}/{Normalize(plugin.Name)}";
    }

    private static bool IsValidRepositorySlug(string value)
    {
        var parts = (value ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2 && parts.All(part => part.Length > 0 && part.All(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' or '.'));
    }

    private static bool IsValidCatalogStatus(string value) =>
        string.Equals(value, "decky", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "github", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeCatalogStatus(string value) =>
        string.Equals(value, "decky", StringComparison.OrdinalIgnoreCase) ? "decky" : "github";

    private static string NormalizeCatalogSource(string value) =>
        string.Equals(value, "decky-store", StringComparison.OrdinalIgnoreCase)
            ? "decky-store"
            : "outside-store";

    private static string NormalizeExternalCategory(string value)
    {
        var normalized = (value ?? "").Trim();
        if (normalized.Equals("Giochi e libreria", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Libreria e giochi", StringComparison.OrdinalIgnoreCase))
        {
            return "Libreria e giochi";
        }

        if (normalized.Equals("Media e personalizzazione", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Personalizzazione e media", StringComparison.OrdinalIgnoreCase))
        {
            return "Personalizzazione e media";
        }

        if (normalized.Equals("Sistema e connettività", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Sistema e hardware", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Controller e hardware", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Rete e strumenti", StringComparison.OrdinalIgnoreCase))
        {
            return "Sistema e hardware";
        }

        if (normalized.Equals("Social e community", StringComparison.OrdinalIgnoreCase))
        {
            return "Social e community";
        }

        if (normalized.Equals("Strumenti e utilità", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Strumenti e utilita", StringComparison.OrdinalIgnoreCase))
        {
            return "Strumenti e utilità";
        }

        return normalized;
    }

    private static string SafeInstallFolderName(string requested, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(requested) ? fallback : requested;
        if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.Contains('/') || value.Contains('\\'))
        {
            return MakeInstallFolderName(fallback);
        }
        return value.Trim();
    }

    private static string SelectNewestVersion(params string[] versions)
    {
        var newest = "";
        foreach (var version in versions.Where(version => TryParseSemanticVersion(version, out _)))
        {
            if (string.IsNullOrWhiteSpace(newest) ||
                CompareSemanticVersions(version, newest) > 0)
            {
                newest = version;
            }
        }
        return newest;
    }

    private static string? ResolveCover(string slug)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "PluginCovers", slug + ".png");
        return File.Exists(path) ? path : null;
    }

    private static async Task<List<GithubRepo>> SafeLoadGithubReposAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var json = await Http.GetStringAsync($"https://api.github.com/users/{Owner}/repos?per_page=100", cts.Token);
            using var doc = JsonDocument.Parse(json);
            var repos = new List<GithubRepo>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                repos.Add(new GithubRepo(
                    item.GetProperty("name").GetString() ?? "",
                    item.TryGetProperty("html_url", out var url) ? url.GetString() ?? "" : "",
                    item.TryGetProperty("updated_at", out var up) ? FormatDate(up.GetString()) : ""));
            }

            return repos;
        }
        catch
        {
            return new List<GithubRepo>();
        }
    }

    private static async Task<ReleaseInfo> SafeGetLatestReleaseAsync(string repoName,
        string? repositorySlug = null, HttpClient? http = null)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var json = await (http ?? Http).GetStringAsync($"https://api.github.com/repos/{repositorySlug ?? Owner + "/" + repoName}/releases/latest", cts.Token);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var assets = root.TryGetProperty("assets", out var assetsProperty)
                ? assetsProperty.EnumerateArray().ToList()
                : new List<JsonElement>();

            var asset = assets
                .Where(a => (a.GetProperty("name").GetString() ?? "").EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(a => (a.GetProperty("name").GetString() ?? "").Contains("installer", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            var release = new ReleaseInfo(
                asset.ValueKind == JsonValueKind.Undefined ? null : asset.GetProperty("browser_download_url").GetString(),
                root.TryGetProperty("html_url", out var h) ? h.GetString() : null,
                root.TryGetProperty("tag_name", out var t) ? t.GetString() : null,
                root.TryGetProperty("body", out var b) ? b.GetString() : null,
                root.TryGetProperty("published_at", out var p) ? FormatDate(p.GetString()) : null);
            release = PreserveCachedNotes(repoName, release);
            SaveReleaseCache(repoName, release);
            return release;
        }
        catch
        {
            var atomRelease = await TryGetLatestReleaseFromAtomAsync(repoName, repositorySlug, http);
            if (!string.IsNullOrWhiteSpace(atomRelease.Version) || !string.IsNullOrWhiteSpace(atomRelease.Notes))
            {
                atomRelease = PreserveCachedReleaseData(repoName, atomRelease);
                SaveReleaseCache(repoName, atomRelease);
                return atomRelease;
            }
            return LoadReleaseCache(repoName);
        }
    }

    private static ReleaseInfo SelectChangelog(
        string repoName,
        bool isInstalled,
        string installedVersion,
        bool hasUpdate,
        ReleaseInfo latestRelease)
    {
        if (!isInstalled || string.IsNullOrWhiteSpace(installedVersion))
        {
            return latestRelease;
        }

        if (!hasUpdate)
        {
            if (VersionsEquivalent(installedVersion, latestRelease.Version) && !string.IsNullOrWhiteSpace(latestRelease.Notes))
            {
                SaveInstalledReleaseCache(repoName, installedVersion, latestRelease);
                return latestRelease;
            }
            return LoadInstalledReleaseCache(repoName, installedVersion);
        }

        return latestRelease;
    }

    private static async Task<ReleaseInfo> TryGetLatestReleaseFromAtomAsync(string repoName,
        string? repositorySlug = null, HttpClient? http = null)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var xml = await (http ?? Http).GetStringAsync($"https://github.com/{repositorySlug ?? Owner + "/" + repoName}/releases.atom", cts.Token);
            var document = XDocument.Parse(xml);
            XNamespace atom = "http://www.w3.org/2005/Atom";
            var entry = document.Root?.Elements(atom + "entry").FirstOrDefault();
            if (entry is null)
            {
                return new ReleaseInfo(null, null, null, null, null);
            }

            var pageUrl = entry.Elements(atom + "link")
                .FirstOrDefault(link => string.Equals((string?)link.Attribute("rel"), "alternate", StringComparison.OrdinalIgnoreCase))
                ?.Attribute("href")?.Value;
            var version = string.IsNullOrWhiteSpace(pageUrl)
                ? null
                : Uri.UnescapeDataString(pageUrl[(pageUrl.LastIndexOf('/') + 1)..]);
            var html = entry.Element(atom + "content")?.Value ?? "";
            var notes = CleanReleaseHtml(html);
            var published = FormatDate(entry.Element(atom + "updated")?.Value);
            return new ReleaseInfo(null, pageUrl, version, notes, published);
        }
        catch
        {
            return new ReleaseInfo(null, null, null, null, null);
        }
    }

    private static string CleanReleaseHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html) || string.Equals(html.Trim(), "No content.", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        var text = Regex.Replace(html, @"(?i)<li[^>]*>", "• ");
        text = Regex.Replace(text, @"(?i)</(li|p|h[1-6]|ul|ol)>", "\n");
        text = Regex.Replace(text, "<[^>]+>", "");
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"[ \t]+\n", "\n");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    private static ReleaseInfo PreserveCachedReleaseData(string repoName, ReleaseInfo release)
    {
        var cached = LoadReleaseCache(repoName);
        if (!string.Equals(NormalizeVersion(cached.Version), NormalizeVersion(release.Version), StringComparison.OrdinalIgnoreCase))
        {
            return release;
        }

        return release with
        {
            ZipUrl = string.IsNullOrWhiteSpace(release.ZipUrl) ? cached.ZipUrl : release.ZipUrl,
            Notes = string.IsNullOrWhiteSpace(release.Notes) ? cached.Notes : release.Notes
        };
    }

    private static ReleaseInfo PreserveCachedNotes(string repoName, ReleaseInfo release)
    {
        if (!string.IsNullOrWhiteSpace(release.Notes))
        {
            return release;
        }

        var cached = LoadReleaseCache(repoName);
        if (string.Equals(
                NormalizeVersion(cached.Version),
                NormalizeVersion(release.Version),
                StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(cached.Notes))
        {
            return release with { Notes = cached.Notes };
        }

        return release;
    }

    private static void SaveReleaseCache(string repoName, ReleaseInfo release)
    {
        if (string.IsNullOrWhiteSpace(release.Version) &&
            string.IsNullOrWhiteSpace(release.Notes) &&
            string.IsNullOrWhiteSpace(release.PageUrl))
        {
            return;
        }

        try
        {
            var directory = Path.Combine(AppPaths.LocalDataRoot, "cache", "plugin-releases");
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, SanitizeCacheName(repoName) + ".json"),
                JsonSerializer.Serialize(release, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    private static void SaveInstalledReleaseCache(string repoName, string installedVersion, ReleaseInfo release)
    {
        if (string.IsNullOrWhiteSpace(release.Notes))
        {
            return;
        }

        try
        {
            var directory = Path.Combine(AppPaths.LocalDataRoot, "cache", "plugin-releases", "installed");
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, InstalledReleaseCacheName(repoName, installedVersion)),
                JsonSerializer.Serialize(release, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    private static ReleaseInfo LoadInstalledReleaseCache(string repoName, string installedVersion)
    {
        try
        {
            var path = Path.Combine(
                AppPaths.LocalDataRoot,
                "cache",
                "plugin-releases",
                "installed",
                InstalledReleaseCacheName(repoName, installedVersion));
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<ReleaseInfo>(File.ReadAllText(path))
                    ?? new ReleaseInfo(null, null, null, null, null);
            }
        }
        catch
        {
        }

        return new ReleaseInfo(null, null, null, null, null);
    }

    private static string InstalledReleaseCacheName(string repoName, string installedVersion) =>
        SanitizeCacheName(repoName) + "-" + SanitizeCacheName(installedVersion) + ".json";

    private static ReleaseInfo LoadReleaseCache(string repoName)
    {
        try
        {
            var path = Path.Combine(
                AppPaths.LocalDataRoot,
                "cache",
                "plugin-releases",
                SanitizeCacheName(repoName) + ".json");
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<ReleaseInfo>(File.ReadAllText(path))
                    ?? new ReleaseInfo(null, null, null, null, null);
            }
        }
        catch
        {
        }

        return new ReleaseInfo(null, null, null, null, null);
    }

    private static string SanitizeCacheName(string value) =>
        Regex.Replace(value, "[^a-zA-Z0-9._-]+", "-");

    private static string NormalizeVersion(string? value) =>
        TryParseSemanticVersion(value, out var version) ? version.Canonical : "";

    private sealed record ReadmeDocument(string Markdown, Uri Url, Uri Root);

    private sealed class ReadmeLoader
    {
        private const int MaxReadmeBytes = 1024 * 1024;
        private const int ProbeBytes = 4096;
        private readonly HttpClient _http;
        private readonly TimeSpan _timeout;
        private readonly SemaphoreSlim _requests = new(4);
        private readonly AsyncResultCache<ReadmeInfo> _readmes;
        private readonly AsyncResultCache<string?> _media;

        public ReadmeLoader(HttpClient http, Func<DateTimeOffset>? clock = null, TimeSpan? timeout = null)
        {
            _http = http;
            _timeout = timeout ?? TimeSpan.FromSeconds(8);
            clock ??= () => DateTimeOffset.UtcNow;
            _readmes = new(256, StringComparer.OrdinalIgnoreCase, clock);
            _media = new(1024, StringComparer.Ordinal, clock);
        }

        public Task<ReadmeInfo> GetAsync(string slug) => _readmes.GetAsync(slug, () => LoadAsync(slug));

        public Task<string?> GetMediaKindAsync(string url) => _media.GetAsync(url, () => ProbeAsync(url));

        private async Task<ReadmeInfo> LoadAsync(string slug)
        {
            ReadmeDocument? document;
            using (var timeout = new CancellationTokenSource(_timeout))
                document = await FetchReadmeAsync(slug, timeout.Token).ConfigureAwait(false);
            if (document is null)
                return new ReadmeInfo("", "", new());

            var text = CleanMarkdown(RemoveMediaMarkdown(document.Markdown));
            var media = new List<PluginMediaInfo>();
            try
            {
                var candidates = ExtractMedia(document).Take(12).ToArray();
                using var timeout = new CancellationTokenSource(_timeout);
                // Validate in small batches so broken early links do not consume the gallery limit.
                for (var offset = 0; offset < candidates.Length && media.Count < 6; offset += 4)
                {
                    var batch = candidates.Skip(offset).Take(4).ToArray();
                    var kinds = await Task.WhenAll(batch.Select(candidate =>
                        _media.GetAsync(candidate.Url, () => ProbeAsync(candidate.Url))))
                        .WaitAsync(timeout.Token).ConfigureAwait(false);
                    for (var index = 0; index < batch.Length && media.Count < 6; index++)
                        if (kinds[index] is { } kind)
                            media.Add(new PluginMediaInfo { Url = batch[index].Url, Kind = kind, Alt = batch[index].Alt });
                }
            }
            catch
            {
                // Media is optional. A slow or malformed asset must not discard the README.
            }
            return new ReadmeInfo(text, MakeSummary(text), media);
        }

        private async Task<ReadmeDocument?> FetchReadmeAsync(string slug, CancellationToken token)
        {
            try
            {
                var data = await ReadResponseAsync(new Uri($"https://api.github.com/repos/{slug}/readme"),
                    "application/vnd.github+json", MaxReadmeBytes, false, token).ConfigureAwait(false);
                if (data is not null)
                {
                    using var json = JsonDocument.Parse(data.Value.Bytes);
                    var root = json.RootElement;
                    var path = root.GetProperty("path").GetString() ?? "";
                    var download = root.GetProperty("download_url").GetString() ?? "";
                    var escapedPath = string.Join("/", path.Split('/').Select(Uri.EscapeDataString));
                    if (path.Length > 0 && IsRemoteUri(download, out var url) &&
                        url.Host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase) &&
                        url.AbsolutePath.StartsWith($"/{slug}/", StringComparison.OrdinalIgnoreCase) &&
                        url.AbsolutePath.EndsWith("/" + escapedPath, StringComparison.Ordinal))
                    {
                        var repositoryRoot = new Uri(url.GetLeftPart(UriPartial.Path)[..^escapedPath.Length]);
                        if (root.TryGetProperty("encoding", out var encoding) && encoding.GetString() == "base64" &&
                            root.TryGetProperty("content", out var content) && content.GetString() is { Length: > 0 } encoded)
                            return new ReadmeDocument(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)), url, repositoryRoot);
                        var raw = await ReadResponseAsync(url, "text/plain", MaxReadmeBytes, false, token).ConfigureAwait(false);
                        if (raw is not null && raw.Value.Mime != "text/html")
                            return new ReadmeDocument(Encoding.UTF8.GetString(raw.Value.Bytes), url, repositoryRoot);
                    }
                }
            }
            catch (Exception) when (!token.IsCancellationRequested)
            {
            }
            catch (OperationCanceledException) { return null; }

            // Raw HEAD follows the default branch even when the API is rate-limited.
            foreach (var path in new[] { "README.md", "Readme.md", "readme.md", ".github/README.md", "docs/README.md" })
            {
                try
                {
                    var root = new Uri($"https://raw.githubusercontent.com/{slug}/HEAD/");
                    var url = new Uri(root, path);
                    var data = await ReadResponseAsync(url, "text/plain", MaxReadmeBytes, false, token).ConfigureAwait(false);
                    if (data is not null && data.Value.Mime != "text/html")
                        return new ReadmeDocument(Encoding.UTF8.GetString(data.Value.Bytes), url, root);
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
            return null;
        }

        private async Task<string?> ProbeAsync(string url)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(Math.Min(_timeout.TotalMilliseconds, 3000)));
                var data = await ReadResponseAsync(new Uri(url), "image/*, video/*, application/octet-stream",
                    ProbeBytes, true, timeout.Token).ConfigureAwait(false);
                return data is null ? null : DetectMediaKind(data.Value.Bytes, data.Value.Mime);
            }
            catch { return null; }
        }

        private async Task<(byte[] Bytes, string Mime)?> ReadResponseAsync(Uri url, string accept,
            int limit, bool probe, CancellationToken token)
        {
            await _requests.WaitAsync(token).ConfigureAwait(false);
            try
            {
                for (var redirects = 0; redirects <= 4; redirects++)
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.UserAgent.ParseAdd("Playhub/1.0");
                    request.Headers.Accept.ParseAdd(accept);
                    if (probe) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, limit - 1);
                    using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                    if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or
                        HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                    {
                        if (response.Headers.Location is not { } location ||
                            !Uri.TryCreate(url, location, out var target) || !IsRemoteUri(target.AbsoluteUri, out url))
                            return null;
                        continue;
                    }
                    if (response.StatusCode != HttpStatusCode.OK && response.StatusCode != HttpStatusCode.PartialContent)
                        return null;
                    if (response.StatusCode == HttpStatusCode.PartialContent &&
                        (!probe || response.Content.Headers.ContentRange?.From != 0))
                        return null;
                    var mime = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "";
                    if (probe && !IsMediaMime(mime)) return null;
                    if (!probe && response.Content.Headers.ContentLength > limit) return null;
                    using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                    using var bytes = new MemoryStream();
                    var buffer = new byte[Math.Min(limit, 8192)];
                    while (bytes.Length < limit)
                    {
                        var count = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, limit - (int)bytes.Length)), token).ConfigureAwait(false);
                        if (count == 0) break;
                        bytes.Write(buffer, 0, count);
                    }
                    if (!probe && bytes.Length == limit && await stream.ReadAsync(buffer.AsMemory(0, 1), token).ConfigureAwait(false) != 0)
                        return null;
                    return (bytes.ToArray(), mime);
                }
                return null;
            }
            finally { _requests.Release(); }
        }
    }

    // Cache misses run off the caller's synchronization context, including extraction and JSON parsing.
    private sealed class AsyncResultCache<T>
    {
        private sealed record Entry(DateTimeOffset Created, Lazy<Task<T>> Task);
        private readonly Dictionary<string, Entry> _entries;
        private readonly int _capacity;
        private readonly Func<DateTimeOffset> _clock;

        public AsyncResultCache(int capacity, IEqualityComparer<string> comparer, Func<DateTimeOffset> clock)
        {
            _capacity = capacity;
            _clock = clock;
            _entries = new Dictionary<string, Entry>(comparer);
        }

        public Task<T> GetAsync(string key, Func<Task<T>> factory)
        {
            Entry entry;
            lock (_entries)
            {
                var now = _clock();
                if (_entries.TryGetValue(key, out var cached) &&
                    (now - cached.Created < TimeSpan.FromMinutes(5) || !cached.Task.IsValueCreated || !cached.Task.Value.IsCompleted))
                    entry = cached;
                else
                {
                    entry = new Entry(now, new Lazy<Task<T>>(() => System.Threading.Tasks.Task.Run(factory)));
                    if (_entries.Count >= _capacity)
                    {
                        var oldest = _entries.OrderBy(pair => pair.Value.Created)
                            .FirstOrDefault(pair => pair.Value.Task.IsValueCreated && pair.Value.Task.Value.IsCompleted);
                        if (oldest.Key is not null) _entries.Remove(oldest.Key);
                    }
                    if (_entries.Count < _capacity || _entries.ContainsKey(key)) _entries[key] = entry;
                }
            }
            return entry.Task.Value;
        }
    }

    private static bool IsMediaMime(string mime) => mime.Length == 0 || mime == "application/octet-stream" ||
        mime.StartsWith("image/", StringComparison.Ordinal) || mime.StartsWith("video/", StringComparison.Ordinal);

    private static string? DetectMediaKind(byte[] bytes, string mime)
    {
        if (!IsMediaMime(mime)) return null;
        var data = bytes.AsSpan();
        string? kind = null;
        if ((data.Length >= 24 && data.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) && data.Slice(12, 4).SequenceEqual("IHDR"u8)) ||
            (data.Length >= 4 && data[0] == 255 && data[1] == 216 && data[2] == 255 && data[3] != 0) ||
            (data.Length >= 13 && (data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8))) ||
            (data.Length >= 16 && data.StartsWith("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8)) ||
            (data.Length >= 26 && data.StartsWith("BM"u8)))
            kind = "image";
        else if (data.Length >= 16 && data.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            var brand = Encoding.ASCII.GetString(bytes, 8, 4);
            if (brand is "isom" or "iso2" or "mp41" or "mp42" or "avc1" or "M4V " or "qt  " or "dash") kind = "video";
        }
        else if (data.Length >= 16 && data.StartsWith(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 }) && data.IndexOf("webm"u8) >= 0)
            kind = "video";
        else if (data.Length >= 16 && data.StartsWith("RIFF"u8) && data.Slice(8, 4).SequenceEqual("AVI "u8))
            kind = "video";
        if (kind is null || (mime.StartsWith("image/", StringComparison.Ordinal) && kind != "image") ||
            (mime.StartsWith("video/", StringComparison.Ordinal) && kind != "video")) return null;
        return kind;
    }

    private const string MarkdownMediaDestination = @"(?:<(?<url>[^>\r\n]+)>|(?<url>(?:\\.|[^\s()\\]|\([^()\r\n]*\))+))";

    private static List<PluginMediaInfo> ExtractMedia(ReadmeDocument document)
    {
        var markdown = Regex.Replace(document.Markdown, @"<!--[\s\S]*?-->|(?m)^[ \t]*(`{3,}|~{3,})[^\r\n]*\r?\n[\s\S]*?^[ \t]*\1[^\r\n]*", "");
        markdown = Regex.Replace(markdown, @"`[^`\r\n]+`", "");
        var media = new List<PluginMediaInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(string raw, string alt, bool explicitMedia)
        {
            var url = NormalizeMediaUrl(raw, document);
            if (url.Length == 0 || (!explicitMedia && !IsMediaLink(url))) return;
            var identity = url + " " + alt;
            if (identity.Contains("ko-fi", StringComparison.OrdinalIgnoreCase) ||
                identity.Contains("buymeacoffee", StringComparison.OrdinalIgnoreCase) ||
                identity.Contains("githubbutton", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("shields.io", StringComparison.OrdinalIgnoreCase) || !seen.Add(url)) return;
            media.Add(new PluginMediaInfo { Url = url, Alt = WebUtility.HtmlDecode(alt) });
        }

        var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(markdown, @"(?m)^[ \t]{0,3}\[(?<id>[^\]]+)\]:[ \t]*" + MarkdownMediaDestination))
            references[NormalizeReferenceLabel(match.Groups["id"].Value)] = match.Groups["url"].Value;
        markdown = Regex.Replace(markdown, @"(?m)^[ \t]{0,3}\[[^\]]+\]:[^\r\n]*", "");
        foreach (Match match in Regex.Matches(markdown,
            @"(?<image>!)?\[(?<alt>[^\[\]\r\n]*)\]\(\s*" + MarkdownMediaDestination + @"(?:\s+[""'][^\r\n]*?[""'])?\s*\)"))
            Add(match.Groups["url"].Value, match.Groups["alt"].Value, match.Groups["image"].Success);
        foreach (Match match in Regex.Matches(markdown, @"(?<image>!)?\[(?<alt>[^\[\]\r\n]*)\](?:\[(?<id>[^\]\r\n]*)\])?(?!\()"))
        {
            var label = match.Groups["id"].Value;
            if (label.Length == 0) label = match.Groups["alt"].Value;
            if (references.TryGetValue(NormalizeReferenceLabel(label), out var url))
                Add(url, match.Groups["alt"].Value, match.Groups["image"].Success);
        }
        foreach (Match tag in Regex.Matches(markdown, @"<(?<tag>img|video|source|a)\b[^>]*>", RegexOptions.IgnoreCase))
        {
            var attrs = Regex.Matches(tag.Value, @"\b(?<name>src|href|alt)\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)'|(?<value>[^\s>]+))", RegexOptions.IgnoreCase)
                .Cast<Match>().GroupBy(match => match.Groups["name"].Value, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Groups["value"].Value, StringComparer.OrdinalIgnoreCase);
            var anchor = tag.Groups["tag"].Value.Equals("a", StringComparison.OrdinalIgnoreCase);
            if (attrs.TryGetValue(anchor ? "href" : "src", out var src))
                Add(src, attrs.TryGetValue("alt", out var alt) ? alt : "", !anchor);
        }
        // Do not rescan structured destinations: doing so truncates URLs containing spaces or parentheses.
        var bare = Regex.Replace(markdown, @"<[^>]+>|!?\[[^\]\r\n]*\]\([^\r\n]*?\)(?!\))", "");
        foreach (Match match in Regex.Matches(bare, @"https?://[^\s<>""']+", RegexOptions.IgnoreCase))
            Add(match.Value.TrimEnd('.', ',', ';', ':', '!', ')', ']'), "", false);
        foreach (Match match in Regex.Matches(markdown, @"<(?<url>https?://[^>\r\n]+)>", RegexOptions.IgnoreCase))
            Add(match.Groups["url"].Value, "", false);
        return media;
    }

    private static string NormalizeReferenceLabel(string label) => Regex.Replace(label.Trim(), @"\s+", " ");

    private static bool IsMediaLink(string url)
    {
        var uri = new Uri(url);
        return Regex.IsMatch(uri.AbsolutePath, @"\.(?:png|jpe?g|gif|webp|bmp|mp4|webm|mov|avi)$", RegexOptions.IgnoreCase) ||
            (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
                Regex.IsMatch(uri.AbsolutePath, @"^/(?:user-attachments/assets/[^/]+|[^/]+/[^/]+/assets/[^/]+/[^/]+)$", RegexOptions.IgnoreCase)) ||
            uri.Host.Equals("user-images.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Equals("private-user-images.githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRemoteUri(string value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) &&
            parsed.Scheme is "https" or "http" && !parsed.IsLoopback && parsed.UserInfo.Length == 0)
        {
            uri = parsed;
            return true;
        }
        uri = null!;
        return false;
    }

    private static string NormalizeMediaUrl(string value, ReadmeDocument document)
    {
        value = WebUtility.HtmlDecode(Regex.Replace(value.Trim().Trim('<', '>'), @"\\([\\ ()\[\]])", "$1"));
        if (value.Length == 0 || value[0] is '#' or '?' || value.Contains('\\')) return "";
        Uri? uri;
        if (value.StartsWith("//", StringComparison.Ordinal)) value = "https:" + value;
        // On Windows, Uri treats /assets/image.png as a local absolute file path.
        if (!value.StartsWith('/') && Uri.TryCreate(value, UriKind.Absolute, out uri))
        {
            if (!IsRemoteUri(value, out uri)) return "";
        }
        else
        {
            var rootRelative = value.StartsWith('/');
            if (!Uri.TryCreate(rootRelative ? document.Root : document.Url, rootRelative ? value.TrimStart('/') : value, out uri) ||
                !document.Root.IsBaseOf(uri)) return "";
        }
        if (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            var match = Regex.Match(uri.AbsolutePath, @"^/(?<repo>[^/]+/[^/]+)/(?:blob|raw)/(?<path>.+)$");
            if (match.Success)
                uri = new Uri($"https://raw.githubusercontent.com/{match.Groups["repo"].Value}/{match.Groups["path"].Value}{uri.Query}");
            else if (!IsMediaLink(uri.AbsoluteUri) && !Regex.IsMatch(uri.AbsolutePath, @"^/[^/]+/[^/]+/(?:files/|releases/download/)"))
                return "";
        }
        return new UriBuilder(uri) { Fragment = "" }.Uri.AbsoluteUri;
    }

    private static string RemoveMediaMarkdown(string markdown)
    {
        var withoutImages = Regex.Replace(markdown, @"!\[[^\]]*\]\(\s*" + MarkdownMediaDestination + @"(?:\s+[""'][^\r\n]*?[""'])?\s*\)", "");
        var referenceLabels = Regex.Matches(markdown, @"(?m)^[ \t]{0,3}\[(?<id>[^\]]+)\]:")
            .Cast<Match>().Select(match => NormalizeReferenceLabel(match.Groups["id"].Value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        withoutImages = Regex.Replace(withoutImages, @"!\[(?<alt>[^\]\r\n]*)\](?:\[(?<id>[^\]\r\n]*)\])?(?!\()", match =>
        {
            var label = match.Groups["id"].Value;
            if (label.Length == 0) label = match.Groups["alt"].Value;
            return referenceLabels.Contains(NormalizeReferenceLabel(label)) ? "" : match.Value;
        });
        withoutImages = Regex.Replace(
            withoutImages,
            @"<(?:img|source)\b[^>]*?/?>",
            "",
            RegexOptions.IgnoreCase);
        withoutImages = Regex.Replace(
            withoutImages,
            @"<video\b[^>]*>[\s\S]*?</video>",
            "",
            RegexOptions.IgnoreCase);
        // Remove media-only lines, not URL prefixes or meaningful prose links.
        return Regex.Replace(withoutImages, @"(?m)^[ \t]*<?(?<url>https?://[^\s<>]+)>?[ \t]*$", match =>
            IsRemoteUri(match.Groups["url"].Value, out var uri) && IsMediaLink(uri.AbsoluteUri) ? "" : match.Value,
            RegexOptions.IgnoreCase);
    }

    internal static string PrepareDescriptionForDisplay(string text) => CleanMarkdown(RemoveMediaMarkdown(text));

    private static string CleanMarkdown(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return "";
        }

        var text = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
        text = Regex.Replace(text, @"<!--[\s\S]*?-->", "", RegexOptions.Multiline);
        text = Regex.Replace(text, @"```[\s\S]*?```", "", RegexOptions.Multiline);
        text = Regex.Replace(
            text,
            @"<a\b[^>]*?\bhref\s*=\s*[""'](?<url>[^""']+)[""'][^>]*>(?<label>[\s\S]*?)</a>",
            "[${label}](${url})",
            RegexOptions.IgnoreCase);
        // Image-only HTML links become empty Markdown links after media removal.
        // They carry no useful text and otherwise leak fragments such as
        // "](https://...)" into the rendered description.
        text = Regex.Replace(text, @"\[\s*\]\([^)]+\)", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<(?:strong|b)\b[^>]*>(?<value>[\s\S]*?)</(?:strong|b)>", "**${value}**", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<(?:em|i)\b[^>]*>(?<value>[\s\S]*?)</(?:em|i)>", "*${value}*", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<code\b[^>]*>(?<value>[\s\S]*?)</code>", "`${value}`", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<h[1-6]\b[^>]*>(?<value>[\s\S]*?)</h[1-6]>", "\n## ${value}\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<li\b[^>]*>(?<value>[\s\S]*?)</li>", "\n- ${value}\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<blockquote\b[^>]*>(?<value>[\s\S]*?)</blockquote>",
            match => "\n" + string.Join("\n", match.Groups["value"].Value.Trim().Split('\n').Select(line => "> " + line.Trim())) + "\n",
            RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<(?<url>https?://[^>]+)>", "${url}", RegexOptions.IgnoreCase);
        text = Regex.Replace(
            text,
            @"<(?:br|/?p|/?div|/?section|/?details|/?summary|/?blockquote|/?ul|/?ol|/?table|/?tr)\b[^>]*>",
            "\n",
            RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<[^>\n]+>", "", RegexOptions.IgnoreCase);
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(
            text,
            @"^[ \t]{0,3}#{1,6}[ \t]*(?<heading>.+?)[ \t]*#*[ \t]*$",
            "## ${heading}",
            RegexOptions.Multiline);
        text = Regex.Replace(
            text,
            @"^(?<quote>[ \t]*>[ \t]*)?\[!(?<kind>[A-Za-z]+)\][ \t]*$",
            "${quote}${kind}:",
            RegexOptions.Multiline);
        text = Regex.Replace(text, @"^[ \t]*\[[^\]]+\]:[ \t]*\S+.*$", "", RegexOptions.Multiline);
        text = Regex.Replace(text, @"^[ \t]*[-*+][ \t]+", "- ", RegexOptions.Multiline);
        text = Regex.Replace(text, @"^[ \t]*[-*_]{3,}[ \t]*$", "", RegexOptions.Multiline);
        text = Regex.Replace(text, @"^[ \t]*\|?(?:[ \t]*:?-+:?[ \t]*\|)+[ \t]*$", "", RegexOptions.Multiline);
        text = Regex.Replace(text, @"[ \t]+\n", "\n");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    private static string MakeSummary(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var firstParagraph = text.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(p => p.Length > 45) ?? text;
        return firstParagraph.Length <= 260 ? firstParagraph : firstParagraph[..257].TrimEnd() + "...";
    }

    private static string? FindLocalFolder(string pluginRoot, string expectedName)
    {
        if (string.IsNullOrWhiteSpace(pluginRoot) || !Directory.Exists(pluginRoot))
        {
            return null;
        }

        return Directory.GetDirectories(pluginRoot)
            .FirstOrDefault(folder => Normalize(Path.GetFileName(folder)) == Normalize(expectedName));
    }

    private static string? FindInstallerZip(string folder)
    {
        return Directory.EnumerateFiles(folder, "*.zip", SearchOption.TopDirectoryOnly)
                   .OrderByDescending(File.GetLastWriteTimeUtc)
                   .FirstOrDefault(path => path.Contains("installer", StringComparison.OrdinalIgnoreCase))
               ?? Directory.EnumerateFiles(folder, "*.zip", SearchOption.TopDirectoryOnly)
                   .OrderByDescending(File.GetLastWriteTimeUtc)
                   .FirstOrDefault();
    }

    private static string FindSourceFolder(string folder)
    {
        var pluginJson = Directory.EnumerateFiles(folder, "plugin.json", SearchOption.AllDirectories).FirstOrDefault();
        return pluginJson is null ? folder : Path.GetDirectoryName(pluginJson)!;
    }

    private static string ReadInstalledVersion(string folder, string repositoryName)
    {
        var markerPath = Path.Combine(folder, InstalledReleaseMarker);
        if (File.Exists(markerPath))
        {
            try
            {
                using var marker = JsonDocument.Parse(File.ReadAllText(markerPath));
                if (marker.RootElement.TryGetProperty("version", out var markedVersion) &&
                    markedVersion.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(markedVersion.GetString()))
                {
                    return markedVersion.GetString()!;
                }
            }
            catch
            {
            }
        }

        foreach (var manifestPath in FindManifestPaths(folder, "plugin.json", "package.json", "package-lock.json"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
                foreach (var key in new[]
                {
                    "version", "version_number", "versionName", "version_name",
                    "releaseVersion", "release_version", "tag"
                })
                {
                    if (doc.RootElement.TryGetProperty(key, out var value) &&
                        value.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(value.GetString()))
                    {
                        return NormalizeManifestVersion(repositoryName, value.GetString()!);
                    }
                }

                if (doc.RootElement.TryGetProperty("publish", out var publish) &&
                    publish.ValueKind == JsonValueKind.Object)
                {
                    foreach (var key in new[] { "version", "version_number", "tag" })
                    {
                        if (publish.TryGetProperty(key, out var value) &&
                            value.ValueKind == JsonValueKind.String &&
                            !string.IsNullOrWhiteSpace(value.GetString()))
                        {
                            return NormalizeManifestVersion(repositoryName, value.GetString()!);
                        }
                    }
                }
            }
            catch
            {
            }
        }

        return "";
    }

    private static string ReadInstalledVersionOrDiagnostic(string folder, string repositoryName)
    {
        var version = ReadInstalledVersion(folder, repositoryName);
        return string.IsNullOrWhiteSpace(version) ? MissingInstalledVersion : version;
    }

    private static IReadOnlyList<string> FindManifestPaths(string folder, params string[] fileNames)
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fileName in fileNames)
        {
            var rootPath = Path.Combine(folder, fileName);
            if (File.Exists(rootPath) && seen.Add(CanonicalPath(rootPath)))
            {
                candidates.Add(rootPath);
            }
        }

        foreach (var fileName in fileNames)
        {
            try
            {
                foreach (var path in Directory.EnumerateFiles(folder, fileName, SearchOption.AllDirectories)
                             .Where(path => !IsIgnoredManifestPath(folder, path))
                             .OrderBy(path => Path.GetRelativePath(folder, path)
                                 .Count(character => character is '/' or '\\'))
                             .ThenBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    if (seen.Add(CanonicalPath(path)))
                    {
                        candidates.Add(path);
                    }
                }
            }
            catch
            {
            }
        }

        return candidates;
    }

    private static bool IsIgnoredManifestPath(string folder, string path)
    {
        var relative = Path.GetRelativePath(folder, path);
        var segments = relative.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment =>
            string.Equals(segment, "node_modules", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(segment, ".git", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(segment, "__pycache__", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeManifestVersion(string repositoryName, string version)
    {
        // These projects kept an internal package version that differs from
        // the public GitHub release version. Without Playhub's marker, reading
        // package.json therefore produced a permanent false update notice.
        if (string.Equals(repositoryName, "Now-Playing", StringComparison.OrdinalIgnoreCase) &&
            VersionsEquivalent(version, "0.3.0"))
        {
            return "1.3.0";
        }

        if (string.Equals(repositoryName, "Weather", StringComparison.OrdinalIgnoreCase) &&
            VersionsEquivalent(version, "1.0.0"))
        {
            return "1.1.0";
        }

        // Quick Settings 2.3.1 shipped with package.json still at 2.3.0.
        // Keep this exact mapping so any later manifest or release version wins normally.
        if (string.Equals(repositoryName, "Quick-Settings", StringComparison.OrdinalIgnoreCase) &&
            VersionsEquivalent(version, "2.3.0"))
        {
            return "2.3.1";
        }

        // TabMaster 2.15.1 shipped with package.json still at 2.15.0.
        // Keep this exact mapping so later manifest and release versions compare normally.
        if (string.Equals(repositoryName, "TabMaster", StringComparison.OrdinalIgnoreCase) &&
            VersionsEquivalent(version, "2.15.0"))
        {
            return "2.15.1";
        }

        return version;
    }

    private static bool HasVersionUpdate(string installedVersion, string? latestVersion)
    {
        if (!TryParseSemanticVersion(installedVersion, out var installed) ||
            !TryParseSemanticVersion(latestVersion, out var latest))
        {
            return false;
        }

        return CompareSemanticVersions(latest, installed) > 0;
    }

    private static bool VersionsEquivalent(string? left, string? right) =>
        TryParseSemanticVersion(left, out var leftVersion) &&
        TryParseSemanticVersion(right, out var rightVersion) &&
        CompareSemanticVersions(leftVersion, rightVersion) == 0;

    private static int CompareSemanticVersions(string? left, string? right)
    {
        if (!TryParseSemanticVersion(left, out var leftVersion))
        {
            return TryParseSemanticVersion(right, out _) ? -1 : 0;
        }
        if (!TryParseSemanticVersion(right, out var rightVersion))
        {
            return 1;
        }
        return CompareSemanticVersions(leftVersion, rightVersion);
    }

    private static int CompareSemanticVersions(SemanticVersion left, SemanticVersion right)
    {
        var length = Math.Max(left.Core.Count, right.Core.Count);
        for (var i = 0; i < length; i++)
        {
            var x = i < left.Core.Count ? left.Core[i] : 0;
            var y = i < right.Core.Count ? right.Core[i] : 0;
            if (x != y)
            {
                return x.CompareTo(y);
            }
        }

        if (left.Prerelease.Count == 0 || right.Prerelease.Count == 0)
        {
            return left.Prerelease.Count == right.Prerelease.Count
                ? 0
                : left.Prerelease.Count == 0 ? 1 : -1;
        }

        var prereleaseLength = Math.Max(left.Prerelease.Count, right.Prerelease.Count);
        for (var i = 0; i < prereleaseLength; i++)
        {
            if (i >= left.Prerelease.Count)
            {
                return -1;
            }
            if (i >= right.Prerelease.Count)
            {
                return 1;
            }

            var leftPart = left.Prerelease[i];
            var rightPart = right.Prerelease[i];
            var leftNumeric = long.TryParse(leftPart, out var leftNumber);
            var rightNumeric = long.TryParse(rightPart, out var rightNumber);
            if (leftNumeric && rightNumeric && leftNumber != rightNumber)
            {
                return leftNumber.CompareTo(rightNumber);
            }
            if (leftNumeric != rightNumeric)
            {
                return leftNumeric ? -1 : 1;
            }

            var comparison = string.Compare(leftPart, rightPart, StringComparison.OrdinalIgnoreCase);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    private static bool TryParseSemanticVersion(string? value, out SemanticVersion version)
    {
        version = SemanticVersion.Empty;
        var match = Regex.Match(
            value ?? "",
            @"(?<![0-9A-Za-z])[vV]?(?<core>\d+(?:\.\d+)*)(?:-(?<pre>[0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?",
            RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return false;
        }

        var core = new List<long>();
        foreach (var part in match.Groups["core"].Value.Split('.'))
        {
            if (!long.TryParse(part, out var number))
            {
                return false;
            }
            core.Add(number);
        }
        while (core.Count > 1 && core[^1] == 0)
        {
            core.RemoveAt(core.Count - 1);
        }

        var prerelease = match.Groups["pre"].Success
            ? match.Groups["pre"].Value.Split('.', StringSplitOptions.RemoveEmptyEntries).ToList()
            : new List<string>();
        var canonical = string.Join('.', core) +
                        (prerelease.Count == 0 ? "" : "-" + string.Join('.', prerelease).ToLowerInvariant());
        version = new SemanticVersion(core, prerelease, canonical);
        return true;
    }

    private static void AppendUncataloguedInstalledPlugins(
        List<DeckyPluginInfo> plugins,
        string deckyPluginsPath,
        HashSet<string> claimedInstalledFolders)
    {
        if (string.IsNullOrWhiteSpace(deckyPluginsPath) || !Directory.Exists(deckyPluginsPath))
        {
            return;
        }

        var catalogPlugins = plugins.ToArray();
        foreach (var folder in Directory.GetDirectories(deckyPluginsPath)
                     .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
        {
            var canonicalFolder = CanonicalPath(folder);
            if (claimedInstalledFolders.Contains(canonicalFolder))
            {
                continue;
            }

            var metadata = ReadInstalledPluginMetadata(folder);
            if (IsBlockedPluginIdentity(
                    Path.GetFileName(folder),
                    metadata.Name,
                    metadata.RepositoryName,
                    metadata.RepositorySlug,
                    metadata.RepositoryUrl))
            {
                ClaimInstalledFolder(claimedInstalledFolders, folder);
                continue;
            }

            var identities = metadata.Aliases
                .Append(Path.GetFileName(folder))
                .Append(metadata.Name)
                .Append(metadata.RepositorySlug)
                .Append(metadata.RepositoryName)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var normalizedIdentities = identities
                .Select(Normalize)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            DeckyPluginInfo[] candidates;
            // Repository metadata (with the installed marker taking precedence) is
            // authoritative. Never fall back to shared names after a repository miss.
            if (!string.IsNullOrWhiteSpace(metadata.RepositorySlug))
            {
                candidates = catalogPlugins.Where(plugin => string.Equals(
                    plugin.RepositorySlug, metadata.RepositorySlug, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
            }
            else
            {
                // Resolve the strongest match across the whole catalog before
                // considering weaker aliases; enumeration order is not identity.
                candidates = catalogPlugins.Where(plugin =>
                    !string.IsNullOrWhiteSpace(plugin.InstalledFolder) &&
                    string.Equals(CanonicalPath(plugin.InstalledFolder), canonicalFolder,
                        StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
                if (candidates.Length == 0)
                {
                    candidates = catalogPlugins.Where(plugin => plugin.InstallAliases
                        .Append(plugin.Name)
                        .Append(plugin.FolderName)
                        .Append(plugin.RepositoryName)
                        .Append(plugin.RepositorySlug)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(Normalize)
                        .Any(normalizedIdentities.Contains)).Take(2).ToArray();
                }
            }
            var matchingPlugin = candidates.Length == 1 ? candidates[0] : null;

            if (matchingPlugin is not null)
            {
                matchingPlugin.IsInstalled = true;
                matchingPlugin.InstalledFolder = folder;
                matchingPlugin.InstalledVersion = metadata.Version;
                matchingPlugin.HasUpdate = HasVersionUpdate(metadata.Version, matchingPlugin.Version);
                matchingPlugin.InstallAliases = matchingPlugin.InstallAliases
                    .Concat(identities)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                ClaimInstalledFolder(claimedInstalledFolders, folder);
                continue;
            }

            var canResolveRelease = IsValidRepositorySlug(metadata.RepositorySlug);
            plugins.Add(new DeckyPluginInfo
            {
                Name = metadata.Name,
                FolderName = Path.GetFileName(folder),
                Author = metadata.Author,
                Version = metadata.Version,
                InstalledVersion = metadata.Version,
                HasUpdate = false,
                ShortDescription = metadata.Description,
                LongDescription = metadata.Description,
                Readme = metadata.Description,
                IconGlyph = ((char)0xE8B7).ToString(),
                SourceFolder = "",
                Image = metadata.Image,
                CoverImage = metadata.Image,
                RepositoryUrl = metadata.RepositoryUrl,
                RepositoryName = metadata.RepositoryName,
                RepositorySlug = metadata.RepositorySlug,
                ReleaseAssetName = metadata.ReleaseAssetName,
                CatalogReleaseZipUrl = metadata.ReleaseZipUrl,
                InstallAliases = identities,
                Category = InferInstalledCategory(metadata.Name, metadata.Description, metadata.Tags),
                Keywords = string.Join(' ', metadata.Tags),
                IsPlayhubPlugin = false,
                CatalogStatus = "github",
                CatalogSource = "installed",
                Compatibility = "Plugin installato localmente e non ancora identificato dal catalogo Playhub.",
                ReleasePageUrl = canResolveRelease
                    ? $"https://github.com/{metadata.RepositorySlug}/releases"
                    : metadata.RepositoryUrl,
                IsInstalled = true,
                InstalledFolder = folder
            });
            ClaimInstalledFolder(claimedInstalledFolders, folder);
        }
    }

    private static InstalledPluginMetadata ReadInstalledPluginMetadata(string folder)
    {
        var folderName = Path.GetFileName(folder);
        var name = folderName;
        var author = "";
        var version = "";
        var description = "";
        var image = "";
        var repositoryValue = "";
        var releaseAssetName = "";
        var releaseZipUrl = "";
        var aliases = new List<string>();
        var tags = new List<string>();
        var hasDeclaredPluginName = false;

        var manifestPath = FindManifestPaths(folder, "plugin.json").FirstOrDefault() ?? "";

        if (!string.IsNullOrWhiteSpace(manifestPath))
        {
            try
            {
                using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
                var root = manifest.RootElement;
                var declaredPluginName = FirstNonEmpty(
                    ReadJsonString(root, "name"),
                    ReadJsonString(root, "display_name"),
                    ReadJsonString(root, "title"));
                hasDeclaredPluginName = !string.IsNullOrWhiteSpace(declaredPluginName);
                name = FirstNonEmpty(declaredPluginName, folderName);
                author = ReadJsonString(root, "author");
                version = FirstNonEmpty(
                    ReadJsonString(root, "version"),
                    ReadJsonString(root, "version_number"),
                    ReadJsonString(root, "tag"));
                repositoryValue = FirstNonEmpty(
                    ReadJsonString(root, "repository"),
                    ReadJsonString(root, "repository_url"),
                    ReadJsonString(root, "homepage"));
                image = FirstNonEmpty(
                    ReadJsonString(root, "image"),
                    ReadJsonString(root, "icon"),
                    ReadJsonString(root, "cover"));
                description = ReadJsonString(root, "description");
                AddJsonStrings(root, "tags", tags);

                if (root.TryGetProperty("publish", out var publish) && publish.ValueKind == JsonValueKind.Object)
                {
                    description = FirstNonEmpty(description, ReadJsonString(publish, "description"));
                    image = FirstNonEmpty(image, ReadJsonString(publish, "image"));
                    repositoryValue = FirstNonEmpty(
                        repositoryValue,
                        ReadJsonString(publish, "repository"),
                        ReadJsonString(publish, "repository_url"));
                    AddJsonStrings(publish, "tags", tags);
                }

                aliases.AddRange(new[]
                {
                    ReadJsonString(root, "name"),
                    ReadJsonString(root, "display_name"),
                    ReadJsonString(root, "title")
                }.Where(value => !string.IsNullOrWhiteSpace(value)));
                image = ResolveInstalledImage(folder, manifestPath, image);
            }
            catch
            {
            }
        }

        var packagePath = FindManifestPaths(folder, "package.json").FirstOrDefault() ?? "";
        if (!string.IsNullOrWhiteSpace(packagePath))
        {
            try
            {
                using var package = JsonDocument.Parse(File.ReadAllText(packagePath));
                var root = package.RootElement;
                var packageName = ReadJsonString(root, "name");
                if (!hasDeclaredPluginName)
                {
                    name = FirstNonEmpty(packageName, name);
                }

                author = FirstNonEmpty(author, ReadJsonString(root, "author"));
                description = FirstNonEmpty(description, ReadJsonString(root, "description"));
                var packageRepository = new[]
                    {
                        ReadJsonString(root, "repository"),
                        ReadJsonString(root, "repository_url"),
                        ReadJsonString(root, "homepage")
                    }
                    .FirstOrDefault(value =>
                        !string.IsNullOrWhiteSpace(value) && !IsTemplateRepository(value)) ?? "";
                if (!string.IsNullOrWhiteSpace(packageRepository))
                {
                    repositoryValue = FirstNonEmpty(repositoryValue, packageRepository);
                }

                AddJsonStrings(root, "keywords", tags);
                aliases.AddRange(new[] { packageName, ReadJsonString(root, "displayName") }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
            }
            catch
            {
            }
        }

        var markerPath = Path.Combine(folder, InstalledReleaseMarker);
        if (File.Exists(markerPath))
        {
            try
            {
                using var marker = JsonDocument.Parse(File.ReadAllText(markerPath));
                var root = marker.RootElement;
                name = FirstNonEmpty(ReadJsonString(root, "name"), name);
                author = FirstNonEmpty(ReadJsonString(root, "author"), author);
                description = FirstNonEmpty(ReadJsonString(root, "description"), description);
                repositoryValue = FirstNonEmpty(
                    ReadJsonString(root, "repositorySlug"),
                    ReadJsonString(root, "repositoryUrl"),
                    ReadJsonString(root, "repository"),
                    repositoryValue);
                version = FirstNonEmpty(ReadJsonString(root, "version"), version);
                releaseAssetName = FirstNonEmpty(
                    ReadJsonString(root, "releaseAssetName"),
                    ReadJsonString(root, "asset"));
                releaseZipUrl = FirstNonEmpty(
                    ReadJsonString(root, "releaseZipUrl"),
                    ReadJsonString(root, "zipUrl"),
                    ReadJsonString(root, "releaseUrl"));
                var markerImage = FirstNonEmpty(ReadJsonString(root, "image"), ReadJsonString(root, "cover"));
                if (!string.IsNullOrWhiteSpace(markerImage))
                {
                    image = ResolveInstalledImage(folder, markerPath, markerImage);
                }
                aliases.AddRange(new[]
                {
                    ReadJsonString(root, "name"),
                    ReadJsonString(root, "repository"),
                    ReadJsonString(root, "repositorySlug")
                }.Where(value => !string.IsNullOrWhiteSpace(value)));
            }
            catch
            {
            }
        }

        var repositorySlug = ExtractGithubRepositorySlug(repositoryValue);
        var repositoryName = !string.IsNullOrWhiteSpace(repositorySlug)
            ? repositorySlug[(repositorySlug.IndexOf('/') + 1)..]
            : RepositoryNameFromValue(repositoryValue, folderName);
        var repositoryUrl = ResolveRepositoryUrl(repositoryValue, repositorySlug);
        version = ReadInstalledVersionOrDiagnostic(folder, repositoryName);
        aliases.AddRange(new[] { folderName, name, repositoryValue, repositorySlug, repositoryName });

        return new InstalledPluginMetadata(
            name,
            author,
            version,
            description,
            string.IsNullOrWhiteSpace(image) ? null : image,
            repositoryUrl,
            repositoryName,
            repositorySlug,
            releaseAssetName,
            Uri.TryCreate(releaseZipUrl, UriKind.Absolute, out _) ? releaseZipUrl : null,
            aliases.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            tags.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static string ReadJsonString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return "";
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString()?.Trim() ?? "";
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var nestedName in new[] { "name", "url", "html_url" })
            {
                if (value.TryGetProperty(nestedName, out var nested) && nested.ValueKind == JsonValueKind.String)
                {
                    var text = nested.GetString()?.Trim() ?? "";
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }
        }

        return "";
    }

    private static void AddJsonStrings(JsonElement root, string propertyName, List<string> target)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            target.AddRange((value.GetString() ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            return;
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            target.AddRange(value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? "")
                .Where(item => !string.IsNullOrWhiteSpace(item)));
        }
    }

    private static string ResolveInstalledImage(string folder, string manifestPath, string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            return value;
        }

        try
        {
            var manifestFolder = Path.GetDirectoryName(manifestPath) ?? folder;
            var candidate = Path.GetFullPath(Path.Combine(manifestFolder, value));
            var relative = Path.GetRelativePath(folder, candidate);
            return !relative.StartsWith("..", StringComparison.Ordinal) && File.Exists(candidate) ? candidate : "";
        }
        catch
        {
            return "";
        }
    }

    private static string ExtractGithubRepositorySlug(string value)
    {
        if (IsValidRepositorySlug(value))
        {
            return value.Trim();
        }

        var match = Regex.Match(value ?? "", @"github\.com[/:](?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+)", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            match = Regex.Match(value ?? "", @"raw\.githubusercontent\.com/(?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+)/", RegexOptions.IgnoreCase);
        }
        if (!match.Success)
        {
            return "";
        }

        var repository = Regex.Replace(match.Groups["repo"].Value, @"\.git$", "", RegexOptions.IgnoreCase);
        return $"{match.Groups["owner"].Value}/{repository}";
    }

    private static bool IsTemplateRepository(string value)
    {
        var repository = ExtractGithubRepositorySlug(value);
        return string.Equals(
            repository,
            "SteamDeckHomebrew/decky-plugin-template",
            StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveRepositoryUrl(string declaredValue, string repositorySlug)
    {
        if (!string.IsNullOrWhiteSpace(repositorySlug))
        {
            return $"https://github.com/{repositorySlug}";
        }

        return Uri.TryCreate(declaredValue, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? declaredValue
            : "";
    }

    private static string RepositoryNameFromValue(string value, string fallback)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            var segment = uri.Segments.LastOrDefault()?.Trim('/', '\\');
            if (!string.IsNullOrWhiteSpace(segment))
            {
                return segment.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? segment[..^4] : segment;
            }
        }

        var declaredName = (value ?? "").Trim().Trim('/', '\\');
        if (!string.IsNullOrWhiteSpace(declaredName) &&
            !declaredName.Contains('/') &&
            !declaredName.Contains('\\'))
        {
            return declaredName.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
                ? declaredName[..^4]
                : declaredName;
        }
        return fallback;
    }

    private static string InferInstalledCategory(string name, string description, IEnumerable<string> tags)
    {
        var text = string.Join(' ', tags.Append(name).Append(description)).ToLowerInvariant();
        if (Regex.IsMatch(text, @"\bdiscord\b|\bteamspeak\b|\bvoice chat\b|\brich presence\b|\bplayer counts?\b"))
        {
            return "Social e community";
        }
        if (Regex.IsMatch(text, @"\bclipboard\b|\bpasswords?\b|\bfile (?:manager|server|transfer)\b|\bterminal\b|\bnotebook\b|\btimers?\b|\balarms?\b|\btranslate\b|\btranslator\b|\bweb browser\b|\bvoice-to-text\b|\bsyncthing\b|\bkde connect\b|\blocalsend\b|\bftp\b|\bsmb\b"))
        {
            return "Strumenti e utilità";
        }
        if (Regex.IsMatch(text, @"game|library|achievement|metadata|rom|emulat|backlog|launcher"))
        {
            return "Libreria e giochi";
        }
        if (Regex.IsMatch(text, @"audio|music|media|theme|style|visual|display|notification"))
        {
            return "Personalizzazione e media";
        }
        if (Regex.IsMatch(text, @"network|vpn|wifi|download|file|cloud|ssh|ftp|remote"))
        {
            return "Rete e strumenti";
        }
        return "Sistema e hardware";
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

    private static string CanonicalPath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path;
        }
    }

    private static void ClaimInstalledFolder(ISet<string> claimedInstalledFolders, string? folder)
    {
        if (!string.IsNullOrWhiteSpace(folder))
        {
            claimedInstalledFolders.Add(CanonicalPath(folder));
        }
    }

    private static string? FindInstalledFolder(string deckyPluginsPath, params string[] identities)
        => FindInstalledFolder(deckyPluginsPath, null, identities);

    private static string? FindInstalledFolder(
        string deckyPluginsPath,
        ISet<string>? excludedFolders,
        params string[] identities)
    {
        if (string.IsNullOrWhiteSpace(deckyPluginsPath) || !Directory.Exists(deckyPluginsPath))
        {
            return null;
        }

        var candidates = identities
            .Where(identity => !string.IsNullOrWhiteSpace(identity))
            .Select(identity => Normalize(identity))
            .Where(identity => !string.IsNullOrWhiteSpace(identity))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var repositoryCandidates = identities
            .Select(ExtractGithubRepositorySlug)
            .Where(identity => !string.IsNullOrWhiteSpace(identity))
            .Select(NormalizeRepositorySlug)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in Directory.GetDirectories(deckyPluginsPath))
        {
            if (excludedFolders?.Contains(CanonicalPath(folder)) == true)
            {
                continue;
            }

            var markerPath = Path.Combine(folder, InstalledReleaseMarker);
            var markerHasRepository = false;
            if (File.Exists(markerPath))
            {
                try
                {
                    using var marker = JsonDocument.Parse(File.ReadAllText(markerPath));
                    var markerRepository = FirstNonEmpty(
                        ReadJsonString(marker.RootElement, "repository"),
                        ReadJsonString(marker.RootElement, "repositorySlug"),
                        ReadJsonString(marker.RootElement, "repositoryUrl"));
                    var markerRepositorySlug = NormalizeRepositorySlug(
                        ExtractGithubRepositorySlug(markerRepository));
                    markerHasRepository = !string.IsNullOrWhiteSpace(markerRepositorySlug);
                    if (markerHasRepository && repositoryCandidates.Contains(markerRepositorySlug))
                    {
                        return folder;
                    }

                    if (markerHasRepository && repositoryCandidates.Count > 0)
                    {
                        continue;
                    }

                    var markerIdentities = new[]
                    {
                        ReadJsonString(marker.RootElement, "name")
                    };
                    if (markerIdentities.Any(identity => candidates.Contains(Normalize(identity))))
                    {
                        return folder;
                    }
                }
                catch
                {
                }
            }

            if (candidates.Contains(Normalize(Path.GetFileName(folder))))
            {
                return folder;
            }

            foreach (var manifestPath in new[]
            {
                Path.Combine(folder, "plugin.json"),
                Path.Combine(folder, "package.json")
            })
            {
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
                    var names = new[]
                    {
                        doc.RootElement.TryGetProperty("name", out var nameProperty) ? nameProperty.GetString() ?? "" : "",
                        doc.RootElement.TryGetProperty("display_name", out var displayProperty) ? displayProperty.GetString() ?? "" : "",
                        doc.RootElement.TryGetProperty("title", out var titleProperty) ? titleProperty.GetString() ?? "" : ""
                    };
                    if (names.Any(name => candidates.Contains(Normalize(name))))
                    {
                        return folder;
                    }
                }
                catch
                {
                }
            }
        }

        return null;
    }

    public static string MakeInstallFolderName(string name)
    {
        var safe = Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(safe) ? "plugin" : safe;
    }

    private static string Normalize(string value) => Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", "");

    private static string FormatDate(string? value) =>
        DateTimeOffset.TryParse(value, out var date) ? date.ToLocalTime().ToString("dd/MM/yyyy") : "";

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Playhub/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    // ---------------------------------------------------------------------
    // Descrizioni localizzate dei plugin.
    //
    //  Chiave esterna = RepositoryName del plugin (come in Definitions sopra:
    //                   "Launch-Curtain", "Now-Playing", "Playhub-Metadata", ...).
    //  Chiave interna = codice lingua: en, es, fr, de, pt, uk, zh, ja, ko, hi, ru.
    //  Valore         = PluginText(Short, Long).
    //
    //  REGOLE IMPORTANTI:
    //   • L'italiano NON va qui: resta quello in Definitions (default + fallback).
    //   • Traduci la descrizione COME BLOCCO UNICO, mantenendo la stessa struttura
    //     del testo italiano: righe vuote tra i paragrafi, intestazioni "## ",
    //     elenchi puntati "• ". NON spezzare riga per riga.
    //   • Se per un plugin manca una lingua, viene mostrato l'italiano INTERO
    //     (mai un misto). Quindi: o traduci tutta la descrizione, o lasciala fuori.
    //   • Short = la frase breve sotto al nome del plugin.
    //     Long  = la descrizione estesa che compare aprendo "Dettagli".
    // ---------------------------------------------------------------------
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, PluginText>> DescriptionTranslations =
        new Dictionary<string, IReadOnlyDictionary<string, PluginText>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Playhub-Artworks"] = new Dictionary<string, PluginText>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new PluginText(
                    "The right artwork for every game in your library.",
                    @"Playhub Artworks brings artwork management inside Big Picture, designed for a controller: search, choose and apply covers, banners, heroes, logos and icons without ever going back to the desktop.

## What it does
• Searches artwork on SteamGridDB, IGDB, the PlayStation Store, the Nintendo eShop, Xbox, AlphaCoders, iiDB and IGN.
• Remembers filters and the last source used separately for each artwork type.
• Builds Perfect Hero and Perfect Banner images by merging background and logo, with adjustable position, scale, opacity and shadow.
• Lets you leave the logo out of the composition when you only want the background.
• Positions and resizes a game's logo the way Steam does.
• Shows square covers in the library and in every row of the Home, Steam's own banner included.
• Fills in the missing artwork of the whole library in the background.

## Note
• Searches that use SteamGridDB need your own API key; it is stored only on your PC."),
                ["es"] = new PluginText(
                    "El arte adecuado para cada juego de tu biblioteca.",
                    @"Playhub Artworks gestiona el arte dentro de Big Picture y con el mando: busca portadas, banners, heroes, logos e iconos en SteamGridDB, IGDB, PlayStation, Nintendo, Xbox, AlphaCoders, iiDB e IGN, crea Perfect Hero y Perfect Banner combinando fondo y logo, coloca el logo del juego y muestra portadas cuadradas en la biblioteca y en la Home. Para las búsquedas de SteamGridDB necesitas tu propia clave API, guardada solo en tu PC."),
                ["fr"] = new PluginText(
                    "Les bonnes jaquettes pour chaque jeu de ta bibliothèque.",
                    @"Playhub Artworks gère les visuels directement dans Big Picture, à la manette : recherche de jaquettes, bannières, héros, logos et icônes sur SteamGridDB, IGDB, PlayStation, Nintendo, Xbox, AlphaCoders, iiDB et IGN, création de Perfect Hero et Perfect Banner en fusionnant fond et logo, positionnement du logo du jeu et jaquettes carrées dans la bibliothèque comme sur l'accueil. Les recherches SteamGridDB demandent ta propre clé API, conservée uniquement sur ton PC."),
                ["de"] = new PluginText(
                    "Das passende Artwork für jedes Spiel deiner Bibliothek.",
                    @"Playhub Artworks verwaltet Artwork direkt in Big Picture und mit dem Controller: Cover, Banner, Heroes, Logos und Icons von SteamGridDB, IGDB, PlayStation, Nintendo, Xbox, AlphaCoders, iiDB und IGN, Perfect Hero und Perfect Banner aus Hintergrund und Logo, Logo-Positionierung wie in Steam sowie quadratische Cover in Bibliothek und Startseite. SteamGridDB-Suchen benötigen deinen eigenen API-Schlüssel, der nur auf deinem PC gespeichert wird."),
                ["pt"] = new PluginText(
                    "As artes certas para cada jogo da sua biblioteca.",
                    @"Playhub Artworks cuida das artes dentro do Big Picture, feito para o controle: busca capas, banners, heroes, logos e ícones no SteamGridDB, IGDB, PlayStation, Nintendo, Xbox, AlphaCoders, iiDB e IGN, cria Perfect Hero e Perfect Banner unindo fundo e logo, posiciona o logo do jogo e mostra capas quadradas na biblioteca e na Home. As buscas do SteamGridDB pedem sua própria chave de API, guardada apenas no seu PC."),
                ["uk"] = new PluginText(
                    "Правильні обкладинки для кожної гри у твоїй бібліотеці.",
                    @"Playhub Artworks керує обкладинками просто в Big Picture і з геймпада: пошук обкладинок, банерів, hero-зображень, логотипів та іконок у SteamGridDB, IGDB, PlayStation, Nintendo, Xbox, AlphaCoders, iiDB і IGN, створення Perfect Hero та Perfect Banner з фону й логотипа, розміщення логотипа гри та квадратні обкладинки в бібліотеці й на головній. Для пошуку через SteamGridDB потрібен твій власний API-ключ, який зберігається лише на твоєму ПК."),
                ["zh"] = new PluginText(
                    "为库中的每款游戏配上合适的封面。",
                    @"Playhub Artworks 让你在大屏幕模式下用手柄管理美术资源：在 SteamGridDB、IGDB、PlayStation、任天堂、Xbox、AlphaCoders、iiDB 和 IGN 中搜索封面、横幅、Hero 图、Logo 和图标，将背景与 Logo 合成 Perfect Hero 和 Perfect Banner，按 Steam 的方式摆放游戏 Logo，并在库和主页显示方形封面。使用 SteamGridDB 搜索需要你自己的 API 密钥，密钥只保存在你的电脑上。"),
                ["ja"] = new PluginText(
                    "ライブラリのすべてのゲームに、ふさわしいアートワークを。",
                    @"Playhub Artworks はビッグピクチャーの中でアートワークをコントローラーだけで管理できます。SteamGridDB、IGDB、PlayStation、Nintendo、Xbox、AlphaCoders、iiDB、IGN からカバー・バナー・ヒーロー・ロゴ・アイコンを検索し、背景とロゴを合成して Perfect Hero と Perfect Banner を作成、Steam と同じようにロゴを配置し、ライブラリとホームで正方形カバーを表示します。SteamGridDB の検索には自分の API キーが必要で、キーは PC の中だけに保存されます。"),
                ["ko"] = new PluginText(
                    "라이브러리의 모든 게임에 어울리는 아트워크.",
                    @"Playhub Artworks는 빅 픽처 안에서 컨트롤러만으로 아트워크를 관리합니다. SteamGridDB, IGDB, PlayStation, Nintendo, Xbox, AlphaCoders, iiDB, IGN에서 커버·배너·히어로·로고·아이콘을 검색하고, 배경과 로고를 합쳐 Perfect Hero와 Perfect Banner를 만들며, Steam과 같은 방식으로 로고를 배치하고 라이브러리와 홈에 정사각형 커버를 표시합니다. SteamGridDB 검색에는 개인 API 키가 필요하며 키는 PC에만 저장됩니다."),
                ["hi"] = new PluginText(
                    "आपकी लाइब्रेरी के हर गेम के लिए सही आर्टवर्क।",
                    @"Playhub Artworks बिग पिक्चर के भीतर ही आर्टवर्क संभालता है, कंट्रोलर के लिए बनाया गया: SteamGridDB, IGDB, PlayStation, Nintendo, Xbox, AlphaCoders, iiDB, IGN पर कवर, बैनर, हीरो, लोगो और आइकन खोजें, बैकग्राउंड और लोगो को मिलाकर Perfect Hero तथा Perfect Banner बनाएं, गेम का लोगो Steam की तरह सेट करें और लाइब्रेरी व होम में चौकोर कवर देखें। SteamGridDB खोज के लिए आपकी अपनी API की ज़रूरत होती है, जो केवल आपके पीसी पर सहेजी जाती है."),
                ["ru"] = new PluginText(
                    "Подходящие обложки для каждой игры в библиотеке.",
                    @"Playhub Artworks управляет обложками прямо в Big Picture и с геймпада: поиск обложек, баннеров, hero-изображений, логотипов и иконок в SteamGridDB, IGDB, PlayStation, Nintendo, Xbox, AlphaCoders, iiDB и IGN, создание Perfect Hero и Perfect Banner из фона и логотипа, размещение логотипа игры как в Steam и квадратные обложки в библиотеке и на главной. Для поиска через SteamGridDB нужен ваш собственный API-ключ, он хранится только на вашем ПК.")
            },
            ["Launch-Curtain"] = new Dictionary<string, PluginText>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new PluginText(
                    "A clean doorway into your game.",
                    @"Launch Curtain turns PC game startup into something that feels closer to a console. When you start a game from Steam Big Picture, it drops in a full-screen loading screen that hides desktop flashes, launchers and stray windows, keeping the game logo front and centre with a soft fade. It is a Windows-only Playhub plugin.

## What it does
• Shows a custom loading screen as soon as a game starts.
• Lets you shape Launch Curtain your way: logo position and size, background, opacity and optional logo zoom animation.
• Hides itself when the game window reaches fullscreen, or after a safety timeout.
• Can be closed with ESC or the Back/Close button on common controllers.

## Customisation
• Use your own logo (PNG, JPG, WebP or BMP) instead of the Playhub logo when the game logo is missing.
• Adjust how long Launch Curtain stays visible after the game is ready."),
                ["es"] = new PluginText(
                    "Una entrada limpia a tu juego.",
                    @"Launch Curtain convierte el arranque de los juegos de PC en algo mucho más parecido a una consola. Cuando inicias un juego desde Steam Big Picture, aparece una pantalla de carga a pantalla completa que oculta parpadeos del escritorio, launchers y ventanas fuera de lugar, dejando el logo del juego en primer plano con una transición suave. Es un plugin exclusivo para Windows.

## Qué hace
• Muestra una pantalla personalizable en cuanto se inicia un juego.
• Te deja adaptar Launch Curtain a tu gusto: posición y tamaño del logo, fondo, opacidad y zoom animado opcional.
• Se oculta solo cuando la ventana del juego llega a pantalla completa, o tras un tiempo de seguridad.
• Puede cerrarse con ESC o con el botón Atrás/Cerrar de los mandos más comunes.

## Personalización
• Puedes usar tu propio logo (PNG, JPG, WebP o BMP) en lugar del de Playhub cuando falta el logo del juego.
• Puedes ajustar cuánto tiempo permanece visible Launch Curtain cuando el juego ya está listo."),
                ["fr"] = new PluginText(
                    "Une entrée nette dans ton jeu.",
                    @"Launch Curtain transforme le lancement des jeux PC en une expérience plus proche d'une console. Quand tu lances un jeu depuis Steam Big Picture, un écran de chargement plein écran apparaît pour masquer les flashs du bureau, les launchers et les fenêtres mal placées, en gardant le logo du jeu au premier plan avec un fondu doux. C'est un plugin Playhub exclusif à Windows.

## Ce qu'il fait
• Affiche un écran personnalisable dès le lancement d'un jeu.
• Te laisse régler Launch Curtain comme tu veux : position et taille du logo, fond, opacité et animation de zoom optionnelle.
• Se masque tout seul quand la fenêtre du jeu passe en plein écran, ou après un délai de sécurité.
• Peut être fermé avec ESC ou avec le bouton Retour/Fermer des manettes les plus courantes.

## Personnalisation
• Tu peux utiliser ton propre logo (PNG, JPG, WebP ou BMP) à la place du logo Playhub quand le logo du jeu manque.
• Tu peux régler combien de temps Launch Curtain reste visible une fois le jeu prêt."),
                ["de"] = new PluginText(
                    "Ein sauberer Einstieg ins Spiel.",
                    @"Launch Curtain macht den Start von PC-Spielen deutlich konsolenähnlicher. Wenn du ein Spiel aus Steam Big Picture startest, erscheint ein Ladebildschirm im Vollbild, der Desktop-Flackern, Launcher und falsch platzierte Fenster verdeckt und das Spiellogo mit einer weichen Überblendung in den Mittelpunkt stellt. Es ist ein Playhub-Plugin exklusiv für Windows.

## Was es macht
• Zeigt sofort beim Spielstart einen anpassbaren Ladebildschirm.
• Lässt dich Launch Curtain frei gestalten: Position und Größe des Logos, Hintergrund, Deckkraft und optionaler Logo-Zoom.
• Blendet sich automatisch aus, wenn das Spielfenster Vollbild erreicht, oder nach einem Sicherheits-Timeout.
• Kann mit ESC oder der Zurück/Schließen-Taste gängiger Controller geschlossen werden.

## Anpassung
• Du kannst ein eigenes Logo (PNG, JPG, WebP oder BMP) statt des Playhub-Logos verwenden, wenn kein Spiellogo vorhanden ist.
• Du kannst festlegen, wie lange Launch Curtain sichtbar bleibt, nachdem das Spiel bereit ist."),
                ["pt"] = new PluginText(
                    "Uma entrada limpa para o jogo.",
                    @"Launch Curtain transforma a abertura dos jogos de PC em algo mais próximo de um console. Quando você inicia um jogo pelo Steam Big Picture, surge uma tela de carregamento em tela cheia que esconde flashes do desktop, launchers e janelas fora do lugar, deixando o logo do jogo em destaque com uma transição suave. É um plugin Playhub exclusivo para Windows.

## O que faz
• Mostra uma tela personalizável assim que um jogo é iniciado.
• Permite ajustar o Launch Curtain do seu jeito: posição e tamanho do logo, fundo, opacidade e zoom animado opcional.
• Some sozinho quando a janela do jogo chega ao modo tela cheia, ou depois de um tempo de segurança.
• Pode ser fechado com ESC ou com o botão Voltar/Fechar dos controles mais comuns.

## Personalização
• Você pode usar seu próprio logo (PNG, JPG, WebP ou BMP) no lugar do logo Playhub quando o logo do jogo não estiver disponível.
• Você pode ajustar por quanto tempo o Launch Curtain continua visível depois que o jogo está pronto."),
                ["uk"] = new PluginText(
                    "Чистий вхід у гру.",
                    @"Launch Curtain перетворює запуск ПК-ігор на досвід, ближчий до консолі. Коли ти запускаєш гру зі Steam Big Picture, з'являється повноекранний екран завантаження, який ховає спалахи робочого столу, лаунчери та зайві вікна, залишаючи логотип гри в центрі з м'яким згасанням. Це ексклюзивний плагін Playhub для Windows.

## Що він робить
• Показує налаштовуваний екран одразу після запуску гри.
• Дає налаштувати Launch Curtain під себе: позицію і розмір логотипа, фон, прозорість і додаткову анімацію масштабування.
• Сам ховається, коли вікно гри переходить у повний екран, або після захисного тайм-ауту.
• Закривається клавішею ESC або кнопкою Назад/Закрити на поширених контролерах.

## Налаштування
• Можна використати власний логотип (PNG, JPG, WebP або BMP) замість логотипа Playhub, якщо логотип гри відсутній.
• Можна налаштувати, як довго Launch Curtain лишається видимим після готовності гри."),
                ["zh"] = new PluginText(
                    "干净进入游戏世界。",
                    @"Launch Curtain 让 PC 游戏的启动更像主机体验。你从 Steam Big Picture 启动游戏时，它会显示一个全屏加载画面，隐藏桌面闪烁、启动器和跑偏的窗口，并用柔和淡入淡出把游戏标志放在最前面。这是 Playhub 专为 Windows 打造的插件。

## 功能
• 游戏启动后立即显示可自定义的加载画面。
• 可按你的喜好调整 Launch Curtain：标志位置和大小、背景、透明度，以及可选的标志缩放动画。
• 当游戏窗口进入全屏后自动隐藏，或在安全超时后隐藏。
• 可用 ESC 或常见手柄的返回/关闭按钮关闭。

## 自定义
• 当缺少游戏标志时，可使用你自己的标志（PNG、JPG、WebP 或 BMP）替代 Playhub 标志。
• 可调整游戏就绪后 Launch Curtain 继续显示的时间。"),
                ["ja"] = new PluginText(
                    "ゲームへすっと入れる入口。",
                    @"Launch Curtain は、PC ゲームの起動をコンソールのように整えます。Steam Big Picture からゲームを起動すると、全画面のロード画面が入り、デスクトップのちらつき、ランチャー、余計なウィンドウを隠しながら、ゲームロゴをやわらかなフェードで前面に表示します。Windows 専用の Playhub プラグインです。

## できること
• ゲーム起動直後にカスタムできる画面を表示します。
• ロゴの位置とサイズ、背景、不透明度、任意のロゴズームアニメーションを自由に調整できます。
• ゲームウィンドウがフルスクリーンになった時、または安全タイムアウト後に自動で消えます。
• ESC、または一般的なコントローラーの戻る/閉じるボタンで閉じられます。

## カスタマイズ
• ゲームロゴがない場合、Playhub ロゴの代わりに自分のロゴ（PNG、JPG、WebP、BMP）を使えます。
• ゲームの準備ができたあと、Launch Curtain をどのくらい表示するか調整できます。"),
                ["ko"] = new PluginText(
                    "게임으로 깔끔하게 들어가는 문.",
                    @"Launch Curtain은 PC 게임 실행을 콘솔처럼 매끄럽게 만들어 줍니다. Steam Big Picture에서 게임을 시작하면 전체 화면 로딩 화면이 나타나 데스크톱 깜박임, 런처, 어색한 창을 가리고, 게임 로고를 부드러운 전환으로 전면에 보여 줍니다. Windows 전용 Playhub 플러그인입니다.

## 기능
• 게임이 실행되자마자 사용자 지정 가능한 화면을 표시합니다.
• 로고 위치와 크기, 배경, 불투명도, 선택형 로고 확대 애니메이션까지 Launch Curtain을 원하는 대로 조정할 수 있습니다.
• 게임 창이 전체 화면이 되거나 안전 시간 제한이 지나면 자동으로 숨겨집니다.
• ESC 또는 일반적인 컨트롤러의 뒤로/닫기 버튼으로 닫을 수 있습니다.

## 사용자 지정
• 게임 로고가 없을 때 Playhub 로고 대신 내 로고(PNG, JPG, WebP, BMP)를 사용할 수 있습니다.
• 게임이 준비된 뒤 Launch Curtain이 얼마나 오래 보일지 조정할 수 있습니다."),
                ["hi"] = new PluginText(
                    "गेम में जाने का साफ-सुथरा रास्ता.",
                    @"Launch Curtain PC गेम के शुरू होने को कंसोल जैसा महसूस कराता है। जब आप Steam Big Picture से गेम चलाते हैं, यह पूरी स्क्रीन पर लोडिंग स्क्रीन दिखाता है, डेस्कटॉप की चमक, लॉन्चर और गलत जगह खुली विंडो छुपाता है, और गेम का लोगो नरम फेड के साथ सामने रखता है। यह Windows के लिए खास Playhub प्लगइन है।

## यह क्या करता है
• गेम शुरू होते ही कस्टम स्क्रीन दिखाता है।
• Launch Curtain को अपने हिसाब से सजाने देता है: लोगो की जगह और आकार, बैकग्राउंड, अपारदर्शिता और वैकल्पिक लोगो जूम ऐनिमेशन।
• गेम विंडो फुलस्क्रीन होने पर, या सुरक्षा टाइमआउट के बाद, अपने आप छुप जाता है।
• ESC या आम कंट्रोलर के Back/Close बटन से बंद किया जा सकता है।

## कस्टमाइज़ेशन
• जब गेम लोगो न मिले, तो Playhub लोगो की जगह अपना लोगो (PNG, JPG, WebP या BMP) इस्तेमाल कर सकते हैं।
• गेम तैयार होने के बाद Launch Curtain कितनी देर दिखे, यह तय कर सकते हैं।"),
                ["ru"] = new PluginText(
                    "Чистый вход в игру.",
                    @"Launch Curtain делает запуск ПК-игр ближе к консольному опыту. Когда ты запускаешь игру из Steam Big Picture, появляется полноэкранный экран загрузки, который скрывает вспышки рабочего стола, лаунчеры и лишние окна, оставляя логотип игры на переднем плане с мягким переходом. Это эксклюзивный плагин Playhub для Windows.

## Что он делает
• Показывает настраиваемый экран сразу после запуска игры.
• Позволяет настроить Launch Curtain под себя: положение и размер логотипа, фон, прозрачность и необязательную анимацию приближения логотипа.
• Сам скрывается, когда окно игры переходит в полноэкранный режим, или после защитного тайм-аута.
• Закрывается через ESC или кнопкой Назад/Закрыть на популярных контроллерах.

## Настройка
• Можно использовать свой логотип (PNG, JPG, WebP или BMP) вместо логотипа Playhub, если логотип игры отсутствует.
• Можно настроить, как долго Launch Curtain остаётся видимым после готовности игры.")
            },
            ["Now-Playing"] = new Dictionary<string, PluginText>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new PluginText(
                    "Your favourite songs, always with you.",
                    @"Now Playing is your console-style music companion: it brings the active Windows media session into the Steam quick menu, with cover art, title and controls always within gamepad reach.

## What it does
• Shows the active Windows media session in the quick menu.
• Displays title, artist, album art and track progress.
• Offers play/pause, previous, next, shuffle and repeat controls when the player exposes them.
• Opens popular music apps on the fly: Spotify, TIDAL, Apple Music, Deezer, Amazon Music and SoundCloud.
• Includes a full-screen Now Playing view with visualizer.
• Talks to Windows through a dedicated helper that reads media sessions."),
                ["es"] = new PluginText(
                    "Tus canciones favoritas, siempre contigo.",
                    @"Now Playing es tu compañero musical con sabor a consola: lleva la sesión multimedia activa de Windows al menú rápido de Steam, con carátula, título y controles siempre a mano desde el mando.

## Qué hace
• Muestra en el menú rápido la sesión multimedia activa de Windows.
• Enseña título, artista, carátula del álbum y progreso de la canción.
• Ofrece controles de reproducir/pausa, anterior, siguiente, aleatorio y repetir cuando el reproductor los expone.
• Abre al vuelo las apps musicales más conocidas: Spotify, TIDAL, Apple Music, Deezer, Amazon Music y SoundCloud.
• Incluye una vista Now Playing a pantalla completa con visualizer.
• Se comunica con Windows mediante un helper dedicado para leer las sesiones multimedia."),
                ["fr"] = new PluginText(
                    "Tes morceaux préférés, toujours avec toi.",
                    @"Now Playing est ton compagnon musical façon console : il apporte la session média active de Windows dans le menu rapide de Steam, avec pochette, titre et contrôles toujours accessibles à la manette.

## Ce qu'il fait
• Affiche la session média active de Windows dans le menu rapide.
• Montre le titre, l'artiste, la pochette de l'album et la progression du morceau.
• Propose lecture/pause, précédent, suivant, aléatoire et répétition quand le lecteur les expose.
• Lance rapidement les apps musicales les plus utilisées : Spotify, TIDAL, Apple Music, Deezer, Amazon Music et SoundCloud.
• Inclut une vue Now Playing plein écran avec visualizer.
• Communique avec Windows via un helper dédié pour lire les sessions média."),
                ["de"] = new PluginText(
                    "Deine Lieblingssongs, immer dabei.",
                    @"Now Playing ist dein Musikbegleiter im Konsolenstil: Es bringt die aktive Windows-Mediensitzung in das Steam-Schnellmenü, mit Cover, Titel und Steuerung immer in Reichweite des Controllers.

## Was es macht
• Zeigt die aktive Windows-Mediensitzung im Schnellmenü.
• Zeigt Titel, Künstler, Albumcover und Fortschritt des Songs.
• Bietet Wiedergabe/Pause, Zurück, Weiter, Shuffle und Wiederholen, wenn der Player diese Steuerungen bereitstellt.
• Öffnet beliebte Musik-Apps direkt: Spotify, TIDAL, Apple Music, Deezer, Amazon Music und SoundCloud.
• Enthält eine Now-Playing-Vollbildansicht mit Visualizer.
• Spricht über einen eigenen Helper mit Windows, um Mediensitzungen auszulesen."),
                ["pt"] = new PluginText(
                    "Suas músicas favoritas, sempre com você.",
                    @"Now Playing é seu companheiro musical com jeito de console: leva a sessão de mídia ativa do Windows para o menu rápido do Steam, com capa, título e controles sempre ao alcance do controle.

## O que faz
• Mostra no menu rápido a sessão de mídia ativa do Windows.
• Exibe título, artista, capa do álbum e progresso da faixa.
• Oferece controles de play/pausa, anterior, próximo, aleatório e repetir quando o player disponibiliza.
• Abre rapidamente os apps de música mais populares: Spotify, TIDAL, Apple Music, Deezer, Amazon Music e SoundCloud.
• Inclui uma visualização Now Playing em tela cheia com visualizer.
• Conversa com o Windows por meio de um helper dedicado para ler as sessões de mídia."),
                ["uk"] = new PluginText(
                    "Улюблена музика завжди поруч.",
                    @"Now Playing - це музичний супутник у консольному стилі: він переносить активну медіасесію Windows у швидке меню Steam, з обкладинкою, назвою та керуванням, доступними з геймпада.

## Що він робить
• Показує активну медіасесію Windows у швидкому меню.
• Відображає назву, виконавця, обкладинку альбому та прогрес треку.
• Дає керування відтворенням/паузою, попереднім, наступним, перемішуванням і повтором, якщо плеєр це підтримує.
• Швидко відкриває популярні музичні застосунки: Spotify, TIDAL, Apple Music, Deezer, Amazon Music і SoundCloud.
• Має повноекранний режим Now Playing з візуалізатором.
• Спілкується з Windows через окремий helper, який читає медіасесії."),
                ["zh"] = new PluginText(
                    "喜欢的歌，随时在手边。",
                    @"Now Playing 是你的主机风格音乐伙伴：它把 Windows 当前媒体会话带进 Steam 快捷菜单，封面、标题和控制都能用手柄轻松操作。

## 功能
• 在快捷菜单中显示 Windows 当前媒体会话。
• 显示标题、艺人、专辑封面和播放进度。
• 当播放器提供时，支持播放/暂停、上一首、下一首、随机播放和重复播放。
• 快速打开常见音乐应用：Spotify、TIDAL、Apple Music、Deezer、Amazon Music 和 SoundCloud。
• 包含带可视化效果的全屏 Now Playing 视图。
• 通过专用 helper 与 Windows 通信，读取媒体会话。"),
                ["ja"] = new PluginText(
                    "お気に入りの曲を、いつもそばに。",
                    @"Now Playing はコンソール風の音楽コンパニオンです。Windows のアクティブなメディアセッションを Steam のクイックメニューに表示し、カバーアート、タイトル、操作をいつでもゲームパッドで扱えるようにします。

## できること
• Windows のアクティブなメディアセッションをクイックメニューに表示します。
• タイトル、アーティスト、アルバムアート、曲の進行状況を表示します。
• プレイヤーが対応している場合、再生/一時停止、前へ、次へ、シャッフル、リピートを操作できます。
• Spotify、TIDAL、Apple Music、Deezer、Amazon Music、SoundCloud などの音楽アプリをすぐに開けます。
• ビジュアライザー付きのフルスクリーン Now Playing ビューを含みます。
• 専用 helper を通じて Windows と連携し、メディアセッションを読み取ります。"),
                ["ko"] = new PluginText(
                    "좋아하는 음악을 언제나 곁에.",
                    @"Now Playing은 콘솔 느낌의 음악 동반자입니다. Windows의 현재 미디어 세션을 Steam 빠른 메뉴로 가져와 앨범 아트, 제목, 조작을 게임패드로 바로 다룰 수 있게 해 줍니다.

## 기능
• Windows의 활성 미디어 세션을 빠른 메뉴에 표시합니다.
• 제목, 아티스트, 앨범 아트, 재생 진행률을 보여 줍니다.
• 플레이어가 제공하는 경우 재생/일시정지, 이전, 다음, 셔플, 반복 조작을 제공합니다.
• Spotify, TIDAL, Apple Music, Deezer, Amazon Music, SoundCloud 같은 음악 앱을 빠르게 엽니다.
• 비주얼라이저가 있는 전체 화면 Now Playing 보기를 포함합니다.
• 전용 helper를 통해 Windows와 통신해 미디어 세션을 읽습니다."),
                ["hi"] = new PluginText(
                    "आपके पसंदीदा गाने, हमेशा साथ.",
                    @"Now Playing आपका कंसोल-स्टाइल संगीत साथी है: यह Windows की सक्रिय मीडिया सेशन को Steam के क्विक मेनू में लाता है, कवर आर्ट, शीर्षक और कंट्रोल को हमेशा गेमपैड की पहुंच में रखता है।

## यह क्या करता है
• क्विक मेनू में Windows की सक्रिय मीडिया सेशन दिखाता है।
• शीर्षक, कलाकार, एल्बम आर्ट और गाने की प्रगति दिखाता है।
• प्लेयर उपलब्ध कराए तो play/pause, previous, next, shuffle और repeat कंट्रोल देता है।
• लोकप्रिय संगीत ऐप तुरंत खोलता है: Spotify, TIDAL, Apple Music, Deezer, Amazon Music और SoundCloud।
• visualizer के साथ फुलस्क्रीन Now Playing व्यू शामिल करता है।
• मीडिया सेशन पढ़ने के लिए dedicated helper के जरिए Windows से बात करता है।"),
                ["ru"] = new PluginText(
                    "Любимая музыка всегда рядом.",
                    @"Now Playing - твой музыкальный спутник в консольном стиле: он переносит активную медиасессию Windows в быстрое меню Steam, с обложкой, названием и управлением, всегда доступными с геймпада.

## Что он делает
• Показывает активную медиасессию Windows в быстром меню.
• Отображает название, исполнителя, обложку альбома и прогресс трека.
• Даёт управление воспроизведением/паузой, предыдущим, следующим, перемешиванием и повтором, если плеер это предоставляет.
• Быстро открывает популярные музыкальные приложения: Spotify, TIDAL, Apple Music, Deezer, Amazon Music и SoundCloud.
• Включает полноэкранный экран Now Playing с визуализатором.
• Общается с Windows через отдельный helper для чтения медиасессий.")
            },
            ["Playhub-Metadata"] = new Dictionary<string, PluginText>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new PluginText(
                    "Details, artwork and achievements for your games.",
                    @"Playhub Metadata makes the Big Picture library feel richer, cleaner and more console-like, especially for non-Steam games: external PC titles, Game Pass, Xbox apps and emulators. It adds metadata, images and community videos, categories and even achievements.

## Metadata and images
• Automatically finds missing game metadata.
• Adds descriptions, developers, publishers, release dates, ratings and info panels.
• Adds community screenshots and media when available.
• Lets you manually edit each game's metadata.

## Achievements
• Shows achievements for non-Steam games inside Big Picture.
• Supports RetroAchievements for ROMs and emulators.
• Supports Xbox / Game Pass / Microsoft Store achievements through OpenXBL (games must be imported from Playhub's Import Xbox Games tab).
• Lets you choose the source for each game: Auto, RetroAchievements, Xbox or Off.
• Offers flexible caches (hourly, daily, weekly, per session or manual) to reduce API calls.

## Note
• Achievements do not become real Steam achievements: they are only shown inside Big Picture."),
                ["es"] = new PluginText(
                    "Detalles, imágenes y logros para tus juegos.",
                    @"Playhub Metadata hace que la biblioteca de Big Picture se vea más cuidada, rica y con aire de consola, sobre todo con juegos que no son de Steam: títulos externos de PC, Game Pass, apps Xbox y emuladores. Añade metadatos, imágenes y vídeos de la comunidad, categorías e incluso logros.

## Metadatos e imágenes
• Encuentra automáticamente los metadatos que faltan.
• Añade descripciones, desarrolladores, publishers, fechas de lanzamiento, valoraciones y paneles informativos.
• Añade capturas y medios de la comunidad cuando están disponibles.
• Te deja editar manualmente los metadatos de cada juego.

## Logros
• Muestra logros de juegos no-Steam dentro de Big Picture.
• Soporta RetroAchievements para ROMs y emuladores.
• Soporta logros de Xbox / Game Pass / Microsoft Store mediante OpenXBL (hay que importar los juegos desde la pestaña Importa Juegos Xbox de Playhub).
• Permite elegir la fuente para cada juego: Auto, RetroAchievements, Xbox o Desactivada.
• Ofrece cachés flexibles (por hora, día, semana, sesión o manuales) para reducir llamadas a la API.

## Nota
• Los logros no se convierten en logros reales de Steam: solo se muestran dentro de Big Picture."),
                ["fr"] = new PluginText(
                    "Détails, images et succès pour tes jeux.",
                    @"Playhub Metadata rend la bibliothèque Big Picture plus soignée, plus riche et plus proche d'une console, surtout pour les jeux non-Steam : titres PC externes, Game Pass, apps Xbox et émulateurs. Il ajoute métadonnées, images et vidéos de la communauté, catégories et même succès.

## Métadonnées et images
• Trouve automatiquement les métadonnées manquantes des jeux.
• Ajoute descriptions, développeurs, éditeurs, dates de sortie, notes et panneaux d'information.
• Ajoute captures et médias de la communauté quand ils sont disponibles.
• Te laisse modifier manuellement les métadonnées de chaque jeu.

## Succès
• Affiche les succès des jeux non-Steam dans Big Picture.
• Prend en charge RetroAchievements pour les ROMs et les émulateurs.
• Prend en charge les succès Xbox / Game Pass / Microsoft Store via OpenXBL (les jeux doivent être importés depuis l'onglet Importer Jeux Xbox de Playhub).
• Permet de choisir la source pour chaque jeu : Auto, RetroAchievements, Xbox ou Désactivée.
• Propose des caches flexibles (horaire, quotidien, hebdomadaire, par session ou manuel) pour limiter les appels API.

## Note
• Les succès ne deviennent pas de vrais succès Steam : ils sont seulement affichés dans Big Picture."),
                ["de"] = new PluginText(
                    "Details, Bilder und Erfolge für deine Spiele.",
                    @"Playhub Metadata macht die Big-Picture-Bibliothek gepflegter, reichhaltiger und konsolenähnlicher, besonders bei Nicht-Steam-Spielen: externe PC-Titel, Game Pass, Xbox-Apps und Emulatoren. Es fügt Metadaten, Bilder und Community-Videos, Kategorien und sogar Erfolge hinzu.

## Metadaten und Bilder
• Findet automatisch fehlende Spielmetadaten.
• Fügt Beschreibungen, Entwickler, Publisher, Veröffentlichungsdaten, Bewertungen und Infokarten hinzu.
• Fügt Screenshots und Medien aus der Community hinzu, wenn verfügbar.
• Lässt dich die Metadaten jedes Spiels manuell bearbeiten.

## Erfolge
• Zeigt Erfolge von Nicht-Steam-Spielen in Big Picture.
• Unterstützt RetroAchievements für ROMs und Emulatoren.
• Unterstützt Xbox / Game Pass / Microsoft Store-Erfolge über OpenXBL (Spiele müssen über Playhubs Tab Xbox-Spiele importieren importiert werden).
• Lässt dich die Quelle pro Spiel wählen: Auto, RetroAchievements, Xbox oder Aus.
• Bietet flexible Caches (stündlich, täglich, wöchentlich, pro Sitzung oder manuell) um API-Aufrufe zu reduzieren.

## Hinweis
• Die Erfolge werden nicht zu echten Steam-Erfolgen: Sie werden nur in Big Picture angezeigt."),
                ["pt"] = new PluginText(
                    "Detalhes, imagens e conquistas para seus jogos.",
                    @"Playhub Metadata deixa a biblioteca Big Picture mais caprichada, rica e com cara de console, especialmente para jogos que não são da Steam: títulos externos de PC, Game Pass, apps Xbox e emuladores. Ele adiciona metadados, imagens e vídeos da comunidade, categorias e até conquistas.

## Metadados e imagens
• Encontra automaticamente metadados ausentes dos jogos.
• Adiciona descrições, desenvolvedores, publishers, datas de lançamento, avaliações e painéis informativos.
• Adiciona capturas e mídias da comunidade quando disponíveis.
• Permite editar manualmente os metadados de cada jogo.

## Conquistas
• Mostra conquistas de jogos não-Steam dentro do Big Picture.
• Suporta RetroAchievements para ROMs e emuladores.
• Suporta conquistas Xbox / Game Pass / Microsoft Store via OpenXBL (é preciso importar os jogos pela aba Importar Jogos Xbox do Playhub).
• Permite escolher a fonte para cada jogo: Auto, RetroAchievements, Xbox ou Desativada.
• Oferece caches flexíveis (por hora, dia, semana, sessão ou manual) para reduzir chamadas de API.

## Nota
• As conquistas não viram conquistas reais da Steam: elas são apenas exibidas dentro do Big Picture."),
                ["uk"] = new PluginText(
                    "Деталі, зображення й досягнення для твоїх ігор.",
                    @"Playhub Metadata робить бібліотеку Big Picture охайнішою, багатшою й ближчою до консолі, особливо для не-Steam ігор: зовнішніх ПК-ігор, Game Pass, застосунків Xbox та емуляторів. Він додає метадані, зображення й відео спільноти, категорії та навіть досягнення.

## Метадані та зображення
• Автоматично знаходить відсутні метадані ігор.
• Додає описи, розробників, видавців, дати виходу, оцінки та інформаційні картки.
• Додає скріншоти та медіа спільноти, коли вони доступні.
• Дає вручну редагувати метадані кожної гри.

## Досягнення
• Показує досягнення не-Steam ігор у Big Picture.
• Підтримує RetroAchievements для ROM і емуляторів.
• Підтримує досягнення Xbox / Game Pass / Microsoft Store через OpenXBL (ігри треба імпортувати з вкладки Імпорт ігор Xbox у Playhub).
• Дає вибрати джерело для кожної гри: Auto, RetroAchievements, Xbox або Вимкнено.
• Пропонує гнучкий кеш (щогодини, щодня, щотижня, за сесію або вручну) щоб обмежити API-виклики.

## Примітка
• Досягнення не стають справжніми досягненнями Steam: вони лише показуються в Big Picture."),
                ["zh"] = new PluginText(
                    "为你的游戏补上详情、图片和成就。",
                    @"Playhub Metadata 让 Big Picture 库更精致、更丰富，也更像主机界面，尤其适合非 Steam 游戏：外部 PC 游戏、Game Pass、Xbox 应用和模拟器。它会添加元数据、社区图片和视频、分类，甚至成就。

## 元数据和图片
• 自动查找缺失的游戏元数据。
• 添加描述、开发商、发行商、发售日期、评分和信息面板。
• 在可用时添加社区截图和媒体。
• 允许你手动编辑每个游戏的元数据。

## 成就
• 在 Big Picture 中显示非 Steam 游戏的成就。
• 支持 ROM 和模拟器的 RetroAchievements。
• 通过 OpenXBL 支持 Xbox / Game Pass / Microsoft Store 成就（游戏需要从 Playhub 的导入 Xbox 游戏标签导入）。
• 可为每个游戏选择来源：自动、RetroAchievements、Xbox 或关闭。
• 提供灵活缓存：每小时、每天、每周、每次会话或手动，以减少 API 调用。

## 说明
• 这些成就不会变成真正的 Steam 成就：它们只会显示在 Big Picture 中。"),
                ["ja"] = new PluginText(
                    "ゲームに詳細、画像、実績を。",
                    @"Playhub Metadata は Big Picture ライブラリをより整った、豊かな、コンソールらしい見た目にします。特に非 Steam ゲーム、外部 PC タイトル、Game Pass、Xbox アプリ、エミュレーターで力を発揮します。メタデータ、コミュニティ画像と動画、カテゴリ、さらに実績まで追加します。

## メタデータと画像
• 不足しているゲームメタデータを自動で探します。
• 説明、開発元、パブリッシャー、発売日、評価、情報パネルを追加します。
• 利用できる場合、コミュニティのスクリーンショットやメディアを追加します。
• 各ゲームのメタデータを手動で編集できます。

## 実績
• 非 Steam ゲームの実績を Big Picture 内に表示します。
• ROM とエミュレーター向けに RetroAchievements をサポートします。
• OpenXBL 経由で Xbox / Game Pass / Microsoft Store の実績をサポートします（ゲームは Playhub の Xbox ゲームをインポート タブから取り込む必要があります）。
• ゲームごとにソースを選べます：Auto、RetroAchievements、Xbox、オフ。
• API 呼び出しを抑えるため、毎時、毎日、毎週、セッションごと、手動の柔軟なキャッシュを用意します。

## メモ
• 実績は本物の Steam 実績にはなりません。Big Picture 内に表示されるだけです。"),
                ["ko"] = new PluginText(
                    "게임에 정보, 이미지, 업적을 더합니다.",
                    @"Playhub Metadata는 Big Picture 라이브러리를 더 정돈되고 풍부하며 콘솔처럼 보이게 합니다. 특히 Steam이 아닌 게임, 외부 PC 타이틀, Game Pass, Xbox 앱, 에뮬레이터에 잘 어울립니다. 메타데이터, 커뮤니티 이미지와 영상, 카테고리, 심지어 업적까지 추가합니다.

## 메타데이터와 이미지
• 누락된 게임 메타데이터를 자동으로 찾습니다.
• 설명, 개발사, 퍼블리셔, 출시일, 평점, 정보 패널을 추가합니다.
• 사용 가능한 경우 커뮤니티 스크린샷과 미디어를 추가합니다.
• 각 게임의 메타데이터를 직접 수정할 수 있습니다.

## 업적
• Big Picture 안에서 비 Steam 게임의 업적을 보여 줍니다.
• ROM과 에뮬레이터용 RetroAchievements를 지원합니다.
• OpenXBL을 통해 Xbox / Game Pass / Microsoft Store 업적을 지원합니다(게임은 Playhub의 Xbox 게임 가져오기 탭에서 가져와야 합니다).
• 게임마다 소스를 선택할 수 있습니다: Auto, RetroAchievements, Xbox 또는 끄기.
• API 호출을 줄이기 위해 시간별, 일별, 주별, 세션별, 수동 캐시를 제공합니다.

## 참고
• 업적은 실제 Steam 업적이 되지 않습니다. Big Picture 안에 표시될 뿐입니다."),
                ["hi"] = new PluginText(
                    "आपके गेम के लिए विवरण, चित्र और achievements.",
                    @"Playhub Metadata Big Picture लाइब्रेरी को ज्यादा सजी हुई, समृद्ध और कंसोल जैसी बनाता है, खासकर non-Steam गेम के लिए: बाहरी PC टाइटल, Game Pass, Xbox ऐप और एम्युलेटर। यह metadata, community images और videos, categories और achievements तक जोड़ता है।

## Metadata और images
• गेम के गायब metadata अपने आप ढूंढता है।
• descriptions, developers, publishers, release dates, ratings और info panels जोड़ता है।
• उपलब्ध होने पर community screenshots और media जोड़ता है।
• हर गेम का metadata हाथ से बदलने देता है।

## Achievements
• Big Picture के अंदर non-Steam गेम के achievements दिखाता है।
• ROMs और emulators के लिए RetroAchievements सपोर्ट करता है।
• OpenXBL के जरिए Xbox / Game Pass / Microsoft Store achievements सपोर्ट करता है (गेम Playhub की Import Xbox Games tab से import होने चाहिए)।
• हर गेम के लिए source चुनने देता है: Auto, RetroAchievements, Xbox या Off।
• API calls कम करने के लिए flexible caches देता है - hourly, daily, weekly, per session या manual।

## Note
• Achievements असली Steam achievements नहीं बनते: वे सिर्फ Big Picture के अंदर दिखते हैं।"),
                ["ru"] = new PluginText(
                    "Детали, изображения и достижения для твоих игр.",
                    @"Playhub Metadata делает библиотеку Big Picture более аккуратной, насыщенной и консольной, особенно для не-Steam игр: внешних ПК-игр, Game Pass, приложений Xbox и эмуляторов. Он добавляет метаданные, изображения и видео сообщества, категории и даже достижения.

## Метаданные и изображения
• Автоматически находит недостающие метаданные игр.
• Добавляет описания, разработчиков, издателей, даты выхода, оценки и информационные карточки.
• Добавляет скриншоты и медиа сообщества, когда они доступны.
• Позволяет вручную редактировать метаданные каждой игры.

## Достижения
• Показывает достижения не-Steam игр внутри Big Picture.
• Поддерживает RetroAchievements для ROM и эмуляторов.
• Поддерживает достижения Xbox / Game Pass / Microsoft Store через OpenXBL (игры нужно импортировать через вкладку Импорт игр Xbox в Playhub).
• Позволяет выбрать источник для каждой игры: Auto, RetroAchievements, Xbox или Выкл.
• Даёт гибкие кэши (почасовой, ежедневный, еженедельный, за сессию или ручной) чтобы снизить число API-вызовов.

## Примечание
• Достижения не становятся настоящими достижениями Steam: они только показываются внутри Big Picture.")
            },
            ["Quick-Settings"] = new Dictionary<string, PluginText>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new PluginText(
                    "The important settings, always within reach.",
                    @"Quick Settings brings Windows quick controls into Steam Big Picture through a small local agent started by the plugin. Everything you need to adjust stays available from the quick menu, without going back to the desktop.

## Available controls
• Device volume.
• Microphone volume.
• Overlay to dim the screen.
• Audio output and microphone input selectors.
• HDR switch with a 10-second confirmation.
• HDR state read directly from Windows (DisplayConfig / Advanced Color) instead of trusting a saved plugin state."),
                ["es"] = new PluginText(
                    "Los ajustes importantes, siempre a mano.",
                    @"Quick Settings lleva los controles rápidos de Windows a Steam Big Picture mediante un pequeño agente local iniciado por el plugin. Todo lo que necesitas ajustar queda disponible desde el menú rápido, sin volver al escritorio.

## Controles disponibles
• Volumen del dispositivo.
• Volumen del micrófono.
• Overlay para atenuar la pantalla.
• Selectores de salida de audio y entrada de micrófono.
• Interruptor HDR con confirmación de 10 segundos.
• Estado HDR leído directamente desde Windows (DisplayConfig / Advanced Color), en lugar de depender de un estado guardado por el plugin."),
                ["fr"] = new PluginText(
                    "Les réglages importants, toujours à portée de main.",
                    @"Quick Settings apporte les réglages rapides de Windows dans Steam Big Picture grâce à un petit agent local lancé par le plugin. Tout ce dont tu as besoin reste accessible depuis le menu rapide, sans revenir au bureau.

## Contrôles disponibles
• Volume de l'appareil.
• Volume du microphone.
• Overlay pour assombrir l'écran.
• Sélecteurs de sortie audio et d'entrée micro.
• Interrupteur HDR avec confirmation de 10 secondes.
• État HDR lu directement depuis Windows (DisplayConfig / Advanced Color), au lieu de s'appuyer sur un état enregistré par le plugin."),
                ["de"] = new PluginText(
                    "Die wichtigen Einstellungen, immer griffbereit.",
                    @"Quick Settings bringt Windows-Schnelleinstellungen über einen kleinen lokalen Agenten, den das Plugin startet, in Steam Big Picture. Alles, was du anpassen musst, bleibt über das Schnellmenü erreichbar, ohne zurück zum Desktop zu gehen.

## Verfügbare Steuerungen
• Gerätelautstärke.
• Mikrofonlautstärke.
• Overlay zum Abdunkeln des Bildschirms.
• Auswahl für Audioausgabe und Mikrofoneingang.
• HDR-Schalter mit 10-Sekunden-Bestätigung.
• HDR-Status direkt aus Windows gelesen (DisplayConfig / Advanced Color), statt einem gespeicherten Plugin-Zustand zu vertrauen."),
                ["pt"] = new PluginText(
                    "As configurações importantes, sempre por perto.",
                    @"Quick Settings leva os controles rápidos do Windows para o Steam Big Picture por meio de um pequeno agente local iniciado pelo plugin. Tudo o que você precisa ajustar fica disponível no menu rápido, sem voltar ao desktop.

## Controles disponíveis
• Volume do dispositivo.
• Volume do microfone.
• Overlay para escurecer a tela.
• Seletores de saída de áudio e entrada de microfone.
• Interruptor HDR com confirmação de 10 segundos.
• Estado HDR lido diretamente do Windows (DisplayConfig / Advanced Color), em vez de confiar em um estado salvo pelo plugin."),
                ["uk"] = new PluginText(
                    "Важливі налаштування завжди під рукою.",
                    @"Quick Settings переносить швидкі налаштування Windows у Steam Big Picture через невеликий локальний агент, який запускає плагін. Усе, що треба підкрутити, залишається доступним зі швидкого меню, без повернення на робочий стіл.

## Доступні елементи керування
• Гучність пристрою.
• Гучність мікрофона.
• Оверлей для затемнення екрана.
• Вибір аудіовиходу та входу мікрофона.
• Перемикач HDR із підтвердженням на 10 секунд.
• Стан HDR читається напряму з Windows (DisplayConfig / Advanced Color), а не збереженого стану плагіна."),
                ["zh"] = new PluginText(
                    "重要设置，随时可调。",
                    @"Quick Settings 通过插件启动的小型本地代理，把 Windows 快速控制带进 Steam Big Picture。需要调节的内容都能从快捷菜单完成，不用回到桌面。

## 可用控制
• 设备音量。
• 麦克风音量。
• 用于调暗屏幕的覆盖层。
• 音频输出和麦克风输入选择器。
• 带 10 秒确认的 HDR 开关。
• HDR 状态直接从 Windows 读取（DisplayConfig / Advanced Color），不依赖插件保存的状态。"),
                ["ja"] = new PluginText(
                    "大事な設定を、いつでも手元に。",
                    @"Quick Settings は、プラグインが起動する小さなローカルエージェントを通じて、Windows のクイック設定を Steam Big Picture に持ち込みます。調整したいものはクイックメニューから操作でき、デスクトップに戻る必要がありません。

## 利用できる操作
• デバイス音量。
• マイク音量。
• 画面を暗くするオーバーレイ。
• 音声出力とマイク入力のセレクター。
• 10 秒確認付きの HDR スイッチ。
• プラグインの保存状態ではなく、Windows から直接読み取る HDR 状態（DisplayConfig / Advanced Color）。"),
                ["ko"] = new PluginText(
                    "중요한 설정을 언제나 손끝에.",
                    @"Quick Settings는 플러그인이 시작하는 작은 로컬 에이전트를 통해 Windows 빠른 설정을 Steam Big Picture 안으로 가져옵니다. 조정해야 할 모든 것이 빠른 메뉴에 있어 데스크톱으로 돌아갈 필요가 없습니다.

## 사용 가능한 컨트롤
• 장치 볼륨.
• 마이크 볼륨.
• 화면을 어둡게 하는 오버레이.
• 오디오 출력과 마이크 입력 선택기.
• 10초 확인이 있는 HDR 스위치.
• 플러그인이 저장한 상태가 아니라 Windows에서 직접 읽는 HDR 상태(DisplayConfig / Advanced Color)."),
                ["hi"] = new PluginText(
                    "ज़रूरी सेटिंग्स, हमेशा पास.",
                    @"Quick Settings Windows के quick controls को Steam Big Picture में लाता है, एक छोटे local agent के जरिए जिसे plugin शुरू करता है। जो भी बदलना हो, वह quick menu में रहता है, desktop पर वापस जाने की जरूरत नहीं।

## उपलब्ध controls
• Device volume.
• Microphone volume.
• स्क्रीन को dim करने के लिए overlay.
• Audio output और microphone input selectors.
• 10-second confirmation वाला HDR switch.
• HDR state सीधे Windows से पढ़ा जाता है (DisplayConfig / Advanced Color), plugin में saved state पर भरोसा नहीं किया जाता।"),
                ["ru"] = new PluginText(
                    "Важные настройки всегда под рукой.",
                    @"Quick Settings переносит быстрые настройки Windows в Steam Big Picture через небольшой локальный агент, запускаемый плагином. Всё, что нужно отрегулировать, остаётся доступным из быстрого меню, без возврата на рабочий стол.

## Доступные элементы управления
• Громкость устройства.
• Громкость микрофона.
• Оверлей для затемнения экрана.
• Выбор аудиовыхода и входа микрофона.
• Переключатель HDR с подтверждением на 10 секунд.
• Состояние HDR читается напрямую из Windows (DisplayConfig / Advanced Color), а не из сохранённого состояния плагина.")
            },
            ["Shortcuts"] = new Dictionary<string, PluginText>(StringComparer.OrdinalIgnoreCase)
            {
                ["it"] = new PluginText(
                    "I tuoi plugin preferiti, direttamente nel menu rapido.",
                    @"Shortcuts porta i plugin Decky che usi di più nella barra principale del menu rapido. Scegli i pannelli compatibili, assegna un'icona e riordinali come preferisci, senza rimuovere la voce originale da Decky.

## Cosa fa
• Trasforma i pannelli QAM dei plugin Decky caricati in tab indipendenti.
• Mantiene anche l'accesso originale dentro Decky.
• Permette di scegliere l'icona originale o una delle icone Tabler incluse.
• Riordina e rimuove solo le tab create da Shortcuts.
• Conserva le preferenze e ripristina automaticamente i plugin temporaneamente non disponibili.

## Nota
• Usa il registro interno delle tab QAM di Decky, perché non esiste ancora un'API pubblica per creare tab principali indipendenti. Un aggiornamento di Decky potrebbe richiedere un adeguamento del plugin."),
                ["en"] = new PluginText(
                    "Your favourite plugins, directly in the Quick Access Menu.",
                    @"Shortcuts brings the Decky plugins you use most into the main Quick Access Menu tab bar. Choose compatible panels, assign an icon and arrange them as you like without removing their original Decky entries.

## What it does
• Turns loaded Decky plugin QAM panels into independent tabs.
• Keeps the original access point inside Decky.
• Lets you use the original icon or choose from the included Tabler icons.
• Reorders and removes only the tabs created by Shortcuts.
• Saves your preferences and automatically restores temporarily unavailable plugins.

## Note
• It uses Decky's internal QAM tab registry because there is no public API for independent top-level tabs yet. A future Decky update may require an adjustment to the plugin."),
                ["es"] = new PluginText(
                    "Tus plugins favoritos, directamente en el menú de acceso rápido.",
                    @"Shortcuts lleva los plugins de Decky que más usas a la barra principal del menú de acceso rápido. Elige paneles compatibles, asigna un icono y ordénalos a tu gusto sin eliminar su acceso original en Decky.

## Qué hace
• Convierte los paneles QAM de los plugins de Decky cargados en pestañas independientes.
• Mantiene el acceso original dentro de Decky.
• Permite usar el icono original o elegir entre los iconos de Tabler incluidos.
• Reordena y elimina solo las pestañas creadas por Shortcuts.
• Guarda tus preferencias y restaura automáticamente los plugins que no estaban disponibles temporalmente.

## Nota
• Usa el registro interno de pestañas QAM de Decky porque todavía no existe una API pública para crear pestañas principales independientes. Una futura actualización de Decky podría requerir adaptar el plugin."),
                ["fr"] = new PluginText(
                    "Tes plugins préférés, directement dans le menu d'accès rapide.",
                    @"Shortcuts place les plugins Decky que tu utilises le plus dans la barre principale du menu d'accès rapide. Choisis les panneaux compatibles, attribue-leur une icône et organise-les comme tu veux sans retirer leur entrée d'origine dans Decky.

## Ce qu'il fait
• Transforme les panneaux QAM des plugins Decky chargés en onglets indépendants.
• Conserve l'accès d'origine dans Decky.
• Permet d'utiliser l'icône d'origine ou l'une des icônes Tabler incluses.
• Réorganise et supprime uniquement les onglets créés par Shortcuts.
• Enregistre tes préférences et restaure automatiquement les plugins temporairement indisponibles.

## Remarque
• Il utilise le registre interne des onglets QAM de Decky, car il n'existe pas encore d'API publique pour créer des onglets principaux indépendants. Une future mise à jour de Decky pourra demander une adaptation du plugin."),
                ["de"] = new PluginText(
                    "Deine Lieblingsplugins direkt im Schnellzugriffsmenü.",
                    @"Shortcuts bringt deine meistgenutzten Decky-Plugins in die Hauptleiste des Schnellzugriffsmenüs. Wähle kompatible Bereiche aus, weise ihnen ein Symbol zu und ordne sie nach Wunsch, ohne den ursprünglichen Eintrag in Decky zu entfernen.

## Funktionen
• Macht QAM-Bereiche geladener Decky-Plugins zu eigenständigen Tabs.
• Behält den ursprünglichen Zugriff in Decky bei.
• Verwendet wahlweise das Originalsymbol oder eines der enthaltenen Tabler-Symbole.
• Ordnet und entfernt nur Tabs, die von Shortcuts erstellt wurden.
• Speichert deine Auswahl und stellt vorübergehend nicht verfügbare Plugins automatisch wieder her.

## Hinweis
• Das Plugin nutzt Deckys interne QAM-Tab-Verwaltung, da es noch keine öffentliche API für eigenständige Haupt-Tabs gibt. Ein künftiges Decky-Update kann eine Anpassung erfordern."),
                ["pt"] = new PluginText(
                    "Os teus plugins favoritos, diretamente no menu de acesso rápido.",
                    @"O Shortcuts coloca os plugins Decky que mais usas na barra principal do menu de acesso rápido. Escolhe painéis compatíveis, atribui um ícone e organiza-os como preferires sem remover a entrada original do Decky.

## O que faz
• Transforma os painéis QAM dos plugins Decky carregados em separadores independentes.
• Mantém o acesso original dentro do Decky.
• Permite usar o ícone original ou escolher entre os ícones Tabler incluídos.
• Reordena e remove apenas os separadores criados pelo Shortcuts.
• Guarda as preferências e restaura automaticamente plugins temporariamente indisponíveis.

## Nota
• Usa o registo interno de separadores QAM do Decky porque ainda não existe uma API pública para criar separadores principais independentes. Uma futura atualização do Decky poderá exigir uma adaptação do plugin."),
                ["uk"] = new PluginText(
                    "Улюблені плагіни безпосередньо в меню швидкого доступу.",
                    @"Shortcuts переносить плагіни Decky, якими ти користуєшся найчастіше, на головну панель меню швидкого доступу. Обирай сумісні панелі, призначай іконки та впорядковуй їх, не прибираючи початкові записи з Decky.

## Можливості
• Перетворює QAM-панелі завантажених плагінів Decky на окремі вкладки.
• Зберігає початковий доступ усередині Decky.
• Дозволяє використовувати оригінальну іконку або одну з вбудованих іконок Tabler.
• Змінює порядок і видаляє лише вкладки, створені Shortcuts.
• Зберігає налаштування й автоматично відновлює тимчасово недоступні плагіни.

## Примітка
• Плагін використовує внутрішній реєстр вкладок QAM у Decky, оскільки публічного API для незалежних вкладок верхнього рівня поки немає. Майбутнє оновлення Decky може вимагати адаптації плагіна."),
                ["zh"] = new PluginText(
                    "把常用插件直接放进快捷菜单。",
                    @"Shortcuts 会把你最常用的 Decky 插件放到快捷菜单的主标签栏中。你可以选择兼容的面板、分配图标并自由排序，同时保留它们在 Decky 中的原始入口。

## 功能
• 将已加载 Decky 插件的 QAM 面板变成独立标签。
• 保留 Decky 中原有的访问入口。
• 可使用插件原始图标，也可选择内置的 Tabler 图标。
• 只调整或移除由 Shortcuts 创建的标签。
• 保存你的设置，并在暂时不可用的插件恢复后自动还原标签。

## 注意
• 由于目前没有用于创建独立顶层标签的公开 API，本插件使用 Decky 的内部 QAM 标签注册机制。未来的 Decky 更新可能需要相应调整插件。"),
                ["ja"] = new PluginText(
                    "お気に入りのプラグインをクイックアクセスメニューに直接。",
                    @"Shortcuts は、よく使う Decky プラグインをクイックアクセスメニューのメインタブバーに追加します。対応パネルを選び、アイコンを設定して好きな順番に並べても、Decky 内の元の項目はそのまま残ります。

## 主な機能
• 読み込み済み Decky プラグインの QAM パネルを独立したタブにします。
• Decky 内の元のアクセス方法を維持します。
• 元のアイコン、または同梱の Tabler アイコンを選べます。
• Shortcuts が作成したタブだけを並べ替え、削除します。
• 設定を保存し、一時的に利用できなかったプラグインも復帰時に自動で戻します。

## 注意
• 独立したトップレベルタブを作る公開 API がまだないため、Decky の内部 QAM タブレジストリを使用しています。今後の Decky 更新に合わせてプラグインの調整が必要になる場合があります。"),
                ["ko"] = new PluginText(
                    "즐겨 쓰는 플러그인을 빠른 액세스 메뉴에서 바로 만나세요.",
                    @"Shortcuts는 자주 사용하는 Decky 플러그인을 빠른 액세스 메뉴의 기본 탭 바에 배치합니다. 호환 패널을 고르고 아이콘과 순서를 정해도 Decky 안의 원래 항목은 그대로 유지됩니다.

## 주요 기능
• 로드된 Decky 플러그인의 QAM 패널을 독립 탭으로 만듭니다.
• Decky 안의 원래 접근 경로를 유지합니다.
• 원래 아이콘이나 포함된 Tabler 아이콘을 선택할 수 있습니다.
• Shortcuts가 만든 탭만 순서를 바꾸거나 제거합니다.
• 설정을 저장하고 일시적으로 사용할 수 없던 플러그인이 돌아오면 자동으로 복원합니다.

## 참고
• 독립적인 최상위 탭을 만드는 공개 API가 아직 없어 Decky의 내부 QAM 탭 레지스트리를 사용합니다. 향후 Decky 업데이트에 맞춰 플러그인 조정이 필요할 수 있습니다."),
                ["hi"] = new PluginText(
                    "आपके पसंदीदा प्लगइन सीधे Quick Access Menu में।",
                    @"Shortcuts आपके सबसे अधिक उपयोग किए जाने वाले Decky प्लगइन को Quick Access Menu की मुख्य tab bar में लाता है। compatible panel चुनें, icon तय करें और उन्हें अपनी पसंद के क्रम में रखें, जबकि Decky में उनकी मूल entry बनी रहती है।

## यह क्या करता है
• लोड किए गए Decky plugin के QAM panel को स्वतंत्र tab में बदलता है।
• Decky के भीतर मूल access को बनाए रखता है।
• मूल icon या शामिल Tabler icons में से किसी एक को चुनने देता है।
• केवल Shortcuts द्वारा बनाई गई tabs को क्रमबद्ध या हटाता है।
• आपकी preferences सहेजता है और अस्थायी रूप से अनुपलब्ध plugins को वापस आने पर अपने आप पुनर्स्थापित करता है।

## नोट
• स्वतंत्र top-level tabs बनाने के लिए अभी कोई public API नहीं है, इसलिए यह Decky के internal QAM tab registry का उपयोग करता है। भविष्य के Decky update के बाद plugin में बदलाव की आवश्यकता हो सकती है।"),
                ["ru"] = new PluginText(
                    "Любимые плагины прямо в меню быстрого доступа.",
                    @"Shortcuts переносит самые нужные плагины Decky на главную панель вкладок меню быстрого доступа. Выбирайте совместимые панели, назначайте значки и меняйте их порядок, не удаляя исходные пункты из Decky.

## Возможности
• Превращает QAM-панели загруженных плагинов Decky в отдельные вкладки.
• Сохраняет исходный доступ внутри Decky.
• Позволяет использовать исходный значок или выбрать один из встроенных значков Tabler.
• Меняет порядок и удаляет только вкладки, созданные Shortcuts.
• Сохраняет настройки и автоматически восстанавливает временно недоступные плагины.

## Примечание
• Плагин использует внутренний реестр вкладок QAM в Decky, поскольку публичного API для независимых вкладок верхнего уровня пока нет. Будущее обновление Decky может потребовать адаптации плагина.")
            },
            ["Playhub-Notifications"] = new Dictionary<string, PluginText>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new PluginText(
                    "Notifications that feel clearer, calmer and more personal.",
                    @"Playhub Notifications replaces Steam's visible popups and notification sounds with animated themes designed for Big Picture, while preserving Steam's native notification history.

## What it does
• Includes Xbox Console, PlayStation, GOG Galaxy, Epic Games Launcher, Nintendo, Android and Playhub themes.
• Customizes achievements, messages, invites, downloads, screenshots, controller, warning, system and community notifications.
• Uses real Steam achievement artwork when available.
• Lets you choose position, duration and volume from 0% to 200%.
• Styles the system volume overlay to match the selected theme.
• Includes previews for every notification type.

## Notes
• Only the visible popup and its sound are replaced; Steam's original history remains available.
• The overlay does not take focus or intercept controller, keyboard or mouse input."),
                ["es"] = new PluginText(
                    "Notificaciones más claras, cuidadas y personales.",
                    @"Playhub Notifications sustituye los avisos visibles y sus sonidos por temas animados para Big Picture, sin alterar el historial nativo de Steam. Incluye siete temas, un indicador de volumen coordinado, artwork real de logros, categorías configurables, posición, duración, volumen de 0% a 200% y vistas previas. El overlay no toma el foco ni intercepta tus controles."),
                ["fr"] = new PluginText(
                    "Des notifications plus claires, soignées et personnelles.",
                    @"Playhub Notifications remplace les fenêtres visibles et leurs sons par des thèmes animés conçus pour Big Picture, sans modifier l'historique natif de Steam. Il propose sept thèmes, un indicateur de volume assorti, les images réelles des succès, des catégories configurables, la position, la durée, un volume de 0 % à 200 % et des aperçus. L'overlay ne prend pas le focus et n'intercepte pas les commandes."),
                ["de"] = new PluginText(
                    "Benachrichtigungen, klarer, ruhiger und persönlicher.",
                    @"Playhub Notifications ersetzt sichtbare Steam-Pop-ups und ihre Töne durch animierte Big-Picture-Themes, ohne den nativen Verlauf zu verändern. Enthalten sind sieben Themes, eine passende Lautstärkeanzeige, echte Achievement-Bilder, konfigurierbare Kategorien, Position, Dauer, 0 bis 200 % Lautstärke und Vorschauen. Das Overlay übernimmt weder Fokus noch Eingabe."),
                ["pt"] = new PluginText(
                    "Notificações mais claras, cuidadas e pessoais.",
                    @"Playhub Notifications substitui os pop-ups visíveis e seus sons por temas animados para o Big Picture, preservando o histórico nativo da Steam. Inclui sete temas, imagens reais de conquistas, categorias configuráveis, posição, duração, volume de 0% a 200% e prévias. O overlay não toma o foco nem intercepta controles."),
                ["uk"] = new PluginText(
                    "Зрозуміліші, охайніші та особистіші сповіщення.",
                    @"Playhub Notifications замінює видимі сповіщення Steam та їхні звуки анімованими темами для Big Picture, зберігаючи рідну історію. Доступні сім тем, справжні зображення досягнень, категорії, позиція, тривалість, гучність 0–200% і попередній перегляд. Оверлей не забирає фокус і не перехоплює керування."),
                ["zh"] = new PluginText(
                    "更清晰、更精致、更个性化的通知。",
                    @"Playhub Notifications 使用专为 Big Picture 设计的动画主题替换 Steam 的可见弹窗和提示音，同时保留原生通知历史。它包含七种主题、真实成就图片、可配置类别、位置、时长、0% 至 200% 音量和预览。叠加层不会抢占焦点或拦截输入。"),
                ["ja"] = new PluginText(
                    "より見やすく、穏やかで、自分らしい通知。",
                    @"Playhub Notifications は Steam の表示ポップアップと通知音を Big Picture 向けのアニメーションテーマに置き換え、標準の通知履歴はそのまま残します。7 種類のテーマ、実際の実績画像、カテゴリ設定、位置、表示時間、0～200% の音量、プレビューに対応します。オーバーレイはフォーカスや入力を奪いません。"),
                ["ko"] = new PluginText(
                    "더 선명하고 차분하며 나다운 알림.",
                    @"Playhub Notifications는 Steam의 표시 팝업과 알림음을 Big Picture용 애니메이션 테마로 바꾸면서 기본 알림 기록은 유지합니다. 7가지 테마, 실제 도전 과제 이미지, 알림 유형 설정, 위치, 시간, 0~200% 볼륨과 미리보기를 제공합니다. 오버레이는 포커스나 입력을 가로채지 않습니다."),
                ["hi"] = new PluginText(
                    "ज़्यादा साफ़, सहज और निजी notifications.",
                    @"Playhub Notifications Steam के दिखाई देने वाले popup और sound को Big Picture के animated themes से बदलता है, जबकि native history सुरक्षित रहती है। इसमें सात themes, असली achievement artwork, categories, position, duration, 0–200% volume और previews हैं। Overlay focus या input नहीं लेता।"),
                ["ru"] = new PluginText(
                    "Более ясные, аккуратные и персональные уведомления.",
                    @"Playhub Notifications заменяет видимые всплывающие уведомления Steam и их звуки анимированными темами для Big Picture, сохраняя штатную историю. Доступны семь тем, настоящие изображения достижений, категории, положение, длительность, громкость 0–200% и предпросмотр. Оверлей не забирает фокус и не перехватывает управление.")
            },
            ["ThemeDeck-Windows"] = new Dictionary<string, PluginText>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new PluginText(
                    "Soundtracks, the way they deserve to be heard.",
                    @"ThemeDeck gives your library a soundtrack: it plays a music track when you open a game's page in Gaming Mode, with optional ambient music for the interface and a dedicated track for the Store. It is a Windows-focused fork and still appears inside Decky as ThemeDeck.

## What it does
• Plays a custom track when you open a game's detail page.
• Lets you choose local audio files or search YouTube with yt-dlp.
• Downloads and assigns tracks from YouTube results, with preview before confirming.
• Supports volume, start offset and loop per game.
• Offers a global/ambient track for non-game pages and a separate Store track.
• Stops the music when a game is launched or running.
• Can automatically assign missing tracks by searching YouTube.

## Notes
• It controls only its own audio: it does not touch Windows system volume.
• The Windows release includes yt-dlp.exe for search and download.
• The interface translates itself based on the Steam/Decky language (11 languages supported)."),
                ["es"] = new PluginText(
                    "Bandas sonoras como merecen sonar.",
                    @"ThemeDeck le da una banda sonora a tu biblioteca: reproduce una pista musical cuando abres la página de un juego en Gaming Mode, con música ambiental opcional para la interfaz y una pista dedicada para la Store. Es un fork pensado para Windows y dentro de Decky conserva el nombre ThemeDeck.

## Qué hace
• Reproduce una pista personalizada al abrir la página de detalle de un juego.
• Te deja elegir archivos de audio locales o buscar en YouTube con yt-dlp.
• Descarga y asigna pistas desde los resultados de YouTube, con vista previa antes de confirmar.
• Soporta volumen, salto inicial y loop por juego.
• Ofrece una pista global/ambiental para páginas que no son de juego y otra pista separada para la Store.
• Detiene la música cuando se inicia un juego o está en ejecución.
• Puede asignar automáticamente las pistas que faltan buscándolas en YouTube.

## Notas
• Solo controla su propio audio: no toca el volumen del sistema de Windows.
• La release de Windows incluye yt-dlp.exe para que funcionen la búsqueda y las descargas.
• La interfaz se traduce sola según el idioma de Steam/Decky (11 idiomas soportados)."),
                ["fr"] = new PluginText(
                    "Des bandes-son comme elles méritent d'être écoutées.",
                    @"ThemeDeck donne une bande-son à ta bibliothèque : il joue une piste musicale quand tu ouvres la page d'un jeu en Gaming Mode, avec une musique d'ambiance optionnelle pour l'interface et une piste dédiée au Store. C'est un fork pensé pour Windows et il reste nommé ThemeDeck dans Decky.

## Ce qu'il fait
• Joue une piste personnalisée à l'ouverture de la page de détail d'un jeu.
• Te laisse choisir des fichiers audio locaux ou chercher sur YouTube avec yt-dlp.
• Télécharge et assigne des pistes depuis les résultats YouTube, avec aperçu avant confirmation.
• Prend en charge le volume, le saut de début et la boucle par jeu.
• Propose une piste globale/ambiance pour les pages hors jeu et une piste séparée pour le Store.
• Arrête la musique quand un jeu est lancé ou en cours d'exécution.
• Peut assigner automatiquement les pistes manquantes en les cherchant sur YouTube.

## Notes
• Il contrôle uniquement son propre audio : il ne touche pas au volume système de Windows.
• La version Windows inclut yt-dlp.exe pour faire fonctionner recherche et téléchargement.
• L'interface se traduit automatiquement selon la langue de Steam/Decky (11 langues prises en charge)."),
                ["de"] = new PluginText(
                    "Soundtracks, so wie sie gehört werden sollten.",
                    @"ThemeDeck gibt deiner Bibliothek einen Soundtrack: Es spielt einen Musiktitel ab, wenn du in Gaming Mode die Seite eines Spiels öffnest, mit optionaler Ambient-Musik für die Oberfläche und einem eigenen Track für den Store. Es ist ein Fork für Windows und heißt in Decky weiterhin ThemeDeck.

## Was es macht
• Spielt einen eigenen Track ab, wenn du die Detailseite eines Spiels öffnest.
• Lässt dich lokale Audiodateien wählen oder YouTube mit yt-dlp durchsuchen.
• Lädt Tracks aus YouTube-Ergebnissen herunter und weist sie zu, mit Vorschau vor dem Bestätigen.
• Unterstützt Lautstärke, Startversatz und Loop pro Spiel.
• Bietet einen globalen/Ambient-Track für Nicht-Spielseiten und einen separaten Store-Track.
• Stoppt die Musik, wenn ein Spiel gestartet wird oder läuft.
• Kann fehlende Tracks automatisch über YouTube suchen und zuweisen.

## Hinweise
• Es steuert nur sein eigenes Audio: Die Windows-Systemlautstärke bleibt unberührt.
• Die Windows-Version enthält yt-dlp.exe für Suche und Download.
• Die Oberfläche übersetzt sich anhand der Steam/Decky-Sprache selbst (11 Sprachen unterstützt)."),
                ["pt"] = new PluginText(
                    "Trilhas sonoras como elas merecem ser ouvidas.",
                    @"ThemeDeck dá uma trilha sonora à sua biblioteca: toca uma música quando você abre a página de um jogo no Gaming Mode, com música ambiente opcional para a interface e uma faixa dedicada para a Loja. É um fork pensado para Windows e, dentro do Decky, continua com o nome ThemeDeck.

## O que faz
• Reproduz uma faixa personalizada ao abrir a página de detalhes de um jogo.
• Permite escolher arquivos de áudio locais ou buscar no YouTube com yt-dlp.
• Baixa e atribui faixas dos resultados do YouTube, com prévia antes de confirmar.
• Suporta volume, salto inicial e loop por jogo.
• Oferece uma faixa global/ambiente para páginas que não são de jogo e uma faixa separada para a Loja.
• Para a música quando um jogo é iniciado ou está em execução.
• Pode atribuir automaticamente faixas ausentes pesquisando no YouTube.

## Notas
• Controla apenas o próprio áudio: não mexe no volume do sistema Windows.
• A versão Windows inclui yt-dlp.exe para busca e download.
• A interface se traduz sozinha de acordo com o idioma do Steam/Decky (11 idiomas suportados)."),
                ["uk"] = new PluginText(
                    "Саундтреки так, як вони мають звучати.",
                    @"ThemeDeck додає саундтрек до твоєї бібліотеки: відтворює музичний трек, коли ти відкриваєш сторінку гри в Gaming Mode, з додатковою фоновою музикою для інтерфейсу та окремим треком для Store. Це форк, створений для Windows, а в Decky він лишається під назвою ThemeDeck.

## Що він робить
• Відтворює власний трек під час відкриття сторінки деталей гри.
• Дає вибрати локальні аудіофайли або шукати на YouTube через yt-dlp.
• Завантажує й призначає треки з результатів YouTube, з попереднім прослуховуванням перед підтвердженням.
• Підтримує гучність, пропуск початку та повтор для окремої гри.
• Має глобальний/фоновий трек для неігрових сторінок і окремий трек для Store.
• Зупиняє музику, коли гра запускається або вже працює.
• Може автоматично призначати відсутні треки, шукаючи їх на YouTube.

## Примітки
• Керує тільки власним аудіо: не змінює системну гучність Windows.
• Windows-реліз містить yt-dlp.exe для пошуку й завантаження.
• Інтерфейс перекладається автоматично за мовою Steam/Decky (підтримується 11 мов)."),
                ["zh"] = new PluginText(
                    "让原声以应有的方式响起。",
                    @"ThemeDeck 为你的库加上音乐：在 Gaming Mode 中打开游戏页面时播放一首音乐，也可为界面启用环境音乐，并为商店设置单独曲目。这是面向 Windows 的分支，在 Decky 中仍显示为 ThemeDeck。

## 功能
• 打开游戏详情页时播放自定义曲目。
• 可选择本地音频文件，或用 yt-dlp 搜索 YouTube。
• 从 YouTube 结果下载并分配曲目，确认前可预览。
• 支持每个游戏的音量、起始跳过和循环。
• 为非游戏页面提供全局/环境曲目，并为商店提供单独曲目。
• 游戏启动或运行时停止音乐。
• 可通过搜索 YouTube 自动分配缺失曲目。

## 说明
• 只控制自己的音频：不会修改 Windows 系统音量。
• Windows 版本包含 yt-dlp.exe，用于搜索和下载。
• 界面会根据 Steam/Decky 语言自动翻译（支持 11 种语言）。"),
                ["ja"] = new PluginText(
                    "サウンドトラックを、ふさわしい形で。",
                    @"ThemeDeck はライブラリにサウンドトラックを与えます。Gaming Mode でゲームのページを開くと音楽を再生し、インターフェイス用の任意のアンビエント音楽と、Store 用の専用トラックも設定できます。Windows 向けに作られたフォークで、Decky 内では ThemeDeck の名前のまま表示されます。

## できること
• ゲーム詳細ページを開いたときにカスタムトラックを再生します。
• ローカル音声ファイルを選ぶか、yt-dlp で YouTube を検索できます。
• YouTube の結果から曲をダウンロードして割り当て、確認前にプレビューできます。
• ゲームごとの音量、開始位置スキップ、ループに対応します。
• ゲーム以外のページ向けのグローバル/アンビエント曲と、Store 用の別曲を用意できます。
• ゲームが起動または実行中になると音楽を停止します。
• YouTube 検索で不足している曲を自動割り当てできます。

## メモ
• 制御するのは自分の音声だけです。Windows のシステム音量には触れません。
• Windows リリースには検索とダウンロード用に yt-dlp.exe が含まれています。
• インターフェイスは Steam/Decky の言語に合わせて自動翻訳されます（11 言語対応）。"),
                ["ko"] = new PluginText(
                    "사운드트랙을 제맛대로.",
                    @"ThemeDeck은 라이브러리에 사운드트랙을 더합니다. Gaming Mode에서 게임 페이지를 열 때 음악을 재생하고, 인터페이스용 선택형 배경 음악과 Store용 전용 트랙도 제공합니다. Windows를 위해 만든 포크이며 Decky 안에서는 ThemeDeck이라는 이름을 유지합니다.

## 기능
• 게임 상세 페이지를 열 때 사용자 지정 트랙을 재생합니다.
• 로컬 오디오 파일을 선택하거나 yt-dlp로 YouTube를 검색할 수 있습니다.
• YouTube 결과에서 트랙을 내려받아 지정하고, 확인 전에 미리 들을 수 있습니다.
• 게임별 볼륨, 시작 건너뛰기, 반복을 지원합니다.
• 게임이 아닌 페이지용 글로벌/배경 트랙과 Store용 별도 트랙을 제공합니다.
• 게임이 실행되거나 실행 중이면 음악을 멈춥니다.
• YouTube에서 검색해 빠진 트랙을 자동으로 지정할 수 있습니다.

## 참고
• 자체 오디오만 제어합니다. Windows 시스템 볼륨은 건드리지 않습니다.
• Windows 릴리스에는 검색과 다운로드를 위한 yt-dlp.exe가 포함됩니다.
• 인터페이스는 Steam/Decky 언어에 맞춰 자동 번역됩니다(11개 언어 지원)."),
                ["hi"] = new PluginText(
                    "Soundtracks, जैसे उन्हें सुना जाना चाहिए.",
                    @"ThemeDeck आपकी library को soundtrack देता है: Gaming Mode में किसी game page को खोलते ही music track चलाता है, interface के लिए optional ambient music और Store के लिए अलग track के साथ। यह Windows के लिए बनाया गया fork है और Decky में इसका नाम ThemeDeck ही रहता है।

## यह क्या करता है
• गेम detail page खुलते ही custom track चलाता है।
• Local audio files चुनने या yt-dlp से YouTube search करने देता है।
• YouTube results से tracks download और assign करता है, confirm करने से पहले preview के साथ।
• हर game के लिए volume, start skip और loop सपोर्ट करता है।
• Non-game pages के लिए global/ambient track और Store के लिए अलग track देता है।
• Game launch या running होने पर music रोक देता है।
• YouTube पर खोजकर missing tracks अपने आप assign कर सकता है।

## Notes
• यह केवल अपना audio control करता है: Windows system volume को नहीं छूता।
• Windows release में search और download के लिए yt-dlp.exe शामिल है।
• Interface Steam/Decky की language के आधार पर अपने आप translate होता है (11 languages supported)।"),
                ["ru"] = new PluginText(
                    "Саундтреки так, как они должны звучать.",
                    @"ThemeDeck добавляет саундтрек к твоей библиотеке: воспроизводит музыкальный трек, когда ты открываешь страницу игры в Gaming Mode, с опциональной фоновой музыкой для интерфейса и отдельным треком для Store. Это форк, сделанный для Windows, а внутри Decky он остаётся под названием ThemeDeck.

## Что он делает
• Воспроизводит пользовательский трек при открытии страницы деталей игры.
• Позволяет выбрать локальные аудиофайлы или искать на YouTube через yt-dlp.
• Скачивает и назначает треки из результатов YouTube, с предпрослушиванием перед подтверждением.
• Поддерживает громкость, пропуск начала и цикл для отдельной игры.
• Даёт глобальный/фоновый трек для неигровых страниц и отдельный трек для Store.
• Останавливает музыку, когда игра запускается или уже работает.
• Может автоматически назначать недостающие треки, ища их на YouTube.

## Примечания
• Управляет только собственным звуком: системную громкость Windows не трогает.
• Windows-релиз включает yt-dlp.exe для поиска и скачивания.
• Интерфейс сам переводится по языку Steam/Decky (поддерживается 11 языков).")
            },
            ["TrailerHero"] = new Dictionary<string, PluginText>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new PluginText(
                    "Your game trailers, made for the gamepad.",
                    @"TrailerHero makes Steam Big Picture feel like a console dashboard. When you open a game's page, it keeps the original artwork for three seconds and then fades a muted trailer into the same hero panel, preferring Steam trailers and falling back to YouTube when needed.

## Main controls
• Enabled turns the effect on or off.
• Enable on home plays trailers on the Big Picture library home too.
• Game page logo moves the game logo to the lower-left during the trailer and restores it when you leave.
• Automatic CRT applies a subtle CRT effect to low-resolution trailers.
• Source chooses the automatic, Steam or YouTube mode for each game.
• Quality sets the preferred quality (720p, 1080p or 2160p) for Steam and YouTube.
• Steam video lets you choose any Steam video for the game from a menu, not only the featured trailer.
• Trim start / Trim end save the video's cut points per game.
• Custom YouTube link saves a specific YouTube trailer; without a link, auto-search prefers 4K results and keeps the title match strict.

## Notes
• It was born on and for Windows, though it should also work on Linux.
• It reads and adapts Big Picture interface elements, which Steam updates often: some selectors may need updates over time."),
                ["es"] = new PluginText(
                    "Los trailers de tus juegos, pensados para el mando.",
                    @"TrailerHero hace que Steam Big Picture parezca la pantalla principal de una consola. Cuando abres la página de un juego, mantiene el artwork original durante tres segundos y luego funde un trailer en silencio dentro del mismo panel hero, priorizando trailers de Steam y recurriendo a YouTube cuando hace falta.

## Controles principales
• Enabled activa o desactiva el efecto.
• Enable on home reproduce trailers también en la home de la biblioteca Big Picture.
• Game page logo mueve el logo del juego abajo a la izquierda durante el trailer y lo restaura al salir.
• Automatic CRT aplica un efecto CRT discreto a trailers de baja resolución.
• Source elige para cada juego el modo automático, Steam o YouTube.
• Quality define la calidad preferida (720p, 1080p o 2160p) para Steam y YouTube.
• Steam video te deja elegir cualquier vídeo Steam del juego desde un menú, no solo el trailer destacado.
• Trim start / Trim end guardan el recorte del vídeo para cada juego.
• Custom YouTube link guarda un trailer específico de YouTube; sin enlace, la búsqueda automática prioriza resultados 4K y mantiene una coincidencia estricta del título.

## Notas
• Nació en Windows y para Windows, aunque debería funcionar también en Linux.
• Lee y adapta elementos de la interfaz de Big Picture, que Steam actualiza a menudo: algunos selectores podrían requerir ajustes con el tiempo."),
                ["fr"] = new PluginText(
                    "Les bandes-annonces de tes jeux, pensées pour la manette.",
                    @"TrailerHero donne à Steam Big Picture des airs de tableau de bord de console. Quand tu ouvres la page d'un jeu, il garde l'artwork original pendant trois secondes puis fait apparaître en fondu une bande-annonce muette dans le même panneau hero, en privilégiant les vidéos Steam et en passant à YouTube si besoin.

## Contrôles principaux
• Enabled active ou désactive l'effet.
• Enable on home lit aussi les bandes-annonces sur l'accueil de la bibliothèque Big Picture.
• Game page logo déplace le logo du jeu en bas à gauche pendant la bande-annonce et le restaure quand tu quittes.
• Automatic CRT applique un effet CRT discret aux vidéos basse résolution.
• Source choisit pour chaque jeu le mode automatique, Steam ou YouTube.
• Quality définit la qualité préférée (720p, 1080p ou 2160p) pour Steam et YouTube.
• Steam video te laisse choisir n'importe quelle vidéo Steam du jeu depuis un menu, pas seulement la bande-annonce mise en avant.
• Trim start / Trim end enregistrent les points de coupe de la vidéo pour chaque jeu.
• Custom YouTube link enregistre une bande-annonce YouTube spécifique ; sans lien, la recherche automatique privilégie les résultats 4K et garde une correspondance stricte du titre.

## Notes
• Il est né sur Windows et pour Windows, même s'il devrait aussi fonctionner sur Linux.
• Il lit et adapte les éléments de l'interface Big Picture, que Steam met souvent à jour : certains sélecteurs peuvent nécessiter des ajustements au fil du temps."),
                ["de"] = new PluginText(
                    "Trailer für deine Spiele, gemacht für den Controller.",
                    @"TrailerHero lässt Steam Big Picture wie ein Konsolen-Dashboard wirken. Wenn du die Seite eines Spiels öffnest, bleibt das ursprüngliche Artwork drei Sekunden sichtbar, dann blendet ein stummer Trailer im selben Hero-Bereich ein, bevorzugt von Steam und bei Bedarf von YouTube.

## Hauptsteuerungen
• Enabled schaltet den Effekt ein oder aus.
• Enable on home spielt Trailer auch auf der Big-Picture-Bibliotheksstartseite ab.
• Game page logo verschiebt das Spiellogo während des Trailers nach unten links und stellt es beim Verlassen wieder her.
• Automatic CRT wendet einen dezenten CRT-Effekt auf Trailer mit niedriger Auflösung an.
• Source wählt für jedes Spiel den automatischen, Steam- oder YouTube-Modus.
• Quality legt die bevorzugte Qualität (720p, 1080p oder 2160p) für Steam und YouTube fest.
• Steam video lässt dich jedes Steam-Video des Spiels aus einem Menü wählen, nicht nur den hervorgehobenen Trailer.
• Trim start / Trim end speichern die Schnittpunkte des Videos pro Spiel.
• Custom YouTube link speichert einen bestimmten YouTube-Trailer; ohne Link bevorzugt die automatische Suche 4K-Ergebnisse und achtet streng auf den Titel.

## Hinweise
• Es entstand auf und für Windows, sollte aber auch unter Linux funktionieren.
• Es liest und passt Big-Picture-Oberflächenelemente an, die Steam häufig aktualisiert: Einige Selektoren können mit der Zeit Updates brauchen."),
                ["pt"] = new PluginText(
                    "Os trailers dos seus jogos, no ponto para o controle.",
                    @"TrailerHero faz o Steam Big Picture parecer o painel de um console. Ao abrir a página de um jogo, ele mantém o artwork original por três segundos e depois mistura um trailer sem som no mesmo painel hero, dando preferência aos trailers da Steam e usando YouTube quando necessário.

## Controles principais
• Enabled ativa ou desativa o efeito.
• Enable on home reproduz trailers também na home da biblioteca Big Picture.
• Game page logo move o logo do jogo para o canto inferior esquerdo durante o trailer e restaura ao sair.
• Automatic CRT aplica um efeito CRT discreto a trailers de baixa resolução.
• Source escolhe para cada jogo o modo automático, Steam ou YouTube.
• Quality define a qualidade preferida (720p, 1080p ou 2160p) para Steam e YouTube.
• Steam video permite escolher qualquer vídeo Steam do jogo em um menu, não apenas o trailer em destaque.
• Trim start / Trim end salvam os cortes do vídeo por jogo.
• Custom YouTube link salva um trailer específico do YouTube; sem link, a busca automática prefere resultados 4K e mantém o título bem preciso.

## Notas
• Nasceu no Windows e para o Windows, embora também deva funcionar no Linux.
• Lê e adapta elementos da interface Big Picture, que a Steam atualiza com frequência: alguns seletores podem precisar de ajustes com o tempo."),
                ["uk"] = new PluginText(
                    "Трейлери твоїх ігор, створені для геймпада.",
                    @"TrailerHero робить Steam Big Picture схожим на консольну панель. Коли ти відкриваєш сторінку гри, він тримає оригінальний арт три секунди, а потім плавно показує беззвучний трейлер у тому самому hero-блоці, спершу обираючи трейлери Steam і переходячи на YouTube за потреби.

## Основні елементи керування
• Enabled вмикає або вимикає ефект.
• Enable on home відтворює трейлери також на головній сторінці бібліотеки Big Picture.
• Game page logo переносить логотип гри вниз ліворуч під час трейлера і повертає його після виходу.
• Automatic CRT застосовує стриманий CRT-ефект до трейлерів низької роздільності.
• Source вибирає для кожної гри автоматичний режим, Steam або YouTube.
• Quality задає бажану якість (720p, 1080p або 2160p) для Steam і YouTube.
• Steam video дає вибрати будь-яке Steam-відео гри з меню, не лише головний трейлер.
• Trim start / Trim end зберігають обрізання відео для кожної гри.
• Custom YouTube link зберігає конкретний YouTube-трейлер; без посилання автопошук віддає перевагу 4K і строго звіряє назву.

## Примітки
• Він створений на Windows і для Windows, хоча має працювати й на Linux.
• Він читає й адаптує елементи інтерфейсу Big Picture, які Steam часто оновлює: деякі селектори можуть потребувати оновлень з часом."),
                ["zh"] = new PluginText(
                    "为手柄体验准备的游戏预告片。",
                    @"TrailerHero 让 Steam Big Picture 像主机仪表盘一样。当你打开游戏页面时，它会先保留原始 artwork 三秒，然后在同一个 hero 区域淡入静音预告片，优先使用 Steam 预告片，需要时再切换到 YouTube。

## 主要控制
• Enabled 开启或关闭效果。
• Enable on home 也在 Big Picture 库首页播放预告片。
• Game page logo 在预告片播放时把游戏标志移到左下角，离开页面时恢复。
• Automatic CRT 为低分辨率预告片添加轻微 CRT 效果。
• Source 为每个游戏选择自动、Steam 或 YouTube 模式。
• Quality 为 Steam 和 YouTube 设置首选质量（720p、1080p 或 2160p）。
• Steam video 可从菜单选择该游戏的任意 Steam 视频，不只限于精选预告片。
• Trim start / Trim end 为每个游戏保存视频裁切点。
• Custom YouTube link 保存指定 YouTube 预告片；没有链接时，自动搜索会优先 4K 结果，并严格匹配标题。

## 说明
• 它诞生于 Windows，也为 Windows 而做，不过也应该能在 Linux 上运行。
• 它会读取并适配 Big Picture 界面元素，而 Steam 经常更新这些元素：某些选择器未来可能需要更新。"),
                ["ja"] = new PluginText(
                    "ゲームパッドで楽しむためのトレーラー。",
                    @"TrailerHero は Steam Big Picture をコンソールのダッシュボードのように見せます。ゲームページを開くと、最初の 3 秒は元のアートワークを表示し、その後同じ hero パネルにミュートされたトレーラーをフェードインします。まず Steam トレーラーを優先し、必要に応じて YouTube に切り替えます。

## 主な操作
• Enabled で効果をオン/オフします。
• Enable on home で Big Picture ライブラリのホームでもトレーラーを再生します。
• Game page logo はトレーラー中にゲームロゴを左下へ移動し、離れると元に戻します。
• Automatic CRT は低解像度トレーラーに控えめな CRT 効果を適用します。
• Source はゲームごとに自動、Steam、YouTube モードを選びます。
• Quality は Steam と YouTube の優先品質（720p、1080p、2160p）を設定します。
• Steam video は注目トレーラーだけでなく、ゲームの任意の Steam 動画をメニューから選べます。
• Trim start / Trim end はゲームごとに動画のカット位置を保存します。
• Custom YouTube link は特定の YouTube トレーラーを保存します。リンクがない場合、自動検索は 4K 結果を優先し、タイトル一致を厳密に保ちます。

## メモ
• Windows で、Windows のために生まれましたが、Linux でも動作するはずです。
• Steam が頻繁に更新する Big Picture の UI 要素を読み取って適応するため、一部のセレクターは時間とともに更新が必要になる場合があります。"),
                ["ko"] = new PluginText(
                    "게임패드에 맞춘 게임 트레일러.",
                    @"TrailerHero는 Steam Big Picture를 콘솔 대시보드처럼 보이게 합니다. 게임 페이지를 열면 원래 artwork를 3초 동안 유지한 뒤 같은 hero 영역에 무음 트레일러를 부드럽게 띄웁니다. 먼저 Steam 트레일러를 사용하고, 필요하면 YouTube로 전환합니다.

## 주요 컨트롤
• Enabled로 효과를 켜거나 끕니다.
• Enable on home은 Big Picture 라이브러리 홈에서도 트레일러를 재생합니다.
• Game page logo는 트레일러 중 게임 로고를 왼쪽 아래로 옮기고, 나가면 되돌립니다.
• Automatic CRT는 저해상도 트레일러에 은은한 CRT 효과를 적용합니다.
• Source는 게임마다 자동, Steam, YouTube 모드를 선택합니다.
• Quality는 Steam과 YouTube의 선호 품질(720p, 1080p, 2160p)을 설정합니다.
• Steam video는 대표 트레일러뿐 아니라 게임의 모든 Steam 영상을 메뉴에서 고를 수 있게 합니다.
• Trim start / Trim end는 게임별 영상 자르기 지점을 저장합니다.
• Custom YouTube link는 특정 YouTube 트레일러를 저장합니다. 링크가 없으면 자동 검색은 4K 결과를 우선하고 제목 일치를 엄격하게 유지합니다.

## 참고
• Windows에서, Windows를 위해 만들어졌지만 Linux에서도 동작할 것입니다.
• Steam이 자주 업데이트하는 Big Picture 인터페이스 요소를 읽고 맞추기 때문에, 일부 선택자는 시간이 지나며 업데이트가 필요할 수 있습니다."),
                ["hi"] = new PluginText(
                    "आपके गेम trailers, gamepad के लिए बने.",
                    @"TrailerHero Steam Big Picture को console dashboard जैसा बना देता है। जब आप किसी game page को खोलते हैं, यह original artwork को तीन सेकंड तक रखता है और फिर उसी hero panel में muted trailer fade कर देता है, पहले Steam trailers चुनता है और जरूरत पड़ने पर YouTube पर जाता है।

## Main controls
• Enabled effect को on या off करता है।
• Enable on home Big Picture library home पर भी trailers चलाता है।
• Game page logo trailer के दौरान game logo को नीचे बाईं ओर ले जाता है और बाहर निकलने पर वापस रखता है।
• Automatic CRT low-resolution trailers पर हल्का CRT effect लगाता है।
• Source हर game के लिए automatic, Steam या YouTube mode चुनता है।
• Quality Steam और YouTube के लिए preferred quality (720p, 1080p या 2160p) सेट करता है।
• Steam video menu से game का कोई भी Steam video चुनने देता है, सिर्फ featured trailer नहीं।
• Trim start / Trim end हर game के लिए video cut points save करते हैं।
• Custom YouTube link एक specific YouTube trailer save करता है; link न हो तो auto-search 4K results को प्राथमिकता देता है और title match सख्त रखता है।

## Notes
• यह Windows पर और Windows के लिए बना है, हालांकि Linux पर भी चलना चाहिए।
• यह Big Picture interface elements को पढ़कर adapt करता है, जिन्हें Steam अक्सर update करता है: समय के साथ कुछ selectors को updates चाहिए हो सकते हैं।"),
                ["ru"] = new PluginText(
                    "Трейлеры твоих игр, созданные для геймпада.",
                    @"TrailerHero делает Steam Big Picture похожим на консольную панель. Когда ты открываешь страницу игры, он держит исходный арт три секунды, а затем плавно показывает беззвучный трейлер в той же hero-панели, сначала выбирая трейлеры Steam и переходя к YouTube при необходимости.

## Основные элементы управления
• Enabled включает или выключает эффект.
• Enable on home воспроизводит трейлеры и на главной странице библиотеки Big Picture.
• Game page logo переносит логотип игры вниз влево во время трейлера и возвращает его при выходе.
• Automatic CRT применяет лёгкий CRT-эффект к трейлерам низкого разрешения.
• Source выбирает для каждой игры автоматический режим, Steam или YouTube.
• Quality задаёт предпочтительное качество (720p, 1080p или 2160p) для Steam и YouTube.
• Steam video позволяет выбрать любое Steam-видео игры из меню, не только выделенный трейлер.
• Trim start / Trim end сохраняют точки обрезки видео для каждой игры.
• Custom YouTube link сохраняет конкретный YouTube-трейлер; без ссылки автопоиск предпочитает 4K и строго сверяет название.

## Примечания
• Он создан на Windows и для Windows, хотя должен работать и на Linux.
• Он читает и адаптирует элементы интерфейса Big Picture, которые Steam часто обновляет: некоторые селекторы могут со временем потребовать обновлений.")
            },
            ["News"] = new Dictionary<string, PluginText>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new PluginText("The news that matters, gathered in one place.", "News brings RSS and Atom sources to the quick menu and a controller-friendly fullscreen newsstand. Browse categories, search articles and read in a clean layout without an API key."),
                ["es"] = new PluginText("Las noticias que importan, reunidas en un solo lugar.", "News lleva fuentes RSS y Atom al menú rápido y a un quiosco a pantalla completa pensado para el mando. Explora categorías, busca artículos y lee con un diseño limpio, sin API key."),
                ["fr"] = new PluginText("L'actualité qui compte, réunie au même endroit.", "News rassemble les sources RSS et Atom dans le menu rapide et un kiosque plein écran adapté à la manette. Parcours les catégories, recherche des articles et lis sans clé API."),
                ["de"] = new PluginText("Wichtige Nachrichten, an einem Ort gesammelt.", "News bringt RSS- und Atom-Quellen ins Schnellmenü und in einen controllerfreundlichen Vollbild-Kiosk. Durchsuche Kategorien und Artikel in einer klaren Ansicht, ganz ohne API-Schlüssel."),
                ["pt"] = new PluginText("As notícias importantes, reunidas em um só lugar.", "News leva fontes RSS e Atom ao menu rápido e a uma banca em tela cheia feita para controle. Navegue por categorias, pesquise artigos e leia com um layout limpo, sem chave de API."),
                ["uk"] = new PluginText("Важливі новини в одному місці.", "News додає RSS та Atom джерела у швидке меню й повноекранну читальню для контролера. Переглядайте категорії, шукайте статті та читайте без API-ключа."),
                ["zh"] = new PluginText("重要新闻，汇聚一处。", "News 将 RSS 和 Atom 来源带入快捷菜单和适合手柄操作的全屏报刊亭。无需 API 密钥，即可浏览分类、搜索文章并清爽阅读。"),
                ["ja"] = new PluginText("大切なニュースを、ひとつの場所に。", "News は RSS と Atom の情報源をクイックメニューとコントローラー対応の全画面ニューススタンドにまとめます。API キーなしでカテゴリ閲覧、記事検索、快適な読書ができます。"),
                ["ko"] = new PluginText("중요한 뉴스를 한곳에.", "News는 RSS와 Atom 소스를 빠른 메뉴와 컨트롤러용 전체 화면 뉴스 가판대에 모아 줍니다. API 키 없이 카테고리 탐색, 기사 검색, 편안한 읽기가 가능합니다."),
                ["hi"] = new PluginText("ज़रूरी खबरें, एक ही जगह।", "News RSS और Atom sources को quick menu और controller-friendly fullscreen newsstand में लाता है। बिना API key के categories देखें, articles खोजें और साफ़ layout में पढ़ें।"),
                ["ru"] = new PluginText("Важные новости в одном месте.", "News добавляет RSS- и Atom-источники в быстрое меню и полноэкранный киоск для контроллера. Просматривайте категории, ищите статьи и читайте без API-ключа.")
            },
            ["Proton-VPN"] = new Dictionary<string, PluginText>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new PluginText("Your VPN, without leaving Gaming Mode.", "Proton VPN puts the essential controls of the Windows client in the quick menu. Check the real connection state, connect, disconnect and choose a location. The official Proton VPN app for Windows must already be installed and configured."),
                ["es"] = new PluginText("Tu VPN, sin salir del Gaming Mode.", "Proton VPN lleva los controles esenciales del cliente de Windows al menú rápido. Comprueba el estado real, conecta, desconecta y elige una ubicación. La app oficial de Proton VPN para Windows debe estar instalada y configurada."),
                ["fr"] = new PluginText("Ton VPN, sans quitter le Gaming Mode.", "Proton VPN place les commandes essentielles du client Windows dans le menu rapide. Vérifie l'état réel, connecte, déconnecte et choisis un emplacement. L'application officielle Proton VPN pour Windows doit déjà être installée et configurée."),
                ["de"] = new PluginText("Dein VPN, ohne den Gaming Mode zu verlassen.", "Proton VPN bringt die wichtigsten Funktionen des Windows-Clients ins Schnellmenü. Prüfe den echten Status, verbinde, trenne und wähle einen Standort. Die offizielle Proton-VPN-App für Windows muss installiert und eingerichtet sein."),
                ["pt"] = new PluginText("Sua VPN, sem sair do Gaming Mode.", "Proton VPN leva os controles essenciais do cliente Windows ao menu rápido. Veja o estado real, conecte, desconecte e escolha uma localização. O app oficial Proton VPN para Windows deve estar instalado e configurado."),
                ["uk"] = new PluginText("Ваша VPN без виходу з Gaming Mode.", "Proton VPN додає основні елементи керування Windows-клієнтом у швидке меню. Перевіряйте стан, підключайтеся, відключайтеся й обирайте розташування. Офіційний застосунок Proton VPN для Windows має бути встановлений і налаштований."),
                ["zh"] = new PluginText("无需离开 Gaming Mode，即可管理 VPN。", "Proton VPN 将 Windows 客户端的核心控制带入快捷菜单。查看真实连接状态、连接、断开并选择位置。需要预先安装并配置官方 Proton VPN Windows 应用。"),
                ["ja"] = new PluginText("Gaming Mode を離れずに VPN を管理。", "Proton VPN は Windows クライアントの基本操作をクイックメニューに追加します。接続状態の確認、接続、切断、ロケーション選択が可能です。公式の Windows 版 Proton VPN を事前にインストールして設定してください。"),
                ["ko"] = new PluginText("Gaming Mode를 떠나지 않고 VPN을 관리하세요.", "Proton VPN은 Windows 클라이언트의 핵심 제어를 빠른 메뉴에 제공합니다. 실제 연결 상태 확인, 연결, 해제, 위치 선택이 가능합니다. 공식 Windows용 Proton VPN 앱이 설치되고 설정되어 있어야 합니다."),
                ["hi"] = new PluginText("Gaming Mode छोड़े बिना अपनी VPN संभालें।", "Proton VPN Windows client के ज़रूरी controls को quick menu में लाता है। वास्तविक connection state देखें, connect या disconnect करें और location चुनें। Windows के लिए official Proton VPN app पहले से installed और configured होना चाहिए।"),
                ["ru"] = new PluginText("Управляйте VPN, не выходя из Gaming Mode.", "Proton VPN переносит основные функции Windows-клиента в быстрое меню. Проверяйте реальное состояние, подключайтесь, отключайтесь и выбирайте локацию. Официальное приложение Proton VPN для Windows должно быть установлено и настроено.")
            },
            ["Weather"] = new Dictionary<string, PluginText>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new PluginText(
                    "Weather, simple and quiet, in the quick menu.",
                    @"Weather is a compact plugin that brings current weather, daily forecasts and hourly forecasts into the quick menu. It is built for Big Picture and controller navigation, with a tight, safe layout that avoids clipped text and overflow.

## What it does
• Current weather, 5-day forecast and next 24 hours.
• Open-Meteo backend, no API key needed.
• Metric or imperial units.
• Dedicated settings view to search by city or coordinates.
• Controller-friendly navigation (up, down, left, right).
• Dark, minimal interface with small animated details.
• Automatic language detection (11 languages supported)."),
                ["es"] = new PluginText(
                    "El tiempo, simple y discreto, en el menú rápido.",
                    @"Weather es un plugin compacto que lleva el tiempo actual, previsiones diarias y previsiones por horas al menú rápido. Está pensado para Big Picture y navegación con mando, con un diseño ajustado y seguro que evita textos cortados y desbordes.

## Qué hace
• Tiempo actual, previsión a 5 días y próximas 24 horas.
• Backend Open-Meteo, sin necesidad de API key.
• Unidades métricas o imperiales.
• Vista de ajustes dedicada para buscar ciudad o coordenadas.
• Navegación cómoda con mando (arriba, abajo, izquierda, derecha).
• Interfaz oscura y minimalista con pequeños detalles animados.
• Detección automática del idioma (11 idiomas soportados)."),
                ["fr"] = new PluginText(
                    "La météo, simple et discrète, dans le menu rapide.",
                    @"Weather est un plugin compact qui apporte météo actuelle, prévisions quotidiennes et prévisions horaires dans le menu rapide. Il est pensé pour Big Picture et la navigation à la manette, avec une mise en page serrée et sûre qui évite les textes coupés et les débordements.

## Ce qu'il fait
• Météo actuelle, prévisions sur 5 jours et prochaines 24 heures.
• Backend Open-Meteo, sans clé API.
• Unités métriques ou impériales.
• Vue de réglages dédiée pour chercher une ville ou des coordonnées.
• Navigation pensée pour la manette (haut, bas, gauche, droite).
• Interface sombre et minimale avec de petits détails animés.
• Détection automatique de la langue (11 langues prises en charge)."),
                ["de"] = new PluginText(
                    "Wetter, schlicht und unaufdringlich, im Schnellmenü.",
                    @"Weather ist ein kompaktes Plugin, das aktuelles Wetter, Tagesvorhersagen und stündliche Vorhersagen ins Schnellmenü bringt. Es ist für Big Picture und Controller-Navigation gebaut, mit einem engen, sicheren Layout, das abgeschnittenen Text und Überläufe vermeidet.

## Was es macht
• Aktuelles Wetter, 5-Tage-Vorhersage und die nächsten 24 Stunden.
• Open-Meteo-Backend, kein API-Schlüssel nötig.
• Metrische oder imperiale Einheiten.
• Eigene Einstellungsansicht für Suche nach Stadt oder Koordinaten.
• Controller-freundliche Navigation (hoch, runter, links, rechts).
• Dunkle, minimale Oberfläche mit kleinen animierten Details.
• Automatische Spracherkennung (11 Sprachen unterstützt)."),
                ["pt"] = new PluginText(
                    "O clima, simples e discreto, no menu rápido.",
                    @"Weather é um plugin compacto que leva clima atual, previsão diária e previsão por hora para o menu rápido. Foi pensado para Big Picture e navegação com controle, com um layout justo e seguro que evita texto cortado e overflow.

## O que faz
• Clima atual, previsão de 5 dias e próximas 24 horas.
• Backend Open-Meteo, sem precisar de API key.
• Unidades métricas ou imperiais.
• Tela de configurações dedicada para buscar cidade ou coordenadas.
• Navegação amigável para controle (cima, baixo, esquerda, direita).
• Interface escura e minimalista com pequenos detalhes animados.
• Detecção automática de idioma (11 idiomas suportados)."),
                ["uk"] = new PluginText(
                    "Погода, просто й непомітно, у швидкому меню.",
                    @"Weather - компактний плагін, який додає поточну погоду, щоденний і погодинний прогноз у швидке меню. Він створений для Big Picture і навігації контролером, з щільним і безпечним макетом без обрізаного тексту та переповнення.

## Що він робить
• Поточна погода, прогноз на 5 днів і наступні 24 години.
• Backend Open-Meteo, без API-ключа.
• Метричні або імперські одиниці.
• Окрема сторінка налаштувань для пошуку міста або координат.
• Навігація для контролера (вгору, вниз, ліворуч, праворуч).
• Темний мінімальний інтерфейс із невеликими анімованими деталями.
• Автоматичне визначення мови (підтримується 11 мов)."),
                ["zh"] = new PluginText(
                    "简单、安静地把天气放进快捷菜单。",
                    @"Weather 是一个紧凑插件，把当前天气、每日预报和逐小时预报带进快捷菜单。它为 Big Picture 和手柄导航设计，布局紧凑可靠，避免文字截断和溢出。

## 功能
• 当前天气、5 天预报和未来 24 小时。
• Open-Meteo 后端，不需要 API key。
• 公制或英制单位。
• 专用设置视图，可按城市或坐标搜索。
• 适合手柄的导航（上、下、左、右）。
• 深色极简界面，带少量动态细节。
• 自动检测语言（支持 11 种语言）。"),
                ["ja"] = new PluginText(
                    "天気を、シンプルに静かにクイックメニューへ。",
                    @"Weather は、現在の天気、日別予報、時間別予報をクイックメニューに表示するコンパクトなプラグインです。Big Picture とコントローラー操作向けに作られており、テキスト切れやはみ出しを避ける、きっちり安全なレイアウトを備えています。

## できること
• 現在の天気、5 日予報、今後 24 時間。
• Open-Meteo バックエンド、API key 不要。
• メートル法またはヤード・ポンド法。
• 都市または座標を検索する専用設定ビュー。
• コントローラー向けナビゲーション（上、下、左、右）。
• 小さなアニメーションを添えた暗色でミニマルなインターフェイス。
• 自動言語検出（11 言語対応）。"),
                ["ko"] = new PluginText(
                    "날씨를 간단하고 조용하게 빠른 메뉴에.",
                    @"Weather는 현재 날씨, 일일 예보, 시간별 예보를 빠른 메뉴로 가져오는 작은 플러그인입니다. Big Picture와 컨트롤러 탐색에 맞춰 만들었으며, 텍스트 잘림과 넘침을 피하는 촘촘하고 안전한 레이아웃을 사용합니다.

## 기능
• 현재 날씨, 5일 예보, 다음 24시간.
• Open-Meteo 백엔드, API key 필요 없음.
• 미터법 또는 영미식 단위.
• 도시나 좌표를 검색하는 전용 설정 화면.
• 컨트롤러 친화 탐색(위, 아래, 왼쪽, 오른쪽).
• 작은 애니메이션 디테일이 있는 어둡고 미니멀한 인터페이스.
• 자동 언어 감지(11개 언어 지원)."),
                ["hi"] = new PluginText(
                    "Weather, सरल और शांत, quick menu में.",
                    @"Weather एक compact plugin है जो current weather, daily forecasts और hourly forecasts को quick menu में लाता है। यह Big Picture और controller navigation के लिए बनाया गया है, tight और safe layout के साथ ताकि text कटे नहीं और overflow न हो।

## यह क्या करता है
• Current weather, 5-day forecast और next 24 hours.
• Open-Meteo backend, API key की जरूरत नहीं।
• Metric या imperial units.
• City या coordinates खोजने के लिए dedicated settings view.
• Controller-friendly navigation (up, down, left, right).
• छोटे animated details वाला dark, minimal interface.
• Automatic language detection (11 languages supported)."),
                ["ru"] = new PluginText(
                    "Погода, просто и ненавязчиво, в быстром меню.",
                    @"Weather - компактный плагин, который добавляет текущую погоду, дневной и почасовой прогноз в быстрое меню. Он создан для Big Picture и навигации с контроллера, с плотной и безопасной вёрсткой без обрезанного текста и переполнений.

## Что он делает
• Текущая погода, прогноз на 5 дней и следующие 24 часа.
• Backend Open-Meteo, API key не нужен.
• Метрические или имперские единицы.
• Отдельный экран настроек для поиска города или координат.
• Навигация с контроллера (вверх, вниз, влево, вправо).
• Тёмный минимальный интерфейс с небольшими анимированными деталями.
• Автоматическое определение языка (поддерживается 11 языков).")
            },
            ["Playhub-Surround"] = new Dictionary<string, PluginText>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new PluginText(
                    "Test your speakers, channel by channel.",
                    @"Playhub Surround is a small tool for checking your speaker layout in stereo, 5.1 and 7.1. It shows a living-room-style map and plays synthesized test sounds inspired by classic video games - no copyrighted samples: every sound is generated live with the Web Audio API.

## What it does
• Shows a living-room-style speaker map.
• Supports stereo, 5.1 and 7.1 layouts.
• Plays synthesized test sounds inspired by classic video games.
• Generates every sound live with the Web Audio API, with no protected samples.
• Includes a sequential channel test, volume control and sound presets.
• Supports controller navigation across layout, map, presets, volume and test button.
• Interface translated automatically in the Steam language (11 languages).

## Notes
• Works on Windows; Linux is not tested.
• Multichannel playback depends on Steam/Chromium and the selected output device: if the system exposes only two channels, rear, centre and LFE tests may be downmixed."),
                ["es"] = new PluginText(
                    "Pon a prueba tus altavoces, canal por canal.",
                    @"Playhub Surround es una pequeña herramienta para comprobar la disposición de tus altavoces en estéreo, 5.1 y 7.1. Muestra un mapa con estilo de salón y reproduce sonidos de prueba sintetizados inspirados en videojuegos clásicos - sin muestras protegidas por copyright: cada sonido se genera en vivo con la Web Audio API.

## Qué hace
• Muestra un mapa de altavoces con estilo de salón.
• Soporta configuraciones estéreo, 5.1 y 7.1.
• Reproduce sonidos de prueba sintetizados inspirados en videojuegos clásicos.
• Genera cada sonido en vivo con la Web Audio API, sin muestras protegidas.
• Incluye una prueba secuencial de canales, control de volumen y presets de sonido.
• Navegación con mando por layout, mapa, presets, volumen y botón de prueba.
• Interfaz traducida automáticamente al idioma de Steam (11 idiomas).

## Notas
• Funciona en Windows; Linux no está probado.
• La reproducción multicanal depende de Steam/Chromium y del dispositivo de salida elegido: si el sistema expone solo dos canales, las pruebas traseras, central y LFE pueden mezclarse hacia estéreo."),
                ["fr"] = new PluginText(
                    "Teste tes haut-parleurs, canal par canal.",
                    @"Playhub Surround est un petit outil pour vérifier la disposition de tes haut-parleurs en stéréo, 5.1 et 7.1. Il affiche une carte façon salon et joue des sons de test synthétisés inspirés des jeux vidéo classiques - aucun échantillon protégé : chaque son est généré en direct avec la Web Audio API.

## Ce qu'il fait
• Affiche une carte des haut-parleurs façon salon.
• Prend en charge les dispositions stéréo, 5.1 et 7.1.
• Joue des sons de test synthétisés inspirés des jeux vidéo classiques.
• Génère chaque son en direct avec la Web Audio API, sans échantillons protégés.
• Inclut un test séquentiel des canaux, le contrôle du volume et des presets sonores.
• Navigation à la manette sur la disposition, la carte, les presets, le volume et le bouton de test.
• Interface traduite automatiquement dans la langue de Steam (11 langues).

## Notes
• Fonctionne sur Windows ; Linux n'est pas testé.
• La lecture multicanal dépend de Steam/Chromium et du périphérique de sortie choisi : si le système n'expose que deux canaux, les tests arrière, centre et LFE peuvent être mixés vers le bas."),
                ["de"] = new PluginText(
                    "Teste deine Lautsprecher, Kanal für Kanal.",
                    @"Playhub Surround ist ein kleines Werkzeug, um deine Lautsprecheranordnung in Stereo, 5.1 und 7.1 zu prüfen. Es zeigt eine Wohnzimmer-Karte und spielt synthetisierte Testklänge, inspiriert von klassischen Videospielen - keine urheberrechtlich geschützten Samples: Jeder Klang wird live mit der Web Audio API erzeugt.

## Was es macht
• Zeigt eine Lautsprecherkarte im Wohnzimmerstil.
• Unterstützt Stereo-, 5.1- und 7.1-Layouts.
• Spielt synthetisierte Testklänge, inspiriert von klassischen Videospielen.
• Erzeugt jeden Klang live mit der Web Audio API, ohne geschützte Samples.
• Enthält einen sequenziellen Kanaltest, Lautstärkeregelung und Klang-Presets.
• Unterstützt Controller-Navigation über Layout, Karte, Presets, Lautstärke und Testtaste.
• Oberfläche automatisch in der Steam-Sprache übersetzt (11 Sprachen).

## Hinweise
• Funktioniert unter Windows; Linux ist nicht getestet.
• Mehrkanal-Wiedergabe hängt von Steam/Chromium und dem gewählten Ausgabegerät ab: Wenn das System nur zwei Kanäle bereitstellt, können hintere, Center- und LFE-Tests heruntergemischt werden."),
                ["pt"] = new PluginText(
                    "Teste seus alto-falantes, canal por canal.",
                    @"Playhub Surround é uma pequena ferramenta para verificar a disposição dos seus alto-falantes em estéreo, 5.1 e 7.1. Mostra um mapa em estilo sala de estar e reproduz sons de teste sintetizados inspirados em videogames clássicos - nenhum sample protegido por copyright: cada som é gerado ao vivo com a Web Audio API.

## O que faz
• Mostra um mapa de alto-falantes em estilo sala de estar.
• Suporta layouts estéreo, 5.1 e 7.1.
• Reproduz sons de teste sintetizados, inspirados em videogames clássicos.
• Gera cada som ao vivo com a Web Audio API, sem samples protegidos.
• Inclui teste sequencial dos canais, controle de volume e presets de som.
• Navegação com controle por layout, mapa, presets, volume e botão de teste.
• Interface traduzida automaticamente no idioma do Steam (11 idiomas).

## Notas
• Funciona no Windows; Linux não foi testado.
• A reprodução multicanal depende do Steam/Chromium e do dispositivo de saída escolhido: se o sistema expõe apenas dois canais, os testes traseiros, central e LFE podem ser mixados para estéreo."),
                ["uk"] = new PluginText(
                    "Перевір колонки, канал за каналом.",
                    @"Playhub Surround - невеликий інструмент для перевірки розташування колонок у stereo, 5.1 і 7.1. Він показує карту в стилі вітальні й відтворює синтезовані тестові звуки, натхненні класичними відеоіграми - без захищених семплів: кожен звук генерується наживо через Web Audio API.

## Що він робить
• Показує карту колонок у стилі вітальні.
• Підтримує схеми stereo, 5.1 і 7.1.
• Відтворює синтезовані тестові звуки, натхненні класичними відеоіграми.
• Генерує кожен звук наживо через Web Audio API, без захищених семплів.
• Має послідовний тест каналів, керування гучністю та пресети звуків.
• Підтримує навігацію контролером по схемі, карті, пресетах, гучності й кнопці тесту.
• Інтерфейс автоматично перекладається мовою Steam (11 мов).

## Примітки
• Працює на Windows; Linux не тестувався.
• Багатоканальне відтворення залежить від Steam/Chromium і вибраного пристрою виводу: якщо система показує лише два канали, тести задніх, центрального й LFE каналів можуть мікшуватися вниз."),
                ["zh"] = new PluginText(
                    "逐个声道测试你的扬声器。",
                    @"Playhub Surround 是一个小工具，用于检查 stereo、5.1 和 7.1 的扬声器布局。它显示客厅风格的地图，并播放受经典电子游戏启发的合成测试音 - 不使用受版权保护的采样：每个声音都通过 Web Audio API 实时生成。

## 功能
• 显示客厅风格的扬声器地图。
• 支持 stereo、5.1 和 7.1 布局。
• 播放受经典电子游戏启发的合成测试音。
• 通过 Web Audio API 实时生成每个声音，不使用受保护采样。
• 包含顺序声道测试、音量控制和声音预设。
• 支持用手柄在布局、地图、预设、音量和测试按钮之间导航。
• 界面会自动使用 Steam 语言翻译（11 种语言）。

## 说明
• 可在 Windows 上运行；Linux 未测试。
• 多声道播放取决于 Steam/Chromium 和所选输出设备：如果系统只暴露两个声道，后置、中置和 LFE 测试可能会被下混。"),
                ["ja"] = new PluginText(
                    "スピーカーを、チャンネルごとにテスト。",
                    @"Playhub Surround は、ステレオ、5.1、7.1 のスピーカー配置を確認する小さなツールです。リビング風のマップを表示し、クラシックゲームに着想を得た合成テスト音を再生します。著作権で保護されたサンプルは使わず、すべての音を Web Audio API でリアルタイム生成します。

## できること
• リビング風のスピーカーマップを表示します。
• ステレオ、5.1、7.1 レイアウトをサポートします。
• クラシックゲーム風の合成テスト音を再生します。
• 保護されたサンプルを使わず、Web Audio API で各音をリアルタイム生成します。
• チャンネルの順次テスト、音量調整、サウンドプリセットを含みます。
• レイアウト、マップ、プリセット、音量、テストボタンをコントローラーで操作できます。
• インターフェイスは Steam の言語に合わせて自動翻訳されます（11 言語）。

## メモ
• Windows で動作します。Linux は未テストです。
• マルチチャンネル再生は Steam/Chromium と選択した出力デバイスに依存します。システムが 2 チャンネルしか公開していない場合、リア、センター、LFE のテストはダウンミックスされることがあります。"),
                ["ko"] = new PluginText(
                    "스피커를 채널별로 테스트합니다.",
                    @"Playhub Surround는 stereo, 5.1, 7.1에서 스피커 배치를 확인하는 작은 도구입니다. 거실 스타일 지도를 보여 주고, 고전 비디오게임에서 영감을 받은 합성 테스트 사운드를 재생합니다. 저작권 보호 샘플은 사용하지 않으며, 모든 소리는 Web Audio API로 실시간 생성됩니다.

## 기능
• 거실 스타일 스피커 지도를 보여 줍니다.
• stereo, 5.1, 7.1 레이아웃을 지원합니다.
• 고전 비디오게임에서 영감을 받은 합성 테스트 사운드를 재생합니다.
• 보호된 샘플 없이 Web Audio API로 모든 소리를 실시간 생성합니다.
• 순차 채널 테스트, 볼륨 조절, 사운드 프리셋을 포함합니다.
• 레이아웃, 지도, 프리셋, 볼륨, 테스트 버튼을 컨트롤러로 탐색할 수 있습니다.
• 인터페이스는 Steam 언어로 자동 번역됩니다(11개 언어).

## 참고
• Windows에서 동작합니다. Linux는 테스트되지 않았습니다.
• 멀티채널 재생은 Steam/Chromium과 선택한 출력 장치에 따라 달라집니다. 시스템이 두 채널만 노출하면 후면, 센터, LFE 테스트가 다운믹스될 수 있습니다."),
                ["hi"] = new PluginText(
                    "अपने speakers को channel by channel जांचें.",
                    @"Playhub Surround stereo, 5.1 और 7.1 में आपके speaker layout को जांचने का छोटा tool है। यह living-room style map दिखाता है और classic video games से प्रेरित synthesized test sounds चलाता है - कोई copyrighted sample नहीं: हर sound Web Audio API से live generate होता है।

## यह क्या करता है
• Living-room style speaker map दिखाता है।
• Stereo, 5.1 और 7.1 layouts सपोर्ट करता है।
• Classic video games से प्रेरित synthesized test sounds चलाता है।
• Protected samples के बिना Web Audio API से हर sound live generate करता है।
• Sequential channel test, volume control और sound presets शामिल करता है।
• Layout, map, presets, volume और test button पर controller navigation सपोर्ट करता है।
• Interface Steam language में अपने आप translated होता है (11 languages).

## Notes
• Windows पर चलता है; Linux test नहीं किया गया।
• Multichannel playback Steam/Chromium और चुने गए output device पर निर्भर है: अगर system केवल दो channels expose करता है, तो rear, centre और LFE tests downmix हो सकते हैं।"),
                ["ru"] = new PluginText(
                    "Проверь колонки, канал за каналом.",
                    @"Playhub Surround - небольшой инструмент для проверки расположения колонок в stereo, 5.1 и 7.1. Он показывает карту в стиле гостиной и воспроизводит синтезированные тестовые звуки, вдохновлённые классическими видеоиграми - без защищённых авторским правом сэмплов: каждый звук генерируется вживую через Web Audio API.

## Что он делает
• Показывает карту колонок в стиле гостиной.
• Поддерживает раскладки stereo, 5.1 и 7.1.
• Воспроизводит синтезированные тестовые звуки, вдохновлённые классическими видеоиграми.
• Генерирует каждый звук вживую через Web Audio API, без защищённых сэмплов.
• Включает последовательный тест каналов, управление громкостью и пресеты звуков.
• Поддерживает навигацию контроллером по раскладке, карте, пресетам, громкости и кнопке теста.
• Интерфейс автоматически переводится на язык Steam (11 языков).

## Примечания
• Работает на Windows; Linux не тестировался.
• Многоканальное воспроизведение зависит от Steam/Chromium и выбранного устройства вывода: если система показывает только два канала, задние, центральный и LFE-тесты могут быть сведены вниз.")
            },
        };

    /// <summary>Short description nella lingua richiesta, con fallback all'italiano intero.</summary>
    public static string LocalizedShortDescription(DeckyPluginInfo plugin, string languageKey)
        => ResolveTranslation(plugin.RepositoryName, languageKey)?.Short is { Length: > 0 } shortText
            ? shortText
            : plugin.ShortDescription;

    /// <summary>Long description nella lingua richiesta, con fallback all'italiano intero.</summary>
    public static string LocalizedLongDescription(DeckyPluginInfo plugin, string languageKey)
        => ResolveTranslation(plugin.RepositoryName, languageKey)?.Long is { Length: > 0 } longText
            ? longText
            : plugin.LongDescription;

    private static PluginText? ResolveTranslation(string repositoryName, string languageKey)
    {
        if (string.IsNullOrWhiteSpace(repositoryName) ||
            string.IsNullOrWhiteSpace(languageKey))
        {
            return null;
        }

        return DescriptionTranslations.TryGetValue(repositoryName, out var byLanguage)
            && byLanguage.TryGetValue(languageKey, out var text)
                ? text
                : null;
    }

    public sealed record PluginText(string Short, string Long);

    private sealed record GithubRepo(string Name, string HtmlUrl, string UpdatedAt);

    private sealed record ReleaseInfo(string? ZipUrl, string? PageUrl, string? Version, string? Notes, string? PublishedAt);

    private sealed record SemanticVersion(IReadOnlyList<long> Core, IReadOnlyList<string> Prerelease, string Canonical)
    {
        public static readonly SemanticVersion Empty = new(Array.Empty<long>(), Array.Empty<string>(), "");
    }

    private sealed record ReadmeInfo(string Text, string Summary, List<PluginMediaInfo> Media);

    private sealed record InstalledPluginMetadata(
        string Name,
        string Author,
        string Version,
        string Description,
        string? Image,
        string RepositoryUrl,
        string RepositoryName,
        string RepositorySlug,
        string ReleaseAssetName,
        string? ReleaseZipUrl,
        List<string> Aliases,
        List<string> Tags);

    private sealed class ExternalPluginCatalog
    {
        public int SchemaVersion { get; set; }
        public List<ExternalPluginDefinition> Plugins { get; set; } = new();
    }

    private sealed record PluginDefinition(
        string RepositoryName,
        string LocalFolder,
        string DisplayName,
        string Cover,
        string IconGlyph,
        string ShortDescription,
        string LongDescription);
}

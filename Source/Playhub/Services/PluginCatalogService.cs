using Playhub.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Playhub.Services;

public sealed class PluginCatalogService
{
    private const string Owner = "LoZazaMastro";
    private const string InstalledReleaseMarker = ".playhub-release.json";
    private static readonly HttpClient Http = CreateHttpClient();

    private static readonly IReadOnlyList<PluginDefinition> Definitions = new[]
    {
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
• Offre cache flessibili — oraria, giornaliera, settimanale, a sessione o manuale — per limitare le chiamate API.

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
            @"Playhub Surround è un piccolo strumento per verificare la disposizione dei tuoi altoparlanti in stereo, 5.1 e 7.1. Mostra una mappa in stile salotto e riproduce suoni di test sintetizzati ispirati ai videogiochi classici — nessun campione protetto da copyright: ogni suono è generato dal vivo con la Web Audio API.

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
• La riproduzione multicanale dipende da Steam/Chromium e dal dispositivo di uscita scelto: se il sistema espone solo due canali, i test posteriori, centrale e LFE possono essere mixati verso il basso.")
    };

    public async Task<IReadOnlyList<DeckyPluginInfo>> LoadAsync(string pluginRoot, string deckyPluginsPath)
    {
        var repos = await SafeLoadGithubReposAsync();
        var plugins = new List<DeckyPluginInfo>();

        foreach (var definition in Definitions)
        {
            var repo = repos.FirstOrDefault(r => string.Equals(r.Name, definition.RepositoryName, StringComparison.OrdinalIgnoreCase));
            var releaseTask = SafeGetLatestReleaseAsync(definition.RepositoryName);
            var readmeTask = SafeGetReadmeAsync(definition.RepositoryName);
            await Task.WhenAll(releaseTask, readmeTask);

            var release = releaseTask.Result;
            var readme = readmeTask.Result;
            var localFolder = FindLocalFolder(pluginRoot, definition.LocalFolder);
            var installed = FindInstalledFolder(deckyPluginsPath, definition, definition.DisplayName);
            var installedVersion = installed is null ? "" : ReadInstalledVersion(installed, definition.RepositoryName);
            var hasUpdate = HasVersionUpdate(installedVersion, release.Version);
            var changelog = SelectChangelog(
                definition.RepositoryName,
                installed is not null,
                installedVersion,
                hasUpdate,
                release);

            plugins.Add(new DeckyPluginInfo
            {
                Name = definition.DisplayName,
                FolderName = definition.Cover,
                Author = Owner,
                Version = release.Version ?? "",
                InstalledVersion = installedVersion,
                HasUpdate = hasUpdate,
                ShortDescription = definition.ShortDescription,
                LongDescription = definition.LongDescription,
                Readme = string.IsNullOrWhiteSpace(readme.Text) ? definition.LongDescription : readme.Text,
                IconGlyph = definition.IconGlyph,
                Media = readme.Media,
                SourceFolder = localFolder is not null ? FindSourceFolder(localFolder) : "",
                InstallerZip = localFolder is null ? null : FindInstallerZip(localFolder),
                Image = ResolveCover(definition.Cover),
                CoverImage = ResolveCover(definition.Cover),
                RepositoryUrl = repo?.HtmlUrl ?? $"https://github.com/{Owner}/{definition.RepositoryName}",
                RepositoryName = definition.RepositoryName,
                ReleaseZipUrl = release.ZipUrl,
                ReleasePageUrl = release.PageUrl,
                ReleaseNotes = changelog.Notes ?? "",
                ReleaseNotesVersion = changelog.Version ?? "",
                ReleaseNotesPublishedAt = changelog.PublishedAt ?? "",
                ReleasePublishedAt = release.PublishedAt ?? "",
                UpdatedAt = repo?.UpdatedAt ?? "",
                IsInstalled = installed is not null,
                InstalledFolder = installed ?? Path.Combine(deckyPluginsPath, definition.Cover)
            });
        }

        var displayOrder = new[]
        {
            "Playhub-Metadata",
            "ThemeDeck-Windows",
            "Launch-Curtain",
            "TrailerHero",
            "Now-Playing",
            "Playhub-Surround",
            "Quick-Settings",
            "Weather"
        };

        return plugins
            .OrderBy(p =>
            {
                var index = Array.IndexOf(displayOrder, p.RepositoryName);
                return index >= 0 ? index : int.MaxValue;
            })
            .ToList();
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

    private static async Task<ReleaseInfo> SafeGetLatestReleaseAsync(string repoName)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var json = await Http.GetStringAsync($"https://api.github.com/repos/{Owner}/{repoName}/releases/latest", cts.Token);
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
                root.TryGetProperty("body", out var b) ? CleanMarkdown(b.GetString() ?? "") : null,
                root.TryGetProperty("published_at", out var p) ? FormatDate(p.GetString()) : null);
            release = PreserveCachedNotes(repoName, release);
            SaveReleaseCache(repoName, release);
            return release;
        }
        catch
        {
            var atomRelease = await TryGetLatestReleaseFromAtomAsync(repoName);
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
            if (!string.IsNullOrWhiteSpace(latestRelease.Notes))
            {
                SaveInstalledReleaseCache(repoName, installedVersion, latestRelease);
                return latestRelease;
            }
            return LoadInstalledReleaseCache(repoName, installedVersion);
        }

        // Keep showing the changelog of the installed version while a newer
        // release is available. It switches only after that release is installed.
        return LoadInstalledReleaseCache(repoName, installedVersion);
    }

    private static async Task<ReleaseInfo> TryGetLatestReleaseFromAtomAsync(string repoName)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var xml = await Http.GetStringAsync($"https://github.com/{Owner}/{repoName}/releases.atom", cts.Token);
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
        Regex.Match(value ?? "", @"\d+(?:\.\d+)*").Value;

    private static async Task<ReadmeInfo> SafeGetReadmeAsync(string repoName)
    {
        foreach (var branch in new[] { "main", "master" })
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                var url = $"https://raw.githubusercontent.com/{Owner}/{repoName}/{branch}/README.md";
                var markdown = await Http.GetStringAsync(url, cts.Token);
                var media = ExtractMedia(markdown, repoName, branch);
                var text = CleanMarkdown(RemoveMediaMarkdown(markdown));
                return new ReadmeInfo(text, MakeSummary(text), media);
            }
            catch
            {
            }
        }

        return new ReadmeInfo("", "", new List<PluginMediaInfo>());
    }

    private static List<PluginMediaInfo> ExtractMedia(string markdown, string repoName, string branch)
    {
        var media = new List<PluginMediaInfo>();

        // Markdown images: ![alt](url)
        foreach (Match match in Regex.Matches(markdown, @"!\[(?<alt>[^\]]*)\]\((?<url>[^)\s]+)", RegexOptions.IgnoreCase))
        {
            AddMedia(media, match.Groups["url"].Value, match.Groups["alt"].Value, "image", repoName, branch);
        }

        // HTML <img ... src="url" ...>
        foreach (Match match in Regex.Matches(markdown, @"<img\b[^>]*?\bsrc\s*=\s*[""'](?<url>[^""']+)[""']", RegexOptions.IgnoreCase))
        {
            AddMedia(media, match.Groups["url"].Value, "", "image", repoName, branch);
        }

        // HTML <video>/<source ... src="url" ...>
        foreach (Match match in Regex.Matches(markdown, @"<(?:video|source)\b[^>]*?\bsrc\s*=\s*[""'](?<url>[^""']+)[""']", RegexOptions.IgnoreCase))
        {
            AddMedia(media, match.Groups["url"].Value, "", "video", repoName, branch);
        }

        // Bare media URLs that carry a known extension
        foreach (Match match in Regex.Matches(markdown, @"https?://[^\s)>""']+\.(?:png|jpe?g|gif|webp|mp4|webm|mov)", RegexOptions.IgnoreCase))
        {
            var ext = Path.GetExtension(match.Value.Split('?')[0]).ToLowerInvariant();
            var kind = ext is ".mp4" or ".webm" or ".mov" ? "video" : "image";
            AddMedia(media, match.Value, "", kind, repoName, branch);
        }

        return media
            .GroupBy(m => m.Url, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(6)
            .ToList();
    }

    private static void AddMedia(List<PluginMediaInfo> media, string rawUrl, string alt, string kind, string repoName, string branch)
    {
        var url = NormalizeMediaUrl(rawUrl.Trim(), repoName, branch);
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        media.Add(new PluginMediaInfo { Url = url, Kind = kind, Alt = alt });
    }

    private static string NormalizeMediaUrl(string url, string repoName, string branch)
    {
        url = url.Trim('<', '>', '"', '\'');
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            // GitHub asset/CDN URLs (user-attachments, raw, camo) must stay exactly as they are.
            if (url.Contains("user-attachments", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("camo.", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            // Convert normal github.com/blob links to raw.
            return url.Replace("github.com/", "raw.githubusercontent.com/").Replace("/blob/", "/");
        }

        if (url.StartsWith("#", StringComparison.Ordinal))
        {
            return "";
        }

        return $"https://raw.githubusercontent.com/{Owner}/{repoName}/{branch}/{url.TrimStart('/')}";
    }

    private static string RemoveMediaMarkdown(string markdown)
    {
        var withoutImages = Regex.Replace(markdown, @"!\[[^\]]*\]\([^)]+\)", "", RegexOptions.IgnoreCase);
        const string mediaUrlPattern = "https?://[^\\s)>\\\"']+\\.(?:png|jpe?g|gif|webp|mp4|webm|mov)";
        return Regex.Replace(withoutImages, mediaUrlPattern, "", RegexOptions.IgnoreCase);
    }

    private static string CleanMarkdown(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return "";
        }

        var text = markdown.Replace("\r\n", "\n");
        text = Regex.Replace(text, @"```[\s\S]*?```", "", RegexOptions.Multiline);
        text = Regex.Replace(text, @"`([^`]+)`", "$1");
        text = Regex.Replace(text, @"^\s{0,3}#{1,6}\s*", "", RegexOptions.Multiline);
        text = Regex.Replace(text, @"\[(?<text>[^\]]+)\]\([^)]+\)", "${text}");
        text = Regex.Replace(text, @"^\s*[-*+]\s+", "- ", RegexOptions.Multiline);
        text = Regex.Replace(text, @"^\s*\d+\.\s+", "- ", RegexOptions.Multiline);
        text = Regex.Replace(text, @"[*_~]{1,3}", "");
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
                foreach (var key in new[] { "version", "version_number", "tag" })
                {
                    if (doc.RootElement.TryGetProperty(key, out var value) &&
                        value.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(value.GetString()))
                    {
                        return NormalizeManifestVersion(repositoryName, value.GetString()!);
                    }
                }
            }
            catch
            {
            }
        }

        return "";
    }

    private static string NormalizeManifestVersion(string repositoryName, string version)
    {
        // These projects kept an internal package version that differs from
        // the public GitHub release version. Without Playhub's marker, reading
        // package.json therefore produced a permanent false update notice.
        var normalized = NormalizeVersion(version);
        if (string.Equals(repositoryName, "Now-Playing", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(normalized, "0.3.0", StringComparison.OrdinalIgnoreCase))
        {
            return "1.3.0";
        }

        if (string.Equals(repositoryName, "Weather", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(normalized, "1.0.0", StringComparison.OrdinalIgnoreCase))
        {
            return "1.1.0";
        }

        return version;
    }

    private static bool HasVersionUpdate(string installedVersion, string? latestVersion)
    {
        var installed = ExtractVersionNumbers(installedVersion);
        var latest = ExtractVersionNumbers(latestVersion ?? "");
        if (installed.Length == 0 || latest.Length == 0)
        {
            return false;
        }

        // Only an update when the online version is strictly greater (not just different in format).
        return CompareVersions(latest, installed) > 0;
    }

    private static int[] ExtractVersionNumbers(string version)
    {
        var match = System.Text.RegularExpressions.Regex.Match(version ?? "", @"\d+(?:\.\d+)*");
        if (!match.Success)
        {
            return System.Array.Empty<int>();
        }

        return match.Value.Split('.')
            .Select(part => int.TryParse(part, out var n) ? n : 0)
            .ToArray();
    }

    private static int CompareVersions(int[] a, int[] b)
    {
        var length = System.Math.Max(a.Length, b.Length);
        for (var i = 0; i < length; i++)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;
            if (x != y)
            {
                return x.CompareTo(y);
            }
        }

        return 0;
    }

    private static string? FindInstalledFolder(string deckyPluginsPath, PluginDefinition definition, string pluginName)
    {
        if (string.IsNullOrWhiteSpace(deckyPluginsPath) || !Directory.Exists(deckyPluginsPath))
        {
            return null;
        }

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Normalize(pluginName),
            Normalize(definition.DisplayName),
            Normalize(definition.LocalFolder),
            Normalize(definition.RepositoryName),
            Normalize(definition.Cover)
        };

        foreach (var folder in Directory.GetDirectories(deckyPluginsPath))
        {
            if (candidates.Contains(Normalize(Path.GetFileName(folder))))
            {
                return folder;
            }

            var pluginJson = Path.Combine(folder, "plugin.json");
            if (!File.Exists(pluginJson))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(pluginJson));
                var names = new[]
                {
                    doc.RootElement.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "",
                    doc.RootElement.TryGetProperty("display_name", out var displayProp) ? displayProp.GetString() ?? "" : "",
                    doc.RootElement.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? "" : ""
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
    // Descrizioni dei plugin PER LINGUA (da riempire da Codex).
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
                    @"Now Playing — це музичний супутник у консольному стилі: він переносить активну медіасесію Windows у швидке меню Steam, з обкладинкою, назвою та керуванням, доступними з геймпада.

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
                    @"Now Playing — твой музыкальный спутник в консольном стиле: он переносит активную медиасессию Windows в быстрое меню Steam, с обложкой, названием и управлением, всегда доступными с геймпада.

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
• Offers flexible caches — hourly, daily, weekly, per session or manual — to reduce API calls.

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
• Ofrece cachés flexibles — por hora, día, semana, sesión o manuales — para reducir llamadas a la API.

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
• Propose des caches flexibles — horaire, quotidien, hebdomadaire, par session ou manuel — pour limiter les appels API.

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
• Bietet flexible Caches — stündlich, täglich, wöchentlich, pro Sitzung oder manuell — um API-Aufrufe zu reduzieren.

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
• Oferece caches flexíveis — por hora, dia, semana, sessão ou manual — para reduzir chamadas de API.

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
• Пропонує гнучкий кеш — щогодини, щодня, щотижня, за сесію або вручну — щоб обмежити API-виклики.

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
• API calls कम करने के लिए flexible caches देता है — hourly, daily, weekly, per session या manual।

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
• Даёт гибкие кэши — почасовой, ежедневный, еженедельный, за сессию или ручной — чтобы снизить число API-вызовов.

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
                    @"Weather — компактний плагін, який додає поточну погоду, щоденний і погодинний прогноз у швидке меню. Він створений для Big Picture і навігації контролером, з щільним і безпечним макетом без обрізаного тексту та переповнення.

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
                    @"Weather — компактный плагин, который добавляет текущую погоду, дневной и почасовой прогноз в быстрое меню. Он создан для Big Picture и навигации с контроллера, с плотной и безопасной вёрсткой без обрезанного текста и переполнений.

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
                    @"Playhub Surround is a small tool for checking your speaker layout in stereo, 5.1 and 7.1. It shows a living-room-style map and plays synthesized test sounds inspired by classic video games — no copyrighted samples: every sound is generated live with the Web Audio API.

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
                    @"Playhub Surround es una pequeña herramienta para comprobar la disposición de tus altavoces en estéreo, 5.1 y 7.1. Muestra un mapa con estilo de salón y reproduce sonidos de prueba sintetizados inspirados en videojuegos clásicos — sin muestras protegidas por copyright: cada sonido se genera en vivo con la Web Audio API.

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
                    @"Playhub Surround est un petit outil pour vérifier la disposition de tes haut-parleurs en stéréo, 5.1 et 7.1. Il affiche une carte façon salon et joue des sons de test synthétisés inspirés des jeux vidéo classiques — aucun échantillon protégé : chaque son est généré en direct avec la Web Audio API.

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
                    @"Playhub Surround ist ein kleines Werkzeug, um deine Lautsprecheranordnung in Stereo, 5.1 und 7.1 zu prüfen. Es zeigt eine Wohnzimmer-Karte und spielt synthetisierte Testklänge, inspiriert von klassischen Videospielen — keine urheberrechtlich geschützten Samples: Jeder Klang wird live mit der Web Audio API erzeugt.

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
                    @"Playhub Surround é uma pequena ferramenta para verificar a disposição dos seus alto-falantes em estéreo, 5.1 e 7.1. Mostra um mapa em estilo sala de estar e reproduz sons de teste sintetizados inspirados em videogames clássicos — nenhum sample protegido por copyright: cada som é gerado ao vivo com a Web Audio API.

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
                    @"Playhub Surround — невеликий інструмент для перевірки розташування колонок у stereo, 5.1 і 7.1. Він показує карту в стилі вітальні й відтворює синтезовані тестові звуки, натхненні класичними відеоіграми — без захищених семплів: кожен звук генерується наживо через Web Audio API.

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
                    @"Playhub Surround 是一个小工具，用于检查 stereo、5.1 和 7.1 的扬声器布局。它显示客厅风格的地图，并播放受经典电子游戏启发的合成测试音 — 不使用受版权保护的采样：每个声音都通过 Web Audio API 实时生成。

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
                    @"Playhub Surround stereo, 5.1 और 7.1 में आपके speaker layout को जांचने का छोटा tool है। यह living-room style map दिखाता है और classic video games से प्रेरित synthesized test sounds चलाता है — कोई copyrighted sample नहीं: हर sound Web Audio API से live generate होता है।

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
                    @"Playhub Surround — небольшой инструмент для проверки расположения колонок в stereo, 5.1 и 7.1. Он показывает карту в стиле гостиной и воспроизводит синтезированные тестовые звуки, вдохновлённые классическими видеоиграми — без защищённых авторским правом сэмплов: каждый звук генерируется вживую через Web Audio API.

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
            string.IsNullOrWhiteSpace(languageKey) ||
            languageKey == "it")
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

    private sealed record ReadmeInfo(string Text, string Summary, List<PluginMediaInfo> Media);

    private sealed record PluginDefinition(
        string RepositoryName,
        string LocalFolder,
        string DisplayName,
        string Cover,
        string IconGlyph,
        string ShortDescription,
        string LongDescription);
}

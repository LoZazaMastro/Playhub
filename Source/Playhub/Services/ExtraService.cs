using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Playhub.Services;

public sealed class ExtraService
{
    private readonly SteamService _steam = new();

    public string? GetSteamFolder() => _steam.GetSteamFolder();

    /// <summary>Vero quando steam.cfg è presente, cioè quando il blocco è attivo.</summary>
    public bool IsSteamUpdateBlockApplied()
    {
        try
        {
            var steamFolder = _steam.GetSteamFolder();
            return steamFolder is not null && File.Exists(Path.Combine(steamFolder, "steam.cfg"));
        }
        catch { return false; }
    }

    public Task<string> ApplySteamCfgAsync()
    {
        var steamFolder = _steam.GetSteamFolder();
        if (steamFolder is null)
        {
            return Task.FromResult("Non trovo la cartella di Steam.");
        }

        var source = File.Exists(AppPaths.LocalSteamCfg) ? AppPaths.LocalSteamCfg : AppPaths.BundledSteamCfg;
        if (!File.Exists(source))
        {
            return Task.FromResult("Manca un file necessario. Reinstalla Playhub e riprova.");
        }

        Directory.CreateDirectory(AppPaths.BackupsRoot);
        var destination = Path.Combine(steamFolder, "steam.cfg");
        if (File.Exists(destination))
        {
            var backup = Path.Combine(AppPaths.BackupsRoot, $"steam.cfg.{DateTime.Now:yyyyMMdd-HHmmss}.bak");
            File.Copy(destination, backup, overwrite: false);
        }

        File.Copy(source, destination, overwrite: true);
        return Task.FromResult("Aggiornamenti del client di Steam bloccati.");
    }

    public Task<string> RemoveSteamCfgAsync()
    {
        var steamFolder = _steam.GetSteamFolder();
        if (steamFolder is null)
        {
            return Task.FromResult("Non trovo la cartella di Steam.");
        }

        var destination = Path.Combine(steamFolder, "steam.cfg");
        if (File.Exists(destination))
        {
            File.Delete(destination);
            return Task.FromResult("Aggiornamenti del client di Steam riattivati.");
        }

        return Task.FromResult("Gli aggiornamenti del client di Steam erano già attivi.");
    }

    private static string CssThemesDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "homebrew", "themes");

    private static string CssThemeManifest => Path.Combine(AppPaths.AppDataRoot, "playhub-css-themes.txt");

    public Task<string> ApplyCssLoaderProfileAsync(string _)
    {
        var zip = BundledThemesZip;
        if (!File.Exists(zip))
        {
            return Task.FromResult("Manca un file necessario. Reinstalla Playhub e riprova.");
        }

        var themesDir = CssThemesDir;
        Directory.CreateDirectory(themesDir);

        // Annota le cartelle di primo livello aggiunte (temi + Playhub.profile),
        // così "Rimuovi" cancella solo quelle senza toccare gli altri tuoi temi.
        string[] added;
        using (var archive = ZipFile.OpenRead(zip))
        {
            added = archive.Entries
                .Select(e => e.FullName.Replace('\\', '/').TrimStart('/'))
                .Where(p => p.Contains('/'))
                .Select(p => p.Split('/')[0])
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        // Lo zip è già nel formato corretto: estrai direttamente in homebrew\themes.
        ZipFile.ExtractToDirectory(zip, themesDir, overwriteFiles: true);

        try
        {
            Directory.CreateDirectory(AppPaths.AppDataRoot);
            File.WriteAllLines(CssThemeManifest, added);
        }
        catch
        {
        }

        return Task.FromResult("Profili Playhub installati: Playhub e Playhub (Big Image Mode).");
    }

    private static string BundledThemesZip =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "CssLoaderThemes", "themes.zip");

    public Task<string> RemoveCssLoaderProfileAsync()
    {
        try
        {
            if (!File.Exists(CssThemeManifest))
            {
                return Task.FromResult("Il profilo Playhub non è installato.");
            }

            foreach (var name in File.ReadAllLines(CssThemeManifest))
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var dir = Path.Combine(CssThemesDir, name);
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }

            File.Delete(CssThemeManifest);
            return Task.FromResult("Profili Playhub rimossi da CSS Loader.");
        }
        catch (Exception ex)
        {
            Diag.Crash("ExtraService.RemoveCssLoaderProfileAsync", ex);
            return Task.FromResult("Non riesco a rimuovere il profilo Playhub. Riprova.");
        }
    }

    /// <summary>Esito di un backup o di un ripristino: la chiave da tradurre e i numeri.</summary>
    public readonly record struct BackupResult(string Message, string? Detail, bool Ok);

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string newFile, string existingFile, IntPtr attributes);

    /// <summary>
    /// La cartella grid di Steam contiene facilmente decine di migliaia di file e
    /// diversi gigabyte. Copiarla è un'operazione lunga: deve stare fuori dal thread
    /// della UI, dire a che punto è, e non ricopiare ciò che non è cambiato.
    /// </summary>
    /// <param name="destinationRoot">Cartella scelta dall'utente; null = quella di Playhub.</param>
    public Task<BackupResult> BackupSteamArtworkAsync(string? destinationRoot = null,
        IProgress<double>? progress = null, CancellationToken token = default)
        => Task.Run(() => BackupSteamArtwork(destinationRoot, progress, token), token);

    /// <param name="chosen">Cartella scelta dall'utente; null = l'ultimo backup di Playhub.</param>
    public Task<BackupResult> RestoreSteamArtworkAsync(string? chosen = null,
        IProgress<double>? progress = null, CancellationToken token = default)
        => Task.Run(() => RestoreSteamArtwork(chosen, progress, token), token);

    private BackupResult BackupSteamArtwork(string? destinationRoot, IProgress<double>? progress,
        CancellationToken token)
    {
        var users = _steam.GetUserFolders();
        if (users.Count == 0)
        {
            return new BackupResult("Non trovo alcun profilo Steam su questo PC.", null, false);
        }

        var sources = users
            .Select(user => (User: Path.GetFileName(user.TrimEnd(Path.DirectorySeparatorChar)),
                             Grid: Path.Combine(user, "config", "grid")))
            .Where(entry => Directory.Exists(entry.Grid) && entry.User.Length > 0)
            .ToList();
        if (sources.Count == 0)
        {
            return new BackupResult("Non ho trovato artwork Steam da salvare.", null, false);
        }

        // Uno snapshot precedente serve solo se sta nella stessa destinazione: gli hard
        // link non attraversano i volumi, e collegarsi altrove non farebbe risparmiare nulla.
        var destination = string.IsNullOrWhiteSpace(destinationRoot)
            ? AppPaths.ArtworkBackupsRoot
            : destinationRoot!;
        var previous = NewestSnapshotIn(destination) ?? (string.IsNullOrWhiteSpace(destinationRoot)
            ? LatestArtworkBackup()
            : null);
        var root = Path.Combine(destination, "PlayhubSteamArtwork-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        try
        {
            Directory.CreateDirectory(root);
        }
        catch (Exception ex)
        {
            Diag.Crash("ExtraService.BackupSteamArtwork.CreateRoot", ex);
            return new BackupResult("Il backup degli artwork non è riuscito.", ex.Message, false);
        }

        var files = sources
            .SelectMany(entry => Directory.EnumerateFiles(entry.Grid, "*", SearchOption.AllDirectories)
                .Select(file => (entry.User, entry.Grid, File: file)))
            .ToList();
        var total = files.Sum(item => SafeLength(item.File));

        // Senza uno snapshot precedente da cui collegare, servono davvero tutti i byte.
        if (previous is null && !HasRoomFor(root, total))
        {
            TryDelete(root);
            return new BackupResult("Spazio su disco insufficiente per il backup degli artwork.",
                Size(total), false);
        }

        long copied = 0, linked = 0, skipped = 0, done = 0;
        try
        {
            foreach (var (user, grid, file) in files)
            {
                token.ThrowIfCancellationRequested();
                var relative = Path.Combine(user, "grid", Path.GetRelativePath(grid, file));
                var target = Path.Combine(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                // Un file identico a quello dello snapshot precedente non viene ricopiato:
                // sullo stesso volume si collega, e il backup successivo costa secondi
                // invece di gigabyte.
                var reuse = previous is null ? null : Path.Combine(previous, relative);
                if (reuse is not null && SameFile(file, reuse) && CreateHardLink(target, reuse, IntPtr.Zero))
                {
                    linked++;
                }
                else
                {
                    try
                    {
                        File.Copy(file, target, overwrite: true);
                        copied++;
                    }
                    catch (Exception) when (!token.IsCancellationRequested)
                    {
                        skipped++;
                    }
                }

                done++;
                if (progress is not null && (done % 64 == 0 || done == files.Count))
                {
                    progress.Report(files.Count == 0 ? 1 : (double)done / files.Count);
                }
            }
        }
        catch (OperationCanceledException)
        {
            TryDelete(root);
            throw;
        }
        catch (Exception ex)
        {
            // Uno snapshot a metà è peggio di nessuno snapshot: non deve restare in giro
            // a farsi scambiare per un backup valido.
            Diag.Crash("ExtraService.BackupSteamArtwork", ex);
            TryDelete(root);
            return new BackupResult("Il backup degli artwork non è riuscito.",
                IsDiskFull(ex) ? Size(total) : ex.Message, false);
        }

        var lost = skipped > 0 ? $" · {skipped} saltati" : "";
        return new BackupResult("Backup degli artwork creato.",
            $"{copied + linked} file{lost} · {Size(total)} · {root}", true);
    }

    private BackupResult RestoreSteamArtwork(string? chosen, IProgress<double>? progress,
        CancellationToken token)
    {
        // L'utente può indicare lo snapshot vero o la cartella che lo contiene: accettiamo
        // entrambi, altrimenti scegliere la cartella sbagliata di un livello sembra un errore.
        var latest = string.IsNullOrWhiteSpace(chosen)
            ? LatestArtworkBackup()
            : (LooksLikeSnapshot(chosen!) ? chosen : NewestSnapshotIn(chosen!));
        if (latest is null)
        {
            return new BackupResult("Non ci sono backup degli artwork da ripristinare.", null, false);
        }

        var steam = _steam.GetSteamFolder();
        if (steam is null)
        {
            return new BackupResult("Non trovo la cartella di Steam.", null, false);
        }

        var restores = Directory.GetDirectories(latest)
            .Select(backup => (User: Path.GetFileName(backup.TrimEnd(Path.DirectorySeparatorChar)),
                               Grid: Path.Combine(backup, "grid")))
            .Where(entry => Directory.Exists(entry.Grid) && entry.User.Length > 0)
            .ToList();
        if (restores.Count == 0)
        {
            return new BackupResult("Non ci sono backup degli artwork da ripristinare.", null, false);
        }

        // Il ripristino sovrascrive: quello che c'è adesso va salvato prima, altrimenti
        // un ripristino sbagliato è definitivo.
        var safety = BackupSteamArtwork(null, null, token);

        long written = 0;
        var files = restores
            .SelectMany(entry => Directory.EnumerateFiles(entry.Grid, "*", SearchOption.AllDirectories)
                .Select(file => (entry.User, entry.Grid, File: file)))
            .ToList();
        try
        {
            foreach (var (user, grid, file) in files)
            {
                token.ThrowIfCancellationRequested();
                var target = Path.Combine(steam, "userdata", user, "config", "grid",
                    Path.GetRelativePath(grid, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
                written++;
                if (progress is not null && (written % 64 == 0 || written == files.Count))
                {
                    progress.Report(files.Count == 0 ? 1 : (double)written / files.Count);
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Diag.Crash("ExtraService.RestoreLatestSteamArtwork", ex);
            return new BackupResult("Il ripristino degli artwork non è riuscito.", ex.Message, false);
        }

        var note = safety.Ok ? $" · copia di sicurezza: {safety.Detail}" : "";
        return new BackupResult("Artwork di Steam ripristinati.",
            $"{written} file · {Path.GetFileName(latest)}{note}", true);
    }

    // --------------------------- client di Steam --------------------------- //

    // Cosa NON e' il programma: dati dell'utente, giochi, cache e file rigenerabili.
    // E' una lista di esclusione e non di inclusione di proposito: una versione futura
    // di Steam puo' aggiungere cartelle del client che qui non conosciamo, e un backup
    // incompleto e' molto peggio di uno un po' piu' grande.
    private static readonly string[] SteamClientExcludedFolders =
    {
        "userdata",    // salvataggi, configurazioni e artwork: sono dell'utente
        "steamapps",   // i giochi installati
        "config",      // login e cartelle libreria: ripristinarli vecchi fa danni
        "steam",       // cache avatar e icone delle scorciatoie
        "appcache", "depotcache", "logs", "dumps", "music", "workshop", "temp", "backups"
    };
    private static readonly string[] SteamClientExcludedExtensions = { ".old", ".crash", ".log", ".dmp" };
    // steam.cfg e' il blocco aggiornamenti, gestito a parte dal suo interruttore.
    private static readonly string[] SteamClientExcludedRootFiles = { "steam.cfg" };
    private const string SteamClientMarker = "playhub-steam-client.json";

    /// <summary>Vero quando Steam è in esecuzione: i suoi file sono in uso.</summary>
    public static bool IsSteamRunning()
    {
        try
        {
            return System.Diagnostics.Process.GetProcessesByName("steam").Length > 0
                || System.Diagnostics.Process.GetProcessesByName("steamwebhelper").Length > 0;
        }
        catch { return false; }
    }

    /// <summary>Chiude Steam se serve: i suoi file non si copiano mentre e' in uso.</summary>
    private async Task<bool> EnsureSteamClosedAsync(CancellationToken token)
        => !IsSteamRunning() || await _steam.StopSteamAsync(token);

    public async Task<BackupResult> BackupSteamClientAsync(string destinationRoot,
        IProgress<double>? progress = null, CancellationToken token = default)
    {
        if (!await EnsureSteamClosedAsync(token))
        {
            return new BackupResult("Non riesco a chiudere Steam. Chiudilo a mano e riprova.", null, false);
        }
        return await Task.Run(() => BackupSteamClient(destinationRoot, progress, token), token);
    }

    public async Task<BackupResult> RestoreSteamClientAsync(string chosen,
        IProgress<double>? progress = null, CancellationToken token = default)
    {
        if (!await EnsureSteamClosedAsync(token))
        {
            return new BackupResult("Non riesco a chiudere Steam. Chiudilo a mano e riprova.", null, false);
        }

        var result = await Task.Run(() => RestoreSteamClient(chosen, progress, token), token);
        if (!result.Ok) return result;

        // Senza il blocco, al primo avvio Steam si riaggiorna e il ripristino e' carta
        // straccia: si attiva da soli, e' l'unica cosa che rende utile l'operazione.
        try
        {
            await ApplySteamCfgAsync();
            var blocked = IsSteamUpdateBlockApplied();
            return new BackupResult(result.Message,
                result.Detail + (blocked ? " - aggiornamenti di Steam bloccati" : ""), true);
        }
        catch (Exception ex)
        {
            Diag.Crash("ExtraService.RestoreSteamClient.Block", ex);
            return new BackupResult(result.Message,
                result.Detail + " - blocco aggiornamenti non riuscito: attivalo in Impostazioni", true);
        }
    }

    /// <summary>Vero per giunzioni e collegamenti: vanno saltati, non seguiti.</summary>
    private static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch { return true; }  // se non si riesce nemmeno a leggerne gli attributi, non si tocca
    }

    /// <summary>
    /// Percorre una cartella senza seguire le giunzioni e senza fermarsi su ciò che non
    /// riesce a leggere. In steamui esistono davvero dei collegamenti (sounds_custom,
    /// themes_custom, creati dai temi): seguirli porterebbe fuori da Steam, e una voce
    /// illeggibile non deve far fallire un backup di ottomila file.
    /// </summary>
    private static IEnumerable<string> WalkFiles(string root, Func<string, bool> skipFile)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var folder = pending.Pop();
            string[] directories, files;
            try
            {
                directories = Directory.GetDirectories(folder);
                files = Directory.GetFiles(folder);
            }
            catch { continue; }

            foreach (var directory in directories)
            {
                if (!IsReparsePoint(directory)) pending.Push(directory);
            }
            foreach (var file in files)
            {
                if (IsReparsePoint(file) || skipFile(file)) continue;
                yield return file;
            }
        }
    }

    private static IEnumerable<string> SteamClientFiles(string steam)
    {
        bool ExcludedByExtension(string file)
            => SteamClientExcludedExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);

        string[] entries;
        try { entries = Directory.GetFileSystemEntries(steam); }
        catch { yield break; }

        foreach (var entry in entries)
        {
            var name = Path.GetFileName(entry);
            if (Directory.Exists(entry))
            {
                if (SteamClientExcludedFolders.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                if (IsReparsePoint(entry)) continue;
                foreach (var file in WalkFiles(entry, ExcludedByExtension)) yield return file;
            }
            else
            {
                if (SteamClientExcludedRootFiles.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                if (ExcludedByExtension(entry) || IsReparsePoint(entry)) continue;
                yield return entry;
            }
        }
    }

    /// <summary>La versione di steam.exe, per dire all'utente cosa sta salvando.</summary>
    private static string SteamClientVersion(string steam)
    {
        try
        {
            var exe = Path.Combine(steam, "steam.exe");
            if (!File.Exists(exe)) return "";
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe);
            return string.IsNullOrWhiteSpace(info.FileVersion)
                ? File.GetLastWriteTime(exe).ToString("yyyy-MM-dd")
                : info.FileVersion!;
        }
        catch { return ""; }
    }

    private BackupResult BackupSteamClient(string destinationRoot, IProgress<double>? progress,
        CancellationToken token)
    {
        var steam = _steam.GetSteamFolder();
        if (steam is null || !File.Exists(Path.Combine(steam, "steam.exe")))
        {
            return new BackupResult("Non trovo la cartella di Steam.", null, false);
        }
        if (string.IsNullOrWhiteSpace(destinationRoot))
        {
            return new BackupResult("Il backup di Steam non è riuscito.", null, false);
        }

        List<string> files;
        long total;
        try
        {
            files = SteamClientFiles(steam).ToList();
            total = files.Sum(SafeLength);
        }
        catch (Exception ex)
        {
            Diag.Crash("ExtraService.BackupSteamClient.Enumerate", ex);
            return new BackupResult("Il backup di Steam non è riuscito.", ex.Message, false);
        }

        var version = SteamClientVersion(steam);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var root = Path.Combine(destinationRoot, "PlayhubSteamClient-" + stamp);
        if (!HasRoomFor(destinationRoot, total))
        {
            return new BackupResult("Spazio su disco insufficiente per il backup di Steam.", Size(total), false);
        }

        long done = 0, copied = 0, skipped = 0;
        try
        {
            Directory.CreateDirectory(root);
            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var target = Path.Combine(root, Path.GetRelativePath(steam, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target, overwrite: true);
                    copied++;
                }
                catch (Exception ex) when (!IsDiskFull(ex))
                {
                    // Un file in uso o illeggibile non deve buttare via il backup:
                    // si conta e lo si dice alla fine.
                    skipped++;
                }
                done++;
                if (progress is not null && (done % 64 == 0 || done == files.Count))
                {
                    progress.Report(files.Count == 0 ? 1 : (double)done / files.Count);
                }
            }

            if (copied == 0)
            {
                TryDelete(root);
                return new BackupResult("Il backup di Steam non è riuscito.", null, false);
            }

            // Il contrassegno dice cosa c'e' dentro: senza, una cartella di file
            // sciolti non e' distinguibile da un backup valido.
            File.WriteAllText(Path.Combine(root, SteamClientMarker), JsonSerializer.Serialize(new
            {
                kind = "steam-client",
                version,
                createdAt = DateTime.Now.ToString("o"),
                source = steam,
                files = files.Count,
                bytes = total
            }));
        }
        catch (OperationCanceledException)
        {
            TryDelete(root);
            throw;
        }
        catch (Exception ex)
        {
            Diag.Crash("ExtraService.BackupSteamClient", ex);
            TryDelete(root);
            return new BackupResult("Il backup di Steam non è riuscito.",
                IsDiskFull(ex) ? Size(total) : ex.Message, false);
        }

        var label = string.IsNullOrWhiteSpace(version) ? "" : $"Steam {version} · ";
        var missed = skipped > 0 ? $" · {skipped} saltati" : "";
        return new BackupResult("Backup di Steam creato.",
            $"{label}{copied} file{missed} · {Size(total)} · {root}", true);
    }

    /// <summary>Una cartella è un backup del client se ha steam.exe e il contrassegno.</summary>
    public static bool LooksLikeSteamClientBackup(string path)
    {
        try
        {
            return Directory.Exists(path)
                && File.Exists(Path.Combine(path, "steam.exe"))
                && File.Exists(Path.Combine(path, SteamClientMarker));
        }
        catch { return false; }
    }

    private BackupResult RestoreSteamClient(string chosen, IProgress<double>? progress,
        CancellationToken token)
    {
        // Steam viene chiuso dal chiamante prima di arrivare qui; se e' ancora in piedi
        // ci si ferma, invece di sovrascrivere file in uso e lasciare l'installazione a meta'.
        if (IsSteamRunning())
        {
            return new BackupResult("Non riesco a chiudere Steam. Chiudilo a mano e riprova.", null, false);
        }

        var source = LooksLikeSteamClientBackup(chosen)
            ? chosen
            : NewestSteamClientBackupIn(chosen);
        if (source is null)
        {
            return new BackupResult("Questa cartella non contiene un backup di Steam.", null, false);
        }

        var steam = _steam.GetSteamFolder();
        if (steam is null)
        {
            return new BackupResult("Non trovo la cartella di Steam.", null, false);
        }

        var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Where(file => !string.Equals(Path.GetFileName(file), SteamClientMarker, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (files.Count == 0)
        {
            return new BackupResult("Questa cartella non contiene un backup di Steam.", null, false);
        }

        // Rete di protezione: la versione attuale viene messa da parte prima di
        // essere coperta, così un ripristino sbagliato si può annullare.
        var safety = BackupSteamClient(AppPaths.SteamClientBackupsRoot, null, token);
        if (!safety.Ok)
        {
            return new BackupResult("Il ripristino di Steam non è riuscito.", safety.Detail, false);
        }

        long done = 0;
        try
        {
            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                var target = Path.Combine(steam, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                // Si sovrascrive e basta: cancellare i file in più è il modo più
                // rapido per rendere l'installazione irrecuperabile.
                File.Copy(file, target, overwrite: true);
                done++;
                if (progress is not null && (done % 64 == 0 || done == files.Count))
                {
                    progress.Report(files.Count == 0 ? 1 : (double)done / files.Count);
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Diag.Crash("ExtraService.RestoreSteamClient", ex);
            return new BackupResult("Il ripristino di Steam non è riuscito.",
                $"{ex.Message} · copia di sicurezza: {safety.Detail}", false);
        }

        return new BackupResult("Versione di Steam ripristinata.",
            $"{done} file · {Path.GetFileName(source)} · copia di sicurezza: {safety.Detail}", true);
    }

    private static string? NewestSteamClientBackupIn(string root)
    {
        try
        {
            return Directory.Exists(root)
                ? Directory.GetDirectories(root)
                    .Where(LooksLikeSteamClientBackup)
                    .OrderByDescending(Directory.GetCreationTimeUtc)
                    .FirstOrDefault()
                : null;
        }
        catch { return null; }
    }

    /// <summary>Una cartella è uno snapshot se contiene almeno un &lt;utente&gt;/grid.</summary>
    private static bool LooksLikeSnapshot(string path)
    {
        try
        {
            return Directory.Exists(path)
                && Directory.EnumerateDirectories(path)
                    .Any(user => Directory.Exists(Path.Combine(user, "grid")));
        }
        catch { return false; }
    }

    private static string? NewestSnapshotIn(string root)
    {
        try
        {
            return Directory.Exists(root)
                ? Directory.GetDirectories(root)
                    .Where(LooksLikeSnapshot)
                    .OrderByDescending(path => Path.GetFileName(path) ?? "", StringComparer.Ordinal)
                    .FirstOrDefault()
                : null;
        }
        catch { return null; }
    }

    /// <summary>Lo snapshot più recente, dalla cartella attuale o da quella storica.</summary>
    private static string? LatestArtworkBackup()
    {
        var candidates = new[] { AppPaths.ArtworkBackupsRoot, AppPaths.LegacyArtworkBackupsRoot }
            .Where(Directory.Exists)
            .SelectMany(Directory.GetDirectories)
            .Where(LooksLikeSnapshot)
            .ToList();
        // I nomi hanno avuto due forme nel tempo: ordinare per data di creazione è
        // l'unico criterio che resta vero per entrambe.
        return candidates.Count == 0
            ? null
            : candidates.OrderByDescending(Directory.GetCreationTimeUtc).First();
    }

    private static bool SameFile(string source, string candidate)
    {
        try
        {
            var a = new FileInfo(source);
            var b = new FileInfo(candidate);
            return b.Exists && a.Length == b.Length && a.LastWriteTimeUtc == b.LastWriteTimeUtc;
        }
        catch { return false; }
    }

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; } catch { return 0; }
    }

    private static bool HasRoomFor(string path, long bytes)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            // Un margine: riempire il disco fino all'ultimo byte rompe altro.
            return root is null || new DriveInfo(root).AvailableFreeSpace > bytes + (256L * 1024 * 1024);
        }
        catch { return true; }
    }

    private static bool IsDiskFull(Exception ex)
        => ex is IOException && ((ex.HResult & 0xFFFF) is 0x27 or 0x70);

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { }
    }

    private static string Size(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}

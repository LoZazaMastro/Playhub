using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Playhub.Services;

public sealed class ExtraService
{
    private readonly SteamService _steam = new();
    private readonly HttpClient _http = new();

    public string? GetSteamFolder() => _steam.GetSteamFolder();

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
            return Task.FromResult("Non trovo il file steam.cfg sorgente.");
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

    public async Task<string> DownloadCssLoaderProfileAsync(string mediaFireUrl)
    {
        if (string.IsNullOrWhiteSpace(mediaFireUrl))
        {
            mediaFireUrl = "https://www.mediafire.com/file/qml1pw9wve47xir/themes.zip/file";
        }

        Directory.CreateDirectory(AppPaths.DownloadsRoot);
        var target = Path.Combine(AppPaths.DownloadsRoot, "themes.zip");
        var direct = await ResolveMediaFireDirectUrlAsync(mediaFireUrl);
        using var response = await _http.GetAsync(direct);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = File.Create(target);
        await input.CopyToAsync(output);
        return target;
    }

    private static string CssThemesDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "homebrew", "themes");

    private static string CssThemeManifest => Path.Combine(AppPaths.AppDataRoot, "playhub-css-themes.txt");

    public async Task<string> ApplyCssLoaderProfileAsync(string mediaFireUrl)
    {
        // Usa lo zip dei temi INCLUSO nell'app (niente download da MediaFire).
        // Lo zip è già strutturato per homebrew\themes: temi e Playhub.profile
        // alla radice, senza cartelle contenitore.
        var zip = BundledThemesZip;
        if (!File.Exists(zip))
        {
            zip = File.Exists(Path.Combine(AppPaths.DownloadsRoot, "themes.zip"))
                ? Path.Combine(AppPaths.DownloadsRoot, "themes.zip")
                : await DownloadCssLoaderProfileAsync(mediaFireUrl);
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

        return "Profilo Playhub installato in CSS Loader. Le tue altre opzioni restano invariate.";
    }

    private static string BundledThemesZip =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "CssLoaderThemes", "themes.zip");

    public Task<string> RemoveCssLoaderProfileAsync()
    {
        try
        {
            if (!File.Exists(CssThemeManifest))
            {
                return Task.FromResult("Non risulta installato nessun profilo Playhub.");
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
            return Task.FromResult("Profilo Playhub rimosso da CSS Loader.");
        }
        catch (Exception ex)
        {
            return Task.FromResult("Rimozione non riuscita: " + ex.Message);
        }
    }

    public Task<string> BackupSteamArtworkAsync()
    {
        var users = _steam.GetUserFolders();
        if (users.Count == 0)
        {
            return Task.FromResult("Non trovo utenti Steam con cartella userdata.");
        }

        var backupRoot = Path.Combine(AppPaths.BackupsRoot, "SteamArtwork", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        foreach (var user in users)
        {
            var source = Path.Combine(user, "config", "grid");
            if (!Directory.Exists(source))
            {
                continue;
            }

            var target = Path.Combine(backupRoot, Path.GetFileName(user), "grid");
            CopyDirectory(source, target);
        }

        return Directory.Exists(backupRoot)
            ? Task.FromResult("Backup degli artwork creato.")
            : Task.FromResult("Non ho trovato artwork Steam da salvare.");
    }

    public Task<string> RestoreLatestSteamArtworkAsync()
    {
        var root = Path.Combine(AppPaths.BackupsRoot, "SteamArtwork");
        if (!Directory.Exists(root))
        {
            return Task.FromResult("Non ci sono backup artwork Playhub.");
        }

        var latest = Directory.GetDirectories(root).OrderByDescending(Path.GetFileName).FirstOrDefault();
        if (latest is null)
        {
            return Task.FromResult("Non ci sono backup artwork Playhub.");
        }

        var steam = _steam.GetSteamFolder();
        if (steam is null)
        {
            return Task.FromResult("Non trovo la cartella di Steam.");
        }

        foreach (var userBackup in Directory.GetDirectories(latest))
        {
            var grid = Path.Combine(userBackup, "grid");
            if (!Directory.Exists(grid))
            {
                continue;
            }

            var target = Path.Combine(steam, "userdata", Path.GetFileName(userBackup), "config", "grid");
            CopyDirectory(grid, target);
        }

        return Task.FromResult("Artwork di Steam ripristinati.");
    }

    private async Task<string> ResolveMediaFireDirectUrlAsync(string url)
    {
        var html = await _http.GetStringAsync(url);
        var match = Regex.Match(html, "https?://download[^\"']+");
        return match.Success ? match.Value.Replace("\\/", "/") : url;
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

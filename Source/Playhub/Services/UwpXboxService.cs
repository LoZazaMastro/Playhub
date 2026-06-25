using Playhub.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VDFParser.Models;

namespace Playhub.Services;

public sealed record SteamGridArtworkOption(string Url, string PreviewUrl, int Width, int Height);
public sealed record SteamGridGameOption(int Id, string Name, int? ReleaseYear, bool Verified);

/// <summary>
/// Imports UWP / Xbox Game Pass games into Steam.
///
/// This uses the EXACT engine of UWPHook: shortcuts.vdf is read and written with
/// the original VDFParser library (vendored under Vendor/VDFParser), the exported
/// shortcut points at UWPHook.exe with launch options "{aumid} {executable}", and
/// the Steam grid app id is crc32(exe + name) | 0x80000000 — byte-for-byte the
/// same as UWPHook. The previous hand-rolled binary writer produced a malformed
/// file (missing terminator) which is what caused the missing games and the
/// "LoadLibrary failed with error 87" overlay error.
/// </summary>
public sealed class UwpXboxService
{
    // Timeout esplicito: il default di HttpClient è 100s, troppo per il download
    // di una copertina/icona; 30s evita attese lunghe se SteamGridDB non risponde.
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public UwpXboxService()
    {
        _http.Timeout = TimeSpan.FromSeconds(15);
    }

    public async Task<IReadOnlyList<UwpGameEntry>> ScanAsync()
    {
        var script = """
        # System / Store apps that are NOT games. Exporting these to Steam creates
        # a shortcut to a pure UWP app Steam cannot hook into, which throws
        # "LoadLibrary failed with error 87" at launch. The Xbox app
        # (Microsoft.GamingApp) is the main offender.
        $excludedPackages = @(
          'Microsoft.GamingApp','Microsoft.XboxApp','Microsoft.XboxGameOverlay',
          'Microsoft.XboxGamingOverlay','Microsoft.XboxIdentityProvider',
          'Microsoft.XboxSpeechToTextOverlay','Microsoft.Xbox.TCUI',
          'Microsoft.GamingServices','Microsoft.Windows.GamingApp',
          'Microsoft.WindowsStore','Microsoft.StorePurchaseApp',
          'Microsoft.WindowsCalculator','Microsoft.WindowsCamera',
          'Microsoft.WindowsNotepad','Microsoft.Paint','Microsoft.MicrosoftEdge',
          'Microsoft.MicrosoftEdge.Stable','Microsoft.ZuneMusic','Microsoft.ZuneVideo',
          'Microsoft.Windows.Photos','Microsoft.WindowsTerminal','Microsoft.Todos',
          'Microsoft.PowerAutomateDesktop','Microsoft.GetHelp','Microsoft.People',
          'Microsoft.YourPhone','Microsoft.MicrosoftStickyNotes','Microsoft.ScreenSketch'
        )

        $apps = @()
        foreach ($app in Get-AppxPackage) {
          try {
            if ($app.IsFramework -or $app.IsResourcePackage) { continue }
            if ($app.SignatureKind -eq 'System') { continue }
            if ($excludedPackages -contains $app.Name) { continue }
            [xml]$manifest = Get-AppxPackageManifest $app
            foreach ($application in $manifest.Package.Applications.Application) {
              $name = [string]$manifest.Package.Properties.DisplayName
              if ([string]::IsNullOrWhiteSpace($name) -or $name -like '*ms-resource*' -or $name -like '*DisplayName*') { continue }
              $executable = [string]$application.Executable
              if ([string]::IsNullOrWhiteSpace($executable) -or $executable -eq 'GameLaunchHelper.exe') {
                $config = Join-Path $app.InstallLocation 'MicrosoftGame.Config'
                if (Test-Path $config) {
                  [xml]$gameConfig = Get-Content $config
                  $executable = [string]$gameConfig.Game.ExecutableList.Executable.Name
                } else {
                  continue
                }
              }
              if ($executable -is [Object[]]) { $executable = [string]$executable[1] }
              $visual = $application.VisualElements
              $logo = if ($visual -and $visual.Square150x150Logo) { Join-Path $app.InstallLocation ([string]$visual.Square150x150Logo) } else { '' }
              $apps += [pscustomobject]@{
                Name = $name
                Aumid = "$($app.PackageFamilyName)!$($application.Id)"
                Executable = $executable
                Logo = $logo
                PackageFamilyName = $app.PackageFamilyName
              }
            }
          } catch {}
        }
        $apps | Sort-Object Name -Unique | ConvertTo-Json -Depth 4 -Compress
        """;

        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var result = await ProcessService.RunAsync("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}");
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
        {
            throw new InvalidOperationException(result.Error + result.Output);
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var trimmed = result.Output.Trim();
        if (trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            var single = JsonSerializer.Deserialize<UwpGameEntry>(trimmed, options);
            return single is null ? Array.Empty<UwpGameEntry>() : new[] { single };
        }

        var apps = JsonSerializer.Deserialize<List<UwpGameEntry>>(trimmed, options) ?? new List<UwpGameEntry>();
        ApplyKnownAppNames(apps);
        return apps
            .GroupBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(app => app.Name)
            .ToList();
    }

    public void RefreshLibraryState(IEnumerable<UwpGameEntry> games)
    {
        var gameList = games.ToList();
        foreach (var game in gameList)
        {
            game.InSteamLibrary = false;
        }

        var steamFolder = UwpHookSteamManager.GetSteamFolder();
        if (steamFolder is null)
        {
            return;
        }

        foreach (var user in UwpHookSteamManager.GetUsers(steamFolder))
        {
            VDFEntry[] shortcuts;
            try
            {
                shortcuts = UwpHookSteamManager.ReadShortcuts(user);
            }
            catch
            {
                continue;
            }

            foreach (var game in gameList.Where(game => !game.InSteamLibrary))
            {
                var shortcut = shortcuts.FirstOrDefault(entry => MatchesGame(entry, game));
                if (shortcut is null)
                {
                    continue;
                }

                game.InSteamLibrary = true;
                var gridDirectory = Path.Combine(user, "config", "grid");
                var unsignedAppId = unchecked((uint)shortcut.appid);
                var existingCover = FindExistingImage(gridDirectory, unsignedAppId + "p");
                if (!string.IsNullOrWhiteSpace(existingCover))
                {
                    game.SteamGridDbCoverPath = existingCover;
                }
            }
        }
    }

    public async Task PopulateSteamGridDbCoversAsync(IEnumerable<UwpGameEntry> games, string steamGridDbApiKey)
    {
        if (string.IsNullOrWhiteSpace(steamGridDbApiKey))
        {
            return;
        }

        var cacheDirectory = Path.Combine(AppPaths.LocalDataRoot, "cache", "steamgriddb", "covers");
        Directory.CreateDirectory(cacheDirectory);
        using var gate = new SemaphoreSlim(4);
        using var metadataGate = new SemaphoreSlim(4);
        var tasks = games.Select(async game =>
        {
            if (game.SteamGridDbArtworkDisabled)
            {
                return;
            }

            // Xbox / Store manifests often expose package or internal names.
            // Normalize them to SteamGridDB's canonical title before rendering.
            // A manual Refetch preference already has an id and always wins.
            if (!game.IsLocalExecutable && game.SteamGridDbGameId <= 0)
            {
                await metadataGate.WaitAsync();
                try
                {
                    var matches = await SearchSteamGridDbGamesAsync(game.Name, steamGridDbApiKey);
                    var best = matches.FirstOrDefault();
                    if (best is not null)
                    {
                        game.Name = best.Name;
                        game.SteamGridDbGameId = best.Id;
                    }
                }
                catch
                {
                }
                finally
                {
                    metadataGate.Release();
                }
            }

            if (!string.IsNullOrWhiteSpace(game.SteamGridDbCoverPath) && File.Exists(game.SteamGridDbCoverPath))
            {
                return;
            }

            // Identità UNICA per la cache: se l'AUMID è vuoto (giochi locali non
            // impostati correttamente) ripiega su exe/nome, così due giochi non
            // finiscono mai a condividere la stessa cover.
            var coverIdentity = !string.IsNullOrWhiteSpace(game.Aumid) ? game.Aumid
                : !string.IsNullOrWhiteSpace(game.LocalExecutablePath) ? game.LocalExecutablePath
                : !string.IsNullOrWhiteSpace(game.Executable) ? game.Executable
                : game.Name;
            var cacheKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(coverIdentity)))
                .Substring(0, 24)
                .ToLowerInvariant();
            var cached = FindExistingImage(cacheDirectory, cacheKey);
            if (!string.IsNullOrWhiteSpace(cached))
            {
                game.SteamGridDbCoverPath = cached;
                return;
            }

            await gate.WaitAsync();
            try
            {
                var downloaded = await TryDownloadSteamGridDbCoverAsync(game.Name, cacheDirectory, cacheKey, steamGridDbApiKey, game.SteamGridDbGameId);
                if (!string.IsNullOrWhiteSpace(downloaded))
                {
                    game.SteamGridDbCoverPath = downloaded;
                }
            }
            catch
            {
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    public async Task PopulateApplicationIconsAsync(IEnumerable<UwpGameEntry> games)
    {
        var cacheDirectory = Path.Combine(AppPaths.LocalDataRoot, "cache", "application-icons");
        Directory.CreateDirectory(cacheDirectory);
        using var gate = new SemaphoreSlim(4);
        var tasks = games.Select(async game =>
        {
            if (File.Exists(game.Logo))
            {
                return;
            }

            var logoVariant = FindLogoVariant(game.Logo);
            if (!string.IsNullOrWhiteSpace(logoVariant))
            {
                game.Logo = logoVariant;
                return;
            }

            if (!game.IsLocalExecutable || !File.Exists(game.LocalExecutablePath))
            {
                return;
            }

            await gate.WaitAsync();
            try
            {
                var cacheKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                        Encoding.UTF8.GetBytes(game.LocalExecutablePath)))
                    .Substring(0, 24)
                    .ToLowerInvariant();
                var destination = Path.Combine(cacheDirectory, cacheKey + ".png");
                if (!File.Exists(destination))
                {
                    var source = await Windows.Storage.StorageFile.GetFileFromPathAsync(game.LocalExecutablePath);
                    using var thumbnail = await source.GetThumbnailAsync(
                        Windows.Storage.FileProperties.ThumbnailMode.SingleItem,
                        256,
                        Windows.Storage.FileProperties.ThumbnailOptions.UseCurrentScale);
                    if (thumbnail is null || thumbnail.Size == 0)
                    {
                        return;
                    }

                    var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(cacheDirectory);
                    var target = await folder.CreateFileAsync(
                        Path.GetFileName(destination),
                        Windows.Storage.CreationCollisionOption.ReplaceExisting);
                    using var output = await target.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);
                    output.Size = 0;
                    await Windows.Storage.Streams.RandomAccessStream.CopyAsync(thumbnail, output);
                    await output.FlushAsync();
                }

                game.Logo = destination;
            }
            catch
            {
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private static string? FindLogoVariant(string? logoPath)
    {
        if (string.IsNullOrWhiteSpace(logoPath))
        {
            return null;
        }

        try
        {
            var directory = Path.GetDirectoryName(logoPath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return null;
            }

            var stem = Path.GetFileNameWithoutExtension(logoPath);
            var extension = Path.GetExtension(logoPath);
            return Directory.EnumerateFiles(directory, stem + "*" + extension)
                .Where(path => !Path.GetFileName(path).Contains("contrast-", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(path => Path.GetFileName(path).Contains("targetsize-256", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(path => Path.GetFileName(path).Contains("scale-200", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(path => new FileInfo(path).Length)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<SteamGridGameOption>> SearchSteamGridDbGamesAsync(string query, string steamGridDbApiKey)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(steamGridDbApiKey))
        {
            return Array.Empty<SteamGridGameOption>();
        }

        var searchUrl = $"https://www.steamgriddb.com/api/v2/search/autocomplete/{Uri.EscapeDataString(query.Trim())}";
        using var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
        request.Headers.Add("Authorization", $"Bearer {steamGridDbApiKey}");
        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return Array.Empty<SteamGridGameOption>();
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!document.RootElement.TryGetProperty("data", out var data))
        {
            return Array.Empty<SteamGridGameOption>();
        }

        return data.EnumerateArray()
            .Select(item =>
            {
                if (!item.TryGetProperty("id", out var idProperty) ||
                    !idProperty.TryGetInt32(out var id) ||
                    !item.TryGetProperty("name", out var nameProperty))
                {
                    return null;
                }

                var name = nameProperty.GetString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    return null;
                }

                var verified = item.TryGetProperty("verified", out var verifiedProperty) &&
                    verifiedProperty.ValueKind is JsonValueKind.True;
                return new SteamGridGameOption(id, name, ReadReleaseYear(item), verified);
            })
            .Where(option => option is not null)
            .Cast<SteamGridGameOption>()
            .DistinctBy(option => option.Id)
            .Take(40)
            .ToList();
    }

    public async Task<bool> RefreshSteamGridDbCoverAsync(UwpGameEntry game, string steamGridDbApiKey)
    {
        if (game.SteamGridDbArtworkDisabled || string.IsNullOrWhiteSpace(steamGridDbApiKey))
        {
            return false;
        }

        var cacheDirectory = Path.Combine(AppPaths.LocalDataRoot, "cache", "steamgriddb", "covers");
        Directory.CreateDirectory(cacheDirectory);
        var cacheKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(game.Aumid)))
            .Substring(0, 24)
            .ToLowerInvariant();
        foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".webp" })
        {
            var cached = Path.Combine(cacheDirectory, cacheKey + extension);
            try { if (File.Exists(cached)) File.Delete(cached); } catch { }
        }

        game.SteamGridDbCoverPath = "";
        var downloaded = await TryDownloadSteamGridDbCoverAsync(
            game.Name, cacheDirectory, cacheKey, steamGridDbApiKey, game.SteamGridDbGameId);
        if (string.IsNullOrWhiteSpace(downloaded))
        {
            return false;
        }

        game.SteamGridDbCoverPath = downloaded;
        ApplyArtworkToExistingSteamShortcuts(game, "cover", downloaded);
        return true;
    }

    public async Task<IReadOnlyList<SteamGridArtworkOption>> GetSteamGridDbArtworkAsync(
        UwpGameEntry game,
        string artworkType,
        string steamGridDbApiKey)
    {
        if (game.SteamGridDbArtworkDisabled || string.IsNullOrWhiteSpace(steamGridDbApiKey))
        {
            return Array.Empty<SteamGridArtworkOption>();
        }

        var gameId = game.SteamGridDbGameId > 0
            ? game.SteamGridDbGameId
            : await FindSteamGridDbGameIdAsync(game.Name, steamGridDbApiKey);
        if (gameId is null)
        {
            return Array.Empty<SteamGridArtworkOption>();
        }

        game.SteamGridDbGameId = gameId.Value;
        var endpoint = NormalizeArtworkType(artworkType) switch
        {
            "cover" => $"grids/game/{gameId}?dimensions=600x900,342x482,660x930",
            "banner" => $"grids/game/{gameId}?dimensions=460x215,920x430",
            "hero" => $"heroes/game/{gameId}",
            "logo" => $"logos/game/{gameId}",
            "icon" => $"icons/game/{gameId}",
            _ => $"grids/game/{gameId}?dimensions=600x900,342x482,660x930"
        };

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.steamgriddb.com/api/v2/" + endpoint);
        request.Headers.Add("Authorization", $"Bearer {steamGridDbApiKey}");
        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return Array.Empty<SteamGridArtworkOption>();
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!document.RootElement.TryGetProperty("data", out var data))
        {
            return Array.Empty<SteamGridArtworkOption>();
        }

        return data.EnumerateArray()
            .Select(item =>
            {
                var url = item.TryGetProperty("url", out var urlProperty) ? urlProperty.GetString() : null;
                var preview = item.TryGetProperty("thumb", out var thumbProperty) ? thumbProperty.GetString() : null;
                var width = item.TryGetProperty("width", out var widthProperty) && widthProperty.TryGetInt32(out var parsedWidth)
                    ? parsedWidth
                    : 0;
                var height = item.TryGetProperty("height", out var heightProperty) && heightProperty.TryGetInt32(out var parsedHeight)
                    ? parsedHeight
                    : 0;
                return string.IsNullOrWhiteSpace(url)
                    ? null
                    : new SteamGridArtworkOption(url, string.IsNullOrWhiteSpace(preview) ? url : preview!, width, height);
            })
            .Where(option => option is not null)
            .Cast<SteamGridArtworkOption>()
            .DistinctBy(option => option.Url, StringComparer.OrdinalIgnoreCase)
            .Take(30)
            .ToList();
    }

    public async Task<bool> DownloadAndApplySteamGridDbArtworkAsync(
        UwpGameEntry game,
        string artworkType,
        SteamGridArtworkOption artwork)
    {
        var normalizedType = NormalizeArtworkType(artworkType);
        var cacheKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(game.Aumid)))
            .Substring(0, 24)
            .ToLowerInvariant();
        var cacheDirectory = Path.Combine(AppPaths.LocalDataRoot, "cache", "steamgriddb", "selected", cacheKey);
        Directory.CreateDirectory(cacheDirectory);

        var imageUri = new Uri(artwork.Url);
        var extension = NormalizeImageExtension(Path.GetExtension(imageUri.AbsolutePath));
        var destination = Path.Combine(cacheDirectory, normalizedType + extension);
        var bytes = await _http.GetByteArrayAsync(imageUri);
        await File.WriteAllBytesAsync(destination, bytes);
        SetSelectedArtworkPath(game, normalizedType, destination);
        return ApplyArtworkToExistingSteamShortcuts(game, normalizedType, destination);
    }

    public async Task<string> ExportSelectedToSteamAsync(IEnumerable<UwpGameEntry> games, string steamGridDbApiKey = "")
    {
        var selected = games.Where(g => g.Selected).ToList();
        if (selected.Count == 0)
        {
            return "Seleziona almeno un gioco da importare.";
        }

        if (selected.Any(game => string.IsNullOrWhiteSpace(game.Name)))
        {
            return "Ogni gioco selezionato deve avere un nome.";
        }

        var steamFolder = UwpHookSteamManager.GetSteamFolder();
        if (steamFolder is null)
        {
            return "Non trovo la cartella di Steam.";
        }

        var users = UwpHookSteamManager.GetUsers(steamFolder);
        if (users.Length == 0)
        {
            return "Non trovo utenti Steam in userdata.";
        }

        string? uwpHookExe = null;
        if (selected.Any(game => !game.IsLocalExecutable))
        {
            uwpHookExe = ResolveUwpHookLauncher();
            if (uwpHookExe is null)
            {
                return "Non trovo il componente UWPHook integrato. Reinstalla Playhub e riprova.";
            }
        }

        var uwpHookDir = uwpHookExe is null ? "" : Path.GetDirectoryName(uwpHookExe) ?? AppContext.BaseDirectory;
        var backupRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Briano", "UWPHook", "backups");
        Directory.CreateDirectory(backupRoot);

        // A cover is normally fetched while rendering the cards. Fetch every
        // other missing category as well before creating the Steam shortcuts,
        // while preserving anything the user selected manually.
        if (!string.IsNullOrWhiteSpace(steamGridDbApiKey))
        {
            // Relinking can run immediately after the Xbox scan, before the
            // asynchronous card-cover loader has converted package/AUMID names
            // into the canonical SteamGridDB title. Resolve that title and id
            // here so every missing artwork category can be found reliably.
            await PopulateSteamGridDbCoversAsync(selected, steamGridDbApiKey);
            await PopulateMissingSteamGridDbArtworkAsync(selected, steamGridDbApiKey);
        }

        var blockedPaths = new List<string>();
        foreach (var user in users)
        {
            var configDir = Path.Combine(user, "config");
            Directory.CreateDirectory(configDir);
            var shortcutsPath = Path.Combine(configDir, "shortcuts.vdf");

            // Back up the existing file before touching it (same as UWPHook).
            if (File.Exists(shortcutsPath))
            {
                TryBackupShortcuts(shortcutsPath, Path.Combine(backupRoot, $"{Path.GetFileName(user)}_{DateTime.Now:yyyyMMddHHmmss}_shortcuts.vdf"));
            }

            VDFEntry[] shortcuts;
            try
            {
                shortcuts = UwpHookSteamManager.ReadShortcuts(user);
            }
            catch (UnauthorizedAccessException)
            {
                blockedPaths.Add(shortcutsPath);
                continue;
            }

            foreach (var game in selected)
            {
                var targetExe = GetTargetExecutable(game, uwpHookExe);
                var targetDirectory = game.IsLocalExecutable
                    ? QuotePath(Path.GetDirectoryName(game.LocalExecutablePath) ?? "")
                    : uwpHookDir;
                var appId = unchecked((int)Crc32.SteamGridAppId(game.Name, targetExe));
                var icon = !string.IsNullOrWhiteSpace(game.SteamGridDbIconPath) && File.Exists(game.SteamGridDbIconPath)
                    ? game.SteamGridDbIconPath
                    : game.IsLocalExecutable ? game.LocalExecutablePath : TryPersistIcon(game);

                var entry = new VDFEntry
                {
                    appid = appId,
                    AppName = game.Name,
                    Exe = targetExe,
                    StartDir = targetDirectory,
                    Icon = icon,
                    ShortcutPath = "",
                    LaunchOptions = game.IsLocalExecutable ? "" : game.Aumid + " " + game.Executable,
                    IsHidden = 0,
                    AllowDesktopConfig = 1,
                    AllowOverlay = 1,
                    OpenVR = 0,
                    Devkit = 0,
                    DevkitGameID = "",
                    LastPlayTime = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Tags = new[] { game.IsLocalExecutable ? "Playhub" : "Xbox" }
                };

                var existingIndex = Array.FindIndex(shortcuts, s =>
                    MatchesGame(s, game) ||
                    (string.Equals(s.AppName, game.Name, StringComparison.OrdinalIgnoreCase) &&
                     PathsEqual(s.Exe, targetExe)));

                if (existingIndex >= 0)
                {
                    entry.Index = shortcuts[existingIndex].Index;
                    shortcuts[existingIndex] = entry;
                }
                else
                {
                    entry.Index = shortcuts.Length;
                    Array.Resize(ref shortcuts, shortcuts.Length + 1);
                    shortcuts[^1] = entry;
                }
            }

            try
            {
                UwpHookSteamManager.WriteShortcuts(shortcuts, shortcutsPath);
            }
            catch (UnauthorizedAccessException)
            {
                blockedPaths.Add(shortcutsPath);
                continue;
            }

            foreach (var game in selected)
            {
                ApplySelectedArtworkForUser(game, user, Crc32.SteamGridAppId(game.Name, GetTargetExecutable(game, uwpHookExe)));
            }
        }

        if (blockedPaths.Count > 0)
        {
            return "Windows ha impedito la scrittura del file shortcuts di Steam. Non dipende dal fatto che Steam sia aperto: è la protezione \"Accesso alle cartelle controllato\" di Sicurezza di Windows che blocca questa app (UWPHook funziona perché è già tra le app consentite). " +
                   "Per risolvere: Sicurezza di Windows → Protezione da virus e minacce → Gestisci protezione ransomware → Accesso alle cartelle controllato → Consenti app tramite Accesso alle cartelle controllato → Aggiungi Playhub.exe. Poi riprova. " +
                   $"(File bloccato: {blockedPaths[0]})";
        }

        return $"Ho importato {selected.Count} giochi in Steam. Ho creato anche un backup degli shortcut. Riavvia Steam per vederli.";
    }

    private static string TryPersistIcon(UwpGameEntry game)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(game.Logo) || !File.Exists(game.Logo))
            {
                return "";
            }

            var iconDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Briano", "UWPHook", "icons");
            Directory.CreateDirectory(iconDir);
            var target = Path.Combine(iconDir, SanitizeFileName(game.Aumid) + Path.GetFileName(game.Logo));
            File.Copy(game.Logo, target, overwrite: true);
            return target;
        }
        catch (UnauthorizedAccessException)
        {
            return "";
        }
        catch (IOException)
        {
            return "";
        }
    }

    private static bool MatchesGame(VDFEntry entry, UwpGameEntry game)
    {
        if (game.IsLocalExecutable)
        {
            return PathsEqual(entry.Exe, game.LocalExecutablePath);
        }

        var launchOptions = entry.LaunchOptions ?? "";
        return string.Equals(launchOptions, game.Aumid, StringComparison.OrdinalIgnoreCase) ||
               launchOptions.StartsWith(game.Aumid + " ", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetTargetExecutable(UwpGameEntry game, string? uwpHookExe)
    {
        if (game.IsLocalExecutable)
        {
            return $"\"{Path.GetFullPath(game.LocalExecutablePath)}\"";
        }

        return uwpHookExe ?? "";
    }

    private static string QuotePath(string path) =>
        string.IsNullOrWhiteSpace(path) ? "" : $"\"{Path.GetFullPath(path)}\"";

    private static bool PathsEqual(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(first.Trim().Trim('"')),
                Path.GetFullPath(second.Trim().Trim('"')),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(first.Trim().Trim('"'), second.Trim().Trim('"'), StringComparison.OrdinalIgnoreCase);
        }
    }

    private async Task<int?> FindSteamGridDbGameIdAsync(string gameName, string apiKey)
    {
        var searchUrl = $"https://www.steamgriddb.com/api/v2/search/autocomplete/{Uri.EscapeDataString(gameName)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
        request.Headers.Add("Authorization", $"Bearer {apiKey}");
        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!document.RootElement.TryGetProperty("data", out var data))
        {
            return null;
        }

        var first = data.EnumerateArray().FirstOrDefault();
        return first.ValueKind != JsonValueKind.Undefined && first.TryGetProperty("id", out var id)
            ? id.GetInt32()
            : null;
    }

    private static int? ReadReleaseYear(JsonElement item)
    {
        if (!item.TryGetProperty("release_date", out var releaseProperty) ||
            releaseProperty.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        long unixTime;
        if (releaseProperty.ValueKind == JsonValueKind.Number && releaseProperty.TryGetInt64(out unixTime))
        {
            try { return DateTimeOffset.FromUnixTimeSeconds(unixTime).Year; } catch { return null; }
        }

        if (releaseProperty.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(releaseProperty.GetString(), out var parsed))
        {
            return parsed.Year;
        }

        return null;
    }

    private static string NormalizeArtworkType(string? artworkType)
    {
        return artworkType?.Trim().ToLowerInvariant() switch
        {
            "banner" => "banner",
            "hero" => "hero",
            "logo" => "logo",
            "icon" => "icon",
            _ => "cover"
        };
    }

    private static string NormalizeImageExtension(string? extension)
    {
        var normalized = extension?.ToLowerInvariant();
        return normalized is ".png" or ".jpg" or ".jpeg" or ".webp" or ".ico" ? normalized : ".png";
    }

    private static void SetSelectedArtworkPath(UwpGameEntry game, string artworkType, string path)
    {
        switch (NormalizeArtworkType(artworkType))
        {
            case "banner": game.SteamGridDbBannerPath = path; break;
            case "hero": game.SteamGridDbHeroPath = path; break;
            case "logo": game.SteamGridDbLogoPath = path; break;
            case "icon": game.SteamGridDbIconPath = path; break;
            default: game.SteamGridDbCoverPath = path; break;
        }
    }

    private static bool ApplyArtworkToExistingSteamShortcuts(UwpGameEntry game, string artworkType, string sourcePath)
    {
        var steamFolder = UwpHookSteamManager.GetSteamFolder();
        if (steamFolder is null)
        {
            return false;
        }

        var applied = false;
        foreach (var user in UwpHookSteamManager.GetUsers(steamFolder))
        {
            VDFEntry[] shortcuts;
            try
            {
                shortcuts = UwpHookSteamManager.ReadShortcuts(user);
            }
            catch
            {
                continue;
            }

            var index = Array.FindIndex(shortcuts, shortcut => MatchesGame(shortcut, game));
            if (index < 0)
            {
                continue;
            }

            try
            {
                if (NormalizeArtworkType(artworkType) == "icon")
                {
                    shortcuts[index].Icon = sourcePath;
                    UwpHookSteamManager.WriteShortcuts(shortcuts, Path.Combine(user, "config", "shortcuts.vdf"));
                }
                else
                {
                    CopyArtworkToGrid(user, unchecked((uint)shortcuts[index].appid), artworkType, sourcePath);
                }

                applied = true;
            }
            catch
            {
            }
        }

        return applied;
    }

    private static void ApplySelectedArtworkForUser(UwpGameEntry game, string userPath, uint appId)
    {
        if (game.SteamGridDbArtworkDisabled)
        {
            return;
        }

        foreach (var selection in new[]
        {
            (Type: "cover", Path: game.SteamGridDbCoverPath),
            (Type: "banner", Path: game.SteamGridDbBannerPath),
            (Type: "hero", Path: game.SteamGridDbHeroPath),
            (Type: "logo", Path: game.SteamGridDbLogoPath)
        })
        {
            if (!string.IsNullOrWhiteSpace(selection.Path) && File.Exists(selection.Path))
            {
                CopyArtworkToGrid(userPath, appId, selection.Type, selection.Path);
            }
        }
    }

    private static void CopyArtworkToGrid(string userPath, uint appId, string artworkType, string sourcePath)
    {
        var gridDirectory = Path.Combine(userPath, "config", "grid");
        Directory.CreateDirectory(gridDirectory);
        var baseName = NormalizeArtworkType(artworkType) switch
        {
            "banner" => appId.ToString(),
            "hero" => appId + "_hero",
            "logo" => appId + "_logo",
            _ => appId + "p"
        };

        foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".webp" })
        {
            var oldPath = Path.Combine(gridDirectory, baseName + extension);
            if (!string.Equals(oldPath, sourcePath, StringComparison.OrdinalIgnoreCase) && File.Exists(oldPath))
            {
                try { File.Delete(oldPath); } catch { }
            }
        }

        var destination = Path.Combine(gridDirectory, baseName + NormalizeImageExtension(Path.GetExtension(sourcePath)));
        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        File.Copy(sourcePath, destination, overwrite: true);
    }

    private static string? FindExistingImage(string directory, string fileNameWithoutExtension)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".webp" })
        {
            var candidate = Path.Combine(directory, fileNameWithoutExtension + extension);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private async Task<string?> TryDownloadSteamGridDbCoverAsync(string gameName, string cacheDirectory, string cacheKey, string apiKey, int preferredGameId = 0)
    {
        var gameId = preferredGameId > 0
            ? preferredGameId
            : await FindSteamGridDbGameIdAsync(gameName, apiKey) ?? 0;
        if (gameId <= 0)
        {
            return null;
        }

        var gridsUrl = $"https://www.steamgriddb.com/api/v2/grids/game/{gameId}?dimensions=600x900,342x482,660x930";
        using var gridsRequest = new HttpRequestMessage(HttpMethod.Get, gridsUrl);
        gridsRequest.Headers.Add("Authorization", $"Bearer {apiKey}");
        using var gridsResponse = await _http.SendAsync(gridsRequest);
        if (!gridsResponse.IsSuccessStatusCode)
        {
            return null;
        }

        using var gridsDoc = JsonDocument.Parse(await gridsResponse.Content.ReadAsStringAsync());
        if (!gridsDoc.RootElement.TryGetProperty("data", out var gridsData))
        {
            return null;
        }

        var firstGrid = gridsData.EnumerateArray().FirstOrDefault();
        if (firstGrid.ValueKind == JsonValueKind.Undefined || !firstGrid.TryGetProperty("url", out var urlProperty))
        {
            return null;
        }

        var imageUrl = urlProperty.GetString();
        if (string.IsNullOrWhiteSpace(imageUrl) || !Uri.TryCreate(imageUrl, UriKind.Absolute, out var imageUri))
        {
            return null;
        }

        var extension = Path.GetExtension(imageUri.AbsolutePath).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".webp"))
        {
            extension = ".jpg";
        }

        var destination = Path.Combine(cacheDirectory, cacheKey + extension);
        var bytes = await _http.GetByteArrayAsync(imageUri);
        await File.WriteAllBytesAsync(destination, bytes);
        return destination;
    }

    private static void TryBackupShortcuts(string source, string destination)
    {
        try
        {
            File.Copy(source, destination, overwrite: true);
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
    }

    private static string? ResolveUwpHookLauncher()
    {
        var candidates = new[]
        {
            // The Playhub installer installs UWPHook silently. Prefer that
            // conventional location, while retaining the bundled launcher as
            // a no-prompt fallback if the separate install is later removed.
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Briano", "UWPHook", "UWPHook.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Briano", "UWPHook", "UWPHook.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "UWPHook", "UWPHook.exe"),
            Path.Combine(AppContext.BaseDirectory, "UWPHook", "UWPHook.exe"),
            Path.Combine(AppPaths.UwpHookPackage, "UWPHook.exe"),
            Path.Combine(AppPaths.UwpHookPackage, "UWPHook", "UWPHook.exe")
        };

        return candidates.FirstOrDefault(IsUwpHookLauncher);
    }

    private static bool IsUwpHookLauncher(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var description = FileVersionInfo.GetVersionInfo(path).FileDescription ?? "";
            return string.Equals(description.Trim(), "UWPHook", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void ApplyKnownAppNames(List<UwpGameEntry> apps)
    {
        var knownPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Extra", "KnownApps.json");
        if (!File.Exists(knownPath))
        {
            knownPath = Path.Combine(AppPaths.UwpHookPackage, "UWPHook-2.14.3", "UWPHook", "Resources", "KnownApps.json");
        }

        if (!File.Exists(knownPath))
        {
            return;
        }

        try
        {
            var known = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(knownPath))
                ?? new Dictionary<string, string>();
            foreach (var app in apps)
            {
                foreach (var kvp in known)
                {
                    if (app.Aumid.StartsWith(kvp.Key + "_", StringComparison.OrdinalIgnoreCase))
                    {
                        app.Name = kvp.Value;
                        break;
                    }
                }
            }
        }
        catch
        {
        }
    }

    private async Task PopulateMissingSteamGridDbArtworkAsync(List<UwpGameEntry> games, string steamGridDbApiKey)
    {
        using var gate = new SemaphoreSlim(3);
        var tasks = games.Select(async game =>
        {
            await gate.WaitAsync();
            try
            {
                if (game.SteamGridDbArtworkDisabled)
                {
                    return;
                }

                var cacheKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                        Encoding.UTF8.GetBytes(game.Aumid)))
                    .Substring(0, 24)
                    .ToLowerInvariant();
                var cacheDirectory = Path.Combine(AppPaths.LocalDataRoot, "cache", "steamgriddb", "auto", cacheKey);
                Directory.CreateDirectory(cacheDirectory);

                foreach (var artworkType in new[] { "cover", "banner", "hero", "logo", "icon" })
                {
                    try
                    {
                        var currentPath = GetSelectedArtworkPath(game, artworkType);
                        if (!string.IsNullOrWhiteSpace(currentPath) && File.Exists(currentPath))
                        {
                            continue;
                        }

                        var options = await GetSteamGridDbArtworkAsync(game, artworkType, steamGridDbApiKey);
                        var artwork = options.FirstOrDefault();
                        if (artwork is null)
                        {
                            continue;
                        }

                        var imageUri = new Uri(artwork.Url);
                        var extension = NormalizeImageExtension(Path.GetExtension(imageUri.AbsolutePath));
                        var destination = Path.Combine(cacheDirectory, artworkType + extension);
                        var bytes = await _http.GetByteArrayAsync(imageUri);
                        await File.WriteAllBytesAsync(destination, bytes);
                        SetSelectedArtworkPath(game, artworkType, destination);
                    }
                    catch
                    {
                        // One unavailable category must not prevent the other
                        // missing artwork types from being assigned.
                    }
                }
            }
            catch
            {
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private static string GetSelectedArtworkPath(UwpGameEntry game, string artworkType)
    {
        return NormalizeArtworkType(artworkType) switch
        {
            "banner" => game.SteamGridDbBannerPath,
            "hero" => game.SteamGridDbHeroPath,
            "logo" => game.SteamGridDbLogoPath,
            "icon" => game.SteamGridDbIconPath,
            _ => game.SteamGridDbCoverPath
        };
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '_');
        }
        return value;
    }
}

public static class Crc32
{
    private static readonly uint[] Table = CreateTable();

    public static uint SteamGridAppId(string appName, string target)
    {
        var bytes = Encoding.UTF8.GetBytes(target + appName);
        return Compute(bytes) | 0x80000000;
    }

    private static uint Compute(byte[] bytes)
    {
        var crc = 0xffffffffu;
        foreach (var b in bytes)
        {
            crc = Table[(crc ^ b) & 0xff] ^ (crc >> 8);
        }
        return ~crc;
    }

    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var c = i;
            for (var j = 0; j < 8; j++)
            {
                c = (c & 1) != 0 ? 0xedb88320u ^ (c >> 1) : c >> 1;
            }
            table[i] = c;
        }
        return table;
    }
}

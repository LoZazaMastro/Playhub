using Playhub.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VDFParser.Models;

namespace Playhub.Services;

public sealed record SteamGridArtworkOption(string Url, string PreviewUrl, int Width, int Height,
    string? Provider = null, string? AuthorName = null, string? AuthorSteamId = null);
public sealed record SteamGridGameOption(int Id, string Name, int? ReleaseYear, bool Verified);

/// <summary>
/// Imports UWP / Xbox Game Pass games into Steam.
///
/// Uses UWPHook's VDFParser and retains existing Steam identities. GameSession
/// prepares the desktop shell before Xbox activation and tracks game lifetime.
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

        // Eseguiamo lo script da un file .ps1 temporaneo con -File invece di
        // -EncodedCommand (base64): il PowerShell in base64 è un forte innesco per
        // gli euristici antivirus (falsi positivi). Il file viene poi eliminato.
        var scriptPath = Path.Combine(Path.GetTempPath(), $"playhub-uwp-scan-{Guid.NewGuid():N}.ps1");
        await File.WriteAllTextAsync(scriptPath, script, new UTF8Encoding(true));
        ProcessResult result;
        try
        {
            result = await ProcessService.RunAsync("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"");
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { }
        }
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

    /// <summary>Reads the real shortcut ID using the exporter's identity predicate; ambiguity never picks another game.</summary>
    public uint? TryGetSteamShortcutAppId(UwpGameEntry game)
    {
        var steamFolder = UwpHookSteamManager.GetSteamFolder();
        if (steamFolder is null) return null;
        try
        {
            return global::Playhub.Importing.ImportedShortcutIdentity.Resolve(
                UwpHookSteamManager.GetUsers(steamFolder).Select(UwpHookSteamManager.ReadShortcuts), entry => MatchesGame(entry, game));
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>Reads exact matching profile locations; never authorizes a different shortcut or writes Steam state.</summary>
    public IReadOnlyList<string> TryGetSteamGridDirectories(UwpGameEntry game)
    {
        var steamFolder = UwpHookSteamManager.GetSteamFolder();
        if (steamFolder is null) return Array.Empty<string>();
        try
        {
            var profiles = UwpHookSteamManager.GetUsers(steamFolder).Select(user => (User:user,Entries:UwpHookSteamManager.ReadShortcuts(user))).ToArray();
            var id = global::Playhub.Importing.ImportedShortcutIdentity.Resolve(profiles.Select(profile=>profile.Entries),entry=>MatchesGame(entry,game));
            if (id is not > 0) return Array.Empty<string>();
            var paths = new List<string>();
            foreach(var profile in profiles)
            {
                if (!Path.GetFileName(profile.User).All(char.IsAsciiDigit)) continue;
                var matches = profile.Entries.Where(entry=>MatchesGame(entry,game)).ToArray();
                if(matches.Length != 1 || unchecked((uint)matches[0].appid) != id.Value) continue;
                var grid = Path.Combine(profile.User,"config","grid");
                global::Playhub.Integrations.ApplicationIntegrationDataStore.GuardPath(grid);
                paths.Add(Path.GetFullPath(grid));
            }
            return paths;
        }
        catch(IOException) { return Array.Empty<string>(); }
        catch(UnauthorizedAccessException) { return Array.Empty<string>(); }
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
                var actual = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                AddSteamArtwork(actual, "cover", FindExistingImage(gridDirectory, unsignedAppId + "p"));
                AddSteamArtwork(actual, "banner", FindExistingImage(gridDirectory, unsignedAppId.ToString()));
                AddSteamArtwork(actual, "hero", FindExistingImage(gridDirectory, unsignedAppId + "_hero"));
                AddSteamArtwork(actual, "logo", FindExistingImage(gridDirectory, unsignedAppId + "_logo"));
                if (File.Exists(shortcut.Icon)) actual["icon"] = shortcut.Icon;
                ImportedArtworkSelection.RefreshSteam(game, actual);
            }
        }
    }

    /// <summary>
    /// Reads the artwork that Steam is actually using for a shortcut. This is
    /// deliberately sourced from Steam's grid directory rather than from the
    /// Playhub cache, so the artwork summary reflects the files Steam ROM
    /// Manager and Steam itself will render.
    /// </summary>
    public IReadOnlyDictionary<string, string> ReadCurrentSteamArtwork(UwpGameEntry game)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var steamFolder = UwpHookSteamManager.GetSteamFolder();
        if (steamFolder is null)
        {
            return result;
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

            var shortcut = shortcuts.FirstOrDefault(entry => MatchesGame(entry, game));
            if (shortcut is null)
            {
                continue;
            }

            var grid = Path.Combine(user, "config", "grid");
            var appId = unchecked((uint)shortcut.appid);
            AddSteamArtwork(result, "cover", FindExistingImage(grid, appId + "p"));
            AddSteamArtwork(result, "banner", FindExistingImage(grid, appId.ToString()));
            AddSteamArtwork(result, "hero", FindExistingImage(grid, appId + "_hero"));
            AddSteamArtwork(result, "logo", FindExistingImage(grid, appId + "_logo"));
            if (!string.IsNullOrWhiteSpace(shortcut.Icon) && File.Exists(shortcut.Icon))
            {
                result["icon"] = shortcut.Icon;
            }
            return result;
        }

        return result;
    }

    public IReadOnlyDictionary<string, string> ReadSelectedArtwork(UwpGameEntry game)
        => ImportedArtworkSelection.Current(game, ReadCurrentSteamArtwork(game));

    public async Task RemoveArtworkAsync(UwpGameEntry game, string artworkType, CancellationToken ct = default)
    {
        var type = NormalizeArtworkType(artworkType);
        ct.ThrowIfCancellationRequested();
        var steam = UwpHookSteamManager.GetSteamFolder();
        if (steam is not null)
        {
            var users = UwpHookSteamManager.GetUsers(steam);
            var profiles = users.ToDictionary(user => user, UwpHookSteamManager.ReadShortcuts);
            if (XboxSteamShortcut.HasAmbiguousMatches(profiles.Values, entry => MatchesGame(entry, game)))
                throw new IOException("The game's Steam shortcut identity is ambiguous.");
            if (type == "icon" && profiles.Values.Any(entries => entries.Any(entry => MatchesGame(entry, game))))
            {
                await SteamLibraryEditSession.RunAsync(() =>
                {
                    ct.ThrowIfCancellationRequested();
                    foreach (var user in users)
                    {
                        var entries = UwpHookSteamManager.ReadShortcuts(user);
                        var match = entries.SingleOrDefault(entry => MatchesGame(entry, game));
                        if (match is null) continue;
                        // Clear this shortcut's reference, never delete a shared/local source icon.
                        match.Icon = "";
                        UwpHookSteamManager.WriteShortcuts(entries, Path.Combine(user, "config", "shortcuts.vdf"));
                    }
                    return Task.FromResult(true);
                });
            }
            else if (type != "icon")
            {
                foreach (var profile in profiles)
                {
                    var match = profile.Value.SingleOrDefault(entry => MatchesGame(entry, game));
                    if (match is null) continue;
                    ImportedArtworkSelection.RemoveExactFiles(Path.Combine(profile.Key, "config", "grid"), unchecked((uint)match.appid), type,
                        Path.Combine(AppPaths.LocalDataRoot, "backups", "removed-artwork", Guid.NewGuid().ToString("N")), ct);
                }
            }
        }
        ImportedArtworkSelection.Choose(game, type, null);
    }

    private static void AddSteamArtwork(IDictionary<string, string> result, string type, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            result[type] = path;
        }
    }

    // Formato cover scelto in "Importa Giochi" (CoverFormat.Vertical / .Square).
    // Impostato da MainWindow quando si carica o si cambia l'impostazione.
    public string CoverFormat { get; set; } = global::Playhub.Services.CoverFormat.Vertical;

    public async Task PopulateSteamGridDbCoversAsync(IEnumerable<UwpGameEntry> games, string steamGridDbApiKey)
    {
        if (string.IsNullOrWhiteSpace(steamGridDbApiKey))
        {
            return;
        }

        var cacheDirectory = Path.Combine(AppPaths.LocalDataRoot, "cache", "steamgriddb", global::Playhub.Services.CoverFormat.CoverCacheFolder(CoverFormat));
        Directory.CreateDirectory(cacheDirectory);
        using var gate = new SemaphoreSlim(4);
        using var metadataGate = new SemaphoreSlim(4);
        var tasks = games.Select(async game =>
        {
            if (game.SteamGridDbArtworkDisabled || game.ArtworkChoices.ContainsKey("cover"))
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

        var cacheDirectory = Path.Combine(AppPaths.LocalDataRoot, "cache", "steamgriddb", global::Playhub.Services.CoverFormat.CoverCacheFolder(CoverFormat));
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
        ImportedArtworkSelection.Choose(game, "cover", downloaded);
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
        var coverDimensions = global::Playhub.Services.CoverFormat.GridDimensions(CoverFormat);
        var endpoint = NormalizeArtworkType(artworkType) switch
        {
            "cover" => $"grids/game/{gameId}?dimensions={coverDimensions}",
            "banner" => $"grids/game/{gameId}?dimensions=460x215,920x430",
            "hero" => $"heroes/game/{gameId}",
            "logo" => $"logos/game/{gameId}",
            "icon" => $"icons/game/{gameId}",
            _ => $"grids/game/{gameId}?dimensions={coverDimensions}"
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
                    : new SteamGridArtworkOption(url, string.IsNullOrWhiteSpace(preview) ? url : preview!, width, height,"steamgriddb",
                        item.TryGetProperty("author",out var author)&&author.ValueKind==JsonValueKind.Object&&author.TryGetProperty("name",out var authorName)&&authorName.ValueKind==JsonValueKind.String?authorName.GetString():null,
                        item.TryGetProperty("author",out var authorId)&&authorId.ValueKind==JsonValueKind.Object&&authorId.TryGetProperty("steam64",out var authorSteamId)?authorSteamId.ToString():null);
            })
            .Where(option => option is not null)
            .Cast<SteamGridArtworkOption>()
            .DistinctBy(option => option.Url, StringComparer.OrdinalIgnoreCase)
            .Take(30)
            .ToList();
    }

    public async Task<IReadOnlyList<SteamGridArtworkOption>> GetOfficialSteamArtworkAsync(
        UwpGameEntry game,
        string artworkType)
    {
        var appId = await ResolveSteamAppIdAsync(game);
        if (appId <= 0)
        {
            return Array.Empty<SteamGridArtworkOption>();
        }

        var candidates = NormalizeArtworkType(artworkType) switch
        {
            "cover" => new[]
            {
                ("library_600x900_2x.jpg", 1200, 1800),
                ("library_600x900.jpg", 600, 900)
            },
            "banner" => new[]
            {
                ("header.jpg", 460, 215),
                ("capsule_616x353.jpg", 616, 353)
            },
            "hero" => new[]
            {
                ("library_hero.jpg", 3840, 1240)
            },
            "logo" => new[]
            {
                ("logo.png", 0, 0),
                ("library_logo.png", 0, 0)
            },
            // L'icona di Steam vive nell'appinfo del client, non sul CDN pubblico.
            _ => Array.Empty<(string, int, int)>()
        };

        var options = new List<SteamGridArtworkOption>();
        foreach (var (fileName, width, height) in candidates)
        {
            var url = $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/{fileName}";
            if (await RemoteFileExistsAsync(url))
            {
                options.Add(new SteamGridArtworkOption(url, url, width, height));
            }
        }

        return options;
    }

    private async Task<int> ResolveSteamAppIdAsync(UwpGameEntry game)
    {
        if (game.SteamAppId != 0)
        {
            return game.SteamAppId;
        }

        var name = (game.Name ?? "").Trim();
        if (name.Length == 0)
        {
            game.SteamAppId = -1;
            return -1;
        }

        try
        {
            var url = "https://store.steampowered.com/api/storesearch/?l=english&cc=us&term="
                + Uri.EscapeDataString(name);
            using var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                game.SteamAppId = -1;
                return -1;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!document.RootElement.TryGetProperty("items", out var items) ||
                items.ValueKind != JsonValueKind.Array)
            {
                game.SteamAppId = -1;
                return -1;
            }

            var wanted = NormalizeTitle(name);
            var first = 0;
            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var idProperty) || !idProperty.TryGetInt32(out var id))
                {
                    continue;
                }

                var itemName = item.TryGetProperty("name", out var nameProperty) ? nameProperty.GetString() ?? "" : "";
                if (first == 0)
                {
                    first = id;
                }

                if (NormalizeTitle(itemName) == wanted)
                {
                    game.SteamAppId = id;
                    return id;
                }
            }

            game.SteamAppId = first > 0 ? first : -1;
            return game.SteamAppId;
        }
        catch
        {
            game.SteamAppId = -1;
            return -1;
        }
    }

    private static string NormalizeTitle(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private async Task<bool> RemoteFileExistsAsync(string url)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await _http.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            if (response.StatusCode != HttpStatusCode.MethodNotAllowed &&
                response.StatusCode != HttpStatusCode.Forbidden)
            {
                return false;
            }

            using var ranged = new HttpRequestMessage(HttpMethod.Get, url);
            ranged.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
            using var rangedResponse = await _http.SendAsync(ranged);
            return rangedResponse.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
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

        var local = File.Exists(artwork.Url);
        var imageUri = local ? null : new Uri(artwork.Url);
        var extension = NormalizeImageExtension(Path.GetExtension(local ? artwork.Url : imageUri!.AbsolutePath));
        var destination = Path.Combine(cacheDirectory, normalizedType + extension);
        if (local) File.Copy(artwork.Url, destination, true);
        else await File.WriteAllBytesAsync(destination, await _http.GetByteArrayAsync(imageUri!));
        SetSelectedArtworkPath(game, normalizedType, destination);
        ImportedArtworkSelection.Choose(game, normalizedType, destination);
        return ApplyArtworkToExistingSteamShortcuts(game, normalizedType, destination);
    }

    public async Task<string> ExportSelectedToSteamAsync(IEnumerable<UwpGameEntry> games, string steamGridDbApiKey = "")
    {
        var selected = games.Where(game => game.Selected).ToList();
        if (selected.Count == 0) return "Seleziona almeno un gioco da importare.";
        if (selected.Any(game => game.ArtworkChoices.Values.Any(path => path is not null && !File.Exists(path))))
            return "L'artwork scelto non è più disponibile. Scegli un altro file prima di importare.";
        foreach (var game in selected)
        {
            foreach (var artwork in ReadCurrentSteamArtwork(game))
            {
                if (game.ArtworkChoices.ContainsKey(artwork.Key)) continue;
                var chosen = GetSelectedArtworkPath(game, artwork.Key);
                if (string.IsNullOrWhiteSpace(chosen) || !File.Exists(chosen))
                    SetSelectedArtworkPath(game, artwork.Key, artwork.Value);
            }
        }
        // Network work precedes the short exclusive write phase.
        if (!string.IsNullOrWhiteSpace(steamGridDbApiKey))
        {
            await PopulateSteamGridDbCoversAsync(selected, steamGridDbApiKey);
            await PopulateMissingSteamGridDbArtworkAsync(selected, steamGridDbApiKey);
        }
        return await SteamLibraryEditSession.RunAsync(() => ExportWhileSteamClosedAsync(selected));
    }

    private async Task<string> ExportWhileSteamClosedAsync(IEnumerable<UwpGameEntry> games, string steamGridDbApiKey = "")
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
            return "Non trovo alcun profilo Steam su questo PC.";
        }

        var profiles = new Dictionary<string, VDFEntry[]>();
        try
        {
            foreach (var user in users) profiles[user] = UwpHookSteamManager.ReadShortcuts(user);
        }
        catch (UnauthorizedAccessException)
        {
            return "Sicurezza di Windows impedisce a Playhub di leggere la libreria Steam.";
        }
        foreach (var game in selected)
        {
            if (XboxSteamShortcut.HasAmbiguousMatches(profiles.Values, entry => MatchesGame(entry, game)))
                return $"Il gioco {game.Name} ha collegamenti Steam duplicati o identità diverse tra i profili. Risolvi il conflitto prima di importarlo.";
        }

        string? uwpHookExe = null;
        if (selected.Any(game => !game.IsLocalExecutable))
        {
            uwpHookExe = ResolveUwpHookLauncher();
        }

        var uwpHookDir = uwpHookExe is null ? "" : Path.GetDirectoryName(uwpHookExe) ?? AppContext.BaseDirectory;
        var sessionExe = Path.Combine(AppContext.BaseDirectory, "Playhub.GameSession.exe");
        if (!File.Exists(sessionExe))
            return "Non trovo il componente di avvio Playhub.GameSession. Ripristina l'installazione di Playhub e riprova.";
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
                shortcuts = profiles[user];
            }
            catch (UnauthorizedAccessException)
            {
                blockedPaths.Add(shortcutsPath);
                continue;
            }

            foreach (var game in selected)
            {
                var targetExe = GetTargetExecutable(game, uwpHookExe ?? sessionExe);
                var targetDirectory = game.IsLocalExecutable
                    ? QuotePath(Path.GetDirectoryName(game.LocalExecutablePath) ?? "")
                    : uwpHookDir;
                var appId = XboxSteamShortcut.ResolveAppId(profiles.Values, entry => MatchesGame(entry, game),
                    unchecked((int)Crc32.SteamGridAppId(game.Name, targetExe)));
                var icon = !string.IsNullOrWhiteSpace(game.SteamGridDbIconPath) && File.Exists(game.SteamGridDbIconPath)
                    ? game.SteamGridDbIconPath
                    : game.IsLocalExecutable ? game.LocalExecutablePath : TryPersistIcon(game);

                var existingIndex = Array.FindIndex(shortcuts, s => MatchesGame(s, game));
                var existing = existingIndex >= 0 ? shortcuts[existingIndex] : null;
                // Keep the shortcut identity (and its artwork/collections) when
                // changing the launcher. Steam must keep tracking our session
                // even if a game's bootstrapper exits or registers another AppID.
                var oldArguments = existing?.LaunchOptions ?? game.SourceLaunchArguments;
                var alreadyTracked = existing is not null && IsSessionShortcut(existing);
                var launchOptions = game.IsLocalExecutable
                        ? alreadyTracked ? oldArguments : "--game " + targetExe + (oldArguments.Length > 0 ? " " + oldArguments : "")
                        : global::Playhub.GameSession.UwpShortcutArguments.Build(game.Aumid, game.Executable, existing?.LaunchOptions ?? game.SourceLaunchArguments);
                var entry = XboxSteamShortcut.Create(existing, appId, game.Name, sessionExe,
                    game.IsLocalExecutable ? targetDirectory : QuotePath(AppContext.BaseDirectory),
                    icon, launchOptions, game.IsLocalExecutable, (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds());

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
                var imported = shortcuts.First(s => MatchesGame(s, game));
                ApplySelectedArtworkForUser(game, user, unchecked((uint)imported.appid));
            }
        }

        if (blockedPaths.Count > 0)
        {
            Diag.Crash("UwpXboxService.ExportSelectedToSteamAsync", $"Accesso negato: {blockedPaths[0]}");
            return "Sicurezza di Windows impedisce a Playhub di aggiornare la libreria. Consenti Playhub in “Accesso alle cartelle controllato”, poi riprova.";
        }

        return $"Ho aggiunto {selected.Count} giochi alla libreria Steam.";
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
            if (PathsEqual(entry.Exe, game.LocalExecutablePath)) return true;
            if (!IsSessionShortcut(entry)) return false;
            var arguments = CommandLine.Parse(entry.LaunchOptions ?? "");
            return arguments.Count >= 2 && arguments[0] == "--game" &&
                   PathsEqual(arguments[1], game.LocalExecutablePath);
        }

        return global::Playhub.GameSession.UwpShortcutArguments.Matches(entry.LaunchOptions ?? "", game.Aumid);
    }

    private static bool IsSessionShortcut(VDFEntry entry) =>
        string.Equals(Path.GetFileName((entry.Exe ?? "").Trim().Trim('"')),
            "Playhub.GameSession.exe", StringComparison.OrdinalIgnoreCase);

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
        if (game.SteamGridDbArtworkDisabled && game.ArtworkChoices.Count == 0) return;
        foreach (var selection in new[]
        {
            (Type: "cover", Path: game.SteamGridDbCoverPath),
            (Type: "banner", Path: game.SteamGridDbBannerPath),
            (Type: "hero", Path: game.SteamGridDbHeroPath),
            (Type: "logo", Path: game.SteamGridDbLogoPath)
        })
        {
            if (game.SteamGridDbArtworkDisabled && !game.ArtworkChoices.ContainsKey(selection.Type)) continue;
            if (game.ArtworkChoices.TryGetValue(selection.Type, out var explicitPath))
            {
                if (explicitPath is null)
                    ImportedArtworkSelection.RemoveExactFiles(Path.Combine(userPath, "config", "grid"), appId, selection.Type,
                        Path.Combine(userPath, "config", "playhub-artwork-backups", Guid.NewGuid().ToString("N")), default);
                else if (File.Exists(explicitPath)) CopyArtworkToGrid(userPath, appId, selection.Type, explicitPath);
                else throw new FileNotFoundException("The selected artwork is no longer available.", explicitPath);
                continue;
            }
            if (!string.IsNullOrWhiteSpace(selection.Path) && File.Exists(selection.Path))
            {
                XboxSteamShortcut.ApplyArtworkIfMissing(userPath, appId, selection.Type, selection.Path, CopyArtworkToGrid);
            }
        }
    }

    private static void CopyArtworkToGrid(string userPath, uint appId, string artworkType, string sourcePath)
    {
        var gridDirectory = Path.Combine(userPath, "config", "grid");
        ImportedArtworkSelection.ReplaceExactFile(gridDirectory, appId, NormalizeArtworkType(artworkType), sourcePath,
            Path.Combine(userPath, "config", "playhub-artwork-backups", Guid.NewGuid().ToString("N")), default);
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

        var gridsDimensions = global::Playhub.Services.CoverFormat.GridDimensions(CoverFormat);
        var gridsUrl = $"https://www.steamgriddb.com/api/v2/grids/game/{gameId}?dimensions={gridsDimensions}";
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
                        if (game.ArtworkChoices.ContainsKey(artworkType)) continue;
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

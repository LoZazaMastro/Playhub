using Playhub.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VDFParser.Models;

namespace Playhub.Services;

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
    private readonly HttpClient _http = new();

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

    public async Task<string> ExportSelectedToSteamAsync(IEnumerable<UwpGameEntry> games, string steamGridDbApiKey = "")
    {
        var selected = games.Where(g => g.Selected).ToList();
        if (selected.Count == 0)
        {
            return "Seleziona almeno un gioco Xbox/UWP da importare.";
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

        var uwpHookExe = ResolveUwpHookLauncher();
        if (uwpHookExe is null)
        {
            return "Non trovo UWPHook.exe. Copia UWPHook nella cartella dell'app o nella cartella Plugin.";
        }

        var uwpHookDir = Path.GetDirectoryName(uwpHookExe) ?? AppContext.BaseDirectory;
        var backupRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Briano", "UWPHook", "backups");
        Directory.CreateDirectory(backupRoot);

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
                var appId = unchecked((int)Crc32.SteamGridAppId(game.Name, uwpHookExe));
                var icon = TryPersistIcon(game);

                var entry = new VDFEntry
                {
                    appid = appId,
                    AppName = game.Name,
                    Exe = uwpHookExe,
                    StartDir = uwpHookDir,
                    Icon = icon,
                    ShortcutPath = "",
                    LaunchOptions = game.Aumid + " " + game.Executable,
                    IsHidden = 0,
                    AllowDesktopConfig = 1,
                    AllowOverlay = 1,
                    OpenVR = 0,
                    Devkit = 0,
                    DevkitGameID = "",
                    LastPlayTime = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Tags = new[] { "Xbox" }
                };

                var existingIndex = Array.FindIndex(shortcuts, s =>
                    string.Equals(s.AppName, game.Name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(s.Exe, uwpHookExe, StringComparison.OrdinalIgnoreCase));

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

            if (!string.IsNullOrWhiteSpace(steamGridDbApiKey))
            {
                await TryDownloadSteamGridImagesAsync(selected, targetExe: uwpHookExe, userPath: user, steamGridDbApiKey: steamGridDbApiKey);
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
            // Bundled copy first (portable), then the local plugin folder.
            Path.Combine(AppContext.BaseDirectory, "UWPHook", "UWPHook.exe"),
            Path.Combine(AppPaths.UwpHookPackage, "UWPHook.exe"),
            Path.Combine(AppPaths.UwpHookPackage, "UWPHook", "UWPHook.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
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

    private async Task TryDownloadSteamGridImagesAsync(List<UwpGameEntry> games, string targetExe, string userPath, string steamGridDbApiKey)
    {
        var gridDir = Path.Combine(userPath, "config", "grid");
        Directory.CreateDirectory(gridDir);

        foreach (var game in games)
        {
            try
            {
                var searchUrl = $"https://www.steamgriddb.com/api/v2/search/autocomplete/{Uri.EscapeDataString(game.Name)}";
                using var searchRequest = new HttpRequestMessage(HttpMethod.Get, searchUrl);
                searchRequest.Headers.Add("Authorization", $"Bearer {steamGridDbApiKey}");
                using var searchResponse = await _http.SendAsync(searchRequest);
                if (!searchResponse.IsSuccessStatusCode)
                {
                    continue;
                }

                using var searchDoc = JsonDocument.Parse(await searchResponse.Content.ReadAsStringAsync());
                var first = searchDoc.RootElement.GetProperty("data").EnumerateArray().FirstOrDefault();
                if (first.ValueKind == JsonValueKind.Undefined)
                {
                    continue;
                }

                var sgdbId = first.GetProperty("id").GetInt32();
                var appId = Crc32.SteamGridAppId(game.Name, targetExe);
                await DownloadFirstImageAsync($"https://www.steamgriddb.com/api/v2/grids/game/{sgdbId}?dimensions=460x215,920x430", Path.Combine(gridDir, $"{appId}.png"), steamGridDbApiKey);
                await DownloadFirstImageAsync($"https://www.steamgriddb.com/api/v2/grids/game/{sgdbId}?dimensions=600x900,342x482,660x930", Path.Combine(gridDir, $"{appId}p.png"), steamGridDbApiKey);
                await DownloadFirstImageAsync($"https://www.steamgriddb.com/api/v2/heroes/game/{sgdbId}", Path.Combine(gridDir, $"{appId}_hero.png"), steamGridDbApiKey);
                await DownloadFirstImageAsync($"https://www.steamgriddb.com/api/v2/logos/game/{sgdbId}", Path.Combine(gridDir, $"{appId}_logo.png"), steamGridDbApiKey);
            }
            catch
            {
            }
        }
    }

    private async Task DownloadFirstImageAsync(string apiUrl, string destination, string apiKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
        request.Headers.Add("Authorization", $"Bearer {apiKey}");
        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return;
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var first = doc.RootElement.GetProperty("data").EnumerateArray().FirstOrDefault();
        if (first.ValueKind == JsonValueKind.Undefined || !first.TryGetProperty("url", out var urlProp))
        {
            return;
        }

        var imageUrl = urlProp.GetString();
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return;
        }

        var bytes = await _http.GetByteArrayAsync(imageUrl);
        await File.WriteAllBytesAsync(destination, bytes);
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

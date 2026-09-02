using Playhub.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Playhub.Services;

public enum PluginInstallPhase
{
    Resolving,
    Downloading,
    Extracting,
    Installing,
    Completed
}

public sealed record PluginInstallProgress(
    PluginInstallPhase Phase,
    double? Percent = null);

public sealed class DeckyPluginService
{
    private const string InstalledReleaseMarker = ".playhub-release.json";
    private const string DeckyCatalogUrl = "https://plugins.deckbrew.xyz/plugins";
    private const string DeckyCdnBaseUrl = "https://cdn.tzatzikiweeb.moe/file/steam-deck-homebrew/versions/";
    private static readonly HttpClient Http = CreateHttpClient();

    public async Task InstallOrUpdateAsync(
        DeckyPluginInfo plugin,
        string deckyPluginsPath,
        IProgress<PluginInstallProgress>? progress = null)
    {
        progress?.Report(new PluginInstallProgress(PluginInstallPhase.Resolving));
        var destination = await Task.Run(() =>
        {
            Directory.CreateDirectory(deckyPluginsPath);
            return plugin.IsInstalled && Directory.Exists(plugin.InstalledFolder)
                ? plugin.InstalledFolder
                : Path.Combine(deckyPluginsPath, plugin.FolderName);
        });
        var source = plugin.SourceFolder;

        var release = await Task.Run(() =>
            string.Equals(plugin.CatalogSource, "decky-store", StringComparison.OrdinalIgnoreCase)
                ? ResolveLatestDeckyStoreReleaseAsync(plugin)
                : ResolveLatestReleaseAsync(plugin.RepositorySlug, plugin.ReleaseAssetName));
        var releaseZipUrl = release?.ZipUrl ?? plugin.CatalogReleaseZipUrl ?? plugin.ReleaseZipUrl;
        if (release is not null)
        {
            plugin.ReleaseZipUrl = releaseZipUrl;
            plugin.CatalogReleaseZipUrl = releaseZipUrl;
            plugin.ReleaseAssetName = Path.GetFileName(new Uri(release.ZipUrl).AbsolutePath);
            plugin.ReleasePageUrl = release.PageUrl;
            plugin.ReleasePublishedAt = release.PublishedAt;
            plugin.ReleaseNotes = release.Notes;
            plugin.ReleaseNotesVersion = release.Version;
            plugin.Version = release.Version;
        }

        string? releaseZip = null;
        if (!string.IsNullOrWhiteSpace(releaseZipUrl))
        {
            try
            {
                progress?.Report(new PluginInstallProgress(PluginInstallPhase.Downloading));
                releaseZip = await Task.Run(() => DownloadReleaseAsync(plugin, releaseZipUrl, progress));
            }
            catch
            {
                releaseZip = await Task.Run(() =>
                {
                    var cached = FindCachedReleaseZip(RepositoryIdentity(plugin), plugin.Version);
                    if (cached is null &&
                        !string.Equals(RepositoryIdentity(plugin), plugin.RepositoryName, StringComparison.OrdinalIgnoreCase))
                    {
                        cached = FindCachedReleaseZip(plugin.RepositoryName, plugin.Version);
                    }
                    return cached;
                });
            }
        }

        await Task.Run(() =>
        {
            if (!string.IsNullOrWhiteSpace(releaseZip) && File.Exists(releaseZip))
            {
                progress?.Report(new PluginInstallProgress(PluginInstallPhase.Extracting));
                source = ExtractPluginZip(releaseZip);
            }
            else if (!Directory.Exists(source) && plugin.InstallerZip is not null)
            {
                progress?.Report(new PluginInstallProgress(PluginInstallPhase.Extracting));
                source = ExtractPluginZip(plugin.InstallerZip);
            }

            var pluginRoot = FindPluginRoot(source);
            if (pluginRoot is null)
            {
                throw new DirectoryNotFoundException($"Non trovo i file installabili per {plugin.Name}.");
            }
            source = pluginRoot;

            progress?.Report(new PluginInstallProgress(PluginInstallPhase.Installing));
            if (Directory.Exists(destination))
            {
                StopPluginProcesses(destination);
                DeleteDirectoryWithRetry(destination);
            }

            CopyDirectory(source, destination);
            WriteInstalledReleaseMarker(destination, RepositoryIdentity(plugin), plugin.Version);
        });
        plugin.IsInstalled = true;
        plugin.InstalledFolder = destination;
        plugin.InstalledVersion = plugin.Version;
        plugin.HasUpdate = false;
        progress?.Report(new PluginInstallProgress(PluginInstallPhase.Completed, Percent: 100));
    }

    public Task UninstallAsync(DeckyPluginInfo plugin) =>
        UninstallWithProcessStopAsync(plugin, StopPluginProcesses);

    internal static Task UninstallWithProcessStopAsync(DeckyPluginInfo plugin, Action<string> stopProcesses) =>
        UninstallAsync(plugin, installedFolder => Task.Run(() =>
        {
            if (Directory.Exists(installedFolder))
            {
                stopProcesses(installedFolder);
                DeleteDirectoryWithRetry(installedFolder);
            }
        }));

    internal static async Task UninstallAsync(DeckyPluginInfo plugin, Func<string, Task> removeDirectory)
    {
        if (!plugin.IsInstalled) return;
        var installedFolder = plugin.InstalledFolder;
        if (string.IsNullOrWhiteSpace(installedFolder))
            throw new InvalidOperationException(
                $"Percorso di installazione mancante per {plugin.Name}. Aggiorna l'elenco dei plugin e riprova.");

        await removeDirectory(installedFolder);
        MarkPluginUninstalled(plugin);
    }

    internal static void MarkPluginUninstalled(DeckyPluginInfo plugin)
    {
        plugin.IsInstalled = false;
        plugin.HasUpdate = false;
        plugin.InstalledVersion = "";
        plugin.InstalledFolder = "";
    }

    private static void WriteInstalledReleaseMarker(string destination, string repositoryName, string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return;
        }

        try
        {
            var marker = new
            {
                repository = repositoryName,
                version,
                installedAt = DateTimeOffset.UtcNow
            };
            File.WriteAllText(
                Path.Combine(destination, InstalledReleaseMarker),
                JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Il plugin è comunque installato; il manifest resta il fallback.
        }
    }

    private static void StopPluginProcesses(string pluginFolder)
    {
        try
        {
            var script = @"
$target = [Environment]::GetEnvironmentVariable('PLAYHUB_PLUGIN_REMOVE_PATH')
if ([string]::IsNullOrWhiteSpace($target)) { exit 0 }
$allProcesses = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue)
$pluginProcesses = @($allProcesses | Where-Object {
    $_.ProcessId -ne $PID -and
    $_.CommandLine -and
    $_.CommandLine.IndexOf($target, [StringComparison]::OrdinalIgnoreCase) -ge 0
  })

# Launch Curtain keeps a PowerShell helper alive under a dedicated Decky
# multiprocessing worker. Killing only the helper lets that worker recreate it,
# so stop that specific worker too (never the root PluginLoader process).
$parentIds = @($pluginProcesses | Select-Object -ExpandProperty ParentProcessId -Unique)
$pluginWorkers = @($allProcesses | Where-Object {
    $parentIds -contains $_.ProcessId -and
    $_.Name -like 'PluginLoader*' -and
    $_.CommandLine -match 'multiprocessing-fork'
  })

$pluginWorkers | ForEach-Object {
  Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
}
$pluginProcesses | ForEach-Object {
  Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
}
";
            // Scriviamo lo script su un file .ps1 temporaneo ed eseguiamo con -File
            // invece di -EncodedCommand (base64): il PowerShell codificato in base64
            // è un forte innesco per gli euristici antivirus (falsi positivi).
            var scriptPath = Path.Combine(Path.GetTempPath(), $"playhub-plugin-stop-{Guid.NewGuid():N}.ps1");
            File.WriteAllText(scriptPath, script, new UTF8Encoding(true));
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                startInfo.Environment["PLAYHUB_PLUGIN_REMOVE_PATH"] = Path.GetFullPath(pluginFolder);
                using var process = Process.Start(startInfo);
                if (process is not null)
                {
                    process.WaitForExit(8000);
                }
            }
            finally
            {
                try { File.Delete(scriptPath); } catch { }
            }
        }
        catch
        {
        }

        Thread.Sleep(350);
    }

    private static void DeleteDirectoryWithRetry(string path)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastError = ex;
                Thread.Sleep(350);
            }
        }

        if (lastError is not null)
        {
            throw lastError;
        }
    }

    private static string ExtractPluginZip(string zip)
    {
        var extractRoot = Path.Combine(AppPaths.DownloadsRoot, "plugin-" + Path.GetFileNameWithoutExtension(zip));
        if (Directory.Exists(extractRoot))
        {
            Directory.Delete(extractRoot, recursive: true);
        }

        Directory.CreateDirectory(extractRoot);
        ZipFile.ExtractToDirectory(zip, extractRoot);

        return FindPluginRoot(extractRoot) ?? extractRoot;
    }

    private static async Task<string> DownloadReleaseAsync(
        DeckyPluginInfo plugin,
        string releaseZipUrl,
        IProgress<PluginInstallProgress>? progress)
    {
        Directory.CreateDirectory(AppPaths.DownloadsRoot);
        var fileName = Path.GetFileName(new Uri(releaseZipUrl).AbsolutePath);
        var versionKey = CacheKey(plugin.Version);
        if (string.IsNullOrWhiteSpace(versionKey))
        {
            versionKey = "unknown";
        }
        var target = Path.Combine(
            AppPaths.DownloadsRoot,
            $"{CacheKey(RepositoryIdentity(plugin))}-{versionKey}-{fileName}");
        var partial = target + ".partial";
        try
        {
            using var response = await Http.GetAsync(releaseZipUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength;
            long bytesReceived = 0;
            void ReportDownloadProgress() => progress?.Report(new PluginInstallProgress(
                PluginInstallPhase.Downloading,
                Percent: totalBytes is > 0 ? Math.Clamp(bytesReceived * 100d / totalBytes.Value, 0, 100) : null));

            ReportDownloadProgress();
            await using var input = await response.Content.ReadAsStreamAsync();
            await using (var output = new FileStream(
                partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                var buffer = new byte[81920];
                var reportTimer = Stopwatch.StartNew();
                int bytesRead;
                while ((bytesRead = await input.ReadAsync(buffer.AsMemory())) != 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, bytesRead));
                    bytesReceived += bytesRead;
                    if (reportTimer.ElapsedMilliseconds >= 100)
                    {
                        ReportDownloadProgress();
                        reportTimer.Restart();
                    }
                }
            }
            File.Move(partial, target, overwrite: true);
            ReportDownloadProgress();
        }
        finally
        {
            if (File.Exists(partial))
            {
                try { File.Delete(partial); } catch { }
            }
        }
        return target;
    }

    private static async Task<ResolvedRelease?> ResolveLatestReleaseAsync(string repositorySlug, string preferredAssetName)
    {
        if (!TryParseRepositorySlug(repositorySlug, out var owner, out var repository))
        {
            return null;
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var json = await Http.GetStringAsync(
                    $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}/releases/latest");
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (!root.TryGetProperty("assets", out var assets))
                {
                    return null;
                }

                var candidates = assets.EnumerateArray()
                    .Where(asset => asset.TryGetProperty("name", out var name) &&
                                    (name.GetString() ?? "").EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    .Select(asset => new
                    {
                        Name = asset.GetProperty("name").GetString() ?? "",
                        Url = asset.TryGetProperty("browser_download_url", out var url) ? url.GetString() : null
                    })
                    .Where(asset => !string.IsNullOrWhiteSpace(asset.Url))
                    .ToList();
                var selected = candidates.FirstOrDefault(asset =>
                                   !string.IsNullOrWhiteSpace(preferredAssetName) &&
                                   string.Equals(asset.Name, preferredAssetName, StringComparison.OrdinalIgnoreCase))
                               ?? candidates
                                   .OrderByDescending(asset => asset.Name.Contains("installer", StringComparison.OrdinalIgnoreCase))
                                   .ThenByDescending(asset => asset.Name.Contains("decky", StringComparison.OrdinalIgnoreCase))
                                   .FirstOrDefault();
                if (selected?.Url is null)
                {
                    return null;
                }

                var tag = root.TryGetProperty("tag_name", out var tagProperty)
                    ? tagProperty.GetString() ?? ""
                    : "";
                var version = NormalizeReleaseVersion(tag, selected.Name);
                return new ResolvedRelease(
                    selected.Url,
                    root.TryGetProperty("html_url", out var pageProperty) ? pageProperty.GetString() ?? "" : "",
                    string.IsNullOrWhiteSpace(version) ? tag : version,
                    root.TryGetProperty("body", out var bodyProperty) ? bodyProperty.GetString() ?? "" : "",
                    root.TryGetProperty("published_at", out var publishedProperty)
                        ? FormatDate(publishedProperty.GetString())
                        : "");
            }
            catch when (attempt == 0)
            {
                await Task.Delay(350);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static async Task<ResolvedRelease?> ResolveLatestDeckyStoreReleaseAsync(DeckyPluginInfo plugin)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var json = await Http.GetStringAsync(DeckyCatalogUrl);
                using var document = JsonDocument.Parse(json);
                var catalogPlugin = document.RootElement
                    .EnumerateArray()
                    .FirstOrDefault(item =>
                        (plugin.CatalogPluginId > 0 &&
                         item.TryGetProperty("id", out var id) &&
                         id.TryGetInt32(out var value) &&
                         value == plugin.CatalogPluginId) ||
                        (item.TryGetProperty("name", out var name) &&
                         string.Equals(name.GetString(), plugin.Name, StringComparison.OrdinalIgnoreCase)));
                if (catalogPlugin.ValueKind == JsonValueKind.Undefined ||
                    !catalogPlugin.TryGetProperty("versions", out var versions))
                {
                    return null;
                }

                var latest = versions.EnumerateArray()
                    .Select(version => new
                    {
                        Name = version.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                        Hash = version.TryGetProperty("hash", out var hash) ? hash.GetString() ?? "" : "",
                        Created = version.TryGetProperty("created", out var created) ? created.GetString() ?? "" : ""
                    })
                    .Where(version => Regex.IsMatch(version.Hash, "^[0-9a-f]{64}$", RegexOptions.IgnoreCase))
                    .OrderByDescending(version =>
                        DateTimeOffset.TryParse(version.Created, out var created) ? created : DateTimeOffset.MinValue)
                    .FirstOrDefault();
                if (latest is null)
                {
                    return null;
                }

                return new ResolvedRelease(
                    $"{DeckyCdnBaseUrl}{latest.Hash}.zip",
                    plugin.RepositoryUrl,
                    latest.Name,
                    "",
                    FormatDate(latest.Created));
            }
            catch when (attempt == 0)
            {
                await Task.Delay(350);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static string? FindCachedReleaseZip(string repositoryIdentity, string expectedVersion)
    {
        var versionKey = CacheKey(expectedVersion);
        if (!Directory.Exists(AppPaths.DownloadsRoot) || string.IsNullOrWhiteSpace(versionKey))
        {
            return null;
        }

        return Directory.EnumerateFiles(
                AppPaths.DownloadsRoot,
                $"{CacheKey(repositoryIdentity)}-{versionKey}-*.zip",
                SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Where(IsReadableZip)
            .FirstOrDefault();
    }

    private static string RepositoryIdentity(DeckyPluginInfo plugin) =>
        string.IsNullOrWhiteSpace(plugin.RepositorySlug) ? plugin.RepositoryName : plugin.RepositorySlug;

    private static string CacheKey(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(invalid.Contains(character) || character is '/' or '\\' ? '-' : character);
        }
        return builder.ToString().Trim('-');
    }

    private static bool TryParseRepositorySlug(string value, out string owner, out string repository)
    {
        owner = "";
        repository = "";
        var parts = (value ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || parts.Any(part => part.Any(character =>
                !char.IsLetterOrDigit(character) && character is not '-' and not '_' and not '.')))
        {
            return false;
        }

        owner = parts[0];
        repository = parts[1];
        return true;
    }

    private static string NormalizeReleaseVersion(string tag, string assetName)
    {
        var tagMatch = System.Text.RegularExpressions.Regex.Match(
            tag ?? "",
            @"\d+(?:\.\d+)+(?:[-+][0-9A-Za-z.-]+)?");
        var assetMatches = System.Text.RegularExpressions.Regex.Matches(
                assetName ?? "",
                @"\d+(?:\.\d+)+(?:[-+][0-9A-Za-z.-]+)?")
            .Select(match => match.Value)
            .OrderByDescending(value => value.Count(character => character == '.'))
            .ThenByDescending(value => value.Length)
            .ToList();
        var assetVersion = assetMatches.FirstOrDefault() ?? "";
        if (!tagMatch.Success ||
            assetVersion.Count(character => character == '.') > tagMatch.Value.Count(character => character == '.'))
        {
            return string.IsNullOrWhiteSpace(assetVersion) ? (tag ?? "").TrimStart('v', 'V') : assetVersion;
        }
        return tagMatch.Value;
    }

    private static string FormatDate(string? value) =>
        DateTimeOffset.TryParse(value, out var date) ? date.ToLocalTime().ToString("dd/MM/yyyy") : "";

    private static bool IsReadableZip(string path)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            return archive.Entries.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindPluginRoot(string? root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return null;
        }

        if (File.Exists(Path.Combine(root, "plugin.json")))
        {
            return root;
        }

        var pluginJson = Directory.EnumerateFiles(root, "plugin.json", SearchOption.AllDirectories)
            .OrderBy(path => Path.GetRelativePath(root, path).Count(character => character is '\\' or '/'))
            .FirstOrDefault();
        return pluginJson is null ? null : Path.GetDirectoryName(pluginJson);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Playhub/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
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
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private sealed record ResolvedRelease(string ZipUrl, string PageUrl, string Version, string Notes, string PublishedAt);
}

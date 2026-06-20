using Playhub.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Playhub.Services;

public sealed class DeckyPluginService
{
    private const string InstalledReleaseMarker = ".playhub-release.json";
    private static readonly HttpClient Http = CreateHttpClient();

    public async Task InstallOrUpdateAsync(DeckyPluginInfo plugin, string deckyPluginsPath)
    {
        Directory.CreateDirectory(deckyPluginsPath);
        var destination = plugin.IsInstalled && Directory.Exists(plugin.InstalledFolder)
            ? plugin.InstalledFolder
            : Path.Combine(deckyPluginsPath, plugin.FolderName);
        var source = plugin.SourceFolder;

        var releaseZipUrl = plugin.ReleaseZipUrl;
        if (string.IsNullOrWhiteSpace(releaseZipUrl))
        {
            releaseZipUrl = await ResolveLatestReleaseZipUrlAsync(plugin.RepositoryName);
            plugin.ReleaseZipUrl = releaseZipUrl;
        }

        string? releaseZip = null;
        if (!string.IsNullOrWhiteSpace(releaseZipUrl))
        {
            try
            {
                releaseZip = await DownloadReleaseAsync(plugin, releaseZipUrl);
            }
            catch
            {
                releaseZip = FindCachedReleaseZip(plugin.RepositoryName);
            }
        }

        if (!string.IsNullOrWhiteSpace(releaseZip) && File.Exists(releaseZip))
        {
            source = ExtractPluginZip(releaseZip);
        }
        else if (!Directory.Exists(source) && plugin.InstallerZip is not null)
        {
            source = ExtractPluginZip(plugin.InstallerZip);
        }

        var pluginRoot = FindPluginRoot(source);
        if (pluginRoot is null)
        {
            throw new DirectoryNotFoundException($"Non trovo i file installabili per {plugin.Name}.");
        }
        source = pluginRoot;

        if (Directory.Exists(destination))
        {
            StopPluginProcesses(destination);
            DeleteDirectoryWithRetry(destination);
        }

        CopyDirectory(source, destination);
        WriteInstalledReleaseMarker(destination, plugin.RepositoryName, plugin.Version);
        plugin.IsInstalled = true;
        plugin.InstalledFolder = destination;
        plugin.InstalledVersion = plugin.Version;
        plugin.HasUpdate = false;
    }

    public Task UninstallAsync(DeckyPluginInfo plugin)
    {
        if (!string.IsNullOrWhiteSpace(plugin.InstalledFolder) && Directory.Exists(plugin.InstalledFolder))
        {
            StopPluginProcesses(plugin.InstalledFolder);
            DeleteDirectoryWithRetry(plugin.InstalledFolder);
        }

        plugin.IsInstalled = false;
        return Task.CompletedTask;
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
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
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

    private static async Task<string> DownloadReleaseAsync(DeckyPluginInfo plugin, string releaseZipUrl)
    {
        Directory.CreateDirectory(AppPaths.DownloadsRoot);
        var fileName = Path.GetFileName(new Uri(releaseZipUrl).AbsolutePath);
        var target = Path.Combine(AppPaths.DownloadsRoot, $"{plugin.RepositoryName}-{fileName}");
        var partial = target + ".partial";
        try
        {
            await using var input = await Http.GetStreamAsync(releaseZipUrl);
            await using (var output = File.Create(partial))
            {
                await input.CopyToAsync(output);
            }
            File.Move(partial, target, overwrite: true);
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

    private static async Task<string?> ResolveLatestReleaseZipUrlAsync(string repositoryName)
    {
        if (string.IsNullOrWhiteSpace(repositoryName))
        {
            return null;
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var json = await Http.GetStringAsync($"https://api.github.com/repos/LoZazaMastro/{repositoryName}/releases/latest");
                using var document = JsonDocument.Parse(json);
                if (!document.RootElement.TryGetProperty("assets", out var assets))
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
                    .OrderByDescending(asset => asset.Name.Contains("installer", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                return candidates.FirstOrDefault()?.Url;
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

    private static string? FindCachedReleaseZip(string repositoryName)
    {
        if (!Directory.Exists(AppPaths.DownloadsRoot))
        {
            return null;
        }

        return Directory.EnumerateFiles(AppPaths.DownloadsRoot, repositoryName + "-*.zip", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Where(IsReadableZip)
            .FirstOrDefault();
    }

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
}

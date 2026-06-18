using Playhub.Models;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Playhub.Services;

public sealed class DeckyPluginService
{
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
            Directory.Delete(destination, recursive: true);
        }

        CopyDirectory(source, destination);
        plugin.IsInstalled = true;
        plugin.InstalledFolder = destination;
    }

    public Task UninstallAsync(DeckyPluginInfo plugin)
    {
        if (!string.IsNullOrWhiteSpace(plugin.InstalledFolder) && Directory.Exists(plugin.InstalledFolder))
        {
            Directory.Delete(plugin.InstalledFolder, recursive: true);
        }

        plugin.IsInstalled = false;
        return Task.CompletedTask;
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

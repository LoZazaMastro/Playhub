using Playhub.Models;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Playhub.Services;

public sealed class DeckyPluginService
{
    private static readonly HttpClient Http = new();

    public async Task InstallOrUpdateAsync(DeckyPluginInfo plugin, string deckyPluginsPath)
    {
        Directory.CreateDirectory(deckyPluginsPath);
        var destination = plugin.IsInstalled && Directory.Exists(plugin.InstalledFolder)
            ? plugin.InstalledFolder
            : Path.Combine(deckyPluginsPath, plugin.FolderName);
        var source = plugin.SourceFolder;

        if (!string.IsNullOrWhiteSpace(plugin.ReleaseZipUrl))
        {
            var zip = await DownloadReleaseAsync(plugin);
            source = ExtractPluginZip(zip);
        }
        else if (!Directory.Exists(source) && plugin.InstallerZip is not null)
        {
            source = ExtractPluginZip(plugin.InstallerZip);
        }

        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException($"Non trovo i file installabili per {plugin.Name}.");
        }

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

        if (File.Exists(Path.Combine(extractRoot, "plugin.json")))
        {
            return extractRoot;
        }

        return Directory.EnumerateDirectories(extractRoot)
            .FirstOrDefault(dir => File.Exists(Path.Combine(dir, "plugin.json")))
            ?? extractRoot;
    }

    private static async Task<string> DownloadReleaseAsync(DeckyPluginInfo plugin)
    {
        Directory.CreateDirectory(AppPaths.DownloadsRoot);
        var fileName = Path.GetFileName(new Uri(plugin.ReleaseZipUrl!).AbsolutePath);
        var target = Path.Combine(AppPaths.DownloadsRoot, $"{plugin.RepositoryName}-{fileName}");
        await using var input = await Http.GetStreamAsync(plugin.ReleaseZipUrl);
        await using var output = File.Create(target);
        await input.CopyToAsync(output);
        return target;
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

using System.IO.Compression;
using Playhub.Models;
using Playhub.Services;

var root = Path.Combine(Path.GetTempPath(), "playhub-bundle-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
AppPaths.TestDownloads = Path.Combine(root, "downloads");
var count = 0;
void Check(bool value, string description) { if (!value) throw new Exception(description); count++; Console.WriteLine("PASS " + description); }
string Zip(string name, string version, bool complete, char separator = '/')
{
    var path = Path.Combine(root, name + ".zip");
    using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
    void Entry(string name, string body) { using var output = new StreamWriter(zip.CreateEntry(name.Replace('/', separator)).Open()); output.Write(body); }
    Entry("Plugin/plugin.json", "{\"name\":\"Fixture\"}");
    Entry("Plugin/package.json", "{\"version\":\"" + version + "\"}");
    if (complete) Entry("Plugin/dist/index.js", "fixture compiled frontend");
    return path;
}
try
{
    var source = Path.Combine(root, "zip-only"); Directory.CreateDirectory(source);
    var plugin = new DeckyPluginInfo { SourceFolder = source, InstallerZip = Zip("current", "1.8.3", true) };
    var payload = DeckyPluginService.FindBundledPayload(plugin);
    Check(payload is { IsZip: true, Version: "1.8.3" }, "Existing ZIP-only folder selects complete current archive");
    plugin.FolderName = "installed-fixture";
    plugin.Version = "9.0.0"; // Stale catalog metadata must not label fallback bytes.
    await new DeckyPluginService().InstallOrUpdateAsync(plugin, Path.Combine(root, "installed"));
    Check(File.Exists(Path.Combine(plugin.InstalledFolder, "dist", "index.js")) && plugin.InstalledVersion == "1.8.3",
        "Public offline install extracts archive and records actual installed version");
    Check(File.ReadAllText(Path.Combine(plugin.InstalledFolder, ".playhub-release.json")).Contains("1.8.3"),
        "Installed release provenance matches bundled bytes");
    File.WriteAllText(Path.Combine(source, "plugin.json"), "{\"version\":\"9.0.0\"}");
    Check(DeckyPluginService.FindBundledPayload(plugin)?.Version == "1.8.3", "Incomplete manifest folder cannot mask current archive");
    plugin.InstallerZip = Zip("windows", "2.5.3", true, '\\');
    Check(DeckyPluginService.FindBundledPayload(plugin)?.Version == "2.5.3", "Windows ZIP separators support complete payload discovery");
    plugin.InstallerZip = Zip("incomplete", "5.0.0", false);
    Check(DeckyPluginService.FindBundledPayload(plugin) is null, "Manifest-only archive and source are rejected");
    var incompleteRejected = false;
    try { await new DeckyPluginService().InstallOrUpdateAsync(new DeckyPluginInfo { SourceFolder = source,
        InstallerZip = plugin.InstallerZip, FolderName = "rejected" }, Path.Combine(root, "installed")); }
    catch (DirectoryNotFoundException) { incompleteRejected = true; }
    Check(incompleteRejected && !Directory.Exists(Path.Combine(root, "installed", "rejected")),
        "Public offline install rejects incomplete payload before copying destination");
    Directory.CreateDirectory(Path.Combine(source, "dist")); File.WriteAllText(Path.Combine(source, "dist", "index.js"), "compiled");
    File.WriteAllText(Path.Combine(source, "package.json"), "{\"version\":\"1.8.2\"}");
    Check(DeckyPluginService.FindBundledPayload(plugin) is { IsZip: false, Version: "1.8.2" }, "Complete directory provides actual package provenance");
    File.WriteAllText(plugin.InstallerZip!, "corrupt archive fixture");
    Check(DeckyPluginService.FindBundledPayload(plugin) is { IsZip: false, Version: "1.8.2" },
        "Corrupt local archive does not block complete source or online fallback");
    Check(DeckyPluginService.ShouldUseBundledVersion("1.8.3", "v1.8.2"), "Older online release never downgrades bundled payload");
    Check(DeckyPluginService.ShouldUseBundledVersion("1.8.4", "1.8.3"), "Unpublished Metadata fix remains ahead of the public release");
    Check(DeckyPluginService.ShouldUseBundledVersion("2.5.7", "2.5.6"), "Unpublished Launch Curtain fix remains ahead of the public release");
    Check(DeckyPluginService.ShouldUseBundledVersion("2.1.1", "v2.1.0"), "Remote Weather 2.1.0 cannot replace the bundled refresh fix");
    Check(!DeckyPluginService.ShouldUseBundledVersion("1.8.3", "1.9.0"), "Newer online release retains priority");
    Check(DeckyPluginService.ShouldUseBundledVersion("1.8.3", "v1.8.3"), "Equal online release uses local bytes");
    Check(!DeckyPluginService.ShouldUseBundledVersion("1.8.3-beta", "1.8.3"), "Prerelease bundle cannot displace stable release");
    Check(!DeckyPluginService.ShouldUseBundledVersion("1.8.3", "unknown"), "Unknown remote version preserves online resolution");
    foreach (var pair in args.Chunk(2))
    {
        var real = DeckyPluginService.FindBundledPayload(new DeckyPluginInfo { InstallerZip = pair[0] });
        Check(real is { IsZip: true } && real.Version == pair[1], "Prepared release archive validates " + pair[1]);
        if (Path.GetFileName(pair[0]) == "Discord_Installer-1.0.0.zip")
        {
            var local = new DeckyPluginInfo { InstallerZip = pair[0], FolderName = "playhub-discord",
                RepositoryName = "Discord", RepositorySlug = "", CatalogSource = "bundled-local" };
            await new DeckyPluginService().InstallOrUpdateAsync(local, Path.Combine(root, "local-install"));
            Check(local.IsInstalled && local.InstalledVersion == "1.0.0" &&
                File.Exists(Path.Combine(local.InstalledFolder, "dist", "index.js")) &&
                File.Exists(Path.Combine(local.InstalledFolder, "runtime", "Vesktop-1.6.8-win.zip")) &&
                Directory.GetFiles(Path.Combine(local.InstalledFolder, "runtime", "sources")).Length >= 3,
                "Standalone Discord installs offline with bundled runtime and corresponding sources");
            Check(File.Exists(Path.Combine(local.InstalledFolder, "LICENSE")) &&
                File.ReadAllText(Path.Combine(local.InstalledFolder, ".playhub-release.json")).Contains("Discord"),
                "Standalone install keeps original license and local fork identity");
        }
    }
    Console.WriteLine($"{count}/{count} bundled payload checks passed.");
}
finally { Directory.Delete(root, recursive: true); }

namespace Playhub.Services
{
    internal static class AppPaths { public static string TestDownloads { get; set; } = ""; public static string DownloadsRoot => TestDownloads; }
}

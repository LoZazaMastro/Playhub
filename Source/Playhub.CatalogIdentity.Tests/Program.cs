using Playhub.Models;
using Playhub.Services;
using System.Text.Json;

// Standalone regression contract: links source, never builds/runs the main app.
// Fixtures are synthetic; a failure does not identify the user's installed metadata.
var catalog = RemotePluginCatalogService.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "catalog.json")));
var windows = catalog.Plugins.Single(p => p.Repository == "LoZazaMastro/ThemeDeck-Windows");
var linux = catalog.Plugins.Single(p => p.Repository == "BrenticusMaximus/ThemeDeck");
var decky = catalog.Plugins.First(p => p.CatalogSource == "decky-store");
var failures = 0;
var total = 0;
await Run("Windows declared repository wins over shared name", async () =>
    await Installed(new[] { linux, windows }, "themedeck", "ThemeDeck", windows.Repository, "", windows));
await Run("Linux declared repository remains a separate project", async () =>
    await Installed(new[] { windows, linux }, "ThemeDeck-Linux", "ThemeDeck", linux.Repository, "", linux));
await Run("Windows canonical folder wins before Linux short-name alias", async () =>
    await Installed(new[] { linux, windows }, "themedeck", "ThemeDeck", "", "", windows));
await Run("Upstream cover image cannot claim Windows ThemeDeck", async () =>
    await Installed(new[] { windows, linux }, "themedeck", "ThemeDeck", "",
        "https://raw.githubusercontent.com/BrenticusMaximus/ThemeDeck/main/cover.png", windows));
await Run("Store plugin keeps Decky provenance with unrelated image repository", async () =>
    await Installed(new[] { decky }, decky.InstallFolder, decky.Name, "",
        "https://raw.githubusercontent.com/example/shared-art/main/cover.png", decky));
await Run("Declared unknown repository must not borrow Decky provenance", async () =>
{
    using var files = new Fixture(decky.InstallFolder, decky.Name, "someone/independent-fork", "");
    var loaded = await new PluginCatalogService().LoadAsync(files.Bundled, files.Installed,
        catalog with { Plugins = new[] { decky } });
    var actual = loaded.Single(p => p.IsInstalled);
    Check(actual.RepositorySlug == "someone/independent-fork" && actual.CatalogSource == "installed",
        "An explicit different repository was treated as Decky Store.");
});
foreach (var markerField in new[] { "repository", "repositorySlug", "repositoryUrl" })
{
    await Run($"Trusted marker {markerField} overrides inherited upstream metadata", async () =>
    {
        using var files = new Fixture("themedeck", "ThemeDeck", linux.Repository, "");
        files.PackageRepository(linux.Repository);
        files.Marker(markerField, markerField == "repositoryUrl" ? windows.RepositoryUrl : windows.Repository);
        var loaded = await new PluginCatalogService().LoadAsync(files.Bundled, files.Installed,
            catalog with { Plugins = new[] { linux, windows } });
        var actual = loaded.Single(p => p.IsInstalled);
        Check(actual.RepositorySlug == windows.Repository && actual.CatalogSource == "playhub",
            "Inherited upstream metadata overrode the trusted Windows marker.");
    });
}
await Run("Windows package repository is authoritative without a marker", async () =>
{
    using var files = new Fixture("renamed-folder", "ThemeDeck", "", "");
    files.PackageRepository(windows.Repository);
    var loaded = await new PluginCatalogService().LoadAsync(files.Bundled, files.Installed,
        catalog with { Plugins = new[] { linux, windows } });
    Check(loaded.Single(p => p.IsInstalled).RepositorySlug == windows.Repository,
        "Windows package repository was not respected.");
});
await Run("Declared Linux repository wins even in the Windows canonical folder", async () =>
    await Installed(new[] { windows, linux }, "themedeck", "ThemeDeck", linux.Repository, "", linux));
await Run("Unique Decky alias preserves Store source and ID", async () =>
    await Installed(new[] { decky }, "renamed-folder", decky.Name, "", "", decky));
foreach (var entries in new[] { new[] { windows, linux }, new[] { linux, windows } })
{
    await Run("Ambiguous ThemeDeck alias stays unmatched in either catalog order", async () =>
    {
        using var files = new Fixture("renamed-folder", "ThemeDeck", "", "");
        var loaded = await new PluginCatalogService().LoadAsync(files.Bundled, files.Installed,
            catalog with { Plugins = entries });
        Check(loaded.Single(p => p.IsInstalled).CatalogSource == "installed" &&
            loaded.Where(p => p.RepositorySlug == windows.Repository || p.RepositorySlug == linux.Repository)
                .All(p => !p.IsInstalled), "An ambiguous alias guessed a catalog identity.");
    });
}
await Run("Ambiguous exact folder does not fall through to aliases", async () =>
{
    using var files = new Fixture("themedeck", "ThemeDeck", "", "");
    var loaded = await new PluginCatalogService().LoadAsync(files.Bundled, files.Installed,
        catalog with { Plugins = new[] { windows, linux with { InstallFolder = "themedeck" } } });
    Check(loaded.Single(p => p.IsInstalled).CatalogSource == "installed",
        "An ambiguous exact folder guessed a catalog identity.");
});
await Run("Unknown trusted marker cannot fall back to an inherited catalog repository", async () =>
{
    using var files = new Fixture("themedeck", "ThemeDeck", linux.Repository, "");
    files.Marker("repository", "another/windows-fork");
    var loaded = await new PluginCatalogService().LoadAsync(files.Bundled, files.Installed,
        catalog with { Plugins = new[] { windows, linux } });
    Check(loaded.Single(p => p.IsInstalled).RepositorySlug == "another/windows-fork",
        "A trusted marker miss fell back to the inherited repository.");
});
Console.WriteLine($"{total - failures}/{total} passed; {failures} failed.");
return failures == 0 ? 0 : 1;

async Task Run(string name, Func<Task> action)
{
    total++;
    try { await action(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}

async Task Installed(RemotePluginCatalogEntry[] entries, string folder, string name,
    string repository, string image, RemotePluginCatalogEntry expected)
{
    using var files = new Fixture(folder, name, repository, image);
    var loaded = await new PluginCatalogService().LoadAsync(files.Bundled, files.Installed,
        catalog with { Plugins = entries });
    var actual = loaded.Single(p => p.IsInstalled);
    Check(actual.RepositorySlug == expected.Repository && actual.CatalogSource == expected.CatalogSource &&
        actual.CatalogPluginId == expected.CatalogPluginId,
        $"Expected {expected.Repository} [{expected.CatalogSource}], got {actual.RepositorySlug} [{actual.CatalogSource}].");
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

internal sealed class Fixture : IDisposable
{
    private static readonly string Parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Playhub.CatalogIdentity.Tests"));
    private readonly string root = Path.Combine(Parent, Guid.NewGuid().ToString("N"));
    internal string Bundled => Path.Combine(root, "bundled");
    internal string Installed => Path.Combine(root, "installed");
    private readonly string target;

    internal Fixture(string folder, string name, string repository, string image)
    {
        Directory.CreateDirectory(Bundled);
        target = Path.Combine(Installed, folder);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "plugin.json"), JsonSerializer.Serialize(new
        {
            name, version = "1.0.0", repository, publish = new { image }
        }));
    }

    internal void PackageRepository(string repository) =>
        File.WriteAllText(Path.Combine(target, "package.json"), JsonSerializer.Serialize(new
        {
            name = "themedeck", version = "3.3.2",
            repository = new { type = "git", url = $"git+https://github.com/{repository}.git" }
        }));

    internal void Marker(string field, string repository) =>
        File.WriteAllText(Path.Combine(target, ".playhub-release.json"), JsonSerializer.Serialize(
            new Dictionary<string, string> { [field] = repository, ["version"] = "3.3.2" }));

    public void Dispose()
    {
        if (!Path.GetFullPath(root).StartsWith(Parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unsafe fixture cleanup path.");
        Directory.Delete(root, recursive: true);
    }
}

namespace Playhub.Services
{
    internal static class AppPaths
    {
        public static string LocalDataRoot => throw new InvalidOperationException("Tests must not access application user data.");
    }
}

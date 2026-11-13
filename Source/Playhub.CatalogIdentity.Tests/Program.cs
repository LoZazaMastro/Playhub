using Playhub.Models;
using Playhub.Services;
using System.IO.Compression;
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
await Run("Local Discord candidate is separate and has no fictional release repository", async () =>
{
    using var files = new Fixture("another-plugin", "Another plugin", "", "");
    files.DiscordBundle();
    var loaded = await new PluginCatalogService().LoadAsync(files.Bundled, files.Installed,
        catalog with { Plugins = Array.Empty<RemotePluginCatalogEntry>() });
    var discord = loaded.Single(p => p.CatalogSource == "bundled-local");
    Check(discord.Name == "Discord" && discord.FolderName == "playhub-discord" && discord.Version == "1.0.0" &&
        discord.RepositorySlug == "" && discord.RepositoryUrl == "" && discord.CatalogReleaseZipUrl is null &&
        discord.IsPlayhubPlugin && !discord.IsInstalled && discord.InstallerZip is not null,
        "The local candidate borrowed a public release identity or did not expose its bundle.");
    Check(PluginCatalogService.LocalizedShortDescription(discord, "it") == discord.ShortDescription,
        "Original English developer copy was translated.");
});
await Run("Installed Playhub Discord fork retains local identity despite upstream credits", async () =>
{
    using var files = new Fixture("playhub-discord", "Discord", "Necrosiak/Steamcord", "");
    files.DiscordBundle();
    files.DiscordPackage();
    var upstream = windows with { Name = "Steamcord", Repository = "Necrosiak/Steamcord", InstallFolder = "Steamcord" };
    var loaded = await new PluginCatalogService().LoadAsync(files.Bundled, files.Installed,
        catalog with { Plugins = new[] { upstream } });
    Check(loaded.Count(p => p.IsInstalled) == 1 && loaded.Single(p => p.IsInstalled).CatalogSource == "bundled-local" &&
        !loaded.Single(p => p.RepositorySlug == upstream.Repository).IsInstalled,
        "The installed Windows fork was mistaken for the original Steamcord plugin.");
});
foreach (var installedName in new[] { "Discord", "disCORD", "renamed-discord" })
{
    await Run("Discord fork retains its actual installed directory " + installedName, async () =>
    {
        using var files = new Fixture(installedName, "Discord", "Necrosiak/Steamcord", "");
        files.DiscordBundle(); files.AddPlugin(installedName, "Discord", packageName: "playhub-discord");
        var loaded = await new PluginCatalogService().LoadAsync(files.Bundled, files.Installed,
            catalog with { Plugins = Array.Empty<RemotePluginCatalogEntry>() });
        var actual = loaded.Single(p => p.CatalogSource == "bundled-local");
        Check(loaded.Count(p => p.IsInstalled) == 1 && actual.IsInstalled && actual.FolderName == installedName &&
            actual.InstalledFolder == Path.Combine(files.Installed, installedName) && actual.InstalledVersion == "0.9.0" &&
            actual.RepositorySlug == "" && actual.HasUpdate,
            "The installed fork was duplicated, renamed or assigned the upstream release channel.");
    });
}
await Run("One verified Discord fork wins without touching an unrelated canonical folder", async () =>
{
    using var files = new Fixture("Discord", "Discord", "Necrosiak/Steamcord", "");
    files.DiscordBundle(); files.DiscordPackage();
    files.AddPlugin("playhub-discord", "Other software", packageName: "another-package");
    var loaded = await new PluginCatalogService().LoadAsync(files.Bundled, files.Installed,
        catalog with { Plugins = Array.Empty<RemotePluginCatalogEntry>() });
    var actual = loaded.Single(p => p.CatalogSource == "bundled-local");
    Check(actual.IsInstalled && actual.FolderName == "Discord" && loaded.Count(p => p.IsInstalled) == 2 &&
        loaded.Single(p => p.FolderName == "playhub-discord").CatalogSource == "installed",
        "An unrelated occupied canonical folder was adopted or overwritten.");
});
await Run("Duplicate verified Discord forks never offer a third installation", async () =>
{
    using var files = new Fixture("Discord", "Discord", "Necrosiak/Steamcord", "");
    files.DiscordBundle(); files.DiscordPackage();
    files.AddPlugin("second-discord", "Discord", packageName: "playhub-discord");
    var loaded = await new PluginCatalogService().LoadAsync(files.Bundled, files.Installed,
        catalog with { Plugins = Array.Empty<RemotePluginCatalogEntry>() });
    Check(loaded.All(p => p.CatalogSource != "bundled-local") && loaded.Count(p => p.IsInstalled) == 2,
        "Ambiguous fork directories were guessed or a third install was offered.");
});
await Run("Discord display name and an unknown package never adopt the Windows fork", async () =>
{
    using var files = new Fixture("Discord", "Discord", "", "");
    files.DiscordBundle(); files.DiscordPackage("another-discord");
    var loaded = await new PluginCatalogService().LoadAsync(files.Bundled, files.Installed,
        catalog with { Plugins = Array.Empty<RemotePluginCatalogEntry>() });
    Check(!loaded.Single(p => p.CatalogSource == "bundled-local").IsInstalled &&
        loaded.Single(p => p.IsInstalled).FolderName == "Discord",
        "An unknown package was adopted from its display name alone.");
});
await Run("A file occupying the canonical Discord destination blocks installation", async () =>
{
    using var files = new Fixture("another-plugin", "Another plugin", "", "");
    files.DiscordBundle();
    File.WriteAllText(Path.Combine(files.Installed, "playhub-discord"), "owned fixture content");
    var loaded = await new PluginCatalogService().LoadAsync(files.Bundled, files.Installed,
        catalog with { Plugins = Array.Empty<RemotePluginCatalogEntry>() });
    Check(loaded.All(p => p.CatalogSource != "bundled-local") &&
        File.ReadAllText(Path.Combine(files.Installed, "playhub-discord")) == "owned fixture content",
        "An occupied file destination was exposed as installable.");
});
await Run("A linked canonical Discord folder is never adopted or overwritten", async () =>
{
    using var files = new Fixture("Discord", "Discord", "", "");
    files.DiscordBundle(); files.DiscordPackage();
    Directory.CreateSymbolicLink(Path.Combine(files.Installed, "playhub-discord"), Path.Combine(files.Installed, "Discord"));
    var loaded = await new PluginCatalogService().LoadAsync(files.Bundled, files.Installed,
        catalog with { Plugins = Array.Empty<RemotePluginCatalogEntry>() });
    var actual = loaded.Single(p => p.CatalogSource == "bundled-local");
    Check(actual.IsInstalled && actual.FolderName == "Discord" &&
        actual.InstalledFolder != Path.Combine(files.Installed, "playhub-discord"),
        "A linked directory became the fork's update destination.");
});
await Run("A canonical Discord junction to an external fork blocks a new install", async () =>
{
    using var files = new Fixture("another-plugin", "Another plugin", "", "");
    files.DiscordBundle();
    var external = Path.Combine(files.Bundled, "external-installed-fork");
    Directory.CreateDirectory(external);
    File.WriteAllText(Path.Combine(external, "plugin.json"), "{\"name\":\"Discord\"}");
    File.WriteAllText(Path.Combine(external, "package.json"), "{\"name\":\"playhub-discord\",\"version\":\"0.9.0\"}");
    Directory.CreateSymbolicLink(Path.Combine(files.Installed, "playhub-discord"), external);
    var loaded = await new PluginCatalogService().LoadAsync(files.Bundled, files.Installed,
        catalog with { Plugins = Array.Empty<RemotePluginCatalogEntry>() });
    Check(loaded.All(p => p.CatalogSource != "bundled-local"), "A linked fork was exposed as a managed update or overwrite destination.");
});
await Run("Linked package metadata cannot authorize updating the canonical Discord folder", async () =>
{
    using var files = new Fixture("playhub-discord", "Discord", "", "");
    files.DiscordBundle();
    var external = Path.Combine(files.Bundled, "external-package.json");
    File.WriteAllText(external, "{\"name\":\"playhub-discord\",\"version\":\"0.9.0\"}");
    File.CreateSymbolicLink(Path.Combine(files.Installed, "playhub-discord", "package.json"), external);
    var loaded = await new PluginCatalogService().LoadAsync(files.Bundled, files.Installed,
        catalog with { Plugins = Array.Empty<RemotePluginCatalogEntry>() });
    Check(loaded.All(p => p.CatalogSource != "bundled-local"), "Linked metadata became ownership evidence.");
});
await Run("Unrelated Discord name cannot claim the bundled Windows fork", async () =>
{
    using var files = new Fixture("another-discord", "Discord", "", "");
    files.DiscordBundle();
    var loaded = await new PluginCatalogService().LoadAsync(files.Bundled, files.Installed,
        catalog with { Plugins = Array.Empty<RemotePluginCatalogEntry>() });
    Check(!loaded.Single(p => p.CatalogSource == "bundled-local").IsInstalled &&
        loaded.Single(p => p.IsInstalled).CatalogSource == "installed",
        "A shared display name claimed the bundled fork.");
});
await Run("Foreign canonical folder is preserved instead of offering overwrite", async () =>
{
    using var files = new Fixture("playhub-discord", "Other software", "", "");
    files.DiscordBundle();
    var loaded = await new PluginCatalogService().LoadAsync(files.Bundled, files.Installed,
        catalog with { Plugins = Array.Empty<RemotePluginCatalogEntry>() });
    Check(loaded.All(p => p.CatalogSource != "bundled-local") && loaded.Single().IsInstalled,
        "An unrelated destination was exposed to a destructive install.");
});
await Run("Incomplete local Discord archive is never installable", async () =>
{
    using var files = new Fixture("another-plugin", "Another plugin", "", "");
    files.DiscordBundle(complete: false);
    var loaded = await new PluginCatalogService().LoadAsync(files.Bundled, files.Installed,
        catalog with { Plugins = Array.Empty<RemotePluginCatalogEntry>() });
    Check(loaded.All(p => p.CatalogSource != "bundled-local"), "Incomplete bundle became installable.");
});
foreach (var bundleState in new[] { "missing", "incomplete", "corrupt" })
{
    await Run("Installed Discord never becomes Steamcord with " + bundleState + " bundle", async () =>
    {
        using var files = new Fixture("Discord", "Discord", "", "");
        files.DiscordPackage();
        if (bundleState != "missing") files.DiscordBundle(complete: false);
        if (bundleState == "corrupt")
            File.WriteAllText(Path.Combine(files.Bundled, "Discord", "Discord_Installer-1.0.0.zip"), "broken archive");
        files.Marker("repository", "Necrosiak/Steamcord");
        var upstream = windows with { Name = "Steamcord", Repository = "Necrosiak/Steamcord", InstallFolder = "Steamcord" };
        var loaded = await new PluginCatalogService().LoadAsync(files.Bundled, files.Installed,
            catalog with { Plugins = new[] { upstream } });
        var actual = loaded.Single(p => p.IsInstalled);
        Check(actual.Name == "Discord" && actual.FolderName == "Discord" && actual.IsPlayhubPlugin &&
            actual.RepositorySlug == "" && actual.RepositoryUrl == "" && !actual.HasUpdate &&
            actual.CatalogReleaseZipUrl is null && actual.InstallerZip is null &&
            !loaded.Single(p => p.RepositorySlug == upstream.Repository).IsInstalled,
            "The Windows fork inherited Steamcord identity, update channel or installation destination.");
    });
}
await Run("A genuine Steamcord install stays upstream beside the Discord fork", async () =>
{
    using var files = new Fixture("Discord", "Discord", "Necrosiak/Steamcord", "");
    files.DiscordPackage();
    files.AddPlugin("Steamcord", "Steamcord", packageName: "steamcord");
    var upstream = windows with { Name = "Steamcord", Repository = "Necrosiak/Steamcord", InstallFolder = "Steamcord" };
    var loaded = await new PluginCatalogService().LoadAsync(files.Bundled, files.Installed,
        catalog with { Plugins = new[] { upstream } });
    Check(loaded.Count(p => p.IsInstalled) == 2 &&
        loaded.Single(p => p.RepositorySlug == upstream.Repository).InstalledFolder == Path.Combine(files.Installed, "Steamcord") &&
        loaded.Single(p => p.Name == "Discord").InstalledFolder == Path.Combine(files.Installed, "Discord"),
        "The Windows fork and genuine Steamcord shared an installation identity.");
});
await Run("Duplicate Discord forks never borrow Steamcord when bundled installation is ambiguous", async () =>
{
    using var files = new Fixture("Discord", "Discord", "Necrosiak/Steamcord", "");
    files.DiscordBundle(); files.DiscordPackage();
    files.AddPlugin("second-discord", "Discord", packageName: "playhub-discord");
    var upstream = windows with { Name = "Steamcord", Repository = "Necrosiak/Steamcord", InstallFolder = "Steamcord" };
    var loaded = await new PluginCatalogService().LoadAsync(files.Bundled, files.Installed,
        catalog with { Plugins = new[] { upstream } });
    Check(loaded.Count(p => p.IsInstalled) == 2 && loaded.Where(p => p.IsInstalled).All(p => p.Name == "Discord") &&
        !loaded.Single(p => p.RepositorySlug == upstream.Repository).IsInstalled,
        "An ambiguous local fork was assigned the upstream plugin's updates.");
});
await Run("Published Discord replaces its local bundle and preserves Steamcord identity", async () =>
{
    using var files = new Fixture("Discord", "Discord", "Necrosiak/Steamcord", "");
    files.DiscordBundle(); files.DiscordPackage();
    var published = catalog.Plugins.Single(p => p.Repository == "LoZazaMastro/Playhub-Discord");
    var upstream = windows with { Name = "Steamcord", Repository = "Necrosiak/Steamcord", InstallFolder = "Steamcord" };
    var loaded = await new PluginCatalogService().LoadAsync(files.Bundled, files.Installed,
        catalog with { Plugins = new[] { published, upstream } });
    var actual = loaded.Single(p => p.RepositorySlug == published.Repository);
    Check(loaded.Count(p => p.Name == "Discord") == 1 && actual.IsInstalled &&
        actual.InstalledFolder == Path.Combine(files.Installed, "Discord") &&
        !loaded.Single(p => p.RepositorySlug == upstream.Repository).IsInstalled,
        "The published Windows fork was duplicated or assigned Steamcord identity.");
    Check(actual.CatalogReleaseZipUrl == published.CatalogReleaseUrl &&
        PluginCatalogService.LocalizedShortDescription(actual, "it") == published.ShortDescription,
        "Published release or original English copy was replaced.");
    Check(PluginChronology.News(loaded, DateTimeOffset.Parse("2026-11-13T14:00:00Z"))[0] == actual,
        "Discord did not use its real first publication in News.");
});
await Run("Published Discord adopts a verified renamed folder without a local bundle", async () =>
{
    using var files = new Fixture("renamed-discord", "Discord", "Necrosiak/Steamcord", "");
    files.DiscordPackage();
    var published = catalog.Plugins.Single(p => p.Repository == "LoZazaMastro/Playhub-Discord");
    var loaded = await new PluginCatalogService().LoadAsync(files.Bundled, files.Installed,
        catalog with { Plugins = new[] { published } });
    Check(loaded.Count == 1 && loaded.Single().RepositorySlug == published.Repository &&
        loaded.Single().IsInstalled && loaded.Single().InstalledFolder == Path.Combine(files.Installed, "renamed-discord"),
        "A verified installed Windows fork lost its published identity.");
});
await Run("Published Discord never adopts an unrelated Discord package", async () =>
{
    using var files = new Fixture("Discord", "Discord", "", "");
    files.DiscordPackage("another-discord");
    var published = catalog.Plugins.Single(p => p.Repository == "LoZazaMastro/Playhub-Discord");
    var loaded = await new PluginCatalogService().LoadAsync(files.Bundled, files.Installed,
        catalog with { Plugins = new[] { published } });
    Check(!loaded.Single(p => p.RepositorySlug == published.Repository).IsInstalled,
        "A shared display name adopted an unrelated client.");
});
await Run("Pinned Discord survives shuffled featured selection with three suite plugins", () =>
{
    var suite = Enumerable.Range(0, 12).Select(i => new DeckyPluginInfo {
        Name = "Suite " + i, RepositorySlug = "owner/suite" + i, IsPlayhubPlugin = true }).ToArray();
    var discord = new DeckyPluginInfo { Name = "Discord", RepositorySlug = "LoZazaMastro/Playhub-Discord",
        IsPlayhubPlugin = true, Keywords = "discord voice featured" };
    var external = Enumerable.Range(0, 8).Select(i => new DeckyPluginInfo {
        Name = "Decky " + i, RepositorySlug = "owner/decky" + i, IsPlayhubPlugin = false }).ToArray();
    var random = new Random(8417);
    for (var pass = 0; pass < 1000; pass++)
    {
        var input = suite.Append(discord).Concat(external).OrderBy(_ => random.Next()).ToArray();
        var selected = PluginFeaturedSelection.Select(input);
        Check(selected.Count == 8 && selected.First() == discord &&
            selected.Count(p => p.IsPlayhubPlugin) == 3 &&
            selected.Select(PluginChronology.Identity).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 8,
            "Featured rotation lost the pin, suite quota or unique identity.");
    }
    Check(!PluginFeaturedSelection.IsPinned(new DeckyPluginInfo { Keywords = "notfeatured featured-suffix" }) &&
        PluginFeaturedSelection.IsPinned(new DeckyPluginInfo { Keywords = "FEATURED" }),
        "Pin matching must use an exact case-insensitive keyword.");
    Check(PluginFeaturedSelection.Select(new[] { discord, discord }).Count == 1,
        "A pinned duplicate created two slides.");
    return Task.CompletedTask;
});
await Run("Weather candidate keeps its actual newer bundled version", async () =>
{
    using var files = new Fixture("weather", "Weather", "LoZazaMastro/Weather", "");
    var bundled = Path.Combine(files.Bundled, "Weather");
    Directory.CreateDirectory(bundled);
    File.WriteAllText(Path.Combine(bundled, "release-info.json"), "{\"version\":\"2.1.1\"}");
    var weather = catalog.Plugins.Single(p => p.Repository == "LoZazaMastro/Weather") with { Version = "2.1.0" };
    var loaded = await new PluginCatalogService().LoadAsync(files.Bundled, files.Installed,
        catalog with { Plugins = new[] { weather } });
    Check(loaded.Single().Version == "2.1.1" && loaded.Single().HasUpdate,
        "Old remote catalog metadata masked the local Weather fix.");
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

    internal void DiscordPackage(string name = "playhub-discord") => File.WriteAllText(Path.Combine(target, "package.json"),
        JsonSerializer.Serialize(new { name, version = "0.9.0", repository = new { url = "git+https://github.com/Necrosiak/Steamcord.git" } }));

    internal void AddPlugin(string folder, string name, string packageName)
    {
        var path = Path.Combine(Installed, folder);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "plugin.json"), JsonSerializer.Serialize(new { name, version = "0.9.0" }));
        File.WriteAllText(Path.Combine(path, "package.json"), JsonSerializer.Serialize(new { name = packageName, version = "0.9.0" }));
    }

    internal void DiscordBundle(bool complete = true)
    {
        var folder = Path.Combine(Bundled, "Discord");
        Directory.CreateDirectory(folder);
        using var zip = ZipFile.Open(Path.Combine(folder, "Discord_Installer-1.0.0.zip"), ZipArchiveMode.Create);
        void Entry(string path, string text) { using var writer = new StreamWriter(zip.CreateEntry(path).Open()); writer.Write(text); }
        Entry("Discord/plugin.json", "{\"name\":\"Discord\",\"author\":\"LoZazaMastro\",\"publish\":{\"description\":\"Discord voice and conversations in Playhub for Windows.\"}}");
        Entry("Discord/package.json", "{\"name\":\"playhub-discord\",\"version\":\"1.0.0\"}");
        if (complete) Entry("Discord/dist/index.js", "compiled fixture");
    }

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

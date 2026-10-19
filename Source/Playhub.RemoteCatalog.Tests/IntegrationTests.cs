using Playhub.Models;
using Playhub.Services;
using System.Text.Json;
using System.IO.Compression;
using System.Security.Cryptography;

internal static class IntegrationTests
{
    internal static async Task Mapping()
    {
        using var files = new TestFiles();
        using var remote = new Fixture();
        var bundled = PluginCatalogService.GetBundledCatalog();
        Check(bundled.CatalogRevision > 0 && bundled.Plugins.Count == 241, "Combined packaged baseline missing active/tombstone handling.");
        var service = new PluginCatalogService();
        var empty = await service.LoadAsync(files.PluginRoot, files.InstalledRoot);
        Check(empty.Count == 241 && empty.Count(p => p.IsPlayhubPlugin) == 11 &&
            empty.Select(p => p.RepositorySlug).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 241, "Baseline duplicated/lost active built-ins.");

        var artwork = bundled.Plugins.Single(p => p.Repository == "LoZazaMastro/Playhub-Artworks");
        var decky = bundled.Plugins.First(p => p.CatalogSource == "decky-store");
        files.Install(artwork, "1.0.0");
        files.Install(decky, "0.1.0");
        var source = Path.Combine(files.PluginRoot, "Artwork");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "plugin.json"), "{\"name\":\"Playhub Artworks\",\"version\":\"1.0.0\"}");
        var installer = Path.Combine(source, "installer.zip");
        File.WriteAllBytes(installer, new byte[] { 1 });
        var cover = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Assets/PluginCovers/playhub-artworks.png"));
        Directory.CreateDirectory(Path.GetDirectoryName(cover)!);
        File.WriteAllBytes(cover, new byte[] { 1 });

        var own = artwork with { Name = "Remote New", InstallFolder = "remote-new", Repository = "LoZazaMastro/Remote-New",
            RepositoryUrl = "https://github.com/LoZazaMastro/Remote-New", Version = "2.0.0", Aliases = Array.Empty<string>(),
            ReleaseAssetName = "remote.zip", CatalogReleaseUrl = "https://github.com/LoZazaMastro/Remote-New/releases/download/v2/remote.zip" };
        var github = own with { Name = "Community New", InstallFolder = "community-new", Repository = "community/new-plugin",
            RepositoryUrl = "https://github.com/community/new-plugin", CatalogSource = "outside-store", CatalogStatus = "github",
            CatalogReleaseUrl = "https://github.com/community/new-plugin/releases/download/v2/remote.zip" };
        files.Install(own, "1.0.0");
        var updatedDecky = decky with { Version = "9.0.0", LongDescription = "Remote updated description",
            ReleaseAssetName = "updated.zip", CatalogReleaseUrl = "https://cdn.tzatzikiweeb.moe/file/steam-deck-homebrew/versions/updated.zip" };
        var document = new RemotePluginCatalog { CatalogRevision = bundled.CatalogRevision + 1,
            Plugins = new[] { artwork with { Version = "2.0.0" }, updatedDecky, own, github } };
        remote.Handler.Respond = (_, _) => Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
        { Content = new System.Net.Http.ByteArrayContent(Serialize(document)) });
        var refreshed = await remote.Service.RefreshAsync(bundled);
        Check(refreshed.Origin == "remote", refreshed.Error ?? "Remote manifest rejected.");
        var loaded = await service.LoadAsync(files.PluginRoot, files.InstalledRoot, refreshed.Catalog);
        Check(loaded.Count == 243 && loaded.Select(p => p.RepositorySlug).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 243,
            "Remote additions duplicated/lost records.");
        var mappedArtwork = loaded.Single(p => p.RepositorySlug == artwork.Repository);
        Check(mappedArtwork.SourceFolder == source && mappedArtwork.InstallerZip == installer && mappedArtwork.CoverImage == cover &&
            mappedArtwork.IsInstalled && mappedArtwork.InstalledVersion == "1.0.0" && mappedArtwork.HasUpdate,
            "Bundled assets/installed state/update metadata lost.");
        var mappedDecky = loaded.Single(p => p.RepositorySlug == decky.Repository);
        Check(mappedDecky.Version == "9.0.0" && mappedDecky.CatalogReleaseZipUrl == updatedDecky.CatalogReleaseUrl &&
            mappedDecky.LongDescription == updatedDecky.LongDescription && mappedDecky.HasUpdate && mappedDecky.CatalogSource == "decky-store",
            "Decky manifest update not mapped.");
        var mappedOwn = loaded.Single(p => p.RepositorySlug == own.Repository);
        Check(mappedOwn.IsPlayhubPlugin && mappedOwn.CatalogSource == "playhub" && mappedOwn.CatalogStatus == "playhub" &&
            mappedOwn.HasUpdate && mappedOwn.IsInstalled && mappedOwn.CatalogReleaseZipUrl == own.CatalogReleaseUrl,
            "New Playhub provenance/install/update state incorrect.");
        Check(loaded.Single(p => p.RepositorySlug == github.Repository).CatalogSource == "outside-store", "Community source relabelled.");

        var overrideCatalog = RemotePluginCatalogService.Merge(bundled, document with
        { Plugins = new[] { artwork with { CoverUrl = "https://raw.githubusercontent.com/LoZazaMastro/Playhub/main/new.png" } } });
        Check((await service.LoadAsync(files.PluginRoot, files.InstalledRoot, overrideCatalog))
            .Single(p => p.RepositorySlug == artwork.Repository).CoverImage!.StartsWith("https://"), "Explicit cover override ignored.");
        var disabled = RemotePluginCatalogService.Merge(bundled, document with { Plugins = new[] { artwork with { Active = false } } });
        files.Install(own with { Name = "Gaming Mode", InstallFolder = "gaming-mode", Repository = "LoZazaMastro/GamingMode" }, "1.0.0");
        files.Install(own with { Name = "Varta", InstallFolder = "varta", Repository = "owner/Varta" }, "1.0.0");
        var deactivated = await service.LoadAsync(files.PluginRoot, files.InstalledRoot, disabled);
        Check(deactivated.Single(p => p.RepositorySlug == artwork.Repository).CatalogSource == "installed", "Disabled installed plugin stayed catalogued.");
        Check(deactivated.All(p => p.Name is not ("Gaming Mode" or "Varta")), "Integrated/blocked installed entry leaked.");

        File.SetLastWriteTimeUtc(remote.CachePath, remote.Now.AddDays(-1).UtcDateTime);
        remote.Handler.Respond = (_, _) => throw new HttpRequestException("offline");
        var offline = await remote.NewService().LoadCachedAsync(bundled);
        Check((await service.LoadAsync(files.PluginRoot, files.InstalledRoot, offline.Catalog))
            .Single(p => p.RepositorySlug == own.Repository).IsPlayhubPlugin, "Offline baseline lost remote addition.");
    }

    internal static byte[] Serialize(RemotePluginCatalog catalog) => JsonSerializer.SerializeToUtf8Bytes(catalog,
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    internal static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}

internal sealed class TestFiles : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Playhub.RemoteCatalog.Tests", Guid.NewGuid().ToString("N"));
    internal string PluginRoot => Path.Combine(_directory, "bundled");
    internal string InstalledRoot => Path.Combine(_directory, "installed");
    internal TestFiles() { Directory.CreateDirectory(PluginRoot); Directory.CreateDirectory(InstalledRoot); }
    internal void Install(RemotePluginCatalogEntry entry, string version)
    {
        var folder = Path.Combine(InstalledRoot, entry.InstallFolder);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "plugin.json"), JsonSerializer.Serialize(new { name = entry.Name, author = entry.Author }));
        File.WriteAllText(Path.Combine(folder, ".playhub-release.json"), JsonSerializer.Serialize(new { repository = entry.Repository, version }));
    }
    public void Dispose()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Playhub.RemoteCatalog.Tests")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(_directory).StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe fixture path.");
        Directory.Delete(_directory, recursive: true);
    }
}

internal sealed class PinnedUiCatalogFixture : IDisposable
{
    private readonly HttpClient _http;
    internal readonly PinnedHandler Handler = new();
    internal RemotePluginCatalogService Service { get; }
    internal PinnedUiCatalogFixture(string root)
    {
        _http = new HttpClient(Handler);
        Service = new(_http, Path.Combine(root, "remote-catalog.json"), () => DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }
    public void Dispose() => _http.Dispose();

    internal sealed class PinnedHandler : HttpMessageHandler
    {
        private readonly Dictionary<int, (string Name, string Hash, byte[] Bytes)> _packages = new();
        internal int[] Ids = [501];
        internal int OfficialCalls, PackageCalls;
        internal TaskCompletionSource? Entered { get; set; }
        internal TaskCompletionSource? Release { get; set; }
        internal PinnedHandler()
        {
            foreach (var id in new[] { 501, 502 })
            {
                var name = "PrivateUi" + id;
                using var stream = new MemoryStream();
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
                {
                    using (var writer = new StreamWriter(zip.CreateEntry(name + "/plugin.json").Open()))
                        writer.Write(JsonSerializer.Serialize(new { name }));
                    using (var writer = new StreamWriter(zip.CreateEntry(name + "/package.json").Open()))
                        writer.Write(JsonSerializer.Serialize(new { homepage = "https://github.com/fixture/" + name }));
                }
                var bytes = stream.ToArray();
                _packages[id] = (name, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes);
            }
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.AbsoluteUri == "https://plugins.deckbrew.xyz/plugins")
            {
                Interlocked.Increment(ref OfficialCalls);
                if (Entered is { } entered)
                { Entered = null; entered.TrySetResult(); await Release!.Task.WaitAsync(token); }
                var bytes = JsonSerializer.SerializeToUtf8Bytes(Ids.Select(id => new {
                    id, name = _packages[id].Name, author = "Private fixture", description = "Private metadata", visible = true,
                    versions = new[] { new { name = "1.0", hash = _packages[id].Hash, created = "2026-10-10T00:00:00Z" } }
                }));
                return new(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            }
            var package = _packages.Values.FirstOrDefault(value => request.RequestUri.AbsoluteUri ==
                "https://cdn.tzatzikiweeb.moe/file/steam-deck-homebrew/versions/" + value.Hash + ".zip");
            if (package.Bytes is not null)
            { Interlocked.Increment(ref PackageCalls); return new(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(package.Bytes) }; }
            return new(System.Net.HttpStatusCode.NotFound);
        }
    }
}

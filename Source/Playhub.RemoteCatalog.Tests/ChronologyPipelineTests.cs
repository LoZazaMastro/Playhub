using Playhub.Models;
using Playhub.Services;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class ChronologyPipelineTests
{
    private static int _checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
        Console.WriteLine("PASS chronology pipeline: " + message);
    }
    private static DateTimeOffset? Instant(string value) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : null;
    private static DateTimeOffset Utc(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture).ToUniversalTime();

    internal static async Task RunAsync()
    {
        _checks = 0;
        var root = Path.Combine(Path.GetTempPath(), "Playhub.ChronologyPipeline.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var handler = new FixtureHandler();
        using var http = new HttpClient(handler);
        var cache = Path.Combine(root, "catalog.json");
        var baseline = new RemotePluginCatalog { CatalogRevision = 1, Plugins = new[] { handler.Identity } };
        handler.Baseline = baseline;
        RemotePluginCatalogService Service() => new(http, cache, () => DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
        try
        {
            var service = Service();
            var inferred = await service.RefreshAsync(baseline, forceRefresh: true);
            var entry = inferred.Catalog.Plugins.Single(plugin => plugin.CatalogPluginId == FixtureHandler.Id);
            Check(Instant(entry.FirstPublishedAt) == Utc("2020-02-03T04:05:06.123Z"),
                "Missing top-level created falls back to the earliest genuine version timestamp");
            Check(Instant(entry.UpdatedAt) == Utc("2025-10-19T21:30:00.456Z") && entry.Version == "2.0",
                "Missing top-level updated falls back to the latest version timestamp, independent of array order");
            Check(entry.FirstPublishedAt.EndsWith('Z') && entry.UpdatedAt.EndsWith('Z'),
                "Offset and fractional source dates are normalized to UTC ISO metadata");
            Check(entry.ReleaseAssetName == handler.Pin + ".zip" && entry.CatalogReleaseUrl.EndsWith("/" + handler.Pin + ".zip"),
                "Date ingestion preserves the exact official hash-pinned artifact identity");

            handler.Created = "2018-01-01T05:30:00+05:30";
            handler.Updated = "2025-10-20T01:02:03.789+02:00";
            var declared = await service.RefreshAsync(baseline, forceRefresh: true);
            entry = declared.Catalog.Plugins.Single(plugin => plugin.CatalogPluginId == FixtureHandler.Id);
            Check(Instant(entry.FirstPublishedAt) == Utc("2018-01-01T00:00:00Z") &&
                Instant(entry.UpdatedAt) == Utc("2025-10-19T23:02:03.789Z"),
                "Explicit source created and updated fields take precedence over version-derived fallbacks");
            Check(declared.ContentVersion != inferred.ContentVersion && entry.Version == "2.0" && entry.ReleaseAssetName == handler.Pin + ".zip",
                "A date-only upstream change updates the content fingerprint without inventing a release or changing its pin");

            var catalog = new PluginCatalogService(http);
            var pluginRoot = Path.Combine(root, "bundled"); var installedRoot = Path.Combine(root, "installed");
            Directory.CreateDirectory(pluginRoot); Directory.CreateDirectory(installedRoot);
            var mapped = (await catalog.LoadAsync(pluginRoot, installedRoot, declared.Catalog)).Single();
            Check(Instant(mapped.FirstPublishedAt) == Instant(entry.FirstPublishedAt) && Instant(mapped.UpdatedAt) == Instant(entry.UpdatedAt),
                "Actual catalog hydration carries both precise upstream dates into DeckyPluginInfo");
            Check(mapped.RepositorySlug == entry.Repository && mapped.CatalogPluginId == FixtureHandler.Id && !mapped.IsInstalled,
                "Chronology hydration preserves native source identity and actual installation state");
            await catalog.RefreshReleasesAsync(new[] { mapped }, force: true);
            var hydratedAfterRelease = (await catalog.LoadAsync(pluginRoot, installedRoot, declared.Catalog)).Single();
            Check(Instant(hydratedAfterRelease.FirstPublishedAt) == Instant(entry.FirstPublishedAt) &&
                Instant(hydratedAfterRelease.UpdatedAt) == Instant(entry.UpdatedAt),
                "The release-cache refresh preserves newer precise source metadata and does not replace it with display dates");
            var releaseCache = Path.Combine(AppPaths.LocalDataRoot, "cache", "plugin-releases", "decky-" + entry.Repository.Replace('/', '-') + ".json");
            var originalReleaseBytes = File.ReadAllBytes(releaseCache);
            try
            {
                var legacy = JsonNode.Parse(originalReleaseBytes)!.AsObject();
                legacy["PublishedAt"] = "20/10/2025"; legacy["UpdatedAt"] = "20/10/2025"; legacy["FirstPublishedAt"] = "03/04/2018";
                await File.WriteAllTextAsync(releaseCache, legacy.ToJsonString());
                var fromLegacyCache = (await catalog.LoadAsync(pluginRoot, installedRoot, declared.Catalog)).Single();
                Check(Instant(fromLegacyCache.FirstPublishedAt) == Instant(entry.FirstPublishedAt) &&
                    Instant(fromLegacyCache.UpdatedAt) == Instant(entry.UpdatedAt),
                    "Physical legacy cache display dates cannot overwrite precise UTC source metadata during hydration");
            }
            finally { await File.WriteAllBytesAsync(releaseCache, originalReleaseBytes); }

            var calls = handler.Calls;
            var restarted = await Service().LoadCachedAsync(baseline);
            var cachedEntry = restarted.Catalog.Plugins.Single(plugin => plugin.CatalogPluginId == FixtureHandler.Id);
            Check(handler.Calls == calls && Instant(cachedEntry.FirstPublishedAt) == Instant(entry.FirstPublishedAt) &&
                Instant(cachedEntry.UpdatedAt) == Instant(entry.UpdatedAt),
                "A new service restores precise dates from durable cache without making HTTP requests");
            Check(restarted.ContentVersion == declared.ContentVersion,
                "A cache-only restart retains the date-bearing content fingerprint");
            handler.Offline = true;
            var offline = await Service().RefreshAsync(baseline, forceRefresh: true);
            var offlineEntry = offline.Catalog.Plugins.Single(plugin => plugin.CatalogPluginId == FixtureHandler.Id);
            Check(offline.Error is not null && Instant(offlineEntry.FirstPublishedAt) == Instant(entry.FirstPublishedAt) &&
                Instant(offlineEntry.UpdatedAt) == Instant(entry.UpdatedAt),
                "Upstream failure retains last-good chronology alongside truthful fallback status");

            handler.Offline = false; handler.Created = "invalid"; handler.Updated = "invalid";
            var invalidOptional = await service.RefreshAsync(baseline, forceRefresh: true);
            var fallbackEntry = invalidOptional.Catalog.Plugins.Single(plugin => plugin.CatalogPluginId == FixtureHandler.Id);
            Check(Instant(fallbackEntry.FirstPublishedAt) == Utc("2020-02-03T04:05:06.123Z") &&
                Instant(fallbackEntry.UpdatedAt) == Utc("2025-10-19T21:30:00.456Z"),
                "Malformed optional source dates use valid version chronology without discarding the verified plugin");
            handler.Created = "03/04/2018"; handler.Updated = "03/04/2025";
            var ambiguousOptional = await service.RefreshAsync(baseline, forceRefresh: true);
            var ambiguousEntry = ambiguousOptional.Catalog.Plugins.Single(plugin => plugin.CatalogPluginId == FixtureHandler.Id);
            Check(Instant(ambiguousEntry.FirstPublishedAt) == Utc("2020-02-03T04:05:06.123Z") &&
                Instant(ambiguousEntry.UpdatedAt) == Utc("2025-10-19T21:30:00.456Z"),
                "Ambiguous display dates in optional source fields cannot be promoted into fabricated UTC metadata");
            Check(handler.Urls.All(url => url == RemotePluginCatalogService.CatalogUrl || url == FixtureHandler.OfficialUrl ||
                url == FixtureHandler.Cdn + handler.Pin + ".zip" || url.StartsWith("https://raw.githubusercontent.com/SteamDeckHomebrew/", StringComparison.Ordinal)),
                "Every chronology request stays inside the fake transport's expected source endpoints");
            Console.WriteLine($"{_checks} chronology pipeline checks passed with fake HTTP only.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        internal const int Id = 60177;
        internal const string OfficialUrl = "https://plugins.deckbrew.xyz/plugins";
        internal const string Cdn = "https://cdn.tzatzikiweeb.moe/file/steam-deck-homebrew/versions/";
        internal readonly string Pin;
        internal readonly RemotePluginCatalogEntry Identity;
        private readonly byte[] _package;
        internal RemotePluginCatalog Baseline = new();
        internal string? Created, Updated;
        internal bool Offline;
        internal int Calls;
        internal readonly List<string> Urls = new();
        internal FixtureHandler()
        {
            string name = "ChronologyFixture" + Guid.NewGuid().ToString("N")[..8];
            using var stream = new MemoryStream();
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                using (var writer = new StreamWriter(zip.CreateEntry(name + "/plugin.json").Open())) writer.Write(JsonSerializer.Serialize(new { name }));
                using (var writer = new StreamWriter(zip.CreateEntry(name + "/package.json").Open())) writer.Write(JsonSerializer.Serialize(new { homepage = "https://github.com/chronology-fixture/" + name }));
            }
            _package = stream.ToArray(); Pin = Convert.ToHexString(SHA256.HashData(_package)).ToLowerInvariant();
            Identity = new RemotePluginCatalogEntry
            {
                Name = name, InstallFolder = name, Repository = "chronology-fixture/" + name,
                RepositoryUrl = "https://github.com/chronology-fixture/" + name, Author = "Chronology fixture", Version = "1.0",
                Category = "Utility", ShortDescription = "Private date fixture", CatalogSource = "decky-store", CatalogStatus = "decky",
                CatalogPluginId = Id, ReleaseAssetName = Pin + ".zip", CatalogReleaseUrl = Cdn + Pin + ".zip",
                Keywords = new[] { "catalog-added:2026-10-01", "first-release:2010-01-01" }
            };
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Calls++; string url = request.RequestUri!.AbsoluteUri; Urls.Add(url);
            if (Offline) throw new HttpRequestException("Private fixture offline");
            if (url == RemotePluginCatalogService.CatalogUrl)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(IntegrationTests.Serialize(Baseline)) });
            if (url == Cdn + Pin + ".zip")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_package) });
            if (url == OfficialUrl)
            {
                var item = new Dictionary<string, object>
                {
                    ["id"] = Id, ["name"] = Identity.Name, ["author"] = "Chronology fixture", ["description"] = "Private date fixture", ["visible"] = true,
                    ["versions"] = new[]
                    {
                        new { name = "2.0", hash = Pin, created = "2025-10-19T23:30:00.456+02:00" },
                        new { name = "1.0", hash = Pin, created = "2020-02-03T01:05:06.123-03:00" }
                    }
                };
                if (Created is not null) item["created"] = Created;
                if (Updated is not null) item["updated"] = Updated;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(new[] { item })) });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}

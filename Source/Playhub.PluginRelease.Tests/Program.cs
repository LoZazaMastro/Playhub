using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Playhub.Models;
using Playhub.Services;

internal static class Program
{
    private const string DeckyUrl = "https://plugins.deckbrew.xyz/plugins";
    private const string OldNotes = "## Installed release\n\n- Old **fix**.\n";
    private const string NewNotes = "## New release\n\n- New **fix** with `code`.\n\n[Details](https://example.invalid/details)\n";

    public static async Task<int> Main()
    {
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("New GitHub release is detected after refresh and offline hydration", NewGithubRelease),
            ("Equal version with v prefix does not produce an update", EqualVersion),
            ("New markdown notes replace cached installed-release notes", NewMarkdownNotes),
            ("One Decky catalog request supplies authoritative versions and hashes", DeckyVersions),
            ("Offline refresh and a new service retain persisted metadata", OfflineCache)
        };
        var failures = 0;
        foreach (var test in tests)
        {
            try { await test.Run(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception error) { failures++; Console.WriteLine("FAIL " + test.Name + ": " + error); }
        }
        Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed; {failures} failed.");
        return failures == 0 ? 0 : 1;
    }

    private static async Task NewGithubRelease()
    {
        using var fixture = new Fixture();
        var entry = fixture.Install("github");
        fixture.Release(entry, "v2.0.0", NewNotes);
        var initial = await fixture.Load(entry);
        Check(initial.Single().InstalledVersion == "1.0.0" && !initial.Single().HasUpdate, "Initial installed state is wrong.");
        Check(await fixture.Service.RefreshReleasesAsync(initial), "First refresh did not report changed metadata.");
        Check(initial.Single().Version == "1.0.0" && !initial.Single().HasUpdate, "Refresh mutated the caller's models.");
        var refreshed = (await fixture.Load(entry)).Single();
        Check(refreshed.IsInstalled && refreshed.InstalledVersion == "1.0.0" && refreshed.Version == "v2.0.0" && refreshed.HasUpdate,
            "Latest release was not detected or was mistaken for the installed version.");
        Check(refreshed.CatalogReleaseZipUrl == Fixture.Zip(entry, "v2.0.0"), "Release artifact was not hydrated.");
        Check(fixture.Handler.Requests.Count == 1, "GitHub refresh should use one fixture request.");
        Check(Directory.EnumerateFiles(Path.Combine(fixture.CacheRoot, "cache", "plugin-releases"), "*.json").Any(),
            "Refresh did not persist release metadata.");
    }

    private static async Task EqualVersion()
    {
        using var fixture = new Fixture();
        var entry = fixture.Install("equal");
        fixture.Release(entry, "v1.0.0", OldNotes);
        Check(await fixture.Service.RefreshReleasesAsync(await fixture.Load(entry)), "Release metadata was not refreshed.");
        var plugin = (await fixture.Load(entry)).Single();
        Check(plugin.InstalledVersion == "1.0.0" && plugin.Version == "v1.0.0" && !plugin.HasUpdate,
            "An equivalent v-prefixed release produced a false update.");
        Check(plugin.ReleaseNotes == OldNotes && plugin.ReleaseNotesVersion == "v1.0.0", "Equal-version notes were lost.");
        var count = fixture.Handler.Requests.Count;
        Check(!await fixture.Service.RefreshReleasesAsync(new[] { plugin }) && fixture.Handler.Requests.Count == count,
            "Unforced refresh ignored its throttle.");
        Check(!await fixture.Service.RefreshReleasesAsync(new[] { plugin }, force: true), "Identical forced refresh reported a change.");
        Check(fixture.Handler.Requests.Count == count + 1, "Forced refresh did not bypass the throttle.");
    }

    private static async Task NewMarkdownNotes()
    {
        using var fixture = new Fixture();
        var entry = fixture.Install("notes");
        fixture.Release(entry, "1.0.0", OldNotes);
        await fixture.Service.RefreshReleasesAsync(await fixture.Load(entry));
        var installed = (await fixture.Load(entry)).Single();
        Check(installed.ReleaseNotes == OldNotes && !installed.HasUpdate, "Old notes were not seeded through the public flow.");
        Check(Directory.EnumerateFiles(Path.Combine(fixture.CacheRoot, "cache", "plugin-releases", "installed")).Any(),
            "Installed-release notes cache was not populated.");
        fixture.Release(entry, "2.0.0", NewNotes);
        Check(await fixture.Service.RefreshReleasesAsync(new[] { installed }, force: true), "New release did not change metadata.");
        var updated = (await fixture.Load(entry)).Single();
        Check(updated.HasUpdate && updated.ReleaseNotes == NewNotes && updated.ReleaseNotesVersion == "2.0.0",
            "Old installed notes masked the new release, or markdown was altered.");
        Check(installed.ReleaseNotes == OldNotes, "Refresh mutated the existing notes model.");
    }

    private static async Task DeckyVersions()
    {
        using var fixture = new Fixture();
        var first = fixture.Install("decky-new", 41001);
        var second = fixture.Install("decky-equal", 41002);
        var newHash = new string('a', 64);
        var equalHash = new string('b', 64);
        fixture.Handler.Json(DeckyUrl, new[]
        {
            new { id = first.CatalogPluginId, name = first.Name, versions = new[]
            {
                new { name = "1.0.0", hash = new string('c', 64), created = "2026-01-01T00:00:00Z" },
                new { name = "99.0.0", hash = "invalid-hash", created = "2026-04-01T00:00:00Z" },
                new { name = "2.0.0", hash = newHash, created = "2026-03-01T00:00:00Z" }
            } },
            new { id = second.CatalogPluginId, name = second.Name, versions = new[]
            {
                new { name = "v1.0.0", hash = equalHash, created = "2026-03-01T00:00:00Z" }
            } }
        });
        fixture.Release(first, "9.0.0", "Wrong GitHub release notes");
        fixture.Release(second, "1.0.0", OldNotes);
        Check(await fixture.Service.RefreshReleasesAsync(await fixture.Load(first, second)), "Decky metadata did not change.");
        var plugins = await fixture.Load(first, second);
        var newer = plugins.Single(p => p.RepositorySlug == first.Repository);
        var equal = plugins.Single(p => p.RepositorySlug == second.Repository);
        Check(newer.Version == "2.0.0" && newer.InstalledVersion == "1.0.0" && newer.HasUpdate,
            "Decky version was not authoritative over GitHub or invalid-hash entries.");
        Check(newer.CatalogReleaseZipUrl == DeckyZip(newHash) && newer.ReleaseZipUrl == DeckyZip(newHash),
            "Decky artifact hash was not hydrated into the download URLs.");
        Check(newer.ReleaseNotes == "", "Mismatched GitHub notes were attached to a Decky release.");
        Check(equal.Version == "v1.0.0" && !equal.HasUpdate && equal.CatalogReleaseZipUrl == DeckyZip(equalHash) && equal.ReleaseNotes == OldNotes,
            "Equal Decky version, artifact metadata, or matching notes were lost.");
        Check(fixture.Handler.Requests.Count(url => url == DeckyUrl) == 1, "Decky catalog was fetched once per plugin instead of once per refresh.");
        Check(fixture.Handler.Requests.Count == 3, "Unexpected network requests beyond one catalog and two notes requests.");
        fixture.Handler.Offline = true;
        Check(!await fixture.Service.RefreshReleasesAsync(plugins, force: true), "Offline Decky refresh reported a change.");
        var offline = await fixture.Load(first, second);
        Check(offline.Single(p => p.RepositorySlug == first.Repository).CatalogReleaseZipUrl == DeckyZip(newHash) &&
            offline.Single(p => p.RepositorySlug == second.Repository).ReleaseNotes == OldNotes, "Offline Decky cache was lost.");
    }

    private static async Task OfflineCache()
    {
        using var fixture = new Fixture();
        var entry = fixture.Install("offline");
        fixture.Release(entry, "2.0.0", NewNotes);
        await fixture.Service.RefreshReleasesAsync(await fixture.Load(entry));
        var online = (await fixture.Load(entry)).Single();
        var snapshot = JsonSerializer.Serialize(online);
        fixture.Handler.Offline = true;
        var restarted = new PluginCatalogService(fixture.Client);
        Check(!await restarted.RefreshReleasesAsync(new[] { online }, force: true), "Offline refresh reported changed metadata.");
        Check(fixture.Handler.Requests.Contains($"https://github.com/{entry.Repository}/releases.atom"), "Offline API failure did not exercise Atom fallback.");
        var offline = (await fixture.Load(entry)).Single();
        Check(JsonSerializer.Serialize(offline) == snapshot && offline.HasUpdate && offline.ReleaseNotes == NewNotes,
            "A fresh service failed to hydrate all persisted release metadata offline.");
    }

    private static string DeckyZip(string hash) => "https://cdn.tzatzikiweeb.moe/file/steam-deck-homebrew/versions/" + hash + ".zip";
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture : IDisposable
    {
        private static readonly string Parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Playhub.PluginRelease.Tests"));
        private readonly string root = Path.Combine(Parent, Guid.NewGuid().ToString("N"));
        private readonly string owner = "release-tests-" + Guid.NewGuid().ToString("N");
        public string CacheRoot => Path.Combine(root, "data");
        public FixtureHandler Handler { get; } = new();
        public HttpClient Client { get; }
        public PluginCatalogService Service { get; }

        public Fixture()
        {
            Directory.CreateDirectory(Path.Combine(root, "bundled"));
            Directory.CreateDirectory(Path.Combine(root, "installed"));
            AppPaths.TestLocalDataRoot = CacheRoot;
            Client = new HttpClient(Handler);
            Service = new PluginCatalogService(Client);
        }

        public RemotePluginCatalogEntry Install(string name, int deckyId = 0)
        {
            var entry = new RemotePluginCatalogEntry
            {
                Name = name, InstallFolder = name, Repository = owner + "/" + name,
                RepositoryUrl = "https://github.com/" + owner + "/" + name, Version = "1.0.0",
                CatalogSource = deckyId == 0 ? "outside-store" : "decky-store",
                CatalogStatus = deckyId == 0 ? "github" : "decky", CatalogPluginId = deckyId
            };
            var folder = Path.Combine(root, "installed", name);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "plugin.json"), JsonSerializer.Serialize(new { name, version = "1.0.0", repository = entry.Repository }));
            File.WriteAllText(Path.Combine(folder, ".playhub-release.json"), JsonSerializer.Serialize(new { repository = entry.Repository, version = "1.0.0" }));
            return entry;
        }

        public async Task<IReadOnlyList<DeckyPluginInfo>> Load(params RemotePluginCatalogEntry[] entries)
        {
            var requests = Handler.Requests.Count;
            var loaded = await new PluginCatalogService(Client).LoadAsync(Path.Combine(root, "bundled"), Path.Combine(root, "installed"),
                new RemotePluginCatalog { Plugins = entries });
            Check(Handler.Requests.Count == requests, "LoadAsync performed HTTP instead of hydrating offline.");
            Check(loaded.Count == entries.Length && loaded.All(p => p.IsInstalled), "Fixture plugins were not loaded as installed.");
            return loaded;
        }

        public static string Zip(RemotePluginCatalogEntry entry, string version) => $"https://github.com/{entry.Repository}/releases/download/{version}/plugin.zip";
        public void Release(RemotePluginCatalogEntry entry, string version, string notes) =>
            Handler.Json($"https://api.github.com/repos/{entry.Repository}/releases/latest", new
            {
                tag_name = version, body = notes, html_url = $"https://github.com/{entry.Repository}/releases/tag/{version}",
                published_at = "2026-03-01T00:00:00Z",
                assets = new[] { new { name = "plugin.zip", browser_download_url = Zip(entry, version) } }
            });

        public void Dispose()
        {
            Client.Dispose();
            AppPaths.TestLocalDataRoot = null;
            var target = Path.GetFullPath(root);
            if (!target.StartsWith(Parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unsafe fixture cleanup path.");
            Directory.Delete(target, recursive: true);
        }
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<string, string> responses = new(StringComparer.Ordinal);
        public ConcurrentQueue<string> Requests { get; } = new();
        public bool Offline { get; set; }
        public void Json(string url, object value) => responses[url] = JsonSerializer.Serialize(value);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var url = request.RequestUri!.AbsoluteUri;
            Requests.Enqueue(url);
            if (Offline) throw new HttpRequestException("Synthetic offline fixture.");
            if (!responses.TryGetValue(url, out var json)) throw new InvalidOperationException("Unexpected fixture HTTP: " + url);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json), RequestMessage = request });
        }
    }
}

namespace Playhub.Services
{
    internal static class AppPaths
    {
        internal static string? TestLocalDataRoot { get; set; }
        public static string LocalDataRoot => TestLocalDataRoot ??
            throw new InvalidOperationException("Release tests must not access production cache directories.");
    }
}

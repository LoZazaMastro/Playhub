using Playhub.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Playhub.Services;

public sealed partial class PluginCatalogService
{
    private readonly HttpClient _releaseHttp = Http;
    private readonly SemaphoreSlim _releaseRefreshGate = new(1, 1);
    private DateTimeOffset _nextReleaseCheck;

    private static string ReleaseCacheKey(string repository, string source) =>
        source == "decky-store" ? "decky-" + repository.Replace('/', '-') :
        repository.StartsWith(Owner + "/", StringComparison.OrdinalIgnoreCase)
            ? repository[(Owner.Length + 1)..] : repository.Replace('/', '-');

    // LoadAsync stays offline. Refresh release metadata separately, without mutating
    // the models currently owned by the UI or an install/uninstall operation.
    public async Task<bool> RefreshReleasesAsync(IReadOnlyList<DeckyPluginInfo> plugins, bool force = false)
    {
        await _releaseRefreshGate.WaitAsync();
        try
        {
            if (!force && DateTimeOffset.UtcNow < _nextReleaseCheck) return false;
            _nextReleaseCheck = DateTimeOffset.UtcNow.AddMinutes(2);
            return await Task.Run(async () =>
            {
                var changed = 0;
                using var concurrency = new SemaphoreSlim(3, 3);
                var github = plugins.Where(p => p.CatalogSource != "decky-store" &&
                    (p.IsInstalled || p.IsPlayhubPlugin) && IsValidRepositorySlug(p.RepositorySlug))
                    .GroupBy(p => p.RepositorySlug, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First()).ToArray();
                var requests = github.Select(async plugin =>
                {
                    await concurrency.WaitAsync();
                    try
                    {
                        var key = ReleaseCacheKey(plugin.RepositorySlug, plugin.CatalogSource);
                        var before = LoadReleaseCache(key);
                        var latest = await SafeGetLatestReleaseAsync(key, plugin.RepositorySlug, _releaseHttp);
                        if (before != latest) Interlocked.Exchange(ref changed, 1);
                    }
                    finally { concurrency.Release(); }
                }).ToList();
                requests.Add(RefreshDeckyReleasesAsync(plugins, concurrency, () => Interlocked.Exchange(ref changed, 1)));
                await Task.WhenAll(requests);
                return changed != 0;
            });
        }
        finally { _releaseRefreshGate.Release(); }
    }

    private async Task RefreshDeckyReleasesAsync(IReadOnlyList<DeckyPluginInfo> plugins, SemaphoreSlim concurrency, Action changed)
    {
        var decky = plugins.Where(p => p.CatalogSource == "decky-store").ToArray();
        if (decky.Length == 0) return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var json = await _releaseHttp.GetStringAsync("https://plugins.deckbrew.xyz/plugins", timeout.Token);
            using var document = JsonDocument.Parse(json);
            await Task.WhenAll(decky.Select(async plugin =>
            {
                var entry = document.RootElement.EnumerateArray().FirstOrDefault(item =>
                    plugin.CatalogPluginId > 0
                        ? item.TryGetProperty("id", out var id) && id.TryGetInt32(out var value) && value == plugin.CatalogPluginId
                        : item.TryGetProperty("name", out var name) && string.Equals(name.GetString(), plugin.Name, StringComparison.OrdinalIgnoreCase));
                if (entry.ValueKind == JsonValueKind.Undefined || !entry.TryGetProperty("versions", out var versions)) return;
                var latest = versions.EnumerateArray()
                    .Where(v => v.TryGetProperty("hash", out var hash) && Regex.IsMatch(hash.GetString() ?? "", "^[0-9a-fA-F]{64}$"))
                    .OrderByDescending(v => v.TryGetProperty("created", out var created) && DateTimeOffset.TryParse(created.GetString(), out var date)
                        ? date : DateTimeOffset.MinValue).FirstOrDefault();
                if (latest.ValueKind == JsonValueKind.Undefined) return;
                var version = latest.GetProperty("name").GetString();
                var key = ReleaseCacheKey(plugin.RepositorySlug, plugin.CatalogSource);
                var before = LoadReleaseCache(key);
                var release = new ReleaseInfo(
                    "https://cdn.tzatzikiweeb.moe/file/steam-deck-homebrew/versions/" + latest.GetProperty("hash").GetString() + ".zip",
                    plugin.RepositoryUrl, version, null,
                    latest.TryGetProperty("created", out var published) ? FormatDate(published.GetString()) : "");
                // Store artifact versions remain authoritative. GitHub notes are usable
                // only when they describe that exact version, never a different release.
                if (plugin.IsInstalled && IsValidRepositorySlug(plugin.RepositorySlug))
                {
                    await concurrency.WaitAsync();
                    try
                    {
                        var notes = await SafeGetLatestReleaseAsync("notes-" + key, plugin.RepositorySlug, _releaseHttp);
                        if (VersionsEquivalent(version, notes.Version)) release = release with { Notes = notes.Notes, PageUrl = notes.PageUrl };
                    }
                    finally { concurrency.Release(); }
                }
                release = PreserveCachedNotes(key, release);
                if (before == release) return;
                SaveReleaseCache(key, release);
                changed();
            }));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            // Offline/rate-limited checks retain the last known catalog and releases.
        }
    }

    private static void ApplyLatestRelease(DeckyPluginInfo plugin, ReleaseInfo release)
    {
        if (!string.IsNullOrWhiteSpace(release.Version) &&
            CompareSemanticVersions(release.Version, plugin.Version) >= 0)
        {
            plugin.Version = release.Version;
            if (!string.IsNullOrWhiteSpace(release.ZipUrl))
            {
                plugin.CatalogReleaseZipUrl = release.ZipUrl;
                plugin.ReleaseZipUrl = release.ZipUrl;
                plugin.ReleaseAssetName = System.IO.Path.GetFileName(new Uri(release.ZipUrl).AbsolutePath);
            }
            plugin.ReleasePageUrl = release.PageUrl ?? plugin.ReleasePageUrl;
            plugin.ReleasePublishedAt = release.PublishedAt ?? plugin.ReleasePublishedAt;
            plugin.UpdatedAt = plugin.ReleasePublishedAt;
        }
        plugin.HasUpdate = plugin.IsInstalled && HasVersionUpdate(plugin.InstalledVersion, plugin.Version);
    }
}

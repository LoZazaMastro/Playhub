using Playhub.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace Playhub.Services;

public sealed partial class PluginCatalogService
{
    private readonly HttpClient _releaseHttp = Http;
    private readonly SemaphoreSlim _releaseRefreshGate = new(1, 1);
    private DateTimeOffset _nextReleaseCheck;
    private int _publicationCursor;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> _publicationRetryAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _publicationRefreshGate = new(1, 1);

    private static string ReleaseCacheKey(string repository, string source) =>
        source == "decky-store" ? "decky-" + repository.Replace('/', '-') :
        repository.StartsWith(Owner + "/", StringComparison.OrdinalIgnoreCase)
            ? repository[(Owner.Length + 1)..] : repository.Replace('/', '-');

    // LoadAsync stays offline. Refresh release metadata separately, without mutating
    // the models currently owned by the UI or an install/uninstall operation.
    public async Task<bool> RefreshReleasesAsync(IReadOnlyList<DeckyPluginInfo> plugins, bool force = false, CancellationToken cancellationToken = default)
    {
        await _releaseRefreshGate.WaitAsync(cancellationToken);
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
                    await concurrency.WaitAsync(cancellationToken);
                    try
                    {
                        var key = ReleaseCacheKey(plugin.RepositorySlug, plugin.CatalogSource);
                        var before = LoadReleaseCache(key);
                        var latest = await SafeGetLatestReleaseAsync(key, plugin.RepositorySlug, _releaseHttp, cancellationToken);
                        if (before != latest) Interlocked.Exchange(ref changed, 1);
                    }
                    finally { concurrency.Release(); }
                }).ToList();
                requests.Add(RefreshDeckyReleasesAsync(plugins, concurrency, () => Interlocked.Exchange(ref changed, 1), cancellationToken));
                await Task.WhenAll(requests);
                return changed != 0;
            }, cancellationToken);
        }
        finally { _releaseRefreshGate.Release(); }
    }

    private async Task RefreshDeckyReleasesAsync(IReadOnlyList<DeckyPluginInfo> plugins, SemaphoreSlim concurrency, Action changed, CancellationToken cancellationToken)
    {
        var decky = plugins.Where(p => p.CatalogSource == "decky-store").ToArray();
        if (decky.Length == 0) return;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
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
                var firstDate = versions.EnumerateArray().Select(v => v.TryGetProperty("created", out var value) ? CanonicalPublicationTimestamp(value.GetString()) : "")
                    .Where(value => value.Length > 0).OrderBy(value => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture)).FirstOrDefault() ?? "";
                var createdAt = entry.TryGetProperty("created", out var first) ? CanonicalPublicationTimestamp(first.GetString()) : "";
                var updatedAt = entry.TryGetProperty("updated", out var last) ? CanonicalPublicationTimestamp(last.GetString()) : "";
                var publishedAt = latest.TryGetProperty("created", out var published) ? CanonicalPublicationTimestamp(published.GetString()) : "";
                var release = new ReleaseInfo(
                    "https://cdn.tzatzikiweeb.moe/file/steam-deck-homebrew/versions/" + latest.GetProperty("hash").GetString() + ".zip",
                    plugin.RepositoryUrl, version, null,
                    publishedAt, FirstValidPublicationTimestamp(createdAt, firstDate),
                    FirstValidPublicationTimestamp(updatedAt, publishedAt));
                // Store artifact versions remain authoritative. GitHub notes are usable
                // only when they describe that exact version, never a different release.
                if (plugin.IsInstalled && IsValidRepositorySlug(plugin.RepositorySlug))
                {
                    await concurrency.WaitAsync(cancellationToken);
                    try
                    {
                        var notes = await SafeGetLatestReleaseAsync("notes-" + key, plugin.RepositorySlug, _releaseHttp, cancellationToken);
                        if (VersionsEquivalent(version, notes.Version)) release = release with { Notes = notes.Notes, PageUrl = notes.PageUrl };
                    }
                    finally { concurrency.Release(); }
                }
                release = PreserveCachedNotes(key, release);
                if (before == release) return;
                cancellationToken.ThrowIfCancellationRequested();
                SaveReleaseCache(key, release);
                changed();
            }));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            // Offline/rate-limited checks retain the last known catalog and releases.
        }
    }

    private static void ApplyLatestRelease(DeckyPluginInfo plugin, ReleaseInfo release)
    {
        plugin.FirstPublishedAt = FirstValidPublicationTimestamp(plugin.FirstPublishedAt, release.FirstPublishedAt);
        plugin.UpdatedAt = LatestPublicationTimestamp(plugin.UpdatedAt, release.UpdatedAt, release.PublishedAt);
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
            plugin.ReleasePublishedAt = string.IsNullOrWhiteSpace(release.PublishedAt) ? plugin.ReleasePublishedAt : FormatDate(release.PublishedAt);
        }
        plugin.HasUpdate = plugin.IsInstalled && HasVersionUpdate(plugin.InstalledVersion, plugin.Version);
    }

    private static string CanonicalPublicationTimestamp(string? value)
    {
        // Display-only legacy dates never become fabricated UTC instants or outrank precise source data.
        if (string.IsNullOrWhiteSpace(value) || !value.Contains('T')) return "";
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)
                ? date.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture) : "";
    }

    private static string FirstValidPublicationTimestamp(params string?[] values) =>
        values.Select(CanonicalPublicationTimestamp).FirstOrDefault(value => value.Length > 0) ?? "";

    private static string LatestPublicationTimestamp(params string?[] values) => values
        .Select(CanonicalPublicationTimestamp).Where(value => value.Length > 0)
        .OrderByDescending(value => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture)).FirstOrDefault() ?? "";

    private static string FirstPublicationFromKeywords(IEnumerable<string> keywords) => keywords
        .Where(value => value.StartsWith("first-release:", StringComparison.OrdinalIgnoreCase))
        .Select(value => value["first-release:".Length..])
        .Where(value => Regex.IsMatch(value, @"^\d{4}-\d{2}-\d{2}$"))
        .Select(value => DateTimeOffset.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)
                ? date.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) : "")
        .FirstOrDefault(value => value.Length > 0) ?? "";

    private static async Task<string> ReadPublicDateTextAsync(HttpClient http, string url, int limit, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is { } length && length > limit) throw new InvalidDataException("Publication document exceeds limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, limit + 1 - (int)buffer.Length)), token);
            if (count == 0) break;
            buffer.Write(chunk, 0, count);
            if (buffer.Length > limit) throw new InvalidDataException("Publication document exceeds limit.");
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static async Task<string> ReadGithubPublishedAtAsync(string? url, string repository, HttpClient http, CancellationToken token)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com" ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0 || !uri.AbsolutePath.StartsWith("/" + repository + "/releases/tag/", StringComparison.OrdinalIgnoreCase)) return "";
        var html = await ReadPublicDateTextAsync(http, uri.AbsoluteUri, 2 * 1024 * 1024, token);
        var match = Regex.Match(html, "released this\\s*<relative-time\\b[^>]*\\bdatetime=\"(?<date>[^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? CanonicalPublicationTimestamp(match.Groups["date"].Value) : "";
    }

    // The Store owns cancellation. This separate scan has no release-notes wave deadline.
    // Publish each completed batch so the UI need not wait for all repositories.
    public Task RefreshPublicationDatesAsync(IReadOnlyList<DeckyPluginInfo> plugins, Action onBatchChanged, CancellationToken cancellationToken = default)
    {
        var snapshot = plugins.ToArray();
        return Task.Run(async () =>
        {
            await _publicationRefreshGate.WaitAsync(cancellationToken);
            try
            {
                using var concurrency = new SemaphoreSlim(3, 3);
                while (await RefreshPublicPublicationDateBatchAsync(snapshot, concurrency, onBatchChanged, cancellationToken))
                    await Task.Delay(100, cancellationToken);
            }
            finally { _publicationRefreshGate.Release(); }
        }, cancellationToken);
    }

    private async Task<bool> RefreshPublicPublicationDateBatchAsync(IReadOnlyList<DeckyPluginInfo> plugins, SemaphoreSlim concurrency, Action changed, CancellationToken token)
    {
        var candidates = plugins.Where(plugin => plugin.CatalogSource == "outside-store" && !plugin.IsInstalled && IsValidRepositorySlug(plugin.RepositorySlug))
            .GroupBy(plugin => plugin.RepositorySlug, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToArray();
        if (candidates.Length == 0) return false;
        var selected = Enumerable.Range(0, candidates.Length).Select(index => candidates[(_publicationCursor + index) % candidates.Length])
            .Where(plugin => !_publicationRetryAt.TryGetValue(plugin.RepositorySlug, out var retry) || retry <= DateTimeOffset.UtcNow)
            .Where(plugin => !DateTimeOffset.TryParse(LoadReleaseCache(ReleaseCacheKey(plugin.RepositorySlug, plugin.CatalogSource)).PublicationCheckedAt,
                CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date) || date <= DateTimeOffset.UtcNow.AddHours(-6))
            .Take(6).ToArray();
        _publicationCursor = (_publicationCursor + Math.Max(selected.Length, 1)) % candidates.Length;
        if (selected.Length == 0) return false;
        var batchChanged = 0;
        await Task.WhenAll(selected.Select(async plugin =>
        {
            await concurrency.WaitAsync(token);
            try
            {
                // A failed disk cache must not make this same candidate immediately due
                // again in the next wave. Every attempted source has an in-memory lease.
                _publicationRetryAt[plugin.RepositorySlug] = DateTimeOffset.UtcNow.AddMinutes(5);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(TimeSpan.FromSeconds(8));
                var xml = await ReadPublicDateTextAsync(_releaseHttp, "https://github.com/" + plugin.RepositorySlug + "/releases.atom", 1024 * 1024, deadline.Token);
                var document = XDocument.Parse(xml); XNamespace atom = "http://www.w3.org/2005/Atom";
                var key = ReleaseCacheKey(plugin.RepositorySlug, plugin.CatalogSource);
                var previous = LoadReleaseCache(key);
                var entry = document.Root?.Elements(atom + "entry").FirstOrDefault(item =>
                {
                    var link = item.Elements(atom + "link").FirstOrDefault(link => (string?)link.Attribute("rel") == "alternate")?.Attribute("href")?.Value;
                    var tag = link is null ? "" : Uri.UnescapeDataString(link[(link.LastIndexOf('/') + 1)..]);
                    var known = plugin.CatalogReleaseZipUrl ?? "";
                    return !known.Contains("/releases/download/plugin-", StringComparison.OrdinalIgnoreCase) || tag.StartsWith("plugin-", StringComparison.OrdinalIgnoreCase);
                });
                var url = entry?.Elements(atom + "link").FirstOrDefault(link => (string?)link.Attribute("rel") == "alternate")?.Attribute("href")?.Value;
                var sameRelease = string.Equals(url, previous.PublicationPageUrl, StringComparison.OrdinalIgnoreCase);
                var knownPublished = FirstValidPublicationTimestamp(previous.UpdatedAt);
                var hasPreciseDate = previous.UpdatedAt?.Contains('T') == true && knownPublished.Length > 0;
                var published = sameRelease && hasPreciseDate ? knownPublished
                    : await ReadGithubPublishedAtAsync(url, plugin.RepositorySlug, _releaseHttp, deadline.Token);
                if (published.Length == 0) throw new InvalidDataException("Missing explicit GitHub release publication timestamp.");
                var updated = LatestPublicationTimestamp(plugin.UpdatedAt, previous.UpdatedAt, published);
                var visibleUpdated = LatestPublicationTimestamp(plugin.UpdatedAt, previous.UpdatedAt);
                var release = previous with { Version = previous.Version ?? plugin.Version, UpdatedAt = updated,
                    FirstPublishedAt = FirstValidPublicationTimestamp(previous.FirstPublishedAt, plugin.FirstPublishedAt),
                    PublicationPageUrl = url ?? previous.PublicationPageUrl,
                    PublicationCheckedAt = DateTimeOffset.UtcNow.ToString("O") };
                SaveReleaseCache(key, release);
                _publicationRetryAt[plugin.RepositorySlug] = DateTimeOffset.UtcNow.AddHours(6);
                if (updated != visibleUpdated) Interlocked.Exchange(ref batchChanged, 1);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                _publicationRetryAt.TryRemove(plugin.RepositorySlug, out _);
                throw;
            }
            catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or System.Xml.XmlException or OperationCanceledException)
            { _publicationRetryAt[plugin.RepositorySlug] = DateTimeOffset.UtcNow.AddMinutes(5); }
            finally { concurrency.Release(); }
        }));
        if (batchChanged != 0) changed();
        return true;
    }
}

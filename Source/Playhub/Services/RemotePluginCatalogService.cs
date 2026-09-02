using Playhub.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Playhub.Services;

public sealed class RemotePluginCatalogService
{
    // Confirmed by .git/config and refs/remotes/origin/HEAD; publication is a separate step.
    public const string CatalogUrl = "https://raw.githubusercontent.com/LoZazaMastro/Playhub/main/catalog/plugins.json";
    public const int MaxDocumentBytes = 1024 * 1024;
    public const int MaxPlugins = 1000;
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(6);
    public static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(5);
    private static readonly HttpClient SharedHttp = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        MaxResponseHeadersLength = 32,
        MaxConnectionsPerServer = 2
    }) { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16
    };
    private readonly HttpClient _http;
    private readonly string _cachePath;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _cacheGate = new(1, 1);
    private RemotePluginCatalog? _knownGood;
    private DateTimeOffset _nextCheck;
    private bool _cacheRead;
    private string _origin = "local";
    private string? _error;

    public RemotePluginCatalogService(string cachePath)
        : this(SharedHttp, cachePath, () => DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10)) { }

    internal RemotePluginCatalogService(HttpClient http, string cachePath,
        Func<DateTimeOffset> clock, TimeSpan timeout)
    {
        _http = http;
        _cachePath = Path.GetFullPath(cachePath);
        _clock = clock;
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(timeout));
        _timeout = timeout;
    }

    // File opens, JSON parsing and merging never run inline on a UI synchronization context.
    // Reuse one instance: the gate and intervals coalesce concurrent/repeated store loads.
    public Task<RemotePluginCatalogResult> LoadAsync(RemotePluginCatalog localCatalog,
        CancellationToken cancellationToken = default)
        => RefreshAsync(localCatalog, cancellationToken);

    // Startup baseline: at most one bounded cache read, never an HTTP request.
    public Task<RemotePluginCatalogResult> LoadCachedAsync(RemotePluginCatalog localCatalog,
        CancellationToken cancellationToken = default)
        => RunAsync(localCatalog, refresh: false, cancellationToken);

    public Task<RemotePluginCatalogResult> RefreshAsync(RemotePluginCatalog localCatalog,
        CancellationToken cancellationToken = default)
        => RunAsync(localCatalog, refresh: true, cancellationToken);

    private Task<RemotePluginCatalogResult> RunAsync(RemotePluginCatalog localCatalog,
        bool refresh, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(localCatalog);
        return Task.Run(() => LoadCoreAsync(localCatalog, refresh, cancellationToken), cancellationToken);
    }

    private async Task<RemotePluginCatalogResult> LoadCoreAsync(RemotePluginCatalog local, bool refresh, CancellationToken token)
    {
        await ReadCacheOnceAsync(local, token).ConfigureAwait(false);
        if (!refresh) return Snapshot(local);
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_clock() >= _nextCheck)
            {
                try
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                    deadline.CancelAfter(_timeout);
                    using var request = new HttpRequestMessage(HttpMethod.Get, CatalogUrl);
                    request.Headers.UserAgent.ParseAdd("Playhub-RemoteCatalog/1.0");
                    request.Headers.Accept.ParseAdd("application/json");
                    using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                        deadline.Token).ConfigureAwait(false);
                    // Redirects and partial responses are not catalog documents.
                    if (response.StatusCode != HttpStatusCode.OK)
                        throw new HttpRequestException($"Catalog HTTP {(int)response.StatusCode}.");
                    if (response.Content.Headers.ContentLength is > MaxDocumentBytes)
                        throw new InvalidDataException("Catalog exceeds the byte limit.");
                    await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
                    var bytes = await ReadBoundedAsync(stream, deadline.Token).ConfigureAwait(false);
                    var remote = Parse(bytes);
                    if (remote.CatalogRevision < Math.Max(local.CatalogRevision, _knownGood?.CatalogRevision ?? 0))
                        throw new InvalidDataException("Catalog revision rollback rejected.");
                    Merge(local, remote);
                    token.ThrowIfCancellationRequested();
                    _knownGood = remote;
                    _origin = "remote";
                    _error = null;
                    _nextCheck = _clock() + RefreshInterval;
                    try { await SaveCacheAsync(bytes, token).ConfigureAwait(false); }
                    catch (Exception ex) when (IsRecoverable(ex, token)) { _error = "Cache write: " + ex.Message; }
                }
                catch (Exception ex) when (IsRecoverable(ex, token))
                {
                    _error = ex.Message;
                    _nextCheck = _clock() + RetryInterval;
                    if (_knownGood is not null) _origin = "cache";
                }
            }

            return Snapshot(local);
        }
        finally { _gate.Release(); }
    }

    private async Task ReadCacheOnceAsync(RemotePluginCatalog local, CancellationToken token)
    {
        await _cacheGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_cacheRead) return;
            try
            {
                await using var file = new FileStream(_cachePath, FileMode.Open, FileAccess.Read,
                    FileShare.Read | FileShare.Delete, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var cached = Parse(await ReadBoundedAsync(file, token).ConfigureAwait(false));
                Merge(local, cached);
                _knownGood = cached;
                _origin = "cache";
                var written = new DateTimeOffset(File.GetLastWriteTimeUtc(_cachePath));
                if (written <= _clock()) _nextCheck = written + RefreshInterval;
            }
            catch (Exception ex) when (IsRecoverable(ex, token)) { _error = ex.Message; }
            _cacheRead = true;
        }
        finally { _cacheGate.Release(); }
    }

    private RemotePluginCatalogResult Snapshot(RemotePluginCatalog local)
    {
        var knownGood = _knownGood;
        try { return new(knownGood is null ? local : Merge(local, knownGood), knownGood is null ? "local" : _origin, _error); }
        catch (InvalidDataException ex) { return new(local, "local", ex.Message); }
    }

    private static bool IsRecoverable(Exception ex, CancellationToken token) =>
        ex is IOException or InvalidDataException or HttpRequestException or JsonException or UnauthorizedAccessException ||
        ex is OperationCanceledException && !token.IsCancellationRequested;

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(chunk.AsMemory(0,
                Math.Min(chunk.Length, MaxDocumentBytes + 1 - (int)buffer.Length)), token).ConfigureAwait(false);
            if (count == 0) return buffer.ToArray();
            buffer.Write(chunk, 0, count);
            if (buffer.Length > MaxDocumentBytes) throw new InvalidDataException("Catalog exceeds the byte limit.");
        }
    }

    private async Task SaveCacheAsync(byte[] bytes, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
        var temporary = _cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, _cachePath, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static RemotePluginCatalog Parse(ReadOnlyMemory<byte> utf8Json)
    {
        if (utf8Json.Length > MaxDocumentBytes) throw new InvalidDataException("Catalog exceeds the byte limit.");
        using var json = JsonDocument.Parse(utf8Json, new JsonDocumentOptions { MaxDepth = 16 });
        RejectDuplicateProperties(json.RootElement);
        if (json.RootElement.ValueKind != JsonValueKind.Object ||
            !json.RootElement.TryGetProperty("schemaVersion", out var schema) ||
            schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var version) || version != 3 ||
            !json.RootElement.TryGetProperty("catalogRevision", out var revision) ||
            revision.ValueKind != JsonValueKind.Number || !revision.TryGetInt64(out var number) || number < 1 ||
            !json.RootElement.TryGetProperty("plugins", out var plugins) || plugins.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Expected schemaVersion 3, positive catalogRevision and plugins array.");
        var catalog = json.RootElement.Deserialize<RemotePluginCatalog>(JsonOptions)!;
        if (catalog.Plugins is null || catalog.Plugins.Count is 0 or > MaxPlugins ||
            catalog.OfficialDeckyCatalogUrl != "https://plugins.deckbrew.xyz/plugins" ||
            !Text(catalog.VerifiedAt, 64))
            throw new InvalidDataException("Invalid catalog metadata or plugin count.");
        var repositories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deckyIds = new HashSet<int>();
        foreach (var plugin in catalog.Plugins)
        {
            ValidateEntry(plugin);
            if (!repositories.Add(plugin.Repository) || !folders.Add(plugin.InstallFolder) ||
                plugin.CatalogSource == "decky-store" && !deckyIds.Add(plugin.CatalogPluginId))
                throw new InvalidDataException("Duplicate catalog identity.");
        }
        return catalog with { Plugins = Array.AsReadOnly(catalog.Plugins.Select(p => p with
        {
            RepositoryUrl = p.RepositoryUrl.Length == 0 ? $"https://github.com/{p.Repository}" : p.RepositoryUrl,
            Aliases = Array.AsReadOnly(p.Aliases.ToArray()),
            Keywords = Array.AsReadOnly(p.Keywords.ToArray())
        }).ToArray()) };
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate JSON property.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }

    private static void ValidateEntry(RemotePluginCatalogEntry p)
    {
        if (p is null || !Text(p.Name, 160, true) || !Text(p.Author, 160, true) ||
            !Slug(p.Repository) || !FileName(p.InstallFolder) || !Text(p.Version, 64, true) || p.Version.Any(char.IsWhiteSpace) ||
            !Text(p.Category, 160, true) || !Text(p.ShortDescription, 4096, true) ||
            !Text(p.LongDescription, 32768, true) || !Text(p.Compatibility, 4096) ||
            !Text(p.IconGlyph, 8) || !Text(p.ReleasePublishedAt, 64) ||
            !Text(p.ReleaseAssetName, 255) || p.ReleaseAssetName.Length > 0 && !FileName(p.ReleaseAssetName) ||
            p.Aliases is null || p.Aliases.Count > 64 || p.Aliases.Any(s => !Text(s, 256, true) ||
                s.Any(char.IsControl) || s.IndexOfAny(new[] { '\\', ':' }) >= 0 || s.Contains('/') && !Slug(s)) ||
            p.Keywords is null || p.Keywords.Count > 64 || p.Keywords.Any(s => !Text(s, 256, true)))
            throw new InvalidDataException("Invalid plugin fields.");
        if (new[] { p.Name, p.InstallFolder, p.Repository.Split('/')[1] }.Concat(p.Aliases).Any(IsBlockedIdentity))
            throw new InvalidDataException("Integrated Gaming Mode and blocked Varta identities cannot enter the store.");

        var owner = p.Repository.Split('/')[0];
        var sourceValid = p.CatalogSource switch
        {
            "playhub" => p.CatalogStatus == "playhub" && owner.Equals("LoZazaMastro", StringComparison.OrdinalIgnoreCase)
                && p.CatalogPluginId == 0,
            "outside-store" => p.CatalogStatus == "github" && p.CatalogPluginId == 0,
            "decky-store" => p.CatalogStatus == "decky" && p.CatalogPluginId > 0,
            _ => false
        };
        if (!sourceValid) throw new InvalidDataException("Invalid plugin source/status/id.");
        var repoUrl = p.RepositoryUrl == "" ? $"https://github.com/{p.Repository}" : p.RepositoryUrl;
        var repo = Https(repoUrl);
        if (repo is null || (repo.Host != "github.com" && !(p.CatalogSource == "decky-store" && repo.Host == "gitlab.com")) ||
            !repo.AbsolutePath.TrimEnd('/').Equals("/" + p.Repository, StringComparison.OrdinalIgnoreCase) || repo.Query.Length != 0)
            throw new InvalidDataException("Repository URL does not match its identity/source.");
        if (p.CoverUrl != "" && (Https(p.CoverUrl) is not { } cover ||
            cover.Host is not ("github.com" or "raw.githubusercontent.com" or "opengraph.githubassets.com" or
                "images.steamusercontent.com" or "cdn.tzatzikiweeb.moe")))
            throw new InvalidDataException("Untrusted cover URL.");
        if (p.CatalogReleaseUrl != "")
        {
            var release = Https(p.CatalogReleaseUrl);
            var prefix = "/" + p.Repository + "/releases/";
            if (release is null || release.Query.Length != 0 ||
                !(release.Host == "github.com" && release.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                  p.CatalogSource == "decky-store" && release.Host == "cdn.tzatzikiweeb.moe" &&
                  release.AbsolutePath.StartsWith("/file/steam-deck-homebrew/versions/", StringComparison.Ordinal)) ||
                !Uri.UnescapeDataString(release.Segments.Last()).Equals(p.ReleaseAssetName, StringComparison.Ordinal) ||
                !p.ReleaseAssetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || p.Version.Length == 0)
                throw new InvalidDataException("Release URL does not match its identity/source/asset.");
        }
        else if (p.ReleaseAssetName.Length > 0 || p.CatalogSource != "playhub")
            throw new InvalidDataException("External entries require a versioned release ZIP.");
    }

    private static Uri? Https(string value)
    {
        if (!Text(value, 2048, true) || value.Any(char.IsWhiteSpace) || value.Contains('\\') ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
            uri.HostNameType != UriHostNameType.Dns || uri.IsLoopback)
            return null;
        var decoded = Uri.UnescapeDataString(value);
        if (decoded.Any(char.IsControl) || decoded.Contains('\\') ||
            decoded.Split('/').Any(part => part is "." or "..")) return null;
        return uri;
    }

    private static bool Text(string? text, int limit, bool required = false) => text is not null &&
        text.Length <= limit && (!required || !string.IsNullOrWhiteSpace(text)) &&
        !text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'));

    private static bool Slug(string? value) => value is { Length: > 2 and <= 200 } &&
        value.Split('/') is { Length: 2 } parts && parts.All(part => part.Length > 0 &&
            part is not ("." or "..") && part.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'));

    private static bool FileName(string? value) => value is { Length: > 0 and <= 255 } &&
        value == value.Trim() && !value.EndsWith('.') &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '.' or '_' or '-') &&
        !new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(value.Split('.')[0].TrimEnd(), StringComparer.OrdinalIgnoreCase);

    private static bool IsBlockedIdentity(string identity)
    {
        var name = identity.Split('/').Last();
        var normalized = new string(name.Where(char.IsLetterOrDigit).ToArray());
        return normalized.Equals("gamingmode", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("playhubgamingmode", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("varta", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("vartaplugin", StringComparison.OrdinalIgnoreCase);
    }

    // No disk or network work here. Installed state, bundled media and release resolution
    // belong to PluginCatalogService after this definitions-only merge.
    public static RemotePluginCatalog Merge(RemotePluginCatalog local, RemotePluginCatalog remote)
    {
        if (remote.CatalogRevision < local.CatalogRevision) throw new InvalidDataException("Catalog revision rollback rejected.");
        var entries = local.Plugins.ToList();
        var index = entries.Select((p, i) => (p.Repository, i)).ToDictionary(p => p.Repository, p => p.i, StringComparer.OrdinalIgnoreCase);
        var folders = entries.ToDictionary(p => p.InstallFolder, p => p.Repository, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in remote.Plugins)
        {
            if (folders.TryGetValue(entry.InstallFolder, out var repository) &&
                !repository.Equals(entry.Repository, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Remote install folder conflicts with local identity.");
            if (index.TryGetValue(entry.Repository, out var position))
            {
                var existing = entries[position];
                if (existing.CatalogSource != entry.CatalogSource ||
                    !existing.InstallFolder.Equals(entry.InstallFolder, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(RepositoryHost(existing), RepositoryHost(entry), StringComparison.OrdinalIgnoreCase) ||
                    existing.CatalogPluginId != entry.CatalogPluginId)
                    throw new InvalidDataException("Remote identity/source conflicts with local catalog.");
                if (!entry.Active)
                {
                    entries[position] = entry;
                    continue;
                }
                // Empty optional presentation fields do not erase useful bundled metadata.
                entries[position] = entry with
                {
                    CoverUrl = entry.CoverUrl.Length == 0 ? existing.CoverUrl : entry.CoverUrl,
                    IconGlyph = entry.IconGlyph.Length == 0 ? existing.IconGlyph : entry.IconGlyph,
                    Compatibility = entry.Compatibility.Length == 0 ? existing.Compatibility : entry.Compatibility,
                    Version = entry.Version.Length == 0 ? existing.Version : entry.Version,
                    Aliases = Array.AsReadOnly(existing.Aliases.Concat(entry.Aliases).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()),
                    Keywords = entry.Keywords.Count == 0 ? existing.Keywords : entry.Keywords
                };
            }
            else if (entry.Active)
            {
                index.Add(entry.Repository, entries.Count);
                folders.Add(entry.InstallFolder, entry.Repository);
                entries.Add(entry);
            }
        }
        return remote with { Plugins = entries.Where(p => p.Active).ToList().AsReadOnly() };
    }

    private static string RepositoryHost(RemotePluginCatalogEntry entry) =>
        entry.RepositoryUrl.Length == 0 ? "github.com" : new Uri(entry.RepositoryUrl).Host;
}

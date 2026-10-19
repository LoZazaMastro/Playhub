using Playhub.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Playhub.Services;

public sealed partial class RemotePluginCatalogService
{
    public const string OfficialCatalogUrl = "https://plugins.deckbrew.xyz/plugins";
    public const string OfficialRegistryUrl = "https://raw.githubusercontent.com/SteamDeckHomebrew/decky-plugin-database/main/.gitmodules";
    public static readonly TimeSpan OfficialRefreshInterval = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan OfficialRetryInterval = TimeSpan.FromMinutes(1);
    private RemotePluginCatalog? _officialGood;
    private DateTimeOffset _nextOfficialCheck;
    private string _officialOrigin = "cache";
    private string? _officialError;
    private string OfficialCachePath => _cachePath + ".decky-v1.json";
    private string PinnedIdentityCachePath => _cachePath + ".decky-identities-v1.json";
    private sealed record PinnedIdentity(RemotePluginCatalogEntry Entry, DateTimeOffset VerifiedAt);
    private readonly Dictionary<string, PinnedIdentity> _pinnedIdentities = new(StringComparer.Ordinal);
    private bool _pinnedCacheRead;
    private const int MaxPinnedIdentities = 256;

    private static string PinKey(RemotePluginCatalogEntry entry)
    {
        ValidateEntry(entry);
        if (entry.CatalogSource != "decky-store" || !Regex.IsMatch(entry.ReleaseAssetName, "^[a-fA-F0-9]{64}\\.zip$"))
            throw new InvalidDataException("Invalid pinned official identity.");
        return entry.CatalogPluginId + ":" + entry.ReleaseAssetName[..64].ToLowerInvariant();
    }

    private async Task ReadPinnedIdentitiesOnceAsync(CancellationToken token)
    {
        if (_pinnedCacheRead) return;
        try
        {
            await using var stream = new FileStream(PinnedIdentityCachePath, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var bytes = await ReadBoundedAsync(stream, token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            RejectDuplicateProperties(document.RootElement);
            var records = document.RootElement.Deserialize<PinnedIdentity[]>(JsonOptions);
            if (records is null || records.Length > MaxPinnedIdentities) throw new InvalidDataException("Pinned identity cache bound.");
            var accepted = new Dictionary<string, PinnedIdentity>(StringComparer.Ordinal);
            foreach (var record in records)
            {
                if (record is null || record.VerifiedAt > _clock() || !accepted.TryAdd(PinKey(record.Entry), record))
                    throw new InvalidDataException("Invalid pinned identity cache receipt.");
            }
            foreach (var pair in accepted) _pinnedIdentities.Add(pair.Key, pair.Value);
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        catch (Exception error) when (IsRecoverable(error, token)) { _officialError = "Pinned identity cache: " + error.GetType().Name; }
        _pinnedCacheRead = true;
    }

    private async Task SavePinnedIdentityAsync(RemotePluginCatalogEntry entry, CancellationToken token)
    {
        _pinnedIdentities[PinKey(entry)] = new(entry, _clock());
        while (_pinnedIdentities.Count > MaxPinnedIdentities)
            _pinnedIdentities.Remove(_pinnedIdentities.MinBy(pair => pair.Value.VerifiedAt).Key);
        var records = _pinnedIdentities.Values.OrderBy(record => record.VerifiedAt).ToArray();
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(records, JsonOptions);
        while (bytes.Length > MaxDocumentBytes && records.Length > 0)
        {
            _pinnedIdentities.Remove(PinKey(records[0].Entry)); records = records[1..];
            bytes = JsonSerializer.SerializeToUtf8Bytes(records, JsonOptions);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(PinnedIdentityCachePath)!);
        var temporary = PinnedIdentityCachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested(); File.Move(temporary, PinnedIdentityCachePath, overwrite: true);
        }
        finally { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    private async Task ReadOfficialCacheOnceAsync(CancellationToken token)
    {
        await _cacheGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_officialCacheRead) return;
            try
            {
                await using var stream = new FileStream(OfficialCachePath, FileMode.Open, FileAccess.Read,
                    FileShare.Read | FileShare.Delete, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var catalog = Parse(await ReadBoundedAsync(stream, token).ConfigureAwait(false));
                if (catalog.Plugins.Any(entry => entry.CatalogSource != "decky-store")) throw new InvalidDataException("Invalid official cache source.");
                _officialGood = catalog;
                var written = new DateTimeOffset(File.GetLastWriteTimeUtc(OfficialCachePath));
                if (written <= _clock()) _nextOfficialCheck = written + OfficialRefreshInterval;
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            catch (Exception error) when (IsRecoverable(error, token)) { _officialError = error.Message; }
            _officialCacheRead = true;
        }
        finally { _cacheGate.Release(); }
    }

    private async Task RefreshOfficialAsync(RemotePluginCatalog local, bool force, CancellationToken token)
    {
        if (!force && _clock() < _nextOfficialCheck) return;
        try
        {
            await ReadPinnedIdentitiesOnceAsync(token).ConfigureAwait(false);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(_officialTimeout);
            var bytes = await ReadOfficialBytesAsync(OfficialCatalogUrl, deadline.Token).ConfigureAwait(false);
            var baseline = _knownGood is null ? local : Merge(local, _knownGood);
            var known = (_officialGood?.Plugins ?? Array.Empty<RemotePluginCatalogEntry>()).Concat(baseline.Plugins).ToArray();
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            RejectDuplicateProperties(document.RootElement);
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() is 0 or > MaxPlugins)
                throw new InvalidDataException("Invalid official catalog count.");
            var entries = new List<RemotePluginCatalogEntry>();
            var unresolved = new List<JsonElement>();
            var ids = new HashSet<int>();
            var items = new Dictionary<int, JsonElement>();
            var warnings = new List<string>();
            void Accept(RemotePluginCatalogEntry entry) { ValidateEntry(entry); entries.Add(entry); }
            foreach (var item in document.RootElement.EnumerateArray())
            {
                var id = OfficialId(item);
                if (!ids.Add(id)) throw new InvalidDataException("Duplicate official plugin ID.");
                items.Add(id, item);
                if (item.TryGetProperty("visible", out var visible) && visible.ValueKind == JsonValueKind.False) continue;
                try
                {
                var name = OfficialText(item, "name", 160);
                var matchingId = known.Where(entry => entry.CatalogSource == "decky-store" && entry.CatalogPluginId == id).Take(1).ToArray();
                var matches = matchingId.Length > 0 ? matchingId : known.Where(entry => entry.CatalogSource != "playhub" &&
                    entry.Aliases.Append(entry.Name).Append(entry.InstallFolder).Any(alias => alias.Equals(name, StringComparison.OrdinalIgnoreCase))).ToArray();
                var identities = matches.GroupBy(entry => entry.Repository, StringComparer.OrdinalIgnoreCase).ToArray();
                if (identities.Length > 1) { unresolved.Add(item); continue; }
                if (identities.Length == 1)
                {
                        var identity = identities[0].First(); var fresh = OfficialEntry(item, identity);
                        if (matchingId.Length == 0 || !identity.ReleaseAssetName.Equals(fresh.ReleaseAssetName, StringComparison.OrdinalIgnoreCase))
                            fresh = await ResolvePinnedIdentityAsync(item, identity, deadline.Token).ConfigureAwait(false);
                        Accept(fresh);
                }
                else unresolved.Add(item);
                }
                catch (Exception error) when (IsRecoverable(error, token))
                {
                    // A failed new pin cannot prove a baseline alias/folder. Retain only
                    // this service's previously accepted exact official identity.
                    var previous = _officialGood?.Plugins.FirstOrDefault(entry => entry.CatalogPluginId == id);
                    if (previous is not null) Accept(previous);
                    warnings.Add("Invalid official ID " + id + ": " + error.GetType().Name);
                }
            }
            foreach (var item in unresolved)
            {
                try
                {
                    // Deduplication affects visible membership only. A verified raw ID
                    // may have been superseded by another ID with the same native folder;
                    // reuse its exact pin before asking the registry to rediscover it.
                    if (TryGetPinnedIdentity(item, null, out var cachedIdentity))
                    { Accept(cachedIdentity); continue; }
                    var seeds = new List<RemotePluginCatalogEntry>();
                    await ResolveOfficialIdentitiesAsync(new List<JsonElement> { item }, seeds, known, deadline.Token).ConfigureAwait(false);
                    Accept(await ResolvePinnedIdentityAsync(item, seeds.Single(), deadline.Token, allowVerifiedRepositoryFallback: true).ConfigureAwait(false));
                }
                catch (Exception error) when (IsRecoverable(error, token))
                {
                    try { Accept(await ResolvePinnedIdentityAsync(item, null, deadline.Token).ConfigureAwait(false)); }
                    catch (Exception pinError) when (IsRecoverable(pinError, token)) { warnings.Add("Unresolved official ID " + OfficialId(item) + ": " + pinError.GetType().Name); }
                }
            }
            if (entries.Count == 0) throw new InvalidDataException("No verified official plugin identities.");
            var conflictingIds = entries.GroupBy(entry => entry.Repository, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).SelectMany(group => group)
                .Concat(entries.GroupBy(entry => entry.InstallFolder, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).SelectMany(group => group)).Select(entry => entry.CatalogPluginId).Distinct().ToArray();
            foreach (var id in conflictingIds)
            {
                var previous = entries.Single(entry => entry.CatalogPluginId == id);
                try
                {
                    var resolved = await ResolvePinnedIdentityAsync(items[id], previous, deadline.Token).ConfigureAwait(false);
                    entries.Remove(previous); entries.Add(resolved);
                }
                catch (Exception error) when (IsRecoverable(error, token)) { warnings.Add("Conflicting official ID " + id + ": " + error.GetType().Name); }
            }
            foreach (var group in entries.GroupBy(entry => entry.InstallFolder, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).ToArray())
            {
                // Two official IDs may represent revisions/forks of the same native package.
                // Never offer two installations into one folder. Keep the newest confirmed artifact.
                var newest = group.OrderByDescending(entry => DateTimeOffset.TryParse(entry.ReleasePublishedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date) ? date : DateTimeOffset.MinValue).First();
                var confirmedReplacement = IsConfirmedOfficialReplacement(group.ToArray(), newest, items);
                foreach (var duplicate in group.Where(entry => !ReferenceEquals(entry, newest)).ToArray())
                {
                    entries.Remove(duplicate);
                    if (!confirmedReplacement) warnings.Add("Official ID " + duplicate.CatalogPluginId + " shares native folder with " + newest.CatalogPluginId + ".");
                }
            }
            foreach (var group in entries.GroupBy(entry => entry.Repository, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).ToArray())
                foreach (var duplicate in group.Skip(1).ToArray()) { entries.Remove(duplicate); warnings.Add("Conflicting official repository ID " + duplicate.CatalogPluginId + "."); }
            foreach (var entry in entries.ToArray())
                if (baseline.Plugins.Any(existing => existing.CatalogSource != "decky-store" &&
                    (existing.CatalogSource == "playhub" && existing.Repository.Equals(entry.Repository, StringComparison.OrdinalIgnoreCase) ||
                    existing.InstallFolder.Equals(entry.InstallFolder, StringComparison.OrdinalIgnoreCase) && !existing.Repository.Equals(entry.Repository, StringComparison.OrdinalIgnoreCase))))
                { entries.Remove(entry); warnings.Add("Official ID " + entry.CatalogPluginId + " conflicts with a curated native identity."); }
            // A deadline ending identity resolution is an incomplete refresh, not
            // authoritative evidence that unresolved memberships were removed.
            deadline.Token.ThrowIfCancellationRequested();
            if (entries.Count == 0) throw new InvalidDataException("No verified official plugin identities.");
            var catalog = new RemotePluginCatalog { CatalogRevision = 1, VerifiedAt = _clock().ToString("O"), Plugins = entries.AsReadOnly() };
            // The same strict URL, path, source and identity validation protects network and disk data.
            var acceptedBytes = JsonSerializer.SerializeToUtf8Bytes(catalog, JsonOptions);
            catalog = Parse(acceptedBytes);
            OverlayOfficial(baseline, catalog);
            token.ThrowIfCancellationRequested();
            _officialGood = catalog;
            _officialOrigin = "decky-store";
            _officialError = warnings.Count > 0 ? string.Join("; ", warnings) : null;
            _nextOfficialCheck = _clock() + OfficialRefreshInterval;
            try { await SaveOfficialCacheAsync(acceptedBytes, token).ConfigureAwait(false); }
            catch (Exception error) when (IsRecoverable(error, token)) { _officialError = "Official cache write: " + error.Message; }
        }
        catch (Exception error) when (IsRecoverable(error, token))
        {
            _officialError = "Decky catalog: " + error.Message;
            _nextOfficialCheck = _clock() + OfficialRetryInterval;
            if (_officialGood is not null) _officialOrigin = "cache";
        }
    }

    private bool IsConfirmedOfficialReplacement(RemotePluginCatalogEntry[] entries, RemotePluginCatalogEntry newest,
        IReadOnlyDictionary<int, JsonElement> items)
    {
        // The store can retain an older ID alongside a renamed/forked revision.
        // This is normal membership deduplication only when every current artifact
        // has an exact verified pin, the official display names differ at most in
        // spacing, and publication time identifies a strictly newer replacement.
        // Different projects, tied dates and failed identity verification remain diagnostics.
        try
        {
            string Name(RemotePluginCatalogEntry entry) => string.Concat(OfficialText(items[entry.CatalogPluginId], "name", 160).Where(character => !char.IsWhiteSpace(character)));
            if (!DateTimeOffset.TryParse(newest.ReleasePublishedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var newestDate)) return false;
            var name = Name(newest);
            foreach (var entry in entries)
            {
                if (!Name(entry).Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    !TryGetPinnedIdentity(items[entry.CatalogPluginId], entry, out var pinned) ||
                    !pinned.InstallFolder.Equals(entry.InstallFolder, StringComparison.OrdinalIgnoreCase) ||
                    !pinned.Repository.Equals(entry.Repository, StringComparison.OrdinalIgnoreCase) ||
                    !pinned.ReleaseAssetName.Equals(entry.ReleaseAssetName, StringComparison.OrdinalIgnoreCase)) return false;
                if (!ReferenceEquals(entry, newest) &&
                    (!DateTimeOffset.TryParse(entry.ReleasePublishedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date) || date >= newestDate)) return false;
            }
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or JsonException) { return false; }
    }

    private async Task<byte[]> ReadOfficialBytesAsync(string url, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Playhub-RemoteCatalog/1.0");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK) throw new HttpRequestException($"Official catalog HTTP {(int)response.StatusCode}.");
        if (response.Content.Headers.ContentLength is > MaxDocumentBytes) throw new InvalidDataException("Official catalog exceeds the byte limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        return await ReadBoundedAsync(stream, token).ConfigureAwait(false);
    }

    private async Task ResolveOfficialIdentitiesAsync(List<JsonElement> unresolved, List<RemotePluginCatalogEntry> entries,
        RemotePluginCatalogEntry[] known, CancellationToken token)
    {
        var registry = Encoding.UTF8.GetString(await ReadOfficialBytesAsync(OfficialRegistryUrl, token).ConfigureAwait(false));
        var sources = new List<(string Folder, string Repository, string Url)>();
        string? folder = null;
        foreach (var line in registry.Split('\n'))
        {
            var text = line.Trim();
            if (text.StartsWith("[", StringComparison.Ordinal)) folder = null;
            else if (text.StartsWith("path = plugins/", StringComparison.Ordinal)) folder = text["path = plugins/".Length..].Trim();
            else if (folder is not null && text.StartsWith("url = ", StringComparison.Ordinal))
            {
                var url = text[6..].Trim(); if (url.EndsWith(".git", StringComparison.Ordinal)) url = url[..^4];
                var uri = Https(url); var repository = uri?.AbsolutePath.Trim('/');
                if (FileName(folder) && uri is not null && uri.Host is "github.com" or "gitlab.com" && Slug(repository))
                    sources.Add((folder, repository!, url));
            }
        }
        foreach (var item in unresolved)
        {
            var name = OfficialText(item, "name", 160);
            var matching = sources.Where(source => NormalizeIdentity(source.Folder) == NormalizeIdentity(name)).ToArray();
            if (matching.Length != 1) throw new InvalidDataException("Official plugin repository requires verified metadata: " + name);
            var source = matching[0];
            if (known.Any(entry => entry.CatalogSource == "playhub" && entry.Repository.Equals(source.Repository, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Official identity conflicts with Playhub.");
            // Names alone are never repository evidence: require the registry-owned upstream manifest.
            var manifestUrl = source.Url.StartsWith("https://github.com/", StringComparison.Ordinal)
                ? "https://raw.githubusercontent.com/" + source.Repository + "/HEAD/plugin.json"
                : source.Url + "/-/raw/HEAD/plugin.json";
            using var manifest = JsonDocument.Parse(await ReadOfficialBytesAsync(manifestUrl, token).ConfigureAwait(false));
            RejectDuplicateProperties(manifest.RootElement);
            if (OfficialText(manifest.RootElement, "name", 160) != name) throw new InvalidDataException("Official registry manifest name mismatch.");
            entries.Add(OfficialEntry(item, new RemotePluginCatalogEntry { Repository = source.Repository, RepositoryUrl = source.Url,
                InstallFolder = source.Folder, Category = "Utility", ShortDescription = "", LongDescription = "",
                Compatibility = "", Aliases = new[] { name, source.Folder, source.Repository } }));
        }
    }

    private static string NormalizeIdentity(string name) => new(name.Where(char.IsAsciiLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private async Task<RemotePluginCatalogEntry> ResolvePinnedIdentityAsync(JsonElement item, RemotePluginCatalogEntry? previous, CancellationToken token, bool allowVerifiedRepositoryFallback = false)
    {
        // Only completed hash/manifest verification is checkpointed; membership
        // remains a separate all-or-fallback snapshot if the refresh deadline ends.
        if (TryGetPinnedIdentity(item, previous, out var cachedIdentity)) return cachedIdentity;
        var resolved = await ReadPinnedIdentityAsync(item, previous, token, allowVerifiedRepositoryFallback).ConfigureAwait(false);
        ValidateEntry(resolved);
        try { await SavePinnedIdentityAsync(resolved, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception error) when (IsRecoverable(error, token)) { _officialError = "Pinned identity cache write: " + error.GetType().Name; }
        return resolved;
    }

    private bool TryGetPinnedIdentity(JsonElement item, RemotePluginCatalogEntry? previous, out RemotePluginCatalogEntry entry)
    {
        var probe = OfficialEntry(item, previous ?? new RemotePluginCatalogEntry { Category = "Utility" });
        var key = probe.CatalogPluginId + ":" + probe.ReleaseAssetName[..64].ToLowerInvariant();
        if (_pinnedIdentities.TryGetValue(key, out var receipt) &&
            receipt.Entry.Aliases.Contains(OfficialText(item, "name", 160), StringComparer.Ordinal))
        { entry = OfficialEntry(item, receipt.Entry); ValidateEntry(entry); return true; }
        entry = null!; return false;
    }

    private async Task<RemotePluginCatalogEntry> ReadPinnedIdentityAsync(JsonElement item, RemotePluginCatalogEntry? previous, CancellationToken token, bool allowVerifiedRepositoryFallback = false)
    {
        var seed = previous ?? new RemotePluginCatalogEntry { Category = "Utility" };
        var release = OfficialEntry(item, seed);
        using var request = new HttpRequestMessage(HttpMethod.Get, release.CatalogReleaseUrl);
        request.Headers.UserAgent.ParseAdd("Playhub-RemoteCatalog/1.0");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        const int limit = 24 * 1024 * 1024;
        if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentLength is > limit) throw new InvalidDataException("Official identity package unavailable or oversized.");
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var bytes = new MemoryStream(); var buffer = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, limit + 1 - (int)bytes.Length)), token).ConfigureAwait(false);
            if (read == 0) break; bytes.Write(buffer, 0, read);
            if (bytes.Length > limit) throw new InvalidDataException("Official identity package exceeds bound.");
        }
        if (!Convert.ToHexString(SHA256.HashData(bytes.GetBuffer().AsSpan(0, (int)bytes.Length))).Equals(release.ReleaseAssetName[..64], StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Official identity package checksum mismatch.");
        bytes.Position = 0; using var zip = new ZipArchive(bytes, ZipArchiveMode.Read);
        if (zip.Entries.Count is 0 or > 10000) throw new InvalidDataException("Official identity archive entry bound.");
        foreach (var entry in zip.Entries)
        {
            var parts = entry.FullName.Split('/');
            if (entry.FullName.Contains('\\') || entry.FullName.Contains(':') || entry.FullName.StartsWith('/') || parts.Any(part => part is "." or "..") ||
                ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Unsafe identity archive member.");
        }
        var manifests = zip.Entries.Where(entry => entry.FullName.EndsWith("/plugin.json", StringComparison.Ordinal) && entry.FullName.Split('/').Length == 2).ToArray();
        if (manifests.Length != 1) throw new InvalidDataException("Ambiguous official package manifest.");
        var manifestEntry = manifests[0]; var folder = manifestEntry.FullName.Split('/')[0];
        if (!FileName(folder)) throw new InvalidDataException("Unsafe native package folder.");
        async Task<JsonDocument> ReadManifest(ZipArchiveEntry entry)
        {
            if (entry.Length > 65536) throw new InvalidDataException("Identity manifest too large.");
            using var input = entry.Open(); var body = await ReadBoundedAsync(input, token).ConfigureAwait(false);
            if (body.Length > 65536) throw new InvalidDataException("Identity manifest too large.");
            var parsed = JsonDocument.Parse(body); RejectDuplicateProperties(parsed.RootElement); return parsed;
        }
        using var manifest = await ReadManifest(manifestEntry).ConfigureAwait(false);
        if (OfficialText(manifest.RootElement, "name", 160) != OfficialText(item, "name", 160)) throw new InvalidDataException("Pinned package identity mismatch.");
        var urls = new List<string>();
        var packageEntry = zip.GetEntry(folder + "/package.json");
        if (packageEntry is not null)
        {
            using var package = await ReadManifest(packageEntry).ConfigureAwait(false);
            if (package.RootElement.TryGetProperty("homepage", out var home) && home.ValueKind == JsonValueKind.String) urls.Add(home.GetString()!);
            if (package.RootElement.TryGetProperty("repository", out var repository))
            {
                if (repository.ValueKind == JsonValueKind.String) urls.Add(repository.GetString()!);
                else if (repository.ValueKind == JsonValueKind.Object && repository.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String) urls.Add(url.GetString()!);
            }
        }
        if (manifest.RootElement.TryGetProperty("publish", out var publish) && publish.ValueKind == JsonValueKind.Object)
            foreach (var key in new[] { "resourceLink", "image" }) if (publish.TryGetProperty(key, out var url) && url.ValueKind == JsonValueKind.String) urls.Add(url.GetString()!);
        foreach (var value in urls)
        {
            var url = value.StartsWith("git+", StringComparison.Ordinal) ? value[4..] : value;
            var uri = Https(url.Split('#')[0]); if (uri is null || uri.Host is not ("github.com" or "raw.githubusercontent.com" or "gitlab.com")) continue;
            var parts = uri.AbsolutePath.Trim('/').Split('/'); if (parts.Length < 2) continue;
            var repository = parts[0] + "/" + parts[1].Replace(".git", "", StringComparison.Ordinal);
            if (!Slug(repository) || repository.Equals("SteamDeckHomebrew/decky-plugin-template", StringComparison.OrdinalIgnoreCase)) continue;
            return release with { Name = previous is not null && previous.Repository.Equals(repository, StringComparison.OrdinalIgnoreCase) && previous.InstallFolder.Equals(folder, StringComparison.OrdinalIgnoreCase) ? previous.Name : OfficialText(item, "name", 160), InstallFolder = folder, Repository = repository,
                RepositoryUrl = "https://" + (uri.Host == "raw.githubusercontent.com" ? "github.com" : uri.Host) + "/" + repository,
                Aliases = new[] { OfficialText(item, "name", 160), folder, repository }, Compatibility = previous is not null && previous.Repository.Equals(repository, StringComparison.OrdinalIgnoreCase) ? previous.Compatibility : "" };
        }
        if (allowVerifiedRepositoryFallback && previous is not null)
            return release with { InstallFolder = folder, Name = OfficialText(item, "name", 160) };
        throw new InvalidDataException("Pinned package has no verified repository metadata.");
    }
    private static int OfficialId(JsonElement item) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var id) &&
        id.TryGetInt32(out var value) && value > 0 ? value : throw new InvalidDataException("Invalid official plugin ID.");
    private static string OfficialText(JsonElement item, string name, int limit, bool required = true)
    {
        var value = item.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
        if (!Text(value, limit, required)) throw new InvalidDataException("Invalid official " + name + ".");
        return value!;
    }

    private static bool TryOfficialTimestamp(string? text, out DateTimeOffset timestamp)
    {
        timestamp = default;
        return text is not null && Regex.IsMatch(text, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$") &&
            DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp);
    }

    private static RemotePluginCatalogEntry OfficialEntry(JsonElement item, RemotePluginCatalogEntry identity)
    {
        if (!item.TryGetProperty("versions", out var versions) || versions.ValueKind != JsonValueKind.Array || versions.GetArrayLength() is 0 or > 1000)
            throw new InvalidDataException("Invalid official versions.");
        var releases = versions.EnumerateArray().Select(version => new
        {
            Name = OfficialText(version, "name", 64), Hash = OfficialText(version, "hash", 64),
            Created = OfficialText(version, "created", 64)
        }).ToArray();
        if (releases.Any(release => !Regex.IsMatch(release.Hash, "^[a-fA-F0-9]{64}$") ||
            !TryOfficialTimestamp(release.Created, out _)))
            throw new InvalidDataException("Invalid official release checksum/date.");
        var latest = releases.OrderByDescending(release => DateTimeOffset.Parse(release.Created, CultureInfo.InvariantCulture)).First();
        string DateField(string key, string fallback) => item.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String &&
            TryOfficialTimestamp(field.GetString(), out var value)
                ? value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture)
                : DateTimeOffset.Parse(fallback, CultureInfo.InvariantCulture).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
        var first = releases.OrderBy(release => DateTimeOffset.Parse(release.Created, CultureInfo.InvariantCulture)).First().Created;
        var asset = latest.Hash.ToLowerInvariant() + ".zip";
        var description = OfficialText(item, "description", 4096, required: false);
        if (string.IsNullOrWhiteSpace(description)) description = identity.ShortDescription.Length > 0 ? identity.ShortDescription : OfficialText(item, "name", 160);
        var cover = item.TryGetProperty("image_url", out var image) && image.ValueKind == JsonValueKind.String ? image.GetString() ?? "" : "";
        return identity with
        {
            Active = true, CatalogSource = "decky-store", CatalogStatus = "decky", CatalogPluginId = OfficialId(item),
            Author = OfficialText(item, "author", 160), Version = latest.Name, ReleaseAssetName = asset,
            CatalogReleaseUrl = "https://cdn.tzatzikiweeb.moe/file/steam-deck-homebrew/versions/" + asset,
            ReleasePublishedAt = latest.Created, FirstPublishedAt = DateField("created", first),
            UpdatedAt = DateField("updated", latest.Created), ShortDescription = description,
            LongDescription = identity.LongDescription.Length > 0 ? identity.LongDescription : description,
            Name = identity.Name.Length > 0 ? identity.Name : OfficialText(item, "name", 160),
            CoverUrl = cover.Length > 0 ? cover : identity.CoverUrl
        };
    }

    internal static RemotePluginCatalog OverlayOfficial(RemotePluginCatalog baseline, RemotePluginCatalog official)
    {
        var entries = baseline.Plugins.Where(entry => entry.CatalogSource != "decky-store").ToList();
        foreach (var entry in official.Plugins)
        {
            var matches = entries.Where(existing => existing.Repository.Equals(entry.Repository, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Any(existing => existing.CatalogSource == "playhub") || entries.Any(existing => existing.InstallFolder.Equals(entry.InstallFolder, StringComparison.OrdinalIgnoreCase) &&
                !existing.Repository.Equals(entry.Repository, StringComparison.OrdinalIgnoreCase))) continue;
            entries.RemoveAll(existing => existing.Repository.Equals(entry.Repository, StringComparison.OrdinalIgnoreCase));
            entries.Add(entry);
        }
        return baseline with { Plugins = entries.AsReadOnly() };
    }

    private async Task SaveOfficialCacheAsync(byte[] bytes, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(OfficialCachePath)!);
        var temporary = OfficialCachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested(); File.Move(temporary, OfficialCachePath, overwrite: true);
        }
        finally { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}

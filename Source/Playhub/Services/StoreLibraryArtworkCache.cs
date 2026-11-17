// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Playhub.Integrations;
using Playhub.StoreLibrary;

namespace Playhub.Services;

internal sealed class StoreLibraryArtworkCache(string root, Func<StoreLibraryGame, CancellationToken, Task<byte[]?>>? fallbackCover = null,
    HttpClient? httpClient = null) : IDisposable
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(25) };
    private readonly HttpClient http = httpClient ?? Http;
    private readonly string directory = Path.Combine(Path.GetFullPath(root), "store-library-artwork");
    private readonly ConcurrentDictionary<string, string> completed = new();
    private readonly ConcurrentDictionary<string, Lazy<Task>> pending = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> retryAt = new();
    private readonly SemaphoreSlim concurrency = new(4);
    private readonly CancellationTokenSource lifetime = new();
    private bool disposed;

    internal string? Resolve(StoreLibraryGame game, Action changed)
    {
        if (game.ArtworkKey != game.Key.Store.ToString().ToLowerInvariant() + ":" + game.Key.Id) return null;
        if (disposed || string.IsNullOrWhiteSpace(game.Title) || game.Title.Length > 512) return null;
        // Old account snapshots may contain logoUrl in ArtworkUrl. Only the new
        // provider contract certifies a cover; never revive that legacy image cache.
        var urls = game.ArtworkKind == "cover"
            ? new[] { game.ArtworkUrl }.Concat(game.ArtworkFallbackUrls ?? []).Where(url => Allowed(game.Key.Store, url, out _))
                .Cast<string>().Distinct(StringComparer.Ordinal).Take(4).ToArray()
            : [];
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("store-cover-v2\0" + game.ArtworkKey + "\0" + game.Title + "\0" + string.Join('\0', urls)))).ToLowerInvariant();
        if (completed.TryGetValue(key, out var file) && File.Exists(file)) return file;
        string index = Path.Combine(directory, key + ".txt");
        file = ReadIndex(index);
        if (file is not null) { completed[key] = file; return file; }
        if (retryAt.TryGetValue(key, out var retry) && retry > DateTimeOffset.UtcNow) return null;
        // Download validation and image decoding must not resume on WinUI's UI
        // thread. Only realized cards call Resolve, so the queue stays viewport sized.
        _ = pending.GetOrAdd(key, _ => new Lazy<Task>(() => Task.Run(() => AcquireCover(game, urls, key, index, changed)))).Value;
        return null;
    }
    private string? ReadIndex(string index)
    {
        try
        {
            ApplicationIntegrationDataStore.GuardPath(index);
            if (File.Exists(index) && new FileInfo(index).Length < 100)
            {
                string name = File.ReadAllText(index);
                if (Regex.IsMatch(name, "^[a-f0-9]{64}\\.(png|jpg|webp)$", RegexOptions.CultureInvariant))
                {
                    string file = Path.Combine(directory, "images", name);
                    ApplicationIntegrationDataStore.GuardPath(file);
                    if (File.Exists(file)) return file;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
        return null;
    }
    private async Task AcquireCover(StoreLibraryGame game, string[] urls, string key, string index, Action changed)
    {
        bool entered = false;
        try
        {
            await concurrency.WaitAsync(lifetime.Token); entered = true;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            linked.CancelAfter(TimeSpan.FromSeconds(55));
            foreach (string url in urls)
            {
                try
                {
                    using var attempt = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
                    attempt.CancelAfter(TimeSpan.FromSeconds(10));
                    var bytes = await Download(game.Key.Store, new Uri(url), attempt.Token);
                    await Publish(game.ArtworkKey!, bytes, key, index, changed, attempt.Token);
                    return;
                }
                catch (Exception e) when (AcquisitionFailure(e) && !linked.IsCancellationRequested) { }
            }
            if (fallbackCover is not null)
            {
                var bytes = await fallbackCover(game, linked.Token);
                if (bytes is not null)
                {
                    await Publish(game.ArtworkKey!, bytes, key, index, changed, linked.Token);
                    return;
                }
            }
            retryAt[key] = DateTimeOffset.UtcNow.AddMinutes(urls.Length == 0 ? 60 : 5);
        }
        catch (Exception e) when (AcquisitionFailure(e))
        { retryAt[key] = DateTimeOffset.UtcNow.AddMinutes(5); }
        finally { if (entered) concurrency.Release(); pending.TryRemove(key, out _); }
    }
    internal string? ResolveXbox(StoreXboxCatalogGame game, Action changed)
    {
        if (!Regex.IsMatch(game.ProductId, "^[A-Z0-9]{12}$")) return null;
        return ResolveImage(StoreKind.Xbox, "xbox-catalog:" + game.ProductId, game.ArtworkUrl, changed);
    }
    private string? ResolveImage(StoreKind store, string artworkKey, string? artworkUrl, Action changed)
    {
        if (disposed || !Allowed(store, artworkUrl, out var uri)) return null;
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(artworkKey + "\0" + uri.AbsoluteUri))).ToLowerInvariant();
        if (completed.TryGetValue(key, out var file) && File.Exists(file)) return file;
        string index = Path.Combine(directory, key + ".txt");
        try
        {
            ApplicationIntegrationDataStore.GuardPath(index);
            if (File.Exists(index) && new FileInfo(index).Length < 100)
            {
                string name = File.ReadAllText(index);
                if (Regex.IsMatch(name, "^[a-f0-9]{64}\\.(png|jpg|webp)$", RegexOptions.CultureInvariant))
                {
                    file = Path.Combine(directory, "images", name);
                    ApplicationIntegrationDataStore.GuardPath(file);
                    if (File.Exists(file)) { completed[key] = file; return file; }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
        if (retryAt.TryGetValue(key, out var retry) && retry > DateTimeOffset.UtcNow) return null;
        _ = pending.GetOrAdd(key, _ => new Lazy<Task>(() => Task.Run(() => Acquire(store, artworkKey, uri, key, index, changed)))).Value;
        return null;
    }
    private async Task Acquire(StoreKind store, string artworkKey, Uri uri, string key, string index, Action changed)
    {
        bool entered = false;
        try
        {
            await concurrency.WaitAsync(lifetime.Token); entered = true;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            linked.CancelAfter(TimeSpan.FromSeconds(30));
            var bytes = await Download(store, uri, linked.Token);
            await Publish(artworkKey, bytes, key, index, changed, linked.Token);
        }
        catch (Exception e) when (AcquisitionFailure(e))
        { retryAt[key] = DateTimeOffset.UtcNow.AddMinutes(5); }
        finally { if (entered) concurrency.Release(); pending.TryRemove(key, out _); }
    }
    private static bool AcquisitionFailure(Exception e) => e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException
        or InvalidOperationException or HttpRequestException or OperationCanceledException or System.Runtime.InteropServices.COMException;
    private async Task Publish(string artworkKey, byte[] bytes, string key, string index, Action changed, CancellationToken token)
    {
        string path = await new ApplicationImageCache(directory).SaveAsync("pc:store:" + artworkKey, "cover", bytes, token);
        ApplicationIntegrationDataStore.GuardPath(index);
        Directory.CreateDirectory(directory);
        string temporary = index + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temporary, Path.GetFileName(path), token); token.ThrowIfCancellationRequested(); File.Move(temporary, index, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        completed[key] = path;
        if (!disposed) changed();
    }
    internal static bool Allowed(StoreKind store, string? value, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) || parsed.Scheme != "https" || !parsed.IsDefaultPort ||
            parsed.UserInfo.Length != 0 || parsed.AbsoluteUri.Length > 8192 || parsed.Fragment.Length != 0) return false;
        bool allowed = store switch
        {
            StoreKind.Epic => parsed.Host is "cdn1.epicgames.com" or "cdn2.epicgames.com" or "cdn.unrealengine.com",
            StoreKind.Xbox => parsed.Host is "store-images.s-microsoft.com" or "store-images.microsoft.com" or "images-eds-ssl.xboxlive.com",
            StoreKind.Gog => parsed.Host is "images.gog.com" or "images.gog-statics.com" or "images-1.gog-statics.com" or "images-2.gog-statics.com" or "images-3.gog-statics.com" or "images-4.gog-statics.com",
            StoreKind.Amazon => parsed.Host is "m.media-amazon.com" or "images-na.ssl-images-amazon.com" or "images-eu.ssl-images-amazon.com",
            _ => false
        };
        if (allowed) uri = parsed;
        return allowed;
    }
    private async Task<byte[]> Download(StoreKind store, Uri uri, CancellationToken token)
    {
        for (int redirect = 0; redirect <= 3; redirect++)
        {
            if (!Allowed(store, uri.AbsoluteUri, out uri)) throw new InvalidDataException("Unsupported artwork source.");
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            { uri = new Uri(uri, response.Headers.Location ?? throw new InvalidDataException("Artwork redirect missing.")); continue; }
            response.EnsureSuccessStatusCode();
            const int limit = 25 * 1024 * 1024;
            if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Artwork is too large.");
            using var source = await response.Content.ReadAsStreamAsync(token); using var data = new MemoryStream();
            var buffer = new byte[65536]; int count;
            while ((count = await source.ReadAsync(buffer, token)) > 0)
            { if (data.Length + count > limit) throw new InvalidDataException("Artwork is too large."); data.Write(buffer, 0, count); }
            return data.ToArray();
        }
        throw new InvalidDataException("Artwork redirect limit reached.");
    }
    public void Dispose() { if (disposed) return; disposed = true; lifetime.Cancel(); }
}

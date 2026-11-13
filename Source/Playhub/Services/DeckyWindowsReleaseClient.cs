using Playhub.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Playhub.Services;

internal sealed partial class DeckyWindowsReleaseClient(HttpClient http, Architecture architecture, string? bundledRoot = null)
{
    private readonly BundledDeckyWindowsRelease _bundled = new(bundledRoot);
    private const string ApiRoot = "https://api.github.com/repos/SteamDeckHomebrew/decky-loader/releases";
    private const string AssetRoot = "https://github.com/SteamDeckHomebrew/decky-loader/releases/download/";
    private const int MaxAssetSize = 64 * 1024 * 1024;
    private const int MinAssetSize = 500_000;
    private const int MaxMetadataSize = 4 * 1024 * 1024;

    internal sealed record Asset(string InstallName, Uri Url, int Size, string Digest);
    internal sealed record Release(DeckyWindowsRelease Summary, Asset Console, Asset Background, bool OfflineFallback = false)
    {
        internal IReadOnlyList<Asset> Assets => [Console, Background];
    }

    internal async Task<Release> GetAsync(string? tag = null)
    {
        if (tag is not null && !IsStableTag(tag))
            throw new ArgumentException("La versione di Decky selezionata non è valida.", nameof(tag));
        try
        {
            Release release;
            try { release = await GetApiReleaseAsync(tag).ConfigureAwait(false); }
            catch (ReleaseApiThrottledException) { release = await GetWebReleaseAsync(tag).ConfigureAwait(false); }
            return release;
        }
        catch (Exception error) when ((tag is null || tag == BundledDeckyWindowsRelease.UpstreamTag) && error is HttpRequestException or TaskCanceledException)
        {
            if (_bundled.Get(architecture, offline: true) is { } local) return local;
            throw;
        }
    }

    private async Task<Release> GetApiReleaseAsync(string? tag)
    {
        using var metadata = await GetMetadataAsync(tag is null ? "/latest" : "/tags/" + tag).ConfigureAwait(false);
        var summary = ParseSummary(metadata.RootElement);
        if (summary is null || (tag is not null && summary.Tag != tag))
            throw new InvalidDataException("I dati della release ufficiale di Decky non sono validi.");

        var release = ParseAssets(metadata.RootElement, summary);
        if (release is not null) return release;
        if (tag is null)
        {
            var compatible = await ListCompatibleAsync().ConfigureAwait(false);
            if (compatible.Length > 0) return compatible[0];
        }
        throw new InvalidDataException("La release ufficiale di Decky non contiene due eseguibili Windows verificabili per questo PC.");
    }

    internal async Task<IReadOnlyList<DeckyWindowsRelease>> ListAsync()
    {
        Release[] compatible;
        try
        {
            try { compatible = await ListCompatibleAsync().ConfigureAwait(false); }
            catch (ReleaseApiThrottledException) { compatible = await ListWebCompatibleAsync().ConfigureAwait(false); }
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            if (_bundled.Get(architecture, offline: true) is { } local) return [local.Summary];
            throw;
        }
        return compatible.Select(release => release.Summary).ToArray();
    }

    private async Task<Release[]> ListCompatibleAsync()
    {
        using var metadata = await GetMetadataAsync("?per_page=30").ConfigureAwait(false);
        if (metadata.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("L'elenco delle release di Decky non è valido.");
        return metadata.RootElement.EnumerateArray()
            .Select(element => ParseSummary(element) is { } summary ? ParseAssets(element, summary) : null)
            .OfType<Release>().DistinctBy(release => release.Summary.Tag)
            .OrderByDescending(release => Version.Parse(release.Summary.Tag[1..]))
            .ToArray();
    }

    internal async Task<byte[]> DownloadAsync(Asset asset)
    {
        if (asset.Url.IsFile) return await _bundled.ReadAsync(asset, architecture).ConfigureAwait(false);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        Uri uri = asset.Url;
        for (int attempt = 0; attempt <= 3; attempt++)
        {
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var redirect = response.Headers.Location;
                if (redirect is null || !redirect.IsAbsoluteUri || !IsGitHubAssetRedirect(redirect))
                    throw new InvalidDataException("Il download di Decky ha restituito un reindirizzamento non valido.");
                uri = redirect;
                continue;
            }
            response.EnsureSuccessStatusCode();
            long? length = response.Content.Headers.ContentLength;
            if (length is long declared && (!IsAssetSize(declared) || (asset.Size > 0 && declared != asset.Size)))
                throw new InvalidDataException("La dimensione dell'eseguibile Decky non corrisponde alla release ufficiale.");

            using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            int expected = asset.Size > 0 ? asset.Size : (int)(length ?? 0);
            byte[] bytes;
            if (expected > 0)
            {
                bytes = new byte[expected];
                await stream.ReadExactlyAsync(bytes, timeout.Token).ConfigureAwait(false);
                if (await stream.ReadAsync(new byte[1], timeout.Token).ConfigureAwait(false) != 0)
                    throw new InvalidDataException("La dimensione dell'eseguibile Decky non corrisponde alla release ufficiale.");
            }
            else bytes = await ReadBoundedAsync(stream, MaxAssetSize, timeout.Token).ConfigureAwait(false);
            if (!IsAssetSize(bytes.Length) || !DigestMatches(SHA256.HashData(bytes), asset.Digest) || !IsWindowsExecutable(bytes))
                throw new InvalidDataException("L'eseguibile Decky non supera la verifica della release ufficiale.");
            return bytes;
        }
        throw new InvalidDataException("Troppi reindirizzamenti durante il download di Decky.");
    }

    internal static bool DigestMatches(byte[] hash, string digest) =>
        CryptographicOperations.FixedTimeEquals(hash, Convert.FromHexString(digest));

    internal static bool IsAssetSize(long bytes) => bytes is >= MinAssetSize and <= MaxAssetSize;

    private async Task<JsonDocument> GetMetadataAsync(string suffix)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiRoot + suffix);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.TooManyRequests || (response.StatusCode == HttpStatusCode.Forbidden &&
            (response.Headers.RetryAfter is not null || (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) &&
                remaining.Any(value => value.Trim() == "0")))))
            throw new ReleaseApiThrottledException();
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaxMetadataSize)
            throw new InvalidDataException("I dati della release di Decky sono troppo grandi.");
        using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        return JsonDocument.Parse(await ReadBoundedAsync(stream, MaxMetadataSize, timeout.Token).ConfigureAwait(false));
    }

    private sealed class ReleaseApiThrottledException : HttpRequestException { }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int limit, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int count;
        while ((count = await stream.ReadAsync(chunk, token).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length + count > limit)
                throw new InvalidDataException("I dati della release di Decky sono troppo grandi.");
            buffer.Write(chunk, 0, count);
        }
        return buffer.ToArray();
    }

    private static DeckyWindowsRelease? ParseSummary(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False ||
            !element.TryGetProperty("prerelease", out var prerelease) || prerelease.ValueKind != JsonValueKind.False ||
            !IsStableTag(Property(element, "tag_name")) ||
            !element.TryGetProperty("published_at", out var date) || date.ValueKind != JsonValueKind.String ||
            !date.TryGetDateTimeOffset(out var published) ||
            !element.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;
        return new(Property(element, "tag_name"), published);
    }

    private Release? ParseAssets(JsonElement element, DeckyWindowsRelease summary)
    {
        var assets = element.GetProperty("assets");
        var console = ParseAsset(assets, summary.Tag, WindowsAssetPrefix + ".exe", DeckyFileUpgrade.ConsoleName);
        var background = ParseAsset(assets, summary.Tag, WindowsAssetPrefix + "-noconsole.exe", DeckyFileUpgrade.BackgroundName);
        return console is null || background is null ? null : new(summary, console, background);
    }

    private string WindowsAssetPrefix => architecture switch
    {
        Architecture.X64 => "PluginLoader-win",
        Architecture.Arm64 => "PluginLoader-win-arm64",
        _ => throw new PlatformNotSupportedException("Decky per Windows richiede un PC x64 o ARM64.")
    };

    private static Asset? ParseAsset(JsonElement assets, string tag, string name, string installName)
    {
        var matches = assets.EnumerateArray().Where(a => Property(a, "name") == name).ToArray();
        if (matches.Length != 1) return null;
        var asset = matches[0];
        string expected = AssetRoot + tag + "/" + name;
        string digest = Property(asset, "digest");
        if (Property(asset, "state") != "uploaded" || Property(asset, "browser_download_url") != expected ||
            !asset.TryGetProperty("size", out var size) || size.ValueKind != JsonValueKind.Number ||
            !size.TryGetInt32(out int bytes) || !IsAssetSize(bytes) ||
            !Regex.IsMatch(digest, "\\Asha256:[0-9a-fA-F]{64}\\z", RegexOptions.CultureInvariant))
            return null;
        return new(installName, new Uri(expected), bytes, digest[7..]);
    }

    private bool IsWindowsExecutable(byte[] bytes)
    {
        try
        {
            using var reader = new PEReader(new MemoryStream(bytes, writable: false));
            var headers = reader.PEHeaders;
            return headers.PEHeader?.Magic == PEMagic.PE32Plus && !headers.IsDll &&
                (headers.CoffHeader.Characteristics & Characteristics.ExecutableImage) != 0 &&
                headers.CoffHeader.Machine == (architecture == Architecture.Arm64 ? Machine.Arm64 : Machine.Amd64);
        }
        catch (BadImageFormatException) { return false; }
        catch (IOException) { return false; }
    }

    private static bool IsGitHubAssetRedirect(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort &&
        string.IsNullOrEmpty(uri.UserInfo) && uri.Host is "release-assets.githubusercontent.com" or "objects.githubusercontent.com";

    private static bool IsStableTag(string tag) => Regex.IsMatch(tag,
        "\\Av(?:0|[1-9][0-9]{0,5})\\.(?:0|[1-9][0-9]{0,5})\\.(?:0|[1-9][0-9]{0,5})\\z", RegexOptions.CultureInvariant);

    private static string Property(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : "";
}

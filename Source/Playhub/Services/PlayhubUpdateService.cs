using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Playhub.Services;

/// <summary>
/// Controlla se su GitHub esiste una release di Playhub più recente di quella
/// installata e, su richiesta esplicita dell'utente, scarica l'installer della
/// release mostrando un avanzamento verificabile.
/// </summary>
public sealed class PlayhubUpdateService
{
    private const string PublicInstallerName = "Playhub Setup.exe";
    private const string LegacyInstallerPrefix = "Playhub-Setup";
    private static readonly TimeSpan DownloadStallTimeout = TimeSpan.FromSeconds(60);
    private static readonly HttpClient Http = CreateHttpClient();
    private readonly TimeProvider _downloadTimeProvider;

    public PlayhubUpdateService() : this(TimeProvider.System) { }

    internal PlayhubUpdateService(TimeProvider downloadTimeProvider)
    {
        ArgumentNullException.ThrowIfNull(downloadTimeProvider);
        _downloadTimeProvider = downloadTimeProvider;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Playhub/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public sealed record UpdateInfo(
        bool IsNewer,
        string LatestVersion,
        string CurrentVersion,
        string? ReleaseUrl,
        string? Notes,
        string? DownloadUrl,
        string? AssetName,
        long DownloadSize,
        string? Sha256Digest);

    public sealed record DownloadProgress(long BytesReceived, long TotalBytes, double Fraction);

    /// <summary>
    /// Interroga releases/latest del repository indicato (es. "LoZazaMastro/Playhub")
    /// e confronta il tag con la versione corrente. Restituisce null se la rete
    /// non risponde o non esiste alcuna release pubblicata.
    /// </summary>
    public async Task<UpdateInfo?> CheckAsync(string repository, string currentVersion, string? releaseTag = null)
    {
        if (string.IsNullOrWhiteSpace(repository))
        {
            return null;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var releasePath = string.IsNullOrWhiteSpace(releaseTag)
                ? "latest" : "tags/" + Uri.EscapeDataString(releaseTag);
            var json = await Http.GetStringAsync(
                $"https://api.github.com/repos/{repository.Trim('/')}/releases/{releasePath}", cts.Token);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag))
            {
                return releaseTag is null ? await CheckFromLatestRedirectAsync(repository, currentVersion) : null;
            }
            if (releaseTag is not null && !string.Equals(tag, releaseTag, StringComparison.Ordinal)) return null;

            var url = root.TryGetProperty("html_url", out var h) ? h.GetString() : null;
            var notes = root.TryGetProperty("body", out var b) ? b.GetString() : null;
            var asset = FindInstallerAsset(root, tag);

            var latest = ParseVersion(tag);
            var current = ParseVersion(currentVersion);
            var isNewer = latest is not null && current is not null && latest > current;

            return new UpdateInfo(
                isNewer,
                NormalizeTag(tag),
                currentVersion,
                url,
                notes,
                asset.DownloadUrl,
                asset.Name,
                asset.Size,
                asset.Digest);
        }
        catch
        {
            // Unlike Atom, GitHub's latest-release redirect excludes prereleases.
            return releaseTag is null ? await CheckFromLatestRedirectAsync(repository, currentVersion) : null;
        }
    }

    private static async Task<UpdateInfo?> CheckFromLatestRedirectAsync(string repository, string currentVersion)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var response = await Http.GetAsync(
                $"https://github.com/{repository.Trim('/')}/releases/latest",
                HttpCompletionOption.ResponseHeadersRead, cts.Token);
            response.EnsureSuccessStatusCode();
            var releaseUri = response.RequestMessage?.RequestUri;
            var releasePrefix = $"/{repository.Trim('/')}/releases/tag/";
            if (releaseUri is null || releaseUri.Scheme != Uri.UriSchemeHttps ||
                !string.Equals(releaseUri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
                !releaseUri.AbsolutePath.StartsWith(releasePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var releaseUrl = releaseUri.AbsoluteUri;
            var tag = Uri.UnescapeDataString(releaseUri.AbsolutePath[releasePrefix.Length..]);
            var latest = ParseVersion(tag);
            var current = ParseVersion(currentVersion);
            var isNewer = latest is not null && current is not null && latest > current;
            var version = NormalizeTag(tag);
            var guessedAsset = PublicInstallerName;
            var guessedUrl = $"https://github.com/{repository.Trim('/')}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(guessedAsset)}";
            return new UpdateInfo(isNewer, version, currentVersion, releaseUrl, null, guessedUrl, guessedAsset, 0, null);
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeTag(string tag) => tag.Trim().TrimStart('v', 'V');

    public async Task<string> DownloadInstallerAsync(
        UpdateInfo info,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(info.DownloadUrl) ||
            !Uri.TryCreate(info.DownloadUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("La release non contiene un installer Playhub valido.");
        }

        AppPaths.EnsureRoots();
        var folder = Path.Combine(AppPaths.DownloadsRoot, "updates");
        Directory.CreateDirectory(folder);
        var safeName = Path.GetFileName(info.AssetName ?? "");
        if (string.IsNullOrWhiteSpace(safeName) || !safeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            safeName = PublicInstallerName;
        }

        var destination = Path.Combine(folder, safeName);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using var stallTimeout = new CancellationTokenSource(DownloadStallTimeout, _downloadTimeProvider);
            using var downloadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stallTimeout.Token);
            var downloadToken = downloadCancellation.Token;
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await Http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                downloadToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            stallTimeout.CancelAfter(DownloadStallTimeout);

            var total = response.Content.Headers.ContentLength ?? info.DownloadSize;
            if (total > 1_000_000_000)
            {
                throw new InvalidDataException("L'installer indicato dalla release è troppo grande.");
            }

            await using var source = await response.Content.ReadAsStreamAsync(downloadToken).ConfigureAwait(false);
            stallTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
            await using var output = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[128 * 1024];
            long received = 0;
            var progressClock = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                // Each network read gets a fresh budget; local writes do not count as a network stall.
                stallTimeout.CancelAfter(DownloadStallTimeout);
                var read = await source.ReadAsync(buffer, downloadToken).ConfigureAwait(false);
                downloadToken.ThrowIfCancellationRequested();
                stallTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
                if (read == 0) break;
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                hash.AppendData(buffer, 0, read);
                received += read;
                if (progressClock.ElapsedMilliseconds >= 50)
                {
                    progress?.Report(new DownloadProgress(
                        received,
                        total,
                        total > 0 ? Math.Clamp(received / (double)total, 0, 1) : 0));
                    progressClock.Restart();
                }
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            try { output.Flush(flushToDisk: true); } catch { }

            if (received < 64 * 1024 || (total > 0 && received != total) ||
                (info.DownloadSize > 0 && received != info.DownloadSize))
            {
                throw new InvalidDataException("Il download dell'installer è incompleto.");
            }

            var actualDigest = Convert.ToHexString(hash.GetHashAndReset());
            var expectedDigest = NormalizeDigest(info.Sha256Digest);
            if (expectedDigest is not null &&
                !string.Equals(actualDigest, expectedDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("La verifica SHA-256 dell'installer non è riuscita.");
            }

            output.Close();
            await using (var header = File.OpenRead(temporary))
            {
                if (header.ReadByte() != 'M' || header.ReadByte() != 'Z')
                {
                    throw new InvalidDataException("Il file scaricato non è un installer Windows valido.");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
            progress?.Report(new DownloadProgress(received, received, 1));
            return destination;
        }
        catch
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            throw;
        }
    }

    private static (string? Name, string? DownloadUrl, long Size, string? Digest) FindInstallerAsset(
        JsonElement release,
        string tag)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return (null, null, 0, null);
        }

        var normalizedVersion = NormalizeTag(tag);
        var candidates = assets.EnumerateArray()
            .Select(asset => new
            {
                Name = asset.TryGetProperty("name", out var name) ? name.GetString() : null,
                Url = asset.TryGetProperty("browser_download_url", out var url) ? url.GetString() : null,
                Size = asset.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) ? bytes : 0,
                Digest = asset.TryGetProperty("digest", out var digest) ? digest.GetString() : null
            })
            .Where(asset =>
                !string.IsNullOrWhiteSpace(asset.Name) &&
                asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(asset.Name, PublicInstallerName, StringComparison.OrdinalIgnoreCase) ||
                 asset.Name.StartsWith(LegacyInstallerPrefix, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(asset =>
                string.Equals(asset.Name, PublicInstallerName, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(asset =>
                asset.Name!.Contains(normalizedVersion, StringComparison.OrdinalIgnoreCase))
            .ThenBy(asset => asset.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var selected = candidates.FirstOrDefault();
        return selected is null
            ? (null, null, 0, null)
            : (selected.Name, selected.Url, selected.Size, selected.Digest);
    }

    private static string? NormalizeDigest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest)) return null;
        var value = digest.Trim();
        if (value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) value = value[7..];
        return value.Length == 64 && value.All(Uri.IsHexDigit) ? value : null;
    }

    private static Version? ParseVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var cleaned = raw.Trim().TrimStart('v', 'V');

        // Tieni solo la parte numerica iniziale (es. "1.2.0-beta" -> "1.2.0").
        var end = 0;
        while (end < cleaned.Length && (char.IsDigit(cleaned[end]) || cleaned[end] == '.'))
        {
            end++;
        }

        cleaned = cleaned[..end].Trim('.');
        if (cleaned.Length == 0)
        {
            return null;
        }

        // Version richiede almeno major.minor.
        if (!cleaned.Contains('.'))
        {
            cleaned += ".0";
        }

        return Version.TryParse(cleaned, out var version)
            ? new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision))
            : null;
    }
}

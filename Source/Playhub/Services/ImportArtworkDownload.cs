using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Playhub.Integrations;
using Playhub.Models;

namespace Playhub.Services;

// Original import transport. API lookup and image transfer have independent
// bounded retries; only a validated immutable image can become a selection.
internal static class ImportArtworkDownload
{
    internal static Func<bool> SourceCurrent(UwpGameEntry game)
    {
        var expected = (game.SteamIdentity, game.Aumid, game.LocalExecutablePath, game.Executable, game.SourceLaunchArguments, game.Name, game.SteamGridDbArtworkDisabled);
        return () => expected == (game.SteamIdentity, game.Aumid, game.LocalExecutablePath, game.Executable, game.SourceLaunchArguments, game.Name, game.SteamGridDbArtworkDisabled);
    }
    internal static string Identity(UwpGameEntry game)
    {
        string source = !string.IsNullOrWhiteSpace(game.SteamIdentity) ? "steam:" + game.SteamIdentity
            : !string.IsNullOrWhiteSpace(game.Aumid) ? "aumid:" + game.Aumid
            : !string.IsNullOrWhiteSpace(game.LocalExecutablePath) ? "exe:" + game.LocalExecutablePath + "\0" + game.SourceLaunchArguments
            : !string.IsNullOrWhiteSpace(game.Executable) ? "exe:" + game.Executable + "\0" + game.SourceLaunchArguments
            : throw new InvalidOperationException("A stable artwork game identity is required.");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant()[..24];
    }
    internal static string CacheIdentity(UwpGameEntry game) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(Identity(game) + "\0" + game.SteamGridDbGameId + "\0" + game.Name))).ToLowerInvariant()[..24];

    internal static bool CoverMatches(UwpGameEntry game, string format, string ownedCacheRoot)
    {
        if (!File.Exists(game.SteamGridDbCoverPath)) return false;
        if (game.ArtworkChoices.ContainsKey("cover") || game.SteamArtworkTypes.Contains("cover")) return true;
        string path = Path.GetFullPath(game.SteamGridDbCoverPath), cache = Path.GetFullPath(ownedCacheRoot) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(cache, StringComparison.OrdinalIgnoreCase)) return true; // External/Steam artwork is not an automatic cache choice.
        string relative = path[cache.Length..];
        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Contains(CoverFormat.CoverCacheFolder(format), StringComparer.OrdinalIgnoreCase);
    }

    internal static async Task<byte[]> ReadAsync(HttpClient client, Uri uri, string? apiKey = null, CancellationToken ct = default)
    {
        if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("An HTTPS artwork source is required.");
        if (apiKey is not null && (uri.Host != "www.steamgriddb.com" || !uri.AbsolutePath.StartsWith("/api/v2/", StringComparison.Ordinal)))
            throw new ArgumentException("Artwork credentials cannot be sent to an image CDN.");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(15));
        int limit = apiKey is null ? 25 * 1024 * 1024 : 4 * 1024 * 1024;
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                if (apiKey is not null) request.Headers.Add("Authorization", "Bearer " + apiKey);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token);
                response.EnsureSuccessStatusCode();
                long? expected = response.Content.Headers.ContentLength;
                if (expected > limit) throw new InvalidDataException("Artwork response is too large.");
                using var input = await response.Content.ReadAsStreamAsync(budget.Token);
                using var output = new MemoryStream();
                var buffer = new byte[65536]; int count;
                while ((count = await input.ReadAsync(buffer, budget.Token)) > 0)
                {
                    if (output.Length + count > limit) throw new InvalidDataException("Artwork response is too large.");
                    output.Write(buffer, 0, count);
                }
                if (expected is not null && output.Length != expected) throw new IOException("Artwork response was incomplete.");
                budget.Token.ThrowIfCancellationRequested();
                return output.ToArray();
            }
            catch (Exception error) when (attempt < 2 && !budget.IsCancellationRequested && Retryable(error))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), budget.Token);
            }
        }
    }

    private static bool Retryable(Exception error) => error switch
    {
        HttpRequestException e => e.StatusCode is null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout,
        IOException => true,
        OperationCanceledException => true,
        _ => false
    };

    internal static async Task<string> SaveAsync(string directory, string identity, string type, byte[] bytes, CancellationToken ct)
        => await new ApplicationImageCache(directory).SaveAsync("pc:import-artwork:" + identity, type, bytes, ct);

    internal static async Task RequireCoverShapeAsync(string path, string format, CancellationToken ct)
    {
#if PLAYHUB_NATIVE_INTEGRATIONS
        var bytes = await File.ReadAllBytesAsync(path, ct);
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        using (var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0)))
        { writer.WriteBytes(bytes); await writer.StoreAsync().AsTask(ct); await writer.FlushAsync().AsTask(ct); }
        var image = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream).AsTask(ct);
        uint width = image.PixelWidth, height = image.PixelHeight;
#else
        await Task.CompletedTask;
        using var input = File.OpenRead(path); using var image = System.Drawing.Image.FromStream(input, false, true);
        uint width = (uint)image.Width, height = (uint)image.Height;
#endif
        if (CoverFormat.IsSquare(format) ? width != height : height <= width)
            throw new InvalidDataException("Artwork does not match the requested cover shape.");
    }

    internal static string? Cached(string directory, string key)
    {
        try
        {
            string index = Path.Combine(directory, key + ".json");
            ApplicationIntegrationDataStore.GuardPath(index);
            if (!File.Exists(index) || new FileInfo(index).Length > 512) return null;
            using var json = JsonDocument.Parse(File.ReadAllBytes(index));
            string name = json.RootElement.GetProperty("file").GetString() ?? "";
            string expected = json.RootElement.GetProperty("sha256").GetString() ?? "";
            if (!Regex.IsMatch(name, "^[a-f0-9]{64}\\.(png|jpg|webp)$") || !Regex.IsMatch(expected, "^[A-F0-9]{64}$")) return null;
            string path = Path.Combine(directory, "images", name);
            ApplicationIntegrationDataStore.GuardPath(path);
            if (!File.Exists(path) || new FileInfo(path).Length is < 16 or > 25 * 1024 * 1024) return null;
            return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) == expected ? path : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException) { return null; }
    }

    internal static async Task IndexAsync(string directory, string key, string path, CancellationToken ct)
    {
        string index = Path.Combine(directory, key + ".json");
        ApplicationIntegrationDataStore.GuardPath(index);
        string temporary = index + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var record = new { file = Path.GetFileName(path), sha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path, ct))) };
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(record), ct);
            ct.ThrowIfCancellationRequested(); File.Move(temporary, index, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed class RequestState { internal readonly object Gate = new(); internal readonly Dictionary<string,long> Versions = new(); }
    private static readonly ConditionalWeakTable<UwpGameEntry, RequestState> Requests = new();
    internal sealed class Lease
    {
        private readonly UwpGameEntry game;
        private readonly RequestState state;
        private readonly string identity, type, previous;
        private readonly Func<bool> sourceCurrent;
        private readonly int providerId;
        private readonly bool disabled;
        private readonly bool chosen;
        private readonly string? choice;
        private readonly long version;
        private Lease(UwpGameEntry game, string type, RequestState state, long version)
        {
            this.game = game; this.type = type; this.state = state; this.version = version;
            identity = Identity(game); providerId = game.SteamGridDbGameId; disabled = game.SteamGridDbArtworkDisabled;
            sourceCurrent = SourceCurrent(game);
            previous = ImportedArtworkSelection.Get(game, type);
            chosen = game.ArtworkChoices.TryGetValue(type, out choice);
        }
        private bool Current()
        {
            try { return sourceCurrent() && state.Versions[type] == version && identity == Identity(game) && providerId == game.SteamGridDbGameId
                && disabled == game.SteamGridDbArtworkDisabled && previous == ImportedArtworkSelection.Get(game, type)
                && chosen == game.ArtworkChoices.ContainsKey(type) && (!chosen || choice == game.ArtworkChoices[type]); }
            catch { return false; }
        }
        internal bool Apply(Action publish)
        {
            lock (state.Gate) { if (!Current()) return false; publish(); return true; }
        }
        internal static Lease Begin(UwpGameEntry game, string type)
        {
            var state = Requests.GetOrCreateValue(game);
            lock (state.Gate) { long version = state.Versions.GetValueOrDefault(type) + 1; state.Versions[type] = version; return new(game, type, state, version); }
        }
    }
}

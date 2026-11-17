// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System.Text.Json;

namespace Playhub.StoreLibrary;

public sealed partial class StoreCliProvider
{
    private sealed record GogCoverCache(string ProductId, string? Url, DateTimeOffset CheckedAt);
    private readonly SemaphoreSlim artworkMetadataGate = new(4, 4);
    internal async Task<IReadOnlyList<ProviderGame>> EnrichGogArtworkBatch(IReadOnlyList<ProviderGame> games, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(12));
        async Task<ProviderGame> One(ProviderGame game)
        {
            try { return await EnrichGogArtwork(game, budget.Token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { return game; }
        }
        // Visible A–Z cards get first admission. A slow/offline public metadata
        // service cannot hold the owned library beyond this global budget.
        var enriched = await Task.WhenAll(games.OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase).Select(One));
        token.ThrowIfCancellationRequested();
        var byId = enriched.ToDictionary(g => g.Id, StringComparer.Ordinal);
        return games.Select(g => byId[g.Id]).ToArray();
    }
    private async Task<ProviderGame> EnrichGogArtwork(ProviderGame game, CancellationToken token)
    {
        if (game.Id.Length > 20 || !game.Id.All(char.IsAsciiDigit)) return game;
        var directory = Path.Combine(root, "artwork-metadata-v1"); var cache = Path.Combine(directory, game.Id + ".json"); string? poster = null;
        try
        {
            if (File.Exists(cache) && new FileInfo(cache).Length <= 16384)
            {
                var previous = JsonSerializer.Deserialize<GogCoverCache>(File.ReadAllText(cache));
                if (previous != null && previous.ProductId == game.Id && DateTimeOffset.UtcNow - previous.CheckedAt < TimeSpan.FromHours(previous.Url == null ? 1 : 72))
                    return WithGogCover(game, StoreCliProvider.OfficialArtwork(StoreKind.Gog, previous.Url));
            }
            await artworkMetadataGate.WaitAsync(token);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(8));
                var uri = new Uri("https://api.gog.com/v2/games/" + game.Id);
                using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (response.IsSuccessStatusCode && response.RequestMessage?.RequestUri == uri)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token); using var bytes = new MemoryStream(); var buffer = new byte[32768]; int count;
                    while ((count = await stream.ReadAsync(buffer, timeout.Token)) > 0)
                    { if (bytes.Length + count > 2 * 1024 * 1024) throw new StoreOperationException("artwork_metadata_invalid"); await bytes.WriteAsync(buffer.AsMemory(0, count), timeout.Token); }
                    using var doc = JsonDocument.Parse(bytes.ToArray()); poster = StoreMetadataArtwork.GogProductCover(doc.RootElement, game.Id);
                }
                Directory.CreateDirectory(directory); var temporary = cache + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.WriteAllText(temporary, JsonSerializer.Serialize(new GogCoverCache(game.Id, poster, DateTimeOffset.UtcNow))); File.Move(temporary, cache, true); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            finally { artworkMetadataGate.Release(); }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or HttpRequestException or OperationCanceledException or StoreOperationException) { token.ThrowIfCancellationRequested(); }
        return WithGogCover(game, poster);
    }
    private static ProviderGame WithGogCover(ProviderGame game, string? poster) => poster == null ? game : game with
    { ArtworkUrl = poster, ArtworkFallbackUrls = game.ArtworkUrl != null && game.ArtworkUrl != poster ? new[] { game.ArtworkUrl } : null };
}

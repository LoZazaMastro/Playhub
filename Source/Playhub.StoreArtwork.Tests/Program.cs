// SPDX-License-Identifier: MIT. Artwork metadata only. No login/tool/game/installer/Steam UI.
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Playhub.StoreLibrary;

var root = Path.Combine(Path.GetTempPath(), "Playhub-Artwork-Checks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
if (args.Length == 4 && args[0] == "--sample-metadata-readonly")
{
    using var queue = JsonDocument.Parse(File.ReadAllText(args[1])); using var amazon = JsonDocument.Parse(File.ReadAllText(args[2]));
    var games = queue.RootElement.GetProperty("Games").Deserialize<StoreLibraryGame[]>()!;
    var provider = new StoreCliProvider(StoreKind.Gog, root, root, Path.Combine(root, "never-executed.exe"));
    if (File.Exists(args[3]))
    {
        using var prior = JsonDocument.Parse(File.ReadAllText(args[3]));
        if (prior.RootElement.TryGetProperty("metadataCache", out var cached) && cached.GetString() is { } existing && Directory.Exists(existing))
        { var target = Path.Combine(root, "gog", "artwork-metadata-v1"); Directory.CreateDirectory(target); foreach (var item in Directory.EnumerateFiles(existing, "*.json")) File.Copy(item, Path.Combine(target, Path.GetFileName(item))); }
    }
    var owned = games.Where(g => g.Key.Store == StoreKind.Gog).Select(g =>
    {
        using var record = JsonDocument.Parse(JsonSerializer.Serialize(new { image = g.ArtworkUrl }));
        return new ProviderGame(g.Key.Id, g.Title, true, null, ArtworkUrl: StoreMetadataArtwork.GogAccountCover(record.RootElement));
    }).ToArray();
    var watch = Stopwatch.StartNew(); var cold = await provider.EnrichGogArtworkBatch(owned, default); var coldMs = watch.ElapsedMilliseconds;
    watch.Restart(); var warm = await provider.EnrichGogArtworkBatch(owned, default); var warmMs = watch.ElapsedMilliseconds;
    var byId = warm.ToDictionary(g => g.Id); var amazonProducts = amazon.RootElement.EnumerateArray().Select(g => g.GetProperty("product")).ToDictionary(p => p.GetProperty("id").GetString()!);
    var xboxCovers = new Dictionary<string, string[]>();
    using var publicMetadata = new HttpClient();
    foreach (var ids in games.Where(g => g.Key.Store == StoreKind.Xbox).Select(g => g.Key.Id).Chunk(20))
    {
        using var response = await publicMetadata.GetAsync("https://displaycatalog.mp.microsoft.com/v7.0/products?bigIds=" + string.Join(",", ids) + "&market=US&languages=en-us"); response.EnsureSuccessStatusCode();
        using var catalog = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var catalogProduct in catalog.RootElement.GetProperty("Products").EnumerateArray())
        {
            var id = catalogProduct.GetProperty("ProductId").GetString()!; if (!ids.Contains(id)) throw new Exception("foreign catalog ID");
            var urls = catalogProduct.GetProperty("LocalizedProperties")[0].GetProperty("Images").EnumerateArray().Where(StoreMetadataArtwork.XboxCoverPurpose).OrderBy(StoreMetadataArtwork.XboxCoverPriority)
                .Select(i => i.GetProperty("Uri").GetString()!).Select(url => url.StartsWith("//") ? "https:" + url : url).SelectMany(url => StoreMetadataArtwork.CoverVariants(StoreKind.Xbox, url)).Distinct().Take(4).ToArray();
            xboxCovers[id] = urls;
        }
    }
    var visual = games.Select(game =>
    {
        if (game.Key.Store == StoreKind.Gog && byId.TryGetValue(game.Key.Id, out var metadata)) return game with { ArtworkUrl = metadata.ArtworkUrl, ArtworkFallbackUrls = metadata.ArtworkFallbackUrls, ArtworkKind = metadata.ArtworkUrl == null ? null : "cover" };
        if (game.Key.Store == StoreKind.Amazon && amazonProducts.TryGetValue(game.Key.Id, out var product)) { var image = StoreMetadataArtwork.Amazon(product); return game with { ArtworkUrl = image.Url, ArtworkFallbackUrls = image.Alternatives, ArtworkKind = image.Url == null ? null : "cover" }; }
        if (game.Key.Store == StoreKind.Xbox && xboxCovers.TryGetValue(game.Key.Id, out var xbox)) return game with { ArtworkUrl = xbox.FirstOrDefault(), ArtworkFallbackUrls = xbox.Skip(1).ToArray(), ArtworkKind = xbox.Length == 0 ? null : "cover" };
        if (game.Key.Store == StoreKind.Epic && game.ArtworkUrl is { } epic) { var urls = StoreMetadataArtwork.CoverVariants(StoreKind.Epic, epic).ToArray(); return game with { ArtworkUrl = urls[0], ArtworkFallbackUrls = urls.Skip(1).ToArray(), ArtworkKind = "cover" }; }
        return game with { ArtworkKind = game.ArtworkUrl == null ? null : "cover" };
    }).OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase).ToArray();
    File.WriteAllText(args[3], JsonSerializer.Serialize(new { games = visual, metadataCache = Path.Combine(root, "gog", "artwork-metadata-v1") }, StoreLibraryService.JsonOptions));
    Console.WriteLine(JsonSerializer.Serialize(new { root, gogOwned = owned.Length, coldMs, warmMs, coldPortraits = cold.Count(g => g.ArtworkFallbackUrls?.Count > 0), warmPortraits = warm.Count(g => g.ArtworkFallbackUrls?.Count > 0),
        allHaveCover = visual.All(g => g.ArtworkKind == "cover"), coverRows = visual.Count(g => g.ArtworkKind == "cover"), output = args[3], noAccountOrInstalledMutation = true })); return;
}
var checks = 0; void Check(bool condition, string title) { checks++; if (!condition) throw new Exception(title); }
JsonElement Data(string json) => JsonSerializer.Deserialize<JsonElement>(json);
var product = Data("""{"productDetail":{"iconUrl":"https://m.media-amazon.com/images/I/RealPoster.jpg","details":{"logoUrl":"https://m.media-amazon.com/images/I/Wordmark.png","pgCrownImageUrl":"https://m.media-amazon.com/images/I/Promo.jpg"}}}""");
var cover = StoreMetadataArtwork.Amazon(product);
Check(cover.Url == "https://m.media-amazon.com/images/I/RealPoster._SX480_.jpg", "Amazon actual illustrated poster preferred and resized by official image CDN");
Check(cover.Alternatives.SequenceEqual(new[] { "https://m.media-amazon.com/images/I/RealPoster.jpg", "https://m.media-amazon.com/images/I/Promo.jpg" }), "same-product original cover and promotional art remain bounded alternatives");
Check(StoreMetadataArtwork.Amazon(Data("""{"productDetail":{"details":{"logoUrl":"https://m.media-amazon.com/images/I/Wordmark.png"}}}""")).Url == null, "logo-only product never claims cover readiness");
Check(StoreMetadataArtwork.Amazon(Data("""{"productDetail":{"details":{"backgroundUrl1":"https://m.media-amazon.com/images/I/Screenshot.jpg"}}}""")).Url == null, "screenshots never substituted as product covers");
Check(StoreMetadataArtwork.Amazon(Data("""{"productDetail":{"iconUrl":"https://evil.example/fake.jpg"}}""")).Url == null, "untrusted image hosts refused");
Check(StoreMetadataArtwork.Amazon(Data("""{"productDetail":null}""")).Url == null, "missing optional artwork preserves library row");
var hash = new string('a', 64); var official = "https://images-3.gog-statics.com/" + hash;
Check(StoreMetadataArtwork.GogAccountCover(Data(JsonSerializer.Serialize(new { image = official }))) == official + "_product_card_v2_mobile_slider_639.jpg", "bare GOG account identifier becomes real CDN cover derivative, never 404 hash URL");
var url = "https://images.gog-statics.com/portrait.jpg";
var details = Data(JsonSerializer.Serialize(new { _embedded = new { product = new { id = 123 } }, _links = new { boxArtImage = new { href = url }, logo = new { href = "https://images.gog-statics.com/logo.png" } } }));
Check(StoreMetadataArtwork.GogProductCover(details, "123") == url, "exact product-ID official vertical cover selected instead of logo");
Check(StoreMetadataArtwork.GogProductCover(details, "456") == null, "same-name/different-product response cannot be adopted");
Check(StoreMetadataArtwork.GogProductCover(Data("""{"_embedded":{"product":{"id":123}},"_links":{"logo":{"href":"https://images.gog-statics.com/logo.png"}}}"""), "123") == null, "GOG logo cannot become a cover when poster is absent");
Check(StoreMetadataArtwork.XboxCoverPurpose(Data("""{"ImagePurpose":"Hero","Width":558,"Height":756}""")), "legacy Xbox tall Hero is a verified marketing poster");
Check(!StoreMetadataArtwork.XboxCoverPurpose(Data("""{"ImagePurpose":"Logo","Width":600,"Height":800}""")), "Xbox logo never becomes a cover even when dimensions look tall");
Check(!StoreMetadataArtwork.XboxCoverPurpose(Data("""{"ImagePurpose":"Screenshot","Width":600,"Height":800}""")), "portrait screenshots never become a cover");
Check(!StoreMetadataArtwork.XboxCoverPurpose(Data("""{"ImagePurpose":"Hero","Width":1920,"Height":1080}""")), "landscape hero is not guessed to be a poster");
var handler = new Metadata(); var providerFixture = new StoreCliProvider(StoreKind.Gog, root, root, Path.Combine(root, "no-tool.exe"), new HttpClient(handler));
var gamesFixture = Enumerable.Range(100, 16).Select(i => new ProviderGame(i.ToString(), "Owned " + i, true, null, ArtworkUrl: official + "_product_card_v2_mobile_slider_639.jpg")).ToArray();
var enriched = await providerFixture.EnrichGogArtworkBatch(gamesFixture, default);
Check(handler.Calls == 16 && handler.Peak <= 4, "real metadata batch bounds network concurrency to four");
Check(enriched.All(g => g.ArtworkFallbackUrls?.Single() == official + "_product_card_v2_mobile_slider_639.jpg"), "official alternate cover retained for every exact-ID portrait");
_ = await providerFixture.EnrichGogArtworkBatch(gamesFixture, default); Check(handler.Calls == 16, "persistent cover metadata cache avoids repeated API requests");
using (var stop = new CancellationTokenSource()) { stop.Cancel(); try { await providerFixture.EnrichGogArtworkBatch(gamesFixture, stop.Token); } catch (OperationCanceledException) { checks++; } }
Console.WriteLine($"PASS {checks} store cover metadata/cache checks; no account or game access");

sealed class Metadata : HttpMessageHandler
{
    internal int Calls, Peak, Active;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Interlocked.Increment(ref Calls); var active = Interlocked.Increment(ref Active); Peak = Math.Max(Peak, active);
        try
        {
            if (request.RequestUri!.Host != "api.gog.com" || request.Headers.Authorization != null) throw new Exception("public exact-ID metadata must never carry account credentials");
            await Task.Delay(10, token); var id = request.RequestUri.Segments[^1];
            return new(HttpStatusCode.OK) { RequestMessage = request, Content = new StringContent(JsonSerializer.Serialize(new { _embedded = new { product = new { id } }, _links = new { boxArtImage = new { href = "https://images.gog-statics.com/portrait-" + id + ".jpg" } } })) };
        }
        finally { Interlocked.Decrement(ref Active); }
    }
}

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Playhub.Integrations;

internal static class ArtworkProviderChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        var handler = new FixtureHandler();
        using var providers = new ApplicationArtworkProviders(handler);
        foreach (var request in new[] { ("igdb","grid_p",false), ("iidb","logo",false), ("xbox","grid_l",false), ("nintendo","grid_p",true), ("playstation","grid_l",false), ("ign","grid_p",false), ("alphacoders","hero",false) })
        {
            var result = await providers.SearchAsync(request.Item1, "Cuphead", request.Item2, request.Item3);
            check(result.Count == 1 && result[0]?["provider"]?.ToString() == request.Item1, "direct " + request.Item1 + " adapter parses its public service contract");
        }
        bool unregistered = false;
        try { await providers.DownloadAsync("iidb", "https://assets.iisu.network/games/999/hero/not-selected.png"); } catch (ArgumentException) { unregistered = true; }
        check(unregistered, "download refuses an unregistered URL even on a trusted host");
        var selectedBytes = await providers.DownloadAsync("xbox", "https://store-images.s-microsoft.com/test.jpg");
        check(selectedBytes.Length > 0, "download accepts only the provider's registered candidate");
        check(handler.Calls.All(url => !url.Contains("8080") && !url.Contains("homebrew")), "provider acquisition makes no Steam CDP or Decky RPC request");
        check(!ApplicationArtworkProviders.AllowedImage("iidb", "https://assets.iisu.network.evil.invalid/a.png") && !ApplicationArtworkProviders.AllowedImage("iidb", "https://x:password@assets.iisu.network/a.png"), "artwork host boundary rejects suffix spoofing and credentials");
        handler.IgnImageHost = "assets-prd.ignimgs.com";
        var currentIgn = await providers.SearchAsync("ign", "Cuphead", "grid_p");
        check(currentIgn.Count == 1 && currentIgn[0]?["found_title"]?.ToString() == "Cuphead", "current IGN production image host retains verified title association");
        check((await providers.DownloadAsync("ign", "https://assets-prd.ignimgs.com/test.png")).Length > 0, "registered current IGN image can be acquired");
        check(!ApplicationArtworkProviders.AllowedImage("ign", "https://assets-prd.ignimgs.com.evil.invalid/test.png") &&
            !ApplicationArtworkProviders.AllowedImage("ign", "https://assets-prd.ignimgs.com@evil.invalid/test.png") &&
            !ApplicationArtworkProviders.AllowedImage("ign", "https://user:password@assets-prd.ignimgs.com/test.png") &&
            !ApplicationArtworkProviders.AllowedImage("ign", "http://assets-prd.ignimgs.com/test.png") &&
            !ApplicationArtworkProviders.AllowedImage("ign", "https://assets-prd.ignimgs.com:8443/test.png"), "current IGN host addition preserves exact HTTPS host credential and port boundaries");
        handler.Mismatch = true;
        check((await providers.SearchAsync("igdb", "Cuphead", "grid_p")).Count == 0, "numbered sequel cannot silently match requested game");
        foreach (var pair in new[] { ("ポケモン", " unrelated 日本語"), ("Сталкер", "Другое название"), ("Metal Gear Solid III", "Metal Gear Solid IV") })
        {
            handler.NameOverride = pair.Item2; int before = handler.Calls.Count;
            check((await providers.SearchAsync("igdb", pair.Item1, "grid_p")).Count == 0 && handler.Calls.Count == before+1, "unrelated Unicode or Roman sequel performs no detail/image request: " + pair.Item1);
        }
        handler.NameOverride = "Pokemon";
        check((await providers.SearchAsync("igdb", "Pokémon", "grid_p")).Count == 1, "accented title retains the same complete word when matching");
        bool unsupported = false;
        try { await providers.SearchAsync("ign", "Cuphead", "logo"); } catch (NotSupportedException) { unsupported = true; }
        check(unsupported, "unsupported provider category is explicit instead of a false empty completion");
        using var huge = new ApplicationArtworkProviders(new ReplyHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[8*1024*1024+1]) })));
        bool bounded = false;
        try { await huge.SearchAsync("igdb", "Cuphead", "grid_p"); } catch (IOException) { bounded = true; }
        check(bounded, "oversized provider response fails before JSON parsing");
        using var waiting = new ApplicationArtworkProviders(new ReplyHandler(async (_, ct) => { await Task.Delay(Timeout.Infinite,ct); return new(HttpStatusCode.OK); }));
        using var cancel = new CancellationTokenSource(10);
        bool cancelled = false;
        try { await waiting.SearchAsync("igdb", "Cuphead", "grid_p", ct:cancel.Token); } catch (OperationCanceledException) { cancelled = true; }
        check(cancelled, "provider cancellation reaches the actual HTTP operation");
    }
    private sealed class ReplyHandler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request,ct); }
    private sealed class FixtureHandler : HttpMessageHandler
    {
        public List<string> Calls = []; public bool Mismatch; public string? NameOverride; public string IgnImageHost = "assets1.ignimgs.com";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); var uri = request.RequestUri!; Calls.Add(uri.ToString());
            if (uri.Host is "web.np.playstation.com" or "mollusk.apis.ign.com" && request.Content?.Headers.ContentType?.MediaType != "application/json") throw new Exception("Actual GraphQL CSRF contract requires application/json.");
            if (uri.Host == "iidb-api.iisu.network" && !uri.Query.Contains("assets_per_parent=8")) throw new Exception("Actual iiDB endpoint rejects an oversized group request.");
            string name = NameOverride ?? (Mismatch ? "Cuphead 2" : "Cuphead");
            string text = uri.Host switch
            {
                "api2.playnite.link" when uri.AbsolutePath.EndsWith("search") => "{\"data\":[{\"id\":1,\"name\":\""+name+"\"}]}",
                "api2.playnite.link" => "{\"data\":{\"cover_expanded\":{\"image_id\":\"co_test\",\"width\":600,\"height\":900}}}",
                "iidb-api.iisu.network" => "{\"groups\":[{\"parent\":{\"name\":\"Cuphead\"},\"assets\":[{\"filename\":\"games/1/logo/test.png\",\"resolution_width\":600,\"resolution_height\":300}]}]}",
                "www.microsoft.com" => "{\"ResultSets\":[{\"Type\":\"Product\",\"Suggests\":[{\"Source\":\"game\",\"Title\":\"Cuphead\",\"Metas\":[{\"Key\":\"bigcatalogid\",\"Value\":\"9ABCDEFGHIJK\"}]}]}]}",
                "displaycatalog.mp.microsoft.com" => "{\"Products\":[{\"LocalizedProperties\":[{\"Images\":[{\"ImagePurpose\":\"superheroart\",\"Uri\":\"//store-images.s-microsoft.com/test.jpg\",\"Width\":1920,\"Height\":1080}]}]}]}",
                "u3b6gr4ua3-2.algolia.net" => "{\"results\":[{\"hits\":[{\"title\":\"Cuphead\",\"productImageSquare\":\"https://assets.nintendo.com/image/upload/test.jpg\"}]}]}",
                "web.np.playstation.com" => "{\"data\":{\"universalSearch\":{\"results\":[{\"name\":\"Cuphead\",\"media\":[{\"role\":\"SCREENSHOT\",\"url\":\"https://image.api.playstation.com/test.png\"}]}]}}}",
                "mollusk.apis.ign.com" => "{\"data\":{\"searchObjectsByName\":{\"objects\":[{\"metadata\":{\"names\":{\"name\":\"Cuphead\"}},\"primaryImage\":{\"url\":\"https://"+IgnImageHost+"/test.png\"}}]}}}",
                "alphacoders.com" => "<meta itemprop='contentUrl' content='https://images.alphacoders.com/test.png'>",
                _ => ""
            };
            byte[] png = [137,80,78,71,13,10,26,10,0,0,0,13,73,72,68,82,0,0,7,128,0,0,4,56];
            if (uri.Host.Contains("ignimgs")) { System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16),600); System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20),900); }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = text.Length > 0 ? new StringContent(text,Encoding.UTF8,"application/json") : new ByteArrayContent(png) });
        }
    }
}

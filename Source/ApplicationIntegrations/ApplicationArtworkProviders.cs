using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Playhub.Integrations;

/// <summary>Original HTTP adapters for public artwork services; no Decky backend dependency.</summary>
public sealed class ApplicationArtworkProviders : IDisposable
{
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _requests = new(3, 3);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, (string Provider, DateTime Expires)> _selected = new();
    private readonly object _gate = new();
    private static readonly Dictionary<string, string[]> Hosts = new()
    {
        ["playstation"] = ["image.api.playstation.com"], ["nintendo"] = ["assets.nintendo.com", "assets.nintendo.eu"],
        ["xbox"] = ["store-images.s-microsoft.com", "store-images.microsoft.com"], ["igdb"] = ["images.igdb.com"],
        ["iidb"] = ["assets.iisu.network"], ["ign"] = ["assets1.ignimgs.com", "assets2.ignimgs.com", "assets-prd.ignimgs.com", "assets.ign.com"],
        ["alphacoders"] = ["images.alphacoders.com", "images2.alphacoders.com", "images3.alphacoders.com", "images4.alphacoders.com"]
    };
    public ApplicationArtworkProviders() : this(new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli }) { }
    public ApplicationArtworkProviders(HttpMessageHandler handler)
    {
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Playhub/1.0");
    }
    public async Task<JsonArray> SearchAsync(string provider, string title, string type, bool square = false, int limit = 24, CancellationToken ct = default)
    {
        if (!Hosts.ContainsKey(provider) || string.IsNullOrWhiteSpace(title) || title.Length > 512 || Normalize(title).Length == 0 || type is not ("grid_p" or "grid_l" or "hero" or "logo" or "icon"))
            throw new ArgumentException("Select a known provider, game title and artwork type.");
        if ((provider is "alphacoders" && type is not ("grid_l" or "hero")) || (provider == "ign" && type != "grid_p") ||
            (provider is "playstation" or "nintendo" or "igdb" && type == "logo") || (provider == "iidb" && type == "grid_p"))
            throw new NotSupportedException("This provider does not publish this artwork type.");
        limit = Math.Clamp(limit, 1, 48);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token); deadline.CancelAfter(TimeSpan.FromSeconds(25));
        await _requests.WaitAsync(deadline.Token);
        try
        {
            JsonArray candidates = provider switch
            {
                "igdb" => await IgdbAsync(title, type, deadline.Token), "iidb" => await IidbAsync(title, type, deadline.Token),
                "xbox" => await XboxAsync(title, type, deadline.Token), "nintendo" => await NintendoAsync(title, type, deadline.Token),
                "playstation" => await PlaystationAsync(title, type, deadline.Token), "ign" => await IgnAsync(title, deadline.Token),
                _ => await AlphaAsync(title, deadline.Token)
            };
            var output = new JsonArray(); var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in candidates.OfType<JsonObject>())
            {
                string url = item["url"]?.ToString() ?? "";
                if (!AllowedImage(provider, url) || !seen.Add(url)) continue;
                int width = Number(item, "width"), height = Number(item, "height");
                if (width <= 0 || height <= 0)
                {
                    (width, height) = Dimensions(await GetBytesAsync(url, 65536, deadline.Token, true));
                    item["width"] = width; item["height"] = height;
                }
                if (width <= 0 || height <= 0) continue;
                double ratio = (double)width / height;
                if (type == "grid_p" && (square ? ratio is < .9 or > 1.1 : ratio >= .95)) continue;
                if (type is "grid_l" or "hero" && ratio < 1.3) continue;
                item["provider"] = provider; item["source"] = provider;
                lock (_gate)
                {
                    if (_selected.Count >= 512) foreach (var key in _selected.OrderBy(pair => pair.Value.Expires).Take(128).Select(pair => pair.Key).ToArray()) _selected.Remove(key);
                    _selected[url] = (provider, DateTime.UtcNow.AddMinutes(15));
                }
                output.Add(item.DeepClone());
                if (output.Count == limit) break;
            }
            return output;
        }
        finally { _requests.Release(); }
    }

    public async Task<byte[]> DownloadAsync(string provider, string url, CancellationToken ct = default)
    {
        lock (_gate)
            if (!AllowedImage(provider, url) || !_selected.TryGetValue(url, out var selected) || selected.Provider != provider || selected.Expires < DateTime.UtcNow)
                throw new ArgumentException("Select an artwork result before downloading.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token); deadline.CancelAfter(TimeSpan.FromSeconds(25));
        await _requests.WaitAsync(deadline.Token);
        try
        {
            var bytes = await GetBytesAsync(url, 25 * 1024 * 1024, deadline.Token);
            var dimensions = Dimensions(bytes);
            if (dimensions.Width <= 0 || dimensions.Height <= 0) throw new InvalidDataException("The artwork is not a supported image.");
            return bytes;
        }
        finally { _requests.Release(); }
    }

    public static bool AllowedImage(string provider, string url) => Hosts.TryGetValue(provider, out var hosts) &&
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && hosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
    private static string Escape(string value) => Uri.EscapeDataString(value);
    private static int Number(JsonObject item, string key) => int.TryParse(item[key]?.ToString(), out int n) ? n : 0;
    private static JsonObject Asset(string url, int width = 0, int height = 0, string? thumbnail = null, string kind = "artwork") => new()
        { ["url"] = url.StartsWith("//", StringComparison.Ordinal) ? "https:" + url : url, ["thumb"] = thumbnail ?? url, ["width"] = width, ["height"] = height, ["content_kind"] = kind };
    private static IEnumerable<JsonObject> Objects(JsonNode? node) => (node as JsonArray ?? new()).OfType<JsonObject>();
    private static string Normalize(string title)
    {
        string text = string.Concat(title.ToLowerInvariant().Normalize(NormalizationForm.FormD)
            .Where(character => char.GetUnicodeCategory(character) is not (System.Globalization.UnicodeCategory.NonSpacingMark or System.Globalization.UnicodeCategory.SpacingCombiningMark or System.Globalization.UnicodeCategory.EnclosingMark)));
        return string.Join(' ', Regex.Matches(text, @"[\p{L}\p{Nd}]+", RegexOptions.CultureInvariant).Select(m => m.Value));
    }
    private static double Match(string title, string found)
    {
        string a = Normalize(title), b = Normalize(found); if (a.Length == 0 || b.Length == 0) return 0; if (a == b) return 1;
        var first = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(); var second = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var roman = new Dictionary<string, string> { ["i"]="1", ["ii"]="2", ["iii"]="3", ["iv"]="4", ["v"]="5", ["vi"]="6", ["vii"]="7", ["viii"]="8", ["ix"]="9", ["x"]="10", ["xi"]="11", ["xii"]="12", ["xiii"]="13", ["xiv"]="14", ["xv"]="15", ["xvi"]="16", ["xvii"]="17", ["xviii"]="18", ["xix"]="19", ["xx"]="20" };
        IEnumerable<string> Sequences(HashSet<string> words) => words.Where(word => int.TryParse(word,out _) || roman.ContainsKey(word)).Select(word => roman.GetValueOrDefault(word,word));
        if (!Sequences(first).ToHashSet().SetEquals(Sequences(second))) return 0;
        return first.Count + second.Count == 0 ? 0 : 2d * first.Intersect(second).Count() / (first.Count + second.Count);
    }
    private static JsonObject? Best(string title, IEnumerable<JsonObject> items, Func<JsonObject, string> name) => items.Select(item => (Item: item, Score: Match(title, name(item))))
        .Where(pair => pair.Score >= .75).OrderByDescending(pair => pair.Score).Select(pair => pair.Item).FirstOrDefault();
    private static JsonArray WithFoundTitle(JsonArray assets, string title)
    { foreach (var asset in assets.OfType<JsonObject>()) asset["found_title"] = title; return assets; }

    private async Task<JsonArray> IgdbAsync(string title, string type, CancellationToken ct)
    {
        var found = await JsonAsync("https://api2.playnite.link/api/igdb/search", new JsonObject { ["searchTerm"] = title }, ct);
        var game = Best(title, Objects(found?["data"]), item => item["name"]?.ToString() ?? "");
        if (game is null || !long.TryParse(game["id"]?.ToString(), out long id) || id <= 0) return new();
        var reply = await JsonAsync("https://api2.playnite.link/api/igdb/game/" + id, null, ct);
        var detail = reply?["data"] as JsonObject; var result = new JsonArray();
        if (detail is null) return result;
        IEnumerable<JsonObject> images = type is "grid_p" or "icon" ? new[] { detail["cover_expanded"] }.OfType<JsonObject>()
            : Objects(detail["artworks_expanded"]).Concat(Objects(detail["screenshots_expanded"]));
        foreach (var image in images)
        {
            string token = image["image_id"]?.ToString() ?? "";
            if (Regex.IsMatch(token, "^[A-Za-z0-9_]+$")) result.Add(Asset("https://images.igdb.com/igdb/image/upload/t_original/" + token + ".jpg", Number(image, "width"), Number(image, "height")));
        }
        return WithFoundTitle(result, game["name"]?.ToString() ?? "");
    }
    private async Task<JsonArray> IidbAsync(string title, string type, CancellationToken ct)
    {
        string collection = type switch { "grid_l" => "banner", _ => type };
        var reply = await JsonAsync("https://iidb-api.iisu.network/api/v1/assets/search/groups?q=" + Escape(title) + "&asset_type=" + collection + "&parent_limit=4&assets_per_parent=8", null, ct,
            new() { ["Referer"] = "https://iidb.iisu.network/" });
        var group = Best(title, Objects(reply?["groups"]), item => item["parent"]?["name"]?.ToString() ?? "");
        var result = new JsonArray(); if (group is null) return result;
        foreach (var image in Objects(group["assets"]))
        {
            string url = image["raw_url"]?.ToString() ?? "";
            if (url.Length == 0) url = "https://assets.iisu.network/" + image["filename"]?.ToString().TrimStart('/');
            result.Add(Asset(url, Number(image, "resolution_width"), Number(image, "resolution_height"), image["library_preview_url"]?.ToString() ?? image["preview_url"]?.ToString()));
        }
        return WithFoundTitle(result, group["parent"]?["name"]?.ToString() ?? "");
    }
    private async Task<JsonArray> XboxAsync(string title, string type, CancellationToken ct)
    {
        var search = await JsonAsync("https://www.microsoft.com/msstoreapiprod/api/autosuggest?market=en-us&sources=DCatAll-Products,xSearch-Products&filter=+ClientType:StoreWeb&counts=20,20&query=" + Escape(title), null, ct);
        var suggestions = Objects(search?["ResultSets"] ?? search?["resultSets"]).SelectMany(set => Objects(set["Suggests"] ?? set["suggests"]))
            .Where(item => string.Equals(item["Source"]?.ToString() ?? item["source"]?.ToString(), "game", StringComparison.OrdinalIgnoreCase));
        var game = Best(title, suggestions, item => item["Title"]?.ToString() ?? item["title"]?.ToString() ?? "");
        string? id = game is null ? null : Objects(game["Metas"] ?? game["metas"]).FirstOrDefault(meta => string.Equals(meta["Key"]?.ToString() ?? meta["key"]?.ToString(), "bigcatalogid", StringComparison.OrdinalIgnoreCase))?["Value"]?.ToString();
        if (id is null || !Regex.IsMatch(id, "^[A-Za-z0-9]{8,20}$")) return new();
        var reply = await JsonAsync("https://displaycatalog.mp.microsoft.com/v7.0/products?bigIds=" + id + "&market=US&languages=en-us&fieldsTemplate=Details", null, ct);
        string[] roles = type switch { "grid_p" => ["poster", "boxart", "boxartlg", "tile", "brandedkeyart", "featurepromotionalsquareart"], "logo" or "icon" => ["logo"], _ => ["superheroart", "hero", "titledheroart", "imagegallery", "screenshot"] };
        var result = new JsonArray();
        foreach (var product in Objects(reply?["Products"] ?? reply?["products"]))
            foreach (var props in Objects(product["LocalizedProperties"] ?? product["localizedProperties"]).Take(1))
                foreach (var image in Objects(props["Images"] ?? props["images"]))
                    if (roles.Contains((image["ImagePurpose"]?.ToString() ?? image["imagePurpose"]?.ToString() ?? "").ToLowerInvariant()))
                        result.Add(Asset(image["Uri"]?.ToString() ?? image["uri"]?.ToString() ?? "", Number(image, "Width"), Number(image, "Height")));
        return WithFoundTitle(result, game!["Title"]?.ToString() ?? game["title"]?.ToString() ?? "");
    }
    private async Task<JsonArray> NintendoAsync(string title, string type, CancellationToken ct)
    {
        var reply = await JsonAsync("https://U3B6GR4UA3-2.algolia.net/1/indexes/*/queries", new JsonObject { ["requests"] = new JsonArray(new JsonObject
            { ["indexName"] = "store_game_en_us", ["query"] = title, ["facetFilters"] = new JsonArray("corePlatforms:Nintendo Switch", "hasDlc:false"), ["hitsPerPage"] = 24 }) }, ct,
            new() { ["X-Algolia-Application-Id"] = "U3B6GR4UA3", ["X-Algolia-API-Key"] = "a29c6927638bfd8cee23993e51e721c9" });
        var game = Best(title, Objects(reply?["results"]?[0]?["hits"]), item => item["title"]?.ToString() ?? "");
        var result = new JsonArray(); if (game is null) return result;
        if (type is "grid_p" or "icon")
        {
            string cover = game["productImageSquare"]?.ToString() ?? "";
            if (cover.Length > 0) result.Add(Asset(cover.Replace("/f_auto/", "/f_jpg/"), 1024, 1024));
        }
        else
        {
            string Cloudinary(string token) => token.StartsWith("https://", StringComparison.Ordinal) ? token : "https://assets.nintendo.com/image/upload/f_jpg/" + token;
            if (game["productImage"]?.ToString() is string key && key.Length > 0) result.Add(Asset(Cloudinary(key)));
            foreach (var image in Objects(game["productGallery"]))
                if (image["resourceType"]?.ToString() == "image" && image["publicId"]?.ToString() is string token) result.Add(Asset(Cloudinary(token), kind: "screenshot"));
        }
        return WithFoundTitle(result, game["title"]?.ToString() ?? "");
    }
    private async Task<JsonArray> PlaystationAsync(string title, string type, CancellationToken ct)
    {
        var variables = new JsonObject { ["countryCode"] = "US", ["languageCode"] = "en", ["nextCursor"] = "", ["pageOffset"] = 0, ["pageSize"] = 24, ["searchTerm"] = title };
        var extension = new JsonObject { ["persistedQuery"] = new JsonObject { ["version"] = 1, ["sha256Hash"] = "4df6284f982e57bec70f23c77e2c219dc792eb19af7fb3d3a81767aa3f1958aa" } };
        var reply = await JsonAsync("https://web.np.playstation.com/api/graphql/v1//op?operationName=getSearchResults&variables=" + Escape(variables.ToJsonString()) + "&extensions=" + Escape(extension.ToJsonString()), null, ct,
            new() { ["Content-Type"] = "application/json", ["Origin"] = "https://store.playstation.com", ["Referer"] = "https://store.playstation.com/", ["apollographql-client-name"] = "@sie-ppr-web-store/app", ["apollographql-client-version"] = "0.113.0", ["X-PSN-Store-Locale-Override"] = "en-US" });
        var game = Best(title, Objects(reply?["data"]?["universalSearch"]?["results"]), item => item["name"]?.ToString() ?? item["title"]?.ToString() ?? "");
        var result = new JsonArray(); if (game is null) return result;
        foreach (var media in Objects(game["media"]))
        {
            string role = media["role"]?.ToString().ToUpperInvariant() ?? "";
            if (type is "grid_p" or "icon" ? role.Contains("SCREENSHOT") : !role.Contains("SCREENSHOT") && !role.Contains("BACKGROUND") && !role.Contains("HERO")) continue;
            result.Add(Asset(media["url"]?.ToString() ?? media["src"]?.ToString() ?? media["imageUrl"]?.ToString() ?? "", kind: role.Contains("SCREENSHOT") ? "screenshot" : "artwork"));
        }
        return WithFoundTitle(result, game["name"]?.ToString() ?? game["title"]?.ToString() ?? "");
    }
    private async Task<JsonArray> IgnAsync(string title, CancellationToken ct)
    {
        var variables = new JsonObject { ["term"] = title, ["count"] = 12, ["objectType"] = "Game" };
        var extension = new JsonObject { ["persistedQuery"] = new JsonObject { ["version"] = 1, ["sha256Hash"] = "e1c2e012a21b4a98aaa618ef1b43eb0cafe9136303274a34f5d9ea4f2446e884" } };
        var reply = await JsonAsync("https://mollusk.apis.ign.com/graphql?operationName=SearchObjectsByName&variables=" + Escape(variables.ToJsonString()) + "&extensions=" + Escape(extension.ToJsonString()), null, ct,
            new() { ["Origin"] = "https://www.ign.com", ["Referer"] = "https://www.ign.com/", ["Content-Type"] = "application/json", ["apollographql-client-name"] = "kraken", ["apollographql-client-version"] = "v0.67.0", ["x-apollo-operation-name"] = "SearchObjectsByName" });
        var game = Best(title, Objects(reply?["data"]?["searchObjectsByName"]?["objects"]), item => item["metadata"]?["names"]?["name"]?.ToString() ?? "");
        return game?["primaryImage"]?["url"]?.ToString() is string url ? WithFoundTitle(new JsonArray(Asset(url)), game["metadata"]?["names"]?["name"]?.ToString() ?? "") : new JsonArray();
    }
    private async Task<JsonArray> AlphaAsync(string title, CancellationToken ct)
    {
        string slug = Regex.Replace(Normalize(title), " +", "-");
        string page = Encoding.UTF8.GetString(await GetBytesAsync("https://alphacoders.com/" + slug + "-wallpapers", 4 * 1024 * 1024, ct));
        var result = new JsonArray();
        foreach (Match match in Regex.Matches(page, "itemprop=[\"']contentUrl[\"'][^>]*content=[\"'](?<url>[^\"']+)[\"']", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
            result.Add(Asset(WebUtility.HtmlDecode(match.Groups["url"].Value)));
        return result;
    }

    private async Task<JsonNode?> JsonAsync(string url, JsonObject? body, CancellationToken ct, Dictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, url);
        if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        foreach (var header in headers ?? new())
        {
            if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                request.Content ??= new ByteArrayContent([]);
                request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(header.Value);
            }
            else request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        using var reply = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        reply.EnsureSuccessStatusCode(); return JsonNode.Parse(await ReadAsync(reply, 8 * 1024 * 1024, false, ct));
    }
    private async Task<byte[]> GetBytesAsync(string url, int maximum, CancellationToken ct, bool prefix = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (prefix) request.Headers.Range = new RangeHeaderValue(0, maximum - 1);
        using var reply = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        reply.EnsureSuccessStatusCode(); return await ReadAsync(reply, maximum, prefix, ct);
    }
    private static async Task<byte[]> ReadAsync(HttpResponseMessage reply, int maximum, bool prefix, CancellationToken ct)
    {
        using var input = await reply.Content.ReadAsStreamAsync(ct); using var output = new MemoryStream(); byte[] block = new byte[8192];
        while (output.Length < maximum + (prefix ? 0 : 1))
        {
            int count = await input.ReadAsync(block.AsMemory(0, (int)Math.Min(block.Length, maximum + (prefix ? 0 : 1) - output.Length)), ct);
            if (count == 0) break; output.Write(block, 0, count);
        }
        if (output.Length > maximum) throw new IOException("Artwork service response exceeds the limit.");
        return output.ToArray();
    }
    internal static (int Width, int Height) Dimensions(byte[] data)
    {
        static int Big(byte[] b, int offset) => (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(offset, 4));
        if (data.Length >= 24 && data.AsSpan(0, 8).SequenceEqual(new byte[] { 137,80,78,71,13,10,26,10 })) return (Big(data, 16), Big(data, 20));
        if (data.Length >= 30 && Encoding.ASCII.GetString(data, 0, 4) == "RIFF" && Encoding.ASCII.GetString(data, 8, 8) == "WEBPVP8X")
            return (1 + data[24] + (data[25] << 8) + (data[26] << 16), 1 + data[27] + (data[28] << 8) + (data[29] << 16));
        if (data.Length > 4 && data[0] == 0xff && data[1] == 0xd8)
        {
            int i = 2;
            while (i + 9 < data.Length)
            {
                if (data[i++] != 0xff) break;
                while (i < data.Length && data[i] == 0xff) i++;
                if (i + 2 >= data.Length) break;
                int marker = data[i++]; int size = (data[i] << 8) | data[i+1];
                if (size < 2 || i + size > data.Length) break;
                if (marker is 0xc0 or 0xc1 or 0xc2) return ((data[i+5] << 8) | data[i+6], (data[i+3] << 8) | data[i+4]);
                i += size;
            }
        }
        return (0, 0);
    }
    public void Dispose() { _lifetime.Cancel(); _http.Dispose(); }
}

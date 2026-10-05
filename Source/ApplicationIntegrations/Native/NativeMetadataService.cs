using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Playhub.Emulation.Workbench;

/// <summary>IGN metadata draft acquisition, independent of Steam and app IDs.</summary>
public sealed class NativeMetadataService(HttpClient http)
{
    private readonly SemaphoreSlim concurrency = new(3, 3);
    private readonly ConcurrentDictionary<string, (DateTime Expires, JsonObject Value)> cache = new(StringComparer.OrdinalIgnoreCase);
    private const string SearchQuery = "query SearchObjectsByName($name: String!, $count: Int!, $type: ObjectType!) { searchObjectsByName(name: $name, count: $count, type: $type) { edges { node { id slug url metadata { names { name short } } objectRegions { releases { date } } } } } }";
    private const string DetailQuery = "query ObjectSelectByTypeAndSlug($objectType: ObjectType!, $slug: String!, $state: State) { objectSelectByTypeAndSlug(type: $objectType, slug: $slug, state: $state) { id slug url primaryImage { url } metadata { names { name short } descriptions { short long } } producers { name slug } publishers { name slug } genres { name slug } features { name slug } primaryReview { score } objectRegions { region releases { date platformAttributes { name slug } } } } }";
    public async Task<JsonObject> FetchAsync(string identity, string title, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(identity) || identity.Length > 1024 || string.IsNullOrWhiteSpace(title) || title.Length > 512) throw new ArgumentException("Invalid game identity or title.");
        var key = Normalize(title);
        if (cache.TryGetValue(key, out var cached) && cached.Expires > DateTime.UtcNow) return Draft(identity, cached.Value);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        await concurrency.WaitAsync(timeout.Token);
        try
        {
            if (cache.TryGetValue(key, out cached) && cached.Expires > DateTime.UtcNow) return Draft(identity, cached.Value);
            var search = await QueryAsync(SearchQuery, new JsonObject { ["name"] = title, ["count"] = 8, ["type"] = "Game" }, timeout.Token);
            var candidates = (search["data"]?["searchObjectsByName"]?["edges"] as JsonArray ?? new()).OfType<JsonObject>()
                .Select(edge => edge["node"] as JsonObject).OfType<JsonObject>().ToArray();
            var match = candidates.Select(node => (Node: node, Score: MatchScore(title, Title(node)))).OrderByDescending(item => item.Score).FirstOrDefault();
            JsonObject result;
            if (match.Node is null || match.Score < 0.85 || match.Node["slug"]?.GetValue<string>() is not string slug)
                result = new JsonObject { ["status"] = "not_found" };
            else
            {
                var detail = await QueryAsync(DetailQuery, new JsonObject { ["objectType"] = "Game", ["slug"] = slug, ["state"] = "Published" }, timeout.Token);
                if (detail["data"]?["objectSelectByTypeAndSlug"] is not JsonObject game || MatchScore(title, Title(game)) < 0.85)
                    result = new JsonObject { ["status"] = "not_found" };
                else result = new JsonObject { ["status"] = "completed", ["metadata"] = ParseGame(game), ["match"] = new JsonObject { ["provider"] = "ign", ["id"] = game["id"]?.DeepClone(), ["slug"] = slug, ["score"] = match.Score } };
            }
            if (cache.Count >= 128) foreach (var stale in cache.OrderBy(pair => pair.Value.Expires).Take(32)) cache.TryRemove(stale.Key, out _);
            cache[key] = (DateTime.UtcNow.AddMinutes(result["status"]!.GetValue<string>() == "completed" ? 30 : 2), (JsonObject)result.DeepClone());
            return Draft(identity, result);
        }
        finally { concurrency.Release(); }
    }
    public async Task<JsonArray> SearchAsync(string title, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 512) throw new ArgumentException("Invalid title.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        await concurrency.WaitAsync(timeout.Token);
        try
        {
            var result = await QueryAsync(SearchQuery, new JsonObject { ["name"] = title, ["count"] = 12, ["type"] = "Game" }, timeout.Token);
            return new JsonArray((result["data"]?["searchObjectsByName"]?["edges"] as JsonArray ?? new()).OfType<JsonObject>()
                .Select(edge => edge["node"] as JsonObject).OfType<JsonObject>()
                .Select(game => (JsonNode)new JsonObject { ["id"] = game["id"]?.DeepClone(), ["slug"] = game["slug"]?.DeepClone(), ["title"] = Title(game), ["year"] = ReleaseYear(game) }).ToArray());
        }
        finally { concurrency.Release(); }
    }
    public async Task<JsonObject> FetchSelectedAsync(string identity, string id, string slug, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(identity) || string.IsNullOrWhiteSpace(id) || id.Length > 256 || !Regex.IsMatch(slug ?? "", "^[a-zA-Z0-9-]{1,256}$")) throw new ArgumentException("Invalid IGN selection.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        await concurrency.WaitAsync(timeout.Token);
        try
        {
            var response = await QueryAsync(DetailQuery, new JsonObject { ["objectType"] = "Game", ["slug"] = slug, ["state"] = "Published" }, timeout.Token);
            if (response["data"]?["objectSelectByTypeAndSlug"] is not JsonObject game || game["id"]?.GetValue<string>() != id) throw new InvalidOperationException("IGN selection does not match the requested game.");
            return Draft(identity, new JsonObject { ["status"] = "completed", ["metadata"] = ParseGame(game), ["artworkUrl"] = game["primaryImage"]?["url"]?.DeepClone(), ["match"] = new JsonObject { ["provider"] = "ign", ["id"] = id, ["slug"] = slug, ["selected"] = true } });
        }
        finally { concurrency.Release(); }
    }
    private static JsonObject Draft(string identity, JsonObject result) { var draft = (JsonObject)result.DeepClone(); draft["identity"] = identity; return draft; }
    private async Task<JsonObject> QueryAsync(string query, JsonObject variables, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://mollusk.apis.ign.com/graphql") { Content = new StringContent(new JsonObject { ["query"] = query, ["variables"] = variables }.ToJsonString(), Encoding.UTF8, "application/json") };
        request.Headers.UserAgent.ParseAdd("Playhub/2.1");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 2 * 1024 * 1024) throw new IOException("Metadata response too large.");
        await using var input = await response.Content.ReadAsStreamAsync(ct); using var output = new MemoryStream();
        var buffer = new byte[8192]; int length;
        while ((length = await input.ReadAsync(buffer, ct)) > 0) { if (output.Length + length > 2 * 1024 * 1024) throw new IOException("Metadata response too large."); output.Write(buffer, 0, length); }
        var json = JsonNode.Parse(output.ToArray()) as JsonObject ?? throw new IOException("Invalid metadata response.");
        if (json["errors"] is JsonArray { Count: > 0 }) throw new IOException("IGN metadata query failed.");
        return json;
    }
    internal static string Normalize(string title) => Regex.Replace(title.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ", RegexOptions.CultureInvariant).Trim();
    internal static double MatchScore(string requested, string candidate)
    {
        var a = Normalize(requested); var b = Normalize(candidate); if (a == b && a.Length > 0) return 1;
        if (!Regex.Matches(a, @"\d+").Select(x => x.Value).SequenceEqual(Regex.Matches(b, @"\d+").Select(x => x.Value))) return 0;
        var left = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(); var right = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        return left.Count == 0 || right.Count == 0 ? 0 : (double)left.Intersect(right).Count() / left.Union(right).Count();
    }
    private static string Title(JsonObject game) => game["metadata"]?["names"]?["name"]?.GetValue<string>() ?? game["metadata"]?["names"]?["short"]?.GetValue<string>() ?? "";
    private static int? ReleaseYear(JsonObject game)
    {
        var years=(game["objectRegions"] as JsonArray ?? new()).OfType<JsonObject>().SelectMany(region=>(region["releases"] as JsonArray ?? new()).OfType<JsonObject>())
            .Select(release=>DateTimeOffset.TryParse(release["date"]?.ToString(),System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out var date)?(int?)date.Year:null)
            .Where(year=>year.HasValue).OrderBy(year=>year).ToArray();
        return years.FirstOrDefault();
    }
    private static string Clean(string? text) => WebUtility.HtmlDecode(Regex.Replace(text ?? "", "<[^>]+>", " ")).Trim();
    internal static JsonObject ParseGame(JsonObject game)
    {
        JsonArray Names(string key) => new((game[key] as JsonArray ?? new()).OfType<JsonObject>().Select(item => (JsonNode?)JsonValue.Create(item["name"]?.GetValue<string>())).Where(x => x is not null).ToArray());
        JsonArray People(string key) => new((game[key] as JsonArray ?? new()).OfType<JsonObject>().Where(item => !string.IsNullOrWhiteSpace(item["name"]?.GetValue<string>())).Select(item => (JsonNode?)new JsonObject { ["name"] = item["name"]?.DeepClone(), ["url"] = "" }).ToArray());
        var dates = (game["objectRegions"] as JsonArray ?? new()).OfType<JsonObject>().SelectMany(region => (region["releases"] as JsonArray ?? new()).OfType<JsonObject>()).Select(item => item["date"]?.GetValue<string>()).Where(value => DateTime.TryParse(value, out _)).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var description = Clean(game["metadata"]?["descriptions"]?["long"]?.GetValue<string>() ?? game["metadata"]?["descriptions"]?["short"]?.GetValue<string>());
        var slug = game["slug"]?.GetValue<string>() ?? "";
        var url = game["url"]?.GetValue<string>();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Host is not ("www.ign.com" or "ign.com")) url = "https://www.ign.com/games/" + Uri.EscapeDataString(slug);
        return new JsonObject { ["id"] = game["id"]?.DeepClone(), ["title"] = Title(game), ["source"] = "IGN", ["source_url"] = url, ["language"] = "en", ["description"] = description, ["short_description"] = Clean(game["metadata"]?["descriptions"]?["short"]?.GetValue<string>()), ["developers"] = People("producers"), ["publishers"] = People("publishers"), ["genres"] = Names("genres"), ["features"] = Names("features"), ["release_date"] = dates.FirstOrDefault(), ["rating"] = game["primaryReview"]?["score"] is JsonValue score && score.TryGetValue<double>(out var number) ? Math.Clamp(number * 10, 0, 100) : null };
    }
}
public sealed record NativeMetadataRequest(string Identity, string Title);

public sealed record NativeMetadataSelection(string Identity, string Id, string Slug);

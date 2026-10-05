using System.Text.Json.Nodes;

namespace Playhub.Importing;

public sealed record ImportArtworkResult(string Url, string Preview, int Width, int Height,
    string? Provider = null, string? AuthorName = null, string? AuthorSteamId = null);

/// <summary>Calls the installed Artworks provider contract; contains no provider scraping implementation.</summary>
internal static class ImportArtworkSearch
{
    internal static string AssetType(string category) => category switch
    {
        "cover" => "grid_p", "banner" => "grid_l", "hero" => "hero", "logo" => "logo", "icon" => "icon",
        _ => throw new ArgumentException("Unknown artwork category.", nameof(category))
    };
    public static async Task<IReadOnlyList<ImportArtworkResult>> SearchAsync(
        Func<string, string, JsonArray, CancellationToken, Task<JsonNode?>> call,
        string provider, string title, string category, bool squareCover, CancellationToken ct)
    {
        if (!new[] { "playstation", "nintendo", "xbox", "igdb", "alphacoders", "iidb", "ign" }.Contains(provider)) throw new ArgumentException("Unknown provider.");
        ct.ThrowIfCancellationRequested();
        var response = await call("Playhub Artworks", "search_provider_assets",
            new JsonArray(provider, title, AssetType(category), category == "cover" && squareCover, 24, "standard", new JsonArray(), "all", "", ""), ct);
        ct.ThrowIfCancellationRequested();
        var results = new List<ImportArtworkResult>();
        foreach (var item in (response as JsonArray ?? new()).OfType<JsonObject>())
        {
            var url = item["url"]?.GetValue<string>();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https") continue;
            var thumb = item["thumb"]?.GetValue<string>();
            if (!Uri.TryCreate(thumb, UriKind.Absolute, out var preview) || preview.Scheme != "https") thumb = url;
            results.Add(new(url!, thumb!, item["width"]?.GetValue<int>() ?? 0, item["height"]?.GetValue<int>() ?? 0));
        }
        return results;
    }
}

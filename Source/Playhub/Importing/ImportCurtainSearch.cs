using System.Text.Json.Nodes;

namespace Playhub.Importing;

internal static class ImportCurtainSearch
{
    internal static Task<JsonNode?> SearchAsync(Func<string,string,JsonArray,CancellationToken,Task<JsonNode?>> call, uint appId, string title, string provider, CancellationToken ct, bool sourceScoped = false)
    {
        if (appId == 0 && !sourceScoped || string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Imported game or exact app source and title required.");
        if (!new[] { "playstation", "igdb", "alphacoders", "nintendo", "xbox", "iidb", "steamgriddb" }.Contains(provider)) throw new ArgumentException("Unknown background provider.");
        return call("Launch Curtain", "search_google_images", new JsonArray(new JsonObject { ["app_id"] = appId, ["title"] = title, ["query"] = title, ["resolution"] = "3840x2160", ["services"] = new JsonArray(provider) }), ct);
    }
    internal static async Task<JsonObject> ChooseAsync(Func<string,string,JsonArray,CancellationToken,Task<JsonNode?>> call, uint appId, string title, JsonObject result, CancellationToken ct, bool sourceScoped = false)
    {
        if (appId == 0 && !sourceScoped || !Uri.TryCreate(result["image_url"]?.GetValue<string>(), UriKind.Absolute, out var url) || url.Scheme != "https") throw new ArgumentException("Select an actual background.");
        var response = await call("Launch Curtain", "download_google_image", new JsonArray(new JsonObject { ["app_id"] = appId, ["title"] = title, ["resolution"] = result["resolution"]?.DeepClone() ?? JsonValue.Create("3840x2160"), ["image_url"] = url.AbsoluteUri, ["source"] = result["source"]?.DeepClone() }), ct) as JsonObject;
        if (response?["ok"]?.GetValue<bool>() != true || string.IsNullOrWhiteSpace(response["path"]?.GetValue<string>())) throw new IOException("Background was not saved.");
        return response;
    }
}

using System.Globalization;
using System.Text.Json.Nodes;

namespace Playhub.Integrations;

public sealed record GameTitleIdentity(string Provider, string Id, string Slug, string Title, int? Year = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Title) || Title.Length > 512 || string.IsNullOrWhiteSpace(Id) || Id.Length > 256)
            throw new ArgumentException("Choose a game title result.");
        if (Provider == "steamgriddb")
        {
            if (!int.TryParse(Id, NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id <= 0 || !string.IsNullOrEmpty(Slug))
                throw new ArgumentException("Invalid SteamGridDB game identity.");
        }
        else if (Provider != "ign" || !System.Text.RegularExpressions.Regex.IsMatch(Slug ?? "", "^[a-zA-Z0-9-]{1,256}$"))
            throw new ArgumentException("Invalid game provider identity.");
    }
}

/// <summary>A title choice retains the provider identity; artwork is never used as its identity.</summary>
public sealed class GameTitleRefetchService(
    Func<string, string, CancellationToken, Task<IReadOnlyList<GameTitleIdentity>>> search,
    Func<string, GameTitleIdentity, CancellationToken, Task<JsonObject>> metadata,
    Func<string, GameTitleIdentity, JsonObject, string, string, CancellationToken, Task<string?>> artwork)
{
    private readonly Dictionary<GameTitleIdentity, DateTime> choices = new();
    private readonly object gate = new();
    public async Task<IReadOnlyList<GameTitleIdentity>> SearchAsync(string provider, string title, CancellationToken ct)
    {
        if (provider is not ("ign" or "steamgriddb") || string.IsNullOrWhiteSpace(title) || title.Length > 512)
            throw new ArgumentException("Choose a provider and game title.");
        var found = await search(provider, title.Trim(), ct);
        ct.ThrowIfCancellationRequested();
        var result = found.Where(value => value.Provider == provider).DistinctBy(value => (value.Id,value.Slug)).Take(40).ToArray();
        foreach (var value in result) value.Validate();
        lock (gate)
        {
            foreach (var stale in choices.Where(pair => pair.Value < DateTime.UtcNow).Select(pair => pair.Key).ToArray()) choices.Remove(stale);
            if (choices.Count + result.Length > 512) choices.Clear();
            foreach (var value in result) choices[value] = DateTime.UtcNow.AddMinutes(15);
        }
        return result;
    }
    public async Task<JsonObject> RefetchAsync(string identity, GameTitleIdentity selected, string shape, CancellationToken ct)
    {
        ApplicationIntegrationIdentity.Validate(identity); selected.Validate();
        if (shape is not ("square" or "vertical")) throw new ArgumentException("Choose a cover format.");
        lock (gate)
            if (!choices.TryGetValue(selected,out var expires) || expires < DateTime.UtcNow)
                throw new InvalidOperationException("Search for the title again before using this result.");
        var result = new JsonObject { ["selection"] = System.Text.Json.JsonSerializer.SerializeToNode(selected), ["title"] = selected.Title };
        var details = selected.Provider=="ign" ? await metadata(identity,selected,ct) : new JsonObject { ["status"] = "not_requested" }; result["metadata"] = details;
        var assets = new JsonObject();
        foreach (var type in selected.Provider == "ign" ? Array.Empty<string>() : new[] { "cover", "banner", "hero", "logo", "icon" })
        {
            ct.ThrowIfCancellationRequested();
            var path = await artwork(identity,selected,details,shape,type,ct);
            if (path is not null) assets[type] = path;
        }
        ct.ThrowIfCancellationRequested(); result["assets"] = assets; return result;
    }
    public async Task<JsonObject> RefetchPreparedAsync(string identity,GameTitleIdentity selected,string shape,
        ApplicationIntegrationDataStore store,JsonObject? edits,CancellationToken ct)
    {
        var baseline = await store.ReadCategoryAsync(identity,"metadatadeck",ct);
        var result = await RefetchAsync(identity,selected,shape,ct);
        if (result["metadata"]?["metadata"] is JsonObject fields)
        {
            foreach (var edit in edits ?? new()) if (edit.Key!="title") fields[edit.Key] = edit.Value?.DeepClone();
            fields["title"] = result["title"]?.DeepClone();
            fields["titleIdentity"] = result["selection"]?.DeepClone();
            result["metadata"]!["metadata"] = await store.PatchUnchangedAsync(identity,"metadatadeck",fields,baseline,ct);
        }
        else if(selected.Provider=="steamgriddb")
            await store.PatchAsync(identity,"artwork",new JsonObject { ["titleIdentity"] = result["selection"]?.DeepClone() },ct);
        return result;
    }
}

public static class GameTitleRefetchPolicy
{
    public static bool CanReplaceArtwork(JsonObject preview, JsonObject game, string type) =>
        (preview["compositions"]?[game["id"]?.ToString() ?? ""]?["assets"] as JsonObject)?.ContainsKey(type) != true &&
        (game["artworkChoices"] as JsonObject)?.ContainsKey(type) != true;
    public static void ApplyEmulation(JsonObject preview, JsonObject game, JsonObject result)
    {
        string provider = result["selection"]?["Provider"]?.ToString() ?? result["selection"]?["provider"]?.ToString() ?? "";
        game[provider=="ign"?"metadataTitleIdentity":"artworkTitleIdentity"] = result["selection"]?.DeepClone();
        var fetched = result["metadata"]?["metadata"] as JsonObject;
        if (fetched is not null)
        {
            var current = (JsonObject)fetched.DeepClone();
            foreach (var field in game["metadataEdits"] as JsonObject ?? new())
                if (field.Key!="title") current[field.Key] = field.Value?.DeepClone();
            current["title"] = fetched["title"]?.DeepClone();
            if (game["metadataEdits"] is JsonObject edits && edits.ContainsKey("title")) edits["title"] = fetched["title"]?.DeepClone();
            game["metadata"] = current; game["metadataMatch"] = result["metadata"]?["match"]?.DeepClone();
        }
        string id = game["id"]?.ToString() ?? throw new ArgumentException("A ROM identity is required.");
        var explicitAssets = preview["compositions"]?[id]?["assets"] as JsonObject;
        var choices = game["artworkChoices"] as JsonObject;
        foreach (var type in provider=="steamgriddb"?new[] { "cover", "banner", "hero", "logo", "icon" }:Array.Empty<string>())
        {
            if (explicitAssets?.ContainsKey(type) == true || choices?.ContainsKey(type) == true) continue;
            if(result["assets"]?[type] is null)continue;
            game[type == "cover" ? "coverPath" : type + "Path"] = result["assets"]?[type]?.DeepClone();
        }
    }
}

public static class GameTitleQuery
{
    public static async Task<string> ReadAsync(ApplicationIntegrationDataStore store,string? identity,string category,string fallback,CancellationToken ct = default)
    {
        return (await ReadIdentityAsync(store,identity,category,ct))?.Title ?? fallback;
    }
    public static async Task<GameTitleIdentity?> ReadIdentityAsync(ApplicationIntegrationDataStore store,string? identity,string category,CancellationToken ct = default)
    {
        if(identity is null)return null;
        if(category is not ("metadata" or "metadatadeck" or "artwork"))throw new ArgumentException("Choose Metadata or Artwork.");
        var data=await store.ReadCategoryAsync(identity,category is "metadata" or "metadatadeck"?"metadatadeck":"artwork",ct);
        if(data["titleIdentity"] is not JsonObject selected)return null;
        var value=System.Text.Json.JsonSerializer.Deserialize<GameTitleIdentity>(selected.ToJsonString(),new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        if(value is null || value.Provider!=(category=="artwork"?"steamgriddb":"ign"))return null;
        value.Validate();return value;
    }
}

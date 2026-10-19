using System.Globalization;
using System.Text.Json.Nodes;

namespace Playhub.Integrations;

public sealed record GameTitleIdentity(string Provider, string Id, string Slug, string Title, int? Year = null)
{
    public void Validate()
    {
        if (Provider != "steamgriddb" || string.IsNullOrWhiteSpace(Title) || Title.Length > 512 ||
            !int.TryParse(Id, NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id <= 0 || !string.IsNullOrEmpty(Slug))
            throw new ArgumentException("Choose a SteamGridDB game title result.");
    }
}

/// <summary>Refetch artwork using a title identity explicitly chosen from a recent search.</summary>
public sealed class GameTitleRefetchService(
    Func<string, string, CancellationToken, Task<IReadOnlyList<GameTitleIdentity>>> search,
    Func<string, GameTitleIdentity, string, string, CancellationToken, Task<string?>> artwork)
{
    private readonly Dictionary<GameTitleIdentity, DateTime> choices = new();
    private readonly object gate = new();

    public async Task<IReadOnlyList<GameTitleIdentity>> SearchAsync(string provider, string title, CancellationToken ct)
    {
        if (provider != "steamgriddb" || string.IsNullOrWhiteSpace(title) || title.Length > 512)
            throw new ArgumentException("Choose a game title.");
        var found = await search(provider, title.Trim(), ct);
        ct.ThrowIfCancellationRequested();
        var result = found.Where(value => value.Provider == provider).DistinctBy(value => value.Id).Take(40).ToArray();
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
        ApplicationIntegrationIdentity.Validate(identity);
        selected.Validate();
        if (shape is not ("square" or "vertical")) throw new ArgumentException("Choose a cover format.");
        lock (gate)
            if (!choices.TryGetValue(selected, out var expires) || expires < DateTime.UtcNow)
                throw new InvalidOperationException("Search for the title again before using this result.");
        var result = new JsonObject { ["selection"] = System.Text.Json.JsonSerializer.SerializeToNode(selected), ["title"] = selected.Title };
        var assets = new JsonObject();
        var acquired = await Task.WhenAll(new[] { "cover", "banner", "hero", "logo", "icon" }
            .Select(async type => { ct.ThrowIfCancellationRequested(); return (Type: type, Path: await artwork(identity, selected, shape, type, ct)); }));
        foreach (var asset in acquired) if (asset.Path is not null) assets[asset.Type] = asset.Path;
        ct.ThrowIfCancellationRequested();
        result["assets"] = assets;
        return result;
    }

    public async Task<JsonObject> RefetchPreparedAsync(string identity, GameTitleIdentity selected, string shape,
        ApplicationIntegrationDataStore store, CancellationToken ct)
    {
        var result = await RefetchAsync(identity, selected, shape, ct);
        await store.PatchAsync(identity, "artwork", new JsonObject { ["titleIdentity"] = result["selection"]?.DeepClone() }, ct);
        return result;
    }
}

public static class GameTitleRefetchPolicy
{
    // Only unchanged artwork written by an earlier refetch may be replaced automatically.
    public static async Task<JsonObject> ArtworkReceiptAsync(GameTitleIdentity selection, IEnumerable<string> paths, CancellationToken ct)
    {
        selection.Validate();
        var files = new JsonArray();
        foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ApplicationIntegrationDataStore.GuardPath(path);
            await using var stream = File.OpenRead(path);
            string digest = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream, ct));
            files.Add(new JsonObject { ["path"] = Path.GetFullPath(path), ["sha256"] = digest });
        }
        if (files.Count == 0) throw new IOException("Refetched artwork was not saved.");
        return new JsonObject { ["selection"] = System.Text.Json.JsonSerializer.SerializeToNode(selection), ["files"] = files };
    }

    public static async Task<bool> CanReplaceArtworkAsync(JsonObject? receipt, IEnumerable<string?> currentPaths, bool explicitRemoval, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (explicitRemoval) return false;
        var paths = currentPaths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (receipt is null) return paths.Length == 0;
        if (paths.Length == 0 || receipt["files"] is not JsonArray files) return false;
        foreach (var path in paths)
        {
            try
            {
                string canonical = Path.GetFullPath(path!);
                var saved = files.OfType<JsonObject>().FirstOrDefault(file => string.Equals(file["path"]?.ToString(), canonical, StringComparison.OrdinalIgnoreCase));
                if (saved is null || !File.Exists(canonical)) return false;
                ApplicationIntegrationDataStore.GuardPath(canonical);
                await using var stream = File.OpenRead(canonical);
                string actual = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream, ct));
                if (!string.Equals(actual, saved["sha256"]?.ToString(), StringComparison.OrdinalIgnoreCase)) return false;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
        }
        return true;
    }
}

public static class GameTitleQuery
{
    public static async Task<string> ReadAsync(ApplicationIntegrationDataStore store, string? identity, string category, string fallback, CancellationToken ct = default) =>
        (await ReadIdentityAsync(store, identity, category, ct))?.Title ?? fallback;

    public static async Task<GameTitleIdentity?> ReadIdentityAsync(ApplicationIntegrationDataStore store, string? identity, string category, CancellationToken ct = default)
    {
        if (identity is null) return null;
        if (category != "artwork") throw new ArgumentException("Choose Artwork.");
        var data = await store.ReadCategoryAsync(identity, "artwork", ct);
        if (data["titleIdentity"] is not JsonObject selected) return null;
        var value = System.Text.Json.JsonSerializer.Deserialize<GameTitleIdentity>(selected.ToJsonString(), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        if (value is null || value.Provider != "steamgriddb") return null;
        value.Validate();
        return value;
    }
}

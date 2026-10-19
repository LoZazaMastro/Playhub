using System.Text.Json.Nodes;

namespace Playhub.Integrations;

public static class GameTitleRefetchFactory
{
    public static GameTitleRefetchService Create(
        Func<string,CancellationToken,Task<IReadOnlyList<GameTitleIdentity>>> searchSteamGridDb,
        Func<int,string,string,CancellationToken,Task<JsonArray>> selectedSteamGridDb,
        ApplicationImageCache cache, Func<string,CancellationToken,Task<byte[]>>? downloadSteamGridDb = null)
    {
        return new GameTitleRefetchService((_,title,ct) => searchSteamGridDb(title,ct),
        async(identity,selected,shape,type,ct) =>
        {
            var options = await selectedSteamGridDb(int.Parse(selected.Id,System.Globalization.CultureInfo.InvariantCulture),shape,type,ct);
            if (options.OfType<JsonObject>().FirstOrDefault()?["url"]?.ToString() is not string url) return null;
            var bytes = await (downloadSteamGridDb ?? ApplicationImageCache.DownloadSteamGridDbAsync)(url,ct);
            return await cache.SaveAsync(identity,type,bytes,ct);
        });
    }
}

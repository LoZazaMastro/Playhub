using System.Text.Json.Nodes;
using Playhub.Integrations;

namespace Playhub.Emulation.Workbench;

public static class GameTitleRefetchFactory
{
    public static GameTitleRefetchService Create(NativeMetadataService metadata,
        Func<string,CancellationToken,Task<IReadOnlyList<GameTitleIdentity>>> searchSteamGridDb,
        Func<int,string,string,CancellationToken,Task<JsonArray>> selectedSteamGridDb,
        ApplicationImageCache cache, HttpClient imageHttp, Func<string,CancellationToken,Task<byte[]>>? downloadSteamGridDb = null)
    {
        return new GameTitleRefetchService(async(provider,title,ct) =>
        {
            if (provider == "steamgriddb") return await searchSteamGridDb(title,ct);
            var options = await metadata.SearchAsync(title,ct);
            return options.OfType<JsonObject>().Where(item => item["id"] is not null && item["slug"] is not null)
                .Select(item => new GameTitleIdentity("ign",item["id"]!.ToString(),item["slug"]!.ToString(),item["title"]?.ToString() ?? "",int.TryParse(item["year"]?.ToString(),out int year)?year:null)).ToArray();
        },
        async(identity, selected, ct) =>
        {
            if (selected.Provider == "ign") return await metadata.FetchSelectedAsync(identity,selected.Id,selected.Slug,ct);
            var fetched = await metadata.FetchAsync(identity,selected.Title,ct);
            if (NativeMetadataService.Normalize(fetched["metadata"]?["title"]?.ToString() ?? "") != NativeMetadataService.Normalize(selected.Title))
                return new JsonObject { ["status"] = "not_found", ["identity"] = identity };
            return fetched;
        },
        async(identity,selected,details,shape,type,ct) =>
        {
            if (selected.Provider == "steamgriddb")
            {
                var options = await selectedSteamGridDb(int.Parse(selected.Id,System.Globalization.CultureInfo.InvariantCulture),shape,type,ct);
                if (options.OfType<JsonObject>().FirstOrDefault()?["url"]?.ToString() is not string url) return null;
                var bytes = await (downloadSteamGridDb ?? ApplicationImageCache.DownloadSteamGridDbAsync)(url,ct);
                return await cache.SaveAsync(identity,type,bytes,ct);
            }
            string? candidate = details["artworkUrl"]?.ToString();
            if (candidate is null || !ApplicationArtworkProviders.AllowedImage("ign",candidate)) return null;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(25));
            using var request = new HttpRequestMessage(HttpMethod.Get,candidate);
            using var response = await imageHttp.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 25 * 1024 * 1024) throw new InvalidDataException("Image exceeds the supported size.");
            await using var input = await response.Content.ReadAsStreamAsync(deadline.Token); using var output = new MemoryStream();
            var buffer = new byte[65536]; int count;
            while ((count = await input.ReadAsync(buffer,deadline.Token)) > 0)
            {
                if (output.Length + count > 25 * 1024 * 1024) throw new InvalidDataException("Image exceeds the supported size.");
                output.Write(buffer,0,count);
            }
            return await cache.SaveAsync(identity,type,output.ToArray(),deadline.Token);
        });
    }
}

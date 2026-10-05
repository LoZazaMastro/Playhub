using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Playhub.Integrations;

namespace Playhub.Emulation.Workbench;

/// <summary>App-owned originals and exact-account logo positioning. No Decky backend is required.</summary>
public sealed class PerfectArtworkState(ApplicationIntegrationDataStore store,
    Func<bool> steamRunning, Func<string,CancellationToken,Task<JsonNode?>>? evaluate = null,string? pluginsRoot = null)
{
    private static readonly SemaphoreSlim Order = new(1,1);
    public async Task SetHiddenAsync(string identity,uint appId,IReadOnlyList<string> directories,bool hidden,CancellationToken ct)
    {
        ApplicationIntegrationIdentity.Validate(identity);
        if(appId==0)
        {
            await store.PatchAsync(identity,"artwork",new JsonObject { ["logoHiddenRequested"] = hidden },ct);
            return;
        }
        if(directories.Count==0) throw new IOException("The exact Steam artwork account is unavailable.");
        await Order.WaitAsync(ct);
        try
        {
            var state=await store.ReadCategoryAsync(identity,"artwork",ct);
            var backups=state["logoPositionBackups"] as JsonObject ?? new();
            var changes=new List<(string File,byte[]? Before,JsonObject Root,JsonNode? Position)>();
            foreach(string directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string grid=Path.GetFullPath(directory);
                ApplicationIntegrationDataStore.GuardPath(grid);
                var folder=new DirectoryInfo(grid);
                if(folder.Name!="grid" || folder.Parent?.Name!="config" || folder.Parent.Parent is not { } account ||
                    account.Name.Length==0 || account.Name.Any(c=>c is < '0' or > '9') || account.Parent?.Name!="userdata")
                    throw new IOException("Untrusted Steam artwork directory.");
                string file=Path.Combine(grid,appId+".json");
                ApplicationIntegrationDataStore.GuardPath(file);
                byte[]? before=File.Exists(file)?await File.ReadAllBytesAsync(file,ct):null;
                if(before?.Length>64*1024) throw new IOException("Steam logo settings exceed the limit.");
                var root=before is null?new JsonObject():JsonNode.Parse(before) as JsonObject ?? throw new IOException("Invalid Steam logo settings.");
                string key=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(file.ToUpperInvariant())));
                if(hidden && backups[key] is null)
                {
                    JsonNode? original=root["logoPosition"]?.DeepClone();
                    if(pluginsRoot is not null && original?["nWidthPct"] is JsonValue width && width.TryGetValue<double>(out var w) && w<1)
                    {
                        var legacy=await ReadLegacySettingsAsync(pluginsRoot,ct);
                        if(legacy?["logo_position_backup_"+appId] is JsonObject legacyPosition &&
                            legacyPosition["nWidthPct"] is JsonValue pw && pw.TryGetValue<double>(out var savedWidth) && savedWidth>=1 &&
                            legacyPosition["nHeightPct"] is JsonValue ph && ph.TryGetValue<double>(out var savedHeight) && savedHeight>=1)
                            original=legacyPosition.DeepClone();
                    }
                    backups[key]=new JsonObject { ["file"]=file,["present"]=root.ContainsKey("logoPosition"),["position"]=original };
                }
                JsonNode? position;
                if(hidden) position=new JsonObject { ["pinnedPosition"]="BottomLeft",["nWidthPct"]=.01,["nHeightPct"]=.01 };
                else
                {
                    var backup=backups[key] as JsonObject;
                    if(backup is null) continue;
                    if(!string.Equals(backup["file"]?.ToString(),file,StringComparison.OrdinalIgnoreCase)) throw new IOException("Logo backup account mismatch.");
                    position=backup["position"]?.DeepClone();
                }
                if(position is null) root.Remove("logoPosition"); else root["logoPosition"]=position.DeepClone();
                root["nVersion"] ??= 1;
                changes.Add((file,before,root,position));
            }
            // Persist the original before the first Steam side effect, including cancellation/error paths.
            await store.PatchAsync(identity,"artwork",new JsonObject { ["logoPositionBackups"]=backups.DeepClone(),["logoHiddenRequested"]=hidden },ct);
            if(steamRunning())
            {
                var accounts=directories.Select(directory=>new DirectoryInfo(Path.GetFullPath(directory)).Parent!.Parent!.Name).Distinct(StringComparer.Ordinal).ToArray();
                if(accounts.Length!=1||!uint.TryParse(accounts[0],out uint accountId)||accountId==0||accountId.ToString(System.Globalization.CultureInfo.InvariantCulture)!=accounts[0])
                    throw new IOException("Steam logo delivery is pending for the exact account session.");
                if(evaluate is null || changes.Any(c=>!JsonNode.DeepEquals(c.Position,changes[0].Position)))
                    throw new IOException("Steam cannot restore different account positions while running.");
                if(changes.Count>0)
                {
                    // Steam's own API maintains its running cache; file readback remains mandatory.
                    var payload=changes[0].Root.ToJsonString();
                    var result=await evaluate(BuildLogoPositionExpression(appId,accounts[0],payload),ct);
                    if(result?["ok"]?.GetValue<bool>()!=true) throw new IOException("Steam logo position was not acknowledged.");
                }
            }
            else foreach(var change in changes)
            {
                ApplicationIntegrationDataStore.GuardPath(change.File);
                byte[]? actual=File.Exists(change.File)?await File.ReadAllBytesAsync(change.File,ct):null;
                if(!Same(actual,change.Before)) throw new IOException("Steam logo settings changed during editing.");
                Directory.CreateDirectory(Path.GetDirectoryName(change.File)!);
                string temporary=change.File+"."+Guid.NewGuid().ToString("N")+".tmp";
                try { await File.WriteAllTextAsync(temporary,change.Root.ToJsonString(),ct); File.Move(temporary,change.File,true); }
                finally { if(File.Exists(temporary)) File.Delete(temporary); }
            }
            foreach(var change in changes)
            {
                bool matches=false;
                for(int attempt=0;attempt<4;attempt++)
                {
                    ApplicationIntegrationDataStore.GuardPath(change.File);
                    var current=File.Exists(change.File)?JsonNode.Parse(await File.ReadAllBytesAsync(change.File,ct)) as JsonObject:null;
                    matches=current is not null && JsonNode.DeepEquals(current["logoPosition"],change.Position);
                    if(matches) break;
                    if(attempt<3) await Task.Delay(100,ct);
                }
                if(!matches) throw new IOException("Steam logo position readback failed.");
            }
        }
        finally { Order.Release(); }
    }
    private static bool Same(byte[]? a,byte[]? b)=>a is null?b is null:b is not null && a.AsSpan().SequenceEqual(b);

    internal static string BuildLogoPositionExpression(uint appId,string userId,string payload)=>
        "(async()=>{const a=window.App,u=a?.GetCurrentUser?.();if(!a?.BHasCurrentUser?.()||!a?.GetServicesInitialized?.()||!/^7656[0-9]{13}$/.test(u?.strSteamID??''))throw Error('steam_account_unavailable');const id=BigInt(u.strSteamID)-76561197960265728n;if(id.toString()!=="+System.Text.Json.JsonSerializer.Serialize(userId)+")throw Error('steam_account_mismatch');const f=window.SteamClient?.Apps?.SetCustomLogoPositionForApp;if(!f)throw Error('logo_position_unavailable');await window.SteamClient.Apps.SetCustomLogoPositionForApp("+appId+","+System.Text.Json.JsonSerializer.Serialize(payload)+");return {ok:true};})()";

    public static async Task<string?> LegacyPristineAsync(string pluginsRoot,uint appId,string target,string actualPath,CancellationToken ct)
    {
        if(appId==0) return null;
        string type=target=="banner"?"grid_l":target;
        if(type is not ("hero" or "grid_l")) throw new ArgumentException("Invalid composition target.");
        string home=Path.GetDirectoryName(Path.GetFullPath(pluginsRoot))!;
        var data=await ReadLegacySettingsAsync(pluginsRoot,ct);
        string? expected=data?["perfect_"+type+"_info_"+appId]?["sha256"]?.ToString();
        if(string.IsNullOrEmpty(expected)) return null;
        ApplicationIntegrationDataStore.GuardPath(actualPath);
        if(new FileInfo(actualPath).Length>25*1024*1024) throw new IOException("Artwork exceeds the limit.");
        string hash=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(actualPath,ct)));
        if(!hash.Equals(expected,StringComparison.OrdinalIgnoreCase)) return null;
        string root=Path.Combine(home,"data","Playhub-Artworks","perfect_sources");
        ApplicationIntegrationDataStore.GuardPath(root);
        var paths=new[]{"png","jpg","webp"}.Select(ext=>Path.Combine(root,appId+"_"+type+"."+ext)).Where(File.Exists).ToArray();
        if(paths.Length!=1) throw new IOException("The legacy composition original is unavailable or ambiguous.");
        ApplicationIntegrationDataStore.GuardPath(paths[0]); return paths[0];
    }
    private static async Task<JsonObject?> ReadLegacySettingsAsync(string pluginsRoot,CancellationToken ct)
    {
        string settings=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(pluginsRoot))!,"settings","Playhub-Artworks","playhub_artworks.json");
        ApplicationIntegrationDataStore.GuardPath(settings);
        if(!File.Exists(settings))return null;
        if(new FileInfo(settings).Length>4*1024*1024)throw new IOException("Artwork settings exceed the limit.");
        return JsonNode.Parse(await File.ReadAllBytesAsync(settings,ct)) as JsonObject ?? throw new IOException("Invalid legacy artwork settings.");
    }
}

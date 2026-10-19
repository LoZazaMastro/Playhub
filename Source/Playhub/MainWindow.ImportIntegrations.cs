using Playhub.Importing;
using Playhub.Models;
using Playhub.Services;
using System.Text.Json.Nodes;
using Playhub.Integrations;

namespace Playhub;

public sealed partial class MainWindow
{
    private async Task DeliverPreparedImportedArtworkAsync(IEnumerable<UwpGameEntry> exportedGames, CancellationToken ct = default)
    {
        string pluginsRoot = string.IsNullOrWhiteSpace(_settings.DeckyPluginsPath) ? AppPaths.DefaultDeckyPluginsPath : _settings.DeckyPluginsPath;
        foreach (var game in exportedGames)
        {
            ct.ThrowIfCancellationRequested();
            var token = ct;
            string identity = ImportedIntegrationIdentity.Pc(game.Aumid, game.LocalExecutablePath, game.SourceLaunchArguments);
            var store = new ApplicationIntegrationDataStore(Path.Combine(AppPaths.LocalDataRoot, "integrations"));
            var actualId = _uwpXbox.TryGetSteamShortcutAppId(game);
            if (actualId is not > 0) continue;
            var artworkState=await store.ReadCategoryAsync(identity,"artwork",token);
            if(artworkState["logoHiddenRequested"]?.GetValue<bool>()==true)
            {
                var actual=_uwpXbox.ReadCurrentSteamArtwork(game);
                bool confirmed=ArtworkLogoPolicy.ManualOverride(artworkState)==true;
                foreach(var target in new[]{"hero","banner"})
                    if(actual.TryGetValue(target,out var path)&&File.Exists(path))
                    {
                        string actualHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(path,token)));
                        if(actualHash.Equals(artworkState["perfect_"+target]?["sha256"]?.ToString(),StringComparison.OrdinalIgnoreCase)||target=="hero"&&ArtworkLogoPolicy.HasVerifiedAuthorHero(artworkState,actualHash))confirmed=true;
                    }
                if(!confirmed)throw new IOException("The exported composition did not match its prepared artwork.");
                await new PerfectArtworkState(store,ArtworkSteamRunning,SteamPluginBridge.EvaluateAsync,pluginsRoot).SetHiddenAsync(identity,actualId.Value,_uwpXbox.TryGetSteamGridDirectories(game),true,token);

            }
        }
    }
    private async Task ShowImportedGameArtworkAsync(UwpGameEntry game)
    {
        var context=CreateImportedGameContext(game);try{await ShowGameArtworkAsync(context);}finally{context.DisposeResources?.Invoke();}
    }
    private GameInfoContext CreateImportedGameContext(UwpGameEntry game)
    {
        var appId = _uwpXbox.TryGetSteamShortcutAppId(game) ?? 0;
        var identity = ImportedIntegrationIdentity.Pc(game.Aumid,game.LocalExecutablePath,game.SourceLaunchArguments);
        var store = new ApplicationIntegrationDataStore(Path.Combine(AppPaths.LocalDataRoot,"integrations"));
        var providers=new ApplicationArtworkProviders();
        var titleRefetch=CreateTitleRefetchService();
        var registered = new Dictionary<(string Type,string Url),string>();
        var selectedArtworkSources = new Dictionary<(string Type,string Url),ImportArtworkResult>();
        return new GameInfoContext(game.Name, appId,
            async token =>
            {
                var actual = new Dictionary<string,string>(_uwpXbox.ReadSelectedArtwork(game));
                foreach (var field in await store.ReadCategoryAsync(identity,"artwork",token))
                    if (field.Key is "cover" or "banner" or "hero" or "logo" or "icon")
                    {
                        if(field.Value is null) actual.Remove(field.Key);
                        else if(field.Value is JsonValue value && value.TryGetValue<string>(out var path) && File.Exists(path)) actual[field.Key]=path;
                    }
                return actual;
            },
            async (provider, title, type, token) =>
            {
                if (provider is not ("SteamGridDB" or "Steam"))
                {
                    var results = await providers.SearchAsync(provider.ToLowerInvariant(), title, type == "cover" ? "grid_p" : type == "banner" ? "grid_l" : type, global::Playhub.Services.CoverFormat.IsSquare(_settings.CoverFormat), ct: token);
                    foreach (var result in results.OfType<JsonObject>()) registered[(type,result["url"]!.GetValue<string>())] = provider.ToLowerInvariant();
                    return results.OfType<JsonObject>().Select(result => new ImportArtworkResult(result["url"]!.GetValue<string>(), result["thumb"]?.GetValue<string>() ?? result["url"]!.GetValue<string>(), result["width"]?.GetValue<int>() ?? 0, result["height"]?.GetValue<int>() ?? 0)).ToArray();
                }
                var selectedTitle=await GameTitleQuery.ReadIdentityAsync(store,identity,"artwork",token);
                var searchGame = new UwpGameEntry { Name = title, Aumid = game.Aumid, SteamAppId = game.SteamAppId,
                    SteamGridDbGameId=selectedTitle is not null && string.Equals(selectedTitle.Title,title,StringComparison.OrdinalIgnoreCase) && int.TryParse(selectedTitle.Id,out var matchedId)?matchedId:0 };
                var options = provider == "SteamGridDB" ? await _uwpXbox.GetSteamGridDbArtworkAsync(searchGame, type, _settings.SteamGridDbApiKey) : await _uwpXbox.GetOfficialSteamArtworkAsync(searchGame, type);
                token.ThrowIfCancellationRequested();
                var candidates=options.Select(option => new ImportArtworkResult(option.Url, option.PreviewUrl, option.Width, option.Height,option.Provider,option.AuthorName,option.AuthorSteamId)).ToArray();
                foreach(var candidate in candidates)selectedArtworkSources[(type,candidate.Url)]=candidate;
                return candidates;
            },
            async (type, option, token) =>
            {
                token.ThrowIfCancellationRequested(); string source = option.Url;
                if (registered.TryGetValue((type,source),out var provider))
                {
                    var bytes = await providers.DownloadAsync(provider,source,token);
                    source = await new ApplicationImageCache(Path.Combine(AppPaths.LocalDataRoot,"integrations")).SaveAsync(identity,type,bytes,token);
                }
                bool delivered = await _uwpXbox.DownloadAndApplySteamGridDbArtworkAsync(game, type, new(source, option.Preview, option.Width, option.Height));
                string chosenPath = type switch { "cover" => game.SteamGridDbCoverPath,"banner" => game.SteamGridDbBannerPath,"hero" => game.SteamGridDbHeroPath,"logo" => game.SteamGridDbLogoPath,"icon" => game.SteamGridDbIconPath,_ => throw new ArgumentException("Unknown artwork type.") };
                if (!File.Exists(chosenPath)) throw new IOException("Selected artwork was not saved.");
                selectedArtworkSources.TryGetValue((type,option.Url),out var trustedSource);
                var provenance=trustedSource is null?null:new JsonObject{["path"]=chosenPath,["provider"]=trustedSource.Provider,["authorName"]=trustedSource.AuthorName,["authorSteamId"]=trustedSource.AuthorSteamId,
                    ["sha256"]=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(chosenPath,token)))};
                await store.PatchAsync(identity,"artwork",new JsonObject { [type] = chosenPath,[type+"_source"]=provenance,[type+"_title_auto"]=null },token);
                if (appId != 0 && !delivered) throw new IOException("Steam artwork delivery was not confirmed.");
            },identity,token=>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(_uwpXbox.TryGetSteamGridDirectories(game));
            },async (type,token)=> { await _uwpXbox.RemoveArtworkAsync(game,type,token); await store.PatchAsync(identity,"artwork",new JsonObject { [type]=null,[type+"_source"]=null,[type+"_title_auto"]=null },token); },providers.Dispose,
            ReadArtworkSourceAsync:async(type,token)=>
            {
                var provenance=(await store.ReadCategoryAsync(identity,"artwork",token))[type+"_source"] as JsonObject;
                if(provenance?["path"]?.ToString() is not string path||!File.Exists(path))return null;
                ApplicationIntegrationDataStore.GuardPath(path);
                if(!Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(path,token))).Equals(provenance["sha256"]?.ToString(),StringComparison.OrdinalIgnoreCase))return null;
                return new ImportArtworkResult(path,path,0,0,provenance["provider"]?.ToString(),provenance["authorName"]?.ToString(),provenance["authorSteamId"]?.ToString());
            },SearchTitlesAsync:titleRefetch.SearchAsync,
            RefetchTitleAsync:(selection,token)=>ApplyImportedTitleAsync(game,titleRefetch,selection,token),
            ReadTitleQueryAsync:(_,token)=>GameTitleQuery.ReadAsync(store,identity,"artwork",game.Name,token));
    }

}

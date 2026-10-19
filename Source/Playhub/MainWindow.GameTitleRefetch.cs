using System.Text.Json.Nodes;
using Microsoft.UI.Xaml.Controls;
using Playhub.Importing;
using Playhub.Integrations;
using Playhub.Models;
using Playhub.Services;

namespace Playhub;

public sealed partial class MainWindow
{
    private readonly Dictionary<string,int> _titleRefetchGenerations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,CancellationTokenSource> _titleRefetchRequests = new(StringComparer.OrdinalIgnoreCase);
    private GameTitleRefetchService CreateTitleRefetchService() => GameTitleRefetchFactory.Create(
        async(title,ct) => (await _uwpXbox.SearchSteamGridDbGamesAsync(title,_settings.SteamGridDbApiKey).WaitAsync(ct))
            .Where(item=>item.Id>0).Select(item=>new GameTitleIdentity("steamgriddb",item.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),"",item.Name,item.ReleaseYear)).ToArray(),
        async(id,shape,type,ct) =>
        {
            var surrogate = new UwpGameEntry { SteamGridDbGameId = id };
            var options = await _uwpXbox.GetSteamGridDbArtworkAsync(surrogate,type,_settings.SteamGridDbApiKey).WaitAsync(ct);
            return new JsonArray(options.Select(option=>(JsonNode)new JsonObject { ["url"] = option.Url }).ToArray());
        }, new ApplicationImageCache(Path.Combine(AppPaths.LocalDataRoot,"integrations")));

    private async Task ShowGameTitleRefetchDialogAsync(UwpGameEntry game)
    {
        int generation = _titleRefetchGenerations.GetValueOrDefault(game.Aumid)+1;
        _titleRefetchGenerations[game.Aumid] = generation;
        if (_titleRefetchRequests.TryGetValue(game.Aumid,out var previous)) previous.Cancel();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        _titleRefetchRequests[game.Aumid] = lifetime;
        try
        {
        var service = CreateTitleRefetchService();
        string identity = ImportedIntegrationIdentity.Pc(game.Aumid,game.LocalExecutablePath,game.SourceLaunchArguments);
        var store = new ApplicationIntegrationDataStore(Path.Combine(AppPaths.LocalDataRoot,"integrations"));
        string artworkQuery=await GameTitleQuery.ReadAsync(store,identity,"artwork",game.Name,lifetime.Token);
        var chosen = await GameTitleIdentityPicker.ShowAsync(Content.XamlRoot,game.Name,T,service.SearchAsync,lifetime.Token,true,
            _=>artworkQuery, () => BuildSteamGridDbConnectionCard(),
            () => !string.IsNullOrWhiteSpace(_settings.SteamGridDbApiKey));
        if (_titleRefetchGenerations.GetValueOrDefault(game.Aumid) != generation) return;
        if (chosen.Remove)
        {
            RemoveSteamGridDbPreferenceKey(_settings.SteamGridDbGameOverrides,game.Aumid);
            RemoveSteamGridDbPreferenceKey(_settings.SteamGridDbTitleOverrides,game.Aumid);
            _settings.SteamGridDbArtworkDisabled.RemoveAll(value=>string.Equals(value,game.Aumid,StringComparison.OrdinalIgnoreCase));
            _settings.SteamGridDbArtworkDisabled.Add(game.Aumid); game.SteamGridDbArtworkDisabled = true; game.SteamGridDbGameId = 0;
            ClearSteamGridDbArtwork(game); await SaveSettingsSilentlyAsync(); RenderRefetchedGameCollections(); return;
        }
        if (chosen.Selection is null) return;
        foreach(var selected in chosen.AllSelections ?? new[]{chosen.Selection!})
        {
        await ApplyImportedTitleAsync(game,service,selected,lifetime.Token);
        RenderRefetchedGameCollections();
        }
        }
        finally { if (_titleRefetchGenerations.GetValueOrDefault(game.Aumid)==generation) _titleRefetchRequests.Remove(game.Aumid); }
    }
    private async Task ApplyImportedTitleAsync(UwpGameEntry game,GameTitleRefetchService service,GameTitleIdentity selected,CancellationToken token)
    {
        string identity = ImportedIntegrationIdentity.Pc(game.Aumid,game.LocalExecutablePath,game.SourceLaunchArguments);
        var store = new ApplicationIntegrationDataStore(Path.Combine(AppPaths.LocalDataRoot,"integrations"));
        var acquired = await service.RefetchPreparedAsync(identity,selected,global::Playhub.Services.CoverFormat.IsSquare(_settings.CoverFormat)?"square":"vertical",store,token);
        if(selected.Provider=="steamgriddb")
        {
            RemoveSteamGridDbPreferenceKey(_settings.SteamGridDbGameOverrides,game.Aumid);
            game.SteamGridDbGameId=int.Parse(selected.Id,System.Globalization.CultureInfo.InvariantCulture);
            _settings.SteamGridDbGameOverrides[game.Aumid] = game.SteamGridDbGameId;
        }
        if(selected.Provider=="steamgriddb")
        {
        _settings.SteamGridDbArtworkDisabled.RemoveAll(value=>string.Equals(value,game.Aumid,StringComparison.OrdinalIgnoreCase)); game.SteamGridDbArtworkDisabled = false;
        ImportedArtworkSelection.RefreshSteam(game,_uwpXbox.ReadCurrentSteamArtwork(game));
        foreach (var type in ImportedArtworkSelection.Types)
            if (acquired["assets"]?[type]?.ToString() is string path)
            {
                token.ThrowIfCancellationRequested();
                var previous = await store.ReadCategoryAsync(identity,"artwork",token);
                var physical = _uwpXbox.ReadCurrentSteamArtwork(game);
                bool removed = previous.ContainsKey(type) && previous[type] is null || game.ArtworkChoices.TryGetValue(type,out var choice) && choice is null;
                if (!await GameTitleRefetchPolicy.CanReplaceArtworkAsync(previous[type+"_title_auto"] as JsonObject,
                    new[] { previous[type]?.ToString(), game.ArtworkChoices.GetValueOrDefault(type), ImportedArtworkSelection.Get(game,type), physical.GetValueOrDefault(type) },removed,token)) continue;
                bool delivered = await _uwpXbox.DownloadAndApplySteamGridDbArtworkAsync(game,type,new(path,path,0,0));
                token.ThrowIfCancellationRequested();
                var after = _uwpXbox.ReadCurrentSteamArtwork(game);
                var receipt = await GameTitleRefetchPolicy.ArtworkReceiptAsync(selected,new[] { ImportedArtworkSelection.Get(game,type),after.GetValueOrDefault(type) ?? "" },token);
                await store.PatchAsync(identity,"artwork",new JsonObject { [type] = ImportedArtworkSelection.Get(game,type),[type+"_title_auto"] = receipt,[type+"_source"] = null },token);
                if (_uwpXbox.TryGetSteamShortcutAppId(game) is not null && !delivered) throw new IOException("Steam artwork delivery was not confirmed.");
            }
        }
        await SaveSettingsSilentlyAsync();
        SetStatus(string.Format(T("Risultato aggiornato: {0}."),selected.Title),InfoBarSeverity.Success);
    }
    private void RenderRefetchedGameCollections() { RenderUwpGames(); RenderExecutableGames(); RenderEpicGames(); RenderGogGames(); }
}

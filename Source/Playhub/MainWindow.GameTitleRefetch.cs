using System.Text.Json.Nodes;
using Microsoft.UI.Xaml.Controls;
using Playhub.Emulation.Workbench;
using Playhub.Importing;
using Playhub.Integrations;
using Playhub.Models;
using Playhub.Services;

namespace Playhub;

public sealed partial class MainWindow
{
    private static readonly System.Net.Http.HttpClient RefetchImageHttp = new(new System.Net.Http.HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(25) };
    private readonly Dictionary<string,int> _titleRefetchGenerations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,CancellationTokenSource> _titleRefetchRequests = new(StringComparer.OrdinalIgnoreCase);
    private GameTitleRefetchService CreateTitleRefetchService() => GameTitleRefetchFactory.Create(
        new NativeMetadataService(RefetchImageHttp),
        async(title,ct) => (await _uwpXbox.SearchSteamGridDbGamesAsync(title,_settings.SteamGridDbApiKey).WaitAsync(ct))
            .Where(item=>item.Id>0).Select(item=>new GameTitleIdentity("steamgriddb",item.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),"",item.Name,item.ReleaseYear)).ToArray(),
        async(id,shape,type,ct) =>
        {
            var surrogate = new UwpGameEntry { SteamGridDbGameId = id };
            var options = await _uwpXbox.GetSteamGridDbArtworkAsync(surrogate,type,_settings.SteamGridDbApiKey).WaitAsync(ct);
            return new JsonArray(options.Select(option=>(JsonNode)new JsonObject { ["url"] = option.Url }).ToArray());
        }, new ApplicationImageCache(Path.Combine(AppPaths.LocalDataRoot,"integrations")),RefetchImageHttp);

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
        string metadataQuery=await GameTitleQuery.ReadAsync(store,identity,"metadata",game.Name,lifetime.Token);
        string artworkQuery=await GameTitleQuery.ReadAsync(store,identity,"artwork",game.Name,lifetime.Token);
        var chosen = await GameTitleIdentityPicker.ShowAsync(Content.XamlRoot,game.Name,T,service.SearchAsync,lifetime.Token,true,provider=>provider=="ign"?metadataQuery:artworkQuery);
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
        var acquired = await service.RefetchPreparedAsync(identity,selected,global::Playhub.Services.CoverFormat.IsSquare(_settings.CoverFormat)?"square":"vertical",store,null,lifetime.Token);
        if (_titleRefetchGenerations.GetValueOrDefault(game.Aumid) != generation) return;
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
            if (!game.ArtworkChoices.ContainsKey(type) && acquired["assets"]?[type]?.ToString() is string path)
            {
                lifetime.Token.ThrowIfCancellationRequested();
                bool delivered = await _uwpXbox.DownloadAndApplySteamGridDbArtworkAsync(game,type,new(path,path,0,0));
                lifetime.Token.ThrowIfCancellationRequested();
                await store.PatchAsync(identity,"artwork",new JsonObject { [type] = ImportedArtworkSelection.Get(game,type) },lifetime.Token);
                if (_uwpXbox.TryGetSteamShortcutAppId(game) is not null && !delivered) throw new IOException("Steam artwork delivery was not confirmed.");
            }
        }
        bool metadataReady = acquired["metadata"]?["metadata"] is JsonObject;
        if (acquired["metadata"]?["metadata"] is JsonObject fields)
        {
            if (_uwpXbox.TryGetSteamShortcutAppId(game) is uint actualId)
            {
                await store.BindSteamAppIdAsync(identity,actualId,lifetime.Token);
                string plugins = string.IsNullOrWhiteSpace(_settings.DeckyPluginsPath)?AppPaths.DefaultDeckyPluginsPath:Path.GetFullPath(_settings.DeckyPluginsPath);
                var consumer = new PluginConsumerDelivery(plugins,Path.Combine(Path.GetDirectoryName(plugins)!,"settings"));
                if (consumer.IsInstalled("metadata") && (await consumer.DeliverAsync("metadata",actualId,fields,lifetime.Token))["delivered"]?.GetValue<bool>()!=true)
                    throw new IOException("Metadata delivery was not confirmed.");
            }
        }
        await SaveSettingsSilentlyAsync(); RenderRefetchedGameCollections();
        SetStatus(metadataReady || selected.Provider=="steamgriddb"?string.Format(T("Risultato aggiornato: {0}."),selected.Title):T("Nessun risultato trovato."),metadataReady || selected.Provider=="steamgriddb"?InfoBarSeverity.Success:InfoBarSeverity.Warning);
        }
        }
        finally { if (_titleRefetchGenerations.GetValueOrDefault(game.Aumid)==generation) _titleRefetchRequests.Remove(game.Aumid); }
    }
    private void RenderRefetchedGameCollections() { RenderUwpGames(); RenderExecutableGames(); RenderEpicGames(); RenderGogGames(); }
}

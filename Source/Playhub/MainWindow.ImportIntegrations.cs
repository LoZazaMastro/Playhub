using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Playhub.Emulation.Workbench;
using Playhub.Importing;
using Playhub.Models;
using Playhub.Services;
using System.Text.Json.Nodes;
using Playhub.Integrations;

namespace Playhub;

public sealed partial class MainWindow
{
    private static readonly HttpClient ImportMetadataHttp = new() { Timeout = TimeSpan.FromSeconds(25) };
    private readonly Dictionary<string, JsonObject> _importIntegrationResults = new(StringComparer.Ordinal);
    private bool ImportItalian => LocalizationService.ResolveLanguage(_settings.Language) == "it";
    private string ImportText(string english, string italian) => T(italian);
    private static string IntegrationName(string category) => category switch
    {
        "artworks" or "perfect-hero" or "hero-logo" => "Playhub Artworks",
        "metadata" => "Playhub Metadata", "themedeck" => "ThemeDeck",
        "trailerhero" => "TrailerHero", "launch-curtain" => "Launch Curtain", _ => category
    };
    private PostImportIntegrationService ImportIntegrationService(SteamPluginBridge bridge, uint appId)
    {
        var hero = new PerfectHeroService();
        return new(token => bridge.AppReadyAsync(appId, token),
            bridge.CallAsync, selectStream: bridge.SelectStreamingTrailerAsync, composeHero: hero.ApplyAsync, restoreHeroLogo: hero.RestoreLogoAsync,sourceScoped:bridge.IsAppOwned);
    }
    private ImportIntegrationPlan? ImportPlan(UwpGameEntry game, IEnumerable<string> enabled)
    {
        var appId = _uwpXbox.TryGetSteamShortcutAppId(game);
        return appId is > 0 ? new ImportIntegrationPlan(appId.Value, game.Name, _uwpXbox.ReadSelectedArtwork(game), enabled) : null;
    }
    private Task<JsonArray> DeliverPreparedImportedGamesAsync(IEnumerable<UwpGameEntry> exportedGames, CancellationToken ct = default)
    {
        string pluginsRoot = string.IsNullOrWhiteSpace(_settings.DeckyPluginsPath) ? AppPaths.DefaultDeckyPluginsPath : _settings.DeckyPluginsPath;
        return PreparedIntegrationDelivery.RunAsync(exportedGames,async (game,token)=>
        {
            string identity = ImportedIntegrationIdentity.Pc(game.Aumid,game.LocalExecutablePath,game.SourceLaunchArguments);
            var store = new ApplicationIntegrationDataStore(Path.Combine(AppPaths.LocalDataRoot,"integrations"));
            var actualId = _uwpXbox.TryGetSteamShortcutAppId(game);
            if (actualId is not > 0) return PreparedIntegrationDelivery.Unresolved(identity,game.Name,await store.ReadAsync(identity,token));
            var consumer = new PluginConsumerDelivery(pluginsRoot,Path.Combine(Path.GetDirectoryName(Path.GetFullPath(pluginsRoot))!,"settings"));
            var media = new ApplicationMediaAcquisition(Path.Combine(AppPaths.LocalDataRoot,"integrations","media"),Path.Combine(AppContext.BaseDirectory,"Tools","Media"));
            await using var coordinator = new ApplicationIntegrationCoordinator(identity,actualId.Value,store,media,consumer,(_,_,_)=>throw new InvalidOperationException("Prepared delivery cannot acquire new data."));
            // Only selections already prepared in Info; never enable removed legacy bulk acquisition.
            var delivered = await coordinator.DeliverPreparedAsync(ImportIntegrationPlan.Categories,token);
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
                delivered.Add(new JsonObject{["category"]="perfect-artwork",["delivered"]=true});
            }
            return new JsonObject { ["appId"] = actualId.Value,["identity"] = identity,["outcomes"] = delivered };
        },game=>game.Name,ct);
    }
    private void ShowPreparedIntegrationDeliveryWarning(JsonArray outcomes)
    {
        if(PreparedIntegrationDelivery.HasIncomplete(outcomes))
            SetStatus(T("Alcune integrazioni restano preparate. Il gioco è stato importato correttamente."),InfoBarSeverity.Warning);
    }
    private async Task ShowImportedGameInfoAsync(UwpGameEntry game)
    {
        var context=CreateImportedGameContext(game);try{await ShowGameInfoAsync(context);}finally{context.DisposeResources?.Invoke();}
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
                var searchGame = new UwpGameEntry { Name = title, Aumid = game.Aumid, SteamAppId = game.SteamAppId };
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
                await store.PatchAsync(identity,"artwork",new JsonObject { [type] = chosenPath,[type+"_source"]=provenance },token);
                if (appId != 0 && !delivered) throw new IOException("Steam artwork delivery was not confirmed.");
            },identity,token=>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(_uwpXbox.TryGetSteamGridDirectories(game));
            },async (type,token)=> { await _uwpXbox.RemoveArtworkAsync(game,type,token); await store.PatchAsync(identity,"artwork",new JsonObject { [type]=null,[type+"_source"]=null },token); },providers.Dispose,
            ReadArtworkSourceAsync:async(type,token)=>
            {
                var provenance=(await store.ReadCategoryAsync(identity,"artwork",token))[type+"_source"] as JsonObject;
                if(provenance?["path"]?.ToString() is not string path||!File.Exists(path))return null;
                ApplicationIntegrationDataStore.GuardPath(path);
                if(!Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(path,token))).Equals(provenance["sha256"]?.ToString(),StringComparison.OrdinalIgnoreCase))return null;
                return new ImportArtworkResult(path,path,0,0,provenance["provider"]?.ToString(),provenance["authorName"]?.ToString(),provenance["authorSteamId"]?.ToString());
            });
    }

    internal async Task ShowGameInfoAsync(GameInfoContext game)
    {
        var actualArtwork = await game.ReadArtworkAsync(CancellationToken.None);
        var artwork = new Dictionary<string,string>(actualArtwork);
        var plan = game.ShortcutAppId > 0 || game.StableIdentity is not null ? new ImportIntegrationPlan(game.ShortcutAppId, game.Title, actualArtwork, ImportIntegrationPlan.Categories,game.StableIdentity) : null;
        string? resultKey = ImportedIntegrationIdentity.ResultKey(game.ShortcutAppId,game.StableIdentity);
        using var session = new ImportInfoSession();
        var lifetime = session;
        ImportInfoSaveQueue? saves = null;
        var body = new Grid { RowSpacing = 16, Tag = "noloc" };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var notice = new TextBlock { Text = "", TextWrapping = TextWrapping.Wrap, Opacity = .7, Visibility = Visibility.Collapsed };
        body.Children.Add(notice);
        var tabs = new SelectorBar();
        foreach (var (id, name) in new[] { ("metadata", "Playhub Metadata"), ("themedeck", "ThemeDeck"), ("trailerhero", "TrailerHero"), ("launch-curtain", "Launch Curtain") })
            tabs.Items.Add(new SelectorBarItem { Text = name, Tag = id });
        Grid.SetRow(tabs, 1); body.Children.Add(tabs);
        var host = new StackPanel { Spacing = 16 };
        var bodyScroll = new ScrollViewer { Content = host, Padding = new Thickness(0,0,24,32), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(bodyScroll, 2); body.Children.Add(bodyScroll);
        var panels = new Dictionary<string, StackPanel>();
        foreach (var category in ImportIntegrationPlan.Categories) panels[category] = new StackPanel { Spacing = 12, Visibility = Visibility.Collapsed };
        var sections = new Dictionary<string, ContentControl>();
        foreach (var pair in panels)
        {
            var section = new ContentControl { Content = pair.Value, HorizontalContentAlignment = HorizontalAlignment.Stretch, Visibility = Visibility.Collapsed };
            sections[pair.Key] = section; host.Children.Add(section);
        }
        WebView2? preview = null;
        int previewGeneration=0;
        WebView2 PreviewPlayer()
        {
            if (preview is not null) return preview;
            preview = new WebView2 { Height = 236, HorizontalAlignment = HorizontalAlignment.Stretch };
            preview.CoreWebView2Initialized += (_, _) => { if (session.IsClosed && preview.CoreWebView2 is not null) { try { preview.Close(); } catch (Exception) { } } };
            return preview;
        }
        MediaPlayerElement? currentMusicPlayer = null;
        var mediaPause = new Dictionary<MediaPlayerElement,Action>();
        var mediaCleanup = new Dictionary<MediaPlayerElement,Action>();
        async Task<MediaPlayerElement> CreateLocalPlayerAsync(Uri uri,double height)
        {
            using var opening=CancellationTokenSource.CreateLinkedTokenSource(session.Token);opening.CancelAfter(TimeSpan.FromSeconds(10));
            var source = await ImportLocalMediaSource.OpenAsync(uri,Diag.Step,opening.Token);
            if(session.IsClosed) { source.Dispose();throw new OperationCanceledException(session.Token); }
            var owned = new Windows.Media.Playback.MediaPlayer { AutoPlay = false };
            var element = new MediaPlayerElement { AreTransportControlsEnabled = true,Height = height,HorizontalAlignment = HorizontalAlignment.Stretch };
            var resource = new ImportMediaResource(owned.Pause,()=>element.SetMediaPlayer(null),()=>owned.Source=null,source.Dispose,owned.Dispose);
            try { source.AttachDiagnostics(owned);owned.Source=source.Source;element.SetMediaPlayer(owned); }
            catch { resource.Dispose();throw; }
            bool retiring=false;
            void Retire() { retiring=true;resource.Dispose();mediaPause.Remove(element); }
            element.Unloaded += (_,_)=>
            {
                Diag.Step($"Local media preview unloaded retiring={retiring} sessionClosed={session.IsClosed}");
                if(retiring || session.IsClosed)resource.Dispose();
                else { try { owned.Pause(); } catch(Exception) { } }
            };
            mediaCleanup[element]=Retire;mediaPause[element]=owned.Pause;session.Register(Retire);
            return element;
        }
        void PauseOwnedPlayer(Action pause)
        {
            try { pause(); }
            catch(Exception error) { Diag.Step($"Local media pause failed type={error.GetType().Name} hresult={error.HResult:X8}"); }
        }
        void PauseLocalPlayer(MediaPlayerElement? element)
        { if(element is not null && mediaPause.TryGetValue(element,out var pause))PauseOwnedPlayer(pause); }
        void StopPreview()
        {
            previewGeneration++;
            foreach (var pause in mediaPause.Values.ToArray())PauseOwnedPlayer(pause);
            if (preview is null) return;
            try { if (preview.CoreWebView2 is not null) preview.CoreWebView2.Navigate("about:blank"); } catch (Exception) { }
            preview.Visibility = Visibility.Collapsed;
            if(preview.Parent is Grid stoppedSlot && stoppedSlot.Children.Count==1) { stoppedSlot.Height=double.NaN;stoppedSlot.Visibility=Visibility.Collapsed; }
        }
        session.Register(() => { if (preview?.CoreWebView2 is not null) preview.Close(); });
        tabs.SelectionChanged += (_, _) =>
        {
#if PLAYHUB_UI_REVIEW
            Diag.Step("Info tab begin");
#endif
            StopPreview();
#if PLAYHUB_UI_REVIEW
            Diag.Step("Info tab pause-complete");
#endif
            string? selected=tabs.SelectedItem?.Tag as string;
            foreach (var pair in panels)
            {
                var visibility = pair.Key == selected ? Visibility.Visible : Visibility.Collapsed;
                pair.Value.Visibility = visibility;
                sections[pair.Key].Visibility = visibility;
            }
#if PLAYHUB_UI_REVIEW
            Diag.Step("Info tab show-complete");
#endif
            bodyScroll.ChangeView(null, 0, null, true);
        };
        tabs.SelectedItem = tabs.Items[0];
        var metadata = new JsonObject(); var metadataEdits = new JsonObject(); var metadataManualFields = new JsonObject(); var music = new JsonObject(); var curtain = new JsonObject();
        var runtime = new SteamPluginBridge();
        var integrationRoot = Path.Combine(AppPaths.LocalDataRoot,"integrations");
        var pluginsRoot = string.IsNullOrWhiteSpace(_settings.DeckyPluginsPath) ? AppPaths.DefaultDeckyPluginsPath : Path.GetFullPath(_settings.DeckyPluginsPath);
        var consumer = new PluginConsumerDelivery(pluginsRoot,Path.Combine(Path.GetDirectoryName(pluginsRoot)!,"settings"),runtime.CallAsync);
        await using var mediaSearch = new ApplicationMediaSearch(integrationRoot,Path.Combine(AppContext.BaseDirectory,"Tools","Media"));
        using var curtainProviders = new ApplicationArtworkProviders();
        var curtainSelections = new Dictionary<(string Provider,string Url),ImportArtworkResult>();
        var metadataStore=new ApplicationIntegrationDataStore(integrationRoot);
        ApplicationIntegrationCoordinator? applicationInstance = null;
        await using var application = applicationInstance = game.StableIdentity is not null ? new ApplicationIntegrationCoordinator(game.StableIdentity,game.ShortcutAppId,
            new ApplicationIntegrationDataStore(integrationRoot),new ApplicationMediaAcquisition(integrationRoot,Path.Combine(AppContext.BaseDirectory,"Tools","Media")),consumer,
            async(identity,title,token)=>
            {
                var selected=await GameTitleQuery.ReadIdentityAsync(metadataStore,identity,"metadata",token);
                var native=new NativeMetadataService(ImportMetadataHttp);
                return selected is { Provider:"ign" } ? await native.FetchSelectedAsync(identity,selected.Id,selected.Slug,token) : await native.FetchAsync(identity,title,token);
            },
            async (plugin,method,args,token) =>
            {
                if (plugin is "ThemeDeck" or "TrailerHero" && method is "search_youtube" or "search_youtube_videos")
                    return new JsonObject { ["ok"] = true,["results"] = await mediaSearch.SearchAsync(args[0]!.GetValue<string>(),plugin == "ThemeDeck" ? "themedeck" : "trailerhero",token) };
                if (plugin == "Launch Curtain" && method == "search_google_images")
                {
                    var request = args[0] as JsonObject ?? throw new ArgumentException();
                    if (request["app_id"]?.GetValue<uint>() != game.ShortcutAppId) throw new ArgumentException("Background request source mismatch.");
                    string title = request["title"]!.GetValue<string>();
                    var results = new JsonArray(); curtainSelections.Clear();
                    foreach (var provider in (request["services"] as JsonArray ?? new()).Select(value=>value!.GetValue<string>()).Distinct())
                    {
                        var assets = provider == "steamgriddb" ? await game.SearchStoreArtworkAsync("SteamGridDB",title,"hero",token)
                            : (await curtainProviders.SearchAsync(provider,title,"hero",ct:token)).OfType<JsonObject>().Select(asset=>new ImportArtworkResult(asset["url"]!.GetValue<string>(),asset["thumb"]?.GetValue<string>() ?? asset["url"]!.GetValue<string>(),asset["width"]?.GetValue<int>() ?? 0,asset["height"]?.GetValue<int>() ?? 0)).ToArray();
                        foreach (var asset in assets.Take(24))
                        {
                            curtainSelections[(provider,asset.Url)] = asset;
                            results.Add(new JsonObject { ["id"] = "background-"+results.Count,["image_url"] = asset.Url,["thumbnail_url"] = asset.Preview,["source"] = provider,["resolution"] = request["resolution"]?.DeepClone(),["width"] = asset.Width,["height"] = asset.Height });
                        }
                    }
                    return new JsonObject { ["ok"] = true,["results"] = results };
                }
                if (plugin == "Launch Curtain" && method == "download_google_image")
                {
                    var request = args[0] as JsonObject ?? throw new ArgumentException(); string provider = request["source"]!.GetValue<string>().ToLowerInvariant(),url = request["image_url"]!.GetValue<string>();
                    if (request["app_id"]?.GetValue<uint>() != game.ShortcutAppId || !curtainSelections.ContainsKey((provider,url))) throw new ArgumentException("Search for this background before choosing it.");
                    var bytes = provider == "steamgriddb" ? await ApplicationImageCache.DownloadSteamGridDbAsync(url,token) : await curtainProviders.DownloadAsync(provider,url,token);
                    string path = await new ApplicationImageCache(integrationRoot).SaveAsync(game.StableIdentity!,"curtain",bytes,token);
                    var saved = await applicationInstance!.CallAsync("Launch Curtain","save_game_settings",new JsonArray(new JsonObject { ["app_id"] = game.ShortcutAppId,["settings"] = new JsonObject { ["fullscreen_image_path"] = path } }),token) as JsonObject ?? throw new IOException("Background save was not confirmed.");
                    saved["path"] = path; return saved;
                }
                throw new NotSupportedException("This autonomous acquisition provider is not ready.");
            },selectStream:runtime.SelectStreamingTrailerAsync) : null;
        var bridge = application is null ? runtime : new SteamPluginBridge(application.CallAsync);
        var editor = new GameIntegrationEditorService(bridge);
        var mediaSelection = new Dictionary<string, string>();
        var metadataFields = new Dictionary<string, TextBox>();
        var currentMedia = new Dictionary<string, TextBlock>();
        var mediaPreviewSlots = new Dictionary<string, Grid>();
        void SetMediaPreview(string category,UIElement content,double height,Grid? resultSlot=null)
        {
            var slot=resultSlot??mediaPreviewSlots[category];
            foreach(var old in slot.Children.OfType<MediaPlayerElement>().ToArray())
                if(!ReferenceEquals(old,content) && mediaCleanup.Remove(old,out var cleanup))cleanup();
            slot.Children.Clear(); slot.Height=height;
            slot.Visibility=Visibility.Visible; slot.Children.Add(content);
        }
        async Task PlayYoutubeAsync(string category,string videoId,Grid? resultSlot=null)
        {
            if(session.IsClosed)return;
            StopPreview();int generation=previewGeneration;
            var player=PreviewPlayer();
            if(player.Parent is Panel oldSlot)
            {
                oldSlot.Children.Remove(player);
                if(oldSlot is Grid oldGrid && oldGrid.Children.Count==0) { oldGrid.Height=double.NaN;oldGrid.Visibility=Visibility.Collapsed; }
            }
            SetMediaPreview(category,player,236,resultSlot);player.Visibility=Visibility.Visible;
            try
            {
                await player.EnsureCoreWebView2Async();
                if(session.IsClosed||generation!=previewGeneration)return;
                var request=player.CoreWebView2.Environment.CreateWebResourceRequest("https://www.youtube.com/embed/"+videoId+"?autoplay=1","GET",null,"Referer: https://github.com/LoZazaMastro/Playhub/\r\n");
                player.CoreWebView2.NavigateWithWebResourceRequest(request);
            }
            catch(Exception error) { Diag.Step("Media preview failed: "+error.GetType().Name);if(!session.IsClosed){notice.Text=T("Ricerca non disponibile. Riprova.");notice.Visibility=Visibility.Visible;} }
        }
        foreach (var (key, label) in new[] { ("title", ImportText("Title", "Titolo")), ("short_description", ImportText("Description", "Descrizione")), ("genres", ImportText("Genres, separated by commas", "Generi, separati da virgole")), ("developers", ImportText("Developers, separated by commas", "Sviluppatori, separati da virgole")), ("publishers", ImportText("Publishers, separated by commas", "Editori, separati da virgole")) })
        {
            var field = new TextBox { MaxHeight = key == "short_description" ? 140 : double.PositiveInfinity, Header = label, AcceptsReturn = key == "short_description", TextWrapping = key == "short_description" ? TextWrapping.Wrap : TextWrapping.NoWrap, IsEnabled = plan is not null };
            metadataFields[key] = field; panels["metadata"].Children.Add(field);
        }
        var metadataLoading = false;
        foreach (var (key, field) in metadataFields) field.TextChanged += (_, _) =>
        {
            if (metadataLoading) return;
            metadataEdits[key] = key is "genres" or "developers" or "publishers" ? new JsonArray(field.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(value => key == "genres" ? (JsonNode)JsonValue.Create(value)! : new JsonObject { ["name"] = value, ["url"] = "" }).ToArray()) : JsonValue.Create(field.Text);
            metadataManualFields[key] = metadataEdits[key]?.DeepClone();
            saves?.FieldsChanged();
        };
        void RenderMetadata(JsonObject value)
        {
            value = ImportIntegrationEdits.OverlayManualFields(value, metadataManualFields);
            metadataLoading = true;
            try { foreach (var (key, field) in metadataFields) field.Text = value[key] is JsonArray list ? string.Join(", ", list.Select(item => item is JsonObject obj ? obj["name"]?.GetValue<string>() : item?.GetValue<string>())) : value[key]?.GetValue<string>() ?? (key == "title" ? game.Title : ""); }
            finally { metadataLoading = false; }
        }
        RenderMetadata(metadata);
        var scrape = new Button { Content = ImportText("Find game details", "Cerca i dettagli del gioco"), IsEnabled = plan is not null };
        scrape.Click += async (_, _) =>
        {
            sections["metadata"].IsEnabled = false;
            try { var fetched = await bridge.CallAsync("Playhub Metadata", "auto_fetch_metadata", new JsonArray(plan!.AppId, game.Title), lifetime.Token) as JsonObject; if (fetched is null || fetched.Count == 0 || fetched["error"] is not null || fetched["ok"]?.GetValue<bool>() == false) throw new InvalidOperationException(); var actual = await bridge.CallAsync("Playhub Metadata", "get_metadata", new JsonArray(plan!.AppId), lifetime.Token) as JsonObject; lifetime.Token.ThrowIfCancellationRequested(); if (actual is null || !JsonNode.DeepEquals(actual["title"], fetched["title"])) throw new InvalidOperationException(); metadata = actual; foreach (var pair in metadataManualFields) if (!JsonNode.DeepEquals(actual[pair.Key], pair.Value)) metadataEdits[pair.Key] = pair.Value?.DeepClone(); RenderMetadata(metadata); if (metadataEdits.Count > 0) saves?.FieldsChanged(); else { notice.Text="";notice.Visibility=Visibility.Collapsed; } }
            catch (OperationCanceledException) { }
            catch { notice.Text = ImportText("No game details found. You can enter them manually.", "Nessun dettaglio trovato. Puoi inserirli manualmente."); }
            finally { sections["metadata"].IsEnabled = true; scrape.IsEnabled = plan is not null; }
        };
        panels["metadata"].Children.Insert(0, scrape);

        var dialog = new ContentDialog { Title = game.Title, Content = body, CloseButtonText = T("Chiudi"), XamlRoot = Content.XamlRoot, Tag = "noloc" };
        void ResizeInfo()
        {
            var size = ImportInfoSize.ForRoot(dialog.XamlRoot.Size.Width, dialog.XamlRoot.Size.Height);
            body.Width = size.Width; body.Height = size.Height;
            dialog.Resources["ContentDialogMinWidth"] = size.Width + 48;
            dialog.Resources["ContentDialogMaxWidth"] = size.Width + 48;
        }
        void RootChanged(XamlRoot _, XamlRootChangedEventArgs __) => ResizeInfo();
        ResizeInfo(); dialog.XamlRoot.Changed += RootChanged;
        session.Register(() => dialog.XamlRoot.Changed -= RootChanged);
        dialog.Closed += (_, _) => session.Dispose();
        CheckBox? offlineTrailer = null;
        foreach (var category in new[] { "themedeck", "trailerhero" })
        {
            var current = new TextBlock { Text = T("Caricamento…"), TextWrapping = TextWrapping.Wrap, Opacity = .7 };
            currentMedia[category] = current;
            var currentCard = new StackPanel { Spacing = 10 };
            currentCard.Children.Add(new TextBlock { Text = T("Attuale"), FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            currentCard.Children.Add(current);
            var previewSlot = new Grid { Visibility = Visibility.Collapsed,HorizontalAlignment = HorizontalAlignment.Stretch };
            mediaPreviewSlots[category] = previewSlot; currentCard.Children.Add(previewSlot);
            panels[category].Children.Add(currentCard);
            var searchTitle = new TextBox { Header = ImportText("Search title", "Titolo da cercare"), Text = game.Title };
            var find = new Button { Content = ImportText("Find alternatives", "Cerca alternative"), IsEnabled = plan is not null };
            var choices = new StackPanel { Spacing = 10 };
            panels[category].Children.Add(searchTitle); panels[category].Children.Add(find); panels[category].Children.Add(choices);
            void Choices(JsonArray candidates)
            {
                if(preview?.Parent is Grid oldResult && !mediaPreviewSlots.Values.Contains(oldResult))StopPreview();
                choices.Children.Clear();
                if (candidates.Count == 0) choices.Children.Add(new TextBlock { Text = ImportText("No results found.", "Nessun risultato trovato.") });
                foreach (var candidate in candidates.OfType<JsonObject>())
                {
                    var id = candidate["id"]?.GetValue<string>() ?? "";
                    var value = category == "themedeck" ? candidate["url"]?.GetValue<string>() ?? (id.Length == 11 ? "https://www.youtube.com/watch?v=" + id : "") : id;
                    var card = new StackPanel { Spacing = 10 };
                    var resultPreview=new Grid { Visibility=Visibility.Collapsed,HorizontalAlignment=HorizontalAlignment.Stretch };
                    var thumbnail = candidate["thumbnail"]?.GetValue<string>() ?? candidate["thumbnail_url"]?.GetValue<string>() ?? candidate["thumb"]?.GetValue<string>() ?? (System.Text.RegularExpressions.Regex.IsMatch(id, "^[A-Za-z0-9_-]{11}$") ? "https://i.ytimg.com/vi/" + id + "/hqdefault.jpg" : "");
                    if (Uri.TryCreate(thumbnail, UriKind.Absolute, out var thumbnailUri) && thumbnailUri.Scheme is "https" or "http")
                        card.Children.Add(new Image { Source = new BitmapImage(thumbnailUri), Height = 72, MaxWidth = 128, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left });
                    card.Children.Add(new TextBlock { Text = candidate["title"]?.GetValue<string>() ?? "", TextWrapping = TextWrapping.Wrap });
                    var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    var choose = new Button { Content = ImportText("Use this result", "Usa questo risultato"), IsEnabled = !string.IsNullOrWhiteSpace(value) };
                    choose.Click += async (_, _) => await saves!.ChangeAsync(async () =>
                    {
                        var chosen = new ImportIntegrationPlan(plan!.AppId, game.Title, await game.ReadArtworkAsync(CancellationToken.None), [category],game.StableIdentity);
                        chosen.Game[category == "themedeck" ? "musicUrl" : "trailerVideoId"] = value;
                        chosen.Game[category == "themedeck" ? "musicTitle" : "trailerTitle"] = candidate["title"]?.DeepClone();
                        if (category == "trailerhero") chosen.Preview["trailers"]!["offline"] = offlineTrailer!.IsChecked == true;
                        var result = await ImportIntegrationService(bridge, chosen.AppId).RunAsync(chosen.Request(), CancellationToken.None);
                        _importIntegrationResults[resultKey!] = result;
                        if ((result["outcomes"] as JsonArray ?? new()).Any(item => item?["status"]?.GetValue<string>() is not ("completed" or "prepared"))) throw new InvalidOperationException();
                        if (!session.IsClosed)
                        {
                            StopPreview();
                            currentMedia[category].Text = candidate["title"]?.GetValue<string>() ?? value;
                            if (category == "themedeck")
                            {
                                var track = (result["outcomes"] as JsonArray)?.FirstOrDefault(item => item?["category"]?.GetValue<string>() == "themedeck")?["data"];
                                var trackPath = track?["path"]?.GetValue<string>();
                                if (File.Exists(trackPath))
                                {
                                    var player = await CreateLocalPlayerAsync(new Uri(trackPath!),96);
                                    SetMediaPreview(category,player,96,resultPreview); currentMusicPlayer = player;
                                }
                            }
                            else if (offlineTrailer!.IsChecked == true)
                            {
                                var saved=await bridge.CallAsync("TrailerHero","get_local_trailer",new JsonArray(chosen.AppId),CancellationToken.None);
                                if (saved?["path"]?.GetValue<string>() is string path && File.Exists(path))
                                    SetMediaPreview(category,await CreateLocalPlayerAsync(new Uri(path),320),320,resultPreview);
                            }
                            else if (Uri.TryCreate(thumbnail, UriKind.Absolute, out var selectedThumbnail))
                                SetMediaPreview(category,new Image { Source = new BitmapImage(selectedThumbnail), Height = 236, Stretch = Stretch.Uniform },236,resultPreview);
                        }
                    });
                    actions.Children.Add(choose);
                    if (System.Text.RegularExpressions.Regex.IsMatch(id, "^[A-Za-z0-9_-]{11}$"))
                    {
                        var play = new Button { Content = ImportText("Play preview", "Riproduci anteprima") };
                        play.Click += async (_, _) => await PlayYoutubeAsync(category,id,resultPreview);
                        actions.Children.Add(play);
                    }
                    card.Children.Add(actions);card.Children.Add(resultPreview); choices.Children.Add(new Border { Child = card, Padding = new Thickness(12), CornerRadius = new CornerRadius(8), Background = ResourceBrush("CardBackgroundFillColorDefaultBrush", Microsoft.UI.Colors.Transparent) });
                }
            }
            if (plan is not null && _importIntegrationResults.TryGetValue(resultKey!, out var previous))
                Choices(ImportIntegrationPlan.Candidates((previous["outcomes"] as JsonArray ?? new()).FirstOrDefault(item => item?["category"]?.GetValue<string>() == category)));
            find.Click += async (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(searchTitle.Text)) return;
                find.IsEnabled = false;
                try { var found = await bridge.CallAsync(category == "themedeck" ? "ThemeDeck" : "TrailerHero", category == "themedeck" ? "search_youtube" : "search_youtube_videos", new JsonArray(searchTitle.Text.Trim(), 5), lifetime.Token); Choices(found?["results"] as JsonArray ?? new()); }
                catch (OperationCanceledException) { }
                catch { notice.Text = ImportText("Search unavailable. Open Steam and check the plugin.", "Ricerca non disponibile. Riprova."); }
                finally { find.IsEnabled = plan is not null; }
            };
        }
        var loading = false;
        var volume = new Slider { Header = T("Volume"), Minimum = 0, Maximum = 100, Value = 100, StepFrequency = 1, IsEnabled = plan is not null };
        var offset = new NumberBox { Header = ImportText("Start at (seconds)", "Inizia da (secondi)"), Minimum = 0, Maximum = 30, Value = 0, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, IsEnabled = plan is not null };
        var loop = new ToggleSwitch { Header = ImportText("Repeat track", "Ripeti la traccia"), IsOn = true, IsEnabled = plan is not null };
        volume.ValueChanged += (_, _) => { if (!loading) { music["volume"] = volume.Value / 100; saves?.FieldsChanged(); } };
        offset.ValueChanged += (_, _) => { if (!loading && double.IsFinite(offset.Value)) { music["startOffset"] = offset.Value; saves?.FieldsChanged(); } };
        loop.Toggled += (_, _) => { if (!loading) { music["loop"] = loop.IsOn; saves?.FieldsChanged(); } };
        panels["themedeck"].Children.Add(volume); panels["themedeck"].Children.Add(offset); panels["themedeck"].Children.Add(loop);
        var removeMusic = new Button { Content = ImportText("Remove saved track", "Rimuovi la traccia salvata"), IsEnabled = plan is not null };
        removeMusic.Click += async (_, _) => await saves!.ChangeAsync(async () =>
        {
            music["remove"] = true; await SaveFieldsAsync();
            if (!session.IsClosed)
            {
                PauseLocalPlayer(currentMusicPlayer);
                if(currentMusicPlayer is not null && mediaCleanup.Remove(currentMusicPlayer,out var cleanup))cleanup();
                currentMusicPlayer=null;mediaPreviewSlots["themedeck"].Children.Clear();mediaPreviewSlots["themedeck"].Visibility=Visibility.Collapsed;currentMedia["themedeck"].Text = T("Nessuna traccia salvata.");
            }
        });
        panels["themedeck"].Children.Add(removeMusic);
        offlineTrailer = new CheckBox { Content = ImportText("Download the selected trailer for offline playback", "Scarica il trailer scelto per la riproduzione offline"), IsEnabled = plan is not null };
        panels["trailerhero"].Children.Add(offlineTrailer);
        var curtainControls = new Dictionary<string, Control>();
        var curtainVisualSettings=new JsonObject();
        string? currentBackground=artwork.GetValueOrDefault("hero");
        string? currentLogo=artwork.GetValueOrDefault("logo");
        using var curtainVisual=new LaunchCurtainVisualEditor(currentBackground,currentLogo,new JsonObject(),patch=>
        {
            foreach(var field in patch) { curtain[field.Key]=field.Value?.DeepClone();curtainVisualSettings[field.Key]=field.Value?.DeepClone(); }
            saves?.FieldsChanged();
        },T,showEmptyBackgroundPrompt:false);
        var curtainPreview=curtainVisual.Element;
        void PreviewBackground(string? path)
        {
            currentBackground=path;
            curtainVisual.SetImages(currentBackground,currentLogo);
            ToolTipService.SetToolTip(curtainPreview,path);
        }
        void RefreshCurtainPreview()
        {
            var values=new JsonObject();
            foreach(var pair in curtainControls)
            {
                if(pair.Value is ToggleSwitch toggle)values[pair.Key]=toggle.IsOn;
                if(pair.Value is NumberBox number&&double.IsFinite(number.Value))values[pair.Key]=number.Value;
            }
            foreach(var field in curtainVisualSettings)values[field.Key]=field.Value?.DeepClone();
            foreach(var field in curtain)values[field.Key]=field.Value?.DeepClone();
            curtainVisual.SetSettings(values);
            if(curtainControls.GetValueOrDefault("timeout_seconds") is NumberBox duration)duration.IsEnabled=plan is not null && (curtainControls.GetValueOrDefault("timeout_enabled") as ToggleSwitch)?.IsOn==true;
        }
        foreach (var (key, label) in new[] { ("enabled", ImportText("Launch screen", "Schermata di avvio")), ("show_logo", ImportText("Show logo", "Mostra logo")), ("logo_zoom_enabled", ImportText("Animate logo", "Anima il logo")), ("bg_zoom_enabled", ImportText("Animate background", "Anima lo sfondo")), ("timeout_enabled", ImportText("Limit duration", "Limita la durata")) })
        {
            var input = new ToggleSwitch { Header = label, IsEnabled = plan is not null };
            input.Toggled += (_, _) => { if (!loading) { curtain[key] = input.IsOn; curtainVisualSettings[key]=input.IsOn; RefreshCurtainPreview(); saves?.FieldsChanged(); } };
            curtainControls[key] = input; panels["launch-curtain"].Children.Add(input);
        }
        foreach (var (key, label, minimum, maximum, initial) in new[] { ("logo_scale", ImportText("Logo size (%)", "Dimensione logo (%)"), 50d, 200d, 100d), ("logo_position_x", ImportText("Horizontal position (%)", "Posizione orizzontale (%)"), 0d, 100d, 50d), ("logo_position_y", ImportText("Vertical position (%)", "Posizione verticale (%)"), 0d, 100d, 50d), ("background_opacity", ImportText("Background opacity (%)", "Opacità sfondo (%)"), 0d, 100d, 100d), ("timeout_seconds", ImportText("Maximum duration (seconds)", "Durata massima (secondi)"), 5d, 60d, 50d) })
        {
            var input = new NumberBox { Header = label, Minimum = minimum, Maximum = maximum, Value = initial, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, IsEnabled = plan is not null };
            input.ValueChanged += (_, _) => { if (!loading && double.IsFinite(input.Value)) { curtain[key] = input.Value; curtainVisualSettings[key]=input.Value; RefreshCurtainPreview(); saves?.FieldsChanged(); } };
            curtainControls[key] = input; panels["launch-curtain"].Children.Add(input);
        }
        var background = new TextBlock { Text = ImportText("Background from the game's artwork", "Sfondo dagli artwork del gioco"), TextWrapping = TextWrapping.Wrap };
        var useHero = new Button { Content = ImportText("Use the current background", "Usa lo sfondo attuale"), IsEnabled = plan is not null && artwork.ContainsKey("hero") };
        useHero.Click += (_, _) => { curtain["fullscreen_image_path"] = artwork["hero"]; ToolTipService.SetToolTip(background, artwork["hero"]); PreviewBackground(artwork["hero"]); saves?.FieldsChanged(); };
        var browseBackground = new Button { Content = ImportText("Choose background", "Scegli sfondo"), IsEnabled = plan is not null };
        browseBackground.Click += async (_, _) => { var path = await PickFileAsync(new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" }); if (!string.IsNullOrWhiteSpace(path)) { curtain["fullscreen_image_path"] = path; ToolTipService.SetToolTip(background, path); PreviewBackground(path); saves?.FieldsChanged(); } };
        panels["launch-curtain"].Children.Add(background); panels["launch-curtain"].Children.Add(useHero); panels["launch-curtain"].Children.Add(browseBackground);
        var curtainForm = new StackPanel { Spacing = 14 };
        var curtainGeometry = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        curtainGeometry.ColumnDefinitions.Add(new ColumnDefinition()); curtainGeometry.ColumnDefinitions.Add(new ColumnDefinition());
        var advancedContent = new StackPanel { Spacing = 12 };
        var geometryRow = 0;
        foreach (var pair in curtainControls)
        {
            panels["launch-curtain"].Children.Remove(pair.Value);
            if (pair.Key is "logo_zoom_enabled" or "bg_zoom_enabled" or "timeout_enabled" or "timeout_seconds") advancedContent.Children.Add(pair.Value);
            else if (pair.Key is "logo_scale" or "logo_position_x" or "logo_position_y" or "background_opacity") continue;
            else if (pair.Value is NumberBox)
            {
                if (geometryRow % 2 == 0) curtainGeometry.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(pair.Value, geometryRow / 2); Grid.SetColumn(pair.Value, geometryRow % 2); curtainGeometry.Children.Add(pair.Value); geometryRow++;
            }
            else curtainForm.Children.Add(pair.Value);
        }
        curtainForm.Children.Add(curtainGeometry);
        curtainForm.Children.Add(new Expander { Header = T("Animazioni e durata"), Content = advancedContent, HorizontalAlignment = HorizontalAlignment.Stretch });
        var backgroundChoices = new StackPanel { Spacing = 12 }; backgroundChoices.Children.Add(curtainPreview);
        foreach (var control in panels["launch-curtain"].Children.ToArray()) { panels["launch-curtain"].Children.Remove(control); backgroundChoices.Children.Add(control); }
        var curtainSearchTitle = new TextBox { Header = T("Titolo da cercare"), Text = game.Title };
        var curtainProvider = new ComboBox { Header = T("Fonte"), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var provider in new[] { "PlayStation", "IGDB", "AlphaCoders", "Nintendo", "Xbox", "iiDB", "SteamGridDB" }) curtainProvider.Items.Add(provider);
        curtainProvider.SelectedIndex = 0;
        var findBackground = new Button { Content = T("Cerca sfondi"), IsEnabled = plan is not null };
        var curtainResults = new StackPanel { Spacing = 12 };
        var curtainSearchRow=new Grid { ColumnSpacing=12 };
        curtainSearchRow.ColumnDefinitions.Add(new ColumnDefinition());curtainSearchRow.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(240) });
        curtainSearchRow.Children.Add(curtainSearchTitle);Grid.SetColumn(curtainProvider,1);curtainSearchRow.Children.Add(curtainProvider);
        backgroundChoices.Children.Add(curtainSearchRow);backgroundChoices.Children.Add(findBackground);backgroundChoices.Children.Add(curtainResults);

        findBackground.Click += async (_, _) =>
        {
            findBackground.IsEnabled = false; curtainResults.Children.Clear();
            try
            {
                var response = await ImportCurtainSearch.SearchAsync(bridge.CallAsync, plan!.AppId, curtainSearchTitle.Text.Trim(), curtainProvider.SelectedItem!.ToString()!.ToLowerInvariant(), lifetime.Token,bridge.IsAppOwned);
                lifetime.Token.ThrowIfCancellationRequested();
                if (response?["ok"]?.GetValue<bool>() != true) throw new InvalidOperationException();
                var selectedFrames=new List<Border>();
                foreach (var result in (response["results"] as JsonArray ?? new()).OfType<JsonObject>())
                {
                    var thumb = result["thumbnail_url"]?.GetValue<string>() ?? result["image_url"]?.GetValue<string>();
                    var choice = new StackPanel { Spacing = 8 };
                    if (!Uri.TryCreate(thumb, UriKind.Absolute, out var imageUri) || imageUri.Scheme != "https") continue;
                    var thumbnail=new Border { BorderThickness=new Thickness(3),BorderBrush=new SolidColorBrush(Microsoft.UI.Colors.Transparent),CornerRadius=new CornerRadius(8),Child=new Image { Source=new BitmapImage(imageUri),Height=140,Stretch=Stretch.Uniform } };
                    selectedFrames.Add(thumbnail);
                    choice.Children.Add(new TextBlock { Text = result["source"]?.GetValue<string>() ?? "", Opacity = .7 });
                    var choose = new Button { Content=thumbnail,Padding=new Thickness(0),HorizontalAlignment=HorizontalAlignment.Stretch };
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(choose,T("Sfondo"));
                    choose.Click += async (_, _) =>
                    {
                        choose.IsEnabled=false;
                        try { await saves!.ChangeAsync(async () =>
                        {
                        var saved = await ImportCurtainSearch.ChooseAsync(bridge.CallAsync, plan.AppId, game.Title, result, CancellationToken.None,bridge.IsAppOwned);
                        if (!session.IsClosed) { PreviewBackground(saved["path"]!.GetValue<string>());foreach(var frame in selectedFrames)frame.BorderBrush=new SolidColorBrush(ReferenceEquals(frame,thumbnail)?Microsoft.UI.Colors.Gold:Microsoft.UI.Colors.Transparent); }
                        }); }
                        finally { if(!session.IsClosed)choose.IsEnabled=true; }
                    };
                    choice.Children.Insert(0,choose); curtainResults.Children.Add(choice);
                }
                if (curtainResults.Children.Count == 0) curtainResults.Children.Add(new TextBlock { Text = T("Nessun risultato trovato.") });
            }
            catch (OperationCanceledException) { }
            catch { if (!session.IsClosed) notice.Text = T("Ricerca non disponibile. Riprova."); }
            finally { if (!session.IsClosed) findBackground.IsEnabled = plan is not null; }
        };
        backgroundChoices.Children.Remove(curtainPreview);
        panels["launch-curtain"].Children.Add(curtainPreview);
        panels["launch-curtain"].Children.Add(curtainForm);
        panels["launch-curtain"].Children.Add(backgroundChoices);
        RefreshCurtainPreview();
        async Task ReadSettingsAsync()
        {
            if (plan is null) return;
            using var reading = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            reading.CancelAfter(TimeSpan.FromSeconds(15));
            void Missing(string category) => panels[category].Children.Add(new TextBlock { Text = ImportText("Current settings unavailable. Open Steam and check the plugin.", "Impostazioni attuali non disponibili. Apri Steam e controlla il plugin."), TextWrapping = TextWrapping.Wrap, Opacity = .7 });
            try { metadata = await bridge.CallAsync("Playhub Metadata", "get_metadata", new JsonArray(plan.AppId), reading.Token) as JsonObject ?? new(); reading.Token.ThrowIfCancellationRequested(); RenderMetadata(metadata); }
            catch (OperationCanceledException) when (lifetime.Token.IsCancellationRequested) { return; } catch { Missing("metadata"); }
            try
            {
                var track = await editor.MusicAsync(new(plan.AppId), reading.Token);
                reading.Token.ThrowIfCancellationRequested();
                loading = true; volume.Value = (track?["volume"]?.GetValue<double>() ?? 1) * 100; offset.Value = track?["start_offset"]?.GetValue<double>() ?? 0; loop.IsOn = track?["loop"]?.GetValue<bool>() ?? true; loading = false;
                var path = track?["path"]?.GetValue<string>();
                currentMedia["themedeck"].Text = string.IsNullOrWhiteSpace(path) ? T("Nessuna traccia salvata.") : MediaDisplayName.Resolve(track?["title"]?.GetValue<string>(),path);
                if (File.Exists(path))
                {
                    var player = await CreateLocalPlayerAsync(new Uri(path!),96);
                    currentMusicPlayer = player;
                    SetMediaPreview("themedeck",player,96);
                }
            }
            catch (OperationCanceledException) when (lifetime.Token.IsCancellationRequested) { return; } catch { loading = false; Missing("themedeck"); }
            try
            {
                var trailer = await bridge.CallAsync("TrailerHero", "get_local_trailer", new JsonArray(plan.AppId), reading.Token);
                reading.Token.ThrowIfCancellationRequested();
                var trailerPath = trailer?["videoUrl"]?.GetValue<string>();
                var streaming = await bridge.ReadStreamingTrailerAsync(plan.AppId, reading.Token);
                reading.Token.ThrowIfCancellationRequested();
                var videoId = streaming?["videoId"]?.GetValue<string>();
                if (videoId is not null && streaming?["preferredSource"]?.GetValue<string>() == "youtube")
                {
                    currentMedia["trailerhero"].Text = "YouTube · " + videoId;
                    var thumbnail = new Image { Source = new BitmapImage(new Uri("https://i.ytimg.com/vi/" + videoId + "/hqdefault.jpg")), Height = 236, Stretch = Stretch.Uniform };
                    SetMediaPreview("trailerhero",thumbnail,236);
                    var playCurrent = new Button { Content = T("Riproduci anteprima") };
                    playCurrent.Click += async (_, _) => await PlayYoutubeAsync("trailerhero",videoId);
                    panels["trailerhero"].Children.Insert(1, playCurrent);
                }
                else if (trailer?["assigned"]?.GetValue<bool>() == true && Uri.TryCreate(trailerPath, UriKind.Absolute, out var localTrailerUri) && (localTrailerUri.IsLoopback || localTrailerUri.IsFile && File.Exists(localTrailerUri.LocalPath)))
                {
                    currentMedia["trailerhero"].Text = trailer?["title"]?.GetValue<string>() ?? T("Trailer locale");
                    var trailerPlayer = await CreateLocalPlayerAsync(localTrailerUri,320);
                    SetMediaPreview("trailerhero",trailerPlayer,320);
                }
                else currentMedia["trailerhero"].Text = T("Nessun trailer salvato.");
            }
            catch (OperationCanceledException) when (lifetime.IsClosed) { return; }
            catch { currentMedia["trailerhero"].Text = T("Impostazioni attuali non disponibili. Apri Steam e controlla il plugin."); }
            try
            {
                var saved = await editor.CurtainAsync(new(plan.AppId), reading.Token);
                reading.Token.ThrowIfCancellationRequested();
                var values = saved?["resolved"] as JsonObject ?? saved?["settings"] as JsonObject ?? new(); loading = true;
                foreach(var field in values)curtainVisualSettings[field.Key]=field.Value?.DeepClone();
                foreach (var (key, control) in curtainControls) if (values[key] is JsonValue value)
                { if (control is ToggleSwitch toggle && value.TryGetValue<bool>(out var on)) toggle.IsOn = on; if (control is NumberBox number && value.TryGetValue<double>(out var n)) number.Value = n; }
                loading = false; PreviewBackground(values["fullscreen_image_path"]?.GetValue<string>() ?? artwork.GetValueOrDefault("hero")); RefreshCurtainPreview();
            }
            catch (OperationCanceledException) when (lifetime.Token.IsCancellationRequested) { } catch { loading = false; Missing("launch-curtain"); }
        }
        Task settingsRead = Task.CompletedTask;
        dialog.Opened += (_, _) =>
        {

            foreach (var category in new[] { "metadata", "themedeck", "launch-curtain" }) sections[category].IsEnabled = false;
            async Task InitializeAsync()
            {
                try { await ReadSettingsAsync(); }
                finally
                {
                    if (!lifetime.Token.IsCancellationRequested)
                    {
                        foreach (var category in new[] { "metadata", "themedeck", "launch-curtain" }) sections[category].IsEnabled = true;

                    }
                }
            }
            settingsRead = InitializeAsync();
        };
        async Task SaveFieldsAsync()
        {
            if (plan is null || metadataEdits.Count + music.Count + curtain.Count == 0) return;
            var detailsSnapshot = (JsonObject)metadataEdits.DeepClone();
            var musicSnapshot = (JsonObject)music.DeepClone();
            var curtainSnapshot = (JsonObject)curtain.DeepClone();
            await new ImportIntegrationEdits(bridge.CallAsync,bridge.IsAppOwned).SaveAsync(plan.AppId, detailsSnapshot, musicSnapshot, curtainSnapshot, CancellationToken.None);
            ImportIntegrationEdits.ClearConfirmedFields(metadataEdits, detailsSnapshot);
            ImportIntegrationEdits.ClearConfirmedFields(music, musicSnapshot);
            ImportIntegrationEdits.ClearConfirmedFields(curtain, curtainSnapshot);
        }
        saves = new ImportInfoSaveQueue(SaveFieldsAsync, state =>
        {
            if (session.IsClosed) return;
            notice.Visibility = state == "error" || state == "saving" ? Visibility.Visible : Visibility.Collapsed;
            notice.Text = state switch { "saving" => T("Salvataggio…"), "saved" => T("Salvato"), _ => T("Salvataggio non riuscito. Riprova.") };
        }, hasPendingFields: () => metadataEdits.Count + music.Count + curtain.Count > 0);
        dialog.Closing += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            tabs.IsEnabled = false; bodyScroll.IsEnabled = false;
            try
            {
                curtainVisual.CommitPendingChanges();
                await saves.FlushAsync();
                if (saves.HasFailure && metadataEdits.Count + music.Count + curtain.Count > 0)
                { args.Cancel = true; tabs.IsEnabled = true; bodyScroll.IsEnabled = true; }
            }
            finally { deferral.Complete(); }
        };
        try { ConfigureDialogEntrance(dialog); await dialog.ShowAsync(); }
        finally { lifetime.Cancel(); await settingsRead; await saves.FlushAsync(); }

    }
}

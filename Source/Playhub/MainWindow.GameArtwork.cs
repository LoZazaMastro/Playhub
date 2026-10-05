using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Playhub.Importing;
using Playhub.Integrations;
using Playhub.Emulation.Workbench;
using Playhub.Services;
using System.Text.Json.Nodes;

namespace Playhub;

public sealed partial class MainWindow
{
    internal Task ShowGameArtworkAsync(GameInfoContext game) => ShowGameArtworkCoreAsync(game,"cover");
    private async Task ShowGameArtworkCoreAsync(GameInfoContext game,string initialType)
    {
        using var session=new ImportInfoSession();
        var current=new Dictionary<string,string>(await game.ReadArtworkAsync(session.Token));
        var tabs=new SelectorBar();
        var types=new[]{("cover",T("Copertina")),("banner","Banner"),("hero",T("Sfondo")),("logo","Logo"),("icon",T("Icona")),("summary",T("Riepilogo"))};
        foreach(var (type,label) in types) tabs.Items.Add(new SelectorBarItem { Text=label,Tag=type });
        var status=new TextBlock { Visibility=Visibility.Collapsed,TextWrapping=TextWrapping.Wrap,Opacity=.7 };
        var panel=new StackPanel { Spacing=14,HorizontalAlignment=HorizontalAlignment.Stretch };
        var scroll=new ScrollViewer { Content=panel,Padding=new Thickness(0,0,24,32),HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
        var body=new Grid { RowSpacing=12 };
        body.RowDefinitions.Add(new(){Height=GridLength.Auto});body.RowDefinitions.Add(new(){Height=GridLength.Auto});body.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        body.Children.Add(tabs);Grid.SetRow(status,1);body.Children.Add(status);Grid.SetRow(scroll,2);body.Children.Add(scroll);
        var dialog=new ContentDialog { Title=string.Format(T("Artwork - {0}"),game.Title),Content=body,CloseButtonText=T("Chiudi"),XamlRoot=Content.XamlRoot,Tag="noloc" };
        void Resize() { var size=ImportInfoSize.ForRoot(dialog.XamlRoot.Size.Width,dialog.XamlRoot.Size.Height);body.Width=size.Width;body.Height=size.Height;dialog.Resources["ContentDialogMinWidth"]=size.Width+48;dialog.Resources["ContentDialogMaxWidth"]=size.Width+48; }
        void RootChanged(XamlRoot _,XamlRootChangedEventArgs __)=>Resize();
        Resize();dialog.XamlRoot.Changed+=RootChanged;session.Register(()=>dialog.XamlRoot.Changed-=RootChanged);
        var saves=new ImportInfoSaveQueue(()=>Task.CompletedTask,state=> { if(session.IsClosed)return;status.Visibility=state=="error"||state=="saving"?Visibility.Visible:Visibility.Collapsed;status.Text=state=="saving"?T("Salvataggio…"):T("Salvataggio non riuscito. Riprova."); },hasPendingFields:()=>false);
        string active=initialType;
        string artworkSearchTitle=await GameTitleQuery.ReadAsync(new ApplicationIntegrationDataStore(Path.Combine(AppPaths.LocalDataRoot,"integrations")),game.StableIdentity,"artwork",game.Title,session.Token);
        string? pendingPerfect=null;int generation=0;
        UIElement Picture(string type,string? path,double height)
        {
            var border=new Border { Height=File.Exists(path)?height:72,CornerRadius=new CornerRadius(8),Background=ResourceBrush("CardBackgroundFillColorDefaultBrush",Microsoft.UI.Colors.Transparent) };
            if(File.Exists(path))
            {
                var image=new Image { Stretch=Stretch.Uniform };border.Child=image;
                _=LoadPictureAsync(image,border,path!);
            }
            else border.Child=new TextBlock { Text=T("Non presente"),Opacity=.65,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center };
            return border;
        }
        async Task LoadPictureAsync(Image image,Border border,string path)
        {
            try
            {
                var data=await ArtworkEditorImageSource.ReadAsync(path,session.Token);
                using var stream=await ArtworkEditorImageSource.StreamAsync(data.Bytes,session.Token);
                var bitmap=new BitmapImage { DecodePixelWidth=(int)Math.Min(data.Width,1600u) };
                await bitmap.SetSourceAsync(stream).AsTask(session.Token);
                if(!session.IsClosed&&ReferenceEquals(border.Child,image))image.Source=bitmap;
            }
            catch(OperationCanceledException){}
            catch(Exception error)
            {
                Diag.Step("Artwork current preview failed: "+error.GetType().Name);
                if(!session.IsClosed&&ReferenceEquals(border.Child,image))border.Child=new TextBlock { Text=T("Ricerca non disponibile. Riprova."),TextWrapping=TextWrapping.Wrap,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center };
            }
        }
        async Task RefreshAsync() { current=new(await game.ReadArtworkAsync(session.Token));if(!session.IsClosed) Render(); }
        async Task ChooseAsync(string type,ImportArtworkResult option,bool render = true)
        {
            await game.ChooseArtworkAsync(type,option,CancellationToken.None);
            if(type is "hero" or "banner")
                if(!await ApplySelectedArtworkLogoRuleAsync(game,type))await RestorePlainArtworkLogoAsync(game);
            current=new(await game.ReadArtworkAsync(session.Token));
            if(render&&!session.IsClosed)Render();
        }
        void Render()
        {
            generation++;panel.Children.Clear();
            if(active=="summary")
            {
                var summary=new GridView { SelectionMode=ListViewSelectionMode.None,IsItemClickEnabled=true };
                foreach(var (summaryType,label) in types.Where(t=>t.Item1!="summary"))
                {
                    var card=new StackPanel { Width=280,Spacing=8 };card.Children.Add(Picture(summaryType,current.GetValueOrDefault(summaryType),summaryType=="cover"?210:140));card.Children.Add(new TextBlock { Text=label });
                    var summaryActions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
                    var summaryBrowse=new Button{Content=T("Sfoglia")};
                    summaryBrowse.Click+=async(_,_)=>{var path=await PickFileAsync(new[]{".png",".jpg",".jpeg",".webp"});if(!session.IsClosed&&!string.IsNullOrEmpty(path))await saves.ChangeAsync(()=>ChooseAsync(summaryType,new(path,path,0,0)));};summaryActions.Children.Add(summaryBrowse);
                    var summaryRemove=new Button{Content=T("Rimuovi"),IsEnabled=game.RemoveArtworkAsync is not null&&current.ContainsKey(summaryType)};
                    summaryRemove.Click+=async(_,_)=>await saves.ChangeAsync(async()=>{await game.RemoveArtworkAsync!(summaryType,CancellationToken.None);if(summaryType is "hero" or "banner")await RestorePlainArtworkLogoAsync(game);await RefreshAsync();});summaryActions.Children.Add(summaryRemove);card.Children.Add(summaryActions);
                    summary.Items.Add(new GridViewItem { Content=card,Tag=summaryType,Padding=new Thickness(8) });
                }
                summary.ItemClick+=(_,args)=>{ if((args.ClickedItem as GridViewItem)?.Tag is string summaryType) tabs.SelectedItem=tabs.Items.First(t=>Equals(t.Tag,summaryType)); };
                panel.Children.Add(summary);return;
            }
            string type=active;int revision=generation;
            panel.Children.Add(new TextBlock { Text=T("Attuale"),FontSize=18,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold });
            double currentHeight=type=="cover"?260:type is "logo" or "icon"?160:220;
            var currentPicture=new Grid();currentPicture.Children.Add(Picture(type,current.GetValueOrDefault(type),currentHeight));panel.Children.Add(currentPicture);
            var actions=new StackPanel { Orientation=Orientation.Horizontal,Spacing=12 };
            var browse=new Button { Content=T("Sfoglia") };
            browse.Click+=async(_,_)=>{var path=await PickFileAsync(new[]{".png",".jpg",".jpeg",".webp"});if(!session.IsClosed&&!string.IsNullOrEmpty(path))await saves.ChangeAsync(()=>ChooseAsync(type,new(path,path,0,0)));};actions.Children.Add(browse);
            var remove=new Button { Content=T("Rimuovi"),IsEnabled=game.RemoveArtworkAsync is not null && current.ContainsKey(type) };
            remove.Click+=async(_,_)=>await saves.ChangeAsync(async()=>{await game.RemoveArtworkAsync!(type,CancellationToken.None);if(type is "hero" or "banner")await RestorePlainArtworkLogoAsync(game);await RefreshAsync();});actions.Children.Add(remove);
            if(type is "hero" or "banner")
            {
                var perfect=new Button { Content=type=="hero"?"Perfect Hero":"Perfect Banner",IsEnabled=game.StableIdentity is not null && current.ContainsKey(type)&&current.ContainsKey("logo") };
                perfect.Click+=(_,_)=> { pendingPerfect=type;dialog.Hide(); };
                actions.Children.Insert(0,perfect);
            }
            panel.Children.Add(actions);
            if(type is "hero" or "banner")panel.Children.Add(new TextBlock { Text=T(type=="hero"?"Emulation.Artwork.PerfectHero.Description":"Emulation.Artwork.PerfectBanner.Description"),TextWrapping=TextWrapping.Wrap,Opacity=.75 });
            var search=new Grid { ColumnSpacing=12 };search.ColumnDefinitions.Add(new());search.ColumnDefinitions.Add(new(){Width=new GridLength(240)});
            var title=new TextBox { Header=T("Titolo da cercare"),Text=artworkSearchTitle };
            title.TextChanged+=(_,_)=>artworkSearchTitle=title.Text;
            var provider=new ComboBox { Header=T("Fonte"),HorizontalAlignment=HorizontalAlignment.Stretch };
            foreach(string name in new[]{"SteamGridDB","Steam","PlayStation","Nintendo","Xbox","IGDB","AlphaCoders","iiDB","IGN"})
                if(name=="Steam"||ApplicationArtworkPreferences.Supports(name.ToLowerInvariant(),type))provider.Items.Add(name);
            string requested=game.DefaultArtworkProvider?.Invoke(type)??"SteamGridDB";
            provider.SelectedItem=provider.Items.OfType<string>().FirstOrDefault(name=>name.Equals(requested,StringComparison.OrdinalIgnoreCase))??"SteamGridDB";
            search.Children.Add(title);Grid.SetColumn(provider,1);search.Children.Add(provider);panel.Children.Add(search);
            var find=new Button { Content=T("Cerca alternative") };panel.Children.Add(find);
            var results=new GridView { SelectionMode=ListViewSelectionMode.None,IsItemClickEnabled=false };panel.Children.Add(results);
            find.Click+=async(_,_)=>
            {
                find.IsEnabled=false;results.Items.Clear();status.Text=T("Ricerca in corso…");status.Visibility=Visibility.Visible;
                try
                {
                    var candidates=await game.SearchStoreArtworkAsync(provider.SelectedItem?.ToString()??"SteamGridDB",title.Text.Trim(),type,session.Token);
                    if(session.IsClosed||generation!=revision)return;
                    var selectedFrames=new List<Border>();
                    foreach(var candidate in candidates)
                    {
                        var card=new StackPanel { Width=280,Spacing=8 };
                        var thumbnail=new Border { BorderThickness=new Thickness(3),BorderBrush=new SolidColorBrush(Microsoft.UI.Colors.Transparent),CornerRadius=new CornerRadius(8),Child=new Image { Source=new BitmapImage(new Uri(candidate.Preview)),Height=type=="cover"?220:140,Stretch=Stretch.Uniform } };
                        selectedFrames.Add(thumbnail);
                        var choose=new Button { Content=thumbnail,Padding=new Thickness(0),HorizontalAlignment=HorizontalAlignment.Stretch };
                        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(choose,T("Artwork - {0}").Replace("{0}",game.Title));
                        choose.Click+=async(_,_)=>
                        {
                            choose.IsEnabled=false;
                            try { await saves.ChangeAsync(async()=>
                            {
                                await ChooseAsync(type,candidate,false);
                                if(session.IsClosed||generation!=revision)return;
                                currentPicture.Children.Clear();currentPicture.Children.Add(Picture(type,current.GetValueOrDefault(type),currentHeight));remove.IsEnabled=game.RemoveArtworkAsync is not null&&current.ContainsKey(type);
                                foreach(var frame in selectedFrames)frame.BorderBrush=new SolidColorBrush(ReferenceEquals(frame,thumbnail)?Microsoft.UI.Colors.Gold:Microsoft.UI.Colors.Transparent);
                            }); }
                            finally { if(!session.IsClosed)choose.IsEnabled=true; }
                        };
                        card.Children.Add(choose);
                        card.Children.Add(new TextBlock { Text=candidate.Width>0?$"{candidate.Width} × {candidate.Height}":T("Dimensione originale"),Opacity=.7 });
                        results.Items.Add(new GridViewItem { Content=card,Padding=new Thickness(8) });
                    }
                    status.Text=candidates.Count==0?T("Nessun risultato trovato."):"";status.Visibility=candidates.Count==0?Visibility.Visible:Visibility.Collapsed;
                }
                catch(OperationCanceledException){}catch {if(!session.IsClosed){status.Text=T("Ricerca non disponibile. Riprova.");status.Visibility=Visibility.Visible;}}
                finally {if(!session.IsClosed)find.IsEnabled=true;}
            };
        }
        tabs.SelectionChanged+=(_,_)=>{active=tabs.SelectedItem?.Tag?.ToString()??"cover";Render();};tabs.SelectedItem=tabs.Items.First(t=>Equals(t.Tag,initialType));
        dialog.Closing+=async(_,args)=>{var deferral=args.GetDeferral();tabs.IsEnabled=false;scroll.IsEnabled=false;try{await saves.FlushAsync();}finally{deferral.Complete();}};
        dialog.Closed+=(_,_)=>session.Dispose();
        ConfigureDialogEntrance(dialog);await dialog.ShowAsync();
        if(pendingPerfect is not null) { await ShowPerfectGameArtworkAsync(game,pendingPerfect);await ShowGameArtworkCoreAsync(game,pendingPerfect); }
    }

    private static bool ArtworkSteamRunning()
    {
        var processes=System.Diagnostics.Process.GetProcessesByName("steam");try{return processes.Length>0;}finally{foreach(var process in processes)process.Dispose();}
    }
    private async Task RestorePlainArtworkLogoAsync(GameInfoContext game)
    {
        if(game.StableIdentity is null)return;
        var store=new ApplicationIntegrationDataStore(Path.Combine(AppPaths.LocalDataRoot,"integrations"));
        var state=await store.ReadCategoryAsync(game.StableIdentity,"artwork");
        if(state["logoHiddenRequested"]?.GetValue<bool>()!=true)return;
        var actual=await game.ReadArtworkAsync(CancellationToken.None);
        foreach(var target in new[]{"hero","banner"})
            if(actual.TryGetValue(target,out var path)&&File.Exists(path)&&state["perfect_"+target]?["sha256"]?.ToString() is string hash&&
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(path))).Equals(hash,StringComparison.OrdinalIgnoreCase))return;
        var directories=game.GetSteamGridDirectoriesAsync is null?Array.Empty<string>():await game.GetSteamGridDirectoriesAsync(CancellationToken.None);
        await new PerfectArtworkState(store,ArtworkSteamRunning,SteamPluginBridge.EvaluateAsync).SetHiddenAsync(game.StableIdentity,game.ShortcutAppId,directories,false,CancellationToken.None);
    }
    private async Task<bool> ApplySelectedArtworkLogoRuleAsync(GameInfoContext game,string type)
    {
        if(game.StableIdentity is null)return false;
        var store=new ApplicationIntegrationDataStore(Path.Combine(AppPaths.LocalDataRoot,"integrations"));
        var state=await store.ReadCategoryAsync(game.StableIdentity,"artwork");
        bool? hidden=ArtworkLogoPolicy.ManualOverride(state);
        var source=game.ReadArtworkSourceAsync is null?null:await game.ReadArtworkSourceAsync(type,CancellationToken.None);
        if(type=="hero"&&source is not null)
        {
            var actual=await game.ReadArtworkAsync(CancellationToken.None);
            if(actual.TryGetValue(type,out var selected)&&Path.GetFullPath(selected).Equals(Path.GetFullPath(source.Url),StringComparison.OrdinalIgnoreCase)&&File.Exists(selected))
            {
                string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(selected)));
                await store.PatchAsync(game.StableIdentity,"artwork",new JsonObject { ["hero_source"]=new JsonObject{["path"]=selected,["sha256"]=hash,["provider"]=source.Provider,["authorName"]=source.AuthorName,["authorSteamId"]=source.AuthorSteamId} });
                if(ArtworkLogoPolicy.IsZazaHero(type,source.Provider,source.AuthorName,source.AuthorSteamId))hidden??=true;
            }
        }
        if(hidden is null)return false;
        var directories=game.GetSteamGridDirectoriesAsync is null?Array.Empty<string>():await game.GetSteamGridDirectoriesAsync(CancellationToken.None);
        string plugins=string.IsNullOrWhiteSpace(_settings.DeckyPluginsPath)?AppPaths.DefaultDeckyPluginsPath:_settings.DeckyPluginsPath;
        await new PerfectArtworkState(store,ArtworkSteamRunning,SteamPluginBridge.EvaluateAsync,plugins).SetHiddenAsync(game.StableIdentity,game.ShortcutAppId,directories,hidden.Value,CancellationToken.None);
        return true;
    }
}

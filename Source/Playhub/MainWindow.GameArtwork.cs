using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Playhub.Importing;
using Playhub.Integrations;
using Playhub.Services;
using System.Text.Json.Nodes;

namespace Playhub;

public sealed partial class MainWindow
{
    internal async Task ShowGameArtworkAsync(GameInfoContext game)
    {
        await ShowGameArtworkCoreAsync(game, "cover");
    }
    private async Task ShowGameArtworkCoreAsync(GameInfoContext game,string initialType)
    {
        using var session=new ImportInfoSession();
        var current=new Dictionary<string,string>(await game.ReadArtworkAsync(session.Token));
        var tabs=new SelectorBar { HorizontalAlignment=HorizontalAlignment.Stretch,MinWidth=0 };
        var types=new[]{("cover",T("Copertina")),("banner","Banner"),("hero",T("Sfondo")),("logo","Logo"),("icon",T("Icona")),("summary",T("Riepilogo"))};
        foreach(var (type,label) in types) tabs.Items.Add(new SelectorBarItem { Text=label,Tag=type });
        var status=new TextBlock { Visibility=Visibility.Collapsed,TextWrapping=TextWrapping.Wrap,Opacity=.7 };
        var panel=new StackPanel { Spacing=14,HorizontalAlignment=HorizontalAlignment.Stretch };
        var scroll=new ScrollViewer { Content=panel,Padding=new Thickness(0,0,24,32),HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
        var body=new Grid { RowSpacing=12 };
        body.RowDefinitions.Add(new(){Height=GridLength.Auto});body.RowDefinitions.Add(new(){Height=GridLength.Auto});body.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var header=new Grid { ColumnSpacing=12,RowSpacing=8 };
        header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });header.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        header.Children.Add(tabs);
        Button Action(string label)=>new() { Content=new TextBlock { Text=label,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center },MinWidth=0,Style=StyleResource("PlayhubSecondaryButtonStyle") };
        var refetchTitle=Action(T("Cerca di nuovo"));refetchTitle.VerticalAlignment=VerticalAlignment.Center;
        Grid.SetColumn(refetchTitle,1);header.Children.Add(refetchTitle);
        body.Children.Add(header);Grid.SetRow(status,1);body.Children.Add(status);Grid.SetRow(scroll,2);body.Children.Add(scroll);
        var dialogTitle=new TextBlock { Text=string.Format(T("Artwork - {0}"),game.Title),TextWrapping=TextWrapping.Wrap,HorizontalAlignment=HorizontalAlignment.Stretch };
        var dialog=new ContentDialog { Title=dialogTitle,Content=body,CloseButtonText=T("Chiudi"),XamlRoot=Content.XamlRoot,Tag="noloc" };
        Border? nativeBackground=null;ItemsView? nativeTabs=null;UniformGridLayout? tabLayout=null;
        var tabLabels=new Dictionary<SelectorBarItem,TextBlock>();
        bool resizeQueued=false;
        void FitHeader()
        {
            bool narrow=header.ActualWidth<760;
            Grid.SetColumn(refetchTitle,narrow?0:1);Grid.SetRow(refetchTitle,narrow?1:0);
            Grid.SetColumnSpan(tabs,narrow?2:1);
            refetchTitle.HorizontalAlignment=narrow?HorizontalAlignment.Right:HorizontalAlignment.Stretch;
            double available=Math.Max(0,header.ActualWidth-(narrow?0:refetchTitle.ActualWidth+12));
            if(available<=0)return;
            tabs.MaxWidth=available;
            // SelectorBar defaults to horizontally unconstrained scrolling.
            // Fit the bounded native viewport, including its template border,
            // rather than rounding six cells beyond the available content width.
            double contentWidth=Math.Max(0,available-tabs.Padding.Left-tabs.Padding.Right-tabs.BorderThickness.Left-tabs.BorderThickness.Right);
            if(nativeTabs?.ScrollView is { ViewportWidth: >0 } viewport)contentWidth=Math.Min(contentWidth,viewport.ViewportWidth);
            int columns=contentWidth>=660?6:contentWidth>=340?3:2;
            double scale=dialog.XamlRoot.RasterizationScale;
            double itemWidth=Math.Max(0,Math.Floor((contentWidth/columns-.5)*scale)/scale);
            double itemHeight=40;
            foreach(var item in tabs.Items)
            {
                item.MaxWidth=itemWidth;
                if(tabLabels.TryGetValue(item,out var label))label.MaxWidth=Math.Max(0,itemWidth-32);
                item.Measure(new Windows.Foundation.Size(itemWidth,double.PositiveInfinity));
                itemHeight=Math.Max(itemHeight,item.DesiredSize.Height);
            }
            if(tabLayout is not null){tabLayout.MaximumRowsOrColumns=columns;tabLayout.MinItemWidth=itemWidth;tabLayout.MinItemHeight=itemHeight;}
        }
        void CacheNativeElements()
        {
            tabs.ApplyTemplate();
            var pending=new Stack<DependencyObject>();pending.Push(tabs);
            while(pending.Count>0)
            {
                var item=pending.Pop();
                if(item is ItemsView { Name:"PART_ItemsView" } view)
                {
                    if(!ReferenceEquals(nativeTabs,view))
                    {
                        if(nativeTabs is not null)nativeTabs.SizeChanged-=NativeTabsChanged;
                        nativeTabs=view;nativeTabs.SizeChanged+=NativeTabsChanged;
                        tabLayout=new UniformGridLayout { Orientation=Orientation.Horizontal,MinRowSpacing=4,ItemsStretch=UniformGridLayoutItemsStretch.Fill };view.Layout=tabLayout;
                    }
                    view.ApplyTemplate();
                    if(view.ScrollView is { } viewport)
                    {
                        viewport.ContentOrientation=ScrollingContentOrientation.Vertical;
                        viewport.HorizontalScrollMode=ScrollingScrollMode.Disabled;
                        viewport.HorizontalScrollBarVisibility=ScrollingScrollBarVisibility.Hidden;
                        viewport.VerticalScrollMode=ScrollingScrollMode.Disabled;
                        viewport.VerticalScrollBarVisibility=ScrollingScrollBarVisibility.Hidden;
                    }
                }
                if(item is TextBlock { Name:"PART_TextVisual" } text && text.Parent is FrameworkElement)
                {
                    DependencyObject? parent=text;
                    while(parent is not null && parent is not SelectorBarItem)parent=VisualTreeHelper.GetParent(parent);
                    if(parent is SelectorBarItem tab){text.TextWrapping=TextWrapping.Wrap;tabLabels[tab]=text;}
                }
                for(int index=0;index<VisualTreeHelper.GetChildrenCount(item);index++)pending.Push(VisualTreeHelper.GetChild(item,index));
            }
        }
        void Resize()
        {
            if(session.IsClosed)return;
            var size=ImportInfoSize.ForRoot(dialog.XamlRoot.Size.Width,dialog.XamlRoot.Size.Height);
            body.Width=size.Width;body.Height=size.Height;
            // ContentDialog measures Title before its body. Bound it explicitly,
            // including the initial measure, so long game names cannot widen the popup.
            dialogTitle.Width=size.Width;dialogTitle.MaxWidth=size.Width;
            dialog.Resources["ContentDialogMinWidth"]=size.Width+48;dialog.Resources["ContentDialogMaxWidth"]=size.Width+48;
            dialog.Resources["ContentDialogMinHeight"]=0d;dialog.Resources["ContentDialogMaxHeight"]=Math.Max(0,dialog.XamlRoot.Size.Height-48);
            nativeBackground=NativeContentDialogLayout.ResizeSurface(dialog,size.Width+48,Math.Max(0,dialog.XamlRoot.Size.Height-48));
            CacheNativeElements();
            FitHeader();body.InvalidateMeasure();dialog.InvalidateMeasure();
        }
        void QueueResize()
        {
            if(session.IsClosed||resizeQueued)return;resizeQueued=true;
            body.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,()=>{resizeQueued=false;Resize();});
        }
        void RootChanged(XamlRoot _,XamlRootChangedEventArgs __)=>QueueResize();
        void ContentChanged(object _,SizeChangedEventArgs __)=>QueueResize();
        void HeaderChanged(object _,SizeChangedEventArgs __)=>FitHeader();
        void NativeTabsChanged(object _,SizeChangedEventArgs __)=>QueueResize();
        void TabsLoaded(object _,RoutedEventArgs __)=>QueueResize();
        var windowContent=Content as FrameworkElement;
        Resize();dialog.XamlRoot.Changed+=RootChanged;header.SizeChanged+=HeaderChanged;tabs.Loaded+=TabsLoaded;
        if(windowContent is not null)windowContent.SizeChanged+=ContentChanged;
        dialog.Opened+=(_,_)=>QueueResize();
        session.Register(()=>{dialog.XamlRoot.Changed-=RootChanged;header.SizeChanged-=HeaderChanged;tabs.Loaded-=TabsLoaded;if(nativeTabs is not null)nativeTabs.SizeChanged-=NativeTabsChanged;if(windowContent is not null)windowContent.SizeChanged-=ContentChanged;nativeBackground=null;nativeTabs=null;tabLayout=null;tabLabels.Clear();});
        var saves=new ImportInfoSaveQueue(()=>Task.CompletedTask,state=> { if(session.IsClosed)return;status.Visibility=state=="error"||state=="saving"?Visibility.Visible:Visibility.Collapsed;status.Text=state=="saving"?T("Salvataggio…"):T("Salvataggio non riuscito. Riprova."); },hasPendingFields:()=>false);
        string active=initialType;
        string artworkSearchTitle=game.ReadTitleQueryAsync is not null ? await game.ReadTitleQueryAsync("steamgriddb",session.Token)
            : await GameTitleQuery.ReadAsync(new ApplicationIntegrationDataStore(Path.Combine(AppPaths.LocalDataRoot,"integrations")),game.StableIdentity,"artwork",game.Title,session.Token);
        string? pendingPerfect=null;int generation=0;
        Grid Tiles(out Action arrange)
        {
            var grid=new Grid { ColumnSpacing=12,RowSpacing=12,HorizontalAlignment=HorizontalAlignment.Stretch };
            void Arrange()
            {
                double width=grid.ActualWidth;
                if(width<=0)return;
                int columns=Math.Max(1,(int)Math.Floor((width+12)/252));
                int rows=(grid.Children.Count+columns-1)/columns;
                if(grid.ColumnDefinitions.Count!=columns||grid.RowDefinitions.Count!=rows)
                {
                    grid.ColumnDefinitions.Clear();grid.RowDefinitions.Clear();
                    for(int index=0;index<columns;index++)grid.ColumnDefinitions.Add(new());
                    for(int index=0;index<rows;index++)grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
                }
                for(int index=0;index<grid.Children.Count;index++){var tile=(FrameworkElement)grid.Children[index];Grid.SetColumn(tile,index%columns);Grid.SetRow(tile,index/columns);}
            }
            grid.SizeChanged+=(_,_)=>Arrange();
            arrange=Arrange;
            return grid;
        }
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
                var summary=Tiles(out var arrangeSummary);
                foreach(var (summaryType,label) in types.Where(t=>t.Item1!="summary"))
                {
                    var card=new StackPanel { Spacing=8,HorizontalAlignment=HorizontalAlignment.Stretch };
                    var asset=new Button { Content=Picture(summaryType,current.GetValueOrDefault(summaryType),summaryType=="cover"?210:140),Padding=new Thickness(0),MinWidth=0,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch };
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(asset,label);
                    asset.Click+=(_,_)=>tabs.SelectedItem=tabs.Items.First(t=>Equals(t.Tag,summaryType));
                    card.Children.Add(asset);card.Children.Add(new TextBlock { Text=label,TextWrapping=TextWrapping.Wrap });
                    var summaryActions=new AdaptiveImportToolbar();
                    var summaryBrowse=Action(T("Sfoglia"));
                    summaryBrowse.Click+=async(_,_)=>{var path=await PickFileAsync(new[]{".png",".jpg",".jpeg",".webp"});if(!session.IsClosed&&!string.IsNullOrEmpty(path))await saves.ChangeAsync(()=>ChooseAsync(summaryType,new(path,path,0,0)));};summaryActions.Children.Add(summaryBrowse);
                    var summaryRemove=Action(T("Rimuovi"));summaryRemove.IsEnabled=game.RemoveArtworkAsync is not null&&current.ContainsKey(summaryType);
                    summaryRemove.Click+=async(_,_)=>await saves.ChangeAsync(async()=>{await game.RemoveArtworkAsync!(summaryType,CancellationToken.None);if(summaryType is "hero" or "banner")await RestorePlainArtworkLogoAsync(game);await RefreshAsync();});summaryActions.Children.Add(summaryRemove);card.Children.Add(summaryActions);
                    summary.Children.Add(new Border { Child=card,Padding=new Thickness(12),Style=StyleResource("PlayhubCardBorderStyle"),HorizontalAlignment=HorizontalAlignment.Stretch });
                }
                arrangeSummary();panel.Children.Add(summary);return;
            }
            string type=active;int revision=generation;
            panel.Children.Add(new TextBlock { Text=T("Attuale"),FontSize=18,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold });
            double currentHeight=type=="cover"?260:type is "logo" or "icon"?160:220;
            var currentPicture=new Grid();currentPicture.Children.Add(Picture(type,current.GetValueOrDefault(type),currentHeight));panel.Children.Add(currentPicture);
            var actions=new AdaptiveImportToolbar();
            var browse=Action(T("Sfoglia"));
            browse.Click+=async(_,_)=>{var path=await PickFileAsync(new[]{".png",".jpg",".jpeg",".webp"});if(!session.IsClosed&&!string.IsNullOrEmpty(path))await saves.ChangeAsync(()=>ChooseAsync(type,new(path,path,0,0)));};actions.Children.Add(browse);
            var remove=Action(T("Rimuovi"));remove.IsEnabled=game.RemoveArtworkAsync is not null && current.ContainsKey(type);
            remove.Click+=async(_,_)=>await saves.ChangeAsync(async()=>{await game.RemoveArtworkAsync!(type,CancellationToken.None);if(type is "hero" or "banner")await RestorePlainArtworkLogoAsync(game);await RefreshAsync();});actions.Children.Add(remove);
            if(type is "hero" or "banner")
            {
                var perfect=Action(type=="hero"?"Perfect Hero":"Perfect Banner");perfect.IsEnabled=game.StableIdentity is not null && current.ContainsKey(type)&&current.ContainsKey("logo");
                perfect.Click+=(_,_)=> { pendingPerfect=type;dialog.Hide(); };
                actions.Children.Insert(0,perfect);
            }
            panel.Children.Add(actions);
            if(type is "hero" or "banner")panel.Children.Add(new TextBlock { Text=T(type=="hero"?"Emulation.Artwork.PerfectHero.Description":"Emulation.Artwork.PerfectBanner.Description"),TextWrapping=TextWrapping.Wrap,Opacity=.75 });
            var search=new Grid { ColumnSpacing=12,RowSpacing=12 };search.ColumnDefinitions.Add(new());search.ColumnDefinitions.Add(new(){Width=new GridLength(240)});
            search.RowDefinitions.Add(new(){Height=GridLength.Auto});search.RowDefinitions.Add(new(){Height=GridLength.Auto});
            var title=new TextBox { Header=new TextBlock { Text=T("Titolo da cercare"),TextWrapping=TextWrapping.Wrap },Text=artworkSearchTitle,MinWidth=0 };
            title.TextChanged+=(_,_)=>artworkSearchTitle=title.Text;
            var provider=new ComboBox { Header=new TextBlock { Text=T("Fonte"),TextWrapping=TextWrapping.Wrap },HorizontalAlignment=HorizontalAlignment.Stretch,MinWidth=0 };
            foreach(string name in new[]{"SteamGridDB","Steam","PlayStation","Nintendo","Xbox","IGDB","AlphaCoders","iiDB","IGN"})
                if(name=="Steam"||ApplicationArtworkPreferences.Supports(name.ToLowerInvariant(),type))provider.Items.Add(name);
            string requested=game.DefaultArtworkProvider?.Invoke(type)??"SteamGridDB";
            provider.SelectedItem=provider.Items.OfType<string>().FirstOrDefault(name=>name.Equals(requested,StringComparison.OrdinalIgnoreCase))??"SteamGridDB";
            search.Children.Add(title);Grid.SetColumn(provider,1);search.Children.Add(provider);panel.Children.Add(search);
            if (string.IsNullOrWhiteSpace(_settings.SteamGridDbApiKey))
            {
                var connection = new Border { Child = BuildSteamGridDbConnectionCard() };
                void ShowConnection() => connection.Visibility =
                    string.IsNullOrWhiteSpace(_settings.SteamGridDbApiKey) && Equals(provider.SelectedItem, "SteamGridDB")
                        ? Visibility.Visible : Visibility.Collapsed;
                provider.SelectionChanged += (_, _) => ShowConnection();
                ShowConnection(); panel.Children.Add(connection);
            }
            search.SizeChanged+=(_,_)=>
            {
                bool narrow=search.ActualWidth<560;
                search.ColumnDefinitions[1].Width=narrow?new GridLength(0):new GridLength(240);
                Grid.SetColumn(provider,narrow?0:1);Grid.SetRow(provider,narrow?1:0);
            };
            var find=Action(T("Cerca alternative"));panel.Children.Add(find);
            var results=Tiles(out var arrangeResults);panel.Children.Add(results);
            find.Click+=async(_,_)=>
            {
                if (Equals(provider.SelectedItem, "SteamGridDB") && string.IsNullOrWhiteSpace(_settings.SteamGridDbApiKey))
                {
                    status.Text = T("SteamGridDb.ConnectionHelp"); status.Visibility = Visibility.Visible; return;
                }
                find.IsEnabled=false;results.Children.Clear();results.RowDefinitions.Clear();status.Text=T("Ricerca in corso…");status.Visibility=Visibility.Visible;
                try
                {
                    var candidates=await game.SearchStoreArtworkAsync(provider.SelectedItem?.ToString()??"SteamGridDB",title.Text.Trim(),type,session.Token);
                    if(session.IsClosed||generation!=revision)return;
                    var selectedFrames=new List<Border>();
                    foreach(var candidate in candidates)
                    {
                        var card=new StackPanel { Spacing=8,HorizontalAlignment=HorizontalAlignment.Stretch };
                        var thumbnail=new Border { BorderThickness=new Thickness(3),BorderBrush=new SolidColorBrush(Microsoft.UI.Colors.Transparent),CornerRadius=new CornerRadius(8),Child=new Image { Source=new BitmapImage(new Uri(candidate.Preview)),Height=type=="cover"?220:140,Stretch=Stretch.Uniform } };
                        selectedFrames.Add(thumbnail);
                        var choose=new Button { Content=thumbnail,Padding=new Thickness(0),MinWidth=0,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch };
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
                        card.Children.Add(new TextBlock { Text=candidate.Width>0?$"{candidate.Width} × {candidate.Height}":T("Dimensione originale"),Opacity=.7,TextWrapping=TextWrapping.Wrap });
                        results.Children.Add(new Border { Child=card,Padding=new Thickness(8),HorizontalAlignment=HorizontalAlignment.Stretch });
                    }
                    arrangeResults();
                    status.Text=candidates.Count==0?T("Nessun risultato trovato."):"";status.Visibility=candidates.Count==0?Visibility.Visible:Visibility.Collapsed;
                }
                catch(OperationCanceledException){}catch {if(!session.IsClosed){status.Text=T("Ricerca non disponibile. Riprova.");status.Visibility=Visibility.Visible;}}
                finally {if(!session.IsClosed)find.IsEnabled=true;}
            };
        }
        var titlePicker=GameTitleIdentityPicker.Attach(refetchTitle,game,"steamgriddb",T,session.Token,async selected=>
        {
            if(selected.Provider=="steamgriddb")artworkSearchTitle=selected.Title;
            await RefreshAsync();
        },async()=>{await saves.FlushAsync();if(saves.HasFailure)throw new IOException("Pending artwork was not saved.");},
        () => BuildSteamGridDbConnectionCard(), () => !string.IsNullOrWhiteSpace(_settings.SteamGridDbApiKey));
        session.Register(titlePicker.Dispose);
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

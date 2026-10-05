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
    private async Task ShowPerfectGameArtworkAsync(GameInfoContext game,string target)
    {
        if(game.StableIdentity is null)return;
        using var session=new ImportInfoSession();
        string root=Path.Combine(AppPaths.LocalDataRoot,"integrations");
        string plugins=string.IsNullOrWhiteSpace(_settings.DeckyPluginsPath)?AppPaths.DefaultDeckyPluginsPath:_settings.DeckyPluginsPath;
        var store=new ApplicationIntegrationDataStore(root);
        var existing=await store.ReadCategoryAsync(game.StableIdentity,"artwork",session.Token);
        var artwork=new Dictionary<string,string>(await game.ReadArtworkAsync(session.Token));
        if(!artwork.TryGetValue(target,out string? background)||!artwork.TryGetValue("logo",out string? logo))return;
        string? legacy=await PerfectArtworkState.LegacyPristineAsync(plugins,game.ShortcutAppId,target,background,session.Token);
        string previewBackground=legacy??background;
        var previous=existing["perfect_"+target] as JsonObject;
        bool currentCompositionOutputValid=previous?["sha256"]?.ToString() is string confirmedHash && File.Exists(background) &&
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(background,session.Token))).Equals(confirmedHash,StringComparison.OrdinalIgnoreCase);
        bool compositionConfirmed=currentCompositionOutputValid && previous?["logoSha256"]?.ToString() is string confirmedLogoHash && File.Exists(logo) &&
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(logo,session.Token))).Equals(confirmedLogoHash,StringComparison.OrdinalIgnoreCase);
        bool pristineVerified=false;
        if(currentCompositionOutputValid && previous?["pristineSourcePath"]?.ToString() is string pristine && File.Exists(pristine))
        {
            string owned=Path.Combine(root,"compositions",ApplicationIntegrationDataStore.Key(game.StableIdentity),target)+Path.DirectorySeparatorChar;
            ApplicationIntegrationDataStore.GuardPath(pristine);
            if(Path.GetFullPath(pristine).StartsWith(Path.GetFullPath(owned),StringComparison.OrdinalIgnoreCase) &&
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(pristine,session.Token))).Equals(previous["sourceSha256"]?.ToString(),StringComparison.OrdinalIgnoreCase)){previewBackground=pristine;pristineVerified=true;}
        }
        if(currentCompositionOutputValid&&!pristineVerified){compositionConfirmed=false;previewBackground="";}
        var settings=new JsonObject { ["logo_position_x"]=25,["logo_position_y"]=50,["logo_scale"]=100,["background_position_x"]=50,["background_position_y"]=50,["background_scale"]=100,["background_opacity"]=100,["logo_shadow_opacity"]=55,["logo_shadow_blur"]=40 };
        if(existing["perfect_"+target+"_editorLayout"] is JsonObject saved)foreach(var item in saved)settings[item.Key]=item.Value?.DeepClone();
        settings["hideSteamLogo"]=existing["steamLogoManualOverride"]?.GetValue<bool>()??(existing["perfect_"+target+"_hideSteamLogo"]?.GetValue<bool>()!=false);
        var dirty=new JsonObject();
        var status=new TextBlock { Visibility=Visibility.Collapsed,TextWrapping=TextWrapping.Wrap,Opacity=.7 };
        if(currentCompositionOutputValid&&!pristineVerified){status.Visibility=Visibility.Visible;status.Text=T("Salvataggio non riuscito. Riprova.");}
        ImportInfoSaveQueue? saves=null;
        using var visual=new LaunchCurtainVisualEditor(previewBackground,logo,settings,patch=>
        {
            foreach(var item in patch){settings[item.Key]=item.Value?.DeepClone();dirty[item.Key]=item.Value?.DeepClone();}
            saves?.FieldsChanged();
        },T,target=="hero"?3840d/1240d:1926d/900d, .28,.72,true,resetLogoX:25);
        var panel=new StackPanel { Spacing=14,HorizontalAlignment=HorizontalAlignment.Stretch };panel.Children.Add(visual.Element);
        panel.Children.Insert(0,new TextBlock { Text=T(target=="hero"?"Emulation.Artwork.PerfectHero.Description":"Emulation.Artwork.PerfectBanner.Description"),TextWrapping=TextWrapping.Wrap,Opacity=.75 });
        var showLogo=new CheckBox { Content=T("Emulation.Artwork.CompositionLogo"),IsChecked=settings["show_logo"]?.GetValue<bool>()!=false };
        var hideSteamLogo=new CheckBox { Content=T("Emulation.Artwork.HideSteamLogo"),IsChecked=settings["hideSteamLogo"]!.GetValue<bool>() };
        panel.Children.Add(showLogo);panel.Children.Add(hideSteamLogo);
        void LogoVisibilityChanged() { settings["show_logo"]=showLogo.IsChecked==true;dirty["show_logo"]=settings["show_logo"]!.DeepClone();visual.SetSettings(new JsonObject { ["show_logo"]=showLogo.IsChecked==true });saves?.FieldsChanged(); }
        showLogo.Checked+=(_,_)=>LogoVisibilityChanged();showLogo.Unchecked+=(_,_)=>LogoVisibilityChanged();
        void SeparateLogoVisibilityChanged() { settings["hideSteamLogo"]=hideSteamLogo.IsChecked==true;dirty["hideSteamLogo"]=settings["hideSteamLogo"]!.DeepClone();saves?.FieldsChanged(); }
        hideSteamLogo.Checked+=(_,_)=>SeparateLogoVisibilityChanged();hideSteamLogo.Unchecked+=(_,_)=>SeparateLogoVisibilityChanged();
        var appearance=new StackPanel { Spacing=12 };
        void AppearanceSlider(string key,string label,double fallback)
        {
            var initial=double.TryParse(settings[key]?.ToString(),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var value)&&double.IsFinite(value)?Math.Clamp(value,0,100):fallback;
            var slider=new Slider { Header=T(label),Minimum=0,Maximum=100,StepFrequency=1,Value=initial };
            slider.ValueChanged+=(_,args)=>
            {
                settings[key]=args.NewValue;dirty[key]=args.NewValue;
                visual.SetSettings(new JsonObject { [key]=args.NewValue });saves?.FieldsChanged();
            };
            appearance.Children.Add(slider);
        }
        AppearanceSlider("background_opacity","Emulation.Artwork.BackgroundOpacity",100);
        AppearanceSlider("logo_shadow_opacity","Emulation.Artwork.ShadowOpacity",55);
        AppearanceSlider("logo_shadow_blur","Emulation.Artwork.ShadowBlur",40);
        panel.Children.Add(new Expander { Header=T("Emulation.Artwork.Appearance"),Content=appearance,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch });
        var source=new ComboBox { Header=T("Fonte"),HorizontalAlignment=HorizontalAlignment.Stretch };
        var title=new TextBox { Header=T("Titolo da cercare"),Text=await GameTitleQuery.ReadAsync(store,game.StableIdentity,"artwork",game.Title,session.Token) };
        var asset=new ComboBox { Items={T("Sfondo"),"Logo"},SelectedIndex=0,MinWidth=160 };
        void UpdateProviders()
        {
            string preferred=source.SelectedItem?.ToString()??"SteamGridDB",type=asset.SelectedIndex==1?"logo":target;
            source.Items.Clear();
            foreach(string provider in new[]{"SteamGridDB","Steam","PlayStation","Nintendo","Xbox","IGDB","AlphaCoders","iiDB","IGN"})
                if(provider=="Steam"||ApplicationArtworkPreferences.Supports(provider.ToLowerInvariant(),type))source.Items.Add(provider);
            source.SelectedItem=source.Items.OfType<string>().FirstOrDefault(value=>value==preferred)??"SteamGridDB";
        }
        UpdateProviders();asset.SelectionChanged+=(_,_)=>UpdateProviders();
        var searchRow=new Grid { ColumnSpacing=12 };searchRow.ColumnDefinitions.Add(new());searchRow.ColumnDefinitions.Add(new(){Width=new GridLength(220)});searchRow.Children.Add(title);Grid.SetColumn(source,1);searchRow.Children.Add(source);panel.Children.Add(searchRow);
        var actions=new StackPanel { Orientation=Orientation.Horizontal,Spacing=12 };actions.Children.Add(asset);
        var find=new Button { Content=T("Cerca alternative") };actions.Children.Add(find);
        var browse=new Button { Content=T("Sfoglia") };actions.Children.Add(browse);panel.Children.Add(actions);
        var results=new GridView { SelectionMode=ListViewSelectionMode.None };panel.Children.Add(results);
        var scroll=new ScrollViewer { Content=panel,Padding=new Thickness(0,0,24,32),HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
        var body=new Grid { RowSpacing=12 };body.RowDefinitions.Add(new(){Height=GridLength.Auto});body.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});body.Children.Add(status);Grid.SetRow(scroll,1);body.Children.Add(scroll);
        var dialog=new ContentDialog { Title=target=="hero"?"Perfect Hero":"Perfect Banner",Content=body,CloseButtonText=T("Chiudi"),XamlRoot=Content.XamlRoot,Tag="noloc" };
        void Resize(){var size=ImportInfoSize.ForRoot(dialog.XamlRoot.Size.Width,dialog.XamlRoot.Size.Height);body.Width=size.Width;body.Height=size.Height;dialog.Resources["ContentDialogMinWidth"]=size.Width+48;dialog.Resources["ContentDialogMaxWidth"]=size.Width+48;}
        void RootChanged(XamlRoot _,XamlRootChangedEventArgs __)=>Resize();Resize();dialog.XamlRoot.Changed+=RootChanged;session.Register(()=>dialog.XamlRoot.Changed-=RootChanged);
        async Task ComposeAsync()
        {
            var snapshot=(JsonObject)dirty.DeepClone();var layout=(JsonObject)settings.DeepClone();
            double Value(string name,double fallback)=>double.TryParse(layout[name]?.ToString(),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var number)&&double.IsFinite(number)?number:fallback;
            var options=new PerfectArtworkLayout(Value("logo_position_x",25),Value("logo_position_y",50),Value("logo_scale",100),Value("background_position_x",50),Value("background_position_y",50),Value("background_scale",100),Value("background_opacity",100),Value("logo_shadow_opacity",55),Value("logo_shadow_blur",40),layout["show_logo"]?.GetValue<bool>()!=false);
            var result=await new PerfectArtworkCompositor(root).ComposeAsync(game.StableIdentity,target,background,logo,CancellationToken.None,legacy,options);
            await game.ChooseArtworkAsync(target,new(result.Path,result.Path,result.Width,result.Height),CancellationToken.None);
            var actual=await game.ReadArtworkAsync(CancellationToken.None);
            if(!actual.TryGetValue(target,out var path)||!File.Exists(path)||!Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(path))).Equals(result.Sha256,StringComparison.OrdinalIgnoreCase))throw new IOException("Composed artwork readback failed.");
            var directories=game.GetSteamGridDirectoriesAsync is null?Array.Empty<string>():await game.GetSteamGridDirectoriesAsync(CancellationToken.None);
            bool hideSeparateLogo=layout["hideSteamLogo"]?.GetValue<bool>()!=false;
            await new PerfectArtworkState(store,ArtworkSteamRunning,SteamPluginBridge.EvaluateAsync,plugins).SetHiddenAsync(game.StableIdentity,game.ShortcutAppId,directories,hideSeparateLogo,CancellationToken.None);
            var savedLayout=new JsonObject { ["perfect_"+target+"_editorLayout"]=layout,["perfect_"+target+"_hideSteamLogo"]=hideSeparateLogo };
            if(snapshot.ContainsKey("hideSteamLogo"))savedLayout["steamLogoManualOverride"]=hideSeparateLogo;
            await store.PatchAsync(game.StableIdentity,"artwork",savedLayout,CancellationToken.None);
            // The editor keeps showing the preserved background, never a second baked-in logo.
            background=result.Path;legacy=null;
            if(!session.IsClosed)visual.SetImages(result.PristineSourcePath,logo);
            ImportIntegrationEdits.ClearConfirmedFields(dirty,snapshot);
            compositionConfirmed=true;
        }
        saves=new ImportInfoSaveQueue(ComposeAsync,state=>{if(session.IsClosed)return;status.Visibility=state=="error"?Visibility.Visible:Visibility.Collapsed;status.Text=state=="error"?T("Salvataggio non riuscito. Riprova."):"";},hasPendingFields:()=>dirty.Count>0);
        async Task ChooseAsync(string type,ImportArtworkResult option)
        {
            compositionConfirmed=false;
            await game.ChooseArtworkAsync(type,option,CancellationToken.None);
            var actual=await game.ReadArtworkAsync(CancellationToken.None);
            if(!actual.TryGetValue(type,out var path)||!File.Exists(path))throw new IOException("Selected composition source was not saved.");
            if(type=="logo")logo=path;else{background=path;legacy=null;}
            await ComposeAsync();
        }
        browse.Click+=async(_,_)=>{string type=asset.SelectedIndex==1?"logo":target;var path=await PickFileAsync(new[]{".png",".jpg",".jpeg",".webp"});if(!session.IsClosed&&!string.IsNullOrEmpty(path))await saves.ChangeAsync(()=>ChooseAsync(type,new(path,path,0,0)));};
        find.Click+=async(_,_)=>
        {
            string type=asset.SelectedIndex==1?"logo":target;find.IsEnabled=false;results.Items.Clear();
            try
            {
                var candidates=await game.SearchStoreArtworkAsync(source.SelectedItem?.ToString()??"SteamGridDB",title.Text.Trim(),type,session.Token);session.Token.ThrowIfCancellationRequested();
                var selectedFrames=new List<Border>();
                foreach(var candidate in candidates)
                {
                    var card=new StackPanel { Width=280,Spacing=8 };
                    var thumbnail=new Border { BorderThickness=new Thickness(3),BorderBrush=new SolidColorBrush(Microsoft.UI.Colors.Transparent),CornerRadius=new CornerRadius(8),Child=new Image { Source=new BitmapImage(new Uri(candidate.Preview)),Height=140,Stretch=Stretch.Uniform } };
                    selectedFrames.Add(thumbnail);
                    var choose=new Button { Content=thumbnail,Padding=new Thickness(0),HorizontalAlignment=HorizontalAlignment.Stretch };
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(choose,T("Artwork - {0}").Replace("{0}",game.Title));
                    choose.Click+=async(_,_)=>
                    {
                        choose.IsEnabled=false;
                        try { await saves.ChangeAsync(async()=> { await ChooseAsync(type,candidate);if(!session.IsClosed)foreach(var frame in selectedFrames)frame.BorderBrush=new SolidColorBrush(ReferenceEquals(frame,thumbnail)?Microsoft.UI.Colors.Gold:Microsoft.UI.Colors.Transparent); }); }
                        finally { if(!session.IsClosed)choose.IsEnabled=true; }
                    };
                    card.Children.Add(choose);results.Items.Add(new GridViewItem { Content=card,Padding=new Thickness(8) });
                }
            }
            catch(OperationCanceledException){}catch{if(!session.IsClosed){status.Text=T("Ricerca non disponibile. Riprova.");status.Visibility=Visibility.Visible;}}
            finally{if(!session.IsClosed)find.IsEnabled=true;}
        };
        dialog.Closing+=async(_,args)=>{var deferral=args.GetDeferral();scroll.IsEnabled=false;try{visual.CommitPendingChanges();await saves.FlushAsync();if(!saves.HasFailure&&!compositionConfirmed)await saves.ChangeAsync(ComposeAsync);if(saves.HasFailure){args.Cancel=true;scroll.IsEnabled=true;}}finally{deferral.Complete();}};
        dialog.Closed+=(_,_)=>session.Dispose();ConfigureDialogEntrance(dialog);await dialog.ShowAsync();
    }
}

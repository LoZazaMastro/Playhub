// SPDX-License-Identifier: MIT
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Playhub.Importing;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace Playhub.StoreLibrary;

public sealed partial class StoreLibraryPage : UserControl, IDisposable
{
    private StoreLibraryController? controller;
    private Func<string, string> text = key => key;
    private Func<Task<string?>>? pickFolder;
    private Func<StoreLibraryGame, string?>? artwork;
    private Func<StoreKind, CancellationToken, Task<bool>>? sync;
    private Func<double> aspect = () => 1.5;
    private GameCardViewport<StoreLibraryGame>? viewport;
    private XboxGamePassView? gamePass;
    private IStoreXboxInstalledService? xboxService;
    private IStoreXboxAccountService? xboxAccountService;
    private StoreXboxInstalledSnapshot? xboxInstalled;
    private StoreInstalledGameController? installedActions;
    private ContentDialog? installedActionDialog;
    private sealed class InstalledCardActions(StoreLibraryGame game, Button play, Button uninstall)
    {
        internal readonly StoreLibraryGame Game = game;
        internal readonly Button Play = play, Uninstall = uninstall;
        internal StoreInstalledGameStatus? Status;
        internal bool Attempted;
        internal bool Reading;
        internal int Generation, RetryCount;
        internal string Availability = "";
        internal DateTimeOffset RetryAt;
    }
    private readonly Dictionary<StoreGameKey, InstalledCardActions> installedRows = new();
    private sealed class GameCardState(StoreLibraryGame game, Border cover)
    {
        internal readonly StoreLibraryGame Game = game;
        internal readonly Border Cover = cover;
        internal string? ImagePath;
        internal Button? Install;
        internal InstalledCardActions? Installed;
    }
    private readonly Dictionary<FrameworkElement, GameCardState> gameRows = new();
    private bool artworkRefreshQueued;
    private bool refreshingLibrary;
    private string? localErrorKey;
    private string? localNoticeKey;
    private StoreKind? localNoticeStore, localErrorStore, syncingStore;
    private bool gamePassArtworkQueued;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private string gamesSignature = "";
    private string accountsSignature = "";
    private bool active, rendering, disposed, dialogOpen, syncing, installedViewPending;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? xboxSignInLifetime;
    private string? language, languageFingerprint;
    private sealed class DownloadRow(StoreDownloadJob job)
    {
        internal StoreDownloadJob Latest = job;
        internal readonly TextBlock Title = Label("", 17), State = Label("", opacity: .75), Progress = Label("", 12, .7), Path = Label("", 12, .65), Error = Label("", opacity: .75);
        internal readonly ProgressBar Bar = new() { Minimum = 0, Maximum = 100 };
        internal readonly FontIcon Icon = new() { FontSize = 18, VerticalAlignment = VerticalAlignment.Center };
        internal readonly Dictionary<StoreJobAction, Button> Buttons = new();
        internal readonly Grid Actions = new() { ColumnSpacing = 8, RowSpacing = 8 };
        internal string ActionLayout = "";
        internal Border Card = null!;
    }
    private readonly Dictionary<Guid, DownloadRow> downloadRows = new();

    public StoreLibraryPage() { InitializeComponent(); }
    internal StoreLibraryPage(StoreLibraryController owner, Func<string, string> translate, Func<Task<string?>> chooseFolder,
        Func<double> coverAspect, string? selectedLanguage, Func<StoreLibraryGame, string?>? resolveArtwork = null,
        Func<StoreKind, CancellationToken, Task<bool>>? syncToSteam = null, IStoreXboxCatalogService? gamePassService = null,
        Func<StoreXboxCatalogGame, string?>? resolveGamePassArtwork = null, IStoreXboxInstalledService? installedXboxService = null,
        IStoreXboxAccountService? accountXboxService = null, IStoreLibraryService? installedActionsService = null) : this()
    {
        controller = owner; text = translate; pickFolder = chooseFolder; aspect = coverAspect;
        language = selectedLanguage;
        if (installedActionsService is IStoreInstalledGameService operations)
        {
            var launcher = new StoreGameSessionLauncher(AppContext.BaseDirectory);
            installedActions = new(operations, installedActionsService, CurrentInstalledGame, launcher.LaunchAsync);
            installedActions.Changed += InstalledActionsChanged;
        }
        artwork = resolveArtwork; sync = syncToSteam; xboxService = installedXboxService; xboxAccountService = accountXboxService;
        if (gamePassService is not null)
        {
            gamePass = new XboxGamePassView(gamePassService, text, aspect, resolveGamePassArtwork);
            GamePassPanel.Content = gamePass;
        }
        GamePassTab.Visibility = gamePass is null ? Visibility.Collapsed : Visibility.Visible;
        GamePassTab.Text = text("GamePass.Title");
        SetSectionLabels();
        GamesTab.Text = text("StoreLibrary.Library"); DownloadsTab.Text = text("StoreLibrary.Downloads");
        RefreshButton.Content = text("StoreLibrary.Refresh"); Search.PlaceholderText = text("StoreLibrary.Search");
        AutomationProperties.SetName(Search, text("StoreLibrary.Search"));
        AutomationProperties.SetName(StoreFilter, text("StoreLibrary.AllStores"));
        AutomationProperties.SetName(InstallFilter, text("StoreLibrary.AllGames"));
        StoreFilter.Items.Add(new ComboBoxItem { Content = text("StoreLibrary.AllStores"), Tag = "all" });
        foreach (var store in Enum.GetValues<StoreKind>()) StoreFilter.Items.Add(new ComboBoxItem { Content = StoreLibraryPresentation.Name(store), Tag = store.ToString() });
        foreach (var key in new[] { "AllGames", "Installed", "NotInstalled" }) InstallFilter.Items.Add(new ComboBoxItem { Content = text("StoreLibrary." + key), Tag = key });
        StoreFilter.SelectedIndex = InstallFilter.SelectedIndex = 0;
        DensityHost.Content = GameCardDensityControl.Create(text);
        EmptyLibraryTitle.Text = text("StoreLibrary.EmptyLibrary"); EmptyLibraryBody.Text = text("StoreLibrary.EmptyLibraryHelp");
        EmptyDownloadsTitle.Text = text("StoreLibrary.EmptyDownloads"); EmptyDownloadsBody.Text = text("StoreLibrary.EmptyDownloadsHelp");
        DownloadsDescription.Text = text("StoreLibrary.DownloadHelp");
        owner.Changed += ControllerChanged;
        timer.Tick += async (_, _) =>
        {
            if (!active) return;
            if (owner.Snapshot.Jobs.Any(j => StoreLibraryPresentation.Active(j.State) || j.State == StoreJobState.ExternalHandoff && j.ReasonCode == "windows_store_managed")) await owner.ReadAsync();
            UpdateInstalledButtons(); // retry only due transient status reads, not every card
        };
        Unloaded += (_, _) => { xboxSignInLifetime?.Cancel(); timer.Stop(); viewport?.Dispose(); viewport = null; gamesSignature = ""; };
        Loaded += (_, _) => { if (active) { timer.Start(); Render(); } };
        Render();
    }
    internal void SetSteamSync(Func<StoreKind, CancellationToken, Task<bool>>? callback) { sync = callback; Render(); }
    internal void RefreshGamePassArtwork()
    {
        if (disposed || gamePassArtworkQueued) return;
        gamePassArtworkQueued = true;
        if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            gamePassArtworkQueued = false;
            if (!disposed) gamePass?.RefreshArtwork();
        })) gamePassArtworkQueued = false;
    }
    internal void RefreshArtwork()
    {
        if (disposed || artworkRefreshQueued) return;
        artworkRefreshQueued = true;
        if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            artworkRefreshQueued = false;
            if (disposed || controller is null) return;
            // A completed image changes only its realized image. Replacing the
            // repeater here discards its scroll anchor and the controller focus.
            foreach (var row in gameRows.Values.ToArray()) UpdateCover(row);
        })) artworkRefreshQueued = false;
    }
    internal async Task<bool> RequestInstallAsync(StoreGameKey key)
    {
        if (controller is null || disposed || dialogOpen) return false;
        await controller.ReadAsync();
        var game = controller.Snapshot.Games.FirstOrDefault(g => g.Key == key);
        if (game is not null && CanOpenXboxOwned(game)) { await OpenXboxOwnedAsync(game); return true; }
        if (game is null || !controller.CanInstall(game)) { ShowError("StoreLibrary.PrepareFailed"); return false; }
        await InstallAsync(game); return true; // outcome remains the real service snapshot
    }
    internal void SetActive(bool value)
    {
        active = value; gamePass?.SetActive(value && LibraryTabs.SelectedItem == GamePassTab);
        if (!value) { xboxSignInLifetime?.Cancel(); installedActionDialog?.Hide(); timer.Stop(); return; }
        Relocalize(language); timer.Start(); Render(); if (controller is not null) _ = RefreshLibraryAsync();
    }
    internal void Relocalize(string? selectedLanguage)
    {
        string fingerprint = text("StoreLibrary.Stores");
        if (language == selectedLanguage && languageFingerprint == fingerprint) return;
        languageFingerprint = fingerprint; language = selectedLanguage;
        GamePassTab.Text = text("GamePass.Title"); gamePass?.Relocalize();
        SetSectionLabels();
        int storeIndex = StoreFilter.SelectedIndex, installIndex = InstallFilter.SelectedIndex;
        GamesTab.Text = text("StoreLibrary.Library"); DownloadsTab.Text = text("StoreLibrary.Downloads");
        RefreshButton.Content = text("StoreLibrary.Refresh"); Search.PlaceholderText = text("StoreLibrary.Search");
        foreach (var item in StoreFilter.Items.OfType<ComboBoxItem>()) if (Equals(item.Tag, "all")) item.Content = text("StoreLibrary.AllStores");
        foreach (var item in InstallFilter.Items.OfType<ComboBoxItem>()) item.Content = text("StoreLibrary." + item.Tag);
        StoreFilter.SelectedIndex = InstallFilter.SelectedIndex = -1;
        StoreFilter.SelectedIndex = storeIndex; InstallFilter.SelectedIndex = installIndex;
        EmptyLibraryTitle.Text = text("StoreLibrary.EmptyLibrary"); EmptyLibraryBody.Text = text("StoreLibrary.EmptyLibraryHelp");
        EmptyDownloadsTitle.Text = text("StoreLibrary.EmptyDownloads"); EmptyDownloadsBody.Text = text("StoreLibrary.EmptyDownloadsHelp");
        DownloadsDescription.Text = text("StoreLibrary.DownloadHelp"); gamesSignature = "";
    }
    private void SetSectionLabels()
    {
        AccountsTab.Text = AccountsHeading.Text = text("StoreLibrary.Accounts");
        AccountsDescription.Text = text("StoreLibrary.AccountsHelp");
        LibraryHeading.Text = text("StoreLibrary.Library"); LibraryDescription.Text = text("StoreLibrary.LibraryHelp");
        DownloadsHeading.Text = text("StoreLibrary.Downloads");
        ClearCompletedButton.Content = text("StoreLibrary.ClearCompleted");
    }
    private void ControllerChanged(object? sender, EventArgs args)
    {
        if (disposed) return;
        if (DispatcherQueue.HasThreadAccess) Render(); else DispatcherQueue.TryEnqueue(Render);
    }
    private void Render()
    {
        if (disposed || controller is null || rendering) return;
        rendering = true;
        try
        {
            var error = localErrorKey ?? (localNoticeKey is null ? controller.ErrorKey : null);
            var errorStore = localErrorKey is not null ? localErrorStore : controller.ErrorStore;
            Status.IsOpen = error is not null || localNoticeKey is not null;
            Status.Message = error is { } key ? text(key) : localNoticeKey is { } notice ? text(notice) : "";
            var statusStore = error is not null ? errorStore : localNoticeStore;
            if (statusStore is { } store) Status.Message = StoreLibraryPresentation.Name(store) + " · " + Status.Message;
            Status.Severity = error is null ? InfoBarSeverity.Informational : InfoBarSeverity.Warning;
            RefreshButton.IsEnabled = !controller.Busy;
            RenderAccounts(); RenderGames(); RenderDownloads(); UpdateInstalledButtons(); UpdateInstallButtons();
        }
        finally { rendering = false; }
    }
    private Button Action(string key, Func<Task> action, bool enabled = true, bool primary = false)
    {
        var b = new Button { Content = new TextBlock { Text = text("StoreLibrary." + key), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center }, MinWidth = 0, IsEnabled = enabled, HorizontalAlignment = HorizontalAlignment.Stretch };
        string style = primary ? "PlayhubPrimaryButtonStyle" : "PlayhubSecondaryButtonStyle";
        if (Application.Current.Resources.TryGetValue(style, out var resource) && resource is Style s) b.Style = s;
        b.Click += async (_, _) => { if (!disposed && b.IsEnabled) { b.IsEnabled = false; localErrorKey = localNoticeKey = null; localErrorStore = localNoticeStore = null; try { await action(); } catch { if (!disposed) ShowError("StoreLibrary.CommandFailed"); } finally { if (!disposed) Render(); } } };
        return b;
    }
    private static TextBlock Label(string value, double size = 14, double opacity = 1) => new() { Text = value, FontSize = size, Opacity = opacity, TextWrapping = TextWrapping.Wrap };
    private static Border Card(UIElement child)
    {
        var card = new Border { Child = child, HorizontalAlignment = HorizontalAlignment.Stretch };
        if (Application.Current.Resources.TryGetValue("PlayhubCardBorderStyle", out var style) && style is Style s) card.Style = s;
        return card;
    }
    private void RenderAccounts()
    {
        string signature = string.Join("\n", controller!.Snapshot.Accounts.OrderBy(a => a.Store))
            + "\n" + controller.Busy + "\n" + dialogOpen + "\n" + syncing + "\n" + languageFingerprint + "\n" + (sync is not null) + "\n" + xboxInstalled?.Revision;
        signature += "\n" + string.Join("|", Enum.GetValues<StoreKind>().Select(kind => kind + ":" + controller.IsRefreshing(kind) + ":" + controller.LibraryError(kind) + ":" + controller.VisibleGames.Count(g => g.Key.Store == kind)));
        if (signature == accountsSignature) return;
        accountsSignature = signature;
        Accounts.Children.Clear();
        foreach (var kind in Enum.GetValues<StoreKind>())
        {
            var account = controller!.Snapshot.Accounts.FirstOrDefault(a => a.Store == kind);
            var content = new StackPanel { Spacing = 12 };
            var heading = new Grid { ColumnSpacing = 12 };
            heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); heading.ColumnDefinitions.Add(new());
            FrameworkElement logo = StoreLibraryPresentation.ServiceLogo(kind) is string logoName
                ? new Image { Source = new BitmapImage(new Uri("ms-appx:///Assets/ServiceLogos/" + logoName + ".png")), Width = 36, Height = 36, Stretch = Stretch.Uniform }
                : new FontIcon { Glyph = "\uE719", FontSize = 28, Width = 36, Height = 36 };
            heading.Children.Add(logo); var name = Label(StoreLibraryPresentation.Name(kind), 19); name.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            Grid.SetColumn(name, 1); heading.Children.Add(name); content.Children.Add(heading);
            content.Children.Add(Label(text("StoreLibrary.Account." + (account?.State.ToString() ?? "Unavailable")), opacity: .75));
            if (kind == StoreKind.Xbox) content.Children.Add(Label(text("StoreLibrary.XboxAccountHelp"), opacity: .75));
            if (!string.IsNullOrWhiteSpace(account?.DisplayName)) content.Children.Add(Label(account.DisplayName, opacity: .75));
            var actions = new StackPanel { Spacing = 8 };
            bool connected = account?.State == StoreConnectionState.Connected;
            if (connected)
            {
                int count = controller.VisibleGames.Count(g => g.Key.Store == kind);
                content.Children.Add(Label(string.Format(CultureInfo.CurrentCulture, text(count == 1 ? "StoreLibrary.GameCountOne" : "StoreLibrary.GameCount"), count), opacity: .75));
                if (controller.IsRefreshing(kind)) content.Children.Add(Label(text("StoreLibrary.RefreshingLibrary"), opacity: .75));
                else if (controller.LibraryError(kind) is { } error) content.Children.Add(Label(text(error), opacity: .9));
                if (syncingStore == kind) content.Children.Add(Label(text("StoreLibrary.SyncingSteam"), opacity: .75));
            }
            if (kind == StoreKind.Xbox && xboxAccountService is not null && !connected)
                actions.Children.Add(Action("Connect", ConnectXboxAsync, !controller.Busy && !dialogOpen && account?.Capabilities.CanConnect == true, true));
            if (kind != StoreKind.Xbox && account?.Capabilities.CanConnect == true && !connected) actions.Children.Add(Action("Connect", () => ConnectAsync(kind), !controller.Busy, true));
            if (connected)
            {
                actions.Children.Add(Action("SyncSteam", () => SyncStoreAsync(kind), !controller.Busy && !controller.IsRefreshing(kind) && !syncing && sync is not null, true));
                if (account!.Capabilities.CanRefresh) actions.Children.Add(Action("RefreshLibrary", async () => { if (await controller.RefreshAsync(kind)) ShowStoreGames(kind); }, !controller.Busy && !controller.IsRefreshing(kind)));
                actions.Children.Add(Action("Disconnect", async () => { await controller.DisconnectAsync(kind); }, !controller.Busy));
            }
            if (kind != StoreKind.Xbox)
            {
                if (account?.Capabilities.InstallKind == StoreInstallKind.ExternalHandoff)
                    content.Children.Add(Label(text("StoreLibrary.ExternalHelp"), opacity: .65));
                else if (account?.State == StoreConnectionState.Unavailable || account is null)
                    content.Children.Add(Label(text("StoreLibrary.ProviderUnavailable"), opacity: .65));
            }
            foreach (var button in actions.Children.OfType<Button>()) button.IsEnabled &= !dialogOpen;
            content.Children.Add(actions); var card = Card(content); card.MinWidth = 0; Accounts.Children.Add(card);
        }
        ArrangeAccounts();
    }
    private void ArrangeAccounts()
    {
        int columns = Accounts.ActualWidth >= 1020 ? 4 : Accounts.ActualWidth >= 570 ? 2 : 1;
        Accounts.ColumnDefinitions.Clear(); Accounts.RowDefinitions.Clear();
        for (int i = 0; i < columns; i++) Accounts.ColumnDefinitions.Add(new());
        for (int i = 0; i < (Accounts.Children.Count + columns - 1) / columns; i++) Accounts.RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (int i = 0; i < Accounts.Children.Count; i++) { Grid.SetColumn((FrameworkElement)Accounts.Children[i], i % columns); Grid.SetRow((FrameworkElement)Accounts.Children[i], i / columns); }
    }
    private void AccountsSizeChanged(object sender, SizeChangedEventArgs args) => ArrangeAccounts();
    private void ShowStoreGames(StoreKind kind)
    {
        LibraryTabs.SelectedItem = GamesTab;
        StoreFilter.SelectedItem = StoreFilter.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, kind.ToString()));
        InstallFilter.SelectedIndex = 0; Search.Text = ""; ShowTab(); Render();
    }
    private async Task SyncStoreAsync(StoreKind kind)
    {
        if (sync is null || syncing || controller is null) return;
        syncing = true; syncingStore = kind; Render();
        try
        {
            if (!await controller.RefreshAsync(kind)) return;
            if (!await sync(kind, lifetime.Token)) { localErrorStore = kind; ShowError("StoreLibrary.CommandFailed"); return; }
            await controller.ReadAsync();
            if (disposed) return;
            localNoticeKey = "StoreLibrary.SyncCompleted";
            localNoticeStore = kind;
            ShowStoreGames(kind);
        }
        finally { syncing = false; syncingStore = null; }
    }
    private void FiltersSizeChanged(object sender, SizeChangedEventArgs args)
    {
        bool narrow = args.NewSize.Width < 700; Grid.SetColumn(Filters, narrow ? 0 : 1); Grid.SetRow(Filters, narrow ? 1 : 0);
        Grid.SetColumnSpan(Search, narrow ? 2 : 1);
        Filters.Width = narrow ? Math.Max(0, args.NewSize.Width) : 310;
    }
    private void RenderGames()
    {
        string store = (StoreFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
        string installed = (InstallFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "AllGames";
        var games = controller!.VisibleGames.Where(g => (store == "all" || g.Key.Store.ToString() == store)
            && (installed == "AllGames" || g.IsInstalled == (installed == "Installed"))
            && g.Title.Contains(Search.Text ?? "", StringComparison.CurrentCultureIgnoreCase)).OrderBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
        LibrarySummary.Text = string.Format(CultureInfo.CurrentCulture, text(games.Length == 1 ? "StoreLibrary.GameCountOne" : "StoreLibrary.GameCount"), games.Length);
        LibraryEmpty.Visibility = games.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        string signature = string.Join("\n", games.Select(g => string.Join("\t", g.Key.Store, g.Key.Id, g.Title, g.AccountRevision, g.IsInstalled, g.IsLaunchable, g.InstallPath, g.Capabilities, g.ArtworkKey, g.ArtworkKind, g.ArtworkUrl, string.Join('|', g.ArtworkFallbackUrls ?? []))));
        if (signature == gamesSignature && viewport is not null)
        {
            // A retry can become due without changing library metadata. Keep the
            // scroll/focus anchor and revisit only the already-realized images.
            foreach (var row in gameRows.Values.ToArray()) UpdateCover(row);
            return;
        }
        gamesSignature = signature; viewport?.Dispose(); viewport = null; installedRows.Clear(); GameCards.Children.Clear();
        if (games.Length == 0) return;
        viewport = new GameCardViewport<StoreLibraryGame>(games, BuildGameCard, ReleaseGameCard, aspect, GameCards.ActualWidth);
        GameCards.Children.Add(viewport.View);
    }
    private FrameworkElement BuildGameCard(StoreLibraryGame game)
    {
        var content = new StackPanel { Spacing = 10 };
        var padding = Application.Current.Resources.TryGetValue("PlayhubCardPadding", out var p) && p is Thickness t ? t : new Thickness(16);
        double imageWidth = Math.Max(0, GameCardLayout.CardWidth(GameCards.ActualWidth) - padding.Left - padding.Right);
        var cover = new Border { CornerRadius = new CornerRadius(5), Height = Math.Max(120, imageWidth * aspect()), Background = Application.Current.Resources["ControlFillColorSecondaryBrush"] as Brush };
        var cardState = new GameCardState(game, cover);
        UpdateCover(cardState);
        content.Children.Add(cover); var title = Label(game.Title, GameCardLayout.TitleFontSize(GameCardLayout.SizeStep)); title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        content.Children.Add(title); content.Children.Add(Label(StoreLibraryPresentation.Name(game.Key.Store), 12, .6));
        content.Children.Add(Label(text("StoreLibrary." + (game.IsInstalled ? "Installed" : "NotInstalled")), 12, .75));
        if (game.IsInstalled && installedActions is not null && StoreInstalledGameController.Supported(game))
        {
            var play = Action("Play", () => PlayInstalledAsync(game), false, true);
            var uninstall = Action("Uninstall", () => UninstallInstalledAsync(game), false);
            Danger(uninstall);
            AutomationProperties.SetAutomationId(play, "StoreGame-" + game.Key.Store + "-" + game.Key.Id + "-Play");
            AutomationProperties.SetAutomationId(uninstall, "StoreGame-" + game.Key.Store + "-" + game.Key.Id + "-Uninstall");
            var row = new InstalledCardActions(game, play, uninstall);
            cardState.Installed = row;
            installedRows[game.Key] = row;
            content.Loaded += (_, _) => InstalledCardLoaded(row);
            content.Children.Add(play); content.Children.Add(uninstall);
        }
        else if (!game.IsInstalled && game.Key.Store == StoreKind.Xbox && game.Capabilities.InstallKind == StoreInstallKind.ExternalHandoff)
            content.Children.Add(cardState.Install = Action("Install", () => OpenXboxOwnedAsync(game), CanOpenXboxOwned(game), true));
        else if (!game.IsInstalled && game.Capabilities.InstallKind != StoreInstallKind.Unavailable)
            content.Children.Add(cardState.Install = Action(game.Capabilities.InstallKind == StoreInstallKind.ExternalHandoff ? "OpenStore" : "Install", () => InstallAsync(game), controller!.CanInstall(game), true));
        else if (!game.IsInstalled) content.Children.Add(Label(text("StoreLibrary.ProviderUnavailable"), 12, .65));
        var card = Card(content); gameRows.Add(card, cardState); return card;
    }
    private void UpdateCover(GameCardState row)
    {
        string? path = artwork?.Invoke(row.Game);
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            if (string.Equals(row.ImagePath, path, StringComparison.OrdinalIgnoreCase)) return;
            row.ImagePath = path;
            // Cards are at most 232 logical pixels wide. Decode a two-scale
            // thumbnail rather than a full-resolution poster on every realization.
            var bitmap = new BitmapImage { DecodePixelWidth = 480 };
            var image = new Image { Source = bitmap, Stretch = Stretch.UniformToFill };
            bitmap.ImageFailed += (_, _) =>
            {
                if (ReferenceEquals(row.Cover.Child, image))
                {
                    row.ImagePath = null;
                    row.Cover.Child = new FontIcon { Glyph = "\uE7FC", FontSize = 42, Opacity = .55 };
                }
            };
            row.Cover.Child = image;
            bitmap.UriSource = new Uri(path);
        }
        else if (row.Cover.Child is null) row.Cover.Child = new FontIcon { Glyph = "\uE7FC", FontSize = 42, Opacity = .55 };
    }
    private void ReleaseGameCard(FrameworkElement card)
    {
        if (!gameRows.Remove(card, out var row)) return;
        if (row.Installed is not null && installedRows.TryGetValue(row.Game.Key, out var current) && ReferenceEquals(current, row.Installed))
            installedRows.Remove(row.Game.Key);
    }
    private void UpdateInstallButtons()
    {
        if (controller is null) return;
        foreach (var row in gameRows.Values)
            if (row.Install is not null) row.Install.IsEnabled = !dialogOpen &&
                (row.Game.Key.Store == StoreKind.Xbox && row.Game.Capabilities.InstallKind == StoreInstallKind.ExternalHandoff
                    ? CanOpenXboxOwned(row.Game) : controller.CanInstall(row.Game));
    }
    private bool CurrentInstalledGame(StoreLibraryGame game) => !disposed && active && controller is not null
        && LibraryTabs.SelectedItem == GamesTab && controller.Snapshot.Games.Any(g => g.Key == game.Key
            && g.AccountRevision == game.AccountRevision && g.IsInstalled
            && StoreInstalledGameController.SamePath(g.InstallPath, game.InstallPath));
    private void InstalledActionsChanged(object? sender, EventArgs args)
    {
        if (disposed) return;
        if (DispatcherQueue.HasThreadAccess) UpdateInstalledButtons(); else DispatcherQueue.TryEnqueue(UpdateInstalledButtons);
    }
    private void InstalledCardLoaded(InstalledCardActions row)
    {
        if (disposed || !installedRows.TryGetValue(row.Game.Key, out var current) || !ReferenceEquals(current, row)) return;
        // The virtualized viewport realizes cards after Render/ShowTab have run.
        UpdateInstalledButtons();
    }
    private void UpdateInstalledButtons()
    {
        if (disposed || installedActions is null || controller is null) return;
        bool worker = controller.Snapshot.Jobs.Any(j => j.State is StoreJobState.Starting or StoreJobState.Downloading or StoreJobState.Pausing
            or StoreJobState.Installing or StoreJobState.Verifying or StoreJobState.Canceling);
        bool metadata = Enum.GetValues<StoreKind>().Any(controller.IsRefreshing);
        string availability = worker + ":" + metadata;
        foreach (var row in installedRows.Values.ToArray())
        {
            if (row.Availability != availability)
            {
                row.Availability = availability; row.Generation++; row.Attempted = false; row.RetryCount = 0; row.RetryAt = DateTimeOffset.MinValue;
            }
            bool available = CurrentInstalledGame(row.Game) && !controller.Busy && !dialogOpen && !installedViewPending && !installedActions.Busy;
            row.Play.IsEnabled = available && row.Status?.CanLaunch == true;
            row.Uninstall.IsEnabled = available;
            bool retryDue = row.Status?.CanUninstall != true && row.RetryCount < 4 && DateTimeOffset.UtcNow >= row.RetryAt;
            if (available && !row.Reading && (!row.Attempted || retryDue))
            { row.Attempted = true; row.Reading = true; _ = ReadInstalledCardAsync(row, row.Generation); }
        }
    }
    private async Task ReadInstalledCardAsync(InstalledCardActions row, int generation)
    {
        var status = await installedActions!.ReadStatusAsync(row.Game);
        if (disposed || !installedRows.TryGetValue(row.Game.Key, out var current) || !ReferenceEquals(current, row)) return;
        row.Reading = false;
        if (row.Generation == generation)
        {
            row.Status = status;
            if (status?.CanUninstall != true) { row.RetryCount++; row.RetryAt = DateTimeOffset.UtcNow.AddSeconds(5 * Math.Pow(2, row.RetryCount - 1)); }
        }
        UpdateInstalledButtons();
    }
    private async Task PlayInstalledAsync(StoreLibraryGame game)
    {
        if (installedActions is null || controller is null || dialogOpen || installedViewPending) return;
        installedViewPending = true; UpdateInstalledButtons();
        try
        {
            var result = await installedActions.PlayAsync(game);
            if (disposed) return;
            if (result == StoreInstalledActionResult.Requested) localNoticeKey = "StoreLibrary.LaunchRequested";
            else if (result is not (StoreInstalledActionResult.Canceled or StoreInstalledActionResult.Busy)) ShowError("StoreLibrary.LaunchFailed");
        }
        finally { installedViewPending = false; if (!disposed) UpdateInstalledButtons(); }
    }
    private async Task UninstallInstalledAsync(StoreLibraryGame game)
    {
        if (installedActions is null || controller is null || dialogOpen || installedViewPending) return;
        installedViewPending = true; UpdateInstalledButtons();
        try
        {
        var result = await installedActions.UninstallAsync(game, async (preparation, token) =>
        {
            dialogOpen = true; UpdateInstalledButtons();
            var body = new StackPanel { Spacing = 14 };
            body.Children.Add(Label(text("StoreLibrary.UninstallConfirm")));
            body.Children.Add(Label(preparation.InstallPath ?? ""));
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = game.Title, Content = body,
                PrimaryButtonText = text("StoreLibrary.Uninstall"), CloseButtonText = text("StoreLibrary.Cancel"), DefaultButton = ContentDialogButton.Close };
            installedActionDialog = dialog;
            if (Application.Current.Resources.TryGetValue("PlayhubSecondaryButtonStyle", out var style) && style is Style s) dialog.PrimaryButtonStyle = s;
            dialog.Opened += (_, _) => { foreach (var button in Descendants(dialog).OfType<Button>().Where(b => b.Name == "PrimaryButton")) Danger(button); };
            using var cancellation = token.Register(() => DispatcherQueue.TryEnqueue(() => dialog.Hide()));
            try { return await dialog.ShowAsync() == ContentDialogResult.Primary; }
            finally { dialogOpen = false; installedActionDialog = null; }
        });
        if (disposed) return;
        // Re-read the host's real installed state; the card never retires optimistically.
        await controller.ReadAsync();
        if (disposed) return;
        if (result == StoreInstalledActionResult.Completed) localNoticeKey = "StoreLibrary.UninstallCompleted";
        else if (result is not (StoreInstalledActionResult.Canceled or StoreInstalledActionResult.Busy)) ShowError("StoreLibrary.UninstallFailed");
        gamesSignature = ""; Render();
        if (active && LibraryTabs.SelectedItem == GamesTab)
        {
            if (installedRows.TryGetValue(game.Key, out var row)) row.Uninstall.Focus(FocusState.Programmatic);
            else RefreshButton.Focus(FocusState.Programmatic);
        }
        }
        finally { installedViewPending = false; if (!disposed) UpdateInstalledButtons(); }
    }
    private void RenderDownloads()
    {
        var jobs = controller!.Snapshot.Jobs.OrderByDescending(j => j.UpdatedAt).ToArray();
        ClearCompletedButton.Visibility = jobs.Any(j => j.State == StoreJobState.Completed) ? Visibility.Visible : Visibility.Collapsed;
        ClearCompletedButton.IsEnabled = controller.CanClearCompleted && !dialogOpen;
        DownloadsEmpty.Visibility = jobs.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var id in downloadRows.Keys.Where(id => !jobs.Any(j => j.JobId == id)).ToArray())
        { DownloadRows.Children.Remove(downloadRows[id].Card); downloadRows.Remove(id); }
        while (DownloadRows.RowDefinitions.Count < jobs.Length) DownloadRows.RowDefinitions.Add(new() { Height = GridLength.Auto });
        while (DownloadRows.RowDefinitions.Count > jobs.Length) DownloadRows.RowDefinitions.RemoveAt(DownloadRows.RowDefinitions.Count - 1);
        for (int index = 0; index < jobs.Length; index++)
        {
            var job = jobs[index];
            if (downloadRows.TryGetValue(job.JobId, out var prior) && (prior.Latest.Key != job.Key || prior.Latest.AccountRevision != job.AccountRevision))
            { DownloadRows.Children.Remove(prior.Card); downloadRows.Remove(job.JobId); }
            if (!downloadRows.TryGetValue(job.JobId, out var row))
            {
                row = new DownloadRow(job);
                var content = new StackPanel { Spacing = 10 };
                var status = new Grid { ColumnSpacing = 10 };
                status.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); status.ColumnDefinitions.Add(new());
                status.Children.Add(row.Icon); Grid.SetColumn(row.State, 1); status.Children.Add(row.State);
                foreach (var element in new UIElement[] { row.Title, status, row.Bar, row.Progress, row.Path, row.Error }) content.Children.Add(element);
                var actions = row.Actions;
                var layout = row;
                actions.SizeChanged += (_, _) => ArrangeDownloadActions(layout);
                foreach (var action in Enum.GetValues<StoreJobAction>())
                {
                    var retained = row;
                    var button = Action(action.ToString(), async () =>
                    {
                        var current = retained.Latest;
                        if (action == StoreJobAction.Cancel) await CancelDownloadAsync(current);
                        else await controller.ControlAsync(current, action);
                    });
                    AutomationProperties.SetAutomationId(button, "StoreDownload-" + job.JobId.ToString("N") + "-" + action);
                    if (action == StoreJobAction.Cancel) Danger(button);
                    row.Buttons.Add(action, button); actions.Children.Add(button);
                }
                content.Children.Add(actions); row.Card = Card(content); row.Card.Tag = job.JobId;
                downloadRows.Add(job.JobId, row); DownloadRows.Children.Add(row.Card);
            }
            row.Latest = job; Grid.SetRow(row.Card, index);
            row.Title.Text = job.Title;
            row.Icon.Glyph = StoreLibraryPresentation.StateGlyph(job.State);
            row.State.Text = StoreLibraryPresentation.Name(job.Key.Store) + " \u00B7 " + text(StoreLibraryPresentation.StateKey(job.State));
            double? percent = StoreLibraryPresentation.Percent(job);
            row.Bar.Value = percent ?? 0; row.Bar.IsIndeterminate = percent is null && StoreLibraryPresentation.Active(job.State);
            bool managed = job.ReasonCode == "windows_store_managed";
            row.Bar.Visibility = row.Progress.Visibility = job.State == StoreJobState.ExternalHandoff && !managed ? Visibility.Collapsed : Visibility.Visible;
            row.Progress.Text = StoreLibraryPresentation.ProgressText(job);
            row.Path.Text = managed ? text("StoreLibrary.WindowsManaged") : job.InstallPath; row.Path.Visibility = !managed && (job.State == StoreJobState.ExternalHandoff || string.IsNullOrWhiteSpace(job.InstallPath)) ? Visibility.Collapsed : Visibility.Visible;
            row.Error.Text = text("StoreLibrary.JobFailed"); row.Error.Visibility = job.ErrorCode is null ? Visibility.Collapsed : Visibility.Visible;
            foreach (var pair in row.Buttons)
            {
                var button = pair.Value; var action = pair.Key;
                bool supported = action switch { StoreJobAction.Pause => job.CanPause, StoreJobAction.Resume => job.CanResume, StoreJobAction.Cancel => job.CanCancel, StoreJobAction.DownloadNow => job.CanDownloadNow, _ => false };
                ((TextBlock)button.Content).Text = text("StoreLibrary." + action);
                button.Visibility = supported ? Visibility.Visible : Visibility.Collapsed;
                button.IsEnabled = controller.CanControl(job, action);
            }
            ArrangeDownloadActions(row);
        }
    }
    private static void ArrangeDownloadActions(DownloadRow row)
    {
        var visible = row.Buttons.Where(pair => pair.Value.Visibility == Visibility.Visible).ToArray();
        int columns = Math.Max(1, Math.Min(visible.Length, row.Actions.ActualWidth >= 900 ? 4 : row.Actions.ActualWidth >= 460 ? 2 : 1));
        string signature = columns + ":" + string.Join(",", visible.Select(pair => pair.Key));
        if (signature == row.ActionLayout) return;
        row.ActionLayout = signature;
        row.Actions.ColumnDefinitions.Clear(); row.Actions.RowDefinitions.Clear();
        for (int i = 0; i < columns; i++) row.Actions.ColumnDefinitions.Add(new());
        for (int i = 0; i < (visible.Length + columns - 1) / columns; i++) row.Actions.RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (int i = 0; i < visible.Length; i++) { Grid.SetColumn(visible[i].Value, i % columns); Grid.SetRow(visible[i].Value, i / columns); }
    }
    private void ShowError(string key)
    {
        Status.IsOpen = true; Status.Severity = InfoBarSeverity.Warning; localErrorKey = key; Status.Message = text(key);
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!disposed && active && Status.IsOpen && localErrorKey == key) Status.StartBringIntoView();
        });
    }
    private static void Danger(Button button)
    {
        var red = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 196, 43, 28));
        var white = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
        foreach (string key in new[] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "AccentButtonBackgroundPointerOver", "AccentButtonBackgroundPressed", "FocusVisualPrimaryBrush" }) button.Resources[key] = red;
        foreach (string key in new[] { "ButtonForegroundPointerOver", "ButtonForegroundPressed", "AccentButtonForegroundPointerOver", "AccentButtonForegroundPressed" }) button.Resources[key] = white;
        button.GotFocus += (_, _) => { if (button.FocusState is FocusState.Keyboard or FocusState.Programmatic) { button.Background = red; button.Foreground = white; } };
        button.LostFocus += (_, _) => { button.ClearValue(Control.BackgroundProperty); button.ClearValue(Control.ForegroundProperty); };
        if (button.FocusState is FocusState.Keyboard or FocusState.Programmatic) { button.Background = red; button.Foreground = white; }
    }
    private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private async Task CancelDownloadAsync(StoreDownloadJob job)
    {
        if (controller is null || dialogOpen) return;
        await controller.CancelAsync(job, async () =>
        {
            dialogOpen = true;
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = text("StoreLibrary.ConfirmCancelDownload"), Content = Label(job.Title),
                PrimaryButtonText = text("StoreLibrary.Yes"), CloseButtonText = text("StoreLibrary.No"), DefaultButton = ContentDialogButton.Close };
            if (Application.Current.Resources.TryGetValue("PlayhubSecondaryButtonStyle", out var style) && style is Style s) dialog.PrimaryButtonStyle = s;
            dialog.Opened += (_, _) => { foreach (var button in Descendants(dialog).OfType<Button>().Where(b => b.Name == "PrimaryButton")) Danger(button); };
            try { return await dialog.ShowAsync() == ContentDialogResult.Primary; }
            finally { dialogOpen = false; }
        });
    }
    private async void ClearCompletedClicked(object sender, RoutedEventArgs args)
    {
        if (controller is null || !controller.CanClearCompleted || dialogOpen) return;
        localErrorKey = localNoticeKey = null; localErrorStore = localNoticeStore = null;
        if (await controller.ClearCompletedAsync()) localNoticeKey = "StoreLibrary.CompletedCleared";
        if (!disposed) Render();
    }
    private async Task ConnectAsync(StoreKind kind)
    {
        if (controller is null || dialogOpen) return;
        dialogOpen = true;
        try
        {
            while (!disposed && !lifetime.IsCancellationRequested)
            {
                var session = await controller.BeginConnectAsync(kind);
                if (session is null) return;
                var outcome = await StoreSignInWindow.ShowAsync(session, text, lifetime.Token);
                if (outcome.Outcome == StoreSignInOutcome.RetryRequested) { controller.CancelConnect(); continue; }
                string? code = outcome.Code;
                if (outcome.Outcome != StoreSignInOutcome.Authorized || string.IsNullOrWhiteSpace(code)) { controller.CancelConnect(); return; }
                bool connected;
                try { connected = await controller.ConnectAndLoadLibraryAsync(session, code); }
                finally { code = null; }
                if (connected && !disposed)
                {
                    LibraryTabs.SelectedItem = GamesTab;
                    StoreFilter.SelectedItem = StoreFilter.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, kind.ToString()));
                    InstallFilter.SelectedIndex = 0; Search.Text = ""; ShowTab(); Render();
                    GamesPanel.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = true });
                }
                return;
            }
        }
        finally { dialogOpen = false; }
    }
    private async Task ConnectXboxAsync()
    {
        if (controller is null || xboxAccountService is null || disposed || dialogOpen) return;
        var account = controller.Snapshot.Accounts.FirstOrDefault(a => a.Store == StoreKind.Xbox);
        if (account is null || !account.Capabilities.CanConnect) return;
        dialogOpen = true;
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        xboxSignInLifetime = attempt; Render();
        var signIn = new StoreXboxSignInController(xboxAccountService);
        try
        {
            var session = await signIn.BeginAsync(account.Revision, attempt.Token);
            if (session is null || disposed) { if (!disposed && !attempt.IsCancellationRequested) ShowError("StoreLibrary.ConnectFailed"); return; }
            var result = await StoreXboxSignInWindow.ShowAsync(signIn, session, text, attempt.Token);
            if (disposed) return;
            await controller.ReadAsync();
            if (active && result?.State == StoreXboxSignInState.Connected)
            {
                LibraryTabs.SelectedItem = GamesTab;
                StoreFilter.SelectedItem = StoreFilter.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, StoreKind.Xbox.ToString()));
                InstallFilter.SelectedIndex = 0; Search.Text = ""; ShowTab();
            }
            else if (result is { State: not (StoreXboxSignInState.Canceled or StoreXboxSignInState.Connected) }) ShowError("StoreLibrary.ConnectFailed");
        }
        catch (OperationCanceledException) when (attempt.IsCancellationRequested) { }
        catch { if (!disposed) ShowError("StoreLibrary.ConnectFailed"); }
        finally { await signIn.CancelAsync(); if (ReferenceEquals(xboxSignInLifetime, attempt)) xboxSignInLifetime = null; dialogOpen = false; if (!disposed) { accountsSignature = ""; Render(); } }
    }
    private bool CanOpenXboxOwned(StoreLibraryGame game) => controller is not null && xboxAccountService is not null
        && !disposed && !dialogOpen && !controller.Busy && game.Key.Store == StoreKind.Xbox
        && !game.IsInstalled && game.Capabilities.InstallKind == StoreInstallKind.ExternalHandoff
        && controller.Snapshot.Games.Any(g => g.Key == game.Key && g.AccountRevision == game.AccountRevision && !g.IsInstalled)
        && controller.Snapshot.Accounts.Any(a => a.Store == StoreKind.Xbox && a.State == StoreConnectionState.Connected && a.Revision == game.AccountRevision);
    private async Task OpenXboxOwnedAsync(StoreLibraryGame game)
    {
        if (!CanOpenXboxOwned(game)) return;
        dialogOpen = true;
        try
        {
            var owned = await xboxAccountService!.GetXboxOwnedLibraryAsync(lifetime.Token);
            if (disposed || owned.IsStale || owned.ErrorCode is not null || owned.Revision < 0
                || owned.Games.Count(g => g.ProductId == game.Key.Id && g.SupportsPc) != 1) { if (!disposed) ShowError("GamePass.OpenFailed"); return; }
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = game.Title, Content = Label(text("StoreLibrary.WindowsManagedHelp")),
                PrimaryButtonText = text("StoreLibrary.Install"), CloseButtonText = text("StoreLibrary.Cancel"), DefaultButton = ContentDialogButton.Close };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || disposed) return;
            await controller!.ReadAsync();
            if (disposed || !controller.Snapshot.Accounts.Any(a => a.Store == StoreKind.Xbox && a.State == StoreConnectionState.Connected && a.Revision == game.AccountRevision)
                || !controller.Snapshot.Games.Any(g => g.Key == game.Key && g.AccountRevision == game.AccountRevision)) return;
            var request = new StoreXboxHandoffRequest(game.Key.Id, owned.Revision, Guid.NewGuid());
            var result = await xboxAccountService.OpenXboxOwnedInstallPageAsync(request, lifetime.Token);
            if (disposed) return;
            if (result.ProductId != request.ProductId || result.RequestId != request.RequestId || !result.Accepted || result.State is not ("official-page-requested" or "official-install-requested"))
                ShowError(result.State == "outcome-unknown" ? "GamePass.OpenUnknown" : "GamePass.OpenFailed");
            else
            {
                localErrorKey = null; localNoticeKey = result.State == "official-install-requested" ? "StoreLibrary.WindowsManagedRequested" : "GamePass.OpenRequested";
                if (result.State == "official-install-requested") { await controller.ReadAsync(); LibraryTabs.SelectedItem = DownloadsTab; ShowTab(); }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch { if (!disposed) ShowError("GamePass.OpenFailed"); }
        finally { dialogOpen = false; if (!disposed) Render(); }
    }
    private async Task InstallAsync(StoreLibraryGame game)
    {
        if (controller is null || dialogOpen || !controller.CanInstall(game)) return;
        var initial = await controller.PrepareInstallAsync(game); if (initial is null) return;
        dialogOpen = true;
        try
        {
            StoreInstallPreparation reviewed = initial;
            if (initial.InstallKind == StoreInstallKind.Direct)
            {
                var path = new TextBox { Header = text("StoreLibrary.Folder"), Text = initial.SuggestedPath ?? "" };
                var drives = new ComboBox { Header = text("StoreLibrary.Drive"), HorizontalAlignment = HorizontalAlignment.Stretch };
                foreach (var l in initial.Locations.Where(l => l.IsReady)) drives.Items.Add(new ComboBoxItem { Content = l.DisplayName + " \u00B7 " + StoreLibraryPresentation.Bytes(l.FreeBytes) + " " + text("StoreLibrary.Free"), Tag = l.RootPath });
                string? suggestedRoot = initial.SuggestedPath is string suggested ? Path.GetPathRoot(suggested) : null;
                drives.SelectedItem = drives.Items.OfType<ComboBoxItem>().FirstOrDefault(i => string.Equals(i.Tag?.ToString(), suggestedRoot, StringComparison.OrdinalIgnoreCase));
                drives.SelectionChanged += (_, _) => { if (drives.SelectedItem is ComboBoxItem { Tag: string root }) path.Text = StoreInstallDestination.ChangeDrive(root, path.Text, game.Key.Store, game.Title); };
                var body = new StackPanel { Spacing = 14 }; body.Children.Add(Label(text("StoreLibrary.FolderHelp"), opacity: .8)); body.Children.Add(drives); body.Children.Add(path);
                var browse = Action("Browse", async () => { string? selected = pickFolder is null ? null : await pickFolder(); if (!string.IsNullOrWhiteSpace(selected)) path.Text = StoreInstallDestination.FromLibraryFolder(selected, game.Title); }); body.Children.Add(browse);
                var chooser = new ContentDialog { XamlRoot = XamlRoot, Title = game.Title, Content = body, PrimaryButtonText = text("StoreLibrary.Review"), CloseButtonText = text("StoreLibrary.Cancel"), DefaultButton = ContentDialogButton.None };
                if (await chooser.ShowAsync() != ContentDialogResult.Primary) return;
                var prepared = await controller.PrepareInstallAsync(game, path.Text); if (prepared is null) return; reviewed = prepared;
                if (reviewed.SelectionNonce is null || reviewed.ConfirmedPath is null) { ShowError("StoreLibrary.PrepareFailed"); return; }
            }
            var review = new StackPanel { Spacing = 12 };
            if (reviewed.InstallKind == StoreInstallKind.ExternalHandoff) review.Children.Add(Label(text("StoreLibrary.ExternalHelp")));
            else
            {
                review.Children.Add(Label(text("StoreLibrary.Folder") + ": " + reviewed.ConfirmedPath));
                string root = Path.GetPathRoot(reviewed.ConfirmedPath!) ?? "";
                var location = reviewed.Locations.FirstOrDefault(l => l.RootPath.Equals(root, StringComparison.OrdinalIgnoreCase));
                review.Children.Add(Label(text("StoreLibrary.Free") + ": " + (location is null ? text("StoreLibrary.Unknown") : StoreLibraryPresentation.Bytes(location.FreeBytes))));
                review.Children.Add(Label(text("StoreLibrary.Required") + ": " + (reviewed.RequiredBytes is long required ? StoreLibraryPresentation.Bytes(required) : text("StoreLibrary.Unknown"))));
            }
            var confirm = new ContentDialog { XamlRoot = XamlRoot, Title = game.Title, Content = review, PrimaryButtonText = text("StoreLibrary." + (reviewed.InstallKind == StoreInstallKind.ExternalHandoff ? "OpenStore" : "Install")),
                CloseButtonText = text("StoreLibrary.Cancel"), DefaultButton = ContentDialogButton.None };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
            if (await controller.InstallAsync(game, reviewed, language)) { LibraryTabs.SelectedItem = DownloadsTab; ShowTab(); }
        }
        finally { dialogOpen = false; }
    }
    private void ShowTab()
    {
        bool accounts = LibraryTabs.SelectedItem == AccountsTab, jobs = LibraryTabs.SelectedItem == DownloadsTab, catalogue = LibraryTabs.SelectedItem == GamePassTab;
        AccountsPanel.Visibility = accounts ? Visibility.Visible : Visibility.Collapsed;
        GamesPanel.Visibility = !accounts && !jobs && !catalogue ? Visibility.Visible : Visibility.Collapsed;
        DownloadsPanel.Visibility = jobs ? Visibility.Visible : Visibility.Collapsed;
        GamePassPanel.Visibility = catalogue ? Visibility.Visible : Visibility.Collapsed;
        gamePass?.SetActive(active && catalogue);
        UpdateInstalledButtons();
    }
    private void TabsChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) { if (GamesPanel is not null) ShowTab(); }
    private void FilterChanged(object sender, SelectionChangedEventArgs args) { if (controller is not null && !rendering) Render(); }
    private void SearchChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) { if (controller is not null) Render(); }
    private async void RefreshClicked(object sender, RoutedEventArgs args) { if (controller is not null) await RefreshLibraryAsync(); }
    private async Task RefreshLibraryAsync()
    {
        if (refreshingLibrary || disposed || controller is null) return;
        refreshingLibrary = true; localErrorKey = null;
        try
        {
            await controller.RefreshLibrariesAsync();
            if (xboxService is not null)
            {
                try { xboxInstalled = await xboxService.RefreshXboxInstalledLibraryAsync(lifetime.Token); }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
                catch { ShowError("StoreLibrary.ReadFailed"); }
            }
            if (!disposed) Render();
        }
        finally { refreshingLibrary = false; }
    }
    private async Task OpenXboxAsync()
    {
        if (xboxService is null || disposed) return;
        try
        {
            var snapshot = await xboxService.RefreshXboxInstalledLibraryAsync(lifetime.Token);
            xboxInstalled = snapshot;
            var result = await xboxService.OpenXboxAppAsync(new(snapshot.Revision, Guid.NewGuid()), lifetime.Token);
            if (!result.Accepted) ShowError("StoreLibrary.CommandFailed");
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch { ShowError("StoreLibrary.CommandFailed"); }
    }
    public void Dispose() { if (disposed) return; disposed = true; lifetime.Cancel(); lifetime.Dispose(); timer.Stop(); installedActionDialog?.Hide(); installedActions?.Dispose(); installedRows.Clear(); viewport?.Dispose(); gamePass?.Dispose(); downloadRows.Clear(); DownloadRows.Children.Clear(); if (controller is not null) controller.Changed -= ControllerChanged; controller?.Dispose(); }
}

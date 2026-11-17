// SPDX-License-Identifier: MIT. Original Playhub Game Pass catalogue UI.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Playhub.Importing;
using System.Globalization;

namespace Playhub.StoreLibrary;

public sealed partial class XboxGamePassView : UserControl, IDisposable
{
    private XboxGamePassController? controller;
    private Func<string, string> text = key => key;
    private Func<double> aspect = () => 1.5;
    private Func<StoreXboxCatalogGame, string?>? artwork;
    private GameCardViewport<StoreXboxCatalogGame>? viewport;
    private readonly List<Button> openButtons = [];
    private sealed class CoverState(StoreXboxCatalogGame game, Border cover)
    {
        internal readonly StoreXboxCatalogGame Game = game;
        internal readonly Border Cover = cover;
        internal string? Path;
    }
    private readonly Dictionary<FrameworkElement, CoverState> covers = new();
    private string signature = "", language = "", pendingFocus = "";
    private bool active, disposed, dialogOpen;

    public XboxGamePassView() { InitializeComponent(); }
    internal XboxGamePassView(IStoreXboxCatalogService service, Func<string, string> translate,
        Func<double> coverAspect, Func<StoreXboxCatalogGame, string?>? resolveArtwork) : this()
    {
        controller = new(service); text = translate; aspect = coverAspect; artwork = resolveArtwork;
        controller.Changed += Changed;
        Loaded += (_, _) => Render();
        Unloaded += (_, _) => { controller.CancelPending(); ReleaseCards(); };
        Relocalize();
    }
    internal void Relocalize()
    {
        language = text("GamePass.Help"); Description.Text = language;
        Search.PlaceholderText = text("GamePass.Search");
        AutomationProperties.SetName(Search, text("GamePass.Search"));
        Refresh.Content = text("StoreLibrary.Refresh"); signature = ""; Render();
    }
    internal void SetActive(bool value)
    {
        if (disposed) return;
        bool entering = value && !active; active = value;
        if (!value) { controller?.CancelPending(); return; }
        if (entering)
        {
            // Covers may have completed while another tab was visible. Consume
            // the cached results without rebuilding rows or losing their anchor.
            RefreshArtwork();
            _ = ActivateAsync();
        }
    }
    private async Task ActivateAsync()
    {
        if (controller is null) return;
        await controller.ReadAsync();
        if (active && !disposed && (controller.Snapshot is null || controller.Snapshot.IsStale))
            await controller.RefreshIfStaleAsync();
    }
    internal void RefreshArtwork()
    {
        if (!disposed && active)
            foreach (var cover in covers.Values.ToArray()) UpdateCover(cover);
    }
    private void Changed(object? sender, EventArgs args)
    {
        if (disposed) return;
        if (DispatcherQueue.HasThreadAccess) Render(); else DispatcherQueue.TryEnqueue(Render);
    }
    private void Render()
    {
        if (disposed || controller is null) return;
        Loading.IsActive = controller.Busy;
        Loading.Visibility = controller.Busy ? Visibility.Visible : Visibility.Collapsed;
        Refresh.IsEnabled = !controller.Busy;
        var held = controller.Snapshot;
        foreach (var button in openButtons) button.IsEnabled = !controller.Busy && !dialogOpen
            && held?.Games.Any(game => game.ProductId == button.Tag as string && game.CanOpenOfficialPage) == true;
        string? message = controller.ErrorKey ?? controller.NoticeKey;
        Status.IsOpen = message is not null || held?.IsStale == true && held.Games.Count > 0
            && held.SourceWarning != "transient_response_not_cached";
        Status.Severity = controller.ErrorKey is not null ? InfoBarSeverity.Warning : InfoBarSeverity.Informational;
        Status.Message = message is not null ? text(message) : text("GamePass.Stale");
        CacheDate.Text = held?.ValidatedAt is { } date
            ? string.Format(CultureInfo.CurrentCulture, text("GamePass.Updated"), date.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)) : "";
        var games = controller.VisibleGames(Search.Text).ToArray();
        Summary.Text = string.Format(CultureInfo.CurrentCulture, text("GamePass.Count"), games.Length);
        Empty.Visibility = games.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        bool searching = !string.IsNullOrWhiteSpace(Search.Text);
        EmptyTitle.Text = text(searching ? "GamePass.NoResults" : "GamePass.Empty");
        EmptyBody.Text = searching ? "" : text("GamePass.EmptyHelp");
        string next = (held?.SourceRevision ?? "") + "\n" + Search.Text + "\n" + language;
        if (next == signature && viewport is not null) { RefreshArtwork(); return; }
        if (XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is Button { Tag: string id } && openButtons.Any(b => Equals(b.Tag, id))) pendingFocus = id;
        signature = next; ReleaseCards();
        if (games.Length == 0) return;
        viewport = new(games, BuildCard, ReleaseCard, aspect, Cards.ActualWidth);
        Cards.Children.Add(viewport.View);
    }
    private FrameworkElement BuildCard(StoreXboxCatalogGame game)
    {
        var content = new StackPanel { Spacing = 10 };
        var padding = Application.Current.Resources.TryGetValue("PlayhubCardPadding", out var p) && p is Thickness t ? t : new Thickness(16);
        var cover = new Border { CornerRadius = new(5), Height = Math.Max(120, (GameCardLayout.CardWidth(Cards.ActualWidth) - padding.Left - padding.Right) * aspect()),
            Background = Application.Current.Resources["ControlFillColorSecondaryBrush"] as Brush };
        var coverState = new CoverState(game, cover);
        UpdateCover(coverState);
        content.Children.Add(cover);
        content.Children.Add(new TextBlock { Text = game.Title, FontSize = GameCardLayout.TitleFontSize(GameCardLayout.SizeStep),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var button = new Button { Content = new TextBlock { Text = text("StoreLibrary.OpenStore"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center }, Tag = game.ProductId, MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = game.CanOpenOfficialPage && controller?.Busy == false && !dialogOpen };
        if (Application.Current.Resources.TryGetValue("PlayhubPrimaryButtonStyle", out var s) && s is Style style) button.Style = style;
        AutomationProperties.SetAutomationId(button, "GamePass-" + game.ProductId);
        AutomationProperties.SetName(button, game.Title + ", " + text("StoreLibrary.OpenStore"));
        button.Click += async (_, _) => await OpenAsync(game);
        button.Loaded += (_, _) => { if (pendingFocus == game.ProductId && active) { pendingFocus = ""; button.Focus(FocusState.Programmatic); } };
        openButtons.Add(button); content.Children.Add(button);
        var card = new Border { Child = content, HorizontalAlignment = HorizontalAlignment.Stretch };
        if (Application.Current.Resources.TryGetValue("PlayhubCardBorderStyle", out var a) && a is Style cardStyle) card.Style = cardStyle;
        covers.Add(card, coverState);
        return card;
    }
    private void UpdateCover(CoverState state)
    {
        string? path = artwork?.Invoke(state.Game);
        if (path is not null && File.Exists(path))
        {
            if (path == state.Path) return;
            state.Path = path;
            var bitmap = new BitmapImage { DecodePixelWidth = 480 };
            var image = new Image { Source = bitmap, Stretch = Stretch.UniformToFill };
            bitmap.ImageFailed += (_, _) =>
            {
                if (ReferenceEquals(state.Cover.Child, image))
                {
                    state.Path = null;
                    state.Cover.Child = new FontIcon { Glyph = "\uE7FC", FontSize = 42, Opacity = .55 };
                }
            };
            state.Cover.Child = image;
            bitmap.UriSource = new Uri(path);
        }
        else if (state.Cover.Child is null) state.Cover.Child = new FontIcon { Glyph = "\uE7FC", FontSize = 42, Opacity = .55 };
    }
    private void ReleaseCard(FrameworkElement card)
    {
        covers.Remove(card);
        if (card is Border { Child: StackPanel body })
            foreach (var button in body.Children.OfType<Button>()) openButtons.Remove(button);
    }
    private async Task OpenAsync(StoreXboxCatalogGame game)
    {
        if (controller is null || controller.Busy || disposed || dialogOpen || XamlRoot is null) return;
        long revision = controller.Snapshot?.Revision ?? -1;
        dialogOpen = true; Render();
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = game.Title,
            Content = new TextBlock { Text = text("GamePass.Confirm"), TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = text("StoreLibrary.OpenStore"), CloseButtonText = text("StoreLibrary.Cancel"), DefaultButton = ContentDialogButton.None };
        try { if (await dialog.ShowAsync() == ContentDialogResult.Primary && active && !disposed) await controller.OpenAsync(game.ProductId, revision); }
        finally { dialogOpen = false; if (!disposed) Render(); }
    }
    private void SearchChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) { if (controller is not null) Render(); }
    private async void RefreshClicked(object sender, RoutedEventArgs args) { if (controller is not null) await controller.RefreshAsync(); }
    private void ReleaseCards() { viewport?.Dispose(); viewport = null; openButtons.Clear(); Cards.Children.Clear(); }
    public void Dispose() { if (disposed) return; disposed = true; active = false; if (controller is not null) controller.Changed -= Changed; controller?.Dispose(); ReleaseCards(); }
}

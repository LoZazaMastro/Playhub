using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Playhub.Models;
using Playhub.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Playhub;

public sealed partial class MainWindow
{
    internal async Task RunUiReviewAsync()
    {
        var output = Path.Combine(AppContext.BaseDirectory, "ui-review");
        Directory.CreateDirectory(output);
        var results = new List<string>();
        var timings = new List<double>();
        var welcomeFrameGaps = new List<double>();
        var storeFrameGaps = new List<double>();
        var countdownSamples = new List<(double ActiveSeconds, double Fraction)>();
        var failures = new List<string>();
        void Check(bool valid, string name)
        {
            results.Add((valid ? "PASS " : "FAIL ") + name);
            if (!valid) failures.Add(name);
            Diag.Step("UI review: " + results[^1]);
        }
        try
        {
            var installedDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Playhub");
            var isolated = !string.Equals(Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(installedDirectory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
            Check(isolated, "UI review runs outside the installed production directory");
            if (!isolated) throw new InvalidOperationException("Publish UI review to an isolated directory; do not replace the installed Playhub.");
            if (_appWindow?.Presenter is OverlappedPresenter presenter) presenter.Maximize();
            _settings.Language = "it";
            _settings.AccentColor = "#FFCB0F";
            ApplyTheme();
            ApplyLanguage();
            if (Environment.GetEnvironmentVariable("PLAYHUB_REVIEW_UPDATE_SCROLL_ONLY") == "1")
            {
                await ReviewNativeUpdateScrollAsync(output, Check);
                return;
            }
            if (Environment.GetEnvironmentVariable("PLAYHUB_REVIEW_LANGUAGES_ONLY") == "1")
            {
                await ReviewLocalizationAsync(output, Check);
                return;
            }
            if (Environment.GetEnvironmentVariable("PLAYHUB_REVIEW_SCREENSHOTS_ONLY") == "1")
            {
                await ReviewWelcomeFinalAsync(output, Check);
                await ReviewDeckyBusyAsync(output, Check);
                await ReviewPluginScreenshotsAsync(output, Check);
                await ReviewPluginCoverFallbackAsync(Check);
                await ReviewFeaturedArtworkAsync(output, Check);
                await ReviewPluginActionWidthAsync(Check);
                await ReviewPlayhubUpdateDialogAsync(output, Check);
                await ReviewSupportReminderAsync(output, Check);
                await ReviewClockRenderingAsync(output, Check, countdownSamples);
                return;
            }
            if (Environment.GetEnvironmentVariable("PLAYHUB_REVIEW_PANELS_ONLY") == "1")
            {
                await ReviewPageHeadersAsync(output, Check);
                await ReviewPlayhubUpdateDialogAsync(output, Check);
                await ReviewSupportReminderAsync(output, Check);
                return;
            }
            if (Environment.GetEnvironmentVariable("PLAYHUB_REVIEW_LAYOUT_ONLY") == "1")
            {
                ShowPage("welcome");
                await Task.Delay(700);
                var artwork = ReviewDescendants(_welcomeRoot).OfType<Image>().ToArray();
                Check(artwork.Length == 7 && artwork.All(image => image.Height == 310 && image.Width == 620),
                    "all welcome slides use the same artwork dimensions");
                bool HasScale(Image image, double expected) => image.RenderTransform is ScaleTransform scale &&
                    Math.Abs(scale.ScaleX - expected) < .0001 && Math.Abs(scale.ScaleY - expected) < .0001;
                Check(HasScale(artwork[1], 1.3) &&
                    artwork[1].RenderTransformOrigin == new Windows.Foundation.Point(.5, 1) &&
                    HasScale(artwork[6], 1.2) &&
                    artwork[6].RenderTransformOrigin == new Windows.Foundation.Point(.5, 1) &&
                    artwork.Where((_, index) => index != 1 && index != 6).All(image => HasScale(image, 1)),
                    "Decky artwork is thirty percent larger and final artwork twenty percent larger");
                foreach (var slide in new[] { 0, 1, 5, 6 })
                {
                    _navigateWelcomeSlide!(slide);
                    await Task.Delay(500);
                    var bounds = artwork[slide].TransformToVisual(_welcomeRoot).TransformBounds(new Windows.Foundation.Rect(0, 0,
                        artwork[slide].ActualWidth, artwork[slide].ActualHeight));
                    Check(bounds.Y >= 0 && bounds.Bottom < _welcomeRoot.ActualHeight,
                        "welcome artwork stays inside viewport " + slide);
                    Check(Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(_welcomeRoot.Children[0]).Opacity == 1,
                        "welcome background is consistent " + slide);
                    await ReviewCaptureAsync((FrameworkElement)Content, Path.Combine(output, "welcome-layout-" + slide + ".png"));
                }
                return;
            }
            Check(IsIntegratedGamingModePlugin(new DeckyPluginInfo { Name = "Gaming Mode" }) &&
                IsIntegratedGamingModePlugin(new DeckyPluginInfo { InstalledFolder = @"C:\fixture\gaming-mode" }) &&
                !IsIntegratedGamingModePlugin(new DeckyPluginInfo { Name = "Volume Mixer" }),
                "integrated Gaming Mode excluded without hiding ordinary plugins");
            Check(!IsStoreNotificationContext(), "internal notifications enabled outside store");
            using (BeginNotificationContext("plugins"))
            {
                _status.IsOpen = false;
                SetStatus("Store operation completed", InfoBarSeverity.Success);
                Check(!_status.IsOpen, "ordinary store operations do not show dialogs");
            }
            using (BeginNotificationContext("decky"))
            {
                await Task.Delay(1);
                SetStatus("Decky operation completed", InfoBarSeverity.Success);
                Check(_status.IsOpen, "Decky retains internal notifications");
                _status.IsOpen = false;
            }
            ShowPage("welcome");
            await Task.Delay(600);
            ReviewAccentSwatches(_welcomeRoot, "welcome", Check);
            ReviewAccentSwatches(_accentColorPanel, "settings", Check);
            Check(ReviewDescendants(_welcomeRoot).OfType<Button>().Where(button => button.Tag is string tag && tag.StartsWith("#"))
                .Select(button => (string)button.Tag).SequenceEqual(ReviewDescendants(_accentColorPanel).OfType<Button>()
                    .Where(button => button.Tag is string tag && tag.StartsWith("#")).Select(button => (string)button.Tag)),
                "settings and welcome expose the same ten accent colors");
            for (var slide = 0; slide < 7; slide++) { _navigateWelcomeSlide!(slide); await Task.Delay(380); }
            _navigateWelcomeSlide!(0); await Task.Delay(380);
            var warmed = _readWelcomeMotionState!();
            var warmedLayout = _readWelcomeMotionDiagnostics!();
            var frameWatch = Stopwatch.StartNew();
            var previousFrame = frameWatch.Elapsed.TotalMilliseconds;
            EventHandler<object> onWelcomeFrame = (_, _) => {
                var now = frameWatch.Elapsed.TotalMilliseconds;
                welcomeFrameGaps.Add(now - previousFrame); previousFrame = now;
            };
            CompositionTarget.Rendering += onWelcomeFrame;
            try
            {
                var items = _navigation.MenuItems.OfType<NavigationViewItem>().ToArray();
                for (var repeat = 0; repeat < 40; repeat++)
                {
                    var item = items[repeat % items.Length];
                    AnimateSidebarIconForReview(item, true, bounce: true);
                    _navigateWelcomeSlide!(repeat % 6 + 1);
                    await Task.Delay(380);
                    AnimateSidebarIconForReview(item, false);
                    _navigateWelcomeSlide!(0);
                    await Task.Delay(340);
                    var motion = _readWelcomeMotionState!();
                    Check(motion.RequestedIndex == 0 && motion.RenderedIndex == 0 && !motion.IsAnimating &&
                        Math.Abs(motion.Opacity - 1) < .01 && Math.Abs(motion.OffsetX) < .01,
                        $"welcome forward/back settles {repeat}: requested={motion.RequestedIndex}, rendered={motion.RenderedIndex}, animating={motion.IsAnimating}, opacity={motion.Opacity:F3}, x={motion.OffsetX:F3}");
                    var sidebar = ReadSidebarIconMotionForReview(item);
                    Check(!sidebar.IsAnimating && !sidebar.Hovered && Math.Abs(sidebar.Scale - 1) < .01 && sidebar.Failures == 0,
                        "sidebar hover and bounce settle " + repeat);
                }
            }
            finally { CompositionTarget.Rendering -= onWelcomeFrame; }
            var settled = _readWelcomeMotionState!();
            var settledLayout = _readWelcomeMotionDiagnostics!();
            Check(settledLayout.ActiveLayers == 0 && settledLayout.Failures == 0 &&
                settledLayout.SizeChanges == warmedLayout.SizeChanges,
                $"welcome transitions reuse layout: changes={settledLayout.SizeChanges - warmedLayout.SizeChanges}, failures={settledLayout.Failures}, maxCompletionMs={settledLayout.MaxCompletionMs:F1}");
            Check(settled.ArtworkSources == warmed.ArtworkSources && settled.ArtworkOpened == warmed.ArtworkOpened,
                "welcome reuses decoded artwork across forty round trips");
            for (var repeat = 0; repeat < 40; repeat++) {
                _navigateWelcomeSlide!(3); await Task.Delay(20); _navigateWelcomeSlide!(0); await Task.Delay(20);
            }
            await Task.Delay(400);
            Check(_readWelcomeMotionState!() is { RenderedIndex: 0, IsAnimating: false }, "rapid welcome reversals cancel cleanly");
            _navigateWelcomeSlide!(5); await Task.Delay(400);
            var selectedBackdrop = _welcomeBackdropButtons.First(button => (string)button.Tag == NormalizeBackdropKey(_settings.Backdrop));
            var backdropBrush = (SolidColorBrush)selectedBackdrop.Background;
            var priorColor = backdropBrush.Color;
            _settings.AccentColor = "#6FCF97";
            ApplyAccentResources(ParseColor(_settings.AccentColor)); RefreshWelcomeBackdrop();
            Check(ReferenceEquals(selectedBackdrop.Background, backdropBrush) && backdropBrush.Color == priorColor,
                "backdrop accent begins from current color on retained brush");
            await Task.Delay(150);
            Check(backdropBrush.Color != priorColor && backdropBrush.Color != ParseColor(_settings.AccentColor),
                "backdrop accent has an intermediate fade color");
            await Task.Delay(240);
            Check(backdropBrush.Color == ParseColor(_settings.AccentColor), "backdrop accent reaches selected color");
            Check(_welcomeBackdropButtons.Where(button => !ReferenceEquals(button, selectedBackdrop)).All(button =>
                button.Background is SolidColorBrush brush && brush.Color.A == 0 && !ReferenceEquals(brush, backdropBrush)),
                "only the selected backdrop has the accent with independent brushes");
            Check(ReviewDescendants(_welcomeRoot).OfType<TextBlock>().Any(text => text.Text == "Scegli il tuo stile"),
                "cached welcome slides follow the chosen language");
            await ReviewCaptureAsync((FrameworkElement)Content, Path.Combine(output, "welcome-colors.png"));
            _settings.AccentColor = "#FFCB0F"; ApplyTheme();
            _featuredAutoAdvancePointerOver = true;
            var names = new[] { "Launch Curtain", "News", "Now Playing", "Playhub Artworks", "Playhub Metadata",
                "Playhub Notifications", "Quick Settings", "Playhub Surround", "Weather", "Proton VPN" };
            var categories = new[] { "Personalizzazione e media", "Libreria e giochi", "Social e community", "Strumenti e utilità", "Sistema e hardware" };
            for (var i = 0; i < 160; i++)
            {
                var name = names[i % names.Length];
                _plugins.Add(new DeckyPluginInfo
                {
                    Name = i < names.Length ? name : "Review Plugin " + i.ToString("000"),
                    RepositoryName = "review/" + i, Author = "UI Review",
                    RepositoryUrl = "https://github.com/offline-fixture/plugin-" + i,
                    ShortDescription = "A local fixture for preview and navigation verification.",
                    LongDescription = "# Description\nLocal fixture only. No network or installation operations.\n\n- First feature\n- Second feature",
                    CoverImage = Path.Combine(AppContext.BaseDirectory, "Assets", "PluginImages", name + ".jpg"),
                    IsPlayhubPlugin = i < 12, IsInstalled = i % 3 == 0, HasUpdate = i == 0,
                    InstalledVersion = "1.0.0", Version = "1.1.0",
                    ReleasePublishedAt = new DateTime(2026, 1, 1).AddDays(i).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    UpdatedAt = new DateTime(2026, 1, 1).AddDays(160 - i).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Category = categories[i % categories.Length],
                    CatalogSource = i < 12 ? "playhub" : i % 2 == 0 ? "decky-store" : "github",
                    IconGlyph = ((char)0xE7FC).ToString()
                });
            }
            ShowPage("plugins");
            await Task.Delay(800);
            Check(_pluginDiscoveryCategories.Count == 6 && _pluginCards.Children.Count == 6,
                "discovery home contains all six categories");
            Check(_pluginCards.Children.OfType<StackPanel>().All(section =>
                section.Children.OfType<ItemsRepeater>().SelectMany(repeater =>
                    (IEnumerable<IReadOnlyList<DeckyPluginInfo>>)repeater.ItemsSource).Sum(row => row.Count) == 4),
                "each home category shows exactly four plugin cards");
            Check(_pluginDiscoveryCategories.Keys.OrderBy(PluginStoreCategoryOrder).SequenceEqual(
                new[] { "I plugin di Playhub" }.Concat(categories)), "six category display order");
            foreach (var name in new[] { "Quick Settings", "Launch Curtain", "Playhub Notifications", "Playhub Surround", "Weather" })
            {
                var plugin = _plugins.Single(item => item.Name == name);
                Check(PluginBelongsToCategory(plugin, "I plugin di Playhub") &&
                    PluginBelongsToCategory(plugin, "Strumenti e utilità") &&
                    PluginDiscoveryCategory(plugin) == "Strumenti e utilità",
                    "Playhub plugin also belongs to tools: " + name);
                Check(_pluginDiscoveryCategories["I plugin di Playhub"].Order.ContainsKey(PluginStoreKey(plugin)) &&
                    _pluginDiscoveryCategories["Strumenti e utilità"].Order.ContainsKey(PluginStoreKey(plugin)),
                    "home tracks plugin in both Playhub and functional sections: " + name);
            }
            Check(_plugins.Where(plugin => !plugin.IsPlayhubPlugin).All(plugin =>
                !PluginBelongsToCategory(plugin, "I plugin di Playhub")), "external plugins stay out of the Playhub section");
            ShowPluginUpdatesNotification();
            Check(_status.IsOpen && Equals(_status.Tag, "update-notification"), "available updates retain in-app notification");
            SetStatus("Plugin installed", InfoBarSeverity.Success);
            Check(_status.IsOpen && Equals(_status.Tag, "update-notification"), "routine store status does not replace update notification");
            _status.IsOpen = false;
            _featuredAutoAdvancePointerOver = true; UpdateFeaturedAutoAdvanceState();
            Check(_pluginAllLayout == "list", "all plugins defaults to list");
            Check(_pluginBackButton.Visibility == Visibility.Collapsed, "back hidden with empty store history");
            Check(!_pluginSearchExpanded && Math.Abs((_pluginSearchHost?.ActualWidth ?? 0) - 40) < 1,
                "search starts as icon button");
            Check(_pluginFeaturedHost.ActualHeight >= 340, "featured minimum height");
            Check(_featuredPluginKeys.Count == 5, "five featured plugins");
            Check(GetFeaturedPlugins()[0].HasUpdate, "featured update priority");
            Check(_pluginCategoryFilter is null, "categories start on discovery home");
            Check(!ReviewDescendants(_pluginCards).OfType<TextBlock>().Any(text => text.Text == "Visualizza altro"), "no show-more buttons");
            await ReviewCaptureAsync((FrameworkElement)Content, Path.Combine(output, "discover.png"));

            for (var i = 0; i < 40; i++)
            {
                SetPluginSearchExpanded(true, animate: false);
                Check(_pluginFeaturedHost.Visibility == Visibility.Visible, "empty search keeps featured " + i);
                SetPluginSearchExpanded(false, animate: false);
            }
            SetPluginSearchExpanded(true);
            Check(_pluginSearchExpanded && _pluginSearchBox.Visibility == Visibility.Visible &&
                (!MotionEnabled() || _pluginSearchMorph is not null), "search restores its own morph animation");
            _pluginSearchBox.Focus(FocusState.Programmatic);
            await Task.Delay(260);
            await ReviewCaptureAsync((FrameworkElement)Content, Path.Combine(output, "search.png"));
            _pluginSearchBox.Text = "Review Plugin 159";
            await Task.Delay(230);
            Check(_pluginFeaturedHost.Visibility == Visibility.Collapsed, "query hides featured");
            _pluginSearchBox.Text = string.Empty;
            await Task.Delay(230);
            SetPluginSearchExpanded(false);
            Check(_pluginFeaturedHost.Visibility == Visibility.Visible, "clearing search restores featured");
            SetPluginSearchExpanded(true, animate: false);
            CollapseEmptyPluginSearchOutside(_pluginSearchBox);
            Check(_pluginSearchExpanded, "click inside empty search keeps it expanded");
            CollapseEmptyPluginSearchOutside(_pluginCards);
            await Task.Delay(240);
            Check(!_pluginSearchExpanded, "single outside click collapses empty search");
            SetPluginSearchExpanded(true, animate: false);
            _pluginSearchBox.Text = "Review";
            CollapseEmptyPluginSearchOutside(_pluginCards);
            Check(_pluginSearchExpanded, "outside click preserves nonempty search");
            _pluginSearchBox.Text = ""; await Task.Delay(230); SetPluginSearchExpanded(false, animate: false);

            foreach (var category in _pluginDiscoveryCategories.Keys.ToArray())
            {
                Diag.Step("UI review: opening category " + category);
                OpenPluginCategory(category);
                await Task.Delay(60);
                Check(_pluginCategoryFilter == category && _pluginShowAll && _pluginFeaturedHost.Visibility == Visibility.Collapsed,
                    "category navigation: " + category);
                Check(_pluginBackButton.Visibility == Visibility.Visible, "category back available: " + category);
                var toolbarY = _pluginStoreToolbar.TransformToVisual((UIElement)Content).TransformPoint(default).Y;
                _contentScroller.ChangeView(null, 300, null, true); await Task.Delay(80);
                Check(Math.Abs(_pluginStoreToolbar.TransformToVisual((UIElement)Content).TransformPoint(default).Y - toolbarY) < 1,
                    "toolbar stays pinned while category scrolls: " + category);
                var categoryItems = ReviewPluginItems(_pluginAllLayout == "list" ? _pluginAllListCache : _pluginAllCardsCache);
                Check(categoryItems.Count > 0 && categoryItems.All(plugin => PluginBelongsToCategory(plugin, category)),
                    "category results use membership rather than source-only grouping: " + category);
                OpenPluginPage(_plugins.First(plugin => PluginBelongsToCategory(plugin, category)), _pluginCards);
                Check(_currentPageTag == "plugin-detail" && _pluginPageHost.Opacity == 1 &&
                    ReviewHasIdentityTransform(_pluginPageHost), "category opens detail immediately without morph: " + category);
                await Task.Delay(40);
                NavigatePluginStoreBack();
                await Task.Delay(40);
                Check(_pluginCategoryFilter == category, "plugin back preserves category: " + category);
                NavigatePluginStoreBack();
                await Task.Delay(60);
                Check(_pluginCategoryFilter is null && !_pluginShowAll, "category back restores discovery: " + category);
            }

            for (var i = 0; i < 40; i++)
            {
                var watch = Stopwatch.StartNew();
                SwitchPluginStoreMode("manage", animate: false);
                await Task.Delay(20);
                SwitchPluginStoreMode("discover", animate: false);
                await Task.Delay(20);
                timings.Add(watch.Elapsed.TotalMilliseconds - 40);
                Diag.Step("UI review: discover/manage round trip " + i);
            }
            Check(_pluginDiscoverView.Visibility == Visibility.Visible && _pluginManageView.Visibility == Visibility.Collapsed,
                "40 discover/manage round trips");
            await ReviewPluginManagementAsync(Check);

            _pluginShowAll = true;
            _pluginFeaturedHost.Visibility = Visibility.Collapsed;
            for (var i = 0; i < 40; i++)
            {
                _pluginAllLayout = "list"; RenderPluginCards(); await Task.Delay(20);
                _pluginAllLayout = "cards"; RenderPluginCards(); await Task.Delay(20);
                Diag.Step("UI review: grid/list round trip " + i);
            }
            Check(_pluginAllCardsCache is not null && _pluginAllListCache is not null, "40 grid/list round trips with caches");
            _contentScroller.ChangeView(null, 180, null, true);
            await Task.Delay(100);
            var originalOffset = _contentScroller.VerticalOffset;
            var fixture = _plugins[0];
            for (var i = 0; i < 40; i++)
            {
                OpenPluginPage(fixture, _pluginCards);
                Check(_currentPageTag == "plugin-detail" && _pluginPageHost.Opacity == 1 &&
                    ReviewHasIdentityTransform(_pluginPageHost), "plugin page is immediately visible without morph " + i);
                await Task.Delay(30);
                Check(_currentPageTag == "plugin-detail", "open plugin page " + i);
                ClosePluginPage();
                await Task.Delay(30);
            }
            Check(_currentPageTag == "plugins" && _pluginShowAll, "back preserves all-plugins mode");
            await Task.Delay(250);
            Check(Math.Abs(_contentScroller.VerticalOffset - originalOffset) < 2,
                $"back restores scroll (expected {originalOffset:0.0}, actual {_contentScroller.VerticalOffset:0.0})");
            OpenPluginPage(fixture, _pluginCards);
            await Task.Delay(600);
            ReviewPluginDetailLayout(Check, "Playhub");
            await ReviewCaptureAsync((FrameworkElement)Content, Path.Combine(output, "plugin-page.png"));
            Check(_pluginBackButton.Visibility == Visibility.Visible, "contextual back button");
            Check(!ReviewDescendants(_pluginPageHost).OfType<TextBlock>().Any(text => text.Text == "Dettagli"), "no details button");
            ClosePluginPage();
            await Task.Delay(150);

            foreach (var source in new[] { "decky-store", "github" })
            {
                var external = _plugins.First(plugin => !plugin.IsPlayhubPlugin && plugin.CatalogSource == source);
                OpenPluginPage(external, _pluginCards);
                await Task.Delay(350);
                var warning = ReviewDescendants(_pluginPageContent).OfType<FrameworkElement>()
                    .FirstOrDefault(element => element.Tag as string == "external-plugin-warning");
                Check(warning is not null, "external warning: " + source);
                ReviewPluginDetailLayout(Check, source);
                var warningText = warning is null ? "" : string.Join(" ", ReviewDescendants(warning).OfType<TextBlock>().Select(text => text.Text));
                Check(warningText.Contains(source == "decky-store" ? "Decky Store" : "GitHub") &&
                    warningText.Contains("Windows") && warningText.Contains("sviluppatore"), "warning provenance: " + source);
                await ReviewCaptureAsync((FrameworkElement)Content, Path.Combine(output, "plugin-" + source + ".png"));
                _contentScroller.ChangeView(null, _contentScroller.ScrollableHeight, null, true);
                await Task.Delay(100);
                await ReviewCaptureAsync((FrameworkElement)Content, Path.Combine(output, "warning-" + source + ".png"));
                ClosePluginPage();
                await Task.Delay(100);
            }

            var progressPlugin = _plugins[1];
            var installButton = CreatePluginInstallButton(progressPlugin, compact: false);
            var operation = new PluginInstallOperation { Plugin = progressPlugin };
            operation.Progress = new PluginInstallProgress(PluginInstallPhase.Downloading, 47);
            PublishPluginInstallProgress(PluginStoreKey(progressPlugin), operation);
            Check(!installButton.IsEnabled && Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(installButton).Contains("47%"),
                "install button reports progress without executing installation");
            operation.Installed = true;
            PublishPluginInstallProgress(PluginStoreKey(progressPlugin), operation);
            Check(!installButton.IsEnabled && Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(installButton).Contains("Installato"),
                "install button immediately becomes installed");

            var uninstallPlugin = new DeckyPluginInfo { Name = "Uninstall fixture", RepositoryName = "review/uninstall", IsInstalled = true, InstalledVersion = "1.0.0" };
            var uninstallKey = PluginStoreKey(uninstallPlugin);
            var uninstallButtons = new[] {
                BindPluginUninstallButton(new Button(), uninstallPlugin, compact: true),
                BindPluginUninstallButton(new Button(), uninstallPlugin, compact: false)
            };
            _pluginUninstalls.Add(uninstallKey);
            PublishPluginUninstallState(uninstallKey);
            Check(uninstallButtons.All(button => !button.IsEnabled &&
                ReviewDescendants((DependencyObject)button.Content).OfType<ProgressRing>().Single().IsActive),
                "all uninstall buttons show shared spinner during removal");
            PublishPluginUninstallState(uninstallKey, removed: true);
            _pluginUninstalls.Remove(uninstallKey);
            PublishPluginUninstallState(uninstallKey);
            Check(!uninstallPlugin.IsInstalled && uninstallButtons.All(button => !button.IsEnabled &&
                !ReviewDescendants((DependencyObject)button.Content).OfType<ProgressRing>().Single().IsActive),
                "uninstall completion updates every button without network refresh");

            SwitchPluginStoreMode("discover", animate: false);
            await Task.Delay(200);
            var tile = BuildPluginStoreTile(fixture, plugin =>
            {
                OpenPluginPage(plugin, _pluginCards);
                return Task.CompletedTask;
            });
            var featuredActions = BuildPluginStoreActions(fixture, compact: true, includeUninstall: false);
            var featuredButtons = ((Grid)featuredActions).Children.OfType<Button>().ToArray();
            Check(featuredButtons.Length == 2 && featuredButtons.All(button => button.Width == 32) &&
                !featuredButtons.Any(button => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button).Contains("Disinstalla")),
                "featured actions use compact update and GitHub icons without uninstall");
            var tileHost = new Grid { Width = 340, HorizontalAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 56, 56, 56)) };
            tileHost.Children.Add(tile);
            _pluginCards.Children.Insert(0, tileHost);
            _contentScroller.ChangeView(null, 250, null, true);
            await Task.Delay(200);
            await Task.Delay(550);
            Check(ReviewDescendants(tile).OfType<Button>().All(button => button.ActualWidth <= 33 && button.ActualHeight <= 33),
                "closed card buttons are compact 32px controls");
            Check(ReviewDescendants(tile).OfType<TextBlock>().Any(text => text.Text == fixture.ShortDescription), "card always shows description");
            var cardDescription = ReviewDescendants(tile).OfType<TextBlock>().First(text => text.Text == fixture.ShortDescription);
            Check(cardDescription.MaxLines == 2 && cardDescription.ActualHeight <= 41,
                "card reserves two description lines without oversized gray space");
            Check(ReviewDescendants(tile).OfType<Button>().Count() == 3, "updatable installed card has update, uninstall and GitHub icons");
            Check(tile.Opacity == 1 && tile.RenderTransform is not ScaleTransform, "card has one static state without hover scaling");
            var cardText = ReviewDescendants(tile).OfType<TextBlock>().Select(block => block.Text).ToArray();
            Check(!cardText.Any(text => text == fixture.Author || text == fixture.Category ||
                text == fixture.Version || text == fixture.InstalledVersion || text == "v" + fixture.Version ||
                text == "v" + fixture.InstalledVersion), "card omits author, category and version pills");
            Check(tile.MinHeight == 0, "plugin card has no forced minimum height");
            var sourceBadge = ReviewDescendants(tile).OfType<FrameworkElement>()
                .SingleOrDefault(element => Equals(element.Tag, "plugin-card-source-badge"));
            Check(sourceBadge is not null && sourceBadge.HorizontalAlignment == HorizontalAlignment.Right &&
                sourceBadge.VerticalAlignment == VerticalAlignment.Bottom, "card source badge is anchored bottom right");
            if (sourceBadge is not null)
            {
                var badgeBounds = ReviewBounds(sourceBadge, tile);
                var descriptionBounds = ReviewBounds(cardDescription, tile);
                Check(badgeBounds.Top >= descriptionBounds.Bottom - 1 && badgeBounds.Right <= tile.ActualWidth &&
                    badgeBounds.Bottom <= tile.ActualHeight, "source badge follows description without overlap or clipping");
            }
            await ReviewCaptureAsync(tileHost, Path.Combine(output, "plugin-card.png"));
            _pluginCards.Children.Remove(tileHost);
            _contentScroller.ChangeView(null, 0, null, true);
            await Task.Delay(200);
            frameWatch.Restart(); previousFrame = 0;
            EventHandler<object> onStoreFrame = (_, _) => {
                var now = frameWatch.Elapsed.TotalMilliseconds;
                storeFrameGaps.Add(now - previousFrame); previousFrame = now;
            };
            CompositionTarget.Rendering += onStoreFrame;
            try
            {
                _featuredAutoAdvancePointerOver = true; UpdateFeaturedAutoAdvanceState();
                for (var i = 0; i < 40; i++)
                {
                    SlideFeaturedPlugin(1); await Task.Delay(520);
                    SlideFeaturedPlugin(-1); await Task.Delay(520);
                    Diag.Step("UI review: carousel round trip " + i);
                }
            }
            finally { CompositionTarget.Rendering -= onStoreFrame; }
            Check(!_featuredPluginTransitioning && _pluginFeaturedCarouselHost.Children.Count == 1, "40 carousel round trips");
            await ReviewFeaturedCountdownAsync(Check, countdownSamples);
            foreach (var tag in new[] { "decky", "gaming", "xbox", "styler", "settings", "support" })
            {
                ShowPage(tag); await Task.Delay(80);
                Check(_pageHost.Children.OfType<FrameworkElement>().Count(page => page.Visibility == Visibility.Visible) == 1,
                    "one visible page: " + tag);
                Check(_pluginBackButton.Visibility == Visibility.Collapsed, "no back outside store: " + tag);
                NavigatePluginStoreBack();
                Check(_currentPageTag == tag, "back does not navigate other tabs: " + tag);
                if (tag == "support") ReviewSupportCopy(Check);
            }
            ShowPage("gaming"); await Task.Delay(200);
            var gamingTop = ReviewDescendants(_pageHost).OfType<Grid>().First(grid => grid.ColumnDefinitions.Count == 2 &&
                grid.ColumnDefinitions[0].Width.IsStar && grid.ColumnDefinitions[0].Width.Value == 1 && grid.ColumnDefinitions[1].Width.Value == 3 &&
                ReviewDescendants(grid).OfType<TextBlock>().Any(text => text.Text == "Apri il plugin Gaming Mode"));
            Check(Math.Abs(gamingTop.ColumnDefinitions[1].ActualWidth / gamingTop.ColumnDefinitions[0].ActualWidth - 3) < .02,
                "gaming top cards use one-quarter and three-quarter widths");
            Check(Math.Abs(((FrameworkElement)gamingTop.Children[0]).ActualHeight - ((FrameworkElement)gamingTop.Children[1]).ActualHeight) < 1,
                "gaming top cards have equal height");
            await ReviewCaptureAsync((FrameworkElement)Content, Path.Combine(output, "gaming.png"));
            Check(ReviewFeaturedCountdownStopped(), "carousel clock, deadline and compositor stop outside store");
            await ReviewPageHeadersAsync(output, Check);
            await ReviewPluginScreenshotsAsync(output, Check);
            await ReviewPlayhubUpdateDialogAsync(output, Check);
            await ReviewSupportReminderAsync(output, Check);
        }
        catch (Exception ex) { failures.Add(ex.ToString()); }
        finally
        {
            File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new
            {
                passed = failures.Count == 0, checks = results, failures, processId = Environment.ProcessId,
                navigationOverheadMs = timings, welcomeFrameGaps, storeFrameGaps,
                countdownLogicalSamples = countdownSamples.Select(sample => new { sample.ActiveSeconds, sample.Fraction }),
                limitations = new[] {
                    "Countdown samples are logical stopwatch values, not compositor presentation or 120 FPS measurements.",
                    "RenderTargetBitmap captures are not frame-synchronized; compositor pie pixels need real-window verification.",
                    "No real plugin installation, update download or release-note translation provider is invoked."
                },
                viewport = Content.XamlRoot?.Size
            }, new JsonSerializerOptions { WriteIndented = true }));
            Close();
        }
    }

    private async Task ReviewLocalizationAsync(string output, Action<bool, string> check)
    {
        _settings.Language = "en";
        var coldLabel = Body("Strumenti per diagnosi e sviluppo.");
        var coldButton = new Button { Content = "Rimuovi" };
        SetLocalizedToolTip(coldButton, "Chiudi");
        LocalizeElement(coldButton);
        var richRun = new Run { Text = "Aggiungi app" };
        var paragraph = new Paragraph();
        var bold = new Bold();
        bold.Inlines.Add(richRun);
        paragraph.Inlines.Add(bold);
        var rich = new RichTextBlock();
        rich.Blocks.Add(paragraph);
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot, Title = "Scegli una versione di DeckyLoader",
            PrimaryButtonText = "Installa", SecondaryButtonText = "Rimuovi", CloseButtonText = "Chiudi",
            Content = new StackPanel { Children = { Body("Usa questa opzione solo se ti serve una versione precisa.") } }
        };
        ConfigureDialogEntrance(dialog);
        var input = new Microsoft.UI.Xaml.Controls.TextBox
        {
            Header = "Eseguibile di DeckyLoader", PlaceholderText = "Aggiungi app", Text = "Rimuovi"
        };
        var info = new InfoBar { Title = "Installa", Message = "CSS Loader 2.1.2 è pronto. Ora puoi applicare il profilo Playhub." };
        var external = new TextBlock { Text = "Rimuovi", Tag = "noloc" };
        var menuItem = new MenuFlyoutItem { Text = "Aggiungi app" };
        var menu = new MenuFlyout();
        menu.Items.Add(menuItem);
        var menuButton = new Button { Content = "Chiudi", Flyout = menu };
        var popupText = Body("Scegli cosa vedere quando accendi il PC.");
        var popup = new Microsoft.UI.Xaml.Controls.Primitives.Popup { Child = popupText };
        var content = new StackPanel { Children = { coldLabel, coldButton, rich, input, info, external, menuButton } };
        var nativeOnly = new StackPanel();
        void AddNativeOnlyChildren()
        {
            var nativeText = Body("Strumenti per diagnosi e sviluppo.");
            SetLocalizedToolTip(nativeText, "Chiudi");
            nativeOnly.Children.Add(nativeText);
            var nativeRich = new RichTextBlock();
            var nativeParagraph = new Paragraph();
            nativeParagraph.Inlines.Add(new Run { Text = "Aggiungi app" });
            nativeRich.Blocks.Add(nativeParagraph);
            nativeOnly.Children.Add(nativeRich);
            LocalizeElement(nativeOnly);
        }
        AddNativeOnlyChildren();
        foreach (var language in LocalizationService.Languages.Select(item => item.Key))
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            _settings.Language = language;
            ApplyLanguage();
            LocalizeElement(content);
            LocalizeElement(dialog);
            LocalizeElement(popup);
            LocalizeElement(nativeOnly);
            void Equal(string actual, string key, string name)
                => check(actual == T(key), language + ": " + name + " [" + actual + "]");
            Equal(coldLabel.Text, "Strumenti per diagnosi e sviluppo.", "cold-start body preserves original key");
            Equal(((TextBlock)nativeOnly.Children[0]).Text, "Strumenti per diagnosi e sviluppo.", "native-owned body survives wrapper collection");
            Equal((string)ToolTipService.GetToolTip(nativeOnly.Children[0]), "Chiudi", "native-owned tooltip survives wrapper collection");
            Equal(((Run)((Paragraph)((RichTextBlock)nativeOnly.Children[1]).Blocks[0]).Inlines[0]).Text,
                "Aggiungi app", "native-owned run survives wrapper collection");
            Equal((string)coldButton.Content, "Rimuovi", "cold-start button preserves original key");
            Equal((string)ToolTipService.GetToolTip(coldButton), "Chiudi", "cold-start tooltip preserves original key");
            Equal((string)dialog.Title, "Scegli una versione di DeckyLoader", "dialog title");
            Equal(dialog.PrimaryButtonText, "Installa", "dialog primary button");
            Equal(dialog.SecondaryButtonText, "Rimuovi", "dialog secondary button");
            Equal(dialog.CloseButtonText, "Chiudi", "dialog close button");
            Equal(((TextBlock)((StackPanel)dialog.Content).Children[0]).Text,
                "Usa questa opzione solo se ti serve una versione precisa.", "dialog logical content");
            Equal((string)input.Header, "Eseguibile di DeckyLoader", "input header");
            Equal(input.PlaceholderText, "Aggiungi app", "input placeholder");
            check(input.Text == "Rimuovi", language + ": user input remains untouched");
            Equal(richRun.Text, "Aggiungi app", "rich-text nested run");
            Equal(menuItem.Text, "Aggiungi app", "unopened flyout item");
            Equal(popupText.Text, "Scegli cosa vedere quando accendi il PC.", "popup logical content");
            check(external.Text == "Rimuovi", language + ": external text remains untouched");
            var rawStatus = "CSS Loader 2.1.2 è pronto. Ora puoi applicare il profilo Playhub.";
            Equal(info.Message, rawStatus, "formatted status");
            info.Message = "CSS Loader 2.3.4 è pronto. Ora puoi applicare il profilo Playhub.";
            Equal(info.Message, "CSS Loader 2.3.4 è pronto. Ora puoi applicare il profilo Playhub.", "dynamic status callback");
            info.Message = rawStatus;
            coldButton.Content = "Installa";
            Equal((string)coldButton.Content, "Installa", "dynamic button callback");
            coldButton.Content = "Rimuovi";
            if (language != "it")
                check(T("Strumenti per diagnosi e sviluppo.") != "Strumenti per diagnosi e sviluppo." &&
                    T(rawStatus) != rawStatus, language + ": actual translations exist");

            foreach (var page in new[] { "decky", "gaming", "xbox", "styler", "settings", "support" })
            {
                ShowPage(page);
                await Task.Delay(30);
                var visible = _pageHost.Children.OfType<FrameworkElement>().Single(item => item.Visibility == Visibility.Visible);
                var expected = page switch
                {
                    "decky" => "DeckyLoader con console",
                    "gaming" => "Scegli cosa vedere quando accendi il PC.",
                    "xbox" => "Riunisci i tuoi giochi in Steam e completa automaticamente copertine, sfondi e loghi.",
                    "styler" => "Mantieni la versione attuale di Steam. Puoi riattivare gli aggiornamenti in qualsiasi momento.",
                    "settings" => "Playhub verrà riavviato.",
                    _ => "Fai una donazione"
                };
                var rendered = ReviewDescendants(visible).OfType<TextBlock>().Select(item => item.Text).ToArray();
                check(rendered.Contains(T(expected)), language + ": actual page translation " + page);
            }
            if (language is "en" or "de" or "zh")
            {
                ShowPage("gaming");
                await Task.Delay(180);
                await ReviewCaptureAsync((FrameworkElement)Content, Path.Combine(output, "language-" + language + ".png"));
            }
        }
        _settings.Language = "fr";
        LocalizeElement(content);
        coldLabel.Tag = "noloc";
        coldLabel.Text = "Strumenti per diagnosi e sviluppo.";
        check(coldLabel.Text == "Strumenti per diagnosi e sviluppo.", "callback respects noloc set after registration");
        coldLabel.Tag = null;
        content.Tag = "noloc";
        coldLabel.Text = "Aggiungi app";
        check(coldLabel.Text == "Aggiungi app", "callback respects noloc ancestor");
        content.Tag = null;
        var lazyText = new TextBlock { Text = "Aggiungi app" };
        var lazyDialog = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Chiudi", CloseButtonText = "Chiudi" };
        lazyDialog.Opened += (_, _) => lazyDialog.Content = lazyText;
        ConfigureDialogEntrance(lazyDialog);
        var showing = lazyDialog.ShowAsync();
        check(await ReviewWaitUntilAsync(() => lazyText.IsLoaded), "real lazy dialog opens");
        check(lazyText.Text == T("Aggiungi app"), "content introduced while opening dialog is translated");
        lazyDialog.Hide();
        await showing;
        _settings.Language = "it";
        ApplyLanguage();
    }

    private async Task ReviewPluginScreenshotsAsync(string output, Action<bool, string> check)
    {
        var cover = Path.Combine(AppContext.BaseDirectory, "Assets", "PluginImages", "News.jpg");
        var presenter = AppWindow.Presenter as OverlappedPresenter;
        var originalState = presenter?.State;
        var originalSize = AppWindow.Size;
        try
        {
            presenter?.Restore();
            await ReviewWaitUntilAsync(() => Content.XamlRoot is not null);
            var scale = Content.XamlRoot.RasterizationScale;
            AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(960 * scale), (int)(740 * scale)));
            foreach (var source in new[] { "playhub", "decky-store", "github" })
            {
                var plugin = new DeckyPluginInfo
                {
                    Name = "Screenshot layout", CoverImage = cover, IsPlayhubPlugin = source == "playhub",
                    CatalogSource = source, RepositoryUrl = "https://github.com/offline-fixture/plugin",
                    ShortDescription = "Short description", LongDescription = "# Description\nFull plugin description.",
                    ReleaseNotes = "Release notes", Version = "1.0.0",
                    Media = new List<PluginMediaInfo> {
                        new() { Url = new Uri(cover).AbsoluteUri }, new() { Url = new Uri(cover).AbsoluteUri }
                    }
                };
                OpenPluginPage(plugin, _pluginCards);
                await Task.Delay(500);
                var gallery = ReviewDescendants(_pluginPageContent).OfType<Grid>().Single(element => element.Name == "PluginScreenshots");
                var description = ReviewDescendants(_pluginPageContent).OfType<FrameworkElement>().Single(element => element.Name == "PluginDescription");
                var details = (StackPanel)gallery.Parent;
                check(details.Children[0] == gallery && details.Children[1] == description,
                    source + " screenshots precede the complete description and release notes");
                var page = (StackPanel)details.Parent;
                var actions = (FrameworkElement)page.Children[page.Children.IndexOf(details) - 1];
                check(ReviewDescendants(actions).OfType<Button>().Count() >= 2 &&
                    ReviewBounds(gallery, _pluginPageContent).Top >= ReviewBounds(actions, _pluginPageContent).Bottom - 1,
                    source + " screenshots are immediately below installation and GitHub actions");
                check(ReviewDescendants(gallery).OfType<Image>().All(image => image.Stretch == Stretch.Uniform &&
                    image.Source is BitmapSource { PixelWidth: > 0 }), source + " screenshot proportions and decoded images are preserved");
                ReviewPluginDetailLayout(check, source + " with screenshots");
                await ReviewCaptureAsync(_pluginPageContent, Path.Combine(output, "plugin-screenshots-" + source + ".png"));
                plugin.Media.Insert(0, new PluginMediaInfo { Url = new Uri(Path.Combine(AppContext.BaseDirectory, "missing-screenshot.png")).AbsoluteUri });
                OpenPluginPage(plugin, _pluginCards);
                await Task.Delay(400);
                var survivingGallery = ReviewDescendants(_pluginPageContent).OfType<Grid>().Single(element => element.Name == "PluginScreenshots");
                check(survivingGallery.Children.Count == 2 && survivingGallery.ColumnDefinitions.Count == 2,
                    source + " failed image leaves no empty screenshot tile or column");
                plugin.Media.RemoveRange(1, 2);
                OpenPluginPage(plugin, _pluginCards);
                await Task.Delay(400);
                var missingGallery = ReviewDescendants(_pluginPageContent).OfType<Grid>().Single(element => element.Name == "PluginScreenshots");
                check(missingGallery.Visibility == Visibility.Collapsed && missingGallery.Children.Count == 0,
                    source + " all-failed screenshots hide the entire empty gallery");
                plugin.Media.Clear();
                OpenPluginPage(plugin, _pluginCards);
                await Task.Delay(100);
                check(!ReviewDescendants(_pluginPageContent).OfType<FrameworkElement>().Any(element => element.Name == "PluginScreenshots"),
                    source + " plugins without screenshots have no empty gallery");
            }
        }
        finally
        {
            AppWindow.Resize(originalSize);
            if (originalState == OverlappedPresenterState.Maximized) presenter?.Maximize();
        }
    }

    private async Task ReviewClockRenderingAsync(string output, Action<bool, string> check,
        List<(double ActiveSeconds, double Fraction)> samples)
    {
        foreach (var name in new[] { "Quick Settings", "Launch Curtain" })
            _plugins.Add(new DeckyPluginInfo { Name = name, IsPlayhubPlugin = true,
                CoverImage = Path.Combine(AppContext.BaseDirectory, "Assets", "PluginImages", "News.jpg") });
        ShowPage("plugins");
        await Task.Delay(500);
        ReviewStoreViewPreferences(check);
        SetPluginSearchExpanded(false, animate: false);
        SetPluginSearchExpanded(true);
        check(_pluginSearchExpanded && (!MotionEnabled() || _pluginSearchMorph is not null), "search morph starts on expansion");
        await ReviewWaitUntilAsync(() => _pluginSearchMorph is null);
        check(_pluginSearchMorph is null && _pluginSearchShellScale?.ScaleX == 1 &&
            _pluginSearchLeadingOffset?.X == 0, "search expansion settles without moving text alignment");
        CollapseEmptyPluginSearchOutside(_pluginDiscoverButton);
        check(!_pluginSearchExpanded && (!MotionEnabled() || _pluginSearchMorph is not null), "empty search morphs closed after one outside click");
        await ReviewWaitUntilAsync(() => _pluginSearchMorph is null);
        check(_pluginSearchHost?.Width == PluginSearchClosedWidth && _pluginSearchMorph is null, "search collapse releases reserved layout space");
        _pluginStoreHistory.Clear();
        UpdatePluginBackButton();
        await ReviewWaitUntilAsync(() => _pluginBackAnimation is null);
        PushPluginStoreHistory();
        UpdatePluginBackButton();
        check(_pluginBackVisible && (!MotionEnabled() || _pluginBackAnimation is not null), "store back button animates its appearance");
        await ReviewWaitUntilAsync(() => _pluginBackAnimation is null);
        check(_pluginBackAnimation is null && _pluginBackSwitcherOffset.X == 0, "store switcher settles to the right of Back");
        _pluginStoreHistory.Clear();
        UpdatePluginBackButton();
        check(!_pluginBackVisible && (!MotionEnabled() || _pluginBackAnimation is not null), "store switcher animates back when history is exhausted");
        await ReviewWaitUntilAsync(() => _pluginBackAnimation is null);
        check(_pluginBackButton.Visibility == Visibility.Collapsed && _pluginBackSwitcherOffset.X == 0,
            "store switcher returns to its original position");
        await ReviewFeaturedCountdownAsync(check, samples);
        StopFeaturedAutoAdvance();

        var stage = new StackPanel { Spacing = 40, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center };
        var root = new Grid { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 57, 57, 57)) };
        root.Children.Add(stage);
        Content = root;
        var controls = new List<(FrameworkElement Indicator, double Fraction, int Scale)>();
        foreach (var scale in new[] { 1, 4 })
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            stage.Children.Add(row);
            foreach (var fraction in new[] { 1d, .875, .75, .5, .25, .01 })
            {
                var card = new Grid { Width = 96, Height = 96 };
                var indicator = BuildFeaturedAutoAdvanceControl(card);
                indicator.Margin = new Thickness(0);
                indicator.HorizontalAlignment = HorizontalAlignment.Center;
                indicator.VerticalAlignment = VerticalAlignment.Center;
                indicator.RenderTransformOrigin = new Windows.Foundation.Point(.5, .5);
                indicator.RenderTransform = new ScaleTransform { ScaleX = scale, ScaleY = scale };
                card.Children.Add(indicator);
                row.Children.Add(card);
                var state = _featuredAutoAdvanceControls.GetValue(card, _ => throw new InvalidOperationException());
                UpdateFeaturedPie(state, fraction);
                check(state.Pie.Shapes.Count == 1 && state.Pie.Clip is null && state.Geometry.TrimEnd == (float)fraction,
                    $"countdown fraction {fraction} uses one unjoined native shape at {scale}x");
                controls.Add((indicator, fraction, scale));
            }
        }
        Activate();
        await Task.Delay(500);
        File.WriteAllText(Path.Combine(output, "clock-ready.json"), JsonSerializer.Serialize(new {
            ProcessId = Environment.ProcessId, WindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this).ToInt64(),
            Scale = root.XamlRoot.RasterizationScale,
            Circles = controls.Select(item => new { item.Fraction, item.Scale, Bounds = ReviewBounds(item.Indicator, root) })
        }));
        await Task.Delay(5000);
    }

    private void ReviewStoreViewPreferences(Action<bool, string> check)
    {
        _settings.PluginStoreLayouts.Clear();
        _pluginStoreMode = "discover"; _pluginCategoryFilter = null; _pluginShowAll = true;
        _pluginAllLayout = "cards";
        OpenPluginCategory("Strumenti e utilità");
        check(_pluginAllLayout == "list", "a new category has its own default layout");
        _pluginAllLayout = "list";
        OpenPluginCategory("Personalizzazione e media");
        _pluginAllLayout = "cards";
        _pluginAllSource = "github";
        OpenPluginCategory("Strumenti e utilità");
        check(_pluginAllLayout == "list" && _pluginAllSource == "all", "category restores its own layout and resets source to All");
        _pluginAllSource = "github"; InvalidatePluginAllViews(); RenderPluginCards();
        check(ReviewDescendants(_pluginCards).OfType<FrameworkElement>().Any(element => Equals(element.Tag, "plugin-view-selectors")) &&
            ReviewDescendants(_pluginCards).OfType<TextBlock>().Any(text => text.Text == T("Nessun plugin trovato.")),
            "empty category keeps source, sort and layout filters visible");
        SwitchPluginStoreMode("manage", animate: false);
        _pluginAllLayout = "list";
        check(_pluginAllSource == "all", "opening Manage resets source to All");
        RenderPluginManagement();
        var managementHeading = ReviewDescendants(_pluginManageContent).OfType<Grid>().First(element => Equals(element.Tag, "plugin-management-heading"));
        check(managementHeading.Children.OfType<StackPanel>().Any(element => element.VerticalAlignment == VerticalAlignment.Bottom &&
            element.Children.OfType<TextBlock>().Any(block => block.Text == T("Plugin installati"))) &&
            managementHeading.Children.OfType<FrameworkElement>().Any(element => Equals(element.Tag, "plugin-view-selectors")),
            "Manage heading shares the filter row with bottom alignment");
        check(ReviewDescendants(_pluginManageContent).OfType<FrameworkElement>().Any(element => Equals(element.Tag, "plugin-view-selectors")),
            "empty Manage keeps its filters");
        SwitchPluginStoreMode("discover", animate: false);
        _pluginShowAll = true;
        check(_pluginAllLayout == "cards", "All plugins retains its own card view independently of Manage");
        _pluginAllSource = "github"; InvalidatePluginAllViews(); RenderPluginCards();
        check(ReviewDescendants(_pluginCards).OfType<FrameworkElement>().Any(element => Equals(element.Tag, "plugin-view-selectors")),
            "empty All plugins keeps its filters");
        ShowPage("settings"); ShowPage("plugins");
        check(_pluginAllSource == "all" && _pluginAllLayout == "cards", "returning to the Store resets source but remembers layout");
        var saved = JsonSerializer.Serialize(_settings);
        var restored = JsonSerializer.Deserialize<PlayhubSettings>(saved)!;
        check(restored.PluginStoreLayouts.Count == 4 && restored.PluginStoreLayouts["all"] == "cards" &&
            restored.PluginStoreLayouts["manage"] == "list" && restored.PluginStoreLayouts["category:Strumenti e utilità"] == "list" &&
            restored.PluginStoreLayouts["category:Personalizzazione e media"] == "cards", "per-page layouts survive settings serialization");
        OpenPluginCategory("Personalizzazione e media");
        check(_pluginAllLayout == "cards", "second category retains its independent card view");
        SwitchPluginStoreMode("discover", animate: false);
    }

    private static void ReviewAccentSwatches(DependencyObject root, string context, Action<bool, string> check)
    {
        var swatches = ReviewDescendants(root).OfType<Button>()
            .Where(button => button.Tag is string tag && tag.StartsWith("#")).ToArray();
        check(swatches.Length == 10 && swatches.Select(button => button.Tag).Distinct().Count() == 10,
            context + " has exactly ten distinct accent swatches");
        check(swatches.All(button => ToolTipService.GetToolTip(button) is null &&
            !string.IsNullOrWhiteSpace(AutomationProperties.GetName(button))),
            context + " swatches have accessible names without tooltips");
    }

    private void ReviewSupportCopy(Action<bool, string> check)
    {
        var page = _pageHost.Children.OfType<FrameworkElement>().First(element => Equals(element.Tag, "support"));
        var text = ReviewDescendants(page).OfType<TextBlock>().Select(block => block.Text).ToArray();
        foreach (var expected in new[] {
            "Grazie per essere parte di Playhub",
            "Playhub è gratuito e open source. Lo sviluppo, i test e la manutenzione sono sostenuti da una sola persona.",
            "Se Playhub ti è utile e vuoi aiutare il progetto a continuare a crescere, una donazione è sempre apprezzata. Nessun contenuto è bloccato: è semplicemente un modo gentile per sostenere il lavoro che c'è dietro."
        }) check(text.Contains(T(expected)), "support preserves original copy: " + expected);
    }

    private static IReadOnlyList<DeckyPluginInfo> ReviewPluginItems(UIElement? view) =>
        (view as ItemsRepeater)?.ItemsSource switch
        {
            IEnumerable<DeckyPluginInfo> items => items.ToArray(),
            IEnumerable<IReadOnlyList<DeckyPluginInfo>> rows => rows.SelectMany(row => row).ToArray(),
            _ => Array.Empty<DeckyPluginInfo>()
        };

    private void ReviewPluginViewSelectors(DependencyObject root, Action<bool, string> check, string context)
    {
        var selectors = ReviewDescendants(root).OfType<FrameworkElement>()
            .FirstOrDefault(element => Equals(element.Tag, "plugin-view-selectors"));
        check(selectors is not null, context + " exposes shared source, sort and layout selectors");
        if (selectors is null) return;
        var buttons = ReviewDescendants(selectors).OfType<Button>().ToArray();
        var list = buttons.SingleOrDefault(button => AutomationProperties.GetName(button) == T("Visualizzazione elenco"));
        var cards = buttons.SingleOrDefault(button => AutomationProperties.GetName(button) == T("Visualizzazione a schede"));
        check(list is not null && cards is not null && ReferenceEquals(list.Parent, cards.Parent) &&
            Grid.GetColumn(list) == 0 && Grid.GetColumn(cards) == 1,
            context + " places list before grid in the layout selector");
        check(new[] { "Tutti", "Playhub", "Decky", "GitHub" }.All(label =>
            buttons.Any(button => AutomationProperties.GetName(button) == T(label))), context + " has all four source filters");
        var sort = buttons.SingleOrDefault(button => AutomationProperties.GetName(button) == T("Ordina per"));
        check(sort?.Flyout is MenuFlyout flyout && flyout.Items.OfType<MenuFlyoutItem>().Select(item => item.Text)
            .SequenceEqual(new[] { T("Nome"), T("Data di aggiunta"), T("Data di aggiornamento") }),
            context + " exposes name, added-date and updated-date sorting");
    }

    private async Task ReviewPluginManagementAsync(Action<bool, string> check)
    {
        _pluginAllSource = "all"; _pluginAllSort = "name"; _pluginAllLayout = "list";
        _pluginShowAll = true; _pluginCategoryFilter = null;
        InvalidatePluginAllViews(); RenderPluginCards();
        await Task.Delay(80);
        var discoverList = _pluginAllListCache;
        ReviewPluginViewSelectors(_pluginCards, check, "discovery");
        _pluginAllLayout = "cards"; RenderPluginCards();
        await Task.Delay(80);
        var discoverCards = _pluginAllCardsCache;
        SwitchPluginStoreMode("manage", animate: false);
        await Task.Delay(100);
        check(_pluginDiscoverTools.Visibility == Visibility.Visible && _pluginShowAllButton.Visibility == Visibility.Collapsed &&
            _pluginSearchHost is { IsLoaded: true }, "manage exposes search without the discover-only show-all command");
        ReviewPluginViewSelectors(_pluginManageContent, check, "manage");

        void SelectLayout(string layout)
        {
            _pluginAllLayout = layout;
            _pluginManagementDirty = true;
            RenderVisiblePluginView();
        }
        var installed = _plugins.Where(plugin => plugin.IsInstalled && !IsIntegratedGamingModePlugin(plugin)).ToArray();
        SelectLayout("list"); await Task.Delay(60);
        var managedList = _pluginManageListCache;
        check(ReviewPluginItems(managedList).SequenceEqual(installed.OrderBy(plugin => plugin.Name, StringComparer.CurrentCultureIgnoreCase)),
            "manage list includes every installed plugin, ordered by name");
        SelectLayout("cards"); await Task.Delay(60);
        var managedCards = _pluginManageCardsCache;
        check(ReviewPluginItems(managedCards).SequenceEqual(ReviewPluginItems(managedList)),
            "manage grid and list expose identical installed-only results");
        check(discoverList is not null && discoverCards is not null && managedList is not null && managedCards is not null &&
            !ReferenceEquals(discoverList, managedList) && !ReferenceEquals(discoverCards, managedCards) &&
            ReviewPluginItems(discoverList).Count > ReviewPluginItems(managedList).Count,
            "manage caches are separate from discovery and never reuse uninstalled results");
        SelectLayout("list"); await Task.Delay(40);
        SelectLayout("cards"); await Task.Delay(40);
        check(ReferenceEquals(managedList, _pluginManageListCache) && ReferenceEquals(managedCards, _pluginManageCardsCache),
            "manage layout switches reuse both caches");

        foreach (var (source, label) in new[] { ("playhub", "Playhub"), ("decky", "Decky"), ("github", "GitHub"), ("all", "Tutti") })
        {
            var sourceButton = ReviewDescendants(_pluginManageContent).OfType<Button>()
                .First(button => AutomationProperties.GetName(button) == T(label));
            var invoke = new ButtonAutomationPeer(sourceButton).GetPattern(PatternInterface.Invoke) as IInvokeProvider;
            check(invoke is not null, "manage source filter is invokable: " + source);
            invoke?.Invoke();
            check(await ReviewWaitUntilAsync(() => _pluginAllSource == source), "manage source command selects " + source);
            var expected = installed.Where(plugin => source switch
            {
                "playhub" => plugin.IsPlayhubPlugin,
                "decky" => plugin.CatalogSource == "decky-store",
                "github" => !plugin.IsPlayhubPlugin && plugin.CatalogSource != "decky-store",
                _ => true
            }).OrderBy(plugin => plugin.Name, StringComparer.CurrentCultureIgnoreCase);
            check(ReviewPluginItems(_pluginManageCardsCache).SequenceEqual(expected), "manage source filter keeps only matching installed plugins: " + source);
        }
        foreach (var sort in new[] { "added", "updated", "name" })
        {
            _pluginAllSort = sort; InvalidatePluginAllViews(); RenderVisiblePluginView();
            var expected = sort == "name"
                ? installed.OrderBy(plugin => plugin.Name, StringComparer.CurrentCultureIgnoreCase)
                : installed.OrderByDescending(plugin => sort == "added" ? plugin.ReleasePublishedAt : plugin.UpdatedAt, StringComparer.Ordinal)
                    .ThenBy(plugin => plugin.Name, StringComparer.CurrentCultureIgnoreCase);
            check(ReviewPluginItems(_pluginManageCardsCache).SequenceEqual(expected), "manage sorting changes rendered order: " + sort);
        }

        var match = _plugins.First(plugin => plugin.RepositoryName == "review/159");
        SetPluginSearchExpanded(true, animate: false);
        _pluginSearchBox.Text = match.Name;
        check(await ReviewWaitUntilAsync(() => _pluginManageQuery == match.Name &&
            ReviewPluginItems(_pluginManageCardsCache).SequenceEqual(new[] { match })), "manage search finds the installed fixture");
        _pluginSearchBox.Text = _plugins.First(plugin => plugin.RepositoryName == "review/158").Name;
        check(await ReviewWaitUntilAsync(() => _pluginManageQuery == _pluginSearchBox.Text &&
            ReviewDescendants(_pluginManageContent).OfType<TextBlock>().Any(block => block.Text == T("Nessun plugin trovato."))),
            "manage search excludes matching uninstalled plugins and shows an empty result");
        _pluginSearchBox.Text = match.Name;
        await ReviewWaitUntilAsync(() => ReviewPluginItems(_pluginManageCardsCache).SequenceEqual(new[] { match }));
        OpenPluginPage(match, _pluginManageContent);
        check(_currentPageTag == "plugin-detail", "manage opens the plugin detail page immediately");
        ClosePluginPage(); await Task.Delay(80);
        check(_pluginStoreMode == "manage" && _pluginManageQuery == match.Name && _pluginSearchBox.Text == match.Name &&
            ReviewPluginItems(_pluginManageCardsCache).SequenceEqual(new[] { match }), "back from detail restores manage query and results");
        SwitchPluginStoreMode("discover", animate: false);
        check(_pluginSearchBox.Text == string.Empty && _pluginManageQuery == match.Name,
            "discover search stays independent from the saved manage query");
        SwitchPluginStoreMode("manage", animate: false);
        check(await ReviewWaitUntilAsync(() => _pluginSearchBox.Text == match.Name &&
            ReviewPluginItems(_pluginManageCardsCache).SequenceEqual(new[] { match })),
            "returning to manage restores its search");
        _pluginSearchBox.Text = string.Empty;
        await ReviewWaitUntilAsync(() => _pluginManageQuery == string.Empty);
        SetPluginSearchExpanded(false, animate: false);
        _pluginAllSource = "all"; _pluginAllSort = "name"; _pluginAllLayout = "list";
        InvalidatePluginAllViews(); SwitchPluginStoreMode("discover", animate: false);
    }

    private bool ReviewFeaturedCountdownStopped() => !_featuredAutoAdvanceClock.IsRunning &&
        _featuredAutoAdvanceDeadline?.IsRunning != true && _featuredAutoAdvanceActiveControl?.IsAnimating != true;

    private async Task ReviewFeaturedCountdownAsync(Action<bool, string> check,
        List<(double ActiveSeconds, double Fraction)> samples)
    {
        check(FeaturedAutoAdvanceInterval == TimeSpan.FromSeconds(10), "featured interval is exactly ten active seconds");
        _featuredAutoAdvancePointerOver = false; ResetFeaturedAutoAdvance();
        check(await ReviewWaitUntilAsync(() => _featuredAutoAdvanceClock.IsRunning), "countdown starts automatically");
        var initialIndex = _featuredPluginIndex;
        var card = _pluginFeaturedCarouselHost.Children.OfType<FrameworkElement>().Last();
        check(_featuredAutoAdvanceControls.TryGetValue(card, out var state), "active featured card has a countdown control");
        if (state is null) return;
        check(!state.Indicator.IsHitTestVisible && !ReviewDescendants(state.Indicator).OfType<Button>().Any(),
            "countdown is an indicator without play/pause controls");
        // Drive hover explicitly so the user's cursor cannot pause this timed test.
        _pluginFeaturedHost.PointerEntered -= FeaturedAutoAdvancePointerEntered;
        _pluginFeaturedHost.PointerExited -= FeaturedAutoAdvancePointerExited;
        _pluginFeaturedHost.PointerCanceled -= FeaturedAutoAdvancePointerExited;
        EventHandler<object> sample = (_, _) =>
        {
            if (ReferenceEquals(state, _featuredAutoAdvanceActiveControl) && _featuredAutoAdvanceClock.IsRunning)
                samples.Add((_featuredAutoAdvanceClock.Elapsed.TotalSeconds, state.Fraction));
        };
        var presenter = AppWindow.Presenter as OverlappedPresenter;
        var originalState = presenter?.State;
        CompositionTarget.Rendering += sample;
        try
        {
            _pluginFeaturedNextButton.Focus(FocusState.Programmatic);
            await Task.Delay(1200);
            check(state.Fraction is > .80 and < .93 && state.IsAnimating &&
                state.Progress.TryGetAnimationController("Fraction") is not null,
                "ten-second pie progresses with a native composition animation despite button focus");
            FeaturedAutoAdvancePointerEntered(_pluginFeaturedHost, null!);
            var pausedAt = _featuredAutoAdvanceClock.Elapsed;
            var pausedFraction = state.Fraction;
            await Task.Delay(600);
            check(ReviewFeaturedCountdownStopped() && _featuredAutoAdvanceClock.Elapsed == pausedAt && state.Fraction == pausedFraction &&
                state.Progress.TryGetAnimationController("Fraction") is null && _featuredPluginIndex == initialIndex,
                "hover freezes progress and detaches the native animation and deadline");
            FeaturedAutoAdvancePointerExited(_pluginFeaturedHost, null!);
            await Task.Delay(200);

            _pluginFeaturedHost.Visibility = Visibility.Collapsed;
            check(await ReviewWaitUntilAsync(ReviewFeaturedCountdownStopped), "hiding featured stops animation without waiting for its deadline");
            pausedAt = _featuredAutoAdvanceClock.Elapsed; pausedFraction = state.Fraction;
            await Task.Delay(350);
            check(_featuredAutoAdvanceClock.Elapsed == pausedAt && state.Fraction == pausedFraction && _featuredPluginIndex == initialIndex,
                "hidden time consumes none of the countdown");
            _pluginFeaturedHost.Visibility = Visibility.Visible;
            check(await ReviewWaitUntilAsync(() => _featuredAutoAdvanceClock.IsRunning), "visible featured resumes automatically");

            check(presenter is not null, "window supports the minimize/restore countdown check");
            if (presenter is not null)
            {
                presenter.Minimize();
                check(await ReviewWaitUntilAsync(() => presenter.State == OverlappedPresenterState.Minimized && ReviewFeaturedCountdownStopped()),
                    "minimizing the real window stops countdown and compositor animation");
                pausedAt = _featuredAutoAdvanceClock.Elapsed; pausedFraction = state.Fraction;
                await Task.Delay(600);
                check(_featuredAutoAdvanceClock.Elapsed == pausedAt && state.Fraction == pausedFraction && _featuredPluginIndex == initialIndex,
                    "minimized time consumes none of the countdown");
                presenter.Restore();
                if (originalState == OverlappedPresenterState.Maximized) presenter.Maximize();
                _featuredAutoAdvancePointerOver = false;
                check(await ReviewWaitUntilAsync(() => _featuredAutoAdvanceClock.IsRunning), "restore resumes the remaining countdown automatically");
            }

            var beforeDeadline = FeaturedAutoAdvanceInterval - _featuredAutoAdvanceClock.Elapsed - TimeSpan.FromMilliseconds(500);
            if (beforeDeadline > TimeSpan.Zero) await Task.Delay(beforeDeadline);
            check(_featuredPluginIndex == initialIndex, "featured does not advance before ten active seconds");
            var advanced = await ReviewWaitUntilAsync(() => _featuredPluginIndex != initialIndex, 2500);
            check(advanced, $"one full ten-second cycle advances automatically: elapsed={_featuredAutoAdvanceClock.Elapsed.TotalSeconds:F3}, running={_featuredAutoAdvanceClock.IsRunning}, hovered={_featuredAutoAdvancePointerOver}, visible={IsFeaturedAutoAdvanceHostVisible()}");
            _featuredAutoAdvancePointerOver = true; UpdateFeaturedAutoAdvanceState();
            await Task.Delay(550);
            check(_featuredPluginIndex == (initialIndex + 1) % _featuredPluginKeys.Count && !_featuredPluginTransitioning &&
                _pluginFeaturedCarouselHost.Children.Count == 1, "one completed countdown advances exactly one slide and settles");
            check(samples.Count > 1 && samples.Zip(samples.Skip(1), (before, after) =>
                after.ActiveSeconds >= before.ActiveSeconds && after.Fraction <= before.Fraction).All(valid => valid),
                "render-callback samples show monotonic logical progress, not a measured presentation rate");
            check(samples.All(value => Math.Abs(value.Fraction - Math.Clamp(1 - value.ActiveSeconds / 10, 0, 1)) < .002),
                "logical fraction follows actual active elapsed time");
        }
        finally
        {
            CompositionTarget.Rendering -= sample;
            _pluginFeaturedHost.PointerEntered += FeaturedAutoAdvancePointerEntered;
            _pluginFeaturedHost.PointerExited += FeaturedAutoAdvancePointerExited;
            _pluginFeaturedHost.PointerCanceled += FeaturedAutoAdvancePointerExited;
            if (presenter?.State == OverlappedPresenterState.Minimized)
            {
                presenter.Restore();
                if (originalState == OverlappedPresenterState.Maximized) presenter.Maximize();
            }
            _pluginFeaturedHost.Visibility = Visibility.Visible;
            _featuredAutoAdvancePointerOver = true; UpdateFeaturedAutoAdvanceState();
        }
    }

    private void ReviewPluginDetailLayout(Action<bool, string> check, string source)
    {
        check(_pluginPageHost.Opacity == 1 && ReviewHasIdentityTransform(_pluginPageHost), source + " detail page has no morph transform");
        var status = ReviewDescendants(_pluginPageContent).OfType<FrameworkElement>().Single(element => Equals(element.Tag, "plugin-detail-status-badge"));
        var origin = ReviewDescendants(_pluginPageContent).OfType<FrameworkElement>().Single(element => Equals(element.Tag, "plugin-detail-source-badge"));
        check(status.HorizontalAlignment == HorizontalAlignment.Left && origin.HorizontalAlignment == HorizontalAlignment.Right &&
            ReviewBounds(status, _pluginPageContent).Right < ReviewBounds(origin, _pluginPageContent).Left,
            source + " installed version is top left, separate from the top-right source badge");
        var image = ReviewDescendants(_pluginPageContent).OfType<Image>()
            .FirstOrDefault(candidate => candidate.Parent is Canvas { Parent: Grid { Clip: RectangleGeometry } });
        check(image is not null, source + " detail artwork uses the shared clipped hero");
        if (image?.Parent is not Canvas canvas || canvas.Parent is not Grid { Clip: RectangleGeometry clip }) return;
        check(image.ActualWidth > 0 && Math.Abs(image.ActualWidth - canvas.ActualWidth) < 2 &&
            Math.Abs(Canvas.GetTop(image) - (clip.Rect.Height - image.ActualHeight) / 2) < 2 &&
            clip.Rect.Height <= 360 && clip.Rect.Height <= _contentScroller.ViewportHeight,
            source + " hero keeps artwork width and crops vertically around its center");
        var description = ReviewDescendants(_pluginPageContent).OfType<TextBlock>().FirstOrDefault(block =>
            block.Text == "Description" || ReviewInlines(block.Inlines).OfType<Run>().Any(run => run.Text == "Description"));
        check(description is not null && ReviewBounds(description, _contentScroller).Top < _contentScroller.ActualHeight - 8,
            source + " detail keeps the beginning of its description in the first viewport");
    }

    private async Task ReviewPageHeadersAsync(string output, Action<bool, string> check)
    {
        var presenter = AppWindow.Presenter as OverlappedPresenter;
        var originalState = presenter?.State;
        var originalSize = AppWindow.Size;
        var assets = new Dictionary<string, string> {
            ["decky"] = "decky-installation-onboarding.png", ["gaming"] = "gaming-mode-page-header.png",
            ["xbox"] = "import-games-onboarding.png", ["styler"] = "big-picture-styler-page-header.png",
            ["settings"] = "settings-page-header.png"
        };
        try
        {
            presenter?.Restore();
            foreach (var width in new[] { 1280, 960 })
            {
                var scale = Content.XamlRoot?.RasterizationScale ?? 1;
                AppWindow.Resize(new Windows.Graphics.SizeInt32((int)Math.Round(width * scale), (int)Math.Round(800 * scale)));
                await Task.Delay(160);
                foreach (var (tag, asset) in assets)
                {
                    ShowPage(tag); _contentScroller.ChangeView(null, 0, null, true);
                    await Task.Delay(120);
                    var page = _pageHost.Children.OfType<StackPanel>().First(element => Equals(element.Tag, tag));
                    var image = ReviewDescendants(page).OfType<Image>().FirstOrDefault(candidate =>
                        candidate.Source is BitmapImage bitmap && bitmap.UriSource?.LocalPath.EndsWith(asset, StringComparison.OrdinalIgnoreCase) == true);
                    check(image is not null, tag + " header uses its mascot asset at window width " + width);
                    if (image?.Parent is not Grid header)
                    {
                        check(false, tag + " mascot belongs to the page header grid");
                        continue;
                    }
                    var text = header.Children.OfType<StackPanel>().FirstOrDefault(panel => panel.Children.OfType<TextBlock>().Any());
                    check(text is not null, tag + " mascot has adjacent heading text");
                    if (text is null) continue;
                    check(await ReviewWaitUntilAsync(() => image.Source is BitmapSource { PixelWidth: > 0 } && image.ActualWidth > 0),
                        tag + " mascot image is decoded");
                    var imageBounds = ReviewBounds(image, header);
                    var textBounds = ReviewBounds(text, header);
                    check(Equals(header.Tag, "page-mascot-header") && header.ColumnSpacing == 0 &&
                        header.ColumnDefinitions.Count == 2 && header.ColumnDefinitions[1].Width.IsStar &&
                        Math.Abs(header.ColumnDefinitions[0].ActualWidth - Math.Clamp(header.ActualWidth * .3, 200, 260)) < 2 &&
                        text.MaxWidth == 610 && text.HorizontalAlignment == HorizontalAlignment.Left,
                        tag + " header uses responsive mascot space and left-aligned text at " + width);
                    check(image.Height == 300 && image.HorizontalAlignment == HorizontalAlignment.Center &&
                        imageBounds.Left >= -page.Padding.Left - 1 && imageBounds.Right <= textBounds.Left + 1 &&
                        Math.Abs((imageBounds.Left + imageBounds.Right) / 2 - (textBounds.Left - page.Padding.Left) / 2) <= 2,
                        tag + " mascot centers between the padding-inclusive page edge and text at " + width);
                    var headerBounds = ReviewBounds(header, _contentScroller);
                    check(headerBounds.Left >= -1 && headerBounds.Right <= _contentScroller.ActualWidth + 1 &&
                        textBounds.Right <= header.ActualWidth + 1,
                        tag + " header fits the available responsive width at " + width);
                    await ReviewCaptureAsync(page, Path.Combine(output, "header-" + tag + "-" + width + ".png"));
                    if (tag == "xbox") ReviewImportStoreHeaders(page, check, width);
                }
            }
        }
        finally
        {
            AppWindow.Resize(originalSize);
            if (originalState == OverlappedPresenterState.Maximized) presenter?.Maximize();
        }
    }

    private static void ReviewImportStoreHeaders(DependencyObject page, Action<bool, string> check, int width)
    {
        var headers = ReviewDescendants(page).OfType<Grid>().Where(grid => Equals(grid.Tag, "import-store-header")).ToArray();
        check(headers.Length == 3, "only Xbox, Epic and GOG use import store image headers at " + width);
        var assets = new List<string>();
        foreach (var header in headers)
        {
            var image = header.Children.OfType<Image>().SingleOrDefault();
            var copy = header.Children.OfType<StackPanel>().SingleOrDefault();
            check(image is not null && copy is not null && copy.ActualHeight > 0 &&
                Math.Abs(image.Height - copy.ActualHeight) < 1 && (image is not null && ReviewHasIdentityTransform(image)),
                "import logo follows title-plus-subtitle height without scaling at " + width);
            if (image?.Source is BitmapImage bitmap && bitmap.UriSource is not null)
                assets.Add(Path.GetFileName(bitmap.UriSource.LocalPath));
            if (image is not null && copy is not null)
            {
                var imageBounds = ReviewBounds(image, header);
                var copyBounds = ReviewBounds(copy, header);
                check(imageBounds.Right <= copyBounds.Left && copyBounds.Right <= header.ActualWidth + 1,
                    "import logo and text do not overlap or overflow at " + width);
            }
        }
        check(assets.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).SequenceEqual(
            new[] { "Epic.png", "Gog.png", "Xbox.png" }, StringComparer.OrdinalIgnoreCase),
            "import store headers retain Xbox, Epic and GOG assets");
    }

    private static Windows.Foundation.Rect ReviewBounds(FrameworkElement element, UIElement relativeTo) =>
        element.TransformToVisual(relativeTo).TransformBounds(new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private static bool ReviewHasIdentityTransform(FrameworkElement element)
    {
        if (element.RenderTransform is null) return true;
        var origin = element.RenderTransform.TransformPoint(new Windows.Foundation.Point(0, 0));
        var right = element.RenderTransform.TransformPoint(new Windows.Foundation.Point(1, 0));
        var down = element.RenderTransform.TransformPoint(new Windows.Foundation.Point(0, 1));
        return Math.Abs(origin.X) < .001 && Math.Abs(origin.Y) < .001 &&
            Math.Abs(right.X - 1) < .001 && Math.Abs(right.Y) < .001 &&
            Math.Abs(down.X) < .001 && Math.Abs(down.Y - 1) < .001;
    }

    private static async Task<bool> ReviewWaitUntilAsync(Func<bool> condition, int timeoutMilliseconds = 2000)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.ElapsedMilliseconds >= timeoutMilliseconds) return false;
            await Task.Delay(20);
        }
        return true;
    }

    private async Task ReviewWelcomeFinalAsync(string output, Action<bool, string> check)
    {
        ShowPage("welcome");
        await Task.Delay(650);
        _navigateWelcomeSlide!(6);
        await Task.Delay(450);
        var artwork = ReviewDescendants(_welcomeRoot).OfType<Image>().ToArray();
        check(artwork.Length == 7 && artwork[6].RenderTransform is ScaleTransform scale &&
            Math.Abs(scale.ScaleX - 1.2) < .0001 && Math.Abs(scale.ScaleY - 1.2) < .0001 &&
            artwork[6].RenderTransformOrigin == new Windows.Foundation.Point(.5, 1),
            "final welcome artwork is twenty percent larger and remains bottom anchored");
        var button = ReviewDescendants(_welcomeRoot).OfType<Button>().First(item => item.Name == "WelcomeStartButton");
        check(Math.Abs(button.ActualWidth - CompactPrimaryActionWidth(button)) < 1 && Math.Abs(button.ActualHeight - 40) < .5,
            $"welcome start command matches update action dimensions ({button.ActualWidth} x {button.ActualHeight}, expected {CompactPrimaryActionWidth(button)})");
        var bounds = artwork[6].TransformToVisual(_welcomeRoot).TransformBounds(new Windows.Foundation.Rect(0, 0, 620, 310));
        check(bounds.Y >= 0 && bounds.Bottom < _welcomeRoot.ActualHeight, "enlarged final welcome image fits viewport");
        await ReviewCaptureAsync((FrameworkElement)Content, Path.Combine(output, "welcome-final.png"));
    }

    private async Task ReviewDeckyBusyAsync(string output, Action<bool, string> check)
    {
        ShowPage("decky");
        await Task.Delay(250);
        foreach (var button in _deckyOperationButtons)
        {
            var completion = new TaskCompletionSource<bool>();
            var width = button.ActualWidth;
            var operation = RunDeckyOperationAsync(button, () => completion.Task);
            await Task.Delay(100);
            check(button.Content is ProgressRing { IsActive: true } && _deckyOperationRunning &&
                _deckyOperationButtons.All(item => !item.IsEnabled) && !_deckyBuildCombo.IsEnabled,
                "Decky operation shows a spinner and blocks conflicting actions: " + AutomationProperties.GetName(button));
            var duplicate = false;
            await RunDeckyOperationAsync(button, () => { duplicate = true; return Task.CompletedTask; });
            check(!duplicate && Math.Abs(button.ActualWidth - width) < 1, "Decky spinner keeps button size and suppresses a duplicate operation");
            completion.SetResult(true);
            await operation;
            check(button.Content is string && !_deckyOperationRunning && _deckyOperationButtons.All(item => item.IsEnabled),
                "Decky operation restores command state");
        }
        try { await RunDeckyOperationAsync(_installButton, () => Task.FromException(new InvalidOperationException("Offline fixture"))); }
        catch (InvalidOperationException) { }
        check(!_deckyOperationRunning && _installButton.Content is string && _deckyOperationButtons.All(item => item.IsEnabled),
            "Decky operation failure restores buttons and removes spinner");
    }

    private async Task ReviewFeaturedArtworkAsync(string output, Action<bool, string> check)
    {
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Assets", "PluginImages"), "*.jpg");
        check(files.Length >= 3, "featured artwork fixture uses bundled plugin covers");
        var host = new Grid { Width = 960, Height = 340 };
        var root = (Grid)Content;
        Grid.SetRowSpan(host, 10);
        root.Children.Add(host);
        try
        {
            foreach (var file in files.Take(3))
            {
                var plugin = new DeckyPluginInfo { Name = Path.GetFileNameWithoutExtension(file), CoverImage = file };
                var frame = BuildFeaturedPluginFrame(plugin);
                host.Children.Add(frame);
                var image = ReviewDescendants(frame).OfType<Image>().FirstOrDefault(item => item.Parent is Grid);
                check(image is not null && await ReviewWaitUntilAsync(() => image.Source is BitmapImage { PixelWidth: > 0 } &&
                    image.ActualWidth > 100 && image.ActualHeight > 100),
                    "featured slide loads and displays its actual cover: " + plugin.Name);
                await ReviewCaptureAsync(frame, Path.Combine(output, "featured-artwork-" + Array.IndexOf(files, file) + ".png"));
                host.Children.Remove(frame);
                host.Children.Add(frame);
                check(await ReviewWaitUntilAsync(() => image?.IsLoaded == true && image.Source is BitmapImage { PixelWidth: > 0 }),
                    "cached featured slide keeps its cover after navigation: " + plugin.Name);
                host.Children.Clear();
            }
        }
        finally { root.Children.Remove(host); }
    }

    private async Task ReviewPluginActionWidthAsync(Action<bool, string> check)
    {
        var plugin = new DeckyPluginInfo { Name = "Offline action width", RepositoryName = "offline-action-width" };
        var button = CreatePluginInstallButton(plugin, compact: false);
        var host = new StackPanel { Orientation = Orientation.Horizontal };
        host.Children.Add(button);
        var root = (Grid)Content;
        Grid.SetRowSpan(host, 10);
        root.Children.Add(host);
        try
        {
            await Task.Delay(100);
            var content = (Grid)button.Content;
            var label = ReviewDescendants(content).OfType<TextBlock>().First(block => block.Text == T("Installa"));
            label.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            var expected = label.DesiredSize.Width + 16 + 8 + button.Padding.Left + button.Padding.Right +
                button.BorderThickness.Left + button.BorderThickness.Right;
            check(Math.Abs(button.ActualWidth - expected) < 2,
                "plugin install action fits its icon and label with balanced padding");
            var operation = new PluginInstallOperation { Plugin = plugin,
                Progress = new PluginInstallProgress(PluginInstallPhase.Downloading, 47) };
            PublishPluginInstallProgress(PluginStoreKey(plugin), operation);
            await Task.Delay(80);
            var busyWidth = button.ActualWidth;
            operation.Progress = new PluginInstallProgress(PluginInstallPhase.Installing);
            PublishPluginInstallProgress(PluginStoreKey(plugin), operation);
            await Task.Delay(80);
            check(Math.Abs(button.ActualWidth - busyWidth) < 1,
                "plugin action progress keeps a stable width across phases");
            PublishPluginInstallProgress(PluginStoreKey(plugin), null);
            await Task.Delay(80);
            check(Math.Abs(button.ActualWidth - expected) < 2,
                "plugin action returns to its text width after progress");
            var featured = GetFeaturedPlugins().Select(PluginStoreKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var category = BuildPluginDiscoveryCategory("I plugin di Playhub", _plugins.Where(item => item.IsPlayhubPlugin).ToList());
            var cards = ReviewDescendants(category).OfType<FrameworkElement>()
                .Where(item => item.Tag is DeckyPluginInfo).Select(item => (DeckyPluginInfo)item.Tag);
            check(cards.All(item => !featured.Contains(PluginStoreKey(item))),
                "Playhub category preview excludes featured plugins");
        }
        finally { root.Children.Remove(host); }
    }

    private async Task ReviewPluginCoverFallbackAsync(Action<bool, string> check)
    {
        var invalid = Path.Combine(AppContext.BaseDirectory, "Assets", "Localization", "en.json");
        var valid = Path.Combine(AppContext.BaseDirectory, "Assets", "Brand", "cube.png");
        var plugin = new DeckyPluginInfo
        {
            Name = "Offline card fallback fixture", IsPlayhubPlugin = false, CoverImage = invalid,
            Media = new List<PluginMediaInfo> { new() { Url = valid, Kind = "image" } }
        };
        var image = CreatePluginPreviewImage(plugin, 320)!;
        var host = new Border { Width = 320, Height = 180, Child = image };
        var root = (Grid)Content;
        Grid.SetRowSpan(host, 10);
        root.Children.Add(host);
        try
        {
            check(await ReviewWaitUntilAsync(() => image.Source is BitmapImage bitmap && bitmap.PixelWidth > 0 &&
                string.Equals(bitmap.UriSource?.LocalPath, valid, StringComparison.OrdinalIgnoreCase)),
                "native failed card cover recovers to an existing detail image");
            check(!_pluginBitmapCache.Keys.Any(key => key.Path == invalid),
                "failed card bitmaps are removed from the native image cache");
        }
        finally { root.Children.Remove(host); }
    }

    private async Task ReviewNativeUpdateScrollAsync(string output, Action<bool, string> check)
    {
        await ReviewWaitUntilAsync(() => Content.XamlRoot is not null);
        ShowPage("settings");
        var info = new PlayhubUpdateService.UpdateInfo(false, "1.2.1", "1.3.0", null,
            string.Join("\n\n", Enumerable.Range(1, 16).Select(i => $"## Update {i}\nA release note with **bold text**, a [link](https://github.com/LoZazaMastro/Playhub) and enough content to test the actual native popup.\n\n- First improvement\n- Second improvement")),
            "https://github.com/offline/review/update.exe", "review.exe", 100, null);
        var presenter = AppWindow.Presenter as OverlappedPresenter;
        if (presenter is not null) presenter.IsAlwaysOnTop = true;
        presenter?.Restore();
        await Task.Delay(500);
        foreach (var size in new[] { (Width: 1280, Height: 900), (Width: 960, Height: 740), (Width: 960, Height: 560) })
        {
            var scale = Content.XamlRoot.RasterizationScale;
            AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(size.Width * scale), (int)(size.Height * scale)));
            await Task.Delay(180);
            ShowPlayhubUpdateDialog(info, force: true);
            check(await ReviewWaitUntilAsync(() => _playhubUpdateDialogContent?.IsLoaded == true), "native update popup opens " + size);
            await Task.Delay(300);
            var scroll = _playhubUpdateDialogChangelog!;
            check(_playhubUpdateDialogProgressBar!.Opacity == 0, "no gray line before update " + size);
            Diag.Step("UPDATE SCROLL " + JsonSerializer.Serialize(new
            {
                Size = size.ToString(), scroll.ActualHeight, scroll.MaxHeight, scroll.ViewportHeight,
                scroll.ExtentHeight, scroll.ScrollableHeight, scroll.IsEnabled, scroll.IsHitTestVisible,
                Bounds = ReviewBounds(scroll, (FrameworkElement)Content).ToString(),
                Ancestors = DialogVisuals(_playhubUpdateDialog!).OfType<ScrollViewer>().Select(s => new
                { s.Name, s.ActualHeight, s.ViewportHeight, s.ExtentHeight, s.ScrollableHeight, s.IsEnabled })
            }));
            check(scroll.ViewportHeight > 60 && scroll.ScrollableHeight > 100, "native update popup has bounded scroll area " + size);
            var oldPageOffset = _contentScroller.VerticalOffset;
            var oldActionPosition = ReviewBounds(_playhubUpdateDialogActionButton!, _playhubUpdateDialogContent!);
            var injected = await ReviewWheelAsync(scroll, -360);
            check(injected && scroll.VerticalOffset > 0, "mouse wheel scrolls actual update popup " + size);
            check(Math.Abs(_contentScroller.VerticalOffset - oldPageOffset) < 1, "popup wheel does not scroll background page " + size);
            check(ReviewBounds(_playhubUpdateDialogActionButton!, _playhubUpdateDialogContent!) == oldActionPosition,
                "native popup action remains fixed during wheel scrolling " + size);
            _playhubUpdateRunning = true;
            UpdatePlayhubUpdateDialogProgress(.42, "42% - download di prova");
            await Task.Delay(120);
            var indicator = ReviewDescendants(_playhubUpdateDialogProgressBar!).OfType<FrameworkElement>()
                .FirstOrDefault(item => item.Name == "DeterminateProgressBarIndicator");
            check(_playhubUpdateDialogProgressBar!.Opacity == 1 && indicator?.ActualWidth > 20 && indicator.ActualHeight >= 3 &&
                _playhubUpdateDialogProgressBar.Foreground is SolidColorBrush brush && brush.Color == ParseColor(_settings.AccentColor),
                "actual native popup shows accent download progress " + size);
            check(_playhubUpdateDialogCloseButton!.Visibility == Visibility.Collapsed, "actual download hides close button " + size);
            await ReviewCaptureAsync(_playhubUpdateDialogContent!, Path.Combine(output, "update-native-scroll-" + size.Height + ".png"));
            _playhubUpdateRunning = false;
            _playhubUpdateDialogHasProgress = false;
            _playhubUpdateDialogFraction = null;
            _playhubUpdateDialogStatusText = "";
            RefreshPlayhubUpdateDialogState();
            var clicked = await ReviewPointerAsync(_playhubUpdateDialogCloseButton!, null);
            check(clicked && await ReviewWaitUntilAsync(() => _playhubUpdateDialog is null),
                "actual mouse click closes update popup " + size);
            _playhubUpdateDialog?.Hide();
            await ReviewWaitUntilAsync(() => _playhubUpdateDialog is null);
        }
        ShowPage("support");
        var supportShowing = ShowSupportReminderAsync(force: true);
        check(await ReviewWaitUntilAsync(() => _supportReminderDialog?.Content is FrameworkElement { IsLoaded: true }),
            "native support popup opens");
        await Task.Delay(350);
        if (_supportReminderDialog?.Content is FrameworkElement support)
        {
            var scroll = ReviewDescendants(support).OfType<ScrollViewer>().First();
            check(scroll.ScrollableHeight > 0 && await ReviewWheelAsync(scroll, -360) && scroll.VerticalOffset > 0,
                "actual mouse wheel scrolls support popup");
            var close = ReviewDescendants(support).OfType<Button>().First(button => button.Name == "SupportReminderClose");
            var clicked = await ReviewPointerAsync(close, null);
            check(clicked && await ReviewWaitUntilAsync(() => _supportReminderDialog is null),
                "actual mouse click closes support popup");
        }
        _supportReminderDialog?.Hide();
        await supportShowing;
        if (presenter is not null) presenter.IsAlwaysOnTop = false;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct ReviewNativePoint { public int X; public int Y; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetCursorPos(out ReviewNativePoint point);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ClientToScreen(nint window, ref ReviewNativePoint point);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint WindowFromPoint(ReviewNativePoint point);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, nuint extraInfo);

    private Task<bool> ReviewWheelAsync(FrameworkElement target, int delta) => ReviewPointerAsync(target, delta);

    private async Task<bool> ReviewPointerAsync(FrameworkElement target, int? wheelDelta)
    {
        var window = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var currentThread = GetCurrentThreadId();
        var attached = foregroundThread != currentThread && AttachThreadInput(currentThread, foregroundThread, true);
        try { Activate(); SetForegroundWindow(window); }
        finally { if (attached) AttachThreadInput(currentThread, foregroundThread, false); }
        await Task.Delay(100);
        var bounds = ReviewBounds(target, (FrameworkElement)Content);
        var scale = Content.XamlRoot.RasterizationScale;
        var point = new ReviewNativePoint { X = (int)((bounds.X + bounds.Width / 2) * scale), Y = (int)((bounds.Y + bounds.Height / 2) * scale) };
        ClientToScreen(window, ref point);
        GetWindowThreadProcessId(WindowFromPoint(point), out var process);
        Diag.Step($"POINTER INPUT foreground={GetForegroundWindow()} window={window} hitProcess={process} ownProcess={Environment.ProcessId} point={point.X},{point.Y}");
        if (GetForegroundWindow() != window || process != Environment.ProcessId) return false;
        GetCursorPos(out var original);
        try
        {
            SetCursorPos(point.X, point.Y);
            await Task.Delay(80);
            if (GetForegroundWindow() != window) return false;
            SetCursorPos(point.X, point.Y);
            if (wheelDelta is int delta) mouse_event(0x0800, 0, 0, unchecked((uint)delta), 0);
            else
            {
                mouse_event(0x0002, 0, 0, 0, 0);
                mouse_event(0x0004, 0, 0, 0, 0);
            }
            await Task.Delay(450);
            return true;
        }
        finally { SetCursorPos(original.X, original.Y); }
    }

    private async Task ReviewPlayhubUpdateDialogAsync(string output, Action<bool, string> check)
    {
        const string formatting = "# Heading\nParagraph with **bold**, *italic* and [GitHub](https://github.com/LoZazaMastro/Playhub).\n\n" +
            "* First item\n+ Second item\n1. First ordered\n2. Second ordered\n\n> Quoted **text**\n\n" +
            "<h3>HTML heading</h3><p><strong>Strong</strong> and <em>emphasis</em>.</p>\n<blockquote>HTML quote</blockquote>";
        var info = new PlayhubUpdateService.UpdateInfo(false, "9.8.7", "1.3.0", null,
            string.Join("\n\n", Enumerable.Repeat(formatting, 8)), "https://github.com/offline/review/update.exe", "review.exe", 100, null);
        var root = (Grid)Content;
        var host = new Grid { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(180, 0, 0, 0)) };
        Grid.SetRowSpan(host, 10);
        root.Children.Add(host);
        var completion = new TaskCompletionSource<bool>();
        var downloads = 0;
        async Task FakeDownload()
        {
            downloads++;
            _playhubUpdateRunning = true;
            UpdatePlayhubUpdateDialogProgress(.42, "42% - download di prova");
            await completion.Task;
            _playhubUpdateRunning = false;
            UpdatePlayhubUpdateDialogProgress(null, "Errore di prova", failed: true);
        }
        var content = BuildPlayhubUpdateDialogForReview(info, FakeDownload);
        content.VerticalAlignment = VerticalAlignment.Center;
        host.Children.Add(content);
        await Task.Delay(220);
        check(_playhubUpdateBar.Visibility == Visibility.Collapsed, "update progress is never shown in Settings");
        check(Math.Abs(_playhubUpdateDialogActionButton!.ActualWidth - _playhubUpdateDialogProgressBar!.ActualWidth) < 1 &&
            _playhubUpdateDialogActionButton.ActualWidth < 216, "update button is narrower with a matching minimal progress bar");
        check(ReviewDescendants(content).OfType<Grid>().Any(grid => grid.Padding.Top == 40) &&
            ReviewDescendants(content).OfType<StackPanel>().Any(panel => panel.Margin.Top == 36),
            "update popup doubles top spacing and version-to-action spacing");
        check(ReviewDescendants(content).OfType<TextBlock>().Any(block => block.Text == T("Novità")),
            "update popup localizes the What's new heading");
        var textBlocks = ReviewDescendants(content).OfType<TextBlock>().ToArray();
        var inlines = textBlocks.SelectMany(block => ReviewInlines(block.Inlines)).ToArray();
        check(inlines.OfType<Bold>().Any() && inlines.OfType<Italic>().Any() && inlines.OfType<Hyperlink>().Any(),
            "update changelog renders bold, italic and clickable links");
        check(inlines.OfType<Run>().Any(run => run.Text == "HTML heading") &&
            textBlocks.Any(block => block.Text == "1.") && textBlocks.Any(block => block.Text == "2."),
            "update changelog renders HTML headings and ordered lists");
        check(ReviewDescendants(content).OfType<Border>().Count(border => border.BorderThickness.Left == 3) >= 2,
            "update changelog renders Markdown and HTML quotations");
        check(!inlines.OfType<Run>().Any(run => run.Text.Contains("<strong>") || run.Text.Contains("**") || run.Text.Contains("<blockquote>")),
            "update changelog does not expose raw formatting markup");
        check(_playhubUpdateDialogChangelog!.ScrollableHeight > 100, "long changelog is vertically scrollable");
        var before = _playhubUpdateDialogActionButton!.TransformToVisual(root).TransformPoint(default);
        _playhubUpdateDialogChangelog.ChangeView(null, 400, null, true);
        await Task.Delay(100);
        var after = _playhubUpdateDialogActionButton.TransformToVisual(root).TransformPoint(default);
        check(_playhubUpdateDialogChangelog.VerticalOffset > 0 && before == after, "update action remains fixed while changelog scrolls");
        var pending = InvokePlayhubUpdateDialogUpdateForReviewAsync();
        await InvokePlayhubUpdateDialogUpdateForReviewAsync();
        await Task.Delay(250);
        check(downloads == 1 && !_playhubUpdateDialogActionButton.IsEnabled && _playhubUpdateDialogProgressBar!.Value == .42,
            "update double click starts one operation and shows actual progress");
        var indicator = ReviewDescendants(_playhubUpdateDialogProgressBar!).OfType<FrameworkElement>()
            .FirstOrDefault(item => item.Name == "DeterminateProgressBarIndicator");
        check(indicator is not null && indicator.ActualWidth > 20 && indicator.ActualHeight >= 3 && indicator.Opacity > 0,
            $"update progress has a visible native indicator ({indicator?.ActualWidth} x {indicator?.ActualHeight}, opacity {indicator?.Opacity})");
        check(_playhubUpdateDialogProgressBar!.Foreground is SolidColorBrush accent && accent.Color == ParseColor(_settings.AccentColor),
            "update progress uses the selected accent color");
        check(_playhubUpdateDialogCloseButton!.Visibility == Visibility.Collapsed && !_playhubUpdateDialogCloseButton.IsEnabled,
            "update close button disappears immediately while downloading");
        await ReviewCaptureAsync(content, Path.Combine(output, "update-popup-downloading.png"));
        host.Children.Clear();
        host.Children.Add(BuildPlayhubUpdateDialogForReview(info, FakeDownload, width: 420, maxHeight: 540));
        await Task.Delay(150);
        check(_playhubUpdateDialogProgressBar!.Value == .42 && !_playhubUpdateDialogActionButton!.IsEnabled,
            "reopened popup retains download state");
        check(_playhubUpdateDialogContent!.ActualHeight <= 541 && _playhubUpdateDialogChangelog!.ActualHeight > 80,
            "short popup leaves room for scrollable changelog");
        completion.SetResult(true);
        await pending;
        check(_playhubUpdateDialogActionButton!.IsEnabled && (string)_playhubUpdateDialogActionButton.Content == "Riprova",
            "failed update enables retry without another dialog");
        check(_playhubUpdateDialogCloseButton!.Visibility == Visibility.Visible && _playhubUpdateDialogCloseButton.IsEnabled,
            "failed update restores popup close button");
        root.Children.Remove(host);

        var actual = info with { LatestVersion = "1.2.1", Notes =
            "This maintenance update makes Gaming Mode more reliable when Steam starts or restarts.\n\n" +
            "## Improvements\n- Gaming Mode now waits for Steam Big Picture to become stable before completing a Steam restart.\n" +
            "- The Gaming Mode safety monitor now runs as a single coordinated instance.\n" +
            "- Recovery checks are more resilient during temporary Steam startup transitions.\n\n" +
            "## Fixes\n- Fixed a serious issue where Gaming Mode could return to Desktop Mode immediately after Steam Big Picture opened.\n" +
            "- Restarting Steam no longer reapplies the entire Gaming Mode configuration.\n" +
            "- Fixed false recovery events caused by the brief interval between Steam closing and reopening.\n" +
            "- Fixed duplicate safety monitors potentially reacting to the same Steam transition.\n" +
            "- Gaming Mode now keeps the selected default boot mode after a successful Steam restart." };
        _status.IsOpen = false;
        ShowUpdateNotification(actual);
        await Task.Delay(400);
        check(_playhubUpdateDialog is not null && !_status.IsOpen, "app update uses popup instead of old InfoBar");
        var entranceProperties = DialogVisuals(_playhubUpdateDialog!).SelectMany(VisualStateManager.GetVisualStateGroups)
            .Where(group => group.Name == "DialogShowingStates").SelectMany(group => group.Transitions)
            .Where(transition => transition.To == "DialogShowing" && transition.Storyboard is not null)
            .SelectMany(transition => transition.Storyboard.Children)
            .Select(Microsoft.UI.Xaml.Media.Animation.Storyboard.GetTargetProperty).ToArray();
        check(entranceProperties.Contains("ScaleX") && entranceProperties.Contains("ScaleY") && entranceProperties.Contains("Opacity"),
            "update popup preserves native fade and zoom with XAML hit testing");
        var dialog = _playhubUpdateDialog;
        ShowUpdateNotification(actual);
        check(ReferenceEquals(dialog, _playhubUpdateDialog), "duplicate update detection does not open a second popup");
        if (_playhubUpdateDialogContent is not null)
            await ReviewCaptureAsync(_playhubUpdateDialogContent, Path.Combine(output, "update-popup.png"));
        _playhubUpdateRunning = true;
        RefreshPlayhubUpdateDialogState();
        _playhubUpdateDialog?.Hide();
        await Task.Delay(250);
        check(ReferenceEquals(dialog, _playhubUpdateDialog) && _playhubUpdateDialogContent?.IsLoaded == true,
            "native dialog dismissal is blocked during download, including Escape and Hide");
        _playhubUpdateRunning = false;
        RefreshPlayhubUpdateDialogState();
        _playhubUpdateDialog?.Hide();
        await Task.Delay(300);
        ShowUpdateNotification(actual);
        check(_playhubUpdateDialog is null, "automatic update notice is shown once per version");
        ShowPlayhubUpdateDialog(actual, force: true);
        await Task.Delay(250);
        check(_playhubUpdateDialog is not null, "settings can reopen an already noticed update");
        _playhubUpdateDialog?.Hide();
        await Task.Delay(200);
    }

    private async Task ReviewSupportReminderAsync(string output, Action<bool, string> check)
    {
        ShowPage("support");
        await Task.Delay(200);
        var supportPage = _pageHost.Children.OfType<FrameworkElement>().First(page => Equals(page.Tag, "support"));
        var testButton = ReviewDescendants(supportPage).OfType<Button>()
            .FirstOrDefault(button => AutomationProperties.GetAutomationId(button) == "SupportReminderTest");
        check((testButton is not null) == PlayhubUpdatePolicy.IsPreview, "donation Test button exists only in the update-test build");
        _supportUsageClock = new SupportUsageClock(123);
        var savedSeconds = _settings.SupportReminderUsageSeconds;
        _pluginBulkUpdateRunning = true;
        await ShowSupportReminderAsync(force: true);
        check(_supportReminderDialog is null, "support popup defers while a plugin operation is running");
        _pluginBulkUpdateRunning = false;
        var showing = ShowSupportReminderAsync(force: true);
        check(await ReviewWaitUntilAsync(() => _supportReminderDialog is not null), "support popup opens on explicit test without two hours of waiting");
        await Task.Delay(300);
        if (_supportReminderDialog?.Content is FrameworkElement content)
        {
            var picture = ReviewDescendants(content).OfType<Image>().FirstOrDefault();
            check(picture?.Source is BitmapImage bitmap && bitmap.UriSource?.LocalPath.EndsWith("Donation.png") == true,
                "support popup reuses the approved Support mascot image");
            check(ReviewDescendants(content).OfType<TextBlock>().Any(block => block.Text == T("Ti piace Playhub?")),
                "support popup shows the requested heading");
            var popupText = ReviewDescendants(content).OfType<TextBlock>().Select(block => block.Text).ToArray();
            foreach (var text in new[] {
                "Playhub è gratuito e open source. Lo sviluppo, i test e la manutenzione sono sostenuti da una sola persona.",
                "Se Playhub ti è utile e vuoi aiutare il progetto a continuare a crescere, una donazione è sempre apprezzata. Nessun contenuto è bloccato: è semplicemente un modo gentile per sostenere il lavoro che c'è dietro."
            }) check(popupText.Contains(T(text)), "support popup preserves the Support card paragraph verbatim");
            var donation = ReviewDescendants(content).OfType<Button>().FirstOrDefault(button =>
                AutomationProperties.GetAutomationId(button) == "SupportReminderDonate");
            check(donation is not null && donation.IsEnabled, "support popup exposes the accent donation command");
            check(content.ActualWidth <= Content.XamlRoot.Size.Width && content.ActualHeight <= Content.XamlRoot.Size.Height,
                "support popup fits the current viewport");
            await ReviewCaptureAsync(content, Path.Combine(output, "support-popup.png"));
        }
        _supportReminderDialog?.Hide();
        await showing;
        check(_supportUsageClock.UsageSeconds == 123 && _settings.SupportReminderUsageSeconds == savedSeconds,
            "forced support test does not consume accumulated usage or write settings");
        _supportUsageClock = null;
    }

    private static IEnumerable<Inline> ReviewInlines(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            yield return inline;
            if (inline is Span span)
                foreach (var nested in ReviewInlines(span.Inlines)) yield return nested;
        }
    }

    private static IEnumerable<DependencyObject> ReviewDescendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in ReviewDescendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    private static async Task ReviewCaptureAsync(FrameworkElement element, string path)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element, 1280, 0);
        var pixels = await bitmap.GetPixelsAsync();
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(path)!);
        var file = await folder.CreateFileAsync(Path.GetFileName(path), CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray());
        await encoder.FlushAsync();
    }
}

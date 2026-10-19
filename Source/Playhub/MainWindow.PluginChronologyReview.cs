#if PLAYHUB_UI_REVIEW
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media;
using Playhub.Models;
using Playhub.Services;
using System.Globalization;
using System.Text.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Playhub;

public sealed partial class MainWindow
{
    private async Task ReviewPluginChronologyAsync()
    {
        string output = Environment.GetEnvironmentVariable("PLAYHUB_REVIEW_OUTPUT")
            ?? throw new InvalidOperationException("Explicit chronology review output required.");
        Directory.CreateDirectory(output);
        var checks = new List<string>(); var failures = new List<string>();
        var previousSettings = _settings; var previousPlugins = _plugins.ToArray();
        var previousCulture = CultureInfo.CurrentCulture; var previousUiCulture = CultureInfo.CurrentUICulture;
        string previousPage = _currentPageTag, previousMode = _pluginStoreMode, previousSort = _pluginAllSort;
        string? previousCategory = _pluginCategoryFilter;
        bool previousShowAll = _pluginShowAll, previousDensityReady = _densityReady;
        var previousHistory = _pluginStoreHistory.ToArray();
        var previousNavigation = _navigation.SelectedItem;
        var captures = new List<object>();
        void Check(bool valid, string message)
        {
            checks.Add((valid ? "PASS " : "FAIL ") + message);
            if (!valid) failures.Add(message);
            Diag.Step("Chronology native review: " + checks[^1]);
        }
        async Task Settle()
        {
            await Task.Delay(200);
            ((FrameworkElement)Content).UpdateLayout();
        }
        async Task<(int Width, int Height)> Capture(string fileName)
        {
            var element = (FrameworkElement)Content;
            var bitmap = new RenderTargetBitmap();
            // The default overload already applies the visual's rasterization
            // scale. Multiplying dimensions here would scale the capture twice.
            await bitmap.RenderAsync(element);
            var pixels = await bitmap.GetPixelsAsync();
            var folder = await StorageFolder.GetFolderFromPathAsync(output);
            var file = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
            using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            double scale = element.XamlRoot.RasterizationScale;
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96 * scale, 96 * scale, pixels.ToArray());
            await encoder.FlushAsync();
            return (bitmap.PixelWidth, bitmap.PixelHeight);
        }
        static DateTimeOffset Utc(int day, int hour = 0, int minute = 0) => new(2026, 10, day, hour, minute, 0, TimeSpan.Zero);
        static IEnumerable<DeckyPluginInfo> SectionItems(StackPanel section) => section.Children.OfType<ItemsRepeater>()
            .SelectMany(ReviewPluginItems);
        string SectionTitle(StackPanel section) => ReviewDescendants(section).OfType<TextBlock>()
            .Select(block => block.Text).FirstOrDefault(text => text == T("Novità") || text == T("I plugin di Playhub")) ?? "";
        void Invoke(Button button)
        {
            var provider = new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke) as IInvokeProvider
                ?? throw new InvalidOperationException("Native button does not expose Invoke.");
            provider.Invoke();
        }
        try
        {
            string privateRoot = Path.Combine(AppPaths.LocalDataRoot, "chronology-fixture");
            Check(Path.GetFullPath(privateRoot).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase), "fixture AppPaths stay beneath the isolated review executable");
            if (failures.Count > 0 || _remotePluginCatalogReviewEnabled || _remotePluginCatalogRefreshQueued)
                throw new InvalidOperationException("Chronology review requires isolated AppPaths and the offline review guard.");
            _settings = JsonSerializer.Deserialize<PlayhubSettings>(JsonSerializer.Serialize(previousSettings))!;
            _settings.Language = "it"; _settings.PluginRoot = Path.Combine(privateRoot, "source");
            _settings.DeckyPluginsPath = Path.Combine(privateRoot, "installed");
            Directory.CreateDirectory(_settings.PluginRoot); Directory.CreateDirectory(_settings.DeckyPluginsPath);
            _densityReady = false;
            var fixtures = new List<(DeckyPluginInfo Plugin, DateTimeOffset? First, DateTimeOffset? Updated)>();
            for (int index = 0; index < 125; index++)
            {
                DateTimeOffset? first = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(index);
                DateTimeOffset? updated = Utc(15).AddMinutes(index);
                var plugin = new DeckyPluginInfo
                {
                    Name = $"Chronology {index:00}", FolderName = $"Chronology{index:00}", Author = "Private review fixture",
                    RepositoryName = $"Chronology{index:00}", RepositorySlug = $"private-fixture/Chronology{index:00}",
                    RepositoryUrl = $"https://github.com/private-fixture/Chronology{index:00}",
                    SourceFolder = Path.Combine(_settings.PluginRoot, $"Chronology{index:00}"),
                    Version = "1.0.0", Category = "Personalizzazione e media", IsPlayhubPlugin = index < 20,
                    CatalogSource = index < 20 ? "playhub" : "decky-store", CatalogPluginId = 7000 + index,
                    ShortDescription = "Fixture privata per verificare date, ordinamento e navigazione native.", HasUpdate = index % 3 == 0,
                    FirstPublishedAt = first.Value.ToString("O", CultureInfo.InvariantCulture),
                    UpdatedAt = updated.Value.ToString("O", CultureInfo.InvariantCulture)
                };
                if (index == 0)
                {
                    plugin.Name = "Legacy plugin, updated today"; plugin.HasUpdate = false;
                    plugin.FirstPublishedAt = "2018-01-01T00:00:00Z"; first = new(2018, 1, 1, 0, 0, 0, TimeSpan.Zero);
                    plugin.UpdatedAt = "2026-10-18T22:59:59.9999999-04:00"; updated = Utc(19, 2, 59).AddSeconds(59).AddTicks(9999999);
                }
                if (index == 1)
                {
                    plugin.Name = "New project with exact publication time";
                    plugin.FirstPublishedAt = "2026-10-18T12:00:00.0000001+00:00"; first = Utc(18, 12).AddTicks(1);
                    updated = first; plugin.UpdatedAt = "2026-10-18T08:00:00.0000001-04:00";
                }
                if (index == 2)
                {
                    plugin.Name = "Published project, no update date"; plugin.HasUpdate = false;
                    plugin.FirstPublishedAt = "2026-10-17T23:00:00+02:00"; first = Utc(17, 21);
                    plugin.UpdatedAt = ""; updated = first;
                }
                if (index == 3)
                {
                    plugin.Name = "Same day, later instant through offset";
                    plugin.UpdatedAt = "2026-10-17T22:00:00.0000001Z"; updated = Utc(17, 22).AddTicks(1);
                    plugin.FirstPublishedAt = plugin.UpdatedAt; first = updated;
                }
                if (index is 5 or 6)
                {
                    string owner = index == 5 ? "owner-a" : "owner-b";
                    plugin.Name = "Shared basename / " + owner;
                    plugin.RepositoryName = "SharedBasename"; plugin.FolderName = "SharedBasename";
                    plugin.RepositorySlug = owner + "/SharedBasename";
                    plugin.RepositoryUrl = "https://github.com/" + owner + "/SharedBasename";
                    plugin.LongDescription = "Private fork details belonging exclusively to " + owner + ".";
                    updated = Utc(16, index + 10); plugin.UpdatedAt = updated.Value.ToString("O", CultureInfo.InvariantCulture);
                    first = updated; plugin.FirstPublishedAt = plugin.UpdatedAt;
                }
                if (index == 57)
                {
                    plugin.Name = "Corrupt update keeps known publication";
                    plugin.FirstPublishedAt = "2026-10-16T12:00:00Z"; first = Utc(16, 12);
                    plugin.UpdatedAt = "not-a-date"; updated = first;
                }
                if (index == 58)
                {
                    plugin.Name = "Undated project"; plugin.FirstPublishedAt = plugin.UpdatedAt = "";
                    first = updated = null;
                }
                if (index == 59)
                {
                    plugin.Name = "Duplicate legacy catalog row";
                    plugin.RepositorySlug = "PRIVATE-FIXTURE/CHRONOLOGY00";
                    plugin.RepositoryUrl = "https://github.com/PRIVATE-FIXTURE/CHRONOLOGY00/";
                    first = fixtures[0].First; plugin.FirstPublishedAt = fixtures[0].Plugin.FirstPublishedAt;
                }
                fixtures.Add((plugin, first, updated));
            }
            var expectedNews = fixtures.Where(item => item.First.HasValue).OrderByDescending(item => item.First)
                .DistinctBy(item => item.Plugin.RepositorySlug, StringComparer.OrdinalIgnoreCase).Take(100).Select(item => item.Plugin).ToArray();
            var expectedAdded = fixtures.OrderByDescending(item => item.First ?? DateTimeOffset.MinValue)
                .ThenBy(item => item.Plugin.Name, StringComparer.OrdinalIgnoreCase).Select(item => item.Plugin).ToArray();
            var expectedUpdated = fixtures.OrderByDescending(item => item.Updated ?? DateTimeOffset.MinValue)
                .ThenBy(item => item.Plugin.Name, StringComparer.OrdinalIgnoreCase).Select(item => item.Plugin).ToArray();
            _plugins.Clear(); foreach (var fixture in fixtures) _plugins.Add(fixture.Plugin);
            _pluginStoreHistory.Clear(); _pluginDiscoveryCategories.Clear(); _featuredOrder.Order.Clear();
            _pluginStoreMode = "discover"; _pluginCategoryFilter = null; _pluginShowAll = false;
            _pluginAllSort = "name"; _pluginAllSource = "all";
            _suppressPluginSearchRender = true; try { _pluginSearchBox.Text = ""; } finally { _suppressPluginSearchRender = false; }
            _featuredAutoAdvancePointerOver = true;
            ApplyTheme(); ApplyLanguage(); ShowPage("plugins");
            _navigation.SelectedItem = _navigation.MenuItems.OfType<NavigationViewItem>().Single(item => Equals(item.Tag, "plugins"));
            _pluginCardsDirty = true; RenderPluginCards(); await Settle();
            Check(_plugins.Count == 125 && fixtures.Select(item => item.Plugin.RepositorySlug).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 124,
                "125 production model fixtures include one duplicate project identity");
            Check(fixtures[5].Plugin.RepositoryName == fixtures[6].Plugin.RepositoryName &&
                PluginStoreKey(fixtures[5].Plugin) != PluginStoreKey(fixtures[6].Plugin),
                "same repository basename under different owners retains distinct production store keys");
            foreach (string culture in new[] { "en-US", "it-IT", "ar-SA" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                _pluginAllSort = "added";
                Check(SortPluginAll(_plugins).SequenceEqual(expectedAdded), culture + " actual added sort uses first publication instants descending");
                _pluginAllSort = "updated";
                Check(SortPluginAll(_plugins).SequenceEqual(expectedUpdated), culture + " actual updated sort preserves time, fractions and UTC offsets");
            }
            CultureInfo.CurrentCulture = previousCulture;
            Check(PluginChronology.LastUpdated(fixtures[0].Plugin) == fixtures[0].Updated &&
                PluginChronology.FirstPublished(fixtures[0].Plugin) == fixtures[0].First,
                "an old project updated today retains its original publication date");
            Check(expectedNews[0] == fixtures[1].Plugin && !expectedNews.Contains(fixtures[0].Plugin) && expectedNews.Contains(fixtures[2].Plugin),
                "News uses first publication: old projects updated today stay out, genuinely new projects remain included");

            foreach (int width in new[] { 1280, 960 })
            {
                double scale = ((FrameworkElement)Content).XamlRoot.RasterizationScale;
                _appWindow?.ResizeClient(new SizeInt32((int)Math.Round(width * scale), (int)Math.Round(900 * scale))); await Settle();
                Check(Math.Abs(((FrameworkElement)Content).ActualWidth - width) < 1,
                    width + " logical client width is measured at the actual native DPI");
                _pluginCategoryFilter = null; _pluginShowAll = false; _pluginAllSort = "name";
                _pluginStoreHistory.Clear(); _pluginFeaturedHost.Visibility = Visibility.Visible;
                InvalidatePluginAllViews(); _pluginCardsDirty = true; RenderPluginCards(); await Settle();
                var sections = _pluginCards.Children.OfType<StackPanel>().ToArray();
                Check(sections.Length >= 2 && SectionTitle(sections[0]) == T("Novità") && SectionTitle(sections[1]) == T("I plugin di Playhub"),
                    width + " home places News immediately before Playhub");
                Check(sections.Length > 0 && SectionItems(sections[0]).SequenceEqual(expectedNews.Take(4)),
                    width + " actual home News repeater contains exactly the four latest first-published unique projects");
                _contentScroller.ChangeView(null, Math.Max(0, _pluginFeaturedHost.ActualHeight - 10), null, true); await Settle();
                await Capture($"home-{width}.png");
                var newsHeading = sections.SelectMany(ReviewDescendants).OfType<Button>()
                    .First(button => AutomationProperties.GetName(button) == T("Novità"));
                Invoke(newsHeading);
                Check(await ReviewWaitUntilAsync(() => _pluginCategoryFilter == "Novità" && _pluginShowAll),
                    width + " native heading Invoke opens the News collection");
                await Settle();
                var renderedNews = ReviewPluginItems(_pluginAllLayout == "list" ? _pluginAllListCache : _pluginAllCardsCache);
                Check(renderedNews.Count == 100 && renderedNews.SequenceEqual(expectedNews),
                    width + " actual News collection contains 100 unique projects in first-publication order");
                Check(renderedNews.Contains(fixtures[5].Plugin) && renderedNews.Contains(fixtures[6].Plugin),
                    width + " actual collection retains both projects sharing a repository basename");
                Check(_pluginAllSort == "added" && _pluginBackButton.Visibility == Visibility.Visible &&
                    ReviewDescendants(_pluginCards).OfType<TextBlock>().Any(block => block.Text == T("Novità")),
                    width + " collection has News title, first-publication sorting and native Back control");
                _contentScroller.ChangeView(null, 0, null, true); await Settle();
                if (width == 960)
                {
                    string fullTitle = fixtures[1].Plugin.Name;
                    Check(await ReviewWaitUntilAsync(() => ReviewDescendants(_pluginCards).OfType<TextBlock>()
                        .Any(block => block.IsLoaded && block.Text == fullTitle && block.ActualWidth > 0)),
                        "960 long News title is realized in the actual native list row");
                    var title = ReviewDescendants(_pluginCards).OfType<TextBlock>().First(block => block.IsLoaded && block.Text == fullTitle);
                    Check(title.Text == fullTitle && title.TextWrapping == TextWrapping.Wrap &&
                        title.TextTrimming == TextTrimming.None && title.MaxLines == 0,
                        "960 long News title retains complete text, wraps without ellipsis and has no line limit");
                    Border? row = null;
                    for (DependencyObject? ancestor = VisualTreeHelper.GetParent(title); ancestor is not null; ancestor = VisualTreeHelper.GetParent(ancestor))
                        if (ancestor is Border border) { row = border; break; }
                    var bounds = row is null ? default : title.TransformToVisual(row).TransformBounds(new Windows.Foundation.Rect(0, 0, title.ActualWidth, title.ActualHeight));
                    Check(row is not null && title.ActualHeight > title.FontSize * 1.5 &&
                        bounds.X >= -.5 && bounds.Y >= -.5 && bounds.Right <= row.ActualWidth + .5 && bounds.Bottom <= row.ActualHeight + .5,
                        "960 complete title uses multiple rendered lines and remains within its actual row bounds");
                }
                var capture = await Capture($"news-{width}.png");
                captures.Add(new { RequestedWidth = width, ActualWidth = ((FrameworkElement)Content).ActualWidth,
                    ActualHeight = ((FrameworkElement)Content).ActualHeight, NativePixelWidth = capture.Width,
                    NativePixelHeight = capture.Height, RasterizationScale = scale, NewsCount = renderedNews.Count });
                if (width == 1280)
                {
                    foreach (var fork in new[] { fixtures[5].Plugin, fixtures[6].Plugin })
                    {
                        OpenPluginPage(fork, _pluginCards); await Settle();
                        RefreshOpenPluginPage(); await Settle();
                        Check(_currentPageTag == "plugin-detail" && _pluginPagePluginKey == PluginStoreKey(fork) &&
                            ReviewDescendants(_pluginPageContent).OfType<TextBlock>().Any(block => block.Text == fork.Name),
                            "actual open and refresh resolve the correct owner for " + fork.RepositorySlug);
                        Invoke(_pluginBackButton);
                        Check(await ReviewWaitUntilAsync(() => _currentPageTag == "plugins" && _pluginCategoryFilter == "Novità"),
                            "native Back returns from the matching fork detail to News");
                    }
                }
                Invoke(_pluginBackButton);
                Check(await ReviewWaitUntilAsync(() => _pluginCategoryFilter is null && !_pluginShowAll && _pluginStoreHistory.Count == 0),
                    width + " native Back Invoke restores discovery home and clears collection history");
            }
            _pluginCategoryFilter = null; _pluginShowAll = true; _pluginFeaturedHost.Visibility = Visibility.Collapsed;
            foreach (string sort in new[] { "added", "updated" })
            {
                _pluginAllSort = sort; InvalidatePluginAllViews(); RenderPluginCards(); await Settle();
                Check(ReviewPluginItems(_pluginAllLayout == "list" ? _pluginAllListCache : _pluginAllCardsCache)
                    .SequenceEqual(sort == "added" ? expectedAdded : expectedUpdated),
                    "actual full collection repeater renders " + sort + " order descending");
            }
            var oldInstalledUpdate = fixtures[20].Plugin;
            foreach (var fixture in fixtures)
            {
                fixture.Plugin.IsInstalled = true;
                fixture.Plugin.InstalledVersion = "0.9.0";
                fixture.Plugin.HasUpdate = ReferenceEquals(fixture.Plugin, oldInstalledUpdate);
            }
            _pluginManageQuery = ""; SwitchPluginStoreMode("manage", animate: false);
            foreach (string sort in new[] { "added", "updated" })
            {
                _pluginAllSort = sort; InvalidatePluginAllViews(); RenderPluginManagement(); await Settle();
                var managed = ReviewPluginItems(_pluginAllLayout == "list" ? _pluginManageListCache : _pluginManageCardsCache).ToArray();
                Check(managed.SequenceEqual(sort == "added" ? expectedAdded : expectedUpdated),
                    "actual Manage repeater preserves descending " + sort + " order for installed fixtures");
                Check(Array.IndexOf(managed, oldInstalledUpdate) > 0 && oldInstalledUpdate.HasUpdate,
                    "Manage " + sort + " does not promote an older installed project because an update is available");
            }
            _pluginAllSort = "name"; InvalidatePluginAllViews(); RenderPluginManagement(); await Settle();
            Check(ReviewPluginItems(_pluginAllLayout == "list" ? _pluginManageListCache : _pluginManageCardsCache).FirstOrDefault() == oldInstalledUpdate,
                "Manage name mode retains available-update priority");
            await ReviewPluginDetailStabilityAsync(output, Check, Capture);
            Check(!_remotePluginCatalogRefreshQueued && !_remotePluginCatalogReviewEnabled,
                "native navigation retains offline review guard throughout");
        }
        catch (Exception error) { failures.Add(error.ToString()); }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture; CultureInfo.CurrentUICulture = previousUiCulture;
            _settings = previousSettings; _plugins.Clear(); foreach (var plugin in previousPlugins) _plugins.Add(plugin);
            _pluginStoreMode = previousMode; _pluginCategoryFilter = previousCategory; _pluginShowAll = previousShowAll;
            _pluginAllSort = previousSort; _pluginStoreHistory.Clear();
            foreach (var entry in previousHistory.Reverse()) _pluginStoreHistory.Push(entry);
            _densityReady = previousDensityReady; InvalidatePluginAllViews(); InvalidateFeaturedFrames();
            _navigation.SelectedItem = previousNavigation; ApplyLanguage(); ShowPage(previousPage);
        }
        await File.WriteAllTextAsync(Path.Combine(output, "chronology-native.json"), JsonSerializer.Serialize(new
        {
            ProcessId = Environment.ProcessId, CompletedAt = DateTimeOffset.UtcNow, Passed = failures.Count == 0,
            Checks = checks, Failures = failures, Captures = captures,
            ActualProductionRendering = true, NativeAutomationInvoke = true, FixturePlugins = 125,
            NoLiveNetwork = true, NoSteamMutation = true, NoPluginInstallation = true, NoUserSettingsSaved = true,
            ScreenshotDoesNotCertifyMicaColors = true
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
#endif

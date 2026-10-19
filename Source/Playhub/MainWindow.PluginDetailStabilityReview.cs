#if PLAYHUB_UI_REVIEW
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Playhub.Models;
using Playhub.Services;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Playhub;

public sealed partial class MainWindow
{
    private async Task ReviewPluginDetailStabilityAsync(string output, Action<bool, string> check,
        Func<string, Task<(int Width, int Height)>> capture)
    {
        var catalogField = typeof(MainWindow).GetField("_catalog", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var previousCatalog = _catalog;
        var previousBaseline = _bundledPluginCatalogTask;
        bool previousDeferred = _pluginCatalogRefreshDeferred;
        var handler = new DetailStabilityReadmeHandler();
        using var http = new HttpClient(handler);
        var service = new PluginCatalogService(http, requestTimeout: TimeSpan.FromSeconds(30));
        var initialRace = new TaskCompletionSource<RemotePluginCatalog>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? racingRefresh = null;
        var frames = new List<object>(); var violations = new HashSet<string>();
        var watch = Stopwatch.StartNew(); string phase = "initial";
        FrameworkElement? detailRoot = null;
        int loaded = 0, unloaded = 0, collectionChanges = 0, collectionResets = 0, samples = 0;
        string selectedKey = ""; int history = 0;
        bool observing = false;
        bool beforeAwaitGate = false, afterAwaitGate = false;
        int loadedWhileOpen = 0, unloadedWhileOpen = 0;
        void OnLoaded(object sender, RoutedEventArgs args) => loaded++;
        void OnUnloaded(object sender, RoutedEventArgs args) => unloaded++;
        void OnCollection(object? sender, NotifyCollectionChangedEventArgs args)
        {
            collectionChanges++;
            if (args.Action == NotifyCollectionChangedAction.Reset) collectionResets++;
        }
        void Sample(object? sender, object args)
        {
            if (!observing) return;
            samples++;
            var catalogRoot = _pageHost.Children.OfType<FrameworkElement>().Single(item => Equals(item.Tag, "plugins"));
            bool sameRoot = _pluginPageContent.Children.Count == 1 && ReferenceEquals(_pluginPageContent.Children[0], detailRoot);
            if (_currentPageTag != "plugin-detail") violations.Add("Detail navigation changed during " + phase);
            if (_pluginPageHost.Visibility != Visibility.Visible || catalogRoot.Visibility != Visibility.Collapsed)
                violations.Add("Catalog/detail visibility alternated during " + phase);
            if (!sameRoot || detailRoot?.IsLoaded != true) violations.Add("Selected detail root was cleared or replaced during " + phase);
            if (_pluginPagePluginKey != selectedKey || _pluginStoreHistory.Count != history)
                violations.Add("Selected identity or Back history changed during " + phase);
            if (frames.Count < 500) frames.Add(new { Milliseconds = watch.ElapsedMilliseconds, Phase = phase,
                Page = _currentPageTag, DetailVisible = _pluginPageHost.Visibility.ToString(),
                CatalogVisible = catalogRoot.Visibility.ToString(), SameRoot = sameRoot, RootLoaded = detailRoot?.IsLoaded,
                ContentChildren = _pluginPageContent.Children.Count, History = _pluginStoreHistory.Count });
        }
        async Task Settle() { await Task.Delay(150); ((FrameworkElement)Content).UpdateLayout(); Sample(null, EventArgs.Empty); }
        static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        try
        {
            check(!_remotePluginCatalogReviewEnabled && !_remotePluginCatalogRefreshQueued,
                "external-detail regression starts with the live refresh transport disabled");
            string root = Path.Combine(AppPaths.LocalDataRoot, "detail-stability-fixture");
            Directory.CreateDirectory(root);
            _settings.PluginRoot = Path.Combine(root, "source"); _settings.DeckyPluginsPath = Path.Combine(root, "installed");
            Directory.CreateDirectory(_settings.PluginRoot); Directory.CreateDirectory(_settings.DeckyPluginsPath);
            string cover = Path.Combine(AppContext.BaseDirectory, "Assets", "Brand", "DeckyStoreBadge.png");
            if (!File.Exists(cover)) throw new FileNotFoundException("Bundled local fixture artwork is missing.", cover);
            // Use the real upstream identity and actual production external-details
            // code. Only transport payloads and filesystem roots are private fixtures.
            var external = PluginCatalogService.GetBundledCatalog().Plugins.FirstOrDefault(entry =>
                entry.Repository.Equals("tormak9970/TabMaster", StringComparison.OrdinalIgnoreCase))
                ?? new RemotePluginCatalogEntry { Name = "TabMaster", Repository = "tormak9970/TabMaster", InstallFolder = "TabMaster", Author = "tormak9970" };
            external = external with { CatalogSource = "outside-store", CatalogStatus = "github", Version = "1.0.0",
                RepositoryUrl = "https://github.com/tormak9970/TabMaster", CoverUrl = cover,
                FirstPublishedAt = "2026-10-18T12:00:00Z", UpdatedAt = "2026-10-18T12:00:00Z",
                LongDescription = "Catalog text before the delayed external README completes." };
            var other = new RemotePluginCatalogEntry { Name = "Other deferred publication", Repository = "private-fixture/OtherDeferred",
                InstallFolder = "OtherDeferred", CoverUrl = cover, FirstPublishedAt = "2026-10-17T12:00:00Z", UpdatedAt = "2026-10-17T12:00:00Z" };
            var baseline = new RemotePluginCatalog { CatalogRevision = 9100, Plugins = new[] { external, other } };
            catalogField.SetValue(this, service);
            _bundledPluginCatalogTask = Task.FromResult(baseline);
            _plugins.Clear(); _pluginCategoryFilter = null; _pluginShowAll = false;
            _pluginStoreHistory.Clear(); _pluginAllSort = "name"; _pluginAllSource = "all";
            _pluginFeaturedHost.Visibility = Visibility.Visible; InvalidatePluginAllViews();
            SwitchPluginStoreMode("discover", animate: false); ShowPage("plugins");
            await RefreshRemoteAwarePluginsAsync(queueRemote: false); await Settle();
            var selected = _plugins.Single(plugin => plugin.RepositorySlug.Equals(external.Repository, StringComparison.OrdinalIgnoreCase));
            check(!selected.IsPlayhubPlugin && selected.RepositorySlug.Equals("tormak9970/TabMaster", StringComparison.OrdinalIgnoreCase),
                "real external TabMaster model takes the production README enrichment path");
            var heading = ReviewDescendants(_pluginCards).OfType<Button>().Single(button => AutomationProperties.GetName(button) == T("Novità"));
            Invoke(heading);
            check(await ReviewWaitUntilAsync(() => _pluginCategoryFilter == "Novità" && _pluginShowAll),
                "external detail is opened from the actual News collection");
            await Settle();
            check(_pluginStoreMode == "discover" && _pluginDiscoverView.Visibility == Visibility.Visible &&
                _pluginManageView.Visibility == Visibility.Collapsed,
                "external News entry is visibly in Discover after the actual production mode switch");
            check(handler.ReadmeCalls == 0, "local artwork avoids README prefetch before the detail Loaded event");

            // Race a real catalog load against opening the detail. The deferred
            // post-await check must protect the very same selected object/tree.
            _bundledPluginCatalogTask = initialRace.Task;
            racingRefresh = RefreshRemoteAwarePluginsAsync(queueRemote: false);
            check(!racingRefresh.IsCompleted, "actual catalog load is pending when navigation opens the detail");
            OpenPluginPage(selected, _pluginCards);
            detailRoot = (FrameworkElement)_pluginPageContent.Children.Single();
            loaded = detailRoot.IsLoaded ? 1 : 0;
            detailRoot.Loaded += OnLoaded; detailRoot.Unloaded += OnUnloaded;
            selectedKey = PluginStoreKey(selected); history = _pluginStoreHistory.Count;
            check(await ReviewWaitUntilAsync(() => detailRoot.IsLoaded), "actual external detail root becomes loaded");
            await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(6));
            check(handler.ReadmeCalls == 1 && !handler.Release.Task.IsCompleted,
                "real Loaded enrichment entered the fake HTTP handler and waits for its README response");
            _plugins.CollectionChanged += OnCollection;
            observing = true; phase = "navigation-race"; CompositionTarget.Rendering += Sample;
            initialRace.TrySetResult(baseline with { CatalogRevision = 9101 });
            await racingRefresh; await Settle();
            afterAwaitGate = _pluginCatalogRefreshDeferred && _plugins.Contains(selected) && collectionChanges == 0;
            check(afterAwaitGate,
                "catalog load completing after detail navigation is deferred without replacing the selected object");
            int generationWhileOpen = _pluginCatalogLoadGeneration;
            for (int index = 1; index <= 6; index++)
            {
                phase = index <= 3 ? "readme-pending" : "readme-complete";
                var latestOther = other with { Name = "Other deferred publication batch " + index,
                    FirstPublishedAt = $"2026-10-18T13:00:{index:00}Z", UpdatedAt = $"2026-10-18T13:00:{index:00}Z" };
                var entries = index == 6 ? new[] { external, latestOther, other with { Name = "Newest deferred project", Repository = "private-fixture/NewestDeferred",
                    InstallFolder = "NewestDeferred", FirstPublishedAt = "2026-10-18T14:00:00Z" } } : new[] { external, latestOther };
                _bundledPluginCatalogTask = Task.FromResult(baseline with { CatalogRevision = 9200 + index, Plugins = entries });
                await RefreshRemoteAwarePluginsAsync(queueRemote: false); await Settle();
                check(_pluginCatalogRefreshDeferred && _plugins.Contains(selected) && ReferenceEquals(detailRoot, _pluginPageContent.Children.Single()),
                    "publication/catalog refresh " + index + " preserves the open external detail root");
                if (index == 3)
                {
                    phase = "readme-completing";
                    handler.Release.TrySetResult(true);
                    check(await ReviewWaitUntilAsync(() => selected.LongDescription.Contains(DetailStabilityReadmeHandler.Marker, StringComparison.Ordinal) &&
                        ReviewDescendants(_pluginPageContent).OfType<TextBlock>().Any(block => block.Text.Contains(DetailStabilityReadmeHandler.Marker, StringComparison.Ordinal))),
                        "actual EnsurePluginDetailsAsync completes and the original detail renders the delayed external README");
                    await Settle();
                }
            }
            phase = "settled-detail"; await Task.Delay(400); Sample(null, EventArgs.Empty);
            beforeAwaitGate = _pluginCatalogLoadGeneration == generationWhileOpen;
            check(beforeAwaitGate, "six detail refresh requests are gated before a new catalog load generation starts");
            check(samples >= 12 && violations.Count == 0, "native frame samples retain detail navigation, visibility, selected identity and root throughout metadata refreshes");
            check(loaded == 1 && unloaded == 0 && collectionChanges == 0 && handler.ReadmeCalls == 1,
                "external detail has one Loaded, zero Unloaded and no duplicate enrichment or catalog replacement while open");
            loadedWhileOpen = loaded; unloadedWhileOpen = unloaded;
            await capture("detail-stable-960.png");
            observing = false; CompositionTarget.Rendering -= Sample;
            int generationBeforeBack = _pluginCatalogLoadGeneration;
            Invoke(_pluginBackButton);
            check(await ReviewWaitUntilAsync(() => _currentPageTag == "plugins" && !_pluginCatalogRefreshDeferred &&
                _visiblePluginCatalogRevision == 9206 && _plugins.Count == 3),
                "native Back applies the latest deferred catalog and retains a nonempty News collection");
            await Task.Delay(500);
            var visible = ReviewPluginItems(_pluginAllLayout == "list" ? _pluginAllListCache : _pluginAllCardsCache);
            check(_pluginCategoryFilter == "Novità" && visible.Count == 3 && visible.Any(plugin => plugin.RepositorySlug == "private-fixture/NewestDeferred"),
                "return from external detail shows all newly published projects rather than an empty catalog");
            check(_pluginCatalogLoadGeneration == generationBeforeBack + 1 && collectionResets == 1 && unloaded == 1,
                "Back applies exactly one deferred catalog load and unloads the selected detail only once");
            check(handler.UnexpectedRequests.Count == 0, "external enrichment uses only the fake README HTTP response and no live network");
        }
        finally
        {
            observing = false; CompositionTarget.Rendering -= Sample;
            _plugins.CollectionChanged -= OnCollection;
            handler.Release.TrySetResult(true);
            initialRace.TrySetResult(new RemotePluginCatalog());
            if (racingRefresh is not null) await racingRefresh;
            if (detailRoot is not null) { detailRoot.Loaded -= OnLoaded; detailRoot.Unloaded -= OnUnloaded; }
            catalogField.SetValue(this, previousCatalog);
            _bundledPluginCatalogTask = previousBaseline; _pluginCatalogRefreshDeferred = previousDeferred;
            await File.WriteAllTextAsync(Path.Combine(output, "detail-stability-native.json"), JsonSerializer.Serialize(new
            {
                ProcessId = Environment.ProcessId, Samples = samples, Frames = frames, Violations = violations,
                DetailLoaded = loaded, DetailUnloaded = unloaded, CatalogChanges = collectionChanges, CatalogResets = collectionResets,
                DetailLoadedWhileOpen = loadedWhileOpen, DetailUnloadedWhileOpen = unloadedWhileOpen,
                BeforeAwaitGateVerified = beforeAwaitGate, AfterAwaitGateVerified = afterAwaitGate,
                handler.ReadmeCalls, handler.UnexpectedRequests, DirectRefreshesWhileOpen = 6, InFlightNavigationRace = true,
                ActualExternalRepository = "tormak9970/TabMaster", ActualProductionEnrichment = true, FakeHttpOnly = true,
                ControlledNativeRegression = true, UserReportedEpisodeReproduced = false, HttpReadmePayloadIsSynthetic = true
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private sealed class DetailStabilityReadmeHandler : HttpMessageHandler
    {
        internal const string Marker = "Delayed external README was rendered without replacing this detail root.";
        internal int ReadmeCalls;
        internal readonly List<string> UnexpectedRequests = new();
        internal readonly TaskCompletionSource<bool> Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!request.RequestUri!.AbsoluteUri.Equals("https://api.github.com/repos/tormak9970/TabMaster/readme", StringComparison.OrdinalIgnoreCase))
            {
                lock (UnexpectedRequests) UnexpectedRequests.Add(request.RequestUri.AbsoluteUri);
                throw new HttpRequestException("Unexpected request in isolated detail fixture.");
            }
            Interlocked.Increment(ref ReadmeCalls); Entered.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { path = "README.md", encoding = "base64",
                content = Convert.ToBase64String(Encoding.UTF8.GetBytes(Marker)),
                download_url = "https://raw.githubusercontent.com/tormak9970/TabMaster/HEAD/README.md" });
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            response.Content.Headers.ContentType = new("application/json");
            return response;
        }
    }
}
#endif

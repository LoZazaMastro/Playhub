using Playhub.Models;
using Playhub.Services;
using System.Collections.ObjectModel;

namespace Microsoft.UI.Dispatching
{
    public enum DispatcherQueuePriority { Low }
    public sealed class TestDispatcher
    {
        public Queue<Action> Pending { get; } = new();
        public bool TryEnqueue(DispatcherQueuePriority priority, Action action) { Pending.Enqueue(action); return true; }
        public void RunNext() => Pending.Dequeue()();
    }
}

namespace Microsoft.UI.Xaml.Controls { public enum InfoBarSeverity { Warning } }

namespace Playhub.Services
{
    public static class AppPaths
    {
        public static int Reads;
        public static string LocalDataRoot { get { Reads++; return Path.Combine(AppContext.BaseDirectory, "test-user-cache"); } }
    }
    public static class Diag { public static void Step(string message) { } }
}

namespace Playhub
{
    public sealed partial class MainWindow
    {
        private readonly Dictionary<string, object> _pluginInstallOperations = new();
        private readonly HashSet<string> _pluginUninstalls = new();
        private bool _pluginBulkUpdateRunning;
        private bool _pluginCardsDirty;
        private bool _pluginManagementDirty;
        private string _currentPageTag = "welcome";
        private readonly ObservableCollection<DeckyPluginInfo> _plugins = new();
        private readonly TestCatalog _catalog = new();
        private readonly TestSettings _settings = new();
        private int _renders, _errors, _pluginUpdateNotifications;
        public event EventHandler? Closed;
        private void CloseFixture() => Closed?.Invoke(this, EventArgs.Empty);
        private Microsoft.UI.Dispatching.TestDispatcher DispatcherQueue { get; } = new();
        private sealed class TestSettings { public string PluginRoot = ""; public string DeckyPluginsPath = ""; }
        private sealed class TestCatalog
        {
            public Func<Task<bool>> ReleaseRefresher = () => Task.FromResult(false);
            public Task<bool> RefreshReleasesAsync(IReadOnlyList<DeckyPluginInfo> plugins, CancellationToken cancellationToken = default) => ReleaseRefresher().WaitAsync(cancellationToken);
            public Func<IReadOnlyList<DeckyPluginInfo>, Action, CancellationToken, Task> PublicationRefresher = (_, _, _) => Task.CompletedTask;
            public Task RefreshPublicationDatesAsync(IReadOnlyList<DeckyPluginInfo> plugins, Action onBatchChanged, CancellationToken cancellationToken = default) =>
                PublicationRefresher(plugins, onBatchChanged, cancellationToken);
            public Func<string, string, RemotePluginCatalog, Task<IReadOnlyList<DeckyPluginInfo>>> Loader =
                (root, installed, catalog) => new PluginCatalogService().LoadAsync(root, installed, catalog);
            public Task<IReadOnlyList<DeckyPluginInfo>> LoadAsync(string root, string installed, RemotePluginCatalog catalog) => Loader(root, installed, catalog);
        }
        private static IDisposable BeginNotificationContext(string context) => new Scope();
        private sealed class Scope : IDisposable { public void Dispose() { } }
        private void SetStatus(string message, Microsoft.UI.Xaml.Controls.InfoBarSeverity severity) => _errors++;
        private static void InvalidatePluginAllViews() { }
        private static void InvalidateFeaturedFrames() { }
        private void RenderVisiblePluginView()
        {
            IntegrationTests.Check(_pluginCardsDirty && _pluginManagementDirty, "Views not invalidated.");
            _renders++;
        }
        private static void RefreshOpenPluginPage() { }
        private void ShowPluginUpdatesNotification() => _pluginUpdateNotifications++;
        private static bool IsIntegratedGamingModePlugin(DeckyPluginInfo plugin) => plugin.Name == "Gaming Mode";
        private Task RefreshPluginsAsync() => RefreshRemoteAwarePluginsAsync();

        internal static async Task RunCatalogUiTests()
        {
            using var files = new TestFiles();
            var window = new MainWindow();
            IntegrationTests.Check(window._currentPageTag == "welcome" && !window._pluginCatalogRefreshDeferred,
                "Publication work must begin outside the Store without deferred catalog work in ordinary fixtures.");
#if PLAYHUB_UI_REVIEW
            window._currentPageTag = "plugins";
            window._publicationRenderQueued = false;
            window._remotePluginPublicationLifetime = new CancellationTokenSource();
            window._catalog.PublicationRefresher = (_, _, _) => throw new InvalidOperationException("Review started live publication discovery.");
            window.QueuePluginPublicationRefresh();
            IntegrationTests.Check(window._remotePluginPublicationRefreshCompletion.IsCompleted && !window._publicationRenderQueued &&
                !window._remotePluginPublicationLifetime.IsCancellationRequested, "UI Review changed the publication queue despite its offline guard.");
            window._remotePluginPublicationLifetime.Dispose(); window._remotePluginPublicationLifetime = null;
            window._currentPageTag = "welcome";
#endif
            window._settings.PluginRoot = files.PluginRoot;
            window._settings.DeckyPluginsPath = files.InstalledRoot;
            var bundled = PluginCatalogService.GetBundledCatalog();
            window._bundledPluginCatalogTask = Task.FromResult(bundled);
#if !PLAYHUB_UI_REVIEW
            using var fixture = new Fixture();
            var extra = bundled.Plugins[0] with { Repository = "LoZazaMastro/Ui-New", RepositoryUrl = "https://github.com/LoZazaMastro/Ui-New",
                Name = "UI New", InstallFolder = "ui-new", Aliases = Array.Empty<string>(), ReleaseAssetName = "", CatalogReleaseUrl = "" };
            fixture.Handler.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new ByteArrayContent(IntegrationTests.Serialize(new RemotePluginCatalog { CatalogRevision = bundled.CatalogRevision + 1, Plugins = new[] { extra } })) });
            window._remotePluginCatalog = fixture.Service;
#endif
            AppPaths.Reads = 0;
            await window.RefreshPluginsAsync();
            IntegrationTests.Check(window._renders == 1 && window._plugins.Count == 241 && window._errors == 0, "Local baseline did not render first.");
#if PLAYHUB_UI_REVIEW
            IntegrationTests.Check(window.DispatcherQueue.Pending.Count == 0 && AppPaths.Reads == 0,
                "UI review scheduled network or accessed user cache.");
#else
            IntegrationTests.Check(fixture.Handler.Calls == 0 && window.DispatcherQueue.Pending.Count == 1, "Network ran before first render.");
            window.DispatcherQueue.RunNext();
            await WaitFor(() => !window._remotePluginCatalogRefreshQueued);
            IntegrationTests.Check(window._renders == 2 && window._plugins.Count == 242 && window._visiblePluginCatalogRevision == bundled.CatalogRevision + 1,
                "New remote revision did not refresh visible catalog.");
            window.QueueRemotePluginCatalogRefresh();
            window.DispatcherQueue.RunNext();
            await WaitFor(() => !window._remotePluginCatalogRefreshQueued);
            IntegrationTests.Check(window._renders == 2 && fixture.Handler.Calls == 1, "Unchanged revision rebuilt/refetched.");

            fixture.Now += RemotePluginCatalogService.RefreshInterval;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Handler.Respond = async (_, token) =>
            {
                entered.SetResult();
                await release.Task.WaitAsync(token);
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                { Content = new ByteArrayContent(IntegrationTests.Serialize(new RemotePluginCatalog { CatalogRevision = bundled.CatalogRevision + 2, Plugins = new[] { extra with { Version = "3.0.0" } } })) };
            };
            window.QueueRemotePluginCatalogRefresh();
            window.DispatcherQueue.RunNext();
            await entered.Task;
            var protectedObject = window._plugins[0];
            window._pluginInstallOperations.Add("operation", new object());
            release.SetResult();
            await WaitFor(() => !window._remotePluginCatalogRefreshQueued);
            IntegrationTests.Check(window._renders == 2 && ReferenceEquals(window._plugins[0], protectedObject), "Refresh replaced an installing object.");
            window._pluginInstallOperations.Clear();
            await window.RefreshPluginsAsync();
            IntegrationTests.Check(window._visiblePluginCatalogRevision == bundled.CatalogRevision + 2 && window._renders == 3, "Deferred revision not applied on natural refresh.");
            window.DispatcherQueue.RunNext();
            await WaitFor(() => !window._remotePluginCatalogRefreshQueued);
            window._catalog.ReleaseRefresher = () => Task.FromResult(true);
            window.QueueRemotePluginCatalogRefresh();
            window.DispatcherQueue.RunNext();
            await WaitFor(() => !window._remotePluginCatalogRefreshQueued);
            await window._remotePluginReleaseRefreshCompletion;
            IntegrationTests.Check(window._renders == 4 && window._visiblePluginCatalogRevision == bundled.CatalogRevision + 2,
                "New plugin releases did not refresh the UI when catalog revision stayed unchanged.");
            IntegrationTests.Check(window._pluginUpdateNotifications == window._renders,
                "Plugin update notification was not refreshed after background release discovery.");
            window._catalog.ReleaseRefresher = () => Task.FromResult(false);
#endif
            var original = window._plugins[0];
            var renders = window._renders;
            window._pluginUninstalls.Add("busy");
            await window.RefreshPluginsAsync();
            window._pluginUninstalls.Clear();
            window._pluginBulkUpdateRunning = true;
            await window.RefreshPluginsAsync();
            window._pluginBulkUpdateRunning = false;
            IntegrationTests.Check(window._renders == renders && ReferenceEquals(window._plugins[0], original), "Uninstall/bulk guard replaced objects.");

            var scanEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var scanRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window._catalog.Loader = async (_, _, _) =>
            { scanEntered.SetResult(); await scanRelease.Task; return new[] { new DeckyPluginInfo { Name = "Should not replace" } }; };
            var pendingRefresh = window.RefreshPluginsAsync();
            await scanEntered.Task;
            window._pluginUninstalls.Add("busy");
            scanRelease.SetResult();
            await pendingRefresh;
            IntegrationTests.Check(window._renders == renders && ReferenceEquals(window._plugins[0], original), "Operation starting during scan lost objects.");
            IntegrationTests.Check(window._errors == 0, "Integration raised a store warning.");
            await RunDetailRefreshGuards();
#if PLAYHUB_UI_REVIEW
            await RunOptInCatalogQueueTests();
#else
            await RunPublicationQueueTests();
#endif
        }

        private static async Task RunDetailRefreshGuards()
        {
            using var files = new TestFiles(); using var fixture = new Fixture();
            var window = new MainWindow();
            window._settings.PluginRoot = files.PluginRoot; window._settings.DeckyPluginsPath = files.InstalledRoot;
            window._remotePluginCatalog = fixture.Service;
            window._bundledPluginCatalogTask = Task.FromResult(new RemotePluginCatalog { CatalogRevision = 1 });
            var original = new DeckyPluginInfo { Name = "Enriched detail object", RepositorySlug = "fixture/detail" };
            var replacement = new DeckyPluginInfo { Name = "Refreshed browsing object", RepositorySlug = "fixture/detail" };
            window._plugins.Add(original);
            var loads = 0;
            window._catalog.Loader = (_, _, _) => { loads++; return Task.FromResult<IReadOnlyList<DeckyPluginInfo>>(new[] { replacement }); };
            window._currentPageTag = "plugin-detail";
            await window.RefreshRemoteAwarePluginsAsync(queueRemote: false);
            IntegrationTests.Check(window._pluginCatalogRefreshDeferred && loads == 0 && window._renders == 0 &&
                ReferenceEquals(window._plugins[0], original), "An open detail page did not defer loading and preserve its enriched object.");

            window._currentPageTag = "plugins"; window._pluginCatalogRefreshDeferred = false;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window._catalog.Loader = async (_, _, _) => { loads++; entered.SetResult(); await release.Task; return new[] { replacement }; };
            var pending = window.RefreshRemoteAwarePluginsAsync(queueRemote: false);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            window._currentPageTag = "plugin-detail"; release.SetResult(); await pending;
            IntegrationTests.Check(window._pluginCatalogRefreshDeferred && loads == 1 && window._renders == 0 &&
                ReferenceEquals(window._plugins[0], original), "Opening detail during an awaited load allowed stale catalog objects to replace its tree.");

            window._currentPageTag = "plugins";
            window._catalog.Loader = (_, _, _) => { loads++; return Task.FromResult<IReadOnlyList<DeckyPluginInfo>>(new[] { replacement }); };
            await window.RefreshRemoteAwarePluginsAsync(queueRemote: false);
            IntegrationTests.Check(!window._pluginCatalogRefreshDeferred && loads == 2 && window._renders == 1 &&
                ReferenceEquals(window._plugins[0], replacement) && fixture.Handler.Calls == 0,
                "Resuming browsing failed to commit the deferred local catalog exactly once without a membership request.");
            window.CancelPluginPublicationRefresh();
            await window._remotePluginPublicationRefreshCompletion;
            Console.WriteLine("PASS 3 actual detail refresh guards: before loading, after await and deferred browsing commit.");
        }

#if !PLAYHUB_UI_REVIEW
        private static async Task RunPublicationQueueTests()
        {
            using var files = new TestFiles(); using var fixture = new Fixture();
            var window = new MainWindow();
            window._settings.PluginRoot = files.PluginRoot; window._settings.DeckyPluginsPath = files.InstalledRoot;
            window._remotePluginCatalog = fixture.Service;
            window._bundledPluginCatalogTask = Task.FromResult(new RemotePluginCatalog { CatalogRevision = 1 });
            var original = new DeckyPluginInfo { Name = "Publication fixture", RepositorySlug = "fixture/publication" };
            window._plugins.Add(original);
            window._catalog.Loader = (_, _, _) => Task.FromResult<IReadOnlyList<DeckyPluginInfo>>(new[] { original });
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Action? publish = null; int calls = 0, cancelled = 0;
            window._catalog.PublicationRefresher = async (_, changed, token) =>
            {
                calls++; publish = changed; entered.TrySetResult();
                try { await release.Task.WaitAsync(token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { cancelled++; throw; }
            };
            window.QueuePluginPublicationRefresh();
            IntegrationTests.Check(calls == 0, "Publication discovery started outside the Plugin Store.");
            window._currentPageTag = "plugins";
            for (int index = 0; index < 6; index++) window.QueuePluginPublicationRefresh();
            await entered.Task;
            IntegrationTests.Check(calls == 1, "Publication requests did not coalesce into one active wave.");
            window._pluginInstallOperations.Add("busy", new object()); publish!();
            window.DispatcherQueue.RunNext();
            IntegrationTests.Check(window._renders == 0 && ReferenceEquals(window._plugins[0], original),
                "Publication callback replaced a plugin owned by an installation operation.");
            window._pluginInstallOperations.Clear(); publish(); window.DispatcherQueue.RunNext();
            await WaitFor(() => window._renders == 1);
            IntegrationTests.Check(fixture.Handler.Calls == 0 && calls == 1,
                "Publication rendering triggered a remote membership fetch or duplicated its active wave.");
            window._currentPageTag = "welcome"; window.CancelPluginPublicationRefresh();
            await window._remotePluginPublicationRefreshCompletion;
            while (window.DispatcherQueue.Pending.Count > 0) window.DispatcherQueue.RunNext();
            IntegrationTests.Check(cancelled == 1 && calls == 1,
                "Leaving the Store failed to cancel publication work or restarted it while hidden.");
            window._remotePluginCatalogLifetime.Cancel(); window._currentPageTag = "plugins";
            window.QueuePluginPublicationRefresh();
            IntegrationTests.Check(calls == 1, "Shutdown allowed publication discovery to restart.");
            Console.WriteLine("PASS 6 actual publication queue guards: visibility, coalescing, install ownership, local rendering, cancellation and shutdown.");
        }
#endif

#if PLAYHUB_UI_REVIEW
        private static async Task RunOptInCatalogQueueTests()
        {
            var flag = Environment.GetEnvironmentVariable("PLAYHUB_REVIEW_PLUGIN_CATALOG");
            var root = Path.Combine(AppPaths.LocalDataRoot, "fresh-catalog-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "source")); Directory.CreateDirectory(Path.Combine(root, "installed"));
            try
            {
                var window = new MainWindow();
                window._settings.PluginRoot = Path.Combine(root, "source"); window._settings.DeckyPluginsPath = Path.Combine(root, "installed");
                var existing = PluginCatalogService.GetBundledCatalog().Plugins.First() with {
                    Name = "Private Extra", InstallFolder = "PrivateExtra", Repository = "fixture/PrivateExtra",
                    RepositoryUrl = "https://github.com/fixture/PrivateExtra", CatalogSource = "outside-store", CatalogStatus = "github",
                    CatalogPluginId = 0, Aliases = [], CoverUrl = "", CatalogReleaseUrl = "", ReleaseAssetName = ""
                };
                var bundled = new RemotePluginCatalog { CatalogRevision = 101, Plugins = [existing] };
                using var fixture = new PinnedUiCatalogFixture(root);
                Environment.SetEnvironmentVariable("PLAYHUB_REVIEW_PLUGIN_CATALOG", "0");
                var optInRequired = false;
                try { await window.ReviewUseRemotePluginCatalog(fixture.Service, bundled, () => Task.FromResult(false), root).DisposeAsync(); }
                catch (InvalidOperationException) { optInRequired = true; }
                IntegrationTests.Check(optInRequired && fixture.Handler.OfficialCalls == 0, "Review transport injection ran without explicit opt-in.");
                Environment.SetEnvironmentVariable("PLAYHUB_REVIEW_PLUGIN_CATALOG", "1");
                var privatePathsRequired = false;
                var safeRoot = window._settings.DeckyPluginsPath; window._settings.DeckyPluginsPath = Path.GetTempPath();
                try { await window.ReviewUseRemotePluginCatalog(fixture.Service, bundled, () => Task.FromResult(false), root).DisposeAsync(); }
                catch (InvalidOperationException) { privatePathsRequired = true; }
                window._settings.DeckyPluginsPath = safeRoot;
                IntegrationTests.Check(privatePathsRequired && fixture.Handler.OfficialCalls == 0, "Review transport accepted an installed directory outside its fixture.");
                await using (window.ReviewUseRemotePluginCatalog(fixture.Service, bundled, () => Task.FromResult(false), root))
                {
                    await window.RefreshPluginsAsync();
                    IntegrationTests.Check(window._renders == 1 && fixture.Handler.OfficialCalls == 0,
                        "Explicit review fixture did network before local baseline render.");
                    for (var i = 0; i < 6; i++) window.QueueRemotePluginCatalogRefresh(forceRefresh: true);
                    IntegrationTests.Check(window.DispatcherQueue.Pending.Count == 1, "Navigation refreshes did not coalesce in one queued callback.");
                    window.DispatcherQueue.RunNext(); await window.ReviewRemotePluginCatalogCompletion;
                    IntegrationTests.Check(window._plugins.Count == 2 && fixture.Handler.OfficialCalls == 1 && fixture.Handler.PackageCalls == 1,
                        "Actual opt-in queue did not verify and render fresh official membership.");
                    await window._remotePluginReleaseRefreshCompletion;
                    var pendingNotes = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); int noteWaves = 0;
                    window._remotePluginCatalogReviewReleaseRefresh = () => { noteWaves++; return pendingNotes.Task; };
                    var first = window.ReviewVisiblePluginCatalogContentVersion;
                    fixture.Handler.Ids = [501, 502];
                    fixture.Handler.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    fixture.Handler.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    var entered = fixture.Handler.Entered;
                    window.QueueRemotePluginCatalogRefresh(forceRefresh: true); window.DispatcherQueue.RunNext(); await entered.Task;
                    for (var i = 0; i < 6; i++) window.QueueRemotePluginCatalogRefresh(forceRefresh: true);
                    fixture.Handler.Release.SetResult(); await window.ReviewRemotePluginCatalogCompletion;
                    IntegrationTests.Check(window._plugins.Count == 3 && window._visiblePluginCatalogRevision == 101 &&
                        window.ReviewVisiblePluginCatalogContentVersion != first && fixture.Handler.OfficialCalls == 3,
                        "Same revision membership change or one trailing forced refresh was lost.");
                    await WaitFor(() => noteWaves == 1);
                    IntegrationTests.Check(!window._remotePluginReleaseRefreshCompletion.IsCompleted && window._plugins.Any(item => item.CatalogPluginId == 502),
                        "Fresh source membership waited for pending GitHub release metadata.");
                    var beforeNoteFailure = window._renders;
                    pendingNotes.SetException(new HttpRequestException("fixture notes unavailable")); await window._remotePluginReleaseRefreshCompletion;
                    IntegrationTests.Check(noteWaves == 1 && window._renders == beforeNoteFailure && window._plugins.Any(item => item.CatalogPluginId == 502),
                        "Optional failed notes reverted membership or duplicated a release wave.");
                    window._remotePluginCatalogReviewReleaseRefresh = () => Task.FromResult(false);
                    var protectedObject = window._plugins[0]; var renders = window._renders;
                    window._pluginInstallOperations.Add("fixture-install", new object()); fixture.Handler.Ids = [502];
                    window.QueueRemotePluginCatalogRefresh(forceRefresh: true); window.DispatcherQueue.RunNext(); await window.ReviewRemotePluginCatalogCompletion;
                    IntegrationTests.Check(window._renders == renders && ReferenceEquals(window._plugins[0], protectedObject),
                        "Fresh official removal replaced an object owned by an install operation.");
                    window._pluginInstallOperations.Clear();
                    await window.RefreshPluginsAsync(); window.DispatcherQueue.RunNext(); await window.ReviewRemotePluginCatalogCompletion;
                    IntegrationTests.Check(window._plugins.Count == 2 && window._plugins.All(item => item.CatalogPluginId != 501) &&
                        window._visiblePluginCatalogRevision == 101, "Deferred source removal did not apply on natural refresh.");
                    await window._remotePluginReleaseRefreshCompletion;
                    var lateNotes = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); int timeoutWaves = 0;
                    window._remotePluginReleaseRefreshTimeout = TimeSpan.FromMilliseconds(40);
                    window._remotePluginCatalogReviewReleaseRefresh = () => { timeoutWaves++; return lateNotes.Task; };
                    for (var i = 0; i < 6; i++) window.QueueRemotePluginReleaseRefresh();
                    await window._remotePluginReleaseRefreshCompletion;
                    var timeoutRenders = window._renders; lateNotes.SetResult(true); await Task.Delay(10);
                    IntegrationTests.Check(timeoutWaves == 1 && window._renders == timeoutRenders, "Deadline did not coalesce release metadata or late result refreshed the UI.");
                    window.CloseFixture(); window.QueueRemotePluginCatalogRefresh(forceRefresh: true);
                    IntegrationTests.Check(window.DispatcherQueue.Pending.Count == 0 && window._remotePluginCatalogLifetime.IsCancellationRequested,
                        "Window shutdown allowed catalog/release callbacks to restart.");
                }
                window.QueueRemotePluginCatalogRefresh(forceRefresh: true);
                IntegrationTests.Check(window.DispatcherQueue.Pending.Count == 0 && !window.ReviewRemotePluginCatalogQueued,
                    "Disposed review fixture revived a refresh or live transport.");
                Console.WriteLine("PASS 13 opt-in actual queue assertions: pinned membership, same revision, coalescing, pending/failed/deadline notes, shutdown, operation guard, teardown and isolation.");
            }
            finally
            {
                Environment.SetEnvironmentVariable("PLAYHUB_REVIEW_PLUGIN_CATALOG", flag);
                Directory.Delete(root, true);
            }
        }
#endif

        private static async Task WaitFor(Func<bool> ready)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!ready()) await Task.Delay(1, deadline.Token);
        }
    }
}

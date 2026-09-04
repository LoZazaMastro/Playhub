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
        private readonly ObservableCollection<DeckyPluginInfo> _plugins = new();
        private readonly TestCatalog _catalog = new();
        private readonly TestSettings _settings = new();
        private int _renders, _errors, _pluginUpdateNotifications;
        private Microsoft.UI.Dispatching.TestDispatcher DispatcherQueue { get; } = new();
        private sealed class TestSettings { public string PluginRoot = ""; public string DeckyPluginsPath = ""; }
        private sealed class TestCatalog
        {
            public Func<Task<bool>> ReleaseRefresher = () => Task.FromResult(false);
            public Task<bool> RefreshReleasesAsync(IReadOnlyList<DeckyPluginInfo> plugins) => ReleaseRefresher();
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
            window._settings.PluginRoot = files.PluginRoot;
            window._settings.DeckyPluginsPath = files.InstalledRoot;
            var bundled = PluginCatalogService.GetBundledCatalog();
            window._bundledPluginCatalogTask = Task.FromResult(bundled);
#if !PLAYHUB_UI_REVIEW
            using var fixture = new Fixture();
            var extra = bundled.Plugins[0] with { Repository = "LoZazaMastro/Ui-New", RepositoryUrl = "https://github.com/LoZazaMastro/Ui-New",
                Name = "UI New", InstallFolder = "ui-new", Aliases = Array.Empty<string>() };
            fixture.Handler.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new ByteArrayContent(IntegrationTests.Serialize(new RemotePluginCatalog { CatalogRevision = 4, Plugins = new[] { extra } })) });
            window._remotePluginCatalog = fixture.Service;
#endif
            AppPaths.Reads = 0;
            await window.RefreshPluginsAsync();
            IntegrationTests.Check(window._renders == 1 && window._plugins.Count == 174 && window._errors == 0, "Local baseline did not render first.");
#if PLAYHUB_UI_REVIEW
            IntegrationTests.Check(window.DispatcherQueue.Pending.Count == 0 && AppPaths.Reads == 0,
                "UI review scheduled network or accessed user cache.");
#else
            IntegrationTests.Check(fixture.Handler.Calls == 0 && window.DispatcherQueue.Pending.Count == 1, "Network ran before first render.");
            window.DispatcherQueue.RunNext();
            await WaitFor(() => !window._remotePluginCatalogRefreshQueued);
            IntegrationTests.Check(window._renders == 2 && window._plugins.Count == 175 && window._visiblePluginCatalogRevision == 4,
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
                { Content = new ByteArrayContent(IntegrationTests.Serialize(new RemotePluginCatalog { CatalogRevision = 5, Plugins = new[] { extra with { Version = "3.0.0" } } })) };
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
            IntegrationTests.Check(window._visiblePluginCatalogRevision == 5 && window._renders == 3, "Deferred revision not applied on natural refresh.");
            window.DispatcherQueue.RunNext();
            await WaitFor(() => !window._remotePluginCatalogRefreshQueued);
            window._catalog.ReleaseRefresher = () => Task.FromResult(true);
            window.QueueRemotePluginCatalogRefresh();
            window.DispatcherQueue.RunNext();
            await WaitFor(() => !window._remotePluginCatalogRefreshQueued);
            IntegrationTests.Check(window._renders == 4 && window._visiblePluginCatalogRevision == 5,
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
        }

#if !PLAYHUB_UI_REVIEW
        private static async Task WaitFor(Func<bool> ready)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!ready()) await Task.Delay(1, deadline.Token);
        }
#endif
    }
}

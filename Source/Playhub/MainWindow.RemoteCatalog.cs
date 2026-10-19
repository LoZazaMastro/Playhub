using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Playhub.Models;
using Playhub.Services;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Playhub;

public sealed partial class MainWindow
{
    private Task<RemotePluginCatalog>? _bundledPluginCatalogTask;
    private long _visiblePluginCatalogRevision = -1;
    private string _visiblePluginCatalogContentVersion = "";
    private int _pluginCatalogLoadGeneration;
    private RemotePluginCatalogService? _remotePluginCatalog;
    private bool _remotePluginCatalogRefreshQueued;
    private bool _remotePluginCatalogForceRefreshRequested;
    private Task _remotePluginCatalogRefreshCompletion = Task.CompletedTask;
    private readonly CancellationTokenSource _remotePluginCatalogLifetime = new();
    private bool _remotePluginCatalogLifetimeAttached;
    private Task _remotePluginReleaseRefreshCompletion = Task.CompletedTask;
    private Task _remotePluginPublicationRefreshCompletion = Task.CompletedTask;
    private CancellationTokenSource? _remotePluginPublicationLifetime;
    private bool _publicationRenderQueued;
    private bool _pluginCatalogRefreshDeferred;
    private TimeSpan _remotePluginReleaseRefreshTimeout = TimeSpan.FromSeconds(20);
#if PLAYHUB_UI_REVIEW
    private bool _remotePluginCatalogReviewEnabled;
    private Func<Task<bool>>? _remotePluginCatalogReviewReleaseRefresh;
#endif

    private bool PluginCatalogOperationsActive => _pluginInstallOperations.Count > 0 ||
        _pluginUninstalls.Count > 0 || _pluginBulkUpdateRunning;

    private Task<RemotePluginCatalog> BundledPluginCatalogAsync() =>
        _bundledPluginCatalogTask ??= Task.Run(PluginCatalogService.GetBundledCatalog);

    private async Task<RemotePluginCatalog> LoadPluginCatalogBaselineAsync()
    {
        var bundled = await BundledPluginCatalogAsync();
#if PLAYHUB_UI_REVIEW
        if (!_remotePluginCatalogReviewEnabled) return bundled;
#endif
        _remotePluginCatalog ??= new RemotePluginCatalogService(
            Path.Combine(AppPaths.LocalDataRoot, "remote-plugin-catalog-v3.json"));
        return (await _remotePluginCatalog.LoadCachedAsync(bundled)).Catalog;
    }

    private async Task RefreshRemoteAwarePluginsAsync(bool queueRemote = true)
    {
        if (PluginCatalogOperationsActive || _remotePluginCatalogLifetime.IsCancellationRequested) return;
        // Keep the selected, enriched detail tree intact while background sources refresh.
        // Apply the cached catalog once the user returns to browsing.
        if (_currentPageTag == "plugin-detail")
        {
            _pluginCatalogRefreshDeferred = true;
            return;
        }
        var generation = ++_pluginCatalogLoadGeneration;
        var pluginRoot = _settings.PluginRoot;
        var installedRoot = _settings.DeckyPluginsPath;
        using var context = BeginNotificationContext("plugins");
        try
        {
            var baseline = await LoadPluginCatalogBaselineAsync();
            var loaded = await _catalog.LoadAsync(pluginRoot, installedRoot, baseline);
            // An operation may have started while the worker was scanning installed files.
            // Leave its object references intact; the next natural refresh uses cached data.
            if (PluginCatalogOperationsActive || generation != _pluginCatalogLoadGeneration || _remotePluginCatalogLifetime.IsCancellationRequested) return;
            // The user may have opened a detail page while the offline load was in flight.
            if (_currentPageTag == "plugin-detail")
            {
                _pluginCatalogRefreshDeferred = true;
                return;
            }
            _pluginCatalogRefreshDeferred = false;
            InvalidatePluginAllViews();
            InvalidateFeaturedFrames();
            _plugins.Clear();
            foreach (var plugin in loaded.Where(plugin => !IsIntegratedGamingModePlugin(plugin)))
                _plugins.Add(plugin);
            _visiblePluginCatalogRevision = baseline.CatalogRevision;
            _visiblePluginCatalogContentVersion = RemotePluginCatalogService.ComputeContentVersion(baseline);
            _pluginCardsDirty = true;
            _pluginManagementDirty = true;
            RenderVisiblePluginView();
            ShowPluginUpdatesNotification();
            if (queueRemote) QueueRemotePluginCatalogRefresh();
            QueuePluginPublicationRefresh();
        }
        catch
        {
            SetStatus("Plugin Store non disponibile. Riprova tra poco.", InfoBarSeverity.Warning);
        }
    }

    private void QueueRemotePluginCatalogRefresh(bool forceRefresh = false)
    {
#if PLAYHUB_UI_REVIEW
        // Ordinary visual review remains offline. Only an explicitly enabled,
        // isolated fixture can enter the actual production refresh queue.
        if (!_remotePluginCatalogReviewEnabled) return;
#endif
        if (_remotePluginCatalogLifetime.IsCancellationRequested) return;
        if (!_remotePluginCatalogLifetimeAttached)
        {
            _remotePluginCatalogLifetimeAttached = true;
            Closed += (_, _) => _remotePluginCatalogLifetime.Cancel();
        }
        _remotePluginCatalogForceRefreshRequested |= forceRefresh;
        if (_remotePluginCatalogRefreshQueued) return;
        _remotePluginCatalogRefreshQueued = true;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _remotePluginCatalogRefreshCompletion = completion.Task;
        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, async () =>
        {
            try
            {
                do
                {
                    var force = _remotePluginCatalogForceRefreshRequested;
                    _remotePluginCatalogForceRefreshRequested = false;
                    var bundled = await BundledPluginCatalogAsync();
                    _remotePluginCatalog ??= new RemotePluginCatalogService(Path.Combine(AppPaths.LocalDataRoot, "remote-plugin-catalog-v3.json"));
                    var refreshed = await _remotePluginCatalog.RefreshAsync(bundled, _remotePluginCatalogLifetime.Token, forceRefresh: force);
                    if (!string.IsNullOrEmpty(refreshed.Error)) Diag.Step("Remote plugin catalog retained a fallback: " + refreshed.Error);
                    // Official membership can change without a Playhub publication revision.
                    if (refreshed.ContentVersion != _visiblePluginCatalogContentVersion && !PluginCatalogOperationsActive && !_remotePluginCatalogLifetime.IsCancellationRequested)
                        await RefreshPluginsAsync();
                    QueueRemotePluginReleaseRefresh();
                    QueuePluginPublicationRefresh();
                } while (_remotePluginCatalogForceRefreshRequested);
            }
            catch (Exception ex) { Diag.Step("Remote plugin catalog: " + ex.Message); }
            finally { _remotePluginCatalogRefreshQueued = false; completion.TrySetResult(true); }
        })) { _remotePluginCatalogRefreshQueued = false; completion.TrySetResult(false); }
    }

    private void QueueRemotePluginReleaseRefresh()
    {
        if (_remotePluginCatalogLifetime.IsCancellationRequested || !_remotePluginReleaseRefreshCompletion.IsCompleted) return;
        _remotePluginReleaseRefreshCompletion = RefreshAsync();
        async Task RefreshAsync()
        {
            // Publish source membership before optional release/notes discovery.
            // Yield also installs the coalescing task before a synchronous result.
            await Task.Yield();
            var pluginRoot = _settings.PluginRoot; var installedRoot = _settings.DeckyPluginsPath;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_remotePluginCatalogLifetime.Token);
            deadline.CancelAfter(_remotePluginReleaseRefreshTimeout);
            try
            {
#if PLAYHUB_UI_REVIEW
                var changed = await _remotePluginCatalogReviewReleaseRefresh!().WaitAsync(deadline.Token);
#else
                var changed = await _catalog.RefreshReleasesAsync(_plugins.ToArray(), cancellationToken: deadline.Token);
#endif
                deadline.Token.ThrowIfCancellationRequested();
                if (changed && !PluginCatalogOperationsActive && pluginRoot == _settings.PluginRoot && installedRoot == _settings.DeckyPluginsPath)
                    await RefreshRemoteAwarePluginsAsync(queueRemote: false);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { Diag.Step("Plugin release metadata retained its cache: " + error.GetType().Name); }
        }
    }

    private void CancelPluginPublicationRefresh() => _remotePluginPublicationLifetime?.Cancel();

    private void QueuePluginPublicationRefresh()
    {
#if PLAYHUB_UI_REVIEW
        // Native fixtures remain offline, including source publication discovery.
        return;
#else
        if (_currentPageTag is not ("plugins" or "plugin-detail") ||
            _remotePluginCatalogLifetime.IsCancellationRequested || !_remotePluginPublicationRefreshCompletion.IsCompleted) return;
        _remotePluginPublicationLifetime?.Dispose();
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_remotePluginCatalogLifetime.Token);
        _remotePluginPublicationLifetime = lifetime;
        var plugins = _plugins.ToArray();
        _remotePluginPublicationRefreshCompletion = RefreshAsync();
        async Task RefreshAsync()
        {
            await Task.Yield();
            try
            {
                await _catalog.RefreshPublicationDatesAsync(plugins, () =>
                {
                    DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => PublishBatch());
                }, lifetime.Token);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception error) { Diag.Step("Plugin publication dates retained their cache: " + error.GetType().Name); }
            finally
            {
                // A fast return to the Store may have happened while cancellation drained.
                if (lifetime.IsCancellationRequested && !_remotePluginCatalogLifetime.IsCancellationRequested)
                    DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, QueuePluginPublicationRefresh);
            }
        }
        async void PublishBatch()
        {
            if (lifetime.IsCancellationRequested || _publicationRenderQueued || PluginCatalogOperationsActive ||
                !ReferenceEquals(_remotePluginPublicationLifetime, lifetime)) return;
            _publicationRenderQueued = true;
            try { await RefreshRemoteAwarePluginsAsync(queueRemote: false); }
            catch (Exception error) { Diag.Step("Plugin publication date rendering: " + error.GetType().Name); }
            finally { _publicationRenderQueued = false; }
        }
#endif
    }

#if PLAYHUB_UI_REVIEW
    internal Task ReviewRemotePluginCatalogCompletion => _remotePluginCatalogRefreshCompletion;
    internal string ReviewVisiblePluginCatalogContentVersion => _visiblePluginCatalogContentVersion;
    internal bool ReviewRemotePluginCatalogQueued => _remotePluginCatalogRefreshQueued;

    internal IAsyncDisposable ReviewUseRemotePluginCatalog(RemotePluginCatalogService service,
        RemotePluginCatalog bundled, Func<Task<bool>> releaseRefresher, string fixtureRoot)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(bundled);
        ArgumentNullException.ThrowIfNull(releaseRefresher);
        if (Environment.GetEnvironmentVariable("PLAYHUB_REVIEW_PLUGIN_CATALOG") != "1" ||
            _remotePluginCatalogReviewEnabled || _remotePluginCatalogRefreshQueued || !_remotePluginReleaseRefreshCompletion.IsCompleted)
            throw new InvalidOperationException("Remote catalog fixture requires an explicit idle review scope.");
        static bool Within(string child, string parent) => Path.GetFullPath(child).StartsWith(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
        if (!Within(AppPaths.LocalDataRoot, AppContext.BaseDirectory) ||
            !Within(fixtureRoot, AppPaths.LocalDataRoot) ||
            !Within(_settings.PluginRoot, fixtureRoot) || !Within(_settings.DeckyPluginsPath, fixtureRoot))
            throw new InvalidOperationException("Remote catalog fixture paths must be isolated beneath review AppPaths.");
        var previousService = _remotePluginCatalog;
        var previousBundled = _bundledPluginCatalogTask;
        var previousVersion = _visiblePluginCatalogContentVersion;
        var previousRevision = _visiblePluginCatalogRevision;
        var previousCompletion = _remotePluginCatalogRefreshCompletion;
        _remotePluginCatalog = service;
        _bundledPluginCatalogTask = Task.FromResult(bundled);
        _remotePluginCatalogReviewReleaseRefresh = releaseRefresher;
        _remotePluginCatalogForceRefreshRequested = false;
        _remotePluginCatalogReviewEnabled = true;
        return new RemoteCatalogReviewScope(async () =>
        {
            // Keep the fixture transport installed until every queued callback
            // has completed; teardown must never revive the live transport.
            await _remotePluginCatalogRefreshCompletion;
            await _remotePluginReleaseRefreshCompletion;
            _remotePluginCatalogReviewEnabled = false;
            _remotePluginCatalogReviewReleaseRefresh = null;
            _remotePluginCatalog = previousService;
            _bundledPluginCatalogTask = previousBundled;
            _visiblePluginCatalogContentVersion = previousVersion;
            _visiblePluginCatalogRevision = previousRevision;
            _remotePluginCatalogRefreshCompletion = previousCompletion;
            _remotePluginCatalogForceRefreshRequested = false;
        });
    }

    private sealed class RemoteCatalogReviewScope(Func<Task> restore) : IAsyncDisposable
    {
        private Func<Task>? _restore = restore;
        public async ValueTask DisposeAsync()
        {
            var action = _restore; _restore = null;
            if (action is not null) await action();
        }
    }
#endif
}

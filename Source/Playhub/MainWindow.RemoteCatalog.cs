using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Playhub.Models;
using Playhub.Services;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Playhub;

public sealed partial class MainWindow
{
    private Task<RemotePluginCatalog>? _bundledPluginCatalogTask;
    private long _visiblePluginCatalogRevision = -1;
    private int _pluginCatalogLoadGeneration;
#if !PLAYHUB_UI_REVIEW
    private RemotePluginCatalogService? _remotePluginCatalog;
    private bool _remotePluginCatalogRefreshQueued;
#endif

    private bool PluginCatalogOperationsActive => _pluginInstallOperations.Count > 0 ||
        _pluginUninstalls.Count > 0 || _pluginBulkUpdateRunning;

    private Task<RemotePluginCatalog> BundledPluginCatalogAsync() =>
        _bundledPluginCatalogTask ??= Task.Run(PluginCatalogService.GetBundledCatalog);

    private async Task<RemotePluginCatalog> LoadPluginCatalogBaselineAsync()
    {
        var bundled = await BundledPluginCatalogAsync();
#if PLAYHUB_UI_REVIEW
        return bundled;
#else
        _remotePluginCatalog ??= new RemotePluginCatalogService(
            Path.Combine(AppPaths.LocalDataRoot, "remote-plugin-catalog-v3.json"));
        return (await _remotePluginCatalog.LoadCachedAsync(bundled)).Catalog;
#endif
    }

    private async Task RefreshRemoteAwarePluginsAsync()
    {
        if (PluginCatalogOperationsActive) return;
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
            if (PluginCatalogOperationsActive || generation != _pluginCatalogLoadGeneration) return;
            InvalidatePluginAllViews();
            InvalidateFeaturedFrames();
            _plugins.Clear();
            foreach (var plugin in loaded.Where(plugin => !IsIntegratedGamingModePlugin(plugin)))
                _plugins.Add(plugin);
            _visiblePluginCatalogRevision = baseline.CatalogRevision;
            _pluginCardsDirty = true;
            _pluginManagementDirty = true;
            RenderVisiblePluginView();
            RefreshOpenPluginPage();
            ShowPluginUpdatesNotification();
            QueueRemotePluginCatalogRefresh();
        }
        catch
        {
            SetStatus("Plugin Store non disponibile. Riprova tra poco.", InfoBarSeverity.Warning);
        }
    }

    private void QueueRemotePluginCatalogRefresh()
    {
#if !PLAYHUB_UI_REVIEW
        if (_remotePluginCatalogRefreshQueued) return;
        _remotePluginCatalogRefreshQueued = true;
        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, async () =>
        {
            try
            {
                var bundled = await BundledPluginCatalogAsync();
                var refreshed = await _remotePluginCatalog!.RefreshAsync(bundled);
                var releasesChanged = await _catalog.RefreshReleasesAsync(_plugins.ToArray());
                // No rebuild for unchanged revisions or while install/uninstall owns objects.
                if ((releasesChanged || refreshed.Catalog.CatalogRevision > _visiblePluginCatalogRevision) && !PluginCatalogOperationsActive)
                    await RefreshPluginsAsync();
            }
            catch (Exception ex) { Diag.Step("Remote plugin catalog: " + ex.Message); }
            finally { _remotePluginCatalogRefreshQueued = false; }
        })) _remotePluginCatalogRefreshQueued = false;
#endif
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;

namespace Playhub;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, double> _navigationOffsets = new();
    private int _navigationRestoreVersion;
    private DispatcherTimer? _navigationRestoreTimer;
    private (string Key, double Offset)? _pendingNavigationRestore;
    private bool _navigationBringIntoView = true;
    private DispatcherTimer? _pluginSearchDelay;

    private string NavigationPositionKey => _currentPageTag == "plugins"
        ? "plugins/" + _pluginStoreMode + (_pluginStoreMode == "discover"
            ? "/" + (_pluginCategoryFilter ?? (_pluginShowAll ? "all" : "home")) : string.Empty)
        : _currentPageTag;

    private void SaveNavigationPosition()
    {
        _navigationOffsets[NavigationPositionKey] = CurrentNavigationOffset();
    }

    private double CurrentNavigationOffset() => _pendingNavigationRestore is { } pending &&
        pending.Key == NavigationPositionKey ? pending.Offset : _contentScroller.VerticalOffset;

    private void CancelNavigationRestore()
    {
        _navigationRestoreVersion++;
        _navigationRestoreTimer?.Stop();
        if (_pendingNavigationRestore is not null)
            _contentScroller.BringIntoViewOnFocusChange = _navigationBringIntoView;
        _pendingNavigationRestore = null;
    }

    private void RestoreNavigationPosition(bool reset = false)
    {
        CancelNavigationRestore();
        var version = ++_navigationRestoreVersion;
        var key = NavigationPositionKey;
        var offset = !reset && _navigationOffsets.TryGetValue(key, out var saved) ? saved : 0;
        _pendingNavigationRestore = (key, offset);
        _navigationBringIntoView = _contentScroller.BringIntoViewOnFocusChange;
        _contentScroller.BringIntoViewOnFocusChange = false;
        var attempts = 0;
        var stable = 0;
        // Recycled rows settle after the first layout. Keep the requested offset
        // while that happens, including if another Back arrives in the meantime.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _navigationRestoreTimer = timer;
        timer.Tick += (_, _) => Apply();
        void Apply()
        {
            if (version != _navigationRestoreVersion || key != NavigationPositionKey)
            {
                timer.Stop();
                return;
            }
            var target = Math.Min(offset, _contentScroller.ScrollableHeight);
            stable = Math.Abs(_contentScroller.VerticalOffset - target) < 1 ? stable + 1 : 0;
            _contentScroller.ChangeView(null, target, null, disableAnimation: true);
            if ((stable >= 2 && (offset == 0 || _contentScroller.ScrollableHeight >= offset)) || ++attempts >= 12)
                CancelNavigationRestore();
        }
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (version != _navigationRestoreVersion || key != NavigationPositionKey) return;
            Apply();
            if (version == _navigationRestoreVersion) timer.Start();
        });
    }

    private static bool MotionEnabled()
    {
        try { return new Windows.UI.ViewManagement.UISettings().AnimationsEnabled; }
        catch { return false; }
    }

    private void SchedulePluginSearch()
    {
        _pluginSearchDelay ??= CreatePluginSearchTimer();
        _pluginSearchDelay.Stop();
        _pluginSearchDelay.Start();
    }

    private DispatcherTimer CreatePluginSearchTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_currentPageTag != "plugins") return;
            RenderVisiblePluginView();
            RestoreNavigationPosition(reset: true);
        };
        Closed += (_, _) => timer.Stop();
        return timer;
    }

    private void CancelPluginSearch() => _pluginSearchDelay?.Stop();
}

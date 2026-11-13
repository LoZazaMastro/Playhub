using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Playhub.Models;
using Playhub.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Playhub;

public sealed partial class MainWindow
{
    private event Action<IReadOnlySet<string>>? PluginPresentationChanged;
    private readonly ConditionalWeakTable<ItemsRepeater, Action<IReadOnlyList<DeckyPluginInfo>>> _pluginResultRefreshers = new();
    private readonly Dictionary<string, (StackPanel Section, ItemsRepeater Results)> _pluginSectionViews = new();
    private int _featuredRenderedCacheVersion = -1;

    private static bool PluginOwnsFocus(FrameworkElement owner)
    {
        if (owner.XamlRoot is not { } root) return false;
        for (var element = FocusManager.GetFocusedElement(root) as DependencyObject;
            element is not null; element = VisualTreeHelper.GetParent(element))
            if (ReferenceEquals(element, owner)) return true;
        return false;
    }

    private static bool RestorePluginFocus(FrameworkElement target)
    {
        if (target.IsTabStop && target.Focus(FocusState.Programmatic)) return true;
        // Stable slots are intentionally not extra tab stops. Restore focus to
        // their current card/action, including after that card's metadata changed.
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(target); index++)
            if (VisualTreeHelper.GetChild(target, index) is FrameworkElement child &&
                child.Visibility == Visibility.Visible && RestorePluginFocus(child)) return true;
        return false;
    }

    private Border LivePluginView(DeckyPluginInfo plugin, Func<UIElement> build,
        Action<Border, UIElement>? changed = null)
    {
        var slot = new Border { HorizontalAlignment = HorizontalAlignment.Stretch };
        var rendered = PluginCatalogReconciliation.Presentation(plugin);
        slot.Child = build();
        changed?.Invoke(slot, slot.Child);
        bool subscribed = false;
        var refresh = new PluginViewRefresh(
            () => slot.IsLoaded && !PluginOwnsFocus(slot),
            () => PluginCatalogReconciliation.Presentation(plugin), desired =>
        {
            // Construct before replacing. Keep the slot/section/repeater itself
            // and every unrelated card, image and focused control in place.
            var next = build();
            if (!slot.IsLoaded || PluginOwnsFocus(slot) || desired != PluginCatalogReconciliation.Presentation(plugin)) return false;
            slot.Child = next;
            LocalizeElement(next);
            changed?.Invoke(slot, next);
            return true;
        }, action => DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => action()),
            error => Diag.Step("Plugin presentation retained its previous card: " + error.GetType().Name), rendered);
        void Changed(IReadOnlySet<string> keys)
        {
            if (keys.Contains(PluginStoreKey(plugin))) refresh.Request();
        }
        slot.Loaded += (_, _) =>
        {
            if (!subscribed) { PluginPresentationChanged += Changed; subscribed = true; }
            refresh.Start();
        };
        slot.LostFocus += (_, _) => refresh.Request();
        slot.Unloaded += (_, _) =>
        {
            if (subscribed) { PluginPresentationChanged -= Changed; subscribed = false; }
            refresh.Stop();
        };
        return slot;
    }

    private bool RefreshPluginResults(ItemsRepeater repeater, IReadOnlyList<DeckyPluginInfo> plugins)
    {
        if (!_pluginResultRefreshers.TryGetValue(repeater, out var refresh)) return false;
        refresh(plugins);
        return true;
    }

    private void RegisterPluginResults(ItemsRepeater repeater, Action<IReadOnlyList<DeckyPluginInfo>> apply)
    {
        var refresh = new PluginCollectionRefresh<DeckyPluginInfo>(
            () => repeater.IsLoaded && !PluginOwnsFocus(repeater), desired =>
            {
                var preservePosition = _currentPageTag == "plugins";
                if (preservePosition) SaveNavigationPosition();
                apply(desired);
                if (preservePosition) RestoreNavigationPosition();
            },
            action => DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => action()),
            error => Diag.Step("Plugin results retained their previous rows: " + error.GetType().Name));
        _pluginResultRefreshers.Add(repeater, refresh.Set);
        repeater.Loaded += (_, _) => refresh.Start();
        repeater.LostFocus += (_, _) => refresh.Resume();
        repeater.Unloaded += (_, _) => refresh.Stop();
    }

    private static void SynchronizePluginSections(Panel panel, IReadOnlyList<UIElement> sections)
    {
        // Remove obsolete siblings first. Otherwise inserting a new heading
        // before an unchanged repeater would unnecessarily detach that repeater.
        for (var index = panel.Children.Count - 1; index >= 0; index--)
            if (!sections.Any(section => ReferenceEquals(section, panel.Children[index])))
                panel.Children.RemoveAt(index);
        for (var index = 0; index < sections.Count; index++)
        {
            var section = sections[index];
            if (index < panel.Children.Count && ReferenceEquals(panel.Children[index], section)) continue;
            if (panel.Children.Contains(section)) panel.Children.Remove(section);
            panel.Children.Insert(index, section);
        }
        while (panel.Children.Count > sections.Count) panel.Children.RemoveAt(panel.Children.Count - 1);
    }
}

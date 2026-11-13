using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Playhub.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Playhub;

public sealed partial class MainWindow
{
    private ItemsRepeater? _pluginPublicationFocusRepeater;

    private void UpdatePluginPublicationOrder()
    {
        if (_currentPageTag != "plugins" || _pluginStoreMode != "discover" || _pluginAllSort != "updated" || _pluginCardsDirty) return;
        var listLayout = _pluginAllLayout == "list";
        if ((listLayout ? _pluginAllListCache : _pluginAllCardsCache) is not ItemsRepeater { IsLoaded: true } repeater ||
            !PluginViewOwners.TryGetValue(repeater, out var owner) || !owner.Children.Contains(repeater)) return;

        var visible = repeater.ItemsSource switch
        {
            IReadOnlyList<DeckyPluginInfo> list => list.ToArray(),
            IReadOnlyList<IReadOnlyList<DeckyPluginInfo>> rows => rows.SelectMany(row => row).ToArray(),
            _ => Array.Empty<DeckyPluginInfo>()
        };
        var ordered = SortPluginAll(visible).ToArray();
        if (visible.SequenceEqual(ordered)) return;

        // Reordering underneath a focused card would discard keyboard/controller focus.
        // Apply the new order when focus leaves the results, preserving the controls above them.
        if (repeater.XamlRoot is { } root)
        {
            for (var focused = FocusManager.GetFocusedElement(root) as DependencyObject;
                focused is not null; focused = VisualTreeHelper.GetParent(focused))
            {
                if (!ReferenceEquals(focused, repeater)) continue;
                if (ReferenceEquals(_pluginPublicationFocusRepeater, repeater)) return;
                ClearPluginPublicationFocusListener();
                _pluginPublicationFocusRepeater = repeater;
                repeater.LostFocus += OnPluginPublicationFocusLost;
                repeater.Unloaded += OnPluginPublicationFocusLost;
                return;
            }
        }

        SaveNavigationPosition();
        if (RefreshPluginResults(repeater, ordered))
        {
            RestoreNavigationPosition();
            return;
        }
        var replacement = listLayout ? BuildPluginStoreListRepeater(ordered) : BuildPluginStoreGrid(ordered);
        owner.Children.Remove(repeater);
        PluginViewOwners.Remove(repeater);
        AttachPluginView(owner, replacement);
        _pluginAllListCache = listLayout ? replacement : null;
        _pluginAllCardsCache = listLayout ? null : replacement;
        LocalizeElement(replacement);
        RestoreNavigationPosition();
    }

    private void OnPluginPublicationFocusLost(object sender, RoutedEventArgs args)
    {
        if (!ReferenceEquals(sender, _pluginPublicationFocusRepeater)) return;
        ClearPluginPublicationFocusListener();
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, UpdatePluginPublicationOrder);
    }

    private void ClearPluginPublicationFocusListener()
    {
        if (_pluginPublicationFocusRepeater is not { } repeater) return;
        _pluginPublicationFocusRepeater = null;
        repeater.LostFocus -= OnPluginPublicationFocusLost;
        repeater.Unloaded -= OnPluginPublicationFocusLost;
    }

    private void QueuePluginRow(ItemsRepeater repeater, ContentPresenter presenter, int index,
        double estimatedHeight, Func<UIElement> createContent)
    {
        // WinRT event registration can pump messages. Never construct interactive
        // controls from ElementPrepared while ItemsRepeater owns the layout pass.
        var ticket = new object();
        presenter.Tag = ticket;
        presenter.MinHeight = estimatedHeight;
        presenter.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            // A collection Move keeps the presenter and its captured item. Its
            // current index may legitimately differ from ElementPrepared's index.
            if (!ReferenceEquals(presenter.Tag, ticket) || repeater.GetElementIndex(presenter) < 0) return;
            var content = createContent();
            if (!ReferenceEquals(presenter.Tag, ticket)) return;
            presenter.Content = content;
            presenter.MinHeight = 0;
        });
    }

    private static void ClearPluginRow(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        if (args.Element is not ContentPresenter presenter) return;
        presenter.Tag = null;
        presenter.Content = null;
    }
}

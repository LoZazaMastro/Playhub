using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace Playhub;

public sealed partial class MainWindow
{
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
            if (!ReferenceEquals(presenter.Tag, ticket) || repeater.GetElementIndex(presenter) != index) return;
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

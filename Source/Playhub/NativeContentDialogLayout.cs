using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Playhub.Importing;

internal static class NativeContentDialogLayout
{
    internal static ScrollViewer? FindContentViewport(ContentDialog dialog)
    {
        var pending = new Queue<DependencyObject>();
        pending.Enqueue(dialog);
        while (pending.Count > 0)
        {
            var node = pending.Dequeue();
            if (ReferenceEquals(node, dialog.Content)) continue;
            if (node is ScrollViewer { Name: "ContentScrollViewer" } viewport) return viewport;
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
                pending.Enqueue(VisualTreeHelper.GetChild(node, index));
        }
        return null;
    }

    internal static double ContentHeightBudget(Border surface, ScrollViewer viewport, FrameworkElement content, double surfaceMaxHeight)
    {
        if (!surface.IsLoaded || !viewport.IsLoaded || !content.IsLoaded || viewport.ViewportHeight <= 0 || surface.ActualHeight <= 0)
            return double.PositiveInfinity;
        var top = content.TransformToVisual(viewport).TransformPoint(new Windows.Foundation.Point()).Y + viewport.VerticalOffset;
        double bottomInset = 0;
        for (var parent = VisualTreeHelper.GetParent(content); parent is not null && !ReferenceEquals(parent, viewport); parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is FrameworkElement element) bottomInset += element.Margin.Bottom;
            if (parent is Grid grid) bottomInset += grid.Padding.Bottom + grid.BorderThickness.Bottom;
            if (parent is Border border) bottomInset += border.Padding.Bottom + border.BorderThickness.Bottom;
        }
        // Measure the title, padding and native command row as actual chrome.
        // Subtract that from the new root budget, not the previous viewport:
        // using the latter would stop the dialog growing on a later resize.
        var chrome = Math.Max(0, surface.ActualHeight - viewport.ViewportHeight) + Math.Max(0, top) + bottomInset;
        return Math.Max(0, surfaceMaxHeight - chrome);
    }

    internal static Border? ResizeSurface(ContentDialog dialog, double width, double maxHeight)
    {
        // Template resources can retain the opening size, and WinUI can replace
        // its template tree. Resolve the current surface on coalesced resize
        // events, after ApplyTemplate, rather than retaining an Opened reference.
        dialog.ApplyTemplate();
        var pending = new Queue<DependencyObject>();
        pending.Enqueue(dialog);
        while (pending.Count > 0)
        {
            var node = pending.Dequeue();
            if (ReferenceEquals(node, dialog.Content)) continue;
            if (node is Border { Name: "BackgroundElement" } background)
            {
                background.MinWidth = width;
                background.MaxWidth = width;
                background.Width = width;
                background.MinHeight = 0;
                background.MaxHeight = maxHeight;
                background.InvalidateMeasure();
                return background;
            }
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
                pending.Enqueue(VisualTreeHelper.GetChild(node, index));
        }
        return null;
    }
}

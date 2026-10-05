using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Playhub.Importing;

internal sealed class AdaptiveImportToolbar : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var sizes = MeasureItems(availableSize.Width);
        var layout = ImportToolbarLayout.Arrange(sizes, availableSize.Width);
        return new Size(layout.Width, layout.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var layout = ImportToolbarLayout.Arrange(Children.Select(child =>
            new ToolbarItemSize(child.DesiredSize.Width, child.DesiredSize.Height)).ToArray(), finalSize.Width);
        for (var i = 0; i < Children.Count; i++)
        {
            var item = layout.Items[i];
            Children[i].Arrange(new Rect(item.X, item.Y, item.Width, item.Height));
        }
        return finalSize;
    }

    private ToolbarItemSize[] MeasureItems(double width)
    {
        var sizes = new ToolbarItemSize[Children.Count];
        for (var i = 0; i < Children.Count; i++)
        {
            Children[i].Measure(new Size(width, double.PositiveInfinity));
            sizes[i] = new(Children[i].DesiredSize.Width, Children[i].DesiredSize.Height);
        }
        return sizes;
    }
}

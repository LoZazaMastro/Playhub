namespace Playhub.Importing;

internal readonly record struct ToolbarItemSize(double Width, double Height);
internal readonly record struct ToolbarItemPlacement(double X, double Y, double Width, double Height);
internal sealed record ToolbarLayoutResult(ToolbarItemPlacement[] Items, double Width, double Height);

internal static class ImportToolbarLayout
{
    internal const double Spacing = 10;

    // Only the parent's available width determines row breaks. Row height never
    // feeds back into width, and child order remains the keyboard focus order.
    internal static ToolbarLayoutResult Arrange(IReadOnlyList<ToolbarItemSize> sizes, double availableWidth)
    {
        var width = double.IsNaN(availableWidth) || availableWidth < 0 ? 0 : availableWidth;
        var items = new ToolbarItemPlacement[sizes.Count];
        double x = 0, y = 0, rowHeight = 0, usedWidth = 0;
        var rowStart = 0;
        void FinishRow(int end)
        {
            for (var i = rowStart; i < end; i++)
                items[i] = items[i] with { Y = y + (rowHeight - items[i].Height) / 2 };
        }
        for (var i = 0; i < sizes.Count; i++)
        {
            var itemWidth = Math.Min(Math.Max(0, sizes[i].Width), width);
            var itemHeight = Math.Max(0, sizes[i].Height);
            var gap = i == rowStart ? 0 : Spacing;
            if (i > rowStart && x + gap + itemWidth > width)
            {
                FinishRow(i);
                y += rowHeight + Spacing;
                x = 0;
                rowHeight = 0;
                rowStart = i;
                gap = 0;
            }
            x += gap;
            items[i] = new(x, y, itemWidth, itemHeight);
            x += itemWidth;
            rowHeight = Math.Max(rowHeight, itemHeight);
            usedWidth = Math.Max(usedWidth, x);
        }
        FinishRow(sizes.Count);
        return new(items, usedWidth, sizes.Count == 0 ? 0 : y + rowHeight);
    }
}

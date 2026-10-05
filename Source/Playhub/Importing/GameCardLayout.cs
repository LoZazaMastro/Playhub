namespace Playhub.Importing;

internal static class GameCardLayout
{
    internal const double MinimumWidth = 240;
    internal const double ColumnSpacing = 14;
    internal const int MaximumColumns = 4;

    internal static int ColumnCount(double availableWidth)
    {
        if (!double.IsFinite(availableWidth) || availableWidth <= 0) return 1;
        return (int)Math.Clamp(Math.Floor((availableWidth + ColumnSpacing) /
            (MinimumWidth + ColumnSpacing)), 1, MaximumColumns);
    }
}

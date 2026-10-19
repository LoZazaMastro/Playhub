namespace Playhub.Importing;

internal static class GameCardLayout
{
    internal const double MinimumWidth = 240;
    internal const double ColumnSpacing = 14;
    internal const int MaximumSizeStep = 2;
    // Card density preference for imported PC games.
    // Keep the original first three sizes; legacy larger preferences clamp to the last one.
    private static int _sizeStep = 2;
    internal static int SizeStep => _sizeStep;
    internal static event EventHandler? DensityChanged;
    internal static double PreferredWidth(int step) => Math.Clamp(step, 0, MaximumSizeStep) switch
    { 0 => 184, 1 => 208, _ => 232 };
    internal static int MaximumColumns(int step) => 12;
    internal static double TitleFontSize(int step) => 14 + Math.Clamp(step, 0, MaximumSizeStep);
    internal static double BodyFontSize(int step) => 12 + Math.Clamp(step, 0, MaximumSizeStep) * .5;
    // Adjacent steps can fit the same number of columns: density must also
    // change the actual width of each card instead of stretching it to fill.
    internal static double CardWidth(double availableWidth)
        => !double.IsFinite(availableWidth) || availableWidth <= 0
            ? PreferredWidth(_sizeStep) : Math.Min(PreferredWidth(_sizeStep), availableWidth);
    internal static double ContentWidth(double availableWidth)
        => CardWidth(availableWidth) * ColumnCount(availableWidth)
            + ColumnSpacing * (ColumnCount(availableWidth) - 1);

    internal static void SetSizeStep(int step)
    {
        step = Math.Clamp(step, 0, MaximumSizeStep);
        if (_sizeStep == step) return;
        _sizeStep = step;
        DensityChanged?.Invoke(null, EventArgs.Empty);
    }

    internal static int ColumnCount(double availableWidth)
    {
        if (!double.IsFinite(availableWidth) || availableWidth <= 0) return 1;
        return (int)Math.Clamp(Math.Floor((availableWidth + ColumnSpacing) /
            (PreferredWidth(_sizeStep) + ColumnSpacing)), 1, MaximumColumns(_sizeStep));
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Playhub;

internal static class GameCardActionPresentation
{
    internal static Grid CreateGrid()
    {
        var grid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        foreach (var weight in new[] { 1d, 1d })
            grid.ColumnDefinitions.Add(new() { Width = new GridLength(weight, GridUnitType.Star) });
        grid.SizeChanged += (_, args) =>
        {
            // Translations remain readable at the compact library sizes.
            bool stacked = args.NewSize.Width < 232;
            var buttons = grid.Children.OfType<Button>().ToArray();
            int rows = stacked ? buttons.Length : Math.Max(1, buttons.Length - 1);
            grid.RowDefinitions.Clear();
            for (int row = 0; row < rows; row++) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            for (int index = 0; index < buttons.Length; index++)
            {
                Grid.SetRow(buttons[index], stacked ? index : index < 2 ? 0 : index - 1);
                Grid.SetColumn(buttons[index], stacked || index >= 2 ? 0 : index);
                Grid.SetColumnSpan(buttons[index], stacked || index >= 2 ? 2 : 1);
            }
        };
        return grid;
    }

    internal static void Configure(Button button)
    {
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.VerticalAlignment = VerticalAlignment.Stretch;
        button.MinWidth = 0;
        button.FontSize = 14;
        button.Padding = new Thickness(8, 6, 8, 6);
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        if (button.Content is string value) button.Content = new TextBlock { Text = value };
        if (button.Content is TextBlock label)
        {
            label.TextWrapping = TextWrapping.Wrap;
            label.TextTrimming = TextTrimming.None;
            label.TextAlignment = TextAlignment.Center;
            label.MaxLines = 0;
        }
    }
}

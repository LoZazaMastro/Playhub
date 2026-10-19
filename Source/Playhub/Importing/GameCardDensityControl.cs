using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Playhub.Importing;

/// <summary>A compact, accessible three-step density control. No polling or network work.</summary>
internal static class GameCardDensityControl
{
    internal static FrameworkElement Create(Func<string, string> text)
    {
        var row = new Grid { ColumnSpacing = 10, MinWidth = 0, Width = 188, MaxWidth = 188,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new FontIcon { Glyph = "\uE80A", FontSize = 16, Opacity = .65,
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false });
        var slider = new Slider { Minimum = 0, Maximum = GameCardLayout.MaximumSizeStep, StepFrequency = 1,
            TickFrequency = 1, TickPlacement = TickPlacement.BottomRight,
            SnapsTo = SliderSnapsTo.StepValues, Value = GameCardLayout.SizeStep,
            MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(slider, text("Library.CoverSize"));
        ToolTipService.SetToolTip(slider, text("Library.CoverSizeHelp"));
        Grid.SetColumn(slider, 1); row.Children.Add(slider);
        bool reflecting = false, subscribed = false;
        slider.ValueChanged += (_, _) => { if (!reflecting) GameCardLayout.SetSizeStep((int)Math.Round(slider.Value)); };
        void Reflect(object? _, EventArgs __)
        { reflecting = true; try { slider.Value = GameCardLayout.SizeStep; } finally { reflecting = false; } }
        row.Loaded += (_, _) => { Reflect(null, EventArgs.Empty); if (!subscribed) { GameCardLayout.DensityChanged += Reflect; subscribed = true; } };
        row.Unloaded += (_, _) => { if (subscribed) { GameCardLayout.DensityChanged -= Reflect; subscribed = false; } };
        return row;
    }
}

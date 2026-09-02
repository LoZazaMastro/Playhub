using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Playhub.Models;
using System;

namespace Playhub;

public sealed partial class MainWindow
{
    private UIElement BuildExternalPluginWarning(DeckyPluginInfo plugin)
    {
        var accent = ParseColor(_settings.AccentColor);
        var origin = string.Equals(plugin.CatalogSource, "decky-store", StringComparison.OrdinalIgnoreCase)
            ? "Decky Store" : T("progetti indipendenti su GitHub");
        var content = new Grid { ColumnSpacing = 14 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition());
        content.Children.Add(new FontIcon
        {
            Glyph = ((char)0xE7BA).ToString(), FontSize = 24,
            Foreground = new SolidColorBrush(accent), VerticalAlignment = VerticalAlignment.Top
        });
        var message = new TextBlock
        {
            Text = string.Format(T("Il funzionamento su Windows dei plugin provenienti da {0} non è garantito. Per qualsiasi tipo di assistenza, contatta direttamente lo sviluppatore."), origin),
            TextWrapping = TextWrapping.Wrap, FontSize = 14, Tag = "noloc"
        };
        Grid.SetColumn(message, 1);
        content.Children.Add(message);
        return new Border
        {
            Tag = "external-plugin-warning", Padding = new Thickness(20), CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 12, 0, 0), BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(WithAlpha(accent, 28)),
            BorderBrush = new SolidColorBrush(WithAlpha(accent, 150)), Child = content
        };
    }
}

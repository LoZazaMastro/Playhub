using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Playhub;

public sealed partial class MainWindow
{
    private Border BuildStoreFeaturedCard(ImageSource? image, string title, string description,
        FrameworkElement actions, Func<FrameworkElement, FrameworkElement> countdown,
        Action<FrameworkElement> open, FrameworkElement? leadingStatus = null,
        FrameworkElement? trailingStatus = null, bool trimDescription = true)
    {
        var stage = new Grid();
        stage.Background = image is not null
            ? new ImageBrush { ImageSource = image, Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center }
            : new SolidColorBrush(WithAlpha(ParseColor(_settings.AccentColor), 88));
        stage.Children.Add(new Border { Background = Scrim(.92, .18),
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch });
        var copy = new StackPanel { Spacing = 8, Margin = new Thickness(72, 32, 72, 58), MaxWidth = 760,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
        copy.Children.Add(new TextBlock { Text = title, FontSize = 34, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), TextWrapping = TextWrapping.Wrap });
        copy.Children.Add(new TextBlock { Text = description, FontSize = 16,
            Foreground = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)), TextWrapping = TextWrapping.Wrap,
            MaxLines = trimDescription ? 2 : 0, TextTrimming = trimDescription ? TextTrimming.CharacterEllipsis : TextTrimming.None, Tag = "noloc" });
        actions.Margin = new Thickness(0, 12, 0, 0); copy.Children.Add(actions); stage.Children.Add(copy);
        if (leadingStatus is not null)
        {
            leadingStatus.Margin = new Thickness(18); leadingStatus.HorizontalAlignment = HorizontalAlignment.Left;
            leadingStatus.VerticalAlignment = VerticalAlignment.Top; stage.Children.Add(leadingStatus);
        }
        if (trailingStatus is not null)
        {
            trailingStatus.Margin = new Thickness(18); trailingStatus.HorizontalAlignment = HorizontalAlignment.Right;
            trailingStatus.VerticalAlignment = VerticalAlignment.Top; stage.Children.Add(trailingStatus);
        }
        var card = new Border { CornerRadius = new CornerRadius(8),
            Background = ResourceBrush("CardBackgroundFillColorDefaultBrush", Color.FromArgb(255, 32, 32, 36)),
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Child = stage };
        stage.Children.Add(countdown(card));
        card.Tapped += (_, args) =>
        {
            if (ComesFromButton(args.OriginalSource)) return;
            args.Handled = true; open(card);
        };
        return card;
    }

    private static Button BuildStoreFeaturedArrow(string glyph, HorizontalAlignment alignment, Action advance)
    {
        var button = new Button { Width = 40, Height = 40, Padding = new Thickness(0), Margin = new Thickness(0),
            CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Color.FromArgb(164, 18, 18, 22)), Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            HorizontalAlignment = alignment, VerticalAlignment = VerticalAlignment.Center,
            Content = new FontIcon { Glyph = glyph, FontSize = 18 } };
        button.Click += (_, _) => advance();
        return button;
    }
}

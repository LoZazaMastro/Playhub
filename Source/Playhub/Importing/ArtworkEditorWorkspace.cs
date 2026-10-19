using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Playhub.Importing;

/// <summary>Responsive artwork canvas and inspector for imported PC games.</summary>
internal sealed class ArtworkEditorWorkspace
{
    public Grid Element { get; } = new() { ColumnSpacing = 20, RowSpacing = 18 };
    public StackPanel Inspector { get; } = new() { Spacing = 12, MinWidth = 0 };
    private readonly FrameworkElement _canvas;
    private readonly bool _sourcesBeforeInspectorWhenNarrow;
    private FrameworkElement? _sources;
    private bool? _wide;

    public ArtworkEditorWorkspace(FrameworkElement canvas, bool sourcesBeforeInspectorWhenNarrow = false)
    {
        _canvas = canvas;
        _sourcesBeforeInspectorWhenNarrow = sourcesBeforeInspectorWhenNarrow;
        Element.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star), MinWidth = 0 });
        Element.ColumnDefinitions.Add(new() { Width = new GridLength(0), MinWidth = 0 });
        Element.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Element.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Element.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _canvas.VerticalAlignment = VerticalAlignment.Top;
        Element.Children.Add(_canvas); Element.Children.Add(Inspector);
        Arrange(false);
        Element.SizeChanged += (_, args) => Arrange(args.NewSize.Width >= 820);
    }

    public void SetSources(FrameworkElement sources)
    {
        if (_sources is not null) Element.Children.Remove(_sources);
        _sources = sources;
        sources.VerticalAlignment = VerticalAlignment.Top;
        // Keep the task order in the native tree as well as the visual layout.
        // Resizing changes grid positions, never the controls or their focus state.
        if (_sourcesBeforeInspectorWhenNarrow) Element.Children.Insert(1, sources);
        else Element.Children.Add(sources);
        _wide = null;
        Arrange(Element.ActualWidth >= 820);
    }

    private void Arrange(bool wide)
    {
        if (_wide == wide) return;
        _wide = wide;
        Element.ColumnDefinitions[1].Width = new GridLength(wide ? 304 : 0);
        bool sourcesFirst = !wide && _sourcesBeforeInspectorWhenNarrow && _sources is not null;
        Grid.SetColumn(Inspector, wide ? 1 : 0); Grid.SetRow(Inspector, wide ? 0 : sourcesFirst ? 2 : 1);
        Grid.SetRowSpan(Inspector, wide ? 2 : 1);
        if (_sources is not null) { Grid.SetColumn(_sources, 0); Grid.SetRow(_sources, wide || sourcesFirst ? 1 : 2); }
    }

    public static Border Section(string title, string glyph, params UIElement[] children)
    {
        var body = new StackPanel { Spacing = 12, MinWidth = 0 };
        var heading = new Grid { ColumnSpacing = 10 };
        heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        heading.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star), MinWidth = 0 });
        heading.Children.Add(new FontIcon { Glyph = glyph, FontSize = 16, Opacity = .75 });
        var label = new TextBlock { Text = title, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(label, 1); heading.Children.Add(label); body.Children.Add(heading);
        foreach (var child in children) body.Children.Add(child);
        return new Border { Child = body, Padding = new Thickness(16), CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 43, 43, 47)) };
    }
}

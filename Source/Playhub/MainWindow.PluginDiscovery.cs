using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Playhub.Models;
using Playhub.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Playhub;

public sealed partial class MainWindow
{
    private const int PluginDiscoveryPreviewCount = 4;
    private string? _pluginCategoryFilter;
    private string PluginLayoutKey => _pluginStoreMode == "manage" ? "manage" :
        _pluginCategoryFilter is { } category ? "category:" + category : "all";
    private string _pluginAllLayout
    {
        get => _settings.PluginStoreLayouts?.TryGetValue(PluginLayoutKey, out var layout) == true && layout == "cards"
            ? "cards" : "list";
        set => (_settings.PluginStoreLayouts ??= new())[PluginLayoutKey] = value == "cards" ? "cards" : "list";
    }

    private async Task PersistPluginLayoutAsync()
    {
#if !PLAYHUB_UI_REVIEW
        try { await SaveSettingsSilentlyAsync(); }
        catch (Exception ex) { Diag.Crash(nameof(PersistPluginLayoutAsync), ex); }
#else
        await Task.CompletedTask;
#endif
    }

    private void ResetPluginSourceFilter()
    {
        if (_pluginAllSource == "all") return;
        _pluginAllSource = "all";
        InvalidatePluginAllViews();
    }

    // Window-lifetime state survives store rebuilds, but is not persisted between launches.
    private readonly Dictionary<string, PluginDiscoveryCategoryState> _pluginDiscoveryCategories =
        new(StringComparer.OrdinalIgnoreCase);

    private sealed class PluginDiscoveryCategoryState
    {
        public Dictionary<string, int> Order { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private UIElement BuildPluginDiscoveryCategory(string title, IReadOnlyList<DeckyPluginInfo> plugins)
    {
        if (!_pluginDiscoveryCategories.TryGetValue(title, out var state))
        {
            state = new PluginDiscoveryCategoryState();
            _pluginDiscoveryCategories.Add(title, state);
        }

        var orderedPlugins = OrderPluginDiscoveryCategory(state, plugins);
        if (string.Equals(title, "I plugin di Playhub", StringComparison.OrdinalIgnoreCase))
        {
            var featuredKeys = GetFeaturedPlugins().Select(PluginStoreKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            orderedPlugins = orderedPlugins.Where(plugin => !featuredKeys.Contains(PluginStoreKey(plugin))).ToList();
        }
        return BuildPluginStoreCategory(title,
            orderedPlugins.Take(PluginDiscoveryPreviewCount).ToList(), clickableHeading: true);
    }

    private static string PluginDiscoveryCategory(DeckyPluginInfo plugin) => plugin.IsPlayhubPlugin
        ? plugin.Name switch
        {
            "Quick Settings" or "Shortcuts" or "Launch Curtain" or "Playhub Notifications" or "Playhub Surround" or "Weather" => "Strumenti e utilità",
            "Playhub Artworks" or "Now Playing" or "ThemeDeck" or "TrailerHero" => "Personalizzazione e media",
            "Playhub Metadata" => "Libreria e giochi",
            "News" => "Social e community",
            "Proton VPN" => "Sistema e hardware",
            _ => NormalizePluginStoreCategory(plugin.Category)
        }
        : NormalizePluginStoreCategory(plugin.Category);

    private static bool PluginBelongsToCategory(DeckyPluginInfo plugin, string category) =>
        string.Equals(category, "I plugin di Playhub", StringComparison.OrdinalIgnoreCase)
            ? plugin.IsPlayhubPlugin
            : string.Equals(PluginDiscoveryCategory(plugin), category, StringComparison.OrdinalIgnoreCase);

    private void OpenPluginCategory(string category)
    {
        PushPluginStoreHistory();
        CancelPluginSearch();
        _pluginCategoryFilter = category;
        _pluginShowAll = true;
        _pluginAllSource = "all";
        _suppressPluginSearchRender = true;
        try { _pluginSearchBox.Text = string.Empty; }
        finally { _suppressPluginSearchRender = false; }
        InvalidatePluginAllViews();
        _pluginFeaturedHost.Visibility = Visibility.Collapsed;
        RenderPluginCards();
        UpdatePluginBackButton();
        UpdateFeaturedAutoAdvanceState();
        RestoreNavigationPosition(reset: true);
    }

    private void ClosePluginCategory()
    {
        if (_currentPageTag != "plugins" || _pluginStoreMode != "discover" || _pluginCategoryFilter is null) return;
        NavigatePluginStoreBack();
    }

    private Button BuildPluginCategoryHeading(string title)
    {
        var foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
        var label = new TextBlock
        {
            Text = T(title), FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = foreground, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var arrow = new FontIcon
        {
            Glyph = ((char)0xE76C).ToString(), FontSize = 16, Width = 16, Height = 16,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 0),
            Foreground = foreground, Opacity = 0, IsHitTestVisible = false,
            RenderTransform = new TranslateTransform { X = -4 }
        };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        content.Children.Add(label);
        content.Children.Add(arrow);
        var button = new Button
        {
            Content = content, Padding = new Thickness(0, 4, 0, 4), BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left, HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            UseSystemFocusVisuals = true
        };
        foreach (var key in new[] { "ButtonBackground", "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "ButtonBorderBrush", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed" })
            SetLocalBrush(button, key, Microsoft.UI.Colors.Transparent);
        AutomationProperties.SetName(button, T(title));
        var pointerOver = false;
        Storyboard? transition = null;
        void UpdateHighlight()
        {
            var highlighted = pointerOver || button.FocusState == FocusState.Keyboard;
            var color = highlighted ? ParseColor(_settings.AccentColor) : Microsoft.UI.Colors.White;
            var from = foreground.Color;
            var opacity = arrow.Opacity;
            var transform = (TranslateTransform)arrow.RenderTransform;
            var fromX = transform.X;
            transition?.Stop();
            if (!MotionEnabled())
            {
                foreground.Color = color;
                arrow.Opacity = highlighted ? 1 : 0;
                transform.X = highlighted ? 0 : -4;
                return;
            }
            transition = new Storyboard();
            var duration = new Duration(TimeSpan.FromMilliseconds(170));
            var tint = new ColorAnimation { From = from, To = color, Duration = duration };
            Storyboard.SetTarget(tint, foreground);
            Storyboard.SetTargetProperty(tint, "Color");
            transition.Children.Add(tint);
            var fade = new DoubleAnimation { From = opacity, To = highlighted ? 1 : 0, Duration = duration };
            Storyboard.SetTarget(fade, arrow);
            Storyboard.SetTargetProperty(fade, "Opacity");
            transition.Children.Add(fade);
            var movement = new DoubleAnimation { From = fromX, To = highlighted ? 0 : -4, Duration = duration };
            Storyboard.SetTarget(movement, transform);
            Storyboard.SetTargetProperty(movement, "X");
            transition.Children.Add(movement);
            transition.Begin();
        }
        button.PointerEntered += (_, _) => { pointerOver = true; UpdateHighlight(); };
        button.PointerExited += (_, _) => { pointerOver = false; UpdateHighlight(); };
        button.GotFocus += (_, _) => UpdateHighlight();
        button.LostFocus += (_, _) => UpdateHighlight();
        button.Unloaded += (_, _) => transition?.Stop();
        button.Click += (_, _) => OpenPluginCategory(title);
        return button;
    }

    private static List<DeckyPluginInfo> OrderPluginDiscoveryCategory(
        PluginDiscoveryCategoryState state,
        IReadOnlyList<DeckyPluginInfo> plugins)
    {
        var newKeys = plugins
            .Select(PluginStoreKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(key => !state.Order.ContainsKey(key))
            .ToList();

        for (var index = newKeys.Count - 1; index > 0; index--)
        {
            var swapIndex = Random.Shared.Next(index + 1);
            (newKeys[index], newKeys[swapIndex]) = (newKeys[swapIndex], newKeys[index]);
        }

        // Append new identities without moving known ones. Keep absent keys so a
        // temporarily missing plugin regains its position when the catalog recovers.
        foreach (var key in newKeys)
        {
            state.Order.Add(key, state.Order.Count);
        }

        // Resolve the order against current objects, retaining refreshed install/update data.
        return plugins.OrderBy(plugin => state.Order[PluginStoreKey(plugin)]).ToList();
    }
}

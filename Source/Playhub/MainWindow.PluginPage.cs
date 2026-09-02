using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media.Animation;
using Playhub.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;

namespace Playhub;

public sealed partial class MainWindow
{
    private readonly StackPanel _pluginPageHost = new()
    {
        Tag = "plugin-detail", Spacing = 20, Margin = new Thickness(36, 24, 36, 36),
        HorizontalAlignment = HorizontalAlignment.Stretch, Visibility = Visibility.Collapsed
    };
    private readonly StackPanel _pluginPageContent = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private Grid _pluginStoreToolbar = new();
    private StackPanel _pluginStoreHomeHost = new();
    private FrameworkElement _pluginStoreSwitcher = new Grid();
    private Button _pluginBackButton = new();
    private readonly TranslateTransform _pluginBackSwitcherOffset = new();
    private Storyboard? _pluginBackAnimation;
    private bool _pluginBackVisible;
    private string? _pluginPagePluginKey;
    private readonly Stack<PluginStoreNavigationState> _pluginStoreHistory = new();

    private sealed record PluginStoreNavigationState(
        string Mode, bool ShowAll, string? Category,
        string Sort, string Query, string ManageQuery, double ScrollOffset,
        WeakReference<FrameworkElement>? FocusTarget);

    private Button BuildPluginBackButton()
    {
        _pluginBackButton = new Button
        {
            Width = 32, Height = 32, MinWidth = 32, Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 12, 0), CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(0), Background = new SolidColorBrush(Colors.Transparent),
            VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed,
            Content = new FontIcon { Glyph = ((char)0xE72B).ToString(), FontSize = 14 }
        };
        SetLocalizedToolTip(_pluginBackButton, "Indietro");
        AutomationProperties.SetName(_pluginBackButton, T("Indietro"));
        _pluginBackButton.Click += (_, _) => NavigatePluginStoreBack();
        return _pluginBackButton;
    }

    private void AttachPluginStoreToolbar()
    {
        _pluginStoreToolbar.Visibility = _currentPageTag is "plugins" or "plugin-detail"
            ? Visibility.Visible : Visibility.Collapsed;
        _pluginDiscoverTools.Visibility = _currentPageTag == "plugins"
            ? Visibility.Visible : Visibility.Collapsed;
        _pluginShowAllButton.Visibility = _currentPageTag == "plugins" && _pluginStoreMode == "discover"
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdatePluginBackButton()
    {
        AttachPluginStoreToolbar();
        var visible = (_currentPageTag == "plugins" || _currentPageTag == "plugin-detail") && _pluginStoreHistory.Count > 0;
        if (_pluginBackVisible == visible)
        {
            if (_pluginStoreToolbar.Visibility != Visibility.Visible) FinishPluginBackAnimation();
            return;
        }
        var reserved = _pluginBackButton.Visibility == Visibility.Visible;
        var space = _pluginBackButton.Width + _pluginBackButton.Margin.Right;
        var offset = _pluginBackSwitcherOffset.X - (reserved ? 0 : space);
        var opacity = reserved ? _pluginBackButton.Opacity : 0;
        _pluginBackAnimation?.Stop();
        _pluginBackAnimation = null;
        _pluginBackVisible = visible;
        if (!MotionEnabled() || !_pluginStoreToolbar.IsLoaded || _pluginStoreToolbar.Visibility != Visibility.Visible)
        {
            FinishPluginBackAnimation();
            return;
        }
        _pluginBackButton.Visibility = Visibility.Visible;
        _pluginBackButton.IsHitTestVisible = visible;
        _pluginBackButton.IsTabStop = visible;
        var animation = new Storyboard();
        AddPluginSearchAnimation(animation, _pluginBackSwitcherOffset, "X", offset, visible ? 0 : -space);
        AddPluginSearchAnimation(animation, _pluginBackButton, "Opacity", opacity, visible ? 1 : 0);
        animation.Completed += (_, _) => { if (ReferenceEquals(_pluginBackAnimation, animation)) FinishPluginBackAnimation(); };
        _pluginBackAnimation = animation;
        animation.Begin();
    }

    private void FinishPluginBackAnimation()
    {
        _pluginBackAnimation?.Stop();
        _pluginBackAnimation = null;
        _pluginBackButton.Visibility = _pluginBackVisible ? Visibility.Visible : Visibility.Collapsed;
        _pluginBackButton.Opacity = 1;
        _pluginBackButton.IsHitTestVisible = _pluginBackVisible;
        _pluginBackButton.IsTabStop = _pluginBackVisible;
        _pluginBackSwitcherOffset.X = 0;
    }

    private void PushPluginStoreHistory(FrameworkElement? focusTarget = null)
    {
        if (_currentPageTag != "plugins") return;
        SaveNavigationPosition();
        _pluginStoreHistory.Push(new PluginStoreNavigationState(
            _pluginStoreMode, _pluginShowAll, _pluginCategoryFilter,
            _pluginAllSort, _pluginSearchBox.Text ?? string.Empty, _pluginManageQuery,
            CurrentNavigationOffset(),
            focusTarget is null ? null : new WeakReference<FrameworkElement>(focusTarget)));
    }

    private void NavigatePluginStoreBack()
    {
        if ((_currentPageTag != "plugins" && _currentPageTag != "plugin-detail") || _pluginStoreHistory.Count == 0) return;
        var previous = _pluginStoreHistory.Pop();
        RestorePluginStoreView(previous);
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (_currentPageTag != "plugins" || _pluginStoreMode != previous.Mode) return;
            var bringIntoView = _contentScroller.BringIntoViewOnFocusChange;
            _contentScroller.BringIntoViewOnFocusChange = false;
            try
            {
                if (previous.FocusTarget is not null && previous.FocusTarget.TryGetTarget(out var target) && target.IsLoaded)
                    target.Focus(FocusState.Programmatic);
                else (previous.Mode == "manage" ? _pluginManageButton : _pluginDiscoverButton)
                    .Focus(FocusState.Programmatic);
            }
            finally { _contentScroller.BringIntoViewOnFocusChange = bringIntoView; }
        });
    }

    private void RestorePluginStoreView(PluginStoreNavigationState state)
    {
        CancelPluginSearch();
        var changed = _pluginShowAll != state.ShowAll || _pluginCategoryFilter != state.Category ||
            _pluginAllSource != "all" || _pluginAllSort != state.Sort ||
            _pluginSearchBox.Text != state.Query || _pluginManageQuery != state.ManageQuery;
        _pluginStoreMode = state.Mode;
        _pluginShowAll = state.ShowAll;
        _pluginCategoryFilter = state.Category;
        _pluginAllSource = "all";
        _pluginAllSort = state.Sort;
        _pluginManageQuery = state.ManageQuery;
        _suppressPluginSearchRender = true;
        try { _pluginSearchBox.Text = state.Query; }
        finally { _suppressPluginSearchRender = false; }
        if (changed) InvalidatePluginAllViews();
        _pluginDiscoverView.Visibility = state.Mode == "discover" ? Visibility.Visible : Visibility.Collapsed;
        _pluginManageView.Visibility = state.Mode == "manage" ? Visibility.Visible : Visibility.Collapsed;
        _pluginFeaturedHost.Visibility = state.Mode == "discover" && !state.ShowAll &&
            state.Category is null && string.IsNullOrWhiteSpace(state.Query)
            ? Visibility.Visible : Visibility.Collapsed;
        UpdatePluginStoreModeButtons();
        ShowPage("plugins");
        _navigationOffsets[NavigationPositionKey] = state.ScrollOffset;
        RestoreNavigationPosition();
    }

    private void OpenPluginPage(DeckyPluginInfo plugin, FrameworkElement source)
    {
        CancelPluginSearch();
        PushPluginStoreHistory(source);
        if (_pluginPageHost.Parent is null)
        {
            _pluginPageHost.Children.Add(_pluginPageContent);
            _pageHost.Children.Add(_pluginPageHost);
        }
        _pluginPagePluginKey = PluginStoreKey(plugin);
        _pluginPageContent.Children.Clear();
        _pluginPageContent.Children.Add(PluginBannerCard(plugin, initiallyExpanded: true, pageMode: true));
        ShowPage("plugin-detail");
        RestoreNavigationPosition(reset: true);
    }

    private void ClosePluginPage()
    {
        if (_currentPageTag == "plugin-detail") NavigatePluginStoreBack();
    }

    private void RefreshOpenPluginPage()
    {
        if (_currentPageTag != "plugin-detail" || _pluginPagePluginKey is null) return;
        var plugin = _plugins.FirstOrDefault(item => PluginStoreKey(item) == _pluginPagePluginKey);
        if (plugin is null) return;
        var offset = CurrentNavigationOffset();
        _pluginPageContent.Children.Clear();
        _pluginPageContent.Children.Add(PluginBannerCard(plugin, initiallyExpanded: true, pageMode: true));
        _navigationOffsets[NavigationPositionKey] = offset;
        RestoreNavigationPosition();
    }

    // Page layout: cover, title/actions, screenshots, description, release notes.
    private void ConfigurePluginDetailHero(
        Grid imagePanel, StackPanel pageContent, StackPanel titleActions,
        StackPanel details, FrameworkElement statusBadge, FrameworkElement catalogBadge)
    {
        var artwork = imagePanel.Children.OfType<Image>().FirstOrDefault();
        var artworkScrim = imagePanel.Children.OfType<Border>()
            .FirstOrDefault(child => Equals(child.Tag, "plugin-artwork-scrim"));
        imagePanel.Children.Clear();
        pageContent.Children.Clear();
        foreach (var badge in new[] { statusBadge, catalogBadge })
        {
            if (badge.Parent is Panel parent) parent.Children.Remove(badge);
            badge.Margin = new Thickness(0);
            badge.HorizontalAlignment = HorizontalAlignment.Right;
            badge.VerticalAlignment = VerticalAlignment.Top;
        }

        var badges = new Grid
        {
            Tag = "plugin-detail-badges",
            ColumnSpacing = 8, Margin = new Thickness(16),
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top
        };
        badges.ColumnDefinitions.Add(new ColumnDefinition());
        badges.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        statusBadge.Tag = "plugin-detail-status-badge";
        statusBadge.HorizontalAlignment = HorizontalAlignment.Left;
        catalogBadge.Tag = "plugin-detail-source-badge";
        Grid.SetColumn(catalogBadge, 1);
        badges.Children.Add(statusBadge);
        badges.Children.Add(catalogBadge);

        var imageCanvas = new Canvas { IsHitTestVisible = false };
        var imageClip = new RectangleGeometry();
        imagePanel.Clip = imageClip;
        imagePanel.Children.Add(imageCanvas);
        if (artworkScrim is not null) imagePanel.Children.Add(artworkScrim);
        imagePanel.MinHeight = 0;
        imagePanel.Height = 0;
        imagePanel.VerticalAlignment = VerticalAlignment.Center;
        if (artwork is not null)
        {
            artwork.Stretch = Stretch.Uniform;
            artwork.Margin = new Thickness(0);
            imageCanvas.Children.Add(artwork);
        }

        // Badges are outside the clipped image so even a near-zero image budget
        // leaves them visible. All plugin sources share this same page layout.
        var hero = new Grid();
        hero.Children.Add(imagePanel);
        hero.Children.Add(badges);
        titleActions.Margin = new Thickness(24, 20, 24, 20);
        pageContent.Children.Add(hero);
        pageContent.Children.Add(titleActions);
        pageContent.Children.Add(details);

        XamlRoot? observedRoot = null;
        var scroller = _contentScroller;
        var updateQueued = false;

        void QueueUpdate()
        {
            if (!hero.IsLoaded || updateQueued) return;
            updateQueued = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                updateQueued = false;
                if (hero.IsLoaded) UpdateHeroLayout();
            });
        }

        void UpdateHeroLayout()
        {
            var root = hero.XamlRoot;
            var width = hero.ActualWidth;
            if (root is null || width <= 0 || scroller.Content is not UIElement content) return;

            var viewportHeight = scroller.ViewportHeight > 0 ? scroller.ViewportHeight : scroller.ActualHeight;
            var scrollerTop = scroller.TransformToVisual(root.Content).TransformPoint(new Point()).Y;
            viewportHeight = Math.Min(viewportHeight, Math.Max(0, root.Size.Height - scrollerTop));
            // Content-relative coordinates do not change when the user scrolls.
            var heroTop = hero.TransformToVisual(content).TransformPoint(new Point()).Y;
            titleActions.Measure(new Size(width, double.PositiveInfinity));
            details.Measure(new Size(width, double.PositiveInfinity));

            var visibleDetails = details.Children.OfType<FrameworkElement>()
                .Where(child => child.Visibility == Visibility.Visible).ToList();
            var firstDescription = visibleDetails.FirstOrDefault(child => child.Name == "PluginDescription");
            var previewHeight = firstDescription is null ? 0 : Math.Min(72, firstDescription.DesiredSize.Height) +
                visibleDetails.TakeWhile(child => child != firstDescription)
                    .Sum(child => child.DesiredSize.Height + details.Spacing);
            var reservedHeight = titleActions.DesiredSize.Height + details.Margin.Top +
                details.Padding.Top + previewHeight + 12;

            statusBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            catalogBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            statusBadge.MaxWidth = Math.Max(0, width - badges.Margin.Left - badges.Margin.Right -
                catalogBadge.DesiredSize.Width - badges.ColumnSpacing);

            var aspect = 9.0 / 16.0;
            if (artwork?.Source is BitmapSource bitmap && bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0)
                aspect = (double)bitmap.PixelHeight / bitmap.PixelWidth;
            var naturalHeight = width * aspect;
            var availableHeight = Math.Max(0, viewportHeight - heroTop - reservedHeight);
            var height = Math.Min(naturalHeight, Math.Min(360, availableHeight));
            imagePanel.MaxHeight = height;
            imagePanel.Height = height;
            imageClip.Rect = new Rect(0, 0, width, height);
            if (artwork is not null)
            {
                // Fit the source to the existing width, then crop equal amounts
                // above and below. A shorter viewport never rescales its width.
                artwork.Width = width;
                artwork.Height = naturalHeight;
                Canvas.SetLeft(artwork, 0);
                Canvas.SetTop(artwork, (height - naturalHeight) / 2);
            }
        }

        void OnSizeChanged(object sender, SizeChangedEventArgs args) => QueueUpdate();
        void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => QueueUpdate();
        hero.SizeChanged += OnSizeChanged;
        titleActions.SizeChanged += OnSizeChanged;
        details.SizeChanged += OnSizeChanged;
        statusBadge.SizeChanged += OnSizeChanged;
        catalogBadge.SizeChanged += OnSizeChanged;
        if (artwork is not null) artwork.ImageOpened += (_, _) => QueueUpdate();
        hero.Loaded += (_, _) =>
        {
            if (observedRoot is not null) observedRoot.Changed -= OnRootChanged;
            observedRoot = hero.XamlRoot;
            if (observedRoot is not null) observedRoot.Changed += OnRootChanged;
            scroller.SizeChanged -= OnSizeChanged;
            scroller.SizeChanged += OnSizeChanged;
            QueueUpdate();
        };
        hero.Unloaded += (_, _) =>
        {
            if (observedRoot is not null) observedRoot.Changed -= OnRootChanged;
            observedRoot = null;
            scroller.SizeChanged -= OnSizeChanged;
        };
    }
}

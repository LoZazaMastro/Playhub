using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Collections.Generic;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.System;
using Windows.UI.ViewManagement;

namespace Playhub;

public sealed partial class MainWindow
{
    private const double PluginSearchClosedWidth = 40;
    private const double PluginSearchMaximumWidth = 360;
    private bool _pluginSearchExpanded;
    private double _pluginSearchExpandedWidth = PluginSearchMaximumWidth;
    private Grid? _pluginSearchHost;
    private Grid? _pluginSearchSurface;
    private Border? _pluginSearchShell;
    private Grid? _pluginSearchInputViewport;
    private Button? _pluginSearchToggle;
    private Button? _pluginSearchLeadingButton;
    private ScaleTransform? _pluginSearchShellScale;
    private TranslateTransform? _pluginSearchSurfaceOffset;
    private TranslateTransform? _pluginSearchRevealOffset;
    private TranslateTransform? _pluginSearchTextOffset;
    private TranslateTransform? _pluginSearchLeadingOffset;
    private Storyboard? _pluginSearchMorph;
    private Rect _pluginSearchViewport;
    private Action? _pluginSearchDetach;

    private Grid BuildCollapsiblePluginSearch(Button showAllButton)
    {
        _pluginSearchDetach?.Invoke();
        _pluginSearchMorph?.Stop();
        _pluginSearchMorph = null;
        _pluginSearchViewport = default;
        _pluginSearchExpanded = !string.IsNullOrWhiteSpace(_pluginSearchBox.Text);
        var textBox = _pluginSearchBox;
        var dispatcher = DispatcherQueue;
        var host = new Grid
        {
            Width = PluginSearchClosedWidth, MinWidth = PluginSearchClosedWidth,
            MaxWidth = PluginSearchMaximumWidth, Height = 40,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        _pluginSearchHost = host;
        _pluginSearchSurfaceOffset = new TranslateTransform();
        _pluginSearchShellScale = new ScaleTransform();
        _pluginSearchRevealOffset = new TranslateTransform();
        _pluginSearchTextOffset = new TranslateTransform();
        var surface = new Grid
        {
            Height = 40, HorizontalAlignment = HorizontalAlignment.Left,
            RenderTransform = _pluginSearchSurfaceOffset
        };
        _pluginSearchSurface = surface;
        var shell = new Border
        {
            Background = textBox.Background, BorderBrush = textBox.BorderBrush,
            BorderThickness = textBox.BorderThickness, CornerRadius = textBox.CornerRadius,
            RenderTransform = _pluginSearchShellScale, IsHitTestVisible = false
        };
        _pluginSearchShell = shell;
        AutomationProperties.SetAccessibilityView(shell, AccessibilityView.Raw);
        surface.Children.Add(shell);

        // Move the old leading-icon inset to a separate trailing button. Keep the
        // configured vertical padding and the native TextBox clear button intact.
        textBox.Padding = new Thickness(12, textBox.Padding.Top, 12, textBox.Padding.Bottom);
        textBox.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        textBox.BorderThickness = new Thickness(0);
        textBox.RenderTransform = _pluginSearchTextOffset;
        var inputViewport = new Grid
        {
            Height = 40, HorizontalAlignment = HorizontalAlignment.Left,
            RenderTransform = _pluginSearchRevealOffset
        };
        _pluginSearchInputViewport = inputViewport;
        inputViewport.Children.Add(textBox);
        surface.Children.Add(inputViewport);
        host.Children.Add(surface);

        var glyph = new FontIcon
        {
            Glyph = ((char)0xE721).ToString(), FontSize = 14, IsHitTestVisible = false
        };
        AutomationProperties.SetAccessibilityView(glyph, AccessibilityView.Raw);
        var toggle = new Button
        {
            Width = 40, Height = 40, MinWidth = 40, MinHeight = 40,
            Padding = new Thickness(0), BorderThickness = new Thickness(0),
            CornerRadius = textBox.CornerRadius,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            HorizontalAlignment = HorizontalAlignment.Right,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            UseSystemFocusVisuals = true, Content = glyph
        };
        _pluginSearchToggle = toggle;
        host.Children.Add(toggle);
        _pluginSearchLeadingButton = showAllButton;
        _pluginSearchLeadingOffset = new TranslateTransform();
        var originalLeadingTransform = showAllButton.RenderTransform;
        var leadingTransforms = new TransformGroup();
        if (originalLeadingTransform is not null) leadingTransforms.Children.Add(originalLeadingTransform);
        leadingTransforms.Children.Add(_pluginSearchLeadingOffset);
        showAllButton.RenderTransform = leadingTransforms;

        void RefreshLabels()
        {
            var label = T("Cerca plugin e funzioni");
            ToolTipService.SetToolTip(toggle, label);
            AutomationProperties.SetName(toggle, label);
            AutomationProperties.SetName(textBox, label);
        }
        RefreshLabels();
        var labelToken = textBox.RegisterPropertyChangedCallback(Microsoft.UI.Xaml.Controls.TextBox.PlaceholderTextProperty,
            (_, _) => RefreshLabels());
        toggle.Click += (_, _) =>
        {
            SetPluginSearchExpanded(true);
            textBox.Focus(FocusState.Programmatic);
        };
        host.PreviewKeyDown += (_, args) =>
        {
            if (args.Key != VirtualKey.Escape || !_pluginSearchExpanded) return;
            args.Handled = true;
            textBox.Text = string.Empty;
            SetPluginSearchExpanded(false);
            toggle.Focus(FocusState.Keyboard);
        };
        host.LostFocus += (_, _) => dispatcher.TryEnqueue(() =>
        {
            if (ReferenceEquals(host, _pluginSearchHost) && _pluginSearchExpanded &&
                string.IsNullOrWhiteSpace(textBox.Text) && !PluginSearchContainsFocus())
                SetPluginSearchExpanded(false);
        });
        TextChangedEventHandler queryChanged = (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(textBox.Text)) SetPluginSearchExpanded(true);
            else if (_pluginSearchExpanded && !PluginSearchContainsFocus()) SetPluginSearchExpanded(false);
        };
        textBox.TextChanged += queryChanged;

        var observations = new List<(UIElement Element, DependencyProperty Property, long Token)>();
        XamlRoot? observedRoot = null;
        UIElement? pointerRoot = null;
        PointerEventHandler outsidePress = (_, args) => CollapseEmptyPluginSearchOutside(args.OriginalSource as DependencyObject);
        UISettings? motionSettings = null;
        var motionSubscribed = false;
        void StopWhenHidden()
        {
            if (!PluginSearchIsVisible()) FinishPluginSearchMorph();
        }
        void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => StopWhenHidden();
        void MotionChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args)
        {
            dispatcher.TryEnqueue(() =>
            {
                if (ReferenceEquals(host, _pluginSearchHost) && host.IsLoaded && !MotionEnabled())
                    FinishPluginSearchMorph();
            });
        }
        void DetachObservers()
        {
            pointerRoot?.RemoveHandler(UIElement.PointerPressedEvent, outsidePress);
            pointerRoot = null;
            foreach (var (element, property, token) in observations)
                element.UnregisterPropertyChangedCallback(property, token);
            observations.Clear();
            if (observedRoot is not null) observedRoot.Changed -= RootChanged;
            observedRoot = null;
            if (motionSubscribed && motionSettings is not null && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
                motionSettings.AnimationsEnabledChanged -= MotionChanged;
            motionSubscribed = false;
            motionSettings = null;
        }
        host.Loaded += (_, _) =>
        {
            if (!ReferenceEquals(host, _pluginSearchHost)) return;
            DetachObservers();
            RefreshLabels();
            observedRoot = host.XamlRoot;
            pointerRoot = observedRoot?.Content as UIElement;
            pointerRoot?.AddHandler(UIElement.PointerPressedEvent, outsidePress, handledEventsToo: true);
            if (observedRoot is not null) observedRoot.Changed += RootChanged;
            for (DependencyObject? ancestor = host; ancestor is not null; ancestor = VisualTreeHelper.GetParent(ancestor))
            {
                if (ancestor is not UIElement element) continue;
                foreach (var property in new[] { UIElement.VisibilityProperty, UIElement.OpacityProperty })
                {
                    var token = element.RegisterPropertyChangedCallback(property, (_, _) => StopWhenHidden());
                    observations.Add((element, property, token));
                }
            }
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) &&
                ApiInformation.IsEventPresent("Windows.UI.ViewManagement.UISettings", "AnimationsEnabledChanged"))
            {
                motionSettings = new UISettings();
                motionSettings.AnimationsEnabledChanged += MotionChanged;
                motionSubscribed = true;
            }
            FinishPluginSearchMorph();
        };
        host.Unloaded += (_, _) =>
        {
            DetachObservers();
            if (ReferenceEquals(host, _pluginSearchHost)) FinishPluginSearchMorph();
        };
        host.EffectiveViewportChanged += (_, args) =>
        {
            if (!ReferenceEquals(host, _pluginSearchHost)) return;
            _pluginSearchViewport = args.EffectiveViewport;
            StopWhenHidden();
        };
        void WindowClosed(object sender, WindowEventArgs args)
        {
            DetachObservers();
            FinishPluginSearchMorph();
        }
        Closed += WindowClosed;
        _pluginSearchDetach = () =>
        {
            DetachObservers();
            Closed -= WindowClosed;
            textBox.TextChanged -= queryChanged;
            textBox.UnregisterPropertyChangedCallback(Microsoft.UI.Xaml.Controls.TextBox.PlaceholderTextProperty, labelToken);
            if (ReferenceEquals(showAllButton.RenderTransform, leadingTransforms))
                showAllButton.RenderTransform = originalLeadingTransform;
        };
        FinishPluginSearchMorph();
        return host;
    }

    private void SetPluginSearchExpanded(bool expanded, bool animate = true)
    {
        if (_pluginSearchHost is null || _pluginSearchInputViewport is null ||
            _pluginSearchShellScale is null || _pluginSearchLeadingOffset is null ||
            _pluginSearchRevealOffset is null || _pluginSearchTextOffset is null) return;
        if (_pluginSearchExpanded == expanded)
        {
            if (!animate) FinishPluginSearchMorph();
            return;
        }
        var shellScale = _pluginSearchShellScale.ScaleX;
        var reveal = _pluginSearchRevealOffset.X;
        var textOffset = _pluginSearchTextOffset.X;
        var opacity = _pluginSearchInputViewport.Opacity;
        var sameRow = _pluginSearchLeadingButton is not null &&
            Grid.GetRow(_pluginSearchLeadingButton) == Grid.GetRow(_pluginSearchHost);
        var leadingOffset = sameRow ? _pluginSearchLeadingOffset.X + _pluginSearchExpandedWidth - _pluginSearchHost.Width : 0;
        _pluginSearchMorph?.Stop();
        _pluginSearchMorph = null;
        _pluginSearchExpanded = expanded;
        if (!expanded && PluginSearchContainsFocus()) _pluginSearchToggle?.Focus(FocusState.Programmatic);
        if (!animate || !MotionEnabled() || !PluginSearchIsVisible())
        {
            FinishPluginSearchMorph();
            return;
        }

        // Reserve layout once, then animate only transforms and opacity.
        ApplyPluginSearchLayout(reserveExpandedWidth: true);
        _pluginSearchSurface!.Visibility = Visibility.Visible;
        _pluginSearchInputViewport.Visibility = Visibility.Visible;
        _pluginSearchToggle!.Background = null;
        _pluginSearchToggle.BorderThickness = new Thickness(0);
        var morph = new Storyboard();
        AddPluginSearchAnimation(morph, _pluginSearchShellScale, "ScaleX", shellScale,
            expanded ? 1 : PluginSearchClosedWidth / _pluginSearchExpandedWidth);
        var hiddenWidth = _pluginSearchExpandedWidth - PluginSearchClosedWidth;
        AddPluginSearchAnimation(morph, _pluginSearchRevealOffset, "X", reveal, expanded ? 0 : hiddenWidth);
        AddPluginSearchAnimation(morph, _pluginSearchTextOffset, "X", textOffset, expanded ? 0 : -hiddenWidth);
        AddPluginSearchAnimation(morph, _pluginSearchLeadingOffset, "X", leadingOffset, expanded || !sameRow ? 0 : hiddenWidth);
        AddPluginSearchAnimation(morph, _pluginSearchInputViewport, "Opacity", opacity, expanded ? 1 : 0);
        morph.Completed += (_, _) => { if (ReferenceEquals(_pluginSearchMorph, morph)) FinishPluginSearchMorph(); };
        _pluginSearchMorph = morph;
        morph.Begin();
    }

    // availableWidth is the space for search alone, after any same-row siblings/gaps.
    // Normal widths are 220-360; a narrower viewport is allowed to shrink below 220.
    private void UpdatePluginSearchWidth(double availableWidth)
    {
        if (double.IsNaN(availableWidth) || availableWidth <= 0) return;
        var width = Math.Clamp(availableWidth, PluginSearchClosedWidth, PluginSearchMaximumWidth);
        if (Math.Abs(width - _pluginSearchExpandedWidth) < 0.5) return;
        _pluginSearchExpandedWidth = width;
        FinishPluginSearchMorph();
    }

    private void ApplyPluginSearchLayout(bool reserveExpandedWidth = false)
    {
        if (_pluginSearchHost is null || _pluginSearchSurface is null || _pluginSearchInputViewport is null ||
            _pluginSearchSurfaceOffset is null || _pluginSearchShellScale is null) return;
        var width = _pluginSearchExpanded || reserveExpandedWidth ? _pluginSearchExpandedWidth : PluginSearchClosedWidth;
        _pluginSearchHost.Width = width;
        _pluginSearchSurface.Width = _pluginSearchExpandedWidth;
        _pluginSearchSurfaceOffset.X = width - _pluginSearchExpandedWidth;
        _pluginSearchShellScale.CenterX = _pluginSearchExpandedWidth;
        var inputWidth = _pluginSearchExpandedWidth - PluginSearchClosedWidth;
        _pluginSearchInputViewport.Width = inputWidth;
        _pluginSearchInputViewport.Clip = new RectangleGeometry { Rect = new Rect(0, 0, inputWidth, 40) };
        _pluginSearchInputViewport.IsHitTestVisible = _pluginSearchExpanded;
        _pluginSearchBox.IsTabStop = _pluginSearchExpanded;
        AutomationProperties.SetAccessibilityView(_pluginSearchBox,
            _pluginSearchExpanded ? AccessibilityView.Content : AccessibilityView.Raw);
    }

    private void FinishPluginSearchMorph()
    {
        _pluginSearchMorph?.Stop();
        _pluginSearchMorph = null;
        ApplyPluginSearchLayout();
        if (_pluginSearchShellScale is not null)
            _pluginSearchShellScale.ScaleX = _pluginSearchExpanded ? 1 : PluginSearchClosedWidth / _pluginSearchExpandedWidth;
        var reveal = _pluginSearchExpanded ? 0 : _pluginSearchExpandedWidth - PluginSearchClosedWidth;
        if (_pluginSearchRevealOffset is not null) _pluginSearchRevealOffset.X = reveal;
        if (_pluginSearchTextOffset is not null) _pluginSearchTextOffset.X = -reveal;
        if (_pluginSearchLeadingOffset is not null) _pluginSearchLeadingOffset.X = 0;
        if (_pluginSearchInputViewport is not null)
        {
            _pluginSearchInputViewport.Opacity = _pluginSearchExpanded ? 1 : 0;
            _pluginSearchInputViewport.Visibility = _pluginSearchExpanded ? Visibility.Visible : Visibility.Collapsed;
        }
        if (_pluginSearchSurface is not null)
            _pluginSearchSurface.Visibility = _pluginSearchExpanded ? Visibility.Visible : Visibility.Collapsed;
        if (_pluginSearchToggle is not null && _pluginSearchShell is not null)
        {
            _pluginSearchToggle.Background = _pluginSearchExpanded ? null : _pluginSearchShell.Background;
            _pluginSearchToggle.BorderBrush = _pluginSearchShell.BorderBrush;
            _pluginSearchToggle.BorderThickness = _pluginSearchExpanded ? new Thickness(0) : _pluginSearchShell.BorderThickness;
        }
    }

    private static void AddPluginSearchAnimation(Storyboard storyboard, DependencyObject target,
        string property, double from, double to)
    {
        var animation = new DoubleAnimation
        {
            From = from, To = to, Duration = new Duration(TimeSpan.FromMilliseconds(200)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }

    private bool PluginSearchContainsFocus()
    {
        if (_pluginSearchHost?.XamlRoot is not { } root) return false;
        for (var element = FocusManager.GetFocusedElement(root) as DependencyObject;
            element is not null; element = VisualTreeHelper.GetParent(element))
            if (ReferenceEquals(element, _pluginSearchHost)) return true;
        return false;
    }

    private void CollapseEmptyPluginSearchOutside(DependencyObject? source)
    {
        if (!_pluginSearchExpanded || !string.IsNullOrWhiteSpace(_pluginSearchBox.Text)) return;
        for (var element = source; element != null; element = VisualTreeHelper.GetParent(element))
            if (ReferenceEquals(element, _pluginSearchHost)) return;
        SetPluginSearchExpanded(false);
    }

    private bool PluginSearchIsVisible()
    {
        if (_pluginSearchHost is not { IsLoaded: true } host || host.XamlRoot?.IsHostVisible != true) return false;
        for (DependencyObject? element = host; element is not null; element = VisualTreeHelper.GetParent(element))
            if (element is UIElement visual && (visual.Visibility != Visibility.Visible || visual.Opacity <= 0)) return false;
        return Math.Min(host.ActualWidth, _pluginSearchViewport.Right) > Math.Max(0, _pluginSearchViewport.Left) &&
            Math.Min(host.ActualHeight, _pluginSearchViewport.Bottom) > Math.Max(0, _pluginSearchViewport.Top);
    }
}

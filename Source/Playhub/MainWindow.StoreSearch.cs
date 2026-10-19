using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.System;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Playhub;

public sealed partial class MainWindow
{
    private TextBox CreateStoreSearchTextBox(string placeholder) => new()
    {
        MinWidth = 0, MinHeight = 40, Height = 40,
        Padding = new Thickness(40, 9, 12, 3), Margin = new Thickness(0),
        BorderThickness = new Thickness(1),
        BorderBrush = ResourceBrush("ControlStrokeColorDefaultBrush", Color.FromArgb(58, 255, 255, 255)),
        Background = new SolidColorBrush(Color.FromArgb(130, 38, 38, 42)),
        CornerRadius = new CornerRadius(6), VerticalContentAlignment = VerticalAlignment.Center,
        FontSize = 14, PlaceholderText = placeholder, HorizontalAlignment = HorizontalAlignment.Stretch,
        AcceptsReturn = false, TextWrapping = TextWrapping.NoWrap, IsSpellCheckEnabled = false
    };

    // Both stores use the same native editor, focus and morph behavior.
    private sealed class StoreSearchController : IDisposable
    {
        internal const double ClosedWidth = 40;
        internal const double MaximumWidth = 360;
        private readonly MainWindow _owner;
        private readonly TextBox _input;
        private readonly Button _leading;
        private readonly Func<string> _label;
        private readonly Grid _surface;
        private readonly Border _shell;
        private readonly Grid _inputViewport;
        private readonly TranslateTransform _surfaceOffset = new();
        private readonly TranslateTransform _revealOffset = new();
        private readonly TranslateTransform _textOffset = new();
        private readonly Transform? _originalLeadingTransform;
        private readonly TransformGroup _leadingTransforms = new();
        private readonly List<(UIElement Element, DependencyProperty Property, long Token)> _observations = new();
        private readonly long _labelToken;
        private readonly PointerEventHandler _outsidePress;
        private Rect _viewport;
        private XamlRoot? _observedRoot;
        private UIElement? _pointerRoot;
        private UISettings? _motionSettings;
        private bool _motionSubscribed;
        private bool _disposed;

        internal Grid Host { get; }
        internal Button Toggle { get; }
        internal bool Expanded { get; private set; }
        internal double ExpandedWidth { get; private set; }
        internal Storyboard? Morph { get; private set; }
        internal ScaleTransform ShellScale { get; } = new();
        internal TranslateTransform LeadingOffset { get; } = new();

        internal StoreSearchController(MainWindow owner, TextBox input, Button leading, Func<string> label,
            double expandedWidth = MaximumWidth)
        {
            _owner = owner; _input = input; _leading = leading; _label = label;
            _outsidePress = OutsidePress;
            Expanded = !string.IsNullOrWhiteSpace(input.Text);
            ExpandedWidth = Math.Clamp(expandedWidth, ClosedWidth, MaximumWidth);
            Host = new Grid
            {
                Width = ClosedWidth, MinWidth = ClosedWidth, MaxWidth = MaximumWidth, Height = 40,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
            };
            _surface = new Grid { Height = 40, HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = _surfaceOffset };
            _shell = new Border
            {
                Background = input.Background, BorderBrush = input.BorderBrush,
                BorderThickness = input.BorderThickness, CornerRadius = input.CornerRadius,
                RenderTransform = ShellScale, IsHitTestVisible = false
            };
            AutomationProperties.SetAccessibilityView(_shell, AccessibilityView.Raw);
            _surface.Children.Add(_shell);
            // Keep the native clear button, caret, selection and keyboard editing.
            input.Padding = new Thickness(12, input.Padding.Top, 12, input.Padding.Bottom);
            input.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            input.BorderThickness = new Thickness(0);
            input.RenderTransform = _textOffset;
            _inputViewport = new Grid { Height = 40, HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = _revealOffset };
            _inputViewport.Children.Add(input); _surface.Children.Add(_inputViewport); Host.Children.Add(_surface);
            var glyph = new FontIcon { Glyph = "\uE721", FontSize = 14, IsHitTestVisible = false };
            AutomationProperties.SetAccessibilityView(glyph, AccessibilityView.Raw);
            Toggle = new Button
            {
                Width = 40, Height = 40, MinWidth = 40, MinHeight = 40,
                Padding = new Thickness(0), BorderThickness = new Thickness(0), CornerRadius = input.CornerRadius,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), HorizontalAlignment = HorizontalAlignment.Right,
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                UseSystemFocusVisuals = true, Content = glyph
            };
            Host.Children.Add(Toggle);
            _originalLeadingTransform = leading.RenderTransform;
            if (_originalLeadingTransform is not null) _leadingTransforms.Children.Add(_originalLeadingTransform);
            _leadingTransforms.Children.Add(LeadingOffset); leading.RenderTransform = _leadingTransforms;
            RefreshLabels();
            _labelToken = input.RegisterPropertyChangedCallback(Microsoft.UI.Xaml.Controls.TextBox.PlaceholderTextProperty, (_, _) => RefreshLabels());
            Toggle.Click += ToggleClicked;
            Host.PreviewKeyDown += PreviewKeyDown;
            Host.LostFocus += LostFocus;
            input.TextChanged += TextChanged;
            Host.Loaded += Loaded;
            Host.Unloaded += Unloaded;
            Host.EffectiveViewportChanged += ViewportChanged;
            owner.Closed += WindowClosed;
            Finish();
        }

        private void RefreshLabels()
        {
            if (_disposed) return;
            var label = _label();
            ToolTipService.SetToolTip(Toggle, label);
            AutomationProperties.SetName(Toggle, label); AutomationProperties.SetName(_input, label);
        }

        private void ToggleClicked(object sender, RoutedEventArgs args)
        {
            if (_disposed) return;
            SetExpanded(true); _input.Focus(FocusState.Programmatic);
        }

        private void PreviewKeyDown(object sender, KeyRoutedEventArgs args)
        {
            if (_disposed || args.Key != VirtualKey.Escape || !Expanded) return;
            args.Handled = true; _input.Text = string.Empty;
            SetExpanded(false); Toggle.Focus(FocusState.Keyboard);
        }

        private void LostFocus(object sender, RoutedEventArgs args) => _owner.DispatcherQueue.TryEnqueue(() =>
        {
            if (!_disposed && Expanded && string.IsNullOrWhiteSpace(_input.Text) && !ContainsFocus())
                SetExpanded(false);
        });

        private void TextChanged(object sender, TextChangedEventArgs args)
        {
            if (_disposed) return;
            if (!string.IsNullOrWhiteSpace(_input.Text)) SetExpanded(true);
            else if (Expanded && !ContainsFocus()) SetExpanded(false);
        }

        private void OutsidePress(object sender, PointerRoutedEventArgs args) =>
            CollapseEmptyOutside(args.OriginalSource as DependencyObject);

        private void Loaded(object sender, RoutedEventArgs args)
        {
            if (_disposed) return;
            DetachObservers(); RefreshLabels();
            _observedRoot = Host.XamlRoot; _pointerRoot = _observedRoot?.Content as UIElement;
            _pointerRoot?.AddHandler(UIElement.PointerPressedEvent, _outsidePress, handledEventsToo: true);
            if (_observedRoot is not null) _observedRoot.Changed += RootChanged;
            for (DependencyObject? ancestor = Host; ancestor is not null; ancestor = VisualTreeHelper.GetParent(ancestor))
            {
                if (ancestor is not UIElement element) continue;
                foreach (var property in new[] { UIElement.VisibilityProperty, UIElement.OpacityProperty })
                {
                    var token = element.RegisterPropertyChangedCallback(property, (_, _) => StopWhenHidden());
                    _observations.Add((element, property, token));
                }
            }
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) &&
                ApiInformation.IsEventPresent("Windows.UI.ViewManagement.UISettings", "AnimationsEnabledChanged"))
            {
                _motionSettings = new UISettings();
                _motionSettings.AnimationsEnabledChanged += MotionChanged; _motionSubscribed = true;
            }
            Finish();
        }

        private void Unloaded(object sender, RoutedEventArgs args) { DetachObservers(); Finish(); }
        private void ViewportChanged(object sender, EffectiveViewportChangedEventArgs args)
        {
            if (_disposed) return;
            _viewport = args.EffectiveViewport; StopWhenHidden();
        }
        private void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => StopWhenHidden();
        private void MotionChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args) =>
            _owner.DispatcherQueue.TryEnqueue(() => { if (!_disposed && Host.IsLoaded && !MotionEnabled()) Finish(); });
        private void WindowClosed(object sender, WindowEventArgs args) => Dispose();
        private void StopWhenHidden() { if (!_disposed && !IsVisible()) Finish(); }

        internal void SetExpanded(bool expanded, bool animate = true)
        {
            if (_disposed) return;
            if (Expanded == expanded) { if (!animate) Finish(); return; }
            var shellScale = ShellScale.ScaleX;
            var reveal = _revealOffset.X; var textOffset = _textOffset.X; var opacity = _inputViewport.Opacity;
            var sameRow = Grid.GetRow(_leading) == Grid.GetRow(Host);
            var leadingOffset = sameRow ? LeadingOffset.X + ExpandedWidth - Host.Width : 0;
            Morph?.Stop(); Morph = null; Expanded = expanded;
            if (!expanded && ContainsFocus()) Toggle.Focus(FocusState.Programmatic);
            if (!animate || !MotionEnabled() || !IsVisible()) { Finish(); return; }
            // Reserve layout once; animate transforms and opacity only.
            ApplyLayout(reserveExpandedWidth: true);
            _surface.Visibility = _inputViewport.Visibility = Visibility.Visible;
            Toggle.Background = null; Toggle.BorderThickness = new Thickness(0);
            var morph = new Storyboard();
            AddAnimation(morph, ShellScale, "ScaleX", shellScale, expanded ? 1 : ClosedWidth / ExpandedWidth);
            var hiddenWidth = ExpandedWidth - ClosedWidth;
            AddAnimation(morph, _revealOffset, "X", reveal, expanded ? 0 : hiddenWidth);
            AddAnimation(morph, _textOffset, "X", textOffset, expanded ? 0 : -hiddenWidth);
            AddAnimation(morph, LeadingOffset, "X", leadingOffset, expanded || !sameRow ? 0 : hiddenWidth);
            AddAnimation(morph, _inputViewport, "Opacity", opacity, expanded ? 1 : 0);
            morph.Completed += (_, _) => { if (!_disposed && ReferenceEquals(Morph, morph)) Finish(); };
            Morph = morph; morph.Begin();
        }

        internal void Resize(double availableWidth)
        {
            if (_disposed || double.IsNaN(availableWidth) || availableWidth <= 0) return;
            var width = Math.Clamp(availableWidth, ClosedWidth, MaximumWidth);
            if (Math.Abs(width - ExpandedWidth) < 0.5) return;
            ExpandedWidth = width; Finish();
        }

        private void ApplyLayout(bool reserveExpandedWidth = false)
        {
            var width = Expanded || reserveExpandedWidth ? ExpandedWidth : ClosedWidth;
            Host.Width = width; _surface.Width = ExpandedWidth; _surfaceOffset.X = width - ExpandedWidth;
            ShellScale.CenterX = ExpandedWidth;
            var inputWidth = ExpandedWidth - ClosedWidth; _inputViewport.Width = inputWidth;
            _inputViewport.Clip = new RectangleGeometry { Rect = new Rect(0, 0, inputWidth, 40) };
            _inputViewport.IsHitTestVisible = Expanded; _input.IsTabStop = Expanded;
            AutomationProperties.SetAccessibilityView(_input, Expanded ? AccessibilityView.Content : AccessibilityView.Raw);
        }

        internal void Finish()
        {
            Morph?.Stop(); Morph = null; ApplyLayout();
            ShellScale.ScaleX = Expanded ? 1 : ClosedWidth / ExpandedWidth;
            _revealOffset.X = Expanded ? 0 : ExpandedWidth - ClosedWidth;
            _textOffset.X = -_revealOffset.X; LeadingOffset.X = 0;
            _inputViewport.Opacity = Expanded ? 1 : 0;
            _surface.Visibility = _inputViewport.Visibility = Expanded ? Visibility.Visible : Visibility.Collapsed;
            Toggle.Background = Expanded ? null : _shell.Background; Toggle.BorderBrush = _shell.BorderBrush;
            Toggle.BorderThickness = Expanded ? new Thickness(0) : _shell.BorderThickness;
        }

        internal static void AddAnimation(Storyboard storyboard, DependencyObject target, string property, double from, double to)
        {
            var animation = new DoubleAnimation
            {
                From = from, To = to, Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(animation, target); Storyboard.SetTargetProperty(animation, property);
            storyboard.Children.Add(animation);
        }

        private bool ContainsFocus()
        {
            if (Host.XamlRoot is not { } root) return false;
            for (var element = FocusManager.GetFocusedElement(root) as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
                if (ReferenceEquals(element, Host)) return true;
            return false;
        }

        internal void CollapseEmptyOutside(DependencyObject? source)
        {
            if (_disposed || !Expanded || !string.IsNullOrWhiteSpace(_input.Text)) return;
            for (var element = source; element is not null; element = VisualTreeHelper.GetParent(element))
                if (ReferenceEquals(element, Host)) return;
            SetExpanded(false);
        }

        private bool IsVisible()
        {
            if (!Host.IsLoaded || Host.XamlRoot?.IsHostVisible != true) return false;
            for (DependencyObject? element = Host; element is not null; element = VisualTreeHelper.GetParent(element))
                if (element is UIElement visual && (visual.Visibility != Visibility.Visible || visual.Opacity <= 0)) return false;
            return Math.Min(Host.ActualWidth, _viewport.Right) > Math.Max(0, _viewport.Left) &&
                Math.Min(Host.ActualHeight, _viewport.Bottom) > Math.Max(0, _viewport.Top);
        }

        private void DetachObservers()
        {
            _pointerRoot?.RemoveHandler(UIElement.PointerPressedEvent, _outsidePress); _pointerRoot = null;
            foreach (var (element, property, token) in _observations) element.UnregisterPropertyChangedCallback(property, token);
            _observations.Clear();
            if (_observedRoot is not null) _observedRoot.Changed -= RootChanged;
            _observedRoot = null;
            if (_motionSubscribed && _motionSettings is not null && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
                _motionSettings.AnimationsEnabledChanged -= MotionChanged;
            _motionSubscribed = false; _motionSettings = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; DetachObservers(); Finish(); _owner.Closed -= WindowClosed;
            Toggle.Click -= ToggleClicked; Host.PreviewKeyDown -= PreviewKeyDown; Host.LostFocus -= LostFocus;
            _input.TextChanged -= TextChanged;
            _input.UnregisterPropertyChangedCallback(Microsoft.UI.Xaml.Controls.TextBox.PlaceholderTextProperty, _labelToken);
            Host.Loaded -= Loaded; Host.Unloaded -= Unloaded; Host.EffectiveViewportChanged -= ViewportChanged;
            if (ReferenceEquals(_leading.RenderTransform, _leadingTransforms)) _leading.RenderTransform = _originalLeadingTransform;
        }
    }
}

using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Windows.Foundation;
using Windows.UI;

namespace Playhub;

public sealed partial class MainWindow
{
    private static readonly TimeSpan FeaturedAutoAdvanceInterval = TimeSpan.FromSeconds(10);
    private readonly Stopwatch _featuredAutoAdvanceClock = new();
    private readonly ConditionalWeakTable<FrameworkElement, FeaturedAutoAdvanceControlState> _featuredAutoAdvanceControls = new();
    private DispatcherQueueTimer? _featuredAutoAdvanceDeadline;
    private FeaturedAutoAdvanceControlState? _featuredAutoAdvanceActiveControl;
    private FrameworkElement? _featuredAutoAdvanceCard;
    private FrameworkElement? _featuredAutoAdvanceObservedHost;
    private XamlRoot? _featuredAutoAdvanceRoot;
    private long _featuredAutoAdvanceVisibilityToken;
    private Rect _featuredAutoAdvanceViewport;
    private bool _featuredAutoAdvanceHasViewport;
    private bool _featuredAutoAdvancePointerOver;
    private bool _featuredAutoAdvanceUpdating;
    private bool _featuredAutoAdvanceAdvancing;
    private bool _featuredAutoAdvanceDisposed;

    private sealed record FeaturedAutoAdvanceControlState(
        Border Indicator, ShapeVisual Pie, CompositionEllipseGeometry Geometry,
        CompositionPropertySet Progress, ScalarKeyFrameAnimation Countdown,
        ExpressionAnimation TrimEnd)
    {
        private double _fraction = 1;
        public Stopwatch? Clock { get; set; }
        public bool IsAnimating { get; set; }

        // Keep the UI-review hook current without a UI-thread sampling timer.
        // This reports logical progress, not presented frames or achieved FPS.
        public double Fraction
        {
            get => Clock is { } clock
                ? Math.Clamp(1 - clock.Elapsed.TotalSeconds / FeaturedAutoAdvanceInterval.TotalSeconds, 0, 1)
                : _fraction;
            set { _fraction = value; Clock = null; }
        }
    }

    private FrameworkElement BuildFeaturedAutoAdvanceControl(FrameworkElement card)
    {
        if (_featuredAutoAdvanceControls.TryGetValue(card, out var existing)) return existing.Indicator;
        EnsureFeaturedAutoAdvance();
        var indicator = new Border
        {
            Width = 20, Height = 20, Margin = new Thickness(18, 0, 0, 18),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false, Tag = "featured-countdown"
        };
        var compositor = ElementCompositionPreview.GetElementVisual(indicator).Compositor;
        var pie = compositor.CreateShapeVisual();
        pie.Size = new Vector2(20, 20);
        var ellipse = compositor.CreateEllipseGeometry();
        ellipse.Center = new Vector2(10, 10);
        ellipse.Radius = new Vector2(5, 5);
        var brush = compositor.CreateColorBrush(Color.FromArgb(170, 255, 255, 255));
        // A stroke twice the path radius fills the sector all the way to its
        // center. One continuous path has no seam between clipped semicircles.
        var sector = compositor.CreateSpriteShape(ellipse);
        sector.StrokeBrush = brush;
        sector.StrokeThickness = 10;
        sector.StrokeStartCap = CompositionStrokeCap.Flat;
        sector.StrokeEndCap = CompositionStrokeCap.Flat;
        sector.CenterPoint = new Vector2(10, 10);
        sector.RotationAngleInDegrees = -90;
        pie.Shapes.Add(sector);
        var progress = compositor.CreatePropertySet();
        progress.InsertScalar("Fraction", 1);
        var trimEnd = compositor.CreateExpressionAnimation("progress.Fraction");
        trimEnd.SetReferenceParameter("progress", progress);
        var countdown = compositor.CreateScalarKeyFrameAnimation();
        var linear = compositor.CreateLinearEasingFunction();
        countdown.InsertKeyFrame(1, 0, linear);
        var state = new FeaturedAutoAdvanceControlState(indicator, pie, ellipse,
            progress, countdown, trimEnd);
        ElementCompositionPreview.SetElementChildVisual(indicator, pie);
        _featuredAutoAdvanceControls.Add(card, state);
        UpdateFeaturedPie(state, 1);
        return indicator;
    }

    private void EnsureFeaturedAutoAdvance()
    {
        if (_featuredAutoAdvanceDeadline != null || _featuredAutoAdvanceDisposed) return;
        _featuredAutoAdvanceDeadline = DispatcherQueue.CreateTimer();
        _featuredAutoAdvanceDeadline.IsRepeating = false;
        _featuredAutoAdvanceDeadline.Tick += FeaturedAutoAdvanceDeadlineTick;
        Closed += FeaturedAutoAdvanceClosed;
        AppWindow.Changed += FeaturedAutoAdvanceWindowChanged;
        var host = _pluginFeaturedHost;
        _featuredAutoAdvanceObservedHost = host;
        host.PointerEntered += FeaturedAutoAdvancePointerEntered;
        host.PointerExited += FeaturedAutoAdvancePointerExited;
        host.PointerCanceled += FeaturedAutoAdvancePointerExited;
        host.Loaded += FeaturedAutoAdvanceHostLoaded;
        host.Unloaded += FeaturedAutoAdvanceHostUnloaded;
        // Observe the fixed viewport, not the cards translating through it.
        host.EffectiveViewportChanged += FeaturedAutoAdvanceViewportChanged;
        _featuredAutoAdvanceVisibilityToken = host.RegisterPropertyChangedCallback(
            UIElement.VisibilityProperty, (_, _) =>
            {
                if (host.Visibility != Visibility.Visible) _featuredAutoAdvancePointerOver = false;
                UpdateFeaturedAutoAdvanceState();
            });
    }

    private void ResetFeaturedAutoAdvance()
    {
        StopFeaturedAutoAdvance();
        _featuredAutoAdvanceClock.Reset();
        UpdateFeaturedAutoAdvanceState();
    }

    private void UpdateFeaturedAutoAdvanceState()
    {
        if (_featuredAutoAdvanceDisposed || _featuredAutoAdvanceUpdating ||
            _featuredAutoAdvanceAdvancing || _featuredAutoAdvanceDeadline == null) return;
        _featuredAutoAdvanceUpdating = true;
        try
        {
            var root = _featuredAutoAdvanceObservedHost?.XamlRoot;
            if (!ReferenceEquals(root, _featuredAutoAdvanceRoot))
            {
                if (_featuredAutoAdvanceRoot != null) _featuredAutoAdvanceRoot.Changed -= FeaturedAutoAdvanceRootChanged;
                _featuredAutoAdvanceRoot = root;
                if (root != null) root.Changed += FeaturedAutoAdvanceRootChanged;
            }
            var children = _pluginFeaturedCarouselHost.Children;
            var card = children.Count > 0 ? children[children.Count - 1] as FrameworkElement : null;
            if (card == null || !_featuredAutoAdvanceControls.TryGetValue(card, out var state))
            {
                StopFeaturedAutoAdvance();
                _featuredAutoAdvanceCard = null;
                _featuredAutoAdvanceActiveControl = null;
                return;
            }
            if (!ReferenceEquals(_featuredAutoAdvanceCard, card))
            {
                StopFeaturedAutoAdvance();
                _featuredAutoAdvanceCard = card;
                _featuredAutoAdvanceActiveControl = state;
                _featuredAutoAdvanceClock.Reset();
            }
            var visible = IsFeaturedAutoAdvanceHostVisible();
            state.Indicator.Visibility = visible && _featuredPluginKeys.Count > 1
                ? Visibility.Visible : Visibility.Collapsed;
            if (visible && !_featuredAutoAdvancePointerOver && _featuredPluginKeys.Count > 1)
            {
                if (!_featuredAutoAdvanceClock.IsRunning)
                {
                    _featuredAutoAdvanceClock.Start();
                    StartFeaturedPie(state);
                    ScheduleFeaturedAutoAdvanceDeadline();
                }
            }
            else
            {
                StopFeaturedAutoAdvance();
                UpdateFeaturedPie(state, 1 - _featuredAutoAdvanceClock.Elapsed.TotalSeconds / FeaturedAutoAdvanceInterval.TotalSeconds);
                if (!visible)
                {
                    _featuredAutoAdvancePointerOver = false;
                    CompleteFeaturedSlideTransition();
                }
            }
        }
        finally { _featuredAutoAdvanceUpdating = false; }
    }

    private bool IsFeaturedAutoAdvanceHostVisible()
    {
        var host = _pluginFeaturedHost;
        if (_currentPageTag != "plugins" || _pluginStoreMode != "discover" || _pluginShowAll ||
            !string.IsNullOrWhiteSpace(_pluginSearchBox.Text) || _featuredPluginExpanded ||
            _pluginDiscoverView.Visibility != Visibility.Visible || host.Visibility != Visibility.Visible ||
            !host.IsLoaded || host.XamlRoot?.IsHostVisible != true || !AppWindow.IsVisible ||
            AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized })
            return false;
        // An empty reported viewport means hidden, not "not reported yet".
        if (!_featuredAutoAdvanceHasViewport) return false;
        var viewport = _featuredAutoAdvanceViewport;
        return viewport.Width > 0 && viewport.Height > 0 &&
            Math.Min(host.ActualWidth, viewport.Right) > Math.Max(0, viewport.Left) &&
            Math.Min(host.ActualHeight, viewport.Bottom) > Math.Max(0, viewport.Top);
    }

    private static void UpdateFeaturedPie(FeaturedAutoAdvanceControlState state, double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        if (state.IsAnimating)
        {
            state.Progress.StopAnimation("Fraction");
            state.Geometry.StopAnimation("TrimEnd");
            state.IsAnimating = false;
        }
        state.Fraction = fraction;
        state.Progress.InsertScalar("Fraction", (float)fraction);
        state.Geometry.TrimEnd = (float)fraction;
        state.Pie.IsVisible = fraction > 0;
    }

    private void StartFeaturedPie(FeaturedAutoAdvanceControlState state)
    {
        var remaining = FeaturedAutoAdvanceInterval - _featuredAutoAdvanceClock.Elapsed;
        UpdateFeaturedPie(state, remaining.TotalSeconds / FeaturedAutoAdvanceInterval.TotalSeconds);
        if (remaining <= TimeSpan.Zero) return;
        state.Countdown.InsertKeyFrame(0, (float)state.Fraction);
        // Composition requires at least 1 ms; the independent deadline stays exact.
        var minimumDuration = TimeSpan.FromMilliseconds(1);
        state.Countdown.Duration = remaining >= minimumDuration ? remaining : minimumDuration;
        state.Clock = _featuredAutoAdvanceClock;
        // The compositor samples this at its actual render cadence (including 120 Hz).
        // No Rendering subscription, frame-counting, geometry mutation or layout per frame.
        state.IsAnimating = true;
        state.Geometry.StartAnimation("TrimEnd", state.TrimEnd);
        state.Progress.StartAnimation("Fraction", state.Countdown);
    }

    private void StopFeaturedAutoAdvance()
    {
        _featuredAutoAdvanceDeadline?.Stop();
        _featuredAutoAdvanceClock.Stop();
        if (_featuredAutoAdvanceActiveControl is { IsAnimating: true } state)
            UpdateFeaturedPie(state, state.Fraction);
    }

    private void ScheduleFeaturedAutoAdvanceDeadline()
    {
        if (_featuredAutoAdvanceDeadline == null) return;
        var remaining = FeaturedAutoAdvanceInterval - _featuredAutoAdvanceClock.Elapsed;
        _featuredAutoAdvanceDeadline.Interval = remaining > TimeSpan.Zero ? remaining : TimeSpan.FromTicks(1);
        _featuredAutoAdvanceDeadline.Start();
    }

    private void FeaturedAutoAdvanceDeadlineTick(DispatcherQueueTimer sender, object args)
    {
        FeaturedAutoAdvanceTick(sender, args);
        if (_featuredAutoAdvanceClock.IsRunning && !sender.IsRunning) ScheduleFeaturedAutoAdvanceDeadline();
    }

    private void FeaturedAutoAdvanceTick(object? sender, object args)
    {
        UpdateFeaturedAutoAdvanceState();
        if (_featuredAutoAdvanceDisposed || _featuredAutoAdvanceAdvancing || !_featuredAutoAdvanceClock.IsRunning) return;
        if (_featuredAutoAdvanceClock.Elapsed < FeaturedAutoAdvanceInterval) return;
        StopFeaturedAutoAdvance();
        _featuredAutoAdvanceAdvancing = true;
        try { SlideFeaturedPlugin(1); }
        finally
        {
            _featuredAutoAdvanceAdvancing = false;
            UpdateFeaturedAutoAdvanceState();
        }
    }

    private void FeaturedAutoAdvanceHostLoaded(object sender, RoutedEventArgs args) => UpdateFeaturedAutoAdvanceState();

    private void FeaturedAutoAdvanceHostUnloaded(object sender, RoutedEventArgs args)
    {
        _featuredAutoAdvanceHasViewport = false;
        _featuredAutoAdvancePointerOver = false;
        StopFeaturedAutoAdvance();
        CompleteFeaturedSlideTransition();
    }

    private void FeaturedAutoAdvanceViewportChanged(FrameworkElement sender, EffectiveViewportChangedEventArgs args)
    {
        _featuredAutoAdvanceViewport = args.EffectiveViewport;
        _featuredAutoAdvanceHasViewport = true;
        UpdateFeaturedAutoAdvanceState();
    }

    private void FeaturedAutoAdvanceRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => UpdateFeaturedAutoAdvanceState();
    private void FeaturedAutoAdvanceWindowChanged(AppWindow sender, AppWindowChangedEventArgs args) => UpdateFeaturedAutoAdvanceState();
    private void FeaturedAutoAdvancePointerEntered(object sender, PointerRoutedEventArgs args)
    {
        _featuredAutoAdvancePointerOver = true;
        UpdateFeaturedAutoAdvanceState();
    }
    private void FeaturedAutoAdvancePointerExited(object sender, PointerRoutedEventArgs args)
    {
        _featuredAutoAdvancePointerOver = false;
        UpdateFeaturedAutoAdvanceState();
    }
    private void FeaturedAutoAdvanceClosed(object sender, WindowEventArgs args)
    {
        _featuredAutoAdvanceDisposed = true;
        StopFeaturedAutoAdvance();
        CompleteFeaturedSlideTransition();
        Closed -= FeaturedAutoAdvanceClosed;
        AppWindow.Changed -= FeaturedAutoAdvanceWindowChanged;
        if (_featuredAutoAdvanceDeadline != null) _featuredAutoAdvanceDeadline.Tick -= FeaturedAutoAdvanceDeadlineTick;
        if (_featuredAutoAdvanceRoot != null) _featuredAutoAdvanceRoot.Changed -= FeaturedAutoAdvanceRootChanged;
        if (_featuredAutoAdvanceObservedHost is { } host)
        {
            host.PointerEntered -= FeaturedAutoAdvancePointerEntered;
            host.PointerExited -= FeaturedAutoAdvancePointerExited;
            host.PointerCanceled -= FeaturedAutoAdvancePointerExited;
            host.Loaded -= FeaturedAutoAdvanceHostLoaded;
            host.Unloaded -= FeaturedAutoAdvanceHostUnloaded;
            host.EffectiveViewportChanged -= FeaturedAutoAdvanceViewportChanged;
            host.UnregisterPropertyChangedCallback(UIElement.VisibilityProperty, _featuredAutoAdvanceVisibilityToken);
        }
        _featuredAutoAdvanceCard = null;
        _featuredAutoAdvanceActiveControl = null;
        _featuredAutoAdvanceObservedHost = null;
        _featuredAutoAdvanceRoot = null;
        _featuredAutoAdvanceControls.Clear();
    }
}

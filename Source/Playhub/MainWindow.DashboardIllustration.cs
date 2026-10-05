using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Playhub;

public sealed partial class MainWindow
{
    private FrameworkElement BuildDashboardIllustration()
    {
        var ink = ResourceBrush("TextFillColorPrimaryBrush", Color.FromArgb(255, 235, 235, 240));
        var accent = ResourceBrush("AccentFillColorDefaultBrush", Color.FromArgb(255, 150, 185, 255));
        var scene = new Canvas { Width = 280, Height = 132 };
        var stage = new Grid { Height = 148, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Stretch };
        stage.Children.Add(new Viewbox { Child = scene, Stretch = Stretch.Uniform, MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Center });
        AutomationProperties.SetAccessibilityView(stage, AccessibilityView.Raw);

        void Put(FrameworkElement element, double x, double y)
        {
            Canvas.SetLeft(element, x); Canvas.SetTop(element, y); scene.Children.Add(element);
        }
        Border Outline(double width, double height, double opacity = .4, double radius = 4) => new()
        {
            Width = width, Height = height, CornerRadius = new CornerRadius(radius),
            BorderThickness = new Thickness(1), BorderBrush = ink, Opacity = opacity
        };
        void Rule(double x, double y, double width, double opacity = .25) =>
            Put(new Rectangle { Width = width, Height = 1, Fill = ink, Opacity = opacity }, x, y);
        void Dot(double x, double y, double size, double opacity = .45) =>
            Put(new Ellipse { Width = size, Height = size, Fill = ink, Opacity = opacity }, x, y);

        Put(Outline(258, 108, .3, 8), 11, 12);
        Rule(12, 32, 256, .18);
        Dot(24, 21, 3); Dot(31, 21, 3); Dot(38, 21, 3);
        Rule(53, 22, 32, .4);

        // Window switcher, app library and quick settings share one fixed stage.
        Put(Outline(34, 25, .35), 46, 50);
        Rule(47, 58, 32, .22);
        Put(Outline(34, 25, .7), 57, 60);
        Rule(58, 68, 32, .35);
        Rule(63, 75, 15, .35);
        foreach (var x in new[] { 126d, 146d })
        foreach (var y in new[] { 51d, 71d })
        {
            Put(Outline(14, 14, .55, 3), x, y);
            Dot(x + 5, y + 5, 4, .45);
        }
        foreach (var row in new[] { (Y: 55d, Knob: 9d), (Y: 68d, Knob: 25d), (Y: 81d, Knob: 17d) })
        {
            Rule(204, row.Y, 34, .4);
            Put(new Ellipse { Width = 5, Height = 5, Fill = accent, Opacity = .8 }, 204 + row.Knob, row.Y - 2);
        }
        foreach (double x in new[] { 59d, 137d, 215d }) Rule(x, 102, 18, .25);

        var focus = new Border
        {
            Width = 66, Height = 64, CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1), BorderBrush = accent, Opacity = .8
        };
        Put(focus, 35, 41);
        _ = new DashboardIllustrationSession(this, stage, focus);
        return stage;
    }

    private sealed class DashboardIllustrationSession
    {
        private readonly MainWindow _owner;
        private readonly FrameworkElement _stage;
        private readonly FrameworkElement _focus;
        private readonly UISettings _settings = new();
        private readonly List<(UIElement Element, DependencyProperty Property, long Token)> _observations = new();
        private Visual? _visual;
        private ScalarKeyFrameAnimation? _animation;
        private AnimationController? _controller;
        private XamlRoot? _root;
        private bool _loaded, _active = true, _closed, _motionSubscribed, _playing;
        private bool _failed;
        private Rect _viewport;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        public DashboardIllustrationSession(MainWindow owner, FrameworkElement stage, FrameworkElement focus)
        {
            _owner = owner; _stage = stage; _focus = focus;
            stage.Loaded += Loaded;
            stage.Unloaded += Unloaded;
            stage.EffectiveViewportChanged += ViewportChanged;
            stage.SizeChanged += (_, args) =>
            {
                stage.Clip = new RectangleGeometry { Rect = new Rect(0, 0, Math.Max(0, args.NewSize.Width), Math.Max(0, args.NewSize.Height)) };
                Update();
            };
        }

        private void Loaded(object sender, RoutedEventArgs args)
        {
            if (_loaded || _closed) return;
            _loaded = true;
            _failed = false;
            _active = GetForegroundWindow() == WinRT.Interop.WindowNative.GetWindowHandle(_owner);
            _owner.Activated += Activated;
            _owner.Closed += Closed;
            _owner.AppWindow.Changed += WindowChanged;
            _root = _stage.XamlRoot;
            if (_root is not null) _root.Changed += RootChanged;
            for (DependencyObject? parent = _stage; parent is not null; parent = VisualTreeHelper.GetParent(parent))
            {
                if (parent is not UIElement element) continue;
                foreach (var property in new[] { UIElement.VisibilityProperty, UIElement.OpacityProperty })
                {
                    long token = element.RegisterPropertyChangedCallback(property, (_, _) => Update());
                    _observations.Add((element, property, token));
                }
            }
            if (ApiInformation.IsEventPresent("Windows.UI.ViewManagement.UISettings", "AnimationsEnabledChanged"))
            {
                _settings.AnimationsEnabledChanged += MotionChanged;
                _motionSubscribed = true;
            }
            Update();
        }

        private bool Visible()
        {
            if (!_loaded || _closed || !_active || !_stage.IsLoaded || _root?.IsHostVisible != true ||
                !_owner.AppWindow.IsVisible || _owner.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized }) return false;
            foreach (var observation in _observations)
                if (observation.Element.Visibility != Visibility.Visible || observation.Element.Opacity <= 0) return false;
            return _viewport.Width > 0 && _viewport.Height > 0 &&
                Math.Min(_stage.ActualWidth, _viewport.Right) > Math.Max(0, _viewport.Left) &&
                Math.Min(_stage.ActualHeight, _viewport.Bottom) > Math.Max(0, _viewport.Top);
        }

        private void Update()
        {
            bool play = Visible() && _settings.AnimationsEnabled && !_failed;
            if (play && _visual is null)
            {
                try
                {
                    _visual = ElementCompositionPreview.GetElementVisual(_focus);
                    var compositor = _visual.Compositor;
                    using var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(.3f, 0), new Vector2(.2f, 1));
                    _animation = compositor.CreateScalarKeyFrameAnimation();
                    foreach (var frame in DashboardIllustrationTimeline.FocusFrames)
                        _animation.InsertKeyFrame(frame.Progress, frame.X, ease);
                    _animation.Duration = TimeSpan.FromSeconds(DashboardIllustrationTimeline.DurationSeconds);
                    _animation.IterationBehavior = AnimationIterationBehavior.Forever;
                    _visual.StartAnimation("Offset.X", _animation);
                    _controller = _visual.TryGetAnimationController("Offset.X") ?? throw new InvalidOperationException("Animation controller unavailable.");
                    _playing = true;
                }
                catch { _failed = true; ReleaseAnimation(); return; }
            }
            if (play == _playing) return;
            if (play) _controller?.Resume();
            else _controller?.Pause();
            _playing = play;
        }

        private void ViewportChanged(FrameworkElement sender, EffectiveViewportChangedEventArgs args) { _viewport = args.EffectiveViewport; Update(); }
        private void Activated(object sender, WindowActivatedEventArgs args) { _active = args.WindowActivationState != WindowActivationState.Deactivated; Update(); }
        private void WindowChanged(AppWindow sender, AppWindowChangedEventArgs args) => Update();
        private void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Update();
        private void MotionChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args) => _stage.DispatcherQueue.TryEnqueue(Update);
        private void Unloaded(object sender, RoutedEventArgs args) { Detach(); _viewport = default; }
        private void Closed(object sender, WindowEventArgs args) { _closed = true; Detach(); }

        private void Detach()
        {
            if (_loaded)
            {
                _owner.Activated -= Activated;
                _owner.Closed -= Closed;
                _owner.AppWindow.Changed -= WindowChanged;
                if (_root is not null) _root.Changed -= RootChanged;
                foreach (var observation in _observations)
                    observation.Element.UnregisterPropertyChangedCallback(observation.Property, observation.Token);
                _observations.Clear();
                if (_motionSubscribed) _settings.AnimationsEnabledChanged -= MotionChanged;
            }
            _loaded = _playing = _motionSubscribed = false;
            _root = null;
            ReleaseAnimation();
        }

        private void ReleaseAnimation()
        {
            _visual?.StopAnimation("Offset.X");
            if (_visual is not null) _visual.Offset = Vector3.Zero;
            _controller?.Dispose(); _controller = null;
            _animation?.Dispose(); _animation = null;
            _visual = null;
        }
    }
}

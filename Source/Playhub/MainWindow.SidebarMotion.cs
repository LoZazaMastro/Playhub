using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Windows.System;
using Windows.UI.ViewManagement;

namespace Playhub;

public sealed partial class MainWindow
{
    private static readonly ConditionalWeakTable<NavigationViewItem, SidebarIconMotion> SidebarIconMotions = new();

    private static void AttachSidebarIconMotion(NavigationViewItem item, IconElement icon)
    {
        var motion = new SidebarIconMotion(icon);
        SidebarIconMotions.Add(item, motion);
        icon.Loaded += (_, _) => motion.Initialize();
        icon.SizeChanged += (_, _) => motion.UpdateCenter();
        icon.Unloaded += (_, _) => motion.Reset();
        item.PointerEntered += (_, _) => motion.Animate(true);
        item.PointerExited += (_, _) => motion.Animate(false);
        item.PointerCanceled += (_, _) => motion.Animate(false);
        // Submit the bounce on press, before the later tap/selection work.
        item.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, args) =>
        {
            if (args.GetCurrentPoint(item).Properties.IsLeftButtonPressed)
                motion.Animate(motion.Hovered, bounce: true);
        }), true);
        item.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, args) =>
        {
            if (args.Key is VirtualKey.Enter or VirtualKey.Space)
                motion.Animate(motion.Hovered, bounce: true);
        }), true);
    }

#if PLAYHUB_UI_REVIEW
    private static void AnimateSidebarIconForReview(NavigationViewItem item, bool hovered, bool bounce = false)
    {
        if (SidebarIconMotions.TryGetValue(item, out var motion)) motion.Animate(hovered, bounce);
    }

    private static (bool IsAnimating, bool Hovered, float Scale, int Failures) ReadSidebarIconMotionForReview(NavigationViewItem item)
        => SidebarIconMotions.TryGetValue(item, out var motion)
            ? (motion.IsAnimating, motion.Hovered, motion.Scale, motion.Failures)
            : (false, false, 1, 0);

    private static (int Starts, int Superseded, double LastSubmitMs, double LastCompletionMs, double MaxCompletionMs)
        ReadSidebarIconTimingForReview(NavigationViewItem item)
        => SidebarIconMotions.TryGetValue(item, out var motion)
            ? (motion.Starts, motion.Superseded, motion.LastSubmitMs, motion.LastCompletionMs, motion.MaxCompletionMs)
            : (0, 0, 0, 0, 0);
#endif

    private sealed class SidebarIconMotion
    {
        private readonly IconElement _icon;
        private readonly UISettings _motionSettings = new();
        private readonly Dictionary<(bool Hovered, bool Bounce), Vector3KeyFrameAnimation> _animations = new();
        private Visual? _visual;
        private CubicBezierEasingFunction? _ease;
        private CompositionScopedBatch? _batch;
        public bool Hovered { get; private set; }
        public bool IsAnimating => _batch is not null;
        public float Scale => _visual?.Scale.X ?? 1;
        public int Failures { get; private set; }
#if PLAYHUB_UI_REVIEW
        public int Starts { get; private set; }
        public int Superseded { get; private set; }
        public double LastSubmitMs { get; private set; }
        public double LastCompletionMs { get; private set; }
        public double MaxCompletionMs { get; private set; }
#endif

        public SidebarIconMotion(IconElement icon) => _icon = icon;

        public void Initialize()
        {
            if (_visual is not null) return;
            try
            {
                _visual = ElementCompositionPreview.GetElementVisual(_icon);
                UpdateCenter();
                _ease = _visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1), new Vector2(0.3f, 1));
                foreach (var hovered in new[] { false, true })
                foreach (var bounce in new[] { false, true })
                {
                    var animation = _visual.Compositor.CreateVector3KeyFrameAnimation();
                    // Replacing Scale starts from its compositor value, without a reset to 1.
                    animation.InsertExpressionKeyFrame(0, "this.StartingValue");
                    if (bounce) animation.InsertKeyFrame(110f / 360, new Vector3(1.3f, 1.3f, 1), _ease);
                    var target = hovered ? 1.06f : 1;
                    animation.InsertKeyFrame(1, new Vector3(target, target, 1), _ease);
                    animation.Duration = TimeSpan.FromMilliseconds(bounce ? 360 : 140);
                    _animations[(hovered, bounce)] = animation;
                }
            }
            catch { Failures++; Reset(); }
        }

        public void UpdateCenter()
        {
            if (_visual is not null)
                _visual.CenterPoint = new Vector3((float)_icon.ActualWidth / 2, (float)_icon.ActualHeight / 2, 0);
        }

        public void Animate(bool hovered, bool bounce = false)
        {
            if (Hovered == hovered && !bounce) return;
            Hovered = hovered;
            if (!_icon.IsLoaded) return;
#if PLAYHUB_UI_REVIEW
            var started = Stopwatch.GetTimestamp();
#endif
            try
            {
                if (!_motionSettings.AnimationsEnabled) { Reset(); return; }
                Initialize();
                if (_visual is null) return;
                var previous = _batch;
                _batch = null;
                previous?.Dispose();
#if PLAYHUB_UI_REVIEW
                Starts++;
                if (previous is not null) Superseded++;
#endif
                var batch = _visual.Compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
                _batch = batch;
                var target = hovered ? 1.06f : 1;
                batch.Completed += (_, _) =>
                {
                    if (!ReferenceEquals(_batch, batch)) return;
                    _batch = null;
                    // Disconnect the completed animation; subsequent hovers own one Scale animation.
                    if (_visual is not null) _visual.Scale = new Vector3(target, target, 1);
                    batch.Dispose();
#if PLAYHUB_UI_REVIEW
                    LastCompletionMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    MaxCompletionMs = Math.Max(MaxCompletionMs, LastCompletionMs);
#endif
                };
                _visual.StartAnimation("Scale", _animations[(hovered, bounce)]);
                batch.End();
#if PLAYHUB_UI_REVIEW
                LastSubmitMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
#endif
            }
            catch { Failures++; Reset(); }
        }

        public void Reset()
        {
            var batch = _batch;
            _batch = null;
            batch?.Dispose();
            if (_visual is not null)
            {
                _visual.StopAnimation("Scale");
                _visual.Scale = Vector3.One;
            }
            foreach (var animation in _animations.Values) animation.Dispose();
            _animations.Clear();
            _ease?.Dispose();
            _ease = null;
            _visual = null;
            Hovered = false;
        }
    }
}

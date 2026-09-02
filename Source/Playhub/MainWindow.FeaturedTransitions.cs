using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Playhub.Services;
using System;
using System.Diagnostics;
using System.Numerics;

namespace Playhub;

public sealed partial class MainWindow
{
    private static readonly TimeSpan FeaturedSlideDuration = TimeSpan.FromMilliseconds(480);
    private Action? _completeFeaturedSlide;
    private long _featuredSlideStartedAt;

    private void CompleteFeaturedSlideTransition()
    {
        var complete = _completeFeaturedSlide;
        _completeFeaturedSlide = null;
        try { complete?.Invoke(); }
        finally { _featuredPluginTransitioning = false; }
    }

    private void CompleteFeaturedSlideIfElapsed()
    {
        if (_featuredPluginTransitioning &&
            Stopwatch.GetElapsedTime(_featuredSlideStartedAt) >= FeaturedSlideDuration)
            CompleteFeaturedSlideTransition();
    }

    private void StartFeaturedSlideTransition(FrameworkElement previous, FrameworkElement next, int direction)
    {
        var host = _pluginFeaturedCarouselHost;
        var distance = (float)host.ActualWidth * direction;
        Visual? previousVisual = null;
        Visual? nextVisual = null;
        CompositionScopedBatch? batch = null;
        DispatcherQueueTimer? deadline = null;
        var previousHitTest = previous.IsHitTestVisible;
        var nextHitTest = next.IsHitTestVisible;
        Action? complete = null;

        void OnBatchCompleted(object sender, CompositionBatchCompletedEventArgs args)
        {
            if (ReferenceEquals(_completeFeaturedSlide, complete)) CompleteFeaturedSlideTransition();
        }

        void OnDeadline(DispatcherQueueTimer sender, object args)
        {
            if (ReferenceEquals(_completeFeaturedSlide, complete)) CompleteFeaturedSlideTransition();
        }

        complete = () =>
        {
            if (deadline != null)
            {
                deadline.Stop();
                deadline.Tick -= OnDeadline;
            }
            if (batch != null) batch.Completed -= OnBatchCompleted;
            try
            {
                previousVisual?.StopAnimation("Translation");
                nextVisual?.StopAnimation("Translation");
                previousVisual?.Properties.InsertVector3("Translation", Vector3.Zero);
                nextVisual?.Properties.InsertVector3("Translation", Vector3.Zero);
            }
            finally
            {
                batch?.Dispose();
                previous.IsHitTestVisible = previousHitTest;
                next.IsHitTestVisible = nextHitTest;
                if (ReferenceEquals(previous.Parent, host)) host.Children.Remove(previous);
                if (next.Parent == null) host.Children.Add(next);
            }
        };
        _completeFeaturedSlide = complete;
        _featuredPluginTransitioning = true;
        _featuredSlideStartedAt = Stopwatch.GetTimestamp();

        try
        {
            ElementCompositionPreview.SetIsTranslationEnabled(previous, true);
            ElementCompositionPreview.SetIsTranslationEnabled(next, true);
            previousVisual = ElementCompositionPreview.GetElementVisual(previous);
            nextVisual = ElementCompositionPreview.GetElementVisual(next);
            previousVisual.Properties.InsertVector3("Translation", Vector3.Zero);
            nextVisual.Properties.InsertVector3("Translation", new Vector3(distance, 0, 0));
            previous.IsHitTestVisible = false;
            next.IsHitTestVisible = false;
            host.Children.Add(next);

            var compositor = nextVisual.Compositor;
            using var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.42f, 0), new Vector2(0.58f, 1));
            using var slideOut = compositor.CreateVector3KeyFrameAnimation();
            slideOut.InsertKeyFrame(0, Vector3.Zero);
            slideOut.InsertKeyFrame(1, new Vector3(-distance, 0, 0), ease);
            slideOut.Duration = FeaturedSlideDuration;
            using var slideIn = compositor.CreateVector3KeyFrameAnimation();
            slideIn.InsertKeyFrame(0, new Vector3(distance, 0, 0));
            slideIn.InsertKeyFrame(1, Vector3.Zero, ease);
            slideIn.Duration = FeaturedSlideDuration;

            batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            batch.Completed += OnBatchCompleted;
            try
            {
                previousVisual.StartAnimation("Translation", slideOut);
                nextVisual.StartAnimation("Translation", slideIn);
            }
            finally { batch.End(); }

            // Completion can be lost when XAML detaches a host. Bound the logical
            // transition as well, so neither navigation nor the clock can get stuck.
            deadline = DispatcherQueue.CreateTimer();
            deadline.IsRepeating = false;
            deadline.Interval = FeaturedSlideDuration;
            deadline.Tick += OnDeadline;
            _featuredSlideStartedAt = Stopwatch.GetTimestamp();
            deadline.Start();
        }
        catch (Exception ex)
        {
            CompleteFeaturedSlideTransition();
            Diag.Crash("Featured carousel transition", ex);
        }
    }
}

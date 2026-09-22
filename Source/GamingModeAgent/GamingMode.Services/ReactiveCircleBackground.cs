using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace GamingMode.Services;

internal sealed class ReactiveCircleBackground : FrameworkElement
{
    private readonly Pen[] _pens = new Pen[128];
    private readonly Stopwatch _clock = new();

    internal ReactiveCircleBackground(Color color)
    {
        for (int i = 0; i < _pens.Length; i++)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(8 + i * 1.75), color.R, color.G, color.B)), 1.1);
            pen.Freeze();
            _pens[i] = pen;
        }
        Loaded += (_, _) =>
        {
            _clock.Restart();
            CompositionTarget.Rendering -= RenderFrame;
            if (SystemParameters.ClientAreaAnimation) CompositionTarget.Rendering += RenderFrame;
        };
        Unloaded += (_, _) => { CompositionTarget.Rendering -= RenderFrame; _clock.Stop(); };
    }

    private void RenderFrame(object? sender, EventArgs args) => InvalidateVisual();

    protected override void OnRender(DrawingContext context)
    {
        double width = ActualWidth, height = ActualHeight;
        if (width <= 0 || height <= 0) return;
        double seconds = _clock.Elapsed.TotalSeconds * 2;
        double angle = seconds * Math.PI * 2 / 20;
        double breath = .5 - .5 * Math.Cos(seconds * Math.PI * 2 / 7);
        double cx = width * (.5 + .27 * Math.Cos(angle));
        double cy = height * (.5 + .28 * Math.Sin(angle));
        double spread = .9 + .22 * breath;
        const double pitch = 24;
        for (double y = pitch / 2; y < height; y += pitch)
        for (double x = pitch / 2; x < width; x += pitch)
        {
            double dx = (x - cx) / (width * .36 * spread);
            double dy = (y - cy) / (height * .55 * spread);
            double strength = Math.Exp(-3 * (dx * dx + dy * dy)) * (.72 + .28 * breath);
            double radius = .65 + strength * (pitch * .43 - .65);
            context.DrawEllipse(null, _pens[Math.Clamp((int)(strength * 127), 0, 127)], new Point(x, y), radius, radius);
        }
    }
}

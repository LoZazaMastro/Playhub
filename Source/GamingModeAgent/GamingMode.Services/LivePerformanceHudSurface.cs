using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Media;

namespace GamingMode.Services;

internal sealed class LivePerformanceHudSurface : FrameworkElement
{
    private static readonly Brush Black = FrozenBrush(10, 10, 12);
    private static readonly Pen Outline = FrozenPen(Brushes.Black, 2.2);
    private static readonly Typeface Font = new(new FontFamily("Segoe UI, Microsoft YaHei UI, Nirmala UI, Arial"),
        FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private Brush _text = FrozenBrush(245, 245, 247);
    private Pen _graphPen = FrozenPen(FrozenBrush(245, 245, 247), 1.5);
    private readonly LivePerformanceHudHistory _history = new();
    private readonly List<(Geometry Shape, Point At, Brush Fill)> _labels = new(8);
    private LivePerformanceHudSettings _settings = new(false);
    private LivePerformanceHudUsage? _usage;
    private readonly LivePerformanceHudDrawAccounting _drawAccounting = new();
    internal Action? DiagnosticsChanged;
    internal int BufferedPoints => _history.Count;
    internal long Draws => _drawAccounting.Attempted;
    internal long CompletedDraws => _drawAccounting.Completed;
    internal long FailedDraws => _drawAccounting.Failed;
    internal string? LastDrawFailure => _drawAccounting.LastFailure;
    internal double MaximumDrawMicroseconds => _drawAccounting.MaximumMicroseconds;
    internal double AverageDrawMicroseconds => _drawAccounting.AverageMicroseconds;

    internal LivePerformanceHudSurface() { Focusable = false; IsHitTestVisible = false; SnapsToDevicePixels = true; }

    internal void SetData(LivePerformanceHudSettings settings, LiveFrameTelemetry sample, LivePerformanceHudUsage? usage, long now)
    {
        var normalized = settings.Normalized();
        bool configurationChanged = normalized != _settings || usage != _usage;
        var before = _history.Latest;
        _settings = normalized; _usage = usage;
        if (configurationChanged) {
            var color = LivePerformanceHudPalette.Rgb(_settings.TextColor);
            _text = FrozenBrush(color.Red, color.Green, color.Blue); _graphPen = FrozenPen(_text, 1.5);
        }
        _history.Update(sample, now); _history.Expire(now);
        var size = LivePerformanceHudGeometry.Size(_settings.Detail);
        if (Width != size.Width) Width = size.Width;
        if (Height != size.Height) Height = size.Height;
        if (configurationChanged || !ReferenceEquals(before, _history.Latest)) {
            RebuildLabels(); InvalidateVisual();
        }
    }

    internal void Tick(long now)
    {
        bool expired = _history.Expire(now);
        if (expired) RebuildLabels();
        if (expired || (_settings.Detail >= 2 && _history.Count > 0)) InvalidateVisual();
    }

    internal void Clear()
    {
        _history.Clear(); _usage = null; _labels.Clear(); InvalidateVisual();
    }

    private void RebuildLabels()
    {
        _labels.Clear();
        var sample = _history.Latest;
        Label(sample?.PresentedFps is { } fps ? fps.ToString(fps < 10 ? "0.0" : "0", CultureInfo.InvariantCulture) : "—", 30, 12, 6, _text);
        Label("FPS", 12, Width - 34, 22, _text);
        if (_settings.Detail < 2) return;
        Label(sample?.MeanFrameTimeMs is { } milliseconds ? milliseconds.ToString("0.0", CultureInfo.InvariantCulture) + " ms" : "— ms", 14, 12, 42, _text);
        if (_settings.Detail < 3) return;
        Label("CPU " + Percent(_usage?.CpuPercent), 14, 12, 108, _text);
        Label("GPU " + Percent(_usage?.GpuPercent), 14, 140, 108, _text);
        if (_settings.Detail < 4) return;
        Label("RAM  " + Memory(_usage?.UsedMemoryBytes, _usage?.TotalMemoryBytes), 13, 12, 139, _text);
        Label("VRAM " + Memory(_usage?.VideoMemoryBytes, _usage?.TotalVideoMemoryBytes), 13, 12, 160, _text);
    }

    private void Label(string text, double size, double x, double y, Brush color)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Font, size, color, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        Geometry shape = formatted.BuildGeometry(new Point(0, 0)); shape.Freeze();
        _labels.Add((shape, new(x, y), color));
    }

    private static string Percent(double? value) => value is { } number && double.IsFinite(number) && number is >= 0 and <= 100
        ? number.ToString("0", CultureInfo.InvariantCulture) + "%" : "—";
    private static string Memory(long? used, long? total)
    {
        if (used is not >= 0 || total is not > 0 || used > total) return "—";
        const double gb = 1024d * 1024 * 1024;
        return (used.Value / gb).ToString("0.0", CultureInfo.InvariantCulture) + " / "
            + (total.Value / gb).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
    }

    protected override void OnRender(DrawingContext drawing)
    {
        _drawAccounting.Measure(() => {
            if (_settings.OpaqueBackground) drawing.DrawRoundedRectangle(Black, null, new Rect(0, 0, Width, Height), 10, 10);
            foreach (var label in _labels) {
                drawing.PushTransform(new TranslateTransform(label.At.X, label.At.Y));
                // Paint the dark outline underneath the glyph. A combined
                // fill+pen paints its stroke last and blacks out thin text.
                if (!_settings.OpaqueBackground) drawing.DrawGeometry(null, Outline, label.Shape);
                drawing.DrawGeometry(label.Fill, null, label.Shape);
                drawing.Pop();
            }
            if (_settings.Detail >= 2) DrawGraph(drawing, Environment.TickCount64);
        }, DiagnosticsChanged);
    }

    private void DrawGraph(DrawingContext drawing, long now)
    {
        double maximum = 20;
        foreach (var point in _history.Points) maximum = Math.Max(maximum, point.Milliseconds * 1.15);
        var graph = new StreamGeometry();
        bool first = true;
        using (var context = graph.Open()) {
            foreach (var point in _history.Points) {
                double age = now - point.Tick;
                if (age < 0 || age > LivePerformanceHudHistory.DurationMs) continue;
                var position = new Point(12 + (Width - 24) * (1 - age / LivePerformanceHudHistory.DurationMs),
                    96 - 30 * Math.Clamp(point.Milliseconds / maximum, 0, 1));
                if (first) { context.BeginFigure(position, false, false); first = false; }
                else context.LineTo(position, true, false);
            }
        }
        graph.Freeze();
        if (!_settings.OpaqueBackground) drawing.DrawGeometry(null, Outline, graph);
        drawing.DrawGeometry(null, _graphPen, graph);
    }

    private static Brush FrozenBrush(byte red, byte green, byte blue) { var brush = new SolidColorBrush(Color.FromRgb(red, green, blue)); brush.Freeze(); return brush; }
    private static Pen FrozenPen(Brush brush, double width) { var pen = new Pen(brush, width); pen.Freeze(); return pen; }
}

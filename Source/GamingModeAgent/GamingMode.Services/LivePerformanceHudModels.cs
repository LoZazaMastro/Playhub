using System;
using System.Collections.Generic;

namespace GamingMode.Services;

internal sealed record HudNativeDisplayReadback(long ObservedAtTick,uint Phase,bool CleanupComplete,uint Stage,
    uint Error,int Hresult,ulong Generation,string Source,string Destination,ulong Sequence,ulong DwmUpdates,
    bool Band16,bool Disabled,bool HasThumbnail,bool Visible,bool SourceCurrent,string? Failure);


internal enum HudPosition { TopLeft, TopRight, BottomLeft, BottomRight, Center, TopCenter, BottomCenter }

internal sealed record LivePerformanceHudSettings(bool Enabled, int Detail = 1,
    HudPosition Position = HudPosition.TopLeft, bool OpaqueBackground = true, int MarginPixels = 16, string TextColor = "white")
{
    internal LivePerformanceHudSettings Normalized() => this with {
        Detail = Math.Clamp(Detail, 1, 4), MarginPixels = Math.Clamp(MarginPixels, 0, 200),
        Position = Enum.IsDefined(Position) ? Position : HudPosition.TopLeft,
        TextColor = LivePerformanceHudPalette.Valid(TextColor) ? TextColor : "white"
    };
}

internal static class LivePerformanceHudPalette
{
    internal static bool Valid(string? value) => value is "white" or "yellow" or "red" or "green";
    internal static (byte Red, byte Green, byte Blue) Rgb(string value) => value switch {
        "yellow" => (255, 219, 42), "red" => (255, 96, 96), "green" => (110, 235, 140), _ => (245, 245, 247)
    };
}

internal sealed record LivePerformanceHudUsage(double? CpuPercent = null, double? GpuPercent = null,
    long? UsedMemoryBytes = null, long? TotalMemoryBytes = null,
    long? VideoMemoryBytes = null, long? TotalVideoMemoryBytes = null);

internal readonly record struct LivePerformanceHudBounds(int Left, int Top, int Width, int Height)
{
    internal bool Valid => Width > 0 && Height > 0 && Width <= 65536 && Height <= 65536
        && (long)Left + Width <= int.MaxValue && (long)Top + Height <= int.MaxValue;
}

internal readonly record struct LivePerformanceHudPlacement(int Left, int Top, int Width, int Height);
// An internal resolver proves the game's exact AUMID/instance before supplying
// a hosted foreground window. The renderer still verifies both process births.
internal sealed record LivePerformanceHudForegroundProof(nint WindowHandle, int ProcessId, long ProcessBirth);
internal readonly record struct LivePerformanceHudPoint(long Tick, double Milliseconds);

internal static class LivePerformanceHudGeometry
{
    internal static (double Width, double Height) Size(int detail) => detail switch {
        1 => (150, 50), 2 => (240, 106), 3 => (260, 136), _ => (288, 184)
    };

    internal static LivePerformanceHudPlacement Place(LivePerformanceHudBounds bounds,
        LivePerformanceHudSettings settings, double dpi)
    {
        if (!bounds.Valid || !double.IsFinite(dpi) || dpi < 48 || dpi > 768) throw new ArgumentOutOfRangeException(nameof(bounds));
        settings = settings.Normalized();
        var size = Size(settings.Detail);
        int width = Math.Min(bounds.Width, (int)Math.Ceiling(size.Width * dpi / 96));
        int height = Math.Min(bounds.Height, (int)Math.Ceiling(size.Height * dpi / 96));
        int marginX = Math.Min(settings.MarginPixels, (bounds.Width - width) / 2);
        int marginY = Math.Min(settings.MarginPixels, (bounds.Height - height) / 2);
        int left = settings.Position switch {
            HudPosition.TopRight or HudPosition.BottomRight => bounds.Left + bounds.Width - width - marginX,
            HudPosition.Center or HudPosition.TopCenter or HudPosition.BottomCenter => bounds.Left + (bounds.Width - width) / 2,
            _ => bounds.Left + marginX
        };
        int top = settings.Position switch {
            HudPosition.BottomLeft or HudPosition.BottomRight or HudPosition.BottomCenter => bounds.Top + bounds.Height - height - marginY,
            HudPosition.Center => bounds.Top + (bounds.Height - height) / 2,
            _ => bounds.Top + marginY
        };
        return new(left, top, width, height);
    }
}

internal sealed class LivePerformanceHudHistory
{
    internal const int Capacity = 256, DurationMs = 5000;
    private readonly Queue<LivePerformanceHudPoint> _points = new();
    private long _sequence = -1, _lastTick;
    internal IEnumerable<LivePerformanceHudPoint> Points => _points;
    internal int Count => _points.Count;
    internal LiveFrameTelemetry? Latest { get; private set; }

    internal void Update(LiveFrameTelemetry sample, long now)
    {
        if (sample.Status == FrameMeasurementStatus.Busy) return;
        if (sample.Status != FrameMeasurementStatus.Ready || sample.UpdatedAtTick > now
            || now - sample.UpdatedAtTick >= PresentMonLiveMetrics.StaleAfterMs
            || sample.PresentedFps is not > 0 || !double.IsFinite(sample.PresentedFps.Value)
            || sample.MeanFrameTimeMs is not > 0 || !double.IsFinite(sample.MeanFrameTimeMs.Value)) {
            Clear(); return;
        }
        // Target identity changes always start a separate graph.
        if (Latest is not null && (sample.ProcessId != Latest.ProcessId || sample.ProcessBirth != Latest.ProcessBirth)) Clear();
        if (sample.Sequence <= _sequence || sample.UpdatedAtTick < _lastTick) return;
        Latest = sample;
        _sequence = sample.Sequence;
        _lastTick = sample.UpdatedAtTick;
        while (_points.TryPeek(out var first) && (_points.Count >= Capacity || first.Tick < _lastTick - DurationMs)) _points.Dequeue();
        _points.Enqueue(new(sample.UpdatedAtTick, sample.MeanFrameTimeMs.Value));
    }

    internal bool Expire(long now)
    {
        if (Latest is null || now < _lastTick || now - _lastTick >= PresentMonLiveMetrics.StaleAfterMs) {
            bool changed = Latest is not null;
            Clear(); return changed;
        }
        return false;
    }

    internal void Clear() { _points.Clear(); Latest = null; _sequence = -1; _lastTick = 0; }
}

internal readonly record struct LivePerformanceHudDiagnostics(long Draws, double MaximumDrawMicroseconds,
    double AverageDrawMicroseconds, long Published, long Coalesced, int BufferedPoints, bool Visible);

// Immutable, diagnostic facts only. Visibility/draw completion is not proof of
// compositor presentation, foreground ownership, or input authority.
internal sealed record LivePerformanceHudDisplaySnapshot(long ObservedAtTick, string Stage, long WindowInstanceId,
    string WindowHandle, string? WindowClass, uint WindowThreadId, string? ExtendedStyle,
    LivePerformanceHudPlacement? Placement, bool Visible, double Opacity, int SourceProcessId,
    string SourceProcessBirth, long SampleSequence, long AttemptedDraws, long CompletedDraws, long FailedDraws,
    long Published, long Coalesced, int BufferedPoints, double MaximumDrawMicroseconds,
    double AverageDrawMicroseconds, string? LastFailure, HudNativeDisplayReadback? NativeDisplay = null)
{
    internal static readonly LivePerformanceHudDisplaySnapshot Empty = new(0, "NotStarted", 0, "0", null, 0, null,
        null, false, 0, 0, "0", 0, 0, 0, 0, 0, 0, 0, 0, 0, null);
}

internal sealed class LivePerformanceHudSnapshotStore
{
    private LivePerformanceHudDisplaySnapshot _value = LivePerformanceHudDisplaySnapshot.Empty;
    internal LivePerformanceHudDisplaySnapshot Read() => System.Threading.Volatile.Read(ref _value);
    internal void Publish(LivePerformanceHudDisplaySnapshot value) => System.Threading.Volatile.Write(ref _value, value);
}

// Used by the real OnRender path and pure exception-path checks. Diagnostics
// publication must neither replace a drawing exception nor fail a valid draw.
internal sealed class LivePerformanceHudDrawAccounting
{
    private long _attempted, _completed, _failed, _ticks, _maximum;
    private string? _failure;
    internal long Attempted => System.Threading.Interlocked.Read(ref _attempted);
    internal long Completed => System.Threading.Interlocked.Read(ref _completed);
    internal long Failed => System.Threading.Interlocked.Read(ref _failed);
    internal string? LastFailure => System.Threading.Volatile.Read(ref _failure);
    internal double MaximumMicroseconds => System.Threading.Interlocked.Read(ref _maximum) * 1_000_000d / System.Diagnostics.Stopwatch.Frequency;
    internal double AverageMicroseconds => Attempted == 0 ? 0 : System.Threading.Interlocked.Read(ref _ticks) * 1_000_000d / System.Diagnostics.Stopwatch.Frequency / Attempted;
    internal void Measure(Action draw, Action? publish = null)
    {
        long began = System.Diagnostics.Stopwatch.GetTimestamp();
        try { draw(); System.Threading.Interlocked.Increment(ref _completed); }
        catch (Exception error) {
            System.Threading.Volatile.Write(ref _failure, error.GetType().Name);
            System.Threading.Interlocked.Increment(ref _failed); throw;
        } finally {
            long ticks = System.Diagnostics.Stopwatch.GetTimestamp() - began;
            System.Threading.Interlocked.Add(ref _ticks, ticks);
            long previous;
            while (ticks > (previous = System.Threading.Interlocked.Read(ref _maximum))
                && System.Threading.Interlocked.CompareExchange(ref _maximum, ticks, previous) != previous) { }
            System.Threading.Interlocked.Increment(ref _attempted);
            try { publish?.Invoke(); } catch (Exception) { /* Observation cannot alter drawing. */ }
        }
    }
}

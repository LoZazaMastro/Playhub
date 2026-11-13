using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GamingMode.Services;

internal static class LivePerformanceCadence
{
    // Matches the pinned provider's shortest live-output interval. Legacy
    // preferences remain readable, but never add another publication delay.
    internal const int Milliseconds = 20;
    internal static bool Accepted(int milliseconds) => milliseconds is Milliseconds or 50 or 100;
}

internal sealed record LiveFrameTelemetry(FrameMeasurementStatus Status, long Sequence, long UpdatedAtTick,
    double? PresentedFps = null, double? DisplayCadenceFps = null, double? MeanFrameTimeMs = null,
    double? P95FrameTimeMs = null, int Samples = 0, int DisplayedSamples = 0, int RollingWindowMs = 500,
    double ObservationMs = 0, double? LastPresentSeconds = null, long LastArrivalTick = 0,
    int ProcessId = 0, long ProcessBirth = 0);

internal readonly record struct LivePresentSample(int Pid, ulong Swapchain, double TimeSeconds, double PresentedMs, double? DisplayedMs);

internal sealed class PresentMonLiveCsv
{
    private string[]? _columns;
    private int _pid, _chain, _time, _present, _display, _dropped;
    internal bool Malformed { get; private set; }

    internal LivePresentSample? Read(string line, int expectedPid)
    {
        if (line.Length == 0) return null;
        if (line.Length > 16384) { Malformed = true; return null; }
        if (_columns is null)
        {
            _columns = line.TrimStart('\uFEFF').Split(',');
            _pid = Array.IndexOf(_columns, "ProcessID"); _chain = Array.IndexOf(_columns, "SwapChainAddress");
            _time = Array.IndexOf(_columns, "TimeInSeconds"); _present = Array.IndexOf(_columns, "msBetweenPresents");
            _display = Array.IndexOf(_columns, "msBetweenDisplayChange"); _dropped = Array.IndexOf(_columns, "Dropped");
            Malformed = _columns[0] != "Application" || _pid != 1 || _chain != 2 || _time < 0 || _present < 0 || _display < 0 || _dropped < 0;
            return null;
        }
        if (Malformed) return null;
        var values = line.Split(',');
        int offset = values.Length - _columns.Length; // The CLI emits an unquoted application name, including any commas.
        if (offset < 0 || !int.TryParse(values[_pid + offset], NumberStyles.None, CultureInfo.InvariantCulture, out int pid) || pid != expectedPid)
            return null;
        string chain = values[_chain + offset];
        if (!chain.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            || !ulong.TryParse(chain.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong address) || address == 0
            || !Number(values[_time + offset], out double time) || time < 0
            || !Duration(values[_present + offset], out double present)) return null;
        double? display = values[_dropped + offset] == "0" && Duration(values[_display + offset], out double displayed) ? displayed : null;
        return new(pid, address, time, present, display);
    }
    private static bool Number(string text, out double value) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    private static bool Duration(string text, out double value) => Number(text, out value) && value is >= 0.05 and <= 10000;
}

internal sealed class PresentMonLiveMetrics
{
    private const int Capacity = 4096;
    internal const int WindowMs = 500, StaleAfterMs = 500;
    private readonly Queue<LivePresentSample> _samples = new();
    private readonly Dictionary<ulong, double> _lastTimes = new();
    private long _sequence, _arrival;
    private double _latestTime;
    private bool _overflow;

    internal void Add(LivePresentSample sample, long arrival)
    {
        if (sample.TimeSeconds < _latestTime - WindowMs / 1000.0) return;
        if (_lastTimes.TryGetValue(sample.Swapchain, out double last) && sample.TimeSeconds <= last) return;
        // A bounded set of swapchains prevents an unexpected stream from growing metadata forever.
        if (!_lastTimes.ContainsKey(sample.Swapchain) && _lastTimes.Count >= 64) { _overflow = true; return; }
        _lastTimes[sample.Swapchain] = sample.TimeSeconds;
        _latestTime = Math.Max(_latestTime, sample.TimeSeconds);
        while (_samples.TryPeek(out var old) && old.TimeSeconds < _latestTime - WindowMs / 1000.0) _samples.Dequeue();
        if (_samples.Count == Capacity) { _overflow = true; _samples.Dequeue(); }
        _samples.Enqueue(sample); _arrival = arrival; _sequence++;
    }

    internal LiveFrameTelemetry Read(long now, long previousSequence, FrameMeasurementStatus? providerFailure = null)
    {
        LiveFrameTelemetry Empty(FrameMeasurementStatus status) => new(status, _sequence, now,
            LastPresentSeconds: _sequence > 0 ? _latestTime : null, LastArrivalTick: _arrival);
        if (providerFailure is { } failure) return Empty(failure);
        if (_overflow) return Empty(FrameMeasurementStatus.NoData);
        if (_sequence == 0) return Empty(FrameMeasurementStatus.NoData);
        if (now < _arrival || now - _arrival >= StaleAfterMs) return Empty(FrameMeasurementStatus.NoData);
        if (_sequence == previousSequence) return Empty(FrameMeasurementStatus.Busy); // No new frames: do not redraw a cached number.
        var groups = _samples.GroupBy(sample => sample.Swapchain).ToArray();
        var selected = groups.MaxBy(group => group.Count())?.ToArray();
        if (selected is null || selected.Length < 8 || selected.Length * 10 < _samples.Count * 7)
            return Empty(FrameMeasurementStatus.NoData);
        double observation = (selected[^1].TimeSeconds - selected[0].TimeSeconds) * 1000;
        if (observation < 100) return Empty(FrameMeasurementStatus.NoData);
        var times = selected.Select(sample => sample.PresentedMs).Order().ToArray();
        var displays = selected.Select(sample => sample.DisplayedMs).OfType<double>().ToArray();
        double? cadence = displays.Length >= 8 ? Round(1000 / displays.Average()) : null;
        return new(FrameMeasurementStatus.Ready, _sequence, now, Round(1000 / times.Average()), cadence,
            Round(times.Average()), Round(times[(int)Math.Ceiling(times.Length * 0.95) - 1]), selected.Length,
            displays.Length, WindowMs, Round(observation), _latestTime, _arrival);
    }

    private static double Round(double value) => Math.Round(value, 2);
}

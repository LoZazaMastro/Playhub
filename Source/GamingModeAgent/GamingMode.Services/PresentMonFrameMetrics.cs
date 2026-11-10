using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace GamingMode.Services;

internal static class PresentMonFrameMetrics
{
    private const int MinimumSamples = 8;
    private const double MinimumObservationSeconds = 0.75;

    internal static FrameMeasurement Read(string csv, int processId)
    {
        using var lines = new StringReader(csv);
        string? header = lines.ReadLine();
        if (string.IsNullOrWhiteSpace(header)) return new(FrameMeasurementStatus.NoData);
        string[] columns = header.TrimStart('\uFEFF').Split(',');
        int pidColumn = Array.IndexOf(columns, "ProcessID");
        int chainColumn = Array.IndexOf(columns, "SwapChainAddress");
        int droppedColumn = Array.IndexOf(columns, "Dropped");
        int timeColumn = Array.IndexOf(columns, "TimeInSeconds");
        int presentColumn = Array.IndexOf(columns, "msBetweenPresents");
        int displayColumn = Array.IndexOf(columns, "msBetweenDisplayChange");
        if (columns[0] != "Application" || pidColumn != 1 || chainColumn != 2 || droppedColumn < 0 ||
            timeColumn < 0 || presentColumn < 0 || displayColumn < 0) return new(FrameMeasurementStatus.Failed);

        var chains = new Dictionary<ulong, List<Sample>>();
        int count = 0;
        while (lines.ReadLine() is string line)
        {
            if (line.Length == 0) continue;
            string[] fields = line.Split(',');
            // The pinned CLI leaves the first (application name) field unquoted.
            // Numeric fields and the runtime/mode names are comma-free.
            int offset = fields.Length - columns.Length;
            if (offset < 0 ||
                !int.TryParse(fields[pidColumn + offset], NumberStyles.None, CultureInfo.InvariantCulture, out int pid) ||
                pid != processId) continue;
            if (!TryChain(fields[chainColumn + offset], out ulong chain) ||
                !TryNumber(fields[timeColumn + offset], out double time) || time < 0 ||
                !TryDuration(fields[presentColumn + offset], out double presented)) continue;

            double? displayed = fields[droppedColumn + offset] == "0" &&
                TryDuration(fields[displayColumn + offset], out double displayDuration) ? displayDuration : null;
            if (!chains.TryGetValue(chain, out var samples)) chains.Add(chain, samples = new());
            if (samples.Count > 0 && time <= samples[^1].TimeSeconds) continue;
            samples.Add(new(time, presented, displayed));
            count++;
        }

        var selected = chains.Values.MaxBy(samples => samples.Count);
        // The game can own multiple swapchains, including launchers and videos.
        // Only publish a rate when one sustained stream clearly dominates.
        if (selected is null || selected.Count < MinimumSamples || selected.Count * 10 < count * 7 ||
            selected[^1].TimeSeconds - selected[0].TimeSeconds < MinimumObservationSeconds)
            return new(FrameMeasurementStatus.NoData);

        double[] presents = selected.Select(sample => sample.PresentMs).Order().ToArray();
        double[] displays = selected.Select(sample => sample.DisplayMs).OfType<double>().ToArray();
        double? displayFps = displays.Length >= MinimumSamples && displays.Sum() >= MinimumObservationSeconds * 1000
            ? Round(1000 / displays.Average()) : null;
        double median = (presents[(presents.Length - 1) / 2] + presents[presents.Length / 2]) / 2;
        double p95 = presents[(int)Math.Ceiling(presents.Length * 0.95) - 1];
        return new(FrameMeasurementStatus.Ready, Round(1000 / presents.Average()), displayFps,
            Round(median), Round(p95), presents.Length, displays.Length);
    }

    private static bool TryChain(string value, out ulong chain)
    {
        chain = 0;
        return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            ulong.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out chain) && chain != 0;
    }

    private static bool TryNumber(string value, out double number) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number);

    private static bool TryDuration(string value, out double duration) =>
        TryNumber(value, out duration) && duration >= 0.05 && duration <= PresentMonWindowsCapture.CaptureSeconds * 1000;

    private static double Round(double value) => Math.Round(value, 2);

    private readonly record struct Sample(double TimeSeconds, double PresentMs, double? DisplayMs);
}

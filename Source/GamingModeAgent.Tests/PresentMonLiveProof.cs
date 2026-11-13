using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using GamingMode.Services;

internal static class PresentMonLiveProof
{
    internal static int Run(string fixture, string provider, int interval, bool hardwareOffscreen = false)
    {
        fixture = Path.GetFullPath(fixture); provider = Path.GetFullPath(provider);
        if (Path.GetFileName(fixture) != "PlayhubFramePresentFixture.exe" || interval is not (0 or 20 or 50 or 100))
            throw new ArgumentException("Expected the owned hidden WARP fixture and baseline/automatic20 ms interval");
        using var process = Process.Start(new ProcessStartInfo(fixture)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
            WorkingDirectory = Path.GetDirectoryName(fixture)!, Arguments = hardwareOffscreen ? "hardware offscreen" : ""
        }) ?? throw new Exception("Owned renderer fixture did not start");
        Task<string?> readyTask = process.StandardOutput.ReadLineAsync();
        string ready = readyTask.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult() ?? throw new Exception("Fixture readiness missing");
        long birth = process.StartTime.ToFileTimeUtc();
        if (ready != $"READY {process.Id} {birth}") throw new Exception("Owned renderer identity does not match its readiness");
        using var stop = new CancellationTokenSource();
        var values = new ConcurrentQueue<LiveFrameTelemetry>();
        var platform = new ObservedPlatform();
        var collector = new PresentMonLiveCollector(provider, platform, () => Environment.TickCount64);
        using var self = Process.GetCurrentProcess();
        self.Refresh();
        var cpu = self.TotalProcessorTime;
        var timer = Stopwatch.StartNew();
        Task<FrameMeasurementStatus>? capture = interval == 0 ? null : collector.StreamAsync(new(process.Id, birth), interval, values.Enqueue, stop.Token);
        process.StandardInput.WriteLine("go"); process.StandardInput.Close();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        long rendererPeak = 0;
        double providerCpu = 0;
        long providerPeak = 0;
        var bounded = Stopwatch.StartNew();
        while (!process.HasExited && bounded.ElapsedMilliseconds < 6500)
        {
            try { process.Refresh(); rendererPeak = Math.Max(rendererPeak, process.PeakWorkingSet64); }
            catch (InvalidOperationException) { }
            if ((platform.Capture as IPresentMonLiveResources)?.ReadResources() is { } resources)
            {
                providerCpu = Math.Max(providerCpu, resources.CpuMilliseconds);
                providerPeak = Math.Max(providerPeak, resources.WorkingSetBytes);
            }
            Thread.Sleep(100);
        }
        process.WaitForExit(6500);
        if (!process.HasExited)
        {
            stop.Cancel();
            // Only the exact directly-created fixture is ever closed after a timeout.
            if (process.StartTime.ToFileTimeUtc() == birth) process.Kill(entireProcessTree: false);
            throw new Exception("Owned hidden renderer exceeded its lifetime");
        }
        stop.Cancel();
        string remaining = output.GetAwaiter().GetResult();
        FrameMeasurementStatus? status = capture?.GetAwaiter().GetResult();
        self.Refresh();
        var fresh = values.Where(value => value.Status == FrameMeasurementStatus.Ready).ToArray();
        var gaps = fresh.Zip(fresh.Skip(1), (left, right) => right.UpdatedAtTick - left.UpdatedAtTick).ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            mode = interval == 0 ? "baseline" : "live", interval, hiddenWindow = !hardwareOffscreen,
            offscreenWindow = hardwareOffscreen, fixtureArguments = hardwareOffscreen ? "hardware offscreen" : "",
            gameOrSteamTouched = false,
            pid = process.Id, birth, result = status?.ToString(), process.ExitCode, renderer = remaining.Trim(),
            elapsedMs = timer.ElapsedMilliseconds, collectorHostCpuMs = (self.TotalProcessorTime - cpu).TotalMilliseconds,
            collectorHostWorkingSet = self.WorkingSet64, rendererCpuMs = process.TotalProcessorTime.TotalMilliseconds,
            rendererPeakWorkingSet = rendererPeak, freshPublications = fresh.Length,
            providerCpuMs = providerCpu, providerPeakWorkingSet = providerPeak,
            uniqueEventSequences = fresh.Select(value => value.Sequence).Distinct().Count(),
            minPublishGapMs = gaps.Length == 0 ? (long?)null : gaps.Min(), maxPublishGapMs = gaps.Length == 0 ? (long?)null : gaps.Max(),
            medianPublishGapMs = gaps.Length == 0 ? (long?)null : gaps.Order().ElementAt(gaps.Length / 2),
            publications = fresh.Select(value => new { value.Sequence, value.UpdatedAtTick, value.LastArrivalTick, value.LastPresentSeconds }).ToArray(),
            lastSample = fresh.LastOrDefault(), states = values.Select(value => value.Status.ToString()).Distinct().ToArray(),
            providerLines = platform.Lines, providerHeaderAndFirstSamples = platform.First.ToArray()
        }));
        return 0;
    }

    private sealed class ObservedPlatform : IPresentMonLivePlatform
    {
        private readonly PresentMonLiveWindowsCapture _inner = new();
        internal int Lines;
        internal ConcurrentQueue<string> First = new();
        internal IPresentMonCapture? Capture;
        public IDisposable? PinExecutable(string path) => _inner.PinExecutable(path);
        public IPresentMonTargetLease? OpenTarget(PresentMonTarget target, out FrameMeasurementStatus status) => _inner.OpenTarget(target, out status);
        public TraceStopResult Stop(string session) => _inner.Stop(session);
        public IPresentMonCapture Start(string path, int pid, string session, Action<string> line) => Capture = _inner.Start(path, pid, session, text =>
        {
            Interlocked.Increment(ref Lines);
            if (First.Count < 4) First.Enqueue(text.Length > 450 ? text[..450] : text);
            line(text);
        });
    }
}

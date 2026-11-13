using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using GamingMode.Services;

internal static class PresentMonLiveTests
{
    internal const string Header = "Application,ProcessID,SwapChainAddress,Dropped,TimeInSeconds,msBetweenPresents,msBetweenDisplayChange";
    internal static string Row(int pid, double time, ulong chain = 1, double present = 16.666667, bool dropped = false) =>
        FormattableString.Invariant($"fixture.exe,{pid},0x{chain:X},{(dropped ? 1 : 0)},{time:F9},{present:F9},16.666667");

    internal static void VerifyMetrics()
    {
        var csv = new PresentMonLiveCsv();
        var metrics = new PresentMonLiveMetrics();
        Require(csv.Read(Header, 42) is null && !csv.Malformed, "Pinned v1 CSV header accepted once");
        for (int frame = 0; frame < 31; frame++)
        {
            if (csv.Read(Row(42, frame / 60.0), 42) is { } sample) metrics.Add(sample, 1000);
            Require(csv.Read(Row(43, frame / 60.0), 42) is null, "Another game or companion PID never contributes a frame");
        }
        var value = metrics.Read(1100, 0);
        Require(value is { Status: FrameMeasurementStatus.Ready, PresentedFps: 60, DisplayCadenceFps: 60, RollingWindowMs: 500 }
            && value.Sequence == 31 && value.LastArrivalTick == 1000 && value.LastPresentSeconds == 0.5,
            "Fresh FPS derives from new original-game Presents with timestamp and event count");
        Require(metrics.Read(1150, value.Sequence).Status == FrameMeasurementStatus.Busy, "A faster render tick cannot republish old frame data");
        Require(metrics.Read(1500, value.Sequence) is { Status: FrameMeasurementStatus.NoData, PresentedFps: null, LastArrivalTick: 1000 },
            "500 ms without incoming Presents clears the displayed measurement");
        for (int frame = 31; frame < 65; frame++) if (csv.Read(Row(42, frame / 60.0, present: 8.333333), 42) is { } sample) metrics.Add(sample, 1600);
        var changed = metrics.Read(1650, value.Sequence);
        Require(changed.PresentedFps == 120 && changed.Sequence > value.Sequence && changed.Samples <= 31,
            "The bounded rolling window replaces older slow frames rather than retaining a whole-session average");
        metrics.Add(new(42, 1, 0.3, 999, null), 1700);
        Require(metrics.Read(1700, changed.Sequence).Status == FrameMeasurementStatus.Busy, "Out-of-order and duplicate ETW timestamps cannot become fresh data");
        var mixed = new PresentMonLiveMetrics();
        for (int frame = 0; frame < 30; frame++) mixed.Add(new(42, (ulong)(frame % 2 + 1), frame / 60.0, 16.666667, null), 1000);
        Require(mixed.Read(1100, 0).PresentedFps is null, "Ambiguous equal swapchains refuse an invented game FPS");
        Require(metrics.Read(1700, 0, FrameMeasurementStatus.NoData).PresentedFps is null, "Provider event loss clears all last-known metric values");
        var overflow = new PresentMonLiveMetrics();
        for (int index = 1; index <= 65; index++) overflow.Add(new(42, (ulong)index, index / 1000.0, 1, null), 1000);
        Require(overflow.Read(1100, 0).PresentedFps is null, "Unexpected swapchain growth remains bounded and fails closed");
        var invalid = new PresentMonLiveCsv(); invalid.Read("unknown,columns", 42);
        Require(invalid.Malformed, "An upstream CSV schema change is reported instead of silently misreading columns");
        var comma = new PresentMonLiveCsv(); comma.Read(Header, 42);
        Require(comma.Read("game,with,commas.exe,42,0x1,0,0.2,16.6,16.6", 42)?.Pid == 42,
            "An original application's comma-containing name cannot shift the exact PID filter");
    }

    internal static void VerifyStreaming() => Streaming().GetAwaiter().GetResult();
    private static async Task Streaming()
    {
        var platform = new Platform();
        var collector = new PresentMonLiveCollector("pinned-presentmon", platform, () => Environment.TickCount64);
        var values = new ConcurrentQueue<LiveFrameTelemetry>();
        using var stop = new CancellationTokenSource();
        var task = collector.StreamAsync(new(42, 100), 20, values.Enqueue, stop.Token);
        Require(platform.Starts == 1, "One HUD subscription starts one continuous provider");
        Require(await collector.StreamAsync(new(43, 200), 100, _ => { }, CancellationToken.None) == FrameMeasurementStatus.Busy && platform.Starts == 1,
            "Concurrent callers cannot create a duplicate ETW capture");
        Emit(platform.Capture!, 42, 0, 31);
        await Until(() => values.Any(value => value.Status == FrameMeasurementStatus.Ready));
        await Task.Delay(175);
        Require(values.Count(value => value.Status == FrameMeasurementStatus.Ready) == 1,
            "20 ms publisher emits a sample only once when the provider has delivered no new frames");
        Emit(platform.Capture!, 42, 31, 3, header: false);
        await Until(() => values.Count(value => value.Status == FrameMeasurementStatus.Ready) == 2);
        Require(values.Where(value => value.Status == FrameMeasurementStatus.Ready).All(value => value.ProcessId == 42 && value.ProcessBirth == 100),
            "Every publication carries the exact retained original process and birth");
        await Until(() => values.Last().Status == FrameMeasurementStatus.NoData, 1300);
        Require(values.Last().PresentedFps is null, "A paused or stalled provider clears stale FPS while keeping the same session");
        stop.Cancel();
        Require(await task == FrameMeasurementStatus.Canceled && platform.Stops >= 1 && platform.Capture!.HasExited
            && platform.Game.Disposed && platform.PinsReleased == 1 && values.Last().PresentedFps is null,
            "HUD off cancels capture, stops its exact ETW session and clears data before releasing the process and image leases");
        Require(platform.Starts == 1, "Changing display ticks never restarts PresentMon");

        // A real timer with a private deterministic source establishes that the
        // old 50/100ms requests no longer throttle publication. Broad timing
        // bounds tolerate scheduler contention; sequence checks prove freshness.
        foreach (int legacy in new[] { 20, 50, 100 }) {
            var quick = new Platform();
            var fresh = new ConcurrentQueue<LiveFrameTelemetry>();
            using var ending = new CancellationTokenSource();
            var running = new PresentMonLiveCollector("pinned", quick, () => Environment.TickCount64)
                .StreamAsync(new(42, 100), legacy, fresh.Enqueue, ending.Token);
            Emit(quick.Capture!, 42, 0, 31);
            await Until(() => fresh.Any(value => value.Status == FrameMeasurementStatus.Ready));
            for (int batch = 0; batch < 12; batch++) {
                Emit(quick.Capture!, 42, 31 + batch * 2, 2, header: false);
                await Task.Delay(25);
            }
            var published = fresh.Where(value => value.Status == FrameMeasurementStatus.Ready).ToArray();
            Require(published.Length >= 6 && published.Select(value => value.Sequence).Distinct().Count() == published.Length,
                "Automatic 20ms cadence publishes fresh batches instead of legacy 100ms batching or duplicate FPS");
            Require(published.Zip(published.Skip(1), (left, right) => right.UpdatedAtTick - left.UpdatedAtTick).Any(gap => gap < 45),
                "The collector can deliver actual fresh measurements sooner than the former minimum 50ms");
            ending.Cancel(); await running;
            Require(quick.Starts == 1 && quick.Capture!.HasExited, "Fast publication keeps a single provider and owns its cleanup");
        }

        var exit = new Platform(); var exitedValues = new ConcurrentQueue<LiveFrameTelemetry>();
        var exiting = new PresentMonLiveCollector("pinned", exit, () => Environment.TickCount64)
            .StreamAsync(new(42, 100), 100, exitedValues.Enqueue, CancellationToken.None);
        exit.Game.Exit();
        Require(await exiting == FrameMeasurementStatus.TargetUnavailable && exitedValues.Last().PresentedFps is null,
            "Exact target exit ends capture and cannot follow a recycled PID");
        var lost = new Platform();
        var losing = new PresentMonLiveCollector("pinned", lost, () => Environment.TickCount64)
            .StreamAsync(new(42, 100), 50, _ => { }, CancellationToken.None);
        lost.Capture!.Failure = FrameMeasurementStatus.NoData;
        Require(await losing == FrameMeasurementStatus.NoData && lost.Capture.HasExited,
            "ETW loss stops a live session before publishing further unreliable FPS");
        var collision = new Platform();
        var colliding = new PresentMonLiveCollector("pinned", collision, () => Environment.TickCount64)
            .StreamAsync(new(42, 100), 50, _ => { }, CancellationToken.None);
        collision.Capture!.Complete(6, "already running. Use --stop_existing_session");
        Require(await colliding == FrameMeasurementStatus.Failed && collision.Stops == 0,
            "An existing external logger is never stopped by failed startup cleanup");

        var missing = new Platform { Pin = false };
        Require(await new PresentMonLiveCollector("wrong binary", missing, () => 1).StreamAsync(new(42, 100), 100, _ => { }, CancellationToken.None)
            == FrameMeasurementStatus.ProviderUnavailable && missing.Starts == 0, "Missing pinned provider fails before creating any collector process");
        var denied = new Platform { Available = false };
        Require(await new PresentMonLiveCollector("pinned", denied, () => 1).StreamAsync(new(42, 101), 100, _ => { }, CancellationToken.None)
            == FrameMeasurementStatus.TargetUnavailable && denied.Starts == 0, "A wrong target birth fails before telemetry starts");
        string[] args = PresentMonLiveWindowsCapture.Arguments(42, "owned-session");
        Require(args.Contains("--process_id") && args.Contains("42") && args.Contains("--output_stdout")
            && args.Contains("--live_output_ms") && args.Contains("20")
            && args.Contains("--terminate_on_proc_exit") && !args.Contains("--timed") && !args.Contains("--restart_as_admin")
            && !args.Contains("--stop_existing_session"), "Live provider is continuous, PID scoped and never changes privileges or another logger");

        var lines = new List<string>();
        Require(!await PresentMonLiveWindowsCapture.PumpLines(new StringReader("one\r\ntwo\n"), lines.Add, CancellationToken.None)
            && lines.SequenceEqual(new[] { "one", "two" }), "Incremental pipe processing publishes complete lines without waiting for process exit");
        int invalid = 0;
        Require(await PresentMonLiveWindowsCapture.PumpLines(new StringReader(new string('x', 17000) + "\n"), _ => throw new Exception(), CancellationToken.None, () => invalid++)
            && invalid == 1, "Unexpected pipe line growth is bounded and reported once");
    }

    private static void Emit(Capture capture, int pid, int first, int count, bool header = true)
    {
        if (header) capture.Emit(Header);
        for (int index = first; index < first + count; index++) capture.Emit(Row(pid, index / 60.0));
    }
    private static async Task Until(Func<bool> condition, int timeout = 1000)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition()) { if (clock.ElapsedMilliseconds > timeout) throw new Exception("Live publication did not reach its bounded condition"); await Task.Delay(10); }
    }
    private sealed class Platform : IPresentMonLivePlatform
    {
        internal bool Pin = true, Available = true;
        internal int Starts, Stops, PinsReleased;
        internal Target Game = new();
        internal Capture? Capture;
        public IDisposable? PinExecutable(string path) => Pin ? new Release(() => PinsReleased++) : null;
        public IPresentMonTargetLease? OpenTarget(PresentMonTarget target, out FrameMeasurementStatus status)
        { status = FrameMeasurementStatus.TargetUnavailable; return Available ? Game : null; }
        public IPresentMonCapture Start(string path, int pid, string session, Action<string> line) { Starts++; return Capture = new(line); }
        public TraceStopResult Stop(string session) { Stops++; Capture?.Complete(0, ""); return TraceStopResult.Stopped; }
    }
    private sealed class Target : IPresentMonTargetLease
    {
        private readonly TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HasExited => _exit.Task.IsCompleted;
        public Task Exited => _exit.Task;
        internal bool Disposed;
        internal void Exit() => _exit.TrySetResult();
        public void Dispose() => Disposed = true;
    }
    private sealed class Capture(Action<string> line) : IPresentMonCapture, IPresentMonLiveHealth
    {
        private readonly TaskCompletionSource<PresentMonCaptureOutput> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HasExited => _exit.Task.IsCompleted;
        public Task<PresentMonCaptureOutput> Completion => _exit.Task;
        public FrameMeasurementStatus? Failure { get; set; }
        internal void Emit(string text) => line(text);
        internal void Complete(int code, string error) => _exit.TrySetResult(new(code, "", error, false));
        public void Kill() => Complete(-1, "");
        public void Dispose() { }
    }
    private sealed class Release(Action action) : IDisposable { public void Dispose() => action(); }
    private static void Require(bool value, string name) { if (!value) throw new InvalidOperationException(name); }
}

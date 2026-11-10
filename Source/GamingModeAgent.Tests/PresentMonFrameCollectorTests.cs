using GamingMode.Services;
using System.Diagnostics;
using System.Globalization;
using System.Text;

internal static class PresentMonFrameCollectorTests
{
    private const int Pid = 1234;
    private static readonly PresentMonTarget Target = new(Pid, 50);
    private const string Header = "Application,ProcessID,SwapChainAddress,Runtime,SyncInterval,PresentFlags,Dropped," +
        "TimeInSeconds,msInPresentAPI,msBetweenPresents,AllowsTearing,PresentMode,msUntilRenderComplete," +
        "msUntilDisplayed,msBetweenDisplayChange,msFlipDelay,msUntilRenderStart,msGPUActive";

    internal static void VerifyNativeIdentity()
    {
        using var self = Process.GetCurrentProcess();
        var platform = new PresentMonWindowsCapture();
        var target = new PresentMonTarget(self.Id, self.StartTime.ToFileTimeUtc());
        using var lease = platform.OpenTarget(target, out _);
        Check(lease is { HasExited: false }, "live exact birth should open");
        using var recycled = platform.OpenTarget(target with { CreationFileTimeUtc = target.CreationFileTimeUtc - 1 }, out var status);
        Check(recycled is null && status == FrameMeasurementStatus.TargetUnavailable, "different birth should be rejected");
        Check(platform.Stop("PresentMon") == TraceStopResult.Failed, "foreign ETW names must be refused without a stop");
    }

    internal static void VerifyMetrics()
    {
        var csv = new StringBuilder(Header).AppendLine();
        Add(csv, Pid, "0xAA", 60);
        Add(csv, Pid, "0xBB", 8);
        Add(csv, Pid + 1, "0xCC", 110);
        var result = PresentMonFrameMetrics.Read(csv.ToString(), Pid);
        Check(result is { Status: FrameMeasurementStatus.Ready, PresentedFps: 60, DisplayCadenceFps: 30,
            MedianFrameTimeMs: 16.67, P95FrameTimeMs: 16.67, Samples: 60, DisplayedSamples: 30 },
            "select a single dominant swapchain and keep display drops separate from presentations");
        var noDisplay = new StringBuilder(Header).AppendLine();
        Add(noDisplay, Pid, "0xAA", 60, display: false);
        Check(PresentMonFrameMetrics.Read(noDisplay.ToString(), Pid) is
            { Status: FrameMeasurementStatus.Ready, PresentedFps: 60, DisplayCadenceFps: null, DisplayedSamples: 0 },
            "missing display measurements should stay null");
    }

    internal static void VerifyUnreliableMetrics()
    {
        var ambiguous = new StringBuilder(Header).AppendLine();
        Add(ambiguous, Pid, "0xAA", 60);
        Add(ambiguous, Pid, "0xBB", 60);
        Expect(FrameMeasurementStatus.NoData, PresentMonFrameMetrics.Read(ambiguous.ToString(), Pid));
        var sparse = new StringBuilder(Header).AppendLine();
        Add(sparse, Pid, "0xAA", 10);
        Expect(FrameMeasurementStatus.NoData, PresentMonFrameMetrics.Read(sparse.ToString(), Pid));
        var duplicate = new StringBuilder(Header).AppendLine();
        Add(duplicate, Pid, "0xAA", 60, sameTimestamp: true);
        Expect(FrameMeasurementStatus.NoData, PresentMonFrameMetrics.Read(duplicate.ToString(), Pid));
        Expect(FrameMeasurementStatus.NoData, PresentMonFrameMetrics.Read(string.Empty, Pid));
        Expect(FrameMeasurementStatus.Failed, PresentMonFrameMetrics.Read("Application,ProcessID,FrameTime\n", Pid));
    }

    internal static void VerifyCancellation()
    {
        var platform = new Platform();
        var collector = Collector(platform);
        using var cancel = new CancellationTokenSource();
        var pending = collector.CaptureAsync(Target, cancel.Token);
        Expect(FrameMeasurementStatus.Busy, collector.CaptureAsync(Target, CancellationToken.None).GetAwaiter().GetResult());
        platform.StopBehavior = _ =>
        {
            Check(!platform.TargetLease.Disposed && !platform.PinLease.Disposed, "capture leases stay alive through trace cleanup");
            platform.Child.Complete(0);
            return TraceStopResult.Stopped;
        };
        cancel.Cancel();
        Expect(FrameMeasurementStatus.Canceled, pending.GetAwaiter().GetResult());
        Check(platform.Started.Count == 1 && platform.Child.Kills == 0 &&
            platform.Stopped.SequenceEqual(new[] { platform.Started[0], platform.Started[0] }),
            "only the owned session is stopped, before killing the capture process");
        Check(platform.TargetLease.Disposed && platform.PinLease.Disposed && platform.Child.Disposed,
            "native leases should cover capture and cleanup, then close");
    }

    internal static void VerifyTargetExitAndWatchdog()
    {
        var platform = new Platform();
        var collector = Collector(platform);
        var pending = collector.CaptureAsync(Target, CancellationToken.None);
        platform.TargetLease.End();
        Expect(FrameMeasurementStatus.TargetUnavailable, pending.GetAwaiter().GetResult());
        Check(platform.Events.SequenceEqual(new[] { "start", "stop", "stop", "kill", "stop" }),
            "two named stop attempts precede a forced exit, followed by final trace cleanup");

        platform = new Platform();
        collector = Collector(platform, TimeSpan.Zero);
        Expect(FrameMeasurementStatus.TimedOut, collector.CaptureAsync(Target, CancellationToken.None).GetAwaiter().GetResult());
        Check(platform.Child.Kills == 1 && platform.Stopped.All(session => session == platform.Started.Single()),
            "watchdog is bounded and retries only its own trace");
    }

    internal static void VerifyCleanup()
    {
        var platform = new Platform { CompleteOnStart = true, StopBehavior = _ => TraceStopResult.Failed };
        var collector = Collector(platform);
        Expect(FrameMeasurementStatus.CleanupFailed, collector.CaptureAsync(Target, CancellationToken.None).GetAwaiter().GetResult());
        Expect(FrameMeasurementStatus.CleanupFailed, collector.CaptureAsync(Target, CancellationToken.None).GetAwaiter().GetResult());
        Check(platform.Started.Count == 1 && platform.Stopped.Distinct().Count() == 1, "failed cleanup blocks new captures");
        platform.StopBehavior = _ => TraceStopResult.NotFound;
        Expect(FrameMeasurementStatus.NoData, collector.CaptureAsync(Target, CancellationToken.None).GetAwaiter().GetResult());
        Check(platform.Started.Count == 2 && platform.Stopped[^2] == platform.Started[0] &&
            platform.Stopped[^1] == platform.Started[1], "the next capture starts only after previous exact cleanup");
    }

    internal static void VerifyFailures()
    {
        var platform = new Platform { OpenFailure = FrameMeasurementStatus.PermissionDenied };
        Expect(FrameMeasurementStatus.PermissionDenied, Collector(platform).CaptureAsync(Target, CancellationToken.None).GetAwaiter().GetResult());
        Check(platform.Started.Count == 0, "a denied target must never start PresentMon");
        platform = new Platform { MissingProvider = true };
        Expect(FrameMeasurementStatus.ProviderUnavailable, Collector(platform).CaptureAsync(Target, CancellationToken.None).GetAwaiter().GetResult());
        Check(platform.Started.Count == 0, "unverified executable must never launch");
        platform = new Platform { CompleteOnStart = true, ExitCode = 6,
            Error = "error: failed to start trace session: access denied." };
        Expect(FrameMeasurementStatus.PermissionDenied, Collector(platform).CaptureAsync(Target, CancellationToken.None).GetAwaiter().GetResult());
        Check(platform.Stopped.Count == 0, "a trace never acquired after permission denial should not be stopped");
        platform = new Platform { CompleteOnStart = true, ExitCode = 6,
            Error = "error: a trace session named \"fixture\" is already running. Use --stop_existing_session" };
        Expect(FrameMeasurementStatus.Failed, Collector(platform).CaptureAsync(Target, CancellationToken.None).GetAwaiter().GetResult());
        Check(platform.Stopped.Count == 0, "provider collision must not stop the other trace");
        var csv = new StringBuilder(Header).AppendLine();
        Add(csv, Pid, "0xAA", 60);
        platform = new Platform { CompleteOnStart = true, Csv = csv.ToString(), Error = "warning: 12 ETW events were lost." };
        Expect(FrameMeasurementStatus.NoData, Collector(platform).CaptureAsync(Target, CancellationToken.None).GetAwaiter().GetResult());
        Check(platform.Stopped.Count == 1, "lossy telemetry still requires trace cleanup");
    }

    private static PresentMonFrameCollector Collector(Platform platform, TimeSpan? watchdog = null) =>
        new("fixture", platform, watchdog ?? TimeSpan.FromSeconds(2), TimeSpan.Zero);

    private static void Add(StringBuilder csv, int pid, string chain, int count, bool display = true, bool sameTimestamp = false)
    {
        for (int index = 0; index < count; index++)
        {
            bool displayed = display && index % 2 == 0;
            double time = sameTimestamp ? 0 : index / 60d;
            csv.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"Example, Game.exe,{pid},{chain},DXGI,0,0,{(displayed ? 0 : 1)},{time},0,16.6667,0,Composed: Flip,0,0,{(displayed ? "33.3333" : "NA")},0,0,0"));
        }
    }

    private static void Expect(FrameMeasurementStatus status, FrameMeasurement value) =>
        Check(value.Status == status, $"expected {status}, received {value.Status}");

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Platform : IPresentMonPlatform
    {
        public readonly List<string> Started = new(), Stopped = new(), Events = new();
        public bool CompleteOnStart, MissingProvider;
        public int ExitCode;
        public string Error = string.Empty, Csv = string.Empty;
        public FrameMeasurementStatus? OpenFailure;
        public Func<string, TraceStopResult>? StopBehavior;
        public TargetLease TargetLease = new();
        public Lease PinLease = new();
        public ChildCapture Child = null!;

        public IDisposable? PinExecutable(string path) => MissingProvider ? null : PinLease = new();

        public IPresentMonTargetLease? OpenTarget(PresentMonTarget target, out FrameMeasurementStatus status)
        {
            status = OpenFailure ?? FrameMeasurementStatus.TargetUnavailable;
            return OpenFailure is null ? TargetLease = new() : null;
        }

        public IPresentMonCapture Start(string path, int processId, string session)
        {
            Check(processId == Pid && session.StartsWith(PresentMonWindowsCapture.SessionPrefix, StringComparison.Ordinal),
                "only the validated target and generated trace name may launch");
            Started.Add(session);
            Events.Add("start");
            Child = new(Events);
            if (CompleteOnStart) Child.Complete(ExitCode, Error, Csv);
            return Child;
        }

        public TraceStopResult Stop(string session)
        {
            Check(Started.Contains(session), "trace ownership stays with this collector");
            Stopped.Add(session);
            Events.Add("stop");
            return StopBehavior?.Invoke(session) ?? TraceStopResult.NotFound;
        }
    }

    private class Lease : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class TargetLease : Lease, IPresentMonTargetLease
    {
        private readonly TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HasExited => Exited.IsCompleted;
        public Task Exited => _exit.Task;
        public void End() => _exit.TrySetResult();
    }

    private sealed class ChildCapture(List<string> events) : Lease, IPresentMonCapture
    {
        private readonly TaskCompletionSource<PresentMonCaptureOutput> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HasExited => Completion.IsCompleted;
        public int Kills { get; private set; }
        public Task<PresentMonCaptureOutput> Completion => _done.Task;
        public void Complete(int code, string error = "", string csv = "") => _done.TrySetResult(new(code, csv, error, false));
        public void Kill() { events.Add("kill"); Kills++; Complete(-1); }
    }
}

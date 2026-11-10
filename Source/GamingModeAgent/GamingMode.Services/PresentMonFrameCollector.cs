using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GamingMode.Services;

internal readonly record struct PresentMonTarget(int ProcessId, long CreationFileTimeUtc);

internal enum FrameMeasurementStatus
{
    Ready,
    Busy,
    TargetUnavailable,
    PermissionDenied,
    ProviderUnavailable,
    NoData,
    Canceled,
    TimedOut,
    Failed,
    CleanupFailed
}

internal sealed record FrameMeasurement(
    FrameMeasurementStatus Status,
    double? PresentedFps = null,
    double? DisplayCadenceFps = null,
    double? MedianFrameTimeMs = null,
    double? P95FrameTimeMs = null,
    int Samples = 0,
    int DisplayedSamples = 0);

internal sealed class PresentMonFrameCollector
{
    private readonly string _executablePath;
    private readonly IPresentMonPlatform _platform;
    private readonly TimeSpan _watchdog;
    private readonly TimeSpan _exitGrace;
    private readonly SemaphoreSlim _singleFlight = new(1);
    private string? _pendingCleanup;
    private bool _previousCaptureMayStillBeRunning;

    internal PresentMonFrameCollector(string executablePath)
        : this(executablePath, new PresentMonWindowsCapture(), TimeSpan.FromSeconds(8), TimeSpan.FromMilliseconds(750)) { }

    internal PresentMonFrameCollector(string executablePath, IPresentMonPlatform platform, TimeSpan watchdog, TimeSpan exitGrace)
    {
        _executablePath = executablePath;
        _platform = platform;
        _watchdog = watchdog;
        _exitGrace = exitGrace;
    }

    internal async Task<FrameMeasurement> CaptureAsync(PresentMonTarget target, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return new(FrameMeasurementStatus.Canceled);
        if (!_singleFlight.Wait(0)) return new(FrameMeasurementStatus.Busy);
        try
        {
            if (_previousCaptureMayStillBeRunning) return new(FrameMeasurementStatus.CleanupFailed);
            if (_pendingCleanup is string prior)
            {
                if (Stop(prior) == TraceStopResult.Failed) return new(FrameMeasurementStatus.CleanupFailed);
                _pendingCleanup = null;
            }

            using var observed = _platform.OpenTarget(target, out var targetStatus);
            if (observed is null) return new(targetStatus);
            using var executable = _platform.PinExecutable(_executablePath);
            if (executable is null) return new(FrameMeasurementStatus.ProviderUnavailable);
            if (cancellationToken.IsCancellationRequested) return new(FrameMeasurementStatus.Canceled);
            if (observed.HasExited) return new(FrameMeasurementStatus.TargetUnavailable);

            string session = PresentMonWindowsCapture.SessionPrefix + Guid.NewGuid().ToString("N");
            using var capture = _platform.Start(_executablePath, target.ProcessId, session);
            FrameMeasurement result;
            try
            {
                result = await ObserveAsync(capture, observed, target.ProcessId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or Win32Exception)
            {
                result = new(FrameMeasurementStatus.Failed);
            }
            finally
            {
                // An already-owned name must be left alone. On startup access
                // denial the pinned CLI either failed to create the logger or
                // shut it down while unwinding provider/consumer setup.
                if (!capture.Completion.IsCompletedSuccessfully ||
                    capture.Completion.Result is not { NameCollision: true } and not { AccessDenied: true })
                {
                    if (!await FinishAsync(capture, session).ConfigureAwait(false))
                    {
                        _pendingCleanup = session;
                        _previousCaptureMayStillBeRunning = !capture.HasExited;
                    }
                }
            }
            if (_pendingCleanup is not null) return new(FrameMeasurementStatus.CleanupFailed);
            if (cancellationToken.IsCancellationRequested) return new(FrameMeasurementStatus.Canceled);
            if (observed.HasExited) return new(FrameMeasurementStatus.TargetUnavailable);
            return result;
        }
        catch (Exception error) when (error is UnauthorizedAccessException || error is Win32Exception { NativeErrorCode: 5 })
        {
            return new(FrameMeasurementStatus.PermissionDenied);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or Win32Exception or ArgumentException)
        {
            return new(FrameMeasurementStatus.Failed);
        }
        finally { _singleFlight.Release(); }
    }

    private async Task<FrameMeasurement> ObserveAsync(IPresentMonCapture capture, IPresentMonTargetLease target,
        int processId, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_watchdog);
        var timeout = Task.Delay(Timeout.InfiniteTimeSpan, deadline.Token);
        var finished = await Task.WhenAny(capture.Completion, target.Exited, timeout).ConfigureAwait(false);
        await deadline.CancelAsync().ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested) return new(FrameMeasurementStatus.Canceled);
        if (target.HasExited || finished == target.Exited) return new(FrameMeasurementStatus.TargetUnavailable);
        if (finished != capture.Completion) return new(FrameMeasurementStatus.TimedOut);

        var output = await capture.Completion.ConfigureAwait(false);
        if (output.AccessDenied) return new(FrameMeasurementStatus.PermissionDenied);
        if (output.ExitCode != 0 || output.Truncated) return new(FrameMeasurementStatus.Failed);
        if (output.LostEvents) return new(FrameMeasurementStatus.NoData);
        return PresentMonFrameMetrics.Read(output.StandardOutput, processId);
    }

    private async Task<bool> FinishAsync(IPresentMonCapture capture, string session)
    {
        if (capture.HasExited) return Stop(session) != TraceStopResult.Failed;

        // Stop ETW before stopping its consumer. A killed consumer can leave
        // the logger running. Retry after process exit to cover early cancellation
        // when the process has started but has not created its session yet.
        Stop(session);
        await WaitForExitAsync(capture).ConfigureAwait(false);
        if (!capture.HasExited)
        {
            Stop(session);
            await WaitForExitAsync(capture).ConfigureAwait(false);
        }
        if (!capture.HasExited)
        {
            try { capture.Kill(); }
            catch (Exception error) when (error is InvalidOperationException or Win32Exception) { }
            await WaitForExitAsync(capture).ConfigureAwait(false);
        }
        return Stop(session) != TraceStopResult.Failed && capture.HasExited;
    }

    private async Task WaitForExitAsync(IPresentMonCapture capture)
    {
        if (capture.HasExited) return;
        await Task.WhenAny(capture.Completion, Task.Delay(_exitGrace)).ConfigureAwait(false);
    }

    private TraceStopResult Stop(string session)
    {
        try { return _platform.Stop(session); }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            return TraceStopResult.Failed;
        }
    }
}

internal enum TraceStopResult { NotFound, Stopped, Failed }

internal sealed record PresentMonCaptureOutput(int ExitCode, string StandardOutput, string StandardError, bool Truncated)
{
    internal bool AccessDenied => ExitCode == 6 && StandardError.Contains(
        "failed to start trace session: access denied", StringComparison.OrdinalIgnoreCase);
    internal bool NameCollision => ExitCode == 6 && StandardError.Contains(
        "already running. Use --stop_existing_session", StringComparison.OrdinalIgnoreCase);
    internal bool LostEvents => StandardError.Contains("ETW events were lost", StringComparison.OrdinalIgnoreCase) ||
        StandardError.Contains("ETW buffers were lost", StringComparison.OrdinalIgnoreCase) ||
        StandardError.Contains("overflowed present events detected", StringComparison.OrdinalIgnoreCase);
}

internal interface IPresentMonPlatform
{
    IDisposable? PinExecutable(string path);
    IPresentMonTargetLease? OpenTarget(PresentMonTarget target, out FrameMeasurementStatus status);
    IPresentMonCapture Start(string path, int processId, string session);
    TraceStopResult Stop(string session);
}

internal interface IPresentMonTargetLease : IDisposable
{
    bool HasExited { get; }
    Task Exited { get; }
}

internal interface IPresentMonCapture : IDisposable
{
    bool HasExited { get; }
    Task<PresentMonCaptureOutput> Completion { get; }
    void Kill();
}

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GamingMode.Services;

internal interface IPresentMonLivePlatform
{
    IDisposable? PinExecutable(string path);
    IPresentMonTargetLease? OpenTarget(PresentMonTarget target, out FrameMeasurementStatus status);
    IPresentMonCapture Start(string path, int pid, string session, Action<string> line);
    TraceStopResult Stop(string session);
}
internal interface IPresentMonLiveHealth { FrameMeasurementStatus? Failure { get; } }
internal readonly record struct LiveCollectorResources(int ProcessId, long ProcessBirth, double CpuMilliseconds, long WorkingSetBytes);
internal interface IPresentMonLiveResources { LiveCollectorResources? ReadResources(); }

// Owned by an explicit HUD subscription. One process and one ETW session stay
// alive for that exact game; there is no CLI relaunch on a display tick.
internal sealed class PresentMonLiveCollector
{
    private readonly string _executable;
    private readonly IPresentMonLivePlatform _platform;
    private readonly Func<long> _clock;
    private readonly SemaphoreSlim _single = new(1);
    private string? _cleanup;
    private bool _consumerUncertain;

    internal PresentMonLiveCollector(string executable)
        : this(executable, new PresentMonLiveWindowsCapture(), () => Environment.TickCount64) { }
    internal PresentMonLiveCollector(string executable, IPresentMonLivePlatform platform, Func<long> clock) =>
        (_executable, _platform, _clock) = (executable, platform, clock);

    internal async Task<FrameMeasurementStatus> StreamAsync(PresentMonTarget target, int refreshMs,
        Action<LiveFrameTelemetry> publish, CancellationToken cancellation)
    {
        var result = await RunAsync(target, refreshMs,
            value => publish(value with { ProcessId = target.ProcessId, ProcessBirth = target.CreationFileTimeUtc }), cancellation).ConfigureAwait(false);
        publish(new(result, 0, _clock(), ProcessId: target.ProcessId, ProcessBirth: target.CreationFileTimeUtc));
        return result;
    }

    private async Task<FrameMeasurementStatus> RunAsync(PresentMonTarget target, int refreshMs,
        Action<LiveFrameTelemetry> publish, CancellationToken cancellation)
    {
        if (!LivePerformanceCadence.Accepted(refreshMs)) throw new ArgumentOutOfRangeException(nameof(refreshMs));
        refreshMs = LivePerformanceCadence.Milliseconds;
        if (cancellation.IsCancellationRequested) return FrameMeasurementStatus.Canceled;
        if (!_single.Wait(0)) return FrameMeasurementStatus.Busy;
        FrameMeasurementStatus terminal = FrameMeasurementStatus.Failed;
        IPresentMonCapture? capture = null;
        IPresentMonTargetLease? game = null;
        IDisposable? image = null;
        string? session = null;
        using var subscription = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        Task? refresh = null;
        object sync = new();
        var metrics = new PresentMonLiveMetrics();
        var csv = new PresentMonLiveCsv();
        try
        {
            if (_consumerUncertain) return FrameMeasurementStatus.CleanupFailed;
            if (_cleanup is { } prior)
            {
                if (Stop(prior) == TraceStopResult.Failed) return FrameMeasurementStatus.CleanupFailed;
                _cleanup = null;
            }
            game = _platform.OpenTarget(target, out var status);
            if (game is null) return status;
            image = _platform.PinExecutable(_executable);
            if (image is null) return FrameMeasurementStatus.ProviderUnavailable;
            if (game.HasExited) return FrameMeasurementStatus.TargetUnavailable;
            session = PresentMonWindowsCapture.SessionPrefix + Guid.NewGuid().ToString("N");
            capture = _platform.Start(_executable, target.ProcessId, session, line =>
            {
                lock (sync) if (csv.Read(line, target.ProcessId) is { } sample) metrics.Add(sample, _clock());
            });
            refresh = RefreshAsync(game, capture, publish, refreshMs, metrics, csv, sync, subscription.Token);
            var stopped = Task.Delay(Timeout.InfiniteTimeSpan, subscription.Token);
            Task finished = await Task.WhenAny(capture.Completion, game.Exited, refresh, stopped).ConfigureAwait(false);
            terminal = cancellation.IsCancellationRequested ? FrameMeasurementStatus.Canceled
                : game.HasExited || finished == game.Exited ? FrameMeasurementStatus.TargetUnavailable
                : finished == refresh ? (capture as IPresentMonLiveHealth)?.Failure ?? FrameMeasurementStatus.Failed : FrameMeasurementStatus.NoData;
            if (finished == capture.Completion)
            {
                var output = await capture.Completion.ConfigureAwait(false);
                terminal = output.AccessDenied ? FrameMeasurementStatus.PermissionDenied
                    : output.LostEvents ? FrameMeasurementStatus.NoData
                    : output.ExitCode == 0 && !output.Truncated ? FrameMeasurementStatus.NoData : FrameMeasurementStatus.Failed;
            }
        }
        catch (Exception error) when (error is UnauthorizedAccessException || error is Win32Exception { NativeErrorCode: 5 })
        { terminal = FrameMeasurementStatus.PermissionDenied; }
        catch (OperationCanceledException) { terminal = FrameMeasurementStatus.Canceled; }
        catch (Exception error) when (error is IOException or InvalidOperationException or Win32Exception or ArgumentException)
        { terminal = FrameMeasurementStatus.Failed; }
        finally
        {
            await subscription.CancelAsync().ConfigureAwait(false);
            if (refresh is not null) { try { await refresh.ConfigureAwait(false); } catch (OperationCanceledException) { } catch (Exception) { terminal = FrameMeasurementStatus.Failed; } }
            if (capture is not null && session is not null)
            {
                bool foreignLogger = capture.Completion.IsCompletedSuccessfully
                    && capture.Completion.Result is { NameCollision: true } or { AccessDenied: true };
                if (!foreignLogger && !await FinishAsync(capture, session).ConfigureAwait(false)) terminal = FrameMeasurementStatus.CleanupFailed;
                capture.Dispose();
            }
            image?.Dispose();
            game?.Dispose();
            _single.Release();
        }
        return terminal;
    }

    private async Task RefreshAsync(IPresentMonTargetLease game, IPresentMonCapture capture, Action<LiveFrameTelemetry> publish, int refreshMs,
        PresentMonLiveMetrics metrics, PresentMonLiveCsv csv, object sync, CancellationToken cancellation)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(refreshMs));
        long previousSequence = 0;
        FrameMeasurementStatus? previousStatus = null;
        while (await timer.WaitForNextTickAsync(cancellation).ConfigureAwait(false))
        {
            if (game.HasExited) return;
            if ((capture as IPresentMonLiveHealth)?.Failure is { } providerFailure)
            {
                publish(new(providerFailure, 0, _clock()));
                return;
            }
            LiveFrameTelemetry value;
            lock (sync) value = metrics.Read(_clock(), previousSequence, csv.Malformed ? FrameMeasurementStatus.Failed : null);
            if (value.Status == FrameMeasurementStatus.Busy || cancellation.IsCancellationRequested || game.HasExited) continue;
            if (value.Status != FrameMeasurementStatus.Ready && previousStatus == value.Status) continue;
            if (value.Status == FrameMeasurementStatus.Ready) previousSequence = value.Sequence;
            previousStatus = value.Status;
            publish(value);
            if (value.Status == FrameMeasurementStatus.Failed) return;
        }
    }

    private async Task<bool> FinishAsync(IPresentMonCapture capture, string session)
    {
        Stop(session);
        for (int attempt = 0; attempt < 2 && !capture.HasExited; attempt++)
        {
            await Task.WhenAny(capture.Completion, Task.Delay(500)).ConfigureAwait(false);
            if (!capture.HasExited) Stop(session);
        }
        if (!capture.HasExited)
        {
            try { capture.Kill(); } catch (Exception error) when (error is InvalidOperationException or Win32Exception) { }
            await Task.WhenAny(capture.Completion, Task.Delay(500)).ConfigureAwait(false);
        }
        bool clean = capture.HasExited && Stop(session) != TraceStopResult.Failed;
        if (!clean) { _cleanup = session; _consumerUncertain = !capture.HasExited; }
        return clean;
    }
    private TraceStopResult Stop(string session)
    {
        try { return _platform.Stop(session); }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception) { return TraceStopResult.Failed; }
    }
}

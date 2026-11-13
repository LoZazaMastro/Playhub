using System.IO;

namespace GamingMode.Services;

internal interface ILivePerformanceSource : IDisposable
{
    PresentMonTarget Target { get; }
    bool Current { get; }
    bool TryGetBounds(out LivePerformanceHudBounds bounds);
    LivePerformanceHudForegroundProof? ForegroundProof { get; }
}

// Optional fresh association work belongs to the existing 500ms lifetime loop,
// never the PresentMon sample callback or a UI/foreground getter.
internal interface ILivePerformanceAssociationRefresh
{
    Task<bool> RefreshAssociationAsync(CancellationToken cancellation);
}

internal interface ILivePerformanceRuntime : IDisposable
{
    Task<ILivePerformanceSource?> ResolveAsync(uint appId, CancellationToken cancellation);
    Task<FrameMeasurementStatus> StreamAsync(PresentMonTarget target, int refreshMs,
        Action<LiveFrameTelemetry> publish, CancellationToken cancellation);
    void Configure(LivePerformanceHudSettings settings);
    void Publish(LiveFrameTelemetry sample, LivePerformanceHudUsage? usage, LivePerformanceHudBounds bounds,
        LivePerformanceHudForegroundProof? foreground);
    void Hide();
    LivePerformanceHudUsage? ReadUsage();
}

// Optional copy-only runtime capability; existing fake/alternate runtimes need
// not implement it. No resolution, OS calls or dispatcher work in this getter.
internal interface ILivePerformanceHudDiagnosticsRuntime
{
    LivePerformanceHudDisplaySnapshot CopyHudDiagnostics();
}
internal interface ILivePerformanceHudDisplayRuntime {
    void BindDisplayProof(Func<LivePerformanceHudSubscriptionProof> proof);
}

// Internal only: never returned by an HTTP endpoint. A copied proof remains a
// fact at its observation time, not a reusable native display grant.
internal sealed record LivePerformanceHudSubscriptionProof(string? Lease, uint AppId, long Epoch, long DeadlineTick,
    bool Enabled, bool Suspended, bool LeaseCurrent, bool SourceResolved);
internal sealed record LivePerformanceHudSubscriptionDiagnostics(uint AppId, string Epoch, string DeadlineTick,
    bool Enabled, bool Suspended, bool LeaseCurrent, bool SourceResolved);
internal sealed record LivePerformanceHudReadback(bool Available, LivePerformanceHudSubscriptionDiagnostics Subscription,
    LivePerformanceHudDisplaySnapshot? Surface);

internal sealed record LivePerformanceState(bool Enabled, int Detail, int Corner, bool Transparent, int RefreshMs,
    string Status, uint AppId, string? Lease, long Sequence, long? SampleAgeMs, bool Suspended, string TextColor = "white");
internal sealed record LivePerformanceRequest(string Action, string? Lease = null, uint AppId = 0,
    bool? Enabled = null, int? Detail = null, int? Corner = null, bool? Transparent = null, int? RefreshMs = null,
    bool? Suspended = null, string? TextColor = null);
internal sealed record LivePerformanceResult(bool Ok, LivePerformanceState State, string? Error = null);

// All mutation is serialized. The monitor exists only for a live subscription;
// turning the HUD off stops its provider and removes the monitor as well.
internal sealed class LivePerformanceController : IAsyncDisposable
{
    internal const int LeaseMilliseconds = 6000;
    private readonly SemaphoreSlim _gate = new(1);
    private readonly object _stateGate = new();
    private readonly LivePerformanceSettingsStore _store;
    private readonly ILivePerformanceRuntime _runtime;
    private readonly Func<long> _clock;
    private LivePerformanceOptions _options;
    private string _status = "Off";
    private string? _lease;
    private uint _appId;
    private long _deadline, _sequence, _sampleTick;
    private CancellationTokenSource? _cancellation;
    private Task? _worker;
    private bool _disposed;
    private volatile bool _cleanupFailed;
    private bool _hasSource;
    private bool _suspended;
    private long _epoch;
    private long _usageEpoch;

    internal LivePerformanceController(LivePerformanceSettingsStore store, ILivePerformanceRuntime runtime,
        Func<long>? clock = null)
    {
        (_store, _runtime, _clock) = (store, runtime, clock ?? (() => Environment.TickCount64));
        _options = store.Load();
        _status = _options.Enabled ? "Idle" : "Off";
        if (_runtime is ILivePerformanceHudDisplayRuntime display) display.BindDisplayProof(CopySubscriptionProof);
        _runtime.Configure((_options with { Enabled = false }).Hud);
    }

    internal LivePerformanceState Read()
    {
        lock (_stateGate) {
            bool expired = _lease is not null && _clock() >= _deadline;
            return new(_options.Enabled, _options.Detail, _options.Corner, _options.Transparent,
                _options.RefreshMs, expired ? "LeaseExpired" : _status, expired ? 0 : _appId,
                expired ? null : _lease, expired ? 0 : _sequence,
                expired || _sampleTick == 0 ? null : Math.Max(0, _clock() - _sampleTick), _suspended, _options.TextColor);
        }
    }

    internal LivePerformanceHudSubscriptionProof CopySubscriptionProof()
    {
        lock (_stateGate) {
            long now = _clock();
            bool current = !_disposed && _lease is not null && now < _deadline;
            return new(_lease, _appId, Interlocked.Read(ref _epoch), _deadline, _options.Enabled, _suspended,
                current, _hasSource);
        }
    }

    internal LivePerformanceHudSubscriptionDiagnostics ReadSubscriptionProof()
    {
        var copy = CopySubscriptionProof();
        return new(copy.AppId, copy.Epoch.ToString(System.Globalization.CultureInfo.InvariantCulture),
            copy.DeadlineTick.ToString(System.Globalization.CultureInfo.InvariantCulture), copy.Enabled, copy.Suspended,
            copy.LeaseCurrent, copy.SourceResolved);
    }

    internal LivePerformanceHudReadback ReadHudDiagnostics()
    {
        // Read the immutable producer copy, then the current controller state.
        // A stale/historical surface is deliberately not relabelled Current.
        var diagnostic = _runtime as ILivePerformanceHudDiagnosticsRuntime;
        var copy = diagnostic?.CopyHudDiagnostics();
        return new(diagnostic is not null, ReadSubscriptionProof(), copy);
    }

    internal async Task<LivePerformanceResult> ApplyAsync(LivePerformanceRequest request, CancellationToken cancellation = default)
    {
        await _gate.WaitAsync(cancellation).ConfigureAwait(false);
        try {
            if (_disposed) return Result(false, "Stopped");
            if (request.Action is not ("configure" or "renew" or "stop")) return Result(false, "InvalidRequest");
            if (request.Lease is not null && !Guid.TryParseExact(request.Lease, "N", out _)) return Result(false, "InvalidLease");
            bool expired;
            lock (_stateGate) expired = _lease is not null && _clock() >= _deadline;
            if (expired) {
                await StopWorkerAsync("LeaseExpired").ConfigureAwait(false);
                lock (_stateGate) { _lease = null; _appId = 0; }
                if (request.Action != "configure") return Result(false, "ExpiredLease");
            }
            lock (_stateGate) {
                if (request.Action != "configure" && (_lease is null || request.Lease != _lease)) return Result(false, "InvalidLease");
                if (request.Action == "configure" && _lease is not null && request.Lease != _lease) return Result(false, "Busy");
            }
            if (request.Action == "stop") {
                await StopWorkerAsync("Stopped").ConfigureAwait(false);
                lock (_stateGate) { _lease = null; _appId = 0; }
                return Result(true);
            }
            if (request.Action == "renew") {
                lock (_stateGate) {
                    if (_clock() >= _deadline) return Result(false, "ExpiredLease");
                    _deadline = _clock() + LeaseMilliseconds;
                    if (request.Suspended.HasValue) {
                        bool resumed = _suspended && !request.Suspended.Value;
                        if (_suspended != request.Suspended.Value) Interlocked.Increment(ref _usageEpoch);
                        _suspended = request.Suspended.Value;
                        if (resumed) { _status = "NoData"; _sequence = _sampleTick = 0; }
                    }
                    if (_suspended && _hasSource) _status = "Suspended";
                }
                UpdateRendering();
                if (_suspended) _runtime.Hide();
                if ((_worker is { IsCompleted: true } || !_hasSource) && !_cleanupFailed && _appId != 0) {
                    await StopWorkerAsync("Starting").ConfigureAwait(false);
                    await StartWorkerAsync(cancellation).ConfigureAwait(false);
                }
                return Result(true);
            }

            var value = new LivePerformanceOptions(request.Enabled ?? _options.Enabled, request.Detail ?? _options.Detail,
                request.Corner ?? _options.Corner, request.Transparent ?? _options.Transparent, request.RefreshMs ?? _options.RefreshMs,
                request.TextColor ?? _options.TextColor);
            if (!value.Valid) return Result(false, "InvalidSettings");
            value = value.Normalized();
            if (!_store.Save(value)) return Result(false, "SaveFailed");
            Interlocked.Increment(ref _epoch);
            await StopWorkerAsync(value.Enabled ? "Starting" : "Off").ConfigureAwait(false);
            lock (_stateGate) {
                _options = value; _appId = value.Enabled ? request.AppId : 0;
                _lease = value.Enabled ? _lease ?? Guid.NewGuid().ToString("N") : null;
                _deadline = value.Enabled ? _clock() + LeaseMilliseconds : 0;
                _suspended = value.Enabled && (request.Suspended ?? false);
            }
            UpdateRendering();
            if (value.Enabled) await StartWorkerAsync(cancellation).ConfigureAwait(false);
            return Result(true);
        } finally { _gate.Release(); }
    }

    // Mode changes/suspend revoke the runtime lease without changing the user's
    // preference. Cancellation is immediate; the serialized cleanup follows.
    internal void Invalidate()
    {
        long epoch = Interlocked.Increment(ref _epoch);
        lock (_stateGate) {
            _lease = null; _deadline = 0;
            try { _cancellation?.Cancel(); } catch (ObjectDisposedException) { }
            _runtime.Configure((_options with { Enabled = false }).Hud);
        }
        _runtime.Hide();
        _ = InvalidateAsync(epoch);
    }
    private async Task InvalidateAsync(long epoch)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try {
            if (epoch != Interlocked.Read(ref _epoch) || _disposed) return;
            await StopWorkerAsync("Stopped").ConfigureAwait(false); lock (_stateGate) _appId = 0;
        }
        finally { _gate.Release(); }
    }

    private async Task StartWorkerAsync(CancellationToken requestCancellation)
    {
        if (_cleanupFailed) { SetStatus("CleanupFailed"); return; }
        if (_worker is { IsCompleted: false }) return;
        _cancellation?.Dispose();
        _cancellation = new();
        string? lease;
        uint appId;
        lock (_stateGate) { lease = _lease; appId = _appId; _sequence = _sampleTick = 0; }
        if (lease is null) return;
        ILivePerformanceSource? source = null;
        if (appId != 0) {
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(requestCancellation, _cancellation.Token);
            bounded.CancelAfter(1500);
            try { source = await _runtime.ResolveAsync(appId, bounded.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException
                or System.ComponentModel.Win32Exception or UnauthorizedAccessException) { }
        }
        SetStatus(source is null ? (appId == 0 ? "Idle" : "TargetUnavailable") : "NoData");
        _hasSource = source is not null;
        UpdateRendering();
        var token = _cancellation.Token;
        _worker = Task.Run(() => RunAsync(lease, source, token));
    }

    private async Task RunAsync(string lease, ILivePerformanceSource? source, CancellationToken cancellation)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        UsageSample? usage = null;
        Task<FrameMeasurementStatus>? stream = null;
        Task? usageWorker = null;
        string final = "Stopped";
        long epoch = Interlocked.Read(ref _epoch);
        try {
            if (source is not null) {
                stream = _runtime.StreamAsync(source.Target, _options.RefreshMs, sample => {
                    if (lifetime.IsCancellationRequested) return;
                    lock (_stateGate) {
                        if (_lease != lease || _clock() >= _deadline || epoch != Interlocked.Read(ref _epoch)) return;
                        _status = _suspended ? "Suspended" : sample.Status.ToString();
                        if (sample.Status == FrameMeasurementStatus.Ready) { _sequence = sample.Sequence; _sampleTick = sample.UpdatedAtTick; }
                        else { _sequence = _sampleTick = 0; }
                    }
                    bool hasBounds = source.TryGetBounds(out var bounds);
                    lock (_stateGate) {
                        if (lifetime.IsCancellationRequested || _lease != lease || _clock() >= _deadline
                            || epoch != Interlocked.Read(ref _epoch) || _suspended) return;
                        var hardware = Volatile.Read(ref usage);
                        var currentUsage = hardware is not null && hardware.Epoch == Interlocked.Read(ref _usageEpoch)
                            && _clock() - hardware.Tick is >= 0 and <= 250 ? hardware.Value : null;
                        if (hasBounds) _runtime.Publish(sample, currentUsage, bounds, source.ForegroundProof);
                        else _runtime.Hide();
                    }
                }, lifetime.Token);
                usageWorker = Task.Run(async () => {
                    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
                    while (await timer.WaitForNextTickAsync(lifetime.Token).ConfigureAwait(false)) {
                        bool sampleUsage;
                        lock (_stateGate) sampleUsage = _options.Detail >= 3 && !_suspended && _lease == lease && _clock() < _deadline;
                        if (sampleUsage) {
                            long generation = Interlocked.Read(ref _usageEpoch);
                            var hardware = _runtime is ILivePerformanceTargetUsageRuntime targeted
                                ? targeted.ReadUsage(source.Target.ProcessId) : _runtime.ReadUsage();
                            Volatile.Write(ref usage, hardware is not null && generation == Interlocked.Read(ref _usageEpoch)
                                ? new(hardware, _clock(), generation) : null);
                        } else Volatile.Write(ref usage, null);
                    }
                }, lifetime.Token);
            }
            while (!cancellation.IsCancellationRequested) {
                lock (_stateGate) {
                    if (_lease != lease) break;
                    if (_clock() >= _deadline) { _lease = null; final = "LeaseExpired"; break; }
                }
                if(source is ILivePerformanceAssociationRefresh association
                    &&!await association.RefreshAssociationAsync(lifetime.Token).ConfigureAwait(false))
                {final="TargetUnavailable";break;}
                if (source is not null && !source.Current) { final = "TargetUnavailable"; break; }
                if (stream is { IsCompleted: true }) { final = (await stream.ConfigureAwait(false)).ToString(); break; }
                long remaining;
                lock (_stateGate) remaining = _deadline - _clock();
                await Task.Delay((int)Math.Clamp(remaining, 1, 500), cancellation).ConfigureAwait(false);
            }
        } catch (OperationCanceledException) { }
        catch (Exception error) when (error is InvalidOperationException or IOException or System.ComponentModel.Win32Exception) { final = "Failed"; }
        finally {
            await lifetime.CancelAsync().ConfigureAwait(false);
            if (usageWorker is not null) {
                try { await usageWorker.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (Exception) { Volatile.Write(ref usage, null); }
            }
            if (stream is not null) {
                try { if (await stream.ConfigureAwait(false) == FrameMeasurementStatus.CleanupFailed) { _cleanupFailed = true; final = "CleanupFailed"; } }
                catch (Exception) { _cleanupFailed = true; final = "CleanupFailed"; }
            }
            source?.Dispose();
            lock (_stateGate) _runtime.Configure((_options with { Enabled = false }).Hud);
            _runtime.Hide();
            lock (_stateGate) { _status = final; _sequence = _sampleTick = 0; }
        }
    }

    private async Task StopWorkerAsync(string status)
    {
        lock (_stateGate) _runtime.Configure((_options with { Enabled = false }).Hud);
        _cancellation?.Cancel(); _runtime.Hide();
        if (_worker is not null) await _worker.ConfigureAwait(false);
        _worker = null; _cancellation?.Dispose(); _cancellation = null;
        _hasSource = false;
        SetStatus(_cleanupFailed ? "CleanupFailed" : status);
    }
    private void UpdateRendering()
    {
        lock (_stateGate) _runtime.Configure((_options with { Enabled = _options.Enabled && _lease is not null && !_suspended }).Hud);
    }
    private void SetStatus(string value) { lock (_stateGate) _status = value; }
    private sealed record UsageSample(LivePerformanceHudUsage Value, long Tick, long Epoch);
    private LivePerformanceResult Result(bool ok, string? error = null) => new(ok, Read(), error);
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try {
            if (_disposed) return;
            _disposed = true;
            await StopWorkerAsync("Stopped").ConfigureAwait(false);
            lock (_stateGate) { _lease = null; _appId = 0; }
            _runtime.Dispose();
        } finally { _gate.Release(); }
    }
}

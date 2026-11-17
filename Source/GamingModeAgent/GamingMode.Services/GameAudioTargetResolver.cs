using System.Diagnostics;
using System.IO;

namespace GamingMode.Services;

internal sealed class GameAudioLaunchLease(GameAudioTarget target, Func<bool> current, Action dispose) : IDisposable
{
    int disposed;
    internal GameAudioTarget Target { get; } = target;
    internal bool Current => Volatile.Read(ref disposed) == 0 && current() && Volatile.Read(ref disposed) == 0;
    public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) dispose(); }
}
internal sealed record GameAudioResolution(string Status, GameAudioLaunchLease? Lease = null);

// Audio-only borrowing of existing authenticated launch facts. Never changes
// the overlay, foreground, game input or any launch association.
internal sealed class GameAudioTargetResolver
{
    internal const int ResolutionLimitMs = 1000;
    readonly SemaphoreSlim scanning = new(1, 1);
    readonly Func<uint, CancellationToken, Task<IReadOnlyList<GameAudioLaunchLease>>> find;
    readonly CancellationToken stopping;
    internal GameAudioTargetResolver(GamingWindowFocusService focus, CancellationToken stopping)
        : this((appId, stop) => FindAuthenticated(appId, focus, stop), stopping) { }
    internal GameAudioTargetResolver(Func<uint, CancellationToken, Task<IReadOnlyList<GameAudioLaunchLease>>> find, CancellationToken stopping = default)
    { this.find = find; this.stopping = stopping; }

    internal async Task<GameAudioResolution> ResolveAsync(uint appId, CancellationToken request)
    {
        if (appId == 0 || request.IsCancellationRequested || stopping.IsCancellationRequested) return new("Unavailable");
        if (!scanning.Wait(0)) return new("Resolving");
        var stop = CancellationTokenSource.CreateLinkedTokenSource(request, stopping);
        stop.CancelAfter(ResolutionLimitMs);
        Task<GameAudioResolution> worker;
        try { worker = Task.Run(() => ResolveOwned(appId, stop)); }
        catch { stop.Dispose(); scanning.Release(); throw; }
        try { return await worker.WaitAsync(TimeSpan.FromMilliseconds(ResolutionLimitMs), request).ConfigureAwait(false); }
        catch (Exception e) when (e is TimeoutException or OperationCanceledException)
        {
            // A blocking OS metadata read may outlive the caller. It retains
            // the scan gate until it truly ends, and its result is disposed.
            _ = worker.ContinueWith(done => { if (done.Status == TaskStatus.RanToCompletion) done.Result.Lease?.Dispose(); },
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return new("Resolving");
        }
    }
    async Task<GameAudioResolution> ResolveOwned(uint appId, CancellationTokenSource stop)
    {
        IReadOnlyList<GameAudioLaunchLease> leases = [];
        GameAudioLaunchLease? selected = null;
        try
        {
            leases = await find(appId, stop.Token).ConfigureAwait(false);
            if (stop.IsCancellationRequested || leases.Count > 4) return new("Unavailable");
            var current = leases.Where(value => value.Target.ProcessId > 0 && value.Target.CreationFileTimeUtc > 0 && value.Current).ToArray();
            if (current.Length != 1) return new(current.Length > 1 ? "Ambiguous" : "Unavailable");
            if (!current[0].Current || stop.IsCancellationRequested) return new("Unavailable");
            selected = current[0];
            return new("Ready", selected);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
            or OperationCanceledException or System.ComponentModel.Win32Exception or System.Security.SecurityException)
        { return new("Unavailable"); }
        finally
        {
            foreach (var lease in leases) if (!ReferenceEquals(lease, selected)) lease.Dispose();
            stop.Dispose(); scanning.Release();
        }
    }
    static async Task<IReadOnlyList<GameAudioLaunchLease>> FindAuthenticated(uint appId, GamingWindowFocusService focus, CancellationToken stop)
    {
        var result = new List<GameAudioLaunchLease>();
        try
        {
            // Preserve the existing exact QAM source when present. It remains
            // one option for audio identity, never a prerequisite for audio.
            try
            {
                if (focus.TryGetQamSource(out nint window, out uint pid))
                {
                    using var process = Process.GetProcessById(checked((int)pid));
                    long birth = process.StartTime.ToFileTimeUtc();
                    var legacy = Capture(pid, birth, () => focus.TryGetQamSource(out nint current, out uint currentPid)
                        && current == window && currentPid == pid
                        && (OverlaySteamArtworkResolver.IsTrackedAppProcess((int)pid, appId)
                            || UwpSteamSessionAssociation.Find(pid, appId) is not null), () => { });
                    if (legacy is not null) return [legacy];
                }
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
            var win32 = await Win32SteamSessionAssociation.FindLeaseAsync(appId, stop).ConfigureAwait(false);
            if (win32 is not null)
            {
                var identity = win32.Identity;
                var lease = Capture(identity.GamePid, identity.GameBirth, () => win32.Current, win32.Dispose);
                if (lease is not null) result.Add(lease);
            }
            stop.ThrowIfCancellationRequested();
            foreach (var identity in UwpSteamSessionAssociation.FindForApp(appId))
            {
                stop.ThrowIfCancellationRequested();
                var lease = Capture(identity.GamePid, identity.GameBirth,
                    () => UwpSteamSessionAssociation.Find(identity.GamePid, appId) == identity, () => { });
                if (lease is not null) result.Add(lease);
                if (result.Count > 4) break;
            }
            stop.ThrowIfCancellationRequested();
            return result;
        }
        catch { foreach (var lease in result) lease.Dispose(); throw; }
    }
    static GameAudioLaunchLease? Capture(uint pid, long birth, Func<bool> proofCurrent, Action release)
    {
        Process? process = null;
        try
        {
            if (pid == 0 || pid > int.MaxValue || birth <= 0 || !proofCurrent()) { release(); return null; }
            process = Process.GetProcessById((int)pid);
            _ = process.Handle; // Retain this actual process, not a later PID lookup.
            using var owner = Process.GetCurrentProcess();
            var retained = process;
            bool Current() => !retained.HasExited && retained.StartTime.ToFileTimeUtc() == birth
                && proofCurrent() && !retained.HasExited && retained.StartTime.ToFileTimeUtc() == birth;
            if (process.SessionId != owner.SessionId || !Current()) { process.Dispose(); release(); return null; }
            process = null;
            return new(new((int)pid, birth), Current, () => { retained.Dispose(); release(); });
        }
        catch { process?.Dispose(); release(); throw; }
    }
}

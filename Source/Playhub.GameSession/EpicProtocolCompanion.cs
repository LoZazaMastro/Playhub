using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Playhub.Shared;

namespace Playhub.GameSession;

// Optional post-dispatch work. It cannot prevent or repeat the registered URI.
internal sealed class EpicProtocolCompanion : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly Task worker;
    private EpicObservedCandidate[] candidates = Array.Empty<EpicObservedCandidate>();
    private int revoked, disposed;
    internal EpicProtocolCompanion(EpicInstalledLaunch launch, long startedAt, Action<EpicLaunchPayload>? onPayload = null)
        => worker = Task.Run(() => Run(launch, startedAt, onPayload));
    internal void Observe(IReadOnlyList<EpicObservedCandidate> values)
    {
        if (values.Count > 128) { Volatile.Write(ref revoked, 1); return; }
        if (Volatile.Read(ref disposed) == 0 && Volatile.Read(ref revoked) == 0)
            Volatile.Write(ref candidates, values.ToArray());
    }
    internal static bool CandidateMatches(EpicInstalledLaunch launch, EpicLaunchPayload payload, EpicObservedCandidate value, long startedAt) =>
        value.Pid != 0 && value.Birth >= startedAt && value.Window != 0
        && value.Image.Equals(payload.Executable, StringComparison.OrdinalIgnoreCase)
        && Path.GetFullPath(value.Image).StartsWith(Path.TrimEndingDirectorySeparator(launch.InstallDirectory) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    private void Run(EpicInstalledLaunch launch, long startedAt, Action<EpicLaunchPayload>? onPayload)
    {
        try
        {
            var payload = EpicLaunchPayloadResolver.Resolve(launch);
            if (payload is null || stop.IsCancellationRequested) return;
            onPayload?.Invoke(payload); // observation only; no launch or ownership grant
            long deadline = checked(Environment.TickCount64 + EpicProtocolSession.StartupBudgetMs);
            while (!stop.IsCancellationRequested && Volatile.Read(ref revoked) == 0 && Environment.TickCount64 < deadline)
            {
                var eligible = Volatile.Read(ref candidates).Where(v => CandidateMatches(launch, payload, v, startedAt)).Take(2).ToArray();
                if (eligible.Length > 1) { Volatile.Write(ref revoked, 1); return; }
                if (eligible.Length == 0) { if (stop.Token.WaitHandle.WaitOne(100)) return; continue; }
                var value = eligible[0];
                using var wrapper = Process.GetCurrentProcess();
                using var provenance = EpicLaunchWindows.Capture(launch, (uint)wrapper.Id, wrapper.StartTime.ToFileTimeUtc());
                if (provenance is null || stop.IsCancellationRequested || Volatile.Read(ref revoked) != 0) return;
                var latest = Volatile.Read(ref candidates).Where(v => CandidateMatches(launch, payload, v, startedAt)
                    && v.Pid == value.Pid && v.Birth == value.Birth).Take(2).ToArray();
                if (latest.Length != 1) return;
                value = latest[0];
                var proof = provenance.CreateProvenance(value.Pid, value.Birth, value.Window, startedAt, 0);
                if (proof is null || !provenance.Current || stop.IsCancellationRequested || Volatile.Read(ref revoked) != 0) return;
                using var game = OpenProcess(0x101000, false, value.Pid);
                if (game.IsInvalid || GetProcessId(game) != value.Pid || WaitForSingleObject(game, 0) != 258
                    || !GetProcessTimes(game, out long birth, out _, out _, out _) || birth != value.Birth) return;
                bool Current() => !stop.IsCancellationRequested && Volatile.Read(ref revoked) == 0 && provenance.RetainedCurrent;
                using var companion = Win32CompanionLease.TryStart(game, value.Pid, value.Image, proof, Current);
                if (companion is null) return;
                UwpSessionLog.Write("Epic protocol game has a separately retained launch provenance; Steam observation was not synthesized.");
                while (Current() && WaitForSingleObject(game, 0) == 258)
                    if (stop.Token.WaitHandle.WaitOne(100)) return;
                return;
            }
        }
        catch (Exception error) { UwpSessionLog.Write("Epic optional association unavailable: " + error.GetType().Name + "."); }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stop.Cancel();
        // No termination: the exact game and Epic launch remain independent.
        if (worker.Wait(1500)) stop.Dispose();
        else _ = worker.ContinueWith(_ => stop.Dispose(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    [DllImport("kernel32.dll")] private static extern SafeFileHandle OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern uint GetProcessId(SafeFileHandle process);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(SafeFileHandle process, uint milliseconds);
    [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(SafeFileHandle process, out long birth, out long exit, out long kernel, out long user);
}

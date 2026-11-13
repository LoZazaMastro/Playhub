using Playhub.Shared;
using System.ComponentModel;

namespace Playhub.GameSession;

internal enum EpicObservedState { Alive, Exited, Unknown }
internal interface IEpicObservedProcess : IDisposable
{
    uint Pid { get; }
    long Birth { get; }
    string Image { get; }
    EpicObservedState State { get; }
    bool IsPayload { get; }
    long PresentationWindow { get; }
}
internal interface IEpicProtocolRuntime
{
    long Tick { get; }
    long FileTime { get; }
    IReadOnlyList<IEpicObservedProcess> Snapshot(EpicInstalledLaunch launch);
    void Dispatch(string uri);
    void Delay(int milliseconds);
    void Log(string message);
}
internal sealed record EpicObservedCandidate(uint Pid, long Birth, string Image, long Window);
internal interface IEpicProtocolBindingRuntime
{
    bool SnapshotComplete { get; }
    void AfterDispatch(long startedAt, bool beforeComplete);
    void ObserveCandidates(IReadOnlyList<EpicObservedCandidate> candidates);
}
internal static class EpicProtocolSession
{
    internal const int StartupBudgetMs = 180000, PollMs = 250, BootstrapGapMs = 30000;
    internal static int Run(EpicInstalledLaunch launch, IEpicProtocolRuntime runtime)
    {
        // Observational lifetime only: none of these handles is published as
        // custom game ownership. A separately verified provenance may do that.
        var retained = new Dictionary<(uint, long), IEpicObservedProcess>();
        bool sawProcess = false, sawPayload = false, observationReported = false; long start = runtime.Tick, startedAt, lastAlive = start;
        IReadOnlyList<IEpicObservedProcess> ReadSnapshot(out bool complete)
        {
            try { var value = runtime.Snapshot(launch); complete = runtime is not IEpicProtocolBindingRuntime binding || binding.SnapshotComplete; return value; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
            {
                complete = false;
                if (!observationReported) { observationReported = true; runtime.Log("Epic process observation unavailable; protocol launch remains independent."); }
                return Array.Empty<IEpicObservedProcess>();
            }
        }
        try
        {
            var before = ReadSnapshot(out bool beforeComplete);
            try { if (before.Any(p => p.IsPayload && p.State != EpicObservedState.Exited)) throw new InvalidOperationException("This Epic game is already running."); }
            finally { foreach (var process in before) process.Dispose(); }
            startedAt = runtime.FileTime;
            runtime.Dispatch(launch.LaunchUri);
            if (runtime is IEpicProtocolBindingRuntime binding)
                try { binding.AfterDispatch(startedAt, beforeComplete); }
                catch (Exception) { runtime.Log("Epic optional association unavailable after normal protocol dispatch."); }
            runtime.Log("Epic protocol launch requested; no launcher process was adopted as the game.");
            while (true)
            {
                long now = runtime.Tick;
                if (now < start) throw new InvalidOperationException("Launch clock changed.");
                foreach (var pair in retained.Where(pair => pair.Value.State == EpicObservedState.Exited).ToArray())
                { retained.Remove(pair.Key); pair.Value.Dispose(); }
                if (now - start < StartupBudgetMs)
                {
                    var fresh = ReadSnapshot(out _);
                    try
                    {
                        foreach (var process in fresh)
                        {
                            if (process.Birth <= startedAt || process.State == EpicObservedState.Exited || !WithinInstall(launch.InstallDirectory, process.Image)) continue;
                            var key = (process.Pid, process.Birth);
                            if (retained.TryGetValue(key, out var prior))
                            {
                                // A post-dispatch metadata resolver may identify
                                // an already observed handle's payload role.
                                if (!prior.IsPayload && process.IsPayload) { retained[key] = process; prior.Dispose(); }
                                continue;
                            }
                            retained.Add(key, process); sawProcess = true;
                        }
                    }
                    finally { foreach (var process in fresh) if (!retained.Values.Any(value => ReferenceEquals(value, process))) process.Dispose(); }
                }
                if (runtime is IEpicProtocolBindingRuntime activeBinding)
                    try
                    {
                        var candidates = retained.Values.Where(p => p.State == EpicObservedState.Alive)
                            .Select(p => new EpicObservedCandidate(p.Pid, p.Birth, p.Image, p.PresentationWindow)).Where(p => p.Window != 0).Take(129).ToArray();
                        activeBinding.ObserveCandidates(candidates);
                    }
                    catch (Exception) { runtime.Log("Epic optional process binding was not confirmed."); }
                if (retained.Values.Any(p => p.IsPayload && p.State == EpicObservedState.Alive && p.PresentationWindow != 0)) sawPayload = true;
                if (sawPayload && !retained.Values.Any(p => p.IsPayload && p.State != EpicObservedState.Exited))
                { runtime.Log("Epic observed payload exited; no launcher was used as the game identity."); return 0; }
                if (retained.Count != 0) lastAlive = now;
                else if (now - start >= StartupBudgetMs && (!sawProcess || now - lastAlive >= BootstrapGapMs))
                {
                    runtime.Log(sawProcess ? "Epic observed install processes exited; protocol session ended."
                        : "Epic protocol startup wait ended without an observed install process; authentication/startup was not confirmed.");
                    return sawProcess ? 0 : 2;
                }
                runtime.Delay(PollMs);
            }
        }
        finally { foreach (var process in retained.Values) process.Dispose(); }
    }
    internal static IReadOnlyList<string> ObservationImages(EpicInstalledLaunch launch, Func<string, IEnumerable<string>> enumerate)
    {
        var images = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { launch.GameExecutable };
        try
        {
            foreach (string directory in new[] { launch.InstallDirectory, Path.GetDirectoryName(launch.GameExecutable)! }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                int inspected = 0;
                foreach (string path in enumerate(directory))
                {
                    if (++inspected > 128 || images.Count >= 128) break;
                    images.Add(Path.GetFullPath(path));
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            // Optional observation never substitutes or blocks Epic's launch chain.
            images.Clear(); images.Add(launch.GameExecutable);
        }
        return images.ToArray();
    }
    private static bool WithinInstall(string directory, string image)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        return Path.GetFullPath(image).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}

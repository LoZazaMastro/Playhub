namespace Playhub.DeckyHost;

internal sealed record DeckyHealthSample(long AtMilliseconds, long CpuTicks, DeckyProcessTree.Identity Runtime);

// Recovery is deliberately narrower than a slow plugin RPC: an owned runtime
// must spin near one core, have no port listener, and fail three HTTP probes.
internal sealed class DeckyHealthPolicy(long startedAt)
{
    internal const int StartupGraceMilliseconds = 30_000;
    internal const int ProbeIntervalMilliseconds = 2_000;
    private DeckyHealthSample? _previous;
    private int _failures;
    private DeckyHealthSample? _confirmed;
    private DeckyHealthSample? _busySample;

    internal bool ObserveCpu(DeckyHealthSample sample)
    {
        var previous = _previous;
        _previous = sample;
        _confirmed = null;
        long elapsed = sample.AtMilliseconds - (previous?.AtMilliseconds ?? sample.AtMilliseconds);
        bool busy = sample.AtMilliseconds - startedAt >= StartupGraceMilliseconds
            && previous is not null && previous.Runtime == sample.Runtime
            && elapsed >= 1_000 && elapsed <= 6_000
            && sample.CpuTicks >= previous.CpuTicks
            && (sample.CpuTicks - previous.CpuTicks) / (elapsed * 10_000d) >= .75;
        _busySample = busy ? sample : null;
        if (!busy) _failures = 0;
        return busy;
    }

    internal bool ObserveNetwork(DeckyHealthSample sample, bool? listenerAbsent, bool httpFailed)
    {
        if (_busySample != sample || listenerAbsent != true || !httpFailed)
        { _failures = 0; _confirmed = null; return false; }
        if (++_failures < 3) return false;
        _confirmed = sample;
        return true;
    }

    internal bool IsFresh(DeckyProcessTree.Identity runtime, long now) => _confirmed is { } proof
        && proof.Runtime == runtime && now >= proof.AtMilliseconds && now - proof.AtMilliseconds <= 1_500;

    internal void Clear() { _previous = null; _busySample = null; _confirmed = null; _failures = 0; }

    internal static bool CanReset(SteamGenerationIdentity? retained, SteamGenerationIdentity? current,
        SteamGenerationIdentity? attempted, bool steamAlive, bool rootAlive, bool freshFailure, bool stopping)
        => retained is not null && current == retained && attempted != retained
            && steamAlive && rootAlive && freshFailure && !stopping;

    internal static bool SupportsEndpoint(string? host, string? port) => (host is null or "127.0.0.1")
        && (port is null or "1337");
}

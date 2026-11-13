namespace Playhub.DeckyHost;

internal enum SteamGenerationAction { Ignore, Bind, ResetLoader }
internal sealed record SteamGenerationIdentity(int Pid, long Birth, string Path, string Sid, uint Session);

internal static class SteamGenerationPolicy
{
    internal static bool CanReuse(SteamGenerationIdentity retained, int registeredPid, string registeredPath,
        string sid, uint session, bool retainedAlive) => retainedAlive && retained.Pid == registeredPid
        && retained.Sid == sid && retained.Session == session
        && retained.Path.Equals(registeredPath, StringComparison.OrdinalIgnoreCase);

    internal static bool Accept(DeckyProcessTree.Identity? actual, string expectedPath, string sid, uint session) =>
        actual is { Pid: > 0, Birth: > 0 } && actual.Sid == sid && actual.Session == session
        && actual.Path.Equals(expectedPath, StringComparison.OrdinalIgnoreCase);

    internal static SteamGenerationAction Observe(SteamGenerationIdentity? previous, SteamGenerationIdentity? current,
        bool previousExited, bool loaderAlive, bool stopping)
    {
        if (stopping || !loaderAlive || current is null) return SteamGenerationAction.Ignore;
        if (previous is null) return SteamGenerationAction.Bind;
        if (previous == current || !previousExited) return SteamGenerationAction.Ignore;
        return SteamGenerationAction.ResetLoader;
    }
}

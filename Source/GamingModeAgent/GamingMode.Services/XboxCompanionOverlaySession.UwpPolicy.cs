namespace GamingMode.Services;

internal static partial class XboxCompanionOverlaySession
{
    internal static bool MatchesUwpStatus(ulong fullId, uint pid, long birth, uint appId, uint expectedPid, long expectedBirth) =>
        (uint)fullId == 0x02000000 && (uint)(fullId >> 32) == appId && appId >= 0x80000000
        && pid == expectedPid && pid > 0 && birth == expectedBirth && birth > 0;
}

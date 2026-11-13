using GamingMode.Services;

internal static class UwpCompanionPolicyTests
{
    internal static void Run()
    {
        const uint app = 0xF0010203;
        const ulong full = ((ulong)app << 32) | 0x02000000;
        bool Matches(ulong id = full, uint pid = 200, long birth = 300, uint expectedApp = app,
            uint expectedPid = 200, long expectedBirth = 300) =>
            XboxCompanionOverlaySession.MatchesUwpStatus(id, pid, birth, expectedApp, expectedPid, expectedBirth);
        Require(Matches(), "Current exact wrapper shortcut, game process and birth match the UWP companion status");
        Require(!Matches(id: full + 1) && !Matches(id: full + (1UL << 32)) && !Matches(id: 480),
            "Different shortcut flags, full GameID and store identities cannot reuse a UWP companion");
        Require(!Matches(pid: 201) && !Matches(birth: 301) && !Matches(expectedApp: app + 1),
            "Foreign target and recycled game process are refused");
        Require(!Matches(pid: 0, expectedPid: 0) && !Matches(birth: 0, expectedBirth: 0),
            "Missing process identity cannot authorize a hosted frame");
    }
    private static void Require(bool value, string label) { if (!value) throw new InvalidOperationException(label); }
}

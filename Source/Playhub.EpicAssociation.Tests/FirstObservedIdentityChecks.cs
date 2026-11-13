using GamingMode.Services;

internal static class FirstObservedIdentityChecks
{
    static Task<OwnedWin32Route?> Read(Fixture f) => ReadCore(f);
    static async Task<OwnedWin32Route?> ReadCore(Fixture f)
    {
        using var lease = await f.Catalog.FindAsync(Fixture.Canonical, default);
        return lease?.Route;
    }
    static Fixture First(ulong observed = 480, int at = 0)
    {
        var f = new Fixture(false);
        f.Text = f.Row(-1, Fixture.Full, "adding PID 200 as a tracked process")
            + f.Row(at, observed, "adding PID 300 as a tracked process");
        return f;
    }
    internal static async Task Run(Action<bool, string> check)
    {
        async Task Reject(Action<Fixture> change, string why)
        {
            using var f = First(); change(f);
            check(await Read(f) is null, "first observed: " + why);
        }
        using (var f = First())
        {
            var route = await Read(f);
            check(route?.CanonicalShortcutAppId == Fixture.Canonical
                && route.OriginalGameId == Fixture.Full.ToString() && route.ObservedSteamGameId == "480",
                "first observed: actual first480 preserves authenticated canonical wrapper identity");
        }
        using (var f = First(123456, 8))
            check((await Read(f))?.ObservedSteamGameId == "123456", "first observed: delayed arbitrary real Steam ID has no480 mapping");
        using (var f = First())
        {
            f.Text = f.Row(-10, 730, "adding PID 300 as a tracked process") + f.Text;
            check((await Read(f))?.ObservedSteamGameId == "480", "first observed: prior reused PID history before birth window ignored");
        }
        using (var f = First(480, -2))
            check((await Read(f))?.ObservedSteamGameId == "480", "first observed: exact birth-minus-two-second boundary accepted");
        await Reject(f => f.Text = f.Row(-1, Fixture.Full, "adding PID 200 as a tracked process") + f.Row(-3, 480, "adding PID 300 as a tracked process"), "old-only game event cannot admit");
        await Reject(f => f.Text = f.Row(-1, Fixture.Full, "adding PID 200 as a tracked process") + f.Row(11, 480, "adding PID 300 as a tracked process"), "future first event refused");
        await Reject(f => f.Text = f.Row(-1, Fixture.Full, "adding PID 200 as a tracked process") + f.Row(0, 480, "adding PID 301 as a tracked process"), "foreign PID event refused");
        await Reject(f => f.Text += f.Row(1, 480, "adding PID 300 as a tracked process"), "duplicate addition refused");
        await Reject(f => f.Text = f.Row(-1, Fixture.Full, "adding PID 200 as a tracked process") + f.Row(0, 480, "no longer tracking PID 300") + f.Row(1, 480, "adding PID 300 as a tracked process"), "first removal is not initial identity");
        await Reject(f => f.Text = f.Row(-1, Fixture.Full, "adding PID 200 as a tracked process") + f.Row(0, 0x100000001UL, "adding PID 300 as a tracked process"), "unknown64 identity refused");
        await Reject(f => f.Text += f.Row(-1, 480, "no longer tracking PID 300"), "backward transition refused");
        await Reject(f => f.Text += f.Row(1, 730, "no longer tracking PID 300"), "foreign removal ID refused");
        await Reject(f => f.Text += f.Row(1, 480, "no longer tracking PID 300") + f.Row(4, 730, "adding PID 300 as a tracked process"), "replacement gap beyond two seconds refused");
        using (var f = First())
        {
            f.Text += f.Row(1, 480, "no longer tracking PID 300") + f.Row(3, 730, "adding PID 300 as a tracked process");
            check((await Read(f))?.ObservedSteamGameId == "730", "first observed: ordered real transition within two seconds accepted");
        }
        await Reject(f => f.Publications[200] = f.Publications[200] with { Publication = f.Publications[200].Publication with { GameBirth = f.Api.Processes[300].Birth + 1 } }, "private game birth mismatch refused");
        await Reject(f => f.Api.Processes[300] = f.Api.Processes[300] with { Image = @"C:\Games\other.exe" }, "retained image mismatch refused");
        await Reject(f => f.Api.Processes[300] = f.Api.Processes[300] with { Sid = "other" }, "different user refused");
        await Reject(f => f.Api.Processes[300] = f.Api.Processes[300] with { Session = 2 }, "different session refused");
        await Reject(f => f.Publications[200] = f.Publications[200] with { ServerPid = 201 }, "foreign private server refused");
        await Reject(f => f.Publications[200] = f.Publications[200] with { Publication = f.Publications[200].Publication with { GameId = 0xFACEBEEF02000000UL } }, "observed game ID cannot replace tracked canonical wrapper");
        await Reject(f => f.Api.Processes[300] = f.Api.Processes[300] with { Birth = f.Api.Processes[300].Birth + 1 }, "PID reuse refused");
    }
}

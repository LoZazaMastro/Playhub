using GamingMode.Services;
using System.Globalization;

internal static class Win32SteamSessionTests
{
    internal static void Run()
    {
        const uint app = 2979426017;
        const ulong gameId = ((ulong)app << 32) | 0x02000000;
        var started = DateTime.Now;
        long birth = started.ToUniversalTime().ToFileTimeUtc();
        var launch = new CompanionLaunchIdentity(100, birth, 200, birth + 1);
        Require(launch.MatchesGame(200, birth + 1), "Cached companion launch binds exact original process");
        Require(!launch.MatchesGame(201, birth + 1) && !launch.MatchesGame(200, birth + 2),
            "Another process or recycled original PID cannot inherit companion return rights");
        Require(!(launch with { GamePid = 100 }).MatchesGame(100, birth + 1)
            && !(launch with { GameBirth = birth - 1 }).MatchesGame(200, birth - 1),
            "Wrapper itself or an older process cannot become its own game companion proof");
        string Stamp(string text) => $"[{started.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}] {text}\n";
        string Added(uint pid, ulong id) => Stamp($"AppID {id} adding PID {pid} as a tracked process \"C:\\Playhub\\Playhub.GameSession.exe\" --game \"C:\\Games\\fixture.exe\"");
        var parsed = Win32SteamSessionAssociation.Parse(Added(100, gameId), app);
        Require(parsed.Count == 1 && parsed[0].Pid == 100 && parsed[0].GameId == gameId, "Exact shortcut wrapper parse");
        Require(Win32SteamSessionAssociation.Parse(Added(100, gameId) + Stamp("AppID no longer tracking PID 100"), app).Count == 0,
            "Removed wrapper cannot survive the tracking log");
        Require(Win32SteamSessionAssociation.Parse(Added(100, gameId) + Added(100, 480), app).Count == 0,
            "Reused PID for another app invalidates the old shortcut");
        Require(Win32SteamSessionAssociation.Parse(Added(100, gameId) + Stamp("Client version: fixture"), app).Count == 0,
            "Steam client restart invalidates old tracking");
        Require(Win32SteamSessionAssociation.Parse(Added(100, gameId + 1), app).Count == 0,
            "Nonshortcut full GameID refused");
        var identity = new Win32SteamSessionAssociation.Identity(100, birth, 200, birth + 1, gameId, @"C:\Games\fixture.exe");
        bool Matches(Win32SteamSessionAssociation.Identity value, uint server = 100, long? actualBirth = null,
            string image = @"C:\Games\fixture.exe") => Win32SteamSessionAssociation.Matches(value, parsed[0], server,
                birth, 200, actualBirth ?? birth + 1, image);
        Require(Matches(identity), "Exact server and retained process births accepted");
        Require(!Matches(identity, server: 101), "Foreign pipe server refused");
        Require(!Matches(identity, actualBirth: birth + 2), "Recycled game PID refused");
        Require(!Matches(identity with { WrapperBirth = birth + 1 }), "Recycled wrapper PID refused");
        Require(!Matches(identity with { GameBirth = birth - 1 }, actualBirth: birth - 1), "Game older than wrapper refused");
        Require(!Matches(identity with { GameId = gameId + (1UL << 32) }), "Foreign app identity refused");
        Require(!Matches(identity, image: @"C:\Games\other.exe"), "Foreign game image refused");
        Require(!Matches(identity with { Executable = null! }), "Malformed JSON image refuses without throwing");

        string Line(string text) => "Wed Nov 11 02:00:00 2026 UTC - " + text + "\n";
        string Host(uint host, uint target, ulong id) => Line($"GameOverlay process started: pid: {host}")
            + Line($"GameOverlay process connecting to: pid: {target}") + Line($"Got gameid on commandline: {id}");
        var expected = new XboxCompanionOverlaySession.ValveSession(300, 200, gameId);
        var normal = Host(300, 200, gameId);
        Require(XboxCompanionOverlaySession.ParseCompanionValveSession(normal, 200, gameId) == expected,
            "Exact Valve companion header accepted");
        Require(XboxCompanionOverlaySession.ParseCompanionValveSession(normal + Host(301, 201, gameId), 200, gameId) == expected,
            "Later native-game host cannot displace the exact companion host");
        Require(XboxCompanionOverlaySession.ParseCompanionValveSession(normal + Host(301, 201, gameId)
            + Line("Shutting down overlay"), 200, gameId) == expected, "Native host shutdown preserves a separate companion record");
        Require(XboxCompanionOverlaySession.ParseCompanionValveSession(normal + Line("Game Exit Event signaled."), 200, gameId) is null,
            "Companion shutdown invalidates its prior header");
        Require(XboxCompanionOverlaySession.ParseCompanionValveSession(Host(300, 200, gameId + (1UL << 32)), 200, gameId) is null,
            "Another GameID never qualifies");
        string overlap = Line("GameOverlay process started: pid: 300") + Line("GameOverlay process started: pid: 301")
            + Line("GameOverlay process connecting to: pid: 201") + Line("GameOverlay process connecting to: pid: 200")
            + Line($"Got gameid on commandline: {gameId}");
        Require(XboxCompanionOverlaySession.ParseCompanionValveSession(overlap, 200, gameId) is null,
            "Interleaved host headers cannot pair native renderer PID with companion target");
        Require(XboxCompanionOverlaySession.ParseCompanionValveSession(overlap + normal, 200, gameId) == expected,
            "A later complete unambiguous header can recover safely");
    }

    private static void Require(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
    }
}

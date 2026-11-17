using GamingMode.Services;
using Playhub.Shared;

internal static class TrackedCommandParsingChecks
{
    internal static void Run(Action<bool, string> check)
    {
        const string wrapper = @"C:\Users\Fixture\AppData\Local\Programs\Playhub\Playhub.GameSession.exe";
        const string aumid = "Microsoft.KillerInstinct-Win10_8wekyb3d8bbwe!KillerInstinct.n2019Gaming.Desktop.x64.r";
        const string executable = "KillerInstinct_2019Gaming.Desktop.x64_r.exe";
        const ulong gameId = 15293059651122233344;
        const uint wrapperPid = 4412, gamePid = 5396;
        DateTime time = new(2026, 11, 15, 22, 34, 54, DateTimeKind.Local);
        string Row(string command) => $"[{time:yyyy-MM-dd HH:mm:ss}] AppID {gameId} adding PID {wrapperPid} as a tracked process {command}";
        string arguments = $"--uwp \"{aumid}\" \"{executable}\" \"\"";
        string[] accepted = [$"\"{wrapper}\" {arguments}", $"\"\"{wrapper}\" {arguments}\"", $"\"{wrapper} {arguments}\""];
        var images = new UwpSteamSessionAssociation.InstalledImages(wrapper, @"C:\Fixture\Playhub.XboxSession.exe");
        long wrapperBirth = time.ToUniversalTime().AddMilliseconds(-100).ToFileTimeUtc(), gameBirth = wrapperBirth + 1;
        string gameImage = @"C:\XboxGames\Killer Instinct\Content\" + executable;
        var identity = new UwpSessionIdentity(wrapperPid, wrapperBirth, gamePid, gameBirth, aumid, aumid.Split('!')[0], gameImage);
        foreach (string command in accepted)
        {
            var parsed = UwpSteamSessionAssociation.Parse(Row(command));
            check(parsed.Count == 1 && parsed[0].Image == wrapper && parsed[0].Aumid == aumid
                && parsed[0].Executable == executable && parsed[0].GameId == gameId && parsed[0].Pid == wrapperPid,
                "Both native quoted-executable and whole-command Steam log forms retain the exact wrapper/UWP fields");
            if (parsed.Count != 1) continue;
            bool Matches(uint server = wrapperPid, long? observedWrapperBirth = null, string? observedWrapper = null) =>
                UwpSteamSessionAssociation.Matches(parsed[0], identity, server, gamePid, UwpSteamSessionAssociation.AppId(gameId),
                    observedWrapperBirth ?? wrapperBirth, gameBirth, observedWrapper ?? wrapper, identity.PackageFamily, gameImage, images);
            check(Matches(), "Parsed Steam log form still requires the exact live pipe/process identity");
            check(!Matches(server: wrapperPid + 1), "Whole-command log form cannot admit another pipe server");
            check(!Matches(observedWrapperBirth: wrapperBirth + 1), "Whole-command log form cannot admit recycled wrapper PID");
            check(!Matches(observedWrapper: @"C:\Other\Playhub.GameSession.exe"), "Whole-command log form cannot admit a wrapper at another image path");
        }
        check(UwpSteamSessionAssociation.Parse(Row(accepted[2]) + "\r\n").Count == 1, "Windows CRLF does not change whole-command parsing");
        foreach (string command in new[] {
            wrapper + " " + arguments,
            $"\"{wrapper} --game \"{aumid}\" \"{executable}\" \"\"\"",
            $"\"{wrapper} --uwp {aumid} \"{executable}\" \"\"\"",
            $"\"{wrapper} --uwp \"{aumid}\" {executable} \"\"\"",
            accepted[2] + "\0",
            "\"" + new string('x', 32768) + arguments + "\"",
        })
            check(UwpSteamSessionAssociation.Parse(Row(command)).Count == 0, "Malformed/unbounded UWP tracking commands do not create associations");
        check(UwpSteamSessionAssociation.Parse(Row(accepted[2]) + $"\n[{time:yyyy-MM-dd HH:mm:ss}] AppID {gameId} no longer tracking PID {wrapperPid}").Count == 0,
            "A real whole-command session is invalidated by the matching Steam removal");
    }
}

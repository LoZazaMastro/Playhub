using GamingMode.Services;
using System.Globalization;

internal static class NativeSteamOverlayHostTests
{
    internal static void Run()
    {
        const string image = @"C:\Program Files (x86)\Steam\gameoverlayui64.exe";
        const uint companion = 200, steam = 300;
        const ulong game = ((ulong)2979426017 << 32) | 0x02000000;
        string id = game.ToString(CultureInfo.InvariantCulture);
        string[] valid = [image, "-pid", "200", "-steampid", "300", "-gameid", id];
        bool Matches(string[] args, string executable = image, uint target = companion, uint owner = steam, ulong fullId = game) =>
            NativeSteamOverlayHost.Matches(args, executable, target, owner, fullId);
        Require(Matches(valid), "Exact Valve command identifies companion, Steam owner and complete unsigned GameID");
        Require(Matches([image.ToUpperInvariant(), "-GAMEID", id, "-SteamPid", "300", "-PID", "200"]),
            "Argument order and Windows path casing do not change exact host identity");
        Require(Matches([.. valid, "-screenwidth", "3840", "-screenheight", "2160"]),
            "Unrelated Valve display arguments remain supported");
        Require(!Matches(valid, target: companion + 1) && !Matches(valid, owner: steam + 1)
            && !Matches(valid, fullId: game + (1UL << 32)), "Different companion, Steam process or shortcut is refused");
        Require(!Matches(valid, target: 0) && !Matches(valid, owner: 0) && !Matches(valid, fullId: 480)
            && !Matches(valid, fullId: game + 1), "Absent process identities and nonshortcut GameIDs are refused");
        Require(!Matches(valid, executable: @"C:\OtherSteam\gameoverlayui64.exe")
            && !Matches([image + ".other", .. valid.Skip(1)]), "Another renderer executable cannot claim the installed host");
        foreach (string flag in new[] { "-pid", "-steampid", "-gameid" })
        {
            int index = Array.IndexOf(valid, flag);
            Require(!Matches(valid.Where((_, i) => i != index && i != index + 1).ToArray()),
                "Missing required flag is refused: " + flag);
            Require(!Matches([.. valid, flag, valid[index + 1]])
                && !Matches([.. valid, flag.ToUpperInvariant(), valid[index + 1]]),
                "Duplicate identity flags are refused even when values agree: " + flag);
            string[] missingValue = [.. valid.Where((_, i) => i != index && i != index + 1), flag];
            Require(!Matches(missingValue), "Required identity value cannot be omitted: " + flag);
            foreach (string invalid in new[] { "", "+200", "-200", " 200", "200 ", "0xC8", "18446744073709551616" })
            {
                var changed = (string[])valid.Clone();
                changed[index + 1] = invalid;
                Require(!Matches(changed), "Identity values require bounded decimal integers: " + flag);
            }
        }
        Require(!Matches([]) && !Matches([image]) && !Matches([image, "-pid=200", "-steampid", "300", "-gameid", id]),
            "Truncated and unsupported combined identity arguments are refused");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

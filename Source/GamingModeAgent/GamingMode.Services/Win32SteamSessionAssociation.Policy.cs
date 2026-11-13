using System.Globalization;
using System.Text.RegularExpressions;

namespace GamingMode.Services;

internal static partial class Win32SteamSessionAssociation
{
    internal sealed record Identity(uint WrapperPid, long WrapperBirth, uint GamePid, long GameBirth,
        ulong GameId, string Executable, Playhub.Shared.EpicLaunchProvenance? Epic = null);
    internal sealed record Tracked(uint Pid, ulong GameId, DateTime Started);
    private static readonly Regex Added = new(@"AppID (?<id>\d+) adding PID (?<pid>\d+) as a tracked process", RegexOptions.CultureInvariant);
    private static readonly Regex Removed = new(@"no longer tracking PID (?<pid>\d+)", RegexOptions.CultureInvariant);

    internal static IReadOnlyList<Tracked> Parse(string text, uint appId)
    {
        var active = new Dictionary<uint, Tracked>();
        foreach (string line in text.Split('\n'))
        {
            if (line.Contains("Client version:", StringComparison.Ordinal)) { active.Clear(); continue; }
            var removed = Removed.Match(line);
            if (removed.Success && uint.TryParse(removed.Groups["pid"].Value, out uint gone)) active.Remove(gone);
            var added = Added.Match(line);
            if (!added.Success || !uint.TryParse(added.Groups["pid"].Value, out uint pid)) continue;
            active.Remove(pid);
            if (!ulong.TryParse(added.Groups["id"].Value, out ulong gameId) || (uint)gameId != 0x02000000
                || (uint)(gameId >> 32) != appId || line.Length < 21
                || !DateTime.TryParseExact(line.AsSpan(1, 19), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var started)) continue;
            active[pid] = new(pid, gameId, started);
        }
        return active.Values.ToArray();
    }

    internal static bool Matches(Identity identity, Tracked tracked, uint serverPid, long wrapperBirth,
        uint gamePid, long gameBirth, string image) =>
        identity.WrapperPid == tracked.Pid && serverPid == tracked.Pid && wrapperBirth > 0
        && identity.WrapperBirth == wrapperBirth && identity.GameId == tracked.GameId
        && identity.GamePid == gamePid && gamePid != 0 && gameBirth >= wrapperBirth && identity.GameBirth == gameBirth
        && !string.IsNullOrWhiteSpace(identity.Executable) && !string.IsNullOrWhiteSpace(image)
        && string.Equals(identity.Executable, image, StringComparison.OrdinalIgnoreCase)
        && Math.Abs((DateTime.FromFileTimeUtc(wrapperBirth).ToLocalTime() - tracked.Started).TotalSeconds) <= 2;
}

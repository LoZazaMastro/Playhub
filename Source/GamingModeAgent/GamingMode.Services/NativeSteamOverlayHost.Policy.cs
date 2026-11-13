using System.Globalization;

namespace GamingMode.Services;

internal static partial class NativeSteamOverlayHost
{
    internal static bool Matches(string[] args, string image, uint companionPid, uint steamPid, ulong gameId)
    {
        if (args.Length < 7 || companionPid == 0 || steamPid == 0 || (uint)gameId != 0x02000000
            || (gameId >> 32) < 0x80000000 || !args[0].Equals(image, StringComparison.OrdinalIgnoreCase)) return false;
        bool Read(string flag, ulong expected)
        {
            var positions = Enumerable.Range(1, args.Length - 1).Where(i => args[i].Equals(flag, StringComparison.OrdinalIgnoreCase)).ToArray();
            return positions is [var index] && index + 1 < args.Length
                && ulong.TryParse(args[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out ulong actual) && actual == expected;
        }
        return Read("-pid", companionPid) && Read("-steampid", steamPid) && Read("-gameid", gameId);
    }
}

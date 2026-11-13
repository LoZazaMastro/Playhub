using System;
using System.IO;

namespace GamingMode.Services;

internal static class FrameMeasurementTargetPolicy
{
    internal static bool MatchesPackagedForeground(uint gamePid, string aumid, int windowOwnerPid,
        string? hostedAumid, bool onlyInstance)
    {
        if (gamePid == 0 || string.IsNullOrEmpty(aumid)) return false;
        return hostedAumid is null
            ? windowOwnerPid == gamePid
            : onlyInstance && string.Equals(aumid, hostedAumid, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool CanMeasureWindowOwner(string processName)
    {
        string name = Path.GetFileName(processName);
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name.ToLowerInvariant() is not ("" or "applicationframehost" or "playhub.gamesession" or "playhub.xboxsession")
            && !SteamGameIdentityPolicy.IsSteamClient(name) && !SteamGameIdentityPolicy.IsXboxLaunchHelper(name);
    }
}

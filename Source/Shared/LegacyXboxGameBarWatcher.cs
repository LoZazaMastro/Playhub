using System;

namespace Playhub.Shared;

internal static class LegacyXboxGameBarWatcher
{
    internal static bool Matches(string? name, string? processName, string? arguments) =>
        string.Equals(name, "Playhub Xbox Game Bar", StringComparison.OrdinalIgnoreCase)
        && (string.Equals(processName, "playhub-gm-gamebar", StringComparison.OrdinalIgnoreCase)
            || arguments?.Contains("\\Assets\\GamingMode\\xbox-gamebar.ps1\"", StringComparison.OrdinalIgnoreCase) == true);
}

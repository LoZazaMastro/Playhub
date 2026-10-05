using System;
using System.IO;

namespace Playhub.Shared;

/// <summary>The agent is an optional component, independent of the Playhub Decky plugin.</summary>
public static class GamingModeInstallationPolicy
{
    public const string DisabledMarkerName = "disabled-by-user";
    public static string DisabledMarker => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GamingMode", DisabledMarkerName);

    public static bool ShouldMaintainInstallation(string executable, string disabledMarker) =>
        File.Exists(executable) && !File.Exists(disabledMarker);

    public static void Disable(string marker)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(marker))!);
        File.WriteAllText(marker, "Gaming Mode was removed by the user. Only an explicit installation enables it again.");
    }

    public static void Enable(string marker)
    {
        if (File.Exists(marker)) File.Delete(marker);
    }

    public static bool OwnsExecutable(string? executable, string installationDirectory)
    {
        if (string.IsNullOrWhiteSpace(executable) || string.IsNullOrWhiteSpace(installationDirectory)) return false;
        try
        {
            return string.Equals(Path.GetFileName(executable), "GamingMode.exe", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Path.GetDirectoryName(Path.GetFullPath(executable)),
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(installationDirectory)), StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
    }
}

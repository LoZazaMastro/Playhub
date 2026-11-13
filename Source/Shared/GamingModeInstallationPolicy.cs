using System;
using System.IO;
using System.Security.Cryptography;

namespace Playhub.Shared;

/// <summary>The agent is an optional component, independent of the Playhub Decky plugin.</summary>
public static class GamingModeInstallationPolicy
{
    public const string DisabledMarkerName = "disabled-by-user";
    public static string DisabledMarker => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GamingMode", DisabledMarkerName);

    public static bool ShouldMaintainInstallation(string executable, string disabledMarker) =>
        File.Exists(executable) && !File.Exists(disabledMarker);

    /// <summary>Only verified identical bytes authorize skipping an agent replacement.</summary>
    public static bool HasSameAgentPayload(string package, string installed)
    {
        try
        {
            using var source = File.Open(package, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var destination = File.Open(installed, FileMode.Open, FileAccess.Read, FileShare.Read);
            return source.Length == destination.Length
                && SHA256.HashData(source).AsSpan().SequenceEqual(SHA256.HashData(destination));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            // Unknown content must never be reported as the current package.
            return false;
        }
    }

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

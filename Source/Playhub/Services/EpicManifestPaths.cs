using System.Security;
using Microsoft.Win32;

namespace Playhub.Services;

internal static class EpicManifestPaths
{
    internal static IReadOnlyList<string> Read() => Resolve(
        view =>
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var launcher = machine.OpenSubKey(@"SOFTWARE\Epic Games\EpicGamesLauncher", writable: false);
            return launcher?.GetValue("AppDataPath", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        }, Directory.Exists, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));

    internal static IReadOnlyList<string> Resolve(Func<RegistryView, string?> readAppDataPath,
        Func<string, bool> directoryExists, string commonApplicationData)
    {
        var directories = new List<string>();
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try { Add(readAppDataPath(view)); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException) { }
        }
        Add(Path.Combine(commonApplicationData, "Epic", "EpicGamesLauncher", "Data"));
        return directories;

        void Add(string? dataRoot)
        {
            if (string.IsNullOrWhiteSpace(dataRoot) || !Path.IsPathFullyQualified(dataRoot)) return;
            try
            {
                string directory = Path.GetFullPath(Path.Combine(dataRoot, "Manifests"));
                if (directoryExists(directory) && !directories.Contains(directory, StringComparer.OrdinalIgnoreCase))
                    directories.Add(directory);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException
                or UnauthorizedAccessException or SecurityException) { }
        }
    }
}

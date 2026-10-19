using System.Diagnostics;
using Playhub.Shared;

namespace Playhub.Services;

internal static class GamingModePayloadSync
{
    internal static bool NeedsAgentUpdate(string source, string installed)
    {
        if (!File.Exists(source) || !File.Exists(installed)) return false;
        return NeedsAgentUpdate(source, installed, ReadVersion(source), ReadVersion(installed));
    }

    internal static bool NeedsAgentUpdate(string source, string installed, Version? sourceVersion, Version? installedVersion)
    {
        // Automatic startup synchronization must not replace a newer valid agent
        // with an older package. Explicit installation/repair bypasses this policy.
        if (sourceVersion is not null && installedVersion is not null)
        {
            int comparison = installedVersion.CompareTo(sourceVersion);
            if (comparison > 0 || (comparison == 0 && File.GetLastWriteTimeUtc(installed) > File.GetLastWriteTimeUtc(source))) return false;
            if (comparison < 0) return true;
        }
        return !BundledExecutableCopy.IsCurrent(source, installed);
    }

    private static Version? ReadVersion(string path)
    {
        var info = FileVersionInfo.GetVersionInfo(path);
        return string.IsNullOrWhiteSpace(info.FileVersion) ? null :
            new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
    }

    internal static IEnumerable<string> RequiredPluginFiles(string installRoot, string source)
    {
        var retired = EditorialPhotoMigration.GetRetiredAssetNames(installRoot, source);
        string assets = Path.Combine(source, "dist", "assets");
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if (Path.GetDirectoryName(file)!.Equals(assets, StringComparison.OrdinalIgnoreCase) && retired.Contains(Path.GetFileName(file))) continue;
            yield return file;
        }
    }

    internal static bool NeedsPluginUpdate(string installRoot, string source, string destination)
        => RequiredPluginFiles(installRoot, source).Any(file =>
            !BundledExecutableCopy.IsCurrent(file, Path.Combine(destination, Path.GetRelativePath(source, file))));
}

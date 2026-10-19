using System.Security.Cryptography;

namespace Playhub.Services;

internal static class BundledExecutableCopy
{
    internal static bool IsCurrent(string source, string destination)
    {
        if (!File.Exists(destination)) return false;
        var originalInfo = new FileInfo(source);
        var installedInfo = new FileInfo(destination);
        if (originalInfo.Length != installedInfo.Length) return false;
        if (originalInfo.LastWriteTimeUtc == installedInfo.LastWriteTimeUtc) return true;
        return HasSameContent(source, destination);
    }

    private static bool HasSameContent(string source, string destination)
    {
        using var original = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var installed = File.Open(destination, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return original.Length == installed.Length && SHA256.HashData(original).AsSpan().SequenceEqual(SHA256.HashData(installed));
    }

    internal static void Copy(string source, string destination)
    {
        // Executables may be held open by Decky. Matching bytes never require
        // replacement (or a timestamp write to a running image).
        // Copy is also used by explicit repair: compare actual bytes even when
        // metadata happens to match, so damaged files can still be restored.
        if (File.Exists(destination) && HasSameContent(source, destination)) return;
        File.Copy(source, destination, overwrite: true);
    }
}

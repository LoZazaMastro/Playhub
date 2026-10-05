using System.Security.Cryptography;

namespace Playhub.Services;

internal static class BundledExecutableCopy
{
    internal static void Copy(string source, string destination)
    {
        if (Path.GetExtension(source).Equals(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(destination))
        {
            using var original = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var installed = File.Open(destination, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (original.Length == installed.Length && SHA256.HashData(original).AsSpan().SequenceEqual(SHA256.HashData(installed))) return;
        }
        File.Copy(source, destination, overwrite: true);
    }
}

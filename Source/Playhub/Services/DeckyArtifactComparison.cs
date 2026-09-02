using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Playhub.Services;

internal enum DeckyArtifactMatch { Invalid, Different, Identical }

// Read-only comparison of the actual artifact, independent of installer side effects.
internal static class DeckyArtifactComparison
{
    internal static DeckyArtifactMatch Compare(byte[] zipBytes, string servicesDir)
    {
        try
        {
            var root = Path.GetFullPath(servicesDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using var buffer = new MemoryStream(zipBytes, writable: false);
            using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entries = new List<(ZipArchiveEntry Entry, string Target)>();
            var hasLoader = false;

            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                var directory = name.EndsWith("/", StringComparison.Ordinal);
                var relative = directory ? name[..^1] : name;
                if (string.IsNullOrEmpty(relative) || Path.IsPathRooted(name) || !names.Add(relative))
                    return DeckyArtifactMatch.Invalid;

                foreach (var part in relative.Split('/'))
                {
                    if (!IsNormalComponent(part)) return DeckyArtifactMatch.Invalid;
                }

                // Only ordinary files/directories: reject Unix symlinks and Windows reparse points.
                var type = (entry.ExternalAttributes >> 16) & 0xF000;
                if ((type != 0 && type != (directory ? 0x4000 : 0x8000)) ||
                    (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 ||
                    (directory && entry.Length != 0))
                    return DeckyArtifactMatch.Invalid;

                var target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) || HasReparsePoint(target))
                    return DeckyArtifactMatch.Invalid;

                if (directory) continue;
                files.Add(relative);
                entries.Add((entry, target));
                if (relative.Equals("PluginLoader.exe", StringComparison.OrdinalIgnoreCase) ||
                    relative.Equals("PluginLoader_noconsole.exe", StringComparison.OrdinalIgnoreCase))
                {
                    if (entry.Length == 0) return DeckyArtifactMatch.Invalid;
                    hasLoader = true;
                }
            }

            if (!hasLoader || entries.Count == 0) return DeckyArtifactMatch.Invalid;
            foreach (var name in names)
            {
                for (var slash = name.IndexOf('/'); slash >= 0; slash = name.IndexOf('/', slash + 1))
                    if (files.Contains(name[..slash])) return DeckyArtifactMatch.Invalid;
            }

            var identical = true;
            foreach (var (entry, target) in entries)
            {
                // Consume every archive file even after a mismatch, so later invalid data cannot match.
                using var source = entry.Open();
                var expected = SHA256.HashData(source);
                try
                {
                    using var installed = File.OpenRead(target);
                    if (installed.Length != entry.Length ||
                        !CryptographicOperations.FixedTimeEquals(expected, SHA256.HashData(installed)))
                        identical = false;
                }
                catch (IOException) { identical = false; }
                catch (UnauthorizedAccessException) { identical = false; }
            }

            return identical ? DeckyArtifactMatch.Identical : DeckyArtifactMatch.Different;
        }
        catch (InvalidDataException) { return DeckyArtifactMatch.Invalid; }
        catch (IOException) { return DeckyArtifactMatch.Invalid; }
        catch (UnauthorizedAccessException) { return DeckyArtifactMatch.Invalid; }
        catch (ArgumentException) { return DeckyArtifactMatch.Invalid; }
        catch (NotSupportedException) { return DeckyArtifactMatch.Invalid; }
    }

    private static bool IsNormalComponent(string part)
    {
        if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')) return false;
        foreach (var c in part)
            if (c < 32 || "<>:\"|?*".Contains(c)) return false;
        var stem = part.Split('.')[0].ToUpperInvariant();
        return stem is not ("CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$") &&
            !(stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) ||
                stem.StartsWith("LPT", StringComparison.Ordinal)) &&
                (stem[3] is >= '0' and <= '9' or '\u00b9' or '\u00b2' or '\u00b3'));
    }

    private static bool HasReparsePoint(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return false;
    }
}

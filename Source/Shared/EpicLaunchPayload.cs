// SPDX-License-Identifier: MIT. Playhub.
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Playhub.Shared;

internal sealed record EpicLaunchPayload(string Executable, string? ConfigurationPath = null, string? ConfigurationSha256 = null);
internal enum EpicPayloadFileState { Missing, PlainFile, Unavailable }
internal static class EpicLaunchPayloadResolver
{
    internal const int MaximumConfigurationBytes = 65536;
    internal static EpicLaunchPayload? Resolve(EpicInstalledLaunch launch) => Resolve(launch, ReadState, ReadBounded);
    internal static EpicLaunchPayload? Resolve(EpicInstalledLaunch launch,
        Func<string, EpicPayloadFileState> state, Func<string, string> read)
    {
        try
        {
            if (!EpicLaunchCatalog.LocalAbsolute(launch.InstallDirectory) || !EpicLaunchCatalog.LocalAbsolute(launch.GameExecutable)
                || state(launch.GameExecutable) != EpicPayloadFileState.PlainFile) return null;
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(launch.InstallDirectory));
            string declared = Path.GetFullPath(launch.GameExecutable);
            if (!Within(root, declared)) return null;
            string? configuration = null;
            foreach (string parent in new[] { Path.GetDirectoryName(declared)!, root }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string path = Path.Combine(parent, "EasyAntiCheat", "Settings.json");
                var kind = state(path);
                if (kind == EpicPayloadFileState.Unavailable) return null;
                if (kind == EpicPayloadFileState.Missing) continue;
                if (configuration is not null) return null; // two configurations are ambiguous
                configuration = path;
            }
            // The game's declared launch target remains the default for a
            // direct catalog entry. This does not classify its window as ready.
            if (configuration is null) return new(declared);
            string json = read(configuration);
            if (Encoding.UTF8.GetByteCount(json) > MaximumConfigurationBytes) return null;
            using var doc = JsonDocument.Parse(json);
            var objectValue = doc.RootElement;
            if (objectValue.ValueKind != JsonValueKind.Object || !objectValue.TryGetProperty("executable", out var field)
                || field.ValueKind != JsonValueKind.String || field.GetString() is not { Length: > 0 } relative
                || Path.IsPathRooted(relative) || relative.Contains(':') || relative.Length > 32768) return null;
            // Epic documents executable relative to the game root containing
            // the EasyAntiCheat folder. It is a descriptor, never an argv list.
            string anchor = Path.GetDirectoryName(Path.GetDirectoryName(configuration)!)!;
            string executable = Path.GetFullPath(Path.Combine(anchor, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!Within(root, executable) || !Path.GetExtension(executable).Equals(".exe", StringComparison.OrdinalIgnoreCase)
                || state(executable) != EpicPayloadFileState.PlainFile || state(configuration) != EpicPayloadFileState.PlainFile) return null;
            return new(executable, configuration, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))));
        }
        catch (Exception error) when (EpicLaunchCatalog.Expected(error)) { return null; }
    }
    internal static bool Current(EpicInstalledLaunch launch, EpicLaunchPayload expected) => Resolve(launch) == expected;
    private static bool Within(string root, string path) => path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static EpicPayloadFileState ReadState(string path) => ReadState(path, File.GetAttributes);
    internal static EpicPayloadFileState ReadState(string path, Func<string, FileAttributes> attributes)
    {
        try
        {
            if (!EpicLaunchCatalog.LocalAbsolute(path)) return EpicPayloadFileState.Unavailable;
            // Visit existing parents before following a possibly dangling
            // reparse directory. A missing leaf must not hide an unknown parent.
            var parents = new Stack<string>();
            for (string? parent = Path.GetDirectoryName(path); parent is not null; parent = Path.GetDirectoryName(parent)) parents.Push(parent);
            while (parents.TryPop(out var parent))
            {
                var flags = attributes(parent);
                if ((flags & FileAttributes.ReparsePoint) != 0 || (flags & FileAttributes.Directory) == 0) return EpicPayloadFileState.Unavailable;
            }
            if ((attributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) return EpicPayloadFileState.Unavailable;
            return EpicPayloadFileState.PlainFile;
        }
        catch (FileNotFoundException) { return EpicPayloadFileState.Missing; }
        catch (DirectoryNotFoundException) { return EpicPayloadFileState.Missing; }
        catch (Exception error) when (EpicLaunchCatalog.Expected(error)) { return EpicPayloadFileState.Unavailable; }
    }
    private static string ReadBounded(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (input.Length > MaximumConfigurationBytes) throw new InvalidDataException("Epic payload configuration is oversized.");
        byte[] bytes = new byte[MaximumConfigurationBytes + 1]; int length = 0, count;
        while (length < bytes.Length && (count = input.Read(bytes, length, bytes.Length - length)) != 0) length += count;
        if (length > MaximumConfigurationBytes) throw new InvalidDataException("Epic payload configuration changed beyond its bound.");
        return new UTF8Encoding(false, true).GetString(bytes, 0, length).TrimStart('\uFEFF');
    }
}

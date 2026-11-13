using System.Text.Json;
using Playhub.Models;

namespace Playhub.Services;

// Fortnite compatibility with the locally observed InstalledItems v4 format.
// This format has no general base-game/DLC discriminator: keep the fallback
// restricted to Fortnite. A normal Launcher .item always takes precedence.
internal static class EpicInstalledItem
{
    internal static UwpGameEntry? TryParse(string recordPath)
    {
        try
        {
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(recordPath), "N", out var fileId)
                || new FileInfo(recordPath).Length > 262144) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(recordPath));
            if (!document.RootElement.TryGetProperty("v4", out var root) || root.ValueKind != JsonValueKind.Object
                || !Guid.TryParseExact(Text(root, "installationId"), "N", out var recordId) || recordId != fileId
                || Text(root, "state") != "Installed" || Text(root, "platform") != "Windows"
                || !EmptyString(root, "stagedVersion")) return null;
            string sandbox = Text(root, "sandboxId"), item = Text(root, "itemId"), app = Text(root, "artifactId");
            if (sandbox != "fn" || app != "Fortnite") return null;
            if (!Identifier(sandbox) || !Identifier(item) || !Identifier(app)
                || sandbox.Equals("ue", StringComparison.OrdinalIgnoreCase)
                || app.StartsWith("UE_", StringComparison.OrdinalIgnoreCase)) return null;
            if (root.TryGetProperty("bIsApplication", out var application) && application.ValueKind != JsonValueKind.True)
                return null;
            if (root.TryGetProperty("AppCategories", out var categories)
                && (categories.ValueKind != JsonValueKind.Array || !categories.EnumerateArray().Any(value =>
                    value.ValueKind == JsonValueKind.String && value.GetString() == "games"))) return null;
            if (root.TryGetProperty("pendingTags", out var pendingTags)
                && (pendingTags.ValueKind != JsonValueKind.Array || pendingTags.GetArrayLength() != 0)) return null;
            if (!root.TryGetProperty("manifestData", out var data) || data.ValueKind != JsonValueKind.Object
                || Text(data, "version").Length == 0 || !data.TryGetProperty("launchCommand", out var command)
                || command.ValueKind != JsonValueKind.String) return null;
            if (!root.TryGetProperty("pendingManifestData", out var pending)
                || pending.ValueKind != JsonValueKind.Object || !EmptyString(pending, "version")) return null;
            if (!data.TryGetProperty("buildSize", out var size) || !size.TryGetInt64(out long buildSize) || buildSize <= 0)
                return null;

            string install = Text(root, "dir"), metadata = Text(root, "metaDir"), manifest = Text(root, "manifestPath");
            if (!Path.IsPathFullyQualified(install) || !Path.IsPathFullyQualified(metadata)
                || !Path.IsPathFullyQualified(manifest)) return null;
            install = Path.TrimEndingDirectorySeparator(Path.GetFullPath(install));
            string expectedMetadata = Path.Combine(install, ".egstore");
            string expectedManifest = Path.Combine(expectedMetadata, Text(root, "installationId") + ".manifest");
            if (!Directory.Exists(install)
                || !SamePath(metadata, expectedMetadata) || !SamePath(manifest, expectedManifest)
                || !HasManifest(expectedManifest)) return null;
            string launch = Text(data, "launchExe");
            if (string.IsNullOrWhiteSpace(launch) || Path.IsPathRooted(launch)) return null;
            string executable = Path.GetFullPath(Path.Combine(install, launch.Replace('/', Path.DirectorySeparatorChar)));
            if (!executable.StartsWith(install + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !Path.GetExtension(executable).Equals(".exe", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(executable)) return null;

            // The metadata's target may be an anti-cheat bootstrapper. Preserve
            // it exactly; never search for or substitute a Shipping executable.
            return new UwpGameEntry
            {
                Name = Guid.TryParseExact(app, "N", out _) ? Path.GetFileNameWithoutExtension(executable) : app,
                Aumid = executable, IsLocalExecutable = true, LocalExecutablePath = executable,
                Executable = executable, SourceLaunchArguments = Text(data, "launchCommand"),
                EpicNamespace = sandbox, EpicCatalogItemId = item, EpicAppName = app,
                FileSize = new FileInfo(executable).Length
            };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or JsonException or InvalidOperationException or System.Security.SecurityException) { return null; }
    }

    private static bool Identifier(string value) => value.Length is > 0 and <= 256
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.');
    private static bool EmptyString(JsonElement root, string name) => root.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String && value.GetString() == "";
    private static bool HasManifest(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> magic = stackalloc byte[4];
        return stream.Length >= 41 && stream.Read(magic) == magic.Length
            && System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(magic) == 0x44BEC00C;
    }
    private static bool SamePath(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
        StringComparison.OrdinalIgnoreCase);
    private static string Text(JsonElement root, string name) => root.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}

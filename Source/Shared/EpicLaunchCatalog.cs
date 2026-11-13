using System.IO;
// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
// Installed metadata only; no account or authentication data.
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace Playhub.Shared;

internal sealed record EpicInstalledLaunch(string Namespace, string CatalogItemId, string AppName,
    string ManifestPath, string ManifestIdentitySha256, string InstallDirectory, string GameExecutable, string LaunchUri);

internal sealed record EpicLaunchProvenance(int Protocol, string Ticket, string Namespace, string CatalogItemId,
    string AppName, string ManifestPath, string ManifestIdentitySha256, string InstallDirectory, string GameExecutable,
    uint WrapperPid, long WrapperBirth, uint LauncherPid, long LauncherBirth, string LauncherImage,
    string LauncherSha256, long StartedAt, uint GamePid, long GameBirth, long GameWindow, int BeforeTargetCount,
    string? RecordedLaunchExecutable = null, string? ParentChain = null,
    string? PayloadConfigurationPath = null, string? PayloadConfigurationSha256 = null);

internal static class EpicLaunchCatalog
{
    internal const string RustlerNamespace = "aca2ee0619aa482791f0a71dbf3eeac0";
    internal const string RustlerItem = "74b8de4cd48249d794475a6b9ddea96d";
    internal const string RustlerApp = "6dfbeb5a6f2e42b5a6a5dcf7a8269e9c";
    internal static bool Supported(string ns, string item, string app) =>
        ns == RustlerNamespace && item == RustlerItem && app == RustlerApp;

    internal static EpicInstalledLaunch? ResolveInstalled(string ns, string item, string app, string game) =>
        ResolveInstalled(ns, item, app, game, ManifestDirectories(), File.ReadAllText, PlainExistingPath,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicOnlineServicesShared", "InstallHelper", "InstalledItems"));

    internal static EpicInstalledLaunch? ResolveInstalled(string ns, string item, string app, string game,
        IEnumerable<string> directories, Func<string, string> read, Func<string, bool> existing, string? installedItems = null)
    {
        if (!Identifier(ns) || !Identifier(item) || !Identifier(app) || ns == "ue" || app.StartsWith("UE_", StringComparison.OrdinalIgnoreCase)) return null;
        EpicInstalledLaunch? found = null;
        var preferred = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (string directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(directory)) continue;
                var files = Directory.EnumerateFiles(directory, "*.item").Take(513).ToArray();
                if (files.Length > 512) return null;
                foreach (string file in files)
                {
                    preferred.Add(Path.GetFileNameWithoutExtension(file));
                    var current = Parse(file, read(file), ns, item, app, game, existing);
                    if (current is null) continue;
                    if (found is not null) return null;
                    found = current;
                }
            }
            if (installedItems is not null && Directory.Exists(installedItems))
            {
                var files = Directory.EnumerateFiles(installedItems, "*.egi").Take(513).ToArray();
                if (files.Length > 512) return null;
                foreach (string file in files)
                {
                    if (preferred.Contains(Path.GetFileNameWithoutExtension(file))) continue;
                    var current = ParseInstalledItem(file, read(file), ns, item, app, game, existing);
                    if (current is null) continue;
                    if (found is not null) return null;
                    found = current;
                }
            }
        }
        catch (Exception e) when (Expected(e)) { return null; }
        return found;
    }

    internal static EpicInstalledLaunch? Parse(string manifest, string json, string ns, string item, string app,
        string game, Func<string, bool> existing)
    {
        if (!Identifier(ns) || !Identifier(item) || !Identifier(app) || ns == "ue" || app.StartsWith("UE_", StringComparison.OrdinalIgnoreCase)
            || json.Length > 1024 * 1024) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            string Text(string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
            if (Text("CatalogNamespace") != ns || Text("CatalogItemId") != item || Text("AppName") != app
                || root.TryGetProperty("bIsIncompleteInstall", out var incomplete) && incomplete.ValueKind != JsonValueKind.False
                || root.TryGetProperty("bIsApplication", out var application) && application.ValueKind != JsonValueKind.True) return null;
            if (root.TryGetProperty("AppCategories", out var categories)
                && (categories.ValueKind != JsonValueKind.Array || !categories.EnumerateArray().Any(v => v.ValueKind == JsonValueKind.String && v.GetString() == "games"))) return null;
            string install = Text("InstallLocation"), launch = Text("LaunchExecutable");
            if (!LocalAbsolute(install) || !LocalAbsolute(game) || !LocalAbsolute(manifest)
                || string.IsNullOrWhiteSpace(launch) || Path.IsPathRooted(launch) || launch.Contains(':')) return null;
            install = Path.TrimEndingDirectorySeparator(Path.GetFullPath(install));
            string executable = Path.GetFullPath(Path.Combine(install, launch.Replace('/', Path.DirectorySeparatorChar)));
            manifest = Path.GetFullPath(manifest);
            if (!Path.GetExtension(executable).Equals(".exe", StringComparison.OrdinalIgnoreCase)
                || !executable.StartsWith(install + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !executable.Equals(Path.GetFullPath(game), StringComparison.OrdinalIgnoreCase)
                || !existing(executable) || !existing(manifest)) return null;
            string identity = string.Join("\n", ns, item, app, manifest.ToUpperInvariant(), install.ToUpperInvariant(), executable.ToUpperInvariant());
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
            string key = string.Join("%3A", new[] { ns, item, app }.Select(Uri.EscapeDataString));
            return new(ns, item, app, manifest, hash, install, executable,
                "com.epicgames.launcher://apps/" + key + "?action=launch&silent=true");
        }
        catch (Exception e) when (Expected(e)) { return null; }
    }

    // Same narrow InstalledItems-v4 compatibility as EpicInstalledItem: no
    // undocumented general DLC inference and no Shipping-executable substitution.
    internal static EpicInstalledLaunch? ParseInstalledItem(string file, string json, string ns, string item,
        string app, string game, Func<string, bool> existing)
    {
        if (ns != "fn" || app != "Fortnite" || !Identifier(item) || json.Length > 262144) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("v4", out var root) || root.ValueKind != JsonValueKind.Object) return null;
            string Text(JsonElement value, string key) => value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() ?? "" : "";
            bool Empty(JsonElement value, string key) => value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String && field.GetString() == "";
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out var fileId)
                || !Guid.TryParseExact(Text(root, "installationId"), "N", out var id) || id != fileId
                || Text(root, "sandboxId") != ns || Text(root, "itemId") != item || Text(root, "artifactId") != app
                || Text(root, "state") != "Installed" || Text(root, "platform") != "Windows" || !Empty(root, "stagedVersion")
                || root.TryGetProperty("bIsApplication", out var application) && application.ValueKind != JsonValueKind.True
                || root.TryGetProperty("AppCategories", out var categories) && (categories.ValueKind != JsonValueKind.Array
                    || !categories.EnumerateArray().Any(v => v.ValueKind == JsonValueKind.String && v.GetString() == "games"))
                || root.TryGetProperty("pendingTags", out var tags) && (tags.ValueKind != JsonValueKind.Array || tags.GetArrayLength() != 0)
                || !root.TryGetProperty("manifestData", out var data) || data.ValueKind != JsonValueKind.Object
                || Text(data, "version").Length == 0 || !data.TryGetProperty("launchCommand", out var command) || command.ValueKind != JsonValueKind.String
                || !data.TryGetProperty("buildSize", out var size) || !size.TryGetInt64(out long bytes) || bytes <= 0
                || !root.TryGetProperty("pendingManifestData", out var pending) || pending.ValueKind != JsonValueKind.Object || !Empty(pending, "version")) return null;
            string install = Text(root, "dir"), metadata = Text(root, "metaDir"), manifest = Text(root, "manifestPath");
            if (!LocalAbsolute(install) || !LocalAbsolute(metadata) || !LocalAbsolute(manifest)) return null;
            install = Path.TrimEndingDirectorySeparator(Path.GetFullPath(install));
            string expectedMetadata = Path.Combine(install, ".egstore"), expectedManifest = Path.Combine(expectedMetadata, Text(root, "installationId") + ".manifest");
            if (!Path.GetFullPath(metadata).Equals(expectedMetadata, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFullPath(manifest).Equals(expectedManifest, StringComparison.OrdinalIgnoreCase) || !existing(expectedManifest)) return null;
            using (var stream = File.OpenRead(expectedManifest))
            {
                Span<byte> magic = stackalloc byte[4];
                if (stream.Length < 41 || stream.Read(magic) != 4 || System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(magic) != 0x44BEC00C) return null;
            }
            var normalized = new Dictionary<string, object?> { ["CatalogNamespace"] = ns, ["CatalogItemId"] = item, ["AppName"] = app,
                ["InstallLocation"] = install, ["LaunchExecutable"] = Text(data, "launchExe"), ["bIsApplication"] = true };
            return Parse(file, JsonSerializer.Serialize(normalized), ns, item, app, game, existing);
        }
        catch (Exception e) when (Expected(e)) { return null; }
    }

    internal static bool Same(EpicInstalledLaunch a, EpicInstalledLaunch b) => a == b;
    internal static bool Identifier(string text) => text.Length is > 0 and <= 256
        && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');
    internal static bool LocalAbsolute(string path) => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)
        && !path.StartsWith(@"\\", StringComparison.Ordinal) && path.Length <= 32768 && path.IndexOf(':', 2) < 0;
    internal static bool PlainExistingPath(string path)
    {
        if (!LocalAbsolute(path) || !File.Exists(path)) return false;
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
        return true;
    }
    private static IEnumerable<string> ManifestDirectories()
    {
        var roots = new List<string>();
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            try
            {
                using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = machine.OpenSubKey(@"SOFTWARE\Epic Games\EpicGamesLauncher", false);
                if (key?.GetValue("AppDataPath", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string root && LocalAbsolute(root))
                    roots.Add(Path.GetFullPath(Path.Combine(root, "Manifests")));
            }
            catch (Exception e) when (Expected(e)) { }
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests"));
        return roots;
    }
    internal static bool Expected(Exception e) => e is IOException or UnauthorizedAccessException or ArgumentException
        or NotSupportedException or JsonException or SecurityException or System.Security.Cryptography.CryptographicException
        or System.ComponentModel.Win32Exception or OverflowException;
}

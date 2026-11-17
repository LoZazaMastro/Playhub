// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Playhub.StoreLibrary;

internal sealed record WindowsXboxPackage(string PackageFamilyName, string PackageFullName,
    string InstallLocation, string? Aumid, string? ExecutablePath, bool StatusOk,
    string? ManifestExecutable = null, string? StoreProductId = null, string? DisplayName = null, bool IsPlaceholder = false);

// Current-user Windows package registration is authoritative. No title matching,
// directory search, all-users enumeration, or executable-name guessing is used.
internal static class WindowsXboxPackageCatalog
{
    internal static bool ValidFamily(string? family) => family is { Length: > 0 and <= 255 } &&
        Regex.IsMatch(family, @"^[A-Za-z0-9._-]+_[a-z0-9]{13}$");

    internal static Task<WindowsXboxPackage?> FindAsync(string packageFamilyName, CancellationToken token) =>
        ReadAsync(packageFamilyName, StoreToolProcess.QueryAsync, token);

    internal static Task<IReadOnlyDictionary<string, WindowsXboxPackage>> FindManyAsync(IEnumerable<string> families, CancellationToken token) =>
        ReadManyAsync(families, StoreToolProcess.QueryAsync, token);

    // A local installed PC game can be absent from Consumer Collections (for
    // example a newly available backward-compatible package). Its exact StoreId
    // and launch definition are installation evidence, never a purchased grant.
    internal static async Task<IReadOnlyList<WindowsXboxPackage>> FindInstalledGamesAsync(CancellationToken token)
    {
        string json = await QueryAsync([], StoreToolProcess.QueryAsync, token);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
        if (json.Length > 2 * 1024 * 1024 || document.RootElement.GetProperty("Version").GetInt32() != 1)
            throw new StoreOperationException("xbox_package_scan_invalid");
        var result = new List<WindowsXboxPackage>();
        foreach (var row in document.RootElement.GetProperty("Packages").EnumerateArray())
        {
            token.ThrowIfCancellationRequested();
            string? family = row.GetProperty("PackageFamilyName").GetString();
            if (!ValidFamily(family)) continue;
            var package = Parse(family!, JsonSerializer.Serialize(new { Version = 1, Packages = new[] { row } }));
            if (package is { StatusOk: true, StoreProductId: not null, Aumid: not null, ExecutablePath: not null }) result.Add(package);
        }
        if (result.Count > 2000) throw new StoreOperationException("xbox_package_scan_invalid");
        return result;
    }

    internal static async Task<IReadOnlyDictionary<string, WindowsXboxPackage>> ReadManyAsync(IEnumerable<string> families, StoreToolQuery query, CancellationToken token)
    {
        var wanted = families.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (wanted.Length > 2000 || wanted.Any(f => !ValidFamily(f))) throw new StoreOperationException("xbox_package_identity_invalid");
        token.ThrowIfCancellationRequested();
        var found = new Dictionary<string, WindowsXboxPackage>(StringComparer.OrdinalIgnoreCase);
        if (wanted.Length == 0) return found;
        // One bounded metadata query for the whole refresh, with no command-line
        // size growth for large libraries. Only requested families are examined
        // further or returned, and only the current Windows user is enumerated.
        string json = await QueryAsync([], query, token);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (json.Length > 2 * 1024 * 1024 || root.GetProperty("Version").GetInt32() != 1) throw new InvalidDataException();
            foreach (var row in root.GetProperty("Packages").EnumerateArray())
            {
                string family = row.GetProperty("PackageFamilyName").GetString()!;
                if (!wanted.Contains(family, StringComparer.OrdinalIgnoreCase)) continue;
                if (found.ContainsKey(family)) throw new InvalidDataException();
                var parsed = Parse(family, JsonSerializer.Serialize(new { Version = 1, Packages = new[] { row } })) ?? throw new InvalidDataException();
                found.Add(family, parsed);
            }
            return found;
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or InvalidDataException or NullReferenceException)
        { throw new StoreOperationException("xbox_package_scan_invalid"); }
    }

    internal static async Task<WindowsXboxPackage?> ReadAsync(string family, StoreToolQuery query, CancellationToken token)
    {
        if (!ValidFamily(family)) throw new StoreOperationException("xbox_package_identity_invalid");
        token.ThrowIfCancellationRequested();
        return Parse(family, await QueryAsync([family], query, token));
    }

    static async Task<string> QueryAsync(string[] families, StoreToolQuery query, CancellationToken token)
    {
        // Values are restricted to the package-identity alphabet above. The
        // encoded script is a constant current-user query, never user shell code.
        string script = QueryScript.Replace("__PLAYHUB_FAMILIES__", string.Join("','", families), StringComparison.Ordinal);
        string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var result = await query(powershell, ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))], new Dictionary<string, string>(), token);
        token.ThrowIfCancellationRequested();
        if (result.Code != 0) throw new StoreOperationException("xbox_package_scan_failed");
        return result.Json;
    }

    internal static WindowsXboxPackage? Parse(string family, string json)
    {
        if (!ValidFamily(family) || json.Length > 64 * 1024) throw new StoreOperationException("xbox_package_scan_invalid");
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.GetProperty("Version").GetInt32() != 1) throw new InvalidDataException();
            var rows = root.GetProperty("Packages").EnumerateArray().ToArray();
            if (rows.Length == 0) return null;
            if (rows.Length != 1) throw new InvalidDataException();
            var row = rows[0];
            string actualFamily = row.GetProperty("PackageFamilyName").GetString()!;
            string full = row.GetProperty("PackageFullName").GetString()!;
            string directory = row.GetProperty("InstallLocation").GetString()!;
            int separator = family.LastIndexOf('_');
            if (!StringComparer.OrdinalIgnoreCase.Equals(actualFamily, family) || full.Length > 512 ||
                !full.StartsWith(family[..separator] + "_", StringComparison.OrdinalIgnoreCase) ||
                !full.EndsWith("_" + family[(separator + 1)..], StringComparison.OrdinalIgnoreCase) ||
                !Regex.IsMatch(full, @"^[A-Za-z0-9._-]+$") || !LocalAbsolute(directory)) throw new InvalidDataException();
            directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            bool healthy = row.GetProperty("StatusOk").GetBoolean() && row.GetProperty("SignatureKind").GetString() == "Store" &&
                !row.GetProperty("IsFramework").GetBoolean() && !row.GetProperty("IsResourcePackage").GetBoolean();
            var package = new WindowsXboxPackage(actualFamily, full, directory, null, null, healthy);
            if (!healthy) return package;
            try { return ReadLaunch(package); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or XmlException or InvalidOperationException or ArgumentException)
            {
                // Registration is still valid install readback even when its
                // launch definition cannot be proven. Never invent an EXE.
                return package;
            }
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or InvalidDataException or NullReferenceException)
        { throw new StoreOperationException("xbox_package_scan_invalid"); }
    }

    static WindowsXboxPackage ReadLaunch(WindowsXboxPackage package)
    {
        var manifest = Xml(Path.Combine(package.InstallLocation, "AppxManifest.xml"));
        var identity = manifest.Root?.Elements().SingleOrDefault(e => e.Name.LocalName == "Identity");
        string? name = (string?)identity?.Attribute("Name");
        if (name is null || !package.PackageFamilyName.StartsWith(name + "_", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
        var applications = manifest.Root?.Elements().SingleOrDefault(e => e.Name.LocalName == "Applications")?.Elements()
            .Where(e => e.Name.LocalName == "Application").ToArray() ?? [];
        var launches = new List<(string Id, string Relative, string Path)>();
        foreach (var application in applications)
        {
            string? id = (string?)application.Attribute("Id"), executable = (string?)application.Attribute("Executable");
            if (id is null || !Regex.IsMatch(id, @"^[A-Za-z0-9._-]{1,255}$") || executable is null) continue;
            if (Path.GetFileName(executable).Equals("GameLaunchHelper.exe", StringComparison.OrdinalIgnoreCase))
            {
                if ((string?)application.Attribute("EntryPoint") != "Windows.FullTrustApplication") continue;
                if (ContainedExecutable(package.InstallLocation, executable) is null) continue;
                var config = Xml(Path.Combine(package.InstallLocation, "MicrosoftGame.Config"));
                executable = Playhub.Shared.StoreGdkLaunchDefinition.Executable(config, id, applications.Length);
            }
            // A partner's Microsoft Store package can be only a preorder stub.
            // Its registered EXE is not evidence that the actual game arrived.
            if (executable is not null && Path.GetFileName(executable).Equals("GDKStubGame.exe", StringComparison.OrdinalIgnoreCase))
                return package with { IsPlaceholder = true };
            var path = executable is null ? null : ContainedExecutable(package.InstallLocation, executable);
            if (path is not null) launches.Add((id, executable!, path));
        }
        // Multiple application entries require an explicit product/application
        // association. Picking the first could activate a launcher or another app.
        if (launches.Count != 1) return package;
        var launch = launches[0];
        string? storeId = null;
        string configPath = Path.Combine(package.InstallLocation, "MicrosoftGame.Config");
        if (File.Exists(configPath))
        {
            var config = Xml(configPath);
            string? configName = (string?)config.Root?.Elements().SingleOrDefault(e => e.Name.LocalName == "Identity")?.Attribute("Name");
            string? id = config.Root?.Elements().SingleOrDefault(e => e.Name.LocalName == "StoreId")?.Value;
            if (string.Equals(configName, name, StringComparison.Ordinal) && StoreXboxAccountCollections.ValidId(id)) storeId = id;
        }
        string? title = manifest.Root?.Elements().SingleOrDefault(e => e.Name.LocalName == "Properties")?.Elements()
            .SingleOrDefault(e => e.Name.LocalName == "DisplayName")?.Value;
        if (title is not { Length: > 0 and <= 500 } || title.Any(char.IsControl) || title.Contains("ms-resource:", StringComparison.OrdinalIgnoreCase)) title = null;
        return package with { Aumid = package.PackageFamilyName + "!" + launch.Id, ExecutablePath = launch.Path, ManifestExecutable = launch.Relative,
            StoreProductId = storeId, DisplayName = title };
    }

    static XDocument Xml(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > 2 * 1024 * 1024 || (info.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException();
        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2 * 1024 * 1024 });
        return XDocument.Load(reader);
    }
    static bool LocalAbsolute(string? path) => path is { Length: > 3 and <= 32767 } && Path.IsPathFullyQualified(path) &&
        !path.StartsWith(@"\\", StringComparison.Ordinal) && !path.Any(char.IsControl) && !path[2..].Contains(':');
    static string? ContainedExecutable(string directory, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Length > 4096 || Path.IsPathRooted(relative) || relative.Contains(':') ||
            relative.Any(char.IsControl) || !relative.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            relative.Replace('/', '\\').Split('\\').Any(part => part is "" or "." or "..")) return null;
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        string path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return null;
        for (string? current = path; current is not null && !StringComparer.OrdinalIgnoreCase.Equals(current, root); current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return null;
        return path;
    }
    internal const string QueryScript = """
        $ErrorActionPreference='Stop'
        [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
        $families=@('__PLAYHUB_FAMILIES__')
        $rows=@(Get-AppxPackage | Where-Object { $families -contains '' -or $families -contains $_.PackageFamilyName } | ForEach-Object {
          [pscustomobject]@{PackageFamilyName=$_.PackageFamilyName;PackageFullName=$_.PackageFullName;InstallLocation=$_.InstallLocation;StatusOk=([int]$_.Status -eq 0);SignatureKind=[string]$_.SignatureKind;IsFramework=[bool]$_.IsFramework;IsResourcePackage=[bool]$_.IsResourcePackage}
        })
        [pscustomobject]@{Version=1;Packages=$rows}|ConvertTo-Json -Depth 4 -Compress
        """;
}

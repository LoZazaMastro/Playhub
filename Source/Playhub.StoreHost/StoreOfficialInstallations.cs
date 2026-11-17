// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
using Microsoft.Win32;
using Playhub.Shared;
using Playhub.Services;
using System.Security;
using System.Text.Json;

namespace Playhub.StoreLibrary;

internal sealed record StoreOfficialInstallation(StoreGameKey Key, string InstallPath, string Executable,
    string WorkingDirectory, string GameArguments, string? EpicNamespace = null, string? EpicCatalogItemId = null,
    string? EpicRegistrationPath=null,string? EpicManifestPath=null,string? EpicSecondaryRegistrationPath=null);
internal sealed record StoreOfficialInventory(bool IsComplete, IReadOnlyDictionary<string, StoreOfficialInstallation> Games)
{
    internal StoreOfficialInstallation? Find(string id)
    {
        if (!IsComplete) throw new StoreOperationException("installation_inventory_unavailable");
        return Games.GetValueOrDefault(id);
    }
}
internal interface IStoreOfficialInstallations
{
    StoreOfficialInventory Read(StoreKind store, CancellationToken token);
    StoreOfficialInventory ReadForRemoval(StoreKind store,CancellationToken token)=>Read(store,token);
    StoreOfficialGogUninstaller? FindGogUninstaller(string id,string path,CancellationToken token)=>null;
}
internal sealed record StoreOfficialGogUninstaller(string Id,string InstallPath,string Executable,string RegistrationWitness);

/// <summary>Read-only OS/launcher evidence. It never imports credentials or writes private install records.</summary>
internal sealed class StoreOfficialInstallations : IStoreOfficialInstallations
{
    internal sealed record GogRegistration(string Id, string Directory,string? UninstallCommand=null,string? RegistrationSource=null);
    private readonly Func<IEnumerable<string>> epicDirectories;
    private readonly Func<IEnumerable<GogRegistration>> gogRegistrations;
    private readonly Func<string, string, IEnumerable<string>> enumerate;
    private readonly Func<string, string> read;
    private readonly Func<string, bool> existing;
    private readonly Func<string?> epicInstalledItems;

    internal StoreOfficialInstallations(Func<IEnumerable<string>>? epicDirectories = null,
        Func<IEnumerable<GogRegistration>>? gogRegistrations = null,
        Func<string, string, IEnumerable<string>>? enumerate = null, Func<string, string>? read = null,
        Func<string, bool>? existing = null, Func<string?>? epicInstalledItems = null)
    {
        this.epicDirectories = epicDirectories ?? ReadEpicDirectories;
        this.gogRegistrations = gogRegistrations ?? ReadGogRegistrations;
        this.enumerate = enumerate ?? ((directory, pattern) => Directory.EnumerateFiles(directory, pattern));
        this.read = read ?? ReadBounded;
        this.existing = existing ?? PlainFile;
        this.epicInstalledItems = epicInstalledItems ?? (epicDirectories is null
            ? () => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicOnlineServicesShared", "InstallHelper", "InstalledItems")
            : () => null);
    }

    public StoreOfficialInventory Read(StoreKind store, CancellationToken token)=>ReadCore(store,token,false);
    public StoreOfficialInventory ReadForRemoval(StoreKind store,CancellationToken token)=>ReadCore(store,token,true);
    public StoreOfficialGogUninstaller? FindGogUninstaller(string id,string path,CancellationToken token)
    {
        var candidates=new List<(string Executable,string Source)>();
        int observed=0;
        foreach(var registration in gogRegistrations().Take(8193))
        {
            token.ThrowIfCancellationRequested();
            if(++observed>8192)throw new StoreOperationException("installation_inventory_unavailable");
            if(registration.Id!=id||!EpicLaunchCatalog.LocalAbsolute(registration.Directory)||!SamePath(path,registration.Directory)||registration.UninstallCommand is not {} command)continue;
            command=command.Trim();
            if(command.StartsWith('"')){if(command.Length<3||command[^1]!='"'||command.Count(c=>c=='"')!=2)continue;command=command[1..^1];}
            else if(command.Contains('"'))continue;
            if(!EpicLaunchCatalog.LocalAbsolute(command)||!SamePath(Path.GetDirectoryName(command)!,path)||!System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(command),"^unins[0-9]{3}\\.exe$",System.Text.RegularExpressions.RegexOptions.IgnoreCase|System.Text.RegularExpressions.RegexOptions.CultureInvariant))continue;
            candidates.Add((Path.GetFullPath(command),registration.RegistrationSource??"registered-gog-product"));
        }
        if(candidates.Count==0)return null;
        if(candidates.Select(c=>c.Executable).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=1)throw new StoreOperationException("installed_metadata_invalid");
        string witness=string.Join("\n",candidates.Select(c=>c.Source+"\n"+c.Executable.ToUpperInvariant()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        return new(id,Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)),candidates[0].Executable,witness);
    }
    private StoreOfficialInventory ReadCore(StoreKind store,CancellationToken token,bool removal)
    {
        var games = new Dictionary<string, StoreOfficialInstallation>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            if (store == StoreKind.Epic)
            {
                var preferred = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string directory in epicDirectories().Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    token.ThrowIfCancellationRequested();
                    string[] files = enumerate(directory, "*.item").Take(513).ToArray();
                    if (files.Length > 512) return new(false, games);
                    foreach (string file in files)
                    {
                        token.ThrowIfCancellationRequested();
                        preferred.Add(Path.GetFileNameWithoutExtension(file));
                        string json = read(file);
                        using var document = JsonDocument.Parse(json);
                        var data = document.RootElement;
                        if (data.ValueKind != JsonValueKind.Object) continue;
                        string Value(string key) => data.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
                        string id = Value("AppName"), space = Value("CatalogNamespace"), item = Value("CatalogItemId");
                        if (!EpicLaunchCatalog.Identifier(id) || !EpicLaunchCatalog.Identifier(space) || !EpicLaunchCatalog.Identifier(item)) continue;
                        string root = Value("InstallLocation"), relative = Value("LaunchExecutable");
                        if (!EpicLaunchCatalog.LocalAbsolute(root) || string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) continue;
                        string executable = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
                        var verified = EpicLaunchCatalog.Parse(file, json, space, item, id, executable,
                            path=>existing(path)||removal&&SamePath(path,executable));
                        if (verified is null) continue;
                        string? binary=null;string guid=Value("InstallationGuid");
                        if(Guid.TryParseExact(guid,"N",out _)&&Path.GetFileNameWithoutExtension(file).Equals(guid,StringComparison.OrdinalIgnoreCase))
                        {
                            string expected=Path.Combine(verified.InstallDirectory,".egstore",guid+".manifest");
                            string complete=Value("CompleteManifestPath");
                            if(Path.IsPathFullyQualified(complete)&&SamePath(complete,expected)&&existing(expected))binary=expected;
                        }
                        string? secondary=null;
                        if(binary is not null&&epicInstalledItems() is {} helper)
                        {
                            string candidate=Path.Combine(helper,guid+".egi");
                            if(existing(candidate)&&EpicLaunchCatalog.ParseInstalledItem(candidate,read(candidate),space,item,id,executable,path=>existing(path)||removal&&SamePath(path,executable)) is {} companion
                                &&SamePath(companion.InstallDirectory,verified.InstallDirectory))secondary=candidate;
                        }
                        Add(new(new(StoreKind.Epic, id), verified.InstallDirectory, verified.GameExecutable,
                            Path.GetDirectoryName(verified.GameExecutable)!, "", space, item,file,binary,secondary));
                    }
                }
                if (epicInstalledItems() is { } helperDirectory && PlainDirectory(helperDirectory))
                {
                    string[] files = enumerate(helperDirectory, "*.egi").Take(513).ToArray();
                    if (files.Length > 512) return new(false, games);
                    foreach (string file in files)
                    {
                        token.ThrowIfCancellationRequested();
                        if (preferred.Contains(Path.GetFileNameWithoutExtension(file))) continue;
                        string json = read(file);
                        using var document = JsonDocument.Parse(json);
                        if (!document.RootElement.TryGetProperty("v4", out var data) || data.ValueKind != JsonValueKind.Object ||
                            !data.TryGetProperty("manifestData", out var manifest) || manifest.ValueKind != JsonValueKind.Object) continue;
                        string Value(JsonElement value, string key) => value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() ?? "" : "";
                        string id = Value(data, "artifactId"), space = Value(data, "sandboxId"), item = Value(data, "itemId"), root = Value(data, "dir");
                        string relative = Value(manifest, "launchExe");
                        if (!EpicLaunchCatalog.LocalAbsolute(root) || string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) continue;
                        string game = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
                        var verified = EpicLaunchCatalog.ParseInstalledItem(file, json, space, item, id, game,
                            path=>existing(path)||removal&&SamePath(path,game));
                        if (verified is not null) Add(new(new(StoreKind.Epic, id), verified.InstallDirectory, verified.GameExecutable,
                            Path.GetDirectoryName(verified.GameExecutable)!, "", space, item,file,Value(data,"manifestPath")));
                    }
                }
            }
            else if (store == StoreKind.Gog)
            {
                var records = gogRegistrations().Take(8193).ToArray();
                if (records.Length > 8192) return new(false, games);
                foreach (var registration in records)
                {
                    token.ThrowIfCancellationRequested();
                    var installation = ReadGog(registration, read, existing,removal);
                    if (installation is not null) Add(installation);
                }
            }
            else return new(true, games);
        }
        catch (Exception error) when (Expected(error)) { return new(false, games); }
        foreach (string id in ambiguous) games.Remove(id);
        return new(true, games);

        void Add(StoreOfficialInstallation value)
        {
            if (games.TryGetValue(value.Key.Id, out var previous))
            {
                // Duplicate registry views may describe the exact same GOG installation.
                // Two Epic launcher manifests or different roots are ambiguous.
                if (store == StoreKind.Epic || previous.Key != value.Key ||
                    !SamePath(previous.InstallPath, value.InstallPath) || !SamePath(previous.Executable, value.Executable) ||
                    !SamePath(previous.WorkingDirectory, value.WorkingDirectory) || previous.GameArguments != value.GameArguments)
                    ambiguous.Add(value.Key.Id);
            }
            else games.Add(value.Key.Id, value);
        }
    }

    internal static StoreOfficialInstallation? ReadGog(GogRegistration registration, Func<string, string> read, Func<string, bool> existing,bool removal=false)
    {
        string id = registration.Id;
        if (id.Length is < 1 or > 20 || !id.All(char.IsAsciiDigit) || !EpicLaunchCatalog.LocalAbsolute(registration.Directory)) return null;
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(registration.Directory));
        string manifest = Path.Combine(root, "goggame-" + id + ".info");
        if (!existing(manifest)) return null;
        string json = read(manifest);
        if (json.Length > 1024 * 1024) return null;
        using var document = JsonDocument.Parse(json);
        var data = document.RootElement;
        if (data.ValueKind != JsonValueKind.Object || Text(data, "gameId") != id ||
            data.TryGetProperty("rootGameId", out var parent) && (parent.ValueKind != JsonValueKind.String || parent.GetString() != id)) return null;
        if (!data.TryGetProperty("playTasks", out var tasks) || tasks.ValueKind != JsonValueKind.Array || tasks.GetArrayLength() > 128) return null;
        var primary = tasks.EnumerateArray().Where(task => task.ValueKind == JsonValueKind.Object &&
            task.TryGetProperty("isPrimary", out var flag) && flag.ValueKind == JsonValueKind.True).ToArray();
        if (primary.Length != 1) return null;
        var selected = primary[0];
        if (!selected.TryGetProperty("type", out var kind) || !(kind.ValueKind == JsonValueKind.String && kind.GetString() == "FileTask" ||
            kind.ValueKind == JsonValueKind.Number && kind.TryGetInt32(out int numeric) && numeric == 0)) return null;
        string? relative = Text(selected, "path");
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains(':')) return null;
        string executable = Path.GetFullPath(relative.Replace('/', Path.DirectorySeparatorChar), root);
        if (!Within(root, executable) || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !removal&&!existing(executable)) return null;
        foreach (string field in new[] { "arguments", "workingDir" })
            if (selected.TryGetProperty(field, out var value) && value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) return null;
        string arguments = Text(selected, "arguments") ?? "";
        if (arguments.Length > 30000 || arguments.IndexOfAny(['\0', '\r', '\n']) >= 0) return null;
        string rawWorking = Text(selected, "workingDir") ?? "";
        string working = string.IsNullOrWhiteSpace(rawWorking) ? root : Path.GetFullPath(rawWorking.Replace('/', Path.DirectorySeparatorChar), root);
        if (!Within(root, working) || !removal&&!PlainDirectory(working)) return null;
        return new(new(StoreKind.Gog, id), root, executable, working, arguments);
    }

    private static string? Text(JsonElement data, string key) => data.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool Within(string root, string path) => path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static bool SamePath(string left, string right) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)).Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);
    private static bool PlainDirectory(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.Directory) == 0) return false;
            for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
            return true;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return false; }
    }
    private static string ReadBounded(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length is < 1 or > 1024 * 1024) throw new InvalidDataException("Official installation manifest is outside its bound.");
        using var reader = new StreamReader(stream);
        var buffer = new char[1024 * 1024 + 1]; int length = 0;
        while (length < buffer.Length)
        {
            int count = reader.Read(buffer, length, buffer.Length - length);
            if (count == 0) break;
            length += count;
        }
        if (length > 1024 * 1024) throw new InvalidDataException("Official installation manifest grew beyond its bound.");
        return new(buffer, 0, length);
    }
    private static bool PlainFile(string path)
    {
        if (!EpicLaunchCatalog.LocalAbsolute(path)) return false;
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) return false;
            return EpicLaunchCatalog.PlainExistingPath(path);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return false; }
    }
    private static IEnumerable<string> ReadEpicDirectories()
    {
        bool unknown = false;
        var paths = EpicManifestPaths.Resolve(view =>
        {
            try
            {
                using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = machine.OpenSubKey(@"SOFTWARE\Epic Games\EpicGamesLauncher");
                return key?.GetValue("AppDataPath", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            }
            catch (Exception error) when (Expected(error)) { unknown = true; return null; }
        }, directory =>
        {
            try { return (File.GetAttributes(directory) & FileAttributes.Directory) != 0; }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return false; }
            catch (Exception error) when (Expected(error)) { unknown = true; return false; }
        }, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        if (unknown) throw new IOException("Official Epic inventory could not be read.");
        return paths;
    }

    private static IEnumerable<GogRegistration> ReadGogRegistrations()
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var dedicated = root.OpenSubKey(@"SOFTWARE\GOG.com\Games");
            if (dedicated is not null)
            {
                string[] names = dedicated.GetSubKeyNames();
                if (names.Length > 8192) throw new InvalidDataException("Official GOG inventory exceeds its bound.");
                foreach (string id in names)
                {
                    using var key = dedicated.OpenSubKey(id);
                    if (key?.GetValue("path") is string directory) yield return new(id, directory);
                }
            }
            using var programs = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (programs is null) continue;
            string[] programsNames = programs.GetSubKeyNames();
            if (programsNames.Length > 8192) throw new InvalidDataException("Official GOG inventory exceeds its bound.");
            foreach (string name in programsNames)
            {
                if (!name.EndsWith("_is1", StringComparison.OrdinalIgnoreCase)) continue;
                using var key = programs.OpenSubKey(name);
                if (key?.GetValue("Publisher") as string == "GOG.com" && key.GetValue("InstallLocation") is string directory)
                    yield return new(name[..^4], directory,key.GetValue("UninstallString",null,RegistryValueOptions.DoNotExpandEnvironmentNames) as string,$"{hive}:{view}:{name}");
            }
        }
    }
    private static bool Expected(Exception error) => error is IOException or UnauthorizedAccessException or SecurityException
        or ArgumentException or NotSupportedException or JsonException or InvalidOperationException;
}

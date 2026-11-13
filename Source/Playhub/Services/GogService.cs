using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using Playhub.Models;

namespace Playhub.Services;

/// <summary>
/// Trova i giochi installati di GOG (Galaxy o installer offline) leggendo il
/// registro GOG.com\Games e registrazioni installer GOG nei programmi installati.
/// L'installazione resta a GOG: qui generiamo solo le voci da importare come
/// scorciatoie Steam (l'eseguibile reale del gioco).
/// </summary>
public sealed class GogService
{
    private const string GamesPath = @"SOFTWARE\GOG.com\Games";

    public Task<IReadOnlyList<UwpGameEntry>> ScanAsync() => Task.Run<IReadOnlyList<UwpGameEntry>>(Scan);

    private static IReadOnlyList<UwpGameEntry> Scan()
    {
        var games = new List<UwpGameEntry>();

        // GOG è a 32 bit: su Windows a 64 bit le chiavi sono sotto WOW6432Node
        // (gestito da RegistryView.Registry32). Provo anche la vista a 64 bit e HKCU.
        ReadFrom(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32), games);
        ReadFrom(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64), games);
        ReadFrom(RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry32), games);
        ReadFrom(RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64), games);

        return games
            .GroupBy(g => g.LocalExecutablePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static void ReadFrom(RegistryKey baseKey, List<UwpGameEntry> games)
    {
        try
        {
            try { ReadUninstallRecords(baseKey, games); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Uninstall access is optional; still inspect GOG's dedicated key.
            }
            using var gamesKey = baseKey.OpenSubKey(GamesPath);
            if (gamesKey is null)
            {
                return;
            }

            foreach (var id in gamesKey.GetSubKeyNames())
            {
                var entry = FromGameKey(gamesKey, id);
                if (entry is not null)
                {
                    games.Add(entry);
                }
            }
        }
        catch
        {
            // Vista non disponibile o accesso negato: ignora.
        }
        finally
        {
            baseKey.Dispose();
        }
    }

    private static UwpGameEntry? FromGameKey(RegistryKey gamesKey, string id)
    {
        try
        {
            using var key = gamesKey.OpenSubKey(id);
            if (key is null)
            {
                return null;
            }

            var name = key.GetValue("gameName") as string ?? "";
            var path = key.GetValue("path") as string ?? "";
            var exe = key.GetValue("exe") as string ?? "";
            var exeFile = key.GetValue("exeFile") as string ?? "";

            // A present GOG manifest identifies the base game and its primary
            // file task. DLC/support entries must not become independent games.
            if (ValidGameId(id) && Path.IsPathFullyQualified(path) &&
                File.Exists(Path.Combine(path, $"goggame-{id}.info")))
                return ReadInstalledManifest(path, id, name);

            var exePath = ResolveExe(path, exe, exeFile);
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = Path.GetFileNameWithoutExtension(exePath);
            }

            long size = 0;
            try { size = new FileInfo(exePath).Length; } catch { }
            var primaryLaunch = ReadPrimaryLaunch(path, id, exePath);

            return new UwpGameEntry
            {
                Name = name,
                // Identità unica del gioco (come per l'import EXE): senza questa
                // i giochi locali condividerebbero la stessa cache → cover/icone scambiate.
                Aumid = exePath,
                IsLocalExecutable = true,
                LocalExecutablePath = exePath,
                Executable = exePath,
                SourceLaunchArguments = primaryLaunch?.Arguments ?? (key.GetValue("launchParam") as string ?? ""),
                SourceWorkingDirectory = primaryLaunch?.WorkingDirectory,
                FileSize = size
            };
        }
        catch
        {
            return null;
        }
    }


    private static void ReadUninstallRecords(RegistryKey baseKey, List<UwpGameEntry> games)
    {
        using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
        if (uninstall is null) return;
        foreach (var id in uninstall.GetSubKeyNames())
        {
            try
            {
                using var key = uninstall.OpenSubKey(id);
                if (key is null) continue;
                var entry = FromUninstallRecord(id, key.GetValue("Publisher") as string,
                    key.GetValue("DisplayName") as string, key.GetValue("InstallLocation") as string);
                if (entry is not null) games.Add(entry);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // A single inaccessible registration must not hide other games.
            }
        }
    }

    internal static UwpGameEntry? FromUninstallRecord(string keyName, string? publisher, string? name, string? directory)
    {
        // GOG's registered offline/Galaxy installer identifier, not a folder scan.
        const string suffix = "_is1";
        if (!string.Equals(publisher, "GOG.com", StringComparison.Ordinal) ||
            !keyName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return null;
        string id = keyName[..^suffix.Length];
        return ValidGameId(id) ? ReadInstalledManifest(directory ?? "", id, name ?? "") : null;
    }

    private static bool ValidGameId(string id) => id.Length is > 0 and <= 20 && id.All(c => c is >= '0' and <= '9');

    internal static UwpGameEntry? ReadInstalledManifest(string directory, string id, string name)
    {
        if (!ValidGameId(id) || !Path.IsPathFullyQualified(directory)) return null;
        try
        {
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            if (!Directory.Exists(root)) return null;
            string manifest = Path.Combine(root, $"goggame-{id}.info");
            if (!File.Exists(manifest) || new FileInfo(manifest).Length > 1024 * 1024) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(manifest));
            var data = document.RootElement;
            if (data.ValueKind != JsonValueKind.Object) return null;
            foreach (string field in new[] { "gameId", "rootGameId" })
            {
                if (!data.TryGetProperty(field, out var value)) continue;
                if (value.ValueKind != JsonValueKind.String) return null;
                string declared = value.GetString() ?? "";
                if (declared.Length != 0 && !string.Equals(declared, id, StringComparison.Ordinal)) return null;
            }
            if (!data.TryGetProperty("playTasks", out var tasks) || tasks.ValueKind != JsonValueKind.Array) return null;
            var primary = tasks.EnumerateArray().Where(t => t.ValueKind == JsonValueKind.Object &&
                t.TryGetProperty("isPrimary", out var flag) && flag.ValueKind == JsonValueKind.True).ToArray();
            if (primary.Length != 1) return null;
            var task = primary[0];
            if (task.TryGetProperty("type", out var type) && !(
                type.ValueKind == JsonValueKind.String && type.GetString() == "FileTask" ||
                type.ValueKind == JsonValueKind.Number && type.TryGetInt32(out int number) && number == 0)) return null;
            foreach (string field in new[] { "workingDir", "arguments" })
                if (task.TryGetProperty(field, out var optional) && optional.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) return null;
            string? declaredPath = ReadString(task, "path");
            if (string.IsNullOrWhiteSpace(declaredPath)) return null;
            string executable = Path.GetFullPath(declaredPath.Replace('/', Path.DirectorySeparatorChar), root);
            if (!WithinRoot(root, executable) || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(executable)) return null;
            string declaredWorking = ReadString(task, "workingDir") ?? "";
            string working = string.IsNullOrWhiteSpace(declaredWorking) ? root : Path.GetFullPath(declaredWorking.Replace('/', Path.DirectorySeparatorChar), root);
            if (!WithinRoot(root, working) || !Directory.Exists(working)) return null;
            string title = string.IsNullOrWhiteSpace(name) ? ReadString(data, "name") ?? "" : name;
            if (string.IsNullOrWhiteSpace(title)) title = Path.GetFileNameWithoutExtension(executable);
            return new UwpGameEntry
            {
                Name = title,
                Aumid = executable, IsLocalExecutable = true, LocalExecutablePath = executable, Executable = executable,
                SourceWorkingDirectory = working, SourceLaunchArguments = ReadString(task, "arguments") ?? "",
                FileSize = new FileInfo(executable).Length
            };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or JsonException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool WithinRoot(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    internal static (string WorkingDirectory, string Arguments)? ReadPrimaryLaunch(string installPath, string gameId, string executable)
    {
        if (!Path.IsPathFullyQualified(installPath) || gameId.Length == 0 || gameId.Any(c => c is < '0' or > '9')) return null;
        try
        {
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installPath));
            string target = Path.GetFullPath(executable);
            string manifest = Path.Combine(root, $"goggame-{gameId}.info");
            if (!File.Exists(manifest)) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(manifest));
            if (!document.RootElement.TryGetProperty("playTasks", out var tasks) || tasks.ValueKind != JsonValueKind.Array)
                return null;
            var matches = tasks.EnumerateArray().Where(task =>
                task.ValueKind == JsonValueKind.Object && task.TryGetProperty("isPrimary", out var primary) &&
                primary.ValueKind == JsonValueKind.True && ReadString(task, "path") is { Length: > 0 } taskPath &&
                string.Equals(Path.GetFullPath(taskPath, root), target, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1) return null;
            // GOG's default for a primary file task is the installation directory.
            string relative = ReadString(matches[0], "workingDir") ?? "";
            string directory = string.IsNullOrWhiteSpace(relative) ? root : Path.GetFullPath(relative, root);
            string relativeDirectory = Path.GetRelativePath(root, directory);
            if (relativeDirectory == ".." || relativeDirectory.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || Path.IsPathRooted(relativeDirectory) || !Directory.Exists(directory)) return null;
            return (directory, ReadString(matches[0], "arguments") ?? "");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string ResolveExe(string path, string exe, string exeFile)
    {
        if (!string.IsNullOrWhiteSpace(exe) && Path.IsPathRooted(exe))
        {
            return exe;
        }

        if (!string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(exeFile))
        {
            return Path.Combine(path, exeFile);
        }

        if (!string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(exe))
        {
            return Path.Combine(path, exe);
        }

        return "";
    }
}

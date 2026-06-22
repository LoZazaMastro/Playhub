using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Playhub.Models;

namespace Playhub.Services;

/// <summary>
/// Trova i giochi installati dell'Epic Games Store leggendo i manifest
/// in %ProgramData%\Epic\EpicGamesLauncher\Data\Manifests\*.item.
/// L'installazione resta all'Epic Games Launcher: qui generiamo solo le voci
/// da importare come scorciatoie Steam (l'eseguibile reale del gioco).
/// </summary>
public sealed class EpicGamesService
{
    private static string ManifestsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Epic", "EpicGamesLauncher", "Data", "Manifests");

    public Task<IReadOnlyList<UwpGameEntry>> ScanAsync() => Task.Run<IReadOnlyList<UwpGameEntry>>(Scan);

    private static IReadOnlyList<UwpGameEntry> Scan()
    {
        var dir = ManifestsDir;
        if (!Directory.Exists(dir))
        {
            return Array.Empty<UwpGameEntry>();
        }

        var games = new List<UwpGameEntry>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.item"))
        {
            var entry = TryParse(file);
            if (entry is not null)
            {
                games.Add(entry);
            }
        }

        return games
            .GroupBy(g => g.LocalExecutablePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static UwpGameEntry? TryParse(string manifestPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = doc.RootElement;

            var installLocation = GetString(root, "InstallLocation");
            var launchExe = GetString(root, "LaunchExecutable");
            var displayName = GetString(root, "DisplayName");

            if (string.IsNullOrWhiteSpace(installLocation) || string.IsNullOrWhiteSpace(launchExe))
            {
                return null;
            }

            // Solo applicazioni giocabili (esclude DLC, plugin, tool dell'engine).
            if (root.TryGetProperty("bIsApplication", out var isApp) &&
                isApp.ValueKind == JsonValueKind.False)
            {
                return null;
            }

            if (HasCategories(root) && !HasGameCategory(root))
            {
                return null;
            }

            var exePath = Path.GetFullPath(Path.Combine(installLocation, launchExe.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(exePath))
            {
                return null;
            }

            var name = string.IsNullOrWhiteSpace(displayName)
                ? Path.GetFileNameWithoutExtension(exePath)
                : displayName;

            long size = 0;
            try { size = new FileInfo(exePath).Length; } catch { }

            return new UwpGameEntry
            {
                Name = name,
                // Identità unica del gioco (come per l'import EXE): senza questa
                // i giochi locali condividerebbero la stessa cache → cover/icone scambiate.
                Aumid = exePath,
                IsLocalExecutable = true,
                LocalExecutablePath = exePath,
                Executable = exePath,
                FileSize = size
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool HasCategories(JsonElement root) =>
        root.TryGetProperty("AppCategories", out var cats) && cats.ValueKind == JsonValueKind.Array;

    private static bool HasGameCategory(JsonElement root)
    {
        if (!root.TryGetProperty("AppCategories", out var cats) || cats.ValueKind != JsonValueKind.Array)
        {
            return true;
        }

        return cats.EnumerateArray()
            .Any(c => c.ValueKind == JsonValueKind.String &&
                      string.Equals(c.GetString(), "games", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}

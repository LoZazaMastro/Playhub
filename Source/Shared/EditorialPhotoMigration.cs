using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Collections.Generic;

namespace Playhub.Shared;

public static class EditorialPhotoMigration
{
    public const string AssetDirectory = "Assets/GamingModeDeckyPlugin/gaming-mode/dist/assets";
    public static int Prune(string installRoot, Action<string>? log = null)
        => PrunePlugin(installRoot, Path.Combine(installRoot, "Assets", "GamingModeDeckyPlugin", "gaming-mode"), log);

    // These filenames are retired from the streaming bundle. Omitting them when
    // installing avoids copying photos that PrunePlugin would immediately remove.
    // Actual deletion still requires the exact approved file hash below.
    public static HashSet<string> GetRetiredAssetNames(string installRoot, string pluginRoot)
    {
        try { return ReadApprovedLegacyAssets(installRoot, pluginRoot).Keys.ToHashSet(StringComparer.OrdinalIgnoreCase); }
        catch { return new(StringComparer.OrdinalIgnoreCase); }
    }

    private static Dictionary<string, string> ReadApprovedLegacyAssets(string installRoot, string pluginRoot)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string metadata = Path.Combine(Path.GetFullPath(installRoot), "Assets", "EditorialStreaming");
        string plugin = Path.GetFullPath(pluginRoot);
        if (!Path.GetFileName(plugin.TrimEnd(Path.DirectorySeparatorChar)).Equals("gaming-mode", StringComparison.OrdinalIgnoreCase)) return result;
        string assets = Path.Combine(plugin, "dist", "assets");
        string bundle = Path.Combine(plugin, "dist", "index.js");
        if (HasReparseAncestor(metadata) || HasReparseAncestor(assets) || !File.Exists(bundle) ||
            !File.ReadAllText(bundle).Contains("assets/on-this-day/v1/", StringComparison.Ordinal)) return result;
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(metadata, "manifest.json")));
        using var legacy = JsonDocument.Parse(File.ReadAllText(Path.Combine(metadata, "legacy-photos.json")));
        var approved = manifest.RootElement.GetProperty("files").EnumerateObject()
            .Select(item => item.Value.GetProperty("sha256").GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in legacy.RootElement.GetProperty("files").EnumerateArray())
        {
            string? name = entry.GetProperty("name").GetString();
            string? expected = entry.GetProperty("sha256").GetString();
            if (string.IsNullOrWhiteSpace(name) || name.Contains('/') || name.Contains('\\') ||
                name.Contains(':') || name.Contains("..") ||
                name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || string.IsNullOrWhiteSpace(expected) || !approved.Contains(expected)) continue;
            result[name] = expected;
        }
        return result;
    }

    public static int PrunePlugin(string installRoot, string pluginRoot, Action<string>? log = null)
    {
        int removed = 0;
        try
        {
            string plugin = Path.GetFullPath(pluginRoot);
            string assets = Path.Combine(plugin, "dist", "assets");
            // Una lista legacy da sola non autorizza la rimozione: il payload deve
            // contenere anche il catalogo remoto e la build capace di leggerlo.
            foreach (var entry in ReadApprovedLegacyAssets(installRoot, pluginRoot))
            {
                string expected = entry.Value;
                string file = Path.Combine(assets, entry.Key);
                if (!File.Exists(file) || HasReparseAncestor(file)) continue;
                try
                {
                    using (var stream = File.OpenRead(file))
                        if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase)) continue;
                    File.Delete(file);
                    removed++;
                }
                catch (Exception error) { log?.Invoke("Editorial asset retained: " + error.Message); }
            }
        }
        catch (Exception error) { log?.Invoke("Editorial migration skipped: " + error.Message); }
        return removed;
    }

    private static bool HasReparseAncestor(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
        return false;
    }
}

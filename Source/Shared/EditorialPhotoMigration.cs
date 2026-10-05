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

    public static int PrunePlugin(string installRoot, string pluginRoot, Action<string>? log = null)
    {
        int removed = 0;
        try
        {
            string root = Path.GetFullPath(installRoot);
            string metadata = Path.Combine(root, "Assets", "EditorialStreaming");
            string plugin = Path.GetFullPath(pluginRoot);
            if (!Path.GetFileName(plugin.TrimEnd(Path.DirectorySeparatorChar)).Equals("gaming-mode", StringComparison.OrdinalIgnoreCase)) return 0;
            string assets = Path.Combine(plugin, "dist", "assets");
            string bundle = Path.Combine(plugin, "dist", "index.js");
            if (HasReparseAncestor(metadata) || HasReparseAncestor(assets) || !File.Exists(bundle) ||
                !File.ReadAllText(bundle).Contains("assets/on-this-day/v1/", StringComparison.Ordinal)) return 0;
            // Una lista legacy da sola non autorizza la rimozione: il payload deve
            // contenere anche il catalogo remoto e la build capace di leggerlo.
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(metadata, "manifest.json")));
            using var legacy = JsonDocument.Parse(File.ReadAllText(Path.Combine(metadata, "legacy-photos.json")));
            var approved = manifest.RootElement.GetProperty("files").EnumerateObject()
                .Select(item => item.Value.GetProperty("sha256").GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in legacy.RootElement.GetProperty("files").EnumerateArray())
            {
                string? name = entry.GetProperty("name").GetString();
                string? expected = entry.GetProperty("sha256").GetString();
                if (string.IsNullOrWhiteSpace(name) || name.Contains('/') || name.Contains('\\') ||
                    name.Contains(':') || name.Contains("..") || name.Contains("cover", StringComparison.OrdinalIgnoreCase) ||
                    name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !approved.Contains(expected)) continue;
                string file = Path.Combine(assets, name);
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

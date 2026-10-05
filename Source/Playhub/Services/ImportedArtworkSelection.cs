using Playhub.Models;

namespace Playhub.Services;

internal static class ImportedArtworkSelection
{
    internal static readonly string[] Types = ["cover", "banner", "hero", "logo", "icon"];

    internal static string Get(UwpGameEntry game, string type) => type switch
    {
        "cover" => game.SteamGridDbCoverPath, "banner" => game.SteamGridDbBannerPath,
        "hero" => game.SteamGridDbHeroPath, "logo" => game.SteamGridDbLogoPath,
        "icon" => game.SteamGridDbIconPath, _ => throw new ArgumentException("Unknown artwork type.")
    };

    internal static void Set(UwpGameEntry game, string type, string? path)
    {
        path ??= "";
        switch (type)
        {
            case "cover": game.SteamGridDbCoverPath = path; break;
            case "banner": game.SteamGridDbBannerPath = path; break;
            case "hero": game.SteamGridDbHeroPath = path; break;
            case "logo": game.SteamGridDbLogoPath = path; break;
            case "icon": game.SteamGridDbIconPath = path; break;
            default: throw new ArgumentException("Unknown artwork type.");
        }
    }

    internal static void Choose(UwpGameEntry game, string type, string? path)
    {
        Set(game, type, path);
        game.ArtworkChoices[type] = path;
    }

    internal static void RefreshSteam(UwpGameEntry game, IReadOnlyDictionary<string, string> actual)
    {
        game.SteamArtworkTypes.Clear();
        foreach (var type in Types)
        {
            if (actual.TryGetValue(type, out var path) && File.Exists(path))
            {
                game.SteamArtworkTypes.Add(type);
                if (!game.ArtworkChoices.ContainsKey(type)) Set(game, type, path);
            }
            // Explicit removals are choices too; an automatic refresh cannot resurrect them.
            if (game.ArtworkChoices.TryGetValue(type, out var chosen)) Set(game, type, chosen);
        }
    }

    internal static void ClearAutomatic(UwpGameEntry game, string type)
    {
        if (!game.ArtworkChoices.ContainsKey(type) && !game.SteamArtworkTypes.Contains(type)) Set(game, type, null);
    }

    internal static IReadOnlyDictionary<string, string> Current(UwpGameEntry game, IReadOnlyDictionary<string, string> actual)
    {
        var selected = new Dictionary<string, string>(actual, StringComparer.OrdinalIgnoreCase);
        foreach (var type in Types)
        {
            if (game.ArtworkChoices.TryGetValue(type, out var chosen))
            {
                selected.Remove(type);
                if (chosen is not null && File.Exists(chosen)) selected[type] = chosen;
            }
            else if (!selected.ContainsKey(type) && File.Exists(Get(game, type))) selected[type] = Get(game, type);
        }
        return selected;
    }

    internal static void RemoveExactFiles(string grid, uint appId, string type, string backup, CancellationToken ct)
    {
        var name = type switch { "cover" => appId + "p", "banner" => appId.ToString(), "hero" => appId + "_hero", "logo" => appId + "_logo", _ => throw new ArgumentException("Unknown removable grid asset.") };
        GuardPath(grid); GuardPath(backup);
        var moved = new List<(string Source, string Backup)>();
        try
        {
            foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".avif" })
            {
                ct.ThrowIfCancellationRequested();
                var source = Path.Combine(grid, name + ext);
                if (!File.Exists(source)) continue;
                GuardPath(source); Directory.CreateDirectory(backup);
                var original = Path.Combine(backup, name + ext);
                File.Copy(source, original, false);
                moved.Add((source, original));
                File.Delete(source);
            }
        }
        catch
        {
            foreach (var saved in moved) if (!File.Exists(saved.Source)) File.Copy(saved.Backup, saved.Source, false);
            throw;
        }
    }

    internal static void ReplaceExactFile(string grid, uint appId, string type, string source, string backup, CancellationToken ct)
    {
        var name = type switch { "cover" => appId + "p", "banner" => appId.ToString(), "hero" => appId + "_hero", "logo" => appId + "_logo", _ => throw new ArgumentException("Unknown grid asset.") };
        var extension = Path.GetExtension(source).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".avif")) throw new ArgumentException("Unknown artwork file format.");
        GuardPath(grid); GuardPath(source); GuardPath(backup);
        var destination = Path.Combine(grid, name + extension);
        GuardPath(destination);
        if (!File.Exists(source)) throw new FileNotFoundException("The selected artwork is no longer available.", source);
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase)) return;
        Directory.CreateDirectory(grid);
        var temporary = Path.Combine(grid, ".playhub-artwork-" + Guid.NewGuid().ToString("N") + ".tmp");
        var originalsRemoved = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            // Finish reading the replacement before touching any saved Steam asset.
            File.Copy(source, temporary, false);
            ct.ThrowIfCancellationRequested();
            RemoveExactFiles(grid, appId, type, backup, ct);
            originalsRemoved = true;
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, destination, false);
        }
        catch
        {
            if (originalsRemoved && Directory.Exists(backup))
                foreach (var original in Directory.EnumerateFiles(backup))
                {
                    var restore = Path.Combine(grid, Path.GetFileName(original));
                    if (!File.Exists(restore)) File.Copy(original, restore, false);
                }
            throw;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void GuardPath(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Artwork paths cannot traverse a link.");
    }
}

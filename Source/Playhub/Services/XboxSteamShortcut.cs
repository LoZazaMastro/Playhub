using VDFParser.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Playhub.Services;

internal static class XboxSteamShortcut
{
    internal static VDFEntry Create(VDFEntry? existing, int appId, string name, string sessionExe,
        string startDir, string icon, string launchOptions, bool localExecutable, int lastPlayTime) => new()
    {
        appid = existing?.appid ?? appId,
        Index = existing?.Index ?? 0,
        AppName = name,
        Exe = "\"" + sessionExe + "\"",
        StartDir = !string.IsNullOrWhiteSpace(existing?.StartDir) ? existing.StartDir : startDir,
        Icon = !string.IsNullOrWhiteSpace(existing?.Icon) ? existing.Icon : icon,
        ShortcutPath = existing?.ShortcutPath ?? "",
        LaunchOptions = launchOptions,
        IsHidden = existing?.IsHidden ?? 0,
        // Steam otherwise applies its desktop layout to COM-activated UWP
        // windows outside the tracked launcher tree. Keep the game's layout.
        AllowDesktopConfig = 0,
        AllowOverlay = 1,
        OpenVR = existing?.OpenVR ?? 0,
        Devkit = existing?.Devkit ?? 0,
        DevkitGameID = existing?.DevkitGameID ?? "",
        LastPlayTime = existing?.LastPlayTime ?? lastPlayTime,
        Tags = existing?.Tags?.ToArray() ?? new[] { localExecutable ? "Playhub" : "Xbox" }
    };

    internal static bool HasAmbiguousMatches(IEnumerable<VDFEntry[]> profiles, Func<VDFEntry, bool> matches)
    {
        int? identity = null;
        foreach (var profile in profiles)
        {
            var found = profile.Where(matches).ToArray();
            if (found.Length > 1) return true;
            if (found.Length == 0) continue;
            if (identity.HasValue && identity.Value != found[0].appid) return true;
            identity = found[0].appid;
        }
        return false;
    }

    internal static int ResolveAppId(IEnumerable<VDFEntry[]> profiles, Func<VDFEntry, bool> matches, int newAppId)
    {
        var snapshot = profiles.ToArray();
        if (HasAmbiguousMatches(snapshot, matches)) throw new InvalidOperationException("Ambiguous Steam shortcut identity.");
        return snapshot.SelectMany(profile => profile).FirstOrDefault(matches)?.appid ?? newAppId;
    }

    internal static void ApplyArtworkIfMissing(string userPath, uint appId, string type, string source,
        Action<string, uint, string, string> apply)
    {
        string name = type switch
        {
            "banner" => appId.ToString(), "hero" => appId + "_hero", "logo" => appId + "_logo", _ => appId + "p"
        };
        string grid = Path.Combine(userPath, "config", "grid");
        if (new[] { ".png", ".jpg", ".jpeg", ".webp" }.Any(extension => File.Exists(Path.Combine(grid, name + extension)))) return;
        apply(userPath, appId, type, source);
    }
}

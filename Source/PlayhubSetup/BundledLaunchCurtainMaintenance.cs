using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Playhub.Models;
using Playhub.Services;

namespace PlayhubSetup;

internal sealed record BundledMaintenancePlan(string Status, DeckyPluginInfo? Plugin = null,
    string ZipSha256 = "", int ChangedFiles = 0);
internal sealed record BundledMaintenanceResult(string Status, string Version = "", string ZipSha256 = "",
    int ChangedFiles = 0, bool Reloaded = false);

// Maintains only an existing optional Launch Curtain. No new optional installation,
// internet resolution, data-directory writes, settings mutation, focus or Steam restart.
internal static class BundledLaunchCurtainMaintenance
{
    internal const string FolderName = "launch-curtain";
    internal const string ReloadMarker = ".playhub-lc-reload-pending";
    internal const string ReloadScript = """
        (async () => {
          const store = window.SteamUIStore, bridge = window.DeckyBackend, loader = window.DeckyPluginLoader;
          if (typeof store?.IsAnyAppRunning !== 'function' || typeof bridge?.call !== 'function'
              || !Array.isArray(loader?.plugins)) return 'unavailable';
          if (store.IsAnyAppRunning() !== false) return 'busy';
          const existing = loader.plugins.filter(p => p.name === 'Launch Curtain');
          if (existing.length !== 1) return 'unavailable';
          const disabled = await bridge.call('utilities/settings/get', 'disabled_plugins', []);
          if (disabled !== null && (!Array.isArray(disabled) || disabled.some(x => typeof x !== 'string'))) return 'unavailable';
          const ours = x => typeof x === 'string' && ['launch curtain','launch-curtain'].includes(x.toLowerCase());
          if ((disabled ?? []).some(ours)) return 'disabled';
          if (window.DeckyPluginLoader !== loader || window.DeckyBackend !== bridge || window.SteamUIStore !== store
              || loader.plugins.filter(p => p.name === 'Launch Curtain').length !== 1
              || !loader.plugins.includes(existing[0])) return 'unavailable';
          if (store.IsAnyAppRunning() !== false) return 'busy';
          const result = await bridge.call('loader/reload_plugin', 'Launch Curtain');
          return result === false ? 'unavailable' : 'reloaded';
        })()
        """;

    internal static async Task MaintainInstalledAsync(string installDirectory)
    {
        string plugins = AppPaths.DefaultDeckyPluginsPath;
        string settings = AppPaths.SettingsFile;
        // Read only: do not invoke SettingsService.LoadAsync (it writes migrations).
        if (File.Exists(settings))
        {
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(settings));
            if (doc.RootElement.TryGetProperty("DeckyPluginsPath", out var value) && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString())) plugins = value.GetString()!;
        }
        string zip = Path.Combine(installDirectory, "Plugins", "Launch Curtain", "Launch-Curtain_Installer-2.5.8.zip");
        var result = await RunAsync(zip, plugins,
            (plugin, path, allowed) => new DeckyPluginService().InstallOrUpdateAsync(plugin, path, bundledOnly: true, canInstall: allowed),
            Playhub.Integrations.SteamPluginBridge.EvaluateAsync,
            () => BundledPluginProcessGuard.Read(installDirectory));
        Directory.CreateDirectory(AppPaths.LocalDataRoot);
        await File.WriteAllTextAsync(Path.Combine(AppPaths.LocalDataRoot, "bundled-lc-maintenance.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal static async Task<BundledMaintenanceResult> RunAsync(string zip, string plugins,
        Func<DeckyPluginInfo, string, Func<bool>, Task> install,
        Func<string, CancellationToken, Task<JsonNode?>> evaluate,
        Func<BundledProcessSafety> processSafety)
    {
        var plan = Inspect(zip, plugins);
        if (plan.Plugin is null)
        {
            if (plan.Status == "current")
            {
                string pending = Path.Combine(plugins, FolderName, ReloadMarker);
                if (File.Exists(pending) && await File.ReadAllTextAsync(pending) == plan.ZipSha256)
                    return await CompleteReloadAsync(pending, "2.5.8", plan.ZipSha256, 0, evaluate, processSafety);
            }
            return new(plan.Status, ZipSha256: plan.ZipSha256);
        }
        var plugin = plan.Plugin;
        // Keep the exact packaged archive pinned against write/delete through normal installation.
        using var pinnedZip = new FileStream(zip, FileMode.Open, FileAccess.Read, FileShare.Read);
        // A second byte/identity/version check closes changes while planning.
        var current = Inspect(zip, plugins);
        if (current.Plugin is null || current.ZipSha256 != plan.ZipSha256 || current.Plugin.InstalledVersion != plugin.InstalledVersion)
            return new("changed-before-install", plugin.Version, plan.ZipSha256);
        BundledProcessSafety last = BundledProcessSafety.Unknown;
        bool InstallationAllowed()
        {
            last = ReadSafety(processSafety);
            return last == BundledProcessSafety.Clear;
        }
        if (!InstallationAllowed()) return Deferred(last, plugin.Version, plan.ZipSha256);
        try { await install(plugin, plugins, InstallationAllowed); }
        catch (OperationCanceledException) when (last != BundledProcessSafety.Clear)
        { return Deferred(last, plugin.Version, plan.ZipSha256); }
        var after = Inspect(zip, plugins);
        if (after.Status != "current") throw new IOException("Launch Curtain bundled payload verification failed.");
        string marker = Path.Combine(plugin.InstalledFolder, ReloadMarker);
        await File.WriteAllTextAsync(marker, plan.ZipSha256);
        return await CompleteReloadAsync(marker, plugin.Version, plan.ZipSha256, plan.ChangedFiles, evaluate, processSafety);
    }

    private static async Task<BundledMaintenanceResult> CompleteReloadAsync(string marker, string version,
        string digest, int changed, Func<string, CancellationToken, Task<JsonNode?>> evaluate,
        Func<BundledProcessSafety> processSafety)
    {
        // Steam can claim no running app while the exact wrapper/game is alive.
        // This native process veto is independent of SteamUIStore's idle flag.
        var state = ReadSafety(processSafety);
        if (state != BundledProcessSafety.Clear)
            return new(state == BundledProcessSafety.Active ? "updated-reload-deferred-active-session" : "updated-reload-deferred-uncertain-session",
                version, digest, changed);
        bool reloaded = false;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var receipt = await evaluate(ReloadScript, timeout.Token).WaitAsync(timeout.Token);
            reloaded = receipt?.GetValue<string>() == "reloaded";
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or HttpRequestException or OperationCanceledException or JsonException) { }
        if (reloaded && File.Exists(marker) && await File.ReadAllTextAsync(marker) == digest) File.Delete(marker);
        return new(reloaded ? "updated-reloaded" : "updated-reload-pending", version, digest, changed, reloaded);
    }

    private static BundledProcessSafety ReadSafety(Func<BundledProcessSafety> read)
    {
        try { return read(); }
        catch { return BundledProcessSafety.Unknown; }
    }
    private static BundledMaintenanceResult Deferred(BundledProcessSafety state, string version, string digest) =>
        new(state == BundledProcessSafety.Active ? "deferred-active-session" : "deferred-uncertain-session", version, digest);

    internal static BundledMaintenancePlan Inspect(string zip, string plugins)
    {
        string destination = Path.Combine(plugins, FolderName);
        if (!Directory.Exists(destination)) return new("not-installed");
        if ((File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0) return new("reparse-preserved");
        if (!File.Exists(zip)) return new("payload-unavailable");
        try
        {
            using var archive = ZipFile.OpenRead(zip);
            // PowerShell's normal Windows ZIPs retain backslash member names.
            // Validate canonical ZIP paths before looking up manifests or files;
            // normalization must not hide traversal or mixed-separator collisions.
            var allEntries = archive.Entries.Select(e => new {
                Entry = e, Name = e.FullName.Replace('\\', '/')
            }).ToArray();
            var entries = allEntries.Where(e => !e.Name.EndsWith('/')).ToArray();
            const string prefix = "launch-curtain/";
            if (entries.Length == 0 || allEntries.Any(e => !e.Name.StartsWith(prefix, StringComparison.Ordinal)
                || (e.Name.EndsWith('/') ? e.Name[..^1] : e.Name).Split('/').Any(p =>
                    p.Length == 0 || p is ".." or "." || p.Contains(':') || p.EndsWith(' ') || p.EndsWith('.'))))
                return new("payload-invalid");
            if (allEntries.Select(e => e.Name.TrimEnd('/')).Distinct(StringComparer.OrdinalIgnoreCase).Count() != allEntries.Length)
                return new("payload-invalid");
            var manifest = entries.Single(e => e.Name == prefix + "plugin.json");
            using (var data = JsonDocument.Parse(manifest.Entry.Open()))
                if (data.RootElement.GetProperty("name").GetString() != "Launch Curtain") return new("payload-invalid");
            string version;
            using (var data = JsonDocument.Parse(entries.Single(e => e.Name == prefix + "package.json").Entry.Open()))
                version = data.RootElement.GetProperty("version").GetString() ?? "";
            if (!StableVersion(version, out var bundled)) return new("payload-version-unknown");
            string installed;
            using (var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(destination, "plugin.json"))))
                if (data.RootElement.GetProperty("name").GetString() != "Launch Curtain") return new("identity-preserved");
            using (var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(destination, "package.json"))))
                installed = data.RootElement.GetProperty("version").GetString() ?? "";
            if (!StableVersion(installed, out var old)) return new("installed-version-unknown");
            if (old > bundled) return new("newer-preserved");
            string releaseMarker = Path.Combine(destination, ".playhub-release.json");
            if (!File.Exists(releaseMarker)) releaseMarker = Path.Combine(destination, "release-info.json");
            if (File.Exists(releaseMarker))
            {
                using var data = JsonDocument.Parse(File.ReadAllText(releaseMarker));
                if (!StableVersion(data.RootElement.GetProperty("version").GetString() ?? "", out var marked)) return new("installed-version-unknown");
                if (marked > bundled) return new("newer-preserved");
            }
            // Existing LC stores configuration/artwork/sounds under homebrew/data. A
            // still-unmigrated legacy payload must be preserved rather than deleted.
            if (File.Exists(Path.Combine(destination, "launch-curtain.json"))
                || Directory.Exists(Path.Combine(destination, "launch-images"))
                || Directory.Exists(Path.Combine(destination, "soundbites"))) return new("legacy-data-preserved");
            int changed = 0;
            foreach (var entry in entries)
            {
                string path = Path.Combine(destination, entry.Name[prefix.Length..].Replace('/', Path.DirectorySeparatorChar));
                for (string? directory = Path.GetDirectoryName(path); directory is not null
                    && !string.Equals(directory, destination, StringComparison.OrdinalIgnoreCase); directory = Path.GetDirectoryName(directory))
                    if (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                        return new("reparse-preserved");
                if (!File.Exists(path)) { changed++; continue; }
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return new("reparse-preserved");
                using var source = entry.Entry.Open();
                using var file = File.OpenRead(path);
                if (!SHA256.HashData(source).AsSpan().SequenceEqual(SHA256.HashData(file))) changed++;
            }
            using var zipped = File.OpenRead(zip);
            string digest = Convert.ToHexString(SHA256.HashData(zipped));
            if (changed == 0) return new("current", ZipSha256: digest);
            return new("update", new DeckyPluginInfo { Name = "Launch Curtain", FolderName = FolderName,
                IsInstalled = true, InstalledFolder = destination, InstalledVersion = installed, Version = version,
                InstallerZip = zip, RepositoryName = "Launch-Curtain", CatalogSource = "bundled-local" }, digest, changed);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException
            or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        { return new("unavailable-preserved"); }
    }
    private static bool StableVersion(string text, out Version value)
    {
        value = new Version();
        return text.Length is > 0 and < 64 && text.All(c => char.IsAsciiDigit(c) || c == '.')
            && Version.TryParse(text, out value!);
    }
}

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Playhub.Services;

namespace PlayhubSetup;

internal sealed record PlayhubFrontendResult(string Status, string ExpectedSha256 = "", string ActiveSha256 = "", bool Verified = false, int ChangedFiles = 0);

// Setup-side maintenance is independent of the optional app window. A loader
// request acknowledgement is not a receipt that the new frontend is active.
internal static class BundledPlayhubMaintenance
{
    internal const string PendingName = ".playhub-frontend-pending";
    internal const string VerifiedName = ".playhub-frontend-verified";
    internal const string ReloadScript = """
        (async () => {
          const loader = window.DeckyPluginLoader, bridge = window.DeckyBackend, store = window.SteamUIStore;
          if (!Array.isArray(loader?.plugins) || typeof bridge?.call !== 'function'
              || typeof store?.IsAnyAppRunning !== 'function') return 'unavailable';
          const matches = loader.plugins.filter(p => p.name === 'Playhub');
          if (matches.length !== 1 || store.IsAnyAppRunning() !== false) return 'busy';
          const disabled = await bridge.call('utilities/settings/get', 'disabled_plugins', []);
          if (disabled !== null && (!Array.isArray(disabled) || disabled.some(x => typeof x !== 'string'))) return 'unavailable';
          if ((disabled ?? []).some(x => x.toLowerCase() === 'playhub')) return 'disabled';
          if (window.DeckyPluginLoader !== loader || window.DeckyBackend !== bridge || window.SteamUIStore !== store
              || loader.plugins.filter(p => p.name === 'Playhub').length !== 1 || !loader.plugins.includes(matches[0])) return 'unavailable';
          if (store.IsAnyAppRunning() !== false) return 'busy';
          const reply = await bridge.call('loader/reload_plugin', 'Playhub');
          return reply === false ? 'unavailable' : 'reloaded';
        })()
        """;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    internal static async Task MaintainInstalledAsync(string installDirectory)
    {
        string plugins = AppPaths.DefaultDeckyPluginsPath;
        if (File.Exists(AppPaths.SettingsFile))
        {
            using var config = JsonDocument.Parse(await File.ReadAllTextAsync(AppPaths.SettingsFile));
            if (config.RootElement.TryGetProperty("DeckyPluginsPath", out var value) && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString())) plugins = value.GetString()!;
        }
        var result = await RunAsync(installDirectory, plugins, Playhub.Integrations.SteamPluginBridge.EvaluateAsync,
            ActiveDeckyPluginProof.ReadAsync, () => BundledPluginProcessGuard.Read(installDirectory));
        Directory.CreateDirectory(AppPaths.LocalDataRoot);
        await File.WriteAllTextAsync(Path.Combine(AppPaths.LocalDataRoot, "bundled-playhub-maintenance.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal static async Task<PlayhubFrontendResult> RunAsync(string installation, string plugins,
        Func<string, CancellationToken, Task<JsonNode?>> evaluate, Func<CancellationToken, Task<string?>> readActive,
        Func<BundledProcessSafety> safety)
    {
        await Gate.WaitAsync();
        string expected = ""; int copied = 0;
        try
        {
            string source = Path.Combine(installation, "Assets", "GamingModeDeckyPlugin", "gaming-mode");
            string destination = Path.Combine(plugins, "gaming-mode");
            if (!Directory.Exists(destination)) return new("not-installed");
            if (!SafePath(source) || !SafePath(destination) || !Identity(source) || !Identity(destination)) return new("identity-preserved");
            var retired = Playhub.Shared.EditorialPhotoMigration.GetRetiredAssetNames(installation, source);
            var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Where(p =>
                !(Path.GetDirectoryName(p)!.Equals(Path.Combine(source, "dist", "assets"), StringComparison.OrdinalIgnoreCase)
                    && retired.Contains(Path.GetFileName(p)))).ToDictionary(p => Path.GetRelativePath(source, p), p => Digest(p), StringComparer.OrdinalIgnoreCase);
            if (!files.TryGetValue(Path.Combine("dist", "index.js"), out var scriptHash) || string.IsNullOrEmpty(scriptHash) || files.Count > 20000) return new("payload-unavailable");
            expected = scriptHash;
            if (files.Keys.Any(relative => !SafePath(Path.Combine(source, relative)) || !SafePath(Path.Combine(destination, relative)))) return new("reparse-preserved", expected);
            string marker = Path.Combine(destination, PendingName);
            if (!SafePath(marker) || !SafePath(Path.Combine(destination, VerifiedName))) return new("reparse-preserved", expected);
            string generation = Guid.NewGuid().ToString("N") + ":" + expected;
            await File.WriteAllTextAsync(marker, generation);
            if (Safety() != BundledProcessSafety.Clear) return new("pending-session", expected);
            foreach (var entry in files)
            {
                string from = Path.Combine(source, entry.Key), to = Path.Combine(destination, entry.Key);
                if (!SafePath(from) || !SafePath(to) || Digest(from) != entry.Value) return new("pending-payload-changed", expected, ChangedFiles: copied);
                if (File.Exists(to) && Digest(to) == entry.Value) continue;
                if (Safety() != BundledProcessSafety.Clear) return new("pending-session", expected, ChangedFiles: copied);
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                BundledExecutableCopy.Copy(from, to); copied++;
            }
            bool CurrentFiles() => files.All(entry => SafePath(Path.Combine(destination, entry.Key))
                && File.Exists(Path.Combine(destination, entry.Key)) && Digest(Path.Combine(destination, entry.Key)) == entry.Value
                && Digest(Path.Combine(source, entry.Key)) == entry.Value);
            if (!CurrentFiles()) return new("pending-payload-changed", expected, ChangedFiles: copied);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            string? active = await Read();
            if (active != expected)
            {
                if (Safety() != BundledProcessSafety.Clear) return new("pending-session", expected, active ?? "", ChangedFiles: copied);
                JsonNode? acknowledgement = await evaluate(ReloadScript, deadline.Token).WaitAsync(deadline.Token);
                if (acknowledgement?.GetValue<string>() != "reloaded") return new("pending-reload", expected, active ?? "", ChangedFiles: copied);
                // One normal reload only. Poll the actual callback, never request a
                // second reload or infer readiness from the API acknowledgement.
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    active = await Read();
                    if (active == expected) break;
                    await Task.Delay(150, deadline.Token);
                }
            }
            if (active != expected || Safety() != BundledProcessSafety.Clear || !CurrentFiles()
                || !File.Exists(marker) || await File.ReadAllTextAsync(marker) != generation)
                return new("pending-active-proof", expected, active ?? "", ChangedFiles: copied);
            await File.WriteAllTextAsync(Path.Combine(destination, VerifiedName), JsonSerializer.Serialize(new { sha256 = expected, generation }));
            if (File.Exists(marker) && await File.ReadAllTextAsync(marker) == generation) File.Delete(marker);
            return new("verified", expected, active, true, copied);

            BundledProcessSafety Safety() { try { return safety(); } catch { return BundledProcessSafety.Unknown; } }
            async Task<string?> Read()
            {
                if (Safety() != BundledProcessSafety.Clear) return null;
                return await readActive(deadline.Token).WaitAsync(deadline.Token);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException
            or InvalidOperationException or OperationCanceledException or HttpRequestException or System.Net.WebSockets.WebSocketException
            or ArgumentException or KeyNotFoundException)
        { return new("pending-unavailable", expected, ChangedFiles: copied); }
        finally { Gate.Release(); }
    }

    private static bool Identity(string directory)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "plugin.json")));
        return doc.RootElement.GetProperty("name").GetString() == "Playhub";
    }
    private static string Digest(string path) { using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static bool SafePath(string path)
    {
        if (!Path.IsPathFullyQualified(path)) return false;
        for (string? p = Path.GetFullPath(path); p is not null; p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) return false;
        return true;
    }
}

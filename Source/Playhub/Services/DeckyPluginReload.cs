using System.Text.Json.Nodes;
using System.Security.Cryptography;

namespace Playhub.Services;

internal static class DeckyPluginReload
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    internal const string MarkerName = ".playhub-reload-pending";
    internal const string ReceiptName = ".playhub-reload-receipt";
    internal const string LoadedScript = """
        (() => {
          if (!window.DeckyPluginLoader?.plugins?.some(p => p.name === 'Playhub')) return 0;
          let latest = 0;
          for (const entry of performance.getEntriesByType('resource')) {
            try {
              const url = new URL(entry.name);
              if (url.protocol !== 'http:' || !['127.0.0.1', 'localhost'].includes(url.hostname)
                  || url.port !== '1337' || decodeURIComponent(url.pathname) !== '/plugins/Playhub/dist/index.js') continue;
              const value = url.searchParams.get('t');
              if (!/^\d{13}$/.test(value ?? '')) continue;
              const stamp = Number(value);
              if (stamp > 0 && stamp <= Date.now()) latest = Math.max(latest, stamp);
            } catch { }
          }
          return latest;
        })()
        """;
    internal const string ReloadScript = """
        (async () => {
          const store = window.SteamUIStore;
          const bridge = window.DeckyBackend;
          const loader = window.DeckyPluginLoader;
          if (typeof store?.IsAnyAppRunning !== 'function' || typeof bridge?.call !== 'function'
              || !Array.isArray(loader?.plugins) || !loader.plugins.some(p => p.name === 'Playhub')) return 'unavailable';
          // Read the live Steam state immediately before invoking its loader.
          // Missing/unknown state must never be interpreted as an idle session.
          if (store.IsAnyAppRunning() !== false) return 'busy';
          const result = await bridge.call('loader/reload_plugin', 'Playhub');
          return result === false ? 'unavailable' : 'reloaded';
        })()
        """;

    internal static void MarkPending(string pluginDirectory) =>
        File.WriteAllText(Path.Combine(pluginDirectory, MarkerName), Guid.NewGuid().ToString("N"));

    // Older installers copied assets without a pending marker. Only migrate
    // when the current packaged and installed bytes agree and CEF proves age.
    internal static async Task DetectLegacyLoadedAsync(string pluginDirectory, string packagedDirectory,
        Func<string, CancellationToken, Task<JsonNode?>> evaluate)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (File.Exists(Path.Combine(pluginDirectory, MarkerName))) return;
            string installed = Path.Combine(pluginDirectory, "dist", "index.js");
            string packaged = Path.Combine(packagedDirectory, "dist", "index.js");
            if (!File.Exists(installed) || !File.Exists(packaged)) return;
            string fingerprint;
            using (var file = File.OpenRead(installed)) fingerprint = Convert.ToHexString(SHA256.HashData(file));
            using (var file = File.OpenRead(packaged))
                if (fingerprint != Convert.ToHexString(SHA256.HashData(file))) return;
            string receipt = Path.Combine(pluginDirectory, ReceiptName);
            if (File.Exists(receipt) && File.ReadAllText(receipt) == fingerprint) return;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var result = await evaluate(LoadedScript, timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
            long loaded = result?.GetValue<long>() ?? 0;
            long disk = Math.Min(new DateTimeOffset(File.GetLastWriteTimeUtc(installed)).ToUnixTimeMilliseconds(),
                new DateTimeOffset(File.GetLastWriteTimeUtc(packaged)).ToUnixTimeMilliseconds());
            if (loaded > 0 && loaded < disk)
            {
                // A normal copy can mark a newer generation during the CEF query.
                // CreateNew preserves that pending update instead of replacing it.
                using var marker = new FileStream(Path.Combine(pluginDirectory, MarkerName), FileMode.CreateNew, FileAccess.Write);
                using var writer = new StreamWriter(marker);
                writer.Write("legacy:" + fingerprint);
            }
        }
        catch { } // Unknown loader state never justifies an unsolicited reload.
        finally { Gate.Release(); }
    }

    internal static async Task<bool> TryPendingAsync(string pluginDirectory,
        Func<string, CancellationToken, Task<JsonNode?>> evaluate)
    {
        string marker = Path.Combine(pluginDirectory, MarkerName);
        if (!File.Exists(marker)) return false;
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(marker)) return false;
            string generation = await File.ReadAllTextAsync(marker).ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var result = await evaluate(ReloadScript, timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
            if (result?.GetValue<string>() != "reloaded") return false;
            // A later update still needs its own reload, even if this one succeeded.
            if (File.Exists(marker) && await File.ReadAllTextAsync(marker).ConfigureAwait(false) == generation)
            {
                if (generation.StartsWith("legacy:", StringComparison.Ordinal))
                    await File.WriteAllTextAsync(Path.Combine(pluginDirectory, ReceiptName), generation[7..]).ConfigureAwait(false);
                File.Delete(marker);
            }
            return true;
        }
        catch { return false; } // Leave the marker for the next normal sync.
        finally { Gate.Release(); }
    }
}

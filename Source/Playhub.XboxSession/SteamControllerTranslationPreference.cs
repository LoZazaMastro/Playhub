// Playhub Xbox Session. Distributed under GPL-3.0-only.
using System.Text.Json;

internal sealed class SteamControllerTranslationPreference
{
    internal readonly record struct State(bool Enabled, string Revision, bool Known = true);
    private readonly object gate = new();
    private readonly Func<string?> read;
    private readonly Func<long> clock;
    private State cached = new(true, "");
    private bool sampled, seenConfig;
    private long sampledAt;

    internal SteamControllerTranslationPreference(Func<string?>? read = null, Func<long>? clock = null)
    {
        this.read = read ?? ReadConfig;
        this.clock = clock ?? (() => Environment.TickCount64);
    }

    internal State Read()
    {
        lock (gate)
        {
            long now = clock();
            if (sampled && now >= sampledAt && now - sampledAt < 100) return cached;
            sampled = true; sampledAt = now;
            try
            {
                string? text = read();
                if (text is null) return cached = seenConfig ? new(false, "missing", false) : new(true, "");
                using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32 });
                if (document.RootElement.ValueKind != JsonValueKind.Object) throw new FormatException();
                seenConfig = true;
                var gaming = Find(document.RootElement, "Gaming");
                if (gaming is null) return cached = new(true, "");
                if (gaming.Value.ValueKind != JsonValueKind.Object) throw new FormatException();
                var enabled = Find(gaming.Value, "SteamControllerTranslationEnabled");
                var revision = Find(gaming.Value, "SteamControllerTranslationRevision");
                bool value = enabled is null || enabled.Value.ValueKind == JsonValueKind.True;
                if (enabled is not null && enabled.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new FormatException();
                string version = revision is null ? "" : revision.Value.GetString() ?? throw new FormatException();
                if (version.Length != 0 && !Guid.TryParseExact(version, "N", out _)) throw new FormatException();
                return cached = new(value, version);
            }
            catch { return cached = new(false, "unknown", false); }
        }
    }

    private static JsonElement? Find(JsonElement parent, string name)
    {
        JsonElement? result = null;
        foreach (var property in parent.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            { if (result is not null) throw new FormatException(); result = property.Value; }
        return result;
    }

    private static string? ReadConfig()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GamingMode", "config.json");
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (file.Length > 4 * 1024 * 1024) throw new IOException("Config too large.");
            using var reader = new StreamReader(file);
            return reader.ReadToEnd();
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
}

internal static class SteamControllerTranslationLifecycle
{
    // Each run owns and synchronously drains its relay in its existing finally.
    // A preference version can revoke an old run, never authorize input itself.
    internal static void Run(Func<bool> current, Func<SteamControllerTranslationPreference.State> read,
        Func<bool, Func<bool>, bool> run, Action<int> wait)
    {
        while (current())
        {
            var state = read();
            bool Same() => current() && read() == state;
            if (!Same()) { wait(10); continue; }
            if (!run(state.Known && state.Enabled, Same)) return;
            // A stable failure is not retried. Only an explicit changed setting
            // permits a new run with fresh source/reader/owned-output proofs.
            while (current() && read() == state) wait(100);
        }
    }
}

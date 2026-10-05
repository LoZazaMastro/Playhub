using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Playhub.Emulation.Workbench;

/// <summary>Publishes only one verified shortcut's data to installed plugin file contracts.
/// Disk delivery and synchronization of a running plugin's cache are separate results.</summary>
public sealed class PluginConsumerDelivery(string pluginsRoot, string settingsRoot,
    Func<string, string, JsonArray, CancellationToken, Task<JsonNode?>>? runtime = null)
{
    private const int MaxJson = 16 * 1024 * 1024;
    private static (string Folder, string Name, string Settings, string File, string? Map) Contract(string category) => category switch
    {
        "metadata" or "metadatadeck" => ("Playhub Metadata", "Playhub Metadata", "Playhub Metadata", "playhub_metadata.json", "metadata"),
        "themedeck" => ("themedeck", "ThemeDeck", "themedeck", "tracks.json", null),
        "launch-curtain" or "launchcurtain" => ("launch-curtain", "Launch Curtain", "launch-curtain", "launch-curtain.json", "per_game"),
        "trailerhero" => ("TrailerHero", "TrailerHero", "trailerhero", "trailer-library.json", "assignments"),
        _ => throw new ArgumentException("Unsupported delivery category.")
    };

    public bool IsInstalled(string category)
    {
        var identity = category is "artworks" or "artwork" ? (Folder: "Playhub-Artworks", Name: "Playhub Artworks") : (Contract(category).Folder, Contract(category).Name);
        string descriptor = Path.Combine(Path.GetFullPath(pluginsRoot), identity.Folder, "plugin.json");
        try
        {
            Guard(descriptor);
            if (!File.Exists(descriptor) || new FileInfo(descriptor).Length > 65536) return false;
            return JsonNode.Parse(File.ReadAllBytes(descriptor))?["name"]?.GetValue<string>() == identity.Name;
        }
        catch (IOException) { return false; }
        catch (System.Text.Json.JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private string SettingsFile(string category)
    {
        var c = Contract(category);
        return category is "launch-curtain" or "launchcurtain"
            ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(pluginsRoot))!,"data","launch-curtain",c.File)
            : Path.Combine(Path.GetFullPath(settingsRoot),c.Settings,c.File);
    }
    private string ReadSettingsFile(string category)
    {
        string primary = SettingsFile(category);
        if (category is not ("launch-curtain" or "launchcurtain") || File.Exists(primary)) return primary;
        foreach (var legacy in new[] { Path.Combine(settingsRoot,"launch-curtain","launch-curtain.json"),Path.Combine(settingsRoot,"launch-curtain.json"),Path.Combine(pluginsRoot,"launch-curtain","launch-curtain.json"),Path.Combine(Path.GetDirectoryName(Path.GetFullPath(pluginsRoot))!,"launch-curtain.json") })
            if (File.Exists(legacy)) return Path.GetFullPath(legacy);
        return primary;
    }
    public async Task<JsonObject?> ReadAsync(string category, uint appId, CancellationToken ct = default)
    {
        RequireApp(appId);
        if (!IsInstalled(category)) return null;
        var (_, root) = await ReadFile(ReadSettingsFile(category), ct);
        var map = Map(root, Contract(category).Map, false);
        var item = map?[Id(appId)];
        if (item is not null && item is not JsonObject) throw new InvalidDataException("Plugin game entry must be an object.");
        return item?.DeepClone() as JsonObject;
    }

    public async Task<JsonObject> ResolveCurtainAsync(JsonObject raw,CancellationToken ct = default)
    {
        JsonObject global = new();
        if (IsInstalled("launch-curtain")) (_,global) = await ReadFile(ReadSettingsFile("launch-curtain"),ct);
        var result = new JsonObject();
        JsonNode? Value(string key,bool inherit=true) => raw.ContainsKey(key) ? raw[key] : inherit ? global[key] : null;
        bool Bool(string key,bool fallback,bool inherit=true)
        {
            var value = Value(key,inherit); if (value is null) return fallback;
            return value.ToString().Trim().ToLowerInvariant() is not ("0" or "false" or "off" or "no" or "disabled");
        }
        int Number(string key,int fallback,int min,int max,bool inherit=true) =>
            double.TryParse(Value(key,inherit)?.ToString(),NumberStyles.Float,CultureInfo.InvariantCulture,out var value) && double.IsFinite(value)
                ? (int)Math.Clamp(value,min,max) : fallback;
        result["enabled"]=Bool("enabled",true,false);
        result["show_logo"]=Bool("show_logo",true);
        result["logo_zoom_enabled"]=Bool("logo_zoom_enabled",true);
        result["bg_zoom_enabled"]=Bool("bg_zoom_enabled",true);
        result["timeout_enabled"]=Bool("timeout_enabled",false,false);
        foreach(var key in new[] {"logo_position_x","logo_position_y"}) result[key]=Number(key,50,0,100,false);
        result["logo_scale"]=Number("logo_scale",100,50,200,false);
        foreach(var key in new[] {"background_position_x","background_position_y"}) result[key]=Rounded(key,50,0,100);
        result["background_scale"]=Rounded("background_scale",100,100,200);
        result["background_opacity"]=Number("background_opacity",100,0,100);
        result["logo_shadow_opacity"]=Number("logo_shadow_opacity",0,0,100,false);
        result["logo_shadow_blur"]=Number("logo_shadow_blur",40,0,100);
        int timeout=double.TryParse(global["curtain_timeout"]?.ToString(),NumberStyles.Float,CultureInfo.InvariantCulture,out var timeoutValue) && double.IsFinite(timeoutValue) ? (int)Math.Clamp(timeoutValue,5,60) : 50;
        result["timeout_seconds"]=Number("timeout_seconds",timeout,5,60,false);
        int delay=double.TryParse(raw["game_settle_seconds"]?.ToString() ?? global["game_settle_seconds"]?.ToString(),NumberStyles.Float,CultureInfo.InvariantCulture,out var delayValue) && double.IsFinite(delayValue) ? (int)Math.Clamp(delayValue,0,10) : 3;
        result["exit_delay_seconds"]=Number("exit_delay_seconds",delay,0,10,false);
        result["soundbite_volume"]=Number("soundbite_volume",100,0,100,false);
        result["fullscreen_image_path"]=Value("fullscreen_image_path")?.ToString() ?? "";
        string mode=raw["force_mode"]?.ToString() ?? "auto";
        result["force_mode"]=mode is "classic" or "modern" ? mode : "auto";
        foreach(var key in new[] {"background_search_query","soundbite_path","soundbite_source","soundbite_title","soundbite_search_query"}) result[key]=raw[key]?.ToString() ?? "";
        return result;
        int Rounded(string key,int fallback,int min,int max) => double.TryParse(Value(key)?.ToString(),NumberStyles.Float,CultureInfo.InvariantCulture,out var value) && double.IsFinite(value) ? (int)Math.Round(Math.Clamp(value,min,max),MidpointRounding.ToEven) : fallback;
    }
    public async Task<string?> ReadTrailerPathAsync(uint appId, CancellationToken ct = default)
    {
        var entry = await ReadAsync("trailerhero", appId, ct);
        if (entry is null) return null;
        string relative = entry["video"]?.GetValue<string>() ?? "";
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':')) throw new InvalidDataException("Trailer path must be relative to its media library.");
        string mediaRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SettingsFile("trailerhero"))!, "trailers")) + Path.DirectorySeparatorChar;
        string file = Path.GetFullPath(Path.Combine(mediaRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!file.StartsWith(mediaRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Trailer assignment escapes its media library.");
        Guard(file);
        if (!File.Exists(file)) return null;
        if (entry["sha256"]?.GetValue<string>() is string expected && expected != await HashFile(file, ct)) throw new IOException("Assigned trailer does not match its saved hash.");
        return file;
    }

    public async Task<JsonObject> DeliverAsync(string category, uint appId, JsonObject data, CancellationToken ct = default)
    {
        RequireApp(appId);
        if (!IsInstalled(category)) return Result(false, false, "plugin_not_installed");
        var contract = Contract(category);
        JsonObject fields = category switch
        {
            "metadata" or "metadatadeck" => NormalizeMetadata(data),
            "launch-curtain" or "launchcurtain" => ValidateCurtain(data),
            "themedeck" => ValidateTrack(data, appId),
            "trailerhero" => new JsonObject(),
            _ => throw new ArgumentException("Unsupported delivery category.")
        };
        string file = SettingsFile(category);
        Guard(file); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using var held = await Acquire(file + ".playhub.lock", ct);
        var (before, root) = await ReadFile(file, ct);
        string readFile = ReadSettingsFile(category);
        byte[]? legacyBefore = null;
        if (before is null && readFile != file) (legacyBefore,root) = await ReadFile(readFile,ct);
        if (category == "trailerhero" && root["version"] is not null && root["version"]!.ToString() != "1") throw new InvalidDataException("Unsupported trailer library version.");
        var map = Map(root, contract.Map, true)!;
        if (map[Id(appId)] is not null and not JsonObject) throw new InvalidDataException("Plugin game entry must be an object.");
        if (category == "trailerhero") fields = await PrepareTrailer(data, appId, ct);
        var entry = map[Id(appId)] as JsonObject ?? new JsonObject();
        foreach (var field in fields) entry[field.Key] = field.Value?.DeepClone();
        if (category == "themedeck" && string.IsNullOrWhiteSpace(entry["path"]?.GetValue<string>())) throw new InvalidOperationException("Select a track before saving playback settings.");
        if (category == "themedeck") { entry["volume"] ??= 1.0; entry["start_offset"] ??= 0.0; entry["loop"] ??= true; }
        map[Id(appId)] = entry;
        if (category == "trailerhero" && root["version"] is null) root["version"] = 1;
        try
        {
            if (legacyBefore is not null)
            {
                var (currentLegacy,_) = await ReadFile(readFile,ct);
                if (currentLegacy is null || !legacyBefore.AsSpan().SequenceEqual(currentLegacy)) throw new IOException("Legacy plugin settings changed during delivery.");
            }
            await Publish(file, before, root, ct);
        }
        catch
        {
            // Only this transaction's unique copied file; never a prior assignment or source file.
            if (category == "trailerhero")
            {
                string ownedCopy = Path.Combine(Path.GetDirectoryName(file)!, "trailers", fields["video"]!.GetValue<string>().Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(ownedCopy)) File.Delete(ownedCopy);
            }
            throw;
        }
        var persisted = await ReadAsync(category, appId, ct);
        if (!Matches(persisted, fields)) throw new IOException("Plugin assignment did not pass readback.");
        bool synced = false;
        if (runtime is not null)
        {
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct); bounded.CancelAfter(TimeSpan.FromSeconds(5));
            try { synced = await Synchronize(category, appId, persisted!, bounded.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            catch (Exception) when (!ct.IsCancellationRequested) { }
        }
        ct.ThrowIfCancellationRequested();
        var result = Result(true, synced, synced ? "runtime_readback" : "persisted_file_contract");
        result["data"] = persisted;
        return result;
    }

    public async Task<JsonObject> RemoveTrackAsync(uint appId, CancellationToken ct = default)
    {
        RequireApp(appId);
        if (!IsInstalled("themedeck")) return Result(false, false, "plugin_not_installed");
        string file = SettingsFile("themedeck"); Guard(file); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using var held = await Acquire(file + ".playhub.lock", ct);
        var (before, root) = await ReadFile(file, ct); root.Remove(Id(appId));
        await Publish(file, before, root, ct);
        if (await ReadAsync("themedeck", appId, ct) is not null) throw new IOException("Track removal did not pass readback.");
        bool synced = false;
        if (runtime is not null)
        {
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct); bounded.CancelAfter(TimeSpan.FromSeconds(5));
            try { synced = await runtime("ThemeDeck", "get_track", new JsonArray(appId), bounded.Token) is null; }
            catch (Exception) when (!ct.IsCancellationRequested) { }
        }
        ct.ThrowIfCancellationRequested();
        return Result(true, synced, "track_assignment_removed"); // Old media is retained; no broad cleanup.
    }

    private async Task<bool> Synchronize(string category, uint appId, JsonObject entry, CancellationToken ct)
    {
        JsonNode? observed;
        switch (category)
        {
            case "metadata": case "metadatadeck":
                // Metadata reloads disk by signature before each save. Unknown entry fields
                // cannot safely pass through its sanitizer, so keep their file contract intact.
                var known = new HashSet<string>(["title", "description", "short_description", "genres", "features", "developers", "publishers", "release_date", "rating", "id", "source", "source_url", "scraper_source", "language", "content_language", "translated", "store_categories", "updated_at"], StringComparer.Ordinal);
                if (entry.All(x=>known.Contains(x.Key))) await runtime!("Playhub Metadata", "save_metadata", new JsonArray(appId, entry.DeepClone()), ct);
                observed = await runtime!("Playhub Metadata", "get_metadata", new JsonArray(appId), ct);
                return Matches(observed as JsonObject, entry, ignoreUpdated: true);
            case "launch-curtain": case "launchcurtain":
                // Cached whole-file setters can discard another app's offline changes.
                // Only observe here; a host-owned safe refresh may synchronize the cache.
                observed = await runtime!("Launch Curtain", "get_game_settings", new JsonArray(new JsonObject { ["app_id"] = appId }), ct);
                return Matches(observed?["settings"] as JsonObject, entry);
            case "themedeck":
                observed = await runtime!("ThemeDeck", "get_track", new JsonArray(appId), ct);
                return Matches(observed as JsonObject, entry);
            case "trailerhero":
                // TrailerHero reads its library per request; no cached assignment setter is needed.
                observed = await runtime!("TrailerHero", "get_local_trailer", new JsonArray(appId), ct);
                return observed?["assigned"]?.GetValue<bool>() == true && observed["sha256"]?.GetValue<string>() == entry["sha256"]?.GetValue<string>();
            default: return false;
        }
    }

    public static JsonObject NormalizeMetadata(JsonObject data)
    {
        var output = (JsonObject)data.DeepClone();
        output.Remove("updated_at"); // Timestamp is generated here, never accepted from acquisition.
        if (output["id"] is not null) output["id"] = output["id"]!.ToString();
        if (output["release_date"] is JsonValue date && date.TryGetValue<string>(out var text))
        {
            if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)) throw new ArgumentException("Invalid release date.");
            output["release_date"] = parsed.ToUnixTimeSeconds();
        }
        var manual = new JsonObject();
        var allowed = new HashSet<string>(["title", "description", "short_description", "genres", "features", "developers", "publishers", "release_date", "rating"], StringComparer.Ordinal);
        var provenance = new HashSet<string>(["id", "source", "scraper_source", "language", "content_language", "source_url", "translated", "store_categories"], StringComparer.Ordinal);
        foreach (var field in output)
        {
            if (allowed.Contains(field.Key)) manual[field.Key] = field.Value?.DeepClone();
            else if (!provenance.Contains(field.Key)) throw new ArgumentException("Unsupported metadata acquisition field: " + field.Key);
        }
        GameIntegrationEditorService.ValidateMetadata(manual);
        foreach (var field in output.Where(x => provenance.Contains(x.Key)))
        {
            if (field.Key == "translated") { if (field.Value is not JsonValue b || !b.TryGetValue<bool>(out _)) throw new ArgumentException("Invalid translation flag."); }
            else if (field.Key == "store_categories") { if (field.Value is not JsonArray a || a.Any(x => x is not JsonValue v || !v.TryGetValue<int>(out _))) throw new ArgumentException("Invalid store categories."); }
            else if (field.Value is not JsonValue s || !s.TryGetValue<string>(out var value) || value.Length > 10000) throw new ArgumentException("Invalid metadata provenance.");
        }
        output["updated_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return output;
    }

    private static JsonObject ValidateCurtain(JsonObject data) { GameIntegrationEditorService.ValidateCurtain(data); return (JsonObject)data.DeepClone(); }
    private static JsonObject ValidateTrack(JsonObject data, uint appId)
    {
        var result = (JsonObject)data.DeepClone();
        var keys = new HashSet<string>(["path", "filename", "normalized", "volume", "start_offset", "loop"], StringComparer.Ordinal);
        if (result.Any(x => !keys.Contains(x.Key))) throw new ArgumentException("Unsupported track setting.");
        if (result["path"]?.GetValue<string>() is string path)
        {
            CheckMedia(path); result["path"] = Path.GetFullPath(path);
            result["filename"] = Path.GetFileName(path); result["app_id"] = appId;
            result["normalized"] ??= false;
        }
        foreach (var key in new[] { "volume", "start_offset" })
            if (result[key] is not null && (!double.TryParse(result[key]!.ToJsonString(), CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n) || n < 0 || n > (key == "volume" ? 1 : 30))) throw new ArgumentException("Invalid track setting.");
        foreach (var key in new[] { "normalized", "loop" }) if (result[key] is not null && (result[key] is not JsonValue v || !v.TryGetValue<bool>(out _))) throw new ArgumentException("Invalid track flag.");
        return result;
    }

    private async Task<JsonObject> PrepareTrailer(JsonObject data, uint appId, CancellationToken ct)
    {
        string source = data["path"]?.GetValue<string>() ?? throw new ArgumentException("Acquired trailer file required.");
        CheckMedia(source);
        if (!string.Equals(Path.GetExtension(source), ".mp4", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Acquired trailer must be MP4.");
        string relative = Id(appId) + "/" + Guid.NewGuid().ToString("N") + ".mp4";
        string target = Path.Combine(Path.GetDirectoryName(SettingsFile("trailerhero"))!, "trailers", relative.Replace('/', Path.DirectorySeparatorChar));
        Guard(target); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        try
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true))
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true)) await input.CopyToAsync(output, ct);
            string hash = await HashFile(source, ct);
            if (hash != await HashFile(target, ct)) throw new IOException("Trailer copy did not pass verification.");
            return new JsonObject { ["appid"] = appId, ["title"] = data["title"]?.DeepClone() ?? JsonValue.Create(""), ["source"] = "import", ["sourceId"] = data["videoId"]?.DeepClone() ?? JsonValue.Create(""), ["video"] = relative, ["audio"] = "", ["requestedHeight"] = data["requestedHeight"]?.DeepClone() ?? JsonValue.Create(1080), ["actualHeight"] = data["actualHeight"]?.DeepClone() ?? JsonValue.Create(0), ["sha256"] = hash, ["createdAt"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
        }
        catch { if (File.Exists(target)) File.Delete(target); throw; }
    }
    private static void CheckMedia(string path) { if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Local media must have an absolute path."); Guard(path); if (!File.Exists(path) || new FileInfo(path).Length is <= 0 or > 2147483648) throw new ArgumentException("Local media is missing or exceeds the limit."); }
    private static async Task<string> HashFile(string path, CancellationToken ct) { using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true); return Convert.ToHexString(await SHA256.HashDataAsync(file, ct)).ToLowerInvariant(); }
    private static bool Matches(JsonObject? observed, JsonObject expected, bool ignoreUpdated = false) => observed is not null && expected.All(x => (ignoreUpdated && x.Key == "updated_at") || JsonNode.DeepEquals(observed[x.Key], x.Value));
    private static void RequireApp(uint id) { if (id == 0) throw new ArgumentException("Delivery requires a real exported Steam shortcut."); }
    private static string Id(uint id) => id.ToString(CultureInfo.InvariantCulture);
    private static JsonObject Result(bool delivered, bool synced, string detail) => new() { ["ok"] = delivered, ["delivered"] = delivered, ["runtimeSynced"] = synced, ["detail"] = detail };
    private static JsonObject? Map(JsonObject root, string? key, bool create)
    {
        if (key is null) return root;
        if (root[key] is JsonObject value) return value;
        if (root.ContainsKey(key)) throw new InvalidDataException("Plugin assignment map must be an object.");
        if (!create) return null;
        var map = new JsonObject(); root[key] = map; return map;
    }
    private static async Task<(byte[]? Bytes, JsonObject Data)> ReadFile(string file, CancellationToken ct)
    {
        Guard(file); if (!File.Exists(file)) return (null, new JsonObject());
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, true);
        if (stream.Length > MaxJson) throw new InvalidDataException("Plugin settings exceed the limit.");
        var bytes = new byte[checked((int)stream.Length)]; await stream.ReadExactlyAsync(bytes, ct);
        return (bytes, JsonNode.Parse(bytes) as JsonObject ?? throw new InvalidDataException("Plugin settings must be an object."));
    }
    private static async Task<FileStream> Acquire(string path, CancellationToken ct)
    {
        Guard(path);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct); limit.CancelAfter(TimeSpan.FromSeconds(5));
        while (true) { limit.Token.ThrowIfCancellationRequested(); try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); } catch (IOException) { await Task.Delay(20, limit.Token); } }
    }
    private static async Task Publish(string file, byte[]? expected, JsonObject data, CancellationToken ct)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(data.ToJsonString()); if (bytes.Length > MaxJson) throw new InvalidDataException("Plugin settings exceed the limit.");
        string temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Guard(file); await File.WriteAllBytesAsync(temporary, bytes, ct);
            var (current, _) = await ReadFile(file, ct);
            if (!(expected is null ? current is null : current is not null && expected.AsSpan().SequenceEqual(current))) throw new IOException("Plugin settings changed during delivery; retry from current data.");
            ct.ThrowIfCancellationRequested(); File.Move(temporary, file, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void Guard(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Plugin delivery cannot follow redirected directories.");
    }
}

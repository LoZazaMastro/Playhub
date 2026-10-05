using System.Text.Json.Nodes;
using Playhub.Integrations;

namespace Playhub.Emulation.Workbench;

/// <summary>App-owned acquisition and editing for one stable source identity; Steam IDs only address delivery.</summary>
public sealed class ApplicationIntegrationCoordinator : IAsyncDisposable
{
    private readonly string _identity;
    private readonly uint _appId;
    private readonly ApplicationIntegrationDataStore _store;
    private readonly ApplicationMediaAcquisition _media;
    private readonly PluginConsumerDelivery _consumer;
    private readonly Func<string, string, CancellationToken, Task<JsonObject>> _metadata;
    private readonly Func<string, string, JsonArray, CancellationToken, Task<JsonNode?>>? _providers;
    private readonly Func<uint,string,CancellationToken,Task<JsonNode?>>? _selectStream;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, (Task Task, JsonObject Result)> _jobs = new();
    private readonly object _gate = new();
    private bool _disposed;
    public ApplicationIntegrationCoordinator(string stableIdentity, uint actualAppId, ApplicationIntegrationDataStore store,
        ApplicationMediaAcquisition media, PluginConsumerDelivery consumer,
        Func<string, string, CancellationToken, Task<JsonObject>> metadata,
        Func<string, string, JsonArray, CancellationToken, Task<JsonNode?>>? providers = null,
        Func<uint,string,CancellationToken,Task<JsonNode?>>? selectStream = null)
    {
        _identity = ApplicationIntegrationIdentity.Validate(stableIdentity); _appId = actualAppId;
        _store = store; _media = media; _consumer = consumer; _metadata = metadata; _providers = providers; _selectStream = selectStream;
    }
    public static string StorageCategory(string category) => category switch
    {
        "metadata" or "Playhub Metadata" or "metadatadeck" => "metadatadeck",
        "artworks" or "Playhub Artworks" or "artwork" => "artwork",
        "launch-curtain" or "Launch Curtain" or "launchcurtain" => "launchcurtain",
        "ThemeDeck" or "themedeck" => "themedeck", "TrailerHero" or "trailerhero" => "trailerhero",
        _ => throw new ArgumentException("Unknown integration category.")
    };
    public async Task BindAsync(CancellationToken ct = default)
    { if (_appId != 0) await _store.BindSteamAppIdAsync(_identity, _appId, ct); }
    /// <summary>Deliver already prepared data after exact export. Never starts acquisition or invents a shortcut ID.</summary>
    public async Task<JsonArray> DeliverPreparedAsync(IEnumerable<string> selectedCategories, CancellationToken ct = default)
    {
        if (_appId == 0) throw new ArgumentException("Delivery requires the actual exported shortcut.");
        await BindAsync(ct);
        var outcomes = new JsonArray();
        foreach (var category in selectedCategories.Select(StorageCategory).Distinct(StringComparer.Ordinal))
        {
            if (category == "artwork") continue; // Actual artwork is delivered by the owning exact Steam artwork service.
            if (!_consumer.IsInstalled(category)) { outcomes.Add(new JsonObject { ["category"] = category,["delivered"] = false,["detail"] = "plugin_not_installed" }); continue; }
            var fields = await _store.ReadCategoryAsync(_identity,category,ct);
            if (fields.Count == 0) continue;
            JsonObject result;
            if (category == "themedeck" && fields["removed"]?.GetValue<bool>() == true) result = await _consumer.RemoveTrackAsync(_appId,ct);
            else if (category == "trailerhero" && fields["preferredSource"]?.GetValue<string>() == "youtube")
                result = (await CallAsync("TrailerHero","select_streaming_trailer",new JsonArray(_appId,fields["videoId"]?.DeepClone()),ct) as JsonObject)!;
            else
            {
                if (category == "themedeck")
                {
                    var track = new JsonObject();
                    foreach (var key in new[] { "path","filename","normalized","volume","start_offset","loop" }) if (fields.ContainsKey(key)) track[key] = fields[key]?.DeepClone();
                    fields = track;
                }
                result = await _consumer.DeliverAsync(category,_appId,fields,ct);
            }
            result["category"] = category; outcomes.Add(result);
        }
        return outcomes;
    }
    private void Exact(JsonArray args)
    { if (args.Count == 0 || !uint.TryParse(args[0]?.ToString(), out var app) || app != _appId) throw new ArgumentException("Integration request does not match its source identity."); }
    private async Task<JsonObject> Read(string category, CancellationToken ct)
    {
        if (_appId != 0 && await _store.ResolveSteamAppIdAsync(_appId,ct) is string bound && bound != _identity) throw new InvalidOperationException("Steam shortcut belongs to another stored source identity.");
        var own = await _store.ReadCategoryAsync(_identity, StorageCategory(category), ct);
        if (_appId == 0) return own;
        var imported = await _consumer.ReadAsync(category, _appId, ct);
        imported ??= new JsonObject();
        if (category == "trailerhero" && imported.Count > 0) imported["path"] = await _consumer.ReadTrailerPathAsync(_appId,ct);
        foreach (var field in own) imported[field.Key] = field.Value?.DeepClone();
        return imported;
    }
    private async Task<JsonObject> Save(string category, JsonObject fields, CancellationToken ct)
    {
        await BindAsync(ct);
        if (category == "themedeck")
        {
            var previous = await Read(category,ct);
            foreach (var field in fields) previous[field.Key] = field.Value?.DeepClone();
            fields = previous;
        }
        var data = await _store.PatchAsync(_identity, StorageCategory(category), fields, ct);
        // Store first: provider drafts and user edits exist independently of consumers.
        var deliveryFields = (JsonObject)fields.DeepClone();
        if (category == "themedeck")
        {
            deliveryFields = new JsonObject();
            foreach (var key in new[] { "path", "filename", "normalized", "volume", "start_offset", "loop" })
                if (data.ContainsKey(key)) deliveryFields[key] = data[key]?.DeepClone();
        }
        JsonObject delivery = _appId == 0 ? new() { ["delivered"] = false, ["runtimeSynced"] = false, ["detail"] = "awaiting_export" }
            : await _consumer.DeliverAsync(category, _appId, deliveryFields, ct);
        LastDelivery = delivery;
        return data;
    }
    public JsonObject? LastDelivery { get; private set; }
    public async Task<JsonNode?> CallAsync(string plugin, string method, JsonArray args, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        var token = linked.Token;
        if (plugin == "Playhub Metadata")
        {
            Exact(args);
            if (method == "get_metadata") return await Read("metadata", token);
            if (method == "save_metadata")
            {
                var fields = args.ElementAtOrDefault(1) as JsonObject ?? throw new ArgumentException("Metadata fields required.");
                // Existing sanitized runtime entries may contain associations not owned by the editor.
                var owned = new JsonObject();
                foreach (var key in new[] { "title", "description", "short_description", "genres", "features", "developers", "publishers", "release_date", "rating", "id", "source", "source_url", "scraper_source", "language", "content_language", "translated", "store_categories" })
                    if (fields.ContainsKey(key)) owned[key] = fields[key]?.DeepClone();
                var saved=await Save("metadata",PluginConsumerDelivery.NormalizeMetadata(owned),token); saved["_delivery"]=LastDelivery?.DeepClone();return saved;
            }
            if (method == "auto_fetch_metadata")
            {
                string title = args.ElementAtOrDefault(1)?.GetValue<string>() ?? throw new ArgumentException("Game title required.");
                var result = await _metadata(_identity, title, token);
                if (result["metadata"] is not JsonObject acquired || result["status"]?.GetValue<string>() != "completed") return null;
                var saved=await Save("metadata",PluginConsumerDelivery.NormalizeMetadata(acquired),token);saved["_delivery"]=LastDelivery?.DeepClone();return saved;
            }
        }
        if (plugin == "ThemeDeck")
        {
            if (method == "search_youtube") return await Provider(plugin, method, args, token);
            if (method == "get_discover_download_progress") return Job(args[0]!.GetValue<string>());
            Exact(args);
            if (method == "get_track") { var data = await Read("themedeck", token); return data["removed"]?.GetValue<bool>() == true || data["path"] is null ? null : data; }
            if (method == "remove_track")
            {
                await BindAsync(token);
                await _store.PatchAsync(_identity,"themedeck",new JsonObject { ["removed"] = true, ["path"] = null },token);
                LastDelivery = _appId == 0 ? new JsonObject { ["delivered"] = false, ["detail"] = "awaiting_export" } : await _consumer.RemoveTrackAsync(_appId,token);
                return new JsonObject();
            }
            if (method is "set_volume" or "set_start_offset" or "set_loop")
            {
                string field = method switch { "set_volume" => "volume", "set_start_offset" => "start_offset", _ => "loop" };
                var result = await Save("themedeck", new JsonObject { [field] = args[1]?.DeepClone() },token);
                return new JsonObject { [_appId.ToString()] = result };
            }
            if (method == "start_game_download")
            {
                string id = YouTubeId(args[1]?.GetValue<string>() ?? "");
                return Start(new(_appId,"themedeck",id,NormalizeAudio:args.ElementAtOrDefault(2)?.GetValue<bool>() ?? false,UpmixAudio:args.ElementAtOrDefault(3)?.GetValue<bool>() ?? false,StableIdentity:_identity),args.ElementAtOrDefault(4)?.GetValue<string>());
            }
        }
        if (plugin == "TrailerHero")
        {
            if (method == "search_youtube_videos") return await Provider(plugin,method,args,token);
            if (method == "get_trailer_job") return Job(args[0]!.GetValue<string>());
            Exact(args);
            if (method == "select_streaming_trailer")
            {
                string id = YouTubeId(args[1]!.GetValue<string>());
                await BindAsync(token);
                await _store.PatchAsync(_identity,"trailerhero",new JsonObject { ["videoId"] = id,["preferredSource"] = "youtube" },token);
                bool delivered = false;
                if (_appId != 0 && _selectStream is not null)
                {
                    using var limit = CancellationTokenSource.CreateLinkedTokenSource(token); limit.CancelAfter(TimeSpan.FromSeconds(5));
                    try { delivered = (await _selectStream(_appId,id,limit.Token))?["ok"]?.GetValue<bool>() == true; }
                    catch(Exception) when(!token.IsCancellationRequested) { }
                }
                return new JsonObject { ["ok"] = true,["selectionSaved"] = true,["downloaded"] = false,["delivered"] = delivered,["runtimeSynced"] = delivered,["videoId"] = id };
            }
            if (method == "get_streaming_trailer")
            {
                var data = await _store.ReadCategoryAsync(_identity,"trailerhero",token);
                return new JsonObject { ["videoId"] = data["videoId"]?.DeepClone(),["preferredSource"] = data["preferredSource"]?.DeepClone() };
            }
            if (method == "get_local_trailer")
            {
                var data = await Read("trailerhero",token); var path = data["path"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return new JsonObject { ["ok"] = true,["assigned"] = false };
                data["ok"] = true; data["assigned"] = true; data["videoUrl"] = new Uri(path).AbsoluteUri; data["mode"] = "local";
                return data; // App-owned local player; no fabricated plugin HTTP URL.
            }
            if (method == "start_trailer_download")
            {
                if (args[1]?.GetValue<string>() != "youtube") throw new ArgumentException("Unsupported trailer source.");
                return Start(new(_appId,"trailerhero",args[2]!.GetValue<string>(),args.ElementAtOrDefault(3)?.GetValue<int>() ?? 1080,StableIdentity:_identity),args.ElementAtOrDefault(4)?.GetValue<string>());
            }
        }
        if (plugin == "Launch Curtain" && method is "get_game_settings" or "save_game_settings")
        {
            var request = args[0] as JsonObject ?? throw new ArgumentException("Curtain request required.");
            Exact(new JsonArray(request["app_id"]?.DeepClone()));
            JsonObject data;
            if (method == "save_game_settings") { var fields = request["settings"] as JsonObject ?? throw new ArgumentException("Settings required."); GameIntegrationEditorService.ValidateCurtain(fields); data = await Save("launch-curtain",fields,token); }
            else data = await Read("launch-curtain",token);
            return new JsonObject { ["ok"] = true,["settings"] = data,["resolved"] = await _consumer.ResolveCurtainAsync(data,token),["delivery"]=method=="save_game_settings"?LastDelivery?.DeepClone():null };
        }
        if (plugin is "Playhub Artworks" or "Launch Curtain") return await Provider(plugin,method,args,token);
        throw new NotSupportedException("This app-owned integration operation is not implemented.");
    }
    private Task<JsonNode?> Provider(string plugin,string method,JsonArray args,CancellationToken ct) => _providers is null
        ? throw new NotSupportedException("This acquisition provider is not available in the app yet.") : _providers(plugin,method,args,ct);
    private JsonObject Start(ApplicationMediaRequest request,string? title)
    {
        var initial = _media.Start(request); string id = initial["jobId"]!.GetValue<string>();
        lock (_gate)
        {
            var state = (JsonObject)initial.DeepClone();
            _jobs.Add(id,(Task.CompletedTask,state));
            _jobs[id] = (Task.Run(()=>Complete(id,request,title)),state);
        }
        return (JsonObject)initial.DeepClone();
    }
    private async Task Complete(string id,ApplicationMediaRequest request,string? title)
    {
        try
        {
            await _media.WaitAsync(id).WaitAsync(_lifetime.Token);
            var finished = _media.Get(id);
            if (finished["acquired"]?.GetValue<bool>() == true)
            {
                var fields = new JsonObject { ["path"] = finished["path"]?.DeepClone(),["filename"] = finished["filename"]?.DeepClone() };
                if (request.Category == "themedeck")
                {
                    var previous = await Read("themedeck",_lifetime.Token);
                    foreach (var field in fields) previous[field.Key] = field.Value?.DeepClone();
                    fields = previous; fields["normalized"] = request.NormalizeAudio; fields["removed"] = false;
                    fields["upmixConsumerOption"] = request.UpmixAudio;
                    if (!string.IsNullOrWhiteSpace(title)) fields["title"] = title;
                }
                else
                {
                    fields["title"] = title ?? ""; fields["videoId"] = request.VideoId; fields["requestedHeight"] = request.Quality; fields["preferredSource"] = "local";
                    foreach(var key in new[] { "actualHeight","actualWidth","duration" }) fields[key] = finished[key]?.DeepClone();
                }
                await _store.PatchAsync(_identity,request.Category,fields,_lifetime.Token);
                if (_appId != 0)
                {
                    await BindAsync(_lifetime.Token);
                    var sinkFields = (JsonObject)fields.DeepClone();
                    if (request.Category == "themedeck")
                    {
                        sinkFields = new JsonObject();
                        foreach(var key in new[] {"path","filename","normalized","volume","start_offset","loop"}) if(fields.ContainsKey(key)) sinkFields[key] = fields[key]?.DeepClone();
                    }
                    var delivery = await _consumer.DeliverAsync(request.Category,_appId,sinkFields,_lifetime.Token);
                    finished["delivered"] = delivery["delivered"]?.DeepClone(); finished["runtimeSynced"] = delivery["runtimeSynced"]?.DeepClone(); finished["deliveryDetail"] = delivery["detail"]?.DeepClone();
                }
            }
            lock (_gate) _jobs[id] = (_jobs[id].Task,finished);
        }
        catch (OperationCanceledException) { _media.Cancel(id); lock (_gate) _jobs[id] = (_jobs[id].Task,new JsonObject { ["jobId"] = id,["status"] = "cancelled",["acquired"] = false,["delivered"] = false }); }
        catch (Exception e) { var observed = _media.Get(id); observed["deliveryError"] = e.GetType().Name; observed["delivered"] = false; lock (_gate) _jobs[id] = (_jobs[id].Task,observed); }
    }
    private JsonObject Job(string id)
    {
        lock (_gate)
        {
            if (!_jobs.TryGetValue(id,out var state)) throw new ArgumentException("Unknown source-scoped job.");
            if (state.Task.IsCompleted) return (JsonObject)state.Result.DeepClone();
            var progress = _media.Get(id); if (progress["status"]?.GetValue<string>() == "done") progress["status"] = "delivering";
            return progress;
        }
    }
    private static string YouTubeId(string value)
    {
        if (System.Text.RegularExpressions.Regex.IsMatch(value,"^[A-Za-z0-9_-]{11}$")) return value;
        if (!Uri.TryCreate(value,UriKind.Absolute,out var uri) || uri.Scheme != "https" || uri.Host is not ("youtube.com" or "www.youtube.com" or "youtu.be")) throw new ArgumentException("Choose a YouTube track.");
        string id = uri.Host == "youtu.be" ? uri.AbsolutePath.Trim('/') : uri.Query.TrimStart('?').Split('&').FirstOrDefault(x=>x.StartsWith("v=",StringComparison.Ordinal))?[2..] ?? "";
        return System.Text.RegularExpressions.Regex.IsMatch(id,"^[A-Za-z0-9_-]{11}$") ? id : throw new ArgumentException("Invalid YouTube media identity.");
    }
    public async ValueTask DisposeAsync()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        _lifetime.Cancel(); Task[] work; lock (_gate) work = _jobs.Values.Select(x=>x.Task).ToArray();
        await Task.WhenAll(work); await _media.DisposeAsync(); _lifetime.Dispose();
    }
}

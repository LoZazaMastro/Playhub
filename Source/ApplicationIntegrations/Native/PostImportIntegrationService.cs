using System.Text.Json.Nodes;
namespace Playhub.Emulation.Workbench;

public sealed record PostImportIntegrationRequest(uint AppId, string Identity, JsonObject Game, JsonObject Preview, string? MusicUrl = null, string? TrailerVideoId = null);

/// <summary>Calls verified Decky plugin contracts after Steam is ready. Download jobs remain queued outcomes until separately observed complete.</summary>
public sealed class PostImportIntegrationService(
    Func<CancellationToken, Task<bool>> ready,
    Func<string, string, JsonArray, CancellationToken, Task<JsonNode?>> call,
    TimeSpan? readinessInterval = null, TimeSpan? jobPollInterval = null, Func<uint, string, CancellationToken, Task<JsonNode?>>? selectStream = null, Func<uint, JsonObject, CancellationToken, Task<JsonNode?>>? composeHero = null, Func<uint, CancellationToken, Task<JsonNode?>>? restoreHeroLogo = null, bool sourceScoped = false)
{
    public async Task<JsonObject> RunAsync(PostImportIntegrationRequest request, CancellationToken ct, Action<JsonObject>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(request.Identity) || request.AppId == 0 && !sourceScoped) throw new ArgumentException("Imported game identity is required.");
        if (sourceScoped) Playhub.Integrations.ApplicationIntegrationIdentity.Validate(request.Identity);
        var title = request.Game["title"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Game title is required.");
        var enabled = (request.Preview["integrations"] as JsonArray ?? new()).Select(item => item?.GetValue<string>()).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var outcomes = new JsonArray();
        JsonObject Snapshot() => new() { ["identity"] = request.Identity, ["appId"] = request.AppId, ["outcomes"] = outcomes.DeepClone() };
        void Publish(JsonObject result) {
            var category=result["category"]?.GetValue<string>();
            var previous=outcomes.FirstOrDefault(x=>x?["category"]?.GetValue<string>()==category);
            if(previous is not null)outcomes.Remove(previous);
            outcomes.Add(result);progress?.Invoke(Snapshot());
        }
        var connected = false;
        for (var attempt = 0; attempt < 3 && !connected; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            using var probe = CancellationTokenSource.CreateLinkedTokenSource(ct); probe.CancelAfter(TimeSpan.FromSeconds(5));
            try { connected = await ready(probe.Token); } catch (OperationCanceledException) when (!ct.IsCancellationRequested) { } catch when (!ct.IsCancellationRequested) { }
            if (!connected && attempt < 2) await Task.Delay(readinessInterval ?? TimeSpan.FromSeconds(2), ct);
        }
        async Task Category(string category, Func<CancellationToken, Task<JsonObject>> action)
        {
            if (!connected) { Publish(Outcome(category, "unavailable", detail: "steam_or_decky_not_ready")); return; }
            Publish(Outcome(category, "running"));
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct); limit.CancelAfter(TimeSpan.FromSeconds(100));
            try { var result = await action(limit.Token); result["category"] = category; Publish(result); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { Publish(Outcome(category, "unavailable", detail: "timeout")); }
            catch when (!ct.IsCancellationRequested) { Publish(Outcome(category, "failed", detail: "plugin_operation_failed")); }
        }
        async Task<JsonObject> ObserveJob(string category, string plugin, string method, JsonNode? job, CancellationToken token)
        {
            job = JobSummary(job);
            if (job?["jobId"]?.GetValue<string>() is not string jobId || job["ok"]?.GetValue<bool>() == false) return Outcome(category, "failed", job);
            for (var attempt = 0; attempt < 30; attempt++)
            {
                var observed = JobSummary(await call(plugin, method, new JsonArray(jobId), token));
                if (observed is null) return Outcome(category, "unavailable", job, "job_status_unavailable");
                var status = observed["status"]?.GetValue<string>() ?? observed["state"]?.GetValue<string>();
                if (status is "done" or "completed") return Outcome(category, sourceScoped && observed["delivered"]?.GetValue<bool>() != true ? "prepared" : "completed", observed);
                if (status is "failed" or "missing" or "cancelled" || observed["ok"]?.GetValue<bool>() == false) return Outcome(category, status == "cancelled" ? "cancelled" : "failed", observed);
                Publish(Outcome(category, "running", observed));
                job = observed;
                await Task.Delay(jobPollInterval ?? TimeSpan.FromSeconds(2), token);
            }
            return Outcome(category, "running", job, "check_plugin_download_progress");
        }
        var editor = new GameIntegrationEditorService(call,sourceScoped);
        var app = request.AppId;
        var musicUrl = request.MusicUrl ?? request.Game["musicUrl"]?.GetValue<string>();
        var trailerVideoId = request.TrailerVideoId ?? request.Game["trailerVideoId"]?.GetValue<string>();
        if (enabled.Contains("artworks") && restoreHeroLogo is not null)
            await Category("hero-logo", async token =>
            {
                var result = await restoreHeroLogo(app, token);
                return Outcome("hero-logo", result?["ok"]?.GetValue<bool>() == true ? "completed" : "failed", result);
            });
        if (enabled.Contains("artworks") && request.Preview["artwork"]?["perfectHero"]?.GetValue<bool>() == true)
            await Category("perfect-hero", async token =>
            {
                if (composeHero is null) return Outcome("perfect-hero", "unavailable", detail: "composition_unavailable");
                var result = await composeHero(app, request.Game, token);
                return Outcome("perfect-hero", result?["ok"]?.GetValue<bool>() == true ? "completed" : "failed", result);
            });
        if (enabled.Contains("metadata"))
        {
            await Category("metadata", async token =>
            {
                JsonNode? data;
                if (request.Game["metadata"] is JsonObject draft)
                    data = await call("Playhub Metadata", "save_metadata", new JsonArray(app, draft.DeepClone()), token);
                else
                    data = await call("Playhub Metadata", "auto_fetch_metadata", new JsonArray(app, title), token);
                return Outcome("metadata", HasData(data) ? sourceScoped && data?["_delivery"]?["delivered"]?.GetValue<bool>()!=true ? "prepared":"completed" : "not_found", data);
            });
            var system = request.Game["system"]?.GetValue<string>() ?? request.Game["platform"]?.GetValue<string>();
            var retro = request.Preview["metadata"]?["achievements"]?["retroachievements"]?["enabled"]?.GetValue<bool>() == true;
            if (system == "ps3" || retro || request.Game.ContainsKey("raGameId"))
                await Category("achievements", async token =>
                {
                    if (request.Game.ContainsKey("raGameId"))
                    {
                        var selected = request.Game["raGameId"]?.GetValue<int>();
                        var selectedAchievements = await editor.SelectRaAsync(new(app,selected),token);
                        return Outcome("achievements", selected is null ? "not_found" : "completed", selectedAchievements, "manual_game_selection");
                    }
                    if (system == "ps3")
                    {
                        await call("Playhub Metadata", "set_achievement_source", new JsonArray(app, "rpcs3"), token);
                        var trophies = await call("Playhub Metadata", "sync_rpcs3_progress", new JsonArray(app), token);
                        return Outcome("achievements", HasData(trophies) ? "completed" : "not_found", trophies);
                    }
                    var path = request.Game["launchPath"]?.GetValue<string>() ?? request.Game["path"]?.GetValue<string>() ?? "";
                    var resolved = await call("Playhub Metadata", "resolve_retroachievements_from_path", new JsonArray(app, path, title), token);
                    if (resolved is null) return Outcome("achievements", "not_found");
                    var achievements = await call("Playhub Metadata", "fetch_achievements", new JsonArray(app), token);
                    return Outcome("achievements", HasData(achievements) ? "completed" : "not_found", achievements);
                });
        }
        async Task<JsonObject> ApplyMusicSettings(JsonNode? assigned, CancellationToken token)
        {
            if(!HasData(assigned) || string.IsNullOrWhiteSpace(assigned?["path"]?.GetValue<string>()))return Outcome("themedeck","failed",assigned,"assignment_readback");
            if(request.Game["musicSettings"] is JsonObject settings)
            {
                foreach(var field in settings)
                    await editor.MusicAsync(new(app,field.Key,field.Value?.DeepClone()),token);
                assigned=await editor.MusicAsync(new(app),token);
            }
            return Outcome("themedeck",HasData(assigned)?"completed":"failed",assigned,"assignment_readback");
        }
        if (enabled.Contains("themedeck"))
            await Category("themedeck", async token =>
            {
                if(request.Game["musicSettings"]?["remove"]?.GetValue<bool>() == true)
                {
                    await editor.MusicAsync(new(app,"remove"),token);
                    var remaining=await editor.MusicAsync(new(app),token);
                    return Outcome("themedeck",remaining is null?"completed":"failed",detail:"manual_track_removal");
                }
                var existing = await call("ThemeDeck", "get_track", new JsonArray(app), token);
                if (string.IsNullOrWhiteSpace(musicUrl) && HasData(existing) && request.Preview["themedeck"]?["onlyMissing"]?.GetValue<bool>() != false) return await ApplyMusicSettings(existing, token);
                if (string.IsNullOrWhiteSpace(musicUrl))
                {
                    var matches = await call("ThemeDeck", "search_youtube", new JsonArray(title + " soundtrack main theme", 5), token);
                    return Outcome("themedeck", (matches?["results"] as JsonArray)?.Count > 0 ? "selection_required" : "not_found", matches);
                }
                if (!YouTube(musicUrl)) throw new ArgumentException("Choose a YouTube track.");
                var settings = request.Preview["themedeck"];
                var downloadArgs = new JsonArray(app,musicUrl,settings?["normalizeAudio"]?.GetValue<bool>() ?? false,settings?["upmixAudio"]?.GetValue<bool>() ?? false);
                if(sourceScoped) downloadArgs.Add(request.Game["musicTitle"]?.DeepClone());
                var job = await call("ThemeDeck", "start_game_download", downloadArgs, token);
                var result = await ObserveJob("themedeck", "ThemeDeck", "get_discover_download_progress", job, token);
                if (result["status"]?.GetValue<string>() != "completed") return result;
                var assigned = await call("ThemeDeck", "get_track", new JsonArray(app), token);
                return await ApplyMusicSettings(assigned, token);
            });
        if (enabled.Contains("trailerhero"))
            await Category("trailerhero", async token =>
            {
                var existing = await call("TrailerHero", "get_local_trailer", new JsonArray(app), token);
                if (string.IsNullOrWhiteSpace(trailerVideoId) && existing?["ok"]?.GetValue<bool>() == true && existing["assigned"]?.GetValue<bool>() == true) return Outcome("trailerhero", "completed", existing, "existing_trailer_kept");
                if (string.IsNullOrWhiteSpace(trailerVideoId))
                {
                    var matches = await call("TrailerHero", "search_youtube_videos", new JsonArray(title + " official trailer", 5), token);
                    return Outcome("trailerhero", matches?["ok"]?.GetValue<bool>() == true && (matches["results"] as JsonArray)?.Count > 0 ? "selection_required" : "not_found", matches);
                }
                if (request.Preview["trailers"]?["offline"]?.GetValue<bool>() != true)
                {
                    if (selectStream is null) return Outcome("trailerhero", "unavailable", detail: "trailer_adapter_unavailable");
                    var selected = await selectStream(app, trailerVideoId, token);
                    return Outcome("trailerhero", selected?["ok"]?.GetValue<bool>() == true ? sourceScoped && selected["delivered"]?.GetValue<bool>() != true ? "prepared" : "completed" : "unavailable", selected);
                }
                var job = await call("TrailerHero", "start_trailer_download", new JsonArray(app, "youtube", trailerVideoId, request.Preview["trailers"]?["quality"]?.GetValue<int>() ?? 1080, sourceScoped ? request.Game["trailerTitle"]?.GetValue<string>() ?? title : title, 0), token);
                return await ObserveJob("trailerhero", "TrailerHero", "get_trailer_job", job, token);
            });
        if (enabled.Contains("launch-curtain"))
            await Category("launch-curtain", async token =>
            {
                if(request.Game["curtainSettings"] is JsonObject settings)
                {
                    var applied=await editor.CurtainAsync(new(app,settings),token);
                    return Outcome("launch-curtain",applied?["ok"]?.GetValue<bool>()==true?sourceScoped && applied?["delivery"]?["delivered"]?.GetValue<bool>()!=true?"prepared":"completed":"failed",applied,"manual_settings");
                }
                var existing = await call("Launch Curtain", "get_game_settings", new JsonArray(new JsonObject { ["app_id"] = app }), token);
                if (!string.IsNullOrEmpty(existing?["settings"]?["fullscreen_image_path"]?.GetValue<string>())) return Outcome("launch-curtain", "completed", existing, "existing_background_kept");
                var path = request.Game["steamArtwork"]?["hero"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return Outcome("launch-curtain", "not_found", detail: "hero_image_required");
                var saved = await call("Launch Curtain", "save_game_settings", new JsonArray(new JsonObject { ["app_id"] = app, ["settings"] = new JsonObject { ["fullscreen_image_path"] = path } }), token);
                return Outcome("launch-curtain", saved?["ok"]?.GetValue<bool>() == true && saved["settings"]?["fullscreen_image_path"]?.GetValue<string>() == path ? sourceScoped && saved?["delivery"]?["delivered"]?.GetValue<bool>()!=true?"prepared":"completed" : "failed", saved);
            });
        return new JsonObject { ["identity"] = request.Identity, ["appId"] = app, ["outcomes"] = outcomes };
    }
    private static JsonNode? JobSummary(JsonNode? value)
    {
        if(value is not JsonObject source)return null;
        var output=new JsonObject();
        foreach(var key in new[]{"jobId","ok","status","state","progress","filename","path","error","running","acquired","delivered","runtimeSynced","deliveryDetail","deliveryError","actualWidth","actualHeight","duration"})
            if(source.TryGetPropertyValue(key,out var item))output[key]=item?.DeepClone();
        return output;
    }
    private static JsonObject Outcome(string category, string status, JsonNode? data = null, string? detail = null) => new() { ["category"] = category, ["status"] = status, ["data"] = data?.DeepClone(), ["detail"] = detail };
    private static bool HasData(JsonNode? value) => value is JsonObject obj && obj.Count > 0 && obj["error"] is null && obj["ok"]?.GetValue<bool>() != false;
    private static bool YouTube(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host is "youtube.com" or "www.youtube.com" or "youtu.be";
}

using System.Text.Json.Nodes;
namespace Playhub.Emulation.Workbench;

public sealed record GameAchievementRequest(uint AppId, string Provider = "retroachievements", bool Refresh = false);
public sealed record GameRaSearchRequest(string Query, uint AppId = 0, int Limit = 8);
public sealed record GameRaSelectRequest(uint AppId, int? GameId);
public sealed record GameMusicRequest(uint AppId, string Action = "get", JsonNode? Value = null);
public sealed record GameCurtainRequest(uint AppId, JsonObject? Settings = null);
public sealed record GameMetadataRequest(uint AppId, JsonObject Fields);

/// <summary>Per-game editing through the real plugin contracts; never exposes account settings.</summary>
public sealed class GameIntegrationEditorService
{
    private readonly Func<string,string,JsonArray,CancellationToken,Task<JsonNode?>> _call;
    private readonly bool _sourceScoped;
    public GameIntegrationEditorService(SteamPluginBridge bridge) : this(bridge.CallAsync, bridge.IsAppOwned) { }
    public GameIntegrationEditorService(Func<string,string,JsonArray,CancellationToken,Task<JsonNode?>> call, bool sourceScoped = false) { _call = call; _sourceScoped = sourceScoped; }
    private void App(uint id) { if(id == 0 && !_sourceScoped) throw new ArgumentException("Import the game before editing its plugin settings."); }
    public Task<JsonNode?> SearchRaAsync(GameRaSearchRequest request, CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(request.Query) || request.Query.Length > 200) throw new ArgumentException("Enter a game title.");
        return _call("Playhub Metadata", "search_retroachievements_games", new JsonArray(request.Query.Trim(), Math.Clamp(request.Limit,1,20), request.AppId),ct);
    }
    public async Task<JsonObject> SelectRaAsync(GameRaSelectRequest request,CancellationToken ct)
    {
        App(request.AppId); if(request.GameId is <= 0) throw new ArgumentException("Invalid RetroAchievements game ID.");
        await _call("Playhub Metadata","set_retroachievements_game_id",new JsonArray(JsonValue.Create(request.AppId),JsonValue.Create(request.GameId)),ct);
        return request.GameId is null ? NormalizeAchievements(null,"retroachievements") : await AchievementsAsync(new(request.AppId),ct);
    }
    public async Task<JsonObject> AchievementsAsync(GameAchievementRequest request,CancellationToken ct)
    {
        App(request.AppId);
        if(request.Provider is not ("retroachievements" or "rpcs3")) throw new ArgumentException("Unsupported achievement provider.");
        var method = request.Refresh ? request.Provider == "rpcs3" ? "sync_rpcs3_progress" : "sync_retroachievements_progress" : "fetch_achievements";
        return NormalizeAchievements(await _call("Playhub Metadata",method,new JsonArray(request.AppId),ct),request.Provider);
    }
    public static JsonObject NormalizeAchievements(JsonNode? response,string provider)
    {
        var items = new JsonArray(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach(var item in AchievementItems(response))
            {
                var id = item["strID"]?.ToString() ?? item["strName"]?.ToString() ?? ""; if(!seen.Add(id)) continue;
                var image = new[]{"strImageURL","strImageUrl","strIconURL","iconUrl","imageUrl","playhubImage","strImage"}.Select(k=>item[k]?.ToString()).FirstOrDefault(v=>!string.IsNullOrWhiteSpace(v));
                items.Add(new JsonObject { ["id"]=id,["name"]=item["strName"]?.DeepClone(),["description"]=item["strDescription"]?.DeepClone(),["achieved"]=item["bAchieved"]?.DeepClone() ?? JsonValue.Create(false),["imageUrl"]=image });
            }
        return new JsonObject { ["available"]=response is not null,["provider"]=response?["provider"]?.ToString() ?? provider,["gameId"]=response?["game_id"]?.ToString(),["title"]=response?["title"]?.DeepClone(),["achieved"]=response?["progress"]?["achieved"]?.DeepClone() ?? response?["steam"]?["nAchieved"]?.DeepClone() ?? JsonValue.Create(0),["total"]=response?["progress"]?["total"]?.DeepClone() ?? response?["steam"]?["nTotal"]?.DeepClone() ?? JsonValue.Create(0),["items"]=items };
    }
    private static IEnumerable<JsonObject> AchievementItems(JsonNode? response)
    {
        foreach(var key in new[]{"achieved","hidden","unachieved"})
            foreach(var pair in response?["user"]?["data"]?[key] as JsonObject ?? new())
                if(pair.Value is JsonObject item) yield return item;
        foreach(var key in new[]{"vecAchievedHidden","vecHighlight","vecUnachieved"})
            foreach(var item in (response?["steam"]?[key] as JsonArray ?? new()).OfType<JsonObject>()) yield return item;
    }
    public async Task<JsonNode?> MusicAsync(GameMusicRequest request,CancellationToken ct)
    {
        App(request.AppId); var args = new JsonArray(request.AppId);
        var method = request.Action switch { "get"=>"get_track","volume"=>"set_volume","startOffset"=>"set_start_offset","loop"=>"set_loop","remove"=>"remove_track",_=>throw new ArgumentException("Unknown music action.") };
        if(request.Action is "volume" or "startOffset") { Number(request.Value,0,request.Action == "volume" ? 1 : 30); args.Add(request.Value!.DeepClone()); }
        if(request.Action == "loop") { if(request.Value is not JsonValue v || !v.TryGetValue<bool>(out _)) throw new ArgumentException("Loop must be boolean."); args.Add(request.Value.DeepClone()); }
        var result = await _call("ThemeDeck",method,args,ct);
        return request.Action == "get" ? result : result?[request.AppId.ToString()]?.DeepClone();
    }
    public Task<JsonNode?> CurtainAsync(GameCurtainRequest request,CancellationToken ct)
    {
        App(request.AppId); var arg = new JsonObject{["app_id"]=request.AppId};
        if(request.Settings is not null) { ValidateCurtain(request.Settings); arg["settings"]=request.Settings.DeepClone(); }
        return _call("Launch Curtain",request.Settings is null ? "get_game_settings" : "save_game_settings",new JsonArray(arg),ct);
    }
    public static void ValidateCurtain(JsonObject fields)
    {
        foreach(var (key,value) in fields)
        {
            switch(key)
            {
                case "enabled": case "show_logo": case "logo_zoom_enabled": case "bg_zoom_enabled": case "timeout_enabled":
                    if(value is not JsonValue b || !b.TryGetValue<bool>(out _)) throw new ArgumentException("Expected boolean: " + key); break;
                case "logo_position_x": case "logo_position_y": case "background_position_x": case "background_position_y": case "background_opacity": case "logo_shadow_opacity": case "logo_shadow_blur": case "soundbite_volume": Number(value,0,100); break;
                case "logo_scale": case "background_scale": Number(value,50,200); break;
                case "timeout_seconds": Number(value,5,60); break;
                case "exit_delay_seconds": Number(value,0,10); break;
                case "force_mode": if(value?.ToString() is not ("auto" or "classic" or "modern")) throw new ArgumentException("Invalid display mode."); break;
                case "fullscreen_image_path": case "soundbite_path":
                    var path = value?.GetValue<string>() ?? ""; if(path.Length > 0 && (!Path.IsPathFullyQualified(path) || !File.Exists(path))) throw new ArgumentException("Choose an existing local file."); break;
                case "background_search_query": case "soundbite_title": case "soundbite_search_query": if(value is not JsonValue s || !s.TryGetValue<string>(out var text) || text.Length >180) throw new ArgumentException("Invalid text."); break;
                default: throw new ArgumentException("Unsupported Launch Curtain setting: " + key);
            }
        }
    }
    private static void Number(JsonNode? value,double min,double max)
    { if(value is null || !double.TryParse(value.ToJsonString(),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var number) || !double.IsFinite(number) || number < min || number > max) throw new ArgumentException("Value is outside the supported range."); }
    public async Task<JsonNode?> MetadataAsync(GameMetadataRequest request,CancellationToken ct)
    {
        App(request.AppId); ValidateMetadata(request.Fields);
        var current = await _call("Playhub Metadata","get_metadata",new JsonArray(request.AppId),ct) as JsonObject ?? new();
        foreach(var field in request.Fields) current[field.Key]=field.Value?.DeepClone();
        return await _call("Playhub Metadata","save_metadata",new JsonArray(JsonValue.Create(request.AppId),current),ct);
    }
    public static void ValidateMetadata(JsonObject fields)
    {
        foreach(var (key,value) in fields)
        {
            if(key is "title" or "description" or "short_description") { if(value is not JsonValue s || !s.TryGetValue<string>(out var text) || text.Length > 100000) throw new ArgumentException("Invalid metadata text."); }
            else if(key is "genres" or "features") { if(value is not JsonArray a || a.Any(x=>x is not JsonValue v || !v.TryGetValue<string>(out _))) throw new ArgumentException("Expected list of text."); }
            else if(key is "developers" or "publishers") { if(value is not JsonArray a || a.Any(x=>x is not JsonObject o || o["name"] is not JsonValue v || !v.TryGetValue<string>(out _))) throw new ArgumentException("Expected people with names."); }
            else if(key == "release_date") { if(value is not null) Number(value,0,32503680000); }
            else if(key == "rating") { if(value is not null) Number(value,0,100); }
            else throw new ArgumentException("Unsupported metadata field: " + key);
        }
    }
}

using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace Playhub.Emulation.Workbench;

/// <summary>Uses Steam's existing Decky router. Never opens a second Decky WebSocket.</summary>
public sealed class SteamPluginBridge
{
    private readonly Func<string, string, JsonArray, CancellationToken, Task<JsonNode?>>? _application;
    public SteamPluginBridge(Func<string, string, JsonArray, CancellationToken, Task<JsonNode?>>? application = null) => _application = application;
    public bool IsAppOwned => _application is not null;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };
    private static readonly Dictionary<string, HashSet<string>> Allowed = new()
    {
        ["Playhub Metadata"] = ["get_scraper_settings", "set_scraper_settings", "set_achievement_cache_policy", "get_metadata", "auto_fetch_metadata", "save_metadata", "set_achievement_source", "sync_rpcs3_progress", "resolve_retroachievements_from_path", "fetch_achievements", "search_retroachievements_games", "set_retroachievements_game_id", "sync_retroachievements_progress"],
        ["Launch Curtain"] = ["get_game_settings", "save_game_settings", "search_google_images", "download_google_image", "get_image_preview"],
        ["ThemeDeck"] = ["get_track", "set_volume", "set_start_offset", "set_loop", "remove_track", "search_youtube", "start_game_download", "get_discover_download_progress"],
        ["TrailerHero"] = ["get_local_trailer", "search_youtube_videos", "start_trailer_download", "get_trailer_job"],
        ["Playhub Artworks"] = ["search_provider_games", "search_provider_assets", "prepare_artwork_transfer", "read_artwork_transfer_chunk", "release_artwork_transfer", "get_local_asset_info"]
    };

    public Task<JsonNode?> CallAsync(string plugin, string method, JsonArray args, CancellationToken ct)
    {
        if (_application is not null && ((plugin == "Playhub Metadata" && method is "get_metadata" or "save_metadata" or "auto_fetch_metadata")
            || plugin is "ThemeDeck" or "TrailerHero" or "Launch Curtain" || plugin == "Playhub Artworks" && method.StartsWith("search_", StringComparison.Ordinal)))
            return _application(plugin, method, args, ct);
        if (!Allowed.TryGetValue(plugin, out var methods) || !methods.Contains(method)) throw new ArgumentException("Plugin operation is not available.");
        var input = new JsonObject { ["plugin"] = plugin, ["method"] = method, ["args"] = args.DeepClone() };
        return EvaluateAsync("(async()=>{const p=" + input.ToJsonString() + "; if(!window.DeckyBackend?.call)throw Error('decky_unavailable');return await window.DeckyBackend.call('loader/call_plugin_method',p.plugin,p.method,...p.args);})()", ct);
    }

    public async Task<JsonNode?> StatusAsync(CancellationToken ct) => await EvaluateAsync(
        "({steam:true,decky:typeof window.DeckyBackend?.call==='function',artwork:typeof window.SteamClient?.Apps?.SetCustomArtworkForApp==='function'})", ct);

    public async Task<bool> AppReadyAsync(uint appId, CancellationToken ct)
    {
        if (_application is not null) { ct.ThrowIfCancellationRequested(); return true; } // Acquisition uses the app's scoped source, not Steam/Decky readiness.
        if (appId == 0) return false;
        var result = await EvaluateAsync("(()=>{const id=" + appId + ";const app=window.appStore?.GetAppOverviewByAppID?.(id);return typeof window.DeckyBackend?.call==='function'&&Number(app?.appid??app?.nAppID)===id})()", ct);
        return result is JsonValue value && value.TryGetValue<bool>(out var ready) && ready;
    }

    public Task<JsonNode?> SelectStreamingTrailerAsync(uint appId, string videoId, CancellationToken ct)
    {
        if (_application is not null) return _application("TrailerHero","select_streaming_trailer",new JsonArray(appId,videoId),ct);
        if (appId == 0 || !System.Text.RegularExpressions.Regex.IsMatch(videoId ?? "", "^[A-Za-z0-9_-]{11}$")) throw new ArgumentException("Invalid trailer selection.");
        var data = new JsonObject { ["appId"] = appId, ["videoId"] = videoId, ["requestId"] = Guid.NewGuid().ToString("N") };
        return EvaluateAsync("new Promise(resolve=>{const d=" + data.ToJsonString() + ";const done=e=>{if(e.detail?.requestId!==d.requestId)return;clearTimeout(timer);window.removeEventListener('playhub:trailer-selection-result',done);resolve(e.detail)};const timer=setTimeout(()=>{window.removeEventListener('playhub:trailer-selection-result',done);resolve({ok:false,error:'trailer_adapter_unavailable'})},3000);window.addEventListener('playhub:trailer-selection-result',done);window.dispatchEvent(new CustomEvent('playhub:select-streaming-trailer',{detail:d}))})", ct);
    }

    public Task<JsonNode?> ReadStreamingTrailerAsync(uint appId, CancellationToken ct)
    {
        if (_application is not null) return _application("TrailerHero","get_streaming_trailer",new JsonArray(appId),ct);
        if (appId == 0) throw new ArgumentException("Imported game required.");
        return EvaluateAsync("(()=>{const key='" + appId + "';const s=JSON.parse(localStorage.getItem('trailerhero.settings.v1')||'{}');const id=s.youtubeVideos?.[key];return {videoId:/^[A-Za-z0-9_-]{11}$/.test(id||'')?id:null,preferredSource:s.preferredSources?.[key]||null}})()", ct);
    }

    public Task<JsonNode?> ApplyCoverAsync(uint appId, string base64, string format, CancellationToken ct)
    {
        if (appId == 0 || base64.Length > 40 * 1024 * 1024 || format is not ("png" or "jpg" or "jpeg")) throw new ArgumentException("Invalid cover.");
        var input = new JsonObject { ["appId"] = appId, ["data"] = base64, ["format"] = format };
        // Same Steam call and asset-type 0 used by Playhub Artworks. No private grid-file writes while Steam runs.
        return EvaluateAsync("(async()=>{const p=" + input.ToJsonString() + ";if(!window.SteamClient?.Apps?.SetCustomArtworkForApp)throw Error('steam_unavailable');await window.SteamClient.Apps.SetCustomArtworkForApp(p.appId,p.data,p.format,0);return {applied:true};})()", ct);
    }

    internal static async Task<JsonNode?> EvaluateAsync(string expression, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(90));
        var targets = JsonNode.Parse(await Http.GetStringAsync("http://127.0.0.1:8080/json", timeout.Token))?.AsArray();
        var target = targets?.OfType<JsonObject>().FirstOrDefault(item => item["title"]?.GetValue<string>() == "SharedJSContext");
        if (!Uri.TryCreate(target?["webSocketDebuggerUrl"]?.GetValue<string>(), UriKind.Absolute, out var address)
            || address.Scheme != "ws" || address.Host is not ("127.0.0.1" or "localhost") || address.Port != 8080)
            throw new InvalidOperationException("Open Steam to apply the selected integrations.");
        using var socket = new ClientWebSocket(); await socket.ConnectAsync(address, timeout.Token);
        var payload = new JsonObject { ["id"] = 1, ["method"] = "Runtime.evaluate", ["params"] = new JsonObject
            { ["expression"] = expression, ["awaitPromise"] = true, ["returnByValue"] = true } };
        await socket.SendAsync(Encoding.UTF8.GetBytes(payload.ToJsonString()), WebSocketMessageType.Text, true, timeout.Token);
        var buffer = new byte[65536];
        while (true)
        {
            using var message = new MemoryStream(); WebSocketReceiveResult part;
            do
            {
                part = await socket.ReceiveAsync(buffer, timeout.Token);
                if (part.MessageType == WebSocketMessageType.Close) throw new IOException("Steam disconnected.");
                message.Write(buffer, 0, part.Count);
                if (message.Length > 48 * 1024 * 1024) throw new IOException("Plugin response is too large.");
            } while (!part.EndOfMessage);
            var response = JsonNode.Parse(message.ToArray());
            if (response?["id"]?.GetValue<int>() != 1) continue;
            if (response["error"] is not null || response["result"]?["exceptionDetails"] is not null)
                throw new InvalidOperationException("The plugin could not complete this operation.");
            return response["result"]?["result"]?["value"]?.DeepClone();
        }
    }
}

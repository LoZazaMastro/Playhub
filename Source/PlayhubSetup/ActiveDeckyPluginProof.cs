using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace PlayhubSetup;

// Reads the function held by the current loader, not a resource timestamp or
// the last scriptParsed event. It never calls the plugin's onDismount callback.
internal static class ActiveDeckyPluginProof
{
    internal const string Capture = """
        (() => {
          const loader = window.DeckyPluginLoader;
          if (!Array.isArray(loader?.plugins)) return null;
          const list = loader.plugins.filter(p => p.name === 'Playhub');
          if (list.length !== 1 || typeof list[0].onDismount !== 'function') return null;
          return {loader, plugin:list[0], callback:list[0].onDismount};
        })()
        """;
    internal const string Confirm = """
        function() {
          const loader = window.DeckyPluginLoader, store = window.SteamUIStore;
          const list = Array.isArray(loader?.plugins) ? loader.plugins.filter(p => p.name === 'Playhub') : [];
          return loader === this.loader && list.length === 1 && list[0] === this.plugin
            && this.plugin.onDismount === this.callback
            && typeof store?.IsAnyAppRunning === 'function' && store.IsAnyAppRunning() === false;
        }
        """;

    internal static async Task<string?> ReadAsync(CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        using var http = new HttpClient();
        var targets = JsonNode.Parse(await http.GetStringAsync("http://127.0.0.1:8080/json", deadline.Token))?.AsArray();
        var matches = targets?.OfType<JsonObject>().Where(x => x["title"]?.GetValue<string>() == "SharedJSContext").ToArray();
        if (matches?.Length != 1 || !Uri.TryCreate(matches[0]["webSocketDebuggerUrl"]?.GetValue<string>(), UriKind.Absolute, out var uri)
            || uri.Scheme != "ws" || uri.Host is not ("localhost" or "127.0.0.1") || uri.Port != 8080) return null;
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(uri, deadline.Token);
        int sequence = 0;
        async Task<JsonObject> Call(string method, JsonObject? parameters = null)
        {
            int id = ++sequence;
            var request = new JsonObject { ["id"] = id, ["method"] = method, ["params"] = parameters ?? new JsonObject() };
            await socket.SendAsync(Encoding.UTF8.GetBytes(request.ToJsonString()), WebSocketMessageType.Text, true, deadline.Token);
            var buffer = new byte[65536];
            while (true)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult part;
                do
                {
                    part = await socket.ReceiveAsync(buffer, deadline.Token);
                    if (part.MessageType != WebSocketMessageType.Text) throw new IOException("Steam debugger disconnected.");
                    message.Write(buffer, 0, part.Count);
                    if (message.Length > 24 * 1024 * 1024) throw new IOException("Steam script response exceeded its limit.");
                } while (!part.EndOfMessage);
                var response = JsonNode.Parse(message.ToArray())?.AsObject();
                if (response?["id"]?.GetValue<int>() != id) continue;
                if (response["error"] is not null) throw new InvalidOperationException("Steam script proof unavailable.");
                return response["result"]?.AsObject() ?? throw new InvalidOperationException("Steam script proof missing.");
            }
        }
        return await ReadAsync(Call, deadline.Token);
    }

    // Injectable CDP boundary for causal tests; all parsing and hashing below is
    // the production path. Unknown or changing loader state never verifies.
    internal static async Task<string?> ReadAsync(Func<string, JsonObject?, Task<JsonObject>> call, CancellationToken ct)
    {
        string group = "playhub-active-proof-" + Guid.NewGuid().ToString("N");
        try
        {
            ct.ThrowIfCancellationRequested();
            await call("Debugger.enable", null).WaitAsync(ct);
            var held = await call("Runtime.evaluate", new() { ["expression"] = Capture, ["returnByValue"] = false, ["objectGroup"] = group }).WaitAsync(ct);
            string? holder = held["result"]?["objectId"]?.GetValue<string>();
            if (held["exceptionDetails"] is not null || string.IsNullOrWhiteSpace(holder)) return null;
            var properties = await call("Runtime.getProperties", new() { ["objectId"] = holder, ["ownProperties"] = true }).WaitAsync(ct);
            var callback = properties["result"]?.AsArray().OfType<JsonObject>().SingleOrDefault(p => p["name"]?.GetValue<string>() == "callback")?["value"];
            string? function = callback?["objectId"]?.GetValue<string>();
            if (callback?["type"]?.GetValue<string>() != "function" || string.IsNullOrWhiteSpace(function)) return null;
            var details = await call("Runtime.getProperties", new() { ["objectId"] = function, ["ownProperties"] = true }).WaitAsync(ct);
            string? scriptId = details["internalProperties"]?.AsArray().OfType<JsonObject>()
                .SingleOrDefault(p => p["name"]?.GetValue<string>() == "[[FunctionLocation]]")?["value"]?["value"]?["scriptId"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(scriptId)) return null;
            var script = await call("Debugger.getScriptSource", new() { ["scriptId"] = scriptId }).WaitAsync(ct);
            string? source = script["scriptSource"]?.GetValue<string>();
            if (source is null || Encoding.UTF8.GetByteCount(source) > 16 * 1024 * 1024) return null;
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
            var final = await call("Runtime.callFunctionOn", new() { ["objectId"] = holder, ["functionDeclaration"] = Confirm, ["returnByValue"] = true }).WaitAsync(ct);
            ct.ThrowIfCancellationRequested();
            return final["exceptionDetails"] is null && final["result"]?["value"]?.GetValue<bool>() == true ? hash : null;
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or System.Text.Json.JsonException
            or OperationCanceledException or HttpRequestException or WebSocketException or ArgumentException)
        { return null; }
        finally
        {
            try { await call("Runtime.releaseObjectGroup", new() { ["objectGroup"] = group }).WaitAsync(ct); }
            catch { }
        }
    }
}

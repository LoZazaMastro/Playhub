using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace Playhub.Integrations;

/// <summary>Applies artwork through Steam's existing shared JavaScript context.</summary>
public static class SteamPluginBridge
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };

    internal static async Task<JsonNode?> EvaluateAsync(string expression, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(90));
        var targets = JsonNode.Parse(await Http.GetStringAsync("http://127.0.0.1:8080/json", timeout.Token))?.AsArray();
        var target = targets?.OfType<JsonObject>().FirstOrDefault(item => item["title"]?.GetValue<string>() == "SharedJSContext");
        if (!Uri.TryCreate(target?["webSocketDebuggerUrl"]?.GetValue<string>(), UriKind.Absolute, out var address)
            || address.Scheme != "ws" || address.Host is not ("127.0.0.1" or "localhost") || address.Port != 8080)
            throw new InvalidOperationException("Open Steam to apply the selected artwork.");
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
                if (message.Length > 48 * 1024 * 1024) throw new IOException("Steam response is too large.");
            } while (!part.EndOfMessage);
            var response = JsonNode.Parse(message.ToArray());
            if (response?["id"]?.GetValue<int>() != 1) continue;
            if (response["error"] is not null || response["result"]?["exceptionDetails"] is not null)
                throw new InvalidOperationException("Steam could not complete the artwork operation.");
            return response["result"]?["result"]?["value"]?.DeepClone();
        }
    }
}

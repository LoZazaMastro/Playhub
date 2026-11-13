using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text.Json;
using Playhub.Shared;

namespace GamingMode.Services;

internal sealed class SteamThemeReadinessProbe
{
    private static readonly HttpClient Http = new(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromMilliseconds(650) };
    private readonly Func<int, long?> _verifyOwner;
    internal SteamThemeReadinessProbe(Func<int, long?> verifyOwner) => _verifyOwner = verifyOwner;
    internal const string ReadCurrentMain = """
        (()=>{try{
          const store=window.SteamUIStore?.WindowStore;
          const main=store?.GamepadUIMainWindowInstance;
          if(!main || main.IsMainGamepadUIWindow?.()!==true || main.IsGamepadUIOverlayWindow?.()!==false || main.IsAnyVRWindow?.()===true) return null;
          const view=main.BrowserWindow??main.m_BrowserWindow??main.m_popup?.window;
          if(!view||view.closed) return null;
          const doc=view.document,gate=view.__playhubThemeReadinessV1;
          // Windows occlusion can hide a valid covered document from Chromium.
          // Steam's active instance plus native geometry identify the surface.
          if(window.SteamUIStore.ActiveWindowInstance!==main) return null;
          if(!gate||gate.ownerDocument!==doc||gate.ownerRoot!==doc.documentElement||gate.ownerHead!==doc.head) return null;
          const browserId=view.SteamClient?.Browser?.GetBrowserID?.(),result=gate.read();
          if(!Number.isSafeInteger(browserId)||browserId<=0||result?.browserId!==browserId) return null;
          if(store.GamepadUIMainWindowInstance!==main||view.document!==doc||view.__playhubThemeReadinessV1!==gate) return null;
          const ratio=view.devicePixelRatio;
          if(!Number.isFinite(ratio)||ratio<=0||ratio>8) return null;
          return {main:true,browserId,documentId:result.documentId,receipt:result,
            physicalWidth:Math.round(view.innerWidth*ratio),physicalHeight:Math.round(view.innerHeight*ratio)};
        }catch{return null;}})()
        """;

    internal async Task<SteamThemeReceipt?> ReadAsync(CancellationToken cancellation)
    {
        var value=await ReadValueAsync(ReadCurrentMain,false,cancellation).ConfigureAwait(false);
        return value is {} result?SteamThemeReceipt.Parse(result):null;
    }
    internal async Task<SteamTaskbarMainProof?> ReadBigPictureAsync(long epoch,CancellationToken cancellation)
    {
        var value=await ReadValueAsync(SteamTaskbarMainProof.ReadCurrentMain,true,cancellation).ConfigureAwait(false);
        return value is {} result?SteamTaskbarMainProof.Parse(result,epoch,Environment.TickCount64):null;
    }
    private async Task<JsonElement?> ReadValueAsync(string expression,bool awaitPromise,CancellationToken cancellation)
    {
        try
        {
            int? owner = DeckyStartupGuard.ListenerOwner(8080);
            long? birth = owner.HasValue ? _verifyOwner(owner.Value) : null;
            if (!owner.HasValue || !birth.HasValue) return null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(750);
            using var response = await Http.GetAsync("http://127.0.0.1:8080/json", HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            byte[] list = await ReadBoundedAsync(stream, 256 * 1024, timeout.Token).ConfigureAwait(false);
            using var targets = JsonDocument.Parse(list);
            if (targets.RootElement.ValueKind != JsonValueKind.Array) return null;
            var shared = targets.RootElement.EnumerateArray().Where(IsSharedSteamTarget).ToArray();
            if (shared.Length != 1 || !shared[0].TryGetProperty("webSocketDebuggerUrl", out var websocket)
                || !IsOwnedEndpoint(websocket.GetString(), out Uri? address)
                || address!.AbsolutePath != "/devtools/page/" + shared[0].GetProperty("id").GetString()) return null;
            if (DeckyStartupGuard.ListenerOwner(8080) != owner || _verifyOwner(owner.Value) != birth) return null;
            using var socket = new ClientWebSocket();
            socket.Options.Proxy = null;
            await socket.ConnectAsync(address!, timeout.Token).ConfigureAwait(false);
            byte[] request = JsonSerializer.SerializeToUtf8Bytes(new { id = 1, method = "Runtime.evaluate",
                @params = new { expression, returnByValue = true, awaitPromise } });
            await socket.SendAsync(request, WebSocketMessageType.Text, true, timeout.Token).ConfigureAwait(false);
            byte[] buffer = new byte[8192];
            while (socket.State == WebSocketState.Open)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult part;
                do
                {
                    part = await socket.ReceiveAsync(buffer, timeout.Token).ConfigureAwait(false);
                    if (part.MessageType != WebSocketMessageType.Text) return null;
                    message.Write(buffer, 0, part.Count);
                    if (message.Length > 64 * 1024) return null;
                } while (!part.EndOfMessage);
                using var reply = JsonDocument.Parse(message.ToArray());
                var root = reply.RootElement;
                if (!root.TryGetProperty("id", out var id) || id.GetInt32() != 1) continue;
                if (root.TryGetProperty("error", out _) || !root.TryGetProperty("result", out var evaluated)
                    || evaluated.TryGetProperty("exceptionDetails", out _)
                    || !evaluated.TryGetProperty("result", out var value) || !value.TryGetProperty("value", out var result)) return null;
                // The listener and its process birth must still own this response.
                if (DeckyStartupGuard.ListenerOwner(8080) != owner || _verifyOwner(owner.Value) != birth) return null;
                return result.Clone();
            }
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or WebSocketException
            or OperationCanceledException or InvalidOperationException or FormatException or OverflowException or System.ComponentModel.Win32Exception) { }
        return null;
    }
    internal static bool IsOwnedEndpoint(string? value, out Uri? address) => Uri.TryCreate(value, UriKind.Absolute, out address)
        && address.Scheme == "ws" && address.Host == "127.0.0.1" && address.Port == 8080
        && address.UserInfo.Length == 0 && address.Query.Length == 0 && address.Fragment.Length == 0
        && address.AbsolutePath.StartsWith("/devtools/page/", StringComparison.Ordinal)
        && address.AbsolutePath.Length is > 15 and < 250;

    internal static bool IsSharedSteamTarget(JsonElement target)
    {
        try
        {
            return target.GetProperty("type").GetString() == "page" && target.GetProperty("title").GetString() == "SharedJSContext"
                && !string.IsNullOrWhiteSpace(target.GetProperty("id").GetString())
                && Uri.TryCreate(target.GetProperty("url").GetString(), UriKind.Absolute, out var origin)
                && origin.Scheme == "https" && origin.Host == "steamloopback.host" && origin.UserInfo.Length == 0
                && origin.IsDefaultPort;
        }
        catch (Exception error) when (error is InvalidOperationException or KeyNotFoundException) { return false; }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximum, CancellationToken cancellation)
    {
        using var output = new MemoryStream(); byte[] buffer = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(buffer, cancellation).ConfigureAwait(false)) != 0)
        { output.Write(buffer, 0, count); if (output.Length > maximum) throw new IOException("Steam target list exceeds its bound."); }
        return output.ToArray();
    }
}

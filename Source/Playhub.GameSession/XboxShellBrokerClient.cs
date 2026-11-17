using System.Reflection;
using System.Net.Http;
using System.Text.Json;

namespace Playhub.GameSession;

internal static class XboxShellBrokerClient
{
    private sealed record BootstrapOwner(uint AppId,uint WrapperPid,long WrapperBirth,string Ticket);
    private static BootstrapOwner? bootstrap;
    internal static string EnsureRoute(uint appId,uint wrapperPid,long wrapperBirth)=>
        appId>=0x80000000&&wrapperPid>0&&wrapperBirth>0
            ? $"/xbox/shell/ensure?bootstrapAppId={appId}&wrapperPid={wrapperPid}&wrapperBirth={wrapperBirth}"
            : "/xbox/shell/ensure";
    private static (uint AppId,uint Pid,long Birth) CurrentBootstrapCaller()
    {
        uint appId=ShortcutAppId(Environment.GetEnvironmentVariable("SteamGameId"));
        if(appId==0)return default;
        using var wrapper=System.Diagnostics.Process.GetCurrentProcess();
        return(appId,(uint)wrapper.Id,wrapper.StartTime.ToFileTimeUtc());
    }

    // Private build opt-in only. Unknown authorization fails this launch;
    // it must not silently restart Explorer against the configured preference.
    internal static bool PrepareDirectActivation()
    {
        bool optedIn = System.Reflection.Assembly.GetExecutingAssembly()
            .GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>()
            .Any(a => a.Key == "PlayhubExplorerFreeUwpActivation" && a.Value == "true");
        if (!optedIn) return false;
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GamingMode");
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(3) };
        return PrepareDirectActivation(directory, client, true);
    }

    internal static bool PrepareDirectActivation(string directory, HttpClient client, bool optedIn)
    {
        if (!optedIn) return false;
        try
        {
            var token = File.ReadAllText(Path.Combine(directory, "xbox-shell-token")).Trim();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{Port(directory)}/xbox/activation/readiness");
            request.Headers.Add("X-Playhub-Shell-Token", token);
            using var response = client.Send(request);
            response.EnsureSuccessStatusCode();
            using var result = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            var value = result.RootElement;
            if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != 3
                || !value.TryGetProperty("protocol", out var protocol) || !protocol.TryGetInt32(out int version) || version != 1
                || !value.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True
                || !value.TryGetProperty("route", out var route) || route.ValueKind != JsonValueKind.String)
                throw new InvalidOperationException("Xbox activation readiness was not authenticated.");
            return route.GetString() switch
            {
                "direct-aam" => true,
                "shell-required" => false,
                _ => throw new InvalidOperationException("Xbox activation readiness route is unknown.")
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Xbox activation readiness is unavailable; Explorer was not started and the game was not activated.", ex);
        }
    }


    internal static void Ensure()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GamingMode");
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(20) };
        Ensure(directory, client);
    }

    internal static void Ensure(string directory, HttpClient client)
    {
        try
        {
            var token = File.ReadAllText(Path.Combine(directory, "xbox-shell-token")).Trim();
            var caller=CurrentBootstrapCaller();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{Port(directory)}"+EnsureRoute(caller.AppId,caller.Pid,caller.Birth));
            request.Headers.Add("X-Playhub-Shell-Token", token);
            using var response = client.Send(request);
            response.EnsureSuccessStatusCode();
            using var result = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            if (!result.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
                throw new InvalidOperationException("GamingMode could not prepare the Windows desktop shell.");
            bootstrap=null;
            if(caller.AppId!=0&&result.RootElement.TryGetProperty("bootstrapProtocol",out var protocol)&&protocol.TryGetInt32(out int version)&&version==1
                &&result.RootElement.TryGetProperty("bootstrapTicket",out var ticket)&&ticket.ValueKind==JsonValueKind.String
                &&ticket.GetString() is {Length:32} value&&Guid.TryParseExact(value,"N",out _))
                bootstrap=new(caller.AppId,caller.Pid,caller.Birth,value);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("The Windows shell is unavailable. Start or update GamingMode, or return to Desktop Mode before launching this Xbox game.", ex);
        }
    }

    internal static uint ShortcutAppId(string? value) =>
        ulong.TryParse(value, out ulong gameId) && (uint)gameId == 0x02000000 && (gameId >> 32) >= 0x80000000
            ? (uint)(gameId >> 32) : 0;

    internal static async Task ArmGameReturnAsync(string directory, HttpClient client, uint appId, uint gamePid,
        CancellationToken stop)
    {
        try
        {
            var token = (await File.ReadAllTextAsync(Path.Combine(directory, "xbox-shell-token"), stop)).Trim();
            int port = Port(directory);
            long started = Environment.TickCount64;
            var caller=CurrentBootstrapCaller();
            string bootstrapTicket=bootstrap is {} held&&held.AppId==appId&&caller.AppId==appId&&held.WrapperPid==caller.Pid&&held.WrapperBirth==caller.Birth
                ? "&bootstrapTicket="+held.Ticket:"";
            // A launch curtain or a slow title may delay the first eligible
            // game window. Back off after the initial focus handoff.
            while (Environment.TickCount64 - started < 45000)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post,
                    $"http://127.0.0.1:{port}/session/steam/arm-return?appId={appId}&gamePid={gamePid}"+bootstrapTicket);
                request.Headers.Add("X-Playhub-Shell-Token", token);
                try
                {
                    using var response = await client.SendAsync(request, stop);
                    if (!response.IsSuccessStatusCode) return;
                    using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(stop));
                    if(result.RootElement.ValueKind!=JsonValueKind.Object||!result.RootElement.TryGetProperty("ok",out var ok))return;
                    if(ok.ValueKind!=JsonValueKind.False){
                        // A true return-arm result is not the shell-retirement
                        // receipt. Only the same existing bounded loop waits.
                        if(ok.ValueKind!=JsonValueKind.True||bootstrapTicket.Length==0
                            ||!result.RootElement.TryGetProperty("bootstrapPending",out var pending)||pending.ValueKind!=JsonValueKind.True)return;
                    }
                }
                catch (OperationCanceledException) when (!stop.IsCancellationRequested) { }
                catch (HttpRequestException) when (!stop.IsCancellationRequested) { }
                await Task.Delay(Environment.TickCount64 - started < 3000 ? 250 : 750, stop);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or HttpRequestException
            or OperationCanceledException or JsonException or ArgumentException) { }
    }

    private static int Port(string directory)
    {
        var path = Path.Combine(directory, "config.json");
        if (!File.Exists(path)) return 47991;
        using var config = JsonDocument.Parse(File.ReadAllText(path));
        return config.RootElement.TryGetProperty("safety", out var safety) && safety.TryGetProperty("apiPort", out var value) &&
            value.TryGetInt32(out var port) && port is > 0 and < 65536 ? port : 47991;
    }
}

using System.Reflection;
using System.Net.Http;
using System.Text.Json;

namespace Playhub.GameSession;

internal static class XboxShellBrokerClient
{
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
            using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{Port(directory)}/xbox/shell/ensure");
            request.Headers.Add("X-Playhub-Shell-Token", token);
            using var response = client.Send(request);
            response.EnsureSuccessStatusCode();
            using var result = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            if (!result.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
                throw new InvalidOperationException("GamingMode could not prepare the Windows desktop shell.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("The Windows shell is unavailable. Start or update GamingMode, or return to Desktop Mode before launching this Xbox game.", ex);
        }
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

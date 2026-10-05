using System.Net.Http;
using System.Text.Json;

namespace Playhub.GameSession;

internal static class XboxShellBrokerClient
{
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

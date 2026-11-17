using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Playhub.Shared;

namespace Playhub.GameSession;

internal static class EpicProtocolBrokerClient
{
    internal static void Dispatch(EpicInstalledLaunch plan)
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GamingMode");
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(15) };
        Dispatch(directory, client, plan);
    }

    internal static void Dispatch(string directory, HttpClient client, EpicInstalledLaunch plan)
    {
        try
        {
            string token = File.ReadAllText(Path.Combine(directory, "xbox-shell-token")).Trim();
            if (token.Length != 64 || !token.All(Uri.IsHexDigit)) throw new InvalidDataException("Agent session is unavailable.");
            int port = 47991;
            string config = Path.Combine(directory, "config.json");
            if (File.Exists(config))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(config));
                if (document.RootElement.TryGetProperty("safety", out var safety) && safety.TryGetProperty("apiPort", out var value))
                {
                    if (!value.TryGetInt32(out port) || port is <= 0 or > 65535) throw new InvalidDataException("Agent port is invalid.");
                }
            }
            using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/session/epic/launch");
            request.Headers.Add("X-Playhub-Shell-Token", token);
            request.Content = new StringContent(JsonSerializer.Serialize(EpicProtocolLaunchRequest.Create(plan),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json");
            // Exactly one attempt. An ambiguous HTTP reply never retries the
            // launch and never falls back to Explorer or the game executable.
            using var response = client.Send(request);
            response.EnsureSuccessStatusCode();
            var result = response.Content.ReadFromJsonAsync<EpicProtocolLaunchResult>().GetAwaiter().GetResult();
            if (result?.Ok != true) throw new InvalidOperationException("Epic launch request was not accepted (" + (result?.Code ?? "unknown") + ").");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or HttpRequestException
            or JsonException or InvalidOperationException or OperationCanceledException or ArgumentException)
        {
            throw new InvalidOperationException("Playhub could not send the Epic launch request. Start or update Playhub and try again.", error);
        }
    }
}

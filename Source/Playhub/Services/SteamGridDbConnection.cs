using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Playhub.Services;

internal enum SteamGridDbConnectionStatus { Connected, InvalidKey, Unavailable }

internal sealed class SteamGridDbConnection : IDisposable
{
    private readonly HttpClient _http;
    internal SteamGridDbConnection(HttpMessageHandler? handler = null)
    {
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        _http.Timeout = TimeSpan.FromSeconds(12);
    }
    internal async Task<SteamGridDbConnectionStatus> CheckAsync(string key, CancellationToken token = default)
    {
        key = key.Trim();
        if (key.Length is 0 or > 256 || key.Any(char.IsWhiteSpace) || key.Any(char.IsControl))
            return SteamGridDbConnectionStatus.InvalidKey;
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.steamgriddb.com/api/v2/search/autocomplete/Portal");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return SteamGridDbConnectionStatus.InvalidKey;
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 1024 * 1024)
                return SteamGridDbConnectionStatus.Unavailable;
            using var source = await response.Content.ReadAsStreamAsync(token);
            using var buffer = new MemoryStream();
            var bytes = new byte[8192];
            int count;
            while ((count = await source.ReadAsync(bytes, token)) > 0)
            {
                if (buffer.Length + count > 1024 * 1024) return SteamGridDbConnectionStatus.Unavailable;
                await buffer.WriteAsync(bytes.AsMemory(0, count), token);
            }
            using var json = JsonDocument.Parse(buffer.ToArray());
            if (json.RootElement.ValueKind != JsonValueKind.Object) return SteamGridDbConnectionStatus.Unavailable;
            return json.RootElement.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True &&
                json.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
                ? SteamGridDbConnectionStatus.Connected : SteamGridDbConnectionStatus.Unavailable;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return SteamGridDbConnectionStatus.Unavailable; }
        catch (Exception error) when (error is HttpRequestException or JsonException or IOException)
        { return SteamGridDbConnectionStatus.Unavailable; }
    }
    public void Dispose() => _http.Dispose();
}

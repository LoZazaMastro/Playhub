using System.Net;
using Playhub.Services;

int checks = 0;
void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
using (var handler = new Fixture(HttpStatusCode.OK, "{\"success\":true,\"data\":[]}"))
using (var service = new SteamGridDbConnection(handler))
{
    Check(await service.CheckAsync(" fixture-key ") == SteamGridDbConnectionStatus.Connected, "Authenticated API acknowledged");
    Check(handler.Uri == "https://www.steamgriddb.com/api/v2/search/autocomplete/Portal", "Only fixed TLS endpoint");
    Check(handler.Authorization == "Bearer fixture-key", "Bearer header, trimmed key");
    Check(!handler.Uri.Contains("fixture-key"), "No credential URL leak");
    foreach (var invalid in new[] { "", "a b", "a\r\nb", new string('a', 257) })
        Check(await service.CheckAsync(invalid) == SteamGridDbConnectionStatus.InvalidKey, "Invalid credential rejected before transport");
    Check(handler.Calls == 1, "No request for malformed keys");
}
foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests, HttpStatusCode.Found, HttpStatusCode.ServiceUnavailable })
{
    using var handler = new Fixture(status, "{}") ; using var service = new SteamGridDbConnection(handler);
    Check(await service.CheckAsync("key") == (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ? SteamGridDbConnectionStatus.InvalidKey : SteamGridDbConnectionStatus.Unavailable), "Auth error distinct from rate/redirect/outage");
}
foreach (var malformed in new[] { "[]", "null", "1", "<html>error</html>", "{\"success\":false,\"data\":[]}", "{\"success\":true,\"data\":{}}", "{}", new string('a', 1024*1024+1) })
{
    using var handler = new Fixture(HttpStatusCode.OK, malformed); using var service = new SteamGridDbConnection(handler);
    Check(await service.CheckAsync("key") == SteamGridDbConnectionStatus.Unavailable, "Invalid/unbounded response never validates connection");
}
using (var handler = new Fixture(HttpStatusCode.OK, "{}", error: true))
using (var service = new SteamGridDbConnection(handler))
    Check(await service.CheckAsync("key") == SteamGridDbConnectionStatus.Unavailable, "Network failure not invalid-key");
using (var handler = new Fixture(HttpStatusCode.OK, "{}"))
using (var service = new SteamGridDbConnection(handler))
{
    using var cancel = new CancellationTokenSource(); cancel.Cancel();
    try { await service.CheckAsync("key", cancel.Token); throw new Exception("Cancelled operation completed"); }
    catch (OperationCanceledException) { Check(true, "User cancellation preserved"); }
}
Console.WriteLine($"PASS {checks} connection checks");

sealed class Fixture(HttpStatusCode status, string body, bool error = false) : HttpMessageHandler
{
    internal string Uri = "", Authorization = ""; internal int Calls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Calls++; Uri = request.RequestUri!.AbsoluteUri; Authorization = request.Headers.Authorization?.ToString() ?? "";
        if (error) throw new HttpRequestException("Offline");
        return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}

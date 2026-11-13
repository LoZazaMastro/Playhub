using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace GamingMode.Services;

internal static class OwnedWin32RoutePolicy
{
    // Client data only narrows a NEW native proof. It never grants ownership.
    internal static bool Same(OwnedWin32Route expected, OwnedWin32Route actual) => expected.RoutingOnly && actual.RoutingOnly
        && expected.CanonicalShortcutAppId == actual.CanonicalShortcutAppId && expected.OriginalGameId == actual.OriginalGameId
        && expected.ObservedSteamGameId == actual.ObservedSteamGameId && expected.LaunchProvenance == actual.LaunchProvenance
        && expected.GamePid == actual.GamePid && expected.GameBirth == actual.GameBirth
        && expected.WrapperPid == actual.WrapperPid && expected.WrapperBirth == actual.WrapperBirth
        && expected.SteamPid == actual.SteamPid && expected.SteamBirth == actual.SteamBirth;
}
internal static class OwnedWin32RouteEndpoints
{
    private sealed record Prepare(uint AppId, OwnedWin32Route? ExpectedRoute);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static void Map(WebApplication app, XboxShellBroker shell, Action<string> diagnostic)
    {
        bool Authorized(HttpContext c) => shell.IsAuthorized(c.Connection.RemoteIpAddress, c.Request.Headers["X-Playhub-Shell-Token"].ToString());
        app.MapPost("/session/steam/owned-launch", async (HttpContext c) =>
        {
            if (!Authorized(c)) return Results.StatusCode(401);
            string uiText = c.Request.Query.ContainsKey("uiObservedApp") ? c.Request.Query["uiObservedApp"].ToString() : c.Request.Query["observedAppId"].ToString();
            uint? observed = null;
            if (uiText != "null") { if (!uint.TryParse(uiText, NumberStyles.None, CultureInfo.InvariantCulture, out uint value)) return Results.StatusCode(400); observed = value; }
            uint? candidate = null;
            if (c.Request.Query.ContainsKey("candidateAppId")) { if (!uint.TryParse(c.Request.Query["candidateAppId"], NumberStyles.None, CultureInfo.InvariantCulture, out uint value) || value < 0x80000000) return Results.StatusCode(400); candidate = value; }
            if ((observed is null or 0) && candidate is null) return Results.StatusCode(400);
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(c.RequestAborted, app.Lifetime.ApplicationStopping);
            try
            {
                if ((observed is null or 0) && await Win32SteamSessionAssociation.ProveSteamUiForegroundAsync(stop.Token).ConfigureAwait(false))
                    return Results.Json(new { ok = true, kind = "absent", receiver = "steam-ui" });
                var route = await Win32SteamSessionAssociation.DiscoverUiRouteAsync(observed, candidate, stop.Token).ConfigureAwait(false);
                if (route is not null) return Results.Json(new { ok = true, route });
                // Only positive retained foreground + native-at-birth log proof
                // authorizes ordinary Home. Missing ownership is still UNKNOWN.
                return await Win32SteamSessionAssociation.ProveOrdinaryForegroundAsync(observed, stop.Token).ConfigureAwait(false)
                    ? Results.Json(new { ok = true, kind = "absent", receiver = "ordinary-game" })
                    : Results.Json(new { ok = false, error = "owned-route-unavailable" });
            }
            catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException
                or OperationCanceledException or JsonException or OverflowException or System.Security.SecurityException or System.ComponentModel.Win32Exception)
            { return Results.Json(new { ok = false, error = "owned-route-unavailable" }); }
        });
        app.MapPost("/session/steam/owned-launch/prepare", async (HttpContext c) =>
        {
            if (!Authorized(c)) return Results.StatusCode(401);
            try
            {
                byte[] bytes = new byte[4097]; int used = 0;
                while (used < bytes.Length)
                {
                    int n = await c.Request.Body.ReadAsync(bytes.AsMemory(used), c.RequestAborted).ConfigureAwait(false);
                    if (n == 0) break; used += n;
                }
                if (used is 0 or > 4096) return Results.StatusCode(400);
                var request = JsonSerializer.Deserialize<Prepare>(bytes.AsSpan(0, used), Json);
                if (request is not { AppId: >= 0x80000000, ExpectedRoute: { } expected }
                    || expected.CanonicalShortcutAppId != request.AppId) return Results.StatusCode(400);
                using var stop = CancellationTokenSource.CreateLinkedTokenSource(c.RequestAborted, app.Lifetime.ApplicationStopping);
                // Same ordinary native protocol, with one additional expected
                // tuple veto BEFORE its existing one-shot pipe delivery.
                var result = await XboxCompanionOverlaySession.TryRequestMirrorPreparationAsync(request.AppId, stop.Token, diagnostic, expected).ConfigureAwait(false);
                return Results.Json(result.Result);
            }
            catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException
                or OperationCanceledException or JsonException or OverflowException or System.Security.SecurityException or System.ComponentModel.Win32Exception)
            { return Results.Json(new { ok = false, error = "owned-route-prepare-unavailable" }); }
        });
    }
}

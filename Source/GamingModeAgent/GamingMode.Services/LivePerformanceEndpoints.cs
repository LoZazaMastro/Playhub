using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Win32;

namespace GamingMode.Services;

internal static class LivePerformanceEndpoints
{
    private static readonly HashSet<string> Fields = new(StringComparer.Ordinal) {
        "action", "lease", "appId", "enabled", "detail", "corner", "transparent", "refreshMs", "suspended", "textColor"
    };
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    internal static void Map(WebApplication app, XboxShellBroker shell, LivePerformanceController controller)
    {
        bool Authorized(HttpContext context) => shell.IsAuthorized(context.Connection.RemoteIpAddress,
            context.Request.Headers["X-Playhub-Shell-Token"].ToString());
        app.MapGet("/session/live-performance", (HttpContext context) =>
            Authorized(context) ? Results.Json(controller.Read()) : Results.StatusCode(401));
        app.MapGet("/session/live-performance/hud-diagnostics", (HttpContext context) =>
            Authorized(context) ? Results.Json(controller.ReadHudDiagnostics()) : Results.StatusCode(401));
        app.MapPost("/session/live-performance", async (HttpContext context) => {
            if (!Authorized(context)) return Results.StatusCode(401);
            try {
                byte[] body = new byte[4097];
                int count = 0;
                while (count < body.Length) {
                    int read = await context.Request.Body.ReadAsync(body.AsMemory(count), context.RequestAborted);
                    if (read == 0) break;
                    count += read;
                }
                if (count is 0 or > 4096) return Results.StatusCode(400);
                using var document = JsonDocument.Parse(body.AsMemory(0, count), new() { MaxDepth = 4 });
                if (document.RootElement.ValueKind != JsonValueKind.Object) return Results.StatusCode(400);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in document.RootElement.EnumerateObject())
                    if (!Fields.Contains(property.Name) || !seen.Add(property.Name)) return Results.StatusCode(400);
                var request = document.RootElement.Deserialize<LivePerformanceRequest>(Json);
                if (request is null || request.Action is not ("configure" or "renew" or "stop")) return Results.StatusCode(400);
                if (request.Detail is < 1 or > 4 || request.Corner is < 1 or > 6 || request.RefreshMs is not (null or 20 or 50 or 100)
                    || request.TextColor is not null && !LivePerformanceHudPalette.Valid(request.TextColor))
                    return Results.StatusCode(400);
                if (request.Action == "renew" && seen.Any(field => field is not ("action" or "lease" or "suspended"))) return Results.StatusCode(400);
                if (request.Action == "stop" && seen.Any(field => field is not ("action" or "lease"))) return Results.StatusCode(400);
                return Results.Json(await controller.ApplyAsync(request, context.RequestAborted));
            } catch (JsonException) { return Results.StatusCode(400); }
            catch (OperationCanceledException) { return Results.StatusCode(408); }
        });
    }
}

internal sealed class LivePerformanceLifetime : IDisposable
{
    private readonly LivePerformanceController _controller;
    private readonly CancellationTokenRegistration _stopping;
    internal LivePerformanceLifetime(LivePerformanceController controller, CancellationToken stopping)
    {
        _controller = controller;
        SystemEvents.PowerModeChanged += PowerChanged;
        _stopping = stopping.Register(controller.Invalidate);
    }
    private void PowerChanged(object? sender, PowerModeChangedEventArgs args)
    {
        if (args.Mode == PowerModes.Suspend) _controller.Invalidate();
    }
    public void Dispose() { SystemEvents.PowerModeChanged -= PowerChanged; _stopping.Dispose(); }
}

using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Playhub.Services;

namespace GamingMode.Services;

// One shared, bounded check serves every Big Picture window and entry.
internal sealed class PlayhubUpdateNotice
{
    readonly object gate = new();
    readonly Func<Task<PlayhubUpdateService.UpdateInfo?>> check;
    readonly Func<long> now;
    Task<PlayhubUpdateService.UpdateInfo?>? pending;
    long checkedAt;
    internal PlayhubUpdateNotice(Func<Task<PlayhubUpdateService.UpdateInfo?>> check, Func<long> now)
    { this.check = check; this.now = now; }
    internal Task<PlayhubUpdateService.UpdateInfo?> Read()
    {
        lock (gate)
        {
            long tick = now();
            if (pending is null || (pending.IsCompleted && tick >= checkedAt && tick - checkedAt >= 30 * 60 * 1000))
            { checkedAt = tick; pending = ReadSafe(); }
            return pending;
        }
    }
    async Task<PlayhubUpdateService.UpdateInfo?> ReadSafe()
    { try { return await check().ConfigureAwait(false); } catch { return null; } }
    internal static void Map(WebApplication app, XboxShellBroker auth)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        string current = version is null ? "" : $"{version.Major}.{version.Minor}.{version.Build}";
        var service = new PlayhubUpdateService();
        var notice = new PlayhubUpdateNotice(() => service.CheckAsync("LoZazaMastro/Playhub", current), () => Environment.TickCount64);
        app.MapPost("/playhub/update", (Func<HttpContext, Task<IResult>>)(async context =>
        {
            if (!auth.IsAuthorized(context.Connection.RemoteIpAddress, context.Request.Headers["X-Playhub-Shell-Token"].ToString()))
                return Results.StatusCode(401);
            var info = await notice.Read().ConfigureAwait(false);
            return Results.Json(new { available = info?.IsNewer == true && !string.IsNullOrWhiteSpace(info.DownloadUrl),
                version = info?.LatestVersion, currentVersion = current });
        }));
    }
}

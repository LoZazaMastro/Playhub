using System.Text.Json;
using Playhub.StoreLibrary;

internal static class MigrationChecks
{
    internal static async Task<int> Run(string root)
    {
        int count = 0; string? account = null;
        var snapshot = new StoreLibrarySnapshot(0, DateTimeOffset.UtcNow, [], [], []);
        string file = Path.Combine(root, "migration-registry.json");
        StoreSteamShortcutRegistry Create() => new(_ => Task.FromResult(snapshot), Path.Combine(root, "Playhub.GameSession.exe"),
            Path.Combine(root, "Playhub.exe"), file, activeSteamAccount: () => account);
        var registry = Create();
        void Assert(bool ok, string why) { if (!ok) throw new Exception(why); count++; }
        async Task<T> Call<T>(string method, object args)
        {
            var r = await registry.TryHandleAsync(method, JsonSerializer.SerializeToElement(args, StoreLibraryService.JsonOptions), default);
            Assert(r.Handled && r.Data is T, "Migration typed " + method); return (T)r.Data!;
        }
        async Task Reject(object input)
        {
            try { await registry.TryHandleAsync("steam-migration-report", JsonSerializer.SerializeToElement(input, StoreLibraryService.JsonOptions), default); throw new Exception("False migration success accepted"); }
            catch (InvalidOperationException e) when (e.Message == "steam_migration_unverified") { count++; }
        }
        var waiting = await Call<StoreSteamCollectionMigration>("steam-migration-request", new { });
        Assert(waiting.State == "waitingSteam" && waiting.SteamAccountId is null, "Steam absent is pending, never successful");
        Assert((await Call<StoreSteamCollectionMigration>("steam-migration-request", new { })).RequestId == waiting.RequestId, "Repeated app activation shares one waiting receipt");
        account = "76561198012345678";
        var bound = await Call<StoreSteamCollectionMigration>("steam-migration-status", new { requestId = waiting.RequestId });
        Assert(bound.State == "pending" && bound.SteamAccountId == account, "Steam start binds the active native account");
        var work = (await Call<IReadOnlyList<StoreSteamCollectionMigration>>("steam-migration-pending", new { })).Single();
        await Reject(new { requestId = work.RequestId, steamAccountId = "76561198012345679", confirmed = 10, total = 10, complete = true });
        await Reject(new { requestId = work.RequestId, steamAccountId = account, confirmed = 9, total = 10, complete = true });
        var progress = await Call<StoreSteamCollectionMigration>("steam-migration-report", new { requestId = work.RequestId, steamAccountId = account, confirmed = 9, total = 10, complete = false });
        Assert(progress.State == "pending" && progress.Confirmed == 9, "Partial native completion remains in progress");
        registry = Create();
        Assert((await Call<StoreSteamCollectionMigration>("steam-migration-status", new { requestId = work.RequestId })).Confirmed == 9, "Progress survives app and host restart");
        var done = await Call<StoreSteamCollectionMigration>("steam-migration-report", new { requestId = work.RequestId, steamAccountId = account, confirmed = 10, total = 10, complete = true });
        Assert(done.State == "completed" && done.Confirmed == done.Total, "Native readback completion becomes durable success");
        Assert((await Call<StoreSteamCollectionMigration>("steam-migration-report", new { requestId = work.RequestId, steamAccountId = account, confirmed = 10, total = 10, complete = true })).State == "completed", "Lost final reply can be retried without a second migration");
        registry = Create();
        Assert((await Call<StoreSteamCollectionMigration>("steam-migration-request", new { })).RequestId == done.RequestId, "Same version and Steam account do not run migration twice");
        account = "76561198012345679";
        var other = await Call<StoreSteamCollectionMigration>("steam-migration-request", new { });
        Assert(other.RequestId != done.RequestId && other.State == "pending", "Another Steam account receives its own migration");
        account = null;
        await Reject(new { requestId = other.RequestId, steamAccountId = other.SteamAccountId, confirmed = 0, total = 0, complete = true });
        return count;
    }
}

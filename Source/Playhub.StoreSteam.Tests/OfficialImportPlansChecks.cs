using System.Text.Json;
using Playhub.StoreLibrary;

internal static class OfficialImportPlansChecks
{
    internal static async Task<int> Run(string temporary)
    {
        int count = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); count++; }
        foreach (var store in new[] { StoreKind.Epic, StoreKind.Gog })
        {
            string root = Path.Combine(temporary, "official-" + store), gameRoot = Path.Combine(root, "installed"), working = Path.Combine(gameRoot, "bin");
            Directory.CreateDirectory(working);
            string wrapper = Path.Combine(root, "Playhub.GameSession.exe"), image = Path.Combine(working, "game.exe");
            var key = new StoreGameKey(store, store == StoreKind.Gog ? "1744110647" : "OfficialApp");
            var cap = new StoreCapabilities(StoreInstallKind.Direct, true, true, true, true, true);
            var game = new StoreLibraryGame(key, "Official fixture", 4, cap, true, true, gameRoot);
            var snapshot = new StoreLibrarySnapshot(1, DateTimeOffset.UtcNow,
                [new(store, StoreConnectionState.Connected, 4, "Fixture", cap)], [game], []);
            var native = new StoreSteamNativeIdentity(key, 4, null, null, gameRoot, wrapper, working, image,
                "ExactNamespace", "ExactCatalog", GameArguments: "--language \"Japanese\" --safe-mode", NativeLaunchKind: "official");
            string path = Path.Combine(root, "registry.json");
            StoreSteamShortcutRegistry Create() => new(_ => Task.FromResult(snapshot), wrapper, Path.Combine(root, "Playhub.exe"), path,
                (_, _) => Task.FromResult<IReadOnlyDictionary<StoreGameKey, StoreSteamNativeIdentity>>(new Dictionary<StoreGameKey, StoreSteamNativeIdentity> { [key] = native }));
            var registry = Create();
            async Task<T> Call<T>(string method, object args)
            {
                var r = await registry.TryHandleAsync(method, JsonSerializer.SerializeToElement(args, StoreLibraryService.JsonOptions), default);
                Check(r.Handled && r.Data is T, "Official typed " + method); return (T)r.Data!;
            }
            async Task RefuseNative(StoreSteamNativeIdentity bad)
            {
                native = bad;
                try { await Call<object>("steam-sync-pending", new { }); throw new Exception("Invalid official identity accepted"); }
                catch (InvalidOperationException error) when (error.Message == "steam_native_identity_invalid") { count++; }
            }
            var plan = (await Call<StoreSteamSyncPending>("steam-sync-pending", new { })).Plans.Single();
            string wanted = store == StoreKind.Epic
                ? "--epic \"ExactNamespace\" \"ExactCatalog\" \"OfficialApp\" " + StoreSteamShortcutIdentity.Quote(image)
                : "--game " + StoreSteamShortcutIdentity.Quote(image) + " " + native.GameArguments;
            Check(plan.Arguments == wanted && plan.StartDirectory == working, "Official repair preserves registered launch tuple instead of requiring a private CLI receipt");
            Check(plan.AppId is null && plan.CreatedAppId is null, "Official plan invents no native ID");
            var ack = new StoreSteamShortcutAck(plan.RequestId, plan.ClaimId, store == StoreKind.Epic ? 3300000001u : 3300000002u, wrapper, plan.Arguments, working);
            await Call<StoreSteamSyncRequest>("steam-sync-created", ack with { Arguments = "" });
            registry = Create();
            Check((await Call<StoreSteamSyncPending>("steam-sync-pending", new { })).Plans.Single().CreatedAppId == ack.AppId,
                "Official creation draft survives restart without another shortcut");
            await Call<StoreSteamSyncRequest>("steam-sync-ack", ack);
            registry = Create();
            Check((await Call<IReadOnlyList<StoreSteamOwnedGame>>("steam-owned", new { })).Single().Arguments == wanted,
                "Official tuple remains valid when durable ownership is reopened");
            var good = native;
            await RefuseNative(good with { NativeLaunchKind = "client-guessed" });
            await RefuseNative(good with { GameExecutablePath = Path.Combine(root, "foreign.exe") });
            await RefuseNative(good with { StartDirectory = root });
            if (store == StoreKind.Gog) await RefuseNative(good with { GameArguments = "\"unterminated" });
            else await RefuseNative(good with { EpicNamespace = "namespace with spaces" });
            native = good;
            Check((await Call<IReadOnlyList<StoreSteamOwnedGame>>("steam-owned", new { })).Single().AppId == ack.AppId,
                "Rejected official proofs preserve the existing native identity");
        }
        return count;
    }
}

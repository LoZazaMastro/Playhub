using System.Text.Json;
using Playhub.StoreLibrary;

internal static class ImportAdoptionChecks
{
    internal static async Task<int> Run(string root)
    {
        int count = 0;
        void Assert(bool ok, string why) { if (!ok) throw new Exception(why); count++; }
        var cap = new StoreCapabilities(StoreInstallKind.ExternalHandoff, true, true, false, false, false);
        var key = new StoreGameKey(StoreKind.Xbox, "9NBLGGH1Z149");
        var account = new StoreAccount(StoreKind.Xbox, StoreConnectionState.Connected, 4, "Fixture", cap);
        var game = new StoreLibraryGame(key, "Killer Instinct", 4, cap, true, true, Path.Combine(root, "game"));
        var snapshot = new StoreLibrarySnapshot(1, DateTimeOffset.UtcNow, [account], [game], []);
        string wrapper = Path.Combine(root, "Playhub.GameSession.exe"), app = Path.Combine(root, "Playhub.exe");
        var native = new StoreSteamNativeIdentity(key, 4, "Fixture_abcdefghijklm!Game", "Retail\\Game.exe", game.InstallPath!, wrapper, root);
        bool changeAccountDuringProof = false;
        Task<IReadOnlyDictionary<StoreGameKey, StoreSteamNativeIdentity>> Resolve(IReadOnlyList<StoreLibraryGame> _, CancellationToken token)
        {
            if (changeAccountDuringProof) snapshot = snapshot with { Accounts = [account with { Revision = 5 }] };
            return Task.FromResult<IReadOnlyDictionary<StoreGameKey, StoreSteamNativeIdentity>>(_.Any(g => g.Key == key)
                ? new Dictionary<StoreGameKey, StoreSteamNativeIdentity> { [key] = native } : new Dictionary<StoreGameKey, StoreSteamNativeIdentity>());
        }
        string file = Path.Combine(root, "native-import-registry.json");
        StoreSteamShortcutRegistry Create() => new(_ => Task.FromResult(snapshot), wrapper, app, file, Resolve);
        var registry = Create();
        async Task<T> Call<T>(string method, object args)
        {
            var reply = await registry.TryHandleAsync(method, JsonSerializer.SerializeToElement(args, StoreLibraryService.JsonOptions), default);
            Assert(reply.Handled && reply.Data is T, "Native import typed " + method); return (T)reply.Data!;
        }
        async Task Reject(string method, object input, string reason)
        {
            try { await registry.TryHandleAsync(method, JsonSerializer.SerializeToElement(input, StoreLibraryService.JsonOptions), default); throw new Exception("Accepted invalid " + reason); }
            catch (InvalidOperationException e) when (e.Message == reason) { count++; }
        }
        var plan = (await Call<StoreSteamSyncPending>("steam-sync-pending", new { })).Plans.Single();
        Assert(plan.Arguments == StoreSteamShortcutIdentity.UwpArguments(native.NativeApplicationId!, native.ManifestExecutable!) && plan.NativeApplicationId == native.NativeApplicationId, "New Xbox links use the same --uwp route as Import Games");
        var ack = new StoreSteamShortcutAck(plan.RequestId, plan.ClaimId, 3560692922, wrapper, plan.Arguments, root);
        await Reject("steam-sync-ack", ack with { Arguments = "--store-library xbox " + key.Id }, "steam_ack_invalid");
        await Call<StoreSteamSyncRequest>("steam-sync-created", ack with { Arguments = "" });
        await Call<StoreSteamSyncRequest>("steam-sync-ack", ack);
        const uint originalId = 3134433872;
        string custom = StoreSteamShortcutIdentity.UwpArguments(native.NativeApplicationId!, native.ManifestExecutable!)[..^2] + StoreSteamShortcutIdentity.Quote("-custom \\\"value\\\"");
        // Keep the user's original arguments, title, ID and directory spelling.
        var adoption = new StoreSteamImportAdoption(key, 4, originalId, "My original name", "\"" + wrapper + "\"", custom, "\"" + root + "\\\"", ack.AppId);
        await Reject("steam-adopt-import", adoption with { ExpectedOwnedAppId = null }, "steam_identity_conflict");
        await Reject("steam-adopt-import", adoption with { Arguments = "--uwp \"Foreign_abcdefghijklm!Game\" \"Retail\\Game.exe\" \"\"" }, "steam_import_unverified");
        await Reject("steam-adopt-import", adoption with { Arguments = "--uwp \"" + native.NativeApplicationId + "\" \"wrong.exe\" \"\"" }, "steam_import_unverified");
        await Reject("steam-adopt-import", adoption with { Executable = Path.Combine(root, "other", "Playhub.GameSession.exe") }, "steam_import_unverified");
        await Reject("steam-adopt-import", adoption with { StartDirectory = root + "other" }, "steam_import_unverified");
        await Reject("steam-adopt-import", adoption with { Arguments = plan.Arguments + " extra" }, "steam_import_unverified");
        var adopted = (await Call<StoreSteamImportAdoptionResult>("steam-adopt-import", adoption)).Owned;
        Assert(adopted.AppId == originalId && adopted.Title == adoption.Title && adopted.Arguments == custom && adopted.Executable == adoption.Executable && adopted.StartDirectory == adoption.StartDirectory, "Adoption preserves every original shortcut field");
        Assert((await Call<StoreSteamSyncPending>("steam-sync-pending", new { })).Plans.Count == 0, "Adoption retires old duplicate claims");
        registry = Create();
        Assert((await Call<IReadOnlyList<StoreSteamOwnedGame>>("steam-owned", new { })).Single().Arguments == custom, "Imported identity survives host restart");
        await Call<StoreSteamImportAdoptionResult>("steam-adopt-import", adoption);
        await Reject("steam-forget-uninstalled", new { key, appId = originalId }, "steam_uninstall_unverified");
        await Reject("steam-repair-missing", new { key, expectedAccountRevision = 4, expectedOwnedAppId = ack.AppId }, "steam_identity_conflict");
        var repair = await Call<StoreSteamSyncRequest>("steam-repair-missing", new { key, expectedAccountRevision = 4, expectedOwnedAppId = originalId });
        Assert(repair.Total == 1 && repair.State == "pending", "Missing Steam link gets one bounded repair claim");
        var repairedPlan = (await Call<StoreSteamSyncPending>("steam-sync-pending", new { })).Plans.Single();
        Assert(repairedPlan.AppId is null && repairedPlan.CreatedAppId is null, "Missing link never reuses stale native ID");
        var repairedAck = new StoreSteamShortcutAck(repairedPlan.RequestId, repairedPlan.ClaimId, 3456789012, wrapper, repairedPlan.Arguments, root);
        await Call<StoreSteamSyncRequest>("steam-sync-ack", repairedAck);
        snapshot = snapshot with { Games = [game with { IsInstalled = false }] };
        await Call<object>("steam-forget-uninstalled", new { key, appId = repairedAck.AppId });
        Assert(!(await Call<IReadOnlyList<StoreSteamOwnedGame>>("steam-owned", new { })).Any(), "Confirmed native removal retires ownership for a later reinstall");
        Assert(!(await Call<StoreSteamSyncPending>("steam-sync-pending", new { })).Plans.Any(), "An uninstalled game cannot be repaired or imported");
        snapshot = snapshot with { Games = [game] }; changeAccountDuringProof = true;
        await Reject("steam-adopt-import", adoption with { ExpectedOwnedAppId = null }, "account_changed");
        Assert(StoreSteamShortcutIdentity.Tokens("--uwp \"unterminated") is null, "Malformed quoting refused");
        Assert(StoreSteamShortcutIdentity.Tokens("--game good.exe\0 foreign.exe") is null, "Control-byte identity refused");
        return count;
    }
}

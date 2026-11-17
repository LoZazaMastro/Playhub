// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System.Text.Json;

namespace Playhub.StoreLibrary;

public sealed partial class StoreQueueEngine : IStoreInstalledGameService
{
    private static Task<bool> RegisteredInstallExists(IStoreProvider provider,StoreLibraryGame game,ProviderLaunch? launch,CancellationToken token)=>
        provider is IStoreRegisteredInstallation registered?registered.HasRegisteredInstallationAsync(game.Key.Id,game.InstallPath!,token):
        provider is IStoreInstallVerifier verifier?verifier.IsInstalledAsync(game.Key.Id,game.InstallPath!,token):Task.FromResult(launch is not null);
    private readonly HashSet<StoreKind> installedOperationStores = [];
    private readonly Dictionary<StoreGameKey, DateTimeOffset> installedLaunchReservations = [];
    private readonly Dictionary<Guid, (StoreUninstallPreparation Receipt, StoreOwnedUninstallPlan Plan,long AccountContextRevision)> uninstallPreparations = [];
    private bool InstalledOperationBusy(StoreKind store) => installedOperationStores.Contains(store);
    private bool RemovalTransferBusy(StoreKind store)=>drainUnconfirmed || active is {} id && (jobs.FirstOrDefault(j=>j.JobId==id) is not {} transfer || transfer.Key.Store==store);
    private StoreDownloadJob? OwnedCompletedInstall(StoreLibraryGame game)
    {
        var candidates = jobs.Where(j => j.Key == game.Key && Terminal(j.State) &&
            StringComparer.OrdinalIgnoreCase.Equals(j.InstallPath, game.InstallPath)).Reverse();
        foreach (var job in candidates)
        {
            if (job.State == StoreJobState.Completed) return job;
            // A canceled/interrupted history row can outlive a successfully
            // registered install. Its retained directory claim, not the queue
            // label, establishes which local installation may be removed.
            try
            {
                using var marker = StoreOwnedInstallFiles.OpenReadFile(Path.Combine(job.InstallPath, ".playhub-install-owner"));
                if (marker.Length != 36) continue;
                using var reader = new StreamReader(marker);
                if (reader.ReadToEnd() == job.JobId.ToString("D")) return job;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or StoreOperationException) { }
        }
        return null;
    }
    private string? UninstallContextError(StoreGameKey key, out StoreLibraryGame? game, out StoreDownloadJob? job)
    {
        game = null; job = null;
        if (disposed || quiescing) return "maintenance_in_progress";
        if (key.Store is not (StoreKind.Epic or StoreKind.Gog or StoreKind.Amazon) || Provider(key.Store) is not IStoreInstalledGameProvider) return "uninstall_unavailable";
        if (InstalledOperationBusy(key.Store) || RemovalTransferBusy(key.Store)) return "provider_busy";
        if (installedLaunchReservations.TryGetValue(key, out var launchExpiry) && now() < launchExpiry) return "launch_pending";
        if (!games.TryGetValue(key, out game) || !game.IsInstalled || game.InstallPath == null) return "game_not_installed";
        if (accounts[key.Store].State == StoreConnectionState.Connecting) return "account_changed";
        if (jobs.Any(j => j.Key == key && !Terminal(j.State))) return "download_active";
        job = OwnedCompletedInstall(game);
        if (job == null) return LegacyInstallFor(game) is null ? "installation_ownership_lost" : null;
        if (!jobAccountIdentities.TryGetValue(job.JobId, out var owner) || string.IsNullOrEmpty(owner)) return "installation_ownership_lost";
        return null;
    }
    public async Task<StoreInstalledGameStatus> GetInstalledGameAsync(StoreGameKey key, CancellationToken cancellationToken = default)
    {
        if (key.Store == StoreKind.Xbox) return await GetXboxInstalledGameAsync(key, cancellationToken);
        // Status polling is read-only and cheap. Recovery/manifest enumeration
        // belongs to the user's explicit removal action, never every 4 seconds.
        await gate.WaitAsync(cancellationToken);
        try
        {
            var error = UninstallContextError(key, out var game, out var job);
            game ??= games.GetValueOrDefault(key);
            var installed = game?.IsInstalled == true;
            var canLaunch = installed && game!.IsLaunchable && !InstalledOperationBusy(key.Store) && !disposed && !quiescing &&
                await Provider(key.Store).ResolveLaunchAsync(key.Id, game.InstallPath!, cancellationToken) is { } launch && File.Exists(launch.Executable);
            var canRemove = error == null && game is not null && InstallOwner(game, job) is var owner && owner != Guid.Empty &&
                await ((IStoreInstalledGameProvider)Provider(key.Store)).CanUninstallAsync(key.Id, game.InstallPath!, owner, cancellationToken);
            return new(key, game?.AccountRevision ?? accounts.GetValueOrDefault(key.Store)?.Revision ?? 0, revision,
                installed, canLaunch, canRemove, game?.InstallPath,
                canLaunch || canRemove ? null : error ?? "installation_ownership_lost",
                canRemove ? null : error ?? "installation_ownership_lost");
        }
        finally { gate.Release(); }
    }

    private static bool SameRemovalSelection(StoreLibraryGame? current,StoreLibraryGame captured)=>current is {IsInstalled:true,InstallPath:not null}
        &&current.Key==captured.Key&&current.AccountRevision==captured.AccountRevision
        &&StringComparer.OrdinalIgnoreCase.Equals(current.InstallPath,captured.InstallPath);
    public Task<StoreUninstallPreparation> PrepareUninstallAsync(StoreGameKey key, long expectedAccountRevision,
        long expectedLibraryRevision, CancellationToken cancellationToken = default)=>PrepareUninstallForPathAsync(key,expectedAccountRevision,expectedLibraryRevision,null,cancellationToken);
    internal async Task<StoreUninstallPreparation> PrepareUninstallForPathAsync(StoreGameKey key,long expectedAccountRevision,
        long expectedLibraryRevision,string? expectedInstallPath,CancellationToken cancellationToken=default)
    {
        StoreUninstallPreparation Denied(string error) => new(key, expectedAccountRevision, expectedLibraryRevision, null, null, now(), false, error);
        if(await PrepareAbsentUninstallAsync(key,expectedAccountRevision,expectedLibraryRevision,expectedInstallPath,cancellationToken) is {} absent)return absent;
        if (key.Store == StoreKind.Xbox) return await PrepareXboxUninstallAsync(key, expectedAccountRevision, expectedLibraryRevision, cancellationToken,expectedInstallPath);
        StoreLibraryGame selected;long selectedAccountContext;
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Library revision also advances for unrelated stores/downloads.
            // Revalidate the selected installation instead of making a second
            // click necessary whenever its old display snapshot was superseded.
            if(expectedLibraryRevision<0||expectedLibraryRevision>revision)return Denied("library_changed");
            if(!games.TryGetValue(key,out var held)||!held.IsInstalled||held.InstallPath is null)return Denied("game_not_installed");
            if(held.AccountRevision!=expectedAccountRevision)return Denied("account_changed");
            if(expectedInstallPath is not null&&!StringComparer.OrdinalIgnoreCase.Equals(expectedInstallPath,held.InstallPath))return Denied("installation_changed");
            selected=held;selectedAccountContext=accounts[key.Store].Revision;
        }
        finally{gate.Release();}
        var migrationError = await EnsureLegacyInstallReceiptAsync(key, cancellationToken);
        StoreLibraryGame game; Guid ownerJob; LegacyInstallReceipt? legacyReceipt;long accountContextRevision;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if(!SameRemovalSelection(games.GetValueOrDefault(key),selected))return Denied("installation_changed");
            if(accounts[key.Store].Revision!=selectedAccountContext)return Denied("account_changed");
            var error = UninstallContextError(key, out var held, out var owned);
            if (error != null) return Denied(migrationError ?? error);
            game = held!; ownerJob = InstallOwner(game, owned); legacyReceipt = owned is null ? LegacyInstallFor(game) : null;
            if (game.AccountRevision != expectedAccountRevision) return Denied("account_changed");
            accountContextRevision=accounts[key.Store].Revision;
            installedOperationStores.Add(key.Store); providerOperations++;
        }
        finally { gate.Release(); }
        StoreOwnedUninstallPlan? plan = null; string? failure = null;
        try { plan = await ((IStoreInstalledGameProvider)Provider(key.Store)).PrepareUninstallAsync(key.Id, game.InstallPath!, ownerJob, cancellationToken); }
        catch (Exception e) when (e is not OutOfMemoryException) { failure = e is StoreOperationException s ? s.Code : e is OperationCanceledException ? "uninstall_canceled" : "uninstall_preparation_failed"; }
        await gate.WaitAsync(CancellationToken.None);
        try
        {
            installedOperationStores.Remove(key.Store); providerOperations--;
            if (failure != null || plan == null) return Denied(failure ?? "uninstall_preparation_failed");
            var current=games.GetValueOrDefault(key);
            if (disposed || quiescing || !SameRemovalSelection(current, game) ||
                InstallOwner(current!,OwnedCompletedInstall(current!))!=ownerJob||
                accounts[key.Store].Revision != accountContextRevision || plan.Key != key || plan.OwnerJob != ownerJob ||
                legacyReceipt is not null && (LegacyInstallFor(game) != legacyReceipt || plan.ManifestHash != legacyReceipt.ManifestHash) ||
                !StringComparer.OrdinalIgnoreCase.Equals(plan.InstallPath, game.InstallPath)) return Denied("installation_changed");
            foreach (var old in uninstallPreparations.Where(p => p.Value.Receipt.ExpiresAt <= now()).Select(p => p.Key).ToArray()) uninstallPreparations.Remove(old);
            if (uninstallPreparations.Count >= 64) return Denied("too_many_confirmations");
            // Store a private immutable copy. The wire receipt carries no files.
            plan = plan with { Files = Array.AsReadOnly(plan.Files.ToArray()) };
            var nonce = Guid.NewGuid();
            var receipt = new StoreUninstallPreparation(key, expectedAccountRevision, revision, game.InstallPath, nonce, now().AddMinutes(2), true);
            uninstallPreparations[nonce] = (receipt, plan,accountContextRevision); return receipt;
        }
        finally { gate.Release(); }
    }

    public async Task<StoreCommandResult> UninstallAsync(StoreUninstallPreparation preparation, Guid requestId, CancellationToken cancellationToken = default)
    {
        if(await UninstallAbsentAsync(preparation,requestId,cancellationToken) is {} absent)return absent;
        if (preparation.Key.Store == StoreKind.Xbox) return await XboxUninstallAsync(preparation, requestId, cancellationToken);
        StoreOwnedUninstallPlan plan; StoreLibraryGame game;
        var fingerprint = "uninstall:" + JsonSerializer.Serialize(preparation);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (requestId == Guid.Empty) return Result(false, "invalid_request");
            if (acknowledged.TryGetValue(requestId, out var prior)) return prior.Fingerprint == fingerprint ? prior.Value : Result(false, "request_id_reused");
            // The exact private receipt, expiry, account context, owner, path and
            // freshly read provider manifest remain the confirmation boundary.
            // A different game's progress cannot revoke this user's dialog.
            if (preparation.Nonce is not { } nonce || !uninstallPreparations.TryGetValue(nonce, out var held) || held.Receipt != preparation ||
                now() >= held.Receipt.ExpiresAt || preparation.LibraryRevision>revision) return Result(false, "confirmation_expired_or_mismatched");
            var error = UninstallContextError(preparation.Key, out var current, out var job);
            if (error != null) return Result(false, error);
            game = current!; plan = held.Plan;
            if(accounts[preparation.Key.Store].Revision!=held.AccountContextRevision)return Result(false,"account_changed");
            if (game.AccountRevision != preparation.AccountRevision || game.InstallPath != preparation.InstallPath || InstallOwner(game, job) != plan.OwnerJob ||
                job is null && LegacyInstallFor(game)?.ManifestHash != plan.ManifestHash)
                return Result(false, "installation_changed");
            uninstallPreparations.Remove(nonce); installedOperationStores.Add(preparation.Key.Store); providerOperations++;
        }
        finally { gate.Release(); }
        string? failure = null;
        try { await ((IStoreInstalledGameProvider)Provider(plan.Key.Store)).UninstallAsync(plan, cancellationToken); }
        catch (Exception e) when (e is not OutOfMemoryException) { failure = e is StoreOperationException s ? s.Code : e is OperationCanceledException ? "uninstall_canceled" : "uninstall_failed"; }
        await gate.WaitAsync(CancellationToken.None);
        try
        {
            installedOperationStores.Remove(plan.Key.Store); providerOperations--;
            if (failure == null)
            {
                games[plan.Key] = game with { IsInstalled = false, IsLaunchable = false, InstallPath = null, UnavailableReason = "uninstalled" };
                legacyInstallReceipts.Remove(plan.Key);
                Changed();
            }
            var result = Result(failure == null, failure);
            Remember(requestId, fingerprint, result); await DispatchLocked(); return result;
        }
        finally { gate.Release(); }
    }
}

public sealed class StoreInstalledGameProtocol(StoreQueueEngine engine) : IStoreProtocolExtension
{
    public async Task<(bool Handled, object? Data)> TryHandleAsync(string method, JsonElement args, CancellationToken cancellationToken)
    {
        T Read<T>(string name) => args.GetProperty(name).Deserialize<T>(StoreLibraryService.JsonOptions) ?? throw new StoreOperationException("request_invalid");
        return method switch
        {
            "installed-game" => (true, await engine.GetInstalledGameAsync(Read<StoreGameKey>("key"), cancellationToken)),
            "prepare-uninstall" => (true, await engine.PrepareUninstallForPathAsync(Read<StoreGameKey>("key"), args.GetProperty("expectedAccountRevision").GetInt64(), args.GetProperty("expectedLibraryRevision").GetInt64(),args.TryGetProperty("expectedInstallPath",out var expectedPath)&&expectedPath.ValueKind==JsonValueKind.String?expectedPath.GetString():null, cancellationToken)),
            "uninstall" => (true, await engine.UninstallAsync(Read<StoreUninstallPreparation>("preparation"), args.GetProperty("requestId").GetGuid(), cancellationToken)),
            _ => (false, null)
        };
    }
}

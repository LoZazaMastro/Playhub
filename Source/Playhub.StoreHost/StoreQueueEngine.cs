// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System.Text.Json;

namespace Playhub.StoreLibrary;

// One per-user owner. Provider completion, not optimistic UI state, releases the slot.
public sealed partial class StoreQueueEngine : IStoreLibraryService, IStoreDownloadHistoryService, IStoreCanceledDownloadHistoryService, IStoreHistoryRemovalService, IStoreCancelCleanupService, IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<StoreKind, IStoreProvider> providers;
    private readonly Dictionary<StoreKind, StoreAccount> accounts = [];
    private readonly Dictionary<StoreKind, string> accountIdentities = [];
    private readonly Dictionary<Guid, string> jobAccountIdentities = [];
    private readonly Dictionary<StoreGameKey, StoreLibraryGame> games = [];
    private readonly List<StoreDownloadJob> jobs = [];
    private readonly HashSet<Guid> clearedCompletedJobs = [];
    private readonly HashSet<Guid> clearedCanceledJobs = [];
    private readonly Dictionary<Guid, (string Fingerprint, StoreCommandResult Value)> acknowledged = [];
    private readonly Dictionary<Guid, (StoreKind Store, DateTimeOffset Expiry)> connections = [];
    private readonly Dictionary<Guid, StoreInstallPreparation> preparations = [];
    private readonly HashSet<Guid> privateLaunchRequests = [];
    private readonly IStoreInstallFileSystem files;
    private readonly Func<DateTimeOffset> now;
    private readonly string persistence;
    private long revision;
    private DateTimeOffset lastProgressSave = DateTimeOffset.MinValue;
    private IStoreTransfer? transfer;
    private CancellationTokenSource? startup;
    private Task? startupTask;
    private Guid? active;
    private long progressAttemptBase;
    private long? progressAttemptTotal;
    private double progressAttemptPercent;
    private bool drainUnconfirmed;
    private string? stopReason;
    private StoreSteamActivity? steam;
    private readonly HashSet<Guid> retiredSteamOwners = [];
    private readonly CancellationTokenSource shutdown = new();
    private readonly Task watchdog;
    private bool disposed;
    private bool quiescing;
    private int providerOperations;
    private static readonly TimeSpan ActivityLifetime = TimeSpan.FromSeconds(15);
    private sealed record QueueDisk(int Version, long Revision, StoreAccount[] Accounts, StoreLibraryGame[] Games, StoreDownloadJob[] Jobs, Dictionary<StoreKind, string> AccountIdentities, Dictionary<Guid, string> JobAccountIdentities, Guid[]? ClearedCompletedJobs = null, WindowsManagedReceipt[]? WindowsManagedInstalls = null, Guid[]? ClearedCanceledJobs = null, LegacyInstallReceipt[]? LegacyInstalls = null, StorePartialReceipt[]? PartialDownloads = null);

    public StoreQueueEngine(IEnumerable<IStoreProvider> providers, IStoreInstallFileSystem files, string persistence, Func<DateTimeOffset>? clock = null)
    {
        this.providers = providers.ToDictionary(p => p.Store);
        this.files = files; this.persistence = persistence; now = clock ?? (() => DateTimeOffset.UtcNow);
        foreach (var kind in Enum.GetValues<StoreKind>())
        {
            var cap = this.providers.TryGetValue(kind, out var p) ? p.Capabilities : new(StoreInstallKind.Unavailable, false, false, false, false, false, "provider_unavailable");
            accounts[kind] = new(kind, StoreConnectionState.Disconnected, 1, null, cap);
        }
        Restore();
        watchdog = WatchActivityAsync();
    }
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var provider in providers.Values)
            {
                foreach (var g in games.Values.Where(g => g.Key.Store == provider.Store && g.InstallPath != null).ToArray())
                {
                    try
                    {
                        var local = await provider.ResolveLaunchAsync(g.Key.Id, g.InstallPath!, cancellationToken);
                        games[g.Key] = g with { IsInstalled = await RegisteredInstallExists(provider,g,local,cancellationToken), IsLaunchable = local != null && provider.Store == StoreKind.Gog, UnavailableReason = g.UnavailableReason == "installation_inventory_unavailable" ? null : g.UnavailableReason };
                    }
                    catch (StoreOperationException error) when (error.Code == "installation_inventory_unavailable")
                    { games[g.Key] = g with { IsLaunchable = false, UnavailableReason = error.Code }; }
                }
                ProviderAccount? account;
                try { account = await provider.GetAccountAsync(cancellationToken); }
                catch (OperationCanceledException) { throw; }
                catch { accounts[provider.Store] = accounts[provider.Store] with { State = StoreConnectionState.Expired, ErrorCode = "credentials_unavailable" }; continue; }
                if (account == null)
                {
                    if (provider.Store == StoreKind.Gog) foreach (var g in games.Values.Where(g => g.Key.Store == StoreKind.Gog && g.InstallPath != null).ToArray())
                    {
                        try { var local = await provider.ResolveLaunchAsync(g.Key.Id, g.InstallPath!, cancellationToken); games[g.Key] = g with { IsInstalled = await RegisteredInstallExists(provider,g,local,cancellationToken), IsLaunchable = local != null, UnavailableReason = g.UnavailableReason == "installation_inventory_unavailable" ? null : g.UnavailableReason }; }
                        catch (StoreOperationException error) when (error.Code == "installation_inventory_unavailable")
                        { games[g.Key] = g with { IsLaunchable = false, UnavailableReason = error.Code }; }
                    }
                    continue;
                }
                var same = accountIdentities.TryGetValue(provider.Store, out var identity) && identity == account.AccountIdentity;
                var prior = accounts[provider.Store];
                if (!same) DropUninstalled(provider.Store);
                accounts[provider.Store] = prior with { State = StoreConnectionState.Connected, Revision = same ? prior.Revision : prior.Revision + 1, DisplayName = account.DisplayName };
                accountIdentities[provider.Store] = account.AccountIdentity;
                foreach (var g in games.Values.Where(g => g.Key.Store == provider.Store && g.InstallPath != null).ToArray())
                {
                    try
                    {
                        var launch = await provider.ResolveLaunchAsync(g.Key.Id, g.InstallPath!, cancellationToken);
                        games[g.Key] = g with { IsInstalled = await RegisteredInstallExists(provider,g,launch,cancellationToken), IsLaunchable = launch != null && (provider.Store == StoreKind.Gog || g.AccountRevision == accounts[provider.Store].Revision), UnavailableReason = g.UnavailableReason == "installation_inventory_unavailable" ? null : g.UnavailableReason };
                    }
                    catch (StoreOperationException error) when (error.Code == "installation_inventory_unavailable")
                    { games[g.Key] = g with { IsLaunchable = false, UnavailableReason = error.Code }; }
                }
            }
            ReconcileWindowsInstallInventoryLocked();Changed();
        }
        finally { gate.Release(); }
        await RepairCompletedWindowsGamesAsync(cancellationToken);
        ResumeWindowsManagedObservations();
    }

    private StoreLibrarySnapshot Snapshot() => new(revision, now(), accounts.Values.OrderBy(a => a.Store).ToArray(), games.Values.OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase).ToArray(), jobs.Where(j => (j.State != StoreJobState.Completed || !clearedCompletedJobs.Contains(j.JobId)) && !clearedCanceledJobs.Contains(j.JobId)).Select(DownloadSnapshot).ToArray());
    private StoreDownloadJob DownloadSnapshot(StoreDownloadJob j)
    {
        var removalKind = DownloadRemovalKind(j);
        if (windowsManagedReceipts.TryGetValue(j.JobId, out var managed))
            return j with { CanPause = managed.PendingLaunch && j.State == StoreJobState.Queued,
                CanResume = managed.PendingLaunch && j.State is StoreJobState.PausedByUser or StoreJobState.Interrupted && Connected(j.Key.Store,j.AccountRevision),
                CanCancel = !Terminal(j.State) && (managed.PendingLaunch || WindowsManagedRuntime is IWindowsStoreInstallDestinationRuntime), CanDownloadNow = false, CanRemove = true, RemovalKind = removalKind ?? "history" };
        return j with
        {
        CanPause = Provider(j.Key.Store).Capabilities.CanPause && j.CanPause,
        CanDownloadNow = !drainUnconfirmed && !PartialCleanupRequested(j.JobId) && (j.State == StoreJobState.Queued || CanResumeJob(j)) && active != j.JobId && SteamIdle() && Connected(j.Key.Store, j.AccountRevision) && j.State is StoreJobState.Queued or StoreJobState.PausedByUser or StoreJobState.PausedBySteam or StoreJobState.Interrupted,
        CanResume = !PartialCleanupRequested(j.JobId) && CanResumeJob(j) && SteamIdle() && Connected(j.Key.Store, j.AccountRevision) && j.State is StoreJobState.PausedByUser or StoreJobState.PausedBySteam or StoreJobState.Interrupted,
        CanRemove = true,
        RemovalKind = removalKind ?? "history"
        };
    }
    private StoreCommandResult Result(bool ok, string? error = null, Guid? job = null) => new(ok, error, Snapshot(), job);
    public async Task<StoreLibrarySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    { await gate.WaitAsync(cancellationToken); try { return Snapshot(); } finally { gate.Release(); } }
    public async Task<StoreCommandResult> ClearCompletedAsync(StoreClearCompletedRequest request, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            RequireRunning();
            if (request.RequestId == Guid.Empty) return Result(false, "invalid_request");
            string fingerprint = "clear-completed:" + JsonSerializer.Serialize(request);
            if (acknowledged.TryGetValue(request.RequestId, out var prior))
                return prior.Fingerprint == fingerprint ? prior.Value : Result(false, "request_id_reused");
            if(HistorySelectionError(request.ExpectedRevision,request.Selections) is {} selectionError)return Result(false,selectionError);
            if(request.Selections is {} selections&&selections.Any(s=>jobs.Single(j=>j.JobId==s.JobId).State is not(StoreJobState.Completed or StoreJobState.Canceled)))return Result(false,"job_not_terminal");
            var selected=request.Selections?.Select(s=>s.JobId).ToHashSet();
            var completed = jobs.Where(j => (selected is null||selected.Contains(j.JobId))&&(j.State == StoreJobState.Completed && j.JobId != active && !clearedCompletedJobs.Contains(j.JobId)
                || CanceledHistoryClearable(j) && !clearedCanceledJobs.Contains(j.JobId))).ToArray();
            // History only: install ownership, manifests, games, credentials and
            // payloads remain untouched. Running/paused/failed jobs stay visible.
            HideHistoryRows(completed);
            var result = Result(true); Remember(request.RequestId, fingerprint, result); return result;
        }
        finally { gate.Release(); }
    }
    public async Task<StoreLaunchDescriptor> GetVerifiedLaunchDescriptorAsync(StoreGameKey key, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!games.TryGetValue(key, out var g)) return new(key, 0, revision, false, false, null, null, [], "game_not_found");
            if (InstalledOperationBusy(key.Store)) return new(key, g.AccountRevision, revision, g.IsInstalled, false, null, g.InstallPath, [], "provider_busy");
            // Provider-installed metadata is resolved by the provider, never by title
            // guessing or a client-supplied executable path.
            if (!g.IsInstalled || !g.IsLaunchable) return new(key, g.AccountRevision, revision, g.IsInstalled, false, null, null, [], "game_not_launchable");
            var launch = await Provider(key.Store).ResolveLaunchAsync(key.Id, g.InstallPath!, cancellationToken);
            if (launch == null || !File.Exists(launch.Executable)) return new(key, g.AccountRevision, revision, true, false, null, g.InstallPath, [], "launch_verification_failed");
            return new(key, g.AccountRevision, revision, true, true, launch.Executable, launch.WorkingDirectory, launch.Arguments, GameExecutablePath: launch.GameExecutablePath);
        }
        finally { gate.Release(); }
    }
    private IStoreProvider Provider(StoreKind s) => providers.TryGetValue(s, out var p) ? p : throw new StoreOperationException("provider_unavailable");
    private void RequireRunning() { if (disposed || quiescing) throw new StoreOperationException("maintenance_in_progress"); }
    private async Task<T> ProviderOperation<T>(StoreKind store, Func<Task<T>> action, CancellationToken token)
    {
        await gate.WaitAsync(token); try { RequireRunning(); if (InstalledOperationBusy(store)) throw new StoreOperationException("provider_busy"); providerOperations++; } finally { gate.Release(); }
        try { return await action(); }
        finally { await gate.WaitAsync(); try { providerOperations--; } finally { gate.Release(); } }
    }
    public async Task<StorePrivateLaunchPlan> PreparePrivateLaunchAsync(StoreGameKey key, Guid requestId, CancellationToken token)
    {
        long accountRevision; string path;
        await gate.WaitAsync(token);
        try
        {
            RequireRunning();
            if (requestId == Guid.Empty || !privateLaunchRequests.Add(requestId)) throw new StoreOperationException("launch_request_replayed");
            if (InstalledOperationBusy(key.Store) || !games.TryGetValue(key, out var game) || !game.IsInstalled || !game.IsLaunchable || game.InstallPath == null || key.Store != StoreKind.Gog && !Connected(key.Store, game.AccountRevision)) throw new StoreOperationException("game_not_launchable");
            accountRevision = game.AccountRevision; path = game.InstallPath;
        }
        finally { gate.Release(); }
        var plan = await ProviderOperation(key.Store, () => Provider(key.Store).PrepareLaunchAsync(key.Id, path, token), token);
        await gate.WaitAsync(token);
        try
        {
            RequireRunning();
            if (InstalledOperationBusy(key.Store) || key.Store != StoreKind.Gog && !Connected(key.Store, accountRevision) || !games.TryGetValue(key, out var game) || game.AccountRevision != accountRevision || game.InstallPath != path || plan.Key != key || plan.ExpiresAt <= now() || plan.ExpiresAt > now().AddSeconds(31) || !File.Exists(plan.GameExecutable)) throw new StoreOperationException("launch_context_changed");
            installedLaunchReservations[key] = plan.ExpiresAt;
            return plan;
        }
        finally { gate.Release(); }
    }

    public async Task<StoreConnectSession> BeginConnectAsync(StoreKind store, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var p = Provider(store);
            RequireRunning();
            if (InstalledOperationBusy(store)) throw new StoreOperationException("provider_busy");
            if (!p.Capabilities.CanConnect) throw new StoreOperationException("connect_unavailable");
            if (p is IStoreTypedSignInProvider) throw new StoreOperationException("typed_signin_required");
            if (store == StoreKind.Amazon && accounts[store].State == StoreConnectionState.Connecting) throw new StoreOperationException("account_busy");
            var authorizationUri = p.AuthorizationUri;
            // Amazon's device verifier belongs to the latest sign-in window.
            // An older window must never consume that new window's verifier.
            if (store == StoreKind.Amazon)
                foreach (var old in connections.Where(c => c.Value.Store == store).Select(c => c.Key).ToArray()) connections.Remove(old);
            var id = Guid.NewGuid(); var expiry = now().AddMinutes(5); connections[id] = (store, expiry);
            return new(id, store, authorizationUri, expiry, true);
        }
        finally { gate.Release(); }
    }

    public async Task<StoreCommandResult> CompleteConnectAsync(Guid sessionId, string authorizationCode, CancellationToken cancellationToken = default)
    {
        (StoreKind Store, DateTimeOffset Expiry) c;
        StoreAccount prior;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!connections.Remove(sessionId, out c) || now() >= c.Expiry || string.IsNullOrWhiteSpace(authorizationCode) || authorizationCode.Length > 8192) return Result(false, "connect_expired_or_invalid");
            prior = accounts[c.Store];
            RequireRunning();
            if (InstalledOperationBusy(c.Store)) return Result(false, "provider_busy");
            if (prior.State == StoreConnectionState.Connecting) return Result(false, "account_busy");
            if (active is { } id && jobs.Single(j => j.JobId == id).Key.Store == c.Store) return Result(false, "download_active");
            // Reserve before network I/O. A losing concurrent login must never
            // replace credentials after a different account has been published.
            accounts[c.Store] = prior with { State = StoreConnectionState.Connecting, Revision = prior.Revision + 1 };
            try { Changed(); }
            catch { accounts[c.Store] = prior; throw; }
            providerOperations++;
        }
        finally { gate.Release(); }
        ProviderAccount? account = null; string? error = null;
        try { account = await Provider(c.Store).ConnectAsync(authorizationCode, cancellationToken); }
        catch (OperationCanceledException) { error = "authentication_canceled"; }
        catch (StoreOperationException e) when (c.Store == StoreKind.Amazon) { error = e.Code; }
        catch { error = "authentication_failed"; }
        // Complete the reservation even if the caller disconnected after OAuth.
        await gate.WaitAsync(CancellationToken.None);
        try
        {
            providerOperations--;
            if (accounts[c.Store].State != StoreConnectionState.Connecting || accounts[c.Store].Revision != prior.Revision + 1) return Result(false, "account_changed");
            accounts[c.Store] = prior with { State = account == null ? prior.State : StoreConnectionState.Connected, Revision = prior.Revision + 2, DisplayName = account?.DisplayName ?? prior.DisplayName, ErrorCode = error };
            if (account != null) accountIdentities[c.Store] = account.AccountIdentity;
            DropUninstalled(c.Store);
            Changed(); return Result(account != null, error);
        }
        finally { gate.Release(); }
    }

    public async Task<StoreCommandResult> DisconnectAsync(StoreKind store, long expectedAccountRevision, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            RequireRunning();
            if (accounts[store].Revision != expectedAccountRevision || accounts[store].State == StoreConnectionState.Connecting) return Result(false, "account_changed");
            if (InstalledOperationBusy(store)) return Result(false, "provider_busy");
            if (active is { } id && jobs.Single(j => j.JobId == id).Key.Store == store) return Result(false, "download_active");
            await Provider(store).DisconnectAsync(cancellationToken);
            accounts[store] = accounts[store] with { State = StoreConnectionState.Disconnected, Revision = expectedAccountRevision + 1, DisplayName = null };
            accountIdentities.Remove(store);
            for (var i = 0; i < jobs.Count; i++) if (jobs[i].Key.Store == store && !Terminal(jobs[i].State)) Set(i, jobs[i].State == StoreJobState.PausedByUser ? StoreJobState.PausedByUser : StoreJobState.Interrupted, "account_disconnected");
            DropUninstalled(store);
            Changed(); return Result(true);
        }
        catch (StoreOperationException e) { return Result(false, e.Code); }
        finally { gate.Release(); }
    }

    public async Task<StoreCommandResult> RefreshAsync(StoreKind store, long expectedAccountRevision, CancellationToken cancellationToken = default)
    {
        var result=await RefreshCoreAsync(store,expectedAccountRevision,cancellationToken);
        if(result.Accepted&&store==StoreKind.Xbox){await RepairCompletedWindowsGamesAsync(cancellationToken);return result with{Snapshot=await GetSnapshotAsync(cancellationToken)};}
        return result;
    }
    private async Task<StoreCommandResult> RefreshCoreAsync(StoreKind store, long expectedAccountRevision, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try { if (InstalledOperationBusy(store)) return Result(false, "provider_busy"); if (!Connected(store, expectedAccountRevision)) return Result(false, "account_changed"); }
        finally { gate.Release(); }
        IReadOnlyList<ProviderGame> found;
        try { found = await ProviderOperation(store, () => Provider(store).ListAsync(cancellationToken), cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch { await gate.WaitAsync(cancellationToken); try { return Result(false, "library_unavailable"); } finally { gate.Release(); } }
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (InstalledOperationBusy(store)) return Result(false, "provider_busy");
            if (!Connected(store, expectedAccountRevision)) return Result(false, "account_changed");
            if (found.Count > 50000 || found.Any(g => string.IsNullOrWhiteSpace(g.Id) || g.Id.Length > 256 || string.IsNullOrWhiteSpace(g.Title)) || found.Select(g => g.Id).Distinct(StringComparer.Ordinal).Count() != found.Count) return Result(false, "invalid_library");
            DropUninstalled(store,preservePendingJobs:true);
            foreach (var g in found)
            {
                var cap = Provider(store).Capabilities;
                if (!g.CanInstall) cap = cap with { InstallKind = StoreInstallKind.Unavailable, UnavailableReason = "title_not_installable" };
                var key = new StoreGameKey(store, g.Id);
                games[key] = new(key, g.Title, expectedAccountRevision, cap, g.InstallPath != null, g.Launchable, g.InstallPath, store.ToString().ToLowerInvariant() + ":" + g.Id, g.RequiredBytes, cap.UnavailableReason, g.ArtworkUrl, g.ArtworkFallbackUrls, g.ArtworkUrl == null ? null : "cover", g.LibraryKind,g.InstallProvider);
            }
            for (var i = 0; i < jobs.Count; i++)
            {
                var j = jobs[i];
                if (j.Key.Store == store && !Terminal(j.State) && j.AccountRevision != expectedAccountRevision && games.ContainsKey(j.Key) && jobAccountIdentities.TryGetValue(j.JobId, out var identity) && accountIdentities.TryGetValue(store, out var currentIdentity) && identity == currentIdentity)
                {
                    jobs[i] = j with { AccountRevision = expectedAccountRevision, Revision = j.Revision + 1, State = j.State == StoreJobState.PausedByUser ? StoreJobState.PausedByUser : StoreJobState.Interrupted, ReasonCode = "account_revalidated" };
                    if (windowsManagedReceipts.TryGetValue(j.JobId,out var receipt)) windowsManagedReceipts[j.JobId]=receipt with { AccountRevision=expectedAccountRevision };
                }
            }
            ReconcileWindowsInstallInventoryLocked();Changed(); return Result(true);
        }
        finally { gate.Release(); }
    }

    public Task<StoreInstallPreparation> PrepareInstallAsync(StoreGameKey key,long expectedAccountRevision,string? absoluteDestination=null,CancellationToken cancellationToken=default)
        =>PrepareInstallWithLanguageAsync(key,expectedAccountRevision,absoluteDestination,cancellationToken,null);
    internal async Task<StoreInstallPreparation> PrepareInstallWithLanguageAsync(StoreGameKey key, long expectedAccountRevision, string? absoluteDestination, CancellationToken cancellationToken, string? language)
    {
        ProviderInstallSize size; StoreInstallOptions? options = null;
        await gate.WaitAsync(cancellationToken);
        try { if (InstalledOperationBusy(key.Store)) throw new StoreOperationException("provider_busy"); if (!Connected(key.Store, expectedAccountRevision) || !games.ContainsKey(key)) throw new StoreOperationException("account_or_catalog_changed"); }
        finally { gate.Release(); }
        try { if(Provider(key.Store) is IStoreInstallLanguageSource source){options=await ProviderOperation(key.Store,()=>source.GetInstallOptionsAsync(key.Id,language,cancellationToken),cancellationToken);size=options.Size;}else size = await ProviderOperation(key.Store, () => Provider(key.Store).GetInstallSizeAsync(key.Id, cancellationToken), cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch { throw new StoreOperationException("install_size_unavailable"); }
        if (size.RequiredBytes < 0 || size.DownloadBytes < 0) throw new StoreOperationException("install_size_invalid");
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (InstalledOperationBusy(key.Store)) throw new StoreOperationException("provider_busy");
            if (!Connected(key.Store, expectedAccountRevision) || !games.TryGetValue(key, out var game) || game.AccountRevision != expectedAccountRevision) throw new StoreOperationException("account_or_catalog_changed");
            var locations = files.Locations();
            var expiry = now().AddMinutes(5); var r = ++revision;
            var result = new StoreInstallPreparation(key, expectedAccountRevision, r, null, expiry, size.RequiredBytes,
                StoreInstallDestination.Suggest(key.Store, game.Title, locations, size.RequiredBytes), null, locations, game.Capabilities.InstallKind, Languages:options?.Languages, Language:options?.Language);
            if (game.Capabilities.InstallKind != StoreInstallKind.Direct) return result with { ErrorCode = "direct_install_unavailable" };
            if (absoluteDestination == null) return result;
            string path;
            try { path = files.CanonicalDestination(absoluteDestination); }
            catch { return result with { ErrorCode = "invalid_destination" }; }
            if (!files.CanClaim(path, null) && ReusableDownloadRoot(key, path) is null) return result with { ErrorCode = "destination_not_empty_or_unwritable" };
            if (files.FreeBytes(path) < size.RequiredBytes) return result with { ErrorCode = "insufficient_space" };
            games[key] = game with { DownloadBytes = size.DownloadBytes };
            var nonce = Guid.NewGuid(); result = result with { SelectionNonce = nonce, ConfirmedPath = path };
            foreach (var old in preparations.Where(p => p.Value.ExpiresAt <= now()).Select(p => p.Key).ToArray()) preparations.Remove(old);
            preparations[nonce] = result; return result;
        }
        finally { gate.Release(); }
    }

    public async Task<StoreCommandResult> EnqueueAsync(StoreEnqueueRequest request, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            RequireRunning();
            if (request.RequestId == Guid.Empty) return Result(false, "invalid_request");
            if (InstalledOperationBusy(request.Key.Store)) return Result(false, "provider_busy");
            var fingerprint = "enqueue:" + JsonSerializer.Serialize(request);
            if (acknowledged.TryGetValue(request.RequestId, out var prior)) return prior.Fingerprint == fingerprint ? prior.Value : Result(false, "request_id_reused");
            if (!Connected(request.Key.Store, request.ExpectedAccountRevision) || !games.TryGetValue(request.Key, out var game) || game.AccountRevision != request.ExpectedAccountRevision) return Result(false, "account_or_catalog_changed");
            if (!preparations.Remove(request.SelectionNonce, out var p) || p.Revision != request.ExpectedPreparationRevision || p.Key != request.Key || p.AccountRevision != request.ExpectedAccountRevision || p.ConfirmedPath == null || !StringComparer.OrdinalIgnoreCase.Equals(p.ConfirmedPath, request.InstallPath) || now() >= p.ExpiresAt) return Result(false, "confirmation_expired_or_mismatched");
            if(request.Language!=p.Language)return Result(false,"confirmation_expired_or_mismatched");
            if (jobs.Any(j => j.Key == request.Key && !Terminal(j.State))) return Result(false, "already_queued");
            if (game.Capabilities.InstallKind != StoreInstallKind.Direct) return Result(false, "direct_install_unavailable");
            if (p.RequiredBytes is not { } required || files.FreeBytes(p.ConfirmedPath) < required) return Result(false, "insufficient_space");
            var id = Guid.NewGuid();
            var next = new StoreDownloadJob(id, request.Key, game.Title, SteamIdle() ? StoreJobState.Queued : StoreJobState.BlockedSteam, 1, request.ExpectedAccountRevision, p.ConfirmedPath, 0, game.DownloadBytes, null, now(), false, false, true, LibraryKind: game.LibraryKind, Language:p.Language);
            StorePartialReceipt? reclaimed = null;
            if (!files.Claim(p.ConfirmedPath, id) && (reclaimed = ReclaimDownloadRoot(next)) is null) return Result(false, "destination_not_empty_or_unwritable");
            jobs.Add(next);
            jobAccountIdentities[id] = accountIdentities[request.Key.Store];
            if (reclaimed != null) partialDownloads.Add(id, reclaimed);
            else RetainFreshDownloadRoot(jobs[^1]);
            Changed(); await DispatchLocked(); var result = Result(true, job: id); Remember(request.RequestId, fingerprint, result); return result;
        }
        finally { gate.Release(); }
    }

    public async Task<StoreCommandResult> ControlAsync(StoreJobCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Action == StoreJobAction.Cancel && await TryCancelWindowsControlAsync(command,cancellationToken) is {} nativeCancel) return nativeCancel;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (command.RequestId == Guid.Empty) return Result(false, "invalid_request");
            var fingerprint = "control:" + JsonSerializer.Serialize(command);
            if (acknowledged.TryGetValue(command.RequestId, out var prior)) return prior.Fingerprint == fingerprint ? prior.Value : Result(false, "request_id_reused");
            var index = jobs.FindIndex(j => j.JobId == command.JobId); if (index < 0) return Result(false, "job_not_found");
            if (quiescing && command.Action is StoreJobAction.Resume or StoreJobAction.DownloadNow) return Result(false, "maintenance_in_progress");
            var j = jobs[index]; if (DownloadSelectionError(j,command.ExpectedRevision,command.ExpectedKey,command.ExpectedAccountRevision,command.ExpectedInstallPath) is {} selectionError)return Result(false,selectionError);
            if (command.Action == StoreJobAction.Cancel && !Terminal(j.State) && windowsManagedReceipts.TryGetValue(j.JobId,out var cancelReceipt)
                && cancelReceipt.PendingLaunch)
            {
                jobs[index]=j with { State=StoreJobState.Canceled, Revision=j.Revision+1, UpdatedAt=now(), CanCancel=false, CanPause=false, CanResume=false };
                clearedCanceledJobs.Add(j.JobId); Changed();
                DispatchWindowsManagedLocked();var canceled=Result(true,job:j.JobId);Remember(command.RequestId,fingerprint,canceled);return canceled;
            }
            if (windowsManagedReceipts.TryGetValue(j.JobId, out var managed) && managed.PendingLaunch)
            {
                var next = command.Action switch {
                    StoreJobAction.Cancel when !Terminal(j.State) => StoreJobState.Canceled,
                    StoreJobAction.Pause when j.State == StoreJobState.Queued => StoreJobState.PausedByUser,
                    StoreJobAction.Resume when j.State is StoreJobState.PausedByUser or StoreJobState.Interrupted && Connected(j.Key.Store,j.AccountRevision) => StoreJobState.Queued,
                    _ => j.State };
                if (next == j.State) return Result(false,"action_unavailable");
                jobs[index] = j with { State = next, Revision = j.Revision + 1, UpdatedAt = now() };
                Changed(); DispatchWindowsManagedLocked();
                var accepted = Result(true,job:j.JobId); Remember(command.RequestId,fingerprint,accepted); return accepted;
            }
            if (PartialCleanupRequested(j.JobId)) return Result(false, "partial_cleanup_in_progress");
            if (InstalledOperationBusy(j.Key.Store)) return Result(false, "provider_busy");
            if (drainUnconfirmed && (active == j.JobId || command.Action == StoreJobAction.DownloadNow)) return Result(false, "worker_drain_unconfirmed");
            if (command.Action == StoreJobAction.Cancel && Provider(j.Key.Store).Capabilities.CanCancel && !Terminal(j.State))
            {
                if (active == j.JobId) { stopReason = "user_cancel"; Set(index, StoreJobState.Canceling, "user_cancel"); RequestStopLocked(); }
                else Set(index, StoreJobState.Canceled);
            }
            else if (command.Action == StoreJobAction.Pause && Provider(j.Key.Store).Capabilities.CanPause && !Terminal(j.State) && j.State is not StoreJobState.PausedByUser)
            {
                if (active == j.JobId) { stopReason = "user_pause"; Set(index, StoreJobState.Pausing, "user_pause"); RequestStopLocked(); }
                else Set(index, StoreJobState.PausedByUser);
            }
            else if (command.Action == StoreJobAction.Resume && CanResumeJob(j) && j.State is StoreJobState.PausedByUser or StoreJobState.PausedBySteam or StoreJobState.Interrupted)
            {
                if (!Connected(j.Key.Store, j.AccountRevision)) return Result(false, "account_changed");
                if (!SteamIdle()) return Result(false, "steam_priority");
                if (!files.CanClaim(j.InstallPath, j.JobId)) return Result(false, "destination_ownership_lost");
                Set(index, SteamIdle() ? StoreJobState.Queued : StoreJobState.BlockedSteam); await DispatchLocked();
            }
            else if (command.Action == StoreJobAction.DownloadNow && (j.State == StoreJobState.Queued || CanResumeJob(j)) && active != j.JobId && j.State is StoreJobState.Queued or StoreJobState.PausedByUser or StoreJobState.PausedBySteam or StoreJobState.Interrupted)
            {
                if (!SteamIdle()) return Result(false, "steam_priority");
                if (!Connected(j.Key.Store, j.AccountRevision)) return Result(false, "account_changed");
                if (!files.CanClaim(j.InstallPath, j.JobId)) return Result(false, "destination_ownership_lost");
                Set(index, StoreJobState.Queued, "user_prioritized"); var selected = jobs[index]; jobs.RemoveAt(index); jobs.Insert(0, selected);
                if (active is { } current) { if (stopReason == null) { var other = jobs.FindIndex(x => x.JobId == current); stopReason = "queue_preempt"; Set(other, StoreJobState.Pausing, "queue_preempt"); RequestStopLocked(); } }
                else await DispatchLocked();
            }
            else return Result(false, "action_unavailable");
            Changed(); var result = Result(true, job: j.JobId); Remember(command.RequestId, fingerprint, result); return result;
        }
        finally { gate.Release(); }
    }

    // Called only by the authenticated installed app/agent endpoint. A later owner
    // replaces the old stream; stale/replayed/future observations never resume work.
    public async Task<bool> ObserveSteamAsync(StoreSteamActivity activity, bool authenticatedOwner, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var time = now();
            if (!authenticatedOwner || activity.OwnerId == Guid.Empty || activity.Sequence <= 0 || retiredSteamOwners.Contains(activity.OwnerId) || activity.ObservedAt > time.AddMilliseconds(250) || time - activity.ObservedAt > ActivityLifetime || (steam?.OwnerId == activity.OwnerId && (activity.Sequence <= steam.Sequence || activity.ObservedAt < steam.ObservedAt))) return false;
            if (steam != null && steam.OwnerId != activity.OwnerId) retiredSteamOwners.Add(steam.OwnerId);
            steam = activity;
            await ApplyActivityLocked(); return true;
        }
        finally { gate.Release(); }
    }
    public async Task InvalidateSteamAsync(Guid ownerId, CancellationToken token = default)
    {
        await gate.WaitAsync(token); try { if (steam?.OwnerId == ownerId) { retiredSteamOwners.Add(ownerId); steam = null; await ApplyActivityLocked(); } } finally { gate.Release(); }
    }

    private void DropUninstalled(StoreKind store,bool preservePendingJobs=false)
    {
        foreach (var g in games.Values.Where(g => g.Key.Store == store).ToArray())
            if (!g.IsInstalled)
            {
                if(preservePendingJobs&&Connected(store,g.AccountRevision)&&jobs.Any(j=>j.Key==g.Key&&(!Terminal(j.State)||windowsManagedReceipts.ContainsKey(j.JobId)&&!WindowsManagedTerminal(j))&&j.AccountRevision==g.AccountRevision
                    &&jobAccountIdentities.GetValueOrDefault(j.JobId)==accountIdentities.GetValueOrDefault(store)))continue;
                games.Remove(g.Key);
            }
            else if (store != StoreKind.Gog) games[g.Key] = g with { IsLaunchable = false, UnavailableReason = "authentication_required" };
    }
    private bool Connected(StoreKind s, long r) => accounts[s].State == StoreConnectionState.Connected && accounts[s].Revision == r;
    private bool SteamIdle() => steam != null && !steam.ActiveLocalDownload && now() >= steam.ObservedAt && now() - steam.ObservedAt <= ActivityLifetime;
    private static bool Terminal(StoreJobState s) => s is StoreJobState.Completed or StoreJobState.Canceled or StoreJobState.Failed or StoreJobState.ExternalHandoff;
    private void Set(int i, StoreJobState state, string? reason = null, string? error = null)
    {
        var j = jobs[i]; jobs[i] = j with { State = state, ProgressPercent = state == StoreJobState.Completed ? 100 : j.ProgressPercent, Revision = j.Revision + 1, UpdatedAt = now(), ReasonCode = reason, ErrorCode = error, CanPause = Provider(j.Key.Store).Capabilities.CanPause && state is StoreJobState.Downloading or StoreJobState.Starting or StoreJobState.Queued or StoreJobState.BlockedSteam, CanResume = Provider(j.Key.Store).Capabilities.CanResume && state is StoreJobState.PausedByUser or StoreJobState.PausedBySteam or StoreJobState.Interrupted, CanCancel = Provider(j.Key.Store).Capabilities.CanCancel && !Terminal(state) };
    }
    private async Task ApplyActivityLocked()
    {
        if (!SteamIdle() && active != null && stopReason == null) { stopReason = "steam_priority"; Set(jobs.FindIndex(j => j.JobId == active), StoreJobState.Pausing, "steam_priority"); RequestStopLocked(); Changed(); }
        if (SteamIdle())
        {
            for (var i = 0; i < jobs.Count; i++) if (jobs[i].State is StoreJobState.BlockedSteam or StoreJobState.PausedBySteam) Set(i, StoreJobState.Queued);
            await DispatchLocked();
        }
    }
    private Task DispatchLocked()
    {
        if (active != null || partialCleanupSlots.Count != 0 || !SteamIdle() || disposed || quiescing) return Task.CompletedTask;
        var index = jobs.FindIndex(j => j.State == StoreJobState.Queued && !windowsManagedReceipts.ContainsKey(j.JobId) && !PartialCleanupRequested(j.JobId) && !InstalledOperationBusy(j.Key.Store) && Connected(j.Key.Store, j.AccountRevision)
            && jobAccountIdentities.TryGetValue(j.JobId,out var identity) && accountIdentities.GetValueOrDefault(j.Key.Store)==identity); if (index < 0) return Task.CompletedTask;
        var j = jobs[index];
        if (!files.CanClaim(j.InstallPath, j.JobId)) { Set(index, StoreJobState.Failed, error: "destination_ownership_lost"); Changed(); return DispatchLocked(); }
        active = j.JobId; stopReason = null;
        progressAttemptBase=j.Key.Store is StoreKind.Epic or StoreKind.Gog?j.DownloadedBytes:0;
        progressAttemptTotal=j.TotalBytes;progressAttemptPercent=progressAttemptBase>0?j.ProgressPercent??0:0;
        Set(index, StoreJobState.Starting); Changed();
        startup = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
        startupTask = StartTransferAsync(j, startup.Token);
        return Task.CompletedTask;
    }
    private async Task StartTransferAsync(StoreDownloadJob job, CancellationToken token)
    {
        // This task MUST yield before provider I/O so no slow auth/query can hold
        // the queue gate and delay a real Steam-priority observation.
        await Task.Yield();
        IStoreTransfer? owned = null; Exception? failure = null; bool providerEntered = false;
        try
        {
            await PreparePartialWorkerAsync(job, token);
            token.ThrowIfCancellationRequested();
            providerEntered = true;
            owned = await Provider(job.Key.Store).StartAsync(new(job.JobId, job.Key, job.InstallPath, job.Language), p => _ = ProgressAsync(job.JobId, p), token);
        }
        catch (Exception e) { failure = e; }
        await gate.WaitAsync();
        try
        {
            if (active != job.JobId) throw new InvalidOperationException("worker_slot_changed");
            startup?.Dispose(); startup = null;
            if (owned == null)
            {
                var i = jobs.FindIndex(j => j.JobId == job.JobId);
                if (partialDownloads.TryGetValue(job.JobId, out var receipt))
                    partialDownloads[job.JobId] = receipt with { DrainConfirmed = !providerEntered };
                if (PartialCleanupRequested(job.JobId))
                {
                    if (!providerEntered) { Set(i, StoreJobState.Canceling, "partial_cleanup"); Changed(); SchedulePartialCleanupLocked(job.JobId); }
                    else { Set(i, StoreJobState.Interrupted, "partial_cleanup", "partial_cleanup_drain_unconfirmed"); active = null; stopReason = null; Changed(); await DispatchLocked(); }
                    return;
                }
                var state = failure is OperationCanceledException && stopReason != null
                    ? stopReason == "user_cancel" ? StoreJobState.Canceled : stopReason == "user_pause" ? StoreJobState.PausedByUser : stopReason == "queue_preempt" ? StoreJobState.Queued : stopReason is "maintenance" or "host_shutdown" ? StoreJobState.Interrupted : StoreJobState.BlockedSteam
                    : StoreJobState.Failed;
                Set(i, state, stopReason, state == StoreJobState.Failed ? failure is StoreOperationException operation ? operation.Code : "worker_start_failed" : null);
                active = null; transfer = null; stopReason = null; Changed(); await DispatchLocked(); return;
            }
            transfer = owned; if (stopReason != null || disposed) RequestStopLocked();
            _ = FinishAsync(job.JobId, owned);
        }
        finally { gate.Release(); }
    }
    private void RequestStopLocked()
    {
        var t = transfer;
        if (t == null) { startup?.Cancel(); return; }
        _ = Task.Run(async () => { try { await t.RequestCheckpointAsync(CancellationToken.None); } catch { /* Completion is the sole slot-release authority. */ } });
    }
    private async Task ProgressAsync(Guid id, TransferProgress p)
    {
        await gate.WaitAsync();
        try
        {
            var i = jobs.FindIndex(j => j.JobId == id); if (active != id || i < 0 || stopReason != null) return;
            if (p.DownloadedBytes < 0 || p.TotalBytes is < 0 || p.BytesPerSecond is { } speed && (!double.IsFinite(speed) || speed < 0) || p.ProgressPercent is { } percent && (!double.IsFinite(percent) || percent is < 0 or > 100)) return;
            var j = jobs[i];
            // Legendary/GOG report bytes and percent for this worker's remaining
            // tasks, starting at zero after resume. Add the saved baseline once;
            // keep the original full size instead of replacing it with remaining size.
            var downloaded=p.DownloadedBytes>long.MaxValue-progressAttemptBase?long.MaxValue:progressAttemptBase+p.DownloadedBytes;
            long? total=progressAttemptBase>0?progressAttemptTotal??(p.TotalBytes is {} remaining&&remaining<=long.MaxValue-progressAttemptBase?progressAttemptBase+remaining:(long?)null):p.TotalBytes??j.TotalBytes;
            if(total is >0)downloaded=Math.Min(downloaded,total.Value);
            var reported=p.ProgressPercent is {} value?progressAttemptPercent+(100-progressAttemptPercent)*value/100:(double?)null;
            jobs[i] = j with { DownloadedBytes = Math.Max(j.DownloadedBytes,downloaded), TotalBytes = total, BytesPerSecond = p.BytesPerSecond,
                ProgressPercent = reported is {} nextPercent?Math.Max(j.ProgressPercent??0,nextPercent):j.ProgressPercent, UpdatedAt = now() };
            var state = p.Phase == "verifying" ? StoreJobState.Verifying : p.Phase == "installing" ? StoreJobState.Installing : StoreJobState.Downloading;
            if (j.State != state) { Set(i, state); Changed(); }
            else
            {
                // A progress tick is not a new command authority. A visible
                // confirmation remains valid until lifecycle/ownership changes.
                revision++;
                if (now() - lastProgressSave >= TimeSpan.FromSeconds(2)) { Save(); lastProgressSave = now(); }
            }
        }
        finally { gate.Release(); }
    }
    private async Task FinishAsync(Guid id, IStoreTransfer owned)
    {
        TransferCompletion outcome;
        try { outcome = await owned.Completion; } catch { outcome = new(false, false, false, "worker_failed", DrainConfirmed: false); }
        await gate.WaitAsync();
        try
        {
            if (active != id || !ReferenceEquals(transfer, owned)) return;
            var i = jobs.FindIndex(j => j.JobId == id);
            if (!outcome.DrainConfirmed)
            {
                drainUnconfirmed = true; stopReason ??= "worker_drain_unconfirmed";
                Set(i, StoreJobState.Interrupted, stopReason, "worker_drain_unconfirmed");
                jobs[i] = jobs[i] with { CanCancel = false, CanPause = false, CanResume = false };
                Changed(); return; // Retain the actual slot/transfer; no second worker or successful quiesce.
            }
            if (partialDownloads.TryGetValue(id, out var partial)) partialDownloads[id] = partial with { DrainConfirmed = true };
            if (outcome.Completed && outcome.InstallVerified)
            {
                Set(i, StoreJobState.Completed); var j = jobs[i];
                if(Connected(j.Key.Store,j.AccountRevision)&&jobAccountIdentities.TryGetValue(j.JobId,out var heldIdentity)&&accountIdentities.GetValueOrDefault(j.Key.Store)==heldIdentity)
                {
                    games.TryGetValue(j.Key,out var g);
                    g??=new(j.Key,j.Title,j.AccountRevision,Provider(j.Key.Store).Capabilities,false,false,ArtworkKey:j.Key.Store.ToString().ToLowerInvariant()+":"+j.Key.Id,DownloadBytes:j.TotalBytes,LibraryKind:j.LibraryKind);
                    if(g.AccountRevision==j.AccountRevision)
                    {
                        ProviderLaunch? launch=null;
                        try{launch=await Provider(j.Key.Store).ResolveLaunchAsync(j.Key.Id,j.InstallPath,CancellationToken.None);}
                        catch(Exception error)when(error is not OutOfMemoryException){ }
                        games[j.Key]=g with{IsInstalled=true,IsLaunchable=launch is not null,InstallPath=j.InstallPath};
                    }
                }
            }
            else if (PartialCleanupRequested(id))
            {
                await owned.DisposeAsync(); transfer = null;
                Set(i, StoreJobState.Canceling, "partial_cleanup"); Changed(); SchedulePartialCleanupLocked(id); return;
            }
            else if (stopReason == "user_cancel" && outcome.Stopped) Set(i, StoreJobState.Canceled, stopReason);
            else if (outcome.Checkpointed && stopReason != null) Set(i, stopReason == "user_cancel" ? StoreJobState.Canceled : stopReason == "user_pause" ? StoreJobState.PausedByUser : stopReason == "queue_preempt" ? StoreJobState.Queued : stopReason is "maintenance" or "host_shutdown" ? StoreJobState.Interrupted : StoreJobState.PausedBySteam, stopReason);
            else Set(i, StoreJobState.Interrupted, error: outcome.ErrorCode ?? "checkpoint_unconfirmed");
            await owned.DisposeAsync(); active = null; transfer = null; stopReason = null; Changed(); await DispatchLocked();
        }
        finally { gate.Release(); }
    }
    private async Task WatchActivityAsync()
    {
        try { while (!shutdown.IsCancellationRequested) { await Task.Delay(500, shutdown.Token); await gate.WaitAsync(shutdown.Token); try { await ApplyActivityLocked(); } finally { gate.Release(); } } }
        catch (OperationCanceledException) { }
    }
    private void Remember(Guid id, string fingerprint, StoreCommandResult value) { if (acknowledged.Count >= 1024) acknowledged.Remove(acknowledged.Keys.First()); acknowledged[id] = (fingerprint, value); }
    private void Changed() { revision++; Save(); }
    private void Save()
    {
        if (string.IsNullOrEmpty(persistence)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(persistence)!);
        var tmp = persistence + ".new";
        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(stream, new QueueDisk(1, revision, accounts.Values.ToArray(), games.Values.ToArray(), jobs.ToArray(), accountIdentities, jobAccountIdentities, clearedCompletedJobs.ToArray(), windowsManagedReceipts.Values.ToArray(), clearedCanceledJobs.ToArray(), legacyInstallReceipts.Values.ToArray(), partialDownloads.Values.ToArray())); stream.Flush(true); }
        File.Move(tmp, persistence, true);
    }
    private void Restore()
    {
        if (string.IsNullOrEmpty(persistence) || !File.Exists(persistence)) return;
        var data = JsonSerializer.Deserialize<QueueDisk>(File.ReadAllText(persistence)) ?? throw new StoreOperationException("queue_invalid");
        if (data.Version != 1 || data.Jobs.Length > 10000 || data.Games.Length > 50000) throw new StoreOperationException("queue_invalid");
        if(data.Jobs.Any(job=>job.Language is not null&&!StoreCliProvider.ValidInstallLanguage(job.Language)))throw new StoreOperationException("queue_invalid");
        revision = data.Revision;
        foreach (var a in data.Accounts) accounts[a.Store] = a with { State = StoreConnectionState.Disconnected, Capabilities = accounts[a.Store].Capabilities };
        foreach (var g in data.Games) games[g.Key] = g with { IsLaunchable = false };
        foreach (var id in data.AccountIdentities) accountIdentities[id.Key] = id.Value;
        foreach (var id in data.JobAccountIdentities) jobAccountIdentities[id.Key] = id.Value;
        foreach (var j in data.Jobs)
        {
            // A restarted host must retain automatic queue intent. Dispatch still
            // waits for a fresh authenticated Steam-idle observation and the same
            // connected account; user pauses/cancels and genuine failures stay put.
            var userPaused=j.State==StoreJobState.PausedByUser || j.State==StoreJobState.Pausing && j.ReasonCode is null or "user_pause" || j.State==StoreJobState.Interrupted && j.ReasonCode=="user_pause";
            var resume=j.State is StoreJobState.Queued or StoreJobState.BlockedSteam or StoreJobState.PausedBySteam or StoreJobState.Starting or StoreJobState.Downloading or StoreJobState.Installing or StoreJobState.Verifying
                || j.State==StoreJobState.Pausing && j.ReasonCode is "steam_priority" or "queue_preempt"
                || j.State==StoreJobState.Interrupted && j.ErrorCode is null && j.ReasonCode is "maintenance" or "host_shutdown" or "host_restarted" or "steam_priority" or "queue_preempt";
            var restored=Terminal(j.State)||j.State==StoreJobState.PausedByUser?j:userPaused
                ?j with{State=StoreJobState.PausedByUser,CanPause=false,CanResume=true,ReasonCode="user_pause",Revision=j.Revision+1}
                :resume?j with{State=StoreJobState.BlockedSteam,CanPause=false,CanResume=true,ReasonCode="host_restarted",Revision=j.Revision+1}
                :j with{State=StoreJobState.Interrupted,CanPause=false,CanResume=true,ReasonCode=j.ReasonCode??"host_restarted",Revision=j.Revision+1};
            jobs.Add(restored.State==StoreJobState.Completed?restored with{ProgressPercent=100}:restored);
        }
        foreach (var id in data.ClearedCompletedJobs ?? [])
            if (jobs.Any(j => j.JobId == id && j.State == StoreJobState.Completed)) clearedCompletedJobs.Add(id);
        foreach (var id in data.ClearedCanceledJobs ?? [])
            if (jobs.Any(j => j.JobId == id)) clearedCanceledJobs.Add(id);
        RestoreWindowsManagedReceipts(data.WindowsManagedInstalls ?? []);
        RestoreLegacyInstalls(data.LegacyInstalls ?? []);
        RestorePartialDownloads(data.PartialDownloads ?? []);
    }
    public async Task<bool> QuiesceAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try { quiescing = true; if (active != null) { stopReason ??= "maintenance"; RequestStopLocked(); } }
        finally { gate.Release(); }
        try
        {
            while (true)
            {
                await gate.WaitAsync(token);
                try { if (active == null && transfer == null && startup == null && providerOperations == 0) { Save(); return true; } }
                finally { gate.Release(); }
                await Task.Delay(20, token);
            }
        }
        catch (OperationCanceledException)
        {
            await gate.WaitAsync(); try { quiescing = false; } finally { gate.Release(); }
            return false;
        }
    }
    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync(); try { disposed = true; if (active != null) stopReason ??= "host_shutdown"; startup?.Cancel(); } finally { gate.Release(); }
        shutdown.Cancel();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        await WaitWindowsObservationsAsync(deadline.Token);
        if (startupTask != null) { try { await startupTask.WaitAsync(deadline.Token); } catch (OperationCanceledException) { } }
        IStoreTransfer? owned; await gate.WaitAsync(); try { owned = transfer; } finally { gate.Release(); }
        if (owned != null) { try { await owned.RequestCheckpointAsync(CancellationToken.None); await owned.Completion.WaitAsync(deadline.Token); } catch { } }
        await watchdog;
        // Process supervisor independently owns bounded drain; disposing the host
        // never relabels a live download paused or completed.
    }
}

// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
namespace Playhub.StoreLibrary;

public sealed partial class StoreQueueEngine
{
    internal const string WindowsManagedReason = "windows_store_managed";
    private sealed record WindowsManagedReceipt(Guid JobId, StoreGameKey Key, long AccountRevision, string PackageFamilyName, DateTimeOffset RequestedAt,
        DateTimeOffset? CliCompletedAt = null, int? CliExitCode = null, string? CliHResult = null, bool CliReportedProgress = false,string? LibraryKind=null, string? TargetVolume=null, string? TargetRoot=null,string? InstallProductId=null,bool PendingLaunch=false,bool FinalDeploymentConfirmed=false);
    internal static readonly TimeSpan WindowsManagedReadbackGrace = TimeSpan.FromSeconds(30);
    readonly Dictionary<Guid, WindowsManagedReceipt> windowsManagedReceipts = [];
    readonly object windowsObservationSync = new();
    readonly Dictionary<Guid, Task> windowsObservations = [];
    readonly Dictionary<Guid, CancellationTokenSource> windowsObservationStops = [];
    readonly HashSet<Guid> windowsActiveCli = [];
    readonly HashSet<Guid> windowsRestoringCli = [];
    Guid? windowsQueueActive;
    internal IWindowsStoreManagedRuntime WindowsManagedRuntime { get; set; } = new WindowsStoreManagedRuntime();
    internal Func<TimeSpan, CancellationToken, Task> WindowsManagedDelay { get; set; } = Task.Delay;

    internal async Task<StoreCommandResult> BeginWindowsManagedInstallAsync(StoreXboxAccountProvider provider, string productId,
        long accountRevision, string family, Guid requestId, CancellationToken token,string? libraryKind=null, string? volumeName=null,string? installProductId=null)
    {
        WindowsStoreInstallVolume? selected = null;
        if (WindowsManagedRuntime is IWindowsStoreInstallDestinationRuntime destinations)
        {
            if (!StoreWindowsInstallControl.ValidVolume(volumeName)) return Result(false, "windows_store_destination_unavailable");
            selected = (await destinations.GetInstallVolumesAsync(token)).SingleOrDefault(v => v.VolumeName.Equals(volumeName, StringComparison.OrdinalIgnoreCase));
            if (selected is null) return Result(false, "windows_store_destination_unavailable");
        }
        WindowsManagedReceipt receipt; StoreCommandResult result;
        await gate.WaitAsync(token);
        try
        {
            RequireRunning();
            var key = new StoreGameKey(StoreKind.Xbox, productId);
            if (requestId == Guid.Empty || !StoreXboxAccountCollections.ValidId(productId) || !WindowsXboxPackageCatalog.ValidFamily(family)
                || !ReferenceEquals(Provider(StoreKind.Xbox), provider) || !Connected(StoreKind.Xbox, accountRevision)
                || !games.TryGetValue(key, out var game) || game.AccountRevision != accountRevision || !(libraryKind=="gamePass"?provider.CurrentGamePassFamily(productId,family):provider.CurrentOwnedFamily(productId, family)) || !WindowsManagedRuntime.Available)
                return Result(false, "owned_library_changed_or_unavailable");
            if(libraryKind=="gamePass"&&!provider.CurrentGamePassRegisteredProduct(productId,family,installProductId))return Result(false,"owned_library_changed_or_unavailable");
            if(selected is not null&&game.DownloadBytes is {} required&&required>selected.FreeBytes)return Result(false,"insufficient_space");
            string fingerprint = "windows-store-install:" + productId + ":" + accountRevision + ":" + family+(libraryKind=="gamePass"?":gamePass":"") + ":" + volumeName + ":" + installProductId;
            if (acknowledged.TryGetValue(requestId, out var prior)) return prior.Fingerprint == fingerprint ? prior.Value : Result(false, "request_id_reused");
            var existing = jobs.LastOrDefault(j => j.Key == key && !clearedCanceledJobs.Contains(j.JobId) && j.ReasonCode == WindowsManagedReason && j.State is not (StoreJobState.Completed or StoreJobState.Failed or StoreJobState.Canceled));
            if (existing is not null) return Result(true, job: existing.JobId);
            lock (windowsObservationSync) if (jobs.Any(j => j.Key == key && windowsActiveCli.Contains(j.JobId))) return Result(false, "provider_busy");
            var id = Guid.NewGuid();
            receipt = new(id, key, accountRevision, family, now(),LibraryKind:libraryKind,TargetVolume:selected?.VolumeName,TargetRoot:selected?.RootPath,InstallProductId:installProductId,PendingLaunch:true);
            windowsManagedReceipts.Add(id, receipt);
            jobs.Add(new(id, key, game.Title, StoreJobState.Queued, 1, accountRevision, selected?.RootPath ?? "Microsoft Store", 0, null, null, now(), true, false, true, WindowsManagedReason,LibraryKind:game.LibraryKind));
            jobAccountIdentities[id] = accountIdentities[StoreKind.Xbox];
            Changed(); DispatchWindowsManagedLocked(); result = Result(true, job: id); Remember(requestId, fingerprint, result);
        }
        finally { gate.Release(); }
        return result;
    }
    // Windows owns deployment and licensing. Keep its execution slot separate
    // from the file-transfer worker, while all stores share the persistent list.
    void DispatchWindowsManagedLocked()
    {
        if (disposed || quiescing || windowsQueueActive is not null) return;
        lock (windowsObservationSync) if (windowsActiveCli.Count != 0 || windowsRestoringCli.Count != 0) return;
        int index = jobs.FindIndex(j => j.State == StoreJobState.Queued && !clearedCanceledJobs.Contains(j.JobId)
            && windowsManagedReceipts.TryGetValue(j.JobId, out var r) && r.PendingLaunch && Connected(j.Key.Store, j.AccountRevision)
            && jobAccountIdentities.GetValueOrDefault(j.JobId) == accountIdentities.GetValueOrDefault(j.Key.Store));
        if (index < 0) return;
        var job = jobs[index]; var receipt = windowsManagedReceipts[job.JobId] with { PendingLaunch = false };
        windowsManagedReceipts[job.JobId] = receipt; windowsQueueActive = job.JobId;
        jobs[index] = job with { State = StoreJobState.Starting, CanPause = false, CanCancel = true, Revision = job.Revision + 1, UpdatedAt = now() };
        Changed(); StartWindowsObservation(receipt, launch: true);
    }
    async Task ReleaseWindowsQueueAsync(Guid id)
    {
        await gate.WaitAsync();
        try { if (windowsQueueActive == id) windowsQueueActive = null; DispatchWindowsManagedLocked(); }
        finally { gate.Release(); }
    }
    void StartWindowsObservation(WindowsManagedReceipt receipt, bool launch)
    {
        lock (windowsObservationSync)
        {
            if (windowsObservations.ContainsKey(receipt.JobId)) return;
            var stop = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
            windowsObservationStops.Add(receipt.JobId, stop);
            windowsObservations.Add(receipt.JobId, Task.Run(async () =>
            {
                try { await ObserveWindowsInstallAsync(receipt, launch, stop.Token); }
                finally
                {
                    // Keep the completed-task entry: duplicate requests and
                    // restart observation semantics remain unchanged. Release
                    // only this observation's exact cancellation resource.
                    lock (windowsObservationSync)
                        if (windowsObservationStops.TryGetValue(receipt.JobId, out var current) && ReferenceEquals(current, stop))
                            windowsObservationStops.Remove(receipt.JobId);
                    stop.Dispose();
                    lock (windowsObservationSync) windowsRestoringCli.Remove(receipt.JobId);
                    await ReleaseWindowsQueueAsync(receipt.JobId);
                }
            }));
        }
    }
    void ResumeWindowsManagedObservations()
    {
        foreach (var receipt in windowsManagedReceipts.Values)
            if (!receipt.PendingLaunch && jobs.Any(j => j.JobId == receipt.JobId && j.State is not (StoreJobState.Completed or StoreJobState.Failed or StoreJobState.Canceled)))
            {
                // Reserve synchronously: FindPackage may await before the
                // restored observer attaches to the existing Windows operation.
                if (WindowsManagedRuntime is IWindowsStoreInstallDestinationRuntime)
                { windowsQueueActive ??= receipt.JobId; lock (windowsObservationSync) windowsRestoringCli.Add(receipt.JobId); }
                StartWindowsObservation(receipt, launch: false);
            }
        _ = ResumeWindowsQueueAsync();
    }
    async Task ResumeWindowsQueueAsync() { await gate.WaitAsync(); try { DispatchWindowsManagedLocked(); } finally { gate.Release(); } }
    void RestoreWindowsManagedReceipts(IEnumerable<WindowsManagedReceipt> receipts)
    {
        foreach (var receipt in receipts)
        {
            var job = jobs.SingleOrDefault(j => j.JobId == receipt.JobId);
            if (receipt.JobId == Guid.Empty || receipt.Key.Store != StoreKind.Xbox || !StoreXboxAccountCollections.ValidId(receipt.Key.Id)
                || !WindowsXboxPackageCatalog.ValidFamily(receipt.PackageFamilyName) || receipt.AccountRevision < 1
                || receipt.RequestedAt > now().AddMinutes(5) || job is null || job.Key != receipt.Key || job.AccountRevision != receipt.AccountRevision
                || !jobAccountIdentities.ContainsKey(receipt.JobId) || windowsManagedReceipts.ContainsKey(receipt.JobId)||receipt.LibraryKind is not(null or "store" or "gamePass")
                || receipt.CliCompletedAt > now().AddMinutes(5) || receipt.CliCompletedAt < receipt.RequestedAt
                || receipt.CliHResult is not null && !ValidWindowsHResult(receipt.CliHResult)
                || receipt.InstallProductId is not null && !StoreXboxAccountCollections.ValidId(receipt.InstallProductId)
                || receipt.TargetVolume is not null && (!StoreWindowsInstallControl.ValidVolume(receipt.TargetVolume) || receipt.TargetRoot is not {Length:3} || !char.IsAsciiLetter(receipt.TargetRoot[0]) || receipt.TargetRoot[1..] != @":\")
                || receipt.CliCompletedAt is null && (receipt.CliExitCode is not null || receipt.CliHResult is not null || receipt.CliReportedProgress)) throw new StoreOperationException("queue_invalid");
            windowsManagedReceipts.Add(receipt.JobId, receipt);
            int index = jobs.FindIndex(j => j.JobId == receipt.JobId);
            bool incomplete=(KnownIncompleteWindowsInstall(job,receipt)||job.State==StoreJobState.Failed&&receipt.CliExitCode==3)&&!clearedCanceledJobs.Contains(job.JobId)&&!clearedCompletedJobs.Contains(job.JobId);
            jobs[index] = job with { State = incomplete?StoreJobState.ExternalHandoff:Terminal(job.State) ? job.State : receipt.PendingLaunch ? job.State == StoreJobState.PausedByUser ? StoreJobState.PausedByUser : StoreJobState.Queued : StoreJobState.ExternalHandoff,
                ProgressPercent=incomplete&&job.TotalBytes is >0?100d*job.DownloadedBytes/job.TotalBytes.Value:job.ProgressPercent,
                ReasonCode = WindowsManagedReason, CanPause = receipt.PendingLaunch && job.State != StoreJobState.PausedByUser, CanResume = receipt.PendingLaunch && job.State == StoreJobState.PausedByUser, CanCancel = receipt.PendingLaunch, CanDownloadNow = false };
        }
    }
    async Task ObserveWindowsInstallAsync(WindowsManagedReceipt receipt, bool launch, CancellationToken observation)
    {
        Task<int?>? process = null;
        try
        {
            var initial = await WindowsManagedRuntime.FindPackageAsync(receipt.PackageFamilyName, observation);
            if (Verified(initial, receipt)&&await WindowsDeploymentCompleteAsync(receipt)) { await CompleteWindowsInstallAsync(receipt, initial!); return; }
            if (receipt.CliExitCode is not null and not 0 && !(receipt.CliExitCode==3&&WindowsManagedRuntime is IWindowsStoreInstallDestinationRuntime))
            { await SetWindowsObservedStateAsync(receipt.JobId, StoreJobState.Failed, "windows_store_install_failed"); return; }
            if (launch || WindowsManagedRuntime is IWindowsStoreInstallDestinationRuntime)
            {
                lock (windowsObservationSync) { windowsActiveCli.Add(receipt.JobId); windowsRestoringCli.Remove(receipt.JobId); }
                process = RunObservedWindowsCliAsync(receipt, observation, launch);
            }
            else await SetWindowsObservedStateAsync(receipt.JobId, StoreJobState.ExternalHandoff, preserveError: true);
            bool unconfirmed = false;
            while (now() - receipt.RequestedAt < TimeSpan.FromHours(24))
            {
                observation.ThrowIfCancellationRequested();
                var package = await WindowsManagedRuntime.FindPackageAsync(receipt.PackageFamilyName, observation);
                if (Verified(package, receipt)&&await WindowsDeploymentCompleteAsync(receipt)) { await CompleteWindowsInstallAsync(receipt, package!); return; }
                if (process?.IsCompleted == true)
                {
                    int? exit = null;
                    try { exit = await process; }
                    catch (StoreOperationException error)
                    { await SetWindowsObservedStateAsync(receipt.JobId, StoreJobState.Failed, error.Code); return; }
                    catch (Exception error) when (error is not OutOfMemoryException) { }
                    var diagnostic = (WindowsManagedRuntime as IWindowsStoreManagedOutcomeSource)?.TakeOutcome(receipt.Key.Id);
                    receipt = await RememberWindowsOutcomeAsync(receipt, exit, diagnostic);
                    if(receipt.CliExitCode==0&&(receipt.FinalDeploymentConfirmed||exit==3))exit=0;
                    process = null;
                    if (exit is not null and not 0)
                    { await SetWindowsObservedStateAsync(receipt.JobId, exit == 5 ? StoreJobState.Canceled : StoreJobState.Failed, diagnostic?.ErrorCode ?? "windows_store_install_failed"); return; }
                    // A zero/unknown CLI exit is not OS installation proof.
                    // Give registration a short grace, then expose uncertainty
                    // while continuing the exact-PFN observation, without replay.
                    await SetWindowsObservedStateAsync(receipt.JobId, StoreJobState.ExternalHandoff);
                }
                if (process is null && !unconfirmed && now() - (receipt.CliCompletedAt ?? receipt.RequestedAt) >= WindowsManagedReadbackGrace)
                {
                    unconfirmed = true;
                    await SetWindowsObservedStateAsync(receipt.JobId, StoreJobState.ExternalHandoff, "windows_store_install_unconfirmed");
                }
                if(process is null)await WindowsManagedDelay(TimeSpan.FromSeconds(2),observation);
                else
                {
                    // CLI completion is itself a new observation. Wake for it
                    // immediately and retire the unused poll wait, so no delayed
                    // waiter can consume a later package-registration signal.
                    using var pollStop=CancellationTokenSource.CreateLinkedTokenSource(observation);
                    var poll=WindowsManagedDelay(TimeSpan.FromSeconds(2),pollStop.Token);
                    await Task.WhenAny(process,poll);
                    if(!poll.IsCompleted)pollStop.Cancel();
                    try{await poll;}catch(OperationCanceledException)when(!observation.IsCancellationRequested){ }
                }
            }
            await SetWindowsObservedStateAsync(receipt.JobId, StoreJobState.ExternalHandoff, "windows_store_install_unconfirmed");
        }
        catch (OperationCanceledException) when (observation.IsCancellationRequested)
        { await SetWindowsObservedStateAsync(receipt.JobId, StoreJobState.ExternalHandoff, preserveError: true); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { await SetWindowsObservedStateAsync(receipt.JobId, StoreJobState.ExternalHandoff, "windows_store_install_unconfirmed"); }
    }
    async Task<int?> RunObservedWindowsCliAsync(WindowsManagedReceipt receipt, CancellationToken observation, bool launch)
    {
        try
        {
            if (WindowsManagedRuntime is IWindowsStoreInstallDestinationRuntime)
            {
                await gate.WaitAsync(observation);
                try { if (clearedCanceledJobs.Contains(receipt.JobId)) return 5; }
                finally { gate.Release(); }
            }
            return WindowsManagedRuntime is IWindowsStoreInstallDestinationRuntime destinations
                ? await destinations.RunOnVolumeAsync(receipt.Key.Id, receipt.PackageFamilyName, receipt.TargetVolume, launch, p => UpdateWindowsProgressAsync(receipt.JobId, p), observation)
                : await WindowsManagedRuntime.RunInstallAsync(receipt.Key.Id, p => UpdateWindowsProgressAsync(receipt.JobId, p), observation);
        }
        finally { lock (windowsObservationSync) windowsActiveCli.Remove(receipt.JobId); _ = ReleaseWindowsQueueAsync(receipt.JobId); }
    }
    static bool Verified(WindowsXboxPackage? package, WindowsManagedReceipt receipt) => package?.StatusOk == true && !package.IsPlaceholder
        && (receipt.LibraryKind!="gamePass"||package.StoreProductId==(receipt.InstallProductId??receipt.Key.Id))
        && string.Equals(package.PackageFamilyName, receipt.PackageFamilyName, StringComparison.OrdinalIgnoreCase)
        && Path.IsPathFullyQualified(package.InstallLocation) && Directory.Exists(package.InstallLocation) && MatchesWindowsDestination(package.InstallLocation, receipt.TargetRoot);
    static bool KnownIncompleteWindowsInstall(StoreDownloadJob job,WindowsManagedReceipt receipt)=>!receipt.FinalDeploymentConfirmed&&receipt.TargetVolume is not null&&receipt.CliExitCode is not 5&&job.State==StoreJobState.Completed&&job.TotalBytes is >0&&job.DownloadedBytes<job.TotalBytes;
    async Task<bool> WindowsDeploymentCompleteAsync(WindowsManagedReceipt receipt)
    {
        if(WindowsManagedRuntime is not IWindowsStoreInstallDestinationRuntime)return true;
        await gate.WaitAsync();try{return receipt.CliCompletedAt is not null&&receipt.CliExitCode==0&&jobs.Any(j=>j.JobId==receipt.JobId&&(receipt.FinalDeploymentConfirmed||j.TotalBytes is not >0||j.DownloadedBytes>=j.TotalBytes));}finally{gate.Release();}
    }
    void ReconcileWindowsInstallInventoryLocked()
    {
        // Only the newest current-account attempt can qualify present inventory.
        // Old hidden partial receipts must never veto a later full deployment.
        var current=windowsManagedReceipts.Values.Where(r=>r.TargetVolume is not null&&Connected(r.Key.Store,r.AccountRevision)
            &&jobs.Any(j=>j.JobId==r.JobId&&j.AccountRevision==r.AccountRevision&&j.State is not(StoreJobState.Canceled or StoreJobState.Failed))
            &&!clearedCanceledJobs.Contains(r.JobId)&&jobAccountIdentities.GetValueOrDefault(r.JobId)==accountIdentities.GetValueOrDefault(r.Key.Store))
            .GroupBy(r=>r.Key).Select(group=>group.OrderByDescending(r=>r.RequestedAt).First());
        foreach(var receipt in current)
        {
            var job=jobs.Single(j=>j.JobId==receipt.JobId);
            if((!WindowsManagedTerminal(job)||KnownIncompleteWindowsInstall(job,receipt))&&games.TryGetValue(job.Key,out var game)&&game.AccountRevision==receipt.AccountRevision)
                games[job.Key]=game with{IsInstalled=false,IsLaunchable=false};
        }
    }
    static bool MatchesWindowsDestination(string path, string? root)
    {
        if (root is null) return true;
        try { var directory = new DirectoryInfo(path); var physical = directory.ResolveLinkTarget(true)?.FullName ?? directory.FullName;
            return string.Equals(Path.GetPathRoot(physical), root, StringComparison.OrdinalIgnoreCase); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    static bool ValidWindowsHResult(string value) => System.Text.RegularExpressions.Regex.IsMatch(value, "^0x[89A-F][0-9A-F]{7}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    async Task<WindowsManagedReceipt> RememberWindowsOutcomeAsync(WindowsManagedReceipt receipt, int? exit, WindowsStoreManagedOutcome? outcome)
    {
        await gate.WaitAsync();
        try
        {
            if (!windowsManagedReceipts.TryGetValue(receipt.JobId, out var current) || current.Key != receipt.Key || current.AccountRevision != receipt.AccountRevision)
                return receipt;
            if(current.CliExitCode==0&&(current.FinalDeploymentConfirmed||exit==3))return current;
            var updated = current with { CliCompletedAt = now(), CliExitCode = exit,
                CliHResult = outcome?.HResult is { } result && ValidWindowsHResult(result) ? result : null,
                CliReportedProgress = outcome?.ReportedProgress == true };
            windowsManagedReceipts[receipt.JobId] = updated; Changed(); return updated;
        }
        finally { gate.Release(); }
    }

    async Task UpdateWindowsProgressAsync(Guid id, WindowsStoreManagedProgress progress)
    {
        await gate.WaitAsync();
        try
        {
            int index = jobs.FindIndex(j => j.JobId == id);
            if (index < 0 || WindowsManagedTerminal(jobs[index]) || !windowsManagedReceipts.ContainsKey(id)) return;
            var job = jobs[index];
            if(progress.FinalDeploymentConfirmed)windowsManagedReceipts[id]=windowsManagedReceipts[id] with{FinalDeploymentConfirmed=true};
            var state = progress.Phase == "starting" ? StoreJobState.Starting : progress.Phase == "paused" ? StoreJobState.PausedByUser : progress.Phase == "verifying" ? StoreJobState.Verifying : progress.Phase == "downloading" ? StoreJobState.Downloading : StoreJobState.Installing;
            jobs[index] = job with { State = state, ProgressPercent = progress.Percent, DownloadedBytes = progress.FinalDeploymentConfirmed?progress.DownloadedBytes??job.DownloadedBytes:Math.Max(job.DownloadedBytes,progress.DownloadedBytes ?? 0),
                TotalBytes = progress.FinalDeploymentConfirmed?progress.TotalBytes:progress.TotalBytes is {} reportedTotal?Math.Max(job.TotalBytes??0,reportedTotal):job.TotalBytes, BytesPerSecond = progress.BytesPerSecond, Revision = job.Revision + 1, UpdatedAt = now(), ErrorCode = null };
            Changed();
        }
        finally { gate.Release(); }
    }
    async Task SetWindowsObservedStateAsync(Guid id, StoreJobState state, string? error = null, bool preserveError = false)
    {
        await gate.WaitAsync();
        try
        {
            int index = jobs.FindIndex(j => j.JobId == id);
            if (index < 0 || WindowsManagedTerminal(jobs[index])) return;
            error = preserveError ? jobs[index].ErrorCode : error;
            if (jobs[index].State == state && jobs[index].ErrorCode == error) return;
            jobs[index] = jobs[index] with { State = state, ErrorCode = error, Revision = jobs[index].Revision + 1, UpdatedAt = now() };
            Changed();
        }
        finally { gate.Release(); }
    }
    static bool WindowsManagedTerminal(StoreDownloadJob job) => job.State is StoreJobState.Completed or StoreJobState.Canceled or StoreJobState.Failed
        || job.State == StoreJobState.ExternalHandoff && job.ReasonCode != WindowsManagedReason;
    async Task CompleteWindowsInstallAsync(WindowsManagedReceipt receipt, WindowsXboxPackage package,bool repairCompleted=false,CancellationToken? repairToken=null)
    {
        var provider = (StoreXboxAccountProvider)Provider(StoreKind.Xbox);
        var launch = await provider.ResolveLaunchAsync(receipt.Key.Id, package.InstallLocation, repairToken??shutdown.Token);
        await gate.WaitAsync(repairToken??CancellationToken.None);
        try
        {
            int index = jobs.FindIndex(j => j.JobId == receipt.JobId);
            if (index < 0 || WindowsManagedTerminal(jobs[index])&&!(repairCompleted&&jobs[index].State==StoreJobState.Completed)) return;
            bool alreadyComplete=jobs[index].State==StoreJobState.Completed,changed=!alreadyComplete;
            if(!alreadyComplete)jobs[index] = jobs[index] with { State = StoreJobState.Completed, InstallPath = package.InstallLocation, ProgressPercent = 100,
                ErrorCode = null, Revision = jobs[index].Revision + 1, UpdatedAt = now() };
            if (Connected(StoreKind.Xbox, receipt.AccountRevision) && jobAccountIdentities.TryGetValue(receipt.JobId, out var heldIdentity)
                && accountIdentities.GetValueOrDefault(StoreKind.Xbox) == heldIdentity && provider.CurrentInstallFamily(receipt.Key.Id, receipt.PackageFamilyName))
            {
                games.TryGetValue(receipt.Key,out var game);
                if(game is null&&provider.RetainedInstallMetadata(receipt.Key.Id,receipt.PackageFamilyName) is {} metadata)
                    game=new(receipt.Key,metadata.Title,receipt.AccountRevision,provider.Capabilities,false,false,ArtworkKey:"xbox:"+receipt.Key.Id,DownloadBytes:metadata.RequiredBytes,
                        ArtworkUrl:metadata.ArtworkUrl,ArtworkFallbackUrls:metadata.ArtworkFallbackUrls,ArtworkKind:metadata.ArtworkUrl is null?null:"cover",LibraryKind:jobs[index].LibraryKind??receipt.LibraryKind,InstallProvider:metadata.InstallProvider);
                if(game?.AccountRevision==receipt.AccountRevision)
                {
                    var installed=game with{IsInstalled=true,IsLaunchable=launch is not null,InstallPath=package.InstallLocation,UnavailableReason=game.UnavailableReason=="uninstalled"?null:game.UnavailableReason};
                    if(!games.TryGetValue(receipt.Key,out var current)||current!=installed){games[receipt.Key]=installed;changed=true;}
                }
            }
            if(changed)Changed();
        }
        finally { gate.Release(); }
    }
    async Task RepairCompletedWindowsGamesAsync(CancellationToken token)
    {
        WindowsManagedReceipt[] candidates;
        await gate.WaitAsync(token);
        try{candidates=windowsManagedReceipts.Values.Where(r=>Connected(StoreKind.Xbox,r.AccountRevision)
            &&jobAccountIdentities.GetValueOrDefault(r.JobId)==accountIdentities.GetValueOrDefault(StoreKind.Xbox)
            &&jobs.Any(j=>j.JobId==r.JobId&&j.State==StoreJobState.Completed)
            &&(!games.TryGetValue(r.Key,out var g)||!g.IsInstalled)).ToArray();}
        finally{gate.Release();}
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(token);budget.CancelAfter(TimeSpan.FromSeconds(15));
        foreach(var receipt in candidates)
        {
            token.ThrowIfCancellationRequested();
            if(budget.IsCancellationRequested)break;
            using var candidate=CancellationTokenSource.CreateLinkedTokenSource(budget.Token);candidate.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                var package=await WindowsManagedRuntime.FindPackageAsync(receipt.PackageFamilyName,candidate.Token).WaitAsync(candidate.Token);
                if(Verified(package,receipt)&&await WindowsDeploymentCompleteAsync(receipt))await CompleteWindowsInstallAsync(receipt,package!,repairCompleted:true,repairToken:candidate.Token).WaitAsync(candidate.Token);
            }
            catch(OperationCanceledException)when(!token.IsCancellationRequested){ }
            catch(Exception error)when(error is not OutOfMemoryException and not OperationCanceledException){ }
        }
    }
    async Task WaitWindowsObservationsAsync(CancellationToken token)
    {
        Task[] pending; lock (windowsObservationSync) pending = windowsObservations.Values.ToArray();
        try { await Task.WhenAll(pending).WaitAsync(token); } catch (OperationCanceledException) { }
    }
}

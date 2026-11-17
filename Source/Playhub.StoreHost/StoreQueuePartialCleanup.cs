// SPDX-License-Identifier: MIT. Original Playhub code, Copyright (c) 2026 LoZazaMastro.
namespace Playhub.StoreLibrary;

public sealed partial class StoreQueueEngine
{
    readonly Dictionary<Guid, StorePartialReceipt> partialDownloads = [];
    readonly Dictionary<Guid, Task> partialCleanupTasks = [];
    readonly HashSet<Guid> partialCleanupSlots = [];
    StorePartialReceipt? ReusableDownloadRoot(StoreGameKey key, string path) => partialDownloads.Values.LastOrDefault(r => r.Key == key && r.CleanupFinished &&
        StringComparer.OrdinalIgnoreCase.Equals(r.InstallPath, path) && accountIdentities.TryGetValue(key.Store, out var identity) && identity == r.AccountIdentity && StoreOwnedPartialFiles.CanReuse(r));
    StorePartialReceipt? ReclaimDownloadRoot(StoreDownloadJob next) => ReusableDownloadRoot(next.Key, next.InstallPath) is { } prior &&
        accountIdentities.TryGetValue(next.Key.Store, out var identity) ? StoreOwnedPartialFiles.Reclaim(prior, next, identity) : null;
    bool LegacyRequestRemovable(StoreDownloadJob job) => job.State is StoreJobState.Failed or StoreJobState.ExternalHandoff &&
        !partialDownloads.ContainsKey(job.JobId) && active != job.JobId && !drainUnconfirmed && !InstalledOperationBusy(job.Key.Store) &&
        !(job.Key.Store == StoreKind.Xbox && windowsManagedReceipts.ContainsKey(job.JobId)) &&
        !jobs.Any(j => j.JobId == active && j.Key.Store == job.Key.Store) &&
        job.ReasonCode != "worker_drain_unconfirmed" && job.ErrorCode != "worker_drain_unconfirmed" &&
        job.ErrorCode != "partial_cleanup_drain_unconfirmed" &&
        (Provider(job.Key.Store) is not IStorePartialDownloadSource source || !source.HasOperationLease);
    void HideLegacyRequestLocked(int index)
    {
        var job = jobs[index]; var previousRevision = revision;
        bool added = clearedCanceledJobs.Add(job.JobId);
        Set(index, StoreJobState.Canceled, "download_request_dismissed");
        try { Changed(); }
        catch { jobs[index] = job; revision = previousRevision; if (added) clearedCanceledJobs.Remove(job.JobId); throw; }
    }

    void RetainFreshDownloadRoot(StoreDownloadJob job)
    {
        if (jobAccountIdentities.TryGetValue(job.JobId, out var identity) &&
            StoreOwnedPartialFiles.Capture(job, identity) is { } receipt) partialDownloads.Add(job.JobId, receipt);
    }
    async Task PreparePartialWorkerAsync(StoreDownloadJob job, CancellationToken token)
    {
        StorePartialReceipt? receipt;
        await gate.WaitAsync(token);
        try { partialDownloads.TryGetValue(job.JobId, out receipt); }
        finally { gate.Release(); }
        if (receipt is null) return; // Legacy jobs gain no speculative file ownership.
        var manifest = receipt.Manifest;
        if (manifest is null && Provider(job.Key.Store) is IStorePartialDownloadSource source)
        {
            manifest = source is IStorePartialLanguageDownloadSource languages ? await languages.ReadLanguagePartialManifestAsync(job.Key.Id,job.Language,token) : await source.ReadPartialManifestAsync(job.Key.Id, token);
            StoreOwnedPartialFiles.ValidateManifest(manifest, receipt.InstallPath);
        }
        StoreOwnedPartialFiles.AssertCurrentRoot(receipt);
        await gate.WaitAsync(token);
        try
        {
            if (active != job.JobId || PartialCleanupRequested(job.JobId)) throw new OperationCanceledException(token);
            var current = jobs.Single(j => j.JobId == job.JobId);
            if (CleanupContextError(current, receipt, requireDrain: false) is { } error) throw new StoreOperationException(error);
            partialDownloads[job.JobId] = receipt with { Manifest = manifest, WorkerStarted = true, DrainConfirmed = false };
            Changed();
        }
        finally { gate.Release(); }
    }
    bool PartialCleanupRequested(Guid id) => partialDownloads.TryGetValue(id, out var receipt) && receipt.CleanupRequestId is not null && !receipt.CleanupFinished;

    string? DownloadRemovalKind(StoreDownloadJob job)
    {
        if (disposed || quiescing || job.State is StoreJobState.Completed or StoreJobState.Canceled or StoreJobState.Canceling ||
            partialCleanupTasks.TryGetValue(job.JobId, out var cleanup) && !cleanup.IsCompleted) return null;
        if (WindowsRequestRemovable(job)) return "windowsRequest";
        if (LegacyRequestRemovable(job)) return "history";
        if (!partialDownloads.TryGetValue(job.JobId, out var receipt) || receipt.CleanupFinished ||
            CleanupContextError(job, receipt, requireDrain: active != job.JobId) != null ||
            active == job.JobId && drainUnconfirmed) return null;
        return !receipt.WorkerStarted || receipt.Manifest != null ? "cleanup" : null;
    }
    string? CleanupContextError(StoreDownloadJob job, StorePartialReceipt receipt, bool requireDrain)
    {
        if (job.Key.Store == StoreKind.Xbox || receipt.Version != 1 || receipt.JobId != job.JobId || receipt.Key != job.Key ||
            receipt.AccountRevision != job.AccountRevision || !StringComparer.OrdinalIgnoreCase.Equals(receipt.InstallPath, job.InstallPath) ||
            !jobAccountIdentities.TryGetValue(job.JobId, out var identity) || identity != receipt.AccountIdentity ||
            !accountIdentities.TryGetValue(job.Key.Store, out var current) || current != receipt.AccountIdentity) return "partial_cleanup_ownership_lost";
        if (job.State == StoreJobState.Completed || games.Values.Any(g => g.IsInstalled && g.InstallPath != null && Overlap(g.InstallPath, job.InstallPath))) return "partial_cleanup_installed_game";
        if (jobs.Any(other => other.JobId != job.JobId && Overlap(other.InstallPath, job.InstallPath) &&
            !HistoricalDestinationReference(other, job))) return "partial_cleanup_shared_destination";
        if (requireDrain && receipt.WorkerStarted && !receipt.DrainConfirmed) return "partial_cleanup_drain_unconfirmed";
        return null;
    }
    bool HistoricalDestinationReference(StoreDownloadJob other, StoreDownloadJob current)
    {
        // A job-history path is not filesystem ownership. The current receipt
        // exists only after a fresh empty-root claim (or verified clean-root
        // reclaim); actual marker/object identity is rechecked before Start and
        // before any cleanup. A moved/deleted old generation cannot block it.
        if (!Terminal(other.State) || other.JobId == active ||
            other.ReasonCode is "worker_drain_unconfirmed" or "partial_cleanup_drain_unconfirmed" ||
            other.ErrorCode is "worker_drain_unconfirmed" or "partial_cleanup_drain_unconfirmed" ||
            partialDownloads.TryGetValue(other.JobId, out var old) && old.WorkerStarted && !old.DrainConfirmed)
            return false;
        try
        {
            return StringComparer.OrdinalIgnoreCase.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(other.InstallPath)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(current.InstallPath)));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }
    static bool Overlap(string first, string second)
    {
        try
        {
            var a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(first));
            var b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(second));
            return a.Equals(b, StringComparison.OrdinalIgnoreCase) || a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || b.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return true; }
    }
    public async Task<StoreCommandResult> CancelCleanupAsync(StoreCancelCleanupRequest request, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            RequireRunning();
            if (request.RequestId == Guid.Empty || request.JobId == Guid.Empty) return Result(false, "invalid_request");
            string fingerprint = "cancel-cleanup:" + System.Text.Json.JsonSerializer.Serialize(request);
            if (acknowledged.TryGetValue(request.RequestId, out var prior)) return prior.Fingerprint == fingerprint ? Result(prior.Value.Accepted, prior.Value.ErrorCode, request.JobId) : Result(false, "request_id_reused");
            var receiptByRequest = partialDownloads.Values.FirstOrDefault(r => r.CleanupRequestId == request.RequestId || r.CleanupAttempts?.Any(a => a.RequestId == request.RequestId) == true);
            if (receiptByRequest != null)
                return receiptByRequest.JobId == request.JobId && jobs.Find(j=>j.JobId==request.JobId) is {} retained&&MatchesDownloadScope(retained,request.ExpectedKey,request.ExpectedAccountRevision,request.ExpectedInstallPath) && (receiptByRequest.CleanupRequestId == request.RequestId && receiptByRequest.CleanupExpectedRevision == request.ExpectedRevision || receiptByRequest.CleanupAttempts?.Any(a => a.RequestId == request.RequestId && a.ExpectedRevision == request.ExpectedRevision) == true)
                    ? Result(true, job: request.JobId) : Result(false, "request_id_reused");
            int index = jobs.FindIndex(j => j.JobId == request.JobId);
            if (index < 0) return Result(false, "job_not_found");
            var job = jobs[index];
            if(DownloadSelectionError(job,request.ExpectedRevision,request.ExpectedKey,request.ExpectedAccountRevision,request.ExpectedInstallPath) is {} selectionError)return Result(false,selectionError);
            if (WindowsRequestRemovable(job))
            {
                HideWindowsRequestLocked(index);
                var dismissed = Result(true, job: job.JobId); Remember(request.RequestId, fingerprint, dismissed); return dismissed;
            }
            if (LegacyRequestRemovable(job))
            {
                HideLegacyRequestLocked(index);
                var dismissed = Result(true, job: job.JobId); Remember(request.RequestId, fingerprint, dismissed); return dismissed;
            }
            if (clearedCanceledJobs.Contains(job.JobId)) return Result(true, job: job.JobId);
            // Visibility is independent of file ownership and worker drainage.
            // Retain all internal leases and receipts while the row stays dismissed.
            if (!request.DeletePartial || job.State is StoreJobState.Completed or StoreJobState.Canceled ||
                !partialDownloads.TryGetValue(job.JobId, out var receipt) || receipt.CleanupFinished ||
                partialCleanupTasks.TryGetValue(job.JobId, out var task) && !task.IsCompleted ||
                CleanupContextError(job, receipt, requireDrain: active != job.JobId) != null ||
                active == job.JobId && drainUnconfirmed ||
                receipt.WorkerStarted && receipt.Manifest is null)
            {
                var before = revision;
                clearedCanceledJobs.Add(job.JobId);
                if (active == job.JobId) Set(index, StoreJobState.Canceling, "user_cancel");
                else if (!Terminal(job.State) && job.State != StoreJobState.ExternalHandoff) Set(index, StoreJobState.Canceled, "download_request_dismissed");
                try { Changed(); }
                catch { jobs[index] = job; revision = before; clearedCanceledJobs.Remove(job.JobId); throw; }
                if(job.Key.Store==StoreKind.Xbox && !Terminal(job.State) && windowsManagedReceipts.ContainsKey(job.JobId) && WindowsManagedRuntime is IWindowsStoreInstallDestinationRuntime native)
                {
                    _ = Task.Run(async()=> { try { await native.CancelInstallAsync(job.Key.Id,shutdown.Token); } catch(Exception error) when(error is not OutOfMemoryException) { } });
                }
                if (active == job.JobId) { stopReason = "user_cancel"; RequestStopLocked(); }
                DispatchWindowsManagedLocked();
                var dismissed = Result(true, job: job.JobId); Remember(request.RequestId, fingerprint, dismissed); return dismissed;
            }
            var previousRevision = revision;
            var attempts = receipt.CleanupAttempts?.ToList() ?? [];
            if (attempts.Count >= 1024) return Result(false, "partial_cleanup_request_history_full");
            attempts.Add(new(request.RequestId, request.ExpectedRevision));
            partialDownloads[job.JobId] = receipt with { CleanupRequestId = request.RequestId, CleanupExpectedRevision = request.ExpectedRevision, CleanupFinished = false, CleanupAttempts = attempts.ToArray() };
            Set(index, StoreJobState.Canceling, "partial_cleanup");
            jobs[index] = jobs[index] with { CanPause = false, CanResume = false, CanCancel = false, CanDownloadNow = false, CanRemove = false };
            clearedCanceledJobs.Add(job.JobId);
            try { Changed(); }
            catch { jobs[index] = job; partialDownloads[job.JobId] = receipt; revision = previousRevision; clearedCanceledJobs.Remove(job.JobId); throw; }
            if (active == job.JobId) { stopReason = "partial_cleanup"; RequestStopLocked(); }
            else SchedulePartialCleanupLocked(job.JobId);
            var accepted = Result(true, job: job.JobId); Remember(request.RequestId, fingerprint, accepted); return accepted;
        }
        finally { gate.Release(); }
    }
    void SchedulePartialCleanupLocked(Guid id)
    {
        if (partialCleanupTasks.TryGetValue(id, out var prior) && !prior.IsCompleted) return;
        providerOperations++;
        partialCleanupSlots.Add(id);
        partialCleanupTasks[id] = Task.Run(() => CompletePartialCleanupAsync(id));
    }
    sealed class CleanupCommit(SemaphoreSlim gate) : IDisposable { public void Dispose() => gate.Release(); }
    IDisposable EnterCleanupCommit(Guid id, StorePartialReceipt receipt)
    {
        gate.Wait();
        try
        {
            var job = jobs.Single(j => j.JobId == id);
            if (!partialDownloads.TryGetValue(id, out var current) || current != receipt || !PartialCleanupRequested(id) ||
                job.State != StoreJobState.Canceling || disposed) throw new StoreOperationException("partial_cleanup_ownership_lost");
            if (CleanupContextError(job, receipt, requireDrain: true) is { } error) throw new StoreOperationException(error);
            return new CleanupCommit(gate);
        }
        catch { gate.Release(); throw; }
    }
    async Task CompletePartialCleanupAsync(Guid id)
    {
        string? failure = null; StorePartialReceipt? receipt = null; StoreDownloadJob? job = null;
        try
        {
            await gate.WaitAsync();
            try
            {
                job = jobs.Single(j => j.JobId == id); receipt = partialDownloads[id];
                if (CleanupContextError(job, receipt, true) is { } error) throw new StoreOperationException(error);
            }
            finally { gate.Release(); }
            if (await Provider(job.Key.Store).ResolveLaunchAsync(job.Key.Id, job.InstallPath, CancellationToken.None) != null)
                throw new StoreOperationException("partial_cleanup_installed_game");
            StoreOwnedPartialFiles.Delete(receipt, CancellationToken.None, () => EnterCleanupCommit(id, receipt));
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            failure = error is StoreOperationException operation && operation.Code.StartsWith("partial_cleanup_", StringComparison.Ordinal)
                ? operation.Code : error is OperationCanceledException ? "partial_cleanup_interrupted" : "partial_cleanup_file_busy";
        }
        await gate.WaitAsync();
        try
        {
            int index = jobs.FindIndex(j => j.JobId == id);
            if (index >= 0 && jobs[index].State != StoreJobState.Completed)
            {
                if (failure is null && receipt != null)
                {
                    Set(index, StoreJobState.Canceled, "partial_cleanup_complete");
                    clearedCanceledJobs.Add(id);
                    partialDownloads[id] = receipt with { CleanupFinished = true };
                }
                else
                {
                    Set(index, StoreJobState.Interrupted, "partial_cleanup", failure ?? "partial_cleanup_ownership_lost");
                    if (receipt != null) partialDownloads[id] = receipt with { CleanupRequestId = null, CleanupExpectedRevision = null };
                }
            }
            if (active == id) { active = null; transfer = null; stopReason = null; }
            partialCleanupSlots.Remove(id); providerOperations--; Changed(); await DispatchLocked();
        }
        finally { gate.Release(); }
    }
    void RestorePartialDownloads(IEnumerable<StorePartialReceipt> receipts)
    {
        var requestIds = new HashSet<Guid>();
        foreach (var receipt in receipts)
        {
            if (receipt is null) throw new StoreOperationException("queue_invalid");
            var job = jobs.SingleOrDefault(j => j.JobId == receipt.JobId);
            if (job == null || receipt.Version != 1 || receipt.JobId == Guid.Empty || receipt.Key != job.Key ||
                receipt.AccountRevision != job.AccountRevision || !StringComparer.OrdinalIgnoreCase.Equals(receipt.InstallPath, job.InstallPath) ||
                !jobAccountIdentities.TryGetValue(job.JobId, out var identity) || identity != receipt.AccountIdentity ||
                receipt.DirectoryIdentity is not { Length: >= 1 and <= 128 } || receipt.CleanupRequestId == Guid.Empty ||
                receipt.CleanupRequestId.HasValue != receipt.CleanupExpectedRevision.HasValue || partialDownloads.ContainsKey(job.JobId)) throw new StoreOperationException("queue_invalid");
            if (receipt.Manifest is { } manifest)
                try { StoreOwnedPartialFiles.ValidateManifest(manifest, receipt.InstallPath); }
                catch (StoreOperationException) { throw new StoreOperationException("queue_invalid"); }
            if (receipt.CleanupAttempts is { } attempts && (attempts.Count > 1024 || attempts.Any(a => a is null || a.RequestId == Guid.Empty || a.ExpectedRevision < 1 || !requestIds.Add(a.RequestId)))) throw new StoreOperationException("queue_invalid");
            partialDownloads.Add(job.JobId, receipt);
            if (receipt.CleanupRequestId != null && !receipt.CleanupFinished && receipt.WorkerStarted && !receipt.DrainConfirmed)
            {
                int index = jobs.FindIndex(j => j.JobId == receipt.JobId);
                if (jobs[index].State != StoreJobState.Completed)
                    jobs[index] = jobs[index] with { State = StoreJobState.Interrupted, ReasonCode = "partial_cleanup", ErrorCode = "partial_cleanup_drain_unconfirmed", CanPause = false, CanResume = false, CanCancel = false };
            }
        }
    }
}

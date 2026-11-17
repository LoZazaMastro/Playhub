// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
using System.Text.Json;
namespace Playhub.StoreLibrary;

public sealed partial class StoreQueueEngine
{
    bool WindowsRequestRemovable(StoreDownloadJob job)
    {
        if (job.Key.Store != StoreKind.Xbox || job.ReasonCode != WindowsManagedReason ||
            !(job.State == StoreJobState.Failed || job.State == StoreJobState.ExternalHandoff && job.ErrorCode == "windows_store_install_unconfirmed") ||
            !windowsManagedReceipts.TryGetValue(job.JobId, out var receipt) || receipt.Key != job.Key || receipt.AccountRevision != job.AccountRevision)
            return false;
        lock (windowsObservationSync) return !windowsActiveCli.Contains(job.JobId);
    }
    void HideWindowsRequestLocked(int index)
    {
        var job = jobs[index];
        if (!WindowsRequestRemovable(job)) throw new StoreOperationException("windows_request_not_removable");
        var previousRevision = revision;
        bool added = clearedCanceledJobs.Add(job.JobId);
        jobs[index] = job with { State = StoreJobState.Canceled, Revision = job.Revision + 1, UpdatedAt = now(), CanPause = false, CanResume = false, CanCancel = false, CanDownloadNow = false, CanRemove = false };
        try { Changed(); }
        catch { jobs[index] = job; revision = previousRevision; if (added) clearedCanceledJobs.Remove(job.JobId); throw; }
        lock (windowsObservationSync) if (windowsObservationStops.TryGetValue(job.JobId, out var observation)) observation.Cancel();
    }
    // Forget an unconfirmed external request, not the OS operation or its files.
    // No active CLI can be dismissed. The retained receipt remains diagnostic
    // history and a later normal scan can still discover an installed package.
    public async Task<StoreCommandResult> DismissWindowsInstallRequestAsync(StoreRemoveHistoryRequest request, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            RequireRunning();
            if (request.JobId == Guid.Empty || request.RequestId == Guid.Empty) return Result(false, "invalid_request");
            string fingerprint = "windows-install-dismiss:" + JsonSerializer.Serialize(request);
            if (acknowledged.TryGetValue(request.RequestId, out var prior)) return prior.Fingerprint == fingerprint ? prior.Value : Result(false, "request_id_reused");
            if(HistorySelectionError(request) is {} selectionError)return Result(false,selectionError);
            int index = jobs.FindIndex(j => j.JobId == request.JobId);
            if (index < 0) return Result(false, "job_not_found");
            var job = jobs[index];
            lock (windowsObservationSync) if (windowsActiveCli.Contains(job.JobId)) return Result(false, "provider_busy");
            if (!WindowsRequestRemovable(job)) return Result(false, "windows_request_not_removable");
            HideWindowsRequestLocked(index);
            var result = Result(true, job: job.JobId); Remember(request.RequestId, fingerprint, result); return result;
        }
        finally { gate.Release(); }
    }
}

public sealed class StoreWindowsManagedHistoryProtocol(StoreQueueEngine engine) : IStoreProtocolExtension
{
    public async Task<(bool Handled, object? Data)> TryHandleAsync(string method, JsonElement args, CancellationToken token) =>
        method == "windows-install-dismiss"
            ? (true, await engine.DismissWindowsInstallRequestAsync(args.Deserialize<StoreRemoveHistoryRequest>(StoreLibraryService.JsonOptions) ?? throw new StoreOperationException("request_invalid"), token))
            : (false, null);
}

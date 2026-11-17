// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
namespace Playhub.StoreLibrary;

public sealed partial class StoreQueueEngine
{
    // Global progress is observational. Reviewed terminal rows carry their own
    // immutable job/account selection, so another worker cannot invalidate it.
    string? HistorySelectionError(long expected,IReadOnlyList<StoreHistorySelection>? selections)
    {
        if(expected<0||expected>revision)return "queue_changed";
        if(selections is null)return expected==revision?null:"queue_changed";
        if(selections.Count>50000||selections.Any(s=>s is null||s.JobId==Guid.Empty||s.Key is null||s.ExpectedJobRevision<1||s.ExpectedAccountRevision<1)||selections.Select(s=>s.JobId).Distinct().Count()!=selections.Count)return "invalid_request";
        foreach(var selection in selections)
        {
            var job=jobs.Find(j=>j.JobId==selection.JobId);if(job is null)return "job_not_found";
            if(job.Key!=selection.Key||job.AccountRevision!=selection.ExpectedAccountRevision||job.Revision!=selection.ExpectedJobRevision)return "job_selection_changed";
        }
        return null;
    }
    string? HistorySelectionError(StoreRemoveHistoryRequest request)
    {
        bool scoped=request.ExpectedJobRevision is not null||request.ExpectedKey is not null||request.ExpectedAccountRevision is not null;
        if(!scoped)return HistorySelectionError(request.ExpectedRevision,null);
        if(request.ExpectedJobRevision is not {} jobRevision||request.ExpectedKey is not {} key||request.ExpectedAccountRevision is not {} accountRevision)return "invalid_request";
        return HistorySelectionError(request.ExpectedRevision,[new(request.JobId,jobRevision,key,accountRevision)]);
    }
    private bool CanceledHistoryClearable(StoreDownloadJob job) => job.State == StoreJobState.Canceled && job.JobId != active
        && job.ErrorCode != "worker_drain_unconfirmed" && job.ReasonCode != "worker_drain_unconfirmed";

    public async Task<StoreCommandResult> ClearCanceledAsync(StoreClearCanceledRequest request, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            RequireRunning();
            if (request.RequestId == Guid.Empty || request.JobId == Guid.Empty) return Result(false, "invalid_request");
            string fingerprint = "clear-canceled:" + System.Text.Json.JsonSerializer.Serialize(request);
            if (acknowledged.TryGetValue(request.RequestId, out var prior))
                return prior.Fingerprint == fingerprint ? prior.Value : Result(false, "request_id_reused");
            if (HistorySelectionError(request.ExpectedRevision,request.Selections) is {} selectionError)return Result(false,selectionError);
            if(request.Selections is {} selections&&(request.JobId is {} selectedId&&(selections.Count!=1||selections[0].JobId!=selectedId)||selections.Any(s=>jobs.Single(j=>j.JobId==s.JobId).State!=StoreJobState.Canceled)))return Result(false,"job_not_canceled");
            if (request.JobId is Guid jobId)
            {
                var job = jobs.Find(value => value.JobId == jobId);
                if (job is null) return Result(false, "job_not_found");
                if (job.State != StoreJobState.Canceled) return Result(false, "job_not_canceled");
                if (!CanceledHistoryClearable(job)) return Result(false, "provider_busy");
            }
            var selected=request.Selections?.Select(s=>s.JobId).ToHashSet();
            var canceled = jobs.Where(job => CanceledHistoryClearable(job) && (selected is null||selected.Contains(job.JobId)) && (!request.JobId.HasValue || job.JobId == request.JobId)
                && !clearedCanceledJobs.Contains(job.JobId)).ToArray();
            // This hides terminal history only. Original job/account provenance,
            // owned receipts, paths, game payloads and OS operations remain intact.
            HideHistoryRows(canceled);
            var result = Result(true, job: request.JobId);
            Remember(request.RequestId, fingerprint, result);
            return result;
        }
        finally { gate.Release(); }
    }

    public async Task<StoreCommandResult> RemoveHistoryAsync(StoreRemoveHistoryRequest request, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            RequireRunning();
            if (request.RequestId == Guid.Empty || request.JobId == Guid.Empty) return Result(false, "invalid_request");
            string fingerprint = "remove-history:" + System.Text.Json.JsonSerializer.Serialize(request);
            if (acknowledged.TryGetValue(request.RequestId, out var prior))
                return prior.Fingerprint == fingerprint ? prior.Value : Result(false, "request_id_reused");
            if (HistorySelectionError(request) is {} selectionError)return Result(false,selectionError);
            var job = jobs.Find(value => value.JobId == request.JobId);
            if (job is null) return Result(false, "job_not_found");
            if (job.State is not (StoreJobState.Completed or StoreJobState.Canceled)) return Result(false, "job_not_terminal");
            // Removing a visible history row does not release any worker lease.
            HideHistoryRows([job]);
            var result = Result(true, job: job.JobId);
            Remember(request.RequestId, fingerprint, result);
            return result;
        }
        finally { gate.Release(); }
    }

    private void HideHistoryRows(IReadOnlyList<StoreDownloadJob> rows)
    {
        var completed = rows.Where(job => job.State == StoreJobState.Completed && !clearedCompletedJobs.Contains(job.JobId)).Select(job => job.JobId).ToArray();
        var canceled = rows.Where(job => job.State == StoreJobState.Canceled && !clearedCanceledJobs.Contains(job.JobId)).Select(job => job.JobId).ToArray();
        if (completed.Length == 0 && canceled.Length == 0) return;
        foreach (var id in completed) clearedCompletedJobs.Add(id);
        foreach (var id in canceled) clearedCanceledJobs.Add(id);
        long before = revision;
        try { Changed(); }
        catch
        {
            // A failed persistence commit is not a successful removal.
            foreach (var id in completed) clearedCompletedJobs.Remove(id);
            foreach (var id in canceled) clearedCanceledJobs.Remove(id);
            revision = before;
            throw;
        }
    }
}

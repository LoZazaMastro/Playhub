// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
namespace Playhub.StoreLibrary;

public sealed partial class StoreQueueEngine
{
    // Nile cannot checkpoint a running transfer. An already downloaded, drained
    // installation whose prerequisite failed is a separate retry: StartAsync
    // revalidates the signed manifest/files before rerunning finalization.
    bool CanRetryFinalization(StoreDownloadJob job) => job.Key.Store == StoreKind.Amazon &&
        job.State == StoreJobState.Interrupted && job.TotalBytes is > 0 && job.DownloadedBytes == job.TotalBytes &&
        job.ErrorCode?.StartsWith("amazon_setup_", StringComparison.Ordinal) == true &&
        active != job.JobId && !drainUnconfirmed && !PartialCleanupRequested(job.JobId) &&
        partialDownloads.TryGetValue(job.JobId, out var receipt) && receipt.WorkerStarted && receipt.DrainConfirmed &&
        receipt.Manifest != null && !receipt.CleanupFinished && CleanupContextError(job, receipt, requireDrain: true) == null;

    bool CanResumeJob(StoreDownloadJob job) => Provider(job.Key.Store).Capabilities.CanResume || CanRetryFinalization(job);
}

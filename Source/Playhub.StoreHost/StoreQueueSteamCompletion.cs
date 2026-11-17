// SPDX-License-Identifier: MIT. Original Playhub code, Copyright (c) 2026 LoZazaMastro.
namespace Playhub.StoreLibrary;
public sealed partial class StoreQueueEngine
{
    // Steam's installation events outlive the optional Downloads history row.
    // This view never changes the public queue or manufactures a completion time.
    internal async Task<IReadOnlyList<StoreDownloadJob>> GetRetainedCompletedInstallsAsync(CancellationToken token=default)
    {
        await gate.WaitAsync(token);
        try
        {
            return jobs.Where(job=>job.State==StoreJobState.Completed&&Connected(job.Key.Store,job.AccountRevision)
                &&jobAccountIdentities.TryGetValue(job.JobId,out var identity)&&accountIdentities.GetValueOrDefault(job.Key.Store)==identity
                &&games.TryGetValue(job.Key,out var game)&&game.AccountRevision==job.AccountRevision&&game.IsInstalled&&game.IsLaunchable
                &&game.InstallPath is not null&&StringComparer.OrdinalIgnoreCase.Equals(job.InstallPath,game.InstallPath))
                .OrderByDescending(job=>job.UpdatedAt).ToArray();
        }
        finally{gate.Release();}
    }
}

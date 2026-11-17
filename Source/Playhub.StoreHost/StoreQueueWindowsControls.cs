// SPDX-License-Identifier: MIT. Original Playhub code, Copyright (c) 2026 LoZazaMastro.
using System.Text.Json;
namespace Playhub.StoreLibrary;
public sealed partial class StoreQueueEngine
{
    readonly HashSet<Guid> windowsCancelRequests=[];
    async Task<StoreCommandResult?> TryCancelWindowsControlAsync(StoreJobCommand command,CancellationToken token)
    {
        StoreDownloadJob job;IWindowsStoreInstallDestinationRuntime native;string fingerprint="control:"+JsonSerializer.Serialize(command);
        await gate.WaitAsync(token);
        try
        {
            int index=jobs.FindIndex(j=>j.JobId==command.JobId);
            if(index<0||!windowsManagedReceipts.TryGetValue(command.JobId,out var receipt)||receipt.PendingLaunch||WindowsManagedRuntime is not IWindowsStoreInstallDestinationRuntime runtime)return null;
            if(command.RequestId==Guid.Empty)return Result(false,"invalid_request");
            if(acknowledged.TryGetValue(command.RequestId,out var prior))return prior.Fingerprint==fingerprint?prior.Value:Result(false,"request_id_reused");
            job=jobs[index];if(DownloadSelectionError(job,command.ExpectedRevision,command.ExpectedKey,command.ExpectedAccountRevision,command.ExpectedInstallPath) is {} selectionError)return Result(false,selectionError);
            if(Terminal(job.State))return Result(false,"action_unavailable");
            if(!windowsCancelRequests.Add(job.JobId))return Result(false,"provider_busy");
            native=runtime;jobs[index]=job with{State=StoreJobState.Canceling,Revision=job.Revision+1,UpdatedAt=now(),ErrorCode=null};Changed();
        }
        finally{gate.Release();}
        string? error=null;
        try{await native.CancelInstallAsync(job.Key.Id,token);}
        catch(Exception failure)when(failure is not OutOfMemoryException){error="windows_store_cancel_failed";}
        await gate.WaitAsync();
        try
        {
            windowsCancelRequests.Remove(job.JobId);int index=jobs.FindIndex(j=>j.JobId==job.JobId);
            if(index<0)return Result(false,"job_not_found");var current=jobs[index];
            if(error is not null)
            {
                // Keep the request visible and retryable when Windows did not
                // accept cancellation. The native execution slot remains owned.
                if(!Terminal(current.State))jobs[index]=current with{State=current.State==StoreJobState.Canceling?job.State:current.State,ErrorCode=error,Revision=current.Revision+1,UpdatedAt=now()};
                Changed();return Result(false,error,job.JobId);
            }
            jobs[index]=current with{State=StoreJobState.Canceled,Revision=current.Revision+1,UpdatedAt=now(),CanCancel=false,CanPause=false,CanResume=false};
            clearedCanceledJobs.Add(job.JobId);Changed();DispatchWindowsManagedLocked();
            var result=Result(true,job:job.JobId);Remember(command.RequestId,fingerprint,result);return result;
        }
        finally{gate.Release();}
    }
}

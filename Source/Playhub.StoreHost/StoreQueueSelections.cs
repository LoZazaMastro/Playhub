// SPDX-License-Identifier: MIT. Original Playhub code, Copyright (c) 2026 LoZazaMastro.
namespace Playhub.StoreLibrary;
public sealed partial class StoreQueueEngine
{
    static bool HasDownloadScope(StoreGameKey? key,long? account,string? path)=>key is not null||account is not null||path is not null;
    static bool MatchesDownloadScope(StoreDownloadJob job,StoreGameKey? key,long? account,string? path)=>
        !HasDownloadScope(key,account,path)||key is not null&&account is not null&&path is not null&&job.Key==key&&job.AccountRevision==account&&StringComparer.OrdinalIgnoreCase.Equals(job.InstallPath,path);
    static string? DownloadSelectionError(StoreDownloadJob job,long expected,StoreGameKey? key,long? account,string? path)
    {
        if(!HasDownloadScope(key,account,path))return job.Revision==expected?null:"job_changed";
        if(key is null||account is null||path is null)return "invalid_request";
        if(expected<1||expected>job.Revision)return "job_changed";
        return MatchesDownloadScope(job,key,account,path)?null:"job_selection_changed";
    }
}

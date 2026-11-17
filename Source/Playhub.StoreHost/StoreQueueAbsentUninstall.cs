// SPDX-License-Identifier: MIT. Explicit unlink of an installation already removed outside Playhub.
using System.Text.Json;
namespace Playhub.StoreLibrary;
public sealed partial class StoreQueueEngine
{
    private readonly Dictionary<Guid,(StoreUninstallPreparation Receipt,StoreLibraryGame Game,long AccountContext,string Path)> absentUninstalls=[];
    private static bool InstallationDirectoryDefinitelyAbsent(string path)
    {
        try
        {
            if(!Path.IsPathFullyQualified(path))return false;
            var root=Path.GetPathRoot(path);if(string.IsNullOrEmpty(root)||root.StartsWith("\\\\",StringComparison.Ordinal)||!new DriveInfo(root).IsReady)return false;
            _=File.GetAttributes(root);
            try{_=File.GetAttributes(path);return false;}
            catch(FileNotFoundException){return true;}
            catch(DirectoryNotFoundException){return true;}
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException){return false;}
    }
    private bool SameAbsentSelection(StoreLibraryGame? current,StoreLibraryGame held)=>current is not null&&current.Key==held.Key&&current.AccountRevision==held.AccountRevision&&current.IsInstalled==held.IsInstalled&&StringComparer.OrdinalIgnoreCase.Equals(current.InstallPath,held.InstallPath);
    private async Task<StoreUninstallPreparation?> PrepareAbsentUninstallAsync(StoreGameKey key,long accountRevision,long libraryRevision,string? expectedPath,CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            if(disposed||quiescing||libraryRevision<0||libraryRevision>revision||!games.TryGetValue(key,out var game)||game.AccountRevision!=accountRevision||InstalledOperationBusy(key.Store)||RemovalTransferBusy(key.Store)||accounts[key.Store].State==StoreConnectionState.Connecting)return null;
            if(expectedPath is not null&&!StringComparer.OrdinalIgnoreCase.Equals(expectedPath,game.InstallPath))return null;
            if(installedLaunchReservations.TryGetValue(key,out var expiry)&&now()<expiry||jobs.Any(j=>j.Key==key&&(!Terminal(j.State)||windowsManagedReceipts.ContainsKey(j.JobId)&&!WindowsManagedTerminal(j))))return null;
            var path=game.InstallPath??jobs.Where(j=>j.Key==key&&j.AccountRevision==accountRevision&&j.State==StoreJobState.Completed).OrderByDescending(j=>j.UpdatedAt).Select(j=>j.InstallPath).FirstOrDefault();
            if(path is null||!InstallationDirectoryDefinitelyAbsent(path))return null;
            foreach(var stale in absentUninstalls.Where(p=>p.Value.Receipt.ExpiresAt<=now()).Select(p=>p.Key).ToArray())absentUninstalls.Remove(stale);
            if(absentUninstalls.Count>=64)return null;
            var nonce=Guid.NewGuid();var receipt=new StoreUninstallPreparation(key,accountRevision,revision,game.InstallPath,nonce,now().AddMinutes(2),true);
            absentUninstalls[nonce]=(receipt,game,accounts[key.Store].Revision,path);return receipt;
        }
        finally{gate.Release();}
    }
    private async Task<StoreCommandResult?> UninstallAbsentAsync(StoreUninstallPreparation receipt,Guid requestId,CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            var fingerprint="uninstall:"+JsonSerializer.Serialize(receipt);
            if(acknowledged.TryGetValue(requestId,out var prior))return prior.Fingerprint==fingerprint?prior.Value:Result(false,"request_id_reused");
            if(receipt.Nonce is not {} nonce||!absentUninstalls.TryGetValue(nonce,out var held))return null;
            if(requestId==Guid.Empty||held.Receipt!=receipt||now()>=receipt.ExpiresAt||disposed||quiescing||accounts[receipt.Key.Store].Revision!=held.AccountContext||!SameAbsentSelection(games.GetValueOrDefault(receipt.Key),held.Game)||InstalledOperationBusy(receipt.Key.Store)||RemovalTransferBusy(receipt.Key.Store)||jobs.Any(j=>j.Key==receipt.Key&&(!Terminal(j.State)||windowsManagedReceipts.ContainsKey(j.JobId)&&!WindowsManagedTerminal(j)))||!InstallationDirectoryDefinitelyAbsent(held.Path))return Result(false,"installation_changed");
            absentUninstalls.Remove(nonce);
            // No provider invocation or filesystem deletion: the user's confirmed
            // request retires only the already absent installation/Steam import.
            games[receipt.Key]=held.Game with{IsInstalled=false,IsLaunchable=false,InstallPath=null,UnavailableReason="uninstalled"};
            legacyInstallReceipts.Remove(receipt.Key);Changed();var result=Result(true);Remember(requestId,fingerprint,result);return result;
        }
        finally{gate.Release();}
    }
}

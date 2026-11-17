// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
using System.Text.Json;
namespace Playhub.StoreLibrary;

public sealed partial class StoreQueueEngine
{
    readonly Dictionary<Guid,(StoreUninstallPreparation Receipt,WindowsXboxUninstallPlan Plan)> xboxUninstallPreparations=[];
    string? XboxUninstallContext(StoreGameKey key,out StoreLibraryGame? game)
    {
        game=games.GetValueOrDefault(key);
        if(disposed||quiescing)return "maintenance_in_progress";
        if(key.Store!=StoreKind.Xbox||Provider(StoreKind.Xbox) is not StoreXboxAccountProvider)return "uninstall_unavailable";
        if(InstalledOperationBusy(key.Store)||RemovalTransferBusy(key.Store))return "provider_busy";
        if(installedLaunchReservations.TryGetValue(key,out var expiry)&&now()<expiry)return "launch_pending";
        if(game?.IsInstalled!=true||game.InstallPath is null)return "game_not_installed";
        if(!Connected(StoreKind.Xbox,game.AccountRevision))return "account_changed";
        if(jobs.Any(j=>j.Key==key&&j.State is not (StoreJobState.Completed or StoreJobState.Canceled or StoreJobState.Failed)))return "download_active";
        return null;
    }
    async Task<StoreInstalledGameStatus> GetXboxInstalledGameAsync(StoreGameKey key,CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            var error=XboxUninstallContext(key,out var game);bool removable=false,launchable=false;
            if(game?.IsInstalled==true&&Provider(StoreKind.Xbox) is StoreXboxAccountProvider provider)
            {
                if(error is null)
                    try{await provider.PrepareNativeUninstallAsync(key.Id,game.InstallPath!,token);removable=true;}
                    catch(StoreOperationException e){error=e.Code;}
                launchable=game.IsLaunchable&&!InstalledOperationBusy(StoreKind.Xbox)&&!disposed&&!quiescing
                    &&await provider.ResolveLaunchAsync(key.Id,game.InstallPath!,token) is { } launch&&File.Exists(launch.Executable);
            }
            return new(key,game?.AccountRevision??accounts[StoreKind.Xbox].Revision,revision,game?.IsInstalled==true,launchable,removable,game?.InstallPath,removable||launchable?null:error);
        }
        finally{gate.Release();}
    }
    async Task<StoreUninstallPreparation> PrepareXboxUninstallAsync(StoreGameKey key,long accountRevision,long libraryRevision,CancellationToken token,string? expectedInstallPath=null)
    {
        StoreLibraryGame game;
        StoreUninstallPreparation Denied(string error)=>new(key,accountRevision,libraryRevision,null,null,now(),false,error);
        await gate.WaitAsync(token);
        try
        {
            if(libraryRevision<0||libraryRevision>revision)return Denied("library_changed");
            var error=XboxUninstallContext(key,out var held);if(error is not null)return Denied(error);
            game=held!;if(game.AccountRevision!=accountRevision)return Denied("account_changed");
            if(expectedInstallPath is not null&&!StringComparer.OrdinalIgnoreCase.Equals(expectedInstallPath,game.InstallPath))return Denied("installation_changed");
            installedOperationStores.Add(StoreKind.Xbox);providerOperations++;
        }
        finally{gate.Release();}
        WindowsXboxUninstallPlan? plan=null;string? failure=null;
        try{plan=await ((StoreXboxAccountProvider)Provider(StoreKind.Xbox)).PrepareNativeUninstallAsync(key.Id,game.InstallPath!,token);}
        catch(Exception e)when(e is not OutOfMemoryException){failure=e is StoreOperationException s?s.Code:"uninstall_preparation_failed";}
        await gate.WaitAsync();
        try
        {
            installedOperationStores.Remove(StoreKind.Xbox);providerOperations--;
            if(failure is not null||plan is null)return Denied(failure??"uninstall_preparation_failed");
            if(!SameRemovalSelection(games.GetValueOrDefault(key),game)||!Connected(StoreKind.Xbox,accountRevision)
                ||accountIdentities.GetValueOrDefault(StoreKind.Xbox)!=plan.AccountIdentity)return Denied("installation_changed");
            foreach(var old in xboxUninstallPreparations.Where(p=>p.Value.Receipt.ExpiresAt<=now()).Select(p=>p.Key).ToArray())xboxUninstallPreparations.Remove(old);
            if(xboxUninstallPreparations.Count>=64)return Denied("too_many_confirmations");
            var nonce=Guid.NewGuid();var receipt=new StoreUninstallPreparation(key,accountRevision,revision,game.InstallPath,nonce,now().AddMinutes(2),true);
            xboxUninstallPreparations.Add(nonce,(receipt,plan));return receipt;
        }
        finally{gate.Release();}
    }
    async Task<StoreCommandResult> XboxUninstallAsync(StoreUninstallPreparation preparation,Guid requestId,CancellationToken token)
    {
        string fingerprint="uninstall:"+JsonSerializer.Serialize(preparation);WindowsXboxUninstallPlan plan;StoreLibraryGame game;
        await gate.WaitAsync(token);
        try
        {
            if(requestId==Guid.Empty)return Result(false,"invalid_request");
            if(acknowledged.TryGetValue(requestId,out var prior))return prior.Fingerprint==fingerprint?prior.Value:Result(false,"request_id_reused");
            if(preparation.Nonce is not {}nonce||!xboxUninstallPreparations.TryGetValue(nonce,out var held)||held.Receipt!=preparation||now()>=preparation.ExpiresAt||preparation.LibraryRevision>revision)
                return Result(false,"confirmation_expired_or_mismatched");
            var error=XboxUninstallContext(preparation.Key,out var current);if(error is not null)return Result(false,error);
            game=current!;plan=held.Plan;
            if(game.AccountRevision!=preparation.AccountRevision||game.InstallPath!=preparation.InstallPath||accountIdentities.GetValueOrDefault(StoreKind.Xbox)!=plan.AccountIdentity)return Result(false,"installation_changed");
            xboxUninstallPreparations.Remove(nonce);installedOperationStores.Add(StoreKind.Xbox);providerOperations++;
        }
        finally{gate.Release();}
        string? failure=null;
        try{await ((StoreXboxAccountProvider)Provider(StoreKind.Xbox)).NativeUninstallAsync(plan,token);}
        catch(Exception e)when(e is not OutOfMemoryException){failure=e is StoreOperationException s?s.Code:e is OperationCanceledException?"uninstall_unconfirmed":"uninstall_failed";}
        await gate.WaitAsync();
        try
        {
            installedOperationStores.Remove(StoreKind.Xbox);providerOperations--;
            if(failure is null)
            {
                if(Connected(StoreKind.Xbox,preparation.AccountRevision)&&accountIdentities.GetValueOrDefault(StoreKind.Xbox)==plan.AccountIdentity&&ReferenceEquals(games.GetValueOrDefault(plan.Key),game))
                    games[plan.Key]=game with{IsInstalled=false,IsLaunchable=false,InstallPath=null};
                Changed();
            }
            var result=Result(failure is null,failure);Remember(requestId,fingerprint,result);return result;
        }
        finally{gate.Release();}
    }
}

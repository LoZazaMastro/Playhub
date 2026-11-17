// SPDX-License-Identifier: MIT. Copyright (c) 2026 LoZazaMastro.
namespace Playhub.StoreLibrary;
public sealed partial class StoreAmazonProvider:IStoreLegacyInstalledGameProvider,IStoreRegisteredInstallation
{
    Task<bool> IStoreRegisteredInstallation.HasRegisteredInstallationAsync(string id,string installPath,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();StoreCliProvider.ValidateId(id);
        var registered=InstalledPath(id);
        return Task.FromResult(registered is not null&&StringComparer.OrdinalIgnoreCase.Equals(registered,new StoreInstallFileSystem().CanonicalDestination(installPath))&&Directory.Exists(registered));
    }
    async Task<StoreOwnedUninstallPlan?> IStoreLegacyInstalledGameProvider.VerifyLegacyInstallationAsync(string id,string installPath,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();StoreCliProvider.ValidateId(id);
        await WaitForRemovalGateAsync(token);
        try
        {
            string path=new StoreInstallFileSystem().CanonicalDestination(installPath);
            if(InstalledPath(id) is not {} installed||!StringComparer.OrdinalIgnoreCase.Equals(installed,path))return null;
            using var pins=StoreOwnedInstallFiles.PinRoot(path);StoreOwnedInstallFiles.RefuseRunning(path);
            string markerPath=Path.Combine(path,".playhub-install-owner");Guid owner;bool created=false,accepted=false;
            if(File.Exists(markerPath))
            {
                using var marker=StoreOwnedInstallFiles.OpenReadFile(markerPath);
                if(marker.Length!=36)return null;using var reader=new StreamReader(marker);
                if(!Guid.TryParseExact(reader.ReadToEnd(),"D",out owner)||owner==Guid.Empty)return null;
            }
            else
            {
                owner=Guid.NewGuid();using(var marker=new FileStream(markerPath,FileMode.CreateNew,FileAccess.Write,FileShare.Read))marker.Write(System.Text.Encoding.UTF8.GetBytes(owner.ToString("D")));
                created=true;
            }
            try{using var state=ReadAmazonUninstall(id,path,owner,token);accepted=true;return state.Plan;}
            finally{if(created&&!accepted&&File.Exists(markerPath)&&File.ReadAllText(markerPath)==owner.ToString("D"))File.Delete(markerPath);}
        }
        finally{gate.Release();}
    }
}

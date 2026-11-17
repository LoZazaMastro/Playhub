// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
namespace Playhub.StoreLibrary;

public sealed partial class StoreCliProvider : IStoreLegacyInstalledGameProvider
{
    async Task<StoreOwnedUninstallPlan?> IStoreLegacyInstalledGameProvider.VerifyLegacyInstallationAsync(string id, string installPath, CancellationToken token)
    {
        // Exact private/official product registrations and their retained
        // manifests authorize removal even after history was cleared or assets
        // were changed. Nothing is inferred from a display title or arbitrary tree.
        ValidateId(id);
        await WaitForRemovalGateAsync(token);
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
            budget.CancelAfter(TimeSpan.FromMinutes(1));
            token = budget.Token;
            var path = new StoreInstallFileSystem().CanonicalDestination(installPath);
            var installed = ReadRemovalInstall(id);
            if (installed is null || !StringComparer.OrdinalIgnoreCase.Equals(installed.Path, path)) return null;
            using var pins=StoreOwnedInstallFiles.PinRoot(path);StoreOwnedInstallFiles.RefuseRunning(path);RefuseSharedRemoval(id,path,token);
            string markerPath=Path.Combine(path,".playhub-install-owner");Guid owner;bool created=false,accepted=false;
            if(File.Exists(markerPath))
            {
                using var marker=StoreOwnedInstallFiles.OpenReadFile(markerPath);
                if(marker.Length!=36)return null;using var reader=new StreamReader(marker);
                if(!Guid.TryParseExact(await reader.ReadToEndAsync(token),"D",out owner)||owner==Guid.Empty)return null;
            }
            else
            {
                // The path has already been independently identified by its
                // exact provider record. The server owns only this new marker.
                owner=Guid.NewGuid();using(var marker=new FileStream(markerPath,FileMode.CreateNew,FileAccess.Write,FileShare.Read))
                    marker.Write(System.Text.Encoding.UTF8.GetBytes(owner.ToString("D")));
                created=true;
            }
            try
            {
                var plan=await ReadUninstallPlan(id,path,owner,token);
                var after=ReadRemovalInstall(id);
                if(after is null||after.Path!=path)throw new StoreOperationException("installation_changed");
                accepted=true;return plan;
            }
            finally{if(created&&!accepted&&File.Exists(markerPath)&&File.ReadAllText(markerPath)==owner.ToString("D"))File.Delete(markerPath);}
        }
        finally { nativeGate.Release(); }
    }
}

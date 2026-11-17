// SPDX-License-Identifier: MIT. Copyright (c) 2026 LoZazaMastro.
using System.Security.Cryptography;
using System.Text.Json;
namespace Playhub.StoreLibrary;
public sealed partial class StoreCliProvider:IStoreRegisteredInstallation
{
    private async Task WaitForRemovalGateAsync(CancellationToken token)
    {
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(token);budget.CancelAfter(TimeSpan.FromSeconds(20));
        try{await nativeGate.WaitAsync(budget.Token);}
        catch(OperationCanceledException)when(!token.IsCancellationRequested){throw new StoreOperationException("provider_busy");}
    }
    Task<bool> IStoreRegisteredInstallation.HasRegisteredInstallationAsync(string id,string installPath,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();ValidateId(id);
        var local=ReadRemovalInstall(id);
        return Task.FromResult(local is not null&&SameOfficialPath(local.Path,installPath)&&Directory.Exists(local.Path));
    }
    private Installed? ReadRemovalInstall(string id,bool includeOfficial=true)
    {
        if(Store==StoreKind.Epic)
        {
            string file=Path.Combine(Path.GetDirectoryName(nativeAuth)!,"installed.json");
            if(File.Exists(file))
            {
                using var document=JsonDocument.Parse(File.ReadAllBytes(file));
                if(document.RootElement.TryGetProperty(id,out var record)&&record.TryGetProperty("install_path",out var path)&&record.TryGetProperty("executable",out var relative)
                    &&path.GetString() is {} root&&relative.GetString() is {} name&&Path.IsPathFullyQualified(root)&&!Path.IsPathRooted(name))
                    return new(new StoreInstallFileSystem().CanonicalDestination(root),StoreOwnedInstallFiles.PathFor(root,name.Replace('\\','/')));
            }
        }
        else
        {
            string file=Path.Combine(root,"installed-"+id+".json");
            if(File.Exists(file)&&JsonSerializer.Deserialize<string>(File.ReadAllText(file)) is {} path&&Path.IsPathFullyQualified(path))
            {
                path=new StoreInstallFileSystem().CanonicalDestination(path);
                string info=Path.Combine(path,"goggame-"+id+".info");
                if(File.Exists(info))
                {
                    using var document=JsonDocument.Parse(File.ReadAllBytes(info));var data=document.RootElement;
                    if(data.GetProperty("gameId").ToString()!=id)throw new StoreOperationException("installed_metadata_invalid");
                    var primary=data.GetProperty("playTasks").EnumerateArray().Where(t=>t.TryGetProperty("isPrimary",out var p)&&p.ValueKind==JsonValueKind.True).ToArray();
                    if(primary.Length==1&&primary[0].TryGetProperty("path",out var relative)&&relative.GetString() is {} name)return new(path,StoreOwnedInstallFiles.PathFor(path,name.Replace('\\','/')));
                }
                if(File.Exists(GogManifestPath(id)))return new(path,info);
            }
        }
        if(!includeOfficial)return null;
        var official=OfficialInstallations.ReadForRemoval(Store,CancellationToken.None).Find(id);
        return official is null?null:new(official.InstallPath,official.Executable);
    }
    private void RefuseSharedRemoval(string id,string path,CancellationToken token)
    {
        var inventory=OfficialInstallations.ReadForRemoval(Store,token);
        if(!inventory.IsComplete)throw new StoreOperationException("installation_inventory_unavailable");
        foreach(var other in inventory.Games.Values.Where(g=>g.Key.Id!=id))
            if(SameOfficialPath(path,other.InstallPath)||other.InstallPath.StartsWith(path+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)
                ||path.StartsWith(Path.TrimEndingDirectorySeparator(other.InstallPath)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new StoreOperationException("shared_installation_refused");
    }
    private (string Manifest,string RegistrationHash) EpicRemovalManifest(string id,string path,CancellationToken token)
    {
        var official=OfficialInstallations.ReadForRemoval(StoreKind.Epic,token).Find(id);
        if(official is not null&&SameOfficialPath(path,official.InstallPath)&&official.EpicManifestPath is {} manifest&&official.EpicRegistrationPath is {} registration)
        {
            using var item=StoreOwnedInstallFiles.OpenReadFile(registration);
            if(item.Length is <1 or >1024*1024)throw new StoreOperationException("installed_metadata_invalid");
            string fingerprint=Convert.ToHexString(SHA256.HashData(item));
            if(official.EpicSecondaryRegistrationPath is {} secondary)
            {
                using var companion=StoreOwnedInstallFiles.OpenReadFile(secondary);
                if(companion.Length is <1 or >262144)throw new StoreOperationException("installed_metadata_invalid");
                fingerprint+="\n"+Convert.ToHexString(SHA256.HashData(companion));
            }
            return(manifest,fingerprint);
        }
        return(EpicInstalledManifest(id),"");
    }
}

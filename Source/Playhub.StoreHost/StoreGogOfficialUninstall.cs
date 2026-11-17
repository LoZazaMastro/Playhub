// SPDX-License-Identifier: MIT. Copyright (c) 2026 LoZazaMastro.
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Playhub.StoreLibrary;

public sealed partial class StoreCliProvider
{
    internal Func<string,bool> GogUninstallerTrust {get;set;}=StoreGogImageTrust.Verify;
    internal Func<ProcessStartInfo,Task<int>> GogUninstallerRun {get;set;}=RunGogUninstaller;
    private StoreOwnedUninstallPlan ReadGogOfficialUninstallPlan(string id,string path,Guid owner,CancellationToken token)
    {
        var native=OfficialInstallations.ReadForRemoval(StoreKind.Gog,token).Find(id);
        var recipe=OfficialInstallations.FindGogUninstaller(id,path,token);
        if(native is null||recipe is null||!SameOfficialPath(path,native.InstallPath))throw new StoreOperationException("uninstall_manifest_unavailable");
        using var roots=StoreOwnedInstallFiles.PinRoot(path);
        var files=new List<StoreOwnedInstallFile>();
        foreach(string file in new[]{recipe.Executable,Path.ChangeExtension(recipe.Executable,"dat"),Path.Combine(path,"goggame-"+id+".info")})
        {
            token.ThrowIfCancellationRequested();using var input=StoreOwnedInstallFiles.OpenReadFile(file);
            if(input.Length is <1 or >128*1024*1024)throw new StoreOperationException("installed_metadata_invalid");
            files.Add(new(Path.GetRelativePath(path,file),input.Length,"SHA256",Convert.ToHexString(SHA256.HashData(input))));
        }
        if(!GogUninstallerTrust(recipe.Executable))throw new StoreOperationException("uninstall_image_untrusted");
        string fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(recipe.RegistrationWitness+"\n"+JsonSerializer.Serialize(files))));
        var plan=new StoreOwnedUninstallPlan(new(StoreKind.Gog,id),path,recipe.Executable,owner,fingerprint,files.AsReadOnly());
        StoreOwnedInstallFiles.ValidatePlan(plan);return plan;
    }
    private async Task ExecuteGogOfficialUninstall(StoreOwnedUninstallPlan plan,CancellationToken token)
    {
        StoreOwnedInstallFiles.RefuseRunning(plan.InstallPath);RefuseSharedRemoval(plan.Key.Id,plan.InstallPath,token);
        Task<int> process;
        // Retain the exact witness files until the verified image is activated.
        // Release before waiting: the official uninstaller owns its file removal.
        using(var roots=StoreOwnedInstallFiles.PinRoot(plan.InstallPath))
        {
            var pins=new List<FileStream>();
            try
            {
                var current=ReadGogOfficialUninstallPlan(plan.Key.Id,plan.InstallPath,plan.OwnerJob,token);
                if(current.ManifestHash!=plan.ManifestHash||current.GameExecutable!=plan.GameExecutable)throw new StoreOperationException("installation_changed");
                foreach(var file in current.Files)
                {
                    var stream=StoreOwnedInstallFiles.OpenReadFile(StoreOwnedInstallFiles.PathFor(plan.InstallPath,file.RelativePath));pins.Add(stream);
                    StoreOwnedInstallFiles.VerifyContents(stream,file,token);
                }
                token.ThrowIfCancellationRequested();
                var start=new ProcessStartInfo(plan.GameExecutable){UseShellExecute=true,WorkingDirectory=plan.InstallPath,WindowStyle=ProcessWindowStyle.Hidden};
                // Inno's documented flags prevent reboot while preserving any
                // native custom prompt asking whether user saves should be kept.
                start.ArgumentList.Add("/SILENT");start.ArgumentList.Add("/NORESTART");
                process=GogUninstallerRun(start);
            }
            finally{foreach(var pin in pins)pin.Dispose();}
        }
        // Once started, closing the requesting UI cannot abort the registry/file
        // commit or make a second operation race this native uninstaller.
        int code=await process;
        if(code!=0)throw new StoreOperationException(code==2?"uninstall_canceled":"uninstall_failed");
        if(OfficialInstallations.FindGogUninstaller(plan.Key.Id,plan.InstallPath,CancellationToken.None) is not null)throw new StoreOperationException("uninstall_not_confirmed");
        string local=Path.Combine(root,"installed-"+plan.Key.Id+".json");
        if(File.Exists(local)&&JsonSerializer.Deserialize<string>(File.ReadAllText(local)) is {} registered&&SameOfficialPath(registered,plan.InstallPath))File.Delete(local);
        string marker=Path.Combine(plan.InstallPath,".playhub-install-owner");
        if(File.Exists(marker))
        {
            using var input=StoreOwnedInstallFiles.OpenReadFile(marker);using var reader=new StreamReader(input);
            if(reader.ReadToEnd()!=plan.OwnerJob.ToString("D"))throw new StoreOperationException("installation_ownership_lost");
        }
        if(File.Exists(marker))File.Delete(marker);
    }
    private static async Task<int> RunGogUninstaller(ProcessStartInfo start)
    {
        using var process=Process.Start(start)??throw new StoreOperationException("uninstall_start_failed");
        using var budget=new CancellationTokenSource(TimeSpan.FromMinutes(10));
        try{await process.WaitForExitAsync(budget.Token);return process.ExitCode;}
        catch(OperationCanceledException){throw new StoreOperationException("uninstall_in_progress");}
    }
}

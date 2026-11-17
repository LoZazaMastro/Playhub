// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
using System.Text;
using System.Text.RegularExpressions;

namespace Playhub.StoreLibrary;

internal sealed record WindowsXboxUninstallPlan(StoreGameKey Key, string AccountIdentity, string Family, string FullName, string InstallPath);

public sealed partial class StoreXboxAccountProvider
{
    internal Func<WindowsXboxUninstallPlan, CancellationToken, Task> NativePackageRemove { private get; set; } = RemoveCurrentWindowsPackageAsync;
    internal Action<string> NativeRefuseRunning { private get; set; } = StoreOwnedInstallFiles.RefuseRunning;

    internal async Task<WindowsXboxUninstallPlan> PrepareNativeUninstallAsync(string id, string path, CancellationToken token)
    {
        var owner = LaunchOwner(id);
        var package = await PackageLookup(owner.Family, token);
        var current = LaunchOwner(id);
        if (!ReferenceEquals(current.Account, owner.Account) || current.Identity != owner.Identity || current.Family != owner.Family)
            throw new StoreOperationException("account_changed");
        if (package is null || !package.StatusOk || !string.Equals(package.PackageFamilyName, owner.Family, StringComparison.OrdinalIgnoreCase)
            || owner.LocalInstallation && package.StoreProductId != (owner.InstallProductId??id)
            || !Path.IsPathFullyQualified(path) || !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), package.InstallLocation, StringComparison.OrdinalIgnoreCase)
            || !Regex.IsMatch(package.PackageFullName, @"^[A-Za-z0-9._-]{1,512}$")) throw new StoreOperationException("installation_ownership_lost");
        return new(new(StoreKind.Xbox, id), owner.Identity, owner.Family, package.PackageFullName, package.InstallLocation);
    }
    internal async Task NativeUninstallAsync(WindowsXboxUninstallPlan plan, CancellationToken token)
    {
        var current = await PrepareNativeUninstallAsync(plan.Key.Id, plan.InstallPath, token);
        if (current != plan) throw new StoreOperationException("installation_changed");
        NativeRefuseRunning(plan.InstallPath);
        token.ThrowIfCancellationRequested();
        // After the confirmed request starts, observe the OS operation to its
        // result independently of a closing page. Never remove package files.
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await NativePackageRemove(plan, deadline.Token);
        if (await PackageLookup(plan.Family, deadline.Token) is not null) throw new StoreOperationException("uninstall_unconfirmed");
    }
    static async Task RemoveCurrentWindowsPackageAsync(WindowsXboxUninstallPlan plan, CancellationToken token)
    {
        if (!WindowsXboxPackageCatalog.ValidFamily(plan.Family) || !Regex.IsMatch(plan.FullName, @"^[A-Za-z0-9._-]{1,512}$"))
            throw new StoreOperationException("xbox_package_identity_invalid");
        string family = plan.Family, full = plan.FullName;
        string script = "$ErrorActionPreference='Stop';$p=@(Get-AppxPackage | Where-Object { $_.PackageFamilyName -ceq '" + family
            + "' -and $_.PackageFullName -ceq '" + full + "' -and $_.SignatureKind -eq 'Store' -and -not $_.IsFramework -and -not $_.IsResourcePackage });"
            + "if($p.Count -ne 1){throw 'installation_changed'};Remove-AppxPackage -Package $p[0].PackageFullName -PreserveRoamableApplicationData -ErrorAction Stop";
        string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var result = await StoreToolProcess.QueryAsync(executable, ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))], new Dictionary<string,string>(), token);
        if (result.Code != 0) throw new StoreOperationException("uninstall_failed");
    }
}

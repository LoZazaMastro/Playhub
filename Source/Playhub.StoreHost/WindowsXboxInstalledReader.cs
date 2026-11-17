// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
namespace Playhub.StoreLibrary;

internal sealed class WindowsXboxInstalledReader(string privateDirectory)
{
    internal const string OfficialAumid="Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App";
    internal async Task<XboxInstalledRead> ReadAsync(CancellationToken token)
    {
        if(!OperatingSystem.IsWindows())throw new StoreOperationException("installed_scan_unavailable");
        token.ThrowIfCancellationRequested();
        string directory=Path.GetFullPath(privateDirectory);Directory.CreateDirectory(directory);
        string temporary=Path.Combine(directory,"installed-scan-"+Guid.NewGuid().ToString("N")+".ps1");
        try
        {
            await using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.Read))
            {byte[] bytes=new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(Script)).ToArray();await file.WriteAsync(bytes,token);}
            string powershell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");
            if(!File.Exists(powershell))throw new StoreOperationException("installed_scan_unavailable");
            var result=await StoreToolProcess.QueryAsync(powershell,["-NoLogo","-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-File",temporary],new Dictionary<string,string>(),token);
            if(result.Code!=0)throw new StoreOperationException("installed_scan_failed");
            return Parse(result.Json);
        }
        finally{try{File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
    internal static XboxInstalledRead Parse(string json)
    {
        if(json.Length>2*1024*1024)throw new StoreOperationException("installed_scan_invalid");
        using var doc=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=8});var root=doc.RootElement;
        if(root.GetProperty("Version").GetInt32()!=1||root.GetProperty("Games").ValueKind!=JsonValueKind.Array)throw new StoreOperationException("installed_scan_invalid");
        var games=root.GetProperty("Games").EnumerateArray().Select(g=>new StoreXboxInstalledGame(g.GetProperty("Aumid").GetString()!,g.GetProperty("Name").GetString()!,g.GetProperty("PackageFamilyName").GetString()!)).ToArray();
        return new(games,root.GetProperty("OfficialAppInstalled").GetBoolean(),root.GetProperty("Partial").GetBoolean());
    }
    internal static Task<bool> OpenOfficialAppAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(!OperatingSystem.IsWindows())return Task.FromResult(false);
        object? manager=null;
        try
        {
            manager=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C"),throwOnError:true)!);
            int hr=((IApplicationActivationManager)manager!).ActivateApplication(OfficialAumid,"",2,out uint pid);
            return Task.FromResult(hr==0&&pid>0);
        }
        finally{if(manager is not null&&Marshal.IsComObject(manager))Marshal.FinalReleaseComObject(manager);}
    }
    [ComImport,Guid("2e941141-7f97-4756-ba1d-9decde894a3d"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IApplicationActivationManager
    {
        [PreserveSig]int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)]string aumid,[MarshalAs(UnmanagedType.LPWStr)]string arguments,uint options,out uint processId);
        [PreserveSig]int ActivateForFile([MarshalAs(UnmanagedType.LPWStr)]string aumid,nint items,[MarshalAs(UnmanagedType.LPWStr)]string verb,out uint processId);
        [PreserveSig]int ActivateForProtocol([MarshalAs(UnmanagedType.LPWStr)]string aumid,nint items,out uint processId);
    }
    internal const string Script="""
        $ErrorActionPreference='Stop'
        [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
        # System / Store apps that are NOT games. Exporting these to Steam creates
        # a shortcut to a pure UWP app Steam cannot hook into, which throws
        # "LoadLibrary failed with error 87" at launch. The Xbox app
        # (Microsoft.GamingApp) is the main offender.
        $excludedPackages = @(
          'Microsoft.GamingApp','Microsoft.XboxApp','Microsoft.XboxGameOverlay',
          'Microsoft.XboxGamingOverlay','Microsoft.XboxIdentityProvider',
          'Microsoft.XboxSpeechToTextOverlay','Microsoft.Xbox.TCUI',
          'Microsoft.GamingServices','Microsoft.Windows.GamingApp',
          'Microsoft.WindowsStore','Microsoft.StorePurchaseApp',
          'Microsoft.WindowsCalculator','Microsoft.WindowsCamera',
          'Microsoft.WindowsNotepad','Microsoft.Paint','Microsoft.MicrosoftEdge',
          'Microsoft.MicrosoftEdge.Stable','Microsoft.ZuneMusic','Microsoft.ZuneVideo',
          'Microsoft.Windows.Photos','Microsoft.WindowsTerminal','Microsoft.Todos',
          'Microsoft.PowerAutomateDesktop','Microsoft.GetHelp','Microsoft.People',
          'Microsoft.YourPhone','Microsoft.MicrosoftStickyNotes','Microsoft.ScreenSketch'
        )

        $apps = @()
        $scanErrors=0
        $officialApp=$false
        foreach ($app in Get-AppxPackage) {
          try {
            if ($app.IsFramework -or $app.IsResourcePackage) { continue }
            if ($app.SignatureKind -eq 'System') { continue }
            if ($app.Name -eq 'Microsoft.GamingApp') {
              if ($app.PackageFamilyName -eq 'Microsoft.GamingApp_8wekyb3d8bbwe' -and $app.SignatureKind -eq 'Store') {
                [xml]$officialManifest = Get-AppxPackageManifest $app
                $officialApp = @($officialManifest.Package.Applications.Application | Where-Object { $_.Id -eq 'Microsoft.Xbox.App' }).Count -eq 1
              }
              continue
            }
            if ($excludedPackages -contains $app.Name) { continue }
            [xml]$manifest = Get-AppxPackageManifest $app
            foreach ($application in $manifest.Package.Applications.Application) {
              $name = [string]$manifest.Package.Properties.DisplayName
              if ([string]::IsNullOrWhiteSpace($name) -or $name -like '*ms-resource*' -or $name -like '*DisplayName*') { continue }
              $executable = [string]$application.Executable
              if ([string]::IsNullOrWhiteSpace($executable) -or $executable -eq 'GameLaunchHelper.exe') {
                $config = Join-Path $app.InstallLocation 'MicrosoftGame.Config'
                if (Test-Path $config) {
                  [xml]$gameConfig = Get-Content $config
                  $executable = [string]$gameConfig.Game.ExecutableList.Executable.Name
                } else {
                  continue
                }
              }
              if ($executable -is [Object[]]) { $executable = [string]$executable[1] }
              $visual = $application.VisualElements
              $logo = if ($visual -and $visual.Square150x150Logo) { Join-Path $app.InstallLocation ([string]$visual.Square150x150Logo) } else { '' }
              $apps += [pscustomobject]@{
                Name = $name
                Aumid = "$($app.PackageFamilyName)!$($application.Id)"
                Executable = $executable
                Logo = $logo
                PackageFamilyName = $app.PackageFamilyName
              }
            }
          } catch { $scanErrors++ }
        }
        [pscustomobject]@{ Version=1; Games=@($apps | Sort-Object Aumid -Unique); Partial=($scanErrors -gt 0); OfficialAppInstalled=$officialApp } | ConvertTo-Json -Depth 5 -Compress
        """;
}

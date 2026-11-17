# SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
# Windows PowerShell hosts the inbox WinRT projection. No Store/Xbox window opens.
param(
 [ValidateSet('locations','install','observe')][string]$Action='locations',
 [string]$ProductId='', [string]$VolumeName='', [string]$FamilyName=''
)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false)
Add-Type -AssemblyName System.Runtime.WindowsRuntime
Add-Type -TypeDefinition @'
using System;
using System.Threading.Tasks;
public static class PlayhubInstallInput {
 public static Task<string> Read() { return Task.Run<string>(delegate { return Console.In.ReadLine(); }); }
}
'@
function Send($value) { $value.protocol=1; [Console]::Out.WriteLine(($value | ConvertTo-Json -Depth 5 -Compress)); [Console]::Out.Flush() }
function HResult($error) {
 $exception=$error.Exception
 while($exception.InnerException){$exception=$exception.InnerException}
 '0x'+$exception.HResult.ToString('X8')
}
function Await-Operation($operation,[type]$resultType) {
 $adapter=[System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {$_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetGenericArguments().Count -eq 1 -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'} | Select-Object -First 1
 $task=$adapter.MakeGenericMethod($resultType).Invoke($null,@($operation))
 if(!$task.Wait(45000)){throw [TimeoutException]::new('Native installation request timed out')}
 $task.GetAwaiter().GetResult()
}
function Get-InstallLeaves($roots,[switch]$All) {
 $pending=[Collections.Generic.Queue[object]]::new()
 foreach($root in $roots){$pending.Enqueue(@{item=$root;depth=0})}
 # Product/PFN may be shared by separate full-game phases. Deduplicate the
 # actual returned item, never discard a distinct phase with the same identity.
 $seen=[Collections.Generic.HashSet[object]]::new()
 $count=0
 while($pending.Count) {
  $node=$pending.Dequeue();$item=$node.item
  if(!$seen.Add($item)){continue}
  $count++
  if($count -gt 256 -or $node.depth -gt 8){throw [IO.InvalidDataException]::new('Invalid installation group')}
  if($All){$item}
  $children=@($item.Children | Where-Object {$null -ne $_})
  if(@($children | Where-Object {[object]::ReferenceEquals($_,$item)}).Count){throw [IO.InvalidDataException]::new('Invalid cyclic installation group')}
  if($children.Count){foreach($child in $children){$pending.Enqueue(@{item=$child;depth=$node.depth+1})}}
  elseif(!$All){$item}
 }
}
function Confirm-Destination([switch]$ReadOnly) {
 if(!$VolumeName){return}
 $selectedVolume=@($volumes | Where-Object {$_.Name -ceq $VolumeName})
 $package=@(Get-AppxPackage | Where-Object {$_.PackageFamilyName -ceq $FamilyName})
 if($selectedVolume.Count -ne 1 -or $package.Count -ne 1){throw [IO.IOException]::new('Installation destination not confirmed')}
 $directory=Get-Item -LiteralPath $package[0].InstallLocation -Force
 $physical=if($directory.LinkType -eq 'Junction' -and @($directory.Target).Count -eq 1){[string]$directory.Target[0]}else{$directory.FullName}
 if([IO.Path]::GetPathRoot($physical) -ine [IO.Path]::GetPathRoot($selectedVolume[0].PackageStorePath)) {
  if($ReadOnly){throw [IO.IOException]::new('Installation destination not confirmed')}
  $appxVolume=@(Get-AppxVolume | Where-Object {$_.Name -ceq $VolumeName})
  if($appxVolume.Count -ne 1){throw [IO.IOException]::new('Installation destination not confirmed')}
  Move-AppxPackage -Package $package[0].PackageFullName -Volume $appxVolume[0] -ErrorAction Stop
  $directory=Get-Item -LiteralPath $package[0].InstallLocation -Force
  $physical=if($directory.LinkType -eq 'Junction' -and @($directory.Target).Count -eq 1){[string]$directory.Target[0]}else{$directory.FullName}
  if([IO.Path]::GetPathRoot($physical) -ine [IO.Path]::GetPathRoot($selectedVolume[0].PackageStorePath)){throw [IO.IOException]::new('Installation destination not confirmed')}
 }
}
try {
 $deploymentManager=[Windows.Management.Deployment.PackageManager,Windows.Management.Deployment,ContentType=WindowsRuntime]::new()
 $volumes=@($deploymentManager.FindPackageVolumes() | Where-Object {!$_.IsOffline -and $_.IsFullTrustPackageSupported})
 if($Action -eq 'locations') {
  $locations=@(foreach($volume in $volumes) {
   $driveRoot=[IO.Path]::GetPathRoot($volume.PackageStorePath)
   if($driveRoot -notmatch '^[A-Za-z]:\\$'){continue}
   $drive=[IO.DriveInfo]::new($driveRoot)
   if($drive.IsReady){[ordered]@{volumeName=$volume.Name;rootPath=$driveRoot;freeBytes=$drive.AvailableFreeSpace;isReady=$true}}
  })
  Send @{kind='locations';locations=@($locations | Sort-Object rootPath)}
  exit 0
 }
 if($ProductId -cnotmatch '^[A-Z0-9]{12}$' -or $FamilyName -cnotmatch '^[A-Za-z0-9._-]{1,240}_[a-z0-9]{13}$'){throw [ArgumentException]::new('Invalid product identity')}
 $manager=[Windows.ApplicationModel.Store.Preview.InstallControl.AppInstallManager,Windows.ApplicationModel.Store.Preview.InstallControl,ContentType=WindowsRuntime]::new()
 if($Action -eq 'install') {
  $options=[Windows.ApplicationModel.Store.Preview.InstallControl.AppInstallOptions,Windows.ApplicationModel.Store.Preview.InstallControl,ContentType=WindowsRuntime]::new()
  if($VolumeName) {
   $selected=@($volumes | Where-Object {$_.Name -ceq $VolumeName})
   if($selected.Count -ne 1){Send @{kind='error';code='windows_store_destination_unavailable'};exit 2}
   $options.TargetVolume=$selected[0]
  }
  $options.LaunchAfterInstall=$false; $options.AllowForcedAppRestart=$false; $options.InstallForAllUsers=$false
  $options.InstallInProgressToastNotificationMode='NoToast';$options.CompletedInstallToastNotificationMode='NoToast'
  $items=@(Await-Operation ($manager.StartProductInstallAsync($ProductId,'','Playhub','',$options)) ([System.Collections.Generic.IReadOnlyList[Windows.ApplicationModel.Store.Preview.InstallControl.AppInstallItem]]))
 } else {
  $items=@($manager.AppInstallItemsWithGroupSupport | Where-Object {$_.ProductId -ceq $ProductId -or $_.PackageFamilyName -ceq $FamilyName})
 }
 if(!$items.Count){
  # Completed items disappear from InstallControl after host restart. Registration
  # alone is insufficient: require explicit native full-staging/deployment flags.
  if($Action -eq 'observe') {
   $registered=@(Get-AppxPackage | Where-Object {$_.PackageFamilyName -ceq $FamilyName})
   if($registered.Count -eq 1) {
    $native=$deploymentManager.FindPackageForUser('', $registered[0].PackageFullName)
    if($null -ne $native -and $native.IsPartiallyStaged -is [bool] -and !$native.IsPartiallyStaged -and $native.Status.DeploymentInProgress -is [bool] -and !$native.Status.DeploymentInProgress -and $native.Status.VerifyIsOK() -and !$native.IsStub) {
     Confirm-Destination -ReadOnly
     Send @{kind='completed';productId=$ProductId;finalDeploymentConfirmed=$true};exit 0
    }
   }
  }
  Send @{kind='error';code='windows_store_install_not_started'};exit 3
 }
 # Only objects returned by this install (or its exact retained product/family)
 # receive control commands. Never enumerate/cancel unrelated Store requests.
 Send @{kind='accepted';productId=$ProductId;itemCount=$items.Count;volumeName=$VolumeName}
 if($Action -eq 'install') {
  foreach($item in $items){if($item.GetCurrentStatus().InstallState.ToString() -eq 'Error'){$item.Restart()}}
 }
 $inputTask=[PlayhubInstallInput]::Read();$watch=[Diagnostics.Stopwatch]::StartNew();$lastBytes=$null;$lastSeconds=0.0;$lastPhase='';$lastTotal=0
 while($true) {
  if($inputTask.IsCompleted) {
   $command=$inputTask.GetAwaiter().GetResult()
   if($null -eq $command -or $command -eq 'detach'){Send @{kind='detached'};exit 0}
   if($command -eq 'cancel'){foreach($item in $items){$item.Cancel()}}
   elseif($command -eq 'pause'){foreach($item in $items){$item.Pause()}}
   elseif($command -eq 'resume'){foreach($item in $items){$item.Restart()}}
   $inputTask=[PlayhubInstallInput]::Read()
  }
  $statuses=@(Get-InstallLeaves $items | ForEach-Object {$_.GetCurrentStatus()})
  if(!$statuses.Count){throw [IO.InvalidDataException]::new('Missing installation status')}
  $allStatuses=@(Get-InstallLeaves $items -All | ForEach-Object {$_.GetCurrentStatus()})
  $states=@($allStatuses | ForEach-Object {$_.InstallState.ToString()})
  $failed=@($statuses | Where-Object {$_.InstallState.ToString() -eq 'Error'})
  if($failed.Count) {
   $hr=if($null -ne $failed[0].ErrorCode){'0x'+$failed[0].ErrorCode.HResult.ToString('X8')}else{$null}
   Send @{kind='error';code='windows_store_install_failed';hresult=$hr};exit 4
  }
  if(@($states | Where-Object {$_ -ne 'Completed'}).Count -eq 0){
   [long]$finalBytes=0;[long]$finalTotal=0
   foreach($status in $statuses){$finalBytes+=[long]$status.BytesDownloaded;$finalTotal+=[long]$status.DownloadSizeInBytes}
   Send @{kind='progress';phase='installing';percent=100;downloadedBytes=$finalBytes;totalBytes=$finalTotal;bytesPerSecond=$null;finalDeploymentConfirmed=$true}
   Confirm-Destination;Send @{kind='completed';productId=$ProductId};exit 0
  }
  if(@($states | Where-Object {$_ -notin @('Canceled','Cancelled','Completed')}).Count -eq 0){Send @{kind='canceled';productId=$ProductId};exit 5}
  [long]$downloaded=0;[long]$total=0;[double]$percent=0
  foreach($status in $statuses){$downloaded+=[long]$status.BytesDownloaded;$total+=[long]$status.DownloadSizeInBytes;$percent+=[double]$status.PercentComplete}
  $percent=if($total -gt 1){[Math]::Min(100.0,100.0*$downloaded/$total)}else{$percent/$statuses.Count}
  $phase=if($states -contains 'Downloading'){'downloading'}elseif($states -contains 'Installing'){'installing'}elseif($states -contains 'Paused'){'paused'}else{'starting'}
  $seconds=$watch.Elapsed.TotalSeconds;$speed=$null
  if($phase -eq 'downloading' -and $lastPhase -eq 'downloading' -and $total -gt 1 -and $lastTotal -eq $total -and $null -ne $lastBytes -and $downloaded -ge $lastBytes -and $seconds -gt $lastSeconds){$speed=($downloaded-$lastBytes)/($seconds-$lastSeconds)}
  Send @{kind='progress';phase=$phase;percent=$percent;downloadedBytes=$downloaded;totalBytes=$total;bytesPerSecond=$speed}
  $lastBytes=$downloaded;$lastSeconds=$seconds;$lastPhase=$phase;$lastTotal=$total
  Start-Sleep -Milliseconds 500
 }
}catch{Send @{kind='error';code='windows_store_install_failed';hresult=(HResult $_)};exit 6}

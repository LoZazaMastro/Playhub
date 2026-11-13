$ErrorActionPreference='Stop'
$path=Join-Path $PSScriptRoot '..\Playhub\Assets\GamingMode\retire-gamebar-watcher.ps1'
$source=[IO.File]::ReadAllText($path);$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseInput($source,[ref]$tokens,[ref]$errors)
function Assert($condition,$label){if(-not $condition){throw $label};Write-Output "PASS $label"}
Assert ($errors.Count -eq 0) 'Production syntax'
foreach($name in @('Test-LegacyWatcherIdentity','Test-LegacyWatcherHandle')){
 $node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
 . ([scriptblock]::Create($node.Extent.Text))
}
$exe='C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe';$script='C:\Playhub\Assets\GamingMode\xbox-gamebar.ps1'
$birth=[DateTime]::UtcNow
$snapshot=[pscustomobject]@{SessionId=2;ExecutablePath=$exe;CommandLine=('"'+$exe+'" -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "'+$script+'"');CreationDate=$birth}
Assert (Test-LegacyWatcherIdentity $snapshot $exe $script 2) 'Exact original launcher accepted'
Assert (-not (Test-LegacyWatcherIdentity $snapshot $exe 'C:\Other\xbox-gamebar.ps1' 2)) 'Different installation excluded'
Assert (-not (Test-LegacyWatcherIdentity $snapshot $exe $script 3)) 'Other session excluded'
$snapshot.CommandLine+=' -Extra arbitrary'
Assert (-not (Test-LegacyWatcherIdentity $snapshot $exe $script 2)) 'Additional arguments excluded'
$process=[pscustomobject]@{HasExited=$false;SessionId=2;StartTime=$birth;MainModule=[pscustomobject]@{FileName=$exe}}
Assert (Test-LegacyWatcherHandle $process $snapshot $exe 2) 'Retained handle identity matches'
$process.StartTime=$birth.AddTicks(4)
Assert (Test-LegacyWatcherHandle $process $snapshot $exe 2) 'CIM sub-microsecond truncation accepted'
$process.StartTime=$birth.AddTicks(10)
Assert (-not (Test-LegacyWatcherHandle $process $snapshot $exe 2)) 'Different birth beyond CIM precision excluded'
$process.StartTime=$birth.AddSeconds(1)
Assert (-not (Test-LegacyWatcherHandle $process $snapshot $exe 2)) 'PID reuse excluded by birth'
$process.StartTime=$birth;$process.HasExited=$true
Assert (-not (Test-LegacyWatcherHandle $process $snapshot $exe 2)) 'Exited original excluded'
$process.HasExited=$false;$process.MainModule.FileName='C:\Other\powershell.exe'
Assert (-not (Test-LegacyWatcherHandle $process $snapshot $exe 2)) 'Handle executable mismatch excluded'
Assert (-not $source.Contains('Set-ItemProperty')) 'No user registry writes'

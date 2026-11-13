# Upgrade-only retirement of the obsolete Playhub watcher. Does not change user settings.
$ErrorActionPreference = 'Stop'

function Test-LegacyWatcherIdentity($Snapshot, [string]$Executable, [string]$Script, [int]$Session) {
    $arguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + $Script + '"'
    return $Snapshot.SessionId -eq $Session -and
        [string]::Equals($Snapshot.ExecutablePath, $Executable, [StringComparison]::OrdinalIgnoreCase) -and
        [string]::Equals($Snapshot.CommandLine, ('"' + $Executable + '" ' + $arguments), [StringComparison]::OrdinalIgnoreCase)
}

function Test-LegacyWatcherHandle($Process, $Snapshot, [string]$Executable, [int]$Session) {
    if ($Process.HasExited -or $Process.SessionId -ne $Session) { return $false }
    if ([Math]::Abs(($Process.StartTime.ToUniversalTime() - $Snapshot.CreationDate.ToUniversalTime()).Ticks) -ge 10) { return $false }
    return [string]::Equals($Process.MainModule.FileName, $Executable, [StringComparison]::OrdinalIgnoreCase)
}

function Stop-LegacyWatcher($Snapshot, [string]$Executable, [string]$Script, [int]$Session, [string]$Sid) {
    if (-not (Test-LegacyWatcherIdentity $Snapshot $Executable $Script $Session)) { return }
    $owner = Invoke-CimMethod -InputObject $Snapshot -MethodName GetOwnerSid
    if ($owner.ReturnValue -ne 0 -or $owner.Sid -ne $Sid) { return }
    $process = $null
    try {
        $process = [Diagnostics.Process]::GetProcessById([int]$Snapshot.ProcessId)
        # Cache the handle before checking birth: Kill then uses this object, never a new PID lookup.
        $null = $process.Handle
        if (-not (Test-LegacyWatcherHandle $process $Snapshot $Executable $Session)) { return }
        $process.Kill()
        $null = $process.WaitForExit(2000)
        Write-Output ('Retired obsolete Playhub Game Bar watcher PID ' + $Snapshot.ProcessId)
    }
    finally { if ($null -ne $process) { $process.Dispose() } }
}

$executable = Join-Path ([Environment]::GetFolderPath('System')) 'WindowsPowerShell\v1.0\powershell.exe'
$script = Join-Path $PSScriptRoot 'xbox-gamebar.ps1'
$session = [Diagnostics.Process]::GetCurrentProcess().SessionId
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
foreach ($candidate in (Get-CimInstance Win32_Process -Filter "Name='powershell.exe'")) {
    try { Stop-LegacyWatcher $candidate $executable $script $session $sid }
    catch { Write-Warning ('Legacy watcher retirement deferred: ' + $_.Exception.Message) }
}

$ErrorActionPreference = 'Stop'

$InstallDir = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'GamingMode'))
$ExpectedExe = Join-Path $InstallDir 'GamingMode.exe'
$ConfigDir = [IO.Path]::GetFullPath((Join-Path $env:APPDATA 'GamingMode'))
$DisabledMarker = Join-Path $ConfigDir 'disabled-by-user'
New-Item -ItemType Directory -Path $ConfigDir -Force | Out-Null
[IO.File]::WriteAllText($DisabledMarker, 'Gaming Mode was removed by the user.')


# Restore a desktop boot before removing the component that owned the shell.
$Winlogon = 'HKCU:\Software\Microsoft\Windows NT\CurrentVersion\Winlogon'
$ShellValue = (Get-ItemProperty -LiteralPath $Winlogon -Name Shell -ErrorAction SilentlyContinue).Shell
if ($ShellValue -and $ShellValue.IndexOf($ExpectedExe, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
    Remove-ItemProperty -LiteralPath $Winlogon -Name Shell -ErrorAction Stop
}
$Config = Join-Path $ConfigDir 'config.json'
if (Test-Path -LiteralPath $Config) {
    $Settings = Get-Content -LiteralPath $Config -Raw | ConvertFrom-Json
    $Settings | Add-Member -NotePropertyName defaultMode -NotePropertyValue 'Desktop' -Force
    $Settings | Add-Member -NotePropertyName nextBootMode -NotePropertyValue $null -Force
    $Temporary = Join-Path $ConfigDir 'config.uninstall.tmp'
    [IO.File]::WriteAllText($Temporary, ($Settings | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $Temporary -Destination $Config -Force
}

$StartupShortcut = Join-Path ([Environment]::GetFolderPath('Startup')) 'Gaming Mode Agent.lnk'
if (Test-Path -LiteralPath $StartupShortcut) { Remove-Item -LiteralPath $StartupShortcut -Force }
$CurrentSession = [Diagnostics.Process]::GetCurrentProcess().SessionId
foreach ($Process in @(Get-Process -Name 'GamingMode' -ErrorAction SilentlyContinue)) {
    try {
        # A process name alone does not prove ownership. Never stop another install/session.
        if ($Process.SessionId -ne $CurrentSession) { continue }
        if (-not [string]::Equals([IO.Path]::GetFullPath($Process.Path), $ExpectedExe, [StringComparison]::OrdinalIgnoreCase)) { continue }
        $Process.Kill()
        if (-not $Process.WaitForExit(10000)) { throw 'Gaming Mode did not stop.' }
    } finally { $Process.Dispose() }
}

# Restore the Windows desktop if Gaming Mode had closed it in this session.
$DesktopRunning = @(Get-Process -Name explorer -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -eq $CurrentSession }).Count -gt 0
if (-not $DesktopRunning) {
    Start-Process -FilePath (Join-Path $env:WINDIR 'explorer.exe') -WindowStyle Hidden
}

# Desktop restoration precedes optional helper cleanup, including any failure.
. (Join-Path $PSScriptRoot 'owned-helpers.ps1')
$AppRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
Stop-OwnedGamingModeHelpers (Join-Path $AppRoot 'Assets\GamingMode')

$DesktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Gaming Mode.lnk'
$StartMenuRoot = [IO.Path]::GetFullPath((Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'))
$StartMenuDir = [IO.Path]::GetFullPath((Join-Path $StartMenuRoot 'Gaming Mode'))
# Verify recursive targets against their explicitly defined owner directories.
if ([IO.Path]::GetDirectoryName($InstallDir) -ne [IO.Path]::GetFullPath($env:LOCALAPPDATA).TrimEnd('\') -or
    [IO.Path]::GetDirectoryName($StartMenuDir) -ne $StartMenuRoot) { throw 'Invalid removal path.' }
$Failures = @()
foreach ($Path in @($DesktopShortcut, $StartMenuDir, $InstallDir)) {
    if (-not (Test-Path -LiteralPath $Path)) { continue }
    for ($Attempt = 0; $Attempt -lt 20; $Attempt++) {
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            break
        } catch {
            if ($Attempt -eq 19) { $Failures += ('{0}: {1}' -f $Path, $_.Exception.Message) }
            else { Start-Sleep -Milliseconds 250 }
        }
    }
}
if ($Failures.Count) { throw ('Gaming Mode removal failed: ' + ($Failures -join ' | ')) }
Write-Host 'Gaming Mode was removed. Playhub and its Decky plugin remain available.'

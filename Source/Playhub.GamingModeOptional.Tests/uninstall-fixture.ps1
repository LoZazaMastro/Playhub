$ErrorActionPreference = 'Stop'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('playhub-uninstall-' + [Guid]::NewGuid().ToString('N'))
$originalLocal = $env:LOCALAPPDATA
$originalRoaming = $env:APPDATA
$global:fixtureStopped = @()
$global:fixtureRegistryRestored = $false
$global:fixtureDesktopRestored = $false
$session = [Diagnostics.Process]::GetCurrentProcess().SessionId
try {
    $env:LOCALAPPDATA = Join-Path $fixtureRoot 'local'
    $env:APPDATA = Join-Path $fixtureRoot 'roaming'
    $installation = Join-Path $env:LOCALAPPDATA 'GamingMode'
    $configDir = Join-Path $env:APPDATA 'GamingMode'
    New-Item -ItemType Directory -Path $installation, $configDir -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $installation 'GamingMode.exe'), 'fixture')
    [IO.File]::WriteAllText((Join-Path $configDir 'config.json'), '{"defaultMode":"Gaming","nextBootMode":"Gaming","gaming":{"preserveMe":42},"custom":{"x":true}}')
    function New-TestProcess($Path, $Id, $SessionId) {
        $process = [pscustomobject]@{Path=$Path;Id=$Id;SessionId=$SessionId}
        $process | Add-Member ScriptMethod Kill { $global:fixtureStopped += $this.Id }
        $process | Add-Member ScriptMethod WaitForExit { param($timeout) return $true }
        $process | Add-Member ScriptMethod Dispose { }
        return $process
    }
    $global:fixtureProcesses = @(
        (New-TestProcess (Join-Path $installation 'GamingMode.exe') 101 $session),
        (New-TestProcess (Join-Path $fixtureRoot 'other\GamingMode.exe') 102 $session),
        (New-TestProcess (Join-Path $installation 'GamingMode.exe') 103 ($session + 1))
    )
    function Get-Process { param($Name,$ErrorAction) if ($Name -eq 'GamingMode') { return $global:fixtureProcesses } return @() }
    function Get-ItemProperty { param($LiteralPath,$Name,$ErrorAction) return @{Shell=('"' + (Join-Path $installation 'GamingMode.exe') + '" shell')} }
    function Remove-ItemProperty { param($LiteralPath,$Name,$ErrorAction) $global:fixtureRegistryRestored = $true }
    function Start-Process { param($FilePath,$WindowStyle) $global:fixtureDesktopRestored = $FilePath.EndsWith('explorer.exe') }
    function Test-Path {
        param($LiteralPath,$Path)
        $target = if ($LiteralPath) { $LiteralPath } else { $Path }
        if (-not $target.StartsWith($fixtureRoot,[StringComparison]::OrdinalIgnoreCase)) { return $false }
        Microsoft.PowerShell.Management\Test-Path -LiteralPath $target
    }
    function Remove-Item {
        param($LiteralPath,[switch]$Recurse,[switch]$Force,$ErrorAction)
        if (-not [IO.Path]::GetFullPath($LiteralPath).StartsWith($fixtureRoot + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture attempted outside removal' }
        Microsoft.PowerShell.Management\Remove-Item -LiteralPath $LiteralPath -Recurse:$Recurse -Force:$Force -ErrorAction Stop
    }
    & (Join-Path $PSScriptRoot '..\Playhub\Plugins\Gaming Mode\gaming-mode-win-x64\uninstall.ps1')
    if (($global:fixtureStopped -join ',') -ne '101') { throw 'Uninstaller stopped unowned processes' }
    if (-not $global:fixtureRegistryRestored -or -not $global:fixtureDesktopRestored) { throw 'Desktop recovery missing' }
    if (Microsoft.PowerShell.Management\Test-Path -LiteralPath $installation) { throw 'Installation was not removed' }
    if (-not (Microsoft.PowerShell.Management\Test-Path -LiteralPath (Join-Path $configDir 'disabled-by-user'))) { throw 'Opt-out marker missing' }
    $settings = Get-Content -LiteralPath (Join-Path $configDir 'config.json') -Raw | ConvertFrom-Json
    if ($settings.defaultMode -ne 'Desktop' -or $null -ne $settings.nextBootMode -or $settings.gaming.preserveMe -ne 42 -or $settings.custom.x -ne $true) { throw 'User preferences not preserved' }
    Write-Host 'PASS isolated uninstall: only owned process stopped, desktop restored, files removed, opt-out and preferences retained.'
} finally {
    $env:LOCALAPPDATA = $originalLocal
    $env:APPDATA = $originalRoaming
    if ([IO.Path]::GetDirectoryName($fixtureRoot).TrimEnd('\') -ne [IO.Path]::GetTempPath().TrimEnd('\')) { throw 'Invalid fixture cleanup' }
    Microsoft.PowerShell.Management\Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
}

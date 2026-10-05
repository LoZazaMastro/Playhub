$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
. (Join-Path $repo 'Source\Playhub\Plugins\Gaming Mode\gaming-mode-win-x64\owned-helpers.ps1')
$checks = 0
function Assert-Check([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "FAIL $Name" }
    $script:checks++
    Write-Output "PASS $Name"
}
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('Playhub-owned-helper-' + [Guid]::NewGuid().ToString('N'))
$assets = Join-Path $fixture 'Assets with spaces'
$shell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$scripts = @('desktop-safety.ps1', 'focus-rescue.ps1', 'xbox-gamebar.ps1') | ForEach-Object { Join-Path $assets $_ }
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$session = [Diagnostics.Process]::GetCurrentProcess().SessionId
$goodCommand = '"' + $shell + '" -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + $scripts[1] + '"'
$record = [pscustomobject]@{SessionId=$session; ExecutablePath=$shell; CommandLine=$goodCommand}
function Test-Record($candidate = $record, $owner = $sid) { Test-OwnedGamingModeHelper $candidate $owner $session $sid @($shell) $scripts }
Assert-Check (Test-Record) 'Quoted exact owned helper accepted'
Assert-Check (-not (Test-Record -owner 'S-1-5-18')) 'Different user excluded'
$record.SessionId++
Assert-Check (-not (Test-Record)) 'Different session excluded'
$record.SessionId = $session
$record.ExecutablePath = $shell.Replace('System32', 'Other')
Assert-Check (-not (Test-Record)) 'Other PowerShell image excluded'
$record.ExecutablePath = $shell
foreach ($invalid in @(
    ($goodCommand + ' extra')
    $goodCommand.Replace('focus-rescue.ps1', 'focus-rescue.ps1.other')
    $goodCommand.Replace('Assets with spaces', 'Assets with spaces-other')
    ('"' + $shell + '" -Command "Write-Output -File ' + $scripts[1] + '"')
    $goodCommand.Replace('-File', '-f')
    $goodCommand.Replace('-NoProfile', '-EncodedCommand')
    $goodCommand.Replace($shell, 'C:\Other\powershell.exe')
    $goodCommand.Replace('-ExecutionPolicy Bypass', '-ExecutionPolicy RemoteSigned')
    $goodCommand.Replace($scripts[1], 'focus-rescue.ps1')
)) {
    $record.CommandLine = $invalid
    Assert-Check (-not (Test-Record)) 'Unowned or ambiguous command excluded'
}
$record.CommandLine = $null
Assert-Check (-not (Test-Record)) 'Unreadable command line excluded'
$record.CommandLine = $goodCommand
$oldAppData = $env:APPDATA
$owned = $unrelated = $null
try {
    New-Item -ItemType Directory -Path $assets -Force | Out-Null
    foreach ($path in $scripts) { [IO.File]::WriteAllText($path, 'Start-Sleep -Seconds 60') }
    $other = Join-Path $assets 'unrelated.ps1'
    [IO.File]::WriteAllText($other, 'Start-Sleep -Seconds 60')
    $owned = Start-Process -FilePath $shell -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + $scripts[1] + '"') -WindowStyle Hidden -PassThru
    $unrelated = Start-Process -FilePath $shell -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + $other + '"') -WindowStyle Hidden -PassThru
    Start-Sleep -Milliseconds 300
    Stop-OwnedGamingModeHelpers $assets
    Assert-Check ($owned.WaitForExit(3000)) 'Real isolated same-user same-session helper terminated'
    Assert-Check (-not $unrelated.HasExited) 'Real unrelated isolated PowerShell preserved'
    $env:APPDATA = Join-Path $fixture 'roaming'
    $markerDirectory = Join-Path $env:APPDATA 'GamingMode'
    New-Item -ItemType Directory -Path $markerDirectory -Force | Out-Null
    foreach ($name in @('focus-rescue.ps1', 'xbox-gamebar.ps1')) {
        $path = Join-Path $repo "Source\Playhub\Assets\GamingMode\$name"
        $tokens = $null; $errors = $null
        $ast = [Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors)
        Assert-Check ($errors.Count -eq 0) "$name production syntax"
        $guard = $ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Test-GamingModeOptOut'}, $true)
        . ([scriptblock]::Create($guard.Extent.Text))
        Assert-Check (-not (Test-GamingModeOptOut)) "$name opt-in guard"
        $marker = Join-Path $markerDirectory 'disabled-by-user'
        [IO.File]::WriteAllText($marker, 'isolated fixture')
        Assert-Check (Test-GamingModeOptOut) "$name opt-out guard"
        $child = Start-Process -FilePath $shell -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + $path + '"') -WindowStyle Hidden -PassThru
        try {
            Assert-Check ($child.WaitForExit(5000) -and $child.ExitCode -eq 0) "$name full production script exits before startup effects when opted out"
        } finally { if (-not $child.HasExited) { $child.Kill() }; $child.Dispose() }
        Remove-Item -LiteralPath $marker
        $loop = $ast.Find({param($node) $node -is [Management.Automation.Language.WhileStatementAst] -and $node.Extent.Text.StartsWith('while ($true)')}, $true)
        & {
            $lastSteamCheck = (Get-Date).AddSeconds(-6)
            $steamSeen = $false; $steamGoneSince = $null; $currentlyOn = $false
            $listener = [pscustomobject]@{}
            $listener | Add-Member -MemberType ScriptMethod -Name Pending -Value { return $false }
            $script:fixtureSleeps = 0; $script:fixtureGameBarWrites = @(); $script:fixtureLogs = @()
            if ($name -eq 'focus-rescue.ps1') {
                $wait = $ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Wait-FocusRequest'}, $true)
                . ([scriptblock]::Create($wait.Extent.Text))
                $socket = [pscustomobject]@{}
                $socket | Add-Member -MemberType ScriptMethod -Name Poll -Value {
                    param($timeout, $mode)
                    $script:fixtureSleeps++
                    if ($script:fixtureSleeps -gt 2) { throw 'Production socket loop ignored opt-out' }
                    [IO.File]::WriteAllText($marker, 'isolated fixture')
                    return $false
                }
                $listener | Add-Member -MemberType NoteProperty -Name Server -Value $socket
                function Get-Date { return [DateTime]::Now.AddSeconds($script:fixtureSleeps * 6) }
            }
            function Get-Process { return [pscustomobject]@{ Id=1 } }
            function Test-SteamControllerProfileActive { return $true }
            function Get-GameBarState { return 0 }
            function Set-GameBar([int]$Value) { $script:fixtureGameBarWrites += $Value; return $true }
            function Write-Log([string]$Message) { $script:fixtureLogs += $Message }
            function Start-Sleep {
                $script:fixtureSleeps++
                if ($script:fixtureSleeps -gt 2) { throw 'Production loop ignored opt-out' }
                [IO.File]::WriteAllText($marker, 'isolated fixture')
                Set-Variable -Name lastSteamCheck -Value ((Get-Date).AddSeconds(-6)) -Scope 1
            }
            . ([scriptblock]::Create($loop.Extent.Text))
            Assert-Check ($script:fixtureSleeps -eq 1 -and ($script:fixtureLogs -join ' ').Contains('helper stopped')) "$name production loop stops on new opt-out"
            if ($name -eq 'xbox-gamebar.ps1') { Assert-Check ($script:fixtureGameBarWrites.Count -eq 1 -and $script:fixtureGameBarWrites[0] -eq 0) 'Game Bar loop disables controller trigger before exiting' }
        }
        Remove-Item -LiteralPath $marker
    }
    $uninstall = [IO.File]::ReadAllText((Join-Path $repo 'Source\Playhub\Plugins\Gaming Mode\gaming-mode-win-x64\uninstall.ps1'))
    Assert-Check ($uninstall.IndexOf('WriteAllText($DisabledMarker') -lt $uninstall.IndexOf('Stop-OwnedGamingModeHelpers')) 'Opt-out persisted before helper cleanup'
    Assert-Check ($uninstall.IndexOf("Start-Process -FilePath (Join-Path `$env:WINDIR 'explorer.exe')") -lt $uninstall.IndexOf('Stop-OwnedGamingModeHelpers')) 'Explorer restore precedes helper cleanup failure'
    Assert-Check ($uninstall.Contains("Stop-OwnedGamingModeHelpers (Join-Path `$AppRoot 'Assets\GamingMode')")) 'Cleanup limited to packaged installation assets'
    Assert-Check ($uninstall.IndexOf("Start-Process -FilePath (Join-Path `$env:WINDIR 'explorer.exe')") -lt $uninstall.IndexOf(". (Join-Path `$PSScriptRoot 'owned-helpers.ps1')")) 'Explorer restore precedes helper dependency loading'
} finally {
    $env:APPDATA = $oldAppData
    foreach ($process in @($owned, $unrelated)) {
        if ($null -ne $process) { if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit(3000) | Out-Null }; $process.Dispose() }
    }
    $resolved = [IO.Path]::GetFullPath($fixture)
    if ($resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase) -and (Split-Path $resolved -Leaf).StartsWith('Playhub-owned-helper-')) {
        Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction SilentlyContinue
    }
}
Write-Output "$checks helper ownership and opt-out checks passed. Only disposable fixture processes were terminated."

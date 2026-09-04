<#
  Verified release build: offline tests -> native UI review -> both installers.
  Writes release-verify-log.txt and release-verify-state.txt next to this script,
  so progress can be followed without watching the console.
#>
param([switch]$SkipReview, [switch]$SkipTests)

$ErrorActionPreference = 'Continue'
$setupRoot = $PSScriptRoot
$root = [IO.Path]::GetFullPath((Join-Path $setupRoot '..\..'))
$log = Join-Path $setupRoot 'release-verify-log.txt'
$state = Join-Path $setupRoot 'release-verify-state.txt'

$shell = $null
$pwshCommand = Get-Command pwsh -ErrorAction SilentlyContinue
if ($pwshCommand) { $shell = $pwshCommand.Source }
if (-not $shell) { $shell = (Get-Command powershell -ErrorAction SilentlyContinue).Source }

Set-Content -LiteralPath $log -Value "===== PLAYHUB VERIFIED RELEASE $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') =====" -Encoding utf8
Set-Content -LiteralPath $state -Value 'RUNNING' -Encoding ascii
function Note([string]$text) { Add-Content -LiteralPath $log -Value $text -Encoding utf8 }
Note "root=$root"
Note "shell=$shell"

function Step([string]$name, [scriptblock]$body) {
    Note ""
    Note "===== $name ====="
    $global:LASTEXITCODE = 0
    & $body 2>&1 | ForEach-Object { $_.ToString() } | Add-Content -LiteralPath $log -Encoding utf8
    $code = $LASTEXITCODE
    if ($code -ne 0) {
        Note "FAILED: $name (exit $code)"
        Set-Content -LiteralPath $state -Value "FAILED: $name (exit $code)" -Encoding ascii
        exit 1
    }
    Note "OK: $name"
}

if (-not $SkipTests) {
    Step 'update policy tests (public build)' {
        Push-Location (Join-Path $root 'Source\PlayhubUpdate.Tests')
        try { & dotnet run -c Release } finally { Pop-Location }
    }
    Step 'update policy tests (update-test build)' {
        Push-Location (Join-Path $root 'Source\PlayhubUpdate.Tests')
        try { & dotnet run -c Release -p:PlayhubUpdatePreview=true } finally { Pop-Location }
    }
    Step 'remote catalog tests' {
        Push-Location (Join-Path $root 'Source\Playhub.RemoteCatalog.Tests')
        try { & dotnet run -c Release } finally { Pop-Location }
    }
}

if (-not $SkipReview) {
    Step 'build UI review app (Debug x64)' {
        & dotnet build (Join-Path $root 'Source\Playhub\Playhub.csproj') -c Debug -p:Platform=x64 -p:PlayhubUiReview=true
    }
    Step 'native UI review' {
        & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'Source\Playhub.UiTests\run-native-review.ps1')
    }
}

Step 'restore' {
    & dotnet restore (Join-Path $root 'Source\Playhub\Playhub.csproj') -p:Platform=x64
    if ($LASTEXITCODE) { return }
    & dotnet restore (Join-Path $root 'Source\PlayhubSetup\PlayhubSetup.csproj')
}

Step 'build both installers' {
    & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $setupRoot 'build-update-preview.ps1')
}

Note ""
Note '===== OUTPUT ====='
Get-ChildItem -LiteralPath (Join-Path $setupRoot 'Output') |
    Select-Object Name, Length, LastWriteTime |
    Format-Table -AutoSize | Out-String | Add-Content -LiteralPath $log -Encoding utf8
Set-Content -LiteralPath $state -Value 'DONE' -Encoding ascii
Note 'DONE'

[CmdletBinding()]
param(
    [string]$SourceRoot = (Split-Path -Parent $PSScriptRoot),
    [Parameter(Mandatory = $true)][string]$DeliveryRoot,
    [Parameter(Mandatory = $true)][string]$ApprovedManifestPath,
    [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-fA-F]{64}$')][string]$ExpectedBundleSha256,
    [switch]$FinalDistFrozen
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $FinalDistFrozen) { throw 'Parent must freeze the final dist before staging.' }

function Assert-PlainPath([string]$Path) {
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Reparse point refused: $cursor"
            }
        }
        $cursor = Split-Path -Parent $cursor
    }
}

$source = (Get-Item -LiteralPath $SourceRoot).FullName.TrimEnd('\')
$delivery = (Get-Item -LiteralPath $DeliveryRoot).FullName.TrimEnd('\')
if (-not (Test-Path -LiteralPath $source -PathType Container) -or
    -not (Test-Path -LiteralPath $delivery -PathType Container)) { throw 'Source and delivery must be existing directories.' }
Assert-PlainPath $source
Assert-PlainPath $delivery
if ($source.Equals($delivery, [StringComparison]::OrdinalIgnoreCase) -or
    $delivery.StartsWith($source + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $source.StartsWith($delivery + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Source and delivery must be disjoint.'
}
$destination = Join-Path $delivery 'gaming-mode-fresh'
$manifestPath = Join-Path $delivery 'gaming-mode-fresh.manifest.json'
if ((Test-Path -LiteralPath $destination) -or (Test-Path -LiteralPath $manifestPath)) {
    throw 'Fresh destination or manifest already exists; nothing will be merged or overwritten.'
}

$names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$nativeFiles = @('quick_settings\bin\QuickSettingsAgent.exe', 'quick_settings\amd\adlx_helper.exe', 'quick_settings\amd\ADLXCSharpBind.dll')
function Add-PayloadFile([string]$Relative) {
    $normalized = $Relative.Replace('/', '\')
    if ($normalized -match '(^|\\)\.\.(\\|$)|(^|\\)__pycache__(\\|$)|\.(pyc|pyo|sys)$' -or
        [IO.Path]::IsPathRooted($normalized)) { throw "Forbidden payload path: $Relative" }
    if ([IO.Path]::GetExtension($normalized) -in @('.exe', '.dll') -and $normalized -notin $nativeFiles) {
        throw "Unreviewed native binary: $Relative"
    }
    $full = [IO.Path]::GetFullPath((Join-Path $source $normalized))
    if (-not $full.StartsWith($source + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Source path escaped root.' }
    Assert-PlainPath $full
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "Required payload file missing: $Relative" }
    [void]$names.Add($normalized)
}

$required = @(
    'main.py', 'package.json', 'plugin.json', 'README.md', 'THIRD-PARTY-NOTICES.md',
    'dist\index.js', 'assets\playhub-logo-decky.png',
    'quick_settings\__init__.py', 'quick_settings\main.py',
    'quick_settings\history_days.json', 'quick_settings\history_images.json',
    'quick_settings\history_editorial.json',
    'quick_settings\LICENSE', 'quick_settings\NOTICE', 'quick_settings\LEGAL.md',
    'quick_settings\THIRD-PARTY-NOTICES.md',
    'quick_settings\licenses\GPL-3.0.txt', 'quick_settings\licenses\RyzenAdj-LGPL-3.0.txt',
    'quick_settings\bin\QuickSettingsAgent.exe',
    'quick_settings\helper\apply_perf.ps1', 'quick_settings\helper\setup_perf.ps1',
    'quick_settings\amd\adlx_helper.exe', 'quick_settings\amd\ADLXCSharpBind.dll',
    'quick_settings\amd\build_amd.bat', 'quick_settings\amd\LICENSES.txt',
    'cpu_power\__init__.py', 'cpu_power\capability.py', 'cpu_power\pawnio.py',
    'cpu_power\transaction.py', 'cpu_power\README.md'
)
foreach ($name in $required) { Add-PayloadFile $name }
foreach ($folder in @('', 'quick_settings', 'cpu_power')) {
    $directory = if ($folder) { Join-Path $source $folder } else { $source }
    foreach ($file in (Get-ChildItem -LiteralPath $directory -File)) {
        if (($file.Extension -in @('', '.md', '.txt') -and $file.Name -match '^(LICENSE|NOTICE|THIRD-PARTY-NOTICES|PROVENANCE|LEGAL|README)([.-].*)?$') -or
            ($folder -and $file.Extension -eq '.py')) {
            Add-PayloadFile $file.FullName.Substring($source.Length + 1)
        }
    }
}
foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $source 'quick_settings\licenses') -File)) {
    if ($file.Extension -in @('.txt', '.md') -or ($file.Extension -eq '' -and $file.Name -match '^(LICENSE|NOTICE)')) {
        Add-PayloadFile $file.FullName.Substring($source.Length + 1)
    }
}
foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $source 'quick_settings\amd') -Recurse -File)) {
    if ($file.Extension -eq '.cs' -or ($file.Extension -in @('', '.md', '.txt') -and $file.Name -match '^(LICENSE|NOTICE|COPYING|THIRD-PARTY-NOTICES)([.-].*)?$')) {
        Add-PayloadFile $file.FullName.Substring($source.Length + 1)
    }
}
$assetExtensions = @('.png', '.jpg', '.jpeg', '.webp', '.gif', '.svg', '.ico', '.woff', '.woff2', '.ttf', '.otf')
foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $source 'dist\assets') -Recurse -File)) {
    if ($file.Extension -notin $assetExtensions) { throw "Unreviewed dist asset type: $($file.Name)" }
    Add-PayloadFile $file.FullName.Substring($source.Length + 1)
}

$bundle = Join-Path $source 'dist\index.js'
$approved = Get-Content -LiteralPath $ApprovedManifestPath -Raw | ConvertFrom-Json
if ($approved.schemaVersion -ne 1 -or @($approved.files).Count -ne 304) {
    throw 'Expected the parent-approved 304-file runtime manifest.'
}
$approvedNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $approved.files) {
    $name = ([string]$entry.path).Replace('/', '\')
    if (-not $approvedNames.Add($name) -or -not $names.Contains($name)) {
        throw "Approved path missing from current allowlist or duplicated: $name"
    }
}
# Freeze the approved path set; new helper/source artifacts cannot enter delivery.
$names.IntersectWith($approvedNames)
if ((Get-FileHash -LiteralPath $bundle -Algorithm SHA256).Hash -ine $ExpectedBundleSha256) {
    throw 'Bundle hash differs from the parent-approved final build.'
}
$bundleTime = (Get-Item -LiteralPath $bundle).LastWriteTimeUtc
foreach ($file in (Get-ChildItem -Path (Join-Path $source 'src'), (Join-Path $source 'assets') -Recurse -File)) {
    if ($file.LastWriteTimeUtc -gt $bundleTime) { throw "Source newer than final bundle: $($file.FullName)" }
}
foreach ($name in @('package.json', 'pnpm-lock.yaml', 'rollup.config.js', 'tsconfig.json')) {
    if ((Get-Item -LiteralPath (Join-Path $source $name)).LastWriteTimeUtc -gt $bundleTime) {
        throw "Build input newer than final bundle: $name"
    }
}

[string[]]$ordered = @($names)
[Array]::Sort($ordered, [StringComparer]::Ordinal)
$manifest = @(
    foreach ($name in $ordered) {
        $file = Get-Item -LiteralPath (Join-Path $source $name)
        [pscustomobject][ordered]@{
            path = $name.Replace('\', '/')
            length = $file.Length
            sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        }
    }
)

# Create only a new sibling. On failure leave it for inspection, never delete sources or old stages.
[void](New-Item -ItemType Directory -Path $destination)
foreach ($entry in $manifest) {
    $target = [IO.Path]::GetFullPath((Join-Path $destination $entry.path))
    if (-not $target.StartsWith($destination + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Target escaped fresh stage.' }
    Assert-PlainPath $target
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
    [IO.File]::Copy((Join-Path $source $entry.path), $target, $false)
}
$actualFiles = @(Get-ChildItem -LiteralPath $destination -Recurse -File)
if ($actualFiles.Count -ne $manifest.Count) { throw 'Unexpected staged file count.' }
foreach ($entry in $manifest) {
    $target = Join-Path $destination $entry.path
    $original = Join-Path $source $entry.path
    Assert-PlainPath $target
    Assert-PlainPath $original
    if ((Get-Item -LiteralPath $target).Length -ne $entry.length -or
        (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -cne $entry.sha256 -or
        (Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash -cne $entry.sha256) {
        throw "Copy mismatch or source changed during staging: $($entry.path)"
    }
}
$report = [ordered]@{
    schemaVersion = 1
    bundleSha256 = $ExpectedBundleSha256.ToUpperInvariant()
    files = $manifest
}
$json = ($report | ConvertTo-Json -Depth 5) + "`n"
$stream = [IO.File]::Open($manifestPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
try {
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($json)
    $stream.Write($bytes, 0, $bytes.Length)
} finally { $stream.Dispose() }
[pscustomobject]@{
    freshRuntime = $destination
    verifiedFiles = $manifest.Count
    manifest = $manifestPath
    manifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
    deliveryAppUntouched = $true
} | ConvertTo-Json

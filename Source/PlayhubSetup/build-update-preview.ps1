#requires -Version 7.0
[CmdletBinding()]
param([switch]$KeepStage, [string]$ReuseStage)

$ErrorActionPreference = 'Stop'
$setupRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$appProject = Join-Path $setupRoot '..\Playhub\Playhub.csproj'
$objRoot = Join-Path $setupRoot 'obj'
$stage = Join-Path $objRoot ('update-preview-' + [Guid]::NewGuid().ToString('N'))
if ($ReuseStage) {
    $stage = [IO.Path]::GetFullPath($ReuseStage)
    $allowed = [IO.Path]::GetFullPath($objRoot).TrimEnd('\') + '\'
    if (!$stage.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase) -or !(Split-Path $stage -Leaf).StartsWith('update-preview-')) { throw 'Invalid existing production stage.' }
}
$output = Join-Path $setupRoot 'Output'
$agentDir = Join-Path $stage 'agent'

function Assert-PackagingDirectory([string]$directory, [string]$parent) {
    $absolute = [IO.Path]::GetFullPath($directory)
    $boundary = [IO.Path]::GetFullPath($parent).TrimEnd('\') + '\'
    if (!$absolute.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Packaging path escaped its owned directory.' }
    for ($ancestor = $absolute; $ancestor; $ancestor = [IO.Path]::GetDirectoryName($ancestor)) {
        if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Packaging directories must not pass through links or junctions.'
        }
    }
}

function Archive-PreviousPublicOutput {
    $repository = [IO.Path]::GetFullPath((Join-Path $setupRoot '..\..'))
    Assert-PackagingDirectory $output $setupRoot
    $existing = @(Get-ChildItem -LiteralPath $output -Force -ErrorAction SilentlyContinue)
    if (!$existing.Count) { return }
    $archiveRoot = Join-Path $repository '.local\release-packaging\retired-output'
    $retired = Join-Path $archiveRoot ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N'))
    Assert-PackagingDirectory $retired $repository
    foreach ($entry in $existing) {
        Assert-PackagingDirectory $entry.FullName $output
        if ($entry.PSIsContainer) {
            foreach ($child in (Get-ChildItem -LiteralPath $entry.FullName -Recurse -Force)) {
                Assert-PackagingDirectory $child.FullName $output
            }
        }
    }
    New-Item -ItemType Directory -Path $retired -Force | Out-Null
    foreach ($entry in $existing) {
        $destination = Join-Path $retired $entry.Name
        Assert-PackagingDirectory $destination $retired
        Move-Item -LiteralPath $entry.FullName -Destination $destination
    }
    Write-Host "Previous output preserved privately: $retired"
}

function Get-ReleaseSourceManifest {
    $sourceRoot = [IO.Path]::GetFullPath((Join-Path $setupRoot '..'))
    $files = foreach ($folder in @('GamingModeAgent', 'Playhub.GameSession', 'Playhub.XboxSession', 'Playhub.DeckyHost', 'Playhub', 'Shared', 'ApplicationIntegrations', 'PerfectArtwork')) {
        Get-ChildItem -LiteralPath (Join-Path $sourceRoot $folder) -Recurse -File |
            Where-Object { $_.Extension -in '.cs', '.csproj', '.ps1' -and $_.FullName -notmatch '[\\/](bin|obj|dist_publish|Assets|Plugins)[\\/]' }
    }
    $files += Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'Playhub.XboxSession') -Recurse -File |
        Where-Object { ($_.Extension -eq '.md' -or $_.Name -in @('LICENSE', 'MIT-LICENSE')) -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
    $files += Get-Item -LiteralPath (Join-Path $sourceRoot 'Playhub\Assets\GamingMode\desktop-safety.ps1')
    $files += Get-Item -LiteralPath (Join-Path $sourceRoot '..\catalog\plugins.json'), (Join-Path $sourceRoot 'Playhub\THIRD-PARTY-NOTICES.txt')
    foreach ($contract in @('SteamPluginBridge.cs', 'PerfectArtworkState.cs', 'ApplicationImageCache.cs', 'GameTitleRefetchFactory.cs')) {
        $files += Get-Item -LiteralPath (Join-Path $sourceRoot ('ApplicationIntegrations\Native\' + $contract))
    }
    $files += Get-Item -LiteralPath (Join-Path $setupRoot 'build-update-preview.ps1')
    $files += Get-ChildItem -LiteralPath $setupRoot -File | Where-Object { $_.Extension -in '.cs', '.csproj', '.xaml', '.manifest', '.ps1' }
    $files += Get-ChildItem -LiteralPath (Join-Path $setupRoot 'Assets') -Recurse -File
    $files += Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'Playhub\Assets\Localization') -File -Filter '*.json'
    foreach ($helper in @('focus-rescue.ps1', 'xbox-gamebar.ps1')) {
        $files += Get-Item -LiteralPath (Join-Path $sourceRoot ('Playhub\Assets\GamingMode\' + $helper))
    }
    foreach ($script in @('install.ps1', 'uninstall.ps1', 'owned-helpers.ps1')) {
        $files += Get-Item -LiteralPath (Join-Path $sourceRoot ('Playhub\Plugins\Gaming Mode\gaming-mode-win-x64\' + $script))
    }
    foreach ($pluginName in @('Artwork', 'Metadata', 'ThemeDeck', 'Launch Curtain', 'Playhub Notifications', 'Now Playing', 'TrailerHero')) {
        $bundleRoot = Join-Path $sourceRoot ('Playhub\Plugins\' + $pluginName)
        if ($pluginName -in @('Metadata', 'Launch Curtain')) {
            $installers = @(Get-ChildItem -LiteralPath $bundleRoot -File -Filter '*Installer*.zip')
            if ($installers.Count -ne 1) { throw "Expected one current bundled installer for $pluginName." }
            $files += $installers[0]
        }
        $files += Get-Item -LiteralPath (Join-Path $bundleRoot 'release-info.json')
    }
    $files += Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'GamingModeDeckyPlugin\src') -Recurse -File |
        Where-Object { $_.Extension -in '.ts', '.tsx' }
    $files += Get-Item -LiteralPath (Join-Path $sourceRoot 'GamingModeDeckyPlugin\main.py'), (Join-Path $sourceRoot 'GamingModeDeckyPlugin\decky_ipc_health.py'), (Join-Path $sourceRoot 'GamingModeDeckyPlugin\package.json'), (Join-Path $sourceRoot 'GamingModeDeckyPlugin\rollup.config.js')
    $manifest = [ordered]@{}
    foreach ($file in ($files | Sort-Object FullName -Unique)) {
        $relative = [IO.Path]::GetRelativePath($sourceRoot, $file.FullName).Replace('\', '/')
        $manifest[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }
    return ($manifest | ConvertTo-Json -Depth 3 -Compress)
}
$sourceManifest = Get-ReleaseSourceManifest
$sourceManifestPath = Join-Path $stage 'release-source-manifest.json'
if ($ReuseStage) {
    if (!(Test-Path -LiteralPath $sourceManifestPath) -or [IO.File]::ReadAllText($sourceManifestPath) -cne $sourceManifest) {
        throw 'The reused stage predates the current native sources; perform a fresh build.'
    }
}

function Assert-ReleaseFlags([string]$assemblyPath) {
    $stream = [IO.File]::OpenRead($assemblyPath)
    $pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        $reader = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        $flags = @{ 'PlayhubUpdatePolicy.IsPreview' = $false }
        $found = @{}
        foreach ($handle in $reader.TypeDefinitions) {
            $type = $reader.GetTypeDefinition($handle)
            $typeName = $reader.GetString($type.Name)
            if ($typeName -eq 'PlayhubUpdatePolicy') {
                foreach ($fieldHandle in $type.GetFields()) {
                    $field = $reader.GetFieldDefinition($fieldHandle)
                    $key = $typeName + '.' + $reader.GetString($field.Name)
                    if ($flags.ContainsKey($key)) {
                        $constant = $reader.GetConstant($field.GetDefaultValue())
                        $value = $reader.GetBlobBytes($constant.Value)[0] -ne 0
                        if ($value -ne $flags[$key]) { throw "Wrong release flag in payload: $key" }
                        $found[$key] = $true
                    }
                }
            }
            foreach ($methodHandle in $type.GetMethods()) {
                if ($reader.GetString($reader.GetMethodDefinition($methodHandle).Name) -eq 'RunUiReviewAsync') {
                    throw 'UI review code must not be shipped.'
                }
            }
        }
        foreach ($key in $flags.Keys) { if (!$found.ContainsKey($key)) { throw "Missing release flag in payload: $key" } }
    } finally { $pe.Dispose(); $stream.Dispose() }
}

New-Item -ItemType Directory -Path $stage, $output -Force | Out-Null
Write-Host "Production staging: $stage"
try {
    $stubDir = Join-Path $stage 'stub'
    $publish = Join-Path $stage 'normal'
    if (!$ReuseStage) {
    & (Join-Path $setupRoot '..\GamingModeDeckyPlugin\build-plugin.bat')
    if ($LASTEXITCODE) { throw 'Current Decky source build failed.' }
    & dotnet publish (Join-Path $setupRoot 'PlayhubSetup.csproj') -c Release -r win-x64 -o $stubDir
    if ($LASTEXITCODE) { throw 'Installer stub build failed.' }
    $stub = Join-Path $stubDir 'Playhub Setup.exe'

    $publish = Join-Path $stage 'normal'
    & dotnet publish $appProject -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:WindowsAppSDKSelfContained=true -p:PlayhubUiReview=false -p:PlayhubUpdatePreview=false -o $publish
    if ($LASTEXITCODE) { throw 'App normal build failed.' }
    Assert-ReleaseFlags (Join-Path $publish 'Playhub.dll')
    & dotnet publish (Join-Path $setupRoot '..\GamingModeAgent\GamingMode.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $agentDir
    if ($LASTEXITCODE) { throw 'Gaming Mode agent build failed.' }
    $agentPayload = Join-Path $publish 'Plugins\Gaming Mode\gaming-mode-win-x64'
    New-Item -ItemType Directory -Path $agentPayload -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $agentDir 'GamingMode.exe') -Destination $agentPayload -Force
    if ((Get-ReleaseSourceManifest) -cne $sourceManifest) { throw 'Native sources changed while building; freeze and rebuild.' }
    [IO.File]::WriteAllText($sourceManifestPath, $sourceManifest)
    }
    $stub = Join-Path $stubDir 'Playhub Setup.exe'
    Assert-ReleaseFlags (Join-Path $publish 'Playhub.dll')
    # Rollup's old hashed editorial images can remain after switching to remote
    # photos. Prune only unreferenced hashed assets in this isolated payload.
    $dist = Join-Path $publish 'Assets\GamingModeDeckyPlugin\gaming-mode\dist'
    $references = (Get-ChildItem -LiteralPath $dist -Recurse -File | Where-Object { $_.Extension -in '.js', '.css', '.html', '.json' } | ForEach-Object { [IO.File]::ReadAllText($_.FullName) }) -join "`n"
    $excluded = @()
    foreach ($asset in (Get-ChildItem -LiteralPath (Join-Path $dist 'assets') -File)) {
        if ($asset.Name -match '-[a-f0-9]{8}\.[a-z0-9]+$' -and !$references.Contains($asset.Name)) {
            $excluded += [pscustomobject]@{ File=$asset.Name; Bytes=$asset.Length }
            Remove-Item -LiteralPath $asset.FullName
        }
    }
    $excluded | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'excluded-stale-assets.json')
    $zipPath = Join-Path $stage 'normal.zip'
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath }
    [IO.Compression.ZipFile]::CreateFromDirectory($publish, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $false)
    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        foreach ($required in @('Playhub.exe', 'Playhub.dll', 'Assets/Brand/cube.png', 'Assets/Brand/playhub-wordmark-white.png', 'Assets/Welcome/Mascots/final-onboarding.png')) {
            if ($null -eq $archive.GetEntry($required)) { throw "Missing payload entry: $required" }
        }
        $runtimeEntries = @('Plugins/Gaming Mode/gaming-mode-win-x64/GamingMode.exe', 'Assets/GamingModeDeckyPlugin/gaming-mode/main.py', 'Playhub.GameSession.exe', 'Playhub.XboxSession.exe', 'XboxSession/source/LICENSE', 'XboxSession/source/NOTICE.md', 'XboxSession/source/BUILD.md', 'XboxSession/source/Playhub.XboxSession.csproj', 'XboxSession/source/Runtime/MIT-LICENSE')
        foreach ($legacyHelperFile in @('Playhub.GameSession.dll', 'Playhub.GameSession.deps.json', 'Playhub.GameSession.runtimeconfig.json', 'Playhub.XboxSession.dll', 'Playhub.XboxSession.deps.json', 'Playhub.XboxSession.runtimeconfig.json')) {
            if ($null -ne $archive.GetEntry($legacyHelperFile)) { throw "Stale non-single-file helper payload: $legacyHelperFile" }
        }
        if (@($archive.Entries | Where-Object { $_.FullName -match '^(Emulation/|Assets/Emulation/|Assets/Emulators/|RecompCore\.|Quiver-|Vendor/(QUIVER|SHARPCOMPRESS))' }).Count) {
            throw 'Removed feature payload found in the release.'
        }
        foreach ($required in $runtimeEntries) {
            if ($null -eq $archive.GetEntry($required)) { throw "Missing runtime payload entry: $required" }
        }
        foreach ($required in @('Assets/DeckyRuntime/Playhub-DeckyHost.exe')) {
            if ($null -eq $archive.GetEntry($required)) { throw "Missing 2.5 runtime payload entry: $required" }
        }
        if (@($archive.Entries | Where-Object { $_.FullName.StartsWith('Assets/DeckyRuntime/') -and $_.FullName -ne 'Assets/DeckyRuntime/Playhub-DeckyHost.exe' }).Count) {
            throw 'Unrelated application files found inside the Decky lifetime runtime.'
        }
        $artwork = $archive.GetEntry('Assets/Welcome/Mascots/final-onboarding.png').Open()
        try {
            $actual = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($artwork))
            $expected = (Get-FileHash -LiteralPath (Join-Path $setupRoot '..\Playhub\Assets\Welcome\Mascots\final-onboarding.png') -Algorithm SHA256).Hash
            if ($actual -ne $expected) { throw 'Final welcome artwork does not match the approved source asset.' }
        } finally { $artwork.Dispose() }
        $dataAssets = @{
            'Plugins/Gaming Mode/gaming-mode-win-x64/GamingMode.exe' = Join-Path $agentDir 'GamingMode.exe'
            'Assets/PluginCatalog/store-catalog.json' = Join-Path $setupRoot '..\..\catalog\plugins.json'
            'THIRD-PARTY-NOTICES.txt' = Join-Path $setupRoot '..\Playhub\THIRD-PARTY-NOTICES.txt'
            'Playhub.GameSession.exe' = Join-Path $setupRoot '..\Playhub.GameSession\bin\Release\net8.0-windows\win-x64\publish\Playhub.GameSession.exe'
            'Playhub.XboxSession.exe' = Join-Path $setupRoot '..\Playhub.XboxSession\bin\Release\net8.0-windows\win-x64\publish\Playhub.XboxSession.exe'
            'Assets/DeckyRuntime/Playhub-DeckyHost.exe' = Join-Path $setupRoot '..\Playhub.DeckyHost\bin\Release\net8.0-windows\win-x64\publish\Playhub-DeckyHost.exe'
            'Assets/GamingModeDeckyPlugin/gaming-mode/dist/index.js' = Join-Path $setupRoot '..\GamingModeDeckyPlugin\dist\index.js'
            'Assets/GamingModeDeckyPlugin/gaming-mode/main.py' = Join-Path $setupRoot '..\GamingModeDeckyPlugin\main.py'
            'Assets/GamingModeDeckyPlugin/gaming-mode/decky_ipc_health.py' = Join-Path $setupRoot '..\GamingModeDeckyPlugin\decky_ipc_health.py'
            'Assets/GamingModeDeckyPlugin/gaming-mode/plugin.json' = Join-Path $setupRoot '..\GamingModeDeckyPlugin\plugin.json'
            'Assets/GamingModeDeckyPlugin/gaming-mode/package.json' = Join-Path $setupRoot '..\GamingModeDeckyPlugin\package.json'
            'Assets/GamingMode/desktop-safety.ps1' = Join-Path $setupRoot '..\Playhub\Assets\GamingMode\desktop-safety.ps1'
            'Assets/GamingMode/focus-rescue.ps1' = Join-Path $setupRoot '..\Playhub\Assets\GamingMode\focus-rescue.ps1'
            'Assets/GamingMode/xbox-gamebar.ps1' = Join-Path $setupRoot '..\Playhub\Assets\GamingMode\xbox-gamebar.ps1'
            'Plugins/Gaming Mode/gaming-mode-win-x64/owned-helpers.ps1' = Join-Path $setupRoot '..\Playhub\Plugins\Gaming Mode\gaming-mode-win-x64\owned-helpers.ps1'
            'Plugins/Gaming Mode/gaming-mode-win-x64/install.ps1' = Join-Path $setupRoot '..\Playhub\Plugins\Gaming Mode\gaming-mode-win-x64\install.ps1'
            'Plugins/Gaming Mode/gaming-mode-win-x64/uninstall.ps1' = Join-Path $setupRoot '..\Playhub\Plugins\Gaming Mode\gaming-mode-win-x64\uninstall.ps1'
        }
        $xboxSourceRoot = [IO.Path]::GetFullPath((Join-Path $setupRoot '..\Playhub.XboxSession'))
        $xboxSources = @(Get-ChildItem -LiteralPath $xboxSourceRoot -Recurse -File |
            Where-Object { ($_.Extension -in '.cs', '.csproj' -or $_.Name -in @('LICENSE', 'NOTICE.md', 'BUILD.md', 'MIT-LICENSE')) -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })
        $xboxSourceEntries = @()
        foreach ($xboxFile in $xboxSources) {
            $entryName = 'XboxSession/source/' + [IO.Path]::GetRelativePath($xboxSourceRoot, $xboxFile.FullName).Replace('\', '/')
            $dataAssets[$entryName] = $xboxFile.FullName
            $xboxSourceEntries += $entryName
        }
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName.StartsWith('XboxSession/source/') -and !$entry.FullName.EndsWith('/') -and $entry.FullName -notin $xboxSourceEntries) {
                throw "Unexpected Xbox corresponding source payload: $($entry.FullName)"
            }
        }
        foreach ($language in @('it', 'en', 'es', 'fr', 'de', 'pt', 'uk', 'zh', 'ja', 'ko', 'hi', 'ru')) {
            $dataAssets["Assets/Localization/$language.json"] = Join-Path $setupRoot "..\Playhub\Assets\Localization\$language.json"
        }
        foreach ($pluginName in @('Artwork', 'Metadata', 'ThemeDeck', 'Launch Curtain', 'Playhub Notifications', 'Now Playing', 'TrailerHero')) {
            $pluginSource = Join-Path $setupRoot "..\Playhub\Plugins\$pluginName"
            if ($pluginName -in @('Metadata', 'Launch Curtain')) {
                $installers = @(Get-ChildItem -LiteralPath $pluginSource -File -Filter '*Installer*.zip')
                if ($installers.Count -ne 1) { throw "Expected one current bundled installer for $pluginName." }
                $dataAssets["Plugins/$pluginName/$($installers[0].Name)"] = $installers[0].FullName
            }
            $marker = Join-Path $pluginSource 'release-info.json'
            if (!(Test-Path -LiteralPath $marker)) { throw "Missing bundled version provenance for $pluginName." }
            $dataAssets["Plugins/$pluginName/release-info.json"] = $marker
        }
        if (@($archive.Entries | Where-Object { $_.FullName -match '^Plugins/(?!Metadata/|Launch Curtain/).*\.zip$' }).Count) {
            throw 'Remotely available plugin installer archives must not be bundled.'
        }
        foreach ($entryPath in $dataAssets.Keys) {
            $entry = $archive.GetEntry($entryPath)
            if ($null -eq $entry) { throw "Missing payload data: $entryPath" }
            $data = $entry.Open()
            try {
                $actual = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($data))
                $expected = (Get-FileHash -LiteralPath $dataAssets[$entryPath] -Algorithm SHA256).Hash
                if ($actual -ne $expected) { throw "Outdated payload data: $entryPath" }
            } finally { $data.Dispose() }
        }
    } finally { $archive.Dispose() }
    if ((Get-ReleaseSourceManifest) -cne $sourceManifest) { throw 'Native sources changed while packaging; freeze and rebuild.' }
    $name = 'Playhub-Setup.exe'
    $partial = Join-Path $stage $name
    $target = [IO.File]::Create($partial)
    try {
        foreach ($sourcePath in @($stub, $zipPath)) {
            $source = [IO.File]::OpenRead($sourcePath)
            try { $source.CopyTo($target) } finally { $source.Dispose() }
        }
        $target.Write([BitConverter]::GetBytes([long](Get-Item -LiteralPath $zipPath).Length))
        $target.Write([Text.Encoding]::ASCII.GetBytes('PLHB'))
    } finally { $target.Dispose() }
    $check = [IO.File]::OpenRead($partial)
    try {
        $null = $check.Seek(-12, [IO.SeekOrigin]::End)
        $footer = [byte[]]::new(12)
        $null = $check.Read($footer, 0, 12)
        if ([Text.Encoding]::ASCII.GetString($footer, 8, 4) -ne 'PLHB' -or
            [BitConverter]::ToInt64($footer, 0) -ne (Get-Item -LiteralPath $zipPath).Length) {
            throw 'Invalid installer payload footer.'
        }
    } finally { $check.Dispose() }
    $destination = Join-Path $output $name
    Archive-PreviousPublicOutput
    Move-Item -LiteralPath $partial -Destination $destination -Force
    $hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $name" | Set-Content -LiteralPath ($destination + '.sha256') -Encoding ascii
    Get-Item -LiteralPath $destination | Select-Object FullName, Length, LastWriteTime
} finally {
    $resolvedStage = [IO.Path]::GetFullPath($stage)
    $resolvedObj = [IO.Path]::GetFullPath($objRoot).TrimEnd('\') + '\'
    if (!$resolvedStage.StartsWith($resolvedObj, [StringComparison]::OrdinalIgnoreCase) -or
        !(Split-Path $resolvedStage -Leaf).StartsWith('update-preview-')) {
        throw 'Refusing to clean staging path outside the installer obj directory.'
    }
    if (!$KeepStage -and (Test-Path -LiteralPath $resolvedStage)) { Remove-Item -LiteralPath $resolvedStage -Recurse -Force }
}

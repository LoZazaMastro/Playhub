#requires -Version 7.0
[CmdletBinding()]
param([switch]$KeepStage, [string]$ReuseStage, [switch]$Candidate, [string]$XboxOverlayPilotBridge,
    [string]$XboxSteamCompanion, [switch]$XboxSteamControllerLayoutTrial, [string]$XboxSteamInputReader,
    [string]$XboxSteamRelayClient, [string]$XboxSteamRelayLicense, [switch]$XboxCupheadWindowedPreference,
    [switch]$XboxUwpSteamCompanionTrial, [string]$XboxSteamBandCallback, [string]$HudDisplayNative, [string]$CurtainDisplayNative, [string]$WindowContentObserver)

$ErrorActionPreference = 'Stop'
$setupRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$appProject = Join-Path $setupRoot '..\Playhub\Playhub.csproj'
$objRoot = Join-Path $setupRoot 'obj'
$stage = Join-Path $objRoot ('update-preview-' + [Guid]::NewGuid().ToString('N'))
$bridgeHash = ''
$companionHash = ''
$bandCallbackHash = ''
$hudDisplayHash = ''
$curtainDisplayHash = ''
$windowContentHash = '1F9728D6C2EA3AF8E46095484F7E0723C6038C7BE981777CAF99EFDF21316105'
$inputReaderHash = ''
$relayDriverHash = ''
$controllerDriverRequired = 'false'
$controllerProbe = 'false'
$controllerLayout = 'false'
$cupheadWindowedPreference = 'false'
$uwpSteamCompanion = 'false'
if ($XboxUwpSteamCompanionTrial) {
    if (!$Candidate -or !$XboxSteamCompanion -or $ReuseStage -or $XboxOverlayPilotBridge) {
        throw 'The registered UWP companion trial requires a fresh private Steam companion candidate.'
    }
    $uwpSteamCompanion = 'true'
}
if ($XboxCupheadWindowedPreference) {
    if (!$Candidate -or !$XboxSteamCompanion -or $ReuseStage) {
        throw 'The Cuphead display preference requires a fresh private companion candidate.'
    }
    $cupheadWindowedPreference = 'true'
}
if ($XboxSteamControllerLayoutTrial) {
    if (!$Candidate -or !$XboxSteamCompanion -or $ReuseStage) {
        throw 'The Xbox controller layout trial requires a fresh private companion candidate.'
    }
    $controllerLayout = 'true'
}
if ($XboxSteamCompanion) {
    if (!$Candidate -or $ReuseStage -or $XboxOverlayPilotBridge) { throw 'Use a fresh private candidate for the Xbox companion.' }
    $XboxSteamCompanion = (Get-Item -LiteralPath $XboxSteamCompanion).FullName
    if ((Split-Path -Leaf $XboxSteamCompanion) -cne 'PlayhubSteamCompanion.dll') { throw 'Expected PlayhubSteamCompanion.dll.' }
    $companionHash = (Get-FileHash -LiteralPath $XboxSteamCompanion -Algorithm SHA256).Hash
    $controllerProbe = 'true'
}
if ($XboxSteamBandCallback) {
    if (!$Candidate -or !$XboxSteamCompanion -or $ReuseStage) { throw 'The mirror callback requires a fresh private companion candidate.' }
    $XboxSteamBandCallback = (Get-Item -LiteralPath $XboxSteamBandCallback).FullName
    if ((Split-Path -Leaf $XboxSteamBandCallback) -cne 'PlayhubExactBand.dll') { throw 'Expected PlayhubExactBand.dll.' }
    $bandCallbackHash = (Get-FileHash -LiteralPath $XboxSteamBandCallback -Algorithm SHA256).Hash
}
if ($HudDisplayNative) {
    if (!$Candidate -or !$XboxSteamBandCallback -or $ReuseStage) {
        throw 'The HUD display requires a fresh private candidate and its pinned mirror callback.'
    }
    $HudDisplayNative = (Get-Item -LiteralPath $HudDisplayNative).FullName
    if ((Split-Path -Leaf $HudDisplayNative) -cne 'PlayhubHudDisplayNative.dll') {
        throw 'Expected PlayhubHudDisplayNative.dll.'
    }
    $hudDisplayHash = (Get-FileHash -LiteralPath $HudDisplayNative -Algorithm SHA256).Hash
}
if ($CurtainDisplayNative -or $WindowContentObserver) {
    if (!$Candidate -or !$XboxSteamBandCallback -or $ReuseStage -or !$CurtainDisplayNative -or !$WindowContentObserver) {
        throw 'The first-content cover requires a fresh private candidate, native display, pinned callback and capture observer.'
    }
    $CurtainDisplayNative = (Get-Item -LiteralPath $CurtainDisplayNative).FullName
    $WindowContentObserver = (Get-Item -LiteralPath $WindowContentObserver).FullName
    if ((Split-Path -Leaf $CurtainDisplayNative) -cne 'PlayhubCurtainDisplayNative.dll' -or
        (Split-Path -Leaf $WindowContentObserver) -cne 'PlayhubWindowContentRead.exe' -or
        (Get-FileHash -LiteralPath $WindowContentObserver -Algorithm SHA256).Hash -cne $windowContentHash) {
        throw 'The first-content component filename or capture pin differs.'
    }
    $curtainDisplayHash = (Get-FileHash -LiteralPath $CurtainDisplayNative -Algorithm SHA256).Hash
}
if ($XboxSteamInputReader) {
    if (!$XboxSteamControllerLayoutTrial) { throw 'The Xbox input reader requires a private controller layout candidate.' }
    $XboxSteamInputReader = (Get-Item -LiteralPath $XboxSteamInputReader).FullName
    if ((Split-Path -Leaf $XboxSteamInputReader) -cne 'PlayhubSteamInputReader.exe') { throw 'Expected PlayhubSteamInputReader.exe.' }
    $inputReaderHash = (Get-FileHash -LiteralPath $XboxSteamInputReader -Algorithm SHA256).Hash
}
if ($XboxSteamRelayClient -or $XboxSteamRelayLicense) {
    if (!$XboxSteamInputReader -or !$XboxSteamRelayClient -or !$XboxSteamRelayLicense) {
        throw 'The private Xbox relay requires the input reader, ViGEmClient and its license.'
    }
    $XboxSteamRelayClient = (Get-Item -LiteralPath $XboxSteamRelayClient).FullName
    $XboxSteamRelayLicense = (Get-Item -LiteralPath $XboxSteamRelayLicense).FullName
    $relayClientHash = '00303F99E1968D523FBD08A7C691D60D145B7335059EC593947841D124C10875'
    $relayLicenseHash = '445B3BCCD103D39CAB7E9B276DD966D60269CF5C44B4B8A118E48BC4C7C4F0DB'
    if ((Get-FileHash -LiteralPath $XboxSteamRelayClient -Algorithm SHA256).Hash -cne $relayClientHash -or
        (Get-FileHash -LiteralPath $XboxSteamRelayLicense -Algorithm SHA256).Hash -cne $relayLicenseHash) {
        throw 'The ViGEmClient file or its upstream license does not match the private relay.'
    }
    $relayDriverHash = 'B6D6FA5CA8334368FC366A3E78552EFB74EEF657061371B2DE407AA158B0A11C'
    $controllerDriverRequired = 'true'
}
if ($XboxOverlayPilotBridge) {
    if (!$Candidate -or $ReuseStage) { throw 'The Xbox overlay pilot requires a fresh private candidate.' }
    $XboxOverlayPilotBridge = (Get-Item -LiteralPath $XboxOverlayPilotBridge).FullName
    if ((Split-Path -Leaf $XboxOverlayPilotBridge) -cne 'WsgmUwpBridge.dll') { throw 'Expected WsgmUwpBridge.dll.' }
    $bridgeHash = (Get-FileHash -LiteralPath $XboxOverlayPilotBridge -Algorithm SHA256).Hash
}
if ($ReuseStage) {
    $stage = [IO.Path]::GetFullPath($ReuseStage)
    $allowed = [IO.Path]::GetFullPath($objRoot).TrimEnd('\') + '\'
    if (!$stage.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase) -or !(Split-Path $stage -Leaf).StartsWith('update-preview-')) { throw 'Invalid existing production stage.' }
}
$output = Join-Path $setupRoot 'Output'
if ($Candidate) {
    $repository = [IO.Path]::GetFullPath((Join-Path $setupRoot '..\..'))
    $candidateRoot = Join-Path $repository '.local\release-packaging\candidates'
    $output = Join-Path $candidateRoot ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
}
$agentDir = Join-Path $stage 'agent'
$tutorialAssets = @(
    'Assets/Tutorials/Decky-Store.mp4',
    'Assets/Tutorials/DeckyLoader-QAM.mp4',
    'Assets/Tutorials/Gaming-Mode-Plugin.mp4',
    'Assets/Tutorials/Big Picture Mode tutorial.png',
    'Assets/Extra/css-theme-preview.png'
)

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

function Get-XboxSessionSources([string]$directory) {
    Get-ChildItem -LiteralPath $directory -Recurse -File |
        Where-Object {
            $_.FullName -notmatch '[\\/](bin|obj|CMakeFiles)[\\/]' -and
                ($_.Extension -in '.cs', '.csproj', '.cpp', '.c', '.h', '.cmake' -or
                 ($_.Name -cin 'Build.cmd', 'PerMonitor.manifest', 'provenance.json' -and
                  $_.DirectoryName -ieq (Join-Path $directory 'Native\HudDisplay\WindowContent')) -or
                 ($_.Extension -eq '.py' -and $_.FullName -match '[\\/]Native[\\/]HudDisplay[\\/]tests[\\/][^\\/]+\.py$') -or
                 ($_.Name -ceq 'callback-source.sha256' -and $_.FullName -match '[\\/]Native[\\/]HudDisplay[\\/]tests[\\/]callback-source\.sha256$') -or
                 $_.Name -in 'LICENSE', 'NOTICE.md', 'BUILD.md', 'MIT-LICENSE', 'CMakeLists.txt', 'AUTHORS.txt', 'LICENSE.txt', 'README.md', 'INTEGRATION.md', 'PublicSteamContext.md')
        }
}

function Get-OfficialDeckyFiles {
    $officialRoot = [IO.Path]::GetFullPath((Join-Path $setupRoot '..\Playhub\Assets\DeckyRuntime\Official-3.3.1'))
    $runtimeRoot = [IO.Path]::GetDirectoryName($officialRoot)
    Assert-PackagingDirectory $officialRoot $runtimeRoot
    $manifestFile = Get-Item -LiteralPath (Join-Path $officialRoot 'PACKAGE-MANIFEST.json')
    if ($manifestFile.Length -gt 256KB -or
        (Get-FileHash -LiteralPath $manifestFile.FullName -Algorithm SHA256).Hash -ine '9a0cf680ce61631a2d5dc711e34d77dcfd179ac7a385874679d443067a4b4ec7') {
        throw 'The official Decky manifest does not match the reviewed upstream package.'
    }
    $package = Get-Content -LiteralPath $manifestFile.FullName -Raw | ConvertFrom-Json
    if ($package.version -cne '3.3.1' -or $package.upstreamTag -cne 'v3.3.1' -or $package.official -ne $true -or @($package.files).Count -ne 7) {
        throw 'Unexpected official Decky package.'
    }
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $result = [Collections.Generic.List[IO.FileInfo]]::new()
    $result.Add($manifestFile)
    foreach ($item in $package.files) {
        $relative = [string]$item.path
        if (!$relative -or $relative.Length -gt 512 -or $relative -match '(^/|\\|:|(^|/)\.{1,2}(/|$))' -or
            !$names.Add($relative) -or [string]$item.sha256 -notmatch '^[0-9a-f]{64}$' -or
            ($item.size -isnot [long] -and $item.size -isnot [int]) -or $item.size -le 0 -or $item.size -gt 64MB) {
            throw 'Invalid file in the official Decky manifest.'
        }
        $file = Get-Item -LiteralPath (Join-Path $officialRoot $relative)
        Assert-PackagingDirectory $file.FullName $officialRoot
        if ($file.PSIsContainer -or $file.Length -ne $item.size -or
            (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ine $item.sha256) {
            throw "Official Decky file failed verification: $relative"
        }
        $result.Add($file)
    }
    if (@(Get-ChildItem -LiteralPath $officialRoot -Recurse -File -Force).Count -ne $result.Count) {
        throw 'Unexpected files in the official Decky package.'
    }
    return $result.ToArray()
}

function Get-ControllerDriverFiles {
    if (!$XboxSteamRelayClient) { return @() }
    $root = Join-Path $setupRoot 'Runtime\ViGEmBus'
    $expected = [ordered]@{
        'ViGEmBus_1.22.0_x64_x86_arm64.exe' = '89220A7865076B342892F98865F3499FB7C4CFD673159E89D352C360FD014C6A'
        'LICENSE.txt' = '3959F6CA50466675190590DFD024585744B6456AE7D39A3222E6550D62882586'
    }
    $files = [Collections.Generic.List[IO.FileInfo]]::new()
    foreach ($name in @($expected.Keys) + @('SOURCE.json')) {
        $file = Get-Item -LiteralPath (Join-Path $root $name)
        Assert-PackagingDirectory $file.FullName $setupRoot
        if ($file.PSIsContainer -or $file.Length -le 0 -or $file.Length -gt 16MB) {
            throw 'Invalid bundled controller dependency.'
        }
        if ($expected.Contains($name) -and (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -cne $expected[$name]) {
            throw "Bundled controller dependency failed verification: $name"
        }
        $files.Add($file)
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $files[0].FullName
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(^|,\s*)O=Nefarius Software Solutions e\.U\.(,|$)') {
        throw 'The bundled controller installer must retain its official publisher signature.'
    }
    $source = Get-Content -LiteralPath (Join-Path $root 'SOURCE.json') -Raw | ConvertFrom-Json
    if ($source.schema -ne 1 -or $source.project -cne 'ViGEmBus' -or $source.setupVersion -cne '1.22.0' -or
        $source.sha256 -cne $expected['ViGEmBus_1.22.0_x64_x86_arm64.exe'] -or
        $source.licenseSha256 -cne $expected['LICENSE.txt'] -or $source.x64DriverSha256 -cne $relayDriverHash -or
        $source.license -cne 'BSD-3-Clause') { throw 'Invalid controller dependency provenance.' }
    return $files.ToArray()
}

function Get-ReleaseSourceManifest {
    $sourceRoot = [IO.Path]::GetFullPath((Join-Path $setupRoot '..'))
    $files = foreach ($folder in @('GamingModeAgent', 'Playhub.GameSession', 'Playhub.XboxSession', 'Playhub.DeckyHost', 'Playhub', 'Shared', 'ApplicationIntegrations', 'PerfectArtwork')) {
        Get-ChildItem -LiteralPath (Join-Path $sourceRoot $folder) -Recurse -File |
            Where-Object { $_.Extension -in '.cs', '.csproj', '.ps1' -and $_.FullName -notmatch '[\\/](bin|obj|dist_publish|Assets|Plugins)[\\/]' }
    }
    $files += Get-XboxSessionSources (Join-Path $sourceRoot 'Playhub.XboxSession')
    $files += Get-Item -LiteralPath (Join-Path $sourceRoot 'Playhub\Assets\GamingMode\desktop-safety.ps1')
    $files += Get-Item -LiteralPath (Join-Path $sourceRoot '..\catalog\plugins.json'), (Join-Path $sourceRoot 'Playhub\THIRD-PARTY-NOTICES.txt')
    foreach ($contract in @('SteamPluginBridge.cs', 'PerfectArtworkState.cs', 'ApplicationImageCache.cs', 'GameTitleRefetchFactory.cs')) {
        $files += Get-Item -LiteralPath (Join-Path $sourceRoot ('ApplicationIntegrations\Native\' + $contract))
    }
    $files += Get-Item -LiteralPath (Join-Path $setupRoot 'build-update-preview.ps1')
    $files += Get-ChildItem -LiteralPath $setupRoot -File | Where-Object { $_.Extension -in '.cs', '.csproj', '.xaml', '.manifest', '.ps1' }
    $files += Get-ChildItem -LiteralPath (Join-Path $setupRoot 'Assets') -Recurse -File
    $files += Get-ControllerDriverFiles
    $files += Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'Playhub\Assets\Localization') -File -Filter '*.json'
    foreach ($asset in $tutorialAssets) {
        $files += Get-Item -LiteralPath (Join-Path $sourceRoot ("Playhub/" + $asset))
    }
    foreach ($helper in @('focus-rescue.ps1', 'xbox-gamebar.ps1', 'retire-gamebar-watcher.ps1')) {
        $files += Get-Item -LiteralPath (Join-Path $sourceRoot ('Playhub\Assets\GamingMode\' + $helper))
    }
    foreach ($script in @('install.ps1', 'uninstall.ps1', 'owned-helpers.ps1')) {
        $files += Get-Item -LiteralPath (Join-Path $sourceRoot ('Playhub\Plugins\Gaming Mode\gaming-mode-win-x64\' + $script))
    }
    $files += Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'Playhub\Plugins\Gaming Mode\gaming-mode-win-x64\PresentMon') -File
    $files += Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'GamingModeAgent\Native\PresentMon') -Recurse -File
    $files += Get-OfficialDeckyFiles
    foreach ($pluginName in @('Artwork', 'Metadata', 'ThemeDeck', 'Launch Curtain', 'Playhub Notifications', 'Now Playing', 'TrailerHero', 'Weather', 'Discord', 'CSS Loader')) {
        $bundleRoot = Join-Path $sourceRoot ('Playhub\Plugins\' + $pluginName)
        if ($pluginName -in @('Metadata', 'Launch Curtain', 'Weather', 'Discord', 'CSS Loader')) {
            $installers = @(Get-ChildItem -LiteralPath $bundleRoot -File -Filter '*.zip')
            if ($installers.Count -ne 1) { throw "Expected one current bundled installer for $pluginName." }
            $files += Get-ChildItem -LiteralPath $bundleRoot -Recurse -File
        }
        if ($pluginName -ne 'CSS Loader') { $files += Get-Item -LiteralPath (Join-Path $bundleRoot 'release-info.json') }
    }
    $files += Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'GamingModeDeckyPlugin\src') -Recurse -File |
        Where-Object { $_.Extension -in '.ts', '.tsx' }
    $files += Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'GamingModeDeckyPlugin\quick_settings') -File -Filter '*.py'
    $files += Get-Item -LiteralPath (Join-Path $sourceRoot 'GamingModeDeckyPlugin\main.py'), (Join-Path $sourceRoot 'GamingModeDeckyPlugin\decky_ipc_health.py'), (Join-Path $sourceRoot 'GamingModeDeckyPlugin\package.json'), (Join-Path $sourceRoot 'GamingModeDeckyPlugin\rollup.config.js')
    foreach ($config in @('plugin.json', 'build-plugin.bat', 'tsconfig.json')) {
        $files += Get-Item -LiteralPath (Join-Path $sourceRoot ('GamingModeDeckyPlugin\' + $config))
    }
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

function Assert-UwpCompanionMetadata([string]$assemblyPath) {
    $stream = [IO.File]::OpenRead($assemblyPath)
    $pe = [Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        $reader = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        $found = @{}
        foreach ($handle in $reader.GetAssemblyDefinition().GetCustomAttributes()) {
            $attribute = $reader.GetCustomAttribute($handle)
            if ($attribute.Constructor.Kind -ne [Reflection.Metadata.HandleKind]::MemberReference) { continue }
            $member = $reader.GetMemberReference([Reflection.Metadata.MemberReferenceHandle]$attribute.Constructor)
            if ($member.Parent.Kind -ne [Reflection.Metadata.HandleKind]::TypeReference) { continue }
            $type = $reader.GetTypeReference([Reflection.Metadata.TypeReferenceHandle]$member.Parent)
            if ($reader.GetString($type.Namespace) -ne 'System.Reflection' -or
                $reader.GetString($type.Name) -ne 'AssemblyMetadataAttribute') { continue }
            $blob = $reader.GetBlobReader($attribute.Value)
            if ($blob.ReadUInt16() -ne 1) { throw 'Invalid session assembly metadata.' }
            $key = $blob.ReadSerializedString()
            $value = $blob.ReadSerializedString()
            if ($key -in 'PlayhubUwpSteamCompanion', 'PlayhubSteamCompanionSha256', 'PlayhubSteamBandCallbackSha256') {
                if ($found.ContainsKey($key)) { throw "Duplicate session metadata: $key" }
                $found[$key] = $value
            }
        }
        if ($XboxUwpSteamCompanionTrial) {
            if ($found['PlayhubUwpSteamCompanion'] -cne 'true' -or $companionHash.Length -ne 64 -or
                $found['PlayhubSteamCompanionSha256'] -cne $companionHash) {
                throw 'Both session helpers must contain the registered UWP trial flag and the exact packaged companion digest.'
            }
        } elseif ($found.ContainsKey('PlayhubUwpSteamCompanion')) {
            throw 'The registered UWP companion preview must be absent from an ordinary build.'
        }
        if ((Split-Path -Leaf $assemblyPath) -ceq 'Playhub.XboxSession.dll') {
            if ($XboxSteamBandCallback -and $found['PlayhubSteamBandCallbackSha256'] -cne $bandCallbackHash) {
                throw 'The session helper must pin the exact packaged mirror callback.'
            }
            if (!$XboxSteamBandCallback -and $found.ContainsKey('PlayhubSteamBandCallbackSha256')) {
                throw 'A mirror callback pin must be absent from an ordinary build.'
            }
        }
    } finally { $pe.Dispose(); $stream.Dispose() }
}

function Assert-HudDisplayMetadata([string]$assemblyPath) {
    $stream = [IO.File]::OpenRead($assemblyPath)
    $pe = [Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        $reader = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        $found = @{}
        foreach ($handle in $reader.GetAssemblyDefinition().GetCustomAttributes()) {
            $attribute = $reader.GetCustomAttribute($handle)
            if ($attribute.Constructor.Kind -ne [Reflection.Metadata.HandleKind]::MemberReference) { continue }
            $member = $reader.GetMemberReference([Reflection.Metadata.MemberReferenceHandle]$attribute.Constructor)
            if ($member.Parent.Kind -ne [Reflection.Metadata.HandleKind]::TypeReference) { continue }
            $type = $reader.GetTypeReference([Reflection.Metadata.TypeReferenceHandle]$member.Parent)
            if ($reader.GetString($type.Namespace) -ne 'System.Reflection' -or
                $reader.GetString($type.Name) -ne 'AssemblyMetadataAttribute') { continue }
            $blob = $reader.GetBlobReader($attribute.Value)
            if ($blob.ReadUInt16() -ne 1) { throw 'Invalid agent assembly metadata.' }
            $key = $blob.ReadSerializedString()
            $value = $blob.ReadSerializedString()
            if ($key -in 'PlayhubHudDisplaySha256', 'PlayhubHudDisplayCallbackSha256') {
                if ($found.ContainsKey($key)) { throw "Duplicate HUD display metadata: $key" }
                $found[$key] = $value
            }
        }
        if ($HudDisplayNative) {
            if ($found['PlayhubHudDisplaySha256'] -cne $hudDisplayHash -or
                $found['PlayhubHudDisplayCallbackSha256'] -cne $bandCallbackHash) {
                throw 'The agent must pin both exact packaged HUD display components.'
            }
        } elseif ($found.Count) { throw 'HUD display pins must be absent from an ordinary build.' }
    } finally { $pe.Dispose(); $stream.Dispose() }
}

function Assert-CurtainDisplayMetadata([string]$assemblyPath) {
    $stream = [IO.File]::OpenRead($assemblyPath)
    $pe = [Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        $reader = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        $found = @{}
        foreach ($handle in $reader.GetAssemblyDefinition().GetCustomAttributes()) {
            $attribute = $reader.GetCustomAttribute($handle)
            if ($attribute.Constructor.Kind -ne [Reflection.Metadata.HandleKind]::MemberReference) { continue }
            $member = $reader.GetMemberReference([Reflection.Metadata.MemberReferenceHandle]$attribute.Constructor)
            if ($member.Parent.Kind -ne [Reflection.Metadata.HandleKind]::TypeReference) { continue }
            $type = $reader.GetTypeReference([Reflection.Metadata.TypeReferenceHandle]$member.Parent)
            if ($reader.GetString($type.Namespace) -ne 'System.Reflection' -or
                $reader.GetString($type.Name) -ne 'AssemblyMetadataAttribute') { continue }
            $blob = $reader.GetBlobReader($attribute.Value)
            if ($blob.ReadUInt16() -ne 1) { throw 'Invalid agent assembly metadata.' }
            $key = $blob.ReadSerializedString()
            $value = $blob.ReadSerializedString()
            if ($key -in 'PlayhubCurtainDisplaySha256', 'PlayhubCurtainCallbackSha256') {
                if ($found.ContainsKey($key)) { throw "Duplicate first-content display metadata: $key" }
                $found[$key] = $value
            }
        }
        if ($CurtainDisplayNative) {
            if ($found['PlayhubCurtainDisplaySha256'] -cne $curtainDisplayHash -or
                $found['PlayhubCurtainCallbackSha256'] -cne $bandCallbackHash) {
                throw 'The agent must pin both exact packaged first-content display components.'
            }
        } elseif ($found.Count) { throw 'first-content display pins must be absent from an ordinary build.' }
    } finally { $pe.Dispose(); $stream.Dispose() }
}

function Assert-ControllerDriverSetupMetadata([string]$assemblyPath) {
    $stream = [IO.File]::OpenRead($assemblyPath)
    $pe = [Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        $reader = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        $values = [Collections.Generic.List[string]]::new()
        foreach ($handle in $reader.GetAssemblyDefinition().GetCustomAttributes()) {
            $attribute = $reader.GetCustomAttribute($handle)
            if ($attribute.Constructor.Kind -ne [Reflection.Metadata.HandleKind]::MemberReference) { continue }
            $member = $reader.GetMemberReference([Reflection.Metadata.MemberReferenceHandle]$attribute.Constructor)
            if ($member.Parent.Kind -ne [Reflection.Metadata.HandleKind]::TypeReference) { continue }
            $type = $reader.GetTypeReference([Reflection.Metadata.TypeReferenceHandle]$member.Parent)
            if ($reader.GetString($type.Namespace) -ne 'System.Reflection' -or
                $reader.GetString($type.Name) -ne 'AssemblyMetadataAttribute') { continue }
            $blob = $reader.GetBlobReader($attribute.Value)
            if ($blob.ReadUInt16() -ne 1) { throw 'Invalid setup metadata.' }
            $key = $blob.ReadSerializedString()
            $value = $blob.ReadSerializedString()
            if ($key -ceq 'PlayhubControllerDriverRequired') { $values.Add($value) }
        }
        if (($XboxSteamRelayClient -and ($values.Count -ne 1 -or $values[0] -cne 'true')) -or
            (!$XboxSteamRelayClient -and $values.Count -ne 0)) {
            throw 'Controller dependency setup metadata differs from the packaged relay capability.'
        }
    } finally { $pe.Dispose(); $stream.Dispose() }
}

if ($Candidate) { Assert-PackagingDirectory $output $repository }
New-Item -ItemType Directory -Path $stage, $output -Force | Out-Null
Write-Host "Production staging: $stage"
try {
    $stubDir = Join-Path $stage 'stub'
    $publish = Join-Path $stage 'normal'
    if (!$ReuseStage) {
    & (Join-Path $setupRoot '..\GamingModeDeckyPlugin\build-plugin.bat')
    if ($LASTEXITCODE) { throw 'Current Decky source build failed.' }
    & dotnet publish (Join-Path $setupRoot 'PlayhubSetup.csproj') -c Release -r win-x64 "-p:PlayhubControllerDriverRequired=$controllerDriverRequired" -o $stubDir
    if ($LASTEXITCODE) { throw 'Installer stub build failed.' }
    Assert-ControllerDriverSetupMetadata (Join-Path $setupRoot 'bin/Release/net8.0-windows/win-x64/Playhub Setup.dll')
    $stub = Join-Path $stubDir 'Playhub Setup.exe'

    $publish = Join-Path $stage 'normal'
    & dotnet publish $appProject -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:WindowsAppSDKSelfContained=true -p:PlayhubUiReview=false -p:PlayhubUpdatePreview=false "-p:PlayhubAppContainerBridgeSha256=$bridgeHash" "-p:PlayhubSteamCompanionSha256=$companionHash" "-p:PlayhubSteamBandCallbackSha256=$bandCallbackHash" "-p:PlayhubUwpSteamCompanion=$uwpSteamCompanion" "-p:PlayhubSteamControllerProbe=$controllerProbe" "-p:PlayhubSteamControllerLayoutTrial=$controllerLayout" "-p:PlayhubSteamInputReaderSha256=$inputReaderHash" "-p:PlayhubSteamInputRelayDriverSha256=$relayDriverHash" "-p:PlayhubCupheadWindowedPreference=$cupheadWindowedPreference" -o $publish
    if ($LASTEXITCODE) { throw 'App normal build failed.' }
    foreach ($helper in 'Playhub.GameSession', 'Playhub.XboxSession') {
        Assert-UwpCompanionMetadata (Join-Path $setupRoot ("../$helper/bin/Release/net8.0-windows/win-x64/$helper.dll"))
    }
    if ($XboxOverlayPilotBridge) {
        $packagedBridge = Join-Path $publish 'WsgmUwpBridge.dll'
        Copy-Item -LiteralPath $XboxOverlayPilotBridge -Destination $packagedBridge
        if ((Get-FileHash -LiteralPath $packagedBridge -Algorithm SHA256).Hash -cne $bridgeHash) { throw 'The bridge changed while building.' }
    }
    if ($XboxSteamCompanion) {
        $packagedCompanion = Join-Path $publish 'PlayhubSteamCompanion.dll'
        Copy-Item -LiteralPath $XboxSteamCompanion -Destination $packagedCompanion
        if ((Get-FileHash -LiteralPath $packagedCompanion -Algorithm SHA256).Hash -cne $companionHash) { throw 'The companion changed while building.' }
    }
    if ($XboxSteamBandCallback) {
        $packagedCallback = Join-Path $publish 'PlayhubExactBand.dll'
        Copy-Item -LiteralPath $XboxSteamBandCallback -Destination $packagedCallback
        if ((Get-FileHash -LiteralPath $packagedCallback -Algorithm SHA256).Hash -cne $bandCallbackHash) {
            throw 'The mirror callback changed while building.'
        }
    }
    if ($XboxSteamInputReader) {
        $packagedReader = Join-Path $publish 'PlayhubSteamInputReader.exe'
        Copy-Item -LiteralPath $XboxSteamInputReader -Destination $packagedReader
        if ((Get-FileHash -LiteralPath $packagedReader -Algorithm SHA256).Hash -cne $inputReaderHash) { throw 'The input reader changed while building.' }
    }
    if ($XboxSteamRelayClient) {
        $packagedRelay = Join-Path $publish 'ViGEmClient.x64.dll'
        $packagedLicense = Join-Path $publish 'XboxSession\ViGEmClient-LICENSE.txt'
        Copy-Item -LiteralPath $XboxSteamRelayClient -Destination $packagedRelay
        Copy-Item -LiteralPath $XboxSteamRelayLicense -Destination $packagedLicense
        if ((Get-FileHash -LiteralPath $packagedRelay -Algorithm SHA256).Hash -cne $relayClientHash -or
            (Get-FileHash -LiteralPath $packagedLicense -Algorithm SHA256).Hash -cne $relayLicenseHash) {
            throw 'The ViGEmClient file or license changed while building.'
        }
        $driverPayload = Join-Path $publish 'Dependencies\ViGEmBus'
        New-Item -ItemType Directory -Path $driverPayload -Force | Out-Null
        foreach ($file in (Get-ControllerDriverFiles)) {
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $driverPayload $file.Name)
        }
    }
    Assert-ReleaseFlags (Join-Path $publish 'Playhub.dll')
    & dotnet publish (Join-Path $setupRoot '..\GamingModeAgent\GamingMode.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true "-p:PlayhubCurtainDisplaySha256=$curtainDisplayHash" "-p:PlayhubCurtainCallbackSha256=$(if ($CurtainDisplayNative) { $bandCallbackHash } else { '' })" "-p:PlayhubHudDisplaySha256=$hudDisplayHash" "-p:PlayhubHudDisplayCallbackSha256=$(if ($HudDisplayNative) { $bandCallbackHash } else { '' })" -o $agentDir
    if ($LASTEXITCODE) { throw 'Gaming Mode agent build failed.' }
    Assert-HudDisplayMetadata (Join-Path $setupRoot '..\GamingModeAgent\bin\Release\net8.0-windows10.0.19041.0\win-x64\GamingMode.dll')
    Assert-CurtainDisplayMetadata (Join-Path $setupRoot '..\GamingModeAgent\bin\Release\net8.0-windows10.0.19041.0\win-x64\GamingMode.dll')
    $agentPayload = Join-Path $publish 'Plugins\Gaming Mode\gaming-mode-win-x64'
    New-Item -ItemType Directory -Path $agentPayload -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $agentDir 'GamingMode.exe') -Destination $agentPayload -Force
    if ($HudDisplayNative) {
        if ((Get-FileHash -LiteralPath $HudDisplayNative -Algorithm SHA256).Hash -cne $hudDisplayHash -or
            (Get-FileHash -LiteralPath $XboxSteamBandCallback -Algorithm SHA256).Hash -cne $bandCallbackHash) {
            throw 'The pinned HUD display component changed during publishing.'
        }
        Copy-Item -LiteralPath $HudDisplayNative -Destination (Join-Path $agentPayload 'PlayhubHudDisplayNative.dll')
        Copy-Item -LiteralPath $XboxSteamBandCallback -Destination (Join-Path $agentPayload 'PlayhubExactBand.dll')
        if ((Get-FileHash -LiteralPath (Join-Path $agentPayload 'PlayhubHudDisplayNative.dll') -Algorithm SHA256).Hash -cne $hudDisplayHash -or
            (Get-FileHash -LiteralPath (Join-Path $agentPayload 'PlayhubExactBand.dll') -Algorithm SHA256).Hash -cne $bandCallbackHash) {
            throw 'The copied HUD display payload differs from the pinned components.'
        }
    }
    if ($CurtainDisplayNative) {
        $components = @{'PlayhubCurtainDisplayNative.dll' = @($CurtainDisplayNative, $curtainDisplayHash);
                        'PlayhubWindowContentRead.exe' = @($WindowContentObserver, $windowContentHash);
                        'PlayhubExactBand.dll' = @($XboxSteamBandCallback, $bandCallbackHash)}
        foreach ($name in $components.Keys) {
            $item = $components[$name]
            if ((Get-FileHash -LiteralPath $item[0] -Algorithm SHA256).Hash -cne $item[1]) { throw 'The first-content component changed during publishing.' }
            Copy-Item -LiteralPath $item[0] -Destination (Join-Path $agentPayload $name) -Force
            if ((Get-FileHash -LiteralPath (Join-Path $agentPayload $name) -Algorithm SHA256).Hash -cne $item[1]) { throw 'The copied first-content component differs.' }
        }
        Copy-Item -LiteralPath (Join-Path $setupRoot '../Playhub.XboxSession/Native/HudDisplay/WindowContent/LICENSE') -Destination (Join-Path $agentPayload 'WindowContent-LICENSE.txt') -Force
    }
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
        if ($XboxOverlayPilotBridge) { $runtimeEntries += 'WsgmUwpBridge.dll' }
        elseif ($archive.GetEntry('WsgmUwpBridge.dll')) { throw 'The Xbox overlay pilot is limited to fresh private candidates.' }
        if ($XboxSteamCompanion) { $runtimeEntries += 'PlayhubSteamCompanion.dll' }
        elseif ($archive.GetEntry('PlayhubSteamCompanion.dll')) { throw 'The Xbox companion is limited to fresh private candidates.' }
        if ($XboxSteamBandCallback) { $runtimeEntries += 'PlayhubExactBand.dll' }
        elseif ($archive.GetEntry('PlayhubExactBand.dll')) { throw 'The mirror callback is limited to fresh private candidates.' }
        if ($HudDisplayNative) {
            $runtimeEntries += 'Plugins/Gaming Mode/gaming-mode-win-x64/PlayhubHudDisplayNative.dll', 'Plugins/Gaming Mode/gaming-mode-win-x64/PlayhubExactBand.dll'
        }
        elseif ($archive.GetEntry('Plugins/Gaming Mode/gaming-mode-win-x64/PlayhubHudDisplayNative.dll') -or
            (!$CurtainDisplayNative -and $archive.GetEntry('Plugins/Gaming Mode/gaming-mode-win-x64/PlayhubExactBand.dll'))) {
            throw 'The HUD display is limited to fresh private candidates.'
        }
        if ($CurtainDisplayNative) {
            $runtimeEntries += 'Plugins/Gaming Mode/gaming-mode-win-x64/PlayhubCurtainDisplayNative.dll', 'Plugins/Gaming Mode/gaming-mode-win-x64/PlayhubWindowContentRead.exe', 'Plugins/Gaming Mode/gaming-mode-win-x64/PlayhubExactBand.dll', 'Plugins/Gaming Mode/gaming-mode-win-x64/WindowContent-LICENSE.txt'
        } elseif ($archive.GetEntry('Plugins/Gaming Mode/gaming-mode-win-x64/PlayhubCurtainDisplayNative.dll') -or
                  $archive.GetEntry('Plugins/Gaming Mode/gaming-mode-win-x64/PlayhubWindowContentRead.exe') -or
                  $archive.GetEntry('Plugins/Gaming Mode/gaming-mode-win-x64/WindowContent-LICENSE.txt')) {
            throw 'The first-content cover is limited to fresh private candidates.'
        }
        if ($XboxSteamInputReader) { $runtimeEntries += 'PlayhubSteamInputReader.exe' }
        elseif ($archive.GetEntry('PlayhubSteamInputReader.exe')) { throw 'The Xbox input reader is limited to fresh private candidates.' }
        if ($XboxSteamRelayClient) {
            $runtimeEntries += 'ViGEmClient.x64.dll', 'XboxSession/ViGEmClient-LICENSE.txt'
            $runtimeEntries += @(Get-ControllerDriverFiles | ForEach-Object { 'Dependencies/ViGEmBus/' + $_.Name })
        }
        elseif ($archive.GetEntry('ViGEmClient.x64.dll')) { throw 'The Xbox relay is limited to fresh private candidates.' }
        if (!$XboxSteamRelayClient -and @($archive.Entries | Where-Object { $_.FullName.StartsWith('Dependencies/ViGEmBus/') }).Count) {
            throw 'An ordinary build must not contain the private controller dependency.'
        }
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
        $officialDeckyAssets = @{}
        $officialDeckyRoot = [IO.Path]::GetFullPath((Join-Path $setupRoot '..\Playhub\Assets\DeckyRuntime\Official-3.3.1'))
        foreach ($file in (Get-OfficialDeckyFiles)) {
            $relativeAsset = [IO.Path]::GetRelativePath($officialDeckyRoot, $file.FullName).Replace('\', '/')
            $officialDeckyAssets["Assets/DeckyRuntime/Official-3.3.1/$relativeAsset"] = $file.FullName
        }
        if (@($archive.Entries | Where-Object { $_.FullName.StartsWith('Assets/DeckyRuntime/') -and $_.FullName -ne 'Assets/DeckyRuntime/Playhub-DeckyHost.exe' -and !$officialDeckyAssets.ContainsKey($_.FullName) }).Count) {
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
            'Assets/GamingMode/retire-gamebar-watcher.ps1' = Join-Path $setupRoot '..\Playhub\Assets\GamingMode\retire-gamebar-watcher.ps1'
            'Plugins/Gaming Mode/gaming-mode-win-x64/owned-helpers.ps1' = Join-Path $setupRoot '..\Playhub\Plugins\Gaming Mode\gaming-mode-win-x64\owned-helpers.ps1'
            'Plugins/Gaming Mode/gaming-mode-win-x64/install.ps1' = Join-Path $setupRoot '..\Playhub\Plugins\Gaming Mode\gaming-mode-win-x64\install.ps1'
            'Plugins/Gaming Mode/gaming-mode-win-x64/uninstall.ps1' = Join-Path $setupRoot '..\Playhub\Plugins\Gaming Mode\gaming-mode-win-x64\uninstall.ps1'
        }
        foreach ($asset in $officialDeckyAssets.Keys) { $dataAssets[$asset] = $officialDeckyAssets[$asset] }
        if ($HudDisplayNative) {
            $dataAssets['Plugins/Gaming Mode/gaming-mode-win-x64/PlayhubHudDisplayNative.dll'] = $HudDisplayNative
            $dataAssets['Plugins/Gaming Mode/gaming-mode-win-x64/PlayhubExactBand.dll'] = $XboxSteamBandCallback
        }
        if ($CurtainDisplayNative) {
            $dataAssets['Plugins/Gaming Mode/gaming-mode-win-x64/PlayhubCurtainDisplayNative.dll'] = $CurtainDisplayNative
            $dataAssets['Plugins/Gaming Mode/gaming-mode-win-x64/PlayhubWindowContentRead.exe'] = $WindowContentObserver
            $dataAssets['Plugins/Gaming Mode/gaming-mode-win-x64/PlayhubExactBand.dll'] = $XboxSteamBandCallback
            $dataAssets['Plugins/Gaming Mode/gaming-mode-win-x64/WindowContent-LICENSE.txt'] = Join-Path $setupRoot '../Playhub.XboxSession/Native/HudDisplay/WindowContent/LICENSE'
        }
        foreach ($file in @('PresentMon.exe', 'LICENSE.txt', 'THIRD_PARTY.txt', 'SOURCE.txt', 'live-output.patch')) {
            $asset = "Plugins/Gaming Mode/gaming-mode-win-x64/PresentMon/$file"
            $dataAssets[$asset] = Join-Path $setupRoot ("../Playhub/" + $asset)
        }
        foreach ($helper in (Get-ChildItem -LiteralPath (Join-Path $setupRoot '..\GamingModeDeckyPlugin\quick_settings') -File -Filter '*.py')) {
            $dataAssets["Assets/GamingModeDeckyPlugin/gaming-mode/quick_settings/$($helper.Name)"] = $helper.FullName
        }
        foreach ($asset in $tutorialAssets) {
            $dataAssets[$asset] = Join-Path $setupRoot ("../Playhub/" + $asset)
        }
        if (@($archive.Entries | Where-Object { $_.FullName -match '^Assets/Welcome/welcome-(?:[0-5]|bg)\.mp4$' }).Count) {
            throw 'Obsolete onboarding video found in the release.'
        }
        $xboxSourceRoot = [IO.Path]::GetFullPath((Join-Path $setupRoot '..\Playhub.XboxSession'))
        $xboxSources = @(Get-XboxSessionSources $xboxSourceRoot)
        $xboxSourceEntries = @()
        foreach ($xboxFile in $xboxSources) {
            $entryName = 'XboxSession/source/' + [IO.Path]::GetRelativePath($xboxSourceRoot, $xboxFile.FullName).Replace('\', '/')
            $dataAssets[$entryName] = $xboxFile.FullName
            $xboxSourceEntries += $entryName
        }
        $xboxSharedSourceEntries = @()
        foreach ($name in @('EpicLaunchWindows.cs', 'EpicLaunchCatalog.cs', 'EpicLaunchPayload.cs')) {
            $entryName = 'XboxSession/Shared/' + $name
            $dataAssets[$entryName] = Join-Path $setupRoot ('..\Shared\' + $name)
            $xboxSharedSourceEntries += $entryName
        }
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName.StartsWith('XboxSession/source/') -and !$entry.FullName.EndsWith('/') -and $entry.FullName -notin $xboxSourceEntries) {
                throw "Unexpected Xbox corresponding source payload: $($entry.FullName)"
            }
            if ($entry.FullName.StartsWith('XboxSession/Shared/') -and !$entry.FullName.EndsWith('/') -and $entry.FullName -notin $xboxSharedSourceEntries) {
                throw "Unexpected Xbox shared corresponding source payload: $($entry.FullName)"
            }
        }
        foreach ($language in @('it', 'en', 'es', 'fr', 'de', 'pt', 'uk', 'zh', 'ja', 'ko', 'hi', 'ru')) {
            $dataAssets["Assets/Localization/$language.json"] = Join-Path $setupRoot "..\Playhub\Assets\Localization\$language.json"
        }
        foreach ($pluginName in @('Artwork', 'Metadata', 'ThemeDeck', 'Launch Curtain', 'Playhub Notifications', 'Now Playing', 'TrailerHero', 'Weather', 'Discord', 'CSS Loader')) {
            $pluginSource = Join-Path $setupRoot "..\Playhub\Plugins\$pluginName"
            if ($pluginName -in @('Metadata', 'Launch Curtain', 'Weather', 'Discord', 'CSS Loader')) {
                $installers = @(Get-ChildItem -LiteralPath $pluginSource -File -Filter '*.zip')
                if ($installers.Count -ne 1) { throw "Expected one current bundled installer for $pluginName." }
                foreach ($bundledFile in (Get-ChildItem -LiteralPath $pluginSource -Recurse -File)) {
                    $relativeAsset = [IO.Path]::GetRelativePath($pluginSource, $bundledFile.FullName).Replace('\', '/')
                    $dataAssets["Plugins/$pluginName/$relativeAsset"] = $bundledFile.FullName
                }
            }
            if ($pluginName -eq 'CSS Loader') { continue }
            $marker = Join-Path $pluginSource 'release-info.json'
            if (!(Test-Path -LiteralPath $marker)) { throw "Missing bundled version provenance for $pluginName." }
            $dataAssets["Plugins/$pluginName/release-info.json"] = $marker
        }
        if (@($archive.Entries | Where-Object { $_.FullName -match '^Plugins/(?!Metadata/|Launch Curtain/|Weather/|Discord/|CSS Loader/).*\.zip$' }).Count) {
            throw 'Remotely available plugin installer archives must not be bundled.'
        }
        foreach ($file in (Get-ControllerDriverFiles)) {
            $dataAssets['Dependencies/ViGEmBus/' + $file.Name] = $file.FullName
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
    if (!$Candidate) { Archive-PreviousPublicOutput }
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

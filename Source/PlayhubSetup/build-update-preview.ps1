param([switch]$PreviewOnly)

$ErrorActionPreference = 'Stop'
$setupRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$appProject = Join-Path $setupRoot '..\Playhub\Playhub.csproj'
$objRoot = Join-Path $setupRoot 'obj'
$stage = Join-Path $objRoot ('update-preview-' + [Guid]::NewGuid().ToString('N'))
$output = Join-Path $setupRoot 'Output'

function Assert-PreviewFlag([string]$assemblyPath, [bool]$expected) {
    $stream = [IO.File]::OpenRead($assemblyPath)
    $pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        $reader = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        $found = $false
        foreach ($handle in $reader.TypeDefinitions) {
            $type = $reader.GetTypeDefinition($handle)
            if ($reader.GetString($type.Name) -eq 'PlayhubUpdatePolicy') {
                foreach ($fieldHandle in $type.GetFields()) {
                    $field = $reader.GetFieldDefinition($fieldHandle)
                    if ($reader.GetString($field.Name) -eq 'IsPreview') {
                        $constant = $reader.GetConstant($field.GetDefaultValue())
                        $value = $reader.GetBlobBytes($constant.Value)[0] -ne 0
                        if ($value -ne $expected) { throw 'Wrong update preview flag in payload.' }
                        $found = $true
                    }
                }
            }
            foreach ($methodHandle in $type.GetMethods()) {
                if ($reader.GetString($reader.GetMethodDefinition($methodHandle).Name) -eq 'RunUiReviewAsync') {
                    throw 'UI review code must not be shipped.'
                }
            }
        }
        if (!$found) { throw 'Missing update policy in payload.' }
    } finally { $pe.Dispose(); $stream.Dispose() }
}

New-Item -ItemType Directory -Path $stage, $output -Force | Out-Null
try {
    $stubDir = Join-Path $stage 'stub'
    & dotnet publish (Join-Path $setupRoot 'PlayhubSetup.csproj') -c Release -r win-x64 --no-restore -o $stubDir
    if ($LASTEXITCODE) { throw 'Installer stub build failed.' }
    $stub = Join-Path $stubDir 'Playhub Setup.exe'

    # Build the test variant first, leaving the normal Release build last.
    $variants = if ($PreviewOnly) { @($true) } else { @($true, $false) }
    foreach ($preview in $variants) {
        $variant = if ($preview) { 'preview' } else { 'normal' }
        $publish = Join-Path $stage $variant
        $flag = $preview.ToString().ToLowerInvariant()
        & dotnet publish $appProject -c Release -r win-x64 --self-contained true --no-restore -p:Platform=x64 -p:WindowsAppSDKSelfContained=true -p:PlayhubUiReview=false "-p:PlayhubUpdatePreview=$flag" -o $publish
        if ($LASTEXITCODE) { throw "App $variant build failed." }
        Assert-PreviewFlag (Join-Path $publish 'Playhub.dll') $preview
        $zipPath = Join-Path $stage "$variant.zip"
        [IO.Compression.ZipFile]::CreateFromDirectory($publish, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $false)
        $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
        try {
            foreach ($required in @('Playhub.exe', 'Playhub.dll', 'Assets/Brand/cube.png', 'Assets/Brand/playhub-wordmark-white.png', 'Assets/Welcome/Mascots/final-onboarding.png')) {
                if ($null -eq $archive.GetEntry($required)) { throw "Missing payload entry: $required" }
            }
            $artwork = $archive.GetEntry('Assets/Welcome/Mascots/final-onboarding.png').Open()
            try {
                $actual = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($artwork))
                $expected = (Get-FileHash -LiteralPath (Join-Path $setupRoot '..\Playhub\Assets\Welcome\Mascots\final-onboarding.png') -Algorithm SHA256).Hash
                if ($actual -ne $expected) { throw 'Final welcome artwork does not match the approved source asset.' }
            } finally { $artwork.Dispose() }
            $dataAssets = @{
                'Assets/PluginCatalog/store-catalog.json' = Join-Path $setupRoot '..\..\catalog\plugins.json'
            }
            foreach ($language in @('it', 'en', 'es', 'fr', 'de', 'pt', 'uk', 'zh', 'ja', 'ko', 'hi', 'ru')) {
                $dataAssets["Assets/Localization/$language.json"] = Join-Path $setupRoot "..\Playhub\Assets\Localization\$language.json"
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
        $name = if ($preview) { 'Playhub Setup - Update Test.exe' } else { 'Playhub Setup.exe' }
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
        Move-Item -LiteralPath $partial -Destination $destination -Force
        Get-Item -LiteralPath $destination | Select-Object FullName, Length, LastWriteTime
    }
} finally {
    $resolvedStage = [IO.Path]::GetFullPath($stage)
    $resolvedObj = [IO.Path]::GetFullPath($objRoot).TrimEnd('\') + '\'
    if (!$resolvedStage.StartsWith($resolvedObj, [StringComparison]::OrdinalIgnoreCase) -or
        !(Split-Path $resolvedStage -Leaf).StartsWith('update-preview-')) {
        throw 'Refusing to clean staging path outside the installer obj directory.'
    }
    if (Test-Path -LiteralPath $resolvedStage) { Remove-Item -LiteralPath $resolvedStage -Recurse -Force }
}

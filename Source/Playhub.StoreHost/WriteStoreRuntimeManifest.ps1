# SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
param(
    [Parameter(Mandatory=$true)][string]$BundleDirectory,
    [string]$AppDirectory,
    [string[]]$DeckyBinaryPaths = @()
)
$ErrorActionPreference = 'Stop'
function Get-Sha256([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($hash.ComputeHash($stream))).Replace('-', '') }
    finally { $hash.Dispose(); $stream.Dispose() }
}
$bundle = [IO.Path]::GetFullPath($BundleDirectory).TrimEnd('\','/')
if (-not [IO.Directory]::Exists($bundle)) { throw 'Store runtime publish directory is missing.' }
$hostPath = Join-Path $bundle 'Playhub.StoreHost.exe'
if (-not [IO.File]::Exists($hostPath)) { throw 'Published StoreHost is missing.' }
$expected = @{
    'legendary_windows_x64.exe' = '6BE77857DC0A6DD33EA7442CE8B5291944EC6DA53757F81CA95B57CE794BAF5B'
    'gogdl_windows_x86_64.exe' = '291B653E6DBF7A394A3342ED0EBCEA2CBC83E5F66D030777D65CCDD60D18AB02'
}
foreach ($entry in $expected.GetEnumerator()) {
    if ((Get-Sha256 (Join-Path $bundle $entry.Key)) -ne $entry.Value) { throw 'Pinned store tool differs from its official release.' }
}
$clients = @()
$finalized = -not [string]::IsNullOrWhiteSpace($AppDirectory)
if ($finalized) {
    $app = [IO.Path]::GetFullPath($AppDirectory).TrimEnd('\','/')
    if (-not $bundle.Equals((Join-Path $app 'StoreLibrary'), [StringComparison]::OrdinalIgnoreCase)) { throw 'Finalize only the actual app StoreLibrary directory.' }
    foreach ($name in @('Playhub.exe', 'Playhub.GameSession.exe')) {
        $path = Join-Path $app $name
        if (-not [IO.File]::Exists($path)) { throw 'Publish the app and GameSession before finalizing store clients.' }
        $clients += [ordered]@{kind='app';path=$name;sha256=(Get-Sha256 $path)}
    }
    $companion = Join-Path $app 'Playhub.XboxSession.exe'
    if (-not [IO.File]::Exists($companion)) { throw 'Publish XboxSession before finalizing its read-only store client.' }
    $clients += [ordered]@{kind='xbox-companion';path='Playhub.XboxSession.exe';sha256=(Get-Sha256 $companion);methods=@('launch-descriptor')}
    foreach ($path in $DeckyBinaryPaths) {
        $name = [IO.Path]::GetFileName($path)
        if ($name -notin @('PluginLoader.exe','PluginLoader_noconsole.exe')) { throw 'Unexpected Decky executable.' }
        $clients += [ordered]@{kind='decky';fileName=$name;sha256=(Get-Sha256 $path)}
    }
}
$members = @()
foreach ($file in (Get-ChildItem -LiteralPath $bundle -Recurse -File | Sort-Object FullName)) {
    if ($file.Name -in @('store-runtime.json','store-runtime.json.new')) { continue }
    if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Runtime members cannot be reparse points.' }
    $relative = $file.FullName.Substring($bundle.Length + 1).Replace('\','/')
    $members += [ordered]@{path=$relative;bytes=$file.Length;sha256=(Get-Sha256 $file.FullName)}
}
if ($members.Count -lt 1 -or $members.Count -gt 512) { throw 'Unexpected runtime closure size.' }
$manifest = [ordered]@{protocol=1;finalized=$finalized;hostSha256=(Get-Sha256 $hostPath);allowedClients=$clients;files=$members}
$json = $manifest | ConvertTo-Json -Depth 6
[IO.File]::WriteAllText((Join-Path $bundle 'store-runtime.json.new'), $json, (New-Object Text.UTF8Encoding($false)))
Move-Item -LiteralPath (Join-Path $bundle 'store-runtime.json.new') -Destination (Join-Path $bundle 'store-runtime.json') -Force
Write-Output ('Store runtime manifest: finalized={0}, files={1}, clients={2}' -f $finalized,$members.Count,$clients.Count)

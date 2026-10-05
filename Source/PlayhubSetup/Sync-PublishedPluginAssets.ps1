$ErrorActionPreference = 'Stop'
# Solo output della build: non viene mai eseguito su installazioni o cartelle utente.
$source = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\GamingModeDeckyPlugin\dist'))
$publish = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Playhub\dist_publish'))
$target = Join-Path $publish 'Assets\GamingModeDeckyPlugin\gaming-mode\dist'
foreach ($path in @((Join-Path $source 'assets'), (Join-Path $target 'assets'))) {
    for ($current = $path; $current; $current = [IO.Path]::GetDirectoryName($current)) {
        if ((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Linked build directory refused.'
        }
    }
}
if ((Get-FileHash -LiteralPath (Join-Path $source 'index.js')).Hash -ne (Get-FileHash -LiteralPath (Join-Path $target 'index.js')).Hash) {
    throw 'Published plugin bundle differs from source build.'
}
$expected = @{}
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $source 'assets') -File) {
    if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked source asset refused.' }
    $expected[$file.Name] = $true
    $copy = Join-Path $target ('assets\' + $file.Name)
    if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $copy).Hash) {
        throw "Published plugin asset differs: $($file.Name)"
    }
}
if ($expected.Count -eq 0) { throw 'Empty source assets refused.' }
$removed = 0
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $target 'assets') -File) {
    if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked published asset refused.' }
    if (-not $expected.ContainsKey($file.Name)) { Remove-Item -LiteralPath $file.FullName -Force; $removed++ }
}
Write-Output "Published plugin assets: $($expected.Count) verified, $removed obsolete build copies removed."

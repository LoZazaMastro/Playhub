#requires -Version 5.1
param([Parameter(Mandatory)][string]$FixtureRoot,[switch]$ReadOnlyActualAlias)
$ErrorActionPreference='Stop'
$source=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Playhub\Plugins\Gaming Mode\gaming-mode-win-x64\install.ps1'))
$owner=[IO.Path]::GetFullPath('F:\Playhub\Plugin\Playhub\.local\red-code')
$FixtureRoot=[IO.Path]::GetFullPath($FixtureRoot)
if ([IO.Path]::GetDirectoryName($FixtureRoot) -ne $owner -or [IO.Path]::GetFileName($FixtureRoot) -notmatch '^gaming-mode-install-contract-[0-9a-f]{32}$' -or (Test-Path -LiteralPath $FixtureRoot)) { throw 'New private GUID fixture root required.' }
for ($part=$owner;$part;$part=[IO.Path]::GetDirectoryName($part)) { if ((Get-Item -LiteralPath $part -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Fixture owner redirected.' } }
$tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile($source,[ref]$tokens,[ref]$errors)
$checks=New-Object 'Collections.Generic.List[object]'
function Check([string]$Name,[bool]$Passed){$checks.Add([ordered]@{Name=$Name;Passed=$Passed});if(!$Passed){throw $Name};Write-Output ('PASS '+$Name)}
Check 'Production install script parses on this PowerShell runtime' ($errors.Count -eq 0)
foreach($name in @('Resolve-GamingModeInstallIdentity','Test-GamingModeInstallProcess')) {
    $definition=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
    if (!$definition) {throw 'Production helper missing.'}
    . ([ScriptBlock]::Create($definition.Extent.Text))
}
# Only the production read/predicate functions above are loaded, never installation/stop/copy code.
[IO.Directory]::CreateDirectory($FixtureRoot)|Out-Null
$unicodeFolder='Spazi '+[char]0x65e5+[char]0x672c+' caf'+[char]0x00e9
$physical=Join-Path (Join-Path $FixtureRoot $unicodeFolder) 'GamingMode.exe';[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($physical))|Out-Null;[IO.File]::WriteAllText($physical,'fixture-not-an-executable')
$unrelated=Join-Path $FixtureRoot 'different-install\GamingMode.exe';[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($unrelated))|Out-Null;[IO.File]::WriteAllText($unrelated,'other fixture')
$otherExe=Join-Path (Join-Path $FixtureRoot $unicodeFolder) 'Other.exe';[IO.File]::WriteAllText($otherExe,'not agent')
$identity=Resolve-GamingModeInstallIdentity $physical
Check 'Native read handle preserves physical space/Unicode path' ([string]::Equals($identity.PhysicalPath,$physical,[StringComparison]::OrdinalIgnoreCase))
Check 'Same-session exact physical agent accepted' (Test-GamingModeInstallProcess $physical 5 5 $identity)
Check 'Case-insensitive Windows agent path accepted' (Test-GamingModeInstallProcess $physical.ToUpperInvariant() 5 5 $identity)
# A canonical identity supplied by the production handle may represent a redirected logical path.
$aliasIdentity=[pscustomobject]@{PhysicalPath=$identity.PhysicalPath;Volume=$identity.Volume;FileIndex=$identity.FileIndex;LogicalPath=(Join-Path $FixtureRoot 'logical-install\GamingMode.exe')}
Check 'Resolved logical alias accepts exact physical process path' (Test-GamingModeInstallProcess $physical 5 5 $aliasIdentity)
Check 'Different session rejected' (!(Test-GamingModeInstallProcess $physical 4 5 $identity))
Check 'Different installation path rejected' (!(Test-GamingModeInstallProcess $unrelated 5 5 $identity))
Check 'Other executable basename rejected' (!(Test-GamingModeInstallProcess $otherExe 5 5 $identity))
Check 'Absent target never authorizes another agent' (!(Test-GamingModeInstallProcess $physical 5 5 $null))
Check 'Missing process path rejected' (!(Test-GamingModeInstallProcess (Join-Path $FixtureRoot 'gone\GamingMode.exe') 5 5 $identity))
Check 'Unreadable or empty process path rejected' (!(Test-GamingModeInstallProcess '' 5 5 $identity))
$wrongIndex=[pscustomobject]@{PhysicalPath=$identity.PhysicalPath;Volume=$identity.Volume;FileIndex=($identity.FileIndex+1)}
Check 'Same canonical path with different file identity rejected' (!(Test-GamingModeInstallProcess $physical 5 5 $wrongIndex))
$wrongVolume=[pscustomobject]@{PhysicalPath=$identity.PhysicalPath;Volume=($identity.Volume+1);FileIndex=$identity.FileIndex}
Check 'Same canonical path on different volume rejected' (!(Test-GamingModeInstallProcess $physical 5 5 $wrongVolume))
$linked=Join-Path $FixtureRoot 'linked-install'
New-Item -ItemType Junction -Path $linked -Target ([IO.Path]::GetDirectoryName($physical))|Out-Null
$rejected=$false;try {Resolve-GamingModeInstallIdentity (Join-Path $linked 'GamingMode.exe')|Out-Null} catch {$rejected=$true}
Check 'Junction installation target fails closed' $rejected
Check 'Junction candidate path cannot authorize termination' (!(Test-GamingModeInstallProcess (Join-Path $linked 'GamingMode.exe') 5 5 $identity))
$hard=Join-Path ([IO.Path]::GetDirectoryName($unrelated)) 'hardlink\GamingMode.exe';[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($hard))|Out-Null
New-Item -ItemType HardLink -Path $hard -Target $physical|Out-Null
$hardIdentity=Resolve-GamingModeInstallIdentity $hard
Check 'Hardlink fixture is same file identity but different canonical path' ($hardIdentity.FileIndex -eq $identity.FileIndex -and $hardIdentity.Volume -eq $identity.Volume -and $hardIdentity.PhysicalPath -ne $identity.PhysicalPath)
Check 'Other installation hardlink rejected despite same bytes/file ID' (!(Test-GamingModeInstallProcess $hard 5 5 $identity))
$missingRejected=$false;try {Resolve-GamingModeInstallIdentity (Join-Path $FixtureRoot 'missing\GamingMode.exe')|Out-Null} catch {$missingRejected=$true}
Check 'Existing-target resolver failure throws instead of widening' $missingRejected
$text=[IO.File]::ReadAllText($source)
Check 'Production stop loop uses semantic predicate and existing target resolves before loop' ($text.IndexOf('$ExpectedIdentity = Resolve-GamingModeInstallIdentity $Target') -lt $text.IndexOf('Get-Process -Name "GamingMode"') -and $text.Contains('Test-GamingModeInstallProcess $_.Path $_.SessionId'))
Check 'Resolver has no package-specific hardcoding' (!$text.Contains('OpenAI.Codex') -and !$text.Contains('LocalCache'))
Check 'Native helper opens only read/share-all streams' ($text.Contains('FileAccess.Read, FileShare.ReadWrite | FileShare.Delete'))
Check 'Installer flags/disable/startup and health retained' ($text.Contains('[switch]$NoStartupTask') -and $text.Contains("if (-not `$NoStartupTask)") -and $text.Contains("`$DisabledMarker = Join-Path `$env:APPDATA 'GamingMode\disabled-by-user'") -and $text.Contains('http://127.0.0.1:47991/health'))
if ($ReadOnlyActualAlias) {
    $logical='C:\Users\Andrea\AppData\Local\GamingMode\GamingMode.exe'
    $actual=Resolve-GamingModeInstallIdentity $logical
    $resolved=Resolve-GamingModeInstallIdentity $actual.PhysicalPath
    Check 'Actual known logical/physical aliases resolve same file and canonical path read-only' ($actual.PhysicalPath -eq $resolved.PhysicalPath -and $actual.FileIndex -eq $resolved.FileIndex -and $actual.Volume -eq $resolved.Volume)
    Check 'Actual canonical physical process-path predicate accepts resolved target read-only' (Test-GamingModeInstallProcess $actual.PhysicalPath 1 1 $actual)
}
$sha=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$receipt=[ordered]@{Passed=$true;Scope='Production helpers AST-linked; private data fixtures and optional known target read-only; no process action or install script run';PowerShellVersion=$PSVersionTable.PSVersion.ToString();Source=$source;SourceSHA256=$sha;Checks=$checks.ToArray();NoProcessesStopped=$true;NoInstalledFilesWritten=$true}
[IO.File]::WriteAllText((Join-Path $FixtureRoot 'receipt.json'),($receipt|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
Write-Output ($checks.Count.ToString()+'/'+$checks.Count+' checks passed.')

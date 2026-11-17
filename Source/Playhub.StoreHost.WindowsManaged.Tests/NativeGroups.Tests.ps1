# Exercise the production traversal against request-scoped SDK-shaped objects.
# No Store activation, real package or filesystem changes.
$ErrorActionPreference='Stop'
$source=Join-Path $PSScriptRoot '../Playhub.StoreHost/Runtime/StoreInstallControl.ps1'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($source,[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Native helper does not parse'}
$function=$ast.Find({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Get-InstallLeaves'},$true)
if(!$function){throw 'Production traversal missing'}
Invoke-Expression $function.Extent.Text
function Item($product,$family,$children=@()) {[pscustomobject]@{ProductId=$product;PackageFamilyName=$family;Children=$children}}
$game=Item 'GAME' 'Exact.Game_abcdefghijklm'
$dependency=Item 'RUNTIME' 'Exact.Runtime_abcdefghijklm'
$other=Item 'OTHER' 'Other.Game_abcdefghijklm'
$group=Item 'GROUP' '' @($game,$dependency)
$leaves=@(Get-InstallLeaves @($group,$game))
if($leaves.Count -ne 2 -or $leaves[0] -ne $game -or $leaves[1] -ne $dependency){throw 'GDK child transfer must replace parent metadata and deduplicate overlapping roots'}
if($leaves -contains $other -or $leaves -contains $group){throw 'Unrelated request or metadata-only parent included'}
$single=@(Get-InstallLeaves @($game))
if($single.Count -ne 1 -or $single[0] -ne $game){throw 'A native UWP request without children remains observable'}
$nested=Item 'NESTED' '' @($group)
if(@(Get-InstallLeaves @($nested)).Count -ne 2){throw 'Nested group lost exact child requests'}
$nullChildren=Item 'NULL' 'Null.Game_abcdefghijklm' $null
if(@(Get-InstallLeaves @($nullChildren)).Count -ne 1){throw 'Empty optional Children is not a group'}
$cycle=Item 'CYCLE' ''
$cycle.Children=@($cycle)
$refused=$false
try{Get-InstallLeaves @($cycle) | Out-Null}catch [IO.InvalidDataException]{$refused=$true}
if(!$refused){throw 'Malformed cyclic native groups must be bounded'}
'PASS 6 native installation group checks (production traversal; inert SDK-shaped objects only)'
$phaseTwo=Item 'GAME' 'Exact.Game_abcdefghijklm'
$twoStage=Item 'GROUP' '' @($game,$phaseTwo)
if(@(Get-InstallLeaves @($twoStage)).Count -ne 2){throw 'Distinct full-game phases with the same product/PFN must both remain observable'}
if(@(Get-InstallLeaves @($twoStage) -All).Count -ne 3){throw 'Completion proof must include the outstanding group root and every phase'}
'PASS full-game phase and group-root completion traversal'

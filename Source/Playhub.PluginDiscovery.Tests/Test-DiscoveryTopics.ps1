# Pure production category functions; no app, account or network is opened.
$ErrorActionPreference='Stop'
$app=Join-Path $PSScriptRoot '../Playhub'
$main=Get-Content -Raw -LiteralPath (Join-Path $app 'MainWindow.xaml.cs')
$discovery=Get-Content -Raw -LiteralPath (Join-Path $app 'MainWindow.PluginDiscovery.cs')
function Member($source,$declaration) {
 $match=[regex]::Match($source,'(?ms)^    '+[regex]::Escape($declaration)+'.*?^    \}')
 if(!$match.Success){throw "Missing production member: $declaration"}
 $match.Value -replace '^    private','    public'
}
$members=(Member $main 'private static string NormalizePluginStoreCategory(')+"`n"+(Member $discovery 'private static string PluginTopicCategory(')
Add-Type -TypeDefinition ("using System; using System.Linq; public static class DiscoveryTopics {`n"+$members+"`n}")
$plugins=(Get-Content -Raw -LiteralPath (Join-Path $app '../../catalog/plugins.json') | ConvertFrom-Json).plugins | Where-Object {$_.active -ne $false}
$topics=$plugins | ForEach-Object { [pscustomobject]@{name=$_.name;topic=[DiscoveryTopics]::PluginTopicCategory($_.name,$_.category)} }
foreach($name in @('SAM','RetroAchievements','Achievement Companion','SDAchievement')) {
 if(($topics | Where-Object name -eq $name).topic -cne 'Achievement'){throw "Achievement grouping: $name"}
}
foreach($name in @('Audio Output Switcher','Deck Mic Toggle','Audio Loader','Music Control','Museck','Audiobookshelf')) {
 if(($topics | Where-Object name -eq $name).topic -cne 'Audio'){throw "Audio grouping: $name"}
}
$visible=@($topics | Group-Object topic | Where-Object Count -ge 3)
if($visible.Count -gt 12 -or @($visible | Where-Object Name -match '/').Count){throw 'Discovery categories are still fragmented'}
if(@($visible | Where-Object Name -eq 'Accessibilità').Count){throw 'Two-plugin category must stay out of Discovery'}
if(@($topics | Where-Object name -eq 'SpeechToDeck').Count -ne 1){throw 'Suppression must not remove plugins from catalog'}
"PASS native Discovery topics on $($plugins.Count) real catalog entries; $($visible.Count) qualifying topics."

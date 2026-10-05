$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot '..\Assets\GamingMode\desktop-safety.ps1'
$tokens = $null; $errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($source, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw ($errors | Out-String) }
# Load only the pure watcher/helper function definitions. Never execute the
# production entry point, P/Invoke, mutex, filesystem log or real HTTP request.
foreach ($name in @('Get-AgentBaseUri', 'Get-AgentMode', 'Invoke-DesktopSafetyWatch')) {
    $fn = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true)
    if (-not $fn) { throw "Missing function $name" }
    Invoke-Expression $fn.Extent.Text
}

$script:passed = 0
Add-Type @'
public sealed class ModeFixtureResponse {
    public int StatusCode { get; set; }
}
public sealed class ModeFixtureHttpException : System.Exception {
    public ModeFixtureResponse Response { get; private set; }
    public ModeFixtureHttpException(int status) : base("Fixture HTTP error") {
        Response = new ModeFixtureResponse { StatusCode = status };
    }
}
'@
function Assert($condition, [string]$label) {
    if (-not $condition) { throw "FAIL: $label" }
    $script:passed++
}
function Reset-Fixture([string[]]$modes, [bool[]]$steam) {
    $script:modes = [System.Collections.Generic.Queue[string]]::new()
    foreach ($mode in $modes) { $script:modes.Enqueue($mode) }
    $script:lastMode = 'Gaming'
    $script:steam = [System.Collections.Generic.Queue[bool]]::new()
    foreach ($present in $steam) { $script:steam.Enqueue($present) }
    $script:lastSteam = $false
    $script:posts = @(); $script:sleeps = 0; $script:shutdown = $false
    $script:gets = @(); $script:legacyModeAgentUri = $null
    $script:modeEndpointFailure = 0; $script:legacyEndpointFailure = 0
    $script:agentRunning = $true
}
function Invoke-RestMethod {
    param($Uri, $Method, $TimeoutSec, $ErrorAction)
    if ($Method -ne 'POST' -or $TimeoutSec -ne 90) { throw 'Unexpected recovery request' }
    $script:posts += $Uri; return @{ ok = $true }
}
function Invoke-AgentModeRequest {
    param($Uri)
    $script:gets += $Uri
    if ($Uri.EndsWith('/mode/current') -and $script:modeEndpointFailure) {
        return @{StatusCode=$script:modeEndpointFailure;Status=$null}
    }
    if ($Uri.EndsWith('/status') -and $script:legacyEndpointFailure) {
        return @{StatusCode=$script:legacyEndpointFailure;Status=$null}
    }
    if ($script:modes.Count) { $script:lastMode = $script:modes.Dequeue() }
    if ($script:lastMode -eq 'unavailable') { return $null }
    return @{StatusCode=200; Status=@{ agentRunning = $script:agentRunning; currentMode = $script:lastMode }}
}
function Test-SteamProcessRunning {
    if ($script:steam.Count) { $script:lastSteam = $script:steam.Dequeue() }
    return $script:lastSteam
}
function Start-Sleep { param($Seconds) if ($Seconds -ne 1) { throw 'Recovery polling cadence changed' }; $script:sleeps++ }
function Test-SystemShuttingDown { return $script:shutdown }
function Write-Log { param($message) }
function Get-Content { param($LiteralPath, [switch]$Raw, $ErrorAction) return $script:configJson }

Reset-Fixture @('Desktop', 'Gaming', 'Desktop') @($true)
Invoke-DesktopSafetyWatch 'http://127.0.0.1:48123' 4 3
Assert ($script:posts.Count -eq 0) 'Manual Desktop return disarms without waiting for Steam exit'
Assert ($script:sleeps -eq 2) 'Watcher permits startup commit delay but exits on Desktop'

Reset-Fixture @('Gaming') @($true, $false, $false, $false, $false)
Invoke-DesktopSafetyWatch 'http://127.0.0.1:48123' 4 3
Assert ($script:posts.Count -eq 1 -and $script:posts[0] -eq 'http://127.0.0.1:48123/mode/desktop/switch') 'Continuous Steam exit sends one request using configured port'

Reset-Fixture @('Gaming') @($true, $false, $false, $true, $false, $false, $false, $false)
Invoke-DesktopSafetyWatch 'http://127.0.0.1:48123' 4 3
Assert ($script:posts.Count -eq 1 -and $script:sleeps -eq 6) 'Steam restart resets stabilization window'

Reset-Fixture @('Gaming', 'Gaming', 'Gaming', 'Gaming', 'Desktop') @($true, $false)
Invoke-DesktopSafetyWatch 'http://127.0.0.1:48123' 4 3
Assert ($script:posts.Count -eq 0) 'Desktop transition immediately before request prevents replay'

Reset-Fixture @('Gaming', 'Gaming', 'Gaming', 'Gaming', 'unavailable') @($true, $false)
Invoke-DesktopSafetyWatch 'http://127.0.0.1:48123' 4 3
Assert ($script:posts.Count -eq 0) 'Unverified final agent state prevents transition'

Reset-Fixture @('Gaming', 'unavailable') @($true)
Invoke-DesktopSafetyWatch 'http://127.0.0.1:48123' 4 3
Assert ($script:posts.Count -eq 0 -and $script:sleeps -eq 3) 'Unavailable agent stops watcher without stale request'

Reset-Fixture @('Desktop') @($true)
Invoke-DesktopSafetyWatch 'http://127.0.0.1:48123' 4 3
Assert ($script:posts.Count -eq 0 -and $script:sleeps -eq 4) 'Watcher never arms in Desktop-only session'

Reset-Fixture @('Gaming') @($true)
$script:shutdown = $true
Invoke-DesktopSafetyWatch 'http://127.0.0.1:48123' 4 3
Assert ($script:posts.Count -eq 0) 'Shutdown does not request Desktop'

$script:configJson = '{"safety":{"apiPort":48123}}'
Assert ((Get-AgentBaseUri) -eq 'http://127.0.0.1:48123') 'Configured valid port is used'
$script:configJson = '{"safety":{"apiPort":99999}}'
Assert ((Get-AgentBaseUri) -eq 'http://127.0.0.1:47991') 'Invalid port uses safe default'
$script:configJson = 'broken json'
Assert ((Get-AgentBaseUri) -eq 'http://127.0.0.1:47991') 'Unreadable configuration uses safe default'

Reset-Fixture @('Gaming') @()
for ($i = 0; $i -lt 20; $i++) { Assert ((Get-AgentMode 'http://127.0.0.1:48123') -eq 'Gaming') 'Current-mode endpoint response' }
Assert ($script:gets.Count -eq 20 -and @($script:gets | Where-Object { $_.EndsWith('/status') }).Count -eq 0) 'Repeated modern polling never calls full status'

Reset-Fixture @('Gaming', 'Desktop') @()
$script:modeEndpointFailure = 404
Assert ((Get-AgentMode 'http://127.0.0.1:48123') -eq 'Gaming') 'Old agent 404 falls back immediately'
Assert ((Get-AgentMode 'http://127.0.0.1:48123') -eq 'Desktop') 'Legacy fallback reads current state rather than caching mode'
for ($i = 0; $i -lt 18; $i++) { $null = Get-AgentMode 'http://127.0.0.1:48123' }
Assert ($script:gets.Count -eq 21 -and @($script:gets | Where-Object { $_.EndsWith('/mode/current') }).Count -eq 1) 'Old route capability is cached without two calls on every tick'
$script:modeEndpointFailure = 0
$null = Get-AgentMode 'http://127.0.0.1:48124'
Assert ($script:gets[-1] -eq 'http://127.0.0.1:48124/mode/current') 'Another agent endpoint is probed independently'

Reset-Fixture @('Gaming') @($true, $false, $false, $false, $false)
$script:modeEndpointFailure = 404
Invoke-DesktopSafetyWatch 'http://127.0.0.1:48123' 4 3
Assert ($script:posts.Count -eq 1 -and $script:sleeps -eq 3 -and @($script:gets | Where-Object { $_.EndsWith('/mode/current') }).Count -eq 1) 'Legacy agent retains one-second recovery and final mode recheck'

foreach ($httpError in @(401, 500, 503)) {
    Reset-Fixture @('Gaming') @()
    $script:modeEndpointFailure = $httpError
    Assert ($null -eq (Get-AgentMode 'http://127.0.0.1:48123')) "HTTP $httpError cannot authorize a transition"
    Assert ($script:gets.Count -eq 1 -and $null -eq $script:legacyModeAgentUri) "HTTP $httpError never enables expensive fallback"
}
Reset-Fixture @('unavailable') @()
Assert ($null -eq (Get-AgentMode 'http://127.0.0.1:48123') -and $script:gets.Count -eq 1) 'Connection failures do not trigger full status'
Reset-Fixture @('Gaming') @()
$script:agentRunning = $false
Assert ($null -eq (Get-AgentMode 'http://127.0.0.1:48123') -and $script:gets.Count -eq 1) 'Invalid agent-running response stays unavailable without fallback'
Reset-Fixture @('Unknown') @()
Assert ($null -eq (Get-AgentMode 'http://127.0.0.1:48123') -and $script:gets.Count -eq 1) 'Unknown modes stay unavailable without fallback'
Reset-Fixture @('Gaming') @($true)
$script:modeEndpointFailure = 404; $script:legacyEndpointFailure = 404
Invoke-DesktopSafetyWatch 'http://127.0.0.1:48123' 4 3
Assert ($script:posts.Count -eq 0 -and $script:sleeps -eq 4 -and $script:gets.Count -eq 5) 'Absent optional agent never arms, restarts or requests a mode change'

Write-Output "PASS: $script:passed isolated Desktop safety watcher checks. No live processes, HTTP, logs or mode changes."

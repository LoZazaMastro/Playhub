# Watches only the current Gaming Mode session. Never replays a Desktop transition
# after the user has already returned to Desktop, even if Steam closes much later.
$ErrorActionPreference = 'SilentlyContinue'
$script:legacyModeAgentUri = $null
$script:modeHttpClient = $null
$script:steamProcess = $null

Add-Type -Namespace PlayhubNative -Name Sys -MemberDefinition '[DllImport("user32.dll")] public static extern int GetSystemMetrics(int nIndex);'

function Test-SystemShuttingDown {
    try { return ([PlayhubNative.Sys]::GetSystemMetrics(0x2000) -ne 0) }
    catch { return $false }
}

$logPath = Join-Path $env:APPDATA 'GamingMode\playhub-safety.log'
function Write-Log([string]$message) {
    try { "$((Get-Date).ToString('yyyy-MM-dd HH:mm:ss')) $message" | Add-Content -LiteralPath $logPath -Encoding UTF8 }
    catch { }
}

function Get-AgentBaseUri {
    $port = 47991
    try {
        $configPath = Join-Path $env:APPDATA 'GamingMode\config.json'
        $config = Get-Content -LiteralPath $configPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
        $configuredPort = 0
        if ([int]::TryParse([string]$config.safety.apiPort, [ref]$configuredPort) -and $configuredPort -gt 0 -and $configuredPort -lt 65536) {
            $port = $configuredPort
        }
    }
    catch { }
    return "http://127.0.0.1:$port"
}

function Test-SteamProcessRunning {
    # A live Process handle is checked every tick, never a cached Boolean.
    if ($null -ne $script:steamProcess) {
        try { if (-not $script:steamProcess.get_HasExited()) { return $true } }
        catch { }
        $script:steamProcess.Dispose()
        $script:steamProcess = $null
    }
    $processes = @()
    try {
        $processes = [Diagnostics.Process]::GetProcessesByName('steam')
        foreach ($process in $processes) {
            try {
                # Pin the original process object before retaining it; a reused
                # PID must not turn an exited Steam instance into a live cache.
                [void]$process.get_SafeHandle()
                if (-not $process.get_HasExited()) {
                    $script:steamProcess = $process
                    return $true
                }
            }
            catch {
                # Preserve the old presence check when Steam is elevated or
                # access is denied. Retry enumeration next tick, without cache.
                return $true
            }
        }
        return $false
    }
    catch { return $false }
    finally {
        foreach ($process in $processes) {
            if ($process -ne $script:steamProcess) { $process.Dispose() }
        }
    }
}

# One loopback client per watcher; response/request handles are always released.
function Invoke-AgentModeRequest([string]$Uri) {
    $request = $null; $response = $null
    try {
        if ($null -eq $script:modeHttpClient) {
            Add-Type -AssemblyName System.Net.Http -ErrorAction Stop
            $handler = [Net.Http.HttpClientHandler]::new()
            $handler.UseProxy = $false
            $script:modeHttpClient = [Net.Http.HttpClient]::new($handler)
            $script:modeHttpClient.Timeout = [TimeSpan]::FromSeconds(2)
        }
        $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, $Uri)
        # ResponseContentRead keeps the two-second deadline on the body too.
        $response = $script:modeHttpClient.SendAsync($request).GetAwaiter().GetResult()
        $code = [int]$response.StatusCode
        $status = $null
        if ($response.IsSuccessStatusCode) {
            $json = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            $status = $json | ConvertFrom-Json -ErrorAction Stop
        }
        return @{ StatusCode = $code; Status = $status }
    }
    catch { return $null }
    finally {
        if ($null -ne $response) { $response.Dispose() }
        if ($null -ne $request) { $request.Dispose() }
    }
}

function Get-AgentMode([string]$baseUri) {
    $route = '/mode/current'
    if ($script:legacyModeAgentUri -eq $baseUri) { $route = '/status' }
    $reply = Invoke-AgentModeRequest "$baseUri$route"
    if ($null -eq $reply) { return $null }
    if ($route -eq '/mode/current' -and $reply.StatusCode -eq 404) {
        $script:legacyModeAgentUri = $baseUri
        $reply = Invoke-AgentModeRequest "$baseUri/status"
    }
    if ($null -eq $reply -or $reply.StatusCode -lt 200 -or $reply.StatusCode -ge 300) { return $null }
    try {
        $status = $reply.Status
        if ($status.agentRunning -eq $true -and $status.currentMode -in @('Gaming', 'Desktop')) {
            return [string]$status.currentMode
        }
    }
    catch { }
    return $null
}

function Invoke-DesktopSafetyWatch([string]$baseUri, [int]$startupPolls = 300, [int]$exitPolls = 15) {
    # Custom startup apps can start just before the agent commits Gaming status.
    # Arm only after both Gaming and Steam have actually been observed.
    $armed = $false
    for ($i = 0; $i -lt $startupPolls; $i++) {
        if (Test-SystemShuttingDown) { return }
        if ((Get-AgentMode $baseUri) -eq 'Gaming' -and (Test-SteamProcessRunning)) {
            $armed = $true
            break
        }
        Start-Sleep -Seconds 1
    }
    if (-not $armed) {
        Write-Log 'Gaming Mode e Steam non rilevati entro il timeout: nessuna modifica.'
        return
    }

    $missingSteam = 0
    $unavailableAgent = 0
    while ($true) {
        Start-Sleep -Seconds 1
        if (Test-SystemShuttingDown) { return }
        $mode = Get-AgentMode $baseUri
        if ($mode -eq 'Desktop') {
            Write-Log 'Sessione Gaming terminata: watcher disattivato.'
            return
        }
        if ($mode -ne 'Gaming') {
            $missingSteam = 0
            $unavailableAgent++
            if ($unavailableAgent -ge $exitPolls) {
                Write-Log 'Stato agente non disponibile: nessuna transizione richiesta.'
                return
            }
            continue
        }
        $unavailableAgent = 0
        if (Test-SteamProcessRunning) {
            $missingSteam = 0
            continue
        }
        $missingSteam++
        if ($missingSteam -lt $exitPolls) { continue }

        # Recheck immediately before sending. The agent also guards same-mode
        # requests under its own transition lock, covering a simultaneous switch.
        if ((Get-AgentMode $baseUri) -ne 'Gaming' -or (Test-SystemShuttingDown) -or (Test-SteamProcessRunning)) { return }
        try {
            $result = Invoke-RestMethod -Uri "$baseUri/mode/desktop/switch" -Method POST -TimeoutSec 90 -ErrorAction Stop
            if ($result.ok -eq $true) { Write-Log 'Desktop Mode ripristinata senza riavviare Windows.' }
            else { Write-Log "L'agente non ha completato il ritorno al Desktop." }
        }
        catch { Write-Log "Errore nel ripristino Desktop via agente: $_" }
        return
    }
}

$watcherMutex = $null
$ownsWatcherMutex = $false
try {
    $watcherMutex = New-Object System.Threading.Mutex($false, 'Global\PlayhubGamingModeDesktopSafety')
    try { $ownsWatcherMutex = $watcherMutex.WaitOne(0, $false) }
    catch [System.Threading.AbandonedMutexException] { $ownsWatcherMutex = $true }
    if (-not $ownsWatcherMutex) { return }
    Write-Log '--- Watcher avviato ---'
    Invoke-DesktopSafetyWatch (Get-AgentBaseUri)
    Write-Log '--- Watcher terminato ---'
}
finally {
    if ($null -ne $script:steamProcess) { $script:steamProcess.Dispose(); $script:steamProcess = $null }
    if ($null -ne $script:modeHttpClient) { $script:modeHttpClient.Dispose(); $script:modeHttpClient = $null }
    if ($ownsWatcherMutex -and $null -ne $watcherMutex) {
        try { $watcherMutex.ReleaseMutex() } catch { }
    }
    if ($null -ne $watcherMutex) {
        try { $watcherMutex.Dispose() } catch { }
    }
}

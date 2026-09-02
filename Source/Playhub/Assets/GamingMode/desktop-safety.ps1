# Watcher di sicurezza Playhub.
# Lanciato dall'agente SOLO in Gaming Mode (come "processo personalizzato").
# Quando Steam resta chiuso, riporta la sessione corrente in Desktop Mode senza
# riavviare Windows. Le uscite per aggiornamento o manutenzione non devono
# diventare riavvii forzati del PC.

$ErrorActionPreference = 'SilentlyContinue'

Add-Type -Namespace PlayhubNative -Name Sys -MemberDefinition '[DllImport("user32.dll")] public static extern int GetSystemMetrics(int nIndex);'

# SM_SHUTTINGDOWN (0x2000): la sessione sta terminando (arresto/riavvio in corso).
function Test-SystemShuttingDown {
    try {
        return ([PlayhubNative.Sys]::GetSystemMetrics(0x2000) -ne 0)
    }
    catch {
        return $false
    }
}

$logPath = Join-Path $env:APPDATA 'GamingMode\playhub-safety.log'

function Write-Log([string]$message) {
    try {
        "$((Get-Date).ToString('yyyy-MM-dd HH:mm:ss')) $message" | Add-Content -LiteralPath $logPath -Encoding UTF8
    }
    catch {
    }
}

$watcherMutex = $null
$ownsWatcherMutex = $false
try {
    $watcherMutex = New-Object System.Threading.Mutex($false, 'Global\PlayhubGamingModeDesktopSafety')
    try {
        $ownsWatcherMutex = $watcherMutex.WaitOne(0, $false)
    }
    catch [System.Threading.AbandonedMutexException] {
        $ownsWatcherMutex = $true
    }

    if (-not $ownsWatcherMutex) {
        Write-Log 'Un watcher e'' gia'' attivo: questa istanza termina.'
        return
    }

    Write-Log '--- Watcher avviato ---'

# 1) Attendi che Steam parta (fino a ~5 minuti).
$started = $false
for ($i = 0; $i -lt 300; $i++) {
    if (Get-Process steam -ErrorAction SilentlyContinue) {
        $started = $true
        break
    }
    Start-Sleep -Seconds 1
}
if (-not $started) {
    Write-Log 'Steam non e'' partito entro il timeout: esco senza fare nulla.'
    return
}
Write-Log 'Steam rilevato.'

# 2) Steam puo' sostituire il proprio processo durante l'avvio di Big Picture,
#    gli aggiornamenti o un riavvio Decky. Riaggancia sempre la nuova istanza e
#    considera una vera uscita soltanto un'assenza continua di 15 secondi.
while ($true) {
    try {
        Get-Process steam -ErrorAction Stop | Wait-Process -ErrorAction SilentlyContinue
    }
    catch {
        while (Get-Process steam -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 400 }
    }
    Write-Log 'Steam chiuso; avvio finestra di stabilizzazione.'

    $steamRestarted = $false
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Milliseconds 500
        if (Test-SystemShuttingDown) {
            Write-Log 'Arresto/riavvio di sistema in corso: nessuna modifica, vale la modalita'' predefinita.'
            Write-Log '--- Watcher terminato ---'
            return
        }
        if (Get-Process steam -ErrorAction SilentlyContinue) {
            $steamRestarted = $true
            break
        }
    }

    if ($steamRestarted) {
        Write-Log 'Steam e'' ripartito: watcher riagganciato alla nuova istanza.'
        continue
    }
    break
}

# 4) Ripristina il Desktop nella sessione corrente. L'endpoint applica il modo
#    senza sign-out e senza riavvio, lasciando invariata la modalita' predefinita.
try {
    $response = Invoke-WebRequest -Uri 'http://127.0.0.1:47991/mode/desktop' -Method POST -UseBasicParsing -TimeoutSec 10
    if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 300) {
        Write-Log 'Desktop Mode ripristinata senza riavviare Windows.'
    }
    else {
        Write-Log "Ripristino Desktop non riuscito: HTTP $($response.StatusCode)."
    }
}
catch {
    Write-Log "Errore nel ripristino Desktop via agente: $_"
}

    Write-Log '--- Watcher terminato ---'
}
finally {
    if ($ownsWatcherMutex -and $null -ne $watcherMutex) {
        try { $watcherMutex.ReleaseMutex() } catch {}
    }
    if ($null -ne $watcherMutex) {
        try { $watcherMutex.Dispose() } catch {}
    }
}

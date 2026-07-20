# Watcher di sicurezza Playhub.
# Lanciato dall'agente SOLO in Gaming Mode (come "processo personalizzato").
# Quando l'utente CHIUDE Steam, riavvia il PC facendolo ripartire in Desktop
# Mode: avvio pulito, senza il "sign-out morbido" che riapre i processi bloccati.
#
# IMPORTANTE: nextBootMode=Desktop viene scritto SOLO nel momento in cui Steam
# si chiude E il sistema NON sta gia' spegnendosi/riavviandosi. Scriverlo in
# anticipo (come nelle vecchie versioni) faceva si' che un riavvio/arresto dal
# menu di Steam Big Picture riportasse sempre in Desktop Mode, ignorando la
# modalita' predefinita scelta dall'utente.

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

$configPath = Join-Path $env:APPDATA 'GamingMode\config.json'
$logPath = Join-Path $env:APPDATA 'GamingMode\playhub-safety.log'

function Write-Log([string]$message) {
    try {
        "$((Get-Date).ToString('yyyy-MM-dd HH:mm:ss')) $message" | Add-Content -LiteralPath $logPath -Encoding UTF8
    }
    catch {
    }
}

function Set-NextBootDesktop {
    try {
        if (-not (Test-Path -LiteralPath $configPath)) {
            Write-Log "config.json non trovato: $configPath"
            return $false
        }

        $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
        if ($config.PSObject.Properties.Name -contains 'nextBootMode') {
            $config.nextBootMode = 'Desktop'
        }
        else {
            $config | Add-Member -NotePropertyName 'nextBootMode' -NotePropertyValue 'Desktop' -Force
        }
        $json = $config | ConvertTo-Json -Depth 40
        [System.IO.File]::WriteAllText($configPath, $json, (New-Object System.Text.UTF8Encoding($false)))
        Write-Log 'nextBootMode impostato su Desktop.'
        return $true
    }
    catch {
        Write-Log "Errore nella scrittura della config: $_"
        return $false
    }
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

# 2) Attendi che il processo principale di Steam termini. Wait-Process reagisce
#    nell'istante esatto in cui Steam si chiude (piu'' rapido di un polling).
try {
    Get-Process steam -ErrorAction Stop | Wait-Process -ErrorAction SilentlyContinue
}
catch {
    # In caso di problemi con Wait-Process, ripiego su un polling veloce.
    while (Get-Process steam -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 400 }
}
Write-Log 'Steam chiuso.'

# 3) Se il sistema si sta gia' spegnendo o riavviando (es. riavvio/arresto dal
#    menu di Steam Big Picture), NON toccare nulla: al prossimo avvio deve
#    valere la modalita' predefinita scelta dall'utente.
$shuttingDown = Test-SystemShuttingDown
if (-not $shuttingDown) {
    # Piccola attesa: Steam potrebbe chiudersi un attimo PRIMA che il comando di
    # arresto/riavvio del sistema venga emesso. Ricontrolla per qualche secondo.
    for ($i = 0; $i -lt 12; $i++) {
        Start-Sleep -Milliseconds 400
        if (Test-SystemShuttingDown) {
            $shuttingDown = $true
            break
        }
    }
}
if ($shuttingDown) {
    Write-Log 'Arresto/riavvio di sistema in corso: nessuna modifica, vale la modalita'' predefinita.'
    Write-Log '--- Watcher terminato ---'
    return
}

# Steam potrebbe essere stato riavviato (es. aggiornamento o riavvio da Decky):
# se e' di nuovo attivo, non fare nulla e resta in ascolto sulla nuova istanza.
if (Get-Process steam -ErrorAction SilentlyContinue) {
    Write-Log 'Steam e'' ripartito: nessun ritorno al desktop.'
    try {
        Get-Process steam -ErrorAction Stop | Wait-Process -ErrorAction SilentlyContinue
    }
    catch {
        while (Get-Process steam -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 400 }
    }
    $shuttingDown = Test-SystemShuttingDown
    if (-not $shuttingDown) {
        for ($i = 0; $i -lt 12; $i++) {
            Start-Sleep -Milliseconds 400
            if (Test-SystemShuttingDown) {
                $shuttingDown = $true
                break
            }
        }
    }
    if ($shuttingDown) {
        Write-Log 'Arresto/riavvio di sistema in corso: nessuna modifica.'
        Write-Log '--- Watcher terminato ---'
        return
    }
}

# 4) Solo ora prepara il prossimo avvio in Desktop e riavvia.
$prepared = Set-NextBootDesktop
if ($prepared) {
    Write-Log 'Riavvio del PC (shutdown /r /f /t 0).'
    Start-Process 'shutdown.exe' -ArgumentList '/r', '/f', '/t', '0'
}
else {
    # Ripiego: torna comunque al desktop via agente per non restare bloccati.
    Write-Log 'Config non scrivibile: ripiego sul ritorno al desktop via agente.'
    try {
        Invoke-WebRequest -Uri 'http://127.0.0.1:47991/mode/desktop/switch' -Method POST -UseBasicParsing -TimeoutSec 6 | Out-Null
    }
    catch {
        Write-Log "Errore nel ripiego via agente: $_"
    }
}

Write-Log '--- Watcher terminato ---'

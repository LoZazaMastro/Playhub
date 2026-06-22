# Watcher di sicurezza Playhub.
# Lanciato dall'agente SOLO in Gaming Mode (come "processo personalizzato").
# Quando Steam si chiude, RIAVVIA il PC facendolo ripartire in Desktop Mode:
# avvio pulito, senza il "sign-out morbido" che riapre i processi bloccati.
#
# Nota: l'agente ha un proprio watchdog che alla chiusura di Steam riporta al
# desktop con un sign-out. Per vincere la "corsa" e ottenere un vero riavvio
# prepariamo PRIMA nextBootMode=Desktop (mentre Steam è ancora aperto) così alla
# chiusura non resta che riavviare all'istante. Se per qualunque motivo il
# sign-out dell'agente arriva prima, al rientro si parte comunque in Desktop.

$ErrorActionPreference = 'SilentlyContinue'

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

# 2) Prepara SUBITO il prossimo avvio in Desktop (una sola volta), senza cambiare
#    la modalita'' predefinita. Cosi'' alla chiusura di Steam resta solo il riavvio.
$prepared = Set-NextBootDesktop

# 3) Attendi che il processo principale di Steam termini. Wait-Process reagisce
#    nell'istante esatto in cui Steam si chiude (piu'' rapido di un polling).
try {
    Get-Process steam -ErrorAction Stop | Wait-Process -ErrorAction SilentlyContinue
}
catch {
    # In caso di problemi con Wait-Process, ripiego su un polling veloce.
    while (Get-Process steam -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 400 }
}
Write-Log 'Steam chiuso.'

if (-not $prepared) {
    $prepared = Set-NextBootDesktop
}

# 4) Riavvia all'istante. Al rientro l'agente parte in Desktop Mode.
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

# Watcher Xbox Game Bar di Playhub.
# Lanciato dall'agente SOLO in Gaming Mode (processo personalizzato), quando il
# toggle "Xbox Game Bar" è attivo.
#
# Scopo: il QAM di Steam non si disegna sopra i giochi Xbox/MS Store (app UWP).
# Per quei giochi si usa la Xbox Game Bar. Ma "Apri Game Bar dal controller" va
# tenuta SPENTA di norma (dà fastidio alla navigazione in Big Picture), quindi la
# accendiamo SOLO mentre gira un gioco Xbox e la rispegniamo alla chiusura.
#
# Rilevamento: i giochi Xbox/UWP importati partono tramite UWPHook.exe, che resta
# vivo per tutta la durata del gioco. Quindi "UWPHook in esecuzione" = gioco
# Xbox attivo. Epic/GOG/exe NON usano UWPHook, quindi non vengono toccati.

$ErrorActionPreference = 'SilentlyContinue'

$gameBarKey = 'HKCU:\Software\Microsoft\GameBar'
$gameBarValue = 'UseNexusForGameBarEnabled'

$logMain = Join-Path $env:APPDATA 'GamingMode\playhub-gamebar.log'
# Copia del log su F:\Playhub se presente (macchina di sviluppo), così è leggibile
# esternamente per il debug. Sugli altri PC resta solo il log in %APPDATA%.
$logMirror = $null
if (Test-Path -LiteralPath 'F:\Playhub') { $logMirror = 'F:\Playhub\playhub-gamebar.log' }

# Cap del log: oltre ~200 KB si riparte da capo.
try {
    if ((Test-Path -LiteralPath $logMain) -and ((Get-Item -LiteralPath $logMain).Length -gt 200KB)) {
        Remove-Item -LiteralPath $logMain -Force
    }
}
catch {
}

function Write-Log([string]$message) {
    $line = "$((Get-Date).ToString('yyyy-MM-dd HH:mm:ss')) $message"
    try { Add-Content -LiteralPath $logMain -Value $line -Encoding UTF8 } catch {}
    if ($logMirror) { try { Add-Content -LiteralPath $logMirror -Value $line -Encoding UTF8 } catch {} }
}

function Get-GameBarState {
    try {
        return [int](Get-ItemProperty -LiteralPath $gameBarKey -Name $gameBarValue -ErrorAction Stop).$gameBarValue
    }
    catch {
        return 0
    }
}

function Set-GameBar([int]$value) {
    try {
        if (-not (Test-Path -LiteralPath $gameBarKey)) {
            New-Item -Path $gameBarKey -Force | Out-Null
        }
        Set-ItemProperty -LiteralPath $gameBarKey -Name $gameBarValue -Value $value -Type DWord
        return $true
    }
    catch {
        Write-Log "ERRORE scrittura registro ($value): $_"
        return $false
    }
}

Write-Log '--- Watcher Xbox Game Bar avviato ---'

# Diagnostica: la Xbox Game Bar è installata? (se manca, la feature è inutile)
try {
    $pkg = Get-AppxPackage -Name 'Microsoft.XboxGamingOverlay' -ErrorAction SilentlyContinue
    if ($pkg) { Write-Log "Xbox Game Bar installata (versione $($pkg.Version))." }
    else { Write-Log 'ATTENZIONE: pacchetto Xbox Game Bar NON trovato.' }
}
catch {
}
Write-Log "Stato iniziale 'apri Game Bar dal controller': $(Get-GameBarState)"

# All'avvio: spegni (nessun gioco Xbox ancora in esecuzione).
Set-GameBar 0 | Out-Null
$currentlyOn = $false
Write-Log "Game Bar controller: OFF (avvio, nessun gioco Xbox)."

# Loop di sorveglianza: UWPHook vivo => gioco Xbox in corso.
while ($true) {
    $xboxRunning = [bool](Get-Process -Name 'UWPHook' -ErrorAction SilentlyContinue)

    if ($xboxRunning -and -not $currentlyOn) {
        if (Set-GameBar 1) {
            $currentlyOn = $true
            Write-Log 'Gioco Xbox rilevato (UWPHook) -> Game Bar controller: ON.'
        }
    }
    elseif (-not $xboxRunning -and $currentlyOn) {
        if (Set-GameBar 0) {
            $currentlyOn = $false
            Write-Log 'Gioco Xbox chiuso -> Game Bar controller: OFF.'
        }
    }

    Start-Sleep -Seconds 1
}

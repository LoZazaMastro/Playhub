param(
  [string]$SourceDir = "",
  [switch]$NoStartupTask
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($SourceDir)) {
  if (Test-Path (Join-Path $PSScriptRoot "GamingMode.exe")) {
    $SourceDir = $PSScriptRoot
  }
  elseif (Test-Path (Join-Path $RepoRoot "artifacts\release\gaming-mode\GamingMode.exe")) {
    $SourceDir = Join-Path $RepoRoot "artifacts\release\gaming-mode"
  }
  else {
    $SourceDir = Join-Path $RepoRoot "artifacts\app"
  }
}

$SourceDir = (Resolve-Path $SourceDir).Path
$ExeSource = Join-Path $SourceDir "GamingMode.exe"
if (-not (Test-Path $ExeSource)) {
  throw "GamingMode.exe was not found in $SourceDir. Run scripts\build.ps1 first or pass -SourceDir."
}

$InstallDir = Join-Path $env:LOCALAPPDATA "GamingMode"
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

# FERMARE L'AGENTE PRIMA DI SOVRASCRIVERLO.
#
# Chiedere l'uscita non basta: fra il momento in cui il processo termina e
# quello in cui Windows rilascia il file passa un istante, e la copia arrivava
# proprio li' in mezzo. Ne usciva un errore rosso pieno di righe che non
# significava niente per chi installa, e per giunta durante un'installazione
# andata a buon fine.
#
# Qui si aspetta di poter davvero scrivere sul file prima di provarci.
Get-Process -Name "GamingMode" -ErrorAction SilentlyContinue | ForEach-Object {
  try {
    if (-not $_.CloseMainWindow()) {
      $_.Kill()
    }
    elseif (-not $_.WaitForExit(3000)) {
      $_.Kill()
    }
    $_.WaitForExit(5000) | Out-Null
  }
  catch {
  }
}

$Target = Join-Path $InstallDir "GamingMode.exe"
for ($attempt = 0; $attempt -lt 40; $attempt++) {
  if (-not (Test-Path $Target)) { break }
  try {
    # Se si apre in scrittura, nessuno lo sta piu' tenendo.
    $handle = [System.IO.File]::Open($Target, 'Open', 'Write', 'None')
    $handle.Close()
    break
  }
  catch {
    Start-Sleep -Milliseconds 250
  }
}

# Anche cosi' puo' capitare che un antivirus stia ancora leggendo i file
# appena scritti: qualche tentativo, in silenzio, e solo alla fine si parla.
$copied = $false
for ($attempt = 0; $attempt -lt 5; $attempt++) {
  try {
    Copy-Item -Path (Join-Path $SourceDir "*") -Destination $InstallDir -Recurse -Force -ErrorAction Stop
    $copied = $true
    break
  }
  catch {
    Start-Sleep -Milliseconds 500
  }
}

if (-not $copied) {
  Write-Error "Non sono riuscito ad aggiornare Gaming Mode: un altro programma sta tenendo aperti i suoi file. Chiudi Playhub e riprova."
  exit 1
}

# Remove the "mark of the web" so Windows doesn't show the "unknown publisher"
# security prompt every time the agent is launched.
Get-ChildItem -Path $InstallDir -Recurse -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue

$Exe = Join-Path $InstallDir "GamingMode.exe"
$Icon = Join-Path $InstallDir "assets\logo.ico"

# Playhub integrates Gaming Mode. The standalone app must NOT be exposed:
# remove any Desktop / Start Menu shortcuts left by older installs.
$DesktopShortcut = Join-Path ([Environment]::GetFolderPath("Desktop")) "Gaming Mode.lnk"
$StartMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Gaming Mode"
Remove-Item -Path $DesktopShortcut -Force -ErrorAction SilentlyContinue
Remove-Item -Path $StartMenuDir -Recurse -Force -ErrorAction SilentlyContinue

$Shell = New-Object -ComObject WScript.Shell

if (-not $NoStartupTask) {
  $StartupShortcut = Join-Path ([Environment]::GetFolderPath("Startup")) "Gaming Mode Agent.lnk"
  $Startup = $Shell.CreateShortcut($StartupShortcut)
  $Startup.TargetPath = $Exe
  $Startup.Arguments = "agent --boot"
  $Startup.WorkingDirectory = $InstallDir
  $Startup.Description = "Start Gaming Mode Agent"
  $Startup.WindowStyle = 7
  if (Test-Path $Icon) {
    $Startup.IconLocation = $Icon
  }
  $Startup.Save()
}

Start-Process -FilePath $Exe -ArgumentList "agent" -WindowStyle Hidden -WorkingDirectory $InstallDir
$AgentReady = $false
for ($i = 0; $i -lt 20; $i++) {
  Start-Sleep -Milliseconds 250
  try {
    $Health = Invoke-WebRequest -Uri "http://127.0.0.1:47991/health" -UseBasicParsing -TimeoutSec 1
    if ($Health.StatusCode -eq 200) {
      $AgentReady = $true
      break
    }
  }
  catch {
  }
}

Write-Host "Installed Gaming Mode to $InstallDir"
if ($AgentReady) {
  Write-Host "Local agent is running on http://127.0.0.1:47991"
}
else {
  Write-Host "WARNING: Local agent did not answer on http://127.0.0.1:47991"
}

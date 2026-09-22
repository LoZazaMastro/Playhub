$ErrorActionPreference = "Stop"

# FERMARE L'AGENTE E ASPETTARE DAVVERO CHE ESCA.
#
# Prima si chiamava Stop-Process -Force e subito dopo si cancellava la cartella.
# Fra i due istanti Windows non ha ancora rilasciato GamingMode.exe (oltre 200 MB),
# quindi Remove-Item falliva con "file in uso", $ErrorActionPreference = Stop
# trasformava tutto in un errore terminante e Playhub mostrava soltanto
# "Non riesco a rimuovere Gaming Mode. Riprova." Nessuna traccia della causa.
Get-Process -Name "GamingMode" -ErrorAction SilentlyContinue | ForEach-Object {
  try {
    $_.Kill()
    $_.WaitForExit(10000) | Out-Null
  }
  catch {
  }
}

$DesktopShortcut = Join-Path ([Environment]::GetFolderPath("Desktop")) "Gaming Mode.lnk"
$StartupShortcut = Join-Path ([Environment]::GetFolderPath("Startup")) "Gaming Mode Agent.lnk"
$StartMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Gaming Mode"
$InstallDir = Join-Path $env:LOCALAPPDATA "GamingMode"

$Failures = @()
foreach ($Path in @($DesktopShortcut, $StartupShortcut, $StartMenuDir, $InstallDir)) {
  if (-not (Test-Path $Path)) { continue }
  $removed = $false
  $lastError = $null
  for ($attempt = 0; $attempt -lt 20; $attempt++) {
    try {
      Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
      $removed = $true
      break
    }
    catch {
      $lastError = $_
      Start-Sleep -Milliseconds 250
    }
  }
  if (-not $removed) {
    # Il messaggio dice QUALE percorso e QUALE errore: e' l'unica informazione
    # che permette di capire chi sta tenendo aperto il file.
    $Failures += ("{0}: {1}" -f $Path, $lastError.Exception.Message)
  }
}

if ($Failures.Count -gt 0) {
  Write-Error ("Rimozione non riuscita. " + ($Failures -join " | "))
  exit 1
}

Write-Host "Gaming Mode was removed."

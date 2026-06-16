$ErrorActionPreference = "Stop"

Get-Process -Name "GamingMode" -ErrorAction SilentlyContinue | Stop-Process -Force

$DesktopShortcut = Join-Path ([Environment]::GetFolderPath("Desktop")) "Gaming Mode.lnk"
$StartupShortcut = Join-Path ([Environment]::GetFolderPath("Startup")) "Gaming Mode Agent.lnk"
$StartMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Gaming Mode"
$InstallDir = Join-Path $env:LOCALAPPDATA "GamingMode"

foreach ($Path in @($DesktopShortcut, $StartupShortcut, $StartMenuDir, $InstallDir)) {
  if (Test-Path $Path) {
    Remove-Item -LiteralPath $Path -Recurse -Force
  }
}

Write-Host "Gaming Mode was removed."

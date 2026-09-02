@echo off
chcp 65001 >nul
set "EXE="
for /f "usebackq delims=" %%F in (`powershell -NoProfile -Command "Get-ChildItem -LiteralPath '%~dp0bin' -Filter 'Playhub.exe' -File -Recurse -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1 -ExpandProperty FullName"`) do set "EXE=%%F"
if not defined EXE (
  echo Playhub.exe non trovato. Compila prima il progetto in Debug o Release.
  exit /b 1
)
echo Avvio Playhub e attendo qualche secondo...
start "" "%EXE%"
timeout /t 6 >nul
echo.
echo ===== Ultimi errori applicazione (.NET) dal registro eventi =====
powershell -NoProfile -Command "Get-WinEvent -FilterHashtable @{LogName='Application'; Level=2; StartTime=(Get-Date).AddMinutes(-3)} -ErrorAction SilentlyContinue | Select-Object -First 5 | ForEach-Object { '----'; $_.TimeCreated; $_.ProviderName; $_.Message }" > "%~dp0crash_log.txt" 2>&1
type "%~dp0crash_log.txt"
echo.
echo Log salvato in crash_log.txt
pause

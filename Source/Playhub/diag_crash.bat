@echo off
chcp 65001 >nul
set "EXE=F:\Playhub\Plugin\Playhub App\Source\Playhub\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\Playhub.exe"
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

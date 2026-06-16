@echo off
setlocal
cd /d "%~dp0"

rem ====================================================================
rem  Build VELOCE: ricompila SOLO l'installer e riusa il payload.zip
rem  gia' esistente (NON ripubblica l'app). Usa questo mentre iteri
rem  sulla grafica dell'installer. Per un build completo: build-installer.bat
rem ====================================================================

set LOG=build-installer-log.txt
set PAYLOAD=Payload\payload.zip
set STUB_DIR=Output\stub
set FINAL=Output\Playhub-Setup.exe

if not exist "%PAYLOAD%" (
  echo Manca %PAYLOAD%. Esegui prima build-installer.bat almeno una volta.
  pause
  exit /b 1
)

echo ===== PLAYHUB INSTALLER FAST BUILD %DATE% %TIME% ===== > "%LOG%" 2>&1
echo Log completo in: %~dp0%LOG%
echo.

echo [1/3] Compilo lo stub dell'installer...
echo. >> "%LOG%" & echo ===== [1/3] STUB ===== >> "%LOG%"
if exist "Output" rmdir /s /q "Output"
dotnet publish "PlayhubSetup.csproj" -c Release -r win-x64 -o "%STUB_DIR%" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail

echo [2/3] Appendo il payload in coda all'exe...
echo. >> "%LOG%" & echo ===== [2/3] APPEND ===== >> "%LOG%"
powershell -NoProfile -Command "$len=(Get-Item '%PAYLOAD%').Length; $b=[System.BitConverter]::GetBytes([int64]$len); $m=[System.Text.Encoding]::ASCII.GetBytes('PLHB'); [System.IO.File]::WriteAllBytes('Output\footer.bin', $b + $m)" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
copy /b "%STUB_DIR%\Playhub-Setup.exe"+"%PAYLOAD%"+"Output\footer.bin" "%FINAL%" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail

echo [3/3] Pulizia...
del /q "Output\footer.bin" 2>nul
rmdir /s /q "%STUB_DIR%" 2>nul

echo ===== FATTO ===== >> "%LOG%"
echo.
echo ============================================================
echo  FATTO. Installer pronto:
echo    %~dp0%FINAL%
echo ============================================================
echo.
echo Premi un tasto per chiudere...
pause >nul
exit /b 0

:fail
echo. >> "%LOG%" & echo ===== BUILD FALLITA (exitcode=%errorlevel%) ===== >> "%LOG%"
echo.
echo ************************************************************
echo  BUILD FALLITA. Dettagli nel file:
echo    %~dp0%LOG%
echo  Dillo a Claude: legge il log da solo.
echo ************************************************************
echo.
echo Premi un tasto per chiudere...
pause >nul
exit /b 1

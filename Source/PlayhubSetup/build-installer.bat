@echo off
setlocal
cd /d "%~dp0"

set LOG=build-installer-log.txt
set APP_PROJ=..\Playhub\Playhub.csproj
set APP_OUT=..\Playhub\dist_publish
set PAYLOAD=Payload\payload.zip
set STUB_DIR=Output\stub
set FINAL=Output\Playhub-Setup.exe

echo ===== PLAYHUB INSTALLER BUILD %DATE% %TIME% ===== > "%LOG%" 2>&1
echo Log completo in: %~dp0%LOG%
echo.

echo [1/5] Pubblico l'app (self-contained x64)... puo' richiedere qualche minuto.
echo. >> "%LOG%" & echo ===== [1/5] PUBLISH APP ===== >> "%LOG%"
dotnet publish "%APP_PROJ%" -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:WindowsAppSDKSelfContained=true -o "%APP_OUT%" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail

echo [2/5] Creo il payload.zip...
echo. >> "%LOG%" & echo ===== [2/5] PAYLOAD ===== >> "%LOG%"
if exist "%PAYLOAD%" del /q "%PAYLOAD%"
if not exist "Payload" mkdir "Payload"
powershell -NoProfile -Command "Compress-Archive -Path '%APP_OUT%\*' -DestinationPath '%PAYLOAD%' -Force" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail

echo [3/5] Compilo lo stub dell'installer...
echo. >> "%LOG%" & echo ===== [3/5] STUB ===== >> "%LOG%"
if exist "Output" rmdir /s /q "Output"
dotnet publish "PlayhubSetup.csproj" -c Release -r win-x64 -o "%STUB_DIR%" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail

echo [4/5] Appendo il payload in coda all'exe...
echo. >> "%LOG%" & echo ===== [4/5] APPEND ===== >> "%LOG%"
powershell -NoProfile -Command "$len=(Get-Item '%PAYLOAD%').Length; $b=[System.BitConverter]::GetBytes([int64]$len); $m=[System.Text.Encoding]::ASCII.GetBytes('PLHB'); [System.IO.File]::WriteAllBytes('Output\footer.bin', $b + $m)" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
copy /b "%STUB_DIR%\Playhub-Setup.exe"+"%PAYLOAD%"+"Output\footer.bin" "%FINAL%" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail

echo [5/5] Pulizia...
echo. >> "%LOG%" & echo ===== [5/5] CLEANUP ===== >> "%LOG%"
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
echo  BUILD FALLITA. I dettagli sono nel file:
echo    %~dp0%LOG%
echo  Dillo a Claude: legge il log da solo.
echo ************************************************************
echo.
echo Premi un tasto per chiudere...
pause >nul
exit /b 1

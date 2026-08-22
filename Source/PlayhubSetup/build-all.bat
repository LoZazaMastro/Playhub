@echo off
setlocal
cd /d "%~dp0"

rem ============================================================
rem  BUILD COMPLETA DI PLAYHUB - UN SOLO COMANDO
rem
rem  Esegue in ordine tutto cio' che serve per produrre l'installer:
rem    [1] agente Gaming Mode  -> aggiorna il pacchetto bundlato
rem    [2] plugin Decky        -> compila src\*.tsx e aggiorna gli Assets
rem    [3] app Playhub         -> publish self-contained x64
rem    [4] payload.zip         -> archivio dell'app
rem    [5] stub installer      -> PlayhubSetup
rem    [6] installer finale    -> Output\Playhub-Setup.exe
rem
rem  Uso:
rem    build-all.bat            build completa dell'installer
rem    build-all.bat debug      come sopra, ma prima compila anche la
rem                             configurazione Debug dell'app (test locale)
rem ============================================================

set LOG=build-all-log.txt
set APP_PROJ=..\Playhub\Playhub.csproj
set APP_OUT=..\Playhub\dist_publish
set AGENT_BAT=..\GamingModeAgent\build-agent.bat
set PLUGIN_BAT=..\GamingModeDeckyPlugin\build-plugin.bat
set PAYLOAD=Payload\payload.zip
set STUB_DIR=Output\stub
set FINAL=Output\Playhub-Setup.exe

set WITH_DEBUG=0
if /i "%~1"=="debug" set WITH_DEBUG=1
if /i "%~1"=="--with-debug" set WITH_DEBUG=1

echo ===== PLAYHUB BUILD COMPLETA %DATE% %TIME% ===== > "%LOG%" 2>&1
echo.
echo ============================================================
echo  BUILD COMPLETA DI PLAYHUB
echo  Log completo in: %~dp0%LOG%
echo ============================================================
echo.

echo [1/6] Compilo l'agente Gaming Mode e aggiorno il pacchetto bundlato...
echo. >> "%LOG%" & echo ===== [1/6] GAMING MODE AGENT ===== >> "%LOG%"
call "%AGENT_BAT%" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail

echo [2/6] Compilo il plugin Decky Gaming Mode...
echo. >> "%LOG%" & echo ===== [2/6] PLUGIN DECKY ===== >> "%LOG%"
call "%PLUGIN_BAT%"
if errorlevel 1 (
    echo. >> "%LOG%" & echo ===== PLUGIN DECKY FALLITO ===== >> "%LOG%"
    echo  ^(vedi anche: %~dp0..\GamingModeDeckyPlugin\build-plugin-log.txt^)
    goto :fail
)

if "%WITH_DEBUG%"=="1" (
    echo [+]   Compilo anche la configurazione Debug ^(test locale^)...
    echo. >> "%LOG%" & echo ===== [+] BUILD DEBUG ===== >> "%LOG%"
    dotnet build "%APP_PROJ%" -c Debug -p:Platform=x64 >> "%LOG%" 2>&1
    if errorlevel 1 goto :fail
)

echo [3/6] Pubblico l'app ^(self-contained x64^)... puo' richiedere qualche minuto.
echo. >> "%LOG%" & echo ===== [3/6] PUBLISH APP ===== >> "%LOG%"
dotnet publish "%APP_PROJ%" -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:WindowsAppSDKSelfContained=true -o "%APP_OUT%" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail

echo [4/6] Creo il payload.zip...
echo. >> "%LOG%" & echo ===== [4/6] PAYLOAD ===== >> "%LOG%"
if exist "%PAYLOAD%" del /q "%PAYLOAD%"
if not exist "Payload" mkdir "Payload"
powershell -NoProfile -Command "Compress-Archive -Path '%APP_OUT%\*' -DestinationPath '%PAYLOAD%' -Force" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail

echo [5/6] Compilo lo stub dell'installer...
echo. >> "%LOG%" & echo ===== [5/6] STUB ===== >> "%LOG%"
if exist "Output" rmdir /s /q "Output"
dotnet publish "PlayhubSetup.csproj" -c Release -r win-x64 -o "%STUB_DIR%" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail

echo [6/6] Appendo il payload e finalizzo l'installer...
echo. >> "%LOG%" & echo ===== [6/6] APPEND + CLEANUP ===== >> "%LOG%"
powershell -NoProfile -Command "$len=(Get-Item '%PAYLOAD%').Length; $b=[System.BitConverter]::GetBytes([int64]$len); $m=[System.Text.Encoding]::ASCII.GetBytes('PLHB'); [System.IO.File]::WriteAllBytes('Output\footer.bin', $b + $m)" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
copy /b "%STUB_DIR%\Playhub-Setup.exe"+"%PAYLOAD%"+"Output\footer.bin" "%FINAL%" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
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
echo  ^(se ha fallito l'agente, vedi anche:
echo    %~dp0..\GamingModeAgent\build_agent_log.txt^)
echo ************************************************************
echo.
echo Premi un tasto per chiudere...
pause >nul
exit /b 1

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
rem    build-all.bat --with-emulation  include la sezione futura Emulazione
rem ============================================================

set LOG=build-all-log.txt
set APP_PROJ=..\Playhub\Playhub.csproj
set APP_OUT=..\Playhub\dist_publish
set AGENT_BAT=..\GamingModeAgent\build-agent.bat
set PLUGIN_BAT=..\GamingModeDeckyPlugin\build-plugin.bat
set PAYLOAD=Payload\payload.zip
set STUB_DIR=Output\stub
set FINAL=Output\Playhub-Setup.exe
set ENGINE_SRC=..\ChainFreeEngine
set ENGINE_OUT=%APP_OUT%\ChainFreeEngine
set PYTHON_ZIP=Runtime\python\python-3.12.7-embed-amd64.zip
set EMU_SRC=..\EmulationWorkbench
set EMU_OUT=%APP_OUT%\Emulation

rem Chain Free is suspended; release builds use Decky.
set WITH_CHAINFREE=0
set WITH_DEBUG=0
set WITH_EMULATION=false
for %%A in (%*) do (
    if /i "%%~A"=="--with-chainfree" set WITH_CHAINFREE=1
    if /i "%%~A"=="debug" set WITH_DEBUG=1
    if /i "%%~A"=="--with-debug" set WITH_DEBUG=1
    if /i "%%~A"=="--with-emulation" set WITH_EMULATION=true
)

echo ===== PLAYHUB BUILD COMPLETA %DATE% %TIME% ===== > "%LOG%" 2>&1
echo.
echo ============================================================
echo  BUILD COMPLETA DI PLAYHUB
echo  Log completo in: %~dp0%LOG%
echo ============================================================
echo.

echo Verifico migrazione foto e recupero Gaming Mode...
dotnet run --project "..\DeckyStartupChecks\DeckyStartupChecks.csproj" -c Release >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
dotnet run --project "..\Playhub.EditorialMigration.Tests\Playhub.EditorialMigration.Tests.csproj" -c Release >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
powershell -NoProfile -ExecutionPolicy Bypass -File "..\Playhub\Checks\desktop-safety.checks.ps1" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
dotnet run --project "..\GamingModeRegressionChecks\GamingModeRegressionChecks.csproj" -c Release >> "%LOG%" 2>&1
if errorlevel 1 goto :fail

echo [1/8] Compilo l'agente Gaming Mode e aggiorno il pacchetto bundlato...
echo. >> "%LOG%" & echo ===== [1/8] GAMING MODE AGENT ===== >> "%LOG%"
call "%AGENT_BAT%" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail

echo [2/8] Compilo il plugin Decky Gaming Mode...
echo. >> "%LOG%" & echo ===== [2/8] PLUGIN DECKY ===== >> "%LOG%"
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

echo [3/8] Pubblico l'app ^(self-contained x64^)... puo' richiedere qualche minuto.
echo. >> "%LOG%" & echo ===== [3/8] PUBLISH APP ===== >> "%LOG%"
if exist "%APP_OUT%" rmdir /s /q "%APP_OUT%"
dotnet publish "%APP_PROJ%" -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:WindowsAppSDKSelfContained=true -p:PlayhubEmulation=%WITH_EMULATION% -p:PlayhubUiReview=false -p:PlayhubUpdatePreview=false -o "%APP_OUT%" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
powershell -NoProfile -Command "$features=@{emulation=[bool]::Parse('%WITH_EMULATION%')}; [IO.File]::WriteAllText('%APP_OUT%\playhub-release-features.json', ($features | ConvertTo-Json -Compress))" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail

if "%WITH_EMULATION%"=="false" goto :emulation_suspended
echo [4/8] Compilo la sezione Emulazione ^(servizio locale e interfaccia^)...
echo. >> "%LOG%" & echo ===== [4/8] EMULAZIONE ===== >> "%LOG%"
dotnet run --project "%EMU_SRC%\ManagedInstallChecks\ManagedInstallChecks.csproj" -c Release >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
dotnet run --project "%EMU_SRC%\RetroArchChecks\RetroArchChecks.csproj" -c Release >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
dotnet run --project "%EMU_SRC%\Pcsx2Checks\Pcsx2Checks.csproj" -c Release >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
dotnet run --project "%EMU_SRC%\PpssppChecks\PpssppChecks.csproj" -c Release >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
dotnet run --project "%EMU_SRC%\FlycastChecks\FlycastChecks.csproj" -c Release >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
dotnet run --project "%EMU_SRC%\CemuChecks\CemuChecks.csproj" -c Release >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
dotnet run --project "%EMU_SRC%\AzaharChecks\AzaharChecks.csproj" -c Release >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
dotnet run --project "%EMU_SRC%\MelonDsChecks\MelonDsChecks.csproj" -c Release >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
dotnet run --project "%EMU_SRC%\MgbaChecks\MgbaChecks.csproj" -c Release >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
dotnet run --project "%EMU_SRC%\MesenChecks\MesenChecks.csproj" -c Release >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
dotnet run --project "%EMU_SRC%\XemuChecks\XemuChecks.csproj" -c Release >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
dotnet run --project "%EMU_SRC%\XeniaChecks\XeniaChecks.csproj" -c Release >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
dotnet run --project "%EMU_SRC%\EdenChecks\EdenChecks.csproj" -c Release >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
dotnet run --project "%EMU_SRC%\ScummVmChecks\ScummVmChecks.csproj" -c Release >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
dotnet run --project "%EMU_SRC%\ScummVmImportChecks\ScummVmImportChecks.csproj" -c Release >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
dotnet run --project "%EMU_SRC%\ShadPs4Checks\ShadPs4Checks.csproj" -c Release >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
dotnet run --project "%EMU_SRC%\KytyChecks\KytyChecks.csproj" -c Release >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
dotnet run --project "%EMU_SRC%\RyujinxChecks\RyujinxChecks.csproj" -c Release >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
dotnet run --project "%EMU_SRC%\Vita3KChecks\Vita3KChecks.csproj" -c Release >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
dotnet run --project "%EMU_SRC%\ScummVmGraphicsChecks\ScummVmGraphicsChecks.csproj" -c Release >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
dotnet run --project "%EMU_SRC%\Vita3KRuntimeChecks\Vita3KRuntimeChecks.csproj" -c Release >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
dotnet run --project "%EMU_SRC%\ScannerAdapterChecks\ScannerAdapterChecks.csproj" -c Release >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
dotnet run --project "%EMU_SRC%\ImportedCollectionChecks\ImportedCollectionChecks.csproj" -c Release >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
dotnet run --project "%EMU_SRC%\CatalogContractChecks\CatalogContractChecks.csproj" -c Release >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
dotnet run --project "..\PlatformSearchChecks\PlatformSearchChecks.csproj" -c Release >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
dotnet run --project "%EMU_SRC%\CustomLaunchChecks\CustomLaunchChecks.csproj" -c Release >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
dotnet run --project "%EMU_SRC%\SteamExportChecks\SteamExportChecks.csproj" -c Release >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
dotnet run --project "%EMU_SRC%\SteamArtworkChecks\SteamArtworkChecks.csproj" -c Release >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
dotnet run --project "%EMU_SRC%\NativeGraphicsChecks\NativeGraphicsChecks.csproj" -c Release >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
dotnet publish "%EMU_SRC%\Host\Playhub.Emulation.Workbench.csproj" -c Release -r win-x64 --self-contained true -o "%EMU_OUT%\Host" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
robocopy "%EMU_SRC%\Preview" "%EMU_OUT%\Preview" /E /NFL /NDL /NJH /NJS /NP >> "%LOG%" 2>&1
if errorlevel 8 goto :fail
robocopy "%EMU_SRC%\Runtime\7zip" "%EMU_OUT%\Runtime\7zip" /E /NFL /NDL /NJH /NJS /NP >> "%LOG%" 2>&1
if errorlevel 8 goto :fail
if not exist "%EMU_OUT%\Research" mkdir "%EMU_OUT%\Research"
copy /y "%EMU_SRC%\Research\metadata-languages.json" "%EMU_OUT%\Research\metadata-languages.json" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
copy /y "%EMU_SRC%\Research\graphics-audio-profiles.json" "%EMU_OUT%\Research\graphics-audio-profiles.json" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
if not exist "%EMU_OUT%\Host\Playhub.Emulation.Workbench.exe" goto :fail
if not exist "%EMU_OUT%\Preview\index.html" goto :fail

:emulation_suspended
if "%WITH_CHAINFREE%"=="0" goto :engine_suspended
echo [5/8] Compilo il Playhub Chain Free Engine ^(plugin senza Decky Loader^)...
echo. >> "%LOG%" & echo ===== [5/8] CHAIN FREE ENGINE ===== >> "%LOG%"
dotnet run --project "..\ChainFreeEngineChecks\ChainFreeEngineChecks.csproj" --no-launch-profile >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
rem  Il bundle del renderer si ricompila solo se Node c'e', come per il plugin:
rem  senza Node si spedisce quello gia' compilato, e se manca del tutto ci si ferma.
where node >nul 2>&1
if errorlevel 1 goto :engine_bundle_kept
node --test "%ENGINE_SRC%\Plugin\tests\react19-refresh.test.mjs" "%ENGINE_SRC%\Plugin\tests\bootstrap-and-injection.test.mjs" "%ENGINE_SRC%\Plugin\tests\steam-host-fixture.test.mjs" "%ENGINE_SRC%\RuntimeChecks\renderer-ownership.test.mjs" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
dotnet run --project "%ENGINE_SRC%\MigrationChecks\MigrationChecks.csproj" -c Release >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
call :build_renderer_bundle
if errorlevel 1 goto :fail
goto :engine_bundle_ready
:engine_bundle_kept
echo   - Node.js non e' installato: non ricompilo il bundle del motore.
echo     Uso quello gia' compilato in StandaloneHost\Plugin\dist.
echo Node.js assente: bundle del renderer non ricompilato, spedito quello esistente. >> "%LOG%"
:engine_bundle_ready
if exist "%ENGINE_SRC%\Plugin\dist\playhub-standalone.js" goto :engine_bundle_present
echo   - Manca il bundle del motore e senza Node.js non posso ricompilarlo.
echo Bundle del renderer assente: build fermata. >> "%LOG%"
goto :fail
:engine_bundle_present
dotnet publish "%ENGINE_SRC%\Host\Playhub.StandaloneHost.csproj" -c Release -r win-x64 --self-contained true -o "%ENGINE_OUT%" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
if not exist "%ENGINE_OUT%\Renderer" mkdir "%ENGINE_OUT%\Renderer"
if not exist "%ENGINE_OUT%\Backend" mkdir "%ENGINE_OUT%\Backend"
copy /y "%ENGINE_SRC%\Plugin\dist\playhub-standalone.js" "%ENGINE_OUT%\Renderer\playhub-standalone.js" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
copy /y "%ENGINE_SRC%\Plugin\vendor\DeckyUI-LICENSE" "%ENGINE_OUT%\Renderer\DeckyUI-LICENSE" >> "%LOG%" 2>&1
copy /y "%ENGINE_SRC%\Plugin\vendor\ShelvesHub-LICENSE" "%ENGINE_OUT%\Renderer\ShelvesHub-LICENSE" >> "%LOG%" 2>&1
copy /y "%ENGINE_SRC%\Backend\runner.py" "%ENGINE_OUT%\Backend\runner.py" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
rem  Python incluso: senza Decky Loader sul PC non c'e' nessun interprete per il
rem  backend del plugin. Si usa la distribuzione ufficiale "embeddable", inclusa
rem  nel repository e verificata con SHA-256 prima di essere estratta.
powershell -NoProfile -Command "$zip='%PYTHON_ZIP%'; $expected='0d57bb6cb078b74d23dbfe91f77d6780d45bed328911609f1f7ee2ba1606bf44'; if (-not (Test-Path $zip)) { throw 'Manca la distribuzione Python embeddable.' }; $actual=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash; if ($actual -ne $expected.ToUpper()) { throw 'SHA-256 della distribuzione Python non corrispondente.' }; $target='%ENGINE_OUT%\Python'; if (Test-Path $target) { Remove-Item -Recurse -Force $target }; Expand-Archive -LiteralPath $zip -DestinationPath $target -Force; Add-Content -LiteralPath (Join-Path $target 'python312._pth') -Value 'import site'" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
if not exist "%ENGINE_OUT%\Python\python.exe" goto :fail

:engine_suspended
echo [6/8] Creo il payload.zip...
rem Rimuove soltanto foto legacy identiche all'allowlist; cover e file modificati restano.
dotnet run --project "..\Playhub.EditorialMigration.Tests\Playhub.EditorialMigration.Tests.csproj" -c Release -- --prune "%APP_OUT%" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
powershell -NoProfile -ExecutionPolicy Bypass -File "Sync-PublishedPluginAssets.ps1" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
if "%WITH_EMULATION%"=="false" (
    if exist "%EMU_OUT%" goto :fail
    if exist "%APP_OUT%\Assets\Emulation" goto :fail
    if exist "%APP_OUT%\Assets\Emulators" goto :fail
)
if not exist "%APP_OUT%\Playhub.GameSession.exe" goto :fail
echo. >> "%LOG%" & echo ===== [6/8] PAYLOAD ===== >> "%LOG%"
if exist "%PAYLOAD%" del /q "%PAYLOAD%"
if not exist "Payload" mkdir "Payload"
powershell -NoProfile -Command "Compress-Archive -Path '%APP_OUT%\*' -DestinationPath '%PAYLOAD%' -Force" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail

echo [7/8] Compilo lo stub dell'installer...
echo. >> "%LOG%" & echo ===== [7/8] STUB ===== >> "%LOG%"
if exist "Output" rmdir /s /q "Output"
dotnet publish "PlayhubSetup.csproj" -c Release -r win-x64 -o "%STUB_DIR%" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail

echo [8/8] Appendo il payload e finalizzo l'installer...
echo. >> "%LOG%" & echo ===== [8/8] APPEND + CLEANUP ===== >> "%LOG%"
powershell -NoProfile -Command "$len=(Get-Item '%PAYLOAD%').Length; $b=[System.BitConverter]::GetBytes([int64]$len); $m=[System.Text.Encoding]::ASCII.GetBytes('PLHB'); [System.IO.File]::WriteAllBytes('Output\footer.bin', $b + $m)" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail
copy /b "%STUB_DIR%\Playhub Setup.exe"+"%PAYLOAD%"+"Output\footer.bin" "%FINAL%" >> "%LOG%" 2>&1
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

:build_renderer_bundle
pushd "%ENGINE_SRC%\Plugin" >nul
node build.mjs >> "%~dp0%LOG%" 2>&1
if errorlevel 1 (popd & exit /b 1)
node --test "tests/*.test.mjs" >> "%~dp0%LOG%" 2>&1
if errorlevel 1 (popd & exit /b 1)
popd >nul
exit /b 0

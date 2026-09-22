@echo off
rem  NIENTE enabledelayedexpansion: con quello attivo il punto esclamativo
rem  dentro un echo viene mangiato come se fosse una variabile, e i messaggi
rem  di avviso arrivano a meta'.
setlocal
cd /d "%~dp0"

rem ============================================================
rem  BUILD DEL PLUGIN DECKY "GAMING MODE"
rem
rem  Compila src\*.tsx in dist\index.js e copia il risultato in
rem  Source\Playhub\Assets\GamingModeDeckyPlugin\gaming-mode, che e' la
rem  cartella che l'app copia dentro homebrew\plugins.
rem
rem  PERCHE' QUESTO FILE ESISTE.
rem  Per mesi i sorgenti del plugin non venivano compilati da nessuno: la build
rem  dell'app impacchettava il dist\index.js che si trovava, vecchio di
rem  settimane. Risultato: modifiche fatte al plugin che non comparivano mai in
rem  Steam, senza un solo errore a segnalarlo. Se npm non c'e', qui sotto la
rem  build FALLISCE invece di spedire in silenzio un bundle vecchio.
rem ============================================================

set LOG=build-plugin-log.txt
set ASSETS=..\Playhub\Assets\GamingModeDeckyPlugin\gaming-mode

echo ===== BUILD PLUGIN %DATE% %TIME% ===== > "%LOG%" 2>&1

rem ---------- npm disponibile? ----------
where npm >nul 2>&1
if errorlevel 1 goto :no_npm

if exist "node_modules\.bin\tsc.cmd" if exist "node_modules\.bin\rollup.cmd" goto :deps_ready

echo   - Installo le dipendenze del plugin...
call npm install --legacy-peer-deps --no-audit --no-fund >> "%LOG%" 2>&1
if errorlevel 1 (
    echo   ! npm install non riuscito. Dettagli in "%~dp0%LOG%"
    exit /b 1
)

:deps_ready
echo   - Dipendenze del plugin disponibili.

echo   - Controllo i tipi...
call "%~dp0node_modules\.bin\tsc.cmd" --noEmit >> "%LOG%" 2>&1
if errorlevel 1 (
    echo   ! Il plugin non compila: errori di tipo. Dettagli in "%~dp0%LOG%"
    exit /b 1
)

echo   - Compilo il bundle...
call "%~dp0node_modules\.bin\rollup.cmd" -c >> "%LOG%" 2>&1
if errorlevel 1 (
    echo   ! rollup non riuscito. Dettagli in "%~dp0%LOG%"
    exit /b 1
)

if not exist "dist\index.js" (
    echo   ! rollup non ha prodotto dist\index.js. Dettagli in "%~dp0%LOG%"
    exit /b 1
)

rem ---------- copia negli Assets ----------
rem  Il logo ufficiale viene importato dal bundle e quindi Rollup lo emette
rem  sotto dist\assets. Copiamo anche quella cartella, oltre all'asset usato
rem  dal manifest, cosi' sorgente e runtime installato restano identici.
if not exist "%ASSETS%\dist" mkdir "%ASSETS%\dist"
copy /y "dist\index.js" "%ASSETS%\dist\index.js" >> "%LOG%" 2>&1
del /q "%ASSETS%\dist\index.js.map" 2>nul
if exist "dist\assets" xcopy /e /i /y "dist\assets" "%ASSETS%\dist\assets" >> "%LOG%" 2>&1
if not exist "%ASSETS%\assets" mkdir "%ASSETS%\assets"
copy /y "assets\playhub-logo-decky.png" "%ASSETS%\assets\playhub-logo-decky.png" >> "%LOG%" 2>&1
copy /y "plugin.json"   "%ASSETS%\plugin.json"   >> "%LOG%" 2>&1
copy /y "package.json"  "%ASSETS%\package.json"  >> "%LOG%" 2>&1
copy /y "LICENSE-Shortcuts" "%ASSETS%\LICENSE-Shortcuts" >> "%LOG%" 2>&1
copy /y "main.py" "%ASSETS%\main.py" >> "%LOG%" 2>&1
if not exist "%ASSETS%\screensaver" mkdir "%ASSETS%\screensaver"
copy /y "screensaver\index.html" "%ASSETS%\screensaver\index.html" >> "%LOG%" 2>&1
copy /y "screensaver\main.js" "%ASSETS%\screensaver\main.js" >> "%LOG%" 2>&1
if errorlevel 1 exit /b 1
copy /y "THIRD-PARTY-NOTICES.md" "%ASSETS%\THIRD-PARTY-NOTICES.md" >> "%LOG%" 2>&1
rem ---------- helper payload allowlist ----------
rem Keep runtime helpers, AMD redistribution sources and all license notices.
rem Never copy arbitrary binaries, drivers or Python bytecode from helper trees.
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference='Stop';" ^
  "$root=[IO.Path]::GetFullPath('%ASSETS%'); $prefix=$root.TrimEnd('\')+'\';" ^
  "if (Test-Path -LiteralPath $root) { $existing=@(Get-Item -LiteralPath $root)+@(Get-ChildItem -LiteralPath $root -Recurse -Force); if ($existing | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Linked payload path refused' } };" ^
  "$required=@('quick_settings\history_images.json','quick_settings\history_editorial.json','quick_settings\history_days.json','quick_settings\history_imported.json','quick_settings\main.py','quick_settings\__init__.py','quick_settings\bin\QuickSettingsAgent.exe','quick_settings\helper\setup_perf.ps1','quick_settings\helper\apply_perf.ps1','quick_settings\amd\adlx_helper.exe','quick_settings\amd\ADLXCSharpBind.dll','quick_settings\amd\build_amd.bat','quick_settings\LICENSE','quick_settings\NOTICE','quick_settings\LEGAL.md','quick_settings\THIRD-PARTY-NOTICES.md','quick_settings\amd\LICENSES.txt');" ^
  "foreach ($path in $required) { if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw ('Missing helper: '+$path) } };" ^
  "$files=@(Get-ChildItem -Path 'quick_settings\*.py','quick_settings\licenses\*.txt' -File); $files+=@(Get-ChildItem -LiteralPath 'quick_settings\amd' -Recurse -File | Where-Object { $_.Extension -eq '.cs' -and $_.FullName -notmatch '[\\/]__pycache__[\\/]' }); $files+=@($required | ForEach-Object { Get-Item -LiteralPath $_ });" ^
  "foreach ($file in ($files | Sort-Object FullName -Unique)) { $relative=$file.FullName.Substring((Get-Location).Path.Length+1); $target=[IO.Path]::GetFullPath((Join-Path $root $relative)); if (-not $target.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe helper target' }; [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)); Copy-Item -LiteralPath $file.FullName -Destination $target -Force };" ^
  "foreach ($folder in @('quick_settings','cpu_power')) { $directory=Join-Path $root $folder; if (Test-Path -LiteralPath $directory) { $items=@(Get-Item -LiteralPath $directory)+@(Get-ChildItem -LiteralPath $directory -Recurse -Force); if ($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Linked payload path refused' }; foreach ($file in ($items | Where-Object { -not $_.PSIsContainer -and $_.Extension -in @('.pyc','.pyo') })) { if (-not $file.FullName.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe bytecode target' }; Remove-Item -LiteralPath $file.FullName -Force } } }" >> "%LOG%" 2>&1
if errorlevel 1 exit /b 1
rem ---------- end helper payload allowlist ----------
echo   - Plugin aggiornato negli Assets.
echo ===== PLUGIN OK ===== >> "%LOG%" 2>&1
exit /b 0

rem ------------------------------------------------------------
:no_npm
rem  Senza npm non possiamo compilare. Non spediamo un bundle a caso: si
rem  controlla se quello gia' negli Assets e' piu' recente di ogni sorgente.
rem  Se lo e', va bene ed e' solo un avviso. Se e' vecchio, si ferma tutto.
echo   - Node.js non e' installato: non posso ricompilare il plugin.
echo npm non trovato >> "%LOG%" 2>&1

if not exist "%ASSETS%\dist\index.js" (
    echo.
    echo   ************************************************************
    echo    Non c'e' nemmeno un plugin gia' compilato da spedire.
    echo    Installa Node.js ^(https://nodejs.org^) e rilancia la build.
    echo   ************************************************************
    echo.
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference='Stop';" ^
  "$b=Get-Item '%ASSETS%\dist\index.js';" ^
  "$bytecode=Get-ChildItem -LiteralPath '%ASSETS%' -Recurse -File | Where-Object { $_.Extension -in @('.pyc','.pyo') }; if ($bytecode) { exit 1 };" ^
  "$helpers=Get-ChildItem -Path 'screensaver\index.html','screensaver\main.js','LICENSE-Shortcuts','main.py','THIRD-PARTY-NOTICES.md','quick_settings\history_images.json','quick_settings\history_editorial.json','quick_settings\history_days.json','quick_settings\history_imported.json','quick_settings\*.py','quick_settings\LICENSE','quick_settings\NOTICE','quick_settings\LEGAL.md','quick_settings\THIRD-PARTY-NOTICES.md','quick_settings\licenses\*.txt','quick_settings\helper\*.ps1','quick_settings\bin\QuickSettingsAgent.exe','quick_settings\amd\adlx_helper.exe','quick_settings\amd\ADLXCSharpBind.dll','quick_settings\amd\LICENSES.txt','quick_settings\amd\build_amd.bat' -File -ErrorAction Stop;" ^
  "foreach ($file in $helpers) { $relative=$file.FullName.Substring((Get-Location).Path.Length+1); $target=Join-Path '%ASSETS%' $relative; if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { exit 1 }; if ((Get-FileHash -LiteralPath $file.FullName).Hash -cne (Get-FileHash -LiteralPath $target).Hash) { exit 1 } };" ^
  "$s=Get-ChildItem -Path 'src','assets','screensaver','quick_settings','main.py','package.json','plugin.json','rollup.config.js','tsconfig.json' -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $_.Extension -notin @('.pyc','.pyo') -and $_.FullName -notmatch '[\\/]__pycache__[\\/]' };" ^
  "$newest=($s | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1);" ^
  "if ($newest -and $newest.LastWriteTimeUtc -gt $b.LastWriteTimeUtc) { exit 1 } else { exit 0 }" >> "%LOG%" 2>&1
if errorlevel 1 (
    echo.
    echo   ************************************************************
    echo    I sorgenti del plugin sono piu' recenti del bundle gia'
    echo    pronto: continuando spediresti una versione vecchia del
    echo    plugin, senza accorgertene.
    echo.
    echo    Installa Node.js ^(https://nodejs.org^) e rilancia la build.
    echo   ************************************************************
    echo.
    exit /b 1
)

echo   - Il plugin gia' compilato negli Assets e' aggiornato: viene spedito
echo     quello. Nessuna modifica ai sorgenti e' rimasta fuori.
exit /b 0


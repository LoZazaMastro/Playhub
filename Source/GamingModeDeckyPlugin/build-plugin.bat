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

echo   - Installo le dipendenze del plugin...
call npm install --no-audit --no-fund >> "%LOG%" 2>&1
if errorlevel 1 (
    echo   ! npm install non riuscito. Dettagli in "%~dp0%LOG%"
    exit /b 1
)

echo   - Controllo i tipi...
call npx tsc --noEmit >> "%LOG%" 2>&1
if errorlevel 1 (
    echo   ! Il plugin non compila: errori di tipo. Dettagli in "%~dp0%LOG%"
    exit /b 1
)

echo   - Compilo il bundle...
call npx rollup -c >> "%LOG%" 2>&1
if errorlevel 1 (
    echo   ! rollup non riuscito. Dettagli in "%~dp0%LOG%"
    exit /b 1
)

if not exist "dist\index.js" (
    echo   ! rollup non ha prodotto dist\index.js. Dettagli in "%~dp0%LOG%"
    exit /b 1
)

rem ---------- copia negli Assets ----------
rem  Solo index.js: la sourcemap pesa oltre un mega e a Decky non serve.
if not exist "%ASSETS%\dist" mkdir "%ASSETS%\dist"
copy /y "dist\index.js" "%ASSETS%\dist\index.js" >> "%LOG%" 2>&1
del /q "%ASSETS%\dist\index.js.map" 2>nul
copy /y "plugin.json"   "%ASSETS%\plugin.json"   >> "%LOG%" 2>&1
copy /y "package.json"  "%ASSETS%\package.json"  >> "%LOG%" 2>&1

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
  "$b=Get-Item '%ASSETS%\dist\index.js';" ^
  "$s=Get-ChildItem -Path 'src','package.json','rollup.config.js','tsconfig.json' -Recurse -File -ErrorAction SilentlyContinue;" ^
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

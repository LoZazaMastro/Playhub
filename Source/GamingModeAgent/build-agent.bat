@echo off
setlocal
cd /d "%~dp0"
echo ===== BUILD AGENT START %DATE% %TIME% ===== > build_agent_log.txt 2>&1

rem Pubblica l'agente Gaming Mode come singolo eseguibile autosufficiente.
dotnet publish "GamingMode.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "publish_tmp" >> build_agent_log.txt 2>&1
if errorlevel 1 (
    echo ERRORE: dotnet publish fallito. Vedi build_agent_log.txt
    exit /b 1
)

if not exist "publish_tmp\GamingMode.exe" (
    echo ERRORE: publish_tmp\GamingMode.exe non trovato. Vedi build_agent_log.txt
    exit /b 1
)

rem Aggiorna la copia in publish\ (storico) e nel pacchetto bundlato in Playhub.
if not exist "publish" mkdir "publish"
copy /y "publish_tmp\GamingMode.exe" "publish\GamingMode.exe" >> build_agent_log.txt 2>&1
copy /y "publish_tmp\GamingMode.exe" "..\Playhub\Plugins\Gaming Mode\gaming-mode-win-x64\GamingMode.exe" >> build_agent_log.txt 2>&1
if errorlevel 1 (
    echo ERRORE: copia nel pacchetto Playhub fallita. Vedi build_agent_log.txt
    exit /b 1
)

echo ===== BUILD AGENT DONE ===== >> build_agent_log.txt 2>&1
echo Fatto. GamingMode.exe aggiornato in publish\ e nel pacchetto "Plugins\Gaming Mode\gaming-mode-win-x64".
echo (Questo bat viene chiamato automaticamente da build.bat e build-installer.bat: non serve lanciarlo a mano.)

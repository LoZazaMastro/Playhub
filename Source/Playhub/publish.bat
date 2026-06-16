@echo off
cd /d "%~dp0"
echo ===== PUBLISH START %DATE% %TIME% ===== > publish_log.txt 2>&1
echo Creo un pacchetto autosufficiente e spostabile in: dist_publish\ >> publish_log.txt 2>&1
dotnet publish "Playhub.csproj" -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:WindowsAppSDKSelfContained=true -o "dist_publish" >> publish_log.txt 2>&1
echo ===== PUBLISH DONE exitcode=%ERRORLEVEL% ===== >> publish_log.txt 2>&1
echo Fatto. Il pacchetto e' nella cartella dist_publish (puoi spostarla/zipparla intera).

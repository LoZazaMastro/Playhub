@echo off
REM Double-click this on Windows, where the network actually works.
setlocal
cd /d "%~dp0.."
echo Scarico le immagini editoriali mancanti...
python tools\fetch-history-images.py %*
echo.
pause

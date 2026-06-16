@echo off
chcp 65001 >nul
set "LOG=%~dp0diag2_log.txt"
set "VDF=C:\Program Files (x86)\Steam\userdata\168089063\config\shortcuts.vdf"
echo ===== DIAG2 (file shortcuts.vdf) %DATE% %TIME% ===== > "%LOG%" 2>&1
echo File: %VDF% >> "%LOG%" 2>&1
echo. >> "%LOG%" 2>&1
echo --- Esiste? --- >> "%LOG%" 2>&1
if exist "%VDF%" (echo SI >> "%LOG%" 2>&1) else (echo NO >> "%LOG%" 2>&1)
echo. >> "%LOG%" 2>&1
echo --- Proprietario --- >> "%LOG%" 2>&1
powershell -NoProfile -Command "(Get-Acl '%VDF%').Owner" >> "%LOG%" 2>&1
echo. >> "%LOG%" 2>&1
echo --- Permessi (icacls) --- >> "%LOG%" 2>&1
icacls "%VDF%" >> "%LOG%" 2>&1
echo. >> "%LOG%" 2>&1
echo --- Andrea puo' aprirlo in lettura+scrittura? --- >> "%LOG%" 2>&1
powershell -NoProfile -Command "try{ $fs=[System.IO.File]::Open('%VDF%',[System.IO.FileMode]::Open,[System.IO.FileAccess]::ReadWrite,[System.IO.FileShare]::None); $fs.Close(); 'APERTURA RW OK' } catch { 'APERTURA RW FALLITA: ' + $_.Exception.GetType().Name }" >> "%LOG%" 2>&1
echo ===== DIAG2 DONE ===== >> "%LOG%" 2>&1
echo Fatto. Puoi chiudere questa finestra.

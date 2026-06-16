@echo off
chcp 65001 >nul
set "LOG=%~dp0diag3_log.txt"
set "VDF=C:\Program Files (x86)\Steam\userdata\168089063\config\shortcuts.vdf"
echo ===== DIAG3 %DATE% %TIME% ===== > "%LOG%" 2>&1

echo --- Steam in esecuzione? --- >> "%LOG%" 2>&1
tasklist /fi "imagename eq steam.exe" >> "%LOG%" 2>&1
tasklist /fi "imagename eq steamwebhelper.exe" >> "%LOG%" 2>&1

echo. >> "%LOG%" 2>&1
echo --- Attributi file (R = sola lettura) --- >> "%LOG%" 2>&1
attrib "%VDF%" >> "%LOG%" 2>&1

echo. >> "%LOG%" 2>&1
echo --- Apertura con condivisione Read (come fa la scrittura tipica) --- >> "%LOG%" 2>&1
powershell -NoProfile -Command "try{ $fs=[System.IO.File]::Open('%VDF%',[System.IO.FileMode]::Open,[System.IO.FileAccess]::ReadWrite,[System.IO.FileShare]::Read); $fs.Close(); 'OK' } catch { 'FAIL: ' + $_.Exception.InnerException.GetType().FullName + ' :: ' + $_.Exception.InnerException.Message }" >> "%LOG%" 2>&1

echo. >> "%LOG%" 2>&1
echo --- Apertura con condivisione ReadWrite --- >> "%LOG%" 2>&1
powershell -NoProfile -Command "try{ $fs=[System.IO.File]::Open('%VDF%',[System.IO.FileMode]::Open,[System.IO.FileAccess]::ReadWrite,[System.IO.FileShare]::ReadWrite); $fs.Close(); 'OK' } catch { 'FAIL: ' + $_.Exception.InnerException.GetType().FullName + ' :: ' + $_.Exception.InnerException.Message }" >> "%LOG%" 2>&1

echo ===== DIAG3 DONE ===== >> "%LOG%" 2>&1
echo Fatto. Puoi chiudere questa finestra.

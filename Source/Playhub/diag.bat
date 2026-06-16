@echo off
chcp 65001 >nul
set "LOG=%~dp0diag_log.txt"
echo ===== PLAYHUB DIAG %DATE% %TIME% ===== > "%LOG%" 2>&1
echo --- whoami --- >> "%LOG%" 2>&1
whoami >> "%LOG%" 2>&1

echo. >> "%LOG%" 2>&1
echo --- Controlled Folder Access (0=off, 1=on, 2=audit) --- >> "%LOG%" 2>&1
powershell -NoProfile -Command "(Get-MpPreference).EnableControlledFolderAccess" >> "%LOG%" 2>&1
echo --- Cartelle protette --- >> "%LOG%" 2>&1
powershell -NoProfile -Command "(Get-MpPreference).ControlledFolderAccessProtectedFolders" >> "%LOG%" 2>&1
echo --- App consentite --- >> "%LOG%" 2>&1
powershell -NoProfile -Command "(Get-MpPreference).ControlledFolderAccessAllowedApplications" >> "%LOG%" 2>&1

echo. >> "%LOG%" 2>&1
echo --- Prova di scrittura nella cartella shortcuts di Steam --- >> "%LOG%" 2>&1
set "CFG=C:\Program Files (x86)\Steam\userdata\168089063\config"
echo Cartella: %CFG% >> "%LOG%" 2>&1
echo test> "%CFG%\playhub_writetest.tmp" 2>>"%LOG%"
if exist "%CFG%\playhub_writetest.tmp" (
  echo SCRITTURA OK >> "%LOG%" 2>&1
  del "%CFG%\playhub_writetest.tmp" >nul 2>&1
) else (
  echo SCRITTURA BLOCCATA >> "%LOG%" 2>&1
)

echo. >> "%LOG%" 2>&1
echo --- Permessi cartella config (icacls) --- >> "%LOG%" 2>&1
icacls "%CFG%" >> "%LOG%" 2>&1

echo ===== DIAG DONE ===== >> "%LOG%" 2>&1
echo Fatto. Puoi chiudere questa finestra.

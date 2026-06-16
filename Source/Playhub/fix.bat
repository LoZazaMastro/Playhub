@echo off
chcp 65001 >nul
set "VDF=C:\Program Files (x86)\Steam\userdata\168089063\config\shortcuts.vdf"
echo Tolgo l'attributo "sola lettura" da shortcuts.vdf...
attrib -R "%VDF%"
echo.
echo Attributi adesso:
attrib "%VDF%"
echo.
echo Fatto. Ora chiudi COMPLETAMENTE Steam (clic destro sull'icona vicino all'orologio - Esci) e riprova l'import.
pause

@echo off
setlocal
title Gaming Mode Uninstaller

set "ROOT=%~dp0"
set "SCRIPT=%ROOT%scripts\uninstall.ps1"

if not exist "%SCRIPT%" (
  set "SCRIPT=%ROOT%uninstall.ps1"
)

if not exist "%SCRIPT%" (
  echo.
  echo Gaming Mode uninstaller was not found.
  echo.
  echo Expected one of:
  echo   %ROOT%scripts\uninstall.ps1
  echo   %ROOT%uninstall.ps1
  echo.
  pause
  exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%"
set "EXITCODE=%ERRORLEVEL%"

echo.
if "%EXITCODE%"=="0" (
  echo Uninstall complete.
) else (
  echo Uninstall failed with exit code %EXITCODE%.
)
echo.
pause
exit /b %EXITCODE%

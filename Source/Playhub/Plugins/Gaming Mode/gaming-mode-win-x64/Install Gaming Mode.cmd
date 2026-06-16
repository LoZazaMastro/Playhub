@echo off
setlocal
title Gaming Mode Installer

set "ROOT=%~dp0"
set "SETUP=%ROOT%Setup.exe"
set "SCRIPT=%ROOT%scripts\install.ps1"

if exist "%SETUP%" (
  start "" "%SETUP%"
  exit /b 0
)

set "SETUP=%ROOT%artifacts\release\gaming-mode\Setup.exe"
if exist "%SETUP%" (
  start "" "%SETUP%"
  exit /b 0
)

if not exist "%SCRIPT%" (
  set "SCRIPT=%ROOT%install.ps1"
)

if not exist "%SCRIPT%" (
  echo.
  echo Gaming Mode installer was not found.
  echo.
  echo Expected one of:
  echo   %ROOT%scripts\install.ps1
  echo   %ROOT%install.ps1
  echo.
  pause
  exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%"
set "EXITCODE=%ERRORLEVEL%"

echo.
if "%EXITCODE%"=="0" (
  echo Installation complete.
) else (
  echo Installation failed with exit code %EXITCODE%.
)
echo.
pause
exit /b %EXITCODE%

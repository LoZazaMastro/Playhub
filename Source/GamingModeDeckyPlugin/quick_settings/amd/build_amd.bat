@echo off
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Could not find the C# compiler csc.exe ^(it ships with the Windows .NET Framework^).
  pause
  exit /b 1
)
echo Compiling adlx_helper.exe ...
"%CSC%" /nologo /platform:x64 /optimize /out:adlx_helper.exe /recurse:*.cs
echo.
if exist adlx_helper.exe (
  echo SUCCESS: adlx_helper.exe created. Close this window and reload the Quick Settings plugin.
) else (
  echo BUILD FAILED - please copy the messages above and send them back.
)
pause

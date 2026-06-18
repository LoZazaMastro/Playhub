@echo off
setlocal
cd /d "%~dp0"
echo ===== BUILD START %DATE% %TIME% ===== > build_log.txt 2>&1
echo --- where dotnet --- >> build_log.txt 2>&1
where dotnet >> build_log.txt 2>&1
echo --- dotnet --version --- >> build_log.txt 2>&1
dotnet --version >> build_log.txt 2>&1
echo --- stop previous local Debug instance --- >> build_log.txt 2>&1
powershell -NoProfile -ExecutionPolicy Bypass -Command "$root=[IO.Path]::GetFullPath('%CD%\bin\x64\Debug\'); Get-Process Playhub -ErrorAction SilentlyContinue ^| Where-Object { $_.Path -and [IO.Path]::GetFullPath($_.Path).StartsWith($root,[StringComparison]::OrdinalIgnoreCase) } ^| Stop-Process -Force" >> build_log.txt 2>&1
echo --- dotnet clean (Debug x64) --- >> build_log.txt 2>&1
dotnet clean "Playhub.csproj" -c Debug -p:Platform=x64 >> build_log.txt 2>&1
echo --- dotnet build (Debug x64) --- >> build_log.txt 2>&1
dotnet build "Playhub.csproj" -c Debug -p:Platform=x64 >> build_log.txt 2>&1
set "BUILD_EXIT=%ERRORLEVEL%"
echo ===== BUILD DONE exitcode=%BUILD_EXIT% ===== >> build_log.txt 2>&1
exit /b %BUILD_EXIT%

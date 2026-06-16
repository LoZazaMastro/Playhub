@echo off
cd /d "%~dp0"
echo ===== BUILD START %DATE% %TIME% ===== > build_log.txt 2>&1
echo --- where dotnet --- >> build_log.txt 2>&1
where dotnet >> build_log.txt 2>&1
echo --- dotnet --version --- >> build_log.txt 2>&1
dotnet --version >> build_log.txt 2>&1
echo --- dotnet build (Debug x64) --- >> build_log.txt 2>&1
dotnet build "Playhub.csproj" -c Debug -p:Platform=x64 >> build_log.txt 2>&1
echo ===== BUILD DONE exitcode=%ERRORLEVEL% ===== >> build_log.txt 2>&1

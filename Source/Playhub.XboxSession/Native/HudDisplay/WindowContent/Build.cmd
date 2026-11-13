@echo off
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" >nul
if errorlevel 1 exit /b 1
cd /d "%~dp0"
cl /nologo /std:c++20 /W4 /WX /external:anglebrackets /external:W0 /EHsc /O2 /MT /DUNICODE /D_UNICODE Program.cpp Checks.cpp /Fe:PlayhubWindowContentRead.exe /link /Brepro /MANIFEST:EMBED /MANIFESTINPUT:PerMonitor.manifest
exit /b %errorlevel%

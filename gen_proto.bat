@echo off
setlocal
cd /d "%~dp0"

set "PS=pwsh"
pwsh -NoProfile -Command "$null" >nul 2>nul
if errorlevel 1 set "PS=powershell"

echo [1/3] Generating client protobuf classes and Unity meta...
%PS% -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\gen_proto.ps1"
if errorlevel 1 goto :fail

echo [2/3] Generating client MessageIds and TipFaults...
%PS% -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\gen_messageids.ps1"
if errorlevel 1 goto :fail

echo [3/3] Running client compile check...
%PS% -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\client_compile_check.ps1" -ProjectPath "%~dp0." -FallbackRsp "%~dp0tools\compile_check\runtime_refs.rsp"
if errorlevel 1 goto :fail

echo All client protocol generation and compile checks passed.
call :pause_if_double_clicked
exit /b 0

:fail
set "ERR=%ERRORLEVEL%"
echo Failed with exit code %ERR%.
call :pause_if_double_clicked
exit /b %ERR%

:pause_if_double_clicked
echo %CMDCMDLINE% | find /i "/c """ >nul && pause
exit /b 0

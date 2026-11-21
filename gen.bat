@echo off
setlocal
cd /d "%~dp0"

set "MODE=%~1"
if "%MODE%"=="" set "MODE=all"
if /I "%MODE%"=="-h" goto :usage
if /I "%MODE%"=="--help" goto :usage
if /I "%MODE%"=="help" goto :usage
if /I not "%MODE%"=="all" if /I not "%MODE%"=="proto" if /I not "%MODE%"=="client" (
    echo Unknown mode: %MODE%
    goto :usage
)

set "PS=pwsh"
pwsh -NoProfile -Command "$null" >nul 2>nul
if errorlevel 1 set "PS=powershell"

rem Locate sibling authoritative C++/Go server repo (matches gen_proto.ps1 / sync_tables.ps1).
set "SERVER_ROOT="
for %%D in (xuanming-server-mmo-cpp xuanming-server-mmo mmorpg) do (
    if not defined SERVER_ROOT (
        if exist "%~dp0..\%%D\proto\message_id.txt" if exist "%~dp0..\%%D\tools\data_table_exporter\run.py" (
            for %%I in ("%~dp0..\%%D") do set "SERVER_ROOT=%%~fI"
        )
    )
)
if not defined SERVER_ROOT (
    echo Authoritative server repo not found next to mmorpg-client.
    goto :fail_code1
)

if /I "%MODE%"=="client" goto :client_only

set "REPO_PROTOC_DIR=%SERVER_ROOT%\third_party\protobuf\install_vs2026_dbg\bin"
set "REPO_PROTOC=%REPO_PROTOC_DIR%\protoc.exe"
if not exist "%REPO_PROTOC%" (
    echo Required protoc was not found: "%REPO_PROTOC%"
    goto :fail_code1
)
"%REPO_PROTOC%" --version 2>nul | findstr /x /c:"libprotoc 35.1" >nul
if errorlevel 1 (
    echo Repository generation requires libprotoc 35.1. Found:
    "%REPO_PROTOC%" --version
    goto :fail_code1
)
set "PATH=%REPO_PROTOC_DIR%;%PATH%"

if /I "%MODE%"=="proto" goto :server_proto

echo [1/6] Exporting server data tables (and client Table/Generated C#)...
set "EXPORT_PYTHON="
if defined PYTHON_EXE set "EXPORT_PYTHON=%PYTHON_EXE:"=%"
if not defined EXPORT_PYTHON (
    for /f "delims=" %%I in ('py -3 -c "import sys; print(sys.executable)" 2^>nul') do (
        if not defined EXPORT_PYTHON set "EXPORT_PYTHON=%%I"
    )
)
if not defined EXPORT_PYTHON (
    for /f "delims=" %%I in ('python -c "import sys; print(sys.executable)" 2^>nul') do (
        if not defined EXPORT_PYTHON set "EXPORT_PYTHON=%%I"
    )
)
if not defined EXPORT_PYTHON (
    echo Python 3 was not found. Install Python 3 or set PYTHON_EXE.
    goto :fail_code1
)
pushd "%SERVER_ROOT%"
"%EXPORT_PYTHON%" tools\data_table_exporter\run.py tools\data_table_exporter\exporter_config.yaml
set "STEP_ERR=%ERRORLEVEL%"
popd
if not "%STEP_ERR%"=="0" (
    echo Server table export failed with exit code %STEP_ERR%.
    goto :fail_step
)

:server_proto
echo [2/6] Running server proto generator (updating message_id.txt and client Rpc stubs)...
if /I not "%PS%"=="pwsh" (
    echo PowerShell 7 ^(pwsh^) is required to run server dev_tools.ps1 proto-gen-run.
    goto :fail_code1
)
pushd "%SERVER_ROOT%"
%PS% -NoProfile -ExecutionPolicy Bypass -File tools\scripts\dev_tools.ps1 -Command proto-gen-run -ConfigPath tools\proto_generator\protogen\etc\proto_gen.yaml
set "STEP_ERR=%ERRORLEVEL%"
popd
if not "%STEP_ERR%"=="0" (
    echo Server proto generation failed with exit code %STEP_ERR%.
    goto :fail_step
)

:client_only
if /I not "%MODE%"=="proto" (
    echo [3/6] Syncing client binary tables ^(Assets/StreamingAssets/Tables/*.pb^) and meta...
    %PS% -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\sync_tables.ps1" -ServerRoot "%SERVER_ROOT%"
    if errorlevel 1 goto :fail
)

echo [4/6] Generating client protobuf classes and Unity meta...
%PS% -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\gen_proto.ps1" -ProtoRoot "%SERVER_ROOT%"
if errorlevel 1 goto :fail

echo [5/6] Generating client MessageIds and TipFaults...
%PS% -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\gen_messageids.ps1" -ProtoRoot "%SERVER_ROOT%"
if errorlevel 1 goto :fail

echo [6/6] Running client compile check...
%PS% -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\client_compile_check.ps1" -ProjectPath "%~dp0." -FallbackRsp "%~dp0tools\compile_check\runtime_refs.rsp"
if errorlevel 1 goto :fail

echo All table + protocol generation and client compile checks passed.
call :pause_if_double_clicked
exit /b 0

:usage
echo Usage: gen.bat [all ^| proto ^| client]
echo   all    ^(default^) Server export tables + server proto-gen + client tables/proto/compile check
echo   proto  Server proto-gen + client proto/MessageIds/compile check ^(skip Excel export^)
echo   client Client sync_tables + gen_proto + gen_messageids + compile check ^(server already ran^)
exit /b 1

:fail_code1
set "ERR=1"
goto :fail_exit

:fail_step
set "ERR=%STEP_ERR%"
goto :fail_exit

:fail
set "ERR=%ERRORLEVEL%"

:fail_exit
echo Failed with exit code %ERR%.
call :pause_if_double_clicked
exit /b %ERR%

:pause_if_double_clicked
echo %CMDCMDLINE% | find /i "/c """ >nul && pause
exit /b 0

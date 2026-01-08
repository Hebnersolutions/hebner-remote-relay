@echo off
setlocal

REM Always run relative to the script folder
set SCRIPT_DIR=%~dp0
set ROOT_DIR=%SCRIPT_DIR%..

echo Starting broker stub on http://127.0.0.1:5189 ...
start "BrokerStub" cmd /c "dotnet run --project "%ROOT_DIR%\src\Hebner.Agent.BrokerStub\Hebner.Agent.BrokerStub.csproj""
timeout /t 2 >nul

echo Starting tray app...
start "Tray" cmd /c "dotnet run --project "%ROOT_DIR%\src\Hebner.Agent.Tray\Hebner.Agent.Tray.csproj""

echo Starting service in console mode (dev)...
dotnet run --project "%ROOT_DIR%\src\Hebner.Agent.Service\Hebner.Agent.Service.csproj"

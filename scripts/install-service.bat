@echo off
setlocal
REM Install the service (requires admin)
sc stop "Hebner Remote Agent Service" >nul 2>&1
sc delete "Hebner Remote Agent Service" >nul 2>&1

dotnet publish ..\src\Hebner.Agent.Service -c Release -r win-x64 --self-contained false -o ..\publish\service
sc create "Hebner Remote Agent Service" binPath= "%~dp0..\publish\service\Hebner.Agent.Service.exe" start= auto
sc description "Hebner Remote Agent Service" "Hebner Solutions Remote Support background service"
sc start "Hebner Remote Agent Service"
echo Service installed and started.

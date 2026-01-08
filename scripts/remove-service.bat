@echo off
setlocal
sc stop "Hebner Remote Agent Service" >nul 2>&1
sc delete "Hebner Remote Agent Service"
echo Removed.

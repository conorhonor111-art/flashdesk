@echo off
REM Double-click after touching the mark's geometry anywhere -- checks all six copies still agree.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Check-MarkGeometry.ps1"
echo.
pause

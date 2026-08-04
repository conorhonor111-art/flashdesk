@echo off
REM Double-click this. It runs the check and keeps the window open so you can read the result.
REM -ExecutionPolicy Bypass is here because a freshly-cloned .ps1 is blocked by default on Windows,
REM and a check nobody can run is a check nobody runs.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Check-LiveBuild.ps1"
echo.
pause

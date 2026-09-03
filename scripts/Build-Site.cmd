@echo off
REM Run this after editing anything under site-src\ -- regenerates every page in site\.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-Site.ps1"
echo.
pause

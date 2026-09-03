@echo off
REM Double-click this after every "uipro update" or "uipro init --global" -- both regenerate
REM SKILL.md from a template that says python3, which fails on this machine. This puts the fix
REM back. Keeps the window open so you can read the result.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Fix-UiUxProMaxPython.ps1"
echo.
pause

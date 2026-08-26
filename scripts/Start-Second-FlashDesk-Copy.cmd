@echo off
REM Starts a SECOND, independent copy of FlashDesk on THIS machine, so two copies can pair with
REM each other and the two-machine test can be run without a second computer.
REM
REM Three environment variables make this copy safe and independent, and nothing else changes:
REM
REM   FLASHDESK_CONFIG_DIR    Points this copy at its OWN data folder, so it generates its own
REM                           9-digit number instead of sharing the normal copy's number (which
REM                           lives in %APPDATA%\FlashDesk\identity.json).
REM
REM   FLASHDESK_DISABLE_CONTROL   Makes this copy physically unable to send OR act on a "control
REM                           mouse and keyboard" request, in either direction. Both copies are on
REM                           the SAME real desktop here - real control would move your actual
REM                           mouse, not a separate machine's. The consent (Accept) dialog is NOT
REM                           affected by this: it still requires a real click either way.
REM
REM   FLASHDESK_FORCE_GDI     Skips straight to GDI screen capture. Windows will not let two
REM                           processes both hold DXGI Desktop Duplication on the same screen at
REM                           once, so the first copy keeps DXGI and this one does not spend 30
REM                           seconds failing to get it before falling back anyway.
REM
REM This window's title bar will say "TEST COPY (remote control disabled)" so the two windows can
REM never be confused with each other.

setlocal

set "FLASHDESK_EXE=C:\Users\PC\Desktop\FlashDesk-test-build\FlashDesk.exe"

if not exist "%FLASHDESK_EXE%" (
    echo Cannot find %FLASHDESK_EXE%
    echo Publish a build there first ^(see CLAUDE.md's publish command^), then run this again.
    pause
    exit /b 1
)

set "FLASHDESK_CONFIG_DIR=%LOCALAPPDATA%\FlashDesk-SecondCopy"
set "FLASHDESK_DISABLE_CONTROL=1"
set "FLASHDESK_FORCE_GDI=1"

start "" "%FLASHDESK_EXE%"

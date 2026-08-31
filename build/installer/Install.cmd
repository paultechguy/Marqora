@echo off
rem ---------------------------------------------------------------------------
rem  Installs Marqora for the current user. Double-click, or run from a prompt.
rem
rem  This wrapper exists so nobody has to know how to run a PowerShell script.
rem  It calls Windows PowerShell by full path rather than relying on PATH, and
rem  passes -ExecutionPolicy Bypass because a script extracted from a downloaded
rem  zip is otherwise blocked by the default policy on a stock machine.
rem
rem  Any arguments given here are forwarded, so this still works:
rem      Install.cmd -NoDesktopShortcut
rem ---------------------------------------------------------------------------

setlocal
title Install Marqora

set "PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"

if not exist "%PS%" (
    echo Windows PowerShell was not found at:
    echo   %PS%
    echo.
    echo Marqora cannot be installed without it.
    pause
    exit /b 1
)

if not exist "%~dp0install\Install.ps1" (
    echo This file has been separated from the rest of the release.
    echo.
    echo Extract the whole zip, keeping its folder structure, then run
    echo Install.cmd from the top of the extracted folder.
    pause
    exit /b 1
)

"%PS%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0install\Install.ps1" %*
set "RC=%ERRORLEVEL%"

if not "%RC%"=="0" (
    echo.
    echo Install failed with exit code %RC%.
)

pause
exit /b %RC%

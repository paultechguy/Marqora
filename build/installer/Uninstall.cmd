@echo off
rem ---------------------------------------------------------------------------
rem  Removes Marqora. Double-click, or run from a prompt.
rem
rem  The usual way to uninstall is Settings ^> Apps ^> Installed apps ^> Marqora,
rem  which the installer registers. This is here for anyone who still has the
rem  extracted release folder and would rather not go hunting through Settings.
rem
rem  It runs the copy of Uninstall.ps1 in this folder, not the one inside the
rem  install, so nothing is executing from the directory being deleted.
rem
rem  cd to TEMP first: cmd.exe holds its working directory open for as long as
rem  it lives, and if that directory is anywhere under the install, the delete
rem  fails with a sharing violation that looks like a permissions problem.
rem
rem  Any arguments given here are forwarded, so this works:
rem      Uninstall.cmd -RemoveUserData
rem ---------------------------------------------------------------------------

setlocal
title Uninstall Marqora

cd /d "%TEMP%"

set "PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"

if not exist "%PS%" (
    echo Windows PowerShell was not found at:
    echo   %PS%
    pause
    exit /b 1
)

if not exist "%~dp0install\Uninstall.ps1" (
    echo This file has been separated from the rest of the release.
    echo.
    echo Uninstall Marqora from Settings ^> Apps ^> Installed apps instead.
    pause
    exit /b 1
)

rem -NoPause because this wrapper does the pausing.
"%PS%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0install\Uninstall.ps1" -NoPause %*
set "RC=%ERRORLEVEL%"

if not "%RC%"=="0" (
    echo.
    echo Uninstall failed with exit code %RC%.
)

pause
exit /b %RC%

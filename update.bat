@echo off
chcp 65001 >nul 2>nul
setlocal enabledelayedexpansion
title QuadClaude Updater
color 0F
cd /d "%~dp0"

echo.
echo  +--------------------------------------------+
echo  :          QuadClaude Updater                :
echo  +--------------------------------------------+
echo.
echo  Pulls the latest code, rebuilds QuadClaude, and
echo  (optionally) re-runs setup to refresh your config
echo  and helper commands.
echo.

:: --- Must be a git checkout to auto-update ---
if not exist ".git" (
    echo  This folder isn't a git checkout, so it can't auto-update.
    echo  Re-download or clone the latest from:
    echo    https://github.com/jamesonhussey/quad-claude-standalone
    echo.
    pause
    exit /b 1
)

:: --- Close any running instance so the exe can be overwritten ---
echo  [1/3] Closing any running QuadClaude...
taskkill /IM QuadClaude.exe /F >nul 2>nul
echo.

:: --- Pull latest ---
echo  [2/3] Pulling latest changes...
git pull
if errorlevel 1 (
    echo.
    echo  ERROR: git pull failed. Resolve the message above, then re-run.
    echo  ^(If you have local edits, commit or stash them first.^)
    pause
    exit /b 1
)
echo.

:: --- Rebuild ---
echo  [3/3] Rebuilding QuadClaude...
pushd QuadClaude
dotnet publish -c Release -r win-x64 --no-self-contained -o publish
if errorlevel 1 (
    echo.
    echo  ERROR: Build failed. If the .NET 9 SDK is missing, run install.bat
    echo  once ^(it can auto-install it^), then re-run update.bat.
    popd
    pause
    exit /b 1
)
popd
echo.

echo  =============================================
echo    Updated to the latest version.
echo.
set /p "RUN_SETUP=  Re-run setup to refresh config + helper commands? [Y/n]: "
if /i "!RUN_SETUP!"=="n" goto :done
echo.
"%~dp0QuadClaude\publish\QuadClaude.exe" setup

:done
echo.
echo    Relaunch QuadClaude from your shortcut when ready.
echo  =============================================
echo.
pause

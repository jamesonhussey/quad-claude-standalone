@echo off
chcp 65001 >nul 2>nul
setlocal enabledelayedexpansion
title QuadClaude Installer
color 0F

:: Resolve the directory this script lives in (the extracted zip root)
set "SETUP_DIR=%~dp0"
if "!SETUP_DIR:~-1!"=="\" set "SETUP_DIR=!SETUP_DIR:~0,-1!"

echo.
echo  +--------------------------------------------+
echo  :         QuadClaude Installer                :
echo  +--------------------------------------------+
echo.
echo  This will set up QuadClaude on your machine.
echo  QuadClaude gives you 4 Claude Code terminals
echo  snapped into a quad grid with glow borders
echo  and sound notifications.
echo.

:: =============================================
::  Step 1: Check for .NET SDK
:: =============================================

echo  [1/4] Checking prerequisites...
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
    echo  ERROR: .NET 9 SDK is not installed.
    echo.
    echo  To install it, open PowerShell and run:
    echo    winget install Microsoft.DotNet.SDK.9
    echo.
    echo  Then re-run this installer.
    echo.
    pause
    exit /b 1
)

for /f "tokens=1 delims=." %%v in ('dotnet --version 2^>nul') do set "DOTNET_MAJOR=%%v"
if "!DOTNET_MAJOR!" lss "9" (
    echo  WARNING: Found .NET SDK !DOTNET_MAJOR! but QuadClaude needs .NET 9.
    echo  Install it: winget install Microsoft.DotNet.SDK.9
    echo.
    set /p "CONT=  Continue anyway? [y/N]: "
    if /i not "!CONT!"=="y" exit /b 1
    echo.
)

for /f "tokens=*" %%d in ('dotnet --version') do echo    .NET SDK %%d -- OK
echo.

:: =============================================
::  Step 2: Build QuadClaude
:: =============================================

echo  [2/4] Building QuadClaude...
echo.

pushd "!SETUP_DIR!\QuadClaude"
dotnet publish -c Release -r win-x64 --no-self-contained -o publish 2>&1
if errorlevel 1 (
    echo.
    echo  ERROR: Build failed.
    echo  Make sure .NET 9 SDK is installed: winget install Microsoft.DotNet.SDK.9
    popd
    pause
    exit /b 1
)
popd

set "EXE_PATH=!SETUP_DIR!\QuadClaude\publish\QuadClaude.exe"
set "ICO_PATH=!SETUP_DIR!\QuadClaude\QuadClaude.ico"

echo.
echo    Build succeeded.
echo.

:: =============================================
::  Step 3: Create shortcuts
:: =============================================

echo  [3/4] Creating shortcuts...
echo.

set "PS_SCRIPT=%TEMP%\quadclaude_shortcuts.ps1"

:: Detect actual desktop path (may be OneDrive-redirected)
for /f "tokens=*" %%p in ('powershell -NoProfile -Command "[Environment]::GetFolderPath('Desktop')"') do set "REAL_DESKTOP=%%p"
if "!REAL_DESKTOP!"=="" set "REAL_DESKTOP=%USERPROFILE%\Desktop"

:: Write the PowerShell script line by line
echo $exe = '!EXE_PATH!' > "!PS_SCRIPT!"
echo $ico = '!ICO_PATH!' >> "!PS_SCRIPT!"
echo $dir = '!SETUP_DIR!' >> "!PS_SCRIPT!"
echo $desktop = '!REAL_DESKTOP!' >> "!PS_SCRIPT!"
echo $startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs' >> "!PS_SCRIPT!"
echo $ws = New-Object -ComObject WScript.Shell >> "!PS_SCRIPT!"
echo. >> "!PS_SCRIPT!"
echo $s = $ws.CreateShortcut("$desktop\QuadClaude.lnk") >> "!PS_SCRIPT!"
echo $s.TargetPath = $exe >> "!PS_SCRIPT!"
echo $s.Arguments = 'launch' >> "!PS_SCRIPT!"
echo $s.WorkingDirectory = $dir >> "!PS_SCRIPT!"
echo $s.Description = 'Launch QuadClaude' >> "!PS_SCRIPT!"
echo $s.IconLocation = "$ico,0" >> "!PS_SCRIPT!"
echo $s.Save() >> "!PS_SCRIPT!"
echo. >> "!PS_SCRIPT!"
echo $s = $ws.CreateShortcut("$desktop\QuadClaude Setup.lnk") >> "!PS_SCRIPT!"
echo $s.TargetPath = $exe >> "!PS_SCRIPT!"
echo $s.Arguments = 'setup' >> "!PS_SCRIPT!"
echo $s.WorkingDirectory = $dir >> "!PS_SCRIPT!"
echo $s.Description = 'QuadClaude Setup Wizard' >> "!PS_SCRIPT!"
echo $s.IconLocation = "$ico,0" >> "!PS_SCRIPT!"
echo $s.Save() >> "!PS_SCRIPT!"
echo. >> "!PS_SCRIPT!"
echo $s = $ws.CreateShortcut("$startMenu\QuadClaude.lnk") >> "!PS_SCRIPT!"
echo $s.TargetPath = $exe >> "!PS_SCRIPT!"
echo $s.Arguments = 'launch' >> "!PS_SCRIPT!"
echo $s.WorkingDirectory = $dir >> "!PS_SCRIPT!"
echo $s.Description = 'Launch QuadClaude' >> "!PS_SCRIPT!"
echo $s.IconLocation = "$ico,0" >> "!PS_SCRIPT!"
echo $s.Save() >> "!PS_SCRIPT!"

powershell -NoProfile -ExecutionPolicy Bypass -File "!PS_SCRIPT!" 2>&1

if exist "!REAL_DESKTOP!\QuadClaude.lnk" (
    echo    Desktop:    QuadClaude.lnk       -- launches the quad grid
) else (
    echo    Warning: Could not create desktop launch shortcut
)

if exist "!REAL_DESKTOP!\QuadClaude Setup.lnk" (
    echo    Desktop:    QuadClaude Setup.lnk -- re-run setup wizard
) else (
    echo    Warning: Could not create desktop setup shortcut
)

if exist "%APPDATA%\Microsoft\Windows\Start Menu\Programs\QuadClaude.lnk" (
    echo    Start Menu: QuadClaude.lnk       -- searchable, pin to taskbar
) else (
    echo    Warning: Could not create Start Menu shortcut
)

del "!PS_SCRIPT!" >nul 2>nul
echo.

:: =============================================
::  Step 4: Run Setup Wizard
:: =============================================

echo  [4/4] Setup Wizard
echo.
echo  =============================================
echo    Almost done! The setup wizard will now
echo    configure your shell, projects directory,
echo    layout, sounds, and permissions.
echo  =============================================
echo.

set /p "RUN_SETUP=  Launch the setup wizard now? [Y/n]: "
if /i "!RUN_SETUP!"=="n" goto :skip_setup

echo.
"!EXE_PATH!" setup
goto :done

:skip_setup
echo.
echo  Skipped. You can run it later by double-clicking
echo  "QuadClaude Setup" on your desktop.

:done
echo.
echo  =============================================
echo    Installation complete!
echo  =============================================
echo.
echo    Installed from: !SETUP_DIR!
echo.
echo    Your shortcuts:
echo      Desktop:    "QuadClaude"       -- launch the quad grid
echo      Desktop:    "QuadClaude Setup" -- re-run the wizard
echo      Start Menu: "QuadClaude"       -- search or pin to taskbar
echo.
echo    Tip: Search "QuadClaude" in Start, then right-click
echo         and "Pin to taskbar" for one-click launches.
echo  =============================================
echo.
pause

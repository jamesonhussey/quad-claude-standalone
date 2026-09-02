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

:: QuadClaude targets net9.0-windows, so an older SDK (e.g. .NET 8) CANNOT build
:: it. Detect a real .NET 9+ SDK; if none is present, auto-install it with winget
:: and re-check. Hard-block if it still isn't available (no "continue anyway").
call :detect_net9
if defined HAVE_NET9 goto :sdk_ok

echo    No .NET 9+ SDK found (an older SDK like .NET 8 can't build QuadClaude).
echo    Attempting to install the .NET 9 SDK with winget...
echo.
where winget >nul 2>nul
if errorlevel 1 (
    echo    ERROR: winget isn't available to auto-install the SDK.
    echo    Install the .NET 9 SDK manually, then re-run install.bat:
    echo      https://dotnet.microsoft.com/download/dotnet/9.0
    echo.
    pause
    exit /b 1
)

winget install --id Microsoft.DotNet.SDK.9 -e --source winget --accept-source-agreements --accept-package-agreements
echo.

call :detect_net9
if defined HAVE_NET9 goto :sdk_ok

echo    ERROR: .NET 9 SDK still not detected after installing.
echo    Close this window, open a NEW terminal (so PATH refreshes), and run
echo    install.bat again. If it still fails, install manually:
echo      https://dotnet.microsoft.com/download/dotnet/9.0
echo.
pause
exit /b 1

:sdk_ok
for /f "tokens=*" %%d in ('dotnet --version 2^>nul') do set "DOTNET_VER=%%d"
echo    .NET SDK !DOTNET_VER! (9+) -- OK
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
exit /b 0

:: ============================================================
::  Subroutines
:: ============================================================

:: Sets HAVE_NET9=1 if a .NET SDK with major version >= 9 is installed.
:detect_net9
set "HAVE_NET9="
where dotnet >nul 2>nul
if errorlevel 1 goto :eof
for /f "tokens=1 delims=." %%a in ('dotnet --list-sdks 2^>nul') do (
    if %%a GEQ 9 set "HAVE_NET9=1"
)
goto :eof

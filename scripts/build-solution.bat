@echo off
echo ========================================
echo Building Full Solution
echo ========================================
echo.

cd /d "%~dp0.."

echo Restoring NuGet packages...
dotnet restore RadioConsole.sln

if %ERRORLEVEL% NEQ 0 (
    echo ❌ Restore failed
    pause
    exit /b 1
)

echo.
echo Building solution...
dotnet build RadioConsole.sln --no-restore --verbosity minimal

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ========================================
    echo ✅ Build Successful!
    echo ========================================
    echo.
    echo All projects built successfully.
    echo Spotify loopback feature is ready.
    echo.
    echo Documentation:
    echo   - Quick Start: SPOTIFY_LOOPBACK_QUICKSTART.md
    echo   - Full Guide:  SPOTIFY_LOOPBACK_SETUP.md
    echo   - Index:       design\SPOTIFY_LOOPBACK_INDEX.md
    echo.
) else (
    echo.
    echo ========================================
    echo ❌ Build Failed
    echo ========================================
    echo.
    echo Please review the error messages above.
    echo.
)

pause

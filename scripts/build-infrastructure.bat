@echo off
echo ========================================
echo Building Radio.Infrastructure Project
echo ========================================
echo.

cd /d "%~dp0.."

echo Building project...
dotnet build src\Radio.Infrastructure\Radio.Infrastructure.csproj --no-restore

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ========================================
    echo ✅ Build Successful!
    echo ========================================
    echo.
    echo Spotify loopback implementation is ready.
    echo.
    echo Next steps:
    echo   1. Read SPOTIFY_LOOPBACK_QUICKSTART.md
    echo   2. Run scripts\Setup-SpotifyLoopback.ps1
    echo   3. Test the implementation
    echo.
) else (
    echo.
    echo ========================================
    echo ❌ Build Failed
    echo ========================================
    echo.
    echo Please check the error messages above.
    echo.
)

pause

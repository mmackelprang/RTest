@echo off
echo Running Spotify Loopback Diagnostics...
echo.
powershell -ExecutionPolicy Bypass -File "%~dp0Test-SpotifyLoopback.ps1"

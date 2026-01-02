#
# Run UAT Test Tool interactively with real radio hardware
# Usage: .\run-uat-interactive.ps1
#
# This runs the Audio User Acceptance Testing (UAT) tool which provides
# an interactive menu for testing various audio subsystems with real hardware.
#

$ErrorActionPreference = "Stop"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Radio Console - UAT Test Runner" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Interactive User Acceptance Testing Tool" -ForegroundColor Yellow
Write-Host ""
Write-Host "IMPORTANT:" -ForegroundColor Red
Write-Host "   - This requires real radio hardware (RTL-SDR, RF320, etc.)" -ForegroundColor Yellow
Write-Host "   - Audio output devices must be configured" -ForegroundColor Yellow
Write-Host "   - Tests will produce audible audio output" -ForegroundColor Yellow
Write-Host ""
$response = Read-Host "Press Enter to continue or Ctrl+C to cancel"
Write-Host ""

# Build the UAT tool
Write-Host "Building UAT test tool..." -ForegroundColor Yellow
dotnet build tools/Radio.Tools.AudioUAT/Radio.Tools.AudioUAT.csproj --configuration Release

Write-Host ""
Write-Host "Starting UAT Test Runner..." -ForegroundColor Green
Write-Host ""

# Run the UAT tool
dotnet run --project tools/Radio.Tools.AudioUAT/Radio.Tools.AudioUAT.csproj --configuration Release

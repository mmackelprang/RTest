#
# Run xUnit tests with real radio hardware
# Usage: .\run-unit-tests-hardware.ps1
#
# IMPORTANT: This requires real radio hardware to be connected.
# Tests will interact with actual USB radio devices (RTL-SDR, RF320).
#

$ErrorActionPreference = "Stop"

Write-Host "======================================" -ForegroundColor Cyan
Write-Host "Radio Console - xUnit Tests with Hardware" -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "WARNING: This will interact with real radio hardware!" -ForegroundColor Red
Write-Host "   Make sure your USB radio devices are connected." -ForegroundColor Yellow
Write-Host ""
$response = Read-Host "Press Enter to continue or Ctrl+C to cancel"
Write-Host ""

# Build the solution first
Write-Host "Building solution..." -ForegroundColor Yellow
dotnet build --configuration Release

Write-Host ""
Write-Host "Running xUnit tests (excluding E2E and bUnit tests)..." -ForegroundColor Yellow
Write-Host ""

# Run all xUnit test projects except E2E and bUnit (Web.Tests)
dotnet test `
    --configuration Release `
    --logger "console;verbosity=normal" `
    --results-directory ./TestResults/xUnit `
    --collect:"XPlat Code Coverage" `
    --filter "FullyQualifiedName!~Radio.Web.E2ETests&FullyQualifiedName!~Radio.Web.Tests"

Write-Host ""
Write-Host "======================================" -ForegroundColor Green
Write-Host "xUnit Tests Complete" -ForegroundColor Green
Write-Host "======================================" -ForegroundColor Green

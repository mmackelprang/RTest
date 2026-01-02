#
# Run bUnit tests only
# Usage: .\run-bunit-tests.ps1
#

$ErrorActionPreference = "Stop"

Write-Host "======================================" -ForegroundColor Cyan
Write-Host "Radio Console - bUnit Tests" -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""

# Build the solution first
Write-Host "Building solution..." -ForegroundColor Yellow
dotnet build --configuration Release

Write-Host ""
Write-Host "Running bUnit tests..." -ForegroundColor Yellow
Write-Host ""

# Run bUnit tests (Radio.Web.Tests project)
dotnet test tests/Radio.Web.Tests/Radio.Web.Tests.csproj `
    --configuration Release `
    --logger "console;verbosity=normal" `
    --results-directory ./TestResults/bUnit `
    --collect:"XPlat Code Coverage"

Write-Host ""
Write-Host "======================================" -ForegroundColor Green
Write-Host "bUnit Tests Complete" -ForegroundColor Green
Write-Host "======================================" -ForegroundColor Green

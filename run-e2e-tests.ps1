#
# Run E2E tests only
# Usage: .\run-e2e-tests.ps1
#

$ErrorActionPreference = "Stop"

Write-Host "======================================" -ForegroundColor Cyan
Write-Host "Radio Console - E2E Tests" -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""

# Build the solution first
Write-Host "Building solution..." -ForegroundColor Yellow
dotnet build --configuration Release

Write-Host ""
Write-Host "Running E2E tests..." -ForegroundColor Yellow
Write-Host ""

# Run E2E tests
dotnet test tests/Radio.Web.E2ETests/Radio.Web.E2ETests.csproj `
    --configuration Release `
    --logger "console;verbosity=normal" `
    --results-directory ./TestResults/E2E `
    --collect:"XPlat Code Coverage"

Write-Host ""
Write-Host "======================================" -ForegroundColor Green
Write-Host "E2E Tests Complete" -ForegroundColor Green
Write-Host "======================================" -ForegroundColor Green

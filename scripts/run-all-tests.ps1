#
# Run all test suites
# Usage: .\run-all-tests.ps1
#
# This runs:
# 1. bUnit tests (UI component tests)
# 2. xUnit tests (unit tests - may require hardware)
# 3. E2E tests (end-to-end browser tests)
#

$ErrorActionPreference = "Stop"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Radio Console - All Test Suites" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "This will run all test suites:" -ForegroundColor Yellow
Write-Host "  1. bUnit tests (Blazor component tests)"
Write-Host "  2. xUnit tests (unit tests with hardware)"
Write-Host "  3. E2E tests (browser-based tests)"
Write-Host ""
Write-Host "NOTE: xUnit tests may require real radio hardware!" -ForegroundColor Red
Write-Host ""
$response = Read-Host "Press Enter to continue or Ctrl+C to cancel"
Write-Host ""

# Build the solution first
Write-Host "Building solution..." -ForegroundColor Yellow
dotnet build --configuration Release

Write-Host ""
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Step 1/3: Running bUnit Tests" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""

dotnet test tests/Radio.Web.Tests/Radio.Web.Tests.csproj `
    --configuration Release `
    --logger "console;verbosity=normal" `
    --results-directory ./TestResults/bUnit `
    --collect:"XPlat Code Coverage"

Write-Host ""
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Step 2/3: Running xUnit Tests" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""

dotnet test `
    --configuration Release `
    --logger "console;verbosity=normal" `
    --results-directory ./TestResults/xUnit `
    --collect:"XPlat Code Coverage" `
    --filter "FullyQualifiedName!~Radio.Web.E2ETests&FullyQualifiedName!~Radio.Web.Tests"

Write-Host ""
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Step 3/3: Running E2E Tests" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""

dotnet test tests/Radio.Web.E2ETests/Radio.Web.E2ETests.csproj `
    --configuration Release `
    --logger "console;verbosity=normal" `
    --results-directory ./TestResults/E2E `
    --collect:"XPlat Code Coverage"

Write-Host ""
Write-Host "==========================================" -ForegroundColor Green
Write-Host "All Test Suites Complete" -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Test results saved to ./TestResults/" -ForegroundColor Yellow

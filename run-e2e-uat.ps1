#
# Run E2E UAT Tests
# Usage: .\run-e2e-uat.ps1 [-Phase <phase_number>] [-Interactive] [-NoShutdown]
#
param(
    [int]$Phase = 0,
    [switch]$Interactive = $false,
    [switch]$NoShutdown = $false
)

$ErrorActionPreference = "Stop"

Write-Host "======================================" -ForegroundColor Cyan
Write-Host "Radio Console - E2E UAT Tests" -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""

# Check if API is running
Write-Host "Checking if Radio API is running..." -ForegroundColor Yellow
$apiUrl = "http://localhost:5000/api/sources"
try {
    $response = Invoke-RestMethod -Uri $apiUrl -Method Get -TimeoutSec 5
    Write-Host "✓ API is running" -ForegroundColor Green
} catch {
    Write-Host "✗ API is not running!" -ForegroundColor Red
    Write-Host "Please start the Radio API first:" -ForegroundColor Red
    Write-Host "  cd src/Radio.API" -ForegroundColor Yellow
    Write-Host "  dotnet run" -ForegroundColor Yellow
    exit 1
}

# Check if Web UI is running
Write-Host "Checking if Radio Web UI is running..." -ForegroundColor Yellow
$webUrl = "http://localhost:5001"
try {
    $response = Invoke-RestMethod -Uri $webUrl -Method Get -TimeoutSec 5
    Write-Host "✓ Web UI is running" -ForegroundColor Green
} catch {
    Write-Host "✗ Web UI is not running!" -ForegroundColor Red
    Write-Host "Please start the Radio Web UI first:" -ForegroundColor Red
    Write-Host "  cd src/Radio.Web" -ForegroundColor Yellow
    Write-Host "  dotnet run" -ForegroundColor Yellow
    exit 1
}

Write-Host ""

# Build UAT tool
Write-Host "Building E2E UAT tool..." -ForegroundColor Yellow
dotnet build tools/Radio.Tools.AudioUAT --configuration Release

# Run tests
Write-Host ""
Write-Host "Running E2E UAT tests..." -ForegroundColor Yellow
Write-Host ""

$uatArgs = @("run", "--project", "tools/Radio.Tools.AudioUAT", "--configuration", "Release", "--")

if ($Phase -gt 0) {
    $uatArgs += "--phase", $Phase
}

if ($Interactive) {
    $uatArgs += "--interactive"
}

dotnet @uatArgs

$testExitCode = $LASTEXITCODE

Write-Host ""
Write-Host "======================================" -ForegroundColor Green
Write-Host "E2E UAT Tests Complete" -ForegroundColor Green
Write-Host "======================================" -ForegroundColor Green

# Optionally shutdown application
if (-not $NoShutdown) {
    Write-Host ""
    Write-Host "Shutting down application..." -ForegroundColor Yellow
    try {
        Invoke-RestMethod -Uri "http://localhost:5000/api/system/shutdown" -Method Post -TimeoutSec 5
        Write-Host "✓ Shutdown initiated" -ForegroundColor Green
    } catch {
        Write-Host "⚠ Could not shutdown application (may already be stopped)" -ForegroundColor Yellow
    }
}

exit $testExitCode

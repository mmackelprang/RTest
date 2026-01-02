# E2E Testing Guide for CLI Coding Assistants

## Overview

This guide helps CLI-based coding assistants use the E2E UAT tests to find and fix bugs in the Radio Console application (API and Web UI).

## Quick Start

### Prerequisites on Windows Development Machine

1. **Required Software**:
   - .NET 8 SDK installed
   - Git for Windows
   - PowerShell 5.1+

2. **Hardware Requirements**:
   - USB audio devices (for device management tests)
   - Network connectivity (for API communication)
   - Optional: Chromecast for casting tests

3. **Configuration**:
   - Application configured in `src/Radio.API/appsettings.json`
   - Database files in `./data/` directory

### Running E2E Tests

#### Step 1: Start the Application

Open **two PowerShell terminals**:

**Terminal 1 - Start API**:
```powershell
cd C:\path\to\RTest\src\Radio.API
dotnet run
```

Wait for: `Now listening on: http://localhost:5000`

**Terminal 2 - Start Web UI**:
```powershell
cd C:\path\to\RTest\src\Radio.Web
dotnet run
```

Wait for: `Now listening on: http://localhost:5001`

#### Step 2: Run E2E Tests

Open **Terminal 3** at repository root:

```powershell
# Run all Phase 15 tests (43 tests) with JSON output
.\scripts\run-e2e-uat.ps1 -Phase 15 -Output e2e-results.json -NoShutdown

# Run specific test category
.\scripts\run-e2e-uat.ps1 -Test "PLAY-001" -NoShutdown

# Run with interactive mode for debugging
.\scripts\run-e2e-uat.ps1 -Phase 15 -Interactive -NoShutdown
```

#### Step 3: Analyze Results

**Option A: Parse JSON output** (recommended for automation):
```powershell
# Get failed tests
Get-Content e2e-results.json | ConvertFrom-Json | 
  Select-Object -ExpandProperty results | 
  Where-Object { $_.status -eq "failed" }

# Count failures
$results = Get-Content e2e-results.json | ConvertFrom-Json
Write-Host "Passed: $($results.testRun.passed)"
Write-Host "Failed: $($results.testRun.failed)"
Write-Host "Total: $($results.testRun.totalTests)"
```

**Option B: Read console output** (for quick checks):
- Look for `✗ FAILED` lines
- Test IDs like `PLAY-001`, `QUEUE-002`, etc.
- Error messages explain what failed

## Understanding Test Results

### Test Status Codes

- **PASSED**: Test succeeded, functionality works as expected
- **FAILED**: Test failed, bug detected (review error message)
- **SKIPPED**: Test couldn't run (e.g., feature not supported by current source)

### JSON Output Structure

```json
{
  "testRun": {
    "id": "run-2026-01-02-16-00-00",
    "startTime": "2026-01-02T16:00:00Z",
    "endTime": "2026-01-02T16:05:30Z",
    "duration": "00:05:30",
    "totalTests": 43,
    "passed": 41,
    "failed": 2,
    "skipped": 0
  },
  "results": [
    {
      "testId": "PLAY-001",
      "phase": 15,
      "testName": "Start playback from stopped state",
      "status": "passed",
      "duration": "0.523s",
      "message": "Playback started successfully"
    },
    {
      "testId": "QUEUE-004",
      "phase": 15,
      "testName": "Remove track from queue by index",
      "status": "failed",
      "duration": "1.234s",
      "message": "API returned 500",
      "error": {
        "type": "HttpRequestException",
        "message": "Response status code does not indicate success: 500 (Internal Server Error)",
        "stackTrace": "..."
      }
    }
  ]
}
```

## Bug Detection Workflow

### 1. Identify Failing Tests

```powershell
# Run tests
.\scripts\run-e2e-uat.ps1 -Phase 15 -Output results.json -NoShutdown

# Extract failures
$results = Get-Content results.json | ConvertFrom-Json
$failures = $results.results | Where-Object { $_.status -eq "failed" }

foreach ($test in $failures) {
    Write-Host "FAILED: $($test.testId) - $($test.testName)"
    Write-Host "  Error: $($test.message)"
    Write-Host "  Duration: $($test.duration)"
    Write-Host ""
}
```

### 2. Locate Bug in Code

Use test ID to find the API endpoint being tested:

| Test ID Prefix | Component | Endpoints |
|---------------|-----------|-----------|
| **PLAY-***    | Audio Playback | `/api/audio/*` |
| **QUEUE-***   | Queue Management | `/api/queue/*` |
| **SRC-***     | Source Management | `/api/sources/*` |
| **DEV-***     | Device Management | `/api/devices/*` |
| **CFG-***     | Configuration | `/api/configuration/*` |
| **SYS-***     | System Management | `/api/system/*` |

**Example**: `QUEUE-004` fails → Check `src/Radio.API/Controllers/QueueController.cs`

### 3. Debug the Issue

**Review test implementation**:
```powershell
# Find test file
code tools/Radio.Tools.AudioUAT/Phases/Phase15/QueueManagementApiTests.cs
# Search for "QUEUE-004" to see what the test does
```

**Check API logs**:
- API console (Terminal 1) shows request/response details
- Look for exceptions, 500 errors, validation failures

**Reproduce manually**:
```powershell
# Use curl or Invoke-RestMethod to test endpoint
Invoke-RestMethod -Uri "http://localhost:5000/api/queue" -Method Get
```

### 4. Fix the Bug

1. Locate the controller method in `src/Radio.API/Controllers/`
2. Identify the issue (null reference, validation error, logic bug)
3. Apply fix
4. Restart API: `dotnet run` in Terminal 1
5. Re-run failing test: `.\scripts\run-e2e-uat.ps1 -Test "QUEUE-004" -NoShutdown`
6. Verify: Test should now PASS

### 5. Verify Full Suite

After fixing bugs, run all tests to ensure no regressions:

```powershell
.\scripts\run-e2e-uat.ps1 -Phase 15 -Output final-results.json

# Compare before/after
$before = Get-Content results.json | ConvertFrom-Json
$after = Get-Content final-results.json | ConvertFrom-Json

Write-Host "Before: $($before.testRun.failed) failures"
Write-Host "After: $($after.testRun.failed) failures"
Write-Host "Fixed: $($before.testRun.failed - $after.testRun.failed) bugs"
```

## Test Categories Reference

### Phase 15.1: Audio Playback and Control (9 tests)

Tests basic playback operations via `/api/audio` endpoints.

**Common Failures**:
- `PLAY-001` fails → Playback not starting (check audio engine initialization)
- `PLAY-004` fails → Volume not applying (check mixer configuration)
- `PLAY-006` fails → Mute state not persisting (check state management)

**Quick Fix Locations**:
- `src/Radio.API/Controllers/AudioController.cs`
- `src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowAudioEngine.cs`
- `src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowMasterMixer.cs`

### Phase 15.2: Queue Management (8 tests)

Tests queue operations via `/api/queue` endpoints.

**Common Failures**:
- `QUEUE-004` fails → Remove from queue not working (check index validation)
- `QUEUE-005` fails → Reorder not persisting (check move logic)
- `QUEUE-007` fails → Clear queue not emptying (check clear implementation)

**Quick Fix Locations**:
- `src/Radio.API/Controllers/QueueController.cs`
- Queue service implementation (check DI registration)

### Phase 15.3: Source Management (6 tests)

Tests source switching via `/api/sources` endpoints.

**Common Failures**:
- `SRC-003` fails → Source switch not completing (check cleanup/initialization)
- `SRC-004` fails → Capabilities not reported correctly (check source interface)

**Quick Fix Locations**:
- `src/Radio.API/Controllers/SourcesController.cs`
- Source implementations in `src/Radio.Infrastructure/Audio/Sources/`

### Phase 15.4: Device Management (7 tests)

Tests device enumeration and selection via `/api/devices` endpoints.

**Common Failures**:
- `DEV-001` fails → No devices listed (check device manager initialization)
- `DEV-004` fails → Device switch not working (check device selection logic)
- `DEV-007` fails → Invalid device properties (check device info mapping)

**Quick Fix Locations**:
- `src/Radio.API/Controllers/DevicesController.cs`
- `src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowDeviceManager.cs`

### Phase 15.5: Configuration Management (8 tests)

Tests configuration CRUD via `/api/configuration` endpoints.

**Common Failures**:
- `CFG-002` fails → Section not found (check configuration store)
- `CFG-006` fails → Update not persisting (check save logic)

**Quick Fix Locations**:
- `src/Radio.API/Controllers/ConfigurationController.cs`
- `src/Radio.Infrastructure/Configuration/`

### Phase 15.6: System Management (5 tests)

Tests system monitoring via `/api/system` endpoints.

**Common Failures**:
- `SYS-001` fails → Stats not available (check system info gathering)
- `SYS-002` fails → Logs empty (check logging configuration)

**Quick Fix Locations**:
- `src/Radio.API/Controllers/SystemController.cs`
- Logging configuration in `src/Radio.API/Program.cs`

## Advanced Usage

### Running Subset of Tests

```powershell
# Run only playback tests
.\scripts\run-e2e-uat.ps1 -Test "PLAY-001" -NoShutdown
.\scripts\run-e2e-uat.ps1 -Test "PLAY-002" -NoShutdown
# ... run each test individually

# Run only device tests
.\scripts\run-e2e-uat.ps1 -Test "DEV-001" -NoShutdown
# ...
```

### Continuous Testing During Development

```powershell
# Keep API and Web UI running in background
# In Terminal 3, run tests repeatedly:

while ($true) {
    .\scripts\run-e2e-uat.ps1 -Phase 15 -Output results.json -NoShutdown
    $results = Get-Content results.json | ConvertFrom-Json
    
    if ($results.testRun.failed -eq 0) {
        Write-Host "✓ All tests passed!" -ForegroundColor Green
        break
    } else {
        Write-Host "✗ $($results.testRun.failed) tests failed" -ForegroundColor Red
        Start-Sleep -Seconds 5
    }
}
```

### Test-Driven Bug Fixing

1. **Identify failing test**: `PLAY-004` (Set volume)
2. **Write test case** to reproduce: Already exists!
3. **Run test**: `.\scripts\run-e2e-uat.ps1 -Test "PLAY-004"`
4. **Fix code** in `AudioController.cs`
5. **Re-run test** until it passes
6. **Run full suite** to ensure no regressions

## Troubleshooting

### Tests Can't Connect to API

**Symptom**: `API is not running!` error

**Solution**:
1. Check Terminal 1 - API should show "Now listening on: http://localhost:5000"
2. Test manually: `curl http://localhost:5000/api/sources`
3. Check firewall: Allow port 5000
4. Check appsettings.json for correct URL

### Tests Hang or Timeout

**Symptom**: Tests don't complete, hang indefinitely

**Solution**:
1. Check API console for exceptions
2. Reduce test timeout in RadioApiClient.cs
3. Check for deadlocks in async code
4. Review API logs for stuck operations

### Tests Pass but Application Doesn't Work

**Symptom**: Tests pass but manual testing shows bugs

**Solution**:
1. Tests may not cover all edge cases
2. Add more specific tests for the scenario
3. Check Web UI behavior (Phase 22 tests needed)
4. Verify with real hardware/devices

### Many Tests Fail at Once

**Symptom**: Most/all tests fail with same error

**Solution**:
1. Likely infrastructure issue (database, configuration)
2. Check `./data/` directory exists and is writable
3. Check SQLite database files aren't corrupted
4. Restart API with clean state

## Exit Codes

The test scripts return specific exit codes:

| Code | Meaning | Action |
|------|---------|--------|
| `0` | All tests passed | Continue development |
| `1` | One or more tests failed | Review failures, fix bugs |
| `2` | Application not running | Start API and Web UI |
| `3` | Configuration/build error | Check build output |

## Summary

The E2E tests provide **automated validation** of all core API functionality. By running tests after code changes, you can:

1. **Detect bugs immediately** - Failed tests indicate broken functionality
2. **Prevent regressions** - Ensure fixes don't break other features
3. **Validate fixes** - Confirm bugs are resolved before committing
4. **Document API behavior** - Tests serve as executable specifications

**Current Coverage**: 43 tests across 6 API categories (100% of Phase 15)

**Recommended Workflow**:
1. Make code changes
2. Run E2E tests: `.\scripts\run-e2e-uat.ps1 -Phase 15 -Output results.json`
3. Review failures: Parse `results.json`
4. Fix bugs: Update controllers/services
5. Re-run tests: Verify fixes
6. Commit: Only when all tests pass

This ensures high code quality and catches bugs before they reach production.

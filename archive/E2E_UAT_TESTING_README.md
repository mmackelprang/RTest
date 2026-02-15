# E2E UAT Testing Scripts

This directory contains scripts for running End-to-End User Acceptance Testing (UAT) on the Radio Console application.

## Overview

The E2E UAT tests are designed to validate the full Radio Console application (API + Web UI) in a UAT environment. These tests connect to a running instance of the application and verify functionality across all components.

**Important**: These tests are NOT part of the CI/CD pipeline. They must be run manually on real hardware (e.g., Raspberry Pi 5) with actual devices and service authorizations (Spotify, TTS APIs, Chromecast, etc.).

## Quick Links

- **[CLI Assistant Guide](./CLI_ASSISTANT_E2E_GUIDE.md)** - Comprehensive guide for CLI coding assistants on using E2E tests to find and fix bugs
- **[E2E Testing Plan](./E2E_TESTING.md)** - Complete testing strategy and phase breakdown
- **[Implementation Summary](./E2E_IMPLEMENTATION_SUMMARY.md)** - Current progress and technical details

## Current Status

**Phase 15 Complete**: 43 API tests ready for use ✅

The E2E test suite can now help identify bugs in:
- Audio playback control (9 tests)
- Queue management (8 tests)
- Source switching (6 tests)
- Device management (7 tests)
- Configuration CRUD (8 tests)
- System monitoring (5 tests)

## Test Plan

For comprehensive details on the E2E UAT testing strategy, see [`/E2E_TESTING.md`](./E2E_TESTING.md).

## Running the Tests

### Prerequisites

1. **Start the Application**:
   - Both the API and Web UI must be running before executing the tests
   - The tests will check for connectivity before starting

2. **Required Services**:
   - Audio devices (USB, local, Chromecast)
   - Network connectivity (for Chromecast, Spotify)
   - Service authorizations (Spotify tokens, TTS API keys)

### Windows (PowerShell)

```powershell
# Start the application
# Terminal 1:
cd src/Radio.API
dotnet run

# Terminal 2:
cd src/Radio.Web
dotnet run

# Run E2E tests
# Terminal 3:
.\run-e2e-uat.ps1                          # Run all tests (non-interactive)
.\run-e2e-uat.ps1 -Phase 15                # Run specific phase only
.\run-e2e-uat.ps1 -Interactive             # Run with interactive prompts
.\run-e2e-uat.ps1 -Output results.json     # JSON output for CLI agents
.\run-e2e-uat.ps1 -NoShutdown              # Keep app running after tests
```

### Linux / Raspberry Pi (Bash)

```bash
# Start the application
# Terminal 1:
cd src/Radio.API
dotnet run

# Terminal 2:
cd src/Radio.Web
dotnet run

# Run E2E tests
# Terminal 3:
./run-e2e-uat.sh                           # Run all tests (non-interactive)
./run-e2e-uat.sh --phase 15                # Run specific phase only
./run-e2e-uat.sh --interactive             # Run with interactive prompts
./run-e2e-uat.sh --output results.json     # JSON output for CLI agents
./run-e2e-uat.sh --no-shutdown             # Keep app running after tests
```

### CLI Coding Agent Mode

For automated testing by CLI coding agents:

```bash
# Run tests with JSON output for machine parsing
./run-e2e-uat.sh --output e2e-results.json

# Check exit code for success/failure
if [ $? -eq 0 ]; then
  echo "All tests passed"
else
  echo "Tests failed - see results.json for details"
fi
```

**JSON Output Structure:**
```json
{
  "testRun": {
    "id": "run-2026-01-02-14-30-00",
    "startTime": "2026-01-02T14:30:00Z",
    "endTime": "2026-01-02T14:45:00Z",
    "duration": "00:15:00",
    "totalTests": 150,
    "passed": 145,
    "failed": 5,
    "skipped": 0
  },
  "results": [
    {
      "testId": "PLAY-001",
      "phase": 15,
      "name": "Start playback from stopped state",
      "status": "passed",
      "duration": "0.523s",
      "message": "Playback started successfully"
    },
    {
      "testId": "PLAY-002",
      "phase": 15,
      "name": "Pause and resume playback",
      "status": "failed",
      "duration": "1.234s",
      "message": "API returned 500",
      "error": {
        "type": "HttpRequestException",
        "message": "Response status code does not indicate success: 500",
        "stackTrace": "..."
      }
    }
  ]
}
```

## Test Phases

The E2E tests are organized into phases for incremental testing:

| Phase | Name | Test Count | Description |
|-------|------|-----------|-------------|
| 15 | Comprehensive API Tests | 44 | API endpoints (audio, queue, sources, devices, config, system) |
| 16 | Visualization Tests | 15 | Real-time visualization and SignalR integration |
| 17 | Casting Tests | 20 | Device selection, Chromecast, HTTP streaming |
| 18 | Fingerprinting Tests | 17 | Audio identification and metadata enrichment |
| 19 | System Integration Tests | 18 | Cross-component workflows |
| 20 | Configuration Tests | 17 | Configuration CRUD, secrets, backup/restore |
| 21 | Performance Tests | 18 | API, audio latency, UI responsiveness |
| 22 | Web UI Tests | 30+ | Playwright-based UI automation |

**Total**: ~180+ E2E tests

## Script Options

### PowerShell (`run-e2e-uat.ps1`)

```powershell
-Phase <number>       # Run tests for a specific phase (15-22)
-Interactive          # Enable interactive mode with user prompts
-Output <file>        # Write results to specified file (e.g., results.json)
-NoShutdown           # Keep application running after tests complete
```

**Note**: When `-Output` is specified, the UAT tool will generate JSON-formatted results to the specified file.

### Bash (`run-e2e-uat.sh`)

```bash
--phase <number>      # Run tests for a specific phase (15-22)
--interactive         # Enable interactive mode with user prompts
--output <file>       # Write results to specified file (e.g., results.json)
--no-shutdown         # Keep application running after tests complete
```

**Note**: When `--output` is specified, the UAT tool will generate JSON-formatted results to the specified file.

### Exit Codes

The scripts return specific exit codes for automation:

| Code | Meaning |
|------|---------|
| `0` | All tests passed successfully |
| `1` | One or more tests failed |
| `2` | Application not running (API or Web UI unavailable) |
| `3` | Configuration or build error |

## What the Scripts Do

1. **Check Application Status**:
   - Verify API is running at `http://localhost:5000`
   - Verify Web UI is running at `http://localhost:5001`
   - Exit with code 2 if either is not available

2. **Build UAT Tool**:
   - Compile the `Radio.Tools.AudioUAT` project in Release configuration
   - Exit with code 3 if build fails

3. **Execute Tests**:
   - Run tests for the specified phase (or all phases if no phase specified)
   - Display test results in real-time with clear status indicators
   - Default to non-interactive mode (no prompts)
   - Generate test reports (console and/or JSON)

4. **Report Results**:
   - Display summary: total tests, passed, failed, skipped
   - Show execution time and performance metrics
   - Write JSON output if `--output` specified

5. **Shutdown (Optional)**:
   - By default, gracefully shutdown the application via `POST /api/system/shutdown`
   - Use `-NoShutdown` or `--no-shutdown` to keep the application running

6. **Return Exit Code**:
   - `0` if all tests passed
   - `1` if any test failed
   - `2` if application not running
   - `3` if build/setup error

## Configuration

The E2E tests use the **exact same configuration** as the running application. This eliminates configuration differences between the tests and the app.

**Configuration Files Used**:
- `/src/Radio.API/appsettings.json` - API configuration
- `/src/Radio.Web/appsettings.json` - Web UI configuration
- `./data/` - Database files (shared)

**No Separate Test Configuration**: Tests do NOT use test-specific configuration files to ensure consistency.

## Test Results

Test results are displayed in real-time to the console and saved to:
- `tools/Radio.Tools.AudioUAT/TestResults/` (JSON reports)

## Troubleshooting

### API is not running

**Error**: `✗ API is not running!`

**Solution**: Start the Radio API:
```bash
cd src/Radio.API
dotnet run
```

### Web UI is not running

**Error**: `✗ Web UI is not running!`

**Solution**: Start the Radio Web UI:
```bash
cd src/Radio.Web
dotnet run
```

### Tests fail due to missing devices

**Issue**: Tests fail because required hardware is not available (e.g., USB audio devices, Chromecast)

**Solution**: 
- Run tests on hardware with the required devices attached
- Skip phases that require unavailable hardware using the `-Phase` or `--phase` option

### Shutdown fails

**Warning**: `⚠ Could not shutdown application (may already be stopped)`

**Cause**: The application may have already been stopped manually or crashed during testing

**Solution**: This is typically not an issue. The warning is informational only.

## Example Usage

### Run All Tests on Development Machine (Windows)

```powershell
# Terminal 1: Start API
cd src/Radio.API
dotnet run

# Terminal 2: Start Web UI
cd src/Radio.Web
dotnet run

# Terminal 3: Run all E2E tests
.\run-e2e-uat.ps1
```

### Run Specific Phase on Raspberry Pi

```bash
# Start application (assuming it's already configured as a service)
sudo systemctl start radioconsole

# Run only API tests (Phase 15)
./run-e2e-uat.sh --phase 15 --no-shutdown

# Run only Visualization tests (Phase 16)
./run-e2e-uat.sh --phase 16 --no-shutdown
```

### Interactive Testing Session

```bash
# Run tests interactively with prompts after each test
./run-e2e-uat.sh --interactive --no-shutdown
```

## CI/CD Integration

**Note**: These E2E tests are **NOT** intended for CI/CD automation because they:
- Require real hardware (audio devices, Chromecast, etc.)
- Need actual service authorizations (Spotify, TTS API keys)
- May involve manual verification steps (e.g., "Did you hear audio?")
- Must run on physical Raspberry Pi hardware

**Recommended Usage**:
- Manual execution during UAT sessions
- Pre-release validation on staging hardware
- Local development verification
- Hardware integration testing
- **CLI coding agent validation** - Automated testing with JSON output

For automated CI/CD testing, use:
- `run-unit-tests-hardware.sh` - Hardware-specific unit tests
- `run-bunit-tests.sh` - Blazor component tests
- `run-e2e-tests.sh` - Basic E2E UI tests (Playwright, no hardware)

## CLI Coding Agent Support

The E2E UAT tests are designed to be easily executed and analyzed by CLI coding agents:

### Non-Interactive Mode (Default)

By default, the tests run in non-interactive mode:
- No user prompts or confirmations required
- Tests execute automatically from start to finish
- Real-time progress output to console
- Clear status indicators for each test
- Exit codes indicate overall success/failure

```bash
# Simple execution - no prompts
./run-e2e-uat.sh
```

### JSON Output for Machine Parsing

Use the `--output` flag to generate structured test results:

```bash
./run-e2e-uat.sh --output e2e-results.json
```

**JSON Structure:**
```json
{
  "testRun": {
    "id": "run-2026-01-02-14-30-00",
    "startTime": "2026-01-02T14:30:00Z",
    "endTime": "2026-01-02T14:45:00Z",
    "duration": "00:15:00",
    "totalTests": 150,
    "passed": 145,
    "failed": 5,
    "skipped": 0
  },
  "results": [
    {
      "testId": "PLAY-001",
      "phase": 15,
      "testName": "Start playback",
      "status": "passed",
      "duration": "0.523s",
      "message": "Playback started successfully"
    }
  ]
}
```

### Real-Time Console Output

Even in non-interactive mode, tests provide clear real-time feedback:

```
[Phase 15] Running SYS-001: Get system stats...
✓ PASSED (0.234s) - System stats retrieved successfully

[Phase 15] Running SYS-003: Get metrics data...
✗ FAILED (1.423s) - API returned 404 Not Found
```

### Exit Code Automation

Scripts return specific exit codes for automation workflows:

```bash
./run-e2e-uat.sh --json --output results.json
EXIT_CODE=$?

if [ $EXIT_CODE -eq 0 ]; then
  echo "✓ All tests passed"
elif [ $EXIT_CODE -eq 1 ]; then
  echo "✗ Tests failed - see results.json"
  jq -r '.results[] | select(.status=="failed")' results.json
fi
```

### Best Practices for CLI Agents

1. **Always use `--output`** for structured results parsing
2. **Check exit codes** before parsing results (exit 2/3 means no results)
3. **Parse failures first** to identify critical issues
4. **Analyze performance trends** by comparing execution times across runs
5. **Monitor API endpoint health** by tracking failure rates

## Additional Resources

- [Full E2E Testing Plan](./E2E_TESTING.md) - Comprehensive test strategy and implementation details
- [Project Plan](./PLAN.md) - Overall project development plan
- [UAT Testing Documentation](./tools/Radio.Tools.AudioUAT/docs/) - UAT tool documentation

## Support

For questions or issues with E2E UAT testing:
1. Review the [E2E Testing Plan](./E2E_TESTING.md)
2. Check existing test implementations in `tools/Radio.Tools.AudioUAT/Phases/`
3. Review test results in `tools/Radio.Tools.AudioUAT/TestResults/`
4. File an issue on GitHub with test logs attached

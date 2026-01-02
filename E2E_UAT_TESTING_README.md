# E2E UAT Testing Scripts

This directory contains scripts for running End-to-End User Acceptance Testing (UAT) on the Radio Console application.

## Overview

The E2E UAT tests are designed to validate the full Radio Console application (API + Web UI) in a UAT environment. These tests connect to a running instance of the application and verify functionality across all components.

**Important**: These tests are NOT part of the CI/CD pipeline. They must be run manually on real hardware (e.g., Raspberry Pi 5) with actual devices and service authorizations (Spotify, TTS APIs, Chromecast, etc.).

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
.\run-e2e-uat.ps1                  # Run all tests
.\run-e2e-uat.ps1 -Phase 15        # Run specific phase only
.\run-e2e-uat.ps1 -Interactive     # Run with interactive prompts
.\run-e2e-uat.ps1 -NoShutdown      # Keep app running after tests
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
./run-e2e-uat.sh                   # Run all tests
./run-e2e-uat.sh --phase 15        # Run specific phase only
./run-e2e-uat.sh --interactive     # Run with interactive prompts
./run-e2e-uat.sh --no-shutdown     # Keep app running after tests
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
-NoShutdown           # Keep application running after tests complete
```

### Bash (`run-e2e-uat.sh`)

```bash
--phase <number>      # Run tests for a specific phase (15-22)
--interactive         # Enable interactive mode with user prompts
--no-shutdown         # Keep application running after tests complete
```

## What the Scripts Do

1. **Check Application Status**:
   - Verify API is running at `http://localhost:5000`
   - Verify Web UI is running at `http://localhost:5001`
   - Exit with error message if either is not available

2. **Build UAT Tool**:
   - Compile the `Radio.Tools.AudioUAT` project in Release configuration

3. **Execute Tests**:
   - Run tests for the specified phase (or all phases if no phase specified)
   - Display test results in real-time
   - Generate test reports

4. **Shutdown (Optional)**:
   - By default, gracefully shutdown the application via `POST /api/system/shutdown`
   - Use `-NoShutdown` or `--no-shutdown` to keep the application running

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

For automated CI/CD testing, use:
- `run-unit-tests-hardware.sh` - Hardware-specific unit tests
- `run-bunit-tests.sh` - Blazor component tests
- `run-e2e-tests.sh` - Basic E2E UI tests (Playwright, no hardware)

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

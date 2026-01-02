# Radio Console E2E UAT Testing Plan

## Implementation Status

**Status**: Phase 15 complete (43/44 tests complete - 98%)

**Current Progress**:
- ✅ System shutdown endpoint exists (`POST /api/system/shutdown`)
- ✅ Test runner scripts exist (`run-e2e-uat.ps1`, `run-e2e-uat.sh`)  
- ✅ RadioApiClient extended with all required methods and consistent error handling
- ✅ Phase 15.1: Audio Playback and Control - 9 tests complete
- ✅ Phase 15.2: Queue Management - 8 tests complete
- ✅ Phase 15.3: Source Management - 6 tests complete
- ✅ Phase 15.4: Device Management - 7 tests complete
- ✅ Phase 15.5: Configuration Management - 8 tests complete
- ✅ Phase 15.6: System Management - 5 tests complete
- ⏳ Phases 16-22: Other E2E test categories - TO DO

**Next Steps**:
1. ✅ Complete Phase 15.2 (Queue Management - 8 tests)
2. ✅ Complete Phase 15.3 (Source Management - 6 tests)
3. ✅ Complete Phase 15.4 (Device Management - 7 tests)
4. ✅ Complete Phase 15.5 (Configuration Management - 8 tests)
5. ✅ Complete Phase 15.6 (System Management - 5 tests)
6. Implement Phases 16-21 (Visualization, Casting, Fingerprinting, Integration, Configuration, Performance)
7. Implement Phase 22 (Web UI E2E tests with Playwright)

---

## Overview

This document outlines a comprehensive End-to-End (E2E) testing strategy for the Radio Console application. The goal is to enable rapid User Acceptance Testing (UAT) during development and deployment, allowing quick identification of issues in both the UI and API layers.

### Purpose

- Enable rapid iteration during UAT testing of the full application
- Quickly identify problems in UI, API, and system integration
- Provide confidence that all system components work together correctly
- Support testing on real hardware (Raspberry Pi 5) with actual devices and authorizations

### Scope

- **API E2E Tests**: REST endpoints for audio sources, playback, configuration, etc.
- **Web UI E2E Tests**: Playwright-based browser automation for UI workflows
- **Visualization Tests**: Real-time audio visualization and SignalR integration
- **Device/Casting Tests**: Output device selection, Chromecast integration
- **Fingerprinting Tests**: Audio identification and metadata enrichment
- **System Integration Tests**: Cross-component workflows (e.g., source switching, queue management)
- **Configuration Tests**: Settings persistence and restoration
- **Performance Tests**: Load testing and responsiveness validation

### Test Infrastructure Location

All E2E tests will be consolidated into the existing `tools/Radio.Tools.AudioUAT` project for consistency and to leverage the existing test infrastructure. However, the tests will be organized into more manageable, focused files to avoid creating overly large test files.

**Rationale for Using Existing AudioUAT Tool:**
- Already has RadioApiClient for API communication
- Existing test runner infrastructure and reporting
- Phase-based organization aligns well with E2E test categories
- Console UI utilities for interactive test execution
- Configuration management already in place

**Addressing Large File Concerns:**
- Break tests into smaller, focused files by feature area
- Use subfolder organization within each phase
- Limit each test file to 200-300 lines maximum
- Group related tests into logical test classes

### Configuration Sharing Strategy

**Critical Requirement**: E2E tests must use the **exact same configuration** as the running application to eliminate configuration differences.

**Implementation Approach:**
1. **Shared Configuration Files**: Both the application and E2E tests will read from the same `appsettings.json` files
2. **Configuration Path**: Tests will use the same database and configuration paths as the application
3. **Environment Variables**: Support environment-specific configuration overrides
4. **No Test-Specific Config**: Avoid creating separate test configurations that could introduce discrepancies

**Configuration Files Used:**
- `/src/Radio.API/appsettings.json` - Primary API configuration
- `/src/Radio.Web/appsettings.json` - Web UI configuration
- Database files in `./data/` directory (shared between app and tests)
- Secrets stored in Configuration Database (shared)

**Test Execution Model:**
- Tests connect to a **running instance** of the application
- Tests do NOT start their own instance (to ensure exact configuration match)
- Tests use the RadioApiClient to communicate with the running API
- Web UI tests use Playwright to automate the running web application

### Test Execution Scripts

E2E tests will be easy to run on both Windows and Linux via simple scripts:

**Windows (PowerShell):**
- `run-e2e-uat.ps1` - Run all E2E UAT tests (non-interactive by default)
- `run-e2e-uat-interactive.ps1` - Run E2E tests in interactive mode with prompts

**Linux/Raspberry Pi (Bash):**
- `run-e2e-uat.sh` - Run all E2E UAT tests (non-interactive by default)
- `run-e2e-uat-interactive.sh` - Run E2E tests in interactive mode

**Script Responsibilities:**
1. Check that the application is running (API and Web UI)
2. Run E2E tests in the desired phase(s)
3. Generate test reports (JSON for machine parsing, human-readable for console)
4. Provide real-time progress updates with clear status indicators
5. Optionally shut down the application after tests complete

**CLI Coding Agent Support:**
- **Non-interactive mode is the default** - tests run automatically without prompts
- **Structured JSON output** via `--output` flag for machine parsing
- **Real-time progress logging** with clear test status (PASS/FAIL/SKIP)
- **Detailed failure information** including error messages, stack traces, and context
- **Metrics and timing data** for performance analysis
- **Exit codes** indicate success (0) or failure (non-zero) for automation

### CLI Coding Agent Support

**Design for Automation**: These E2E tests are designed to be easily run by CLI coding agents and automation scripts:

**Non-Interactive Mode (Default):**
- No user prompts or manual inputs required
- Tests run automatically from start to finish
- Clear console output with real-time status updates
- Structured exit codes for success/failure detection

**JSON Output Mode:**
```bash
./run-e2e-uat.sh --output results.json
```
Produces machine-readable JSON with:
- Test results (pass/fail/skip) for each test
- Execution times and performance metrics
- Detailed error messages and stack traces
- API response samples and debugging context
- Summary statistics (total, passed, failed, skipped)

**Log Visibility:**
- Real-time test execution progress: `[Phase 15] Running SYS-001: Get system stats...`
- Clear status indicators: `✓ PASSED`, `✗ FAILED`, `⊘ SKIPPED`
- Failure details logged immediately when tests fail
- API request/response logging for debugging
- System metrics captured during test execution

**Exit Codes:**
- `0` - All tests passed successfully
- `1` - One or more tests failed
- `2` - Application not running (API or Web UI)
- `3` - Configuration or setup error

### CI/CD Considerations

**Important**: These E2E tests are **NOT** part of the normal CI/CD flow because they:
- Require real hardware (audio devices, Chromecast, etc.)
- Need actual service authorizations (Spotify, TTS API keys)
- Require running on a physical Raspberry Pi with attached devices
- May involve manual verification steps for certain audio tests

**Usage Models:**
- **Manual UAT sessions** - Run interactively during development
- **Automated UAT validation** - Run via CLI scripts before releases
- **CLI coding agent testing** - Run non-interactively with JSON output
- **Staging/production validation** - Run on real hardware with full configuration
- **Local development** - Quick verification of changes

---

## Phase 15: Enhanced API UAT Tests

**Purpose**: Extend existing API tests to cover all REST endpoints comprehensively.

**Location**: `tools/Radio.Tools.AudioUAT/Phases/Phase15-ApiComprehensive/`

**Existing Coverage** (Phases 12-14):
- Phase 12: FilePlayer API Tests (11 tests)
- Phase 13: Radio API Tests (15 tests)
- Phase 14: Spotify API Tests (9 tests)

**New Coverage Needed:**

### Phase 15: Comprehensive API Tests

#### 15.1 Audio Playback and Control (10 tests)
**File**: `Phase15/AudioPlaybackApiTests.cs`

Tests:
1. `PLAY-001`: Start playback from stopped state
2. `PLAY-002`: Pause and resume playback
3. `PLAY-003`: Stop playback and verify cleanup
4. `PLAY-004`: Set master volume (0-100 range)
5. `PLAY-005`: Set balance (-100 to +100 range)
6. `PLAY-006`: Mute and unmute audio
7. `PLAY-007`: Get current playback state
8. `PLAY-008`: Verify playback position updates in real-time
9. `PLAY-009`: Skip to next track (when supported)
10. `PLAY-010`: Skip to previous track (when supported)

**API Endpoints Tested:**
- `GET /api/audio/state`
- `POST /api/audio/play`
- `POST /api/audio/pause`
- `POST /api/audio/stop`
- `POST /api/audio/volume/{value}`
- `POST /api/audio/balance/{value}`
- `POST /api/audio/mute`
- `POST /api/audio/next`
- `POST /api/audio/previous`

#### 15.2 Queue Management (8 tests)
**File**: `Phase15/QueueManagementApiTests.cs`

Tests:
1. `QUEUE-001`: Get current queue
2. `QUEUE-002`: Add track to end of queue
3. `QUEUE-003`: Add track at specific position
4. `QUEUE-004`: Remove track from queue by index
5. `QUEUE-005`: Move track within queue (reorder)
6. `QUEUE-006`: Jump to specific queue index
7. `QUEUE-007`: Clear entire queue
8. `QUEUE-008`: Verify queue updates trigger SignalR events

**API Endpoints Tested:**
- `GET /api/queue`
- `POST /api/queue`
- `DELETE /api/queue/{index}`
- `POST /api/queue/move`
- `POST /api/queue/jump/{index}`
- `DELETE /api/queue/clear`

#### 15.3 Source Management (6 tests)
**File**: `Phase15/SourceManagementApiTests.cs`

Tests:
1. `SRC-001`: List all available sources
2. `SRC-002`: Get current primary source
3. `SRC-003`: Switch between sources (FilePlayer → Radio → Spotify)
4. `SRC-004`: Verify source capabilities (CanSeek, CanQueue, etc.)
5. `SRC-005`: Get active event sources
6. `SRC-006`: Verify source state after switch

**API Endpoints Tested:**
- `GET /api/sources`
- `GET /api/sources/primary`
- `POST /api/sources`
- `GET /api/sources/active`

#### 15.4 Device Management (7 tests)
**File**: `Phase15/DeviceManagementApiTests.cs`

Tests:
1. `DEV-001`: List output devices
2. `DEV-002`: List input devices
3. `DEV-003`: Get default output device
4. `DEV-004`: Set output device
5. `DEV-005`: Verify USB device conflict detection
6. `DEV-006`: Reserve and release USB port
7. `DEV-007`: Hot-plug device detection

**API Endpoints Tested:**
- `GET /api/devices/output`
- `GET /api/devices/input`
- `GET /api/devices/output/default`
- `POST /api/devices/output/{deviceId}`

#### 15.5 Configuration Management (8 tests)
**File**: `Phase15/ConfigurationApiTests.cs`

Tests:
1. `CFG-001`: Get all configuration entries
2. `CFG-002`: Get specific configuration value
3. `CFG-003`: Set configuration value
4. `CFG-004`: Delete configuration entry
5. `CFG-005`: Get configuration section (e.g., "Audio:")
6. `CFG-006`: Create and resolve secret tag
7. `CFG-007`: Backup configuration
8. `CFG-008`: Restore configuration from backup

**API Endpoints Tested:**
- `GET /api/configuration`
- `GET /api/configuration/{storeId}`
- `GET /api/configuration/{storeId}/{key}`
- `POST /api/configuration/{storeId}/{key}`
- `DELETE /api/configuration/{storeId}/{key}`
- `POST /api/configuration/secrets`
- `POST /api/configuration/backup`
- `POST /api/configuration/restore`

#### 15.6 System Management (5 tests)
**File**: `Phase15/SystemManagementApiTests.cs`

Tests:
1. `SYS-001`: Get system stats (CPU, RAM, uptime)
2. `SYS-002`: Get application logs (filtered by level)
3. `SYS-003`: Get metrics data (if enabled)
4. `SYS-004`: Shutdown API endpoint
5. `SYS-005`: Health check endpoint

**API Endpoints Tested:**
- `GET /api/system/stats`
- `GET /api/system/logs`
- `GET /api/metrics`
- `POST /api/system/shutdown`
- `GET /api/health`

**New Endpoints Needed:**
- `POST /api/system/shutdown` (extend existing implementation)
- `GET /api/system/stats` (if not exists)
- `GET /api/system/logs` (if not exists)

---

## Phase 22: Web UI E2E Tests (Playwright)

**Purpose**: Automate critical user workflows in the Blazor Web UI using Playwright.

**Location**: `tests/Radio.Web.E2ETests/` (existing project, add new tests)

**Test Organization**: Create new test files for each feature area.

### 2.1 Navigation and Layout (5 tests)
**File**: `NavigationE2ETests.cs`

Tests:
1. `NAV-001`: Main navigation bar displays correctly
2. `NAV-002`: Navigate between all pages (Home, Queue, Spotify, Radio, Config, Visualizer)
3. `NAV-003`: Verify page transitions are smooth
4. `NAV-004`: System stats update in navigation bar
5. `NAV-005`: Date/time display updates

**UI Elements Tested:**
- Navigation bar icons and buttons
- Page routing and transitions
- Real-time stat updates in nav bar

### 2.2 Home/Now Playing Page (8 tests)
**File**: `NowPlayingE2ETests.cs`

Tests:
1. `NP-001`: Now Playing card displays track info correctly
2. `NP-002`: Play button starts playback
3. `NP-003`: Pause button pauses playback
4. `NP-004`: Next/Previous buttons work
5. `NP-005`: Shuffle toggle works
6. `NP-006`: Repeat mode cycles correctly
7. `NP-007`: Progress bar updates in real-time
8. `NP-008`: Seek by clicking progress bar

**UI Elements Tested:**
- Now Playing card (album art, title, artist, album)
- Transport controls (play, pause, next, previous)
- Shuffle and repeat toggles
- Progress bar and seek functionality

### 2.3 Volume and Balance Controls (4 tests)
**File**: `VolumeControlsE2ETests.cs`

Tests:
1. `VOL-001`: Volume slider adjusts master volume
2. `VOL-002`: Balance slider adjusts left/right balance
3. `VOL-003`: Mute button toggles mute state
4. `VOL-004`: Volume controls update when changed via API

**UI Elements Tested:**
- Volume slider (MudSlider)
- Balance slider (MudSlider)
- Mute button

### 2.4 Queue Page (6 tests)
**File**: `QueuePageE2ETests.cs`

Tests:
1. `QUE-001`: Queue displays all tracks
2. `QUE-002`: Click track to jump to it
3. `QUE-003`: Delete track from queue
4. `QUE-004`: Drag-and-drop to reorder queue (if supported)
5. `QUE-005`: Clear all queue items
6. `QUE-006`: Current playing track highlighted

**UI Elements Tested:**
- Queue grid/list
- Track rows (title, artist, album, duration)
- Delete buttons
- Drag-and-drop reordering
- Clear All button

---

## Phase 16: Visualization E2E Tests

**Purpose**: Test real-time audio visualization components and SignalR integration.

**Location**: `tools/Radio.Tools.AudioUAT/Phases/Phase16-Visualization/`

### 3.1 Visualizer Page UI Tests (6 tests)
**File**: `Phase16/VisualizerPageTests.cs`

Tests:
1. `VIS-001`: Navigate to Visualizer page
2. `VIS-002`: Switch between visualization modes (VU Meter, Waveform, Spectrum)
3. `VIS-003`: Canvas elements render correctly
4. `VIS-004`: Visualizations update when audio is playing
5. `VIS-005`: Visualizations stop when audio stops
6. `VIS-006`: No console errors during visualization

---

## Phase 17: Casting / Device Selection E2E Tests

**Purpose**: Test output device selection, Chromecast integration, and device switching.

**Location**: `tools/Radio.Tools.AudioUAT/Phases/Phase17-Casting/`

### 4.1 Local Audio Output Tests (5 tests)
**File**: `Phase17/LocalAudioOutputTests.cs`

Tests:
1. `LOCAL-001`: Initialize local audio output
2. `LOCAL-002`: Start playback to local device
3. `LOCAL-003`: Adjust volume on local output
4. `LOCAL-004`: Switch between available local devices
5. `LOCAL-005`: Verify audio output to default device

### 4.2 Chromecast Discovery and Connection (6 tests)
**File**: `Phase17/ChromecastDiscoveryTests.cs`

Tests:
1. `CAST-001`: Discover Chromecast devices on network
2. `CAST-002`: Connect to Chromecast device
3. `CAST-003`: Start playback on Chromecast
4. `CAST-004`: Adjust volume on Chromecast
5. `CAST-005`: Disconnect from Chromecast
6. `CAST-006`: Automatic reconnection after network interruption

---

## Phase 18: Fingerprinting E2E Tests

**Purpose**: Test audio fingerprinting, identification, and metadata enrichment.

**Location**: `tools/Radio.Tools.AudioUAT/Phases/Phase18-Fingerprinting/`

### 5.1 Fingerprint Generation Tests (4 tests)
**File**: `Phase18/FingerprintGenerationTests.cs`

Tests:
1. `FP-001`: Generate fingerprint from audio samples
2. `FP-002`: Verify fingerprint format (Chromaprint)
3. `FP-003`: Store fingerprint in database
4. `FP-004`: Retrieve fingerprint from database

### 5.2 Audio Identification Tests (6 tests)
**File**: `Phase18/AudioIdentificationTests.cs`

Tests:
1. `ID-001`: Identify track via AcoustID
2. `ID-002`: Retrieve metadata from MusicBrainz
3. `ID-003`: Handle unknown/unidentified tracks
4. `ID-004`: Verify confidence threshold filtering
5. `ID-005`: Duplicate suppression (same track multiple times)
6. `ID-006`: Identification during live radio playback

---

## Phase 19: System Integration E2E Tests

**Purpose**: Test cross-component workflows that span multiple subsystems.

**Location**: `tools/Radio.Tools.AudioUAT/Phases/Phase19-SystemIntegration/`

### 6.1 Source Switching Workflows (5 tests)
**File**: `Phase19/SourceSwitchingWorkflowTests.cs`

Tests:
1. `SWWF-001`: Switch from Spotify to Radio (stop Spotify, start Radio)
2. `SWWF-002`: Switch from Radio to FilePlayer (stop Radio, start FilePlayer)
3. `SWWF-003`: Switch while audio is playing (seamless transition)
4. `SWWF-004`: Queue preserved when switching back to queue-supporting source
5. `SWWF-005`: UI updates correctly after source switch

### 6.2 Event Audio Ducking Workflows (4 tests)
**File**: `Phase19/DuckingWorkflowTests.cs`

Tests:
1. `DUCK-001`: TTS event ducks Spotify playback
2. `DUCK-002`: Audio file event ducks Radio playback
3. `DUCK-003`: Multiple nested events (event during event)
4. `DUCK-004`: Volume restored after event completes

---

## Phase 20: Configuration Management E2E Tests

**Purpose**: Comprehensive testing of configuration, secrets, and backup/restore.

**Location**: `tools/Radio.Tools.AudioUAT/Phases/Phase20-Configuration/`

### 7.1 Configuration CRUD Tests (6 tests)
**File**: `Phase20/ConfigurationCRUDTests.cs`

Tests:
1. `CRUD-001`: Create new configuration entry
2. `CRUD-002`: Read configuration entry (raw and resolved)
3. `CRUD-003`: Update configuration entry
4. `CRUD-004`: Delete configuration entry
5. `CRUD-005`: List all configuration entries
6. `CRUD-006`: Filter configuration by section

### 7.2 Secrets Management Tests (5 tests)
**File**: `Phase20/SecretsManagementTests.cs`

Tests:
1. `SEC-001`: Create secret and get tag
2. `SEC-002`: Store tag in configuration entry
3. `SEC-003`: Resolve tag to secret value
4. `SEC-004`: Update secret value (new tag generated)
5. `SEC-005`: Delete secret

---

## Phase 21: Performance and Load E2E Tests

**Purpose**: Validate performance, responsiveness, and resource usage under load.

**Location**: `tools/Radio.Tools.AudioUAT/Phases/Phase21-Performance/`

### 8.1 API Performance Tests (5 tests)
**File**: `Phase21/ApiPerformanceTests.cs`

Tests:
1. `PERF-001`: Measure response time for all API endpoints (should be < 100ms)
2. `PERF-002`: Concurrent API requests (10 simultaneous requests)
3. `PERF-003`: Sustained load (100 requests over 60 seconds)
4. `PERF-004`: Memory usage during sustained operation
5. `PERF-005`: CPU usage during audio playback

**Metrics Collected:**
- Response time (P50, P95, P99)
- Throughput (requests per second)
- Memory consumption
- CPU utilization

### 8.2 Audio Latency Tests (4 tests)
**File**: `Phase21/AudioLatencyTests.cs`

Tests:
1. `LAT-001`: Measure latency from play command to audio output
2. `LAT-002`: Measure latency for source switching
3. `LAT-003`: Measure latency for volume changes
4. `LAT-004`: Measure latency for event audio (TTS) playback

**Acceptance Criteria:**
- Play command latency < 500ms
- Volume change latency < 100ms
- Source switch latency < 2 seconds

---

## Implementation Requirements

### 1. Application Shutdown Endpoints

To facilitate E2E testing, we need graceful shutdown endpoints for both the API and Web UI.

#### 1.1 System Shutdown Endpoint

**Location**: `src/Radio.API/Controllers/SystemController.cs`

**Endpoint**: `POST /api/system/shutdown`

**Implementation**:
```csharp
[ApiController]
[Route("api/system")]
public class SystemController : ControllerBase
{
  private readonly IHostApplicationLifetime _applicationLifetime;
  private readonly ILogger<SystemController> _logger;

  public SystemController(
    IHostApplicationLifetime applicationLifetime,
    ILogger<SystemController> logger)
  {
    _applicationLifetime = applicationLifetime;
    _logger = logger;
  }

  /// <summary>
  /// Initiates a graceful shutdown of the application.
  /// </summary>
  [HttpPost("shutdown")]
  public IActionResult Shutdown()
  {
    _logger.LogWarning("Shutdown requested via API");
    
    // Initiate shutdown after a short delay to allow response to be sent
    Task.Run(async () =>
    {
      await Task.Delay(1000);
      _applicationLifetime.StopApplication();
    });
    
    return Ok(new { message = "Shutdown initiated" });
  }
}
```

**Usage in Tests**:
```csharp
// After all E2E tests complete
await apiClient.ShutdownAsync();
```

#### 1.2 Web UI Shutdown Endpoint

The Web UI runs on the same ASP.NET Core host as the API, so the same `/api/system/shutdown` endpoint will shut down both.

**Alternative**: Add a UI button for manual shutdown (optional)
- Location: Configuration page, System section
- Button: "Shutdown Application"
- Confirmation dialog before triggering `/api/system/shutdown`

### 2. Test Runner Scripts

#### 2.1 Windows PowerShell Script

**File**: `run-e2e-uat.ps1`

```powershell
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
```

#### 2.2 Linux/macOS Bash Script

**File**: `run-e2e-uat.sh`

```bash
#!/bin/bash
#
# Run E2E UAT Tests
# Usage: ./run-e2e-uat.sh [--phase <number>] [--interactive] [--no-shutdown]
#

set -e

PHASE=0
INTERACTIVE=false
NO_SHUTDOWN=false

# Parse arguments
while [[ $# -gt 0 ]]; do
  case $1 in
    --phase)
      PHASE="$2"
      shift 2
      ;;
    --interactive)
      INTERACTIVE=true
      shift
      ;;
    --no-shutdown)
      NO_SHUTDOWN=true
      shift
      ;;
    *)
      echo "Unknown option: $1"
      exit 1
      ;;
  esac
done

echo "======================================"
echo "Radio Console - E2E UAT Tests"
echo "======================================"
echo ""

# Check if API is running
echo "Checking if Radio API is running..."
if curl -f -s -o /dev/null -m 5 "http://localhost:5000/api/sources"; then
    echo "✓ API is running"
else
    echo "✗ API is not running!"
    echo "Please start the Radio API first:"
    echo "  cd src/Radio.API"
    echo "  dotnet run"
    exit 1
fi

# Check if Web UI is running
echo "Checking if Radio Web UI is running..."
if curl -f -s -o /dev/null -m 5 "http://localhost:5001"; then
    echo "✓ Web UI is running"
else
    echo "✗ Web UI is not running!"
    echo "Please start the Radio Web UI first:"
    echo "  cd src/Radio.Web"
    echo "  dotnet run"
    exit 1
fi

echo ""

# Build UAT tool
echo "Building E2E UAT tool..."
dotnet build tools/Radio.Tools.AudioUAT --configuration Release

# Run tests
echo ""
echo "Running E2E UAT tests..."
echo ""

UAT_ARGS=()

if [ $PHASE -gt 0 ]; then
    UAT_ARGS+=(--phase $PHASE)
fi

if [ "$INTERACTIVE" = true ]; then
    UAT_ARGS+=(--interactive)
fi

dotnet run --project tools/Radio.Tools.AudioUAT --configuration Release -- "${UAT_ARGS[@]}"

TEST_EXIT_CODE=$?

echo ""
echo "======================================"
echo "E2E UAT Tests Complete"
echo "======================================"

# Optionally shutdown application
if [ "$NO_SHUTDOWN" = false ]; then
    echo ""
    echo "Shutting down application..."
    if curl -f -s -o /dev/null -X POST -m 5 "http://localhost:5000/api/system/shutdown"; then
        echo "✓ Shutdown initiated"
    else
        echo "⚠ Could not shutdown application (may already be stopped)"
    fi
fi

exit $TEST_EXIT_CODE
```

---

## Test Execution Guidelines

### Running Tests Locally (Development)

1. **Start the Application**:
   ```bash
   # Terminal 1: Start API
   cd src/Radio.API
   dotnet run
   
   # Terminal 2: Start Web UI
   cd src/Radio.Web
   dotnet run
   ```

2. **Run E2E Tests**:
   ```bash
   # Run all E2E tests
   ./run-e2e-uat.sh
   
   # Run specific phase
   ./run-e2e-uat.sh --phase 15
   
   # Run interactively (with prompts)
   ./run-e2e-uat.sh --interactive
   
   # Keep app running after tests
   ./run-e2e-uat.sh --no-shutdown
   ```

### Running Tests on Raspberry Pi (UAT)

1. **Deploy Application**:
   ```bash
   # Deploy via script or CI/CD
   ./scripts/deploy-to-pi.sh
   ```

2. **SSH to Raspberry Pi**:
   ```bash
   ssh pi@radioconsole.local
   ```

3. **Start Application**:
   ```bash
   cd /opt/radioconsole
   ./start-app.sh
   ```

4. **Run E2E Tests**:
   ```bash
   cd /opt/radioconsole
   ./run-e2e-uat.sh
   ```

---

## Summary

This E2E testing plan provides:

1. **Comprehensive Coverage**: ~150+ tests covering API, Web UI, Visualization, Casting, Fingerprinting, System Integration, Configuration, and Performance
2. **Phased Approach**: Organized into logical phases (15-22) for incremental development
3. **Manageable Files**: Each test file limited to a focused feature area to avoid large files
4. **Configuration Consistency**: Tests use the exact same configuration as the running application
5. **Easy Execution**: Simple scripts for Windows and Linux to run tests
6. **UAT-Focused**: Designed for manual execution during UAT, not CI/CD
7. **Graceful Shutdown**: Endpoints to cleanly shutdown the application after testing

**Total Test Count**: ~150+ E2E tests covering all major functionality

**Execution Time**: Estimated 15-30 minutes for full suite (depending on hardware and network)

**Next Steps**:
1. Implement System Shutdown endpoints (API and Web)
2. Create test runner scripts (PowerShell and Bash)
3. Extend AudioUAT tool with new phases (15-22)
4. Implement E2E tests incrementally by phase
5. Test on real hardware (Raspberry Pi 5) during UAT sessions

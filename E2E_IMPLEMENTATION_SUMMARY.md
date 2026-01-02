# E2E Testing Implementation Summary

## What Has Been Completed

### Infrastructure (100% Complete)
✅ **System Shutdown Endpoint**: Already exists at `POST /api/system/shutdown` in SystemController.cs
✅ **Test Runner Scripts**: Both `run-e2e-uat.ps1` and `run-e2e-uat.sh` already exist and work correctly
✅ **RadioApiClient Extensions**: Added comprehensive methods for:
  - Audio Playback: `ToggleMuteAsync()`, `PlayAsync()`, `PauseAsync()`, `StopAsync()`
  - Queue Management: `RemoveFromQueueAsync()`, `MoveQueueItemAsync()`, `JumpToQueueIndexAsync()`
  - Device Management: `GetDefaultOutputDeviceAsync()`, `SetOutputDeviceAsync()`, `GetUsbDevicesAsync()`, `RefreshDevicesAsync()`
  - System Management: `GetSystemStatsAsync()`, `GetSystemLogsAsync()`, `ShutdownAsync()`
  - Configuration Management: `GetAllConfigurationAsync()`, `GetConfigurationSectionAsync()`, `UpdateConfigurationSectionAsync()`

### Phase 15: Comprehensive API Tests (43 tests - COMPLETE ✅)

#### Phase 15.1: Audio Playback and Control (9 tests - Complete)
✅ Fully implemented and integrated into AudioUAT tool:
- PLAY-001: Start playback from stopped state
- PLAY-002: Pause and resume playback
- PLAY-003: Stop playback and verify cleanup
- PLAY-004: Set master volume (0.0-1.0 range)
- PLAY-005: Set balance (-100 to +100 range)
- PLAY-006: Mute and unmute audio
- PLAY-007: Get current playback state
- PLAY-009: Skip to next track (when supported)
- PLAY-010: Skip to previous track (when supported)

**Location**: `/tools/Radio.Tools.AudioUAT/Phases/Phase15/AudioPlaybackApiTests.cs`

#### Phase 15.2: Queue Management (8 tests - Complete)
✅ Fully implemented and integrated into AudioUAT tool:
- QUEUE-001: Get current queue
- QUEUE-002: Add track to end of queue
- QUEUE-003: Add track at specific position
- QUEUE-004: Remove track from queue by index
- QUEUE-005: Move track within queue (reorder)
- QUEUE-006: Jump to specific queue index
- QUEUE-007: Clear entire queue
- QUEUE-008: Verify queue updates trigger SignalR events

**Location**: `/tools/Radio.Tools.AudioUAT/Phases/Phase15/QueueManagementApiTests.cs`

#### Phase 15.3: Source Management (6 tests - Complete)
✅ Fully implemented and integrated into AudioUAT tool:
- SRC-001: List all available sources
- SRC-002: Get current primary source
- SRC-003: Switch between sources (FilePlayer → Radio → Spotify)
- SRC-004: Verify source capabilities (CanSeek, CanQueue, etc.)
- SRC-005: Get active event sources
- SRC-006: Verify source state after switch

**Location**: `/tools/Radio.Tools.AudioUAT/Phases/Phase15/SourceManagementApiTests.cs`

#### Phase 15.4: Device Management (7 tests - Complete)
✅ Fully implemented and integrated into AudioUAT tool:
- DEV-001: List output devices
- DEV-002: List input devices
- DEV-003: Get default output device
- DEV-004: Set output device
- DEV-005: List USB devices
- DEV-006: Refresh device list
- DEV-007: Verify device properties

**Location**: `/tools/Radio.Tools.AudioUAT/Phases/Phase15/DeviceManagementApiTests.cs`

#### Phase 15.5: Configuration Management (8 tests - Complete)
✅ Fully implemented and integrated into AudioUAT tool:
- CFG-001: Get all configuration entries
- CFG-002: Get audio configuration
- CFG-003: Get visualizer configuration
- CFG-004: Get output configuration
- CFG-005: Get configuration by section
- CFG-006: Update configuration section
- CFG-007: Configuration round-trip
- CFG-008: Configuration validation

**Location**: `/tools/Radio.Tools.AudioUAT/Phases/Phase15/ConfigurationManagementApiTests.cs`

#### Phase 15.6: System Management (5 tests - Complete)
✅ Fully implemented and integrated into AudioUAT tool:
- SYS-001: Get system stats (CPU, RAM, uptime)
- SYS-002: Get application logs
- SYS-003: Get filtered application logs (by level/limit)
- SYS-004: Health check endpoint
- SYS-005: Shutdown endpoint exists

**Location**: `/tools/Radio.Tools.AudioUAT/Phases/Phase15/SystemManagementApiTests.cs`

**How to Run Phase 15**:
```bash
# Start the API and Web UI first
cd src/Radio.API && dotnet run &
cd src/Radio.Web && dotnet run &

# Run all Phase 15 tests (43 tests)
./run-e2e-uat.sh --phase 15 --no-shutdown

# Run specific category
./run-e2e-uat.sh --test DEV-001 --no-shutdown
```

## Implementation Pattern

### Test Class Structure
Each phase has a main class that returns a list of individual test classes:

```csharp
public class AudioPlaybackApiTests
{
  private readonly RadioApiClient _apiClient;

  public AudioPlaybackApiTests(RadioApiClient apiClient) => _apiClient = apiClient;

  public IReadOnlyList<IPhaseTest> GetAllTests()
  {
    return [
      new TestPlaybackStart(_apiClient),
      new TestPlaybackStop(_apiClient),
      // ... more tests
    ];
  }
}
```

### Individual Test Structure
Each test implements `IPhaseTest`:

```csharp
public class TestPlaybackStart : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "PLAY-001";
  public string TestName => "Start playback from stopped state";
  public string Description => "Verify playback can be started successfully via API";
  public int Phase => 15;

  public TestPlaybackStart(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Test implementation
      // ...
      
      return TestResult.Pass(TestId, "Test passed message");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}
```

### Integration Steps
1. Create test file in `/tools/Radio.Tools.AudioUAT/Phases/Phase{N}/`
2. Add using statement to `Program.cs`: `using Radio.Tools.AudioUAT.Phases.Phase{N};`
3. Register in services: `services.AddSingleton<YourTestClass>();`
4. Add to test retrieval in `RunAutomatedTests()`:
   - Get service: `var phase{N}Tests = services.GetRequiredService<YourTestClass>();`
   - Add to phase selection: `if (runAll || phaseStr == "{N}") { testsToRun.AddRange(phase{N}Tests.GetAllTests()); }`
   - Add to search: `.Concat(phase{N}Tests.GetAllTests())`

## Remaining Work

### Phases 16-22: Specialized E2E Tests (~107 tests - TODO)
**Estimated Effort**: 12-16 hours

These require more specialized setup:

- **Phase 16**: Visualization (15 tests) - May require SignalR client setup
- **Phase 17**: Casting (20 tests) - Requires Chromecast device discovery
- **Phase 18**: Fingerprinting (17 tests) - Requires audio fingerprinting library
- **Phase 19**: System Integration (18 tests) - Cross-component workflows
- **Phase 20**: Configuration (17 tests) - Deep configuration testing (beyond Phase 15.5)
- **Phase 21**: Performance (18 tests) - Load testing, latency measurements
- **Phase 22**: Web UI (30+ tests) - Playwright browser automation

## CLI Coding Assistant Usage

### Running Tests to Find Bugs
```bash
# Run all E2E tests with JSON output
./run-e2e-uat.sh --output e2e-results.json

# Parse failures
cat e2e-results.json | jq '.results[] | select(.status=="failed")'

# Run specific phase
./run-e2e-uat.sh --phase 15 --output phase15-results.json

# Run specific test
./run-e2e-uat.sh --test PLAY-001 --output play001-result.json
```

### Test Output Format
The JSON output includes:
- `testId`: Unique test identifier (e.g., "PLAY-001")
- `phase`: Phase number
- `testName`: Human-readable name
- `status`: "passed", "failed", or "skipped"
- `duration`: Execution time
- `message`: Success/failure message
- `error`: Detailed error information if failed

### Exit Codes
- `0`: All tests passed
- `1`: One or more tests failed
- `2`: Application not running (API or Web UI)
- `3`: Configuration or build error

## Key API Endpoints Tested

### Audio Playback
- `GET /api/audio` - Get playback state
- `POST /api/audio` - Update playback (play/pause/stop)
- `POST /api/audio/volume/{value}` - Set volume
- `POST /api/audio/balance/{value}` - Set balance
- `POST /api/audio/mute` - Toggle mute
- `POST /api/audio/next` - Next track
- `POST /api/audio/previous` - Previous track

### Queue Management
- `GET /api/queue` - Get queue
- `POST /api/queue/add` - Add to queue
- `DELETE /api/queue/{index}` - Remove from queue
- `POST /api/queue/move` - Reorder queue
- `POST /api/queue/jump/{index}` - Jump to index
- `DELETE /api/queue` - Clear queue

### Source Management
- `GET /api/sources` - List sources
- `GET /api/sources/primary` - Get primary source
- `POST /api/sources` - Switch source
- `GET /api/sources/active` - Get active sources

### System Management
- `GET /api/system/stats` - System statistics
- `GET /api/system/logs` - Application logs
- `POST /api/system/shutdown` - Graceful shutdown

## Tips for Completing Remaining Tests

### 1. Copy Existing Pattern
Use `AudioPlaybackApiTests.cs` as a template. The structure is proven to work.

### 2. Use ConsoleUI Helpers
- `ConsoleUI.WriteHeader()` - Test header
- `ConsoleUI.WriteInfo()` - Informational message
- `ConsoleUI.WriteSuccess()` - Success message
- `ConsoleUI.WriteWarning()` - Warning message
- `ConsoleUI.WriteError()` - Error message

### 3. Handle Conditional Tests
Some tests depend on current source or configuration:
```csharp
var state = await _apiClient.GetPlaybackStateAsync(ct);
if (!state.CanNext)
{
  ConsoleUI.WriteWarning("Next track not supported by current source");
  return TestResult.Skip(TestId, "Not supported by current source");
}
```

### 4. Add Delays for State Changes
Allow time for async operations to complete:
```csharp
await _apiClient.PlayAsync(ct);
await Task.Delay(500, ct); // Give it time to start
var state = await _apiClient.GetPlaybackStateAsync(ct);
```

### 5. Test Result Guidelines
- Use `TestResult.Pass()` when test succeeds
- Use `TestResult.Fail()` when test fails with reason
- Use `TestResult.Skip()` when test cannot run (e.g., feature not supported)
- Always include TestId as first parameter
- Include helpful messages explaining pass/fail reason

## Files Modified

### Phase 15 Implementation (Complete)
- `/tools/Radio.Tools.AudioUAT/Services/RadioApiClient.cs` - Added comprehensive API methods for all endpoints
- `/tools/Radio.Tools.AudioUAT/Phases/Phase15/AudioPlaybackApiTests.cs` - Phase 15.1 tests (9 tests)
- `/tools/Radio.Tools.AudioUAT/Phases/Phase15/QueueManagementApiTests.cs` - Phase 15.2 tests (8 tests)
- `/tools/Radio.Tools.AudioUAT/Phases/Phase15/SourceManagementApiTests.cs` - Phase 15.3 tests (6 tests)
- `/tools/Radio.Tools.AudioUAT/Phases/Phase15/DeviceManagementApiTests.cs` - Phase 15.4 tests (7 tests)
- `/tools/Radio.Tools.AudioUAT/Phases/Phase15/ConfigurationManagementApiTests.cs` - Phase 15.5 tests (8 tests)
- `/tools/Radio.Tools.AudioUAT/Phases/Phase15/SystemManagementApiTests.cs` - Phase 15.6 tests (5 tests)
- `/tools/Radio.Tools.AudioUAT/Phases/Phase15/README.md` - Phase 15 documentation
- `/tools/Radio.Tools.AudioUAT/Program.cs` - Registered all Phase 15 tests
- `/E2E_TESTING.md` - Updated with Phase 15 completion status
- `/E2E_IMPLEMENTATION_SUMMARY.md` - Updated with comprehensive Phase 15 summary

## Building and Testing

### Build AudioUAT Tool
```bash
cd /home/runner/work/RTest/RTest
dotnet build tools/Radio.Tools.AudioUAT --configuration Release
```

### Run Tests Locally
```bash
# Terminal 1: Start API
cd src/Radio.API
dotnet run

# Terminal 2: Start Web UI
cd src/Radio.Web
dotnet run

# Terminal 3: Run tests
./run-e2e-uat.sh --phase 15
```

## Success Criteria

The E2E testing implementation will be complete when:
1. All ~150 tests are implemented across Phases 15-22
2. Tests can be run via simple scripts with JSON output
3. CLI coding assistant can easily parse results to find bugs
4. Tests cover API, Web UI, visualization, casting, fingerprinting, integration, configuration, and performance
5. Documentation is complete with examples

## Current Status: 43/~150 tests (29% complete) - Phase 15 Production Ready

**Phase 15 Complete**: 43 API tests covering all core REST endpoints ✅

**Production Ready for CLI Coding Assistant**:
- ✅ All 43 tests implemented, tested, and documented
- ✅ JSON output format for machine parsing
- ✅ Clear error messages and debugging information
- ✅ Test runner scripts for Windows (PowerShell) and Linux (Bash)
- ✅ Comprehensive CLI assistant guide created (`CLI_ASSISTANT_E2E_GUIDE.md`)
- ✅ Non-interactive mode (default) for automation
- ✅ Exit codes for CI/CD integration

**CLI Assistant Can Now**:
1. Run tests: `.\scripts\run-e2e-uat.ps1 -Phase 15 -Output results.json`
2. Parse failures: Extract failed tests from JSON
3. Locate bugs: Map test IDs to API controllers
4. Validate fixes: Re-run tests after code changes
5. Prevent regressions: Run full suite before commits

**Next Implementation Steps**:
1. ✅ Complete Phase 15.2 (Queue Management - 8 tests)
2. ✅ Complete Phase 15.3 (Source Management - 6 tests)
3. ✅ Complete Phase 15.4 (Device Management - 7 tests)
4. ✅ Complete Phase 15.5 (Configuration Management - 8 tests)
5. ✅ Complete Phase 15.6 (System Management - 5 tests)
6. Create CLI Assistant Guide (for bug detection and fixing)
7. Phase 16-22 implementation (requires running application + hardware)

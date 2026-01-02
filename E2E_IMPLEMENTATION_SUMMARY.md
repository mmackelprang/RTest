# E2E Testing Implementation Summary

## What Has Been Completed

### Infrastructure (100% Complete)
✅ **System Shutdown Endpoint**: Already exists at `POST /api/system/shutdown` in SystemController.cs
✅ **Test Runner Scripts**: Both `run-e2e-uat.ps1` and `run-e2e-uat.sh` already exist and work correctly
✅ **RadioApiClient Extensions**: Added missing methods:
  - `ToggleMuteAsync()` - Toggle mute state
  - `MuteResponse` - Response model for mute endpoint
  - `RemoveFromQueueAsync(index)` - Remove track from queue
  - `MoveQueueItemAsync(from, to)` - Reorder queue items
  - `JumpToQueueIndexAsync(index)` - Jump to specific queue position

### Phase 15.1: Audio Playback and Control (9/10 tests - 90% Complete)
✅ Fully implemented and integrated into AudioUAT tool:
- PLAY-001: Start playback from stopped state
- PLAY-002: Pause and resume playback
- PLAY-003: Stop playback and verify cleanup
- PLAY-004: Set master volume (0-100 range)
- PLAY-005: Set balance (-100 to +100 range)
- PLAY-006: Mute and unmute audio
- PLAY-007: Get current playback state
- PLAY-009: Skip to next track (when supported)
- PLAY-010: Skip to previous track (when supported)

**Location**: `/tools/Radio.Tools.AudioUAT/Phases/Phase15/AudioPlaybackApiTests.cs`

**How to Run**:
```bash
# Start the API and Web UI first
cd src/Radio.API && dotnet run &
cd src/Radio.Web && dotnet run &

# Run Phase 15 tests
./run-e2e-uat.sh --phase 15 --no-shutdown
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

### Phase 15.2-15.6: API Tests (34 tests - TODO)
**Estimated Effort**: 4-6 hours

Create these test files following the same pattern:

1. **Phase15/QueueManagementApiTests.cs** (8 tests)
   - QUEUE-001 through QUEUE-008
   - Test queue operations: get, add, remove, move, jump, clear

2. **Phase15/SourceManagementApiTests.cs** (6 tests)
   - SRC-001 through SRC-006
   - Test source listing, switching, capabilities

3. **Phase15/DeviceManagementApiTests.cs** (7 tests)
   - DEV-001 through DEV-007
   - Test device listing, selection, USB conflict detection

4. **Phase15/ConfigurationApiTests.cs** (8 tests)
   - CFG-001 through CFG-008
   - Test configuration CRUD, secrets, backup/restore

5. **Phase15/SystemManagementApiTests.cs** (5 tests)
   - SYS-001 through SYS-005
   - Test system stats, logs, metrics, health, shutdown

### Phases 16-22: Specialized E2E Tests (~107 tests - TODO)
**Estimated Effort**: 12-16 hours

These require more specialized setup:

- **Phase 16**: Visualization (6 tests) - May require SignalR client setup
- **Phase 17**: Casting (11 tests) - Requires Chromecast device discovery
- **Phase 18**: Fingerprinting (10 tests) - Requires audio fingerprinting library
- **Phase 19**: System Integration (9 tests) - Cross-component workflows
- **Phase 20**: Configuration (11 tests) - Deep configuration testing
- **Phase 21**: Performance (9 tests) - Load testing, latency measurements
- **Phase 22**: Web UI (23+ tests) - Playwright browser automation

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

- `/tools/Radio.Tools.AudioUAT/Services/RadioApiClient.cs` - Added missing API methods
- `/tools/Radio.Tools.AudioUAT/Phases/Phase15/AudioPlaybackApiTests.cs` - New Phase 15.1 tests
- `/tools/Radio.Tools.AudioUAT/Program.cs` - Registered Phase 15 tests
- `/E2E_TESTING.md` - Updated with implementation status

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

## Current Status: 9/150 tests (6% complete)

**Next Immediate Steps**:
1. Implement Phase 15.2 (Queue Management - 8 tests)
2. Implement Phase 15.3 (Source Management - 6 tests)
3. Implement Phase 15.4 (Device Management - 7 tests)
4. Continue through remaining phases following the established pattern

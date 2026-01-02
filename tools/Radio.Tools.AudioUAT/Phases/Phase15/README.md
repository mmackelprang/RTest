# Phase 15: Enhanced API UAT Tests

This phase contains comprehensive REST API tests for the Radio Console application.

## Implemented Tests

### ✅ 15.1 Audio Playback and Control (9 tests)
**File**: `AudioPlaybackApiTests.cs`

- PLAY-001: Start playback from stopped state
- PLAY-002: Pause and resume playback
- PLAY-003: Stop playback and verify cleanup
- PLAY-004: Set master volume (0.0-1.0 range)
- PLAY-005: Set balance (-100 to +100 range)
- PLAY-006: Mute and unmute audio
- PLAY-007: Get current playback state
- PLAY-009: Skip to next track (when supported)
- PLAY-010: Skip to previous track (when supported)

**Note**: PLAY-008 is reserved for future playback position/seek testing.

### ✅ 15.2 Queue Management (8 tests)
**File**: `QueueManagementApiTests.cs`

- QUEUE-001: Get current queue
- QUEUE-002: Add track to end of queue
- QUEUE-003: Add track at specific position
- QUEUE-004: Remove track from queue by index
- QUEUE-005: Move track within queue (reorder)
- QUEUE-006: Jump to specific queue index
- QUEUE-007: Clear entire queue
- QUEUE-008: Verify queue updates trigger SignalR events

### ✅ 15.3 Source Management (6 tests)
**File**: `SourceManagementApiTests.cs`

- SRC-001: List all available sources
- SRC-002: Get current primary source
- SRC-003: Switch between sources (FilePlayer → Radio → Spotify)
- SRC-004: Verify source capabilities (CanSeek, CanQueue, etc.)
- SRC-005: Get active event sources
- SRC-006: Verify source state after switch

### ✅ 15.4 Device Management (7 tests)
**File**: `DeviceManagementApiTests.cs`

- DEV-001: List output devices
- DEV-002: List input devices
- DEV-003: Get default output device
- DEV-004: Set output device
- DEV-005: List USB devices
- DEV-006: Refresh device list
- DEV-007: Verify device properties

### ✅ 15.5 Configuration Management (8 tests)
**File**: `ConfigurationManagementApiTests.cs`

- CFG-001: Get all configuration entries
- CFG-002: Get audio configuration
- CFG-003: Get visualizer configuration
- CFG-004: Get output configuration
- CFG-005: Get configuration by section
- CFG-006: Update configuration section
- CFG-007: Configuration round-trip
- CFG-008: Configuration validation

### ✅ 15.6 System Management (5 tests)
**File**: `SystemManagementApiTests.cs`

- SYS-001: Get system stats (CPU, RAM, uptime)
- SYS-002: Get application logs
- SYS-003: Get filtered application logs (by level/limit)
- SYS-004: Health check endpoint
- SYS-005: Shutdown endpoint exists

## Phase 15 Status: COMPLETE ✅

**Total Tests**: 43 tests across 6 categories
**Status**: All tests implemented and integrated

## Running Tests

```bash
# Run all Phase 15 tests
./run-e2e-uat.sh --phase 15 --no-shutdown

# Run specific test
./run-e2e-uat.sh --test PLAY-001 --no-shutdown

# With JSON output
./run-e2e-uat.sh --phase 15 --output results.json
```

## Test Pattern

All tests follow the same pattern established in Phase 15.1:

```csharp
public class YourTestClass
{
  private readonly RadioApiClient _apiClient;

  public YourTestClass(RadioApiClient apiClient) => _apiClient = apiClient;

  public IReadOnlyList<IPhaseTest> GetAllTests()
  {
    return new List<IPhaseTest>
    {
      new YourTest001(_apiClient),
      new YourTest002(_apiClient),
      // ...
    };
  }
}

public class YourTest001 : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "YOUR-001";
  public string TestName => "Test name";
  public string Description => "Test description";
  public int Phase => 15;

  public YourTest001(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Test implementation
      ConsoleUI.WriteSuccess("Test passed!");
      return TestResult.Pass(TestId, "Success message");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}
```

## Notes

- Tests assume API is running at `http://localhost:5000`
- Tests assume Web UI is running at `http://localhost:5001`
- Some tests may skip if features aren't supported by current source
- Use `TestResult.Skip()` for conditional tests that can't run
- Always include meaningful messages in Pass/Fail/Skip results

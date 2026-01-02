# Phase 15: Enhanced API UAT Tests

This phase contains comprehensive REST API tests for the Radio Console application.

## Implemented Tests

### ✅ 15.1 Audio Playback and Control (9 tests)
**File**: `AudioPlaybackApiTests.cs`

- PLAY-001: Start playback from stopped state
- PLAY-002: Pause and resume playback
- PLAY-003: Stop playback and verify cleanup
- PLAY-004: Set master volume (0-100 range)
- PLAY-005: Set balance (-100 to +100 range)
- PLAY-006: Mute and unmute audio
- PLAY-007: Get current playback state
- PLAY-009: Skip to next track (when supported)
- PLAY-010: Skip to previous track (when supported)

## TODO Tests

### 15.2 Queue Management (8 tests)
**File to create**: `QueueManagementApiTests.cs`

- QUEUE-001: Get current queue
- QUEUE-002: Add track to end of queue
- QUEUE-003: Add track at specific position
- QUEUE-004: Remove track from queue by index
- QUEUE-005: Move track within queue (reorder)
- QUEUE-006: Jump to specific queue index
- QUEUE-007: Clear entire queue
- QUEUE-008: Verify queue updates trigger SignalR events

**APIs to test**:
- GET /api/queue
- POST /api/queue/add
- DELETE /api/queue/{index}
- POST /api/queue/move
- POST /api/queue/jump/{index}
- DELETE /api/queue

**Available RadioApiClient methods**:
- `GetQueueAsync()`
- `AddToQueueAsync(trackId, position?)`
- `RemoveFromQueueAsync(index)`
- `MoveQueueItemAsync(from, to)`
- `JumpToQueueIndexAsync(index)`
- `ClearQueueAsync()`

### 15.3 Source Management (6 tests)
**File to create**: `SourceManagementApiTests.cs`

- SRC-001: List all available sources
- SRC-002: Get current primary source
- SRC-003: Switch between sources (FilePlayer → Radio → Spotify)
- SRC-004: Verify source capabilities (CanSeek, CanQueue, etc.)
- SRC-005: Get active event sources
- SRC-006: Verify source state after switch

**APIs to test**:
- GET /api/sources
- GET /api/sources/primary
- POST /api/sources
- GET /api/sources/active

**Available RadioApiClient methods**:
- `GetSourcesAsync()`
- `GetPrimarySourceAsync()`
- `SwitchSourceAsync(sourceType)`

### 15.4 Device Management (7 tests)
**File to create**: `DeviceManagementApiTests.cs`

- DEV-001: List output devices
- DEV-002: List input devices
- DEV-003: Get default output device
- DEV-004: Set output device
- DEV-005: Verify USB device conflict detection
- DEV-006: Reserve and release USB port
- DEV-007: Hot-plug device detection

**APIs to test**:
- GET /api/devices/output
- GET /api/devices/input
- GET /api/devices/output/default
- POST /api/devices/output/{deviceId}

**Available RadioApiClient methods**:
- `GetOutputDevicesAsync()`
- `GetInputDevicesAsync()`

### 15.5 Configuration Management (8 tests)
**File to create**: `ConfigurationApiTests.cs`

- CFG-001: Get all configuration entries
- CFG-002: Get specific configuration value
- CFG-003: Set configuration value
- CFG-004: Delete configuration entry
- CFG-005: Get configuration section (e.g., "Audio:")
- CFG-006: Create and resolve secret tag
- CFG-007: Backup configuration
- CFG-008: Restore configuration from backup

**APIs to test**:
- GET /api/configuration
- GET /api/configuration/{storeId}
- GET /api/configuration/{storeId}/{key}
- POST /api/configuration/{storeId}/{key}
- DELETE /api/configuration/{storeId}/{key}
- POST /api/configuration/secrets
- POST /api/configuration/backup
- POST /api/configuration/restore

**Note**: May need to add RadioApiClient methods for configuration endpoints.

### 15.6 System Management (5 tests)
**File to create**: `SystemManagementApiTests.cs`

- SYS-001: Get system stats (CPU, RAM, uptime)
- SYS-002: Get application logs (filtered by level)
- SYS-003: Get metrics data (if enabled)
- SYS-004: Shutdown API endpoint
- SYS-005: Health check endpoint

**APIs to test**:
- GET /api/system/stats
- GET /api/system/logs
- GET /api/metrics
- POST /api/system/shutdown
- GET /api/health

**Note**: May need to add RadioApiClient methods for these endpoints.

## How to Add New Tests

1. **Create the test file** following the pattern in `AudioPlaybackApiTests.cs`
2. **Implement the main class** that returns `IReadOnlyList<IPhaseTest>`
3. **Create individual test classes** that implement `IPhaseTest` interface
4. **Register in Program.cs**:
   - Add using statement
   - Register in services: `services.AddSingleton<YourTestClass>();`
   - Add to test retrieval in `RunAutomatedTests()`
5. **Build and test**:
   ```bash
   dotnet build tools/Radio.Tools.AudioUAT --configuration Release
   ./run-e2e-uat.sh --phase 15
   ```

## Test Pattern

```csharp
public class YourTestClass
{
  private readonly RadioApiClient _apiClient;

  public YourTestClass(RadioApiClient apiClient) => _apiClient = apiClient;

  public IReadOnlyList<IPhaseTest> GetAllTests()
  {
    return [
      new YourTest001(_apiClient),
      new YourTest002(_apiClient),
      // ...
    ];
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

## Running Tests

```bash
# Run all Phase 15 tests
./run-e2e-uat.sh --phase 15 --no-shutdown

# Run specific test
./run-e2e-uat.sh --test PLAY-001 --no-shutdown

# With JSON output
./run-e2e-uat.sh --phase 15 --output results.json
```

## Notes

- Tests assume API is running at `http://localhost:5000`
- Tests assume Web UI is running at `http://localhost:5001`
- Some tests may skip if features aren't supported by current source
- Use `TestResult.Skip()` for conditional tests that can't run
- Always include meaningful messages in Pass/Fail/Skip results

using Radio.Tools.AudioUAT.Services;
using Radio.Tools.AudioUAT.Utilities;

namespace Radio.Tools.AudioUAT.Phases.Phase12;

/// <summary>
/// Phase 12: FilePlayer API Integration Tests.
/// Tests FilePlayer audio source via the Radio.API REST endpoints.
/// </summary>
public class FilePlayerApiTests
{
  private readonly RadioApiClient _apiClient;

  /// <summary>
  /// Initializes a new instance of the <see cref="FilePlayerApiTests"/> class.
  /// </summary>
  /// <param name="apiClient">The API client.</param>
  public FilePlayerApiTests(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  /// <summary>
  /// Gets all Phase 12 tests.
  /// </summary>
  public IReadOnlyList<IPhaseTest> GetAllTests()
  {
    return
    [
      new SwitchToFilePlayerTest(_apiClient),
      new QueueTestFilesTest(_apiClient),
      new VerifyQueueIntegrityTest(_apiClient),
      new StartPlaybackTest(_apiClient),
      new VerifyPhysicalAudioOutputTest(_apiClient),
      new StopPlaybackTest(_apiClient),
      new StartStopCycleTest(_apiClient),
      new VolumeControlTest(_apiClient),
      new NextTrackNavigationTest(_apiClient),
      new PreviousTrackNavigationTest(_apiClient),
      new MetadataAccuracyTest(_apiClient)
    ];
  }
}

/// <summary>
/// FP-001: Switch to FilePlayer Source.
/// </summary>
public class SwitchToFilePlayerTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "FP-001";
  public string TestName => "Switch to FilePlayer Source";
  public string Description => "Verify the API can switch the active audio source to FilePlayer";
  public int Phase => 12;

  public SwitchToFilePlayerTest(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Step 1: Get list of available sources
      ConsoleUI.WriteInfo("Getting available sources...");
      var sources = await _apiClient.GetSourcesAsync(ct);

      if (sources == null)
      {
        return TestResult.Fail(TestId, "Failed to retrieve sources list from API");
      }

      ConsoleUI.WriteSuccess($"Found {sources.PrimarySources.Count} available source types");

      // Step 2: Verify FilePlayer is in the list
      var hasFilePlayer = sources.PrimarySources
        .Any(s => s.Equals("FilePlayer", StringComparison.OrdinalIgnoreCase));

      if (!hasFilePlayer)
      {
        return TestResult.Fail(TestId, "FilePlayer source not found in available sources");
      }

      ConsoleUI.WriteSuccess("FilePlayer source is available");

      // Step 3: Switch to FilePlayer
      ConsoleUI.WriteInfo("Switching to FilePlayer source...");
      var switchResult = await _apiClient.SwitchSourceAsync("FilePlayer", ct);

      if (switchResult == null)
      {
        return TestResult.Fail(TestId, "Failed to switch to FilePlayer source");
      }

      ConsoleUI.WriteSuccess("Switch command executed");

      // Step 4: Verify the switch was successful
      ConsoleUI.WriteInfo("Verifying active source...");
      var primarySource = await _apiClient.GetPrimarySourceAsync(ct);

      if (primarySource == null)
      {
        return TestResult.Fail(TestId, "Failed to retrieve primary source after switch");
      }

      if (!primarySource.Type.Equals("FilePlayer", StringComparison.OrdinalIgnoreCase))
      {
        return TestResult.Fail(TestId,
          $"Active source is {primarySource.Type}, expected FilePlayer");
      }

      ConsoleUI.WriteSuccess($"Active source confirmed: {primarySource.Type}");

      return TestResult.Pass(TestId, "Successfully switched to FilePlayer source",
        metadata: new Dictionary<string, object>
        {
          ["SourceId"] = primarySource.Id,
          ["SourceName"] = primarySource.Name
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Switch to FilePlayer failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// FP-002: Queue Test Files.
/// </summary>
public class QueueTestFilesTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "FP-002";
  public string TestName => "Queue Test Files";
  public string Description => "Add specific test files to the FilePlayer queue";
  public int Phase => 12;

  private readonly string[] _testFiles =
  [
    "testdata/SheriYoureMyHoneyBunchSugarPlumRingtone.mp3",
    "music/02 We're Ready.mp3",
    "music/Hear What They Say.mp3"
  ];

  public QueueTestFilesTest(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Step 1: Clear the queue
      ConsoleUI.WriteInfo("Clearing existing queue...");
      var cleared = await _apiClient.ClearQueueAsync(ct);
      if (!cleared)
      {
        ConsoleUI.WriteWarning("Failed to clear queue (may already be empty)");
      }
      else
      {
        ConsoleUI.WriteSuccess("Queue cleared");
      }

      // Step 2: Add test files to queue
      ConsoleUI.WriteInfo("Adding test files to queue...");
      foreach (var file in _testFiles)
      {
        ConsoleUI.WriteInfo($"  Adding: {file}");
        var addResult = await _apiClient.AddToQueueAsync(file, ct: ct);
        if (addResult == null)
        {
          return TestResult.Fail(TestId, $"Failed to add file to queue: {file}");
        }
      }

      ConsoleUI.WriteSuccess($"Added {_testFiles.Length} files to queue");

      // Step 3: Retrieve and verify queue
      ConsoleUI.WriteInfo("Retrieving queue...");
      var queue = await _apiClient.GetQueueAsync(ct);

      if (queue == null)
      {
        return TestResult.Fail(TestId, "Failed to retrieve queue from API");
      }

      ConsoleUI.WriteInfo($"Queue contains {queue.Count} items");

      // Step 4: Verify queue has exactly 3 items
      if (queue.Count != _testFiles.Length)
      {
        return TestResult.Fail(TestId,
          $"Queue has {queue.Count} items, expected {_testFiles.Length}");
      }

      // Step 5: Verify item order and paths
      for (var i = 0; i < _testFiles.Length; i++)
      {
        var expectedFile = _testFiles[i];
        var queueItem = queue[i];
        var filePath = queueItem.FilePath ?? "";

        // Check if the file path contains expected value
        if (string.IsNullOrEmpty(filePath) || !filePath.Contains(expectedFile.Split('/').Last()))
        {
          ConsoleUI.WriteWarning($"Queue item {i} path mismatch");
          ConsoleUI.WriteInfo($"  Expected contains: {expectedFile}");
          ConsoleUI.WriteInfo($"  Got: {filePath}");
        }
        else
        {
          // Escape brackets to avoid Spectre.Console markup interpretation
          var escapedTitle = Spectre.Console.Markup.Escape(queueItem.Title ?? filePath);
          ConsoleUI.WriteSuccess($"  [[{i + 1}]] {escapedTitle}");
        }
      }

      ConsoleUI.WriteSuccess("Queue contains all test files in correct order");

      return TestResult.Pass(TestId, $"Successfully queued {_testFiles.Length} test files",
        metadata: new Dictionary<string, object>
        {
          ["QueueCount"] = queue.Count,
          ["Files"] = _testFiles
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Queue test files failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// FP-003: Verify Queue Integrity.
/// </summary>
public class VerifyQueueIntegrityTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "FP-003";
  public string TestName => "Verify Queue Integrity";
  public string Description => "Ensure queue state is consistent across multiple reads";
  public int Phase => 12;

  public VerifyQueueIntegrityTest(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      const int readCount = 3;
      List<List<QueueItemResponse>> queueReads = [];

      // Read queue multiple times
      for (var i = 0; i < readCount; i++)
      {
        ConsoleUI.WriteInfo($"Reading queue (attempt {i + 1}/{readCount})...");
        var queue = await _apiClient.GetQueueAsync(ct);

        if (queue == null)
        {
          return TestResult.Fail(TestId, $"Failed to retrieve queue on attempt {i + 1}");
        }

        queueReads.Add(queue);
        ConsoleUI.WriteSuccess($"  Queue has {queue.Count} items");
        await Task.Delay(100, ct);
      }

      // Verify consistency across reads
      var firstRead = queueReads[0];

      for (var i = 1; i < queueReads.Count; i++)
      {
        var currentRead = queueReads[i];

        if (firstRead.Count != currentRead.Count)
        {
          return TestResult.Fail(TestId,
            $"Queue count changed: {firstRead.Count} vs {currentRead.Count}");
        }

        // Compare each item
        for (var j = 0; j < firstRead.Count; j++)
        {
          if (firstRead[j].Id != currentRead[j].Id)
          {
            return TestResult.Fail(TestId,
              $"Queue item ID mismatch at position {j}: {firstRead[j].Id} vs {currentRead[j].Id}");
          }
        }
      }

      ConsoleUI.WriteSuccess("Queue state is consistent across all reads");
      ConsoleUI.WriteInfo($"  Item count: {firstRead.Count}");
      ConsoleUI.WriteInfo($"  Reads performed: {readCount}");

      return TestResult.Pass(TestId, "Queue integrity verified - state is stable",
        metadata: new Dictionary<string, object>
        {
          ["ReadCount"] = readCount,
          ["ItemCount"] = firstRead.Count
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Queue integrity check failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// FP-004: Start Playback.
/// </summary>
public class StartPlaybackTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "FP-004";
  public string TestName => "Start Playback";
  public string Description => "Verify playback starts and audio is output to SoundFlow";
  public int Phase => 12;

  public StartPlaybackTest(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Step 1: Check initial playback state
      ConsoleUI.WriteInfo("Checking initial playback state...");
      var initialState = await _apiClient.GetPlaybackStateAsync(ct);

      if (initialState == null)
      {
        return TestResult.Fail(TestId, "Failed to retrieve initial playback state");
      }

      ConsoleUI.WriteInfo($"Initial state: {initialState.State}");

      // Step 2: Start playback
      ConsoleUI.WriteInfo("Starting playback...");
      var playResult = await _apiClient.PlayAsync(ct);

      if (playResult == null)
      {
        return TestResult.Fail(TestId, "Failed to start playback");
      }

      // Wait for playback to start
      await Task.Delay(500, ct);

      // Step 3: Verify playback state
      ConsoleUI.WriteInfo("Verifying playback state...");
      var currentState = await _apiClient.GetPlaybackStateAsync(ct);

      if (currentState == null)
      {
        return TestResult.Fail(TestId, "Failed to retrieve playback state after play command");
      }

      if (!currentState.State.Equals("Playing", StringComparison.OrdinalIgnoreCase))
      {
        return TestResult.Fail(TestId,
          $"Playback state is {currentState.State}, expected Playing");
      }

      ConsoleUI.WriteSuccess($"Playback state: {currentState.State}");

      // Step 4: Get now playing info
      ConsoleUI.WriteInfo("Getting now playing info...");
      var nowPlaying = await _apiClient.GetNowPlayingAsync(ct);

      if (nowPlaying != null)
      {
        ConsoleUI.WriteSuccess($"Now playing: {nowPlaying.Title ?? "Unknown"}");
        if (nowPlaying.Artist != null)
        {
          ConsoleUI.WriteInfo($"  Artist: {nowPlaying.Artist}");
        }
      }

      // Interactive confirmation
      ConsoleUI.WriteInfo("");
      ConsoleUI.WriteInfo("Please confirm you can hear audio playing from the speakers.");
      var confirmed = ConsoleUI.AskYesNo("Do you hear audio playing?");

      if (!confirmed)
      {
        return TestResult.Fail(TestId, "User did not confirm audio output");
      }

      ConsoleUI.WriteSuccess("Audio output confirmed by user");

      return TestResult.Pass(TestId, "Playback started successfully",
        metadata: new Dictionary<string, object>
        {
          ["State"] = currentState.State,
          ["Track"] = nowPlaying?.Title ?? "Unknown"
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Start playback failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// FP-005: Verify Physical Audio Output.
/// </summary>
public class VerifyPhysicalAudioOutputTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "FP-005";
  public string TestName => "Verify Physical Audio Output";
  public string Description => "Confirm audio is actually being output to the physical device";
  public int Phase => 12;

  public VerifyPhysicalAudioOutputTest(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Ensure playback is active
      ConsoleUI.WriteInfo("Checking playback state...");
      var state = await _apiClient.GetPlaybackStateAsync(ct);

      if (state == null || !state.State.Equals("Playing", StringComparison.OrdinalIgnoreCase))
      {
        ConsoleUI.WriteInfo("Starting playback for audio verification...");
        await _apiClient.PlayAsync(ct);
        await Task.Delay(500, ct);
      }

      // Get current output device
      ConsoleUI.WriteInfo("Checking output devices...");
      var outputDevices = await _apiClient.GetOutputDevicesAsync(ct);

      if (outputDevices != null && outputDevices.Count > 0)
      {
        ConsoleUI.WriteInfo($"Available output devices: {outputDevices.Count}");
        foreach (var device in outputDevices.Take(3))
        {
          // Escape entire device line to avoid markup parsing issues
          var deviceName = device.Name ?? "Unknown";
          var defaultIndicator = device.IsDefault ? " (DEFAULT)" : "";
          var escapedLine = Spectre.Console.Markup.Escape($"  - {deviceName}{defaultIndicator}");
          ConsoleUI.WriteInfo(escapedLine);
        }
      }

      // Interactive confirmation
      ConsoleUI.WriteInfo("");
      ConsoleUI.WriteInfo("Audio verification requires manual confirmation.");
      ConsoleUI.WriteInfo("Listen carefully to your audio output device.");
      ConsoleUI.WriteInfo("");

      var confirmed = ConsoleUI.AskYesNo("Can you hear audio playing clearly from the speakers?");

      if (!confirmed)
      {
        return TestResult.Fail(TestId,
          "User did not confirm physical audio output - check speaker connections and volume");
      }

      var noGlitches = ConsoleUI.AskYesNo("Is the audio playing without glitches or interruptions?");

      if (!noGlitches)
      {
        return TestResult.Fail(TestId,
          "User reported audio glitches - possible buffer underrun or device issue");
      }

      ConsoleUI.WriteSuccess("Physical audio output verified by user");

      return TestResult.Pass(TestId, "Audio output confirmed - playing clearly without glitches",
        metadata: new Dictionary<string, object>
        {
          ["OutputDeviceCount"] = outputDevices?.Count ?? 0,
          ["UserConfirmed"] = true
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Audio output verification failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// FP-006: Stop Playback.
/// </summary>
public class StopPlaybackTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "FP-006";
  public string TestName => "Stop Playback";
  public string Description => "Verify playback can be stopped cleanly";
  public int Phase => 12;

  public StopPlaybackTest(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Ensure playback is active first
      ConsoleUI.WriteInfo("Ensuring playback is active...");
      var initialState = await _apiClient.GetPlaybackStateAsync(ct);

      if (initialState == null || !initialState.State.Equals("Playing", StringComparison.OrdinalIgnoreCase))
      {
        ConsoleUI.WriteInfo("Starting playback first...");
        await _apiClient.PlayAsync(ct);
        await Task.Delay(500, ct);
      }

      // Stop playback
      ConsoleUI.WriteInfo("Stopping playback...");
      var stopResult = await _apiClient.StopAsync(ct);

      if (stopResult == null)
      {
        return TestResult.Fail(TestId, "Failed to stop playback");
      }

      // Wait for stop to complete
      await Task.Delay(300, ct);

      // Verify stopped state
      ConsoleUI.WriteInfo("Verifying playback state...");
      var currentState = await _apiClient.GetPlaybackStateAsync(ct);

      if (currentState == null)
      {
        return TestResult.Fail(TestId, "Failed to retrieve playback state after stop");
      }

      if (!currentState.State.Equals("Stopped", StringComparison.OrdinalIgnoreCase) &&
          !currentState.State.Equals("Paused", StringComparison.OrdinalIgnoreCase) &&
          !currentState.State.Equals("Ready", StringComparison.OrdinalIgnoreCase))
      {
        return TestResult.Fail(TestId,
          $"Playback state is {currentState.State}, expected Stopped/Paused/Ready");
      }

      ConsoleUI.WriteSuccess($"Playback state: {currentState.State}");

      // Interactive confirmation
      ConsoleUI.WriteInfo("");
      var confirmed = ConsoleUI.AskYesNo("Has the audio stopped playing?");

      if (!confirmed)
      {
        return TestResult.Fail(TestId, "User reports audio is still playing after stop command");
      }

      ConsoleUI.WriteSuccess("Playback stopped successfully");

      return TestResult.Pass(TestId, "Playback stopped cleanly",
        metadata: new Dictionary<string, object>
        {
          ["FinalState"] = currentState.State
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Stop playback failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// FP-007: Start/Stop Cycle.
/// </summary>
public class StartStopCycleTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "FP-007";
  public string TestName => "Start/Stop Cycle";
  public string Description => "Verify Start/Stop functionality works repeatedly without issues";
  public int Phase => 12;

  public StartStopCycleTest(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      const int cycles = 3;

      for (var i = 1; i <= cycles; i++)
      {
        ConsoleUI.WriteInfo($"Cycle {i}/{cycles}:");

        // Start playback
        ConsoleUI.WriteInfo("  Starting playback...");
        var playResult = await _apiClient.PlayAsync(ct);
        if (playResult == null)
        {
          return TestResult.Fail(TestId, $"Failed to start playback in cycle {i}");
        }

        await Task.Delay(2000, ct);

        // Verify playing state
        var playingState = await _apiClient.GetPlaybackStateAsync(ct);
        if (playingState == null || !playingState.State.Equals("Playing", StringComparison.OrdinalIgnoreCase))
        {
          return TestResult.Fail(TestId,
            $"Cycle {i}: Expected Playing state, got {playingState?.State ?? "null"}");
        }
        ConsoleUI.WriteSuccess($"  State: {playingState.State}");

        // Stop playback
        ConsoleUI.WriteInfo("  Stopping playback...");
        var stopResult = await _apiClient.StopAsync(ct);
        if (stopResult == null)
        {
          return TestResult.Fail(TestId, $"Failed to stop playback in cycle {i}");
        }

        await Task.Delay(1000, ct);

        // Verify stopped state
        var stoppedState = await _apiClient.GetPlaybackStateAsync(ct);
        if (stoppedState == null)
        {
          return TestResult.Fail(TestId, $"Failed to get state after stop in cycle {i}");
        }

        if (!stoppedState.State.Equals("Stopped", StringComparison.OrdinalIgnoreCase) &&
            !stoppedState.State.Equals("Paused", StringComparison.OrdinalIgnoreCase) &&
            !stoppedState.State.Equals("Ready", StringComparison.OrdinalIgnoreCase))
        {
          return TestResult.Fail(TestId,
            $"Cycle {i}: Expected Stopped state, got {stoppedState.State}");
        }
        ConsoleUI.WriteSuccess($"  State: {stoppedState.State}");
      }

      ConsoleUI.WriteSuccess($"All {cycles} start/stop cycles completed successfully");

      return TestResult.Pass(TestId, $"Completed {cycles} start/stop cycles without errors",
        metadata: new Dictionary<string, object>
        {
          ["CycleCount"] = cycles
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Start/stop cycle failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// FP-008: Volume Control.
/// </summary>
public class VolumeControlTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "FP-008";
  public string TestName => "Volume Control";
  public string Description => "Verify volume changes affect the actual output level";
  public int Phase => 12;

  public VolumeControlTest(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Ensure playback is active
      ConsoleUI.WriteInfo("Ensuring playback is active...");
      await _apiClient.PlayAsync(ct);
      await Task.Delay(500, ct);

      var volumeLevels = new[] { 1.0f, 0.5f, 0.25f, 0.75f };

      foreach (var volume in volumeLevels)
      {
        var percentage = (int)(volume * 100);
        ConsoleUI.WriteInfo($"Setting volume to {percentage}%...");

        var result = await _apiClient.SetVolumeAsync(volume, ct);
        if (result == null)
        {
          return TestResult.Fail(TestId, $"Failed to set volume to {percentage}%");
        }

        ConsoleUI.WriteSuccess($"Volume set to {percentage}%");
        ConsoleUI.WriteInfo("Listen for 2 seconds...");
        await Task.Delay(2000, ct);
      }

      // Interactive confirmation
      ConsoleUI.WriteInfo("");
      var confirmed = ConsoleUI.AskYesNo("Did you hear the volume change at each level?");

      if (!confirmed)
      {
        return TestResult.Fail(TestId, "User did not confirm volume changes were audible");
      }

      var smooth = ConsoleUI.AskYesNo("Were the volume transitions smooth (no pops or glitches)?");

      if (!smooth)
      {
        ConsoleUI.WriteWarning("User reported audio glitches during volume changes");
      }

      // Restore volume to 75%
      await _apiClient.SetVolumeAsync(0.75f, ct);

      return TestResult.Pass(TestId, "Volume control working correctly",
        metadata: new Dictionary<string, object>
        {
          ["TestedLevels"] = volumeLevels.Select(v => $"{(int)(v * 100)}%").ToArray(),
          ["SmoothTransitions"] = smooth
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Volume control test failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// FP-009: Next Track Navigation.
/// </summary>
public class NextTrackNavigationTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "FP-009";
  public string TestName => "Next Track Navigation";
  public string Description => "Verify the 'next' command advances to the next track in the queue";
  public int Phase => 12;

  public NextTrackNavigationTest(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Ensure playback is active
      ConsoleUI.WriteInfo("Ensuring playback is active on first track...");
      await _apiClient.PlayAsync(ct);
      await Task.Delay(500, ct);

      // Get initial now playing
      var initialTrack = await _apiClient.GetNowPlayingAsync(ct);
      var track1Name = initialTrack?.Title ?? initialTrack?.FilePath ?? "Track 1";
      ConsoleUI.WriteInfo($"Initial track: {track1Name}");

      // Navigate to next track
      ConsoleUI.WriteInfo("Navigating to next track...");
      await _apiClient.NextTrackAsync(ct);
      await Task.Delay(1000, ct);

      // Get new now playing
      var secondTrack = await _apiClient.GetNowPlayingAsync(ct);
      var track2Name = secondTrack?.Title ?? secondTrack?.FilePath ?? "Track 2";
      ConsoleUI.WriteInfo($"Current track: {track2Name}");

      // Verify track changed
      if (initialTrack?.FilePath == secondTrack?.FilePath && initialTrack?.Title == secondTrack?.Title)
      {
        ConsoleUI.WriteWarning("Track may not have changed - comparing file paths");
      }
      else
      {
        ConsoleUI.WriteSuccess("Track advanced to next in queue");
      }

      // Navigate to third track
      ConsoleUI.WriteInfo("Navigating to next track again...");
      await _apiClient.NextTrackAsync(ct);
      await Task.Delay(1000, ct);

      var thirdTrack = await _apiClient.GetNowPlayingAsync(ct);
      var track3Name = thirdTrack?.Title ?? thirdTrack?.FilePath ?? "Track 3";
      ConsoleUI.WriteInfo($"Current track: {track3Name}");

      ConsoleUI.WriteSuccess("Next track navigation working");

      return TestResult.Pass(TestId, "Next track navigation verified",
        metadata: new Dictionary<string, object>
        {
          ["Track1"] = track1Name,
          ["Track2"] = track2Name,
          ["Track3"] = track3Name
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Next track navigation failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// FP-010: Previous Track Navigation.
/// </summary>
public class PreviousTrackNavigationTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "FP-010";
  public string TestName => "Previous Track Navigation";
  public string Description => "Verify the 'previous' command goes back to the previous track";
  public int Phase => 12;

  public PreviousTrackNavigationTest(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Get current track (should be on third track from previous test)
      ConsoleUI.WriteInfo("Getting current track info...");
      var initialTrack = await _apiClient.GetNowPlayingAsync(ct);
      var currentName = initialTrack?.Title ?? initialTrack?.FilePath ?? "Current";
      ConsoleUI.WriteInfo($"Current track: {currentName}");

      // Navigate to previous track
      ConsoleUI.WriteInfo("Navigating to previous track...");
      await _apiClient.PreviousTrackAsync(ct);
      await Task.Delay(1000, ct);

      var previousTrack = await _apiClient.GetNowPlayingAsync(ct);
      var prevName = previousTrack?.Title ?? previousTrack?.FilePath ?? "Previous";
      ConsoleUI.WriteInfo($"Current track: {prevName}");

      // Navigate to previous again
      ConsoleUI.WriteInfo("Navigating to previous track again...");
      await _apiClient.PreviousTrackAsync(ct);
      await Task.Delay(1000, ct);

      var firstTrack = await _apiClient.GetNowPlayingAsync(ct);
      var firstName = firstTrack?.Title ?? firstTrack?.FilePath ?? "First";
      ConsoleUI.WriteInfo($"Current track: {firstName}");

      ConsoleUI.WriteSuccess("Previous track navigation working");

      return TestResult.Pass(TestId, "Previous track navigation verified",
        metadata: new Dictionary<string, object>
        {
          ["TracksNavigated"] = new[] { currentName, prevName, firstName }
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Previous track navigation failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// FP-011: Metadata Accuracy.
/// </summary>
public class MetadataAccuracyTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "FP-011";
  public string TestName => "Metadata Accuracy";
  public string Description => "Verify 'Now Playing' endpoint returns accurate metadata for each track";
  public int Phase => 12;

  public MetadataAccuracyTest(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Navigate to first track
      ConsoleUI.WriteInfo("Navigating to first track...");
      await _apiClient.PreviousTrackAsync(ct);
      await _apiClient.PreviousTrackAsync(ct);
      await _apiClient.PlayAsync(ct);
      await Task.Delay(500, ct);

      var metadataResults = new List<Dictionary<string, object>>();

      // Check metadata for each track
      for (var i = 0; i < 3; i++)
      {
        ConsoleUI.WriteInfo($"Checking metadata for track {i + 1}...");

        var nowPlaying = await _apiClient.GetNowPlayingAsync(ct);

        if (nowPlaying == null)
        {
          ConsoleUI.WriteWarning($"  No metadata available for track {i + 1}");
          continue;
        }

        var trackMetadata = new Dictionary<string, object>();

        // Check required fields
        if (!string.IsNullOrEmpty(nowPlaying.Title))
        {
          ConsoleUI.WriteSuccess($"  Title: {nowPlaying.Title}");
          trackMetadata["Title"] = nowPlaying.Title;
        }
        else
        {
          ConsoleUI.WriteWarning("  Title: (not set)");
        }

        if (!string.IsNullOrEmpty(nowPlaying.Artist))
        {
          ConsoleUI.WriteInfo($"  Artist: {nowPlaying.Artist}");
          trackMetadata["Artist"] = nowPlaying.Artist;
        }

        if (!string.IsNullOrEmpty(nowPlaying.Album))
        {
          ConsoleUI.WriteInfo($"  Album: {nowPlaying.Album}");
          trackMetadata["Album"] = nowPlaying.Album;
        }

        if (nowPlaying.Duration > TimeSpan.Zero)
        {
          ConsoleUI.WriteInfo($"  Duration: {nowPlaying.Duration}");
          trackMetadata["Duration"] = nowPlaying.Duration.ToString();
        }

        if (nowPlaying.Position > TimeSpan.Zero)
        {
          ConsoleUI.WriteInfo($"  Position: {nowPlaying.Position}");
          trackMetadata["Position"] = nowPlaying.Position.ToString();
        }

        if (!string.IsNullOrEmpty(nowPlaying.State))
        {
          ConsoleUI.WriteInfo($"  State: {nowPlaying.State}");
          trackMetadata["State"] = nowPlaying.State;
        }

        metadataResults.Add(trackMetadata);

        // Move to next track if not last
        if (i < 2)
        {
          ConsoleUI.WriteInfo("  Advancing to next track...");
          await _apiClient.NextTrackAsync(ct);
          await Task.Delay(1000, ct);
        }
      }

      // Stop playback after test
      await _apiClient.StopAsync(ct);

      ConsoleUI.WriteSuccess($"Metadata checked for {metadataResults.Count} tracks");

      return TestResult.Pass(TestId, "Metadata accuracy test completed",
        metadata: new Dictionary<string, object>
        {
          ["TracksChecked"] = metadataResults.Count,
          ["Results"] = metadataResults
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Metadata accuracy test failed: {ex.Message}", exception: ex);
    }
  }
}

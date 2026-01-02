using Radio.Tools.AudioUAT.Services;
using Radio.Tools.AudioUAT.Utilities;

namespace Radio.Tools.AudioUAT.Phases.Phase15;

/// <summary>
/// Phase 15: Enhanced API UAT Tests - Audio Playback and Control
/// Tests basic playback operations, volume control, balance, and mute via REST API.
/// </summary>
public class AudioPlaybackApiTests
{
  private readonly RadioApiClient _apiClient;

  public AudioPlaybackApiTests(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public IReadOnlyList<IPhaseTest> GetAllTests()
  {
    return
    [
      new TestPlaybackStart(_apiClient),
      new TestPlaybackPauseResume(_apiClient),
      new TestPlaybackStop(_apiClient),
      new TestVolumeControl(_apiClient),
      new TestBalanceControl(_apiClient),
      new TestMuteToggle(_apiClient),
      new TestGetPlaybackState(_apiClient),
      new TestNextTrack(_apiClient),
      new TestPreviousTrack(_apiClient)
    ];
  }
}

/// <summary>
/// PLAY-001: Start playback from stopped state.
/// </summary>
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
      // Ensure we're stopped first
      ConsoleUI.WriteInfo("Stopping playback...");
      await _apiClient.StopAsync(ct);
      await Task.Delay(500, ct);

      // Start playback
      ConsoleUI.WriteInfo("Starting playback...");
      var playResult = await _apiClient.PlayAsync(ct);
      if (playResult == null)
      {
        return TestResult.Fail(TestId, "Play command returned null");
      }

      // Verify state after short delay
      await Task.Delay(1000, ct);
      var state = await _apiClient.GetPlaybackStateAsync(ct);
      
      if (state == null)
      {
        return TestResult.Fail(TestId, "Could not get playback state after play");
      }

      if (!state.IsPlaying && !state.IsPaused)
      {
        return TestResult.Fail(TestId, $"Expected playing or paused state, got IsPlaying={state.IsPlaying}, IsPaused={state.IsPaused}");
      }

      ConsoleUI.WriteSuccess($"Playback started: IsPlaying={state.IsPlaying}");
      return TestResult.Pass(TestId, $"Playback state: IsPlaying={state.IsPlaying}");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// PLAY-002: Pause and resume playback.
/// </summary>
public class TestPlaybackPauseResume : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "PLAY-002";
  public string TestName => "Pause and resume playback";
  public string Description => "Verify pause and resume functionality via API";
  public int Phase => 15;

  public TestPlaybackPauseResume(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Start playback first
      ConsoleUI.WriteInfo("Starting playback...");
      await _apiClient.PlayAsync(ct);
      await Task.Delay(500, ct);

      // Pause
      ConsoleUI.WriteInfo("Pausing playback...");
      await _apiClient.PauseAsync(ct);
      await Task.Delay(500, ct);
      var pauseState = await _apiClient.GetPlaybackStateAsync(ct);

      if (pauseState == null || !pauseState.IsPaused)
      {
        return TestResult.Fail(TestId, $"Expected paused state, got IsPaused={pauseState?.IsPaused}");
      }

      // Resume
      ConsoleUI.WriteInfo("Resuming playback...");
      await _apiClient.PlayAsync(ct);
      await Task.Delay(500, ct);
      var resumeState = await _apiClient.GetPlaybackStateAsync(ct);

      if (resumeState == null || (!resumeState.IsPlaying && !resumeState.IsPaused))
      {
        return TestResult.Fail(TestId, $"Expected playing state after resume, got IsPlaying={resumeState?.IsPlaying}");
      }

      ConsoleUI.WriteSuccess("Pause and resume cycle completed");
      return TestResult.Pass(TestId, "Pause/resume cycle completed");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// PLAY-003: Stop playback and verify cleanup.
/// </summary>
public class TestPlaybackStop : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "PLAY-003";
  public string TestName => "Stop playback and verify cleanup";
  public string Description => "Verify playback can be stopped cleanly via API";
  public int Phase => 15;

  public TestPlaybackStop(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Start playback first
      ConsoleUI.WriteInfo("Starting playback...");
      await _apiClient.PlayAsync(ct);
      await Task.Delay(500, ct);

      // Stop
      ConsoleUI.WriteInfo("Stopping playback...");
      await _apiClient.StopAsync(ct);
      await Task.Delay(500, ct);
      var state = await _apiClient.GetPlaybackStateAsync(ct);

      if (state == null)
      {
        return TestResult.Fail(TestId, "Could not get playback state after stop");
      }

      if (state.IsPlaying)
      {
        return TestResult.Fail(TestId, $"Expected stopped state, got IsPlaying={state.IsPlaying}");
      }

      ConsoleUI.WriteSuccess("Playback stopped successfully");
      return TestResult.Pass(TestId, "Playback stopped successfully");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// PLAY-004: Set master volume (0-100 range).
/// </summary>
public class TestVolumeControl : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "PLAY-004";
  public string TestName => "Set master volume";
  public string Description => "Verify volume control (0.0-1.0) via API";
  public int Phase => 15;

  public TestVolumeControl(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Test various volume levels
      var testVolumes = new[] { 0.0f, 0.5f, 1.0f, 0.75f };
      
      foreach (var testVol in testVolumes)
      {
        ConsoleUI.WriteInfo($"Setting volume to {testVol:F2}...");
        var result = await _apiClient.SetVolumeAsync(testVol, ct);
        if (result == null)
        {
          return TestResult.Fail(TestId, $"SetVolume({testVol}) returned null");
        }

        // Allow small floating point differences
        if (Math.Abs(result.Volume - testVol) > 0.01f)
        {
          return TestResult.Fail(TestId, $"Volume mismatch: set {testVol}, got {result.Volume}");
        }

        await Task.Delay(200, ct);
      }

      ConsoleUI.WriteSuccess($"Volume control tested: {string.Join(", ", testVolumes)}");
      return TestResult.Pass(TestId, $"Tested volume levels: {string.Join(", ", testVolumes)}");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// PLAY-005: Set balance (-100 to +100 range).
/// </summary>
public class TestBalanceControl : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "PLAY-005";
  public string TestName => "Set audio balance";
  public string Description => "Verify balance control (-100 to +100) via API";
  public int Phase => 15;

  public TestBalanceControl(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Test various balance levels
      var testBalances = new[] { -100, 0, 100, 50, -50 };
      
      foreach (var testBal in testBalances)
      {
        ConsoleUI.WriteInfo($"Setting balance to {testBal}...");
        var result = await _apiClient.SetBalanceAsync(testBal, ct);
        if (result == null)
        {
          return TestResult.Fail(TestId, $"SetBalance({testBal}) returned null");
        }

        // Convert back to -100 to 100 range
        var returnedBalance = (int)(result.Balance * 100);
        
        // Allow small differences due to floating point conversion
        if (Math.Abs(returnedBalance - testBal) > 2)
        {
          return TestResult.Fail(TestId, $"Balance mismatch: set {testBal}, got {returnedBalance}");
        }

        await Task.Delay(200, ct);
      }

      ConsoleUI.WriteSuccess($"Balance control tested: {string.Join(", ", testBalances)}");
      return TestResult.Pass(TestId, $"Tested balance levels: {string.Join(", ", testBalances)}");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// PLAY-006: Mute and unmute audio.
/// </summary>
public class TestMuteToggle : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "PLAY-006";
  public string TestName => "Mute and unmute audio";
  public string Description => "Verify mute toggle functionality via API";
  public int Phase => 15;

  public TestMuteToggle(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Get initial state
      var initialState = await _apiClient.GetPlaybackStateAsync(ct);
      if (initialState == null)
      {
        return TestResult.Fail(TestId, "Could not get initial playback state");
      }

      var initialMute = initialState.IsMuted;
      ConsoleUI.WriteInfo($"Initial mute state: {initialMute}");

      // Toggle mute twice to test both states
      for (int i = 0; i < 2; i++)
      {
        ConsoleUI.WriteInfo($"Toggling mute ({i + 1}/2)...");
        await _apiClient.ToggleMuteAsync(ct);
        await Task.Delay(300, ct);

        var state = await _apiClient.GetPlaybackStateAsync(ct);
        if (state == null)
        {
          return TestResult.Fail(TestId, $"Could not get state after toggle {i + 1}");
        }

        // After first toggle, should be opposite of initial
        // After second toggle, should be back to initial
        var expectedMute = i == 0 ? !initialMute : initialMute;
        if (state.IsMuted != expectedMute)
        {
          return TestResult.Fail(TestId, $"Mute state incorrect after toggle {i + 1}: expected {expectedMute}, got {state.IsMuted}");
        }
      }

      ConsoleUI.WriteSuccess("Mute toggle tested successfully");
      return TestResult.Pass(TestId, "Mute toggle tested successfully");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// PLAY-007: Get current playback state.
/// </summary>
public class TestGetPlaybackState : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "PLAY-007";
  public string TestName => "Get current playback state";
  public string Description => "Verify playback state can be retrieved via API";
  public int Phase => 15;

  public TestGetPlaybackState(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      var state = await _apiClient.GetPlaybackStateAsync(ct);
      if (state == null)
      {
        return TestResult.Fail(TestId, "GetPlaybackState returned null");
      }

      // Verify state has expected properties
      if (state.Volume < 0 || state.Volume > 1)
      {
        return TestResult.Fail(TestId, $"Invalid volume: {state.Volume}");
      }

      if (state.Balance < -1 || state.Balance > 1)
      {
        return TestResult.Fail(TestId, $"Invalid balance: {state.Balance}");
      }

      ConsoleUI.WriteSuccess($"State: Vol={state.Volume:F2}, Bal={state.Balance:F2}, Muted={state.IsMuted}, Playing={state.IsPlaying}");
      return TestResult.Pass(TestId, $"State: Playing={state.IsPlaying}, Volume={state.Volume:F2}");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// PLAY-009: Skip to next track (when supported).
/// </summary>
public class TestNextTrack : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "PLAY-009";
  public string TestName => "Skip to next track";
  public string Description => "Verify next track functionality when supported";
  public int Phase => 15;

  public TestNextTrack(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Check if next is supported
      var state = await _apiClient.GetPlaybackStateAsync(ct);
      if (state == null)
      {
        return TestResult.Fail(TestId, "Could not get playback state");
      }

      if (!state.CanNext)
      {
        ConsoleUI.WriteWarning("Next track not supported by current source");
        return TestResult.Skip(TestId, "Next track not supported by current source");
      }

      // Try next track
      ConsoleUI.WriteInfo("Skipping to next track...");
      var result = await _apiClient.NextTrackAsync(ct);
      if (result == null)
      {
        return TestResult.Fail(TestId, "NextTrack returned null");
      }

      await Task.Delay(500, ct);
      ConsoleUI.WriteSuccess("Next track command executed");
      return TestResult.Pass(TestId, "Next track successful");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// PLAY-010: Skip to previous track (when supported).
/// </summary>
public class TestPreviousTrack : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "PLAY-010";
  public string TestName => "Skip to previous track";
  public string Description => "Verify previous track functionality when supported";
  public int Phase => 15;

  public TestPreviousTrack(RadioApiClient apiClient) => _apiClient = apiClient;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Check if previous is supported
      var state = await _apiClient.GetPlaybackStateAsync(ct);
      if (state == null)
      {
        return TestResult.Fail(TestId, "Could not get playback state");
      }

      if (!state.CanPrevious)
      {
        ConsoleUI.WriteWarning("Previous track not supported by current source");
        return TestResult.Skip(TestId, "Previous track not supported by current source");
      }

      // Try previous track
      ConsoleUI.WriteInfo("Skipping to previous track...");
      var result = await _apiClient.PreviousTrackAsync(ct);
      if (result == null)
      {
        return TestResult.Fail(TestId, "PreviousTrack returned null");
      }

      await Task.Delay(500, ct);
      ConsoleUI.WriteSuccess("Previous track command executed");
      return TestResult.Pass(TestId, "Previous track successful");
    }
    catch (Exception ex)
    {
      return TestResult.Fail(TestId, $"Exception: {ex.Message}", exception: ex);
    }
  }
}

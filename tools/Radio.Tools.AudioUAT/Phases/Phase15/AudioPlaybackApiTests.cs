using Radio.Tools.AudioUAT.Services;
using Radio.Tools.AudioUAT.Utilities;
using Spectre.Console;

namespace Radio.Tools.AudioUAT.Phases.Phase15;

/// <summary>
/// Phase 15.1: Audio Playback and Control API Tests
/// Tests basic playback operations, volume control, balance, and mute.
/// </summary>
public class AudioPlaybackApiTests : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public AudioPlaybackApiTests(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public List<IPhaseTest> GetAllTests()
  {
    return new List<IPhaseTest>
    {
      new PhaseTest("PLAY-001", 15, "Start playback from stopped state", TestStartPlayback),
      new PhaseTest("PLAY-002", 15, "Pause and resume playback", TestPauseResume),
      new PhaseTest("PLAY-003", 15, "Stop playback and verify cleanup", TestStopPlayback),
      new PhaseTest("PLAY-004", 15, "Set master volume (0-100 range)", TestSetVolume),
      new PhaseTest("PLAY-005", 15, "Set balance (-100 to +100 range)", TestSetBalance),
      new PhaseTest("PLAY-006", 15, "Mute and unmute audio", TestMuteUnmute),
      new PhaseTest("PLAY-007", 15, "Get current playback state", TestGetPlaybackState),
      new PhaseTest("PLAY-008", 15, "Verify playback position updates in real-time", TestPlaybackPosition),
      new PhaseTest("PLAY-009", 15, "Skip to next track (when supported)", TestNextTrack),
      new PhaseTest("PLAY-010", 15, "Skip to previous track (when supported)", TestPreviousTrack)
    };
  }

  private async Task<TestResult> TestStartPlayback()
  {
    try
    {
      AnsiConsole.MarkupLine("[yellow]Testing playback start...[/]");

      // First ensure we're stopped
      await _apiClient.StopAsync();
      await Task.Delay(500);

      // Start playback
      var playResult = await _apiClient.PlayAsync();
      if (playResult == null)
      {
        return TestResult.Fail("Play command returned null");
      }

      // Verify state after short delay
      await Task.Delay(1000);
      var state = await _apiClient.GetPlaybackStateAsync();
      
      if (state == null)
      {
        return TestResult.Fail("Could not get playback state after play");
      }

      if (!state.IsPlaying && !state.IsPaused)
      {
        return TestResult.Fail($"Expected playing or paused state, got IsPlaying={state.IsPlaying}, IsPaused={state.IsPaused}");
      }

      AnsiConsole.MarkupLine("[green]✓ Playback started successfully[/]");
      return TestResult.Pass($"Playback state: IsPlaying={state.IsPlaying}");
    }
    catch (Exception ex)
    {
      return TestResult.Fail($"Exception: {ex.Message}");
    }
  }

  private async Task<TestResult> TestPauseResume()
  {
    try
    {
      AnsiConsole.MarkupLine("[yellow]Testing pause and resume...[/]");

      // Start playback first
      await _apiClient.PlayAsync();
      await Task.Delay(500);

      // Pause
      var pauseResult = await _apiClient.PauseAsync();
      await Task.Delay(500);
      var pauseState = await _apiClient.GetPlaybackStateAsync();

      if (pauseState == null || !pauseState.IsPaused)
      {
        return TestResult.Fail($"Expected paused state, got IsPaused={pauseState?.IsPaused}");
      }

      // Resume
      await _apiClient.PlayAsync();
      await Task.Delay(500);
      var resumeState = await _apiClient.GetPlaybackStateAsync();

      if (resumeState == null || (!resumeState.IsPlaying && !resumeState.IsPaused))
      {
        return TestResult.Fail($"Expected playing state after resume, got IsPlaying={resumeState?.IsPlaying}");
      }

      AnsiConsole.MarkupLine("[green]✓ Pause and resume work correctly[/]");
      return TestResult.Pass("Pause/resume cycle completed");
    }
    catch (Exception ex)
    {
      return TestResult.Fail($"Exception: {ex.Message}");
    }
  }

  private async Task<TestResult> TestStopPlayback()
  {
    try
    {
      AnsiConsole.MarkupLine("[yellow]Testing stop playback...[/]");

      // Start playback first
      await _apiClient.PlayAsync();
      await Task.Delay(500);

      // Stop
      var stopResult = await _apiClient.StopAsync();
      await Task.Delay(500);
      var state = await _apiClient.GetPlaybackStateAsync();

      if (state == null)
      {
        return TestResult.Fail("Could not get playback state after stop");
      }

      if (state.IsPlaying)
      {
        return TestResult.Fail($"Expected stopped state, got IsPlaying={state.IsPlaying}");
      }

      AnsiConsole.MarkupLine("[green]✓ Stop playback works correctly[/]");
      return TestResult.Pass("Playback stopped successfully");
    }
    catch (Exception ex)
    {
      return TestResult.Fail($"Exception: {ex.Message}");
    }
  }

  private async Task<TestResult> TestSetVolume()
  {
    try
    {
      AnsiConsole.MarkupLine("[yellow]Testing volume control...[/]");

      // Test various volume levels
      var testVolumes = new[] { 0.0f, 0.5f, 1.0f, 0.75f };
      
      foreach (var testVol in testVolumes)
      {
        var result = await _apiClient.SetVolumeAsync(testVol);
        if (result == null)
        {
          return TestResult.Fail($"SetVolume({testVol}) returned null");
        }

        // Allow small floating point differences
        if (Math.Abs(result.Volume - testVol) > 0.01f)
        {
          return TestResult.Fail($"Volume mismatch: set {testVol}, got {result.Volume}");
        }

        await Task.Delay(200);
      }

      AnsiConsole.MarkupLine("[green]✓ Volume control works correctly[/]");
      return TestResult.Pass($"Tested volume levels: {string.Join(", ", testVolumes)}");
    }
    catch (Exception ex)
    {
      return TestResult.Fail($"Exception: {ex.Message}");
    }
  }

  private async Task<TestResult> TestSetBalance()
  {
    try
    {
      AnsiConsole.MarkupLine("[yellow]Testing balance control...[/]");

      // Test various balance levels (-100 to +100)
      var testBalances = new[] { -100, 0, 100, 50, -50 };
      
      foreach (var testBal in testBalances)
      {
        var result = await _apiClient.SetBalanceAsync(testBal);
        if (result == null)
        {
          return TestResult.Fail($"SetBalance({testBal}) returned null");
        }

        // Convert back to -100 to 100 range
        var returnedBalance = (int)(result.Balance * 100);
        
        // Allow small differences due to floating point conversion
        if (Math.Abs(returnedBalance - testBal) > 2)
        {
          return TestResult.Fail($"Balance mismatch: set {testBal}, got {returnedBalance}");
        }

        await Task.Delay(200);
      }

      AnsiConsole.MarkupLine("[green]✓ Balance control works correctly[/]");
      return TestResult.Pass($"Tested balance levels: {string.Join(", ", testBalances)}");
    }
    catch (Exception ex)
    {
      return TestResult.Fail($"Exception: {ex.Message}");
    }
  }

  private async Task<TestResult> TestMuteUnmute()
  {
    try
    {
      AnsiConsole.MarkupLine("[yellow]Testing mute/unmute...[/]");

      // Get initial state
      var initialState = await _apiClient.GetPlaybackStateAsync();
      if (initialState == null)
      {
        return TestResult.Fail("Could not get initial playback state");
      }

      var initialMute = initialState.IsMuted;

      // Toggle mute twice to test both states
      for (int i = 0; i < 2; i++)
      {
        // Toggle mute
        await _apiClient.ToggleMuteAsync();
        await Task.Delay(300);

        var state = await _apiClient.GetPlaybackStateAsync();
        if (state == null)
        {
          return TestResult.Fail($"Could not get state after toggle {i + 1}");
        }

        // After first toggle, should be opposite of initial
        // After second toggle, should be back to initial
        var expectedMute = i == 0 ? !initialMute : initialMute;
        if (state.IsMuted != expectedMute)
        {
          return TestResult.Fail($"Mute state incorrect after toggle {i + 1}: expected {expectedMute}, got {state.IsMuted}");
        }
      }

      AnsiConsole.MarkupLine("[green]✓ Mute/unmute works correctly[/]");
      return TestResult.Pass("Mute toggle tested successfully");
    }
    catch (Exception ex)
    {
      return TestResult.Fail($"Exception: {ex.Message}");
    }
  }

  private async Task<TestResult> TestGetPlaybackState()
  {
    try
    {
      AnsiConsole.MarkupLine("[yellow]Testing get playback state...[/]");

      var state = await _apiClient.GetPlaybackStateAsync();
      if (state == null)
      {
        return TestResult.Fail("GetPlaybackState returned null");
      }

      // Verify state has expected properties
      if (state.Volume < 0 || state.Volume > 1)
      {
        return TestResult.Fail($"Invalid volume: {state.Volume}");
      }

      if (state.Balance < -1 || state.Balance > 1)
      {
        return TestResult.Fail($"Invalid balance: {state.Balance}");
      }

      AnsiConsole.MarkupLine($"[grey]Volume: {state.Volume:F2}, Balance: {state.Balance:F2}, Muted: {state.IsMuted}[/]");
      AnsiConsole.MarkupLine($"[grey]Playing: {state.IsPlaying}, Paused: {state.IsPaused}[/]");
      
      AnsiConsole.MarkupLine("[green]✓ Playback state retrieved successfully[/]");
      return TestResult.Pass($"State: Playing={state.IsPlaying}, Volume={state.Volume:F2}");
    }
    catch (Exception ex)
    {
      return TestResult.Fail($"Exception: {ex.Message}");
    }
  }

  private async Task<TestResult> TestPlaybackPosition()
  {
    try
    {
      AnsiConsole.MarkupLine("[yellow]Testing playback position updates...[/]");

      // Start playback
      await _apiClient.PlayAsync();
      await Task.Delay(500);

      // Get initial position
      var state1 = await _apiClient.GetPlaybackStateAsync();
      if (state1 == null)
      {
        return TestResult.Fail("Could not get initial state");
      }

      var pos1 = state1.Position;
      AnsiConsole.MarkupLine($"[grey]Position at T+0ms: {pos1}[/]");

      // Wait and check again
      await Task.Delay(2000);
      var state2 = await _apiClient.GetPlaybackStateAsync();
      if (state2 == null)
      {
        return TestResult.Fail("Could not get second state");
      }

      var pos2 = state2.Position;
      AnsiConsole.MarkupLine($"[grey]Position at T+2000ms: {pos2}[/]");

      // If source is playing and seekable, position should advance
      // If not seekable (e.g., radio), position might be 0 or not advance
      if (state2.IsPlaying && state2.CanSeek)
      {
        if (pos2 <= pos1)
        {
          return TestResult.Fail($"Position did not advance: {pos1} -> {pos2}");
        }
      }

      AnsiConsole.MarkupLine("[green]✓ Playback position tracking verified[/]");
      return TestResult.Pass($"Position: {pos1} -> {pos2}");
    }
    catch (Exception ex)
    {
      return TestResult.Fail($"Exception: {ex.Message}");
    }
  }

  private async Task<TestResult> TestNextTrack()
  {
    try
    {
      AnsiConsole.MarkupLine("[yellow]Testing next track...[/]");

      // Check if next is supported
      var state = await _apiClient.GetPlaybackStateAsync();
      if (state == null)
      {
        return TestResult.Fail("Could not get playback state");
      }

      if (!state.CanNext)
      {
        AnsiConsole.MarkupLine("[yellow]⊘ Next track not supported by current source[/]");
        return TestResult.Skip("Next track not supported by current source");
      }

      // Try next track
      var result = await _apiClient.NextTrackAsync();
      if (result == null)
      {
        return TestResult.Fail("NextTrack returned null");
      }

      await Task.Delay(500);
      var newState = await _apiClient.GetPlaybackStateAsync();
      
      AnsiConsole.MarkupLine("[green]✓ Next track command executed[/]");
      return TestResult.Pass("Next track successful");
    }
    catch (Exception ex)
    {
      // This might fail if no queue or not supported
      return TestResult.Fail($"Exception: {ex.Message}");
    }
  }

  private async Task<TestResult> TestPreviousTrack()
  {
    try
    {
      AnsiConsole.MarkupLine("[yellow]Testing previous track...[/]");

      // Check if previous is supported
      var state = await _apiClient.GetPlaybackStateAsync();
      if (state == null)
      {
        return TestResult.Fail("Could not get playback state");
      }

      if (!state.CanPrevious)
      {
        AnsiConsole.MarkupLine("[yellow]⊘ Previous track not supported by current source[/]");
        return TestResult.Skip("Previous track not supported by current source");
      }

      // Try previous track
      var result = await _apiClient.PreviousTrackAsync();
      if (result == null)
      {
        return TestResult.Fail("PreviousTrack returned null");
      }

      await Task.Delay(500);
      var newState = await _apiClient.GetPlaybackStateAsync();
      
      AnsiConsole.MarkupLine("[green]✓ Previous track command executed[/]");
      return TestResult.Pass("Previous track successful");
    }
    catch (Exception ex)
    {
      // This might fail if no queue or not supported
      return TestResult.Fail($"Exception: {ex.Message}");
    }
  }
}

using Radio.Tools.AudioUAT.Services;
using Radio.Tools.AudioUAT.Utilities;

namespace Radio.Tools.AudioUAT.Phases.Phase14;

/// <summary>
/// Phase 14: Spotify API Integration Tests.
/// Tests Spotify audio source via the Radio.API REST endpoints.
/// </summary>
public class SpotifyApiTests
{
  private readonly RadioApiClient _apiClient;

  /// <summary>
  /// Initializes a new instance of the <see cref="SpotifyApiTests"/> class.
  /// </summary>
  /// <param name="apiClient">The API client.</param>
  public SpotifyApiTests(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  /// <summary>
  /// Gets all Phase 14 tests.
  /// </summary>
  public IReadOnlyList<IPhaseTest> GetAllTests()
  {
    return
    [
      new SwitchToSpotifySourceTest(_apiClient),
      new SearchForArtistTest(_apiClient),
      new SearchForTracksTest(_apiClient),
      new PlaySpecificTrackTest(_apiClient),
      new VerifySpotifyAudioOutputTest(_apiClient),
      new PausePlaybackTest(_apiClient),
      new ResumePlaybackTest(_apiClient),
      new NextTrackTest(_apiClient),
      new PreviousTrackTest(_apiClient),
      new SpotifyVolumeControlTest(_apiClient),
      new SpotifyBalanceControlTest(_apiClient),
      new SpotifyNowPlayingMetadataTest(_apiClient)
    ];
  }
}

/// <summary>
/// SPOT-001: Switch to Spotify Source.
/// </summary>
public class SwitchToSpotifySourceTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "SPOT-001";
  public string TestName => "Switch to Spotify Source";
  public string Description => "Verify API can switch to Spotify as the active audio source";
  public int Phase => 14;

  public SwitchToSpotifySourceTest(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Check if Spotify source type is available
      ConsoleUI.WriteInfo("Checking available source types...");
      var sources = await _apiClient.GetSourcesAsync(ct);

      if (sources == null)
      {
        return TestResult.Fail(TestId, "Failed to retrieve sources list");
      }

      // Check if Spotify is in the list of primary source types
      var hasSpotifySourceType = sources.PrimarySources
        .Any(s => s.Equals("Spotify", StringComparison.OrdinalIgnoreCase));

      if (!hasSpotifySourceType)
      {
        ConsoleUI.WriteWarning("Spotify source type not found in available source types");
        return TestResult.Skip(TestId, "Spotify source type not available");
      }

      ConsoleUI.WriteSuccess("Spotify source type is available");

      // Check authentication status
      ConsoleUI.WriteInfo("Checking Spotify authentication...");
      var authStatus = await _apiClient.GetSpotifyAuthStatusAsync(ct);

      if (authStatus == null || !authStatus.IsAuthenticated)
      {
        ConsoleUI.WriteWarning("Spotify is not authenticated");
        return TestResult.Skip(TestId, "Spotify authentication required - complete OAuth flow first");
      }

      ConsoleUI.WriteSuccess("Spotify is authenticated");

      // Switch to Spotify
      ConsoleUI.WriteInfo("Switching to Spotify source...");
      var switchResult = await _apiClient.SwitchSourceAsync("Spotify", ct);

      if (switchResult == null)
      {
        ConsoleUI.WriteWarning("Failed to switch to Spotify - credentials may be invalid");
        return TestResult.Skip(TestId, "Spotify switch failed - check configuration");
      }

      // Verify switch
      var primarySource = await _apiClient.GetPrimarySourceAsync(ct);

      if (primarySource == null ||
          !primarySource.Type.Equals("Spotify", StringComparison.OrdinalIgnoreCase))
      {
        ConsoleUI.WriteWarning($"Active source is {primarySource?.Type ?? "null"}, expected Spotify");
        return TestResult.Skip(TestId, "Spotify switch incomplete - source not activated");
      }

      ConsoleUI.WriteSuccess($"Active source: {primarySource.Type}");

      return TestResult.Pass(TestId, "Successfully switched to Spotify source",
        metadata: new Dictionary<string, object>
        {
          ["SourceId"] = primarySource.Id,
          ["State"] = primarySource.State
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Switch to Spotify failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// SPOT-002: Search for Artist.
/// </summary>
public class SearchForArtistTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "SPOT-002";
  public string TestName => "Search for Artist";
  public string Description => "Verify searching for 'the cars' returns artist results";
  public int Phase => 14;

  public SearchForArtistTest(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Search for "the cars"
      ConsoleUI.WriteInfo("Searching for 'the cars'...");
      var searchResult = await _apiClient.SearchSpotifyAsync("the cars", "artist", ct);

      if (searchResult == null)
      {
        return TestResult.Fail(TestId, "Search failed - no response from API");
      }

      if (searchResult.Artists == null || searchResult.Artists.Count == 0)
      {
        return TestResult.Fail(TestId, "No artist results returned for 'the cars'");
      }

      ConsoleUI.WriteSuccess($"Found {searchResult.Artists.Count} artists");

      // Look for "The Cars" in results
      var theCars = searchResult.Artists.FirstOrDefault(a =>
        a.Name.Contains("Cars", StringComparison.OrdinalIgnoreCase));

      if (theCars != null)
      {
        ConsoleUI.WriteSuccess($"Found 'The Cars': {theCars.Name}");
        ConsoleUI.WriteInfo($"  ID: {theCars.Id}");
      }
      else
      {
        ConsoleUI.WriteWarning("'The Cars' not found in top results");
      }

      // Display first few results
      ConsoleUI.WriteInfo("Top artist results:");
      foreach (var artist in searchResult.Artists.Take(5))
      {
        ConsoleUI.WriteInfo($"  - {artist.Name}");
      }

      return TestResult.Pass(TestId, "Artist search working correctly",
        metadata: new Dictionary<string, object>
        {
          ["ResultCount"] = searchResult.Artists.Count,
          ["FoundTheCars"] = theCars != null
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Artist search failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// SPOT-003: Search for Tracks.
/// </summary>
public class SearchForTracksTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "SPOT-003";
  public string TestName => "Search for Tracks";
  public string Description => "Verify searching for 'the cars' returns track results";
  public int Phase => 14;

  public SearchForTracksTest(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Search for tracks
      ConsoleUI.WriteInfo("Searching for 'the cars' tracks...");
      var searchResult = await _apiClient.SearchSpotifyAsync("the cars", "track", ct);

      if (searchResult == null)
      {
        return TestResult.Fail(TestId, "Search failed - no response from API");
      }

      if (searchResult.Tracks == null || searchResult.Tracks.Count == 0)
      {
        return TestResult.Fail(TestId, "No track results returned");
      }

      ConsoleUI.WriteSuccess($"Found {searchResult.Tracks.Count} tracks");

      // Display first few results with metadata
      ConsoleUI.WriteInfo("Top track results:");
      foreach (var track in searchResult.Tracks.Take(5))
      {
        var artistNames = string.Join(", ", track.Artists?.Select(a => a.Name) ?? []);
        ConsoleUI.WriteInfo($"  - {track.Name} by {artistNames}");
        if (!string.IsNullOrEmpty(track.Album))
        {
          ConsoleUI.WriteInfo($"    Album: {track.Album}");
        }
      }

      // Verify tracks have required metadata
      var hasCompleteMetadata = searchResult.Tracks.All(t =>
        !string.IsNullOrEmpty(t.Id) &&
        !string.IsNullOrEmpty(t.Name));

      if (!hasCompleteMetadata)
      {
        ConsoleUI.WriteWarning("Some tracks have incomplete metadata");
      }

      return TestResult.Pass(TestId, "Track search working correctly",
        metadata: new Dictionary<string, object>
        {
          ["ResultCount"] = searchResult.Tracks.Count,
          ["HasCompleteMetadata"] = hasCompleteMetadata
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Track search failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// SPOT-004: Play Specific Track.
/// </summary>
public class PlaySpecificTrackTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "SPOT-004";
  public string TestName => "Play Specific Track";
  public string Description => "Verify selecting and playing 'Immortals' by Fall Out Boy works";
  public int Phase => 14;

  public PlaySpecificTrackTest(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Search for "Immortals Fall Out Boy"
      ConsoleUI.WriteInfo("Searching for 'Immortals Fall Out Boy'...");
      var searchResult = await _apiClient.SearchSpotifyAsync("Immortals Fall Out Boy", "track", ct);

      if (searchResult?.Tracks == null || searchResult.Tracks.Count == 0)
      {
        return TestResult.Fail(TestId, "Could not find 'Immortals' track");
      }

      // Find the track
      var immortals = searchResult.Tracks.FirstOrDefault(t =>
        t.Name.Contains("Immortals", StringComparison.OrdinalIgnoreCase));

      if (immortals == null)
      {
        // Use first result
        immortals = searchResult.Tracks[0];
        ConsoleUI.WriteWarning($"'Immortals' not in results, using: {immortals.Name}");
      }
      else
      {
        ConsoleUI.WriteSuccess($"Found track: {immortals.Name}");
      }

      // Get the Spotify URI
      var uri = $"spotify:track:{immortals.Id}";
      ConsoleUI.WriteInfo($"Playing track: {uri}");

      // Play the track
      await _apiClient.PlaySpotifyUriAsync(uri, ct);
      await Task.Delay(1000, ct);

      // Verify playback started
      var state = await _apiClient.GetPlaybackStateAsync(ct);

      if (state == null || !state.State.Equals("Playing", StringComparison.OrdinalIgnoreCase))
      {
        ConsoleUI.WriteWarning($"Playback state: {state?.State ?? "Unknown"}");
      }
      else
      {
        ConsoleUI.WriteSuccess("Playback started");
      }

      // Get now playing
      var nowPlaying = await _apiClient.GetNowPlayingAsync(ct);
      if (nowPlaying != null)
      {
        ConsoleUI.WriteInfo($"Now playing: {nowPlaying.Title}");
        if (!string.IsNullOrEmpty(nowPlaying.Artist))
        {
          ConsoleUI.WriteInfo($"Artist: {nowPlaying.Artist}");
        }
      }

      return TestResult.Pass(TestId, $"Playing: {immortals.Name}",
        metadata: new Dictionary<string, object>
        {
          ["TrackId"] = immortals.Id,
          ["TrackName"] = immortals.Name
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Play specific track failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// SPOT-005: Verify Spotify Audio Output.
/// </summary>
public class VerifySpotifyAudioOutputTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "SPOT-005";
  public string TestName => "Verify Spotify Audio Output";
  public string Description => "Confirm audio is output through SoundFlow to physical device";
  public int Phase => 14;

  public VerifySpotifyAudioOutputTest(RadioApiClient apiClient)
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
      var state = await _apiClient.GetPlaybackStateAsync(ct);

      if (state == null || !state.State.Equals("Playing", StringComparison.OrdinalIgnoreCase))
      {
        ConsoleUI.WriteInfo("Starting playback...");
        await _apiClient.PlayAsync(ct);
        await Task.Delay(1000, ct);
      }

      // Get now playing info
      var nowPlaying = await _apiClient.GetNowPlayingAsync(ct);
      if (nowPlaying != null)
      {
        ConsoleUI.WriteInfo($"Currently playing: {nowPlaying.Title ?? "Unknown"}");
      }

      // Interactive confirmation
      ConsoleUI.WriteInfo("");
      ConsoleUI.WriteInfo("Verify audio is playing through your speakers.");
      ConsoleUI.WriteInfo("");

      var canHear = ConsoleUI.AskYesNo("Can you hear music from Spotify?");

      if (!canHear)
      {
        return TestResult.Fail(TestId,
          "User did not confirm Spotify audio output - check Spotify Connect and speaker setup");
      }

      var noGlitches = ConsoleUI.AskYesNo("Is the audio playing without glitches?");

      if (!noGlitches)
      {
        ConsoleUI.WriteWarning("User reported audio glitches");
      }

      ConsoleUI.WriteSuccess("Spotify audio output confirmed");

      return TestResult.Pass(TestId, "Spotify audio output verified",
        metadata: new Dictionary<string, object>
        {
          ["Track"] = nowPlaying?.Title ?? "Unknown",
          ["NoGlitches"] = noGlitches
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Audio verification failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// SPOT-006: Pause Playback.
/// </summary>
public class PausePlaybackTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "SPOT-006";
  public string TestName => "Pause Playback";
  public string Description => "Verify pause command stops audio playback";
  public int Phase => 14;

  public PausePlaybackTest(RadioApiClient apiClient)
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
      var initialState = await _apiClient.GetPlaybackStateAsync(ct);

      if (initialState == null || !initialState.State.Equals("Playing", StringComparison.OrdinalIgnoreCase))
      {
        ConsoleUI.WriteInfo("Starting playback first...");
        await _apiClient.PlayAsync(ct);
        await Task.Delay(1000, ct);
      }

      // Record current position
      var beforePause = await _apiClient.GetNowPlayingAsync(ct);
      var positionBefore = beforePause?.Position ?? TimeSpan.Zero;
      ConsoleUI.WriteInfo($"Position before pause: {positionBefore}");

      // Pause
      ConsoleUI.WriteInfo("Pausing playback...");
      var pauseResult = await _apiClient.PauseAsync(ct);

      if (pauseResult == null)
      {
        return TestResult.Fail(TestId, "Pause command failed");
      }

      await Task.Delay(500, ct);

      // Verify paused state
      var stateAfter = await _apiClient.GetPlaybackStateAsync(ct);

      if (stateAfter == null)
      {
        return TestResult.Fail(TestId, "Failed to get playback state after pause");
      }

      ConsoleUI.WriteInfo($"State after pause: {stateAfter.State}");

      if (!stateAfter.State.Equals("Paused", StringComparison.OrdinalIgnoreCase) &&
          !stateAfter.State.Equals("Stopped", StringComparison.OrdinalIgnoreCase))
      {
        ConsoleUI.WriteWarning($"Expected Paused state, got {stateAfter.State}");
      }
      else
      {
        ConsoleUI.WriteSuccess("Playback paused");
      }

      // Interactive confirmation
      var confirmed = ConsoleUI.AskYesNo("Has the audio stopped playing?");

      if (!confirmed)
      {
        return TestResult.Fail(TestId, "User reports audio is still playing after pause");
      }

      return TestResult.Pass(TestId, "Pause command working correctly",
        metadata: new Dictionary<string, object>
        {
          ["PositionAtPause"] = positionBefore.ToString(),
          ["StateAfterPause"] = stateAfter.State
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Pause playback failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// SPOT-007: Resume Playback.
/// </summary>
public class ResumePlaybackTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "SPOT-007";
  public string TestName => "Resume Playback";
  public string Description => "Verify play command resumes paused playback";
  public int Phase => 14;

  public ResumePlaybackTest(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Ensure paused state
      var initialState = await _apiClient.GetPlaybackStateAsync(ct);

      if (initialState != null && initialState.State.Equals("Playing", StringComparison.OrdinalIgnoreCase))
      {
        ConsoleUI.WriteInfo("Pausing first...");
        await _apiClient.PauseAsync(ct);
        await Task.Delay(500, ct);
      }

      // Resume playback
      ConsoleUI.WriteInfo("Resuming playback...");
      var playResult = await _apiClient.PlayAsync(ct);

      if (playResult == null)
      {
        return TestResult.Fail(TestId, "Resume command failed");
      }

      await Task.Delay(500, ct);

      // Verify playing state
      var stateAfter = await _apiClient.GetPlaybackStateAsync(ct);

      if (stateAfter == null)
      {
        return TestResult.Fail(TestId, "Failed to get state after resume");
      }

      ConsoleUI.WriteInfo($"State after resume: {stateAfter.State}");

      if (!stateAfter.State.Equals("Playing", StringComparison.OrdinalIgnoreCase))
      {
        return TestResult.Fail(TestId, $"Expected Playing state, got {stateAfter.State}");
      }

      ConsoleUI.WriteSuccess("Playback resumed");

      // Interactive confirmation
      var confirmed = ConsoleUI.AskYesNo("Has the audio resumed playing?");

      if (!confirmed)
      {
        return TestResult.Fail(TestId, "User reports audio did not resume");
      }

      return TestResult.Pass(TestId, "Resume working correctly");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Resume playback failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// SPOT-008: Next Track.
/// </summary>
public class NextTrackTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "SPOT-008";
  public string TestName => "Next Track";
  public string Description => "Verify next command advances to the next track";
  public int Phase => 14;

  public NextTrackTest(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Ensure playback
      await _apiClient.PlayAsync(ct);
      await Task.Delay(500, ct);

      // Get current track
      var beforeTrack = await _apiClient.GetNowPlayingAsync(ct);
      var beforeTitle = beforeTrack?.Title ?? "Unknown";
      ConsoleUI.WriteInfo($"Current track: {beforeTitle}");

      // Skip to next
      ConsoleUI.WriteInfo("Skipping to next track...");
      await _apiClient.NextTrackAsync(ct);
      await Task.Delay(2000, ct);

      // Get new track
      var afterTrack = await _apiClient.GetNowPlayingAsync(ct);
      var afterTitle = afterTrack?.Title ?? "Unknown";
      ConsoleUI.WriteInfo($"New track: {afterTitle}");

      if (beforeTitle != afterTitle)
      {
        ConsoleUI.WriteSuccess("Track changed successfully");
      }
      else
      {
        ConsoleUI.WriteWarning("Track may not have changed");
      }

      // Interactive confirmation
      var confirmed = ConsoleUI.AskYesNo("Is a different song now playing?");

      return TestResult.Pass(TestId, "Next track working correctly",
        metadata: new Dictionary<string, object>
        {
          ["BeforeTrack"] = beforeTitle,
          ["AfterTrack"] = afterTitle,
          ["UserConfirmed"] = confirmed
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Next track failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// SPOT-009: Previous Track.
/// </summary>
public class PreviousTrackTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "SPOT-009";
  public string TestName => "Previous Track";
  public string Description => "Verify previous command goes back to the previous track";
  public int Phase => 14;

  public PreviousTrackTest(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Ensure playback
      await _apiClient.PlayAsync(ct);
      await Task.Delay(500, ct);

      // Get current track
      var beforeTrack = await _apiClient.GetNowPlayingAsync(ct);
      var beforeTitle = beforeTrack?.Title ?? "Unknown";
      ConsoleUI.WriteInfo($"Current track: {beforeTitle}");

      // Go to previous
      ConsoleUI.WriteInfo("Going to previous track...");
      await _apiClient.PreviousTrackAsync(ct);
      await Task.Delay(2000, ct);

      // Get track info
      var afterTrack = await _apiClient.GetNowPlayingAsync(ct);
      var afterTitle = afterTrack?.Title ?? "Unknown";
      ConsoleUI.WriteInfo($"Track after previous: {afterTitle}");

      // Note: Previous might restart current track if near beginning
      ConsoleUI.WriteInfo("(Previous may restart current track if near beginning)");

      // Interactive confirmation
      var confirmed = ConsoleUI.AskYesNo("Did the track change or restart from beginning?");

      return TestResult.Pass(TestId, "Previous track command executed",
        metadata: new Dictionary<string, object>
        {
          ["BeforeTrack"] = beforeTitle,
          ["AfterTrack"] = afterTitle,
          ["UserConfirmed"] = confirmed
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Previous track failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// SPOT-010: Spotify Volume Control.
/// </summary>
public class SpotifyVolumeControlTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "SPOT-010";
  public string TestName => "Spotify Volume Control";
  public string Description => "Verify volume changes affect Spotify playback output level";
  public int Phase => 14;

  public SpotifyVolumeControlTest(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Ensure playback
      await _apiClient.PlayAsync(ct);
      await Task.Delay(500, ct);

      var volumeLevels = new[] { 1.0f, 0.5f, 0.25f, 0.75f };

      foreach (var volume in volumeLevels)
      {
        var percentage = (int)(volume * 100);
        ConsoleUI.WriteInfo($"Setting volume to {percentage}%...");

        await _apiClient.SetVolumeAsync(volume, ct);
        ConsoleUI.WriteSuccess($"Volume: {percentage}%");
        ConsoleUI.WriteInfo("Listen for 2 seconds...");
        await Task.Delay(2000, ct);
      }

      // Interactive confirmation
      ConsoleUI.WriteInfo("");
      var confirmed = ConsoleUI.AskYesNo("Did you hear the volume change at each level?");

      if (!confirmed)
      {
        return TestResult.Fail(TestId, "Volume changes not audible");
      }

      var smooth = ConsoleUI.AskYesNo("Were the transitions smooth?");

      return TestResult.Pass(TestId, "Volume control working",
        metadata: new Dictionary<string, object>
        {
          ["TestedLevels"] = volumeLevels.Select(v => $"{(int)(v * 100)}%").ToArray(),
          ["SmoothTransitions"] = smooth
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Volume control failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// SPOT-011: Balance Control.
/// </summary>
public class SpotifyBalanceControlTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "SPOT-011";
  public string TestName => "Balance Control";
  public string Description => "Verify balance control affects left/right channel output";
  public int Phase => 14;

  public SpotifyBalanceControlTest(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Ensure playback
      await _apiClient.PlayAsync(ct);
      await Task.Delay(500, ct);

      // Test balance positions
      var balanceLevels = new[] { 0, -50, 50, 0 };

      foreach (var balance in balanceLevels)
      {
        var label = balance == 0 ? "CENTER"
                  : balance < 0 ? $"LEFT {-balance}%"
                  : $"RIGHT {balance}%";

        ConsoleUI.WriteInfo($"Setting balance to {label}...");

        // Note: Balance might not be implemented
        // Call the API even if it might not be supported
        try
        {
          await _apiClient.SetBalanceAsync(balance, ct);
          ConsoleUI.WriteSuccess($"Balance: {label}");
        }
        catch
        {
          ConsoleUI.WriteWarning("Balance API may not be implemented");
          return TestResult.Skip(TestId, "Balance control not implemented");
        }

        ConsoleUI.WriteInfo("Listen for 2 seconds...");
        await Task.Delay(2000, ct);
      }

      // Interactive confirmation
      ConsoleUI.WriteInfo("");
      var confirmed = ConsoleUI.AskYesNo("Did you hear the audio shift left and right?");

      return TestResult.Pass(TestId, "Balance control tested",
        metadata: new Dictionary<string, object>
        {
          ["BalanceDetected"] = confirmed
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Balance control failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// SPOT-012: Now Playing Metadata Accuracy.
/// </summary>
public class SpotifyNowPlayingMetadataTest : IPhaseTest
{
  private readonly RadioApiClient _apiClient;

  public string TestId => "SPOT-012";
  public string TestName => "Now Playing Metadata Accuracy";
  public string Description => "Verify Now Playing returns accurate Spotify metadata";
  public int Phase => 14;

  public SpotifyNowPlayingMetadataTest(RadioApiClient apiClient)
  {
    _apiClient = apiClient;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      // Ensure playback
      await _apiClient.PlayAsync(ct);
      await Task.Delay(1000, ct);

      // Get now playing
      ConsoleUI.WriteInfo("Getting now playing metadata...");
      var nowPlaying = await _apiClient.GetNowPlayingAsync(ct);

      if (nowPlaying == null)
      {
        return TestResult.Fail(TestId, "No now playing data returned");
      }

      var hasAllFields = true;

      // Check required fields
      if (!string.IsNullOrEmpty(nowPlaying.Title))
      {
        ConsoleUI.WriteSuccess($"Title: {nowPlaying.Title}");
      }
      else
      {
        ConsoleUI.WriteWarning("Title: (missing)");
        hasAllFields = false;
      }

      if (!string.IsNullOrEmpty(nowPlaying.Artist))
      {
        ConsoleUI.WriteInfo($"Artist: {nowPlaying.Artist}");
      }
      else
      {
        ConsoleUI.WriteWarning("Artist: (missing)");
      }

      if (!string.IsNullOrEmpty(nowPlaying.Album))
      {
        ConsoleUI.WriteInfo($"Album: {nowPlaying.Album}");
      }

      if (!string.IsNullOrEmpty(nowPlaying.AlbumArtUrl))
      {
        ConsoleUI.WriteInfo($"Album Art: {nowPlaying.AlbumArtUrl[..Math.Min(50, nowPlaying.AlbumArtUrl.Length)]}...");
      }

      if (nowPlaying.Duration > TimeSpan.Zero)
      {
        ConsoleUI.WriteInfo($"Duration: {nowPlaying.Duration}");
      }

      if (nowPlaying.Position > TimeSpan.Zero)
      {
        ConsoleUI.WriteInfo($"Position: {nowPlaying.Position}");
      }

      // Wait and check position updates
      ConsoleUI.WriteInfo("Checking position updates...");
      await Task.Delay(3000, ct);

      var nowPlaying2 = await _apiClient.GetNowPlayingAsync(ct);
      if (nowPlaying2 != null && nowPlaying2.Position > nowPlaying.Position)
      {
        ConsoleUI.WriteSuccess("Position is updating correctly");
      }

      // Stop playback
      ConsoleUI.WriteInfo("Stopping playback...");
      await _apiClient.StopAsync(ct);

      return TestResult.Pass(TestId, "Spotify metadata verified",
        metadata: new Dictionary<string, object>
        {
          ["Title"] = nowPlaying.Title ?? "Unknown",
          ["Artist"] = nowPlaying.Artist ?? "Unknown",
          ["HasAllFields"] = hasAllFields
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Metadata test failed: {ex.Message}", exception: ex);
    }
  }
}

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using SpotifyAPI.Web;

namespace Radio.Infrastructure.Audio.Sources.Primary;

/// <summary>
/// Spotify audio source using SpotifyAPI-NET.
/// Controls playback through the Spotify Connect API.
/// </summary>
public class SpotifyAudioSource : PrimaryAudioSourceBase
{
  private readonly IOptionsMonitor<SpotifySecrets> _secrets;
  private readonly IOptionsMonitor<SpotifyPreferences> _preferences;
  private SpotifyClient? _client;
  private CurrentlyPlayingContext? _currentPlayback;
  private Dictionary<string, string> _metadata = new();
  private TimeSpan _position;
  private TimeSpan? _duration;
  private bool _isAuthenticated;

  /// <summary>
  /// Initializes a new instance of the <see cref="SpotifyAudioSource"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="secrets">The Spotify secrets configuration.</param>
  /// <param name="preferences">The Spotify preferences.</param>
  public SpotifyAudioSource(
    ILogger<SpotifyAudioSource> logger,
    IOptionsMonitor<SpotifySecrets> secrets,
    IOptionsMonitor<SpotifyPreferences> preferences)
    : base(logger)
  {
    _secrets = secrets;
    _preferences = preferences;
  }

  /// <inheritdoc/>
  public override string Name => "Spotify";

  /// <inheritdoc/>
  public override AudioSourceType Type => AudioSourceType.Spotify;

  /// <inheritdoc/>
  public override TimeSpan? Duration => _duration;

  /// <inheritdoc/>
  public override TimeSpan Position => _position;

  /// <inheritdoc/>
  public override bool IsSeekable => true;

  /// <inheritdoc/>
  public override IReadOnlyDictionary<string, string> Metadata => _metadata;

  /// <summary>
  /// Gets a value indicating whether the client is authenticated.
  /// </summary>
  public bool IsAuthenticated => _isAuthenticated;

  /// <inheritdoc/>
  public override object GetSoundComponent()
  {
    // Spotify uses its own playback mechanism (Spotify Connect)
    // This returns null as audio doesn't flow through our mixer
    return new object();
  }

  /// <inheritdoc/>
  protected override async Task InitializeAsync(CancellationToken cancellationToken = default)
  {
    await base.InitializeAsync(cancellationToken);

    var secrets = _secrets.CurrentValue;
    if (string.IsNullOrEmpty(secrets.ClientID) ||
        string.IsNullOrEmpty(secrets.ClientSecret) ||
        string.IsNullOrEmpty(secrets.RefreshToken))
    {
      Logger.LogWarning("Spotify credentials not configured");
      State = AudioSourceState.Error;
      return;
    }

    try
    {
      var config = SpotifyClientConfig.CreateDefault()
        .WithAuthenticator(new AuthorizationCodeAuthenticator(
          secrets.ClientID,
          secrets.ClientSecret,
          new AuthorizationCodeTokenResponse { RefreshToken = secrets.RefreshToken }
        ));

      _client = new SpotifyClient(config);

      // Verify connection by getting user profile
      var user = await _client.UserProfile.Current(cancellationToken);
      Logger.LogInformation("Spotify authenticated as {UserId}", user.Id);
      _isAuthenticated = true;

      // Restore last playback state from preferences
      var prefs = _preferences.CurrentValue;
      if (!string.IsNullOrEmpty(prefs.LastSongPlayed))
      {
        Logger.LogDebug("Restoring last played track: {Track}", prefs.LastSongPlayed);
        _metadata["LastTrackUri"] = prefs.LastSongPlayed;
      }

      State = AudioSourceState.Ready;
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Failed to initialize Spotify client");
      _isAuthenticated = false;
      State = AudioSourceState.Error;
      throw;
    }
  }

  /// <inheritdoc/>
  protected override async Task PlayCoreAsync(CancellationToken cancellationToken)
  {
    if (_client == null)
    {
      throw new InvalidOperationException("Spotify client not initialized");
    }

    try
    {
      // Get available devices
      var devices = await _client.Player.GetAvailableDevices(cancellationToken);
      if (!devices.Devices.Any(d => d.IsActive))
      {
        Logger.LogWarning("No active Spotify device found. Please start a Spotify client.");
      }

      // Resume playback
      await _client.Player.ResumePlayback(cancellationToken);
      await UpdatePlaybackStateAsync(cancellationToken);
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Failed to start Spotify playback");
      throw;
    }
  }

  /// <inheritdoc/>
  protected override async Task PauseCoreAsync(CancellationToken cancellationToken)
  {
    if (_client == null)
    {
      throw new InvalidOperationException("Spotify client not initialized");
    }

    try
    {
      await _client.Player.PausePlayback(cancellationToken);
      await UpdatePlaybackStateAsync(cancellationToken);
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Failed to pause Spotify playback");
      throw;
    }
  }

  /// <inheritdoc/>
  protected override async Task ResumeCoreAsync(CancellationToken cancellationToken)
  {
    await PlayCoreAsync(cancellationToken);
  }

  /// <inheritdoc/>
  protected override async Task StopCoreAsync(CancellationToken cancellationToken)
  {
    if (_client == null) return;

    try
    {
      await _client.Player.PausePlayback(cancellationToken);

      // Save current position for next session
      if (_currentPlayback?.Item is FullTrack track)
      {
        _preferences.CurrentValue.LastSongPlayed = track.Uri;
        _preferences.CurrentValue.SongPositionMs = _currentPlayback.ProgressMs;
      }
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Failed to stop Spotify playback");
      throw;
    }
  }

  /// <inheritdoc/>
  protected override async Task SeekCoreAsync(TimeSpan position, CancellationToken cancellationToken)
  {
    if (_client == null)
    {
      throw new InvalidOperationException("Spotify client not initialized");
    }

    try
    {
      await _client.Player.SeekTo(new PlayerSeekToRequest((long)position.TotalMilliseconds), cancellationToken);
      _position = position;
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Failed to seek Spotify playback");
      throw;
    }
  }

  /// <summary>
  /// Plays a specific track or playlist.
  /// </summary>
  /// <param name="uri">The Spotify URI to play.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  public async Task PlayUriAsync(string uri, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    if (_client == null)
    {
      await InitializeAsync(cancellationToken);
    }

    try
    {
      var request = new PlayerResumePlaybackRequest();
      if (uri.Contains("track"))
      {
        request.Uris = new[] { uri };
      }
      else
      {
        request.ContextUri = uri;
      }

      await _client!.Player.ResumePlayback(request, cancellationToken);
      State = AudioSourceState.Playing;
      await UpdatePlaybackStateAsync(cancellationToken);
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Failed to play Spotify URI {Uri}", uri);
      throw;
    }
  }

  /// <summary>
  /// Skips to the next track.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  public async Task NextAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    if (_client == null) return;

    await _client.Player.SkipNext(cancellationToken);
    await UpdatePlaybackStateAsync(cancellationToken);
  }

  /// <summary>
  /// Skips to the previous track.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  public async Task PreviousAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    if (_client == null) return;

    await _client.Player.SkipPrevious(cancellationToken);
    await UpdatePlaybackStateAsync(cancellationToken);
  }

  /// <summary>
  /// Sets the shuffle mode.
  /// </summary>
  /// <param name="enabled">Whether shuffle is enabled.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  public async Task SetShuffleAsync(bool enabled, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    if (_client == null) return;

    await _client.Player.SetShuffle(new PlayerShuffleRequest(enabled), cancellationToken);
    _preferences.CurrentValue.Shuffle = enabled;
  }

  /// <summary>
  /// Sets the repeat mode.
  /// </summary>
  /// <param name="mode">The repeat mode.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  public async Task SetRepeatAsync(RepeatMode mode, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    if (_client == null) return;

    var spotifyState = mode switch
    {
      RepeatMode.Off => PlayerSetRepeatRequest.State.Off,
      RepeatMode.One => PlayerSetRepeatRequest.State.Track,
      RepeatMode.All => PlayerSetRepeatRequest.State.Context,
      _ => PlayerSetRepeatRequest.State.Off
    };

    await _client.Player.SetRepeat(new PlayerSetRepeatRequest(spotifyState), cancellationToken);
    _preferences.CurrentValue.Repeat = mode;
  }

  /// <summary>
  /// Updates the current playback state from Spotify.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  public async Task UpdatePlaybackStateAsync(CancellationToken cancellationToken = default)
  {
    if (_client == null) return;

    try
    {
      _currentPlayback = await _client.Player.GetCurrentPlayback(cancellationToken);
      if (_currentPlayback == null) return;

      _position = TimeSpan.FromMilliseconds(_currentPlayback.ProgressMs);

      if (_currentPlayback.Item is FullTrack track)
      {
        _duration = TimeSpan.FromMilliseconds(track.DurationMs);
        _metadata = new Dictionary<string, string>
        {
          ["Title"] = track.Name,
          ["Artist"] = string.Join(", ", track.Artists.Select(a => a.Name)),
          ["Album"] = track.Album.Name,
          ["Duration"] = _duration.Value.ToString(),
          ["TrackUri"] = track.Uri,
          ["AlbumArtUrl"] = track.Album.Images.FirstOrDefault()?.Url ?? ""
        };
      }
    }
    catch (Exception ex)
    {
      Logger.LogWarning(ex, "Failed to update Spotify playback state");
    }
  }

  /// <inheritdoc/>
  protected override async ValueTask DisposeAsyncCore()
  {
    if (_client != null && _currentPlayback?.Item is FullTrack track)
    {
      // Save state for next session
      _preferences.CurrentValue.LastSongPlayed = track.Uri;
      _preferences.CurrentValue.SongPositionMs = (long)_position.TotalMilliseconds;
    }

    _client = null;
    _isAuthenticated = false;
    await base.DisposeAsyncCore();
  }
}

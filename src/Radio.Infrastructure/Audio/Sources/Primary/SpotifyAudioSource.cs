using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoundFlow.Abstracts;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.SoundFlow;
using System.Runtime.InteropServices;
using Radio.Infrastructure.Audio.Services;
using SpotifyAPI.Web;

namespace Radio.Infrastructure.Audio.Sources.Primary;

/// <summary>
/// Spotify audio source with dual mode support:
/// - RemoteControl: Spotify Connect API (no audio data flows through app)
/// - Integrated: Managed librespot process with pipe audio capture (enables visualization)
/// </summary>
public class SpotifyAudioSource : PrimaryAudioSourceBase, IPlayQueue
{
  private readonly IOptionsMonitor<SpotifySecrets> _secrets;
  private readonly IOptionsMonitor<SpotifyPreferences> _preferences;
  private readonly IOptionsMonitor<DeviceOptions> _deviceOptions;
  private readonly SoundFlow.SoundFlowPlaybackService? _playbackService;
  private readonly ILoggerFactory? _loggerFactory;
  private SpotifyClient? _client;
  private CurrentlyPlayingContext? _currentPlayback;
  private Dictionary<string, object> _metadata = new();
  private TimeSpan _position;
  private TimeSpan? _duration;
  private bool _isAuthenticated;
  private Timer? _pollingTimer;
  private List<QueueItem> _queueItems = new();
  private int _currentIndex = -1;
  private readonly SemaphoreSlim _pollingLock = new(1, 1);
  private readonly object _queueLock = new(); // Protects _queueItems and _currentIndex
  private SpotifyMode _mode;
  private LibrespotManager? _librespotManager;
  private SpotifyIntegratedAudioSource? _integratedSource;

  /// <summary>
  /// Initializes a new instance of the <see cref="SpotifyAudioSource"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="secrets">The Spotify secrets configuration.</param>
  /// <param name="preferences">The Spotify preferences.</param>
  /// <param name="deviceOptions">The device configuration options.</param>
  /// <param name="playbackService">The SoundFlow playback service.</param>
  /// <param name="metricsCollector">Optional metrics collector for tracking playback metrics.</param>
  /// <param name="loggerFactory">Optional logger factory for creating typed loggers.</param>
  public SpotifyAudioSource(
    ILogger<SpotifyAudioSource> logger,
    IOptionsMonitor<SpotifySecrets> secrets,
    IOptionsMonitor<SpotifyPreferences> preferences,
    IOptionsMonitor<DeviceOptions> deviceOptions,
    SoundFlow.SoundFlowPlaybackService? playbackService = null,
    Radio.Core.Interfaces.IMetricsCollector? metricsCollector = null,
    ILoggerFactory? loggerFactory = null)
    : base(logger, metricsCollector)
  {
    _secrets = secrets;
    _preferences = preferences;
    _deviceOptions = deviceOptions;
    _playbackService = playbackService;
    _loggerFactory = loggerFactory;
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
  public override IReadOnlyDictionary<string, object> Metadata => _metadata;

  // Spotify supports next, previous, shuffle, and repeat
  /// <inheritdoc/>
  public override bool SupportsNext => true;

  /// <inheritdoc/>
  public override bool SupportsPrevious => true;

  /// <inheritdoc/>
  public override bool SupportsShuffle => true;

  /// <inheritdoc/>
  public override bool SupportsRepeat => true;

  /// <inheritdoc/>
  public override bool SupportsQueue => true;

  /// <inheritdoc/>
  public override bool IsShuffleEnabled => _preferences.CurrentValue.Shuffle;

  /// <inheritdoc/>
  public override RepeatMode RepeatMode => _preferences.CurrentValue.Repeat;

  /// <summary>
  /// Gets a value indicating whether the client is authenticated.
  /// </summary>
  public bool IsAuthenticated => _isAuthenticated;

  // IPlayQueue implementation

  /// <inheritdoc/>
  public IReadOnlyList<QueueItem> QueueItems
  {
    get
    {
      lock (_queueLock)
      {
        return _queueItems.ToList().AsReadOnly();
      }
    }
  }

  /// <inheritdoc/>
  public int CurrentIndex
  {
    get
    {
      lock (_queueLock)
      {
        return _currentIndex;
      }
    }
  }

  /// <inheritdoc/>
  public int Count
  {
    get
    {
      lock (_queueLock)
      {
        return _queueItems.Count;
      }
    }
  }

  /// <inheritdoc/>
  public event EventHandler<QueueChangedEventArgs>? QueueChanged;

  /// <inheritdoc/>
  public override object GetSoundComponent()
  {
    if (_mode == SpotifyMode.Integrated && _integratedSource != null)
    {
      // Integrated mode: Return SoundFlow audio source from librespot manager
      return _integratedSource.GetSoundComponent();
    }
    else
    {
      // Remote control mode: Spotify uses its own playback mechanism
      // This returns a placeholder as audio doesn't flow through our mixer
      return new object();
    }
  }

  /// <inheritdoc/>
  public override async Task InitializeAsync(CancellationToken cancellationToken = default)
  {
    await base.InitializeAsync(cancellationToken);

    _mode = _deviceOptions.CurrentValue.Spotify?.Mode ?? SpotifyMode.RemoteControl;
    Logger.LogInformation("Initializing Spotify in {Mode} mode", _mode);

    if (_mode == SpotifyMode.Integrated)
    {
      await InitializeIntegratedModeAsync(cancellationToken);
    }
    else
    {
      await InitializeRemoteControlModeAsync(cancellationToken);
    }
  }

  /// <summary>
  /// Initializes Spotify in integrated mode with managed librespot process.
  /// </summary>
  private async Task InitializeIntegratedModeAsync(CancellationToken cancellationToken)
  {
    Logger.LogInformation("Initializing Spotify integrated mode with librespot");

    // Create properly typed logger for LibrespotManager
    var librespotLogger = _loggerFactory?.CreateLogger<LibrespotManager>() 
      ?? throw new InvalidOperationException("LoggerFactory is required for Integrated mode");

    // Create and initialize the librespot manager
    _librespotManager = new LibrespotManager(
      librespotLogger,
      _secrets,
      _deviceOptions);

    // Create the integrated audio source wrapper
    _integratedSource = new SpotifyIntegratedAudioSource(
      Logger,
      _librespotManager,
      _playbackService);

    await _integratedSource.InitializeAsync(cancellationToken);

    // Start the librespot device
    await _librespotManager.StartDeviceAsync(cancellationToken: cancellationToken);

    // Initialize Spotify API for metadata and control
    await InitializeSpotifyAPIAsync(cancellationToken);

    State = AudioSourceState.Ready;
    Logger.LogInformation("Spotify integrated mode initialized successfully");
  }

  /// <summary>
  /// Initializes Spotify in remote control mode (original behavior).
  /// </summary>
  private async Task InitializeRemoteControlModeAsync(CancellationToken cancellationToken)
  {
    Logger.LogInformation("Initializing Spotify remote control mode");

    await InitializeSpotifyAPIAsync(cancellationToken);

    State = AudioSourceState.Ready;
  }

  /// <summary>
  /// Initializes the Spotify API client for metadata and playback control.
  /// </summary>
  private async Task InitializeSpotifyAPIAsync(CancellationToken cancellationToken)
  {
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

      // Start polling timer for playback state updates (every 2 seconds)
      _pollingTimer = new Timer(
        _ => PollPlaybackStateAsync().GetAwaiter().GetResult(),
        null,
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2));
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Failed to initialize Spotify client");
      _isAuthenticated = false;
      if (_mode == SpotifyMode.RemoteControl)
      {
        State = AudioSourceState.Error;
        throw;
      }
      // In loopback mode, continue even if API fails (audio will still work)
      Logger.LogWarning("Spotify API unavailable, metadata will be limited");
    }
  }

  /// <inheritdoc/>
  protected override async Task PlayCoreAsync(CancellationToken cancellationToken)
  {
    Logger.LogInformation("SpotifyAudioSource.PlayCoreAsync - Mode: {Mode}, IsAuthenticated: {IsAuth}", _mode, _isAuthenticated);
    
    if (_mode == SpotifyMode.Integrated)
    {
      // Integrated mode: librespot is already running, just ensure it's started
      if (_librespotManager == null)
      {
        Logger.LogError("PlayCoreAsync failed: Librespot manager not initialized");
        throw new InvalidOperationException("Librespot manager not initialized");
      }

      Logger.LogInformation("Integrated mode - Librespot running: {IsRunning}", _librespotManager.IsRunning);
      
      if (!_librespotManager.IsRunning)
      {
        Logger.LogInformation("Starting librespot device");
        await _librespotManager.StartDeviceAsync(cancellationToken: cancellationToken);
      }

      // Start the integrated audio source
      if (_integratedSource != null)
      {
        Logger.LogInformation("Starting integrated audio source");
        await _integratedSource.PlayAsync(cancellationToken);
      }
      else
      {
        Logger.LogWarning("Integrated source is null, cannot start playback");
      }

      // Optionally send play command via API if authenticated
      if (_client != null && _isAuthenticated)
      {
        try
        {
          Logger.LogInformation("Sending resume playback command to Spotify API");
          await _client.Player.ResumePlayback(cancellationToken);
        }
        catch (Exception ex)
        {
          Logger.LogWarning(ex, "Failed to send play command to Spotify API");
        }
      }
    }
    else
    {
      // Remote control mode: use API only
      Logger.LogInformation("Remote control mode - using Spotify API");
      
      if (_client == null)
      {
        Logger.LogError("PlayCoreAsync failed: Spotify client not initialized");
        throw new InvalidOperationException("Spotify client not initialized");
      }

      try
      {
        // Get available devices
        var devices = await _client.Player.GetAvailableDevices(cancellationToken);
        Logger.LogInformation("Found {DeviceCount} Spotify device(s)", devices.Devices.Count);
        
        var activeDevice = devices.Devices.FirstOrDefault(d => d.IsActive);
        if (activeDevice == null)
        {
          Logger.LogWarning("No active Spotify device found. Available devices: {Devices}", 
            string.Join(", ", devices.Devices.Select(d => $"{d.Name} ({d.Type})")));
        }
        else
        {
          Logger.LogInformation("Active Spotify device: {DeviceName} ({DeviceType})", activeDevice.Name, activeDevice.Type);
        }

        // Resume playback
        Logger.LogInformation("Sending resume playback command");
        await _client.Player.ResumePlayback(cancellationToken);
        await UpdatePlaybackStateAsync(cancellationToken);
      }
      catch (Exception ex)
      {
        Logger.LogError(ex, "Failed to start Spotify playback in remote control mode");
        throw;
      }
    }
    
    Logger.LogInformation("SpotifyAudioSource.PlayCoreAsync completed");
  }

  /// <inheritdoc/>
  protected override async Task PauseCoreAsync(CancellationToken cancellationToken)
  {
    if (_mode == SpotifyMode.Integrated && _integratedSource != null)
    {
      await _integratedSource.PauseAsync(cancellationToken);
    }

    if (_client == null) return;

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
    if (_mode == SpotifyMode.Integrated && _integratedSource != null)
    {
      await _integratedSource.StopAsync(cancellationToken);
    }

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

      try
      {
        await _client!.Player.ResumePlayback(request, cancellationToken);
      }
      catch (APIException ex) when (ex.Message.Contains("No active device found") || ex.Response?.StatusCode == System.Net.HttpStatusCode.NotFound)
      {
        Logger.LogWarning("No active Spotify device found. Attempting to find and activate device...");
        
        var deviceId = await GetBestDeviceIdAsync(cancellationToken);
        if (!string.IsNullOrEmpty(deviceId))
        {
          request.DeviceId = deviceId;
          await _client!.Player.ResumePlayback(request, cancellationToken);
          Logger.LogInformation("Activated Spotify device: {DeviceId}", deviceId);
        }
        else
        {
          throw;
        }
      }

      State = AudioSourceState.Playing;
      await UpdatePlaybackStateAsync(cancellationToken);
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Failed to play Spotify URI {Uri}", uri);
      throw;
    }
  }

  private async Task<string?> GetBestDeviceIdAsync(CancellationToken cancellationToken)
  {
      try 
      {
          var devices = await _client!.Player.GetAvailableDevices(cancellationToken);
          
          if (devices.Devices.Count == 0) return null;

          // 1. Try to find the integrated device by name
          if (_mode == SpotifyMode.Integrated)
          {
              // LibrespotManager defaults to "Radio Console"
              var integratedDevice = devices.Devices.FirstOrDefault(d => d.Name == "Radio Console"); 
              if (integratedDevice != null) return integratedDevice.Id;
          }
          
          // 2. Return currently active device
          var activeDevice = devices.Devices.FirstOrDefault(d => d.IsActive);
          if (activeDevice != null) return activeDevice.Id;
          
          // 3. Just return the first available device
          return devices.Devices.FirstOrDefault()?.Id;
      }
      catch (Exception ex)
      {
          Logger.LogWarning(ex, "Failed to retrieve Spotify devices");
          return null;
      }
  }

  /// <summary>
  /// Skips to the next track.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  public override async Task NextAsync(CancellationToken cancellationToken = default)
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
  public override async Task PreviousAsync(CancellationToken cancellationToken = default)
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
  public override async Task SetShuffleAsync(bool enabled, CancellationToken cancellationToken = default)
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
  public override async Task SetRepeatModeAsync(RepeatMode mode, CancellationToken cancellationToken = default)
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
        _metadata = new Dictionary<string, object>
        {
          [StandardMetadataKeys.Title] = track.Name,
          [StandardMetadataKeys.Artist] = string.Join(", ", track.Artists.Select(a => a.Name)),
          [StandardMetadataKeys.Album] = track.Album.Name,
          [StandardMetadataKeys.Duration] = _duration.Value,
          [StandardMetadataKeys.AlbumArtUrl] = track.Album.Images.FirstOrDefault()?.Url ?? StandardMetadataKeys.DefaultAlbumArtUrl,
          ["TrackUri"] = track.Uri
        };
      }
      else
      {
        // No track playing - set defaults
        _metadata = new Dictionary<string, object>
        {
          [StandardMetadataKeys.Title] = StandardMetadataKeys.DefaultTitle,
          [StandardMetadataKeys.Artist] = StandardMetadataKeys.DefaultArtist,
          [StandardMetadataKeys.Album] = StandardMetadataKeys.DefaultAlbum,
          [StandardMetadataKeys.AlbumArtUrl] = StandardMetadataKeys.DefaultAlbumArtUrl
        };
      }
    }
    catch (Exception ex)
    {
      Logger.LogWarning(ex, "Failed to update Spotify playback state");
    }
  }

  // IPlayQueue implementation

  /// <inheritdoc/>
  public async Task<IReadOnlyList<QueueItem>> GetQueueAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    if (_client == null)
    {
      return Array.Empty<QueueItem>();
    }

    try
    {
      var queue = await _client.Player.GetQueue(cancellationToken);
      var items = new List<QueueItem>();
      int index = 0;
      int currentIdx = -1;

      // Add currently playing track
      if (queue.CurrentlyPlaying is FullTrack currentTrack)
      {
        items.Add(CreateQueueItem(currentTrack, index++, true));
        currentIdx = 0; // Current track is at index 0
      }

      // Add upcoming tracks
      foreach (var item in queue.Queue)
      {
        if (item is FullTrack track)
        {
          items.Add(CreateQueueItem(track, index++, false));
        }
      }

      // Update internal queue state with proper locking
      lock (_queueLock)
      {
        _queueItems = items;
        _currentIndex = currentIdx;
      }

      return items;
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Failed to get Spotify queue");
      return Array.Empty<QueueItem>();
    }
  }

  /// <inheritdoc/>
  public async Task AddToQueueAsync(string trackIdentifier, int? position = null, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    if (_client == null)
    {
      throw new InvalidOperationException("Spotify client not initialized");
    }

    try
    {
      // Spotify API only supports adding to the end of the queue
      // The position parameter is ignored for Spotify
      await _client.Player.AddToQueue(new PlayerAddToQueueRequest(trackIdentifier), cancellationToken);
      
      Logger.LogInformation("Added track to Spotify queue: {TrackUri}", trackIdentifier);

      // Update queue and raise event
      var queue = await GetQueueAsync(cancellationToken);
      QueueChanged?.Invoke(this, new QueueChangedEventArgs
      {
        ChangeType = QueueChangeType.Added,
        AffectedIndex = queue.Count - 1,
        AffectedItem = queue.LastOrDefault()
      });
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Failed to add track to Spotify queue");
      throw;
    }
  }

  /// <inheritdoc/>
  public Task RemoveFromQueueAsync(int index, CancellationToken cancellationToken = default)
  {
    // Spotify API doesn't support removing specific items from the queue
    throw new NotSupportedException("Spotify API does not support removing individual items from the queue.");
  }

  /// <inheritdoc/>
  public Task ClearQueueAsync(CancellationToken cancellationToken = default)
  {
    // Spotify API doesn't support clearing the queue
    throw new NotSupportedException("Spotify API does not support clearing the queue.");
  }

  /// <inheritdoc/>
  public Task MoveQueueItemAsync(int fromIndex, int toIndex, CancellationToken cancellationToken = default)
  {
    // Spotify API doesn't support reordering queue items
    throw new NotSupportedException("Spotify API does not support reordering queue items.");
  }

  /// <inheritdoc/>
  public Task JumpToIndexAsync(int index, CancellationToken cancellationToken = default)
  {
    // Spotify API doesn't support jumping to a specific queue index
    // Users must use Next/Previous to navigate
    throw new NotSupportedException("Spotify API does not support jumping to a specific queue index. Use NextAsync() or PreviousAsync() to navigate.");
  }

  /// <summary>
  /// Polls the Spotify API for current playback state updates.
  /// Called every 2 seconds by the polling timer.
  /// </summary>
  private async Task PollPlaybackStateAsync()
  {
    if (_client == null || !_isAuthenticated)
    {
      return;
    }

    // Prevent concurrent polling
    if (!await _pollingLock.WaitAsync(0))
    {
      return;
    }

    try
    {
      var previousState = State;
      var previousPosition = _position;
      var previousTrackUri = _metadata.TryGetValue("TrackUri", out var uri) ? uri as string : null;

      await UpdatePlaybackStateAsync(CancellationToken.None);

      // Check if playback state changed
      if (State != previousState)
      {
        OnStateChanged(previousState, State);
      }

      // Check if track changed
      var currentTrackUri = _metadata.TryGetValue("TrackUri", out var currentUri) ? currentUri as string : null;
      if (currentTrackUri != previousTrackUri && !string.IsNullOrEmpty(currentTrackUri))
      {
        // Save last played track
        _preferences.CurrentValue.LastSongPlayed = currentTrackUri;
        _preferences.CurrentValue.SongPositionMs = (long)_position.TotalMilliseconds;

        // Update queue and raise event
        QueueChanged?.Invoke(this, new QueueChangedEventArgs
        {
          ChangeType = QueueChangeType.CurrentChanged
        });

        Logger.LogDebug("Track changed to: {TrackUri}", currentTrackUri);
      }

      // Note: Position updates are reflected in the Position property
      // No need to raise state changed event for position updates alone
    }
    catch (Exception ex)
    {
      Logger.LogDebug(ex, "Error during Spotify playback state polling");
    }
    finally
    {
      _pollingLock.Release();
    }
  }

  /// <summary>
  /// Creates a QueueItem from a Spotify track.
  /// </summary>
  private QueueItem CreateQueueItem(FullTrack track, int index, bool isCurrent)
  {
    return new QueueItem
    {
      Id = track.Uri,
      Title = track.Name,
      Artist = string.Join(", ", track.Artists.Select(a => a.Name)),
      Album = track.Album.Name,
      Duration = TimeSpan.FromMilliseconds(track.DurationMs),
      AlbumArtUrl = track.Album.Images.FirstOrDefault()?.Url,
      Index = index,
      IsCurrent = isCurrent
    };
  }

  /// <inheritdoc/>
  protected override async ValueTask DisposeAsyncCore()
  {
    // Stop polling timer
    if (_pollingTimer != null)
    {
      await _pollingTimer.DisposeAsync();
      _pollingTimer = null;
    }

    _pollingLock.Dispose();

    // Dispose integrated source and manager if present
    if (_integratedSource != null)
    {
      await _integratedSource.DisposeAsync();
      _integratedSource = null;
    }

    if (_librespotManager != null)
    {
      await _librespotManager.DisposeAsync();
      _librespotManager = null;
    }

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

  /// <summary>
  /// Internal helper class for capturing audio from integrated librespot process.
  /// </summary>
  private class SpotifyIntegratedAudioSource : PrimaryAudioSourceBase
  {
    private readonly LibrespotManager _manager;
    private readonly SoundFlow.SoundFlowPlaybackService? _playbackService;
    private readonly Dictionary<string, object> _metadata = new();
    private LibrespotAudioDataProvider? _audioProvider;
    private BufferedSoundGenerator<short>? _soundGenerator;
    private string? _playbackId;

    public SpotifyIntegratedAudioSource(
      ILogger logger,
      LibrespotManager manager,
      SoundFlow.SoundFlowPlaybackService? playbackService)
      : base(logger)
    {
      _manager = manager;
      _playbackService = playbackService;
    }

    public override string Name => "Spotify (Integrated)";
    public override AudioSourceType Type => AudioSourceType.Spotify;
    public override TimeSpan? Duration => null; // Live stream has no duration
    public override TimeSpan Position => TimeSpan.Zero; // Live stream position
    public override bool IsSeekable => false; // Cannot seek live stream
    public override IReadOnlyDictionary<string, object> Metadata => _metadata;

    public override object GetSoundComponent()
    {
      if (_soundGenerator != null) return _soundGenerator;

      // Fallback for non-SoundFlow usage (though this path is largely legacy now)
      if (_audioProvider == null)
      {
         throw new InvalidOperationException(
          "Audio provider not initialized. Call InitializeAsync before GetSoundComponent.");
      }
      return _audioProvider;
    }

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
      await base.InitializeAsync(cancellationToken);
      
      // Set default metadata
      _metadata[StandardMetadataKeys.Title] = "Spotify (Integrated)";
      _metadata[StandardMetadataKeys.Artist] = "Librespot";
      _metadata[StandardMetadataKeys.Album] = "Spotify";
      
      // Create the audio data provider that bridges LibrespotManager to SoundFlow
      _audioProvider = new LibrespotAudioDataProvider(_manager, Logger);

      // If we have the playback service, initialize the SoundGenerator
      if (_playbackService != null)
      {
         var engine = _playbackService.GetUnderlyingEngine();
         if (engine != null)
         {
              var format = _playbackService.GetAudioFormat();
              _soundGenerator = new BufferedSoundGenerator<short>(engine, format, Logger);
              // We need to wire up the audio provider to the sound generator
              // But LibrespotAudioDataProvider outputs byte[], and BufferedSoundGenerator<short> needs short[]
              // LibrespotAudioDataProvider is designed to be polled or event driven?
              // The LibrespotManager emits AudioDataReceived.
              
              // We can hook into the LibrespotManager event directly here or use the provider.
              // Let's hook the generator directly to the manager's event for efficiency,
              // mimicking how SDRRadioAudioSource does it.
              
              _manager.AudioDataReceived += OnAudioDataReceived;
         }
      }
      
      State = AudioSourceState.Ready;
      Logger.LogInformation("Spotify integrated audio source initialized with audio provider");
    }

    private void OnAudioDataReceived(object? sender, AudioDataEventArgs e) {
        if (_soundGenerator == null) return;
        
        // Convert byte[] to short[]
        // Librespot is 16-bit signed, little-endian, stereo (interleaved)
        // e.AudioData is the raw bytes.
        
        var byteSpan = new ReadOnlySpan<byte>(e.AudioData);
        var shortCount = byteSpan.Length / 2;
        var shortSpan = MemoryMarshal.Cast<byte, short>(byteSpan);
        
        // We need to pass a ReadOnlySpan<short>
        _soundGenerator.AddSamples(shortSpan);
    }

    protected override async Task PlayCoreAsync(CancellationToken cancellationToken)
    {
      State = AudioSourceState.Playing;
      
      // Start polling for audio data (legacy support if soundgenerator not used)
      _audioProvider?.StartPolling();

      // Ensure SoundFlow playback if service is available
      if (_playbackService != null && _soundGenerator != null)
      {
           _playbackId = $"spotify-integrated-{Guid.NewGuid():N}";
           var success = await _playbackService.PlayComponentAsync(
                _playbackId,
                _soundGenerator,
                1.0f, // Volume handled by source base class? No, integrated source doesn't expose volume separately properly yet, assuming 1.0
                cancellationToken);
           
           if (success) {
               Logger.LogInformation("Spotify integrated audio routed through SoundFlow");
           } else {
               Logger.LogError("Failed to verify Spotify SoundFlow playback");
           }
      }
      
      return;
    }

    protected override async Task PauseCoreAsync(CancellationToken cancellationToken)
    {
      State = AudioSourceState.Paused;
      
      // Stop polling for audio data
      _audioProvider?.StopPolling();

      // Mute/Pause soundflow logic
      // Ideally we'd pause the playback service stream, but for now we might just let it run silence or rely on the source mute.
      // But PlayComponentAsync starts a fresh playback. 
      // SoundFlow `PauseAsync` should be called if we had an ID.
      // But this integrated source is a sub-class and doesn't fully own the lifecycle in the same way SDR does at the top level?
      // Wait, SDRRadioAudioSource uses `_playbackService.StopAsync`.
      
      // We should probably do similar if we want to release the mixer slot.
      // For now, let's just leave the component active but stop feeding it data (which happens because Librespot pauses).
      // However, if we want to stop the mixer from processing silence, we could remove it.
      
      // Kept simple: Librespot stops sending data, buffer drains, silence ensues.
      
      return;
    }

    protected override async Task ResumeCoreAsync(CancellationToken cancellationToken)
    {
      State = AudioSourceState.Playing;
      
      // Resume polling for audio data
      _audioProvider?.StartPolling();
      
      // If we killed the playback in Pause, we'd need to restart it here.
      // Since we didn't, we are good.
      
      return;
    }

    protected override async Task StopCoreAsync(CancellationToken cancellationToken)
    {
      State = AudioSourceState.Stopped;
      
      // Stop polling for audio data
      _audioProvider?.StopPolling();

      if (_playbackService != null && _playbackId != null) 
      {
          await _playbackService.StopAsync(_playbackId, cancellationToken);
          _playbackId = null;
      }
      
      return;
    }

    protected override Task SeekCoreAsync(TimeSpan position, CancellationToken cancellationToken)
    {
      throw new NotSupportedException("Cannot seek in integrated Spotify stream");
    }

    protected override async ValueTask DisposeAsyncCore()
    {
      if (_manager != null)
      {
         _manager.AudioDataReceived -= OnAudioDataReceived;
      }

      _audioProvider?.Dispose();
      _audioProvider = null;

      if (_playbackService != null && _playbackId != null) 
      {
           await _playbackService.StopAsync(_playbackId);
      }
      
      await base.DisposeAsyncCore();
    }
  }
}

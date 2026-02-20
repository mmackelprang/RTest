using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Sharpcaster;
using Sharpcaster.Channels;
using Sharpcaster.Models;
using Sharpcaster.Models.ChromecastStatus;
using Sharpcaster.Models.Media;

namespace Radio.Infrastructure.Audio.Outputs;

/// <summary>
/// Google Chromecast audio output implementation using SharpCaster.
/// Streams audio to Chromecast devices via HTTP stream endpoint.
/// </summary>
public class GoogleCastOutput : AudioOutputBase
{
  private readonly ILogger<GoogleCastOutput> _logger;
  private readonly GoogleCastOutputOptions _options;
  private readonly CastDeviceCacheRepository? _cacheRepository;
  private ChromecastClient? _client;
  private ChromecastReceiver? _connectedReceiver;
  private string? _streamUrl;

  // Direct Channel streaming (experimental)
  private IAudioEngine? _audioEngine;
  private DirectCastStreamingService? _directStreaming;
  private DirectCastAudioChannel? _directChannel;

  // Cache discovered receivers to use the original objects for connection
  // Indexed by device ID (DeviceUri.ToString()) and also by IP address for fallback matching
  private readonly Dictionary<string, ChromecastReceiver> _discoveredReceivers = new();
  private readonly Dictionary<string, ChromecastReceiver> _discoveredReceiversByIp = new();
  private CastNowPlayingMetadata? _nowPlayingMetadata;

  // Metadata update debounce: when metadata changes rapidly (source switch
  // → "No Track" → actual track → fingerprint ID), coalesce into a single
  // Cast media reload to avoid garbled audio from rapid reconnections.
  private CancellationTokenSource? _metadataDebouncesCts;
  private readonly object _debounceLock = new();

  // Volume sync: track locally-initiated volume changes to filter out echo events
  private float _lastSetVolume = -1f;
  private bool _lastSetMute;
  private bool _suppressNextVolumeEvent;

  /// <inheritdoc />
  protected override ILogger Logger => _logger;

  /// <inheritdoc />
  public override AudioOutputType Type => AudioOutputType.GoogleCast;

  /// <summary>
  /// Event raised when a Chromecast device is discovered.
  /// </summary>
  public event EventHandler<ChromecastDeviceDiscoveredEventArgs>? DeviceDiscovered;

  /// <summary>
  /// Event raised when connected to a Chromecast device.
  /// </summary>
  public event EventHandler<ChromecastConnectedEventArgs>? Connected;

  /// <summary>
  /// Event raised when disconnected from a Chromecast device.
  /// </summary>
  public event EventHandler<ChromecastDisconnectedEventArgs>? Disconnected;

  /// <summary>
  /// Event raised when the Cast device volume or mute state changes externally
  /// (e.g., via Google Home app, voice command, or physical controls).
  /// Not fired for changes initiated by this application.
  /// </summary>
  public event EventHandler<CastVolumeChangedEventArgs>? CastVolumeChanged;

  /// <summary>
  /// Gets the currently connected Chromecast device information.
  /// </summary>
  public ChromecastDeviceInfo? ConnectedDevice { get; private set; }

  /// <summary>
  /// Gets the Google Cast output options for external inspection (e.g., by controllers
  /// to determine the streaming mode).
  /// </summary>
  public GoogleCastOutputOptions Options => _options;

  /// <summary>
  /// Initializes a new instance of the <see cref="GoogleCastOutput"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="options">The Google Cast output options.</param>
  /// <param name="cacheRepository">Optional SQLite-backed cache repository.</param>
  public GoogleCastOutput(
    ILogger<GoogleCastOutput> logger,
    IOptions<AudioOutputOptions> options,
    CastDeviceCacheRepository? cacheRepository = null)
    : base("cast-output", "Google Cast Output",
        options?.Value?.GoogleCast?.DefaultVolume ?? 0.7f,
        options?.Value?.GoogleCast?.Enabled ?? false)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _options = options?.Value?.GoogleCast ?? throw new ArgumentNullException(nameof(options));
    _cacheRepository = cacheRepository;
  }

  /// <inheritdoc />
  protected override void OnVolumeChanged(float volume)
  {
    // Apply volume to connected device if available
    _ = SetCastVolumeAsync(volume);
  }

  /// <inheritdoc />
  protected override void OnMuteChanged(bool muted)
  {
    // Apply mute state to connected device if available
    _ = SetCastMuteAsync(muted);
  }

  /// <inheritdoc />
  public override async Task InitializeAsync(CancellationToken cancellationToken = default)
  {
    ValidateCanInitialize();

    State = AudioOutputState.Initializing;

    try
    {
      _logger.LogInformation("Initializing Google Cast output");

      // Dispose old client if reinitializing after error
      if (_client != null)
      {
        try { await _client.DisconnectAsync(); } catch { }
      }

      // Create a fresh ChromecastClient
      _client = new ChromecastClient();
      _connectedReceiver = null;
      ConnectedDevice = null;

      State = AudioOutputState.Ready;
      _logger.LogInformation("Google Cast output initialized");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to initialize Google Cast output");
      State = AudioOutputState.Error;
      throw;
    }
  }

  /// <summary>
  /// Returns cached Cast devices without running mDNS discovery.
  /// Fast — no network I/O. Removes stale entries before returning.
  /// </summary>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>A list of cached Chromecast devices.</returns>
  public async Task<IReadOnlyList<ChromecastDeviceInfo>> GetCachedDevicesAsync(
    CancellationToken ct = default)
  {
    ThrowIfDisposed();

    var cachedDevices = await LoadCacheAsync();

    // Remove stale entries
    var expirationCutoff = DateTime.UtcNow.AddDays(-_options.CacheExpirationDays);
    var staleKeys = cachedDevices
      .Where(kv => kv.Value.LastSeen < expirationCutoff)
      .Select(kv => kv.Key)
      .ToList();
    foreach (var key in staleKeys)
    {
      cachedDevices.Remove(key);
    }

    _logger.LogDebug("Returning {Count} cached Cast devices", cachedDevices.Count);
    return cachedDevices.Values.Select(c => c.Device).ToList();
  }

  /// <summary>
  /// Discovers available Chromecast devices on the network.
  /// Merges live mDNS results with a persistent cache so previously seen
  /// devices remain available even if they are temporarily offline.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A list of discovered Chromecast devices.</returns>
  public async Task<IReadOnlyList<ChromecastDeviceInfo>> DiscoverDevicesAsync(
    CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    _logger.LogInformation(
      "Starting Chromecast device discovery (timeout: {Timeout}s)",
      _options.DiscoveryTimeoutSeconds);

    // Load persistent cache first (SQLite if available, fallback to JSON)
    var cachedDevices = await LoadCacheAsync();

    try
    {
      ChromecastLocator locator = new ChromecastLocator();
      var discoveredDevices = await locator.FindReceiversAsync(TimeSpan.FromSeconds(10));

      foreach (var device in discoveredDevices)
      {
        if (device?.DeviceUri == null)
        {
          _logger.LogDebug("Skipping discovered device with null DeviceUri");
          continue;
        }

        var deviceId = device.DeviceUri.ToString();

        var deviceInfo = new ChromecastDeviceInfo
        {
          Id = deviceId,
          FriendlyName = device.Name ?? "Unknown",
          IpAddress = device.DeviceUri.Host,
          Port = device.DeviceUri.Port,
          Model = device.Model ?? "Unknown"
        };

        // Update or add to cache
        cachedDevices[deviceId] = new CachedCastDevice
        {
          Device = deviceInfo,
          LastSeen = DateTime.UtcNow
        };

        // Keep the live receiver for connection (indexed by ID and by IP for fallback)
        _discoveredReceivers[deviceId] = device;
        _discoveredReceiversByIp[deviceInfo.IpAddress] = device;

        DeviceDiscovered?.Invoke(this, new ChromecastDeviceDiscoveredEventArgs { Device = deviceInfo });

        _logger.LogDebug(
          "Discovered Chromecast: {Name} at {IP}:{Port}",
          deviceInfo.FriendlyName, deviceInfo.IpAddress, deviceInfo.Port);
      }

      _logger.LogInformation("Discovered {Count} Chromecast device(s) via mDNS, {CacheCount} total cached",
        discoveredDevices.Count(), cachedDevices.Count);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error during Chromecast device discovery");
    }

    // Remove stale entries
    var expirationCutoff = DateTime.UtcNow.AddDays(-_options.CacheExpirationDays);
    var staleKeys = cachedDevices
      .Where(kv => kv.Value.LastSeen < expirationCutoff)
      .Select(kv => kv.Key)
      .ToList();
    foreach (var key in staleKeys)
    {
      if (cachedDevices.TryGetValue(key, out var stale))
      {
        _discoveredReceiversByIp.Remove(stale.Device.IpAddress);
      }
      cachedDevices.Remove(key);
      _discoveredReceivers.Remove(key);
    }

    // Save merged cache
    await SaveCacheAsync(cachedDevices);

    return cachedDevices.Values.Select(c => c.Device).ToList();
  }

  /// <summary>
  /// Loads the persistent device cache. Uses SQLite if available, falling back to JSON.
  /// Migrates JSON data to SQLite on first load.
  /// </summary>
  private async Task<Dictionary<string, CachedCastDevice>> LoadCacheAsync()
  {
    // Try SQLite first
    if (_cacheRepository != null)
    {
      var sqliteCache = await _cacheRepository.GetAllAsync();

      // One-time migration: if JSON file exists and SQLite is empty, import from JSON
      if (sqliteCache.Count == 0 && !string.IsNullOrEmpty(_options.CacheFilePath) &&
          File.Exists(_options.CacheFilePath))
      {
        try
        {
          var json = await File.ReadAllTextAsync(_options.CacheFilePath);
          var jsonCache = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, CachedCastDevice>>(json);
          if (jsonCache != null && jsonCache.Count > 0)
          {
            _logger.LogInformation("Migrating {Count} Cast devices from JSON to SQLite", jsonCache.Count);
            await _cacheRepository.SaveAllAsync(jsonCache);
            sqliteCache = jsonCache;

            // Remove the old JSON file after successful migration
            File.Delete(_options.CacheFilePath);
            _logger.LogInformation("Deleted old JSON cache file: {Path}", _options.CacheFilePath);
          }
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "Failed to migrate Cast device cache from JSON to SQLite");
        }
      }

      return sqliteCache;
    }

    // Fallback to JSON file
    try
    {
      if (!string.IsNullOrEmpty(_options.CacheFilePath) && File.Exists(_options.CacheFilePath))
      {
        var json = await File.ReadAllTextAsync(_options.CacheFilePath);
        var cached = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, CachedCastDevice>>(json);
        if (cached != null)
        {
          _logger.LogDebug("Loaded {Count} cached Cast devices from JSON", cached.Count);
          return cached;
        }
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to load Cast device cache");
    }

    return new Dictionary<string, CachedCastDevice>();
  }

  /// <summary>
  /// Saves the device cache. Uses SQLite if available, falling back to JSON.
  /// </summary>
  private async Task SaveCacheAsync(Dictionary<string, CachedCastDevice> cache)
  {
    if (_cacheRepository != null)
    {
      await _cacheRepository.SaveAllAsync(cache);
      var expirationCutoff = DateTime.UtcNow.AddDays(-_options.CacheExpirationDays);
      await _cacheRepository.RemoveStaleAsync(expirationCutoff);
      return;
    }

    // Fallback to JSON file
    try
    {
      var directory = Path.GetDirectoryName(_options.CacheFilePath);
      if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
      {
        Directory.CreateDirectory(directory);
      }

      var json = System.Text.Json.JsonSerializer.Serialize(cache, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
      await File.WriteAllTextAsync(_options.CacheFilePath!, json);
      _logger.LogDebug("Saved {Count} Cast devices to JSON cache", cache.Count);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to save Cast device cache");
    }
  }

  /// <summary>
  /// Connects to a specific Chromecast device.
  /// </summary>
  /// <param name="device">The device to connect to.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  public async Task ConnectAsync(ChromecastDeviceInfo device, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(device);

    // Recover from Error state by reinitializing
    if (State == AudioOutputState.Error)
    {
      _logger.LogInformation("Recovering from Error state before connecting");
      await InitializeAsync(cancellationToken);
    }

    // Stop streaming before switching to a new device, and remember to restart after
    var wasStreaming = State == AudioOutputState.Streaming;
    if (wasStreaming)
    {
      _logger.LogInformation("Stopping current Cast stream before connecting to new device");
      await StopAsync(cancellationToken);
    }

    if (State != AudioOutputState.Ready && State != AudioOutputState.Stopped)
    {
      throw new InvalidOperationException(
        $"Cannot connect in state {State}. Output must be in Ready or Stopped state.");
    }

    State = AudioOutputState.Connecting;

    try
    {
      _logger.LogInformation(
        "Connecting to Chromecast: {Name} at {IP}:{Port}",
        device.FriendlyName, device.IpAddress, device.Port);

      // Try to get the live receiver from discovery (preferred - uses original SharpCaster object)
      // First try by exact ID, then by IP address (IDs can differ due to URI normalization)
      if (_discoveredReceivers.TryGetValue(device.Id, out _connectedReceiver))
      {
        _logger.LogDebug("Using live ChromecastReceiver from discovery (matched by ID)");
      }
      else if (_discoveredReceiversByIp.TryGetValue(device.IpAddress, out _connectedReceiver))
      {
        _logger.LogInformation("Live ChromecastReceiver matched by IP {IP} (ID mismatch: cached={CachedId}, live={LiveId})",
          device.IpAddress, device.Id, _connectedReceiver.DeviceUri);
      }
      else
      {
        // Device is from persistent cache, not live discovery.
        // Verify reachability with a TCP connect check before attempting full connection.
        var connectPort = await FindReachablePortAsync(device, cancellationToken);

        var deviceUri = new Uri($"https://{device.IpAddress}:{connectPort}");
        _logger.LogInformation("TCP check passed, creating ChromecastReceiver from cache: {Uri}", deviceUri);

        // Create a fresh ChromecastClient for cached connections to avoid stale socket state
        if (_client != null)
        {
          try { await _client.DisconnectAsync(); } catch { }
        }
        _client = new ChromecastClient();

        _connectedReceiver = new ChromecastReceiver
        {
          DeviceUri = deviceUri,
          Port = connectPort,
          Name = device.FriendlyName,
          Model = device.Model
        };
      }

      if (_client == null)
      {
        throw new InvalidOperationException("Client not initialized. Call InitializeAsync first.");
      }

      _logger.LogDebug("Calling ConnectChromecast with URI: {Uri}", _connectedReceiver.DeviceUri);
      await _client.ConnectChromecast(_connectedReceiver);

      // Subscribe to receiver status changes for bidirectional volume sync
      SubscribeToReceiverStatus();

      // Read initial device volume
      await SyncInitialVolumeAsync();

      ConnectedDevice = device;
      Name = $"Cast: {device.FriendlyName}";

      Connected?.Invoke(this, new ChromecastConnectedEventArgs { Device = device });

      _logger.LogInformation(
        "Connected to Chromecast: {Name}",
        device.FriendlyName);

      State = AudioOutputState.Ready;

      // Resume streaming if we auto-stopped to switch devices
      if (wasStreaming)
      {
        _logger.LogInformation("Restarting Cast stream after device switch");
        await StartAsync(cancellationToken);
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to connect to Chromecast: {Name}", device.FriendlyName);
      State = AudioOutputState.Error;
      throw;
    }
  }

  /// <summary>
  /// Disconnects from the currently connected Chromecast device.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  public async Task DisconnectAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    if (_connectedReceiver == null)
    {
      _logger.LogWarning("No Chromecast device connected");
      return;
    }

    try
    {
      _logger.LogInformation("Disconnecting from Chromecast: {Name}", ConnectedDevice?.FriendlyName);

      UnsubscribeFromReceiverStatus();

      if (_client != null)
      {
        await _client.DisconnectAsync();
      }

      var disconnectedDevice = ConnectedDevice;
      _connectedReceiver = null;
      ConnectedDevice = null;
      Name = "Google Cast Output";

      Disconnected?.Invoke(this, new ChromecastDisconnectedEventArgs
      {
        Device = disconnectedDevice,
        Reason = "User requested disconnect"
      });

      State = AudioOutputState.Ready;
      _logger.LogInformation("Disconnected from Chromecast");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error disconnecting from Chromecast");
      throw;
    }
  }

  /// <inheritdoc />
  public override async Task StartAsync(CancellationToken cancellationToken = default)
  {
    ValidateCanStart();

    if (_connectedReceiver == null)
    {
      _logger.LogInformation("No Chromecast device connected yet — output ready, waiting for device connection");
      State = AudioOutputState.Ready;
      return;
    }

    try
    {
      _logger.LogInformation("Starting Google Cast output (mode: {Mode})", _options.StreamingMode);

      if (_client != null)
      {
        // Launch the receiver application (default CC1AD845 or custom receiver)
        var launchStatus = await _client.LaunchApplicationAsync(_options.ApplicationId);
        _logger.LogInformation("Cast: Receiver launched on {Device}", ConnectedDevice?.FriendlyName);

        // Allow receiver to start initializing before sending commands
        await Task.Delay(250, cancellationToken);

        // Branch on streaming mode
        if (string.Equals(_options.StreamingMode, "DirectChannel", StringComparison.OrdinalIgnoreCase))
        {
          await StartDirectChannelAsync(launchStatus, cancellationToken);
        }
        else
        {
          await StartHttpMp3Async(cancellationToken);
        }
      }

      IsEnabledInternal = true;
      State = AudioOutputState.Streaming;

      _logger.LogInformation("Google Cast output started streaming to {Name} (mode: {Mode})",
        ConnectedDevice?.FriendlyName, _options.StreamingMode);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to start Google Cast output");
      State = AudioOutputState.Error;
      throw;
    }
  }

  /// <summary>
  /// Starts the DirectChannel streaming mode: sends Base64-encoded WAV chunks
  /// directly over a custom Cast message bus, bypassing HTTP entirely.
  /// </summary>
  private async Task StartDirectChannelAsync(
    Sharpcaster.Models.ChromecastStatus.ChromecastStatus? launchStatus,
    CancellationToken cancellationToken)
  {
    if (_audioEngine == null)
    {
      _logger.LogWarning("Cast: DirectChannel mode requires audio engine — call SetAudioEngine() first. Falling back to HttpMp3.");
      await StartHttpMp3Async(cancellationToken);
      return;
    }

    // Extract the transport ID from the launched application status.
    // This is the destination address for all custom messages to the receiver.
    var transportId = launchStatus?.Application?.TransportId;
    if (string.IsNullOrEmpty(transportId))
    {
      _logger.LogWarning("Cast: Could not get transport ID from launch status — falling back to HttpMp3");
      await StartHttpMp3Async(cancellationToken);
      return;
    }

    _logger.LogInformation(
      "Cast: Starting DirectChannel streaming — transport: {TransportId}, namespace: {Namespace}, chunk: {ChunkMs}ms",
      transportId, _options.DirectChannelNamespace, _options.DirectChannelChunkSizeMs);

    // Create the custom audio channel and wire it to the client.
    // ChromecastChannel.Client is a public setter used internally for sending.
    // Note: without RegisterChannel (not available in SharpCaster v3.0.0),
    // the channel can send but won't receive messages from the receiver.
    // This is acceptable — audio flows sender→receiver; diagnostics use logs.
    _directChannel = new DirectCastAudioChannel(_options.DirectChannelNamespace, _logger);
    _directChannel.Client = _client!;

    // Create the streaming service and start sending audio
    _directStreaming = new DirectCastStreamingService(
      _logger, _audioEngine, _directChannel, _options);
    _directStreaming.SetTransportId(transportId);
    _directStreaming.Start();

    // Sync volume to Cast device
    await SyncVolumeAfterStartAsync();
  }

  /// <summary>
  /// Starts the standard HttpMp3 streaming mode: the Cast device fetches audio
  /// from an HTTP MP3 stream endpoint.
  /// </summary>
  private async Task StartHttpMp3Async(CancellationToken cancellationToken)
  {
    // Subscribe to status changes to monitor device transitions
    var mediaChannel = _client!.GetChannel<MediaChannel>();
    if (mediaChannel != null)
    {
      mediaChannel.StatusChanged += (_, status) =>
      {
        _logger.LogInformation(
          "Cast: StatusChanged event — PlayerState: {State}, IdleReason: {IdleReason}, MediaSessionId: {SessionId}",
          status?.PlayerState, status?.IdleReason, status?.MediaSessionId);
      };
    }

    // Load media if we have a stream URL
    if (!string.IsNullOrEmpty(_streamUrl))
    {
      await LoadMediaOnCastAsync(mediaChannel, cancellationToken);
      await SyncVolumeAfterStartAsync();
    }
    else
    {
      _logger.LogWarning("Cast: No stream URL set — Chromecast will not receive audio");
    }
  }

  /// <summary>
  /// Syncs the volume level to the Cast device after starting playback.
  /// </summary>
  private async Task SyncVolumeAfterStartAsync()
  {
    try
    {
      var receiverChannel = _client!.GetChannel<ReceiverChannel>();
      if (receiverChannel != null)
      {
        await receiverChannel.SetVolume(Volume);
        _logger.LogInformation("Cast: Volume synced to {Volume:P0}", Volume);
      }
    }
    catch (Exception volEx)
    {
      _logger.LogWarning(volEx, "Cast: Failed to sync volume after start");
    }
  }

  /// <inheritdoc />
  public override async Task StopAsync(CancellationToken cancellationToken = default)
  {
    if (!ValidateCanStop())
    {
      return;
    }

    try
    {
      State = AudioOutputState.Stopping;
      _logger.LogInformation("Stopping Google Cast output");

      // Stop DirectChannel streaming if active
      if (_directStreaming != null)
      {
        await _directStreaming.StopAsync();
        await _directStreaming.DisposeAsync();
        _directStreaming = null;
        _directChannel = null;
        _logger.LogInformation("DirectChannel streaming stopped");
      }

      // Stop media playback on the Chromecast
      if (_client != null)
      {
        var mediaChannel = _client.GetChannel<MediaChannel>();
        if (mediaChannel != null)
        {
          try
          {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await mediaChannel.StopAsync().WaitAsync(cts.Token);
          }
          catch (TimeoutException)
          {
            _logger.LogWarning("Timed out stopping Cast media — device may be unreachable");
          }
          catch (OperationCanceledException)
          {
            _logger.LogWarning("Cast media stop cancelled — device may be unreachable");
          }
          catch (System.Reflection.TargetInvocationException tie)
            when (tie.InnerException?.Message.Contains("INVALID_MEDIA_SESSION_ID") == true)
          {
            _logger.LogDebug("Cast media session already ended, ignoring stop error");
          }
          catch (InvalidOperationException ioe)
            when (ioe.Message.Contains("INVALID_MEDIA_SESSION_ID"))
          {
            _logger.LogDebug("Cast media session already ended, ignoring stop error");
          }
          catch (ArgumentNullException)
          {
            _logger.LogDebug("Cast: No active media session to stop (MediaSessionId is null)");
          }
        }
      }

      IsEnabledInternal = false;
      State = AudioOutputState.Stopped;

      _logger.LogInformation("Google Cast output stopped");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to stop Google Cast output");
      State = AudioOutputState.Stopped;
    }
  }

  /// <summary>
  /// Sets the stream URL that will be used when streaming to Chromecast.
  /// The Chromecast will connect to this URL to receive the audio stream.
  /// </summary>
  /// <param name="streamUrl">The HTTP stream URL.</param>
  public void SetStreamUrl(string streamUrl)
  {
    _streamUrl = streamUrl;
    _logger.LogDebug("Stream URL set to: {Url}", streamUrl);
  }

  /// <summary>
  /// Sets the audio engine reference for DirectChannel streaming mode.
  /// When set, the DirectCastStreamingService can create a stream reader
  /// to read PCM audio directly from the engine's output tap.
  /// Only required when <see cref="GoogleCastOutputOptions.StreamingMode"/> is "DirectChannel".
  /// </summary>
  /// <param name="audioEngine">The audio engine instance.</param>
  public void SetAudioEngine(IAudioEngine audioEngine)
  {
    _audioEngine = audioEngine;
    _logger.LogInformation("Cast: Audio engine set for DirectChannel streaming");
  }

  /// <summary>
  /// Sets now-playing metadata that will be sent to the Cast device.
  /// The metadata is included when loading media and can be updated mid-stream
  /// via <see cref="UpdateNowPlayingMetadataAsync"/>.
  /// </summary>
  public void SetNowPlayingMetadata(string? title, string? artist, string? album, string? albumArtUrl)
  {
    _nowPlayingMetadata = new CastNowPlayingMetadata(title, artist, album, albumArtUrl);
    _logger.LogDebug("Cast now-playing metadata set: {Title} - {Artist} [{Album}]", title, artist, album);
  }

  /// <summary>
  /// Tests Cast playback with an arbitrary URL to diagnose audio issues.
  /// Re-launches the receiver app and loads the URL fresh.
  /// </summary>
  public async Task<object> TestPlayUrlAsync(string url, string contentType)
  {
    if (_client == null)
      return new { success = false, error = "Not connected to a Cast device" };

    try
    {
      // Re-launch the media receiver app to get a clean session
      _logger.LogInformation("Cast test: Launching media receiver app");
      await _client.LaunchApplicationAsync(_options.ApplicationId);
      await Task.Delay(500);

      var media = new Media
      {
        ContentId = url,
        ContentUrl = url,
        ContentType = contentType,
        StreamType = StreamType.Buffered
      };

      var payload = JsonSerializer.Serialize(media);
      _logger.LogInformation("Cast test: Payload = {Payload}", payload);

      var mediaChannel = _client.GetChannel<MediaChannel>();
      if (mediaChannel == null)
        return new { success = false, error = "MediaChannel not available" };

      var loadStatus = await mediaChannel.LoadAsync(media, true);
      _logger.LogInformation(
        "Cast test: Load response — PlayerState: {State}, IdleReason: {Reason}, MediaSessionId: {Id}",
        loadStatus?.PlayerState, loadStatus?.IdleReason, loadStatus?.MediaSessionId);

      // Wait for playback to start
      await Task.Delay(3000);
      var finalStatus = await mediaChannel.GetMediaStatusAsync().WaitAsync(TimeSpan.FromSeconds(5));
      _logger.LogInformation(
        "Cast test: Final status (3s) — PlayerState: {State}, IdleReason: {Reason}",
        finalStatus?.PlayerState, finalStatus?.IdleReason);

      return new
      {
        success = finalStatus?.PlayerState is PlayerStateType.Playing or PlayerStateType.Buffering,
        loadState = loadStatus?.PlayerState.ToString(),
        finalState = finalStatus?.PlayerState.ToString(),
        finalIdleReason = finalStatus?.IdleReason,
        url,
        contentType,
        payload
      };
    }
    catch (Exception ex)
    {
      return new { success = false, error = ex.Message, url };
    }
  }

  /// <summary>
  /// Updates now-playing metadata on the Cast device by reloading media.
  /// Uses debouncing to coalesce rapid metadata changes (e.g., source switch
  /// → "No Track" → actual track → fingerprint identification) into a single
  /// Cast media reload. Without debouncing, each change triggers a full stream
  /// reconnection, causing garbled audio during the first few seconds.
  /// </summary>
  public async Task UpdateNowPlayingMetadataAsync(
    string? title, string? artist, string? album, string? albumArtUrl,
    CancellationToken cancellationToken = default)
  {
    _nowPlayingMetadata = new CastNowPlayingMetadata(title, artist, album, albumArtUrl);

    if (State != AudioOutputState.Streaming || _client == null || string.IsNullOrEmpty(_streamUrl))
    {
      _logger.LogDebug("Cast not streaming, metadata stored for next load");
      return;
    }

    // Debounce: cancel any pending metadata reload and schedule a new one.
    // This ensures rapid changes coalesce into a single Cast media reload.
    lock (_debounceLock)
    {
      _metadataDebouncesCts?.Cancel();
      _metadataDebouncesCts?.Dispose();
      _metadataDebouncesCts = new CancellationTokenSource();
    }

    var debounceCts = _metadataDebouncesCts;
    var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
      debounceCts?.Token ?? CancellationToken.None, cancellationToken);

    _logger.LogDebug("Cast metadata update debounced: {Title} - {Artist}", title, artist);

    // Fire-and-forget the delayed reload so we don't block the caller
    _ = Task.Run(async () =>
    {
      try
      {
        // Wait for metadata to stabilize — if another update comes
        // within this window, this task gets cancelled via debounceCts.
        // 3s covers the typical gap between source switch ("No Track")
        // and actual track metadata being available (~2s).
        await Task.Delay(3000, linkedCts.Token);

        await LoadMediaWithRecoveryAsync(cancellationToken);
        _logger.LogInformation(
          "Cast metadata updated: {Title} - {Artist}", title, artist);
      }
      catch (OperationCanceledException)
      {
        // Debounced away — a newer update superseded this one
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Failed to update Cast now-playing metadata");
      }
      finally
      {
        linkedCts.Dispose();
      }
    }, CancellationToken.None);
  }

  /// <summary>
  /// Attempts to load media on the Cast device. If the session has expired
  /// (receiver app closed after idle), relaunches the app and retries once.
  /// </summary>
  private async Task LoadMediaWithRecoveryAsync(CancellationToken cancellationToken)
  {
    var media = BuildMedia();
    var mediaChannel = _client!.GetChannel<MediaChannel>();
    if (mediaChannel == null) return;

    try
    {
      await mediaChannel.LoadAsync(media, true).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
    }
    catch (Exception ex) when (IsCastSessionExpired(ex))
    {
      _logger.LogInformation("Cast session expired — relaunching media receiver");
      await RelaunchMediaReceiverAsync(cancellationToken);

      // Retry load after relaunch
      mediaChannel = _client.GetChannel<MediaChannel>();
      if (mediaChannel != null)
      {
        try
        {
          await mediaChannel.LoadAsync(media, true).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
          _logger.LogInformation("Cast: Media loaded successfully after session recovery");
        }
        catch (TimeoutException)
        {
          _logger.LogDebug("Cast: Recovered LoadAsync timed out — device may still be processing");
        }
      }
    }
    catch (TimeoutException)
    {
      _logger.LogDebug("Cast: Metadata LoadAsync timed out — device may still be processing");
    }
  }

  /// <summary>
  /// Determines whether an exception indicates the Cast session has expired.
  /// </summary>
  private static bool IsCastSessionExpired(Exception ex)
  {
    var message = ex is System.Reflection.TargetInvocationException tie
      ? tie.InnerException?.Message ?? ex.Message
      : ex.Message;

    return message.Contains("INVALID_MEDIA_SESSION_ID", StringComparison.OrdinalIgnoreCase)
        || message.Contains("No running applications", StringComparison.OrdinalIgnoreCase)
        || message.Contains("session not found", StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>
  /// Relaunches the media receiver after an idle timeout.
  /// </summary>
  private async Task RelaunchMediaReceiverAsync(CancellationToken cancellationToken)
  {
    try
    {
      await _client!.LaunchApplicationAsync(_options.ApplicationId);
      await Task.Delay(500, cancellationToken);
      _logger.LogInformation("Cast: Media receiver relaunched after idle recovery");
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Cast: Failed to relaunch media receiver");
    }
  }

  /// <summary>
  /// Standard Google Cast protocol port.
  /// </summary>
  private const int StandardCastPort = 8009;

  /// <summary>
  /// Finds a reachable TCP port on a cached Cast device.
  /// Tries the cached port first, then falls back to the standard Cast port (8009).
  /// </summary>
  private async Task<int> FindReachablePortAsync(ChromecastDeviceInfo device, CancellationToken ct)
  {
    // Always try the standard Cast protocol port (8009) first.
    // Port 443 on Google Home devices is HTTPS management, not Cast protocol —
    // it accepts TCP but times out on Cast protocol messages.
    var portsToTry = device.Port == StandardCastPort
      ? new[] { StandardCastPort }
      : new[] { StandardCastPort, device.Port };

    foreach (var port in portsToTry)
    {
      _logger.LogDebug("Verifying Cast device reachability at {IP}:{Port}", device.IpAddress, port);
      using var tcpCheck = new TcpClient();
      try
      {
        using var tcpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        tcpCts.CancelAfter(TimeSpan.FromSeconds(3));
        await tcpCheck.ConnectAsync(device.IpAddress, port, tcpCts.Token);
        if (port != device.Port)
        {
          _logger.LogInformation(
            "Cast device '{Name}': using standard port {Port} instead of discovered port {DiscoveredPort}",
            device.FriendlyName, port, device.Port);
        }
        return port;
      }
      catch
      {
        _logger.LogDebug("TCP check failed on {IP}:{Port}", device.IpAddress, port);
      }
    }

    throw new InvalidOperationException(
      $"Cast device '{device.FriendlyName}' at {device.IpAddress} is not reachable (tried ports {string.Join(", ", portsToTry)})");
  }

  /// <summary>
  /// Loads media on the Cast device with retry logic. The first load after
  /// LaunchApplicationAsync often fails silently (receiver not yet ready).
  /// If the first load results in FINISHED/Idle quickly, we retry once.
  /// </summary>
  private async Task LoadMediaOnCastAsync(MediaChannel? mediaChannel, CancellationToken cancellationToken)
  {
    if (mediaChannel == null)
    {
      _logger.LogWarning("Cast: MediaChannel is null — cannot load media");
      return;
    }

    _logger.LogInformation(
      "Cast: Loading media URL {StreamUrl} (type: audio/mpeg, stream: Live, metadata: {HasMetadata})",
      _streamUrl, _nowPlayingMetadata != null);

    var media = BuildMedia();

    // First attempt
    MediaStatus? status = null;
    try
    {
      status = await mediaChannel.LoadAsync(media, true).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
      _logger.LogInformation(
        "Cast: Media load response — PlayerState: {State}, IdleReason: {IdleReason}, MediaSessionId: {SessionId}",
        status?.PlayerState, status?.IdleReason, status?.MediaSessionId);
    }
    catch (TimeoutException)
    {
      _logger.LogInformation("Cast: Media load timed out (5s) — continuing in background");
    }

    // If the load immediately resulted in Idle/FINISHED or didn't start playing,
    // wait and retry — receiver may not have been fully initialized
    if (status?.PlayerState is PlayerStateType.Idle ||
        status?.IdleReason is "FINISHED" or "ERROR" or "CANCELLED")
    {
      _logger.LogInformation("Cast: First load resulted in {State}/{Reason} — retrying after 1s delay",
        status?.PlayerState, status?.IdleReason);
      await Task.Delay(1000, cancellationToken);

      try
      {
        media = BuildMedia(); // Rebuild in case metadata changed
        status = await mediaChannel.LoadAsync(media, true).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        _logger.LogInformation(
          "Cast: Retry load response — PlayerState: {State}, IdleReason: {IdleReason}, MediaSessionId: {SessionId}",
          status?.PlayerState, status?.IdleReason, status?.MediaSessionId);
      }
      catch (TimeoutException)
      {
        _logger.LogInformation("Cast: Retry load timed out — Cast device may still be processing");
      }
    }
  }

  /// <summary>
  /// Builds a Media object with the current stream URL and now-playing metadata.
  /// </summary>
  private Media BuildMedia()
  {
    var media = new Media
    {
      ContentId = _streamUrl,
      ContentUrl = _streamUrl,
      ContentType = "audio/mpeg",
      StreamType = StreamType.Live
    };

    if (_nowPlayingMetadata != null)
    {
      var metadata = new MediaMetadata
      {
        MetadataType = MetadataType.Music,
        Title = _nowPlayingMetadata.Title ?? "",
        SubTitle = _nowPlayingMetadata.Artist ?? ""
      };

      // Add album art if we have a valid absolute URL
      if (!string.IsNullOrEmpty(_nowPlayingMetadata.AlbumArtUrl) &&
          _nowPlayingMetadata.AlbumArtUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
      {
        metadata.Images = new[]
        {
          new Image { Url = _nowPlayingMetadata.AlbumArtUrl }
        };
      }

      media.Metadata = metadata;
    }

    return media;
  }

  /// <summary>
  /// Monitors a background LoadAsync task and runs connectivity diagnostics on failure.
  /// </summary>
  private async Task MonitorBackgroundLoadAsync(Task<MediaStatus?> loadTask)
  {
    try
    {
      var status = await loadTask;
      _logger.LogInformation(
        "Cast: Background load completed — PlayerState: {State}, IdleReason: {IdleReason}",
        status?.PlayerState, status?.IdleReason);
    }
    catch (TimeoutException)
    {
      _logger.LogWarning("Cast: LoadAsync timed out (30s) — Cast device never reached PLAYING state");
      await DiagnoseStreamConnectivityAsync();
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Cast: Background load failed");
      await DiagnoseStreamConnectivityAsync();
    }
  }

  /// <summary>
  /// Self-tests the stream URL from this machine to diagnose Cast connectivity issues.
  /// If the URL is reachable locally but Cast can't play it, the issue is likely a firewall.
  /// </summary>
  private async Task DiagnoseStreamConnectivityAsync()
  {
    if (string.IsNullOrEmpty(_streamUrl)) return;

    try
    {
      using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
      var response = await httpClient.GetAsync(_streamUrl, HttpCompletionOption.ResponseHeadersRead);
      _logger.LogWarning(
        "Cast: Stream URL {Url} IS reachable from this machine (HTTP {Status}) but the Cast device cannot reach it. " +
        "This is almost certainly a FIREWALL issue. Fix with: " +
        "netsh advfirewall firewall add rule name=\"Radio Console Stream\" dir=in action=allow protocol=TCP localport=8080",
        _streamUrl, (int)response.StatusCode);
    }
    catch (Exception ex)
    {
      _logger.LogError(
        "Cast: Stream URL {Url} is NOT reachable even from this machine: {Error}. Is the HTTP stream server running?",
        _streamUrl, ex.Message);
    }
  }

  /// <summary>
  /// Subscribes to ReceiverChannel status events for bidirectional volume sync.
  /// </summary>
  private void SubscribeToReceiverStatus()
  {
    if (_client == null) return;

    var receiverChannel = _client.GetChannel<ReceiverChannel>();
    if (receiverChannel != null)
    {
      receiverChannel.ReceiverStatusChanged += OnReceiverStatusChanged;
      _logger.LogDebug("Subscribed to Cast receiver status changes for volume sync");
    }
  }

  /// <summary>
  /// Unsubscribes from ReceiverChannel status events.
  /// </summary>
  private void UnsubscribeFromReceiverStatus()
  {
    if (_client == null) return;

    var receiverChannel = _client.GetChannel<ReceiverChannel>();
    if (receiverChannel != null)
    {
      receiverChannel.ReceiverStatusChanged -= OnReceiverStatusChanged;
    }
  }

  /// <summary>
  /// Reads the initial device volume after connecting and syncs our local state.
  /// </summary>
  private async Task SyncInitialVolumeAsync()
  {
    if (_client == null) return;

    try
    {
      var receiverChannel = _client.GetChannel<ReceiverChannel>();
      if (receiverChannel != null)
      {
        var status = await receiverChannel.GetChromecastStatusAsync();
        if (status?.Volume?.Level != null)
        {
          var deviceVolume = (float)status.Volume.Level.Value;
          var deviceMuted = status.Volume.Muted ?? false;
          _lastSetVolume = deviceVolume;
          _lastSetMute = deviceMuted;

          _logger.LogInformation(
            "Cast device initial volume: {Volume:P0}, Muted: {Muted}",
            deviceVolume, deviceMuted);

          // Fire event so AudioManager can sync its state to the device's actual volume
          CastVolumeChanged?.Invoke(this, new CastVolumeChangedEventArgs
          {
            Volume = deviceVolume,
            IsMuted = deviceMuted,
            IsInitialSync = true
          });
        }
      }
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Could not read initial Cast device volume — will sync on first status update");
    }
  }

  /// <summary>
  /// Handles ReceiverStatusChanged events from SharpCaster.
  /// Detects external volume/mute changes and fires <see cref="CastVolumeChanged"/>.
  /// </summary>
  private void OnReceiverStatusChanged(object? sender, ChromecastStatus status)
  {
    if (status.Volume == null) return;

    var deviceVolume = (float)(status.Volume.Level ?? 0);
    var deviceMuted = status.Volume.Muted ?? false;

    // Filter out echo events from our own SetVolume/SetMute calls
    if (_suppressNextVolumeEvent)
    {
      _suppressNextVolumeEvent = false;
      _lastSetVolume = deviceVolume;
      _lastSetMute = deviceMuted;
      return;
    }

    // Check if volume actually changed from what we last set
    var volumeChanged = Math.Abs(deviceVolume - _lastSetVolume) > 0.01f;
    var muteChanged = deviceMuted != _lastSetMute;

    if (!volumeChanged && !muteChanged) return;

    _lastSetVolume = deviceVolume;
    _lastSetMute = deviceMuted;

    _logger.LogInformation(
      "Cast device volume changed externally: {Volume:P0}, Muted: {Muted}",
      deviceVolume, deviceMuted);

    CastVolumeChanged?.Invoke(this, new CastVolumeChangedEventArgs
    {
      Volume = deviceVolume,
      IsMuted = deviceMuted,
      IsInitialSync = false
    });
  }

  private async Task SetCastVolumeAsync(float volume)
  {
    if (_client == null || _connectedReceiver == null || State != AudioOutputState.Streaming)
    {
      return;
    }

    try
    {
      var receiverChannel = _client.GetChannel<ReceiverChannel>();
      if (receiverChannel != null)
      {
        _suppressNextVolumeEvent = true;
        _lastSetVolume = volume;
        await receiverChannel.SetVolume(volume);
        _logger.LogDebug("Chromecast volume set to {Volume:P0}", volume);
      }
    }
    catch (Exception ex)
    {
      _suppressNextVolumeEvent = false;
      _logger.LogWarning(ex, "Failed to set Chromecast volume");
    }
  }

  private async Task SetCastMuteAsync(bool mute)
  {
    if (_client == null || _connectedReceiver == null || State != AudioOutputState.Streaming)
    {
      return;
    }

    try
    {
      var receiverChannel = _client.GetChannel<ReceiverChannel>();
      if (receiverChannel != null)
      {
        _suppressNextVolumeEvent = true;
        _lastSetMute = mute;
        await receiverChannel.SetMute(mute);
        _logger.LogDebug("Chromecast mute set to {Mute}", mute);
      }
    }
    catch (Exception ex)
    {
      _suppressNextVolumeEvent = false;
      _logger.LogWarning(ex, "Failed to set Chromecast mute state");
    }
  }

  /// <inheritdoc />
  public override async ValueTask DisposeAsync()
  {
    if (IsDisposed)
    {
      return;
    }

    if (_connectedReceiver != null)
    {
      try
      {
        await DisconnectAsync();
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Error during disconnect in dispose");
      }
    }

    if (_directStreaming != null)
    {
      await _directStreaming.DisposeAsync();
      _directStreaming = null;
      _directChannel = null;
    }

    lock (_debounceLock)
    {
      _metadataDebouncesCts?.Cancel();
      _metadataDebouncesCts?.Dispose();
      _metadataDebouncesCts = null;
    }

    if (_client != null)
    {
      await _client.DisconnectAsync();
    }

    DisposeBase();
  }
}

/// <summary>
/// Information about a discovered Chromecast device.
/// </summary>
public record ChromecastDeviceInfo
{
  /// <summary>
  /// Gets or sets the unique identifier for the device.
  /// </summary>
  public required string Id { get; init; }

  /// <summary>
  /// Gets or sets the friendly name of the device.
  /// </summary>
  public required string FriendlyName { get; init; }

  /// <summary>
  /// Gets or sets the IP address of the device.
  /// </summary>
  public required string IpAddress { get; init; }

  /// <summary>
  /// Gets or sets the port number.
  /// </summary>
  public required int Port { get; init; }

  /// <summary>
  /// Gets or sets the device model.
  /// </summary>
  public required string Model { get; init; }
}

/// <summary>
/// Event arguments for Chromecast device discovery.
/// </summary>
public class ChromecastDeviceDiscoveredEventArgs : EventArgs
{
  /// <summary>
  /// Gets the discovered device.
  /// </summary>
  public required ChromecastDeviceInfo Device { get; init; }
}

/// <summary>
/// Event arguments for Chromecast connection.
/// </summary>
public class ChromecastConnectedEventArgs : EventArgs
{
  /// <summary>
  /// Gets the connected device.
  /// </summary>
  public required ChromecastDeviceInfo Device { get; init; }
}

/// <summary>
/// Event arguments for Chromecast disconnection.
/// </summary>
public class ChromecastDisconnectedEventArgs : EventArgs
{
  /// <summary>
  /// Gets the disconnected device.
  /// </summary>
  public ChromecastDeviceInfo? Device { get; init; }

  /// <summary>
  /// Gets the reason for disconnection.
  /// </summary>
  public string? Reason { get; init; }
}

/// <summary>
/// Event arguments for Cast device volume changes (external).
/// </summary>
public class CastVolumeChangedEventArgs : EventArgs
{
  /// <summary>
  /// Gets the new volume level (0.0 to 1.0).
  /// </summary>
  public required float Volume { get; init; }

  /// <summary>
  /// Gets whether the device is muted.
  /// </summary>
  public required bool IsMuted { get; init; }

  /// <summary>
  /// Gets whether this is the initial sync after connecting (not an external change).
  /// </summary>
  public bool IsInitialSync { get; init; }
}

/// <summary>
/// Now-playing metadata to display on Cast devices (Google Home app).
/// </summary>
/// <param name="Title">Track title.</param>
/// <param name="Artist">Artist name.</param>
/// <param name="Album">Album name.</param>
/// <param name="AlbumArtUrl">Absolute URL to album art image.</param>
public record CastNowPlayingMetadata(string? Title, string? Artist, string? Album, string? AlbumArtUrl);

/// <summary>
/// A cached Cast device entry with a last-seen timestamp.
/// </summary>
public class CachedCastDevice
{
  /// <summary>
  /// Gets or sets the device info.
  /// </summary>
  public required ChromecastDeviceInfo Device { get; set; }

  /// <summary>
  /// Gets or sets when the device was last seen on the network.
  /// </summary>
  public DateTime LastSeen { get; set; }
}

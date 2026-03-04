using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.AspNetCore.SignalR;
using Radio.API.Hubs;
using Radio.API.Mappers;
using Radio.API.Models;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Interfaces.Input;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;
using Radio.Infrastructure.Audio.Outputs;
using Radio.Infrastructure.Audio.Services;

namespace Radio.API.Services;

/// <summary>
/// Background service that broadcasts audio state updates to SignalR clients.
/// Monitors playback state, now playing info, queue, radio state, and volume changes.
/// Sends updates only when state actually changes to avoid spamming clients.
/// </summary>
public class AudioStateUpdateService : BackgroundService
{
  private readonly ILogger<AudioStateUpdateService> _logger;
  private readonly IHubContext<AudioStateHub> _hubContext;
  private readonly IAudioManager? _audioManager;
  private readonly IBluetoothService? _bluetoothService;
  private readonly GoogleCastOutput? _castOutput;
  private readonly BackgroundIdentificationService? _fingerprintService;
  private readonly VisualizationModeService? _vizModeService;
  private readonly IRotaryEncoderService? _encoderService;
  private string? _apiBaseUrl;

  /// <summary>
  /// Gets or sets the update interval in milliseconds (default: 500ms).
  /// </summary>
  public int UpdateIntervalMs { get; set; } = 500;

  /// <summary>
  /// Gets or sets whether broadcasting is enabled (default: true).
  /// </summary>
  public bool IsEnabled { get; set; } = true;

  // Cached state to detect changes
  private PlaybackStateDto? _lastPlaybackState;
  private NowPlayingDto? _lastNowPlaying;
  private List<QueueItemDto>? _lastQueue;
  private RadioStateDto? _lastRadioState;
  private VolumeDto? _lastVolume;
  private string? _lastActiveSourceType;

  /// <summary>
  /// Initializes a new instance of the AudioStateUpdateService.
  /// </summary>
  public AudioStateUpdateService(
    ILogger<AudioStateUpdateService> logger,
    IHubContext<AudioStateHub> hubContext,
    IServiceProvider serviceProvider,
    IConfiguration configuration)
  {
    _logger = logger;
    _hubContext = hubContext;

    // Try to get IAudioManager, but don't fail if it's not available
    // This allows the service to start even if audio infrastructure isn't fully initialized
    _audioManager = serviceProvider.GetService<IAudioManager>();
    _bluetoothService = serviceProvider.GetService<IBluetoothService>();
    _castOutput = serviceProvider.GetService<GoogleCastOutput>();
    _fingerprintService = serviceProvider.GetService<BackgroundIdentificationService>();
    _vizModeService = serviceProvider.GetService<VisualizationModeService>();
    _encoderService = serviceProvider.GetService<IRotaryEncoderService>();

    // Resolve API base URL for making relative album art URLs absolute (needed by Cast devices)
    ResolveApiBaseUrl(configuration);
    
    if (_audioManager == null)
    {
      _logger.LogWarning("IAudioManager not available - AudioStateUpdateService will not broadcast playback updates");
      IsEnabled = false;
    }

    if (_bluetoothService != null)
    {
      _bluetoothService.StateChanged += OnBluetoothStateChanged;
      _bluetoothService.DeviceConnected += OnBluetoothDeviceConnected;
      _bluetoothService.DeviceDisconnected += OnBluetoothDeviceDisconnected;
      _bluetoothService.DeviceDiscovered += OnBluetoothDeviceDiscovered;
      // BT AVRCP volume now drives PipeWire node volume directly (LinuxBluetoothService),
      // no longer synced to master volume.
    }
    else
    {
      _logger.LogWarning("IBluetoothService not available - Bluetooth SignalR updates disabled");
    }

    // Subscribe to Cast device volume changes for bidirectional sync
    if (_castOutput != null)
    {
      _castOutput.CastVolumeChanged += OnCastVolumeChanged;
      _logger.LogInformation("Subscribed to Cast volume changes for bidirectional sync");
    }

    // Subscribe to fingerprint status changes for real-time UI updates
    if (_fingerprintService != null)
    {
      _fingerprintService.StatusChanged += OnFingerprintStatusChanged;
      _logger.LogInformation("Subscribed to fingerprint status changes");
    }

    // Subscribe to visualization mode changes from rotary encoder
    if (_vizModeService != null)
    {
      _vizModeService.ModeChanged += OnVisualizationModeChanged;
      _logger.LogInformation("Subscribed to visualization mode changes");
    }

    // Subscribe to encoder connection changes for UI status updates
    if (_encoderService != null)
    {
      _encoderService.ConnectionChanged += OnEncoderConnectionChanged;
      _logger.LogInformation("Subscribed to encoder connection changes");
    }
  }

  /// <summary>
  /// Executes the background service.
  /// </summary>
  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    _logger.LogInformation("AudioStateUpdateService starting with update interval: {IntervalMs}ms", UpdateIntervalMs);

    var updateDelay = TimeSpan.FromMilliseconds(UpdateIntervalMs);

    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        if (IsEnabled)
        {
          await CheckAndBroadcastUpdatesAsync(stoppingToken);
        }

        await Task.Delay(updateDelay, stoppingToken);
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
      {
        // Normal shutdown, don't log as error
        break;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error broadcasting audio state updates");
        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
      }
    }

    _logger.LogInformation("AudioStateUpdateService stopped");
  }

  private async Task CheckAndBroadcastUpdatesAsync(CancellationToken cancellationToken)
  {
    // Skip if audio manager not available
    if (_audioManager == null)
    {
      return;
    }

    // Get current active source
    var activeSource = _audioManager.ActiveSource;

    // Check source changes (broadcasts SourceChanged for UI sync)
    await CheckSourceChangedAsync(activeSource, cancellationToken);

    // Check playback state changes
    await CheckPlaybackStateAsync(activeSource, cancellationToken);

    // Check now playing changes
    await CheckNowPlayingAsync(activeSource, cancellationToken);

    // Check queue changes (if source supports queue)
    await CheckQueueAsync(activeSource, cancellationToken);

    // Check radio state changes (if source is radio)
    await CheckRadioStateAsync(activeSource, cancellationToken);

    // Check volume changes
    await CheckVolumeAsync(cancellationToken);
  }

  private async Task CheckSourceChangedAsync(IAudioSource? activeSource, CancellationToken cancellationToken)
  {
    var currentSourceType = activeSource?.Type.ToString();

    if (currentSourceType != _lastActiveSourceType)
    {
      // Skip broadcast on first poll (null → initial value) to avoid spurious SourceChanged
      var isFirstRun = _lastActiveSourceType == null;
      _lastActiveSourceType = currentSourceType;

      if (!isFirstRun)
      {
        await _hubContext.Clients.All
          .SendAsync("SourceChanged", cancellationToken);
        _logger.LogInformation("Broadcast SourceChanged: {SourceType}", currentSourceType ?? "None");
      }
    }
  }

  private async Task CheckPlaybackStateAsync(IAudioSource? activeSource, CancellationToken cancellationToken)
  {
    var currentState = BuildPlaybackStateDto(activeSource);

    if (HasPlaybackStateChanged(_lastPlaybackState, currentState))
    {
      _lastPlaybackState = currentState;
      await _hubContext.Clients.All
        .SendAsync("PlaybackStateChanged", currentState, cancellationToken);
      _logger.LogDebug("Broadcast PlaybackStateChanged");
    }
  }

  private async Task CheckNowPlayingAsync(IAudioSource? activeSource, CancellationToken cancellationToken)
  {
    var currentNowPlaying = BuildNowPlayingDto(activeSource);

    if (HasNowPlayingChanged(_lastNowPlaying, currentNowPlaying))
    {
      _lastNowPlaying = currentNowPlaying;
      await _hubContext.Clients.All
        .SendAsync("NowPlayingChanged", currentNowPlaying, cancellationToken);
      _logger.LogDebug("Broadcast NowPlayingChanged: Title={Title}, Artist={Artist}, Album={Album}, AlbumArt={AlbumArtUrl}, Source={Source}",
        currentNowPlaying.Title, currentNowPlaying.Artist, currentNowPlaying.Album, currentNowPlaying.AlbumArtUrl, currentNowPlaying.SourceName);

      // Push metadata to Cast device if connected and streaming
      await PushMetadataToCastAsync(currentNowPlaying, cancellationToken);
    }
  }

  /// <summary>
  /// Pushes current now-playing metadata to Google Cast device for display in Google Home.
  /// </summary>
  private async Task PushMetadataToCastAsync(NowPlayingDto nowPlaying, CancellationToken cancellationToken)
  {
    if (_castOutput == null || _castOutput.State != AudioOutputState.Streaming)
    {
      return;
    }

    try
    {
      // Cast devices need absolute HTTP URLs for album art — resolve relative paths
      var albumArtUrl = ResolveAlbumArtUrl(nowPlaying.AlbumArtUrl);

      await _castOutput.UpdateNowPlayingMetadataAsync(
        nowPlaying.Title,
        nowPlaying.Artist,
        nowPlaying.Album,
        albumArtUrl,
        cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Failed to push metadata to Cast device");
    }
  }

  /// <summary>
  /// Resolves a relative album art URL to an absolute URL for Cast devices.
  /// Relative paths like /api/albumart/abc123.jpg become http://{lanIp}:{port}/api/albumart/abc123.jpg.
  /// </summary>
  private string? ResolveAlbumArtUrl(string? albumArtUrl)
  {
    if (string.IsNullOrEmpty(albumArtUrl))
      return albumArtUrl;

    // Already absolute or data URI — return as-is
    if (albumArtUrl.StartsWith("http://") || albumArtUrl.StartsWith("https://") || albumArtUrl.StartsWith("data:"))
      return albumArtUrl;

    // Relative path (e.g., /api/albumart/abc123.jpg) — prepend API base URL
    if (_apiBaseUrl != null && albumArtUrl.StartsWith("/"))
      return _apiBaseUrl + albumArtUrl;

    return albumArtUrl;
  }

  /// <summary>
  /// Resolves the API base URL (http://{lanIp}:{port}) for constructing absolute URLs.
  /// </summary>
  private void ResolveApiBaseUrl(IConfiguration configuration)
  {
    try
    {
      // Try Urls from configuration first, then Kestrel endpoints
      var urls = configuration["Urls"] ?? configuration["ASPNETCORE_URLS"];
      int port = 5000; // default
      if (!string.IsNullOrEmpty(urls))
      {
        // Parse first HTTP URL to extract port
        var firstUrl = urls.Split(';').FirstOrDefault(u => u.StartsWith("http://"));
        if (firstUrl != null && Uri.TryCreate(firstUrl, UriKind.Absolute, out var uri))
        {
          port = uri.Port;
        }
      }

      var lanIp = GetLocalIPAddress();
      if (lanIp != null)
      {
        _apiBaseUrl = $"http://{lanIp}:{port}";
        _logger.LogInformation("Resolved API base URL for Cast album art: {BaseUrl}", _apiBaseUrl);
      }
      else
      {
        _logger.LogWarning("Could not resolve LAN IP for Cast album art URLs");
      }
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Error resolving API base URL");
    }
  }

  /// <summary>
  /// Gets the local LAN IP address. Filters out virtual adapters (Hyper-V, WSL, Docker).
  /// </summary>
  private static string? GetLocalIPAddress()
  {
    string? fallbackIp = null;

    foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
    {
      if (ni.OperationalStatus != OperationalStatus.Up)
        continue;
      if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
        continue;

      var desc = ni.Description.ToLowerInvariant();
      var name = ni.Name.ToLowerInvariant();
      var isVirtual = desc.Contains("hyper-v") || desc.Contains("virtual") ||
                      name.Contains("vethernet") || name.Contains("wsl") ||
                      desc.Contains("docker") || desc.Contains("vmware") ||
                      desc.Contains("virtualbox");

      var props = ni.GetIPProperties();
      foreach (var addr in props.UnicastAddresses)
      {
        if (addr.Address.AddressFamily != AddressFamily.InterNetwork ||
            IPAddress.IsLoopback(addr.Address))
          continue;

        if (!isVirtual)
          return addr.Address.ToString();

        fallbackIp ??= addr.Address.ToString();
      }
    }

    return fallbackIp;
  }

  private async Task CheckQueueAsync(IAudioSource? activeSource, CancellationToken cancellationToken)
  {
    if (activeSource is not IPlayQueue playQueue)
    {
      return;
    }

    // Use full playlist so UI always gets played + current + upcoming with state
    var fullPlaylist = await playQueue.GetFullPlaylistAsync(cancellationToken);
    var currentQueue = fullPlaylist
      .Select(MapToQueueItemDto)
      .ToList();

    if (HasQueueChanged(_lastQueue, currentQueue))
    {
      _lastQueue = currentQueue;
      await _hubContext.Clients.Group("Queue")
        .SendAsync("QueueChanged", currentQueue, cancellationToken);
      _logger.LogDebug("Broadcast QueueChanged with {Count} items", currentQueue.Count);
    }
  }

  private async Task CheckRadioStateAsync(IAudioSource? activeSource, CancellationToken cancellationToken)
  {
    if (activeSource is not IRadioControl radioControls)
    {
      return;
    }

    var currentRadioState = MapToRadioStateDto(radioControls);

    if (HasRadioStateChanged(_lastRadioState, currentRadioState))
    {
      _lastRadioState = currentRadioState;
      await _hubContext.Clients.Group("RadioState")
        .SendAsync("RadioStateChanged", currentRadioState, cancellationToken);
      _logger.LogDebug("Broadcast RadioStateChanged: {Frequency} {Band}", currentRadioState.Frequency, currentRadioState.Band);
    }
  }

  private async Task CheckVolumeAsync(CancellationToken cancellationToken)
  {
    // Skip if audio manager not available
    if (_audioManager == null)
    {
      return;
    }

    var currentVolume = new VolumeDto
    {
      Volume = _audioManager.MasterVolume,
      IsMuted = _audioManager.IsMuted,
      Balance = _audioManager.Balance
    };

    if (HasVolumeChanged(_lastVolume, currentVolume))
    {
      _lastVolume = currentVolume;
      await _hubContext.Clients.All
        .SendAsync("VolumeChanged", currentVolume, cancellationToken);
      _logger.LogDebug("Broadcast VolumeChanged: {Volume}, Muted: {IsMuted}", currentVolume.Volume, currentVolume.IsMuted);

      // BT AVRCP volume is independent of master volume — no sync needed.
    }
  }

  // State comparison methods
  private static bool HasPlaybackStateChanged(PlaybackStateDto? previous, PlaybackStateDto? current)
  {
    if (previous == null || current == null)
    {
      return true;
    }

    return previous.IsPlaying != current.IsPlaying ||
           previous.IsPaused != current.IsPaused ||
           previous.Volume != current.Volume ||
           previous.IsMuted != current.IsMuted ||
           previous.Balance != current.Balance ||
           previous.Position != current.Position ||
           previous.Duration != current.Duration ||
           previous.ActiveSource?.Id != current.ActiveSource?.Id ||
           previous.CanNext != current.CanNext ||
           previous.CanPrevious != current.CanPrevious ||
           previous.CanShuffle != current.CanShuffle ||
           previous.CanRepeat != current.CanRepeat ||
           previous.IsShuffleEnabled != current.IsShuffleEnabled ||
           previous.RepeatMode != current.RepeatMode;
  }

  private static bool HasNowPlayingChanged(NowPlayingDto? previous, NowPlayingDto? current)
  {
    if (previous == null || current == null)
    {
      return true;
    }

    // Check if metadata changed (exclude Position which changes constantly during playback)
    // Position changes are handled by PlaybackState updates
    return previous.SourceType != current.SourceType ||
           previous.SourceName != current.SourceName ||
           previous.IsPlaying != current.IsPlaying ||
           previous.IsPaused != current.IsPaused ||
           previous.Title != current.Title ||
           previous.Artist != current.Artist ||
           previous.Album != current.Album ||
           previous.AlbumArtUrl != current.AlbumArtUrl ||
           previous.Duration != current.Duration; // Duration can change on track change
  }

  private static bool HasQueueChanged(List<QueueItemDto>? previous, List<QueueItemDto>? current)
  {
    if (previous == null || current == null)
    {
      return true;
    }
    if (previous.Count != current.Count)
    {
      return true;
    }

    // Check if items are the same (by ID, order, and state)
    for (int i = 0; i < previous.Count; i++)
    {
      if (previous[i].Id != current[i].Id ||
          previous[i].Index != current[i].Index ||
          previous[i].IsCurrent != current[i].IsCurrent ||
          previous[i].State != current[i].State)
      {
        return true;
      }
    }

    return false;
  }

  private static bool HasRadioStateChanged(RadioStateDto? previous, RadioStateDto? current)
  {
    if (previous == null || current == null)
    {
      return true;
    }

    // Use a tolerance for signal strength so minor fluctuations
    // don't flood the Web UI with re-fetches that starve visualization.
    var sigDelta = Math.Abs((previous.SignalStrength ?? 0) - (current.SignalStrength ?? 0));

    return Math.Abs(previous.Frequency - current.Frequency) > 0.001 ||
           previous.Band != current.Band ||
           Math.Abs(previous.Step - current.Step) > 0.001 ||
           sigDelta > 3 ||
           previous.Equalizer != current.Equalizer ||
           previous.DeviceVolume != current.DeviceVolume ||
           previous.IsScanning != current.IsScanning ||
           previous.ScanDirection != current.ScanDirection ||
           previous.IsStereo != current.IsStereo ||
           previous.RdsStationName != current.RdsStationName;
  }

  private static bool HasVolumeChanged(VolumeDto? previous, VolumeDto? current)
  {
    if (previous == null || current == null)
    {
      return true;
    }

    return Math.Abs(previous.Volume - current.Volume) > 0.001f ||
           previous.IsMuted != current.IsMuted ||
           Math.Abs(previous.Balance - current.Balance) > 0.001f;
  }

  // DTO mapping methods
  private PlaybackStateDto BuildPlaybackStateDto(IAudioSource? activeSource)
  {
    var dto = new PlaybackStateDto
    {
      IsPlaying = activeSource?.State == AudioSourceState.Playing,
      IsPaused = activeSource?.State == AudioSourceState.Paused,
      Volume = _audioManager?.MasterVolume ?? 0.0f,
      IsMuted = _audioManager?.IsMuted ?? false,
      Balance = _audioManager?.Balance ?? 0.0f,
      Position = activeSource is IPrimaryAudioSource primary ? primary.Position : null,
      Duration = activeSource is IPrimaryAudioSource primaryDur ? primaryDur.Duration : null,
      ActiveSource = activeSource?.MapToDto()
    };

    // Add capability flags if primary source
    if (activeSource is IPrimaryAudioSource primarySource)
    {
      dto.CanNext = primarySource.SupportsNext;
      dto.CanPrevious = primarySource.SupportsPrevious;
      dto.CanShuffle = primarySource.SupportsShuffle;
      dto.CanRepeat = primarySource.SupportsRepeat;
      dto.IsShuffleEnabled = primarySource.IsShuffleEnabled;
      dto.RepeatMode = primarySource.RepeatMode.ToString();
    }

    return dto;
  }

  private NowPlayingDto BuildNowPlayingDto(IAudioSource? activeSource)
  {
    var dto = new NowPlayingDto
    {
      SourceType = activeSource?.Type.ToString() ?? "None",
      SourceName = activeSource?.Name ?? "No Source",
      IsPlaying = activeSource?.State == AudioSourceState.Playing,
      IsPaused = activeSource?.State == AudioSourceState.Paused,
      Position = activeSource is IPrimaryAudioSource primary ? primary.Position : null,
      Duration = activeSource is IPrimaryAudioSource primaryDur ? primaryDur.Duration : null
    };

    // Calculate progress percentage
    if (dto.Duration.HasValue && dto.Duration.Value.TotalSeconds > 0 && dto.Position.HasValue)
    {
      dto.ProgressPercentage = (dto.Position.Value.TotalSeconds / dto.Duration.Value.TotalSeconds) * 100.0;
    }

    // Get metadata if available
    if (activeSource is IPrimaryAudioSource primaryMeta)
    {
      _logger.LogDebug("Building NowPlaying for {SourceName}: HasMetadata={HasMetadata}, MetadataCount={Count}",
        primaryMeta.Name, primaryMeta.Metadata != null, primaryMeta.Metadata?.Count ?? 0);

      if (primaryMeta.Metadata != null && primaryMeta.Metadata.Count > 0)
      {
        // Log metadata keys for debugging
        _logger.LogDebug("Metadata keys: {Keys}", string.Join(", ", primaryMeta.Metadata.Keys));

        var metadataDto = primaryMeta.MapToNowPlaying();
        dto.Title = metadataDto.Title;
        dto.Artist = metadataDto.Artist;
        dto.Album = metadataDto.Album;
        dto.AlbumArtUrl = metadataDto.AlbumArtUrl;
        dto.ExtendedMetadata = metadataDto.ExtendedMetadata;

        _logger.LogDebug("Extracted metadata: Title={Title}, Artist={Artist}, Album={Album}",
          dto.Title, dto.Artist, dto.Album);
      }
      else
      {
        _logger.LogDebug("No metadata available for source {SourceName}", primaryMeta.Name);
      }
    }

    return dto;
  }


  private static QueueItemDto MapToQueueItemDto(QueueItem item)
  {
    return new QueueItemDto
    {
      Id = item.Id,
      Title = item.Title,
      Artist = item.Artist,
      Album = item.Album,
      Duration = FormatDuration(item.Duration),
      AlbumArtUrl = item.AlbumArtUrl,
      Index = item.Index,
      IsCurrent = item.IsCurrent,
      State = item.State.ToString(),
      FullPlaylistIndex = item.FullPlaylistIndex
    };
  }

  private static string? FormatDuration(TimeSpan? duration)
  {
    if (duration == null) return null;
    var ts = duration.Value;
    return ts.TotalHours >= 1
      ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
      : $"{ts.Minutes}:{ts.Seconds:D2}";
  }

  private static RadioStateDto MapToRadioStateDto(IRadioControl radioControls)
  {
    return new RadioStateDto
    {
      Frequency = radioControls.CurrentFrequency.Hertz,
      Band = radioControls.CurrentBand.ToString(),
      Step = radioControls.FrequencyStep.Hertz,
      SignalStrength = radioControls.SignalStrength,
      Equalizer = radioControls.EqualizerMode.ToString(),
      DeviceVolume = radioControls.DeviceVolume,
      IsScanning = radioControls.IsScanning,
      ScanDirection = radioControls.ScanDirection?.ToString(),
      ScanStopThreshold = radioControls.ScanStopThreshold,
      AutoGain = radioControls.AutoGainEnabled,
      Gain = (int?)radioControls.Gain,
      IsStereo = radioControls.IsStereo,
      RdsStationName = radioControls.RdsStationName,
    };
  }

  private BluetoothStatusDto BuildBluetoothStatusDto()
  {
    return new BluetoothStatusDto
    {
      IsAvailable = _bluetoothService?.IsAvailable ?? false,
      State = _bluetoothService?.State.ToString() ?? BluetoothAdapterState.Unknown.ToString(),
      ConnectedDevice = _bluetoothService?.ConnectedDevice != null ? MapDevice(_bluetoothService.ConnectedDevice) : null,
      PairedDevices = _bluetoothService?.PairedDevices.Select(MapDevice).ToList() ?? [],
      DiscoveredDevices = _bluetoothService?.DiscoveredDevices.Select(MapDevice).ToList() ?? [],
      IsDiscovering = _bluetoothService?.IsDiscovering ?? false
    };
  }

  private static BluetoothDeviceDto MapDevice(BluetoothDeviceInfo device)
  {
    return new BluetoothDeviceDto
    {
      Address = device.Address,
      Name = device.Name,
      IsPaired = device.IsPaired,
      IsConnected = device.IsConnected,
      LastConnected = device.LastConnected
    };
  }

  private async void OnBluetoothStateChanged(object? sender, BluetoothAdapterStateChangedEventArgs e)
  {
    try
    {
      var status = BuildBluetoothStatusDto();
      await _hubContext.Clients.Group("Bluetooth").SendAsync("BluetoothStateChanged", status);
      _logger.LogDebug("Broadcast BluetoothStateChanged: {State}", status.State);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error broadcasting Bluetooth state change");
    }
  }

  private async void OnBluetoothDeviceConnected(object? sender, BluetoothDeviceConnectedEventArgs e)
  {
    try
    {
      var dto = MapDevice(e.Device);
      await _hubContext.Clients.Group("Bluetooth").SendAsync("BluetoothDeviceConnected", dto);
      await _hubContext.Clients.Group("Bluetooth").SendAsync("BluetoothStateChanged", BuildBluetoothStatusDto());
      _logger.LogDebug("Broadcast BluetoothDeviceConnected: {Device}", dto.Name);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error broadcasting Bluetooth device connection");
    }
  }

  private async void OnBluetoothDeviceDisconnected(object? sender, BluetoothDeviceDisconnectedEventArgs e)
  {
    try
    {
      var dto = MapDevice(e.Device);
      await _hubContext.Clients.Group("Bluetooth").SendAsync("BluetoothDeviceDisconnected", dto);
      await _hubContext.Clients.Group("Bluetooth").SendAsync("BluetoothStateChanged", BuildBluetoothStatusDto());
      _logger.LogDebug("Broadcast BluetoothDeviceDisconnected: {Device}", dto.Name);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error broadcasting Bluetooth device disconnection");
    }
  }

  /// <summary>
  /// Handles external Cast device volume/mute changes.
  /// Updates IAudioManager so the console and UI stay in sync.
  /// </summary>
  private void OnCastVolumeChanged(object? sender, CastVolumeChangedEventArgs e)
  {
    if (_audioManager == null) return;

    try
    {
      // Update master volume and mute to match the Cast device
      if (Math.Abs(_audioManager.MasterVolume - e.Volume) > 0.01f)
      {
        _audioManager.MasterVolume = e.Volume;
        _logger.LogInformation(
          "Synced volume from Cast device: {Volume:P0} (initial: {IsInitial})",
          e.Volume, e.IsInitialSync);
      }

      if (_audioManager.IsMuted != e.IsMuted)
      {
        _audioManager.IsMuted = e.IsMuted;
        _logger.LogInformation(
          "Synced mute from Cast device: {Muted} (initial: {IsInitial})",
          e.IsMuted, e.IsInitialSync);
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error syncing Cast device volume to AudioManager");
    }
  }

  private async void OnBluetoothDeviceDiscovered(object? sender, BluetoothDeviceDiscoveredEventArgs e)
  {
    try
    {
      var dto = MapDevice(e.Device);
      await _hubContext.Clients.Group("Bluetooth").SendAsync("BluetoothDeviceDiscovered", dto);
      _logger.LogDebug("Broadcast BluetoothDeviceDiscovered: {Device}", dto.Name);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error broadcasting Bluetooth device discovery");
    }
  }

  private async void OnFingerprintStatusChanged(object? sender, FingerprintStatusSnapshot snapshot)
  {
    try
    {
      var dto = snapshot.MapToDto();
      await _hubContext.Clients.All.SendAsync("FingerprintStatusChanged", dto);
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Error broadcasting fingerprint status change");
    }
  }

  public override void Dispose()
  {
    if (_bluetoothService != null)
    {
      _bluetoothService.StateChanged -= OnBluetoothStateChanged;
      _bluetoothService.DeviceConnected -= OnBluetoothDeviceConnected;
      _bluetoothService.DeviceDisconnected -= OnBluetoothDeviceDisconnected;
      _bluetoothService.DeviceDiscovered -= OnBluetoothDeviceDiscovered;
      // BT VolumeChanged no longer subscribed
    }

    if (_castOutput != null)
    {
      _castOutput.CastVolumeChanged -= OnCastVolumeChanged;
    }

    if (_fingerprintService != null)
    {
      _fingerprintService.StatusChanged -= OnFingerprintStatusChanged;
    }

    if (_vizModeService != null)
    {
      _vizModeService.ModeChanged -= OnVisualizationModeChanged;
    }

    if (_encoderService != null)
    {
      _encoderService.ConnectionChanged -= OnEncoderConnectionChanged;
    }

    base.Dispose();
  }

  private async void OnEncoderConnectionChanged(object? sender, EncoderConnectionEventArgs e)
  {
    try
    {
      await _hubContext.Clients.All.SendAsync("EncoderConnectionChanged");
      _logger.LogDebug("Broadcast EncoderConnectionChanged: IsConnected={IsConnected}", e.IsConnected);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error broadcasting encoder connection change");
    }
  }

  private async void OnVisualizationModeChanged(object? sender, VisualizationModeChangedEventArgs e)
  {
    try
    {
      await _hubContext.Clients.All.SendAsync("VisualizationModeChanged", new
      {
        e.Mode,
        e.IsEnabled
      });
      _logger.LogDebug("Broadcast VisualizationModeChanged: {Mode}, Enabled={Enabled}", e.Mode, e.IsEnabled);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error broadcasting visualization mode change");
    }
  }
}

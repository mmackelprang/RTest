using Microsoft.AspNetCore.SignalR;
using Radio.API.Hubs;
using Radio.API.Mappers;
using Radio.API.Models;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Outputs;
using System.Linq;

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

  /// <summary>
  /// Initializes a new instance of the AudioStateUpdateService.
  /// </summary>
  public AudioStateUpdateService(
    ILogger<AudioStateUpdateService> logger,
    IHubContext<AudioStateHub> hubContext,
    IServiceProvider serviceProvider)
  {
    _logger = logger;
    _hubContext = hubContext;
    
    // Try to get IAudioManager, but don't fail if it's not available
    // This allows the service to start even if audio infrastructure isn't fully initialized
    _audioManager = serviceProvider.GetService<IAudioManager>();
    _bluetoothService = serviceProvider.GetService<IBluetoothService>();
    _castOutput = serviceProvider.GetService<GoogleCastOutput>();
    
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
    }
    else
    {
      _logger.LogWarning("IBluetoothService not available - Bluetooth SignalR updates disabled");
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
      _logger.LogInformation("Broadcast NowPlayingChanged: Title={Title}, Artist={Artist}, Album={Album}, AlbumArt={AlbumArtUrl}, Source={Source}",
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
      await _castOutput.UpdateNowPlayingMetadataAsync(
        nowPlaying.Title,
        nowPlaying.Artist,
        nowPlaying.Album,
        nowPlaying.AlbumArtUrl,
        cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Failed to push metadata to Cast device");
    }
  }

  private async Task CheckQueueAsync(IAudioSource? activeSource, CancellationToken cancellationToken)
  {
    if (activeSource is not IPlayQueue playQueue)
    {
      return;
    }

    var currentQueue = playQueue.QueueItems
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

    // Check if items are the same (by ID and order)
    for (int i = 0; i < previous.Count; i++)
    {
      if (previous[i].Id != current[i].Id ||
          previous[i].Index != current[i].Index ||
          previous[i].IsCurrent != current[i].IsCurrent)
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

    return Math.Abs(previous.Frequency - current.Frequency) > 0.001 ||
           previous.Band != current.Band ||
           Math.Abs(previous.Step - current.Step) > 0.001 ||
           previous.SignalStrength != current.SignalStrength ||
           previous.Equalizer != current.Equalizer ||
           previous.DeviceVolume != current.DeviceVolume ||
           previous.IsScanning != current.IsScanning ||
           previous.ScanDirection != current.ScanDirection;
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
      IsCurrent = item.IsCurrent
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
      AutoGain = radioControls.AutoGainEnabled,
      Gain = (int?)radioControls.Gain,
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

  public override void Dispose()
  {
    if (_bluetoothService != null)
    {
      _bluetoothService.StateChanged -= OnBluetoothStateChanged;
      _bluetoothService.DeviceConnected -= OnBluetoothDeviceConnected;
      _bluetoothService.DeviceDisconnected -= OnBluetoothDeviceDisconnected;
      _bluetoothService.DeviceDiscovered -= OnBluetoothDeviceDiscovered;
    }

    base.Dispose();
  }
}

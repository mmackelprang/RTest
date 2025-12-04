using Microsoft.AspNetCore.SignalR;
using Radio.API.Hubs;
using Radio.API.Models;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;

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
    
    if (_audioManager == null)
    {
      _logger.LogWarning("IAudioManager not available - AudioStateUpdateService will not broadcast updates");
      IsEnabled = false;
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
      _logger.LogDebug("Broadcast NowPlayingChanged");
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
    if (activeSource is not IRadioControls radioControls)
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
      Balance = 0.0f // TODO: Get balance from audio manager if available
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
    if (previous == null || current == null) return true;
    
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
    if (previous == null || current == null) return true;

    return previous.SourceType != current.SourceType ||
           previous.SourceName != current.SourceName ||
           previous.IsPlaying != current.IsPlaying ||
           previous.IsPaused != current.IsPaused ||
           previous.Title != current.Title ||
           previous.Artist != current.Artist ||
           previous.Album != current.Album ||
           previous.AlbumArtUrl != current.AlbumArtUrl ||
           previous.Position != current.Position ||
           previous.Duration != current.Duration;
  }

  private static bool HasQueueChanged(List<QueueItemDto>? previous, List<QueueItemDto>? current)
  {
    if (previous == null || current == null) return true;
    if (previous.Count != current.Count) return true;

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
    if (previous == null || current == null) return true;

    return Math.Abs(previous.Frequency - current.Frequency) > 0.001 ||
           previous.Band != current.Band ||
           Math.Abs(previous.FrequencyStep - current.FrequencyStep) > 0.001 ||
           previous.SignalStrength != current.SignalStrength ||
           previous.IsStereo != current.IsStereo ||
           previous.EqualizerMode != current.EqualizerMode ||
           previous.DeviceVolume != current.DeviceVolume ||
           previous.IsScanning != current.IsScanning ||
           previous.ScanDirection != current.ScanDirection;
  }

  private static bool HasVolumeChanged(VolumeDto? previous, VolumeDto? current)
  {
    if (previous == null || current == null) return true;

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
      Balance = 0.0f, // TODO: Get from audio manager
      Position = activeSource is IPrimaryAudioSource primary ? primary.Position : null,
      Duration = activeSource is IPrimaryAudioSource primaryDur ? primaryDur.Duration : null,
      ActiveSource = activeSource != null ? MapToAudioSourceDto(activeSource) : null
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
    if (activeSource is IPrimaryAudioSource primaryMeta && primaryMeta.Metadata != null)
    {
      if (primaryMeta.Metadata.TryGetValue("Title", out var title) && title != null)
        dto.Title = title.ToString() ?? "No Track";
      if (primaryMeta.Metadata.TryGetValue("Artist", out var artist) && artist != null)
        dto.Artist = artist.ToString() ?? "--";
      if (primaryMeta.Metadata.TryGetValue("Album", out var album) && album != null)
        dto.Album = album.ToString() ?? "--";
      if (primaryMeta.Metadata.TryGetValue("AlbumArtUrl", out var artUrl) && artUrl != null)
        dto.AlbumArtUrl = artUrl.ToString() ?? "/images/default-album-art.png";

      // Copy extended metadata
      dto.ExtendedMetadata = new Dictionary<string, object>(primaryMeta.Metadata);
    }

    return dto;
  }

  private static AudioSourceDto MapToAudioSourceDto(IAudioSource source)
  {
    return new AudioSourceDto
    {
      Id = source.Id,
      Name = source.Name,
      Type = source.Type.ToString(),
      Category = source.Category.ToString(),
      State = source.State.ToString(),
      Volume = source.Volume,
      Metadata = source is IPrimaryAudioSource primary
        ? primary.Metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
        : new Dictionary<string, object>()
    };
  }

  private static QueueItemDto MapToQueueItemDto(QueueItem item)
  {
    return new QueueItemDto
    {
      Id = item.Id,
      Title = item.Title,
      Artist = item.Artist,
      Album = item.Album,
      Duration = item.Duration,
      AlbumArtUrl = item.AlbumArtUrl,
      Index = item.Index,
      IsCurrent = item.IsCurrent
    };
  }

  private static RadioStateDto MapToRadioStateDto(IRadioControls radioControls)
  {
    return new RadioStateDto
    {
      Frequency = radioControls.CurrentFrequency,
      Band = radioControls.CurrentBand.ToString(),
      FrequencyStep = radioControls.FrequencyStep,
      SignalStrength = radioControls.SignalStrength,
      IsStereo = radioControls.IsStereo,
      EqualizerMode = radioControls.EqualizerMode.ToString(),
      DeviceVolume = radioControls.DeviceVolume,
      IsScanning = radioControls.IsScanning,
      ScanDirection = radioControls.ScanDirection?.ToString()
    };
  }
}

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
using Radio.Fingerprinting.Services;

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
  private readonly IRotaryEncoderService? _encoderService;
  private readonly IEncoderFeedbackSink? _encoderFeedback;
  private readonly IEventPlaybackService? _eventPlayback;
  private string? _apiBaseUrl;

  /// <summary>
  /// Gets or sets the update interval in milliseconds (default: 500ms).
  /// </summary>
  public int UpdateIntervalMs { get; set; } = 500;

  /// <summary>
  /// Gets or sets whether broadcasting is enabled (default: true).
  /// </summary>
  public bool IsEnabled { get; set; } = true;

  // Throttle fingerprint status broadcasts to max once per 3 seconds.
  // The fingerprint service fires StatusChanged rapidly during identification cycles,
  // and each broadcast causes the Web client to make an HTTP API call + re-render.
  private DateTime _lastFingerprintBroadcast = DateTime.MinValue;
  private static readonly TimeSpan FingerprintBroadcastThrottle = TimeSpan.FromSeconds(3);

  // Cached state to detect changes
  private PlaybackStateDto? _lastPlaybackState;
  private NowPlayingDto? _lastNowPlaying;
  private List<QueueItemDto>? _lastQueue;
  // Lightweight queue snapshot for cheap change detection (avoids building full DTOs every 500ms)
  private List<(string Id, int Index, bool IsCurrent, string State)>? _lastQueueSnapshot;
  private RadioStateDto? _lastRadioState;
  private VolumeDto? _lastVolume;
  private string? _lastActiveSourceType;

  // PR 2 of the Radio Controller Polish arc — caches the MatchId of the
  // fingerprint event currently anchored as the playing match. The
  // recognition stream in NowPlayingPanel binds against this to render the
  // NOW header + amber left border above the correct row. Updated by
  // OnFingerprintStatusChanged when the snapshot's most-recent match changes;
  // cleared when the active source changes (OnSourceChanged equivalent) or
  // when the latest event is not a match. Volatile because reads happen on
  // the background-polling thread and writes on the SignalR callback thread.
  private volatile string? _currentMatchId;

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
    _encoderService = serviceProvider.GetService<IRotaryEncoderService>();
    // GetService, not GetRequiredService: the encoder subsystem may not be registered at all, which
    // is the same reason _encoderService is nullable.
    _encoderFeedback = serviceProvider.GetService<IEncoderFeedbackSink>();

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

    // Subscribe to encoder connection changes for UI status updates
    if (_encoderService != null)
    {
      _encoderService.ConnectionChanged += OnEncoderConnectionChanged;
      _logger.LogInformation("Subscribed to encoder connection changes");
    }

    // ENC-12: the config-fault push path. The Settings page polls at 2 Hz while it is open, which is
    // useless for a badge that must be correct on /queue and /metrics, so the tier — and only the
    // tier — goes out on the hub.
    if (_encoderService != null)
    {
      _encoderService.ConfigStatusChanged += OnEncoderConfigStatusChanged;
      _logger.LogInformation("Subscribed to encoder config status changes");
    }

    // ENC-4: the encoder HUD push path. Separate from the 500ms volume poller below because a
    // 2 Hz poller cannot meet the 100ms feedback requirement and does not carry which knob moved.
    if (_encoderFeedback != null)
    {
      _encoderFeedback.Feedback += OnEncoderHudChanged;
      _logger.LogInformation("Subscribed to encoder HUD feedback");
    }

    // ADR-029 D6 §8.1. GetService rather than GetRequiredService, matching every sibling above: this
    // service has to start even when parts of the audio stack are not registered at all.
    _eventPlayback = serviceProvider.GetService<IEventPlaybackService>();

    if (_eventPlayback != null)
    {
      // ⚠ Change-driven. There is deliberately NO position tick and this must never move into
      // CheckAndBroadcastUpdatesAsync's 500 ms loop — ADR-029 §8.2 refuses one outright, because a
      // tick puts a timer on the server and a message on the wire per client for the whole duration,
      // on a box where CPU churn is audible.
      _eventPlayback.PlaybackChanged += OnEventPlaybackChanged;
      _logger.LogInformation("Subscribed to attended event playback transitions");
    }
    else
    {
      _logger.LogWarning(
        "IEventPlaybackService not available - event playback SignalR updates disabled");
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
    {
      return albumArtUrl;
    }

    // Already absolute or data URI — return as-is
    if (albumArtUrl.StartsWith("http://") || albumArtUrl.StartsWith("https://") || albumArtUrl.StartsWith("data:"))
    {
      return albumArtUrl;
    }

    // Relative path (e.g., /api/albumart/abc123.jpg) — prepend API base URL
    if (_apiBaseUrl != null && albumArtUrl.StartsWith("/"))
    {
      return _apiBaseUrl + albumArtUrl;
    }

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
      {
        continue;
      }

      if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
      {
        continue;
      }

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
        {
          continue;
        }

        if (!isVirtual)
        {
          return addr.Address.ToString();
        }

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

    // Build lightweight snapshot first for cheap change detection.
    // Only construct full DTOs when something actually changed.
    var snapshot = fullPlaylist
      .Select(item => (item.Id, item.Index, item.IsCurrent, State: item.State.ToString()))
      .ToList();

    if (!HasQueueSnapshotChanged(_lastQueueSnapshot, snapshot))
    {
      return;
    }

    var currentQueue = fullPlaylist
      .Select(MapToQueueItemDto)
      .ToList();

    _lastQueueSnapshot = snapshot;
    _lastQueue = currentQueue;
    await _hubContext.Clients.Group("Queue")
      .SendAsync("QueueChanged", currentQueue, cancellationToken);
    _logger.LogDebug("Broadcast QueueChanged with {Count} items", currentQueue.Count);
  }

  private async Task CheckRadioStateAsync(IAudioSource? activeSource, CancellationToken cancellationToken)
  {
    if (activeSource is not IRadioControl radioControls)
    {
      return;
    }

    var currentRadioState = radioControls.MapToRadioStateDto(_currentMatchId);

    if (HasRadioStateChanged(_lastRadioState, currentRadioState))
    {
      // Stamp the per-broadcast discriminator BEFORE caching/sending so the
      // Web RDS path can skip its accumulator append on telemetry-only ticks.
      // Computed against the PREVIOUS state (the same baseline HasRadioStateChanged
      // used), so the very first broadcast (_lastRadioState == null) is RDS-relevant.
      currentRadioState.RdsRelevantChanged = HasRdsRelevantChanged(_lastRadioState, currentRadioState);

      _lastRadioState = currentRadioState;
      await _hubContext.Clients.Group("RadioState")
        .SendAsync("RadioStateChanged", currentRadioState, cancellationToken);
      _logger.LogDebug("Broadcast RadioStateChanged: {Frequency} {Band} RdsRelevant={Rds}",
        currentRadioState.Frequency, currentRadioState.Band, currentRadioState.RdsRelevantChanged);
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

  private static bool HasQueueSnapshotChanged(
    List<(string Id, int Index, bool IsCurrent, string State)>? previous,
    List<(string Id, int Index, bool IsCurrent, string State)>? current)
  {
    if (previous == null || current == null)
    {
      return true;
    }

    if (previous.Count != current.Count)
    {
      return true;
    }

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
           previous.Clip != current.Clip ||
           // Same 3-percent tolerance applied via the dBu mapping (~1.8 dBu)
           // so the meter doesn't flicker on every minor fluctuation.
           Math.Abs(previous.RssiDbu - current.RssiDbu) > 1.8 ||
           Math.Abs(previous.AppliedGain - current.AppliedGain) > 0.1 ||
           previous.Equalizer != current.Equalizer ||
           previous.DeviceVolume != current.DeviceVolume ||
           previous.IsScanning != current.IsScanning ||
           previous.ScanDirection != current.ScanDirection ||
           previous.IsStereo != current.IsStereo ||
           previous.RdsStationName != current.RdsStationName ||
           previous.RdsStationNameStable != current.RdsStationNameStable ||
           previous.RdsProgramType != current.RdsProgramType ||
           // Task #80 v4 — broadcast on PI change so the call-sign decode
           // reaches the UI the instant the first RDS frame is decoded
           // after a tune, without waiting for some other state delta.
           previous.RdsPi != current.RdsPi ||
           // PR 3 of the Radio Controller Polish arc — the RT line below the
           // frequency well binds to this. RDS RadioText updates roughly once
           // per minute on most stations; null-safe string comparison is
           // sufficient here.
           previous.RdsRadioText != current.RdsRadioText ||
           // PR 2 of the Radio Controller Polish arc — the recognition stream's
           // NOW row anchors on this. Changes must broadcast so the UI's
           // amber-border row tracks the actively-playing fingerprint match.
           previous.NowPlayingMatchId != current.NowPlayingMatchId;
  }

  /// <summary>
  /// True when an RDS- or tuning-relevant field changed between broadcasts —
  /// the fields the Web RDS card, frequency well, and active-preset highlight
  /// bind to. Deliberately EXCLUDES volatile signal telemetry (signal strength,
  /// RSSI, clip, applied/manual gain, AGC, stereo, equalizer, device volume,
  /// scan state) so the RDS marquee doesn't re-run its accumulator ~twice a
  /// second. A null previous (first broadcast after tune/source-switch) counts
  /// as relevant so the card populates immediately.
  ///
  /// This is a strict subset of <see cref="HasRadioStateChanged"/>'s conditions
  /// (RDS/tuning rows only), so any tick that is RDS-relevant is also a
  /// broadcast — the flag can never be true without a broadcast happening.
  /// </summary>
  private static bool HasRdsRelevantChanged(RadioStateDto? previous, RadioStateDto? current)
  {
    if (previous == null || current == null)
    {
      return true;
    }

    return Math.Abs(previous.Frequency - current.Frequency) > 0.001 ||
           previous.Band != current.Band ||
           Math.Abs(previous.Step - current.Step) > 0.001 ||
           previous.RdsStationName != current.RdsStationName ||
           previous.RdsStationNameStable != current.RdsStationNameStable ||
           previous.RdsProgramType != current.RdsProgramType ||
           previous.RdsPi != current.RdsPi ||
           previous.RdsRadioText != current.RdsRadioText ||
           previous.NowPlayingMatchId != current.NowPlayingMatchId;
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
        dto.FilePath = metadataDto.FilePath;
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
    if (duration == null)
    {
      return null;
    }

    var ts = duration.Value;
    return ts.TotalHours >= 1
      ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
      : $"{ts.Minutes}:{ts.Seconds:D2}";
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
    if (_audioManager == null)
    {
      return;
    }

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
      // Update the "currently playing match" anchor on every snapshot — this is
      // independent of the broadcast throttle below so the RadioState path
      // (which broadcasts on its own cadence in CheckRadioStateAsync) always
      // has a fresh value. The anchor is the most-recent matched event; when
      // the latest event is not a match (no-match window, error, or first
      // capture) we clear it so the recognition stream falls back to its
      // "no current match" rendering.
      UpdateCurrentMatchAnchor(snapshot);

      var now = DateTime.UtcNow;
      if (now - _lastFingerprintBroadcast < FingerprintBroadcastThrottle)
      {
        return;
      }

      _lastFingerprintBroadcast = now;

      var dto = snapshot.MapToDto();
      await _hubContext.Clients.All.SendAsync("FingerprintStatusChanged", dto);
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Error broadcasting fingerprint status change");
    }
  }

  /// <summary>
  /// Maintains the <see cref="_currentMatchId"/> anchor used by the recognition
  /// stream's NOW row. The anchor is the most-recent matched event in the
  /// snapshot; when the most-recent event is a no-match (or there are no
  /// events yet) we clear the anchor so the UI doesn't claim a stale row as
  /// "now playing".
  /// </summary>
  private void UpdateCurrentMatchAnchor(FingerprintStatusSnapshot snapshot)
  {
    // Latest-first scan: the most-recent event is at the end of the list per
    // BackgroundIdentificationService's append semantics. We anchor on the
    // most-recent MATCHED event so a fresh no-match capture at the tail
    // doesn't blank the NOW row mid-track. Only when no recent matches
    // exist at all do we clear.
    string? latestMatch = null;
    for (var i = snapshot.RecentEvents.Count - 1; i >= 0; i--)
    {
      var evt = snapshot.RecentEvents[i];
      if (evt.IsMatch && !string.IsNullOrEmpty(evt.MatchId))
      {
        latestMatch = evt.MatchId;
        break;
      }
    }
    _currentMatchId = latestMatch;
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

    if (_encoderService != null)
    {
      _encoderService.ConnectionChanged -= OnEncoderConnectionChanged;
      _encoderService.ConfigStatusChanged -= OnEncoderConfigStatusChanged;
    }

    if (_encoderFeedback != null)
    {
      _encoderFeedback.Feedback -= OnEncoderHudChanged;
    }

    if (_eventPlayback != null)
    {
      _eventPlayback.PlaybackChanged -= OnEventPlaybackChanged;
    }

    base.Dispose();
  }

  private async void OnEncoderConnectionChanged(object? sender, EncoderConnectionEventArgs e)
  {
    try
    {
      // ENC-0: the payload is the point. This used to broadcast the bare event name, so a client
      // learned that the encoder's connection state had changed but not what it changed TO — and the
      // notification policy is asymmetric, so absent-at-boot and dropped-mid-session must be
      // distinguishable. They share IsConnected=false.
      await _hubContext.Clients.All.SendAsync("EncoderConnectionChanged", new
      {
        e.IsConnected,
        e.WasEverConnected,
      });
      _logger.LogDebug(
        "Broadcast EncoderConnectionChanged: IsConnected={IsConnected}, WasEverConnected={WasEver}",
        e.IsConnected, e.WasEverConnected);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error broadcasting encoder connection change");
    }
  }

  private async void OnEncoderConfigStatusChanged(object? sender, EncoderConfigStatusEventArgs e)
  {
    try
    {
      // The tier only. No field detail: the 24-field comparison belongs on the Settings page (ENC-8),
      // which is the only place it is actionable, and shipping it to every circuit on every change
      // would be traffic nobody reads.
      //
      // Sent as strings for the same reason EncoderHudDto.Phase is: an unknown tier from a newer API
      // build must degrade to "show nothing special" on a kiosk nobody is watching, not throw during
      // deserialization.
      await _hubContext.Clients.All.SendAsync("EncoderConfigStatusChanged", new
      {
        Status = e.Status.ToString(),
        PreviousStatus = e.PreviousStatus.ToString(),
      });
      // Information, where the adjacent connection broadcast logs at Debug, and that asymmetry is
      // deliberate. The tier changes a handful of times per connection rather than continuously, so
      // this is not volume; and since LOG-11 the API's console sink is level-restricted to WARNING
      // and above, which under systemd means Information no longer reaches journald at all — it goes
      // to the file sink under /opt/radio-console/logs, which is where a "why is the volume knob
      // sluggish" question is actually triaged from. At Debug it would not be written anywhere in
      // production.
      _logger.LogInformation(
        "Broadcast EncoderConfigStatusChanged: {Previous} -> {Status}", e.PreviousStatus, e.Status);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error broadcasting encoder config status");
    }
  }

  /// <summary>
  /// Broadcasts one attended-playback transition (ADR-029 D6 §8.1).
  /// </summary>
  /// <remarks>
  /// ⚠ THE ENUMS ARE SENT AS STRINGS, and it is not cosmetic. Radio.API registers
  /// JsonStringEnumConverter on AddControllers().AddJsonOptions ONLY (Program.cs:58-62); SignalR
  /// serialises through JsonHubProtocol.PayloadSerializerOptions, which this project never
  /// configures. Handing the record straight to SendAsync would put "state": 1 on the hub and
  /// "state": "Playing" on GET /api/audio/events/current — and ADR-029 §8.1 feeds BOTH into the same
  /// client field, the REST call as the seed and this as the update. ToString() makes them identical.
  /// It also means a Radio.Web build that predates a new state member receives an unrecognised STRING
  /// and can ignore it, rather than deserialising a number into an enum that has no such value — the
  /// same reason EncoderConfigStatusChanged above sends its tier as a string.
  ///
  /// ⚠ Every other field is copied verbatim, so both paths hand the same CLR types to STJ's own
  /// built-in TimeSpan and DateTimeOffset converters and cannot diverge on however those render.
  /// ⚠ NOT "the same serialiser" — they are two different JsonSerializerOptions instances (MVC's,
  /// from AddJsonOptions, and SignalR's, from JsonHubProtocol), which is the very fact the paragraph
  /// above exists to warn about. The conclusion holds; the reason had to be the converters.
  ///
  /// ⚠ The snapshot ARGUMENT is the payload. Do NOT enrich it from _eventPlayback.Current: this
  /// handler is invoked from inside EventPlaybackService.Raise, and Current is deliberately not
  /// retained for a playback that has been replaced — so a re-read would sometimes describe a
  /// different playback than the transition being broadcast. (It would not deadlock; Current takes
  /// that service's _stateLock, not its _gate. It would just occasionally lie.)
  ///
  /// ⚠ And do not call back into the seam at all. Every TERMINAL publish reaches Raise while
  /// EventPlaybackService holds its non-reentrant _gate, and that file's own remark says so to this
  /// PR by name: a subscriber that re-enters StopAsync or StartAsync deadlocks.
  ///
  /// async void with a catch-all, matching the eight sibling handlers here. This is also raised from
  /// arbitrary thread-pool threads since PHN-1d — a preemption arrives on a Task.Run — which
  /// IHubContext is safe for.
  /// </remarks>
  private async void OnEventPlaybackChanged(object? sender, EventPlaybackSnapshot snapshot)
  {
    try
    {
      await _hubContext.Clients.All.SendAsync("EventPlaybackChanged", new
      {
        snapshot.Id,
        Kind = snapshot.Kind.ToString(),
        snapshot.Label,
        State = snapshot.State.ToString(),
        snapshot.Duration,
        snapshot.PositionAtBroadcast,
        snapshot.BroadcastAtUtc,
        snapshot.FailureReason,
      });

      // Debug, matching PlaybackStateChanged. Label is user-supplied content and the id is a live
      // handle; neither belongs in a production line by default, and the state alone is what a
      // "why did the voicemail stop" question needs from this side.
      _logger.LogDebug("Broadcast EventPlaybackChanged: {State}", snapshot.State);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error broadcasting attended event playback state");
    }
  }

  private async void OnEncoderHudChanged(object? sender, EncoderHudEventArgs e)
  {
    try
    {
      // Already coalesced to >= 50 ms by EncoderFeedbackService - this method does not throttle.
      await _hubContext.Clients.All.SendAsync("EncoderHudChanged", new
      {
        e.EncoderIndex,
        e.Label,
        Phase = e.Phase.ToString(),
        e.VolumePercent,
        e.IsMuted,
        e.PrimaryText,
        e.SecondaryText,
        e.PrimaryIsFrequency,
        // ENC-5 selector payload. Null on every non-selector phase, so a volume card costs the same
        // bytes it did before.
        e.DurationMs,
        e.Title,
        e.TitleSuffix,
        e.Footer,
        e.EmptyPrimary,
        e.EmptySecondary,
        e.HighlightIndex,
        Rows = e.Rows?.Select(r => new
        {
          r.Id,
          r.Primary,
          r.Secondary,
          r.Ordinal,
          r.Icon,
          r.AccentVar,
          r.IsCurrent,
          r.IsAvailable,
          r.UnavailableReason,
        }),
      });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error broadcasting encoder HUD update");
    }
  }
}

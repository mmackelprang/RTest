using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Radio.Infrastructure.Platform.Bluetooth.Windows;

/// <summary>
/// Monitors Windows System Media Transport Controls (SMTC) for track metadata.
/// SMTC provides AVRCP-equivalent data: title, artist, album, duration, album art, playback status.
/// Requires Windows 10 1809+ (build 17763).
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class WindowsMediaSessionWatcher : IDisposable
{
  private readonly ILogger _logger;
  private readonly AlbumArtCacheService? _albumArtCache;
  private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
  private GlobalSystemMediaTransportControlsSession? _currentSession;
  private bool _disposed;

  /// <summary>Fired when track metadata changes (title, artist, album, duration, art).</summary>
  public event EventHandler<BluetoothPlaybackMetadata>? MetadataChanged;

  /// <summary>Fired when playback status changes (playing, paused, etc.).</summary>
  public event EventHandler<BluetoothPlaybackStatus>? PlaybackStatusChanged;

  /// <summary>Fired when playback position changes.</summary>
  public event EventHandler<TimeSpan>? PositionChanged;

  public WindowsMediaSessionWatcher(ILogger logger, AlbumArtCacheService? albumArtCache = null)
  {
    _logger = logger;
    _albumArtCache = albumArtCache;
  }

  /// <summary>
  /// Start monitoring SMTC media sessions for metadata events.
  /// </summary>
  public async Task StartAsync()
  {
    try
    {
      _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
      _sessionManager.SessionsChanged += OnSessionsChanged;

      // Attach to current session if one exists
      AttachToCurrentSession();
      _logger.LogInformation("SMTC media session watcher started");
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to start SMTC media session watcher");
    }
  }

  private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
  {
    _logger.LogDebug("SMTC sessions changed");
    AttachToCurrentSession();
  }

  private void AttachToCurrentSession()
  {
    try
    {
      var newSession = _sessionManager?.GetCurrentSession();

      if (newSession == _currentSession)
      {
        return;
      }

      // Detach from old session
      if (_currentSession != null)
      {
        _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        _currentSession.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
      }

      _currentSession = newSession;

      if (_currentSession != null)
      {
        _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
        _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
        _currentSession.TimelinePropertiesChanged += OnTimelinePropertiesChanged;

        _logger.LogInformation("SMTC attached to session: {SourceAppId}", _currentSession.SourceAppUserModelId);

        // Read initial state
        _ = ReadCurrentMediaPropertiesAsync();
        ReadCurrentPlaybackInfo();
        ReadCurrentTimelineProperties();
      }
      else
      {
        _logger.LogDebug("No active SMTC media session");
      }
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Error attaching to SMTC session");
    }
  }

  private async void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
  {
    await ReadCurrentMediaPropertiesAsync();
  }

  private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
  {
    ReadCurrentPlaybackInfo();
  }

  private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
  {
    ReadCurrentTimelineProperties();
  }

  private async Task ReadCurrentMediaPropertiesAsync()
  {
    if (_currentSession == null)
    {
      return;
    }

    try
    {
      var props = await _currentSession.TryGetMediaPropertiesAsync();
      if (props == null)
      {
        return;
      }

      string? albumArtUrl = null;
      if (props.Thumbnail != null)
      {
        albumArtUrl = await SaveThumbnailAsync(props.Thumbnail);
      }

      var duration = TimeSpan.Zero;
      try
      {
        var timeline = _currentSession.GetTimelineProperties();
        if (timeline != null)
        {
          duration = timeline.EndTime;
        }
      }
      catch
      {
        // Timeline may not be available
      }

      var metadata = new BluetoothPlaybackMetadata
      {
        Title = props.Title ?? string.Empty,
        Artist = props.Artist ?? string.Empty,
        Album = props.AlbumTitle ?? string.Empty,
        Duration = duration,
        AlbumArtUrl = albumArtUrl
      };

      _logger.LogDebug("SMTC metadata: {Title} by {Artist} (album: {Album}, art: {HasArt})",
        metadata.Title, metadata.Artist, metadata.Album, !string.IsNullOrEmpty(metadata.AlbumArtUrl));

      MetadataChanged?.Invoke(this, metadata);
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Error reading SMTC media properties");
    }
  }

  private void ReadCurrentPlaybackInfo()
  {
    if (_currentSession == null)
    {
      return;
    }

    try
    {
      var info = _currentSession.GetPlaybackInfo();
      if (info == null)
      {
        return;
      }

      var status = info.PlaybackStatus switch
      {
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => BluetoothPlaybackStatus.Playing,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => BluetoothPlaybackStatus.Paused,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => BluetoothPlaybackStatus.Stopped,
        _ => BluetoothPlaybackStatus.Stopped
      };

      _logger.LogDebug("SMTC playback status: {Status}", status);
      PlaybackStatusChanged?.Invoke(this, status);
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Error reading SMTC playback info");
    }
  }

  private void ReadCurrentTimelineProperties()
  {
    if (_currentSession == null)
    {
      return;
    }

    try
    {
      var timeline = _currentSession.GetTimelineProperties();
      if (timeline == null)
      {
        return;
      }

      PositionChanged?.Invoke(this, timeline.Position);
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Error reading SMTC timeline properties");
    }
  }

  private async Task<string?> SaveThumbnailAsync(IRandomAccessStreamReference thumbnail)
  {
    try
    {
      using var stream = await thumbnail.OpenReadAsync();
      using var inputStream = stream.AsStreamForRead();
      using var memoryStream = new MemoryStream();
      await inputStream.CopyToAsync(memoryStream);
      var bytes = memoryStream.ToArray();

      if (bytes.Length == 0)
      {
        return null;
      }

      // Detect MIME type from magic bytes
      var mime = AlbumArtCacheService.DetectMimeFromBytes(bytes) ?? "image/jpeg";

      // Prefer file cache (produces HTTP URL for Cast devices)
      if (_albumArtCache != null)
      {
        return await _albumArtCache.SaveAsync(bytes, mime);
      }

      // Fallback: base64 data URI (no cache service available)
      return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Failed to read SMTC album art thumbnail");
      return null;
    }
  }

  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }
    _disposed = true;

    if (_currentSession != null)
    {
      _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
      _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
      _currentSession.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
      _currentSession = null;
    }

    if (_sessionManager != null)
    {
      _sessionManager.SessionsChanged -= OnSessionsChanged;
      _sessionManager = null;
    }
  }
}

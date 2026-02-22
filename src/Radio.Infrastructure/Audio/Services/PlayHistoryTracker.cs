using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Radio.Core.Events;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;
using Radio.Infrastructure.Audio.Sources.Primary;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>
/// Tracks play history by subscribing to audio source state changes,
/// fingerprint identification events, and Bluetooth AVRCP metadata.
/// Extracted from AudioManager to separate play history concerns.
/// </summary>
public class PlayHistoryTracker : IDisposable
{
  private readonly ILogger<PlayHistoryTracker> _logger;
  private readonly IServiceScopeFactory _serviceScopeFactory;
  private readonly Func<IAudioSource?> _getActiveSource;
  private readonly BackgroundIdentificationService? _identificationService;
  private readonly IBluetoothService _bluetoothService;
  private readonly IMetricsCollector? _metricsCollector;

  private string? _currentPlayHistoryEntryId;
  private bool _disposed;

  public PlayHistoryTracker(
    ILogger<PlayHistoryTracker> logger,
    IServiceScopeFactory serviceScopeFactory,
    Func<IAudioSource?> getActiveSource,
    IBluetoothService bluetoothService,
    BackgroundIdentificationService? identificationService = null,
    IMetricsCollector? metricsCollector = null)
  {
    _logger = logger;
    _serviceScopeFactory = serviceScopeFactory;
    _getActiveSource = getActiveSource;
    _bluetoothService = bluetoothService;
    _identificationService = identificationService;
    _metricsCollector = metricsCollector;

    // Subscribe to events
    _bluetoothService.MetadataChanged += OnBluetoothMetadataChanged;

    if (_identificationService != null)
    {
      _identificationService.TrackIdentified += OnTrackIdentified;
      _identificationService.SongChanged += OnSongChanged;
    }
  }

  /// <summary>
  /// Subscribes to state changes for the given source to track play history.
  /// Called by AudioManager when a source is created/cached.
  /// </summary>
  public void SubscribeToSource(IAudioSource source)
  {
    source.StateChanged += OnSourceStateChanged;
  }

  /// <summary>
  /// Unsubscribes from state changes for the given source.
  /// Called by AudioManager during dispose.
  /// </summary>
  public void UnsubscribeFromSource(IAudioSource source)
  {
    source.StateChanged -= OnSourceStateChanged;
  }

  /// <summary>
  /// Handles source state changes to record play history when playback starts.
  /// Only records when a source transitions to Playing and is the active source.
  /// </summary>
  private async void OnSourceStateChanged(object? sender, AudioSourceStateChangedEventArgs e)
  {
    if (sender is not IAudioSource source)
      return;

    // Only record when transitioning to Playing state
    if (e.NewState != AudioSourceState.Playing)
      return;

    // Only track primary sources that are the active source
    if (source != _getActiveSource())
      return;

    _logger.LogInformation(
      "Source {SourceName} transitioned to Playing, recording play history",
      source.Name);
    await RecordPlayStartAsync(source);
  }

  /// <summary>
  /// Records a play history entry when a track starts playing.
  /// </summary>
  private async Task RecordPlayStartAsync(IAudioSource source)
  {
    try
    {
      using var scope = _serviceScopeFactory.CreateScope();
      var playHistoryRepository = scope.ServiceProvider.GetService<IPlayHistoryRepository>();
      if (playHistoryRepository == null)
      {
        _logger.LogWarning("IPlayHistoryRepository not available in DI scope, skipping play history recording");
        return;
      }

      var playSource = MapSourceTypeToPlaySource(source.Type);
      var metadata = GetSourceMetadata(source);

      // Build source details including basic metadata for display
      var sourceDetails = $"{metadata.Title} - {metadata.Artist}";

      // Get duration from source metadata if available
      int? durationSeconds = null;
      if (source is IPrimaryAudioSource ps && ps.Metadata != null &&
          ps.Metadata.TryGetValue(StandardMetadataKeys.Duration, out var durObj))
      {
        if (durObj is TimeSpan ts)
          durationSeconds = (int)ts.TotalSeconds;
        else if (double.TryParse(durObj?.ToString(), out var durVal))
          durationSeconds = (int)durVal;
      }

      // Persist the track metadata so the play history entry can reference it
      string? trackMetadataId = null;
      var metadataRepository = scope.ServiceProvider.GetService<ITrackMetadataRepository>();
      if (metadataRepository != null)
      {
        await metadataRepository.StoreAsync(metadata);
        trackMetadataId = metadata.Id;
      }
      else
      {
        _logger.LogWarning("ITrackMetadataRepository not available, play history entry will lack track metadata");
      }

      var entryId = Guid.NewGuid().ToString();
      var entry = new PlayHistoryEntry
      {
        Id = entryId,
        TrackMetadataId = trackMetadataId,
        FingerprintId = null,
        PlayedAt = DateTime.UtcNow,
        Source = playSource,
        MetadataSource = metadata.Source,
        SourceDetails = sourceDetails,
        DurationSeconds = durationSeconds,
        IdentificationConfidence = null,
        WasIdentified = trackMetadataId != null,
        Track = metadata
      };

      await playHistoryRepository.RecordPlayAsync(entry);
      _currentPlayHistoryEntryId = entryId;

      _logger.LogInformation(
        "Recorded play history entry {EntryId} for source {SourceName} (identified: {WasIdentified}, source: {PlaySource})",
        entryId, source.Name, entry.WasIdentified, playSource);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to record play history for source {SourceName}", source.Name);
    }
  }

  /// <summary>
  /// Gets metadata from the source. Always returns a TrackMetadata with at least
  /// a meaningful title and artist, using source-specific fallbacks.
  /// </summary>
  private static TrackMetadata GetSourceMetadata(IAudioSource source)
  {
    string? title = null;
    string? artist = null;
    string? album = null;
    string? coverArt = null;

    // Try to get metadata from the source's Metadata dictionary
    if (source is IPrimaryAudioSource primarySource)
    {
      var metadata = primarySource.Metadata;
      if (metadata != null && metadata.Count > 0)
      {
        title = metadata.TryGetValue(StandardMetadataKeys.Title, out var titleObj)
          ? titleObj?.ToString() : null;
        artist = metadata.TryGetValue(StandardMetadataKeys.Artist, out var artistObj)
          ? artistObj?.ToString() : null;
        album = metadata.TryGetValue(StandardMetadataKeys.Album, out var albumObj)
          ? albumObj?.ToString() : null;
        coverArt = metadata.TryGetValue(StandardMetadataKeys.AlbumArtUrl, out var coverObj)
          ? coverObj?.ToString() : null;
      }
    }

    // Strip default placeholders so we can apply better fallbacks
    if (title == StandardMetadataKeys.DefaultTitle) title = null;
    if (artist == StandardMetadataKeys.DefaultArtist) artist = null;
    if (album == StandardMetadataKeys.DefaultAlbum) album = null;
    if (coverArt == StandardMetadataKeys.DefaultAlbumArtUrl) coverArt = null;

    // Source-specific fallbacks for title
    if (string.IsNullOrWhiteSpace(title))
    {
      title = source.Type switch
      {
        AudioSourceType.Radio => GetRadioTitle(source),
        AudioSourceType.FilePlayer => GetFilePlayerTitle(source),
        AudioSourceType.Vinyl => "Vinyl",
        AudioSourceType.GenericUSB => "USB Audio",
        AudioSourceType.Bluetooth => "Bluetooth Audio",
        _ => source.Name
      };
    }

    // Source-specific fallbacks for artist
    if (string.IsNullOrWhiteSpace(artist))
    {
      artist = source.Type switch
      {
        AudioSourceType.Radio => "Radio",
        AudioSourceType.FilePlayer => "File Player",
        AudioSourceType.Vinyl => "Vinyl",
        AudioSourceType.GenericUSB => "USB Input",
        AudioSourceType.Bluetooth => "Bluetooth",
        _ => source.Type.ToString()
      };
    }

    // Determine metadata source based on source type
    var metadataSource = source.Type switch
    {
      AudioSourceType.FilePlayer => MetadataSource.FileTag,
      _ => MetadataSource.Manual
    };

    return new TrackMetadata
    {
      Id = Guid.NewGuid().ToString(),
      Title = title,
      Artist = artist,
      Album = string.IsNullOrWhiteSpace(album) ? null : album,
      CoverArtUrl = string.IsNullOrWhiteSpace(coverArt) ? null : coverArt,
      Source = metadataSource,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
  }

  /// <summary>
  /// Gets a descriptive title for radio sources using frequency info.
  /// </summary>
  private static string GetRadioTitle(IAudioSource source)
  {
    if (source is IPrimaryAudioSource primary && primary.Metadata != null)
    {
      if (primary.Metadata.TryGetValue("Frequency", out var freq) && freq != null)
        return freq.ToString()!;
    }
    return "Radio";
  }

  /// <summary>
  /// Gets a title for file player sources using the current filename.
  /// </summary>
  private static string GetFilePlayerTitle(IAudioSource source)
  {
    if (source is FilePlayerAudioSource filePlayer && !string.IsNullOrEmpty(filePlayer.CurrentFile))
      return System.IO.Path.GetFileNameWithoutExtension(filePlayer.CurrentFile);
    return "File Player";
  }

  /// <summary>
  /// Maps AudioSourceType to PlaySource enum.
  /// </summary>
  private static PlaySource MapSourceTypeToPlaySource(AudioSourceType sourceType)
  {
    return sourceType switch
    {
      AudioSourceType.Radio => PlaySource.Radio,
      AudioSourceType.Vinyl => PlaySource.Vinyl,
      AudioSourceType.FilePlayer => PlaySource.File,
      AudioSourceType.GenericUSB => PlaySource.GenericUSB,
      AudioSourceType.Bluetooth => PlaySource.Bluetooth,
      _ => PlaySource.File
    };
  }

  /// <summary>
  /// Handles track identification events to update play history with metadata.
  /// </summary>
  private async void OnTrackIdentified(object? sender, TrackIdentifiedEventArgs e)
  {
    var activeSource = _getActiveSource();
    if (activeSource == null)
      return;

    try
    {
      using var scope = _serviceScopeFactory.CreateScope();
      var playHistoryRepository = scope.ServiceProvider.GetService<IPlayHistoryRepository>();
      if (playHistoryRepository == null)
      {
        _logger.LogDebug("IPlayHistoryRepository not available, skipping play history update");
        return;
      }

      var playSource = MapSourceTypeToPlaySource(activeSource.Type);

      // Try to find a recent unidentified entry for this source to update
      var existingEntry = await playHistoryRepository.GetRecentUnidentifiedAsync(playSource, 5);

      if (existingEntry != null)
      {
        // Update the existing entry with fingerprinting data
        var updatedEntry = existingEntry with
        {
          TrackMetadataId = e.Track.Id,
          FingerprintId = e.Track.FingerprintId,
          MetadataSource = MetadataSource.Fingerprinting,
          IdentificationConfidence = e.Confidence,
          WasIdentified = true,
          Track = e.Track
        };

        await playHistoryRepository.UpdateAsync(updatedEntry);
        _logger.LogInformation(
          "Updated play history entry {EntryId} with fingerprinting data: '{Title}' by '{Artist}' (confidence: {Confidence:P0})",
          existingEntry.Id, e.Track.Title, e.Track.Artist, e.Confidence);
      }
      else
      {
        _logger.LogDebug("No recent unidentified entry found to update with fingerprinting data");
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to update play history with fingerprinting data");
    }
  }

  /// <summary>
  /// Handles song change events from fingerprinting to create new play history entries.
  /// Finalizes the previous entry and creates a new one for the new song.
  /// </summary>
  private async void OnSongChanged(object? sender, SongChangedEventArgs e)
  {
    var activeSource = _getActiveSource();
    if (activeSource == null)
      return;

    try
    {
      using var scope = _serviceScopeFactory.CreateScope();
      var playHistoryRepository = scope.ServiceProvider.GetService<IPlayHistoryRepository>();
      var metadataRepository = scope.ServiceProvider.GetService<ITrackMetadataRepository>();
      if (playHistoryRepository == null)
      {
        _logger.LogDebug("IPlayHistoryRepository not available, skipping song change handling");
        return;
      }

      var playSource = MapSourceTypeToPlaySource(activeSource.Type);

      // Finalize the previous play history entry
      if (!string.IsNullOrEmpty(_currentPlayHistoryEntryId))
      {
        await playHistoryRepository.FinalizeEntryAsync(_currentPlayHistoryEntryId, e.DetectedAt);
        _logger.LogInformation(
          "Finalized play history entry {EntryId} (song ended at {EndedAt})",
          _currentPlayHistoryEntryId, e.DetectedAt);
      }

      // Store the new track metadata
      if (metadataRepository != null)
      {
        await metadataRepository.StoreAsync(e.NewTrack);
      }

      // Create a new play history entry for the new song
      var entryId = Guid.NewGuid().ToString();
      var entry = new PlayHistoryEntry
      {
        Id = entryId,
        TrackMetadataId = e.NewTrack.Id,
        FingerprintId = e.NewTrack.FingerprintId,
        PlayedAt = e.DetectedAt,
        Source = playSource,
        MetadataSource = MetadataSource.Fingerprinting,
        SourceDetails = $"{e.NewTrack.Title} - {e.NewTrack.Artist}",
        DurationSeconds = null, // Will be set when this entry is finalized
        IdentificationConfidence = e.Confidence,
        WasIdentified = true,
        Track = e.NewTrack
      };

      await playHistoryRepository.RecordPlayAsync(entry);
      _currentPlayHistoryEntryId = entryId;

      _logger.LogInformation(
        "Created new play history entry {EntryId} for song change: '{Title}' by '{Artist}' (confidence: {Confidence:P0})",
        entryId, e.NewTrack.Title, e.NewTrack.Artist, e.Confidence);

      _metricsCollector?.Increment("fingerprint.song_change_detected");
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to handle song change event");
    }
  }

  /// <summary>
  /// Updates the current play history entry when real AVRCP metadata arrives.
  /// This fixes entries that were recorded with the device name as title before
  /// the phone sent actual track metadata.
  /// </summary>
  private async void OnBluetoothMetadataChanged(object? sender, BluetoothPlaybackMetadata e)
  {
    // Only update if we have real metadata and the active source is Bluetooth
    if (e == null || string.IsNullOrEmpty(e.Title) || string.IsNullOrEmpty(e.Artist))
      return;
    if (_getActiveSource()?.Type != AudioSourceType.Bluetooth)
      return;
    if (string.IsNullOrEmpty(_currentPlayHistoryEntryId))
      return;

    try
    {
      using var scope = _serviceScopeFactory.CreateScope();
      var playHistoryRepository = scope.ServiceProvider.GetService<IPlayHistoryRepository>();
      if (playHistoryRepository == null) return;

      var entry = await playHistoryRepository.GetByIdAsync(_currentPlayHistoryEntryId);
      if (entry == null) return;

      // Only update if the entry looks like it has a device name as title
      // (i.e., track metadata wasn't available when the entry was recorded)
      var existingTitle = entry.Track?.Title;
      if (existingTitle == e.Title && entry.Track?.Artist == e.Artist)
        return; // Already up to date

      var metadataRepository = scope.ServiceProvider.GetService<ITrackMetadataRepository>();
      var metadata = new TrackMetadata
      {
        Id = entry.TrackMetadataId ?? Guid.NewGuid().ToString(),
        Title = e.Title,
        Artist = e.Artist,
        Album = e.Album,
        Source = MetadataSource.Avrcp,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
      };

      if (metadataRepository != null)
      {
        await metadataRepository.StoreAsync(metadata);
      }

      var updatedEntry = entry with
      {
        TrackMetadataId = metadata.Id,
        MetadataSource = MetadataSource.Avrcp,
        SourceDetails = $"{e.Title} - {e.Artist}",
        WasIdentified = true,
        Track = metadata
      };

      await playHistoryRepository.UpdateAsync(updatedEntry);
      _logger.LogInformation(
        "Updated play history entry {EntryId} with AVRCP metadata: '{Title}' by '{Artist}'",
        entry.Id, e.Title, e.Artist);
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Failed to update play history with AVRCP metadata");
    }
  }

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;

    _bluetoothService.MetadataChanged -= OnBluetoothMetadataChanged;

    if (_identificationService != null)
    {
      _identificationService.TrackIdentified -= OnTrackIdentified;
      _identificationService.SongChanged -= OnSongChanged;
    }
  }
}

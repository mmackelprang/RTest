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
    try
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
      await UpsertPlayHistoryAsync(source);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error handling source state change for play history");
    }
  }

  /// <summary>
  /// Upserts a play history entry. If a recent entry exists for the same source,
  /// updates it with better metadata instead of creating duplicates. This handles:
  /// - BT device name → real track title (AVRCP arrives after Playing state)
  /// - Rapid source state re-fires (same source within seconds)
  /// - Same song replayed within 5 minutes
  ///
  /// For Bluetooth sources with only placeholder metadata (device name), skips
  /// creating an entry entirely — OnBluetoothMetadataChanged will create the
  /// entry when real AVRCP data arrives.
  /// </summary>
  private async Task UpsertPlayHistoryAsync(IAudioSource source)
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
      var newTitle = metadata.Title;
      var newArtist = metadata.Artist;

      // For Bluetooth: if metadata is still just the device name (placeholder),
      // don't create an entry yet. OnBluetoothMetadataChanged will handle it
      // when real AVRCP metadata arrives.
      string? btDeviceName = null;
      if (playSource == PlaySource.Bluetooth && source is IPrimaryAudioSource ps2 &&
          ps2.Metadata?.TryGetValue("Device", out var deviceObj) == true)
        btDeviceName = deviceObj?.ToString();

      if (playSource == PlaySource.Bluetooth &&
          IsPlaceholderMetadata(newTitle, newArtist, playSource, btDeviceName))
      {
        _logger.LogDebug(
          "Skipping BT play history entry with placeholder metadata '{Title}' — waiting for AVRCP",
          newTitle);
        return;
      }

      var sourceDetails = GetSourceDetails(source, playSource, metadata, btDeviceName);

      // For radio: store RDS station name in Album if not already set
      if (playSource == PlaySource.Radio && string.IsNullOrWhiteSpace(metadata.Album) &&
          source is IRadioControl radioCtl && !string.IsNullOrWhiteSpace(radioCtl.RdsStationName))
      {
        metadata = metadata with { Album = radioCtl.RdsStationName };
      }

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

      // Check for a recent entry from the same source to upsert against
      var recentEntries = await playHistoryRepository.GetRecentAsync(10);
      var lastForSource = recentEntries?.FirstOrDefault(e => e.Source == playSource);

      if (lastForSource != null)
      {
        var secondsSinceLast = (DateTime.UtcNow - lastForSource.PlayedAt).TotalSeconds;
        var existingTitle = lastForSource.Track?.Title;
        var existingArtist = lastForSource.Track?.Artist;

        // Same title+artist → skip (already recorded)
        if (string.Equals(existingTitle, newTitle, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existingArtist, newArtist, StringComparison.OrdinalIgnoreCase))
        {
          _logger.LogDebug(
            "Skipping duplicate play history for '{Title}' by '{Artist}' (same source, already recorded)",
            newTitle, newArtist);
          return;
        }

        // Recent entry with placeholder/incomplete metadata → update it instead of inserting.
        // This catches: BT device name → real track, or partial AVRCP → full AVRCP.
        // "Recent" = within 30s for the same source (covers BT state re-fires).
        if (secondsSinceLast < 30 && IsPlaceholderMetadata(existingTitle, existingArtist, playSource, btDeviceName))
        {
          // Persist the new (better) metadata
          var metadataRepository = scope.ServiceProvider.GetService<ITrackMetadataRepository>();
          if (metadataRepository != null)
          {
            await metadataRepository.StoreAsync(metadata);
          }

          var updatedEntry = lastForSource with
          {
            TrackMetadataId = metadata.Id,
            MetadataSource = metadata.Source,
            SourceDetails = sourceDetails,
            DurationSeconds = durationSeconds ?? lastForSource.DurationSeconds,
            WasIdentified = true,
            Track = metadata
          };

          await playHistoryRepository.UpdateAsync(updatedEntry);
          _currentPlayHistoryEntryId = lastForSource.Id;
          _logger.LogInformation(
            "Updated play history entry {EntryId}: '{OldTitle}' → '{NewTitle}' by '{NewArtist}'",
            lastForSource.Id, existingTitle, newTitle, newArtist);
          return;
        }
      }

      // Cross-source dedup: same title+artist within 5 minutes
      if (!string.IsNullOrWhiteSpace(newTitle) && !string.IsNullOrWhiteSpace(newArtist))
      {
        var isDuplicate = await playHistoryRepository.ExistsRecentlyPlayedAsync(
          newTitle, newArtist, withinMinutes: 5);
        if (isDuplicate)
        {
          _logger.LogDebug(
            "Skipping duplicate play history for '{Title}' by '{Artist}' (recently played)",
            newTitle, newArtist);
          return;
        }
      }

      // No recent match — insert a new entry
      string? trackMetadataId = null;
      var metaRepo = scope.ServiceProvider.GetService<ITrackMetadataRepository>();
      if (metaRepo != null)
      {
        await metaRepo.StoreAsync(metadata);
        trackMetadataId = metadata.Id;
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
        WasIdentified = trackMetadataId != null
          && !IsPlaceholderMetadata(newTitle, newArtist, playSource, btDeviceName),
        Track = metadata
      };

      await playHistoryRepository.RecordPlayAsync(entry);
      _currentPlayHistoryEntryId = entryId;

      _logger.LogInformation(
        "Recorded play history entry {EntryId} for source {SourceName}: '{Title}' by '{Artist}'",
        entryId, source.Name, newTitle, newArtist);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to record play history for source {SourceName}", source.Name);
    }
  }

  /// <summary>
  /// Determines if the given title+artist represent placeholder/fallback metadata
  /// rather than real track information (e.g., BT device name, source type name).
  /// </summary>
  private static bool IsPlaceholderMetadata(
    string? title, string? artist, PlaySource source, string? btDeviceName = null)
  {
    if (string.IsNullOrWhiteSpace(title)) return true;

    // BT device name (e.g., "Pixel 8 Pro") used as title before AVRCP arrives
    if (source == PlaySource.Bluetooth && btDeviceName != null &&
        string.Equals(title, btDeviceName, StringComparison.OrdinalIgnoreCase))
      return true;

    // Source-type fallback artists indicate no real metadata was available
    var fallbackArtist = source switch
    {
      PlaySource.Radio => "Radio",
      PlaySource.File => "File Player",
      PlaySource.Vinyl => "Vinyl",
      PlaySource.GenericUSB => "USB Input",
      PlaySource.Bluetooth => "Bluetooth",
      _ => null
    };
    if (fallbackArtist != null && string.Equals(artist, fallbackArtist, StringComparison.OrdinalIgnoreCase))
      return true;

    // Source-type fallback titles
    var fallbackTitles = source switch
    {
      PlaySource.Bluetooth => new[] { "Bluetooth Audio", "Bluetooth" },
      PlaySource.Vinyl => new[] { "Vinyl" },
      PlaySource.GenericUSB => new[] { "USB Audio", "Generic USB Audio" },
      PlaySource.Radio => new[] { "SDR Radio", "Radio" },
      _ => Array.Empty<string>()
    };
    if (fallbackTitles.Any(f => string.Equals(title, f, StringComparison.OrdinalIgnoreCase)))
      return true;

    return false;
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
  /// Gets rich source details for radio entries: "FM / 101.5 MHz / WFJA" or "FM / 101.5 MHz".
  /// </summary>
  private static string GetRadioSourceDetails(IAudioSource source)
  {
    var band = "FM";
    var freq = "";
    string? station = null;

    if (source is IRadioControl radio)
    {
      band = radio.CurrentBand.ToString();
      freq = radio.CurrentFrequency.ToDisplayString();
      station = radio.RdsStationName;
    }
    else if (source is IPrimaryAudioSource ps && ps.Metadata != null)
    {
      if (ps.Metadata.TryGetValue("Frequency", out var f))
        freq = f?.ToString() ?? "";
    }

    if (!string.IsNullOrEmpty(station))
      return $"{band} / {freq} / {station}";
    if (!string.IsNullOrEmpty(freq))
      return $"{band} / {freq}";
    return "Radio";
  }

  /// <summary>
  /// Builds source-type-specific SourceDetails string for a play history entry.
  /// </summary>
  private static string GetSourceDetails(IAudioSource source, PlaySource playSource, TrackMetadata metadata, string? btDeviceName)
  {
    return playSource switch
    {
      PlaySource.Radio => GetRadioSourceDetails(source),
      PlaySource.Bluetooth => btDeviceName != null ? $"Bluetooth / {btDeviceName}" : "Bluetooth",
      _ => $"{metadata.Title} - {metadata.Artist}"
    };
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
        // Persist the identified track metadata so the DB row exists for JOIN queries
        var metadataRepository = scope.ServiceProvider.GetService<ITrackMetadataRepository>();
        if (metadataRepository != null)
          await metadataRepository.StoreAsync(e.Track);

        // Update the existing entry with fingerprinting data
        var updatedEntry = existingEntry with
        {
          TrackMetadataId = e.Track.Id,
          FingerprintId = e.Track.FingerprintId,
          MetadataSource = MetadataSource.Fingerprinting,
          SourceDetails = $"{e.Track.Title} - {e.Track.Artist}",
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
  /// Handles AVRCP metadata changes for Bluetooth play history.
  /// - If no current entry exists, creates one (first track after BT connect).
  /// - If the current entry has placeholder metadata, updates it (device name → real track).
  /// - If the current entry has DIFFERENT real metadata, finalizes the old entry and creates
  ///   a new one (song changed on the phone).
  /// - If the current entry already matches, skips (no-op / duplicate AVRCP event).
  /// </summary>
  private async void OnBluetoothMetadataChanged(object? sender, BluetoothPlaybackMetadata e)
  {
    // Only handle if we have real metadata and the active source is Bluetooth
    if (e == null || string.IsNullOrEmpty(e.Title) || string.IsNullOrEmpty(e.Artist))
      return;
    if (_getActiveSource()?.Type != AudioSourceType.Bluetooth)
      return;

    try
    {
      using var scope = _serviceScopeFactory.CreateScope();
      var playHistoryRepository = scope.ServiceProvider.GetService<IPlayHistoryRepository>();
      if (playHistoryRepository == null) return;
      var metadataRepository = scope.ServiceProvider.GetService<ITrackMetadataRepository>();

      // Dedup: skip if this exact title+artist was recently played (5-min window
      // matches cross-source dedup in UpsertPlayHistoryAsync, covers source-switch scenarios)
      var isDuplicate = await playHistoryRepository.ExistsRecentlyPlayedAsync(
        e.Title, e.Artist, withinMinutes: 5);
      if (isDuplicate)
      {
        _logger.LogDebug(
          "Skipping duplicate BT play history for '{Title}' by '{Artist}' (recently played)",
          e.Title, e.Artist);
        return;
      }

      // Build the new metadata record, including cover art from the source if available
      // (BluetoothAudioSource may have already fetched art via MusicBrainz lookup)
      string? coverArtUrl = null;
      if (_getActiveSource() is IPrimaryAudioSource btSrc && btSrc.Metadata != null &&
          btSrc.Metadata.TryGetValue(StandardMetadataKeys.AlbumArtUrl, out var artObj))
      {
        var artUrl = artObj?.ToString();
        if (!string.IsNullOrWhiteSpace(artUrl) && artUrl != StandardMetadataKeys.DefaultAlbumArtUrl)
          coverArtUrl = artUrl;
      }

      var metadata = new TrackMetadata
      {
        Id = Guid.NewGuid().ToString(),
        Title = e.Title,
        Artist = e.Artist,
        Album = e.Album,
        CoverArtUrl = coverArtUrl,
        Source = MetadataSource.Avrcp,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
      };

      // Case 1: No current entry — create the first one
      if (string.IsNullOrEmpty(_currentPlayHistoryEntryId))
      {
        await CreateBluetoothHistoryEntryAsync(playHistoryRepository, metadataRepository, metadata, e);
        return;
      }

      var entry = await playHistoryRepository.GetByIdAsync(_currentPlayHistoryEntryId);
      if (entry == null)
      {
        // Entry was deleted or ID is stale — create fresh
        await CreateBluetoothHistoryEntryAsync(playHistoryRepository, metadataRepository, metadata, e);
        return;
      }

      var existingTitle = entry.Track?.Title;
      var existingArtist = entry.Track?.Artist;

      // Case 2: Already up to date — skip
      if (string.Equals(existingTitle, e.Title, StringComparison.OrdinalIgnoreCase) &&
          string.Equals(existingArtist, e.Artist, StringComparison.OrdinalIgnoreCase))
        return;

      // Get BT device name for placeholder detection (e.g., "Pixel 8 Pro")
      string? btDeviceName = null;
      if (_getActiveSource() is IPrimaryAudioSource btSource &&
          btSource.Metadata?.TryGetValue("Device", out var devObj) == true)
        btDeviceName = devObj?.ToString();

      // Case 3: Current entry is a placeholder (device name) — update in place
      if (entry.Source == PlaySource.Bluetooth &&
          IsPlaceholderMetadata(existingTitle, existingArtist, PlaySource.Bluetooth, btDeviceName))
      {
        metadata = metadata with { Id = entry.TrackMetadataId ?? metadata.Id };
        if (metadataRepository != null)
          await metadataRepository.StoreAsync(metadata);

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
          "Updated BT play history entry {EntryId}: '{OldTitle}' → '{NewTitle}' by '{NewArtist}'",
          entry.Id, existingTitle, e.Title, e.Artist);
        return;
      }

      // Case 4: Different real song — finalize old entry only if it's from BT
      // (don't finalize Radio/File entries from the BT metadata handler)
      if (entry.Source == PlaySource.Bluetooth)
      {
        await playHistoryRepository.FinalizeEntryAsync(entry.Id, DateTime.UtcNow);
        _logger.LogInformation(
          "Finalized BT play history entry {EntryId} ('{Title}' by '{Artist}')",
          entry.Id, existingTitle, existingArtist);
      }

      await CreateBluetoothHistoryEntryAsync(playHistoryRepository, metadataRepository, metadata, e);
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Failed to update play history with AVRCP metadata");
    }
  }

  /// <summary>
  /// Creates a new play history entry for a Bluetooth track.
  /// </summary>
  private async Task CreateBluetoothHistoryEntryAsync(
    IPlayHistoryRepository playHistoryRepository,
    ITrackMetadataRepository? metadataRepository,
    TrackMetadata metadata,
    BluetoothPlaybackMetadata btMeta)
  {
    if (metadataRepository != null)
      await metadataRepository.StoreAsync(metadata);

    int? durationSeconds = btMeta.Duration > TimeSpan.Zero
      ? (int)btMeta.Duration.TotalSeconds
      : null;

    var entryId = Guid.NewGuid().ToString();
    var entry = new PlayHistoryEntry
    {
      Id = entryId,
      TrackMetadataId = metadata.Id,
      FingerprintId = null,
      PlayedAt = DateTime.UtcNow,
      Source = PlaySource.Bluetooth,
      MetadataSource = MetadataSource.Avrcp,
      SourceDetails = $"{btMeta.Title} - {btMeta.Artist}",
      DurationSeconds = durationSeconds,
      IdentificationConfidence = null,
      WasIdentified = true,
      Track = metadata
    };

    await playHistoryRepository.RecordPlayAsync(entry);
    _currentPlayHistoryEntryId = entryId;

    _logger.LogInformation(
      "Created BT play history entry {EntryId}: '{Title}' by '{Artist}'",
      entryId, btMeta.Title, btMeta.Artist);
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

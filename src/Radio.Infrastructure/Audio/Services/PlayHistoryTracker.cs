using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Radio.Core.Events;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;
using Radio.Infrastructure.Audio.Sources.Primary;
using Radio.Metrics;
using Radio.Fingerprinting.Services;

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

  // Title values that mean "no title" for each source: History's own fallbacks
  // (GetSourceMetadata) plus the defaults the sources themselves write when they have no
  // track metadata. Passed to SourceMetadataPrecedence.IsMissing as extra placeholders,
  // the same way BluetoothAudioSource passes its device name (AUD-19).
  private static readonly string[] BluetoothFallbackTitles = ["Bluetooth Audio", "Bluetooth", "Bluetooth Device"];
  private static readonly string[] FileFallbackTitles = ["File Player"];
  private static readonly string[] VinylFallbackTitles = ["Vinyl"];
  private static readonly string[] UsbFallbackTitles = ["USB Audio", "Generic USB Audio"];
  private static readonly string[] RadioFallbackTitles = ["SDR Radio", "Radio"];

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
      {
        return;
      }

      // Only record when transitioning to Playing state
      if (e.NewState != AudioSourceState.Playing)
      {
        return;
      }

      // Only track primary sources that are the active source
      if (source != _getActiveSource())
      {
        return;
      }

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
      {
        btDeviceName = deviceObj?.ToString();
      }

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
        {
          durationSeconds = (int)ts.TotalSeconds;
        }
        else if (double.TryParse(durObj?.ToString(), out var durVal))
        {
          durationSeconds = (int)durVal;
        }
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
  /// <remarks>
  /// Row-level, not per-field: true when the title is missing OR the artist is History's
  /// fallback artist. "Missing" is <see cref="SourceMetadataPrecedence.IsMissing"/> — the
  /// predicate AUD-1 gave now-playing — with History's own fallbacks as placeholders (AUD-19).
  /// ⚠ A file player title equal to the filename is NOT a placeholder here (no filename is
  /// passed), so a file with an artist tag but no title tag still records as identified. The
  /// per-field merge (<see cref="ApplyIdentification"/>) does treat it as a missing title.
  /// </remarks>
  private static bool IsPlaceholderMetadata(
    string? title, string? artist, PlaySource source, string? btDeviceName = null)
  {
    // BT device name (e.g., "Pixel 8 Pro") is used as the title before AVRCP arrives
    if (SourceMetadataPrecedence.IsMissing(title, TitlePlaceholders(source, btDeviceName, fileTitle: null)))
    {
      return true;
    }

    // Source-type fallback artists indicate no real metadata was available
    var fallbackArtist = FallbackArtist(source);
    return fallbackArtist != null && SourceMetadataPrecedence.IsMissing(artist, fallbackArtist);
  }

  /// <summary>
  /// The artist <see cref="GetSourceMetadata"/> writes when the source supplied none.
  /// </summary>
  private static string? FallbackArtist(PlaySource source) => source switch
  {
    PlaySource.Radio => "Radio",
    PlaySource.File => "File Player",
    PlaySource.Vinyl => "Vinyl",
    PlaySource.GenericUSB => "USB Input",
    PlaySource.Bluetooth => "Bluetooth",
    _ => null
  };

  /// <summary>
  /// Title values that mean "no title" for <paramref name="source"/>, on top of
  /// <see cref="StandardMetadataKeys.DefaultTitle"/>.
  /// </summary>
  /// <param name="source">The play source.</param>
  /// <param name="btDeviceName">The connected phone's name, which Bluetooth writes as the title
  /// when it has no track metadata.</param>
  /// <param name="fileTitle">The current file's name without extension, which the file player
  /// seeds as the title and replaces only when the file has a Title tag.</param>
  private static string[] TitlePlaceholders(PlaySource source, string? btDeviceName, string? fileTitle)
  {
    var fallbacks = source switch
    {
      PlaySource.Bluetooth => BluetoothFallbackTitles,
      PlaySource.File => FileFallbackTitles,
      PlaySource.Vinyl => VinylFallbackTitles,
      PlaySource.GenericUSB => UsbFallbackTitles,
      PlaySource.Radio => RadioFallbackTitles,
      _ => Array.Empty<string>()
    };

    var extra = source switch
    {
      PlaySource.Bluetooth => btDeviceName,
      PlaySource.File => fileTitle,
      _ => null
    };

    return string.IsNullOrWhiteSpace(extra)
      ? [StandardMetadataKeys.DefaultTitle, .. fallbacks]
      : [StandardMetadataKeys.DefaultTitle, .. fallbacks, extra];
  }

  /// <summary>
  /// True for the sources whose now-playing metadata follows AUD-1's per-field rule:
  /// Bluetooth (AVRCP) and the file player (tags). The SDR radio, vinyl and USB sources still
  /// take an identification whole on now-playing (USBAudioSourceBase.UpdateMetadataFromFingerprint),
  /// so History does the same for them.
  /// </summary>
  private static bool FollowsSourcePrecedence(PlaySource source) =>
    source is PlaySource.Bluetooth or PlaySource.File;

  /// <summary>
  /// The provenance of a title the source supplied itself.
  /// </summary>
  private static MetadataSource SourceProvenance(PlaySource source) => source switch
  {
    PlaySource.Bluetooth => MetadataSource.Avrcp,
    PlaySource.File => MetadataSource.FileTag,
    _ => MetadataSource.Manual
  };

  /// <summary>
  /// What History records when an identification lands on <paramref name="recorded"/>: the
  /// owner's AUD-1 rule, per field (AUD-19). Where the source supplied a title, artist, album or
  /// cover art it is kept; the identification fills only what is missing. Fields the sources
  /// never supply to History (genre, year, track number, album artist, MusicBrainz ids) are
  /// taken from the identification where the recorded row has none.
  /// </summary>
  /// <param name="recorded">The source's metadata as History recorded it, or null when there is
  /// none — then the identification is taken whole.</param>
  /// <param name="identified">The fingerprint result.</param>
  /// <param name="playSource">The active play source. For sources outside the rule
  /// (<see cref="FollowsSourcePrecedence"/>) the identification is taken whole, as before.</param>
  /// <param name="source">The active source, read for its placeholders (device name, filename).</param>
  /// <returns>
  /// The track to record, and whether its TITLE came from the identification. When nothing the
  /// source supplied survived, the identification itself is returned (its id included);
  /// otherwise a new row with a fresh id — never the identification's id, so the merged row
  /// cannot be confused with the fingerprint's own.
  /// </returns>
  private static (TrackMetadata Track, bool TitleFromIdentification) ApplyIdentification(
    TrackMetadata? recorded, TrackMetadata identified, PlaySource playSource, IAudioSource source)
  {
    if (recorded == null || !FollowsSourcePrecedence(playSource))
    {
      return (identified, true);
    }

    var titlePlaceholders = TitlePlaceholders(playSource, DeviceName(source), FileTitle(source));
    var fallbackArtist = FallbackArtist(playSource);

    // The recorded row as a metadata dictionary, so the shared helper decides each field. History
    // wrote its fallback artist itself when the source supplied none; that is not source data, so
    // it is left out and reads as missing. (FillMissingFrom only takes extra TITLE placeholders.)
    Dictionary<string, object> fields = new()
    {
      [StandardMetadataKeys.Title] = recorded.Title
    };
    if (fallbackArtist == null || !SourceMetadataPrecedence.IsMissing(recorded.Artist, fallbackArtist))
    {
      fields[StandardMetadataKeys.Artist] = recorded.Artist;
    }
    if (recorded.Album != null)
    {
      fields[StandardMetadataKeys.Album] = recorded.Album;
    }
    if (recorded.CoverArtUrl != null)
    {
      fields[StandardMetadataKeys.AlbumArtUrl] = recorded.CoverArtUrl;
    }

    var filled = SourceMetadataPrecedence.FillMissingFrom(fields, identified, titlePlaceholders);

    // Cover art by the same rule. Like BluetoothAudioSource since the owner's 2026-09-26 decision
    // on AUD-1, there is no artist-match guard: art fills whenever the source had none.
    string? coverArt;
    if (SourceMetadataPrecedence.ShouldFillAlbumArt(fields) && !string.IsNullOrWhiteSpace(identified.CoverArtUrl))
    {
      coverArt = identified.CoverArtUrl;
    }
    else
    {
      coverArt = SourceMetadataPrecedence.IsMissing(recorded.CoverArtUrl, StandardMetadataKeys.DefaultAlbumArtUrl)
        ? null
        : recorded.CoverArtUrl;
    }

    // A field neither side supplied keeps the recorded value (History's fallback), so Title and
    // Artist are never empty.
    var title = fields[StandardMetadataKeys.Title].ToString()!;
    var artist = fields.TryGetValue(StandardMetadataKeys.Artist, out var artistValue)
      ? artistValue.ToString()!
      : recorded.Artist;
    var album = fields.TryGetValue(StandardMetadataKeys.Album, out var albumValue)
        && !SourceMetadataPrecedence.IsMissing(albumValue, StandardMetadataKeys.DefaultAlbum)
      ? albumValue.ToString()
      : null;

    if (filled.Title && filled.Artist
        && string.Equals(album, identified.Album, StringComparison.Ordinal)
        && string.Equals(coverArt, identified.CoverArtUrl, StringComparison.Ordinal))
    {
      // Nothing the source supplied survived: record the identification as it is.
      return (identified, true);
    }

    var now = DateTime.UtcNow;
    var merged = new TrackMetadata
    {
      Id = Guid.NewGuid().ToString(),
      FingerprintId = identified.FingerprintId ?? recorded.FingerprintId,
      Title = title,
      Artist = artist,
      Album = album,
      AlbumArtist = recorded.AlbumArtist ?? identified.AlbumArtist,
      TrackNumber = recorded.TrackNumber ?? identified.TrackNumber,
      DiscNumber = recorded.DiscNumber ?? identified.DiscNumber,
      ReleaseYear = recorded.ReleaseYear ?? identified.ReleaseYear,
      Genre = recorded.Genre ?? identified.Genre,
      MusicBrainzArtistId = recorded.MusicBrainzArtistId ?? identified.MusicBrainzArtistId,
      MusicBrainzReleaseId = recorded.MusicBrainzReleaseId ?? identified.MusicBrainzReleaseId,
      MusicBrainzRecordingId = recorded.MusicBrainzRecordingId ?? identified.MusicBrainzRecordingId,
      CoverArtUrl = coverArt,
      // TrackMetadata.Source records the TITLE's provenance (AUD-17).
      Source = filled.Title ? identified.Source : SourceProvenance(playSource),
      CreatedAt = now,
      UpdatedAt = now
    };
    return (merged, filled.Title);
  }

  /// <summary>The connected phone's name, when the source is Bluetooth and reports one.</summary>
  private static string? DeviceName(IAudioSource source) =>
    source is IPrimaryAudioSource primary && primary.Metadata?.TryGetValue("Device", out var device) == true
      ? device?.ToString()
      : null;

  /// <summary>The current file's name without extension, when the source is the file player.</summary>
  private static string? FileTitle(IAudioSource source) =>
    source is FilePlayerAudioSource filePlayer && !string.IsNullOrEmpty(filePlayer.CurrentFile)
      ? System.IO.Path.GetFileNameWithoutExtension(filePlayer.CurrentFile)
      : null;

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
    if (title == StandardMetadataKeys.DefaultTitle)
    {
      title = null;
    }
    if (artist == StandardMetadataKeys.DefaultArtist)
    {
      artist = null;
    }
    if (album == StandardMetadataKeys.DefaultAlbum)
    {
      album = null;
    }
    if (coverArt == StandardMetadataKeys.DefaultAlbumArtUrl)
    {
      coverArt = null;
    }

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
      {
        return freq.ToString()!;
      }
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
      {
        freq = f?.ToString() ?? "";
      }
    }

    if (!string.IsNullOrEmpty(station))
    {
      return $"{band} / {freq} / {station}";
    }
    if (!string.IsNullOrEmpty(freq))
    {
      return $"{band} / {freq}";
    }
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
    {
      return System.IO.Path.GetFileNameWithoutExtension(filePlayer.CurrentFile);
    }
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
    {
      return;
    }

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
        // AUD-19: the per-field rule — what the source supplied for this row is kept, and the
        // identification fills only what is missing (BT and file; other sources take it whole).
        var (track, titleFromIdentification) = ApplyIdentification(
          existingEntry.Track, e.Track, playSource, activeSource);

        // Persist the recorded track metadata so the DB row exists for JOIN queries
        var metadataRepository = scope.ServiceProvider.GetService<ITrackMetadataRepository>();
        if (metadataRepository != null)
        {
          await metadataRepository.StoreAsync(track);
        }

        // MetadataSource records the TITLE's provenance; the identification itself is
        // recorded by FingerprintId, IdentificationConfidence and WasIdentified.
        var updatedEntry = existingEntry with
        {
          TrackMetadataId = track.Id,
          FingerprintId = e.Track.FingerprintId,
          MetadataSource = titleFromIdentification
            ? MetadataSource.Fingerprinting
            : existingEntry.MetadataSource ?? SourceProvenance(playSource),
          SourceDetails = $"{track.Title} - {track.Artist}",
          IdentificationConfidence = e.Confidence,
          WasIdentified = true,
          Track = track
        };

        await playHistoryRepository.UpdateAsync(updatedEntry);
        _logger.LogInformation(
          "Updated play history entry {EntryId} with fingerprinting data: '{Title}' by '{Artist}' (identified as '{IdentifiedTitle}' by '{IdentifiedArtist}', confidence: {Confidence:P0})",
          existingEntry.Id, track.Title, track.Artist, e.Track.Title, e.Track.Artist, e.Confidence);
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
    {
      return;
    }

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

      // AUD-19: what the song now playing IS comes from the source where the source says so.
      // For Bluetooth and the file player the source's live metadata (AVRCP, or the current
      // file's tags) is the baseline, and the identification fills only what it left missing —
      // so a fingerprint title that differs from the source's (a remaster suffix, or SongRec
      // matching residual audio) is not a song change. On a file player auto-advance the live
      // tags are the NEW file's, so the boundary is still recorded: there is no StateChanged on
      // an auto-advance, and this handler is what starts the new file's row.
      var currentEntryId = _currentPlayHistoryEntryId;
      var currentEntry = string.IsNullOrEmpty(currentEntryId)
        ? null
        : await playHistoryRepository.GetByIdAsync(currentEntryId);

      var liveMetadata = FollowsSourcePrecedence(playSource) ? GetSourceMetadata(activeSource) : null;

      // ⚠ A source can hold a title it FILLED from an earlier identification (Bluetooth keeps one
      // until the next AVRCP event writes the field again). If the live title is the current
      // row's, and that row's title came from fingerprinting, the source has not named this song:
      // take the identification whole, as before AUD-19, or a source that never supplies titles
      // could never record a song change again.
      if (liveMetadata != null
          && currentEntry?.Track != null
          && currentEntry.MetadataSource == MetadataSource.Fingerprinting
          && string.Equals(liveMetadata.Title, currentEntry.Track.Title, StringComparison.OrdinalIgnoreCase))
      {
        liveMetadata = null;
      }

      var (newTrack, titleFromIdentification) = ApplyIdentification(
        liveMetadata, e.NewTrack, playSource, activeSource);

      // Check if the current entry already matches the new song (AVRCP may have
      // created an entry before SongRec identified it — avoid creating a duplicate).
      if (!string.IsNullOrEmpty(currentEntryId))
      {
        if (currentEntry?.Track != null &&
            string.Equals(currentEntry.Track.Title, newTrack.Title, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(currentEntry.Track.Artist, newTrack.Artist, StringComparison.OrdinalIgnoreCase))
        {
          // Same song — fill the existing row's gaps from the identification instead of
          // duplicating. The row's own recorded fields are the baseline here, not the live ones.
          var (enriched, enrichedTitleFromIdentification) = ApplyIdentification(
            currentEntry.Track, e.NewTrack, playSource, activeSource);
          if (metadataRepository != null)
          {
            await metadataRepository.StoreAsync(enriched);
          }

          var updatedEntry = currentEntry with
          {
            TrackMetadataId = enriched.Id,
            FingerprintId = e.NewTrack.FingerprintId,
            MetadataSource = enrichedTitleFromIdentification
              ? MetadataSource.Fingerprinting
              : currentEntry.MetadataSource ?? SourceProvenance(playSource),
            SourceDetails = $"{enriched.Title} - {enriched.Artist}",
            IdentificationConfidence = e.Confidence,
            WasIdentified = true,
            Track = enriched
          };
          await playHistoryRepository.UpdateAsync(updatedEntry);
          _logger.LogInformation(
            "Enriched existing play history entry {EntryId} with fingerprinting data: '{Title}' by '{Artist}' (identified as '{IdentifiedTitle}' by '{IdentifiedArtist}', confidence: {Confidence:P0})",
            currentEntry.Id, enriched.Title, enriched.Artist, e.NewTrack.Title, e.NewTrack.Artist, e.Confidence);
          return;
        }

        // Different song — finalize the previous entry
        await playHistoryRepository.FinalizeEntryAsync(currentEntryId, e.DetectedAt);
        _logger.LogInformation(
          "Finalized play history entry {EntryId} (song ended at {EndedAt})",
          currentEntryId, e.DetectedAt);
      }

      // Create a new play history entry for the new song
      if (metadataRepository != null)
      {
        await metadataRepository.StoreAsync(newTrack);
      }

      var entryId = Guid.NewGuid().ToString();
      var entry = new PlayHistoryEntry
      {
        Id = entryId,
        TrackMetadataId = newTrack.Id,
        FingerprintId = e.NewTrack.FingerprintId,
        PlayedAt = e.DetectedAt,
        Source = playSource,
        // ⚠ "Not from the identification" is read as "from the source" here. The live title is
        // the source's own unless the source itself filled it from an EARLIER identification
        // (Bluetooth without AVRCP titles keeps a filled title until AVRCP next changes).
        MetadataSource = titleFromIdentification ? MetadataSource.Fingerprinting : SourceProvenance(playSource),
        SourceDetails = $"{newTrack.Title} - {newTrack.Artist}",
        DurationSeconds = null, // Will be set when this entry is finalized
        IdentificationConfidence = e.Confidence,
        WasIdentified = true,
        Track = newTrack
      };

      await playHistoryRepository.RecordPlayAsync(entry);
      _currentPlayHistoryEntryId = entryId;

      _logger.LogInformation(
        "Created new play history entry {EntryId} for song change: '{Title}' by '{Artist}' (identified as '{IdentifiedTitle}' by '{IdentifiedArtist}', confidence: {Confidence:P0})",
        entryId, newTrack.Title, newTrack.Artist, e.NewTrack.Title, e.NewTrack.Artist, e.Confidence);

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
    {
      return;
    }
    if (_getActiveSource()?.Type != AudioSourceType.Bluetooth)
    {
      return;
    }

    try
    {
      using var scope = _serviceScopeFactory.CreateScope();
      var playHistoryRepository = scope.ServiceProvider.GetService<IPlayHistoryRepository>();
      if (playHistoryRepository == null)
      {
        return;
      }
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

      // Use album art the Bluetooth service supplied with the event — none on Linux, the appliance,
      // since its service reads no art (AUD-17) — and not from btSrc.Metadata, which may
      // still contain stale art from the previous song — the tracker handler fires
      // before BluetoothAudioSource.OnMetadataChanged clears it due to subscription order).
      // Art from MusicBrainz/SongRec lookups will update the entry later via OnTrackIdentified.
      string? coverArtUrl = null;
      if (!string.IsNullOrWhiteSpace(e.AlbumArtUrl))
      {
        coverArtUrl = e.AlbumArtUrl;
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
      {
        return;
      }

      // Get BT device name for placeholder detection (e.g., "Pixel 8 Pro")
      string? btDeviceName = null;
      if (_getActiveSource() is IPrimaryAudioSource btSource &&
          btSource.Metadata?.TryGetValue("Device", out var devObj) == true)
      {
        btDeviceName = devObj?.ToString();
      }

      // Case 3: Current entry is a placeholder (device name) — update in place
      if (entry.Source == PlaySource.Bluetooth &&
          IsPlaceholderMetadata(existingTitle, existingArtist, PlaySource.Bluetooth, btDeviceName))
      {
        metadata = metadata with { Id = entry.TrackMetadataId ?? metadata.Id };
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
    {
      await metadataRepository.StoreAsync(metadata);
    }

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

  /// <summary>
  /// Finalizes the in-flight play history entry, if there is one, by stamping its end time.
  /// Called from <see cref="PlayHistoryShutdownFinalizer.StopAsync"/> during host shutdown, while
  /// the root service provider is still alive — <see cref="Dispose"/> runs after the container is
  /// disposed and cannot create a scope (AUD-78).
  /// </summary>
  /// <remarks>
  /// Best-effort: a failure is logged at Warning and swallowed so it cannot fail shutdown. If this
  /// never runs (crash, kill), the entry stays open until the next start's orphan cleanup
  /// (<c>AudioEngineInitializationService.CloseOrphanedPlayHistoryEntriesAsync</c>), which only
  /// closes entries whose <c>PlayedAt</c> is more than two minutes before that start, and stamps
  /// an estimated end time rather than the real one.
  /// </remarks>
  public async Task FinalizeInFlightEntryAsync(CancellationToken cancellationToken = default)
  {
    // Snapshot: the event handlers reassign this field from other threads.
    string? entryId = _currentPlayHistoryEntryId;
    if (entryId == null)
    {
      return;
    }

    try
    {
      using var scope = _serviceScopeFactory.CreateScope();
      var repo = scope.ServiceProvider.GetRequiredService<IPlayHistoryRepository>();
      await repo.FinalizeEntryAsync(entryId, DateTime.UtcNow, cancellationToken);
      // Clear only if no handler replaced the entry while the write was in flight.
      Interlocked.CompareExchange(ref _currentPlayHistoryEntryId, null, entryId);
      _logger.LogInformation("Finalized in-flight play history entry {Id} during shutdown", entryId);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to finalize play history entry {Id} during shutdown", entryId);
    }
  }

  /// <summary>
  /// Unsubscribes from the Bluetooth and identification events. Does not touch the database:
  /// by the time the container disposes this singleton it can no longer create a scope, so the
  /// in-flight entry is finalized earlier by <see cref="FinalizeInFlightEntryAsync"/> (AUD-78).
  /// </summary>
  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }
    _disposed = true;

    _bluetoothService.MetadataChanged -= OnBluetoothMetadataChanged;

    if (_identificationService != null)
    {
      _identificationService.TrackIdentified -= OnTrackIdentified;
      _identificationService.SongChanged -= OnSongChanged;
    }
  }
}
